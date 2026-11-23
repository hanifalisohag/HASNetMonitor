using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// Physical memory pressure straight from kernel32: the percent of physical memory in
/// use plus the total and free bytes. The percent is the same number perfmon shows
/// as "% Committed Bytes In Use" is not - this is the simpler "Memory" counter that
/// already gives the figure the widgets usually show.
/// </summary>
internal sealed class MemoryMonitor
{
    /// <summary>The reading: percent in use (0-100), total and free bytes.</summary>
    internal readonly record struct MemoryReading(double Percent, ulong TotalBytes, ulong FreeBytes)
    {
        public bool IsKnown => !double.IsNaN(Percent) && TotalBytes > 0;

        public static MemoryReading Unavailable { get; } = new(double.NaN, 0, 0);
    }

    public MemoryReading Read()
    {
        NativeMethods.NativeMemoryStatusEx buffer = new();
        buffer.Size = Marshal.SizeOf<NativeMethods.NativeMemoryStatusEx>();

        try
        {
            if (!NativeMethods.GlobalMemoryStatusEx(ref buffer))
            {
                return MemoryReading.Unavailable;
            }
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or TypeLoadException or ArgumentException)
        {
            return MemoryReading.Unavailable;
        }

        if (buffer.TotalPhysical == 0)
        {
            return MemoryReading.Unavailable;
        }

        double percent = (double)buffer.Load;
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return MemoryReading.Unavailable;
        }

        percent = Numeric.Clamp(percent, 0.0, 100.0);
        return new MemoryReading(percent, buffer.TotalPhysical, buffer.AvailablePhysical);
    }
}
