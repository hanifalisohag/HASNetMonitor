using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarNetMonitor.Core;

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
    public const int MaxBarThicknessPx = 12;
    public const double MinFontSizePt = 6;
    public const double MaxFontSizePt = 20;
    public const string DefaultDownloadColor = "#2ECC71";
    public const string DefaultUploadColor = "#E74C3C";

    public string DownloadColorHex { get; set; } = DefaultDownloadColor;

    public string UploadColorHex { get; set; } = DefaultUploadColor;

    public int RefreshIntervalMs { get; set; } = DefaultRefreshIntervalMs;

    public int OffsetFromTrayPx { get; set; } = 6;

    public bool AutoScale { get; set; } = true;

    public double MaxScaleMbps { get; set; } = 100;

    public bool ShowText { get; set; } = true;

    public bool ShowBackdrop { get; set; }

    public int BackdropOpacity { get; set; } = 120;

    public int BarThicknessPx { get; set; } = 3;

    public double FontSizePt { get; set; } = 9;

    /// <summary>
    /// Default on: the whole point is a monitor that is always there. It is a
    /// per-user HKCU\...\Run entry, not a Windows service (see StartupRegistration).
    /// </summary>
    public bool StartWithWindows { get; set; } = true;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarNetMonitor",
        "config.json");

    public static string LegacyConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static readonly Color DefaultDownload = Color.FromArgb(0x2E, 0xCC, 0x71);

    private static readonly Color DefaultUpload = Color.FromArgb(0xE7, 0x4C, 0x3C);

    [JsonIgnore]
    public Color DownloadColor => ColorHelper.Parse(DownloadColorHex, DefaultDownload);

    [JsonIgnore]
    public Color UploadColor => ColorHelper.Parse(UploadColorHex, DefaultUpload);

    [JsonIgnore]
    public int EffectiveRefreshIntervalMs => Math.Clamp(RefreshIntervalMs, MinRefreshIntervalMs, MaxRefreshIntervalMs);

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
        FontSizePt = FontSizePt,
        StartWithWindows = StartWithWindows,
    };

    /// <summary>
    /// Copies every value in place. The widget holds a reference to the live config,
    /// so settings have to be applied to the existing instance rather than swapped.
    /// </summary>
    public void CopyFrom(MonitorConfig other)
    {
        ArgumentNullException.ThrowIfNull(other);

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
        FontSizePt = other.FontSizePt;
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

        RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, MinRefreshIntervalMs, MaxRefreshIntervalMs);

        OffsetFromTrayPx = Math.Clamp(OffsetFromTrayPx, 0, 64);

        if (double.IsNaN(MaxScaleMbps) || double.IsInfinity(MaxScaleMbps))
        {
            MaxScaleMbps = 100;
        }

        MaxScaleMbps = Math.Clamp(MaxScaleMbps, 0.01, 100_000);

        BarThicknessPx = Math.Clamp(BarThicknessPx, 1, MaxBarThicknessPx);
        BackdropOpacity = Math.Clamp(BackdropOpacity, 0, 255);

        if (double.IsNaN(FontSizePt) || double.IsInfinity(FontSizePt))
        {
            FontSizePt = 9;
        }

        FontSizePt = Math.Clamp(FontSizePt, MinFontSizePt, MaxFontSizePt);

        return this;
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static MonitorConfig FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MonitorConfig().Normalize();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MonitorConfig>(json, SerializerOptions);
            return (parsed ?? new MonitorConfig()).Normalize();
        }
        catch (JsonException)
        {
            return new MonitorConfig().Normalize();
        }
        catch (NotSupportedException)
        {
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
