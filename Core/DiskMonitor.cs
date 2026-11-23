using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// Free space on the system drive, straight from kernel32. The percent-style label
/// keeps the widget compact; the bytes are only used to decide what to show.
/// </summary>
internal sealed class DiskMonitor
{
    /// <summary>The reading: free bytes on <paramref name="Drive"/>, or null when unknown.</summary>
    internal readonly record struct DiskReading(string Drive, long FreeBytes, long TotalBytes)
    {
        public bool IsKnown => FreeBytes >= 0;

        public static DiskReading Unavailable { get; } = new(string.Empty, -1, 0);
    }

    public DiskReading Read()
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
}
