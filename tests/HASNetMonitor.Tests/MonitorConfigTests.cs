using System.Drawing;
using System.Text.Json;
using HASNetMonitor.Core;

namespace HASNetMonitor.Tests;

public class MonitorConfigTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tnm-tests-" + Guid.NewGuid().ToString("N"));

    public MonitorConfigTests() => Directory.CreateDirectory(_folder);

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

    [Fact]
    public void DefaultsAreAlreadyNormalised()
    {
        var config = new MonitorConfig();
        string before = JsonSerializer.Serialize(config);

        config.Normalize();

        Assert.Equal(before, JsonSerializer.Serialize(config));
        Assert.Equal("#2ECC71", config.DownloadColorHex);
        Assert.Equal("#E6643C", config.UploadColorHex);
        Assert.Equal(1000, config.RefreshIntervalMs);
        Assert.True(config.ShowText);
        Assert.False(config.ShowBackdrop);
        Assert.InRange(config.FontSizePt, MonitorConfig.MinFontSizePt, MonitorConfig.MaxFontSizePt);
    }

    [Fact]
    public void DownloadIsGreenAndUploadIsRedByDefault()
    {
        var config = new MonitorConfig().Normalize();

        Assert.True(config.DownloadColor.G > 150 && config.DownloadColor.G > config.DownloadColor.R);
        Assert.True(config.UploadColor.R > 150 && config.UploadColor.R > config.UploadColor.G);
    }

    [Fact]
    public void RoundTripKeepsEverySetting()
    {
        var original = new MonitorConfig
        {
            DownloadColorHex = "#112233",
            UploadColorHex = "#AABBCC",
            RefreshIntervalMs = 2500,
            OffsetFromTrayPx = 12,
            AutoScale = false,
            MaxScaleMbps = 42.5,
            ShowText = false,
            ShowBackdrop = true,
            BackdropOpacity = 200,
            BarThicknessPx = 5,
            FontSizePt = 11.5,
            StartWithWindows = true,
        }.Normalize();

        Assert.True(original.TrySave(ConfigPath));
        MonitorConfig loaded = MonitorConfig.Load(ConfigPath);

        Assert.Equal("#112233", loaded.DownloadColorHex);
        Assert.Equal("#AABBCC", loaded.UploadColorHex);
        Assert.Equal(2500, loaded.RefreshIntervalMs);
        Assert.Equal(12, loaded.OffsetFromTrayPx);
        Assert.False(loaded.AutoScale);
        Assert.Equal(42.5, loaded.MaxScaleMbps, 3);
        Assert.False(loaded.ShowText);
        Assert.True(loaded.ShowBackdrop);
        Assert.Equal(200, loaded.BackdropOpacity);
        Assert.Equal(5, loaded.BarThicknessPx);
        Assert.Equal(11.5, loaded.FontSizePt, 3);
        Assert.True(loaded.StartWithWindows);
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var config = new MonitorConfig
        {
            RefreshIntervalMs = 1,
            OffsetFromTrayPx = -30,
            MaxScaleMbps = -5,
            BarThicknessPx = 400,
            FontSizePt = 900,
            BackdropOpacity = 1000,
        }.Normalize();

        Assert.Equal(MonitorConfig.MinRefreshIntervalMs, config.RefreshIntervalMs);
        Assert.Equal(0, config.OffsetFromTrayPx);
        Assert.True(config.MaxScaleMbps > 0);
        Assert.Equal(MonitorConfig.MaxBarThicknessPx, config.BarThicknessPx);
        Assert.Equal(MonitorConfig.MaxFontSizePt, config.FontSizePt);
        Assert.Equal(255, config.BackdropOpacity);

        config = new MonitorConfig { RefreshIntervalMs = int.MaxValue, FontSizePt = 0.1, BarThicknessPx = -3 }.Normalize();
        Assert.Equal(MonitorConfig.MaxRefreshIntervalMs, config.RefreshIntervalMs);
        Assert.Equal(MonitorConfig.MinFontSizePt, config.FontSizePt);
        Assert.Equal(1, config.BarThicknessPx);
    }

    [Fact]
    public void NotANumberFontSizeFallsBackToTheDefault()
    {
        var config = new MonitorConfig { FontSizePt = double.NaN }.Normalize();

        Assert.InRange(config.FontSizePt, MonitorConfig.MinFontSizePt, MonitorConfig.MaxFontSizePt);
        Assert.False(double.IsNaN(config.FontSizePt));
    }

    [Theory]
    [InlineData("not-a-colour")]
    [InlineData("#12345")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#GGGGGG")]
    public void InvalidColoursFallBackToTheDefaults(string? broken)
    {
        var config = new MonitorConfig { DownloadColorHex = broken!, UploadColorHex = broken! }.Normalize();

        Assert.Equal("#2ECC71", config.DownloadColorHex);
        Assert.Equal("#E6643C", config.UploadColorHex);
    }

    [Fact]
    public void ColoursAreNormalisedToUppercaseHex()
    {
        var config = new MonitorConfig { DownloadColorHex = "00ff00", UploadColorHex = "#00ff00" }.Normalize();

        Assert.Equal("#00FF00", config.DownloadColorHex);
        Assert.Equal("#00FF00", config.UploadColorHex);
    }

    [Fact]
    public void MissingFileGivesDefaultsAndDoesNotThrow()
    {
        MonitorConfig config = MonitorConfig.Load(ConfigPath);

        Assert.Equal("#2ECC71", config.DownloadColorHex);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public void CorruptFileGivesDefaultsInsteadOfCrashing()
    {
        File.WriteAllText(ConfigPath, "{ this is not json ");

        MonitorConfig config = MonitorConfig.Load(ConfigPath);

        Assert.Equal(1000, config.RefreshIntervalMs);
        Assert.Equal("#2ECC71", config.DownloadColorHex);
    }

    [Fact]
    public void UnknownAndMissingKeysAreIgnored()
    {
        File.WriteAllText(ConfigPath, """
            {
              "DownloadColorHex": "#0000FF",
              "SomeFutureSetting": 12,
              "Nested": { "A": [1, 2, 3] }
            }
            """);

        MonitorConfig config = MonitorConfig.Load(ConfigPath);

        Assert.Equal("#0000FF", config.DownloadColorHex);
        Assert.Equal(1000, config.RefreshIntervalMs);
    }

    [Fact]
    public void KeysAreCaseInsensitiveAndCommentsAreAllowed()
    {
        File.WriteAllText(ConfigPath, """
            {
              // the user hand-edited this file
              "downloadcolorhex": "#0A0B0C",
              "refreshintervalms": 500,
            }
            """);

        MonitorConfig config = MonitorConfig.Load(ConfigPath);

        Assert.Equal("#0A0B0C", config.DownloadColorHex);
        Assert.Equal(500, config.RefreshIntervalMs);
    }

    [Fact]
    public void ValuesAhumanTypedOutOfRangeAreRepairedOnLoad()
    {
        File.WriteAllText(ConfigPath, """{ "RefreshIntervalMs": 999999, "DownloadColorHex": "green" }""");

        MonitorConfig config = MonitorConfig.Load(ConfigPath);

        Assert.Equal(MonitorConfig.MaxRefreshIntervalMs, config.RefreshIntervalMs);
        Assert.Equal("#2ECC71", config.DownloadColorHex);
    }

    [Fact]
    public void ALegacyConfigNextToTheProgramIsPickedUpOnce()
    {
        string legacy = Path.Combine(_folder, "legacy.json");
        File.WriteAllText(legacy, """{ "OffsetFromTrayPx": 8, "RefreshIntervalMs": 750 }""");

        MonitorConfig config = MonitorConfig.Load(ConfigPath, legacy);

        Assert.Equal(8, config.OffsetFromTrayPx);
        Assert.Equal(750, config.RefreshIntervalMs);

        // Once a real config exists it wins over the legacy file.
        config.TrySave(ConfigPath);
        File.WriteAllText(legacy, """{ "OffsetFromTrayPx": 99 }""");
        Assert.Equal(8, MonitorConfig.Load(ConfigPath, legacy).OffsetFromTrayPx);
    }

    [Fact]
    public void SavingCreatesTheFolderAndIsReadableBack()
    {
        string nested = Path.Combine(_folder, "deep", "nested", "config.json");

        Assert.True(new MonitorConfig().TrySave(nested));
        Assert.True(File.Exists(nested));
        Assert.DoesNotContain("DownloadColor\"", File.ReadAllText(nested), StringComparison.Ordinal);
        Assert.NotNull(MonitorConfig.Load(nested));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var original = new MonitorConfig { DownloadColorHex = "#010203" }.Normalize();
        MonitorConfig copy = original.Clone();

        copy.DownloadColorHex = "#0A0B0C";

        Assert.Equal("#010203", original.DownloadColorHex);
        Assert.Equal("#0A0B0C", copy.DownloadColorHex);
    }

    [Fact]
    public void CopyFromOverwritesEveryField()
    {
        var target = new MonitorConfig();
        var source = new MonitorConfig
        {
            DownloadColorHex = "#0A0B0C",
            UploadColorHex = "#0C0B0A",
            RefreshIntervalMs = 250,
            OffsetFromTrayPx = 4,
            AutoScale = false,
            MaxScaleMbps = 10,
            ShowText = false,
            ShowBackdrop = true,
            BackdropOpacity = 10,
            BarThicknessPx = 2,
            FontSizePt = 8,
            StartWithWindows = true,
        }.Normalize();

        target.CopyFrom(source);

        Assert.Equal(source.DownloadColorHex, target.DownloadColorHex);
        Assert.Equal(source.UploadColorHex, target.UploadColorHex);
        Assert.Equal(source.RefreshIntervalMs, target.RefreshIntervalMs);
        Assert.Equal(source.OffsetFromTrayPx, target.OffsetFromTrayPx);
        Assert.Equal(source.AutoScale, target.AutoScale);
        Assert.Equal(source.MaxScaleMbps, target.MaxScaleMbps);
        Assert.Equal(source.ShowText, target.ShowText);
        Assert.Equal(source.ShowBackdrop, target.ShowBackdrop);
        Assert.Equal(source.BackdropOpacity, target.BackdropOpacity);
        Assert.Equal(source.BarThicknessPx, target.BarThicknessPx);
        Assert.Equal(source.FontSizePt, target.FontSizePt);
        Assert.Equal(source.StartWithWindows, target.StartWithWindows);
    }

    [Fact]
    public void ManualPeakIsMegabitsToBytesPerSecond()
    {
        var config = new MonitorConfig { AutoScale = false, MaxScaleMbps = 100 }.Normalize();

        Assert.Equal(12_500_000, config.ManualPeakBytesPerSecond, 0);
    }

    [Fact]
    public void NormalizeReturnsAUsableConfigForGarbage()
    {
        var config = new MonitorConfig
        {
            DownloadColorHex = null!,
            UploadColorHex = "  ",
            RefreshIntervalMs = -5,
            MaxScaleMbps = double.NaN,
            FontSizePt = double.PositiveInfinity,
        }.Normalize();

        Assert.Equal("#2ECC71", config.DownloadColorHex);
        Assert.Equal("#E6643C", config.UploadColorHex);
        Assert.InRange(config.RefreshIntervalMs, MonitorConfig.MinRefreshIntervalMs, MonitorConfig.MaxRefreshIntervalMs);
        Assert.False(double.IsNaN(config.MaxScaleMbps));
        Assert.False(double.IsInfinity(config.FontSizePt));
        Assert.Equal(Color.FromArgb(0x2E, 0xCC, 0x71), config.DownloadColor);
    }
}
