namespace HASNetMonitor.Core;

/// <summary>
/// The direction markers the user can pick. Most are a glyph pair - the up and down
/// arrow of one font family - and <see cref="Bar"/> is drawn from geometry instead, so
/// it keeps the same shape whatever font is chosen. <see cref="None"/> hides the marker.
/// </summary>
public static class ArrowStyles
{
    /// <summary>The classic arrows: U+2191 and U+2193.</summary>
    public const string Classic = "Classic";

    /// <summary>Filled triangles: U+25B2 and U+25BC.</summary>
    public const string Solid = "Solid";

    /// <summary>Small filled triangles: U+25B4 and U+25BE.</summary>
    public const string Small = "Small";

    /// <summary>Double-struck arrows: U+21D1 and U+21D3.</summary>
    public const string Double = "Double";

    /// <summary>Chevron heads: U+2303 and U+2304.</summary>
    public const string Chevron = "Chevron";

    /// <summary>A stem and head drawn from geometry, not a glyph.</summary>
    public const string Bar = "Bar";

    /// <summary>No marker at all; the figure stands on its own.</summary>
    public const string None = "None";

    private const string UpArrow = "\u2191";

    private const string DownArrow = "\u2193";

    /// <summary>Every selectable style, in the order the settings list shows them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Classic,
        Solid,
        Small,
        Double,
        Chevron,
        Bar,
        None,
    ];

    /// <summary>The style's own name, or <see cref="Classic"/> for anything unknown.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Classic;
        }

        string wanted = value!.Trim();
        foreach (string style in All)
        {
            if (string.Equals(style, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return style;
            }
        }

        return Classic;
    }

    /// <summary>A short label for the settings list.</summary>
    public static string Describe(string? value) => Normalize(value) switch
    {
        Solid => "Solid triangles",
        Small => "Small triangles",
        Double => "Double arrows",
        Chevron => "Chevrons",
        Bar => "Drawn bar arrow",
        None => "No arrow",
        _ => "Classic arrows",
    };

    /// <summary>Whether the marker is drawn from geometry rather than from a glyph.</summary>
    public static bool IsDrawn(string? value) => Normalize(value) == Bar;

    /// <summary>Whether the style shows no marker at all.</summary>
    public static bool IsHidden(string? value) => Normalize(value) == None;

    /// <summary>
    /// The glyph pair of a style, or <c>null</c> for the drawn and hidden styles. A family
    /// without the glyph falls back to the classic arrow at paint time.
    /// </summary>
    public static string? GlyphFor(string? style, bool isDownload)
    {
        return Normalize(style) switch
        {
            Solid => isDownload ? "\u25BC" : "\u25B2",
            Small => isDownload ? "\u25BE" : "\u25B4",
            Double => isDownload ? "\u21D3" : "\u21D1",
            Chevron => isDownload ? "\u2304" : "\u2303",
            Bar or None => null,
            _ => isDownload ? DownArrow : UpArrow,
        };
    }
}
