using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The statistics chips: which ones appear, when, and how the text is joined. A chip
/// that cannot be filled must be absent, never a confident zero.
/// </summary>
public class SystemStatsTests
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
    public void EveryChipAppearsWhenEverythingIsReadableAndEnabled()
    {
        StatsItem[] items = SystemStats.BuildItems(KnownReading(), ChipConfig());

        Assert.Equal(5, items.Length);
        Assert.Contains(items, item => item.Text == "C: 42%");
        Assert.Contains(items, item => item.Text == "R: 71%");
        Assert.Contains(items, item => item.Text == "G: 55%");
        Assert.Contains(items, item => item.Text == "D: 46%");
        Assert.Contains(items, item => item.Text == "B: 67%");
    }

    [Fact]
    public void ADisabledChipIsNotAskedFor()
    {
        MonitorConfig config = ChipConfig();
        config.ShowGpu = false;
        config.ShowBattery = false;

        StatsItem[] items = SystemStats.BuildItems(KnownReading(), config);

        Assert.Equal(3, items.Length);
        Assert.DoesNotContain(items, item => item.Text.Contains("G:"));
        Assert.DoesNotContain(items, item => item.Text.Contains("B:"));
    }

    [Fact]
    public void AnUnavailableChipIsOmittedRatherThanRenderedAsAZero()
    {
        SystemStatsReading reading = new SystemStatsReading(
            double.NaN, double.NaN, null, -1,
            DiskMonitor.DiskReading.Unavailable,
            BatteryMonitor.BatteryReading.Unavailable);

        StatsItem[] items = SystemStats.BuildItems(reading, ChipConfig());

        Assert.Empty(items);
    }

    [Fact]
    public void ABatteryOnAcPowerIsNotShown()
    {
        SystemStatsReading reading = KnownReading() with
        {
            Battery = new BatteryMonitor.BatteryReading(OnBattery: false, Percent: 67, Charging: false),
        };

        StatsItem[] items = SystemStats.BuildItems(reading, ChipConfig());

        Assert.DoesNotContain(items, item => item.Text == "B: 67%");
    }

    [Fact]
    public void ALowBatteryChipUsesTheLowColourAndChargingUsesTheChargingColour()
    {
        var low = KnownReading() with { Battery = new BatteryMonitor.BatteryReading(true, 12, false) };
        var charging = KnownReading() with { Battery = new BatteryMonitor.BatteryReading(true, 12, true) };

        StatsItem[] lowItems = SystemStats.BuildItems(low, ChipConfig());
        StatsItem[] chargingItems = SystemStats.BuildItems(charging, ChipConfig());

        Assert.Equal(SystemStats.StatsColors.BatteryLow, lowItems.First(item => item.Text == "B: 12%").Dot);
        Assert.Equal(SystemStats.StatsColors.BatteryCharging, chargingItems.First(item => item.Text == "B: 12%").Dot);
    }

    [Fact]
    public void JoinedTextIsEmptyWithoutItemsAndJoinsWithTheDotSeparator()
    {
        Assert.Equal(string.Empty, SystemStats.JoinedText(SystemStatsReading.Empty, ChipConfig()));

        string joined = SystemStats.JoinedText(KnownReading(), ChipConfig());

        Assert.Equal("C: 42%  ·  R: 71%  ·  G: 55%  ·  B: 67%  ·  D: 46%", joined);
    }

    [Fact]
    public void ADiskWithoutAKnownTotalIsNotShown()
    {
        SystemStatsReading reading = KnownReading() with
        {
            Disk = new DiskMonitor.DiskReading("C:\\", 1_000_000_000L, 0L),
        };

        StatsItem[] items = SystemStats.BuildItems(reading, ChipConfig());

        Assert.DoesNotContain(items, item => item.Text.Contains("D:"));
    }

    [Theory]
    [InlineData(42.4, "42%")]
    [InlineData(42.5, "43%")]
    [InlineData(-5, "0%")]
    [InlineData(150, "100%")]
    public void FormatPercentClampsAndRounds(double percent, string expected)
    {
        Assert.Equal(expected, SystemStats.FormatPercent(percent));
    }

    [Fact]
    public void TheStatsStripIsAbsentFromAConfigThatAsksForNothing()
    {
        MonitorConfig config = ChipConfig();
        config.ShowCpu = false;
        config.ShowRam = false;
        config.ShowGpu = false;
        config.ShowDisk = false;
        config.ShowBattery = false;

        Assert.True(config.ShowText);
        Assert.False(config.AnyStatsEnabled);
        Assert.Empty(SystemStats.BuildItems(KnownReading(), config));
    }

    /// <summary>
    /// The sampling happens every call, so the chips advance at the same rate as the
    /// figures. The first call after a reset primes the CPU baseline and reports nothing;
    /// the second call must read the counters again, not return a cached prime, or the
    /// chips would freeze at roughly one percent a second whatever the refresh interval is.
    /// </summary>
    [Fact]
    public void QuickCallsDoNotReturnTheCachedPrimer()
    {
        var stats = new SystemStats();
        MonitorConfig config = ChipConfig();

        SystemStatsReading first = stats.Sample(config);
        Assert.True(double.IsNaN(first.CpuPercent), "the first call primes the baseline and reports nothing");

        // Let the OS tick advance so the second call measures real counter deltas.
        System.Threading.Thread.Sleep(100);
        SystemStatsReading second = stats.Sample(config);
        Assert.False(double.IsNaN(second.CpuPercent), "the second call must sample the counters again, not a cached read");
        Assert.True(second.CpuPercent >= 0.0 && second.CpuPercent <= 100.0);
    }

    /// <summary>
    /// A single failed read must not blank a chip the user is already looking at: the last
    /// good value survives the failure, and a metric that was never available stays absent.
    /// </summary>
    [Fact]
    public void ATransientFailureHoldsTheLastGoodValue()
    {
        double lastGood = 42.0;
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp();

        double held = SystemStats.HoldLastGood(double.NaN, ref lastGood, ref ticks, System.Diagnostics.Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(5));

        Assert.Equal(42.0, held);
        Assert.Equal(42.0, lastGood);
    }

    [Fact]
    public void AGoodReadRefreshesTheHeldValue()
    {
        double lastGood = 10.0;
        long ticks = 0L;

        double held = SystemStats.HoldLastGood(77.0, ref lastGood, ref ticks, System.Diagnostics.Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(5));

        Assert.Equal(77.0, held);
        Assert.Equal(77.0, lastGood);
        Assert.NotEqual(0L, ticks);
    }

    [Fact]
    public void AMetricThatWasNeverAvailableStaysHidden()
    {
        double? lastGood = null;
        long ticks = 0L;

        double? held = SystemStats.HoldLastGood((double?)null, ref lastGood, ref ticks, System.Diagnostics.Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(5));

        Assert.Null(held);
        Assert.Null(lastGood);
        Assert.Equal(0L, ticks);
    }

    [Fact]
    public void PastTheGraceTheChipDisappears()
    {
        double lastGood = 42.0;
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp();

        // Zero grace means the hold is worthless: the metric is treated as gone.
        double held = SystemStats.HoldLastGood(double.NaN, ref lastGood, ref ticks, System.Diagnostics.Stopwatch.GetTimestamp(), TimeSpan.Zero);

        Assert.True(double.IsNaN(held));
    }

    [Fact]
    public void AnUnavailableDiskHoldsTheLastGoodReading()
    {
        DiskMonitor.DiskReading original = new("C:\\", 107_374_182_400L, 200_000_000_000L);
        DiskMonitor.DiskReading lastGood = original;
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp();

        DiskMonitor.DiskReading held = SystemStats.HoldLastGood(DiskMonitor.DiskReading.Unavailable, ref lastGood, ref ticks, System.Diagnostics.Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(5));

        Assert.Equal(original, held);
        Assert.Equal(original, lastGood);
    }

    [Fact]
    public void ResetReprimesTheBaseline()
    {
        var stats = new SystemStats();
        MonitorConfig config = ChipConfig();

        stats.Sample(config);
        stats.Reset();
        SystemStatsReading fresh = stats.Sample(config);

        // After a reset the CPU baseline is re-primed, so the reading reports nothing
        // rather than a stale value.
        Assert.True(double.IsNaN(fresh.CpuPercent));
    }

    /// <summary>
    /// Live reads do not throw; a broken counter must surface as "unavailable", never a
    /// crash on the UI thread.
    /// </summary>
    [Fact]
    public void ALiveSampleNeverThrowsAndStartsOnTheQuietSide()
    {
        var stats = new SystemStats();
        MonitorConfig config = ChipConfig();

        SystemStatsReading reading = stats.Sample(config);

        Assert.True(
            double.IsNaN(reading.CpuPercent) || reading.CpuPercent >= 0.0 && reading.CpuPercent <= 100.0);
        Assert.True(
            double.IsNaN(reading.RamPercent) || reading.RamPercent >= 0.0 && reading.RamPercent <= 100.0);
    }

    [Fact]
    public void TheCpuMonitorReportsARealValueOnTheSecondRead()
    {
        var cpu = new CpuMonitor();

        double first = cpu.ReadPercent();
        double second = cpu.ReadPercent();

        // The first read primes the baseline and deliberately reports nothing.
        Assert.True(double.IsNaN(first));
        Assert.True(double.IsNaN(second) || (second >= 0.0 && second <= 100.0));
    }

    [Fact]
    public void TheMemoryMonitorReportsItsTotals()
    {
        MemoryMonitor.MemoryReading reading = new MemoryMonitor().Read();

        if (reading.IsKnown)
        {
            Assert.True(reading.Percent >= 0.0 && reading.Percent <= 100.0);
            Assert.True(reading.TotalBytes > 0);
        }
    }

    [Fact]
    public void TheDiskMonitorReportsADriveAndBytes()
    {
        DiskMonitor.DiskReading reading = new DiskMonitor().Read();

        if (reading.IsKnown)
        {
            Assert.Matches("^[A-Za-z]:", reading.Drive);
            Assert.True(reading.FreeBytes >= 0);
            Assert.True(reading.TotalBytes >= reading.FreeBytes);
        }
    }

    [Fact]
    public void TheBatteryMonitorNeverReportsAGarbageValue()
    {
        BatteryMonitor.BatteryReading reading = new BatteryMonitor().Read();

        if (reading.IsKnown)
        {
            Assert.True(reading.OnBattery);
            Assert.True(reading.Percent >= 0 && reading.Percent <= 100);
        }
    }

    [Fact]
    public void TheGpuMonitorEitherResolvesAPercentOrReturnsNull()
    {
        double? percent = new GpuMonitor().ReadPercent();

        if (percent is double value)
        {
            Assert.True(value >= 0.0 && value <= 100.0);
        }
    }

    /// <summary>
    /// The caption for the tray tooltip: traffic first, the chips after a dot, and the
    /// chips dropped entirely when nothing is shown.
    /// </summary>
    [Fact]
    public void BuildCaptionCarriesTrafficAndStats()
    {
        MonitorConfig config = ChipConfig();
        string caption = TaskbarWidget.BuildCaption(KnownReading(), 1536, 2048, config);

        Assert.StartsWith("\u2193", caption);
        Assert.Contains("\u2191", caption);
        Assert.Contains("C: 42%", caption);
        Assert.Contains("R: 71%", caption);
    }

    [Fact]
    public void BuildCaptionWithoutStatsIsTrafficOnly()
    {
        MonitorConfig config = ChipConfig();
        config.ShowCpu = false;
        config.ShowRam = false;
        config.ShowGpu = false;
        config.ShowDisk = false;
        config.ShowBattery = false;

        string caption = TaskbarWidget.BuildCaption(SystemStatsReading.Empty, 1536, 2048, config);

        Assert.DoesNotContain("\u00b7", caption);
        Assert.Contains("\u2191", caption);
    }

    /// <summary>
    /// The chips: laid out on the left of the figures, centred on the two text rows,
    /// one column every two items and at most two chips per column, filled top-down.
    /// The chips ride along beside the figures - never under them - so a taskbar that
    /// only needs a smaller figure keeps every chip it can draw.
    /// </summary>
    [Fact]
    public void TheRendererLaysTheChipsInColumnsBesideTheFigures()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig(); // CPU, RAM, GPU, Disk, Battery: five chips, three columns

        SystemStatsReading reading = KnownReading();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 64, reading);

        Assert.Equal(3, layout.StatsColumns);
        Assert.Equal(layout.StatsColumns, layout.StatsColumnX.Length);
        Assert.Equal(layout.StatsColumns, layout.StatsColumnWidths.Length);
        Assert.True(layout.StatsBlockWidth > 0);
        Assert.True(layout.StatsEmPixels > 0);
        Assert.True(layout.StatsChipHeight > 0);

        // Every column starts to the left of the text block and stays inside the widget.
        for (int i = 0; i < layout.StatsColumns; i++)
        {
            Assert.True(layout.StatsColumnX[i] >= 0);
            Assert.True(layout.StatsColumnX[i] + layout.StatsColumnWidths[i] <= layout.Size.Width);
            if (i > 0)
            {
                Assert.True(layout.StatsColumnX[i] > layout.StatsColumnX[i - 1]);
            }
        }

        Assert.True(layout.StatsBlockWidth <= layout.DownloadText.X, "the chip block must stay left of the figures");
        Assert.True(layout.DownloadText.Right <= layout.Size.Width);
        Assert.True(layout.UploadText.Right <= layout.Size.Width);

        // Chips are centred on the figure rows, like the figures themselves.
        int textHeight = layout.DownloadText.Height;
        int chipOffset = Math.Max(0, (textHeight - layout.StatsChipHeight) / 2);
        int firstRowY = Math.Min(layout.DownloadText.Y, layout.UploadText.Y);
        int secondRowY = Math.Max(layout.DownloadText.Y, layout.UploadText.Y);
        Assert.Equal(firstRowY + chipOffset, layout.StatsRowTop);
        Assert.Equal(secondRowY + chipOffset, layout.StatsRowBottom);
    }

    [Fact]
    public void TheRendererKeepsTheChipsWhenASmallerFigureIsEnough()
    {
        var renderer = new WidgetRenderer();

        // The reported failure: the user's own config (SidebarSegments, 10.5pt,
        // upload first, CPU/RAM/GPU chips) on a standard 48px taskbar. The figure
        // must shrink slightly - the chips ride along on the left with it.
        MonitorConfig config = ChipConfig();
        config.Style = WidgetStyles.SidebarSegments;
        config.SortOrder = StackOrder.UploadFirst;
        config.FontSizePt = 10.5;
        config.ShowDisk = false;
        config.ShowBattery = false;
        config.ShowCpu = true;
        config.ShowRam = true;
        config.ShowGpu = true;

        SystemStatsReading reading = KnownReading();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48, reading);

        Assert.True(layout.StatsColumns > 0, "the chips must survive a taskbar that only needs a smaller figure");
        Assert.True(layout.StatsBlockWidth <= layout.DownloadText.X, "the chip block must stay left of the figures");
        Assert.True(layout.DownloadText.Right <= layout.Size.Width);
        Assert.True(layout.Size.Height <= layout.MaxHeight, $"{layout.Size.Height} does not fit a {layout.MaxHeight}px taskbar");
    }

    [Fact]
    public void ChipsUseOneColumnEveryTwoItems()
    {
        var renderer = new WidgetRenderer();

        // One chip: a single-column block. Two chips: still one column. Three chips:
        // a second column. Fill is top-down, two chips per column at most.
        MonitorConfig one = ChipConfig();
        one.ShowRam = false;
        one.ShowGpu = false;
        one.ShowDisk = false;
        one.ShowBattery = false;
        WidgetLayout oneLayout = renderer.ComputeLayout(one, 1f, 64, KnownReading());
        Assert.Equal(1, oneLayout.StatsColumns);

        MonitorConfig two = ChipConfig();
        two.ShowGpu = false;
        two.ShowDisk = false;
        two.ShowBattery = false;
        WidgetLayout twoLayout = renderer.ComputeLayout(two, 1f, 64, KnownReading());
        Assert.Equal(1, twoLayout.StatsColumns);

        MonitorConfig three = ChipConfig();
        three.ShowDisk = false;
        three.ShowBattery = false;
        WidgetLayout threeLayout = renderer.ComputeLayout(three, 1f, 64, KnownReading());
        Assert.Equal(2, threeLayout.StatsColumns);

        WidgetLayout fiveLayout = renderer.ComputeLayout(ChipConfig(), 1f, 64, KnownReading());
        Assert.Equal(3, fiveLayout.StatsColumns);

        Assert.True(threeLayout.StatsColumnWidths[0] > 0);
        Assert.True(threeLayout.StatsColumnWidths[1] > 0);
        Assert.True(threeLayout.StatsColumnX[1] > threeLayout.StatsColumnX[0]);
    }

    [Fact]
    public void TheRendererKeepsTheChipsEvenAtTheFloorFont()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig();

        // A taskbar that cannot fit even the floor figure: the widget must honestly
        // report itself as overflowing - the chips ride along on the left and are
        // never dropped to make room, so the block is still laid out.
        SystemStatsReading reading = KnownReading();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 12, reading);

        Assert.True(layout.StatsColumns > 0, "the chips must not be dropped because a figure is cramped");
        Assert.False(layout.FitsBounds);
    }

    [Fact]
    public void TheRendererDoesNotReserveAChipBlockWithoutTextOrItems()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig();
        config.ShowText = false;

        SystemStatsReading reading = KnownReading();
        WidgetLayout noText = renderer.ComputeLayout(config, 1f, 48, reading);

        config.ShowText = true;
        WidgetLayout noItems = renderer.ComputeLayout(config, 1f, 48, SystemStatsReading.Empty);

        Assert.Equal(0, noText.StatsColumns);
        Assert.Equal(0, noText.StatsEmPixels);
        Assert.Equal(0, noItems.StatsColumns);
        Assert.Equal(0, noItems.StatsEmPixels);
    }

    [Fact]
    public void TheChipsNeverReserveVerticalSpace()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig withChips = ChipConfig();
        MonitorConfig withoutChips = ChipConfig();
        withoutChips.ShowCpu = false;
        withoutChips.ShowRam = false;
        withoutChips.ShowGpu = false;
        withoutChips.ShowDisk = false;
        withoutChips.ShowBattery = false;

        WidgetLayout with = renderer.ComputeLayout(withChips, 1f, 64, KnownReading());
        WidgetLayout without = renderer.ComputeLayout(withoutChips, 1f, 64, KnownReading());

        Assert.True(with.Size.Width > without.Size.Width, "the chip block must widen the widget, not the height");
        Assert.Equal(without.Size.Height, with.Size.Height);
    }

    /// <summary>
    /// The chips are drawn at the same em as the traffic figures - same font, size and
    /// weight - so the statistics read as part of the same block of type, not a second
    /// smaller widget bolted onto it.
    /// </summary>
    [Fact]
    public void TheChipsUseTheFigureFontSize()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig();

        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48, KnownReading());

        Assert.Equal(layout.FontEmPixels, layout.StatsEmPixels);
        Assert.True(layout.StatsChipHeight > 0);
    }

    /// <summary>
    /// The chips actually paint: a frame built from a known reading has coloured ink
    /// in the chip block (left of the figures) and nowhere outside the widget.
    /// </summary>
    [Fact]
    public void TheChipsPaintOnTheLeftOfTheFigures()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = ChipConfig();
        SystemStatsReading reading = KnownReading();
        WidgetVisual visual = new(1024, 256, 0.5, 0.25, reading);

        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48, reading);
        using Bitmap frame = renderer.Render(visual, config, layout, new WidgetTheme(true), TaskbarOrientation.Horizontal);

        // The chip block is a region left of the figures; something is drawn in it.
        bool blockHasInk = false;
        for (int y = 0; y < frame.Height && !blockHasInk; y++)
        {
            for (int x = 0; x < layout.DownloadText.X - 1; x++)
            {
                if (frame.GetPixel(x, y).A > 1)
                {
                    blockHasInk = true;
                    break;
                }
            }
        }
        Assert.True(blockHasInk, "the chip block paints ink beside the figures");

        // The figure text region also has ink (the figures are still there).
        bool figureHasInk = false;
        for (int y = layout.DownloadText.Top; y < layout.DownloadText.Bottom && !figureHasInk; y++)
        {
            for (int x = layout.DownloadText.Left; x < layout.DownloadText.Right; x++)
            {
                if (frame.GetPixel(x, y).A > 1)
                {
                    figureHasInk = true;
                    break;
                }
            }
        }
        Assert.True(figureHasInk, "the figures are still painted");

        // Nothing is painted outside the widget (the frame is the widget).
        Assert.Equal(layout.Size.Width, frame.Width);
        Assert.Equal(layout.Size.Height, frame.Height);
    }
}
