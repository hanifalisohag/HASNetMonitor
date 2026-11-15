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
    public const string DefaultUploadColor = "#E74C3C";

    public string DownloadColorHex { get; set; } = DefaultDownloadColor;

    public string UploadColorHex { get; set; } = DefaultUploadColor;

    /// <summary>Where the download figure takes its colour from; see <see cref="TextColorModes"/>.</summary>
    public string DownloadTextColorMode { get; set; } = TextColorModes.Auto;

    /// <summary>Where the upload figure takes its colour from; see <see cref="TextColorModes"/>.</summary>
    public string UploadTextColorMode { get; set; } = TextColorModes.Auto;

    /// <summary>Used for the download figure when its mode is <see cref="TextColorModes.Custom"/>.</summary>
    public string DownloadTextColorHex { get; set; } = DefaultTextColor;

    /// <summary>Used for the upload figure when its mode is <see cref="TextColorModes.Custom"/>.</summary>
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

    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HASNetMonitor",
        "config.json");

    public static string LegacyConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly Color DefaultDownload = Color.FromArgb(0x2E, 0xCC, 0x71);

    private static readonly Color DefaultUpload = Color.FromArgb(0xE7, 0x4C, 0x3C);

    [JsonIgnore]
    public Color DownloadColor => ColorHelper.Parse(DownloadColorHex, DefaultDownload);

    [JsonIgnore]
    public Color UploadColor => ColorHelper.Parse(UploadColorHex, DefaultUpload);

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

        // Keep only a family that is really here; otherwise the automatic taskbar font.
        FontFamily = FontCatalog.Normalize(FontFamily);

        return this;
    }

    public string ToJson() => ConfigJson.Serialize(this);

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
    /// Reads the config. A missing or broken file is not an error the user should
    /// have to see: it yields defaults. When no config exists in the primary
    /// location the legacy beside-the-exe file is tried so existing installs keep
    /// their colours.
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

        return new MonitorConfig().Normalize();
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
