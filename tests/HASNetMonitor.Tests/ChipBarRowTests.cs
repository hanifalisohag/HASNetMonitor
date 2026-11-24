using System;
using System.Drawing;
using System.Linq;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The chip level bar hangs below its chip text, so the chip block must reserve room for
/// the bar under the last row. It did not: the block height counted only the two text rows,
/// so the second row's bar was pushed past the widget's bottom edge and clipped to a
/// thinner line than the first row's - the bug the user saw. These tests pin the bar's
/// height to the same value in both rows, at every DPI scale and style.
/// </summary>
public class ChipBarRowTests
{
    private static MonitorConfig ChipConfig() => new MonitorConfig
    {
        ShowCpu = true,
        ShowRam = true,
        ShowGpu = true,
        ShowDisk = true,
        ShowBattery = false,
        ShowText = true,
        ShowBackdrop = false,
        Style = WidgetStyles.SidebarSegments,
        SortOrder = StackOrder.UploadFirst,
        ChipIndicator = ChipIndicators.Bar,
        ChipIcon = ChipIcons.Letter,
    }.Normalize();

    private static SystemStatsReading Reading() => new SystemStatsReading(
        42.0,
        71.0,
        55.0,
        107_374_182_400L,
        new DiskMonitor.DiskReading("C:\\", 107_374_182_400L, 200_000_000_000L) { ActivityPercent = 58.0 },
        new BatteryMonitor.BatteryReading(OnBattery: true, Percent: 67, Charging: false));

    private static WidgetVisual Visual() => new(1024, 1024, 0.5, 0.5, Reading());

    /// <summary>
    /// The height in pixels of the chip's level bar, found by scanning the columns under the
    /// chip for the chip's own colour. Returns 0 when no fill ink is present.
    /// </summary>
    private static int BarHeight(Frame frame, int chipX, int chipWidth, int barY, Color dot)
    {
        int first = -1;
        int last = -1;
        for (int y = barY - 2; y < barY + 8 && y < frame.Bitmap.Height; y++)
        {
            bool hit = false;
            for (int x = chipX; x < chipX + chipWidth && x < frame.Bitmap.Width; x++)
            {
                Color p = frame.Bitmap.GetPixel(x, y);
                if (p.A == 255
                    && Math.Abs(p.R - dot.R) <= 6
                    && Math.Abs(p.G - dot.G) <= 6
                    && Math.Abs(p.B - dot.B) <= 6)
                {
                    hit = true;
                    break;
                }
            }

            if (hit)
            {
                if (first < 0)
                {
                    first = y;
                }

                last = y;
            }
        }

        return last < 0 ? 0 : last - first + 1;
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2.0f)]
    public void BothChipRowsDrawTheSameBarHeight(float scale)
    {
        MonitorConfig config = ChipConfig();
        StatsItem[] items = SystemStats.BuildItems(Reading(), config);
        Assert.Equal(4, items.Length);

        using Frame frame = Frame.Render(config, Visual(), scale: scale);
        WidgetLayout layout = frame.Layout;

        // Two chips per column, filled top-down: items 0/1 are column 0's two rows.
        int chipX = layout.StatsColumnX[0];
        int chipWidth = layout.StatsColumnWidths[0];
        int row0BarY = layout.StatsRowTop + layout.StatsChipHeight;
        int row1BarY = layout.StatsRowBottom + layout.StatsChipHeight;

        int row0 = BarHeight(frame, chipX, chipWidth, row0BarY, items[0].Dot);
        int row1 = BarHeight(frame, chipX, chipWidth, row1BarY, items[1].Dot);

        Assert.True(row0 > 0, $"scale {scale}: the first row's bar drew nothing");
        Assert.True(row1 > 0, $"scale {scale}: the second row's bar drew nothing");
        Assert.Equal(row0, row1);
    }

    /// <summary>
    /// The last row's bar must sit wholly inside the widget. Before the fix its bottom edge
    /// fell on or past the widget's last pixel row, which is what clipped it.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2.0f)]
    public void TheLastChipRowsBarStaysInsideTheWidget(float scale)
    {
        MonitorConfig config = ChipConfig();

        using Frame frame = Frame.Render(config, Visual(), scale: scale);
        WidgetLayout layout = frame.Layout;

        int barHeight = Math.Max(1, (int)Math.Round(1.5 * scale));
        int lastBarBottom = layout.StatsRowBottom + layout.StatsChipHeight + barHeight;

        Assert.True(
            lastBarBottom <= layout.Size.Height,
            $"scale {scale}: the last row's bar ends at {lastBarBottom}, past the widget's {layout.Size.Height}px");
    }

    /// <summary>
    /// The bar indicator must not change the widget's size: the block is centred with room
    /// for the bar, not grown to fit it.
    /// </summary>
    [Fact]
    public void TheBarIndicatorKeepsTheWidgetSize()
    {
        MonitorConfig plain = ChipConfig();
        plain.ChipIndicator = ChipIndicators.None;
        MonitorConfig bar = ChipConfig();

        using Frame plainFrame = Frame.Render(plain, Visual());
        using Frame barFrame = Frame.Render(bar, Visual());

        Assert.Equal(plainFrame.Layout.Size, barFrame.Layout.Size);
    }
}
