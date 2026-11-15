using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The customisation surface: the arrow's shape, the bar designs, and the colour the
/// figure is drawn in. Each test renders a real frame and reads the pixels back, so a
/// setting that is stored but ignored fails here.
/// </summary>
public sealed class CustomizationTests
{
    private static readonly Color DownloadFill = Color.FromArgb(0, 255, 0);

    private static readonly Color UploadFill = Color.FromArgb(255, 0, 0);

    private static MonitorConfig TextConfig(string arrowStyle = ArrowStyles.Classic, int arrowPercent = MonitorConfig.DefaultArrowSizePercent)
        => new MonitorConfig
        {
            Style = WidgetStyles.Bars,
            DownloadColorHex = "#00FF00",
            UploadColorHex = "#FF0000",
            ShowText = true,
            ShowBackdrop = false,
            ArrowStyle = arrowStyle,
            ArrowSizePercent = arrowPercent,
        }.Normalize();

    // ---- arrow shapes ------------------------------------------------------------

    [Fact]
    public void EveryArrowStyleIsSelectableAndDescribed()
    {
        Assert.Contains(ArrowStyles.Classic, ArrowStyles.All);
        Assert.Contains(ArrowStyles.Solid, ArrowStyles.All);
        Assert.Contains(ArrowStyles.Small, ArrowStyles.All);
        Assert.Contains(ArrowStyles.Double, ArrowStyles.All);
        Assert.Contains(ArrowStyles.Chevron, ArrowStyles.All);
        Assert.Contains(ArrowStyles.Bar, ArrowStyles.All);
        Assert.Contains(ArrowStyles.None, ArrowStyles.All);

        var labels = new List<string>();
        foreach (string style in ArrowStyles.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ArrowStyles.Describe(style)), style);
            Assert.Equal(style, ArrowStyles.Normalize(style));
            Assert.Equal(style, ArrowStyles.Normalize(style.ToUpperInvariant()));
            Assert.DoesNotContain(ArrowStyles.Describe(style), labels);
            labels.Add(ArrowStyles.Describe(style));
        }

        // Only the drawn style is drawn and only the hidden style hides.
        Assert.True(ArrowStyles.IsDrawn(ArrowStyles.Bar));
        Assert.True(ArrowStyles.IsHidden(ArrowStyles.None));
        foreach (string style in ArrowStyles.All.Where(s => s != ArrowStyles.Bar))
        {
            Assert.False(ArrowStyles.IsDrawn(style), style);
        }

        foreach (string style in ArrowStyles.All.Where(s => s != ArrowStyles.None))
        {
            Assert.False(ArrowStyles.IsHidden(style), style);
        }

        // Unknown or missing settings fall back to the classic arrows rather than nothing.
        Assert.Equal(ArrowStyles.Classic, ArrowStyles.Normalize("Wobble"));
        Assert.Equal(ArrowStyles.Classic, ArrowStyles.Normalize(null));
        Assert.Equal(ArrowStyles.Classic, ArrowStyles.Normalize("   "));
    }

    [Fact]
    public void EachGlyphStyleUsesItsOwnArrowPair()
    {
        var seen = new List<(string Up, string Down)>();
        foreach (string style in ArrowStyles.All.Where(s => !ArrowStyles.IsDrawn(s) && !ArrowStyles.IsHidden(s)))
        {
            string? up = ArrowStyles.GlyphFor(style, isDownload: false);
            string? down = ArrowStyles.GlyphFor(style, isDownload: true);

            Assert.NotNull(up);
            Assert.NotNull(down);
            Assert.NotEqual(up, down);
            Assert.Single(up!);
            Assert.Single(down!);
            Assert.DoesNotContain((up!, down!), seen);
            seen.Add((up!, down!));

            // A real font resolves it to the same glyph, so no style silently degrades.
            Assert.Equal(up, WidgetRenderer.ResolveArrowGlyph("Segoe UI", style, isDownload: false));
            Assert.Equal(down, WidgetRenderer.ResolveArrowGlyph("Segoe UI", style, isDownload: true));
        }

        // The drawn and hidden styles have no glyph at all.
        Assert.Null(ArrowStyles.GlyphFor(ArrowStyles.Bar, isDownload: false));
        Assert.Null(ArrowStyles.GlyphFor(ArrowStyles.None, isDownload: true));
        Assert.Equal(string.Empty, WidgetRenderer.ResolveArrowGlyph("Segoe UI", ArrowStyles.Bar, isDownload: false));
    }

    [Theory]
    [InlineData(ArrowStyles.Classic)]
    [InlineData(ArrowStyles.Solid)]
    [InlineData(ArrowStyles.Small)]
    [InlineData(ArrowStyles.Double)]
    [InlineData(ArrowStyles.Chevron)]
    [InlineData(ArrowStyles.Bar)]
    public void EveryArrowStyleDrawsAMarkerBesideTheFigure(string style)
    {
        using Frame frame = Frame.Render(TextConfig(style), new WidgetVisual(1024, 1024, 1.0, 1.0));

        foreach (Rectangle row in new[] { frame.Layout.DownloadText, frame.Layout.UploadText })
        {
            Assert.True(ArrowInk(frame, row).Height > 0, $"{style}: no arrow ink in the row");
        }
    }

    [Theory]
    [InlineData(ArrowStyles.Classic, 300)]
    [InlineData(ArrowStyles.Solid, 300)]
    [InlineData(ArrowStyles.Small, 300)]
    [InlineData(ArrowStyles.Double, 300)]
    [InlineData(ArrowStyles.Chevron, 300)]
    [InlineData(ArrowStyles.Bar, 300)]
    [InlineData(ArrowStyles.None, 300)]
    public void TheLayoutReservesTheArrowItWillDraw(string style, int percent)
    {
        var config = TextConfig(style, percent);
        WidgetLayout layout = new WidgetRenderer().ComputeLayout(config, 1f, 48);

        using var surface = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(surface);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using Font font = WidgetRenderer.CreateFont(layout.FontEmPixels, config);
        using var format = new StringFormat(StringFormat.GenericTypographic);

        // What the row has to hold: the offset the drawing uses, plus the widest figure
        // the formatter can produce at that font size.
        float offset = WidgetRenderer.RowNumberOffset(
            graphics, font, format, layout.Scale, layout.ArrowSizePercent, layout.DownloadText.Height, style);

        float widest = 0;
        foreach (double sample in new[] { 0, 999, 1024, 99 * 1024, 888.8 * 1024 * 1024, 100.0 * 1024 * 1024 * 1024 })
        {
            widest = Math.Max(widest, graphics.MeasureString(SpeedFormatter.Format(sample), font, PointF.Empty, format).Width);
        }

        Assert.True(
            layout.DownloadText.Width >= offset + widest - 1,
            $"{style} at {percent}%: row is {layout.DownloadText.Width}px but needs {offset + widest:F1}px");
    }

    [Theory]
    [InlineData(ArrowStyles.Classic, 300)]
    [InlineData(ArrowStyles.Solid, 300)]
    [InlineData(ArrowStyles.Small, 300)]
    [InlineData(ArrowStyles.Double, 300)]
    [InlineData(ArrowStyles.Chevron, 300)]
    [InlineData(ArrowStyles.Bar, 300)]
    public void TheFigureNeverOverlapsTheArrowItWasSizedFor(string style, int percent)
    {
        using Frame frame = Frame.Render(TextConfig(style, percent), new WidgetVisual(888.8 * 1024 * 1024, 1024, 1.0, 1.0));
        Rectangle row = frame.Layout.DownloadText;

        Rectangle arrow = ArrowInk(frame, row);
        Assert.True(arrow.Width > 0, $"{style}: no arrow drawn");

        int figureLeft = int.MaxValue;
        for (int y = Math.Max(0, row.Top); y < Math.Min(frame.Bitmap.Height, row.Bottom); y++)
        {
            for (int x = Math.Max(0, row.Left); x < Math.Min(frame.Bitmap.Width, row.Right); x++)
            {
                Color pixel = frame.Bitmap.GetPixel(x, y);

                // The figure is the fully opaque ink that is not the marker's colour.
                if (pixel.A == 255 && !Pixels.CloseRgb(pixel, frame.Config.DownloadColor))
                {
                    figureLeft = Math.Min(figureLeft, x);
                }
            }
        }

        Assert.True(figureLeft < int.MaxValue, $"{style}: no figure drawn");
        Assert.True(
            figureLeft > arrow.Right - 1,
            $"{style} at {percent}%: figure starts at {figureLeft} but the arrow ends at {arrow.Right - 1}");
    }

    [Fact]
    public void NoArrowStyleDrawsNoMarkerAndGivesTheFigureTheSpaceBack()
    {
        using Frame withArrow = Frame.Render(TextConfig(ArrowStyles.Classic), new WidgetVisual(1024, 1024, 1.0, 1.0));
        using Frame without = Frame.Render(TextConfig(ArrowStyles.None), new WidgetVisual(1024, 1024, 1.0, 1.0));

        Rectangle row = withArrow.Layout.DownloadText;
        Assert.True(
            without.Bitmap.Width <= withArrow.Bitmap.Width,
            $"with={withArrow.Bitmap.Width} without={without.Bitmap.Width} " +
            $"inkWith={ArrowInk(withArrow, row)} inkWithout={ArrowInk(without, row)}");
        Assert.True(ArrowInk(without, row).Width < ArrowInk(withArrow, row).Width);

    }

    [Fact]
    public void SwitchingTheArrowChangesTheDrawing()
    {
        var signatures = new List<string>();
        foreach (string style in new[] { ArrowStyles.Classic, ArrowStyles.Solid, ArrowStyles.Bar })
        {
            using Frame frame = Frame.Render(TextConfig(style, 200), new WidgetVisual(1024, 1024, 1.0, 1.0));
            Rectangle row = frame.Layout.DownloadText;
            signatures.Add(Describe(frame, ArrowInk(frame, row)));
        }

        Assert.Equal(signatures.Count, signatures.Distinct().Count());
    }

    [Fact]
    public void TheArrowSizeSpinnerCoversTheWholeRange()
    {
        // The ratio the spinner shows is the percentage the renderer uses, and every tenth
        // in range is reachable: 0.5, 0.6, 1.0 and 2.0 are all steps.
        Assert.Equal(0.5m, MonitorConfig.MinArrowSizePercent / 100m);
        Assert.Equal(3.0m, MonitorConfig.MaxArrowSizePercent / 100m);
        Assert.Equal(0.1m, MonitorConfig.ArrowSizeStep / 100m);

        foreach (int percent in new[] { 50, 60, 100, 200, 300 })
        {
            Assert.Equal(0, (percent - MonitorConfig.MinArrowSizePercent) % MonitorConfig.ArrowSizeStep);
        }

        // A bigger setting draws a taller arrow; a smaller one draws a shorter arrow.
        Assert.True(DrawnArrowHeight(50) < DrawnArrowHeight(100), "0.5 must be shorter than 1.0");
        Assert.True(DrawnArrowHeight(100) < DrawnArrowHeight(200), "1.0 must be shorter than 2.0");
        Assert.True(DrawnArrowHeight(200) <= DrawnArrowHeight(300), "2.0 must not exceed 3.0");
    }

    // ---- bar designs -------------------------------------------------------------

    [Fact]
    public void TheNewBarDesignsAreSelectableAndDescribed()
    {
        Assert.Contains(WidgetStyles.Gradient, WidgetStyles.All);
        Assert.Contains(WidgetStyles.Outline, WidgetStyles.All);
        Assert.Equal(WidgetStyles.Gradient, WidgetStyles.Normalize(WidgetStyles.Gradient));
        Assert.Equal(WidgetStyles.Outline, WidgetStyles.Normalize(WidgetStyles.Outline));
        Assert.False(string.IsNullOrWhiteSpace(WidgetStyles.Describe(WidgetStyles.Gradient)));
        Assert.False(string.IsNullOrWhiteSpace(WidgetStyles.Describe(WidgetStyles.Outline)));
        Assert.NotEqual(WidgetStyles.Describe(WidgetStyles.Gradient), WidgetStyles.Describe(WidgetStyles.Outline));
        Assert.False(WidgetStyles.IsVertical(WidgetStyles.Gradient));
        Assert.False(WidgetStyles.IsVertical(WidgetStyles.Outline));
    }

    [Fact]
    public void EveryBarDesignKeepsTheSameGeometry()
    {
        // A design is paint only: switching it must not move the rows, change the window
        // size or lose text space.
        WidgetLayout Reference(string style)
        {
            var config = new MonitorConfig { Style = style }.Normalize();
            return new WidgetRenderer().ComputeLayout(config, 1f, 48);
        }

        WidgetLayout bars = Reference(WidgetStyles.Bars);
        foreach (string style in new[] { WidgetStyles.Gradient, WidgetStyles.Outline })
        {
            WidgetLayout other = Reference(style);
            Assert.Equal(bars.Size, other.Size);
            Assert.Equal(bars.DownloadBar, other.DownloadBar);
            Assert.Equal(bars.UploadBar, other.UploadBar);
            Assert.Equal(bars.DownloadText, other.DownloadText);
            Assert.Equal(bars.UploadText, other.UploadText);
        }
    }

    [Fact]
    public void TheGradientDesignFadesAlongTheFill()
    {
        var config = new MonitorConfig
        {
            Style = WidgetStyles.Gradient,
            DownloadColorHex = "#00FF00",
            UploadColorHex = "#FF0000",
            ShowText = false,
            ShowBackdrop = false,
        }.Normalize();

        using Frame frame = Frame.Render(config, new WidgetVisual(1024, 1024, 1.0, 1.0));
        Rectangle bar = frame.Layout.DownloadBar;
        int y = frame.RowCentre(bar);

        Color start = frame.Bitmap.GetPixel(bar.Left + 1, y);
        Color end = frame.Bitmap.GetPixel(bar.Right - 2, y);

        // Green throughout, but the far end is lighter: that is the fade, and the fill
        // still reaches the end of the track.
        Assert.True(start.G > 120 && end.G > 120, $"gradient is not green: {start} -> {end}");
        Assert.True(start.A > end.A + 20, $"gradient does not fade: {start.A} -> {end.A}");
    }

    [Fact]
    public void TheOutlineDesignHollowsTheUnfilledPart()
    {
        var config = new MonitorConfig
        {
            Style = WidgetStyles.Outline,
            DownloadColorHex = "#00FF00",
            UploadColorHex = "#FF0000",
            ShowText = false,
            ShowBackdrop = false,
        }.Normalize();

        using Frame frame = Frame.Render(config, new WidgetVisual(1024, 1024, 0.5, 0.5));
        Rectangle bar = frame.Layout.DownloadBar;
        int y = frame.RowCentre(bar);
        int filled = WidgetRenderer.FillWidth(bar.Width, 0.5);

        // The filled half is solid, the empty half shows only the 1px border: no wash
        // inside it, unlike every other design.
        Assert.True(Pixels.IsOpaque(frame.Bitmap.GetPixel(bar.Left + 1, y), DownloadFill));
        int wash = 0;
        for (int x = bar.Left + filled + 1; x < bar.Right - 1; x++)
        {
            // The hit wash is alpha 1 over the whole widget; anything above that would be
            // the track showing through the hollow middle.
            if (frame.Bitmap.GetPixel(x, y).A > WidgetRenderer.HitWashAlpha + 1)
            {
                wash++;
            }
        }

        Assert.Equal(0, wash);
        Assert.True(
            frame.Bitmap.GetPixel(bar.Left, y).A > WidgetRenderer.HitWashAlpha + 1,
            "the outline is missing its border");
    }

    // ---- figure colours -----------------------------------------------------------

    [Fact]
    public void EveryTextColourModeIsSelectableAndDescribed()
    {
        Assert.Contains(TextColorModes.Auto, TextColorModes.All);
        Assert.Contains(TextColorModes.Bar, TextColorModes.All);
        Assert.Contains(TextColorModes.Custom, TextColorModes.All);

        var labels = new List<string>();
        foreach (string mode in TextColorModes.All)
        {
            Assert.Equal(mode, TextColorModes.Normalize(mode));
            Assert.Equal(mode, TextColorModes.Normalize(mode.ToUpperInvariant()));
            Assert.False(string.IsNullOrWhiteSpace(TextColorModes.Describe(mode)));
            Assert.DoesNotContain(TextColorModes.Describe(mode), labels);
            labels.Add(TextColorModes.Describe(mode));
        }

        Assert.Equal(TextColorModes.Auto, TextColorModes.Normalize("Rainbow"));
        Assert.Equal(TextColorModes.Auto, TextColorModes.Normalize(null));
    }

    [Fact]
    public void TheFigureColourFollowsTheChosenMode()
    {
        var theme = WidgetTheme.Dark;
        Color bar = Color.FromArgb(0, 255, 0);
        Color white = Color.White;
        Color custom = Color.FromArgb(255, 0, 255);

        Assert.True(Pixels.SameRgb(theme.TextColor, TextColorModes.Resolve(TextColorModes.Auto, "#FFFFFF", bar, theme.TextColor)));
        Assert.True(Pixels.SameRgb(bar, TextColorModes.Resolve(TextColorModes.Bar, "#FFFFFF", bar, theme.TextColor)));
        Assert.True(Pixels.SameRgb(custom, TextColorModes.Resolve(TextColorModes.Custom, "#FF00FF", bar, theme.TextColor)));
        Assert.True(Pixels.SameRgb(white, TextColorModes.Resolve(TextColorModes.Custom, "#FFFFFF", bar, theme.TextColor)));

        // An unreadable custom value falls back to the theme colour instead of vanishing.
        Assert.True(Pixels.SameRgb(theme.TextColor, TextColorModes.Resolve(TextColorModes.Custom, "not a colour", bar, theme.TextColor)));
    }

    [Theory]
    [InlineData(TextColorModes.Auto)]
    [InlineData(TextColorModes.Bar)]
    [InlineData(TextColorModes.Custom)]
    public void TheRenderedFigureUsesTheChosenMode(string mode)
    {
        const string custom = "#FF00FF";
        var config = new MonitorConfig
        {
            Style = WidgetStyles.Bars,
            DownloadColorHex = "#00FF00",
            UploadColorHex = "#FF0000",
            ShowText = true,
            ShowBackdrop = false,
            DownloadTextColorMode = mode,
            UploadTextColorMode = mode,
            DownloadTextColorHex = custom,
            UploadTextColorHex = custom,
        }.Normalize();

        using Frame frame = Frame.Render(config, new WidgetVisual(1024, 1024, 1.0, 1.0));
        var theme = WidgetTheme.Dark;

        Color expected = TextColorModes.Resolve(mode, custom, DownloadFill, theme.TextColor);
        Color actual = FigureInk(frame, frame.Layout.DownloadText);

        Assert.True(
            Pixels.SameRgb(actual, expected),
            $"{mode}: figure drawn as {actual} but expected {expected}");
    }

    [Fact]
    public void TheArrowKeepsTheBarColourWhateverTheFigureColourIs()
    {
        var config = new MonitorConfig
        {
            Style = WidgetStyles.Bars,
            DownloadColorHex = "#00FF00",
            UploadColorHex = "#FF0000",
            ShowText = true,
            ShowBackdrop = false,
            DownloadTextColorMode = TextColorModes.Custom,
            UploadTextColorMode = TextColorModes.Custom,
            DownloadTextColorHex = "#FF00FF",
            UploadTextColorHex = "#FF00FF",
        }.Normalize();

        using Frame frame = Frame.Render(config, new WidgetVisual(1024, 1024, 1.0, 1.0));
        Rectangle row = frame.Layout.DownloadText;

        var colours = new HashSet<int>();
        for (int x = row.Left; x < row.Right; x++)
        {
            for (int y = row.Top; y < row.Bottom; y++)
            {
                Color pixel = frame.Bitmap.GetPixel(x, y);
                if (pixel.A > 0)
                {
                    colours.Add(pixel.ToArgb());
                }
            }
        }

        // Both inks are present and distinct: the green marker and the magenta figure.
        Assert.Contains(colours, argb => Pixels.CloseRgb(Color.FromArgb(argb), DownloadFill));
        Assert.Contains(colours, argb => Pixels.CloseRgb(Color.FromArgb(argb), Color.FromArgb(255, 0, 255)));
    }

    [Fact]
    public void TheTextColoursSurviveTheConfigFile()
    {
        var config = new MonitorConfig
        {
            ArrowStyle = ArrowStyles.Chevron,
            DownloadTextColorMode = TextColorModes.Bar,
            UploadTextColorMode = TextColorModes.Custom,
            DownloadTextColorHex = "#abcdef",
            UploadTextColorHex = "#123456",
        }.Normalize();

        string json = config.ToJson();
        MonitorConfig loaded = MonitorConfig.FromJson(json).Normalize();

        Assert.Equal(ArrowStyles.Chevron, loaded.ArrowStyle);
        Assert.Equal(TextColorModes.Bar, loaded.DownloadTextColorMode);
        Assert.Equal(TextColorModes.Custom, loaded.UploadTextColorMode);
        Assert.Equal("#ABCDEF", loaded.DownloadTextColorHex);
        Assert.Equal("#123456", loaded.UploadTextColorHex);

        // A file with junk in it lands on valid values rather than on broken ones.
        MonitorConfig junk = MonitorConfig.FromJson(
            """{"ArrowStyle":"Laser","DownloadTextColorMode":"Glow","DownloadTextColorHex":"nope"}""").Normalize();
        Assert.Equal(ArrowStyles.Classic, junk.ArrowStyle);
        Assert.Equal(TextColorModes.Auto, junk.DownloadTextColorMode);
        Assert.Equal(MonitorConfig.DefaultTextColor, junk.DownloadTextColorHex);

        CheckConfigInvariants(loaded);
        CheckConfigInvariants(junk);
    }

    [Fact]
    public void TheDialogCarriesTheNewPickers()
    {
        string source = SourceText.Read("UI", "SettingsForm.cs");

        Assert.Contains("ArrowStyle = SelectedKey(_arrowStyle, MonitorConfig.DefaultArrowStyle)", source, StringComparison.Ordinal);
        Assert.Contains("AddRow(look, \"Arrow type:\", _arrowStyle)", source, StringComparison.Ordinal);
        Assert.Contains("DownloadTextColorMode = SelectedKey(_downloadTextMode, TextColorModes.Auto)", source, StringComparison.Ordinal);
        Assert.Contains("AddRow(colours, \"Download text:\"", source, StringComparison.Ordinal);
        Assert.Contains("AddRow(colours, \"Upload text:\"", source, StringComparison.Ordinal);
    }

    /// <summary>The rules every config must satisfy, whatever the user typed into it.</summary>
    private static void CheckConfigInvariants(MonitorConfig config)
    {
        Assert.Contains(config.ArrowStyle, ArrowStyles.All);
        Assert.Contains(config.DownloadTextColorMode, TextColorModes.All);
        Assert.Contains(config.UploadTextColorMode, TextColorModes.All);
        Assert.Equal(config.DownloadTextColorHex, ColorHelper.ToHex(ColorHelper.Parse(config.DownloadTextColorHex, Color.White)));
        Assert.Equal(config.UploadTextColorHex, ColorHelper.ToHex(ColorHelper.Parse(config.UploadTextColorHex, Color.White)));
        Assert.InRange(config.ArrowSizePercent, MonitorConfig.MinArrowSizePercent, MonitorConfig.MaxArrowSizePercent);
        Assert.Contains(config.Style, WidgetStyles.All);
    }

    // ---- helpers -----------------------------------------------------------------

    private static Rectangle ArrowInk(Frame frame, Rectangle row)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int y = Math.Max(0, row.Top); y < Math.Min(frame.Bitmap.Height, row.Bottom); y++)
        {
            for (int x = Math.Max(0, row.Left); x < Math.Min(frame.Bitmap.Width, row.Right); x++)
            {
                Color pixel = frame.Bitmap.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    continue;
                }

                // The figure is never the bar colour in these tests, so bar-coloured
                // pixels are the marker.
                if (!Pixels.CloseRgb(pixel, frame.Config.DownloadColor)
                    && !Pixels.CloseRgb(pixel, frame.Config.UploadColor))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    /// <summary>
    /// The figure's ink colour: the commonest fully opaque colour inside the row. The
    /// arrow is painted at 254/255, so it never wins this vote even when the figure is
    /// the same colour as the bar.
    /// </summary>
    private static Color FigureInk(Frame frame, Rectangle row)
    {
        var counts = new Dictionary<int, int>();
        for (int y = Math.Max(0, row.Top); y < Math.Min(frame.Bitmap.Height, row.Bottom); y++)
        {
            for (int x = Math.Max(0, row.Left); x < Math.Min(frame.Bitmap.Width, row.Right); x++)
            {
                Color pixel = frame.Bitmap.GetPixel(x, y);
                if (pixel.A < 255)
                {
                    continue;
                }

                int argb = pixel.ToArgb();
                counts[argb] = counts.TryGetValue(argb, out int seen) ? seen + 1 : 1;
            }
        }

        Assert.NotEmpty(counts);
        return Color.FromArgb(counts.OrderByDescending(pair => pair.Value).First().Key);
    }

    private static string Describe(Frame frame, Rectangle ink)
    {
        var builder = new System.Text.StringBuilder();
        for (int y = ink.Top; y < ink.Bottom; y++)
        {
            for (int x = ink.Left; x < ink.Right; x++)
            {
                builder.Append(frame.Bitmap.GetPixel(x, y).A > 0 ? '#' : '.');
            }

            builder.Append('/');
        }

        return builder.ToString();
    }

    private static int DrawnArrowHeight(int percent)
    {
        using Frame frame = Frame.Render(TextConfig(ArrowStyles.Classic, percent), new WidgetVisual(1024, 1024, 1.0, 1.0));
        Assert.True(frame.Layout.ArrowSizePercent > 0);
        return ArrowInk(frame, frame.Layout.DownloadText).Height;
    }
}
