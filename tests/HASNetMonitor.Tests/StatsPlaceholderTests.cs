using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// A metric that is enabled but has no reading must keep its chip and show a placeholder,
/// never drop out. Dropping the chip changes the widget's size, and a size change forces a
/// new surface - the operation that can fail and leave the widget frozen. Keeping the slot
/// means a reading that comes and goes cannot resize the widget at all.
/// </summary>
public class StatsPlaceholderTests
{
    private static MonitorConfig ChipConfig() => new MonitorConfig
    {
        ShowCpu = true,
        ShowRam = true,
        ShowGpu = true,
        ShowDisk = true,
        ShowBattery = true,
        ShowText = true,
    }.Normalize();

    private static SystemStatsReading KnownReading() => new SystemStatsReading(
        42.0,
        71.0,
        55.0,
        107_374_182_400L,
        new DiskMonitor.DiskReading("C:\\", 107_374_182_400L, 200_000_000_000L),
        new BatteryMonitor.BatteryReading(OnBattery: true, Percent: 67, Charging: false));

    [Fact]
    public void AnEnabledMetricWithNoReadingShowsAPlaceholder()
    {
        SystemStatsReading reading = new SystemStatsReading(
            double.NaN, double.NaN, null, -1,
            DiskMonitor.DiskReading.Unavailable,
            BatteryMonitor.BatteryReading.Unavailable);

        StatsItem[] items = SystemStats.BuildItems(reading, ChipConfig());

        Assert.Contains(items, item => item.Text == $"C: {SystemStats.Placeholder}");
        Assert.Contains(items, item => item.Text == $"R: {SystemStats.Placeholder}");
        Assert.Contains(items, item => item.Text == $"G: {SystemStats.Placeholder}");
        Assert.Contains(items, item => item.Text == $"D: {SystemStats.Placeholder}");
    }

    [Fact]
    public void ThePlaceholderIsNeverAConfidentLookingZero()
    {
        SystemStatsReading reading = new SystemStatsReading(
            double.NaN, double.NaN, null, -1,
            DiskMonitor.DiskReading.Unavailable,
            BatteryMonitor.BatteryReading.Unavailable);

        StatsItem[] items = SystemStats.BuildItems(reading, ChipConfig());

        Assert.DoesNotContain(items, item => item.Text.Contains("0%"));
        Assert.DoesNotContain(items, item => item.Text.Contains("100%"));
    }

    [Fact]
    public void ADisabledMetricStillHasNoChip()
    {
        MonitorConfig config = ChipConfig();
        config.ShowGpu = false;
        config.ShowDisk = false;

        SystemStatsReading reading = new SystemStatsReading(
            double.NaN, double.NaN, null, -1,
            DiskMonitor.DiskReading.Unavailable,
            BatteryMonitor.BatteryReading.Unavailable);

        StatsItem[] items = SystemStats.BuildItems(reading, config);

        Assert.DoesNotContain(items, item => item.Text.StartsWith("G:"));
        Assert.DoesNotContain(items, item => item.Text.StartsWith("D:"));
    }

    /// <summary>
    /// The property the freeze fix rests on: the widget's size is the same whether a
    /// metric has a reading or not, so a reading that comes and goes never resizes it.
    /// </summary>
    [Fact]
    public void TheLayoutSizeIsStableWhenAReadingComesAndGoes()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig();
        config.ShowBattery = false; // the battery chip is deliberately hidden on AC

        SystemStatsReading known = KnownReading();
        SystemStatsReading missing = new SystemStatsReading(
            double.NaN, double.NaN, null, -1,
            DiskMonitor.DiskReading.Unavailable,
            BatteryMonitor.BatteryReading.Unavailable);

        WidgetLayout withReading = renderer.ComputeLayout(config, 1f, 48, known);
        WidgetLayout withoutReading = renderer.ComputeLayout(config, 1f, 48, missing);

        Assert.Equal(withReading.Size, withoutReading.Size);
        Assert.Equal(withReading.StatsColumns, withoutReading.StatsColumns);
    }

    /// <summary>
    /// A GPU reading that flickers between present and absent must not change the size
    /// either - the exact case that used to resize the widget every tick.
    /// </summary>
    [Fact]
    public void AFlickeringGpuReadingDoesNotResizeTheWidget()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig();

        SystemStatsReading present = KnownReading();
        SystemStatsReading absent = KnownReading() with { GpuPercent = null };

        WidgetLayout withGpu = renderer.ComputeLayout(config, 1f, 48, present);
        WidgetLayout withoutGpu = renderer.ComputeLayout(config, 1f, 48, absent);

        Assert.Equal(withGpu.Size, withoutGpu.Size);
    }
}
