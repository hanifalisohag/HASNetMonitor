using HASNetMonitor.Core;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The disk chip shows how busy the drives are, not how full they are. The activity comes
/// from the Windows "PhysicalDisk" counters, and the aggregate instance covers every drive
/// at once, so one chip stands for all of them.
/// </summary>
public sealed class DiskActivitySpecTests
{
    private static MonitorConfig ChipConfig() => new MonitorConfig
    {
        ShowCpu = false,
        ShowRam = false,
        ShowGpu = false,
        ShowDisk = true,
        ShowBattery = false,
        ShowText = true,
    }.Normalize();

    private static SystemStatsReading ReadingWith(double activity) => new SystemStatsReading(
        0.0,
        0.0,
        null,
        -1,
        new DiskMonitor.DiskReading("C:\\", 1_000_000_000L, 2_000_000_000L) { ActivityPercent = activity },
        BatteryMonitor.BatteryReading.Unavailable);

    [Fact]
    public void TheDiskChipShowsActivityNotFreeSpace()
    {
        // The drive is half full but idle: the chip must read the activity, not the space.
        SystemStatsReading reading = new SystemStatsReading(
            0.0,
            0.0,
            null,
            -1,
            new DiskMonitor.DiskReading("C:\\", 1_000_000_000L, 2_000_000_000L) { ActivityPercent = 7.0 },
            BatteryMonitor.BatteryReading.Unavailable);

        StatsItem disk = SystemStats.BuildItems(reading, ChipConfig()).Single();

        Assert.Equal("D: 7%", disk.Text);
    }

    [Fact]
    public void TheDiskChipRoundsActivityToTheNearestPercent()
    {
        Assert.Equal("D: 42%", SystemStats.BuildItems(ReadingWith(42.4), ChipConfig()).Single().Text);
        Assert.Equal("D: 43%", SystemStats.BuildItems(ReadingWith(42.5), ChipConfig()).Single().Text);
    }

    [Fact]
    public void ActivityAboveOneHundredIsClamped()
    {
        // % Disk Time is the summed busy time of every disk and can exceed 100 on a machine
        // with several drives; the chip must never show more than 100%.
        StatsItem disk = SystemStats.BuildItems(ReadingWith(180.0), ChipConfig()).Single();

        Assert.Equal("D: 100%", disk.Text);
        Assert.Equal(1.0, disk.Ratio);
    }

    [Fact]
    public void ANegativeActivityIsTreatedAsUnknown()
    {
        // A negative value means the counter did not resolve, so the chip shows the
        // placeholder rather than a misleading 0%.
        StatsItem disk = SystemStats.BuildItems(ReadingWith(-5.0), ChipConfig()).Single();

        Assert.Equal("D: --", disk.Text);
    }

    [Fact]
    public void AnUnknownActivityShowsAPlaceholderRatherThanZero()
    {
        // No counters resolved: the chip keeps its slot but shows a placeholder, never a
        // confident-looking 0%.
        SystemStatsReading reading = new SystemStatsReading(
            0.0,
            0.0,
            null,
            -1,
            new DiskMonitor.DiskReading("C:\\", 1_000_000_000L, 2_000_000_000L),
            BatteryMonitor.BatteryReading.Unavailable);

        StatsItem disk = SystemStats.BuildItems(reading, ChipConfig()).Single();

        Assert.Equal("D: --", disk.Text);
        Assert.DoesNotContain("0%", disk.Text);
    }

    [Fact]
    public void TheReadingCarriesTheSystemDriveSpaceAlongsideActivity()
    {
        // The chip shows activity, but the reading still describes the drive's space so the
        // record stays a complete picture and existing callers keep working.
        DiskMonitor.DiskReading reading = new DiskMonitor().Read();

        if (reading.IsKnown)
        {
            Assert.Matches("^[A-Za-z]:", reading.Drive);
            Assert.True(reading.FreeBytes >= 0);
            Assert.True(reading.TotalBytes >= reading.FreeBytes);
        }
    }

    [Fact]
    public void TheLiveMonitorReportsActivityInRangeOrNothing()
    {
        DiskMonitor.DiskReading reading = new DiskMonitor().Read();

        if (reading.HasActivity)
        {
            Assert.InRange(reading.ActivityPercent, 0.0, 100.0);
        }
        else
        {
            Assert.Equal(-1.0, reading.ActivityPercent);
        }
    }

    [Fact]
    public void TheAggregateInstanceCoversEveryDrive()
    {
        // The counter path the monitor uses is the _Total instance, which sums every
        // physical disk into one number - the "all drives with a single marker" the chip
        // is meant to show.
        Assert.Contains("_Total", DiskMonitor.AggregateCounterPath);
        Assert.Contains("% Disk Time", DiskMonitor.AggregateCounterPath);
    }

    [Fact]
    public void TheMonitorIsSafeToDisposeMoreThanOnce()
    {
        var monitor = new DiskMonitor();
        monitor.Read();
        monitor.Dispose();
        monitor.Dispose();
    }
}
