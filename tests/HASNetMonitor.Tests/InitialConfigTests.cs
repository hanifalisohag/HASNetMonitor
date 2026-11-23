using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using HASNetMonitor.Core;

namespace HASNetMonitor.Tests;

/// <summary>
/// The settings a brand-new installation starts from: the preset the app writes when no
/// config.json exists, and the copy Setup seeds into %APPDATA% for a new user.
/// </summary>
public class InitialConfigTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "tnm-initial-" + Guid.NewGuid().ToString("N"));

    public InitialConfigTests() => Directory.CreateDirectory(_folder);

    private string ConfigPath => Path.Combine(_folder, "config.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string InstallerConfigPath()
        => Path.Combine(SourceText.Root(), "installer", "config.default.json");

    // ---- the preset itself ------------------------------------------------------------

    [Fact]
    public void AMissingFileGivesTheInitialPreset()
    {
        MonitorConfig config = MonitorConfig.Load(ConfigPath);

        Assert.False(File.Exists(ConfigPath));
        Assert.False(config.ShowBackdrop);
        Assert.Equal(WidgetStyles.SidebarSegments, config.Style);
        Assert.Equal(ArrowStyles.Small, config.ArrowStyle);
    }

    [Fact]
    public void TheInitialPresetIsTheUsersCurrentSettings()
    {
        // Every value below is what a fresh install creates; the preset exists so a new
        // install starts from that working configuration rather than a bare one.
        MonitorConfig c = MonitorConfig.CreateInitial();

        Assert.Equal("#2ECC71", c.DownloadColorHex);
        Assert.Equal("#E6643C", c.UploadColorHex);
        Assert.Equal(TextColorModes.Bar, c.DownloadTextColorMode);
        Assert.Equal(TextColorModes.Bar, c.UploadTextColorMode);
        Assert.Equal("#2ECC71", c.DownloadTextColorHex);
        Assert.Equal("#E6643C", c.UploadTextColorHex);
        Assert.Equal(ArrowStyles.Small, c.ArrowStyle);
        Assert.Equal(1000, c.RefreshIntervalMs);
        Assert.Equal(6, c.OffsetFromTrayPx);
        Assert.Equal(0, c.NudgeXPx);
        Assert.Equal(0, c.NudgeYPx);
        Assert.True(c.AutoScale);
        Assert.Equal(100, c.MaxScaleMbps);
        Assert.True(c.ShowText);
        Assert.False(c.ShowBackdrop);
        Assert.Equal(255, c.BackdropOpacity);
        Assert.Equal(4, c.BarThicknessPx);
        Assert.Equal(0, c.SegmentGapPx);
        Assert.Equal(10.5, c.FontSizePt);
        Assert.Equal(WidgetStyles.SidebarSegments, c.Style);
        Assert.Equal(StackOrder.UploadFirst, c.SortOrder);
        Assert.Equal(FontCatalog.Automatic, c.FontFamily);
        Assert.True(c.FontBold);
        Assert.False(c.FontItalic);
        Assert.True(c.RoundedBars);
        Assert.True(c.SharedScale);
        Assert.False(c.AccentGlow);
        Assert.Equal(150, c.ArrowSizePercent);
        Assert.False(c.CrispText);
        Assert.True(c.StartWithWindows);
    }

    [Fact]
    public void TheInitialPresetSurvivesANormaliseAndJsonRoundTrip()
    {
        MonitorConfig initial = MonitorConfig.CreateInitial();
        string json = ConfigJson.Serialize(initial);

        MonitorConfig parsed = MonitorConfig.FromJson(json);

        Assert.Equal(json, ConfigJson.Serialize(parsed));
        Assert.Equal(json, ConfigJson.Serialize(initial.Normalize()));
    }

    [Fact]
    public void SavingTheInitialPresetAndLoadingItBackKeepsEveryValue()
    {
        Assert.True(MonitorConfig.CreateInitial().TrySave(ConfigPath));

        MonitorConfig reloaded = MonitorConfig.Load(ConfigPath);

        Assert.Equal(
            ConfigJson.Serialize(MonitorConfig.CreateInitial()),
            ConfigJson.Serialize(reloaded));
    }

    [Fact]
    public void TheClassicConstructorDefaultIsNotTheFirstRunPreset()
    {
        // The renderer and the older tests use new MonitorConfig() as the plain baseline;
        // the nicer first-run look is a separate preset, so this stays true.
        var classic = new MonitorConfig();

        Assert.Equal(WidgetStyles.Bars, classic.Style);
        Assert.NotEqual(classic.Style, MonitorConfig.CreateInitial().Style);
    }

    [Fact]
    public void ABrokenFileIsStillRepairedByTheClassicLook()
    {
        // Only an absent file means "new install". A file the user (or an older version)
        // wrote badly keeps the documented repair path rather than silently becoming the
        // preset and losing the visible difference between the two cases.
        File.WriteAllText(ConfigPath, "{ not json ");

        Assert.Equal(WidgetStyles.Bars, MonitorConfig.Load(ConfigPath).Style);
    }

    [Fact]
    public void AnExistingFileIsNeverReplacedByThePreset()
    {
        var chosen = new MonitorConfig { Style = WidgetStyles.Minimal, RefreshIntervalMs = 250 };
        Assert.True(chosen.TrySave(ConfigPath));

        MonitorConfig loaded = MonitorConfig.Load(ConfigPath);

        Assert.Equal(WidgetStyles.Minimal, loaded.Style);
        Assert.Equal(250, loaded.RefreshIntervalMs);
    }

    [Fact]
    public void ALegacyConfigIsPreferredOverThePreset()
    {
        string legacy = Path.Combine(_folder, "legacy.json");
        Assert.True(new MonitorConfig { UploadColorHex = "#010203" }.TrySave(legacy));
        Assert.False(File.Exists(ConfigPath));

        MonitorConfig loaded = MonitorConfig.Load(ConfigPath, legacy);

        Assert.Equal("#010203", loaded.UploadColorHex);
    }

    [Fact]
    public void ThePresetIsAlreadyNormalisedSoNothingChangesOnSave()
    {
        MonitorConfig initial = MonitorConfig.CreateInitial();
        string before = ConfigJson.Serialize(initial);

        initial.Normalize();

        Assert.Equal(before, ConfigJson.Serialize(initial));
    }

    [Fact]
    public void ThePathIsUnderRoamingAppData()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Equal(
            Path.Combine(roaming, "HASNetMonitor", "config.json"),
            MonitorConfig.DefaultConfigPath);
        Assert.StartsWith(roaming, MonitorConfig.DefaultConfigPath, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the copy the installer ships -------------------------------------------------

    [Fact]
    public void TheInstallerCopyMatchesThePresetExactly()
    {
        // installer\config.default.json is what Setup seeds for a new user. If it drifts
        // from CreateInitial, a fresh install and a first run would start differently.
        string json = File.ReadAllText(InstallerConfigPath());
        MonitorConfig fromFile = MonitorConfig.FromJson(json);

        Assert.Equal(
            ConfigJson.Serialize(MonitorConfig.CreateInitial()),
            ConfigJson.Serialize(fromFile));
    }

    [Fact]
    public void TheInstallerCopyNamesEveryPropertyThePresetHas()
    {
        // A field added to the config later must reach the shipped file too, otherwise a
        // new install silently misses it and only the app's own fallback fills it in.
        using var document = JsonDocument.Parse(File.ReadAllText(InstallerConfigPath()));
        var written = new HashSet<string>(
            document.RootElement.EnumerateObject().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        using var preset = JsonDocument.Parse(ConfigJson.Serialize(MonitorConfig.CreateInitial()));
        foreach (string name in preset.RootElement.EnumerateObject().Select(p => p.Name))
        {
            Assert.Contains(name, written);
        }
    }

    [Fact]
    public void SetupSeedsTheConfigOnlyWhenTheUserHasNone()
    {
        string script = SourceText.Read("installer", "HASNetMonitor.iss");

        // The template ships inside the Setup...
        Assert.Contains("config.default.json", script, StringComparison.Ordinal);

        // ...and Setup copies it to the user's folder only when nothing is there yet.
        string seed = SourceText.ExtractProcedure("installer", "HASNetMonitor.iss", "procedure SeedConfig;");
        Assert.Contains("FileExists(ConfigPath + '\\config.json')", seed, StringComparison.Ordinal);
        Assert.Contains("Exit;", seed, StringComparison.Ordinal);
        Assert.Contains("CopyFile(TemplatePath, ConfigPath + '\\config.json', False);", seed, StringComparison.Ordinal);

        // It has to run after the files exist, or the template is not there to copy.
        Assert.Contains("if CurStep = ssPostInstall then", script, StringComparison.Ordinal);

        // The user's file must not be an installed file: if Setup owned it, uninstalling
        // would delete their settings. Only the template is installed.
        foreach (string line in script.Split('\n'))
        {
            if (line.TrimStart().StartsWith("Source:", StringComparison.Ordinal)
                && line.Contains("config", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("config.default.json", line, StringComparison.Ordinal);
            }
        }
    }

    // ---- the published defaults table -----------------------------------------------

    [Theory]
    [InlineData("Style", "SidebarSegments")]
    [InlineData("ArrowStyle", "Small")]
    [InlineData("BarThicknessPx", "4")]
    [InlineData("SegmentGapPx", "0")]
    [InlineData("FontSizePt", "10.5")]
    [InlineData("SortOrder", "UploadFirst")]
    [InlineData("BackdropOpacity", "255")]
    [InlineData("DownloadTextColorMode", "Bar")]
    [InlineData("UploadTextColorMode", "Bar")]
    [InlineData("UploadTextColorHex", "#E6643C")]
    public void TheReadmeDocumentsTheShippedDefault(string key, string value)
    {
        // The table is what a user reads to understand a fresh install; it has to agree
        // with the preset, or the docs describe a config nobody has. It is column-aligned,
        // so match the row without pinning the padding: what matters is that the shipped
        // default is documented, not how much whitespace surrounds it.
        string readme = SourceText.Read(".", "README.md");

        Regex row = new($@"\|\s*`{Regex.Escape(key)}`\s*\|\s*`{Regex.Escape(value)}`\s*\|", RegexOptions.CultureInvariant);

        Assert.True(row.IsMatch(readme), $"README defaults table has no `{key}` row with `{value}`");
    }

    [Fact]
    public void TheReadmeExplainsWhereTheFirstRunSettingsComeFrom()
    {
        string readme = SourceText.Read(".", "README.md");

        Assert.Contains("config.default.json", readme, StringComparison.Ordinal);
        Assert.Contains("never overwritten", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAppWritesTheConfigOnFirstRun()
    {
        string host = SourceText.Read("UI", "MonitorHost.cs");

        Assert.Contains("bool firstRun = !File.Exists(_configPath);", host, StringComparison.Ordinal);
        Assert.Contains("MonitorConfig.LegacyConfigPath", host, StringComparison.Ordinal);

        // The migrated config is what gets saved, so a first run must not overwrite it
        // with the preset after Load has already chosen the legacy settings.
        Assert.DoesNotContain("CopyFrom(MonitorConfig.CreateInitial())", host, StringComparison.Ordinal);
    }
}
