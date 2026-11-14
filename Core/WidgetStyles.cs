namespace TaskbarNetMonitor.Core;

/// <summary>
/// The ways the widget can draw itself. Kept as strings in the config so a
/// hand-edited file stays readable and an unknown value coming from a newer or
/// older build degrades to the classic look instead of failing to start.
/// </summary>
internal static class WidgetStyles
{
    /// <summary>Two text rows, each with a solid bar underneath (the original look).</summary>
    public const string Bars = "Bars";

    /// <summary>Same rows, but each track is a segmented level meter.</summary>
    public const string Level = "Level";

    /// <summary>Text on the left, two vertical bars side by side on the right.</summary>
    public const string Sidebar = "Sidebar";

    /// <summary>Solid bars with a glow halo and rounded caps.</summary>
    public const string Neon = "Neon";

    /// <summary>Chunky separated blocks instead of one continuous bar.</summary>
    public const string Segments = "Segments";

    /// <summary>Side bars made of separated blocks, so the level reads in steps.</summary>
    public const string SidebarSegments = "SidebarSegments";

    /// <summary>A row of round dots that light up as traffic rises.</summary>
    public const string Dots = "Dots";

    /// <summary>Hairline fills with no track behind them.</summary>
    public const string Minimal = "Minimal";

    /// <summary>Every style name, in the order the settings dialog lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Bars, Level, Sidebar, SidebarSegments, Neon, Segments, Dots, Minimal];

    /// <summary>True for the styles whose two tracks stand upright side by side.</summary>
    public static bool IsVertical(string? style)
    {
        string normalized = Normalize(style);
        return normalized is Sidebar or SidebarSegments;
    }

    /// <summary>True for the styles that draw separated blocks rather than a solid fill.</summary>
    public static bool IsSegmented(string? style)
    {
        string normalized = Normalize(style);
        return normalized is Level or Segments or SidebarSegments or Dots;
    }

    public static string Normalize(string? style)
    {
        if (!string.IsNullOrWhiteSpace(style))
        {
            string trimmed = style.Trim();
            foreach (string candidate in All)
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return Bars;
    }

    /// <summary>Human wording for the settings dialog.</summary>
    public static string Describe(string? style) => Normalize(style) switch
    {
        Level => "Level meter (segmented)",
        Sidebar => "Side bars (vertical)",
        SidebarSegments => "Segmented side bars (gaps)",
        Neon => "Neon (glow, rounded)",
        Segments => "Segments (chunky blocks)",
        Dots => "Dots (dot matrix)",
        Minimal => "Minimal (hairline fills)",
        _ => "Bars (classic rows)",
    };
}

/// <summary>
/// Which traffic row is drawn first. "Upload on top, download at the bottom" is a
/// common taste (upload is the smaller number, so it reads calmer), and the classic
/// download-first order stays the default so existing configs look unchanged.
/// </summary>
internal static class StackOrder
{
    public const string DownloadFirst = "DownloadFirst";

    public const string UploadFirst = "UploadFirst";

    public static IReadOnlyList<string> All { get; } = [DownloadFirst, UploadFirst];

    public static string Normalize(string? order)
    {
        if (!string.IsNullOrWhiteSpace(order))
        {
            string trimmed = order.Trim();
            foreach (string candidate in All)
            {
                if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return DownloadFirst;
    }

    /// <summary>True when the upload row (or bar) comes first.</summary>
    public static bool UploadOnTop(string? order) => Normalize(order) == UploadFirst;

    public static string Describe(string? order) => UploadOnTop(order)
        ? "Upload on top, download at the bottom"
        : "Download on top, upload at the bottom";
}
