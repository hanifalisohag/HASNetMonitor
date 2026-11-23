using System.Drawing;
#if NETFRAMEWORK
// .NET Framework serialises with the in-box JavaScriptSerializer, whose ignore
// attribute lives in System.Web.Script.Serialization.
using JsonIgnoreAttribute = System.Web.Script.Serialization.ScriptIgnoreAttribute;
#else
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace HASNetMonitor.Core;

/// <summary>
/// User settings. Everything is public-read/write so it round-trips through
/// System.Text.Json, and <see cref="Normalize"/> is the single place that repairs
/// hand-edited or partially corrupted files.
/// </summary>
internal sealed class MonitorConfig
{
    public const int MinRefreshIntervalMs = 250;
    public const int MaxRefreshIntervalMs = 60000;
    public const int DefaultRefreshIntervalMs = 1000;
    public const int MaxBarThicknessPx = 24;
    public const int MaxSegmentGapPx = 12;

    /// <summary>
    /// Height of the direction arrow's ink as a percentage of a digit's ink. 100 draws it
    /// exactly as tall as the figure; the default is larger because the arrow is the thing
    /// the eye should catch first. It never shrinks below the figure.
    /// </summary>
    public const int DefaultArrowSizePercent = 150;
    public const int MinArrowSizePercent = 50;
    public const int MaxArrowSizePercent = 300;

    /// <summary>The spinner's step, in percentage points: a tenth of the figure's size.</summary>
    public const int ArrowSizeStep = 10;

    /// <summary>The arrow shape a fresh config starts with.</summary>
    public const string DefaultArrowStyle = ArrowStyles.Classic;

    /// <summary>The figure colour a fresh config starts with, used once the mode is Custom.</summary>
    public const string DefaultTextColor = "#FFFFFF";

    public const int MaxNudgePx = 400;
    public const double MinFontSizePt = 6;
    public const double MaxFontSizePt = 20;
    public const string DefaultDownloadColor = "#2ECC71";
    public const string DefaultUploadColor = "#E6643C";

    public string DownloadColorHex { get; set; } = DefaultDownloadColor;

    public string UploadColorHex { get; set; } = DefaultUploadColor;

    /// <summary>Where the download figure takes its colour from; see <see cref="TextColorModes"/>.</summary>
    public string DownloadTextColorMode { get; set; } = TextColorModes.Auto;

    /// <summary>Where the upload figure takes its colour from; see <see cref="TextColorModes"/>.</summary>
    public string UploadTextColorMode { get; set; } = TextColorModes.Auto;

    /// <summary>Used for the download figure when its mode is <see cref="TextColorModes.Bar"/>.</summary>
    public string DownloadTextColorHex { get; set; } = DefaultTextColor;

    /// <summary>Used for the upload figure when its mode is <see cref="TextColorModes.Bar"/>.</summary>
    public string UploadTextColorHex { get; set; } = DefaultTextColor;

    /// <summary>One of <see cref="ArrowStyles"/>; unknown values fall back to the classic arrows.</summary>
    public string ArrowStyle { get; set; } = ArrowStyles.Classic;

    public int RefreshIntervalMs { get; set; } = DefaultRefreshIntervalMs;

    public int OffsetFromTrayPx { get; set; } = 6;

    /// <summary>Shifts the widget left (negative) or right after placing it next to the tray.</summary>
    public int NudgeXPx { get; set; }

    /// <summary>Shifts the widget up (negative) or down after placing it next to the tray.</summary>
    public int NudgeYPx { get; set; }

    public bool AutoScale { get; set; } = true;

    public double MaxScaleMbps { get; set; } = 100;

    public bool ShowText { get; set; } = true;

    public bool ShowBackdrop { get; set; }

    public int BackdropOpacity { get; set; } = 120;

    public int BarThicknessPx { get; set; } = 3;

    /// <summary>Empty space between the blocks of a segmented style, in pixels.</summary>
    public int SegmentGapPx { get; set; } = 2;

    public double FontSizePt { get; set; } = 9;

    /// <summary>One of <see cref="WidgetStyles"/>; unknown values fall back to Bars.</summary>
    public string Style { get; set; } = WidgetStyles.Bars;

    /// <summary>One of <see cref="StackOrder"/>: which traffic row is drawn first.</summary>
    public string SortOrder { get; set; } = StackOrder.DownloadFirst;

    /// <summary>Installed family name, or empty for the automatic taskbar font.</summary>
    public string FontFamily { get; set; } = FontCatalog.Automatic;

    public bool FontBold { get; set; }

    public bool FontItalic { get; set; }

    /// <summary>Rounded bar caps instead of square ones.</summary>
    public bool RoundedBars { get; set; }

    /// <summary>
    /// Both bars are measured against one full-scale rate, so the rows can be compared
    /// directly: a trickle beside a flood reads as a sliver instead of a full bar.
    /// Turned off, each direction fills against its own peak and both usually read full.
    /// </summary>
    public bool SharedScale { get; set; } = true;

    /// <summary>A soft halo around the fills, for the neon look.</summary>
    public bool AccentGlow { get; set; }

    /// <summary>
    /// How tall the direction arrow is drawn relative to a digit, as a percentage.
    /// See <see cref="DefaultArrowSizePercent"/>.
    /// </summary>
    public int ArrowSizePercent { get; set; } = DefaultArrowSizePercent;

    /// <summary>
    /// Render the speeds the way the clock is rendered: subpixel (ClearType) glyphs on
    /// a fully opaque plate. Off by default because the plate necessarily hides the
    /// taskbar behind the widget; on, it is the only way to get identical sharpness,
    /// since ClearType needs an opaque background to compute its colour fringes.
    /// </summary>
    public bool CrispText { get; set; }

    /// <summary>
    /// Default on: the whole point is a monitor that is always there. It is a
    /// per-user HKCU\...\Run entry, not a Windows service (see StartupRegistration).
    /// </summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Show the CPU chip in the widget.</summary>
    public bool ShowCpu { get; set; }

    /// <summary>Show the GPU chip in the widget. Hides itself when no GPU counter resolves.</summary>
    public bool ShowGpu { get; set; }

    /// <summary>Show the RAM chip in the widget.</summary>
    public bool ShowRam { get; set; }

    /// <summary>Show the free-space chip for the system drive in the widget.</summary>
    public bool ShowDisk { get; set; }

    /// <summary>Show the battery chip while on battery power.</summary>
    public bool ShowBattery { get; set; }

    /// <summary>
    /// The colour of the CPU chip's dot. Empty means "use the built-in palette", so an
    /// existing config keeps the look it had before per-chip colours existed.
    /// </summary>
    public string CpuColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the RAM chip's dot; see <see cref="CpuColorHex"/>.</summary>
    public string RamColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the GPU chip's dot; see <see cref="CpuColorHex"/>.</summary>
    public string GpuColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the disk chip's dot; see <see cref="CpuColorHex"/>.</summary>
    public string DiskColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the battery chip's dot; see <see cref="CpuColorHex"/>.</summary>
    public string BatteryColorHex { get; set; } = string.Empty;

    /// <summary>
    /// The colour of the CPU chip's text. Empty means "follow the widget's figure colour",
    /// so an existing config keeps the look it had before per-chip text colours existed.
    /// </summary>
    public string CpuTextColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the RAM chip's text; see <see cref="CpuTextColorHex"/>.</summary>
    public string RamTextColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the GPU chip's text; see <see cref="CpuTextColorHex"/>.</summary>
    public string GpuTextColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the disk chip's text; see <see cref="CpuTextColorHex"/>.</summary>
    public string DiskTextColorHex { get; set; } = string.Empty;

    /// <summary>The colour of the battery chip's text; see <see cref="CpuTextColorHex"/>.</summary>
    public string BatteryTextColorHex { get; set; } = string.Empty;

    /// <summary>One of <see cref="ChipIndicators"/>: how a chip shows its level.</summary>
    public string ChipIndicator { get; set; } = ChipIndicators.None;

    /// <summary>One of <see cref="ChipIcons"/>: the letter or the vector glyph at the chip's left.</summary>
    public string ChipIcon { get; set; } = ChipIcons.Letter;

    /// <summary>
    /// Draw the bar tracks and fills with the translucent glass look: a frosted track, a
    /// gradient fill and a specular line. Off by default so existing configs look unchanged.
    /// </summary>
    public bool GlassMeters { get; set; }

    /// <summary>
    /// Draw a translucent glass pill behind every chip, whatever the bar style. Off by
    /// default so existing configs look unchanged.
    /// </summary>
    public bool GlassChips { get; set; }

    /// <summary>
    /// Draw each chip's text in that chip's own dot colour, so one colour choice covers both.
    /// Off by default, so the text keeps following the figure colour until asked otherwise.
    /// </summary>
    public bool ChipTextFollowsChip { get; set; }

    /// <summary>Whether any statistic chip is wanted at all.</summary>
    [JsonIgnore]
    public bool AnyStatsEnabled => ShowCpu || ShowGpu || ShowRam || ShowDisk || ShowBattery;

    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HASNetMonitor",
        "config.json");

    /// <summary>
    /// The settings a brand-new installation starts with, written to
    /// <see cref="DefaultConfigPath"/> on first run so the user has a working, good-looking
    /// configuration to adjust instead of an empty one. This is deliberately not the
    /// constructor default: <c>new MonitorConfig()</c> stays the plain classic look the
    /// renderer and the tests treat as the baseline, and only the first-run file gets
    /// this look.
    /// </summary>
    public static MonitorConfig CreateInitial() => new()
    {
        DownloadColorHex = "#2ECC71",
        UploadColorHex = "#E6643C",
        DownloadTextColorMode = TextColorModes.Bar,
        UploadTextColorMode = TextColorModes.Bar,
        DownloadTextColorHex = "#2ECC71",
        UploadTextColorHex = "#E6643C",
        ArrowStyle = ArrowStyles.Small,
        RefreshIntervalMs = 1000,
        OffsetFromTrayPx = 6,
        NudgeXPx = 0,
        NudgeYPx = 0,
        AutoScale = true,
        MaxScaleMbps = 100,
        ShowText = true,
        ShowBackdrop = false,
        BackdropOpacity = 255,
        BarThicknessPx = 3,
        SegmentGapPx = 1,
        FontSizePt = 10.5,
        Style = WidgetStyles.SidebarSegments,
        SortOrder = StackOrder.UploadFirst,
        FontFamily = FontCatalog.Automatic,
        FontBold = true,
        FontItalic = false,
        RoundedBars = true,
        SharedScale = true,
        AccentGlow = false,
        ArrowSizePercent = 150,
        CrispText = false,
        StartWithWindows = true,
        ShowCpu = true,
        ShowGpu = false,
        ShowRam = true,
        ShowDisk = false,
        ChipIndicator = ChipIndicators.Bar,
        ChipTextFollowsChip = true,
    };

    public static string LegacyConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly Color DefaultDownload = Color.FromArgb(0x2E, 0xCC, 0x71);

    private static readonly Color DefaultUpload = Color.FromArgb(0xE6, 0x64, 0x3C);

    [JsonIgnore]
    public Color DownloadColor => ColorHelper.Parse(DownloadColorHex, DefaultDownload);

    [JsonIgnore]
    public Color UploadColor => ColorHelper.Parse(UploadColorHex, DefaultUpload);

    /// <summary>
    /// The colour a chip's dot is drawn in: the user's per-chip colour when set, otherwise
    /// the built-in palette entry for that metric. <paramref name="metric"/> is one of the
    /// <see cref="ChipMetrics"/> names.
    /// </summary>
    public Color ChipColor(string metric) => ColorHelper.Parse(ChipColorHex(metric), ChipMetrics.Default(metric));

    /// <summary>The raw per-chip colour string for a metric, empty when the user has not set one.</summary>
    public string ChipColorHex(string metric) => metric switch
    {
        ChipMetrics.Cpu => CpuColorHex,
        ChipMetrics.Ram => RamColorHex,
        ChipMetrics.Gpu => GpuColorHex,
        ChipMetrics.Disk => DiskColorHex,
        ChipMetrics.Battery => BatteryColorHex,
        _ => string.Empty,
    };

    /// <summary>Sets the per-chip colour string for a metric; an empty value restores the palette default.</summary>
    public void SetChipColorHex(string metric, string? hex)
    {
        string value = hex ?? string.Empty;
        switch (metric)
        {
            case ChipMetrics.Cpu:
                CpuColorHex = value;
                break;
            case ChipMetrics.Ram:
                RamColorHex = value;
                break;
            case ChipMetrics.Gpu:
                GpuColorHex = value;
                break;
            case ChipMetrics.Disk:
                DiskColorHex = value;
                break;
            case ChipMetrics.Battery:
                BatteryColorHex = value;
                break;
        }
    }

    /// <summary>
    /// The colour a chip's text is drawn in: the chip's own dot colour when the user asked
    /// the text to match it, otherwise the user's per-chip text colour when set, otherwise
    /// <paramref name="fallback"/> (the widget's figure colour).
    /// </summary>
    public Color ChipTextColor(string metric, Color fallback) =>
        ChipTextFollowsChip
            ? ChipColor(metric)
            : ColorHelper.Parse(ChipTextColorHex(metric), fallback);

    /// <summary>The raw per-chip text colour string for a metric, empty when the user has not set one.</summary>
    public string ChipTextColorHex(string metric) => metric switch
    {
        ChipMetrics.Cpu => CpuTextColorHex,
        ChipMetrics.Ram => RamTextColorHex,
        ChipMetrics.Gpu => GpuTextColorHex,
        ChipMetrics.Disk => DiskTextColorHex,
        ChipMetrics.Battery => BatteryTextColorHex,
        _ => string.Empty,
    };

    /// <summary>Sets the per-chip text colour string for a metric; an empty value restores the fallback.</summary>
    public void SetChipTextColorHex(string metric, string? hex)
    {
        string value = hex ?? string.Empty;
        switch (metric)
        {
            case ChipMetrics.Cpu:
                CpuTextColorHex = value;
                break;
            case ChipMetrics.Ram:
                RamTextColorHex = value;
                break;
            case ChipMetrics.Gpu:
                GpuTextColorHex = value;
                break;
            case ChipMetrics.Disk:
                DiskTextColorHex = value;
                break;
            case ChipMetrics.Battery:
                BatteryTextColorHex = value;
                break;
        }
    }

    /// <summary>True when the glass chip pill is drawn.</summary>
    [JsonIgnore]
    public bool EffectiveGlassChips => GlassChips;

    /// <summary>
    /// The colour the download figure is drawn in: the taskbar's text colour, the row's
    /// bar colour, or the colour the user picked, according to its mode.
    /// </summary>
    public Color DownloadTextColor(Color taskbarText) => ResolveTextColor(DownloadTextColorMode, DownloadTextColorHex, DownloadColor, taskbarText);

    /// <summary>The colour the upload figure is drawn in; see <see cref="DownloadTextColor"/>.</summary>
    public Color UploadTextColor(Color taskbarText) => ResolveTextColor(UploadTextColorMode, UploadTextColorHex, UploadColor, taskbarText);

    private static Color ResolveTextColor(string mode, string hex, Color barColor, Color taskbarText)
        => TextColorModes.Resolve(mode, hex, barColor, taskbarText);

    [JsonIgnore]
    public int EffectiveRefreshIntervalMs => Numeric.Clamp(RefreshIntervalMs, MinRefreshIntervalMs, MaxRefreshIntervalMs);

    [JsonIgnore]
    public double ManualPeakBytesPerSecond => Math.Max(MaxScaleMbps, 0.01) * 1_000_000 / 8.0;

    public MonitorConfig Clone() => new()
    {
        DownloadColorHex = DownloadColorHex,
        UploadColorHex = UploadColorHex,
        DownloadTextColorMode = DownloadTextColorMode,
        UploadTextColorMode = UploadTextColorMode,
        DownloadTextColorHex = DownloadTextColorHex,
        UploadTextColorHex = UploadTextColorHex,
        ArrowStyle = ArrowStyle,
        RefreshIntervalMs = RefreshIntervalMs,
        OffsetFromTrayPx = OffsetFromTrayPx,
        AutoScale = AutoScale,
        MaxScaleMbps = MaxScaleMbps,
        ShowText = ShowText,
        ShowBackdrop = ShowBackdrop,
        BackdropOpacity = BackdropOpacity,
        BarThicknessPx = BarThicknessPx,
        SegmentGapPx = SegmentGapPx,
        NudgeXPx = NudgeXPx,
        NudgeYPx = NudgeYPx,
        FontSizePt = FontSizePt,
        Style = Style,
        SortOrder = SortOrder,
        FontFamily = FontFamily,
        FontBold = FontBold,
        FontItalic = FontItalic,
        RoundedBars = RoundedBars,
        SharedScale = SharedScale,
        AccentGlow = AccentGlow,
        ArrowSizePercent = ArrowSizePercent,
        CrispText = CrispText,
        StartWithWindows = StartWithWindows,
        ShowCpu = ShowCpu,
        ShowGpu = ShowGpu,
        ShowRam = ShowRam,
        ShowDisk = ShowDisk,
        ShowBattery = ShowBattery,
        CpuColorHex = CpuColorHex,
        RamColorHex = RamColorHex,
        GpuColorHex = GpuColorHex,
        DiskColorHex = DiskColorHex,
        BatteryColorHex = BatteryColorHex,
        CpuTextColorHex = CpuTextColorHex,
        RamTextColorHex = RamTextColorHex,
        GpuTextColorHex = GpuTextColorHex,
        DiskTextColorHex = DiskTextColorHex,
        BatteryTextColorHex = BatteryTextColorHex,
        ChipIndicator = ChipIndicator,
        ChipIcon = ChipIcon,
        GlassMeters = GlassMeters,
        GlassChips = GlassChips,
        ChipTextFollowsChip = ChipTextFollowsChip,
    };

    /// <summary>
    /// Copies every value in place. The widget holds a reference to the live config,
    /// so settings have to be applied to the existing instance rather than swapped.
    /// </summary>
    public void CopyFrom(MonitorConfig other)
    {
        if (other is null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        DownloadColorHex = other.DownloadColorHex;
        UploadColorHex = other.UploadColorHex;
        DownloadTextColorMode = other.DownloadTextColorMode;
        UploadTextColorMode = other.UploadTextColorMode;
        DownloadTextColorHex = other.DownloadTextColorHex;
        UploadTextColorHex = other.UploadTextColorHex;
        ArrowStyle = other.ArrowStyle;
        RefreshIntervalMs = other.RefreshIntervalMs;
        OffsetFromTrayPx = other.OffsetFromTrayPx;
        AutoScale = other.AutoScale;
        MaxScaleMbps = other.MaxScaleMbps;
        ShowText = other.ShowText;
        ShowBackdrop = other.ShowBackdrop;
        BackdropOpacity = other.BackdropOpacity;
        BarThicknessPx = other.BarThicknessPx;
        SegmentGapPx = other.SegmentGapPx;
        NudgeXPx = other.NudgeXPx;
        NudgeYPx = other.NudgeYPx;
        FontSizePt = other.FontSizePt;
        Style = other.Style;
        SortOrder = other.SortOrder;
        FontFamily = other.FontFamily;
        FontBold = other.FontBold;
        FontItalic = other.FontItalic;
        RoundedBars = other.RoundedBars;
        SharedScale = other.SharedScale;
        AccentGlow = other.AccentGlow;
        ArrowSizePercent = other.ArrowSizePercent;
        CrispText = other.CrispText;
        StartWithWindows = other.StartWithWindows;
        ShowCpu = other.ShowCpu;
        ShowGpu = other.ShowGpu;
        ShowRam = other.ShowRam;
        ShowDisk = other.ShowDisk;
        ShowBattery = other.ShowBattery;
        CpuColorHex = other.CpuColorHex;
        RamColorHex = other.RamColorHex;
        GpuColorHex = other.GpuColorHex;
        DiskColorHex = other.DiskColorHex;
        BatteryColorHex = other.BatteryColorHex;
        CpuTextColorHex = other.CpuTextColorHex;
        RamTextColorHex = other.RamTextColorHex;
        GpuTextColorHex = other.GpuTextColorHex;
        DiskTextColorHex = other.DiskTextColorHex;
        BatteryTextColorHex = other.BatteryTextColorHex;
        ChipIndicator = other.ChipIndicator;
        ChipIcon = other.ChipIcon;
        GlassMeters = other.GlassMeters;
        GlassChips = other.GlassChips;
        ChipTextFollowsChip = other.ChipTextFollowsChip;
        Normalize();
    }

    /// <summary>
    /// Clamps every value into a range the renderer can actually draw and replaces
    /// unusable colours with the defaults. Idempotent, so it is safe to call on
    /// values that came from the settings dialog as well as from disk.
    /// </summary>
    public MonitorConfig Normalize()
    {
        DownloadColorHex = ColorHelper.ToHex(ColorHelper.Parse(DownloadColorHex, DefaultDownload));
        UploadColorHex = ColorHelper.ToHex(ColorHelper.Parse(UploadColorHex, DefaultUpload));
        DownloadTextColorMode = TextColorModes.Normalize(DownloadTextColorMode);
        UploadTextColorMode = TextColorModes.Normalize(UploadTextColorMode);
        DownloadTextColorHex = ColorHelper.ToHex(ColorHelper.Parse(DownloadTextColorHex, ColorHelper.Parse(DefaultTextColor, Color.White)));
        UploadTextColorHex = ColorHelper.ToHex(ColorHelper.Parse(UploadTextColorHex, ColorHelper.Parse(DefaultTextColor, Color.White)));
        ArrowStyle = ArrowStyles.Normalize(ArrowStyle);

        RefreshIntervalMs = Numeric.Clamp(RefreshIntervalMs, MinRefreshIntervalMs, MaxRefreshIntervalMs);

        OffsetFromTrayPx = Numeric.Clamp(OffsetFromTrayPx, 0, 64);

        // A nudge is a fine adjustment, never a way to fling the widget off its taskbar:
        // placement clamps the result inside the bar as well.
        NudgeXPx = Numeric.Clamp(NudgeXPx, -MaxNudgePx, MaxNudgePx);
        NudgeYPx = Numeric.Clamp(NudgeYPx, -MaxNudgePx, MaxNudgePx);

        if (double.IsNaN(MaxScaleMbps) || double.IsInfinity(MaxScaleMbps))
        {
            MaxScaleMbps = 100;
        }

        MaxScaleMbps = Numeric.Clamp(MaxScaleMbps, 0.01, 100_000);

        BarThicknessPx = Numeric.Clamp(BarThicknessPx, 1, MaxBarThicknessPx);
        SegmentGapPx = Numeric.Clamp(SegmentGapPx, 0, MaxSegmentGapPx);
        BackdropOpacity = Numeric.Clamp(BackdropOpacity, 0, 255);
        ArrowSizePercent = Numeric.Clamp(ArrowSizePercent, MinArrowSizePercent, MaxArrowSizePercent);

        if (double.IsNaN(FontSizePt) || double.IsInfinity(FontSizePt))
        {
            FontSizePt = 9;
        }

        FontSizePt = Numeric.Clamp(FontSizePt, MinFontSizePt, MaxFontSizePt);

        Style = WidgetStyles.Normalize(Style);
        SortOrder = StackOrder.Normalize(SortOrder);

        // An empty chip colour means "use the palette default", so it is left empty. A
        // non-empty one that does not parse is repaired to the palette colour rather than
        // silently dropped, so a typo does not look like "never set".
        CpuColorHex = NormalizeChipColor(CpuColorHex, ChipMetrics.Cpu);
        RamColorHex = NormalizeChipColor(RamColorHex, ChipMetrics.Ram);
        GpuColorHex = NormalizeChipColor(GpuColorHex, ChipMetrics.Gpu);
        DiskColorHex = NormalizeChipColor(DiskColorHex, ChipMetrics.Disk);
        BatteryColorHex = NormalizeChipColor(BatteryColorHex, ChipMetrics.Battery);

        // Chip text colours follow the same rule: empty means "follow the figure colour",
        // and a value that does not parse is repaired to the default figure colour.
        Color textFallback = ColorHelper.Parse(DefaultTextColor, Color.White);
        CpuTextColorHex = NormalizeChipTextColor(CpuTextColorHex, textFallback);
        RamTextColorHex = NormalizeChipTextColor(RamTextColorHex, textFallback);
        GpuTextColorHex = NormalizeChipTextColor(GpuTextColorHex, textFallback);
        DiskTextColorHex = NormalizeChipTextColor(DiskTextColorHex, textFallback);
        BatteryTextColorHex = NormalizeChipTextColor(BatteryTextColorHex, textFallback);

        ChipIndicator = ChipIndicators.Normalize(ChipIndicator);
        ChipIcon = ChipIcons.Normalize(ChipIcon);

        // Keep only a family that is really here; otherwise the automatic taskbar font.
        FontFamily = FontCatalog.Normalize(FontFamily);

        return this;
    }

    public string ToJson() => ConfigJson.Serialize(this);

    /// <summary>
    /// Repairs one per-chip colour. Empty stays empty (the palette default applies); a
    /// value that does not parse becomes the palette colour so a typo is visible as a
    /// concrete colour rather than silently reverting to "never set".
    /// </summary>
    private static string NormalizeChipColor(string? hex, string metric)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return string.Empty;
        }

        return ColorHelper.ToHex(ColorHelper.Parse(hex, ChipMetrics.Default(metric)));
    }

    /// <summary>
    /// Repairs one per-chip text colour. Empty stays empty (the figure colour applies); a
    /// value that does not parse becomes <paramref name="fallback"/> so a typo is visible.
    /// </summary>
    private static string NormalizeChipTextColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return string.Empty;
        }

        return ColorHelper.ToHex(ColorHelper.Parse(hex, fallback));
    }

    public static MonitorConfig FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MonitorConfig().Normalize();
        }

        try
        {
            return ConfigJson.Deserialize(json!).Normalize();
        }
        catch (ConfigFormatException)
        {
            // A hand-edited file that no longer parses falls back to defaults rather
            // than stopping the app from starting.
            return new MonitorConfig().Normalize();
        }
    }

    /// <summary>
    /// Reads the config. A missing file is not an error the user should have to see: it
    /// yields <see cref="CreateInitial"/>, the settings a fresh install starts with, so
    /// the app has a sensible look even before anything is saved. When no config exists
    /// in the primary location the legacy beside-the-exe file is tried so existing
    /// installs keep their colours. A file that exists but cannot be parsed is repaired
    /// by <see cref="FromJson"/> instead, which falls back to the classic look.
    /// </summary>
    public static MonitorConfig Load(string path, string? legacyFallbackPath = null)
    {
        foreach (string candidate in Candidates(path, legacyFallbackPath))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return FromJson(File.ReadAllText(candidate));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return CreateInitial().Normalize();
    }

    /// <summary>Persists the config, creating the directory if needed.</summary>
    public bool TrySave(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Normalize();
            File.WriteAllText(path, ToJson());
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Candidates(string path, string? legacyFallbackPath)
    {
        if (!string.IsNullOrEmpty(path))
        {
            yield return path;
        }

        if (!string.IsNullOrEmpty(legacyFallbackPath)
            && !string.Equals(legacyFallbackPath, path, StringComparison.OrdinalIgnoreCase))
        {
            yield return legacyFallbackPath!;
        }
    }
}
