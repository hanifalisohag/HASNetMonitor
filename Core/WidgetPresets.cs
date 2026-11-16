using System.Drawing;

namespace HASNetMonitor.Core;

/// <summary>
/// One ready-made look: the colours, bar shape, arrow, type and tint that together read
/// as a deliberate style. A preset only ever touches how the widget looks - never the
/// refresh cadence, the scale, the on-screen position or the logon entry - so applying
/// one cannot move the widget or change what it measures.
/// </summary>
internal sealed class WidgetPreset
{
    public WidgetPreset(
        string name,
        string description,
        string downloadColor,
        string uploadColor,
        string style,
        string arrowStyle,
        string sortOrder,
        int barThicknessPx,
        int segmentGapPx,
        double fontSizePt,
        int arrowSizePercent,
        bool bold = false,
        bool italic = false,
        bool roundedBars = false,
        bool accentGlow = false,
        bool showBackdrop = false,
        bool crispText = false,
        int backdropOpacity = -1,
        string? downloadTextMode = null,
        string? downloadTextColor = null,
        string? uploadTextMode = null,
        string? uploadTextColor = null)
    {
        Name = name;
        Description = description;
        DownloadColor = downloadColor;
        UploadColor = uploadColor;
        Style = style;
        ArrowStyle = arrowStyle;
        SortOrder = sortOrder;
        BarThicknessPx = barThicknessPx;
        SegmentGapPx = segmentGapPx;
        FontSizePt = fontSizePt;
        ArrowSizePercent = arrowSizePercent;
        Bold = bold;
        Italic = italic;
        RoundedBars = roundedBars;
        AccentGlow = accentGlow;
        ShowBackdrop = showBackdrop;
        CrispText = crispText;
        BackdropOpacity = backdropOpacity >= 0 ? backdropOpacity : (crispText ? 255 : 120);
        DownloadTextMode = downloadTextMode;
        DownloadTextColor = downloadTextColor;
        UploadTextMode = uploadTextMode;
        UploadTextColor = uploadTextColor;
    }

    public string Name { get; }

    /// <summary>One sentence, shown beside the picker so the name is not the only clue.</summary>
    public string Description { get; }

    public string DownloadColor { get; }

    public string UploadColor { get; }

    public string Style { get; }

    public string ArrowStyle { get; }

    public string SortOrder { get; }

    public int BarThicknessPx { get; }

    public int SegmentGapPx { get; }

    public double FontSizePt { get; }

    public int ArrowSizePercent { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public bool RoundedBars { get; }

    public bool AccentGlow { get; }

    public bool ShowBackdrop { get; }

    public bool CrispText { get; }

    public int BackdropOpacity { get; }

    /// <summary>Explicit figure-colour mode, or null to take the derived one.</summary>
    public string? DownloadTextMode { get; }

    public string? DownloadTextColor { get; }

    public string? UploadTextMode { get; }

    public string? UploadTextColor { get; }

    public override string ToString() => Name;
}

/// <summary>
/// The preset gallery offered in the settings dialog. Every entry is a complete look, so
/// picking one never leaves a half-styled widget, and <see cref="FindByAppearance"/>
/// means the dialog can say which preset - if any - the current settings correspond to.
/// </summary>
internal static class WidgetPresets
{
    /// <summary>
    /// Luminance at or below which a figure is drawn in its bar's colour rather than the
    /// taskbar's text colour. A pale fill used as text would vanish against the bar it
    /// labels, so only fills dark enough to read as type carry the tint.
    /// </summary>
    private const double TextTintLuminanceCeiling = 0.45;

    /// <summary>
    /// The gallery, in the order the dialog lists it. "Harbour" is first and is exactly
    /// the look a fresh install starts with, so the default is never missing from the list.
    /// </summary>
    public static IReadOnlyList<WidgetPreset> All { get; } =
    [
        new(
            "Harbour (default)", "The look the app starts with: green down, red up, plain bars.",
            "#2ECC71","#E74C3C", WidgetStyles.Bars, ArrowStyles.Classic, StackOrder.DownloadFirst,
            3, 2, 9, 150,
            downloadTextMode: TextColorModes.Auto, uploadTextMode: TextColorModes.Auto),

        new(
            "Cobalt", "Cool blue against warm amber, in chunky separated blocks.",
            "#3C83F6","#F59E0A", WidgetStyles.Segments, ArrowStyles.Chevron, StackOrder.DownloadFirst,
            7, 2, 10, 150, bold: true, roundedBars: true),

        new(
            "Ember", "Glowing warm fills, rounded and bright.",
            "#FF8A3D","#E21D48", WidgetStyles.Neon, ArrowStyles.Classic, StackOrder.DownloadFirst,
            5, 0, 10, 160, bold: true, roundedBars: true, accentGlow: true),

        new(
            "Aurora", "A cool fade that brightens as traffic rises, violet on top.",
            "#17D1ED","#A88CFA", WidgetStyles.Gradient, ArrowStyles.Chevron, StackOrder.UploadFirst,
            5, 0, 10, 150, bold: true, roundedBars: true, accentGlow: true),

        new(
            "Graphite", "Barely-there hairlines for a taskbar that already has a palette.",
            "#B1C0D2","#475569", WidgetStyles.Minimal, ArrowStyles.None, StackOrder.DownloadFirst,
            2, 0, 9, 150),

        new(
            "Citrus", "Loud lime and amber in thick blocks, easy to read at a glance.",
            "#8CD11A","#B45309", WidgetStyles.Segments, ArrowStyles.Small, StackOrder.DownloadFirst,
            9, 3, 9.5, 140, bold: true),

        new(
            "Sakura", "Vertical segmented columns in pink and apricot.",
            "#F471B6","#FB923C", WidgetStyles.SidebarSegments, ArrowStyles.Small, StackOrder.DownloadFirst,
            6, 2, 9.5, 140, roundedBars: true),

        new(
            "Ocean", "A segmented level meter in sea colours, upload on top.",
            "#0891B2","#32DA6F", WidgetStyles.Level, ArrowStyles.Bar, StackOrder.UploadFirst,
            4, 1, 9, 150),

        new(
            "Violet", "Hollow tracks with a violet fill inside them.",
            "#8C5DF6","#EC4899", WidgetStyles.Outline, ArrowStyles.Double, StackOrder.DownloadFirst,
            6, 0, 10, 150, bold: true, roundedBars: true),

        new(
            "Terminal", "Terminal green and amber on an opaque plate, crisp like the clock.",
            "#32DA6F","#F5B204", WidgetStyles.Bars, ArrowStyles.Bar, StackOrder.DownloadFirst,
            3, 0, 10, 150, bold: true, showBackdrop: true, crispText: true),

        new(
            "Mono", "Greyscale only, for a taskbar with its own colour story.",
            "#B8BEC9","#5B6473", WidgetStyles.Outline, ArrowStyles.None, StackOrder.DownloadFirst,
            3, 0, 9, 150),

        new(
            "Sunset", "Orange falling into deep red, with a glow on the fill.",
            "#FB923C","#BF123C", WidgetStyles.Gradient, ArrowStyles.Classic, StackOrder.UploadFirst,
            5, 0, 10, 160, bold: true, roundedBars: true, accentGlow: true),

        new(
            "Ice", "Cold pale fills that stay out of the way.",
            "#09D1EC","#6265F1", WidgetStyles.Bars, ArrowStyles.Small, StackOrder.DownloadFirst,
            4, 0, 9, 140),

        new(
            "Nebula", "Deep space glow with rounded caps, purple over sky.",
            "#C185FC","#37BDF8", WidgetStyles.Neon, ArrowStyles.Chevron, StackOrder.UploadFirst,
            5, 0, 10, 150, bold: true, roundedBars: true, accentGlow: true),

        new(
            "Copper", "Warm metal tones in a stepped meter.",
            "#E8A33D","#C3410C", WidgetStyles.Level, ArrowStyles.Bar, StackOrder.DownloadFirst,
            5, 2, 9.5, 150, bold: true),

        new(
            "Mint", "Fresh vertical side bars, mint above teal.",
            "#1FBF82","#0B6B63", WidgetStyles.Sidebar, ArrowStyles.Chevron, StackOrder.UploadFirst,
            8, 0, 10.5, 150, bold: true, roundedBars: true),

        new(
            "Rose", "Rose and lilac dots, doubled arrows.",
            "#FDA5B0","#A855F7", WidgetStyles.Dots, ArrowStyles.Double, StackOrder.DownloadFirst,
            5, 3, 9.5, 150),

        new(
            "Sapphire", "A deep blue fade that reads calm at low traffic.",
            "#2664EB","#A88CFA", WidgetStyles.Gradient, ArrowStyles.Chevron, StackOrder.DownloadFirst,
            5, 0, 10, 150, bold: true, roundedBars: true),

        new(
            "Signal", "Warning colours in bold blocks, impossible to miss.",
            "#DEBB02","#F87272", WidgetStyles.Segments, ArrowStyles.Solid, StackOrder.DownloadFirst,
            10, 4, 9, 150, bold: true),

        new(
            "Tide", "Teal rising into indigo, segmented and quiet.",
            "#06B5D3","#4E45E5", WidgetStyles.Level, ArrowStyles.Small, StackOrder.UploadFirst,
            4, 1, 9.5, 140),

        new(
            "Blaze", "Hot neon for people who like to watch the meter move.",
            "#F43E5D","#F59E0A", WidgetStyles.Neon, ArrowStyles.Classic, StackOrder.DownloadFirst,
            5, 0, 10, 160, bold: true, roundedBars: true, accentGlow: true),

        new(
            "Paper", "Slate against navy: muted, flat and easy to ignore all day.",
            "#A8C1DA","#35475F", WidgetStyles.Bars, ArrowStyles.Small, StackOrder.DownloadFirst,
            4, 0, 9.5, 140, bold: true),

        new(
            "Onyx", "Slate hairlines with no chrome, for a dark taskbar.",
            "#A8C1DA","#5C6B80", WidgetStyles.Minimal, ArrowStyles.None, StackOrder.DownloadFirst,
            2, 0, 9, 140),

        new(
            "Midnight", "Deep blue with a pink highlight, thin and precise.",
            "#61A5FA","#F471B6", WidgetStyles.Bars, ArrowStyles.Small, StackOrder.DownloadFirst,
            3, 0, 9.5, 140, bold: true),
    ];

    /// <summary>The preset names, for the picker.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. All.Select(p => p.Name)];

    public static WidgetPreset? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string trimmed = name!.Trim();
        foreach (WidgetPreset preset in All)
        {
            if (string.Equals(preset.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// The preset that draws exactly this look, or null when the settings are a mix of
    /// presets or hand edits. Used to label the picker, so it never claims a preset that
    /// would change something if chosen.
    /// </summary>
    public static WidgetPreset? FindByAppearance(MonitorConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        foreach (WidgetPreset preset in All)
        {
            if (AppearanceEquals(config, Apply(preset, config)))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// The config this preset produces from <paramref name="current"/>: a full copy with
    /// the appearance replaced, so behaviour and placement are carried over untouched.
    /// </summary>
    public static MonitorConfig Apply(WidgetPreset preset, MonitorConfig current)
    {
        if (preset is null)
        {
            throw new ArgumentNullException(nameof(preset));
        }

        if (current is null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        MonitorConfig result = current.Clone();

        result.DownloadColorHex = preset.DownloadColor;
        result.UploadColorHex = preset.UploadColor;
        result.Style = preset.Style;
        result.ArrowStyle = preset.ArrowStyle;
        result.SortOrder = preset.SortOrder;
        result.BarThicknessPx = preset.BarThicknessPx;
        result.SegmentGapPx = preset.SegmentGapPx;
        result.FontSizePt = preset.FontSizePt;
        result.ArrowSizePercent = preset.ArrowSizePercent;
        result.FontBold = preset.Bold;
        result.FontItalic = preset.Italic;
        result.RoundedBars = preset.RoundedBars;
        result.AccentGlow = preset.AccentGlow;
        result.ShowBackdrop = preset.ShowBackdrop;
        result.BackdropOpacity = preset.BackdropOpacity;
        result.CrispText = preset.CrispText;

        result.DownloadTextColorMode = preset.DownloadTextMode ?? DerivedTextMode(preset.DownloadColor);
        result.UploadTextColorMode = preset.UploadTextMode ?? DerivedTextMode(preset.UploadColor);

        // A custom colour is kept when the preset names one; otherwise the row's existing
        // colour stays, since the mode ignores it and it is the value the user last picked.
        if (preset.DownloadTextColor is { Length: > 0 } downloadText)
        {
            result.DownloadTextColorHex = downloadText;
        }

        if (preset.UploadTextColor is { Length: > 0 } uploadText)
        {
            result.UploadTextColorHex = uploadText;
        }

        return result.Normalize();
    }

    /// <summary>
    /// The figure colour a preset gets when it does not name one: the bar's own colour
    /// when that is dark enough to read as type, otherwise the taskbar's text colour.
    /// </summary>
    public static string DerivedTextMode(string barColorHex)
    {
        Color bar = ColorHelper.Parse(barColorHex, Color.White);
        return RelativeLuminance(bar) <= TextTintLuminanceCeiling ? TextColorModes.Bar : TextColorModes.Auto;
    }

    /// <summary>
    /// WCAG relative luminance, used only to decide whether a colour is dark enough to
    /// tint figures with. Not a contrast measurement: the taskbar behind the widget is
    /// unknown here, so the rule errs towards the safe taskbar text colour.
    /// </summary>
    internal static double RelativeLuminance(Color color)
    {
        static double Channel(int value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    /// <summary>
    /// True when two configs would draw the same widget. Only the appearance fields the
    /// presets own are compared: the refresh cadence and the position are not part of a
    /// look, so a preset still matches after the user has moved the widget.
    /// </summary>
    public static bool AppearanceEquals(MonitorConfig left, MonitorConfig right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.DownloadColorHex, right.DownloadColorHex, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.UploadColorHex, right.UploadColorHex, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.DownloadTextColorMode, right.DownloadTextColorMode, StringComparison.Ordinal)
            && string.Equals(left.UploadTextColorMode, right.UploadTextColorMode, StringComparison.Ordinal)
            && string.Equals(EffectiveTextHex(left.DownloadTextColorMode, left.DownloadTextColorHex), EffectiveTextHex(right.DownloadTextColorMode, right.DownloadTextColorHex), StringComparison.OrdinalIgnoreCase)
            && string.Equals(EffectiveTextHex(left.UploadTextColorMode, left.UploadTextColorHex), EffectiveTextHex(right.UploadTextColorMode, right.UploadTextColorHex), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Style, right.Style, StringComparison.Ordinal)
            && string.Equals(left.ArrowStyle, right.ArrowStyle, StringComparison.Ordinal)
            && string.Equals(left.SortOrder, right.SortOrder, StringComparison.Ordinal)
            && left.BarThicknessPx == right.BarThicknessPx
            && left.SegmentGapPx == right.SegmentGapPx
            && Math.Abs(left.FontSizePt - right.FontSizePt) < 0.001
            && left.ArrowSizePercent == right.ArrowSizePercent
            && left.FontBold == right.FontBold
            && left.FontItalic == right.FontItalic
            && left.RoundedBars == right.RoundedBars
            && left.AccentGlow == right.AccentGlow
            && left.ShowBackdrop == right.ShowBackdrop
            && left.BackdropOpacity == right.BackdropOpacity
            && left.CrispText == right.CrispText;
    }

    /// <summary>
    /// The colour a row's figures are actually drawn in, or the empty string when the
    /// mode makes the stored colour irrelevant. Comparing the stored value regardless of
    /// mode would report two identical looks as different.
    /// </summary>
    private static string EffectiveTextHex(string mode, string hex)
        => string.Equals(mode, TextColorModes.Custom, StringComparison.Ordinal) ? hex : string.Empty;
}
