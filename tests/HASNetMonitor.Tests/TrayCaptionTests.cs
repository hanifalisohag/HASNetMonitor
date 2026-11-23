using HASNetMonitor.Core;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The tray tooltip is capped at 63 characters on .NET Framework and throws
/// ArgumentException above it. The caption grows with every chip, so a four-chip line
/// overflows the cap - and because the caption is built from the sample path, the throw
/// skipped the render and froze the whole widget. These tests pin the cap and the trim.
/// </summary>
public sealed class TrayCaptionTests
{
    private static MonitorConfig AllChips()
    {
        var config = new MonitorConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowGpu = true,
            ShowDisk = true,
            ShowBattery = false,
        };
        config.Normalize();
        return config;
    }

    private static SystemStatsReading Reading() => new(
        CpuPercent: 8,
        RamPercent: 85,
        GpuPercent: 7,
        DiskFreeBytes: 123456789,
        Disk: new DiskMonitor.DiskReading("C:", 123456789, 500000000),
        Battery: BatteryMonitor.BatteryReading.Unavailable);

    [Fact]
    public void AFourChipCaptionOverflowsTheTrayLimit()
    {
        string caption = TaskbarWidget.BuildCaption(Reading(), 11_400_000, 152_000, AllChips());

        // The bug only bites when the caption is longer than the tooltip cap; if this
        // ever stops being true the freeze test below would pass for the wrong reason.
        Assert.True(
            caption.Length > TaskbarWidget.MaxTrayTextLength,
            $"expected the four-chip caption to exceed {TaskbarWidget.MaxTrayTextLength} chars, got {caption.Length}: '{caption}'");
    }

    [Fact]
    public void FitTrayTextTrimsAnOverLongCaptionToTheLimit()
    {
        string caption = TaskbarWidget.BuildCaption(Reading(), 11_400_000, 152_000, AllChips());

        string fitted = TaskbarWidget.FitTrayText(caption);

        Assert.True(fitted.Length <= TaskbarWidget.MaxTrayTextLength);
        Assert.EndsWith("...", fitted);
    }

    [Fact]
    public void FitTrayTextLeavesAShortCaptionAlone()
    {
        const string shortCaption = "\u2193 1.5 KB/s  \u2191 2.0 KB/s";

        Assert.Equal(shortCaption, TaskbarWidget.FitTrayText(shortCaption));
    }

    [Fact]
    public void FitTrayTextKeepsACaptionExactlyAtTheLimit()
    {
        string exact = new string('x', TaskbarWidget.MaxTrayTextLength);

        Assert.Equal(exact, TaskbarWidget.FitTrayText(exact));
    }
}
