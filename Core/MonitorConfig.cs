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
    public const int MaxNudgePx = 400;
    public const double MinFontSizePt = 6;
    public const double MaxFontSizePt = 20;
    public const string DefaultDownloadColor = "#2ECC71";
    public const string DefaultUploadColor = "#E74C3C";

    public string DownloadColorHex { get; set; } = DefaultDownloadColor;

    public string UploadColorHex { get; set; } = DefaultUploadColor;

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

    /// <summary>A soft halo around the fills, for the neon look.</summary>
    public bool AccentGlow { get; set; }

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

    [JsonIgnore]
    public int EffectiveRefreshIntervalMs => Numeric.Clamp(RefreshIntervalMs, MinRefreshIntervalMs, MaxRefreshIntervalMs);

    [JsonIgnore]
    public double ManualPeakBytesPerSecond => Math.Max(MaxScaleMbps, 0.01) * 1_000_000 / 8.0;

    public MonitorConfig Clone() => new()
    {
        DownloadColorHex = DownloadColorHex,
        UploadColorHex = UploadColorHex,
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
        AccentGlow = AccentGlow,
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
        AccentGlow = other.AccentGlow;
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
