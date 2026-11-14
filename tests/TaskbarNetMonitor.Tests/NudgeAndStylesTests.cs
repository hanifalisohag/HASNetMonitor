using System.Drawing;
using System.Reflection;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.Rendering;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// Nudge positioning, the extra styles and the segment gap. Placement is pure maths,
/// so the same code that moves the real widget is checked here.
/// </summary>
public class NudgeAndStylesTests
{
    private static readonly PixelRect BottomTaskbar = new(0, 1032, 1920, 48);
    private static readonly PixelRect BottomTray = new(1700, 1032, 220, 48);
    private static readonly Size Layout = new(72, 44);

    private static PixelRect Place(int nudgeX, int nudgeY)
        => TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, 6, nudgeX: nudgeX, nudgeY: nudgeY)!.Value;

    // A 44px widget exactly fills a 48px taskbar, so vertical nudges need a taller bar.
    private static readonly PixelRect TallTaskbar = new(0, 1010, 1920, 70);
    private static readonly PixelRect TallTray = new(1700, 1010, 220, 70);

    private static PixelRect PlaceTall(int nudgeX, int nudgeY)
        => TaskbarGeometry.ComputePlacement(TallTaskbar, TallTray, Layout, 6, nudgeX: nudgeX, nudgeY: nudgeY)!.Value;

    // ---------- position ----------

    [Fact]
    public void NoNudgeKeepsTheOriginalPlacement()
    {
        PixelRect baseline = Place(0, 0);
        Assert.Equal(1700 - 6 - 72, baseline.Left);
        Assert.Equal(1034, baseline.Top);
    }

    [Fact]
    public void NudgeMovesTheWidgetByExactlyTheGivenPixels()
    {
        PixelRect baseline = Place(0, 0);

        Assert.Equal(baseline.Left - 10, Place(-10, 0).Left);
        Assert.Equal(baseline.Left + 10, Place(10, 0).Left);

        PixelRect tall = PlaceTall(0, 0);
        Assert.Equal(tall.Top - 2, PlaceTall(0, -2).Top);
        Assert.Equal(tall.Top + 2, PlaceTall(0, 2).Top);

        // On the real 48px bar the widget has only 4px of room, but it must still move:
        // the clamp stops at the taskbar edge, not at the padding.
        PixelRect tight = Place(0, 0);
        Assert.Equal(tight.Top - 2, Place(0, -2).Top);
        Assert.Equal(tight.Top + 2, Place(0, 2).Top);

        // Diagonal: both axes at once, and the size never changes.
        PixelRect diagonal = PlaceTall(7, 3);
        Assert.Equal(tall.Left + 7, diagonal.Left);
        Assert.Equal(tall.Top + 3, diagonal.Top);
    }

    [Fact]
    public void ANudgeCannotPushTheWidgetOffItsTaskbar()
    {
        foreach (int nudge in new[] { -5000, -400, 400, 5000 })
        {
            PixelRect rect = Place(nudge, nudge);

            // A huge nudge is clamped to the bar's own edge: still on the taskbar, and
            // never drawn outside it.
            Assert.True(rect.Left >= BottomTaskbar.Left, $"x nudge {nudge} left the taskbar");
            Assert.True(rect.Right <= BottomTaskbar.Right, $"x nudge {nudge} left the taskbar");
            Assert.True(rect.Top >= BottomTaskbar.Top, $"y nudge {nudge} left the taskbar");
            Assert.True(rect.Bottom <= BottomTaskbar.Bottom, $"y nudge {nudge} left the taskbar");
        }
    }

    [Fact]
    public void AVerticalTaskbarIsNudgedAlongItsOwnAxes()
    {
        var side = new PixelRect(0, 0, 48, 1080);
        var sideTray = new PixelRect(0, 700, 48, 220);
        var layout = new Size(72, 40);

        PixelRect baseline = TaskbarGeometry.ComputePlacement(side, sideTray, layout, 6)!.Value;
        PixelRect nudged = TaskbarGeometry.ComputePlacement(side, sideTray, layout, 6, nudgeX: 3, nudgeY: -12)!.Value;

        // The long axis has room, so the vertical nudge moves the widget up.
        Assert.Equal(baseline.Top - 12, nudged.Top);

        // The short axis is 48px for a 44px widget: it may reach either edge but is
        // never pushed out of the bar.
        Assert.InRange(nudged.Left, side.Left, side.Right - nudged.Width);
        Assert.True(nudged.Left > baseline.Left, "a nudge must move the widget while there is room");
        Assert.True(side.Right - nudged.Width - nudged.Left < 50, "the widget must stay inside the bar");
    }

    [Fact]
    public void NudgeValuesAreClampedAndSurviveARoundTrip()
    {
        var config = new MonitorConfig { NudgeXPx = 5000, NudgeYPx = -5000 };
        config.Normalize();
        Assert.Equal(MonitorConfig.MaxNudgePx, config.NudgeXPx);
        Assert.Equal(-MonitorConfig.MaxNudgePx, config.NudgeYPx);

        MonitorConfig clone = config.Clone();
        Assert.Equal(config.NudgeXPx, clone.NudgeXPx);
        Assert.Equal(config.NudgeYPx, clone.NudgeYPx);
    }

    [Fact]
    public void SegmentGapIsClampedAndRoundTrips()
    {
        var config = new MonitorConfig { SegmentGapPx = 999 };
        config.Normalize();
        Assert.Equal(MonitorConfig.MaxSegmentGapPx, config.SegmentGapPx);

        var negative = new MonitorConfig { SegmentGapPx = -4 };
        negative.Normalize();
        Assert.Equal(0, negative.SegmentGapPx);

        MonitorConfig clone = new MonitorConfig { SegmentGapPx = 5 }.Clone();
        Assert.Equal(5, clone.SegmentGapPx);
    }

    [Fact]
    public void BarThicknessAllowsAFatterBarThanBefore()
    {
        Assert.Equal(24, MonitorConfig.MaxBarThicknessPx);

        var config = new MonitorConfig { BarThicknessPx = 30 };
        config.Normalize();
        Assert.Equal(MonitorConfig.MaxBarThicknessPx, config.BarThicknessPx);
    }

    // ---------- styles ----------

    [Fact]
    public void EveryAdvertisedStyleHasADescriptionAndRenders()
    {
        Assert.Contains(WidgetStyles.SidebarSegments, WidgetStyles.All);
        Assert.Contains(WidgetStyles.Segments, WidgetStyles.All);
        Assert.Contains(WidgetStyles.Dots, WidgetStyles.All);
        Assert.Contains(WidgetStyles.Minimal, WidgetStyles.All);

        foreach (string style in WidgetStyles.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(WidgetStyles.Describe(style)));

            var config = new MonitorConfig { Style = style, ShowText = true };
            config.Normalize();
            using Frame frame = Frame.Render(config, new WidgetVisual(1024, 512, 0.5, 0.25));
            Assert.True(frame.Layout.Size.Width > 0 && frame.Layout.Size.Height > 0, style);
        }
    }

    [Fact]
    public void EveryStyleKeepsTheFrameInvariants()
    {
        AssertFrameHoldsForEveryStyle();
    }

    [Fact]
    public void UnknownStyleFallsBackToBars()
    {
        Assert.Equal(WidgetStyles.Bars, WidgetStyles.Normalize("not-a-style"));
        Assert.Equal(WidgetStyles.Bars, WidgetStyles.Normalize(null));
    }

    [Theory]
    [InlineData(WidgetStyles.Sidebar, true)]
    [InlineData(WidgetStyles.SidebarSegments, true)]
    [InlineData(WidgetStyles.Bars, false)]
    [InlineData(WidgetStyles.Segments, false)]
    [InlineData(WidgetStyles.Dots, false)]
    public void OnlySidebarStylesDrawUprightTracks(string style, bool vertical)
    {
        Assert.Equal(vertical, WidgetStyles.IsVertical(style));

        var config = new MonitorConfig { Style = style };
        config.Normalize();
        using Frame frame = Frame.Render(config, new WidgetVisual(1024, 512, 0.5, 0.25));

        // Middle height of the track must fall inside it: grow from the top for rows,
        // stand upright for sidebars.
        Rectangle bar = frame.Layout.DownloadBar;
        Assert.True(bar.Height > 0 && bar.Width > 0, style);
        if (vertical)
        {
            Assert.True(bar.Height > bar.Width, $"{style} tracks should stand upright");
        }
        else
        {
            Assert.True(bar.Width > bar.Height, $"{style} tracks should lie flat");
        }
    }

    [Fact]
    public void SidebarSegmentsGiveTheBarsVisibleGaps()
    {
        var config = new MonitorConfig
        {
            Style = WidgetStyles.SidebarSegments,
            ShowText = true,
            SegmentGapPx = 3,
        };
        config.Normalize();

        using Frame frame = Frame.Render(config, new WidgetVisual(1024, 512, 0.5, 0.25));
        Rectangle bar = frame.Layout.DownloadBar;

        Rectangle[] blocks = WidgetRenderer.VerticalSegments(
            bar,
            WidgetRenderer.BlockWidth(frame.Layout.BarThickness),
            frame.Layout.SegmentGap);

        Assert.True(blocks.Length >= 2, "a side-bar track should be split into blocks");
        Assert.Equal(3, frame.Layout.SegmentGap);

        // Blocks stack upwards from the taskbar edge, with the gap left empty.
        Assert.Equal(bar.Bottom, blocks[0].Bottom);
        for (int i = 1; i < blocks.Length; i++)
        {
            Assert.Equal(blocks[i - 1].Top - 3, blocks[i].Bottom);
            Assert.True(blocks[i].Top < blocks[i - 1].Top, "blocks must climb the track");
        }

        Assert.Equal(bar.Left, blocks[0].Left);
        Assert.Equal(bar.Width, blocks[0].Width);
    }

    [Fact]
    public void ASegmentGapOfZeroStillProducesSeparateBlocks()
    {
        var track = new Rectangle(0, 0, 40, 40);
        Rectangle[] blocks = WidgetRenderer.VerticalSegments(track, 5, 0);

        Assert.True(blocks.Length >= 2);
        foreach (Rectangle block in blocks)
        {
            Assert.Equal(track.Left, block.Left);
            Assert.Equal(track.Width, block.Width);
            Assert.InRange(block.Top, track.Top, track.Bottom);
        }
    }

    [Fact]
    public void HorizontalSegmentsNeverOverlapAndStayInsideTheTrack()
    {
        var track = new Rectangle(10, 5, 70, 6);
        Rectangle[] blocks = WidgetRenderer.HorizontalSegments(track, 8, 2);

        Assert.True(blocks.Length >= 2);
        for (int i = 0; i < blocks.Length; i++)
        {
            Assert.True(blocks[i].Left >= track.Left, "block starts left of the track");
            Assert.True(blocks[i].Right <= track.Right, "block runs past the track");
            Assert.Equal(track.Top, blocks[i].Top);
            Assert.Equal(track.Height, blocks[i].Height);
            if (i > 0)
            {
                Assert.True(blocks[i].Left >= blocks[i - 1].Right + 2, "blocks must not touch");
            }
        }
    }

    [Fact]
    public void ABiggerSegmentGapMeansFewerBlocks()
    {
        var track = new Rectangle(0, 0, 80, 6);
        int tight = WidgetRenderer.HorizontalSegments(track, 8, 0).Length;
        int loose = WidgetRenderer.HorizontalSegments(track, 8, 6).Length;

        Assert.True(loose < tight, "a wider gap must fit fewer blocks");
    }

    [Fact]
    public void DotCellsAreSquareAndCentredInTheirTrack()
    {
        var track = new Rectangle(0, 4, 60, 8);
        Rectangle[] cells = WidgetRenderer.DotCells(track, 6, 2);

        Assert.True(cells.Length >= 2);
        foreach (Rectangle cell in cells)
        {
            Assert.Equal(cell.Width, cell.Height);
            Assert.InRange(cell.Top, track.Top, track.Bottom - cell.Height);
            Assert.InRange(cell.Left, track.Left, track.Right - cell.Width);
        }

        int centre = track.Top + (track.Height / 2);
        Assert.InRange(centre, cells[0].Top, cells[0].Bottom);
    }

    [Fact]
    public void TwoDotsPerBlockIsNeverMoreThanAHairOfTheTrack()
    {
        var tiny = new Rectangle(0, 0, 3, 3);
        Rectangle[] cells = WidgetRenderer.DotCells(tiny, 24, 4);
        Assert.Single(cells);
        Assert.True(cells[0].Width <= 3 && cells[0].Height <= 3);
    }

    // ---------- live apply ----------

    [Fact]
    public void EveryChangeIsHandedToTheLiveWidgetImmediately()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            var seen = new List<MonitorConfig>();
            form.LiveChanged += (_, live) => seen.Add(live);

            int before = seen.Count;
            SetNumeric(form, "_nudgeX", 5);

            Assert.True(seen.Count > before, "changing a nudge must reach the live widget");
            MonitorConfig last = seen[^1];
            Assert.Equal(5, last.NudgeXPx);

            // A different kind of change must arrive too.
            SetNumeric(form, "_segmentGap", 7);
            Assert.Equal(7, seen[^1].SegmentGapPx);

            SetCombo(form, "_style", WidgetStyles.SidebarSegments);
            Assert.Equal(WidgetStyles.SidebarSegments, seen[^1].Style);
        });
    }

    [Fact]
    public void BuildResultCarriesTheNewPositionFields()
    {
        RunOnSta(() =>
        {
            var config = new MonitorConfig();
            config.Normalize();

            using var form = new SettingsForm(config);
            SetNumeric(form, "_nudgeX", -9);
            SetNumeric(form, "_nudgeY", 4);
            SetNumeric(form, "_offsetFromTray", 11);
            SetNumeric(form, "_segmentGap", 6);

            MonitorConfig result = form.BuildResult();
            Assert.Equal(-9, result.NudgeXPx);
            Assert.Equal(4, result.NudgeYPx);
            Assert.Equal(11, result.OffsetFromTrayPx);
            Assert.Equal(6, result.SegmentGapPx);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SidebarSegmentsAreActuallyDrawnWithGaps(bool showText)
    {
        foreach (int gap in new[] { 2, 4 })
        {
            var config = new MonitorConfig
            {
                Style = WidgetStyles.SidebarSegments,
                ShowText = showText,
                SegmentGapPx = gap,
                RoundedBars = false,
                BarThicknessPx = 6,
                DownloadColorHex = "#00FF00",
                UploadColorHex = "#FF0000",
            };
            config.Normalize();

            using Frame frame = Frame.Render(config, new WidgetVisual(1024, 512, 1.0, 1.0));
            Rectangle bar = frame.Layout.DownloadBar;
            int x = bar.Left + (bar.Width / 2);

            // Walk up the fully lit track and count the separate solid runs.
            var runs = new List<int>();
            int current = 0;
            for (int y = bar.Bottom - 1; y >= bar.Top; y--)
            {
                bool solid = Pixels.IsOpaque(frame.Bitmap.GetPixel(x, y), config.DownloadColor);
                if (solid)
                {
                    current++;
                }
                else if (current > 0)
                {
                    runs.Add(current);
                    current = 0;
                }
            }

            if (current > 0)
            {
                runs.Add(current);
            }

            Assert.True(runs.Count >= 2, $"gap {gap}: the side bar was painted as one solid strip ({runs.Count} run)");
            Assert.All(runs, height => Assert.True(height >= 1));
        }
    }

    /// <summary>
    /// The guard every style must pass: the window the layout asks for is the window
    /// that gets painted, the tracks stay inside it, reading order matches the chosen
    /// order, and the same state renders identically twice.
    /// </summary>
    private static void AssertFrameHoldsForEveryStyle()
    {
        foreach (string style in WidgetStyles.All)
        {
            foreach (string order in new[] { StackOrder.DownloadFirst, StackOrder.UploadFirst })
            {
                var config = new MonitorConfig { Style = style, SortOrder = order };
                config.Normalize();
                using Frame frame = Frame.Render(config, new WidgetVisual(1024, 512, 0.5, 0.25));

                Assert.Equal(style, frame.Layout.Style);
                Assert.Equal(frame.Layout.Size, frame.Bitmap.Size);

                Rectangle download = frame.Layout.DownloadBar;
                Rectangle upload = frame.Layout.UploadBar;
                foreach (Rectangle bar in new[] { download, upload })
                {
                    Assert.True(bar.Left >= 0 && bar.Top >= 0, $"{style}: a track starts outside the window");
                    Assert.True(bar.Right <= frame.Bitmap.Width, $"{style}: a track runs past the window");
                    Assert.True(bar.Bottom <= frame.Bitmap.Height, $"{style}: a track runs past the window");
                }

                bool vertical = WidgetStyles.IsVertical(style);
                bool uploadFirst = frame.Layout.UploadOnTop;
                Rectangle first = uploadFirst ? upload : download;
                Rectangle second = uploadFirst ? download : upload;
                if (vertical)
                {
                    // Upright tracks sit side by side: reading order runs left to right.
                    Assert.True(first.Left < second.Left, $"{style}/{order}: tracks read the wrong way round");
                }
                else
                {
                    Assert.True(first.Top < second.Top, $"{style}/{order}: tracks read the wrong way round");
                }

                using Frame again = frame.Rerender();
                Assert.Equal(frame.Layout.Size, again.Layout.Size);
                for (int x = 0; x < frame.Bitmap.Width; x += 5)
                {
                    for (int y = 0; y < frame.Bitmap.Height; y += 3)
                    {
                        Assert.Equal(frame.Bitmap.GetPixel(x, y).ToArgb(), again.Bitmap.GetPixel(x, y).ToArgb());
                    }
                }
            }
        }
    }

    private static void SetNumeric(SettingsForm form, string field, decimal value)
    {
        var control = (NumericUpDown)Field(form, field);
        control.Value = Numeric.Clamp(value, control.Minimum, control.Maximum);
    }

    private static void SetCombo(SettingsForm form, string field, string key)
    {
        var combo = (ComboBox)Field(form, field);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            object? item = combo.Items[i];
            string? itemKey = item?.GetType().GetProperty("Key")?.GetValue(item) as string;
            if (string.Equals(itemKey, key, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        Assert.Fail($"{key} is not offered by the {field} list");
    }

    private static object Field(SettingsForm form, string name)
        => typeof(SettingsForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
    }
}
