using System.Net.NetworkInformation;

namespace TaskbarNetMonitor.Core;

/// <summary>
/// Sums the byte counters of all "real" adapters. Interface enumeration is the
/// expensive part, so the adapter list is cached and only refreshed occasionally:
/// reading the counters of a known adapter is a cheap local call, while enumerating
/// can block for tens of milliseconds (it does DNS-less SMB/NDIS queries) and must
/// never run on the UI thread.
/// </summary>
internal sealed class TrafficSampler
{
    private const int RefreshEveryNSamples = 30;

    private readonly object _gate = new();
    private List<NetworkInterface> _adapters = [];
    private int _samplesSinceRefresh = RefreshEveryNSamples;

    /// <summary>Counters that carry real user traffic: up, not loopback, not a tunnel.</summary>
    public static bool ShouldCount(OperationalStatus status, NetworkInterfaceType type)
    {
        if (status != OperationalStatus.Up)
        {
            return false;
        }

        return type != NetworkInterfaceType.Loopback && type != NetworkInterfaceType.Tunnel;
    }

    public (long Received, long Sent) ReadTotals()
    {
        List<NetworkInterface> adapters = GetAdapters();

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

        if (failed)
        {
            // An adapter disappeared mid-read; rebuild the list on the next sample.
            lock (_gate)
            {
                _samplesSinceRefresh = RefreshEveryNSamples;
            }
        }

        return (received, sent);
    }

    private List<NetworkInterface> GetAdapters()
    {
        lock (_gate)
        {
            if (_samplesSinceRefresh < RefreshEveryNSamples && _adapters.Count > 0)
            {
                _samplesSinceRefresh++;
                return _adapters;
            }

            _samplesSinceRefresh = 0;
            try
            {
                _adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(static a => ShouldCount(a.OperationalStatus, a.NetworkInterfaceType))
                    .ToList();
            }
            catch (NetworkInformationException)
            {
                _adapters = [];
            }

            return _adapters;
        }
    }
}
