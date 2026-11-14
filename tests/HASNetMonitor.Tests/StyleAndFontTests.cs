using System.Drawing;
using System.Text.Json;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The look options: drawing style, stacking order, font face/weight/slant, rounded
/// caps and glow. Each test ends with <see cref="AssertConsistent"/>, which is the
/// one checker for every style: the frame matches its layout, nothing is painted
/// outside a track, the fill is exactly what the ratio asks for, and the same state
/// always draws the same picture.
/// </summary>
public sealed class StyleAndFontTests
{
    private static readonly Color DownloadFill = Color.FromArgb(0, 255, 0);

    private static readonly Color UploadFill = Color.FromArgb(255, 0, 0);

    private static readonly Color TextInk = Color.FromArgb(255, 255, 255, 255);

    private static readonly WidgetVisual HalfAndQuarter = new(
        DownloadBytesPerSecond: 1024 * 1024,
        UploadBytesPerSecond: 256 * 1024,
        DownloadRatio: 0.5,
        UploadRatio: 0.25);

    private static MonitorConfig Config(string style, string order = StackOrder.DownloadFirst)
    {
        MonitorConfig config = TestSupport.BarsOnly().Clone();
        config.Style = style;
        config.SortOrder = order;
        return config.Normalize();
    }

    // ---- defaults: the old look must not move ------------------------------

    [Fact]
    public void DefaultsKeepTheClassicLook()
    {
        var config = new MonitorConfig().Normalize();

        Assert.Equal(WidgetStyles.Bars, config.Style);
        Assert.Equal(StackOrder.DownloadFirst, config.SortOrder);
        Assert.Equal(FontCatalog.Automatic, config.FontFamily);
        Assert.False(config.FontBold);
        Assert.False(config.FontItalic);
        Assert.False(config.RoundedBars);
        Assert.False(config.AccentGlow);

        // Same picture as an explicit "classic" config, and the same frame as before
        // the options existed: download row above the upload row.
        using Frame implicitDefault = Frame.Render(TestSupport.BarsOnly(), HalfAndQuarter);
        using Frame explicitBars = Frame.Render(Config(WidgetStyles.Bars), HalfAndQuarter);

        Assert.Equal(explicitBars.Layout.DownloadBar, implicitDefault.Layout.DownloadBar);
        Assert.Equal(explicitBars.Layout.UploadBar, implicitDefault.Layout.UploadBar);
        Assert.True(implicitDefault.Layout.DownloadBar.Y < implicitDefault.Layout.UploadBar.Y);
        AssertConsistent(implicitDefault, HalfAndQuarter);
    }

    [Fact]
    public void UnknownStyleFallsBackToTheClassicLook()
    {
        Assert.Equal(WidgetStyles.Bars, WidgetStyles.Normalize("Hologram"));
        Assert.Equal(WidgetStyles.Level, WidgetStyles.Normalize("  level "));
        Assert.Equal(WidgetStyles.Bars, WidgetStyles.Normalize(null));
        Assert.Equal(StackOrder.DownloadFirst, StackOrder.Normalize("sideways"));

        MonitorConfig config = TestSupport.BarsOnly().Clone();
        config.Style = "Hologram";
        config.SortOrder = "sideways";
        config.Normalize();

        using Frame frame = Frame.Render(config, HalfAndQuarter);
        using Frame defaultFrame = Frame.Render(TestSupport.BarsOnly(), HalfAndQuarter);
        Assert.Equal(defaultFrame.Layout.Size, frame.Layout.Size);
        AssertConsistent(frame, HalfAndQuarter);
    }

    // ---- stacking order: upload on top, download at the bottom -------------

    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Level)]
    [InlineData(WidgetStyles.Neon)]
    public void UploadFirstPutsUploadOnTopAndDownloadAtTheBottom(string style)
    {
        using Frame frame = Frame.Render(Config(style, StackOrder.UploadFirst), HalfAndQuarter);

        Assert.True(
            frame.Layout.UploadBar.Y < frame.Layout.DownloadBar.Y,
            "upload must be above download when the user asks for it");
        Assert.True(frame.Layout.UploadText.Y < frame.Layout.DownloadText.Y);
        AssertConsistent(frame, HalfAndQuarter);
    }

    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Level)]
    [InlineData(WidgetStyles.Neon)]
    public void ReorderingOnlySwapsTheRows(string style)
    {
        using Frame down = Frame.Render(Config(style), HalfAndQuarter);
        using Frame up = Frame.Render(Config(style, StackOrder.UploadFirst), HalfAndQuarter);

        // Same footprint, same tracks; only which row is which changes.
        Assert.Equal(down.Layout.Size, up.Layout.Size);
        Assert.Equal(down.Layout.DownloadBar, up.Layout.UploadBar);
        Assert.Equal(down.Layout.UploadBar, up.Layout.DownloadBar);
        Assert.Equal(down.Layout.DownloadText, up.Layout.UploadText);

        // The download bar still shows download traffic: the colours travel with the row.
        AssertFill(up, up.Layout.DownloadBar, HalfAndQuarter.DownloadRatio, DownloadFill, IsAntiAliased(up));
    }

    // ---- vertical side bars -----------------------------------------------

    [Fact]
    public void SidebarDrawsVerticalBarsBesideTheText()
    {
        MonitorConfig config = Config(WidgetStyles.Sidebar);
        config.ShowText = true;
        config.Normalize();

        using Frame frame = Frame.Render(config, HalfAndQuarter);
        WidgetLayout layout = frame.Layout;

        Assert.True(layout.DownloadBar.Height > layout.DownloadBar.Width, "side bars are vertical");
        Assert.True(layout.UploadBar.Height > layout.UploadBar.Width);
        Assert.True(layout.DownloadBar.Right <= layout.UploadBar.Left, "download column comes first");
        Assert.True(layout.DownloadText.Right <= layout.DownloadBar.Left, "text sits left of the bars");
        Assert.True(layout.UploadText.Right <= layout.DownloadBar.Left);
        Assert.True(layout.UploadText.Y > layout.DownloadText.Y);
        AssertConsistent(frame, HalfAndQuarter);
    }

    [Fact]
    public void SidebarUploadFirstMirrorsTheColumns()
    {
        using Frame frame = Frame.Render(Config(WidgetStyles.Sidebar, StackOrder.UploadFirst), HalfAndQuarter);

        Assert.True(frame.Layout.UploadBar.Right <= frame.Layout.DownloadBar.Left);
        AssertConsistent(frame, HalfAndQuarter);
    }

    [Fact]
    public void SideBarFillsGrowAwayFromTheTaskbarEdge()
    {
        using Frame frame = Frame.Render(Config(WidgetStyles.Sidebar), HalfAndQuarter);

        int expected = WidgetRenderer.FillWidth(frame.Layout.DownloadBar.Height, HalfAndQuarter.DownloadRatio);
        Assert.Equal(expected, CountVerticalFill(frame, frame.Layout.DownloadBar, DownloadFill));

        // The filled part touches the bottom edge, the empty part is at the top.
        int x = frame.Layout.DownloadBar.Left + (frame.Layout.DownloadBar.Width / 2);
        Assert.True(Pixels.IsOpaque(frame.Bitmap.GetPixel(x, frame.Layout.DownloadBar.Bottom - 1), DownloadFill));
        Assert.False(Pixels.IsOpaque(frame.Bitmap.GetPixel(x, frame.Layout.DownloadBar.Top), DownloadFill));
        AssertConsistent(frame, HalfAndQuarter);
    }

    [Fact]
    public void SidebarWithoutTextUsesTheBarWidthOnly()
    {
        using Frame withText = Frame.Render(Config(WidgetStyles.Sidebar), HalfAndQuarter);
        MonitorConfig noText = Config(WidgetStyles.Sidebar);
        noText.ShowText = true;
        noText.Normalize();
        using Frame widest = Frame.Render(noText, HalfAndQuarter);

        Assert.True(withText.Layout.Size.Width < widest.Layout.Size.Width);
        AssertConsistent(withText, HalfAndQuarter);
    }

    // ---- level meter --------------------------------------------------------

    [Fact]
    public void LevelMeterDrawsDiscreteBlocks()
    {
        using Frame frame = Frame.Render(Config(WidgetStyles.Level), HalfAndQuarter);
        Rectangle track = frame.Layout.DownloadBar;
        int thickness = frame.Layout.BarThickness;

        Rectangle[] segments = WidgetRenderer.LevelSegments(track, thickness);
        Assert.True(segments.Length > 1, "a level meter needs more than one block");

        int lit = WidgetRenderer.LevelLitSegments(segments.Length, HalfAndQuarter.DownloadRatio);
        Assert.InRange(lit, 1, segments.Length);

        // Painted fill is exactly the lit blocks.
        int expected = segments.Take(lit).Sum(segment => segment.Width);
        Assert.Equal(expected, frame.CountFillPixels(track, DownloadFill));

        // The unlit blocks are the track, and the gaps between blocks stay clear.
        int y = frame.RowCentre(track);
        for (int i = lit; i < segments.Length; i++)
        {
            Assert.False(Pixels.IsOpaque(frame.Bitmap.GetPixel(segments[i].Left, y), DownloadFill));
        }

        for (int i = 1; i < segments.Length; i++)
        {
            Assert.False(Pixels.IsOpaque(frame.Bitmap.GetPixel(segments[i].Left - 1, y), DownloadFill));
        }

        AssertConsistent(frame, HalfAndQuarter);
    }

    [Fact]
    public void LevelMeterLitBlocksFollowTheRatio()
    {
        MonitorConfig config = Config(WidgetStyles.Level);
        int count = WidgetRenderer.LevelSegmentCount(
            new WidgetRenderer().ComputeLayout(config, 1f, 48).DownloadBar.Width,
            config.BarThicknessPx);

        Assert.Equal(0, WidgetRenderer.LevelLitSegments(count, 0));
        Assert.Equal(1, WidgetRenderer.LevelLitSegments(count, 0.0001));
        Assert.Equal(count, WidgetRenderer.LevelLitSegments(count, 1));
        Assert.Equal(count, WidgetRenderer.LevelLitSegments(count, 5));

        int previous = 0;
        foreach (double ratio in new[] { 0.05, 0.2, 0.4, 0.6, 0.8, 1.0 })
        {
            int lit = WidgetRenderer.LevelLitSegments(count, ratio);
            Assert.True(lit >= previous, "more traffic can never light fewer blocks");
            Assert.InRange(lit, 0, count);
            previous = lit;
        }

        // Nothing lit means no traffic colour at all.
        using Frame idle = Frame.Render(config, new WidgetVisual(0, 0, 0, 0));
        Assert.Equal(0, idle.CountFillPixels(idle.Layout.DownloadBar, DownloadFill));
        AssertConsistent(idle, WidgetVisual.Empty);
    }

    // ---- neon and rounded caps ---------------------------------------------

    [Fact]
    public void NeonKeepsTheBarGeometryAndAddsAGlow()
    {
        using Frame neon = Frame.Render(Config(WidgetStyles.Neon), HalfAndQuarter);
        using Frame bars = Frame.Render(Config(WidgetStyles.Bars), HalfAndQuarter);

        Assert.Equal(bars.Layout.DownloadBar, neon.Layout.DownloadBar);
        Assert.Equal(bars.Layout.UploadBar, neon.Layout.UploadBar);

        // The core of the fill is still the configured colour.
        Assert.True(Pixels.IsOpaque(neon.BarPixel(neon.Layout.DownloadBar, neon.Layout.DownloadBar.Left + 1), DownloadFill));

        // And there is a halo: partly transparent pixels outside the solid fill.
        Assert.True(CountHaloPixels(neon, neon.Layout.DownloadBar, DownloadFill) > 0, "the glow must be visible");
        Assert.Equal(0, CountHaloPixels(bars, bars.Layout.DownloadBar, DownloadFill));
        AssertConsistent(neon, HalfAndQuarter);
    }

    [Fact]
    public void RoundedCapsCutTheCorners()
    {
        MonitorConfig rounded = Config(WidgetStyles.Neon);
        rounded.RoundedBars = true;
        rounded.Normalize();

        using Frame frame = Frame.Render(rounded, new WidgetVisual(0, 0, 1, 1));
        Rectangle bar = frame.Layout.DownloadBar;

        Assert.False(
            Pixels.IsOpaque(frame.Bitmap.GetPixel(bar.Left, bar.Top), DownloadFill),
            "a rounded cap leaves the corner unpainted");
        Assert.True(
            Pixels.IsOpaque(frame.Bitmap.GetPixel(bar.Left + (bar.Width / 2), frame.RowCentre(bar)), DownloadFill));
        AssertConsistent(frame, new WidgetVisual(0, 0, 1, 1));
    }

    [Fact]
    public void GlowAndRoundedCapsAlsoApplyToTheClassicStyle()
    {
        MonitorConfig fancy = Config(WidgetStyles.Bars);
        fancy.AccentGlow = true;
        fancy.RoundedBars = true;
        fancy.Normalize();

        using Frame frame = Frame.Render(fancy, HalfAndQuarter);
        Assert.True(CountHaloPixels(frame, frame.Layout.DownloadBar, DownloadFill) > 0);
        AssertConsistent(frame, HalfAndQuarter);
    }

    // ---- fonts --------------------------------------------------------------

    [Fact]
    public void FontFallbackNeverAsksForAMissingFamily()
    {
        Assert.Equal(FontCatalog.Automatic, FontCatalog.Normalize(null));
        Assert.Equal(FontCatalog.Automatic, FontCatalog.Normalize("   "));
        Assert.Equal(FontCatalog.Automatic, FontCatalog.Normalize("Definitely Not A Font 123"));

        // A real family survives, and is never resolved to the bogus name.
        string installed = FontCatalog.InstalledFamilies[0];
        Assert.Equal(installed, FontCatalog.Normalize(installed));
        Assert.Equal(installed, FontCatalog.Resolve(installed));

        string automatic = FontCatalog.Resolve(FontCatalog.Automatic);
        Assert.True(FontCatalog.IsInstalled(automatic), "the automatic font must exist on this machine");
        Assert.Equal(automatic, FontCatalog.Resolve("Definitely Not A Font 123"));
    }

    [Fact]
    public void BoldAndItalicAreRealStyles()
    {
        Assert.Equal(FontStyle.Regular, FontCatalog.StyleFor(false, false));
        Assert.Equal(FontStyle.Bold, FontCatalog.StyleFor(true, false));
        Assert.Equal(FontStyle.Italic, FontCatalog.StyleFor(false, true));
        Assert.Equal(FontStyle.Bold | FontStyle.Italic, FontCatalog.StyleFor(true, true));
    }

    [Fact]
    public void BoldChangesTheDrawnText()
    {
        using Frame regular = Frame.Render(TextConfig(false, false), HalfAndQuarter);
        using Frame bold = Frame.Render(TextConfig(true, false), HalfAndQuarter);

        Assert.False(
            SamePicture(regular, bold),
            "asking for bold must actually change the pixels");
        Assert.True(CountTextInk(bold, bold.Layout.DownloadText) >= CountTextInk(regular, regular.Layout.DownloadText));
        AssertConsistent(regular, HalfAndQuarter);
        AssertConsistent(bold, HalfAndQuarter);
    }

    [Fact]
    public void ItalicChangesTheDrawnText()
    {
        using Frame regular = Frame.Render(TextConfig(false, false), HalfAndQuarter);
        using Frame italic = Frame.Render(TextConfig(false, true), HalfAndQuarter);

        Assert.False(SamePicture(regular, italic), "asking for italic must actually change the pixels");
        AssertConsistent(italic, HalfAndQuarter);
    }

    [Fact]
    public void AChosenFamilyIsUsedAndAnUnknownOneStillRenders()
    {
        MonitorConfig config = TextConfig(false, false);
        config.FontFamily = FontCatalog.InstalledFamilies[0];
        config.Normalize();

        Assert.Equal(config.FontFamily, FontCatalog.Resolve(config.FontFamily));

        using Frame chosen = Frame.Render(config, HalfAndQuarter);
        AssertConsistent(chosen, HalfAndQuarter);

        MonitorConfig bogus = TextConfig(false, false);
        bogus.FontFamily = "Definitely Not A Font 123";
        bogus.Normalize();
        Assert.Equal(FontCatalog.Automatic, bogus.FontFamily);

        using Frame fallback = Frame.Render(bogus, HalfAndQuarter);
        AssertConsistent(fallback, HalfAndQuarter);
    }

    // ---- every style survives every reasonable setting ----------------------

    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Level)]
    [InlineData(WidgetStyles.Sidebar)]
    [InlineData(WidgetStyles.Neon)]
    public void EveryStyleFitsTheTaskbarInEveryCombination(string style)
    {
        foreach (string order in StackOrder.All)
        {
            foreach (bool showText in new[] { true, false })
            {
                foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                {
                    MonitorConfig config = Config(style, order);
                    config.ShowText = showText;
                    config.Normalize();

                    using Frame frame = Frame.Render(config, HalfAndQuarter, scale: scale, maxHeight: (int)Math.Round(48 * scale));

                    Assert.True(frame.Layout.FitsBounds, $"{style}/{order}/text={showText}/scale={scale} is too tall");
                    Assert.True(frame.Layout.Size.Height <= frame.Layout.MaxHeight);
                    AssertConsistent(frame, HalfAndQuarter);
                }
            }
        }
    }

    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Level)]
    [InlineData(WidgetStyles.Sidebar)]
    [InlineData(WidgetStyles.Neon)]
    public void EveryStyleRotatesOnAVerticalTaskbar(string style)
    {
        MonitorConfig config = Config(style);
        config.ShowText = true;
        config.Normalize();

        using Frame frame = Frame.Render(config, HalfAndQuarter, orientation: TaskbarOrientation.Vertical);

        Assert.Equal(new Size(frame.Layout.Size.Height, frame.Layout.Size.Width), frame.Bitmap.Size);
        Assert.True(frame.Layout.DownloadBar.Width > 0 && frame.Layout.DownloadBar.Height > 0);
        Assert.True(frame.Layout.FitsBounds);
    }

    [Fact]
    public void ATinyTaskbarNeverClaimsAFitItCannotDeliver()
    {
        // A 12px taskbar cannot hold two rows of text: the layout must stop shrinking at
        // the minimum font and say it does not fit, so the widget hides instead of
        // drawing out of bounds. Bars with no text still has to fit.
        foreach (string style in WidgetStyles.All)
        {
            MonitorConfig config = Config(style);
            WidgetLayout layout = new WidgetRenderer().ComputeLayout(config, 1f, 12);

            Assert.True(layout.Size.Width > 0);
            Assert.True(layout.Size.Height > 0);
            Assert.Equal(layout.Size.Height <= 12, layout.FitsBounds);

            MonitorConfig bare = Config(style);
            bare.ShowText = false;
            bare.Normalize();
            WidgetLayout bareLayout = new WidgetRenderer().ComputeLayout(bare, 1f, 12);
            if (style == WidgetStyles.Sidebar)
            {
                // Without text the side bars need no text rows, so they do fit.
                Assert.True(bareLayout.FitsBounds, "side bars without text must fit a 12px taskbar");
            }
            else
            {
                // Row styles always reserve their two text rows (that is the existing
                // look); they must say so rather than claim a fit they cannot deliver.
                Assert.Equal(bareLayout.Size.Height <= 12, bareLayout.FitsBounds);
            }
        }
    }

    // ---- persistence --------------------------------------------------------

    [Fact]
    public void TheNewOptionsSurviveARoundTrip()
    {
        MonitorConfig config = TestSupport.BarsOnly().Clone();
        config.Style = WidgetStyles.Sidebar;
        config.SortOrder = StackOrder.UploadFirst;
        config.FontFamily = FontCatalog.InstalledFamilies[0];
        config.FontBold = true;
        config.FontItalic = true;
        config.RoundedBars = true;
        config.AccentGlow = true;

        string json = JsonSerializer.Serialize(config);
        MonitorConfig? back = JsonSerializer.Deserialize<MonitorConfig>(json);

        Assert.NotNull(back);
        Assert.Equal(WidgetStyles.Sidebar, back!.Style);
        Assert.Equal(StackOrder.UploadFirst, back.SortOrder);
        Assert.Equal(config.FontFamily, back.FontFamily);
        Assert.True(back.FontBold);
        Assert.True(back.FontItalic);
        Assert.True(back.RoundedBars);
        Assert.True(back.AccentGlow);

        // A file written before these options existed still loads with the classic look.
        MonitorConfig? legacy = JsonSerializer.Deserialize<MonitorConfig>(
            """{"DownloadColorHex":"#00FF00","UploadColorHex":"#FF0000","ShowText":false}""");
        Assert.NotNull(legacy);
        legacy!.Normalize();
        Assert.Equal(WidgetStyles.Bars, legacy.Style);
        Assert.Equal(StackOrder.DownloadFirst, legacy.SortOrder);
        Assert.Equal(FontCatalog.Automatic, legacy.FontFamily);
    }

    [Fact]
    public void CloneCopiesTheNewOptions()
    {
        MonitorConfig config = Config(WidgetStyles.Level, StackOrder.UploadFirst);
        config.FontBold = true;
        config.FontItalic = true;
        config.RoundedBars = true;
        config.AccentGlow = true;

        MonitorConfig copy = config.Clone();

        Assert.Equal(config.Style, copy.Style);
        Assert.Equal(config.SortOrder, copy.SortOrder);
        Assert.Equal(config.FontBold, copy.FontBold);
        Assert.Equal(config.FontItalic, copy.FontItalic);
        Assert.Equal(config.RoundedBars, copy.RoundedBars);
        Assert.Equal(config.AccentGlow, copy.AccentGlow);
    }

    // ---- the one checker ----------------------------------------------------

    /// <summary>
    /// The rules that must hold for every frame, whatever the style: the picture is
    /// the layout, nothing escapes a track, the fill matches the ratio exactly, the
    /// background stays transparent, and the same state draws the same pixels.
    /// </summary>
    private static void AssertConsistent(Frame frame, WidgetVisual visual)
    {
        Assert.Equal(frame.Layout.Size, frame.Bitmap.Size);

        foreach (Rectangle bar in new[] { frame.Layout.DownloadBar, frame.Layout.UploadBar })
        {
            Assert.True(bar.Left >= 0 && bar.Right <= frame.Bitmap.Width, $"{bar} escapes the frame horizontally");
            Assert.True(bar.Top >= 0 && bar.Bottom <= frame.Bitmap.Height, $"{bar} escapes the frame vertically");
        }

        if (!frame.Config.ShowBackdrop)
        {
            Assert.True(
                frame.Bitmap.GetPixel(0, 0).A <= WidgetRenderer.HitWashAlpha + 1,
                "the widget must stay see-through over the taskbar");
        }

        bool antiAliased = IsAntiAliased(frame);

        AssertFill(frame, frame.Layout.DownloadBar, visual.DownloadRatio, DownloadFill, antiAliased);
        AssertFill(frame, frame.Layout.UploadBar, visual.UploadRatio, UploadFill, antiAliased);

        using Frame again = frame.Rerender();
        Assert.Equal(frame.Layout.Size, again.Layout.Size);
        for (int x = 0; x < frame.Bitmap.Width; x += 3)
        {
            for (int y = 0; y < frame.Bitmap.Height; y += 2)
            {
                Assert.Equal(frame.Bitmap.GetPixel(x, y).ToArgb(), again.Bitmap.GetPixel(x, y).ToArgb());
            }
        }
    }

    /// <summary>The fill of one track must be exactly what its ratio asks for.</summary>
    private static void AssertFill(Frame frame, Rectangle bar, double ratio, Color fill, bool antiAliased)
    {
        int expected = ExpectedFillPixels(frame, bar, ratio);
        int actual = CountSolid(frame, bar);

        if (antiAliased)
        {
            // Rounded caps and glows are anti-aliased: the last pixel is a blend.
            Assert.InRange(actual, Math.Max(0, expected - 1), expected);
        }
        else
        {
            Assert.Equal(expected, actual);
        }

        if (expected > 0 && frame.Layout.Style != WidgetStyles.Sidebar)
        {
            // Probe inside the filled part (the bar's middle is the edge at ratio 0.5).
            int probeX = bar.Left + Math.Max(0, (expected - 1) / 2);
            Assert.True(
                Pixels.IsOpaque(frame.Bitmap.GetPixel(probeX, frame.RowCentre(bar)), fill),
                "the filled part must be the solid configured colour");
        }
    }

    /// <summary>How many pixels the ratio should fill, per style.</summary>
    private static int ExpectedFillPixels(Frame frame, Rectangle bar, double ratio)
    {
        if (frame.Layout.Style == WidgetStyles.Sidebar)
        {
            return WidgetRenderer.FillWidth(bar.Height, ratio);
        }

        if (frame.Layout.Style == WidgetStyles.Level)
        {
            Rectangle[] segments = WidgetRenderer.LevelSegments(bar, frame.Layout.BarThickness);
            int lit = WidgetRenderer.LevelLitSegments(segments.Length, ratio);
            return segments.Take(lit).Sum(segment => segment.Width);
        }

        return WidgetRenderer.FillWidth(bar.Width, ratio);
    }

    /// <summary>Full-opacity pixels along a track: the solid fill, never the track or glow.</summary>
    private static int CountSolid(Frame frame, Rectangle bar)
    {
        int count = 0;

        if (frame.Layout.Style == WidgetStyles.Sidebar)
        {
            int x = bar.Left + (bar.Width / 2);
            for (int y = bar.Top; y < bar.Bottom; y++)
            {
                count += frame.Bitmap.GetPixel(x, y).A == 255 ? 1 : 0;
            }

            return count;
        }

        int row = frame.RowCentre(bar);
        for (int x = bar.Left; x < bar.Right; x++)
        {
            count += frame.Bitmap.GetPixel(x, row).A == 255 ? 1 : 0;
        }

        return count;
    }

    private static bool IsAntiAliased(Frame frame)
        => frame.Layout.Style == WidgetStyles.Neon || frame.Config.RoundedBars || frame.Config.AccentGlow;

    private static MonitorConfig TextConfig(bool bold, bool italic)
    {
        MonitorConfig config = TestSupport.BarsOnly().Clone();
        config.ShowText = true;
        config.FontSizePt = 10;
        config.FontBold = bold;
        config.FontItalic = italic;
        return config.Normalize();
    }

    private static int CountVerticalFill(Frame frame, Rectangle bar, Color fill)
    {
        int x = bar.Left + (bar.Width / 2);
        int count = 0;
        for (int y = bar.Top; y < bar.Bottom; y++)
        {
            count += Pixels.IsOpaque(frame.Bitmap.GetPixel(x, y), fill) ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Partly transparent pixels tinted towards the fill colour: the halo of a glow.
    /// The unlit track is drawn too, but it is neutral grey, so the tint separates them.
    /// </summary>
    private static int CountHaloPixels(Frame frame, Rectangle bar, Color fill)
    {
        int row = frame.RowCentre(bar);
        int count = 0;
        int from = Math.Max(0, bar.Left - 12);
        int to = Math.Min(frame.Bitmap.Width, bar.Right + 12);
        for (int x = from; x < to; x++)
        {
            Color pixel = frame.Bitmap.GetPixel(x, row);
            if (pixel.A <= WidgetRenderer.HitWashAlpha + 1 || pixel.A >= 255)
            {
                continue;
            }

            int lead = Math.Max(fill.R, Math.Max(fill.G, fill.B));
            int dominant = lead == fill.G ? pixel.G : lead == fill.R ? pixel.R : pixel.B;
            int others = lead == fill.G
                ? Math.Max(pixel.R, pixel.B)
                : lead == fill.R
                    ? Math.Max(pixel.G, pixel.B)
                    : Math.Max(pixel.R, pixel.G);

            if (dominant - others > 20)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountTextInk(Frame frame, Rectangle bounds)
    {
        int count = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                count += Pixels.IsOpaque(frame.Bitmap.GetPixel(x, y), TextInk) ? 1 : 0;
            }
        }

        return count;
    }

    private static bool SamePicture(Frame left, Frame right)
    {
        if (left.Bitmap.Size != right.Bitmap.Size)
        {
            return false;
        }

        for (int x = 0; x < left.Bitmap.Width; x++)
        {
            for (int y = 0; y < left.Bitmap.Height; y++)
            {
                if (left.Bitmap.GetPixel(x, y).ToArgb() != right.Bitmap.GetPixel(x, y).ToArgb())
                {
                    return false;
                }
            }
        }

        return true;
    }
}
