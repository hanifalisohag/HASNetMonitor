using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// Disk activity - how busy the drives are - from the Windows "PhysicalDisk" performance
/// counters, held in one persistent PDH query for the lifetime of the monitor.
///
/// The counter is <c>\PhysicalDisk(_Total)\% Disk Time</c>: the <c>_Total</c> instance
/// aggregates every physical disk into a single number, so one chip shows the activity of
/// all drives at once rather than picking one. That is the same figure Task Manager calls
/// "Active time". When <c>_Total</c> is missing the wildcard is expanded and the first
/// physical disk is used instead, so the chip still fills on an unusual machine.
///
/// The query is opened and its counter added exactly once, and every sample is a single
/// PdhCollectQueryData plus one bounded read - never an open/add/collect/close per tick.
/// The read runs on the metric's own background thread (see <see cref="MetricSampler{T}"/>),
/// so a slow collect on a spun-down drive delays only this chip.
///
/// <c>% Disk Time</c> is the summed busy time of the disks and can exceed 100 on a machine
/// with several drives, so the value is clamped to 100 before it is reported.
///
/// The reading also carries the system drive's free and total bytes. The chip no longer
/// shows them - it shows activity - but they are kept so the reading stays a complete
/// description of the drive and existing callers keep working.
/// </summary>
internal sealed class DiskMonitor : IDisposable
{
    /// <summary>The aggregate of every physical disk - one number for all drives.</summary>
    private static readonly string TotalPath = @"\PhysicalDisk(_Total)\% Disk Time";

    /// <summary>The counter path the monitor reads, exposed so a test can pin the aggregate.</summary>
    internal static string AggregateCounterPath => TotalPath;

    /// <summary>Every physical disk, used only when the aggregate is unavailable.</summary>
    private static readonly string WildcardPath = @"\PhysicalDisk(*)\% Disk Time";

    private readonly object _gate = new();

    private IntPtr _query = IntPtr.Zero;
    private IntPtr _counter = IntPtr.Zero;
    private int _initState; // 0 pending, 1 ready, 2 hard-disabled, 3 disposed
    private bool _disposed;

    /// <summary>
    /// The reading: the activity percent of the drives, plus the system drive's free and
    /// total bytes. <see cref="ActivityPercent"/> is -1 when the counters did not resolve.
    /// </summary>
    internal readonly record struct DiskReading(string Drive, long FreeBytes, long TotalBytes)
    {
        /// <summary>How busy the drives are, 0-100, or -1 when unknown.</summary>
        public double ActivityPercent { get; init; } = -1.0;

        /// <summary>Whether the drive's free/total bytes are known.</summary>
        public bool IsKnown => FreeBytes >= 0;

        /// <summary>Whether the activity counters resolved.</summary>
        public bool HasActivity => ActivityPercent >= 0.0;

        public static DiskReading Unavailable { get; } = new(string.Empty, -1, 0);
    }

    /// <summary>
    /// The latest reading. Runs on the metric's background thread. The activity comes from
    /// PDH; the free/total bytes come from kernel32 and are best-effort.
    /// </summary>
    public DiskReading Read()
    {
        DiskReading space = ReadSpace();

        try
        {
            EnsureInitialized();

            if (_query == IntPtr.Zero || _counter == IntPtr.Zero)
            {
                return space;
            }

            if (NativeMethods.PdhCollectQueryData(_query) != 0)
            {
                return space;
            }

            double? value = ReadCounter(_counter);
            if (value is not double percent)
            {
                return space;
            }

            return space with { ActivityPercent = Numeric.Clamp(percent, 0.0, 100.0) };
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or System.ComponentModel.Win32Exception or TypeLoadException)
        {
            return space;
        }
    }

    /// <summary>The system drive's free and total bytes, or <see cref="DiskReading.Unavailable"/>.</summary>
    private static DiskReading ReadSpace()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
            {
                return DiskReading.Unavailable;
            }

            if (!NativeMethods.GetDiskFreeSpaceExW(root, out long free, out long total, out _))
            {
                return DiskReading.Unavailable;
            }

            return new DiskReading(root, Math.Max(0, free), Math.Max(0, total));
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception)
        {
            return DiskReading.Unavailable;
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

        if (NativeMethods.PdhOpenQueryW(null, 0, out IntPtr query) != 0 || query == IntPtr.Zero)
        {
            _initState = 2;
            return;
        }

        _query = query;

        // The aggregate first - one number for every drive. Fall back to the first
        // physical disk when the machine exposes no _Total instance.
        if (TryAddCounter(TotalPath))
        {
            return;
        }

        if (TryAddFirstWildcard())
        {
            return;
        }

        // Nothing matched on this machine - do not keep retrying the query.
        NativeMethods.PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _initState = 2;
    }

    /// <summary>Adds one concrete counter path. Returns whether it was added.</summary>
    private bool TryAddCounter(string path)
    {
        if (NativeMethods.PdhAddEnglishCounterW(_query, path, 0, out IntPtr counter) == 0 && counter != IntPtr.Zero)
        {
            _counter = counter;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Expands the wildcard and adds the first physical disk it names. The expansion writes
    /// a multi-string of full counter paths into a raw buffer, so a raw allocation is used
    /// rather than a StringBuilder, which would leave the list unfilled.
    /// </summary>
    private bool TryAddFirstWildcard()
    {
        uint size = 0;
        int result = NativeMethods.PdhExpandWildCardPathW(IntPtr.Zero, WildcardPath, IntPtr.Zero, ref size, 0);
        if (result != NativeMethods.PdhMoreData || size == 0)
        {
            return false;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            result = NativeMethods.PdhExpandWildCardPathW(IntPtr.Zero, WildcardPath, buffer, ref size, 0);
            if (result != 0)
            {
                return false;
            }

            string expanded = Marshal.PtrToStringUni(buffer, (int)size) ?? string.Empty;
            foreach (string path in expanded.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (NativeMethods.PdhAddEnglishCounterW(_query, path, 0, out IntPtr counter) == 0 && counter != IntPtr.Zero)
                {
                    _counter = counter;
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < 0.0)
            {
                return null;
            }

            return raw;
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Closes the query. Safe to call more than once.</summary>
    public void Dispose()
    {
        IntPtr query;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _initState = 3;
            query = _query;
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
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
        }
    }
}
