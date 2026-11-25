using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

/// <summary>
/// The chip marker styles: the original letters, and the two drawn-icon styles. An icon
/// replaces the letter, so the chip drops its "C:" prefix and the marker is drawn larger
/// than the dot it stands in for. The letter style is unchanged.
/// </summary>
public class ChipIconStyleTests
{
    private static MonitorConfig ChipConfig() => new MonitorConfig
    {
        ShowCpu = true,
        ShowRam = true,
        ShowGpu = true,
        ShowDisk = true,
        ShowBattery = true,
        ShowText = true,
        ShowBackdrop = false,
    }.Normalize();

    private static SystemStatsReading Reading() => new SystemStatsReading(
        42.0,
        71.0,
        55.0,
        107_374_182_400L,
        new DiskMonitor.DiskReading("C:\\", 107_374_182_400L, 200_000_000_000L),
        new BatteryMonitor.BatteryReading(OnBattery: true, Percent: 67, Charging: false));

    private static WidgetVisual ChipVisual() => new(1024, 1024, 0.5, 0.5, Reading());

    // ---- the styles on offer ---------------------------------------------------------

    [Fact]
    public void FiveMarkerStylesAreOffered()
    {
        // The user asked for more than one icon style to choose from: the letters, an
        // outline icon set, a solid icon set, and a busier detailed set in both weights.
        Assert.Equal(5, ChipIcons.All.Count);
        Assert.Contains(ChipIcons.Letter, ChipIcons.All);
        Assert.Contains(ChipIcons.Glyph, ChipIcons.All);
        Assert.Contains(ChipIcons.Solid, ChipIcons.All);
        Assert.Contains(ChipIcons.Detailed, ChipIcons.All);
        Assert.Contains(ChipIcons.DetailedSolid, ChipIcons.All);
    }

    [Fact]
    public void OnlyTheLetterStyleKeepsTheLetter()
    {
        Assert.False(ChipIcons.IsIcon(ChipIcons.Letter));
        Assert.True(ChipIcons.IsIcon(ChipIcons.Glyph));
        Assert.True(ChipIcons.IsIcon(ChipIcons.Solid));
        Assert.True(ChipIcons.IsIcon(ChipIcons.Detailed));
        Assert.True(ChipIcons.IsIcon(ChipIcons.DetailedSolid));
    }

    [Fact]
    public void TheSolidAndDetailedFlagsMatchTheStyle()
    {
        Assert.False(ChipIcons.IsSolid(ChipIcons.Glyph));
        Assert.True(ChipIcons.IsSolid(ChipIcons.Solid));
        Assert.False(ChipIcons.IsSolid(ChipIcons.Detailed));
        Assert.True(ChipIcons.IsSolid(ChipIcons.DetailedSolid));

        Assert.False(ChipIcons.IsDetailed(ChipIcons.Glyph));
        Assert.False(ChipIcons.IsDetailed(ChipIcons.Solid));
        Assert.True(ChipIcons.IsDetailed(ChipIcons.Detailed));
        Assert.True(ChipIcons.IsDetailed(ChipIcons.DetailedSolid));
    }

    [Fact]
    public void AnUnknownMarkerStyleFallsBackToTheLetters()
    {
        Assert.Equal(ChipIcons.Letter, ChipIcons.Normalize("sparkles"));
        Assert.Equal(ChipIcons.Letter, ChipIcons.Normalize(null));
        Assert.Equal(ChipIcons.Letter, ChipIcons.Normalize(""));
    }

    [Fact]
    public void EveryMarkerStyleHasItsOwnLabel()
    {
        var labels = ChipIcons.All.Select(ChipIcons.Describe).ToList();

        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- the icon replaces the letter -------------------------------------------------

    [Fact]
    public void AnIconChipDropsTheLetterPrefix()
    {
        // The letter style reserves room for "C: 42%"; the icon style shows the icon and
        // the value alone, so the chip is narrower than the letter chip.
        MonitorConfig letter = ChipConfig();
        MonitorConfig glyph = ChipConfig();
        glyph.ChipIcon = ChipIcons.Glyph;

        using Frame letterFrame = Frame.Render(letter, ChipVisual());
        using Frame glyphFrame = Frame.Render(glyph, ChipVisual());

        Assert.True(
            glyphFrame.Layout.Size.Width < letterFrame.Layout.Size.Width,
            $"the icon chip ({glyphFrame.Layout.Size.Width}) must be narrower than the letter chip ({letterFrame.Layout.Size.Width})");
    }

    [Fact]
    public void TheLetterStyleStillCarriesThePrefix()
    {
        // The default is unchanged: the chip text keeps its "C:" prefix, so the tooltip
        // and the joined caption still name the metric.
        MonitorConfig config = ChipConfig();
        config.ChipIcon = ChipIcons.Letter;

        StatsItem cpu = SystemStats.BuildItems(Reading(), config).First(i => i.Metric == ChipMetrics.Cpu);

        Assert.StartsWith("C:", cpu.Text, StringComparison.Ordinal);
    }

    // ---- the icon is bigger than the dot ----------------------------------------------

    [Fact]
    public void TheIconMarkerIsLargerThanTheDot()
    {
        MonitorConfig letter = ChipConfig();
        MonitorConfig glyph = ChipConfig();
        glyph.ChipIcon = ChipIcons.Glyph;

        using Frame letterFrame = Frame.Render(letter, ChipVisual());
        using Frame glyphFrame = Frame.Render(glyph, ChipVisual());

        Assert.True(
            glyphFrame.Layout.StatsDotSize > letterFrame.Layout.StatsDotSize,
            $"the icon marker ({glyphFrame.Layout.StatsDotSize}) must be larger than the dot ({letterFrame.Layout.StatsDotSize})");
    }

    [Theory]
    [InlineData(ChipIcons.Glyph)]
    [InlineData(ChipIcons.Solid)]
    [InlineData(ChipIcons.Detailed)]
    [InlineData(ChipIcons.DetailedSolid)]
    public void EveryIconStyleDrawsInkForEveryMetric(string style)
    {
        foreach (string metric in new[]
        {
            ChipMetrics.Cpu, ChipMetrics.Ram, ChipMetrics.Gpu, ChipMetrics.Disk, ChipMetrics.Battery,
        })
        {
            MonitorConfig config = ChipConfig();
            config.ChipIcon = style;
            config.ShowCpu = metric == ChipMetrics.Cpu;
            config.ShowRam = metric == ChipMetrics.Ram;
            config.ShowGpu = metric == ChipMetrics.Gpu;
            config.ShowDisk = metric == ChipMetrics.Disk;
            config.ShowBattery = metric == ChipMetrics.Battery;

            using Frame frame = Frame.Render(config, ChipVisual());

            int dot = frame.Layout.StatsDotSize;
            int x = frame.Layout.StatsColumnX[0];
            int y = frame.Layout.StatsRowTop + ((frame.Layout.StatsChipHeight - dot) / 2);
            Assert.True(
                OpaquePixels(frame, new Rectangle(x, y, dot, dot)) > 0,
                $"{style}: the {metric} icon drew nothing");
        }
    }

    [Fact]
    public void TheSolidStyleDiffersFromTheOutlineStyle()
    {
        // The two icon styles must not draw the same picture, or the choice is a no-op.
        MonitorConfig outline = ChipConfig();
        outline.ChipIcon = ChipIcons.Glyph;
        MonitorConfig solid = ChipConfig();
        solid.ChipIcon = ChipIcons.Solid;

        using Frame outlineFrame = Frame.Render(outline, ChipVisual());
        using Frame solidFrame = Frame.Render(solid, ChipVisual());

        Assert.NotEqual(OpaquePixels(outlineFrame), OpaquePixels(solidFrame));
    }

    [Fact]
    public void TheDetailedStyleDiffersFromTheBasicStyle()
    {
        // The detailed set must not draw the same picture as the basic one, or the extra
        // choice is a no-op.
        MonitorConfig basic = ChipConfig();
        basic.ChipIcon = ChipIcons.Glyph;
        MonitorConfig detailed = ChipConfig();
        detailed.ChipIcon = ChipIcons.Detailed;

        using Frame basicFrame = Frame.Render(basic, ChipVisual());
        using Frame detailedFrame = Frame.Render(detailed, ChipVisual());

        Assert.NotEqual(OpaquePixels(basicFrame), OpaquePixels(detailedFrame));
    }

    // ---- the icon size is adjustable ---------------------------------------------------

    [Fact]
    public void ALargerIconSizeDrawsALargerMarker()
    {
        MonitorConfig small = ChipConfig();
        small.ChipIcon = ChipIcons.Glyph;
        small.ChipIconSizePercent = MonitorConfig.MinChipIconSizePercent;
        MonitorConfig large = ChipConfig();
        large.ChipIcon = ChipIcons.Glyph;
        large.ChipIconSizePercent = MonitorConfig.MaxChipIconSizePercent;

        using Frame smallFrame = Frame.Render(small, ChipVisual());
        using Frame largeFrame = Frame.Render(large, ChipVisual());

        Assert.True(
            largeFrame.Layout.StatsDotSize > smallFrame.Layout.StatsDotSize,
            $"the {MonitorConfig.MaxChipIconSizePercent}% marker ({largeFrame.Layout.StatsDotSize}) must be larger than the {MonitorConfig.MinChipIconSizePercent}% one ({smallFrame.Layout.StatsDotSize})");
    }

    [Fact]
    public void TheIconSizeIsClampedToItsBounds()
    {
        MonitorConfig tooSmall = ChipConfig();
        tooSmall.ChipIconSizePercent = 1;
        MonitorConfig tooLarge = ChipConfig();
        tooLarge.ChipIconSizePercent = 9999;

        Assert.Equal(MonitorConfig.MinChipIconSizePercent, tooSmall.Normalize().ChipIconSizePercent);
        Assert.Equal(MonitorConfig.MaxChipIconSizePercent, tooLarge.Normalize().ChipIconSizePercent);
    }

    [Fact]
    public void TheIconSizeStartsAtTwoHundredAndFiftyAndStepsByFifty()
    {
        // The user asked for the size to start at 150 and climb in 50-point stops, with 250
        // as the default, so the spinner never offers the 100/125 values that looked the same.
        Assert.Equal(250, MonitorConfig.DefaultChipIconSizePercent);
        Assert.Equal(150, MonitorConfig.MinChipIconSizePercent);
        Assert.Equal(300, MonitorConfig.MaxChipIconSizePercent);
        Assert.Equal(50, MonitorConfig.ChipIconSizeStep);
        Assert.Equal(250, new MonitorConfig().ChipIconSizePercent);

        // The stops the spinner walks: 150, 200, 250, 300.
        List<int> stops = new();
        for (int v = MonitorConfig.MinChipIconSizePercent; v <= MonitorConfig.MaxChipIconSizePercent; v += MonitorConfig.ChipIconSizeStep)
        {
            stops.Add(v);
        }

        Assert.Equal(new[] { 150, 200, 250, 300 }, stops);
    }

    [Fact]
    public void TheIconSizeDoesNotChangeTheLetterStyle()
    {
        // The size only scales the icon marker; the letter style keeps the plain dot.
        MonitorConfig small = ChipConfig();
        small.ChipIcon = ChipIcons.Letter;
        small.ChipIconSizePercent = MonitorConfig.MinChipIconSizePercent;
        MonitorConfig large = ChipConfig();
        large.ChipIcon = ChipIcons.Letter;
        large.ChipIconSizePercent = MonitorConfig.MaxChipIconSizePercent;

        using Frame smallFrame = Frame.Render(small, ChipVisual());
        using Frame largeFrame = Frame.Render(large, ChipVisual());

        Assert.Equal(smallFrame.Layout.StatsDotSize, largeFrame.Layout.StatsDotSize);
    }

    // ---- the icon keeps its distance from the figure -----------------------------------

    [Fact]
    public void TheIconLeavesMoreRoomBeforeTheFigureThanTheDotDoes()
    {
        // The icon's ink reaches its box edge, so it needs more air before the figure than
        // the letter dot does; otherwise the two read as one blob.
        MonitorConfig letter = ChipConfig();
        letter.ChipIcon = ChipIcons.Letter;
        MonitorConfig icon = ChipConfig();
        icon.ChipIcon = ChipIcons.Glyph;

        using Frame letterFrame = Frame.Render(letter, ChipVisual());
        using Frame iconFrame = Frame.Render(icon, ChipVisual());

        Assert.True(
            GapAfterMarker(iconFrame) > GapAfterMarker(letterFrame),
            $"the icon gap ({GapAfterMarker(iconFrame)}) must exceed the dot gap ({GapAfterMarker(letterFrame)})");
    }

    [Fact]
    public void TheIconNeverTouchesTheFigure()
    {
        // Whatever the size, at least one clear column must separate the icon from the text.
        foreach (int percent in new[] { MonitorConfig.MinChipIconSizePercent, 150, MonitorConfig.MaxChipIconSizePercent })
        {
            MonitorConfig config = ChipConfig();
            config.ChipIcon = ChipIcons.Detailed;
            config.ChipIconSizePercent = percent;

            using Frame frame = Frame.Render(config, ChipVisual());

            Assert.True(GapAfterMarker(frame) >= 1, $"at {percent}% the icon touched the figure");
        }
    }

    // ---- the icon strokes stay crisp ---------------------------------------------------

    [Fact]
    public void TheIconStrokesAreCrispNotSmudged()
    {
        // Anti-aliased strokes at chip size smear into grey, and the smear shifts with every
        // size. Every pixel the icon draws must be either fully inked or fully clear.
        foreach (int percent in new[] { MonitorConfig.MinChipIconSizePercent, 150, MonitorConfig.MaxChipIconSizePercent })
        {
            MonitorConfig config = ChipConfig();
            config.ChipIcon = ChipIcons.Detailed;
            config.ChipIconSizePercent = percent;

            using Frame frame = Frame.Render(config, ChipVisual());

            int dot = frame.Layout.StatsDotSize;
            int x = frame.Layout.StatsColumnX[0];
            int y = frame.Layout.StatsRowTop + ((frame.Layout.StatsChipHeight - dot) / 2);
            int smudged = 0;
            for (int py = y; py < y + dot; py++)
            {
                for (int px = x; px < x + dot; px++)
                {
                    int alpha = frame.Bitmap.GetPixel(px, py).A;
                    if (alpha is > 40 and < 200)
                    {
                        smudged++;
                    }
                }
            }

            Assert.True(smudged == 0, $"at {percent}% the icon left {smudged} half-lit pixels");
        }
    }

    /// <summary>
    /// The clear columns between the first chip's marker box and the first ink of its figure.
    /// </summary>
    private static int GapAfterMarker(Frame frame)
    {
        int dot = frame.Layout.StatsDotSize;
        int x = frame.Layout.StatsColumnX[0];
        int y = frame.Layout.StatsRowTop + ((frame.Layout.StatsChipHeight - dot) / 2);
        int markerRight = x + dot;
        for (int px = markerRight; px < frame.Bitmap.Width; px++)
        {
            bool inked = false;
            for (int py = y; py < y + dot; py++)
            {
                if (frame.Bitmap.GetPixel(px, py).A > 40)
                {
                    inked = true;
                    break;
                }
            }

            if (inked)
            {
                return px - markerRight;
            }
        }

        return frame.Bitmap.Width - markerRight;
    }

    // ---- the solid icons hold their shape as the size changes --------------------------

    [Theory]
    [InlineData(ChipIcons.Solid)]
    [InlineData(ChipIcons.DetailedSolid)]
    public void TheSolidIconStaysSymmetricAtEverySize(string style)
    {
        // The solid shapes used to collapse and skew as the size changed, so the die read as a
        // fat plus at one size and a thin bar at the next. The ink must mirror left-right and
        // top-bottom inside the marker box at every size.
        foreach (int percent in new[] { MonitorConfig.MinChipIconSizePercent, 125, 150, 175, 200, MonitorConfig.MaxChipIconSizePercent })
        {
            MonitorConfig config = ChipConfig();
            config.ChipIcon = style;
            config.ChipIconSizePercent = percent;

            using Frame frame = Frame.Render(config, ChipVisual());

            int dot = frame.Layout.StatsDotSize;
            int x = frame.Layout.StatsColumnX[0];
            int y = frame.Layout.StatsRowTop + ((frame.Layout.StatsChipHeight - dot) / 2);
            bool[,] ink = new bool[dot, dot];
            for (int py = 0; py < dot; py++)
            {
                for (int px = 0; px < dot; px++)
                {
                    ink[px, py] = frame.Bitmap.GetPixel(x + px, y + py).A > 40;
                }
            }

            for (int py = 0; py < dot; py++)
            {
                for (int px = 0; px < dot; px++)
                {
                    Assert.True(
                        ink[px, py] == ink[dot - 1 - px, py],
                        $"{style} at {percent}% is not left-right symmetric at ({px},{py})");
                    Assert.True(
                        ink[px, py] == ink[px, dot - 1 - py],
                        $"{style} at {percent}% is not top-bottom symmetric at ({px},{py})");
                }
            }
        }
    }

    // ---- the settings dialog offers the choice ----------------------------------------

    [Fact]
    public void TheSettingsDialogCarriesEveryMarkerStyle()
    {
        // The dialog is a spinner, so the plumbing is checked at the source: it is built
        // from the config value and offers every style, and the size spinner is wired.
        string source = SourceText.Read("UI", "SettingsForm.cs");

        Assert.Contains("ChipIcons.Letter", source, StringComparison.Ordinal);
        Assert.Contains("ChipIcons.Glyph", source, StringComparison.Ordinal);
        Assert.Contains("ChipIcons.Solid", source, StringComparison.Ordinal);
        Assert.Contains("ChipIcons.Detailed", source, StringComparison.Ordinal);
        Assert.Contains("ChipIcons.DetailedSolid", source, StringComparison.Ordinal);
        Assert.Contains("ChipIcon = SelectedKey(_chipIcon", source, StringComparison.Ordinal);
        Assert.Contains("CreateChipIconSizeBox(config.ChipIconSizePercent)", source, StringComparison.Ordinal);
        Assert.Contains("ChipIconSizePercent = (int)_chipIconSize.Value", source, StringComparison.Ordinal);
        Assert.Contains("AddRow(colours, \"Icon size:\", _chipIconSize)", source, StringComparison.Ordinal);
    }

    // ---- helpers ---------------------------------------------------------------------

    private static int OpaquePixels(Frame frame) =>
        OpaquePixels(frame, new Rectangle(Point.Empty, frame.Bitmap.Size));

    private static int OpaquePixels(Frame frame, Rectangle area)
    {
        int count = 0;
        for (int x = area.Left; x < area.Right; x++)
        {
            for (int y = area.Top; y < area.Bottom; y++)
            {
                count += frame.Bitmap.GetPixel(x, y).A == 255 ? 1 : 0;
            }
        }

        return count;
    }
}
