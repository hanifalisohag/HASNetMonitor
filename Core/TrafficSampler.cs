using System.Net.NetworkInformation;

namespace HASNetMonitor.Core;

/// <summary>
/// Sums the byte counters of all "real" adapters. Interface enumeration is the
/// expensive part, so the adapter list is cached and refreshed on a background
/// thread: reading the counters of a known adapter is a cheap local call, while
/// enumerating can block for seconds (it does DNS-less SMB/NDIS queries) and must
/// never run on the sample thread.
///
/// The refresh is deliberately asynchronous. An earlier version enumerated inline
/// on the sample thread every thirtieth sample; on a machine with a VPN, a
/// disconnected adapter or a virtual switch that call can block for seconds, and
/// because it ran before the sample was posted the whole widget stalled - every
/// chip showed its placeholder and the figures stopped, then caught up in a burst.
/// ReadTotals now only ever reads the cached list and asks the background thread
/// for a refresh; a slow enumeration delays the adapter list, never the sample.
/// </summary>
internal sealed class TrafficSampler : IDisposable
{
    private const int RefreshEveryNSamples = 30;

    /// <summary>How often the background thread re-reads the adapter counters.</summary>
    private const int RefreshIntervalMs = 1000;

    private readonly object _gate = new();
    private List<NetworkInterface> _adapters = [];
    private int _samplesSinceRefresh = RefreshEveryNSamples;

    // The last totals the background thread read. ReadTotals returns these without
    // touching NDIS: GetIPStatistics is a blocking call that can stall for seconds on a
    // VPN, a virtual switch or a disconnected adapter, so it must never run on the
    // sample thread.
    private long _received;
    private long _sent;
    private bool _haveTotals;

    // The background refresh. Created lazily on the first read so a sampler that is
    // never used costs nothing, and joined on dispose so the list is never swapped
    // under a running enumeration.
    private readonly AutoResetEvent _work = new(false);
    private Thread? _worker;
    private bool _stop;
    private bool _disposed;

    // Set once the first enumeration has completed, so the first read can wait briefly
    // for it instead of reporting zero traffic at startup.
    private readonly ManualResetEventSlim _firstRefresh = new(false);

    /// <summary>
    /// Returns the totals the background thread last read. Never enumerates and never
    /// calls GetIPStatistics: both are blocking NDIS calls that can stall for seconds,
    /// so they run on the background thread. This call only reads cached values and
    /// asks the worker for a refresh, so it cannot block the sample loop.
    /// </summary>
    public (long Received, long Sent) ReadTotals()
    {
        bool refresh;
        lock (_gate)
        {
            if (_disposed)
            {
                return (0, 0);
            }

            // Refresh on the usual cadence, and also on every read until the first
            // totals have been read once: the first read runs on the background thread,
            // so without this the sample thread would report zero traffic at startup.
            refresh = _samplesSinceRefresh >= RefreshEveryNSamples || !_haveTotals;
            if (refresh)
            {
                _samplesSinceRefresh = 0;
            }
            else
            {
                _samplesSinceRefresh++;
            }
        }

        if (refresh)
        {
            RequestRefresh();
        }

        // The very first read waits briefly for the initial read so the widget does not
        // seed itself with zero traffic. The wait is bounded: a slow NDIS query delays
        // this one read by at most the timeout, never the sample loop.
        if (!_haveTotals)
        {
            _firstRefresh.Wait(TimeSpan.FromMilliseconds(250));
        }

        lock (_gate)
        {
            return (_received, _sent);
        }
    }

    /// <summary>Counters that carry real user traffic: up, not loopback, not a tunnel.</summary>
    public static bool ShouldCount(OperationalStatus status, NetworkInterfaceType type)
    {
        if (status != OperationalStatus.Up)
        {
            return false;
        }

        return type != NetworkInterfaceType.Loopback && type != NetworkInterfaceType.Tunnel;
    }

    /// <summary>
    /// Asks the background thread to rebuild the adapter list. Called from the sample
    /// thread; returns immediately, so a slow enumeration never delays a sample.
    /// </summary>
    private void RequestRefresh()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                EnsureWorker();
                _work.Set();
            }
        }
        catch
        {
            // If the worker cannot be started or signalled, the cached list is used
            // until the next attempt. A stale adapter list is a better failure mode
            // than a sample that never completes.
        }
    }

    /// <summary>Starts the refresh thread on first use. Called under <see cref="_gate"/>.</summary>
    private void EnsureWorker()
    {
        if (_worker is not null)
        {
            return;
        }

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "HASNetMonitor.TrafficSampler",
        };
        _worker.Start();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            // Refresh on a short cadence so the cached totals stay current, and wake
            // early when the sample thread asks for one. The wait is bounded so a
            // request that arrives while a refresh is running is not lost.
            _work.WaitOne(RefreshIntervalMs);

            lock (_gate)
            {
                if (_stop)
                {
                    return;
                }
            }

            RefreshAdapters();
        }
    }

    /// <summary>
    /// Rebuilds the cached adapter list and reads its counters. Runs on the background
    /// thread, so the enumeration and the per-adapter GetIPStatistics calls - both of
    /// which can block for seconds - never touch the sample path.
    /// </summary>
    private void RefreshAdapters()
    {
        List<NetworkInterface> adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(static a => ShouldCount(a.OperationalStatus, a.NetworkInterfaceType))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            adapters = [];
        }

        long received = 0;
        long sent = 0;
        bool failed = false;

        foreach (NetworkInterface adapter in adapters)
        {
            try
            {
                IPInterfaceStatistics stats = adapter.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                failed = true;
            }
            catch (PlatformNotSupportedException)
            {
                failed = true;
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _adapters = adapters;
                if (!failed)
                {
                    _received = received;
                    _sent = sent;
                    _haveTotals = true;
                }
            }
        }

        _firstRefresh.Set();
    }

    public void Dispose()
    {
        Thread? worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stop = true;
            worker = _worker;
            _worker = null;
        }

        try
        {
            _work.Set();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        if (worker is not null && worker.IsAlive)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }

        bool workerStopped = worker is null || !worker.IsAlive;
        if (workerStopped)
        {
            _work.Dispose();
        }

        _firstRefresh.Dispose();
    }
}
