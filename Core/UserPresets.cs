using System.Text;

namespace HASNetMonitor.Core;

/// <summary>
/// One look the user saved themselves. This is the on-disk shape, so every value is a
/// plain settable property: .NET Framework's in-box <c>JavaScriptSerializer</c> can only
/// rebuild a type through its properties, not through a constructor, and the modern
/// target has to read the same file back. <see cref="WidgetPreset"/> stays immutable
/// because the built-in gallery is compile-time data; this type is what crosses the file
/// boundary.
/// </summary>
internal sealed class UserPreset
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DownloadColor { get; set; } = string.Empty;

    public string UploadColor { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public string ArrowStyle { get; set; } = string.Empty;

    public string SortOrder { get; set; } = string.Empty;

    public int BarThicknessPx { get; set; }

    public int SegmentGapPx { get; set; }

    public double FontSizePt { get; set; }

    public int ArrowSizePercent { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool RoundedBars { get; set; }

    public bool AccentGlow { get; set; }

    public bool ShowBackdrop { get; set; }

    public bool CrispText { get; set; }

    public int BackdropOpacity { get; set; }

    /// <summary>
    /// The figure-colour mode is stored explicitly rather than left to the derived rule.
    /// A look saved with figures tinted in a colour the user picked must come back that
    /// way even if the derived rule would have chosen differently later.
    /// </summary>
    public string DownloadTextMode { get; set; } = string.Empty;

    public string DownloadTextColor { get; set; } = string.Empty;

    public string UploadTextMode { get; set; } = string.Empty;

    public string UploadTextColor { get; set; } = string.Empty;

    /// <summary>
    /// Captures the appearance of a config as a saveable look. Only appearance is copied:
    /// the refresh cadence, the scale, the position and the logon entry are deliberately
    /// left out, because they are not part of a look and a saved preset must not move a
    /// widget or change what it measures when it is applied.
    /// </summary>
    public static UserPreset FromConfig(string name, MonitorConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        MonitorConfig normalized = config.Clone();

        return new UserPreset
        {
            Name = name,
            Description = $"Your saved look, kept as {name}.",
            DownloadColor = normalized.DownloadColorHex,
            UploadColor = normalized.UploadColorHex,
            Style = normalized.Style,
            ArrowStyle = normalized.ArrowStyle,
            SortOrder = normalized.SortOrder,
            BarThicknessPx = normalized.BarThicknessPx,
            SegmentGapPx = normalized.SegmentGapPx,
            FontSizePt = normalized.FontSizePt,
            ArrowSizePercent = normalized.ArrowSizePercent,
            Bold = normalized.FontBold,
            Italic = normalized.FontItalic,
            RoundedBars = normalized.RoundedBars,
            AccentGlow = normalized.AccentGlow,
            ShowBackdrop = normalized.ShowBackdrop,
            CrispText = normalized.CrispText,
            BackdropOpacity = normalized.BackdropOpacity,
            DownloadTextMode = normalized.DownloadTextColorMode,
            DownloadTextColor = normalized.DownloadTextColorHex,
            UploadTextMode = normalized.UploadTextColorMode,
            UploadTextColor = normalized.UploadTextColorHex,
        };
    }

    /// <summary>
    /// The same look as a <see cref="WidgetPreset"/>, so applying and matching a saved
    /// preset go through exactly the code the built-ins use rather than a second path that
    /// could drift from it.
    /// </summary>
    public WidgetPreset ToPreset() => new(
        Name,
        string.IsNullOrEmpty(Description) ? $"Your saved look, kept as {Name}." : Description,
        DownloadColor,
        UploadColor,
        Style,
        ArrowStyle,
        SortOrder,
        BarThicknessPx,
        SegmentGapPx,
        FontSizePt,
        ArrowSizePercent,
        bold: Bold,
        italic: Italic,
        roundedBars: RoundedBars,
        accentGlow: AccentGlow,
        showBackdrop: ShowBackdrop,
        crispText: CrispText,
        backdropOpacity: BackdropOpacity,
        downloadTextMode: DownloadTextMode,
        downloadTextColor: DownloadTextColor,
        uploadTextMode: UploadTextMode,
        uploadTextColor: UploadTextColor);
}

/// <summary>
/// The user's own presets, stored beside the config so they survive restarts and travel
/// with the rest of the settings. A saved look is data, not code: it is written as JSON,
/// it can be hand-edited, and an unreadable file loses the gallery rather than breaking
/// the app.
/// </summary>
internal static class UserPresets
{
    /// <summary>Longest name accepted, in characters.</summary>
    public const int MaxNameLength = 40;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HASNetMonitor",
        "presets.json");

    /// <summary>
    /// Trims and collapses the name so "  My   Look " and "My Look" are the same preset
    /// rather than two that look identical in the picker.
    /// </summary>
    public static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var built = new StringBuilder(raw!.Length);
        bool lastWasSpace = false;
        foreach (char c in raw.Trim())
        {
            // Control characters would render as nothing in the picker, so a name made of
            // them would read as empty; treat them as separators instead.
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    built.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            built.Append(c);
            lastWasSpace = false;
        }

        return built.ToString();
    }

    /// <summary>
    /// Why a name cannot be used, or null when it can. A name that is already a built-in
    /// is refused rather than allowed to shadow it: the picker would then have two entries
    /// reading the same, and choosing the wrong one would apply a different look.
    /// </summary>
    public static string? ValidateName(string? raw)
    {
        string name = NormalizeName(raw);

        if (name.Length == 0)
        {
            return "Give the preset a name.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Keep the name to {MaxNameLength} characters or fewer.";
        }

        foreach (WidgetPreset preset in WidgetPresets.All)
        {
            if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"\"{preset.Name}\" is a built-in preset. Choose another name.";
            }
        }

        return null;
    }

    /// <summary>
    /// The saved looks, in the order they were saved. A missing file is simply no presets;
    /// a damaged one is treated the same way rather than surfacing an error the user
    /// cannot act on.
    /// </summary>
    public static IReadOnlyList<WidgetPreset> Load(string? path = null)
    {
        string file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            List<UserPreset>? stored = ReadJson(File.ReadAllText(file));
            if (stored is null || stored.Count == 0)
            {
                return [];
            }

            var result = new List<WidgetPreset>(stored.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UserPreset entry in stored)
            {
                if (entry is null || ValidateName(entry.Name) is not null)
                {
                    // A hand-edited file can contain a blank name or one that collides with
                    // a built-in. Dropping that entry keeps the picker unambiguous.
                    continue;
                }

                if (!seen.Add(NormalizeName(entry.Name)))
                {
                    // Keep the first of a duplicated name: the later entry cannot be told
                    // apart in the picker, so it would be unselectable.
                    continue;
                }

                result.Add(entry.ToPreset());
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Saves a look under its name, replacing any saved preset with the same name so that
    /// saving the same name twice updates it instead of piling up duplicates. Returns the
    /// full list after saving, and the reason it failed when nothing was written.
    /// </summary>
    public static bool TrySave(WidgetPreset preset, string? path, out IReadOnlyList<WidgetPreset> all, out string? error)
    {
        all = [];
        error = null;

        if (preset is null)
        {
            throw new ArgumentNullException(nameof(preset));
        }

        error = ValidateName(preset.Name);
        if (error is not null)
        {
            return false;
        }

        string name = NormalizeName(preset.Name);
        bool saved = TryWrite(path, [preset], out error, replace: true, out all);
        return saved && all.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Rewrites the whole file from the given looks. Used by delete, which cannot express
    /// its intent as an add. The list is written as given, so the caller is responsible for
    /// having removed the entry it meant to remove.
    /// </summary>
    public static bool TryWrite(string? path, IEnumerable<WidgetPreset> presets, out string? error)
    {
        if (presets is null)
        {
            throw new ArgumentNullException(nameof(presets));
        }

        return TryWrite(path, presets, out error, replace: false, out _);
    }

    private static bool TryWrite(
        string? path,
        IEnumerable<WidgetPreset> presets,
        out string? error,
        bool replace,
        out IReadOnlyList<WidgetPreset> all)
    {
        all = [];
        error = null;

        string file = path ?? DefaultPath;

        try
        {
            List<UserPreset> stored = replace
                ? ReadJson(File.Exists(file) ? File.ReadAllText(file) : null) ?? []
                : [];

            foreach (WidgetPreset preset in presets)
            {
                string? complaint = ValidateName(preset.Name);
                if (complaint is not null)
                {
                    error = complaint;
                    return false;
                }

                string name = NormalizeName(preset.Name);

                // Replace case-insensitively: "my look" and "My Look" are the same entry to
                // the user looking at the picker, so the newer one wins.
                int existing = stored.FindIndex(e => e is not null
                    && string.Equals(NormalizeName(e.Name), name, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    stored[existing] = UserPreset.FromConfig(name, ToConfig(preset));
                }
                else
                {
                    stored.Add(UserPreset.FromConfig(name, ToConfig(preset)));
                }
            }

            string? directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            WriteAtomically(file, WriteJson(stored));
            all = Load(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "The preset could not be saved to disk.";
            return false;
        }
    }

    /// <summary>
    /// The appearance a preset describes, so it can be stored through the same DTO the
    /// file uses. Position and cadence are absent by design; the store never holds them.
    /// </summary>
    private static MonitorConfig ToConfig(WidgetPreset preset) => new()
    {
        DownloadColorHex = preset.DownloadColor,
        UploadColorHex = preset.UploadColor,
        Style = preset.Style,
        ArrowStyle = preset.ArrowStyle,
        SortOrder = preset.SortOrder,
        BarThicknessPx = preset.BarThicknessPx,
        SegmentGapPx = preset.SegmentGapPx,
        FontSizePt = preset.FontSizePt,
        ArrowSizePercent = preset.ArrowSizePercent,
        FontBold = preset.Bold,
        FontItalic = preset.Italic,
        RoundedBars = preset.RoundedBars,
        AccentGlow = preset.AccentGlow,
        ShowBackdrop = preset.ShowBackdrop,
        CrispText = preset.CrispText,
        BackdropOpacity = preset.BackdropOpacity,
        DownloadTextColorMode = preset.DownloadTextMode ?? TextColorModes.Auto,
        DownloadTextColorHex = preset.DownloadTextColor ?? preset.DownloadColor,
        UploadTextColorMode = preset.UploadTextMode ?? TextColorModes.Auto,
        UploadTextColorHex = preset.UploadTextColor ?? preset.UploadColor,
    };

    /// <summary>
    /// Writes through a temporary file and then moves it into place, so an interrupted
    /// write leaves the previous presets intact instead of a half-written file that would
    /// load as no presets at all.
    /// </summary>
    private static void WriteAtomically(string file, string contents)
    {
        string temp = file + ".tmp";
        File.WriteAllText(temp, contents);
        File.Copy(temp, file, overwrite: true);

        try
        {
            File.Delete(temp);
        }
        catch (IOException)
        {
        }
    }

#if NETFRAMEWORK
    private static readonly object Gate = new();

    private static List<UserPreset>? ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            lock (Gate)
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer
                {
                    MaxJsonLength = int.MaxValue,
                };

                return serializer.Deserialize<List<UserPreset>>(json);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.Runtime.Serialization.SerializationException or FormatException)
        {
            return null;
        }
    }

    private static string WriteJson(List<UserPreset> presets)
    {
        lock (Gate)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
            };

            // Indented so the file stays readable and hand-editable, like the config.
            return serializer.Serialize(presets).Replace("},{", "},\n  {");
        }
    }
#else
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static List<UserPreset>? ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<UserPreset>>(json!, SerializerOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string WriteJson(List<UserPreset> presets) =>
        System.Text.Json.JsonSerializer.Serialize(presets, SerializerOptions);
#endif
}
