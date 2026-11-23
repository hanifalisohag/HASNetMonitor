using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// GPU utilization from the generic "GPU Engine" performance counters, held in one
/// persistent PDH query for the lifetime of the monitor.
///
/// Two properties are load-bearing here:
///
/// 1. The query is persistent: it is opened and its counters added exactly once, and
///    every sample is a single PdhCollectQueryData plus a bounded read of the
///    counters - never an open/add/collect/close per tick.
///
/// 2. The collect is asynchronous: PdhCollectQueryData can block for far longer than
///    a tick on a busy GPU, and the sample thread must never wait on it. ReadPercent()
///    returns the latest known reading immediately and lets the actual collect run on
///    a dedicated background thread; while a collect is in flight the tick uses the
///    last value it has. A stale chip is a better failure mode than a widget that
///    stops moving - the figures keep flowing and the chip catches up the next it can.
///
///    The collect deliberately does NOT use the shared ThreadPool. The widget's own
///    sample timer callback runs on the pool, so a slow GPU collect occupying pool
///    threads starves that callback and freezes the whole visualization - the exact
///    symptom this class exists to avoid. One long-lived thread owned by this monitor
///    keeps a slow collect from ever delaying the widget.
///
/// When the counters do not resolve at all the NVIDIA Management Library is used
/// directly (the driver ships it for exactly this), and when neither path resolves
/// the chip hides rather than show a confident-looking number.
/// </summary>
internal sealed class GpuMonitor : IDisposable
{
    /// <summary>Widest-instance wildcard: one counter per running engine queue.</summary>
    private static readonly string WildcardPath = @"\GPU Engine(*)\Utilization Percentage";

    /// <summary>The GPU engine that carries graphics load.</summary>
    private static readonly string ThreeD = "engtype_3D";

    private readonly object _gate = new();

    // Every field below is touched only by the worker thread, except where the
    // doc comment says the dispose path reads it too.
    private readonly List<IntPtr> _counters = new();
    private IntPtr _query = IntPtr.Zero;
    private int _initState; // 0 pending, 1 ready, 2 hard-disabled (NVML-only), 3 disposed

    private double? _latest;   // the latest reading, from the async collect
    private bool _inFlight;    // a collect is running on the worker thread
    private bool _disposed;
    private int _completedCollects; // test hook: proves the in-flight flag is cleared

    // The dedicated collect thread. Created lazily on the first read so a monitor that
    // is never used costs nothing, and joined on dispose so the query is never closed
    // under a running collect.
    private readonly AutoResetEvent _work = new(false);
    private Thread? _worker;
    private bool _stop;

    /// <summary>
    /// Deliberately empty: the query setup is deferred to the first collect, which
    /// runs on the monitor's own background thread. Nothing may touch PDH on the UI
    /// thread - the widget is constructed on it, and the expansion plus every
    /// counter-add would freeze the whole app at startup.
    /// </summary>
    public GpuMonitor()
    {
    }

    /// <summary>
    /// The percent the busiest 3D engine reports (0-100), or null when the machine
    /// has not reported yet. Returns the latest value immediately; the collect that
    /// produces it may still be running in the background.
    /// </summary>
    public double? ReadPercent()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_inFlight)
            {
                // A collect is running; using whatever we last saw keeps the sample
                // path from ever queueing behind the GPU.
                return _latest;
            }

            _inFlight = true;

            try
            {
                EnsureWorker();
                _work.Set();
            }
            catch
            {
                // If the worker cannot be started or signalled, clear the flag so the
                // next tick can try again. Leaving it set would freeze the chip forever.
                _inFlight = false;
                return _latest;
            }

            return _latest;
        }
    }

    /// <summary>
    /// The dedicated collect thread, or null before the first read. Exposed for the
    /// threading tests that pin "the collect never uses a ThreadPool thread".
    /// </summary>
    internal Thread? WorkerThreadForTests
    {
        get
        {
            lock (_gate)
            {
                return _worker;
            }
        }
    }

    /// <summary>How many collects have completed. Exposed so a test can prove the
    /// in-flight flag is cleared after each one (a stuck flag freezes the chip).</summary>
    internal int CompletedCollectsForTests
    {
        get
        {
            lock (_gate)
            {
                return _completedCollects;
            }
        }
    }

    /// <summary>
    /// Starts the dedicated collect thread on first use. Called under <see cref="_gate"/>.
    /// </summary>
    private void EnsureWorker()
    {
        if (_worker is not null)
        {
            return;
        }

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "HASNetMonitor.GpuMonitor",
        };
        _worker.Start();
    }

    /// <summary>
    /// The collect thread: waits for a signal, runs one collect, and repeats. A slow
    /// collect delays only the next GPU reading, never the widget's sample timer.
    /// </summary>
    private void WorkerLoop()
    {
        while (true)
        {
            _work.WaitOne();

            lock (_gate)
            {
                if (_stop)
                {
                    return;
                }
            }

            CollectAndStore();
        }
    }

    /// <summary>
    /// Runs the collect off the sample thread. The query setup and the per-counter
    /// reads happen here, so a slow collect can never freeze the widget.
    /// </summary>
    private void CollectAndStore()
    {
        double? value = null;
        try
        {
            EnsureInitialized();

            if (_query != IntPtr.Zero && _counters.Count > 0 && TryReadMany(out double max))
            {
                value = max;
            }
            else
            {
                value = TryReadNvml();
            }
        }
        catch
        {
            // The GPU path must never surface an exception to the sample or UI
            // thread. Whatever went wrong, the chip hides and the figures flow.
            value = null;
            try
            {
                value = TryReadNvml();
            }
            catch
            {
                // NVML failing too means the chip hides.
                value = null;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _latest = value;
                }

                // Always clear the flag, whatever happened above: a stuck flag would
                // freeze the chip permanently.
                _inFlight = false;
                _completedCollects++;
            }
        }
    }

    /// <summary>One-time query setup. Runs at most once per monitor lifetime.</summary>
    private void EnsureInitialized()
    {
        if (_initState != 0)
        {
            return;
        }

        _initState = 1; // one-shot: never retry the query setup per tick
        _query = IntPtr.Zero;

        if (NativeMethods.PdhOpenQueryW(null, 0, out IntPtr query) != 0 || query == IntPtr.Zero)
        {
            _query = IntPtr.Zero;
            _initState = 2;
            return;
        }

        _query = query;

        try
        {
            // Graphics load first - the 3D engine is what "GPU" means to a user. Fall
            // back to every engine so an unusual driver cannot leave the chip dark.
            if (!AddWildcardCounters(_query, ThreeD, _counters))
            {
                AddWildcardCounters(_query, null, _counters);
            }
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or System.ComponentModel.Win32Exception or TypeLoadException)
        {
            // Fall through to the NVML fallback.
            NativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counters.Clear();
            _initState = 2;
            return;
        }

        if (_counters.Count == 0)
        {
            // No engine matched on this machine - do not keep retrying the query.
            NativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _initState = 2;
        }
    }

    /// <summary>
    /// Expands the wildcard path into the concrete per-engine counter paths and adds them.
    /// When <paramref name="instanceFilter"/> is given, only paths whose instance name
    /// contains it are added. Returns whether anything was added.
    /// </summary>
    private static bool AddWildcardCounters(IntPtr query, string? instanceFilter, List<IntPtr> counters)
    {
        // The wildcard expansion writes a multi-string of full counter paths into the
        // caller-supplied buffer, so a raw allocation is used rather than a StringBuilder:
        // marshalling a StringBuilder there leaves the expanded list unfilled, which
        // silently adds a single counter instead of the whole engine list.
        uint size = 0;
        int result = NativeMethods.PdhExpandWildCardPathW(IntPtr.Zero, WildcardPath, IntPtr.Zero, ref size, 0);
        if (result != NativeMethods.PdhMoreData || size == 0)
        {
            return false;
        }

        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)size * 2);
        try
        {
            result = NativeMethods.PdhExpandWildCardPathW(IntPtr.Zero, WildcardPath, buffer, ref size, 0);
            if (result != 0)
            {
                return false;
            }

            string expanded = System.Runtime.InteropServices.Marshal.PtrToStringUni(buffer, (int)size) ?? string.Empty;
            foreach (string path in expanded.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (instanceFilter is not null
                    && path.ToUpperInvariant().Contains(instanceFilter.ToUpperInvariant()) is false)
                {
                    continue;
                }

                if (NativeMethods.PdhAddEnglishCounterW(query, path, 0, out IntPtr counter) == 0 && counter != IntPtr.Zero)
                {
                    counters.Add(counter);
                }
            }

            return counters.Count > 0;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Collects every counter in the query once and returns the busiest reading, clamped to 100.</summary>
    private bool TryReadMany(out double max)
    {
        max = 0.0;

        if (NativeMethods.PdhCollectQueryData(_query) != 0)
        {
            return false;
        }

        bool any = false;
        foreach (IntPtr counter in _counters)
        {
            double? value = ReadCounter(counter);
            if (value.HasValue)
            {
                any = true;
                if (value.Value > max)
                {
                    max = value.Value;
                }
            }
        }

        if (!any)
        {
            return false;
        }

        max = Numeric.Clamp(max, 0.0, 100.0);
        return true;
    }

    private static double? ReadCounter(IntPtr counter)
    {
        try
        {
            var value = new NativeMethods.PdhFmtCounterValue();
            if (NativeMethods.PdhGetFormattedCounterValue(counter, NativeMethods.PdhFmtDouble, IntPtr.Zero, ref value) != 0)
            {
                return null;
            }

            // The struct carries the counter status; a nonzero CStatus is "no data".
            if (value.CStatus != 0)
            {
                return null;
            }

            double raw = value.DoubleValue;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < -10.0)
            {
                return null;
            }

            // NOCAP100 means the raw value is uncapped: a busiest-engine reading slightly
            // above 100 is a real reading, clamped by the caller, not an error.
            return raw;
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Stops the collect thread, waits briefly for it to drain, then closes the query.
    /// Closing a half-collected query is safe, so a collect that never finishes is not
    /// worth waiting for - the process is exiting anyway and the handle leaks harmlessly.
    /// </summary>
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
            _initState = 3;
            _stop = true;
            worker = _worker;
            _worker = null;
        }

        // Wake the worker so it observes _stop and exits, then wait for it. The wait is
        // bounded: a collect stuck in PDH must not hang shutdown.
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

        // Only release the signal handle once the worker has actually stopped. Disposing
        // it while the worker is still blocked in WaitOne would be a use-after-free; a
        // worker that outlived the join is a background thread the process is exiting
        // anyway, so leaking the handle there is harmless.
        bool workerStopped = worker is null || !worker.IsAlive;

        IntPtr query;
        lock (_gate)
        {
            query = _query;
            _query = IntPtr.Zero;
        }

        if (query != IntPtr.Zero)
        {
            try
            {
                NativeMethods.PdhCloseQuery(query);
            }
            catch (Exception ex) when (ex is MarshalDirectiveException or System.ComponentModel.Win32Exception)
            {
                // Closing a broken query is not worth failing over.
            }

            _counters.Clear();
        }

        if (workerStopped)
        {
            _work.Dispose();
        }
    }

    private static bool _nvmlChecked;
    private static bool _nvmlAvailable;

    private static double? TryReadNvml()
    {
        if (!EnsureNvml())
        {
            return null;
        }

        try
        {
            if (NativeMethods.nvmlDeviceGetCountV2(out uint deviceCount) != 0 || deviceCount == 0)
            {
                return null;
            }

            if (NativeMethods.nvmlDeviceGetHandleByIndexV2(0, out IntPtr device) != 0 || device == IntPtr.Zero)
            {
                return null;
            }

            var rates = new NativeMethods.NvmlUtilizationData();
            if (NativeMethods.nvmlDeviceGetUtilizationRates(device, out rates) != 0)
            {
                return null;
            }

            // NVML_VALUE_NOT_AVAILABLE is -1, read here as 0xFFFFFFFF; anything above
            // 100 is a broken reading, not a zero percent.
            if (rates.Gpu == 0xFFFFFFFFu || rates.Gpu > 100u)
            {
                return null;
            }

            return Numeric.Clamp(rates.Gpu, 0.0, 100.0);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException or TypeLoadException)
        {
            return null;
        }
    }

    private static bool EnsureNvml()
    {
        if (_nvmlChecked)
        {
            return _nvmlAvailable;
        }

        _nvmlChecked = true;
        try
        {
            _nvmlAvailable = NativeMethods.nvmlInitV2() == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException or TypeLoadException)
        {
            _nvmlAvailable = false;
        }

        return _nvmlAvailable;
    }
}
