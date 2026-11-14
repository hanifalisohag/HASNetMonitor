using System.Drawing;

namespace TaskbarNetMonitor.Core;

/// <summary>
/// Parses and formats the configurable bar colours. Invalid input never throws:
/// the caller always gets a usable colour back.
/// </summary>
internal static class ColorHelper
{
    public static Color Parse(string? hex, Color fallback)
        => TryParse(hex, out Color color) ? color : fallback;

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        string text = hex!.Trim();
#if NETFRAMEWORK
        if (text.StartsWith("#", StringComparison.Ordinal))
#else
        if (text.StartsWith('#'))
#endif
        {
            text = text.Substring(1);
        }

        if (text.Length != 6 || !ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out ulong value))
        {
            return false;
        }

        color = Color.FromArgb(
            (int)((value >> 16) & 0xFF),
            (int)((value >> 8) & 0xFF),
            (int)(value & 0xFF));
        return true;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
