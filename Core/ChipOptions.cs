namespace HASNetMonitor.Core;

using System.Drawing;

/// <summary>
/// The metric names a chip colour is keyed by, and the built-in palette each falls back
/// to. Kept here so the config, the renderer and the settings dialog agree on one set of
/// names and one set of defaults.
/// </summary>
internal static class ChipMetrics
{
    public const string Cpu = "Cpu";
    public const string Ram = "Ram";
    public const string Gpu = "Gpu";
    public const string Disk = "Disk";
    public const string Battery = "Battery";

    public static IReadOnlyList<string> All { get; } = [Cpu, Ram, Gpu, Disk, Battery];

    /// <summary>The single letter the chip shows in its compact colour swatch.</summary>
    public static string Letter(string? metric) => Normalize(metric) switch
    {
        Ram => "R",
        Gpu => "G",
        Disk => "D",
        Battery => "B",
        _ => "C",
    };

    /// <summary>Canonical spelling of a metric name, or CPU for an unknown one.</summary>
    public static string Normalize(string? metric)
    {
        if (!string.IsNullOrWhiteSpace(metric))
        {
            string trimmed = metric!.Trim();
            foreach (string candidate in All)
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return Cpu;
    }

    /// <summary>The palette colour for a metric, used when the user has not picked one.</summary>
    public static Color Default(string metric) => metric switch
    {
        Cpu => SystemStats.StatsColors.Cpu,
        Ram => SystemStats.StatsColors.Ram,
        Gpu => SystemStats.StatsColors.Gpu,
        Disk => SystemStats.StatsColors.Disk,
        Battery => SystemStats.StatsColors.BatteryCharging,
        _ => SystemStats.StatsColors.Disk,
    };

    /// <summary>Human wording for the settings dialog.</summary>
    public static string Describe(string metric) => metric switch
    {
        Cpu => "CPU",
        Ram => "RAM",
        Gpu => "GPU",
        Disk => "Disk",
        Battery => "Battery",
        _ => metric,
    };
}

/// <summary>
/// How a statistics chip shows its level beside the dot. Kept as strings in the config
/// so a hand-edited file stays readable and an unknown value degrades to the plain dot
/// instead of failing to start.
/// </summary>
internal static class ChipIndicators
{
    /// <summary>Just the coloured dot and the text (the original look).</summary>
    public const string None = "None";

    /// <summary>The dot becomes a ring filled to the metric's percentage.</summary>
    public const string Ring = "Ring";

    /// <summary>A thin bar under the chip's text, filled to the metric's percentage.</summary>
    public const string Bar = "Bar";

    public static IReadOnlyList<string> All { get; } = [None, Ring, Bar];

    public static string Normalize(string? indicator)
    {
        if (!string.IsNullOrWhiteSpace(indicator))
        {
            string trimmed = indicator!.Trim();
            foreach (string candidate in All)
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return None;
    }

    public static string Describe(string? indicator) => Normalize(indicator) switch
    {
        Ring => "Ring (dot fills to the level)",
        Bar => "Bar (level under the text)",
        _ => "Dot only",
    };
}

/// <summary>
/// What is drawn at the left of a statistics chip: the metric's initial letter or a
/// small vector glyph. Kept as strings for the same reason as <see cref="ChipIndicators"/>.
/// </summary>
internal static class ChipIcons
{
    /// <summary>The metric's initial and a colon, e.g. "C:".</summary>
    public const string Letter = "Letter";

    /// <summary>A hand-drawn vector glyph for the metric.</summary>
    public const string Glyph = "Glyph";

    public static IReadOnlyList<string> All { get; } = [Letter, Glyph];

    public static string Normalize(string? icon)
    {
        if (!string.IsNullOrWhiteSpace(icon))
        {
            string trimmed = icon!.Trim();
            foreach (string candidate in All)
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return Letter;
    }

    public static string Describe(string? icon) => Normalize(icon) switch
    {
        Glyph => "Icons (vector glyphs)",
        _ => "Letters (C:, R:, ...)",
    };
}
