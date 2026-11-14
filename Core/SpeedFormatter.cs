using System.Globalization;

namespace HASNetMonitor.Core;

/// <summary>
/// Formats a byte-per-second rate the way the Windows network flyout does,
/// using binary multiples and a stable number of significant digits.
/// </summary>
internal static class SpeedFormatter
{
    private static readonly string[] Units = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];

    public static string Format(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond <= 0)
        {
            return "0 B/s";
        }

        int unit = 0;
        double value = bytesPerSecond;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // The widget has a fixed width, so the top unit saturates instead of
        // growing the text (which would force a resize and flicker).
        if (value > 9999)
        {
            value = 9999;
        }

        string number = value >= 100 || unit == 0
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

        return $"{number} {Units[unit]}";
    }

    public static string FormatRow(bool isDownload, double bytesPerSecond)
        => $"{(isDownload ? "\u2193" : "\u2191")} {Format(bytesPerSecond)}";
}
