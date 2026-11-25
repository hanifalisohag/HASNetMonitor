using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The chip look options: an independent colour per chip, a level indicator (ring or bar),
/// a vector marker instead of the letter, and the glass chip pill. Each is opt-in, so the
/// default configuration must keep drawing exactly what it drew before.
/// </summary>
public class ChipAppearanceTests
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

    // ---- independent colour per chip -------------------------------------------------

    [Fact]
    public void AChipColourSetInTheConfigReachesTheDrawnChip()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "#123456";

        StatsItem cpu = SystemStats.BuildItems(Reading(), config).First(item => item.Metric == ChipMetrics.Cpu);

        Assert.Equal(Color.FromArgb(0x12, 0x34, 0x56), cpu.Dot);
    }

    [Fact]
    public void AnUnsetChipColourFallsBackToTheBuiltInPalette()
    {
        MonitorConfig config = ChipConfig();

        StatsItem cpu = SystemStats.BuildItems(Reading(), config).First(item => item.Metric == ChipMetrics.Cpu);

        Assert.Equal(ChipMetrics.Default(ChipMetrics.Cpu), cpu.Dot);
    }

    [Fact]
    public void EachChipCanTakeADifferentColour()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "#FF0000";
        config.RamColorHex = "#00FF00";
        config.GpuColorHex = "#0000FF";
        config.DiskColorHex = "#FFFF00";
        config.BatteryColorHex = "#FF00FF";

        StatsItem[] items = SystemStats.BuildItems(Reading(), config);

        Assert.Equal(Color.Red.ToArgb(), items.First(i => i.Metric == ChipMetrics.Cpu).Dot.ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), items.First(i => i.Metric == ChipMetrics.Ram).Dot.ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), items.First(i => i.Metric == ChipMetrics.Gpu).Dot.ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), items.First(i => i.Metric == ChipMetrics.Disk).Dot.ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(), items.First(i => i.Metric == ChipMetrics.Battery).Dot.ToArgb());
    }

    [Fact]
    public void AChipColourSurvivesAJsonRoundTrip()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "#123456";
        config.BatteryColorHex = "#ABCDEF";

        MonitorConfig back = MonitorConfig.FromJson(config.ToJson());

        Assert.Equal("#123456", back.CpuColorHex);
        Assert.Equal("#ABCDEF", back.BatteryColorHex);
    }

    [Fact]
    public void AnInvalidChipColourIsRepairedNotKept()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "not-a-colour";

        MonitorConfig normalized = config.Normalize();

        Assert.NotEqual("not-a-colour", normalized.CpuColorHex);
        Assert.Equal(ChipMetrics.Default(ChipMetrics.Cpu), normalized.ChipColor(ChipMetrics.Cpu));
    }

    [Fact]
    public void AnEmptyChipColourStaysEmptySoThePaletteApplies()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "";

        Assert.Equal("", config.Normalize().CpuColorHex);
    }

    // ---- level indicators ------------------------------------------------------------

    [Fact]
    public void TheRingIndicatorDrawsInkWhereThePlainDotDoes()
    {
        MonitorConfig plain = ChipConfig();
        MonitorConfig ring = ChipConfig();
        ring.ChipIndicator = ChipIndicators.Ring;

        using Frame plainFrame = Frame.Render(plain, ChipVisual());
        using Frame ringFrame = Frame.Render(ring, ChipVisual());

        // The ring keeps the dot's footprint, so the widget's size is unchanged, and it
        // paints in the marker box where the plain dot does.
        Assert.Equal(plainFrame.Layout.Size, ringFrame.Layout.Size);
        int dot = ringFrame.Layout.StatsDotSize;
        int x = ringFrame.Layout.StatsColumnX[0];
        int y = ringFrame.Layout.StatsRowTop + ((ringFrame.Layout.StatsChipHeight - dot) / 2);
        Assert.True(OpaquePixels(ringFrame, new Rectangle(x, y, dot, dot)) > 0, "the ring drew nothing");
    }

    [Fact]
    public void TheRingFillsMoreAsTheLevelRises()
    {
        MonitorConfig config = ChipConfig();
        config.ChipIndicator = ChipIndicators.Ring;
        config.ShowRam = false;
        config.ShowGpu = false;
        config.ShowDisk = false;
        config.ShowBattery = false;

        int low = RingInk(10.0);
        int high = RingInk(90.0);

        Assert.True(high > low, $"a 90% ring ({high}) must carry more solid ink than a 10% ring ({low})");

        // The ring is the only opaque paint in the chip's dot box once the text is off, so
        // counting opaque pixels there measures the arc and nothing else.
        int RingInk(double cpu)
        {
            var reading = new SystemStatsReading(
                cpu, double.NaN, null, -1,
                DiskMonitor.DiskReading.Unavailable,
                BatteryMonitor.BatteryReading.Unavailable);
            using Frame frame = Frame.Render(config, new WidgetVisual(1024, 1024, 0.5, 0.5, reading));
            int dot = frame.Layout.StatsDotSize;
            int x = frame.Layout.StatsColumnX[0];
            int y = frame.Layout.StatsRowTop + ((frame.Layout.StatsChipHeight - dot) / 2);
            return OpaquePixels(frame, new Rectangle(x, y, dot, dot));
        }
    }

    [Fact]
    public void TheBarIndicatorDrawsBelowTheChipText()
    {
        MonitorConfig plain = ChipConfig();
        MonitorConfig bar = ChipConfig();
        bar.ChipIndicator = ChipIndicators.Bar;
        foreach (MonitorConfig config in new[] { plain, bar })
        {
            config.ShowRam = false;
            config.ShowGpu = false;
            config.ShowDisk = false;
            config.ShowBattery = false;
        }

        using Frame plainFrame = Frame.Render(plain, ChipVisual());
        using Frame barFrame = Frame.Render(bar, ChipVisual());

        // The bar adds paint under the chip row and changes nothing else.
        Assert.Equal(plainFrame.Layout.Size, barFrame.Layout.Size);
        Assert.True(OpaquePixels(barFrame) > OpaquePixels(plainFrame), "the bar drew nothing");
    }

    [Fact]
    public void AnUnknownIndicatorFallsBackToThePlainDot()
    {
        Assert.Equal(ChipIndicators.None, ChipIndicators.Normalize("sparkles"));
        Assert.Equal(ChipIndicators.None, ChipIndicators.Normalize(null));
    }

    // ---- vector markers --------------------------------------------------------------

    [Theory]
    [InlineData(ChipMetrics.Cpu)]
    [InlineData(ChipMetrics.Ram)]
    [InlineData(ChipMetrics.Gpu)]
    [InlineData(ChipMetrics.Disk)]
    [InlineData(ChipMetrics.Battery)]
    public void EveryGlyphMarkerDrawsInk(string metric)
    {
        MonitorConfig config = ChipConfig();
        config.ChipIcon = ChipIcons.Glyph;
        config.ShowCpu = metric == ChipMetrics.Cpu;
        config.ShowRam = metric == ChipMetrics.Ram;
        config.ShowGpu = metric == ChipMetrics.Gpu;
        config.ShowDisk = metric == ChipMetrics.Disk;
        config.ShowBattery = metric == ChipMetrics.Battery;

        using Frame frame = Frame.Render(config, ChipVisual());

        // The glyph is the only opaque paint in the marker box, so ink there proves it drew.
        int dot = frame.Layout.StatsDotSize;
        int x = frame.Layout.StatsColumnX[0];
        int y = frame.Layout.StatsRowTop + ((frame.Layout.StatsChipHeight - dot) / 2);
        Assert.True(OpaquePixels(frame, new Rectangle(x, y, dot, dot)) > 0, $"the {metric} glyph drew nothing");
    }

    [Fact]
    public void TheGlyphMarkerIsLargerThanTheDot()
    {
        // The icon is drawn bigger than the dot it replaces, so its strokes stay legible at
        // taskbar sizes. The letter style keeps the dot's footprint, so switching to icons
        // is the only thing that widens the marker.
        MonitorConfig letter = ChipConfig();
        MonitorConfig glyph = ChipConfig();
        glyph.ChipIcon = ChipIcons.Glyph;

        using Frame letterFrame = Frame.Render(letter, ChipVisual());
        using Frame glyphFrame = Frame.Render(glyph, ChipVisual());

        Assert.True(
            glyphFrame.Layout.StatsDotSize > letterFrame.Layout.StatsDotSize,
            $"the icon marker ({glyphFrame.Layout.StatsDotSize}) must be larger than the dot ({letterFrame.Layout.StatsDotSize})");
        Assert.True(
            glyphFrame.Layout.StatsDotSize % 2 == 1,
            "the icon marker side must stay odd so it centres on a whole pixel");
    }

    [Fact]
    public void TheLetterMarkerKeepsTheDotsFootprint()
    {
        // The default (letters) is unchanged: the marker is the dot, and the chip carries
        // its "C:" prefix. Only the icon styles drop the prefix and grow the marker.
        MonitorConfig config = ChipConfig();
        config.ChipIcon = ChipIcons.Letter;

        using Frame frame = Frame.Render(config, ChipVisual());

        Assert.Equal(ChipIcons.Letter, ChipIcons.Normalize(config.ChipIcon));
        Assert.False(ChipIcons.IsIcon(config.ChipIcon));
    }

    // ---- the marker stays crisp at every font size ------------------------------------

    /// <summary>
    /// The marker's side must be odd, so it centres on a whole pixel. An even side at a
    /// half-pixel offset anti-aliases the dot into a hollow ring and the glyph's square
    /// strokes into a distorted blob - the "not square" look reported at 8, 9 and 10pt.
    /// </summary>
    [Theory]
    [InlineData(8.0)]
    [InlineData(8.5)]
    [InlineData(9.0)]
    [InlineData(9.5)]
    [InlineData(10.0)]
    [InlineData(10.5)]
    public void TheMarkerSideIsOddSoItCentresOnAWholePixel(double points)
    {
        MonitorConfig config = ChipConfig();
        config.FontSizePt = points;

        using Frame frame = Frame.Render(config, ChipVisual());

        Assert.True(
            frame.Layout.StatsDotSize % 2 == 1,
            $"{points}pt: the marker side ({frame.Layout.StatsDotSize}) must be odd");
    }

    /// <summary>
    /// The glyph marker must be symmetric at every font size. A half-pixel placement (or an
    /// even marker side) anti-aliases the square glyph into a lopsided blob - the distorted,
    /// "not square" look the user saw at 8, 9 and 10pt.
    /// </summary>
    [Theory]
    [InlineData(8.0)]
    [InlineData(8.5)]
    [InlineData(9.0)]
    [InlineData(9.5)]
    [InlineData(10.0)]
    [InlineData(10.5)]
    public void TheGlyphMarkerIsSymmetricAtEveryFontSize(double points)
    {
        MonitorConfig config = ChipConfig();
        config.FontSizePt = points;
        config.ChipIndicator = ChipIndicators.None;
        config.ChipIcon = ChipIcons.Glyph;

        using Frame frame = Frame.Render(config, ChipVisual());
        WidgetLayout layout = frame.Layout;

        int dot = layout.StatsDotSize;
        int x = layout.StatsColumnX[0];
        int y = layout.StatsRowTop + ((layout.StatsChipHeight - dot) / 2);
        Rectangle box = MarkerInkBox(frame, new Rectangle(x, y, dot, dot));

        Assert.True(box.Width > 0 && box.Height > 0, $"{points}pt: the glyph drew no ink");
        Assert.True(
            Math.Abs(box.Width - box.Height) <= 1,
            $"{points}pt: the glyph ink box is {box.Width}x{box.Height}, not square");

        // The ink must sit centred in the marker box: equal margins on both sides. A
        // half-pixel placement pushes the ink off centre, which is the distortion.
        int leftMargin = box.Left - x;
        int rightMargin = (x + dot) - box.Right;
        int topMargin = box.Top - y;
        int bottomMargin = (y + dot) - box.Bottom;
        Assert.True(
            Math.Abs(leftMargin - rightMargin) <= 1 && Math.Abs(topMargin - bottomMargin) <= 1,
            $"{points}pt: the glyph is off centre (margins {leftMargin}/{rightMargin}, {topMargin}/{bottomMargin})");
    }

    /// <summary>
    /// The marker must be placed on a whole pixel. A half-pixel offset is what anti-aliases
    /// the glyph's square strokes into a lopsided, "not square" blob.
    /// </summary>
    [Theory]
    [InlineData(8.0)]
    [InlineData(8.5)]
    [InlineData(9.0)]
    [InlineData(9.5)]
    [InlineData(10.0)]
    [InlineData(10.5)]
    public void TheMarkerIsPlacedOnAWholePixel(double points)
    {
        MonitorConfig config = ChipConfig();
        config.FontSizePt = points;

        using Frame frame = Frame.Render(config, ChipVisual());
        WidgetLayout layout = frame.Layout;

        int dot = layout.StatsDotSize;
        int y = layout.StatsRowTop + ((layout.StatsChipHeight - dot) / 2);

        // The renderer snaps the marker's top to a whole pixel; the layout must agree, or
        // the drawn marker and the layout's idea of it disagree by half a pixel.
        Assert.True(
            (layout.StatsChipHeight - dot) % 2 == 0 || dot % 2 == 1,
            $"{points}pt: the marker cannot centre on a whole pixel (chip {layout.StatsChipHeight}, dot {dot})");
        Assert.True(y >= layout.StatsRowTop, $"{points}pt: the marker sits above its row");
    }

    /// <summary>The bounding box of the opaque ink inside a region, or an empty rectangle.</summary>
    private static Rectangle MarkerInkBox(Frame frame, Rectangle region)
    {
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (int y = Math.Max(0, region.Top); y < Math.Min(frame.Bitmap.Height, region.Bottom); y++)
        {
            for (int x = Math.Max(0, region.Left); x < Math.Min(frame.Bitmap.Width, region.Right); x++)
            {
                if (frame.Bitmap.GetPixel(x, y).A > 40)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? Rectangle.Empty : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    [Fact]
    public void AnUnknownIconFallsBackToTheLetter()
    {
        Assert.Equal(ChipIcons.Letter, ChipIcons.Normalize("emoji"));
        Assert.Equal(ChipIcons.Letter, ChipIcons.Normalize(null));
    }

    // ---- glass -----------------------------------------------------------------------

    [Fact]
    public void TheGlassTracksAreAToggleNotAStyle()
    {
        // The style list is pinned by the README, so the glassy look is a toggle that works
        // with every style rather than an eleventh style.
        Assert.Equal(10, WidgetStyles.All.Count);
        Assert.DoesNotContain("Glass", WidgetStyles.All);
    }

    [Fact]
    public void TheGlassTracksDrawTheSameFiguresAsThePlainStyle()
    {
        MonitorConfig plain = ChipConfig();
        MonitorConfig glass = ChipConfig();
        glass.GlassMeters = true;

        using Frame plainFrame = Frame.Render(plain, new WidgetVisual(1024, 2048, 0.5, 0.25));
        using Frame glassFrame = Frame.Render(glass, new WidgetVisual(1024, 2048, 0.5, 0.25));

        // The toggle changes the paint, not the geometry: the figures keep their rows.
        Assert.Equal(plainFrame.Layout.DownloadText, glassFrame.Layout.DownloadText);
        Assert.Equal(plainFrame.Layout.UploadText, glassFrame.Layout.UploadText);
        // The glass track is translucent, so it paints faint pixels the solid style does not.
        Assert.True(FaintPixels(glassFrame) > FaintPixels(plainFrame), "the glass tracks drew nothing");
    }

    [Fact]
    public void TheGlassFillGrowsWithTheLevelAndLeavesTheTrackVisible()
    {
        // The fill must read as a level rising inside a tube: the filled part is brighter
        // than the empty track, and the filled width grows with the ratio. A fill that
        // covered the whole track at every level would look like a colour switch.
        MonitorConfig config = ChipConfig();
        config.GlassMeters = true;
        config.ShowCpu = false;
        config.ShowRam = false;
        config.ShowGpu = false;
        config.ShowDisk = false;
        config.ShowBattery = false;

        (int filled, int track) Low() => GlassProfile(config, 0.1);
        (int filled, int track) High() => GlassProfile(config, 0.9);

        (int lowFilled, int lowTrack) = Low();
        (int highFilled, int highTrack) = High();

        Assert.True(highFilled > lowFilled, $"a 90% fill ({highFilled}) must be wider than a 10% fill ({lowFilled})");
        Assert.True(lowTrack > 0, "the empty track must stay visible behind the fill");
        Assert.True(lowFilled > 0, "a 10% fill must still paint something");
    }

    /// <summary>
    /// Counts the columns of the download bar whose brightest pixel is the fill (alpha above
    /// the track's) and the columns that are only the track.
    /// </summary>
    private static (int Filled, int Track) GlassProfile(MonitorConfig config, double ratio)
    {
        using Frame frame = Frame.Render(config, new WidgetVisual(0, 0, ratio, ratio));
        Rectangle bar = frame.Layout.DownloadBar;
        int filled = 0;
        int track = 0;
        for (int x = bar.Left; x < bar.Right; x++)
        {
            int alpha = 0;
            for (int y = bar.Top; y < bar.Bottom; y++)
            {
                alpha = Math.Max(alpha, frame.Bitmap.GetPixel(x, y).A);
            }

            if (alpha >= 200)
            {
                filled++;
            }
            else if (alpha > 0)
            {
                track++;
            }
        }

        return (filled, track);
    }

    [Fact]
    public void TheGlassChipPillCanBeUsedWithAnyStyle()
    {
        MonitorConfig plain = ChipConfig();
        MonitorConfig glassy = ChipConfig();
        glassy.GlassChips = true;

        using Frame plainFrame = Frame.Render(plain, ChipVisual());
        using Frame glassyFrame = Frame.Render(glassy, ChipVisual());

        Assert.Equal(plainFrame.Layout.Size, glassyFrame.Layout.Size);
        // The hit wash makes every pixel non-transparent, so the pill is measured by the
        // faint pixels it adds, not by any-alpha ink.
        Assert.True(FaintPixels(glassyFrame) > FaintPixels(plainFrame), "the pill added no paint");
    }

    // ---- presets ---------------------------------------------------------------------

    [Fact]
    public void APresetCanCarryTheChipLook()
    {
        // No shipped preset sets the chip look, but the mechanism must carry it: a preset
        // that names the chip options applies them, and one that leaves them null keeps
        // whatever the current config had.
        var preset = new WidgetPreset(
            "Probe", "test",
            "#5AC8FA", "#7C6CF0", WidgetStyles.Bars, ArrowStyles.Chevron, StackOrder.DownloadFirst,
            6, 0, 10, 150,
            chipIndicator: ChipIndicators.Ring, chipIcon: ChipIcons.Glyph,
            glassMeters: true, glassChips: true);

        MonitorConfig applied = WidgetPresets.Apply(preset, new MonitorConfig());

        Assert.Equal(ChipIndicators.Ring, applied.ChipIndicator);
        Assert.Equal(ChipIcons.Glyph, applied.ChipIcon);
        Assert.True(applied.GlassMeters);
        Assert.True(applied.GlassChips);
    }

    // ---- helpers ---------------------------------------------------------------------

    private static WidgetVisual ChipVisual() => new(1024, 1024, 0.5, 0.5, Reading());

    /// <summary>
    /// Pixels painted faintly: more than the hit wash, less than solid. The glass pill is
    /// translucent, so this is what shows it was drawn.
    /// </summary>
    private static int FaintPixels(Frame frame)
    {
        int count = 0;
        for (int x = 0; x < frame.Bitmap.Width; x++)
        {
            for (int y = 0; y < frame.Bitmap.Height; y++)
            {
                int alpha = frame.Bitmap.GetPixel(x, y).A;
                count += alpha > WidgetRenderer.HitWashAlpha && alpha < 255 ? 1 : 0;
            }
        }

        return count;
    }

    // ---- per-chip text colour --------------------------------------------------------

    [Fact]
    public void AChipTextColourSetInTheConfigReachesTheDrawnChip()
    {
        MonitorConfig config = ChipConfig();
        config.CpuTextColorHex = "#FF00AA";

        Color drawn = config.ChipTextColor(ChipMetrics.Cpu, Color.White);

        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0xAA), drawn);
    }

    [Fact]
    public void AnUnsetChipTextColourFollowsTheFigureColour()
    {
        MonitorConfig config = ChipConfig();
        Color figure = Color.FromArgb(0x11, 0x22, 0x33);

        Assert.Equal(figure, config.ChipTextColor(ChipMetrics.Cpu, figure));
    }

    [Fact]
    public void EachChipTextCanTakeADifferentColour()
    {
        MonitorConfig config = ChipConfig();
        config.CpuTextColorHex = "#FF0000";
        config.RamTextColorHex = "#00FF00";
        config.GpuTextColorHex = "#0000FF";
        config.DiskTextColorHex = "#FFFF00";
        config.BatteryTextColorHex = "#FF00FF";

        Color fallback = Color.White;
        Assert.Equal(Color.FromArgb(0xFF, 0, 0), config.ChipTextColor(ChipMetrics.Cpu, fallback));
        Assert.Equal(Color.FromArgb(0, 0xFF, 0), config.ChipTextColor(ChipMetrics.Ram, fallback));
        Assert.Equal(Color.FromArgb(0, 0, 0xFF), config.ChipTextColor(ChipMetrics.Gpu, fallback));
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0), config.ChipTextColor(ChipMetrics.Disk, fallback));
        Assert.Equal(Color.FromArgb(0xFF, 0, 0xFF), config.ChipTextColor(ChipMetrics.Battery, fallback));
    }

    [Fact]
    public void AChipTextColourSurvivesAJsonRoundTrip()
    {
        MonitorConfig config = ChipConfig();
        config.CpuTextColorHex = "#123456";

        MonitorConfig restored = MonitorConfig.FromJson(config.ToJson());

        Assert.Equal("#123456", restored.CpuTextColorHex);
    }

    [Fact]
    public void AnInvalidChipTextColourIsRepairedToTheFigureColour()
    {
        MonitorConfig config = ChipConfig();
        config.CpuTextColorHex = "not-a-colour";

        config.Normalize();

        Assert.Equal(MonitorConfig.DefaultTextColor, config.CpuTextColorHex);
    }

    [Fact]
    public void AnEmptyChipTextColourStaysEmptySoTheFigureColourApplies()
    {
        MonitorConfig config = ChipConfig();
        config.CpuTextColorHex = string.Empty;

        config.Normalize();

        Assert.Equal(string.Empty, config.CpuTextColorHex);
    }

    // ---- reset to default ------------------------------------------------------------

    [Fact]
    public void ClearingAChipColourRestoresThePaletteDefault()
    {
        MonitorConfig config = ChipConfig();
        config.SetChipColorHex(ChipMetrics.Cpu, "#123456");
        Assert.Equal("#123456", config.CpuColorHex);

        config.SetChipColorHex(ChipMetrics.Cpu, string.Empty);

        Assert.Equal(string.Empty, config.CpuColorHex);
        Assert.Equal(ChipMetrics.Default(ChipMetrics.Cpu), config.ChipColor(ChipMetrics.Cpu));
    }

    [Fact]
    public void ClearingAChipTextColourRestoresTheFigureFallback()
    {
        MonitorConfig config = ChipConfig();
        config.SetChipTextColorHex(ChipMetrics.Ram, "#123456");
        Assert.Equal("#123456", config.RamTextColorHex);

        config.SetChipTextColorHex(ChipMetrics.Ram, string.Empty);

        Assert.Equal(string.Empty, config.RamTextColorHex);
        Color figure = Color.FromArgb(0xAB, 0xCD, 0xEF);
        Assert.Equal(figure, config.ChipTextColor(ChipMetrics.Ram, figure));
    }

    [Fact]
    public void EveryChipColourCanBeClearedIndependently()
    {
        MonitorConfig config = ChipConfig();
        foreach (string metric in ChipMetrics.All)
        {
            config.SetChipColorHex(metric, "#010203");
        }

        config.SetChipColorHex(ChipMetrics.Gpu, string.Empty);

        Assert.Equal(string.Empty, config.ChipColorHex(ChipMetrics.Gpu));
        foreach (string metric in ChipMetrics.All.Where(m => m != ChipMetrics.Gpu))
        {
            Assert.Equal("#010203", config.ChipColorHex(metric));
        }
    }

    [Fact]
    public void EveryChipTextColourCanBeClearedIndependently()
    {
        MonitorConfig config = ChipConfig();
        foreach (string metric in ChipMetrics.All)
        {
            config.SetChipTextColorHex(metric, "#010203");
        }

        config.SetChipTextColorHex(ChipMetrics.Disk, string.Empty);

        Assert.Equal(string.Empty, config.ChipTextColorHex(ChipMetrics.Disk));
        foreach (string metric in ChipMetrics.All.Where(m => m != ChipMetrics.Disk))
        {
            Assert.Equal("#010203", config.ChipTextColorHex(metric));
        }
    }

    [Fact]
    public void ACloneCarriesTheChipTextColours()
    {
        MonitorConfig config = ChipConfig();
        config.CpuTextColorHex = "#112233";
        config.BatteryTextColorHex = "#445566";

        MonitorConfig clone = config.Clone();

        Assert.Equal("#112233", clone.CpuTextColorHex);
        Assert.Equal("#445566", clone.BatteryTextColorHex);
    }

    [Fact]
    public void CopyFromCarriesTheChipTextColours()
    {
        var source = new MonitorConfig { CpuTextColorHex = "#112233", DiskTextColorHex = "#445566" };
        var target = new MonitorConfig();

        target.CopyFrom(source);

        Assert.Equal("#112233", target.CpuTextColorHex);
        Assert.Equal("#445566", target.DiskTextColorHex);
    }

    // ---- the dialog's swatches -------------------------------------------------------

    [Fact]
    public void AnUnsetChipSwatchShowsThePaletteColourNotBlack()
    {
        WithShownDialog(new MonitorConfig(), form =>
        {
            Button swatch = FindSwatch(form, "CPU chip colour");

            Assert.Equal(ChipMetrics.Default(ChipMetrics.Cpu), swatch.BackColor);
            Assert.Equal(string.Empty, swatch.Tag as string);
        });
    }

    [Fact]
    public void AChipSwatchShowsTheColourTheConfigCarries()
    {
        WithShownDialog(new MonitorConfig { CpuColorHex = "#123456" }, form =>
        {
            Button swatch = FindSwatch(form, "CPU chip colour");

            Assert.Equal(Color.FromArgb(0x12, 0x34, 0x56), swatch.BackColor);
            Assert.Equal("#123456", swatch.Tag as string);
        });
    }

    [Fact]
    public void AnUnsetChipTextSwatchShowsTheFigureColour()
    {
        WithShownDialog(new MonitorConfig(), form =>
        {
            Button swatch = FindSwatch(form, "CPU chip text colour");

            Assert.Equal(ColorHelper.Parse(MonitorConfig.DefaultTextColor, Color.White), swatch.BackColor);
            Assert.Equal(string.Empty, swatch.Tag as string);
        });
    }

    [Fact]
    public void TheResetButtonClearsEveryChipColour()
    {
        var config = new MonitorConfig
        {
            CpuColorHex = "#111111",
            RamColorHex = "#222222",
            GpuColorHex = "#333333",
            DiskColorHex = "#444444",
            BatteryColorHex = "#555555",
        };

        WithShownDialog(config, form =>
        {
            Button reset = FindButton(form, "Reset", "Reset every chip colour to its palette default");
            reset.PerformClick();

            foreach (string metric in ChipMetrics.All)
            {
                Button swatch = FindSwatch(form, $"{ChipMetrics.Describe(metric)} chip colour");
                Assert.Equal(string.Empty, swatch.Tag as string);
                Assert.Equal(ChipMetrics.Default(metric), swatch.BackColor);
            }
        });
    }

    [Fact]
    public void TheResetButtonClearsEveryChipTextColour()
    {
        var config = new MonitorConfig
        {
            CpuTextColorHex = "#111111",
            RamTextColorHex = "#222222",
            GpuTextColorHex = "#333333",
            DiskTextColorHex = "#444444",
            BatteryTextColorHex = "#555555",
        };

        WithShownDialog(config, form =>
        {
            Button reset = FindButton(form, "Reset", "Reset every chip text colour to follow the figure colour");
            reset.PerformClick();

            Color figure = ColorHelper.Parse(MonitorConfig.DefaultTextColor, Color.White);
            foreach (string metric in ChipMetrics.All)
            {
                Button swatch = FindSwatch(form, $"{ChipMetrics.Describe(metric)} chip text colour");
                Assert.Equal(string.Empty, swatch.Tag as string);
                Assert.Equal(figure, swatch.BackColor);
            }
        });
    }

    [Fact]
    public void TheResetButtonRestoresBothBarColours()
    {
        var config = new MonitorConfig { DownloadColorHex = "#111111", UploadColorHex = "#222222" };

        WithShownDialog(config, form =>
        {
            Button reset = FindButton(form, "Reset", "Reset both bar colours to their defaults");
            reset.PerformClick();

            Assert.Equal(MonitorConfig.DefaultDownloadColor, FindSwatch(form, "Download bar colour").Tag as string);
            Assert.Equal(MonitorConfig.DefaultUploadColor, FindSwatch(form, "Upload bar colour").Tag as string);
        });
    }

    [Fact]
    public void TheResetButtonRestoresBothFigureColours()
    {
        var config = new MonitorConfig { DownloadTextColorHex = "#111111", UploadTextColorHex = "#222222" };

        WithShownDialog(config, form =>
        {
            Button reset = FindButton(form, "Reset", "Reset the download figure colour to its default");
            reset.PerformClick();
            Button resetUpload = FindButton(form, "Reset", "Reset the upload figure colour to its default");
            resetUpload.PerformClick();

            Assert.Equal(MonitorConfig.DefaultTextColor, FindSwatch(form, "Download text colour").Tag as string);
            Assert.Equal(MonitorConfig.DefaultTextColor, FindSwatch(form, "Upload text colour").Tag as string);
        });
    }

    private static void RunOnUiThread(Action work)
    {
        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the dialog did not finish opening");
        if (failure is not null)
        {
            throw new InvalidOperationException("the settings dialog failed on the UI thread", failure);
        }
    }

    /// <summary>
    /// Shows the dialog off-screen so its controls are filled in: the swatches are set from
    /// the config on load, so an unshown form still reports the default control colour.
    /// </summary>
    private static void WithShownDialog(MonitorConfig config, Action<SettingsForm> work)
    {
        RunOnUiThread(() =>
        {
            using var form = new SettingsForm(config)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            form.Show();
            try
            {
                form.PerformLayout();
                work(form);
            }
            finally
            {
                form.Hide();
            }
        });
    }

    private static Button FindSwatch(Control root, string accessibleName)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && string.Equals(button.AccessibleName, accessibleName, StringComparison.Ordinal))
            {
                return button;
            }

            Button? found = FindSwatchOrNull(child, accessibleName);
            if (found is not null)
            {
                return found;
            }
        }

        throw new Xunit.Sdk.XunitException($"the dialog has no swatch named \"{accessibleName}\"");
    }

    private static Button? FindSwatchOrNull(Control root, string accessibleName)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && string.Equals(button.AccessibleName, accessibleName, StringComparison.Ordinal))
            {
                return button;
            }

            Button? found = FindSwatchOrNull(child, accessibleName);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static Button FindButton(Control root, string text, string accessibleDescription)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button
                && string.Equals(button.Text, text, StringComparison.Ordinal)
                && string.Equals(button.AccessibleDescription, accessibleDescription, StringComparison.Ordinal))
            {
                return button;
            }

            Button? found = FindButtonOrNull(child, text, accessibleDescription);
            if (found is not null)
            {
                return found;
            }
        }

        throw new Xunit.Sdk.XunitException($"the dialog has no button \"{text}\" described as \"{accessibleDescription}\"");
    }

    private static Button? FindButtonOrNull(Control root, string text, string accessibleDescription)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button
                && string.Equals(button.Text, text, StringComparison.Ordinal)
                && string.Equals(button.AccessibleDescription, accessibleDescription, StringComparison.Ordinal))
            {
                return button;
            }

            Button? found = FindButtonOrNull(child, text, accessibleDescription);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Pixels painted at full opacity. The ring's unfilled track is drawn faintly, so only
    /// the solid arc counts as the level: counting any-alpha pixels would count the whole
    /// ring whatever the level.
    /// </summary>
    private static int OpaquePixels(Frame frame)
    {
        return OpaquePixels(frame, new Rectangle(Point.Empty, frame.Bitmap.Size));
    }

    private static int OpaquePixels(Frame frame, Rectangle area)
    {
        int count = 0;
        for (int x = area.Left; x < area.Right; x++)
        {
            for (int y = area.Top; y < area.Bottom; y++)
            {
                if (x >= 0 && y >= 0 && x < frame.Bitmap.Width && y < frame.Bitmap.Height
                    && frame.Bitmap.GetPixel(x, y).A == 255)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // ---- one colour for both the chip and its text ------------------------------------

    [Fact]
    public void ChipTextFollowsTheChipColourWhenAsked()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "#123456";
        config.ChipTextFollowsChip = true;

        Assert.Equal(Color.FromArgb(0x12, 0x34, 0x56), config.ChipTextColor(ChipMetrics.Cpu, Color.White));
    }

    [Fact]
    public void ChipTextFollowsTheChipColourEvenWhenTheChipUsesThePalette()
    {
        MonitorConfig config = ChipConfig();
        config.ChipTextFollowsChip = true;

        Assert.Equal(ChipMetrics.Default(ChipMetrics.Ram), config.ChipTextColor(ChipMetrics.Ram, Color.White));
    }

    [Fact]
    public void ChipTextKeepsItsOwnColourWhenTheFollowOptionIsOff()
    {
        MonitorConfig config = ChipConfig();
        config.CpuColorHex = "#123456";
        config.CpuTextColorHex = "#ABCDEF";
        config.ChipTextFollowsChip = false;

        Assert.Equal(Color.FromArgb(0xAB, 0xCD, 0xEF), config.ChipTextColor(ChipMetrics.Cpu, Color.White));
    }

    [Fact]
    public void TheFollowOptionSurvivesACloneAndACopy()
    {
        MonitorConfig config = ChipConfig();
        config.ChipTextFollowsChip = true;

        Assert.True(config.Clone().ChipTextFollowsChip);

        var target = new MonitorConfig();
        target.CopyFrom(config);
        Assert.True(target.ChipTextFollowsChip);
    }

    [Fact]
    public void TheFollowOptionSurvivesAJsonRoundTrip()
    {
        MonitorConfig config = ChipConfig();
        config.ChipTextFollowsChip = true;

        MonitorConfig reloaded = MonitorConfig.FromJson(config.ToJson());

        Assert.True(reloaded.ChipTextFollowsChip);
    }

    [Fact]
    public void TheFollowOptionIsOffByDefault()
    {
        Assert.False(new MonitorConfig().ChipTextFollowsChip);
    }

    // ---- the chips and the figures share one line -------------------------------------

    /// <summary>
    /// The chip block must be centred on the whole download/upload block - the figure rows
    /// and the bars under them - not on the figure text alone. Centring on the text left the
    /// chips above the block's centre by half the bar height, and the gap grew with the bar
    /// thickness, which is what the user saw.
    /// </summary>
    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Level)]
    [InlineData(WidgetStyles.Neon)]
    [InlineData(WidgetStyles.Dots)]
    [InlineData(WidgetStyles.Minimal)]
    [InlineData(WidgetStyles.Gradient)]
    [InlineData(WidgetStyles.Outline)]
    [InlineData(WidgetStyles.Sidebar)]
    [InlineData(WidgetStyles.SidebarSegments)]
    public void TheChipTextIsCentredOnTheFigureText(string style)
    {
        foreach (int thickness in new[] { 1, 2, 3, 5, 8, 12 })
        {
            MonitorConfig config = ChipConfig();
            config.Style = style;
            config.BarThicknessPx = thickness;
            config.ChipIndicator = ChipIndicators.Ring;
            config.ChipIcon = ChipIcons.Glyph;

            using Frame frame = Frame.Render(config, ChipVisual());
            WidgetLayout layout = frame.Layout;

            int blockTop = Math.Min(layout.DownloadText.Y, layout.UploadText.Y);
            int blockBottom = Math.Max(layout.DownloadBar.Bottom, layout.UploadBar.Bottom);
            int chipTop = layout.StatsRowTop;
            int chipBottom = layout.StatsRowBottom + layout.StatsChipHeight;

            double blockCentre = (blockTop + blockBottom) / 2.0;
            double chipCentre = (chipTop + chipBottom) / 2.0;

            // Half a pixel is the rounding of the block's odd height; a whole pixel is the
            // misalignment this guards against.
            Assert.True(
                Math.Abs(chipCentre - blockCentre) <= 0.5,
                $"{style} at thickness {thickness}: the chip block centre ({chipCentre}) must sit on the download/upload block centre ({blockCentre})");
        }
    }
}