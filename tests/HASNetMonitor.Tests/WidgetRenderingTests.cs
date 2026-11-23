using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

public class WidgetRenderingTests
{
    private static readonly WidgetVisual HalfTraffic = new(1250, 500, 0.5, 0.25);

    [Fact]
    public void DownloadBarIsPaintedWithTheConfiguredGreen()
    {
        using Frame frame = Frame.Render(TestSupport.BarsOnly("#00FF00", "#FF0000"), HalfTraffic);
        Invariants.AssertFrame(frame, HalfTraffic);

        Assert.Equal(Color.FromArgb(255, 0, 255, 0), frame.BarPixel(frame.Layout.DownloadBar, frame.Layout.DownloadBar.Left));
        Assert.Equal(
            WidgetRenderer.FillWidth(frame.Layout.DownloadBar.Width, 0.5),
            frame.CountFillPixels(frame.Layout.DownloadBar, Color.FromArgb(255, 0, 255, 0)));
    }

    [Fact]
    public void UploadBarIsPaintedWithTheConfiguredRed()
    {
        using Frame frame = Frame.Render(TestSupport.BarsOnly("#00FF00", "#FF0000"), HalfTraffic);
        Invariants.AssertFrame(frame, HalfTraffic);

        Assert.True(
            frame.CountFillPixels(frame.Layout.UploadBar, Color.FromArgb(255, 255, 0, 0)) > 0);
        Assert.Equal(
            WidgetRenderer.FillWidth(frame.Layout.UploadBar.Width, 0.25),
            frame.CountFillPixels(frame.Layout.UploadBar, Color.FromArgb(255, 255, 0, 0)));
    }

    [Theory]
    [InlineData("#123456", "#654321")]
    [InlineData("#2ECC71", "#E6643C")]
    [InlineData("#000000", "#FFFFFF")]
    public void BarColoursComeFromTheConfig(string download, string upload)
    {
        using Frame frame = Frame.Render(TestSupport.BarsOnly(download, upload), new WidgetVisual(10, 10, 1, 1));
        Invariants.AssertFrame(frame, new WidgetVisual(10, 10, 1, 1));

        Color expectedDownload = ColorHelper.Parse(download, Color.Empty);
        Color expectedUpload = ColorHelper.Parse(upload, Color.Empty);

        Assert.Equal(expectedDownload.R, frame.BarPixel(frame.Layout.DownloadBar, frame.Layout.DownloadBar.Left).R);
        Assert.Equal(expectedDownload.G, frame.BarPixel(frame.Layout.DownloadBar, frame.Layout.DownloadBar.Left).G);
        Assert.Equal(expectedDownload.B, frame.BarPixel(frame.Layout.DownloadBar, frame.Layout.DownloadBar.Left).B);

        Assert.Equal(expectedUpload.R, frame.BarPixel(frame.Layout.UploadBar, frame.Layout.UploadBar.Left).R);
        Assert.Equal(expectedUpload.G, frame.BarPixel(frame.Layout.UploadBar, frame.Layout.UploadBar.Left).G);
        Assert.Equal(expectedUpload.B, frame.BarPixel(frame.Layout.UploadBar, frame.Layout.UploadBar.Left).B);
    }

    [Fact]
    public void EmptyBarsPaintNoFillAtAll()
    {
        var idle = new WidgetVisual(0, 0, 0, 0);
        using Frame frame = Frame.Render(TestSupport.BarsOnly(), idle);
        Invariants.AssertFrame(frame, idle);

        Assert.Equal(0, frame.CountFillPixels(frame.Layout.DownloadBar, Color.Lime));
        Assert.Equal(0, frame.CountFillPixels(frame.Layout.UploadBar, Color.Red));
    }

    [Fact]
    public void BackgroundStaysTransparentLikeTheClock()
    {
        var visual = new WidgetVisual(0, 0, 0, 0);
        using Frame frame = Frame.Render(TestSupport.BarsOnly(), visual);
        Invariants.AssertFrame(frame, visual);

        // The padding rows are empty, so the taskbar behind must show through.
        for (int x = 0; x < frame.Bitmap.Width; x++)
        {
            Assert.True(frame.Bitmap.GetPixel(x, 0).A <= WidgetRenderer.HitWashAlpha + 1);
            Assert.True(frame.Bitmap.GetPixel(x, frame.Bitmap.Height - 1).A <= WidgetRenderer.HitWashAlpha + 1);
        }

        // ... and the right-hand end of the bar is only the faint track, not opaque paint.
        Color track = frame.BarPixel(frame.Layout.DownloadBar, frame.Layout.DownloadBar.Right - 1);
        Assert.True(track.A < 120, $"track alpha {track.A} is too solid for a taskbar widget");
    }

    [Fact]
    public void TinyTrafficStillShowsAtLeastOnePixel()
    {
        var trickle = new WidgetVisual(1, 1, 1e-9, 1e-9);
        using Frame frame = Frame.Render(TestSupport.BarsOnly(), trickle);
        Invariants.AssertFrame(frame, trickle);

        Assert.Equal(WidgetRenderer.MinimumVisibleFillPx, frame.CountFillPixels(frame.Layout.DownloadBar, Color.Lime));
    }

    [Fact]
    public void OutOfRangeRatiosAreClampedInsteadOfOverflowing()
    {
        foreach (double ratio in new[] { 5.0, 1.0001, -3.0, double.NaN, double.PositiveInfinity })
        {
            var visual = new WidgetVisual(100, 100, ratio, ratio);
            using Frame frame = Frame.Render(TestSupport.BarsOnly(), visual);
            Invariants.AssertFrame(frame, visual);

            int filled = frame.CountFillPixels(frame.Layout.DownloadBar, Color.Lime);
            Assert.InRange(filled, 0, frame.Layout.DownloadBar.Width);
        }
    }

    [Fact]
    public void TextIsDrawnOnlyWhenEnabled()
    {
        var visual = new WidgetVisual(1250, 500, 0.5, 0.5);

        MonitorConfig withText = new MonitorConfig().Normalize();
        using Frame shown = Frame.Render(withText, visual, maxHeight: 48);
        Invariants.AssertFrame(shown, visual);
        Assert.True(CountInk(shown, shown.Layout.DownloadText) > 0, "no text was drawn");

        MonitorConfig withoutText = new MonitorConfig { ShowText = false }.Normalize();
        using Frame hidden = Frame.Render(withoutText, visual, maxHeight: 48);
        Invariants.AssertFrame(hidden, visual);
        Assert.Equal(0, CountInk(hidden, hidden.Layout.DownloadText));
        Assert.Equal(0, CountInk(hidden, hidden.Layout.UploadText));

        // Both variants occupy exactly the same space, so toggling text does not
        // resize the widget and make it jump.
        Assert.Equal(shown.Layout.Size, hidden.Layout.Size);
    }

    [Fact]
    public void BackdropIsTranslucentAndNeverFullyOpaque()
    {
        MonitorConfig config = new MonitorConfig
        {
            ShowBackdrop = true,
            BackdropOpacity = 120,
            ShowText = false,
        }.Normalize();

        var visual = new WidgetVisual(0, 0, 0, 0);
        using Frame frame = Frame.Render(config, visual);
        Invariants.AssertFrame(frame, visual);

        // Sample the middle of the widget: the rounded corners of the backdrop stay
        // clear on purpose, so the pill blends into the taskbar behind it.
        Color centre = frame.Bitmap.GetPixel(frame.Bitmap.Width / 2, 1);
        Assert.True(centre.A is > 100 and < 160, $"backdrop alpha {centre.A} ignores BackdropOpacity");
    }

    [Fact]
    public void VerticalTaskbarsGetTheSamePictureRotated()
    {
        var visual = new WidgetVisual(1250, 500, 0.5, 0.25);
        using Frame frame = Frame.Render(
            TestSupport.BarsOnly(),
            visual,
            orientation: TaskbarOrientation.Vertical);

        Invariants.AssertFrame(frame, visual);
        Assert.Equal(frame.Layout.Size.Height, frame.Bitmap.Width);
        Assert.Equal(frame.Layout.Size.Width, frame.Bitmap.Height);
    }

    [Theory]
    [InlineData(1f, 48)]
    [InlineData(1.25f, 60)]
    [InlineData(1.5f, 72)]
    [InlineData(2f, 96)]
    public void LayoutFitsTheTaskbarHeightAtEveryScale(float scale, int taskbarHeight)
    {
        MonitorConfig config = new MonitorConfig().Normalize();
        WidgetLayout layout = new WidgetRenderer().ComputeLayout(config, scale, taskbarHeight);

        Assert.True(layout.Size.Height <= taskbarHeight, $"{layout.Size.Height} > {taskbarHeight} at scale {scale}");
        Assert.True(layout.Size.Width > 0);
        Assert.True(layout.FontSizePoints >= WidgetRenderer.MinimumFontSizePt);
        Assert.True(layout.BarThickness >= 1);
        Assert.True(layout.DownloadBar.Bottom <= layout.Size.Height);
        Assert.True(layout.UploadBar.Bottom <= layout.Size.Height);
    }

    [Fact]
    public void ASqueezedTaskbarShrinksTheFontInsteadOfOverflowing()
    {
        MonitorConfig config = new MonitorConfig().Normalize();
        var renderer = new WidgetRenderer();

        WidgetLayout roomy = renderer.ComputeLayout(config, 1f, 48);
        WidgetLayout cramped = renderer.ComputeLayout(config, 1f, 36);

        Assert.True(cramped.FitsBounds, $"{cramped.Size.Height} does not fit a 36px taskbar");
        Assert.True(cramped.FontSizePoints < roomy.FontSizePoints);
        Assert.True(cramped.FontSizePoints >= WidgetRenderer.MinimumFontSizePt);
    }

    [Fact]
    public void ATaskbarTooSmallToReadReportsThatItDoesNotFit()
    {
        MonitorConfig config = new MonitorConfig().Normalize();
        WidgetLayout layout = new WidgetRenderer().ComputeLayout(config, 1f, 20);

        // Two rows of text and two bars physically need more than 20 pixels: the
        // layout says so, so the widget hides instead of overflowing the taskbar.
        Assert.False(layout.FitsBounds);
        Assert.Equal(WidgetRenderer.MinimumFontSizePt, layout.FontSizePoints);
        Assert.True(layout.Size.Height > 20);
    }

    [Fact]
    public void LayoutWidthDoesNotDependOnTheTraffic()
    {
        // A widget that resizes every second is what makes a taskbar item flicker.
        var renderer = new WidgetRenderer();
        MonitorConfig config = new MonitorConfig().Normalize();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);

        using Frame idle = Frame.Render(config, new WidgetVisual(0, 0, 0, 0));
        using Frame busy = Frame.Render(config, new WidgetVisual(99_000_000, 88_000_000, 1, 1));

        Assert.Equal(layout.Size, idle.Layout.Size);
        Assert.Equal(layout.Size, busy.Layout.Size);
        Assert.Equal(idle.Bitmap.Size, busy.Bitmap.Size);
    }

    [Fact]
    public void TheFrameIsWideEnoughForTheWidestSpeedText()
    {
        MonitorConfig config = new MonitorConfig { ShowText = true }.Normalize();
        WidgetLayout layout = new WidgetRenderer().ComputeLayout(config, 1f, 48);

        using Frame frame = Frame.Render(config, new WidgetVisual(1023L * 1024 * 1024, 1023L * 1024 * 1024, 1, 1));
        Invariants.AssertFrame(frame, frame.Visual);

        // Every drawn row must stay inside the frame instead of being clipped.
        Assert.True(layout.DownloadText.Right <= layout.Size.Width);
        Assert.True(layout.UploadText.Right <= layout.Size.Width);
        Assert.True(layout.Size.Width > 60);
    }

    [Theory]
    [InlineData(100, 0.0, 0)]
    [InlineData(100, 0.5, 50)]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 1.5, 100)]
    [InlineData(100, -1.0, 0)]
    [InlineData(100, 0.001, 1)]
    [InlineData(0, 0.5, 0)]
    [InlineData(-5, 0.5, 0)]
    public void FillWidthFollowsTheRatio(int track, double ratio, int expected)
    {
        Assert.Equal(expected, WidgetRenderer.FillWidth(track, ratio));
    }

    [Fact]
    public void FillWidthIsNeverMoreThanTheTrack()
    {
        for (int track = 0; track <= 200; track++)
        {
            for (double ratio = -0.5; ratio <= 1.5; ratio += 0.05)
            {
                int width = WidgetRenderer.FillWidth(track, ratio);
                Assert.InRange(width, 0, track);
            }
        }
    }

    [Fact]
    public void LightAndDarkThemesUseDifferentTextColours()
    {
        var visual = new WidgetVisual(1250, 500, 0.5, 0.5);
        MonitorConfig config = new MonitorConfig().Normalize();

        using Frame dark = Frame.Render(config, visual, dark: true);
        using Frame light = Frame.Render(config, visual, dark: false);

        Color darkInk = BrightestInk(dark, dark.Layout.DownloadText);
        Color lightInk = BrightestInk(light, light.Layout.DownloadText);

        // Dark taskbar: light glyphs. Light taskbar: dark glyphs.
        Assert.True(darkInk.R > 200 && darkInk.G > 200 && darkInk.B > 200, $"dark theme ink {darkInk}");
        Assert.True(lightInk.R < 120 && lightInk.G < 120 && lightInk.B < 120, $"light theme ink {lightInk}");
    }

    /// <summary>Most solid pixel in a region: the text colour, ignoring antialiased edges.</summary>
    private static Color BrightestInk(Frame frame, Rectangle area)
    {
        Color best = Color.Transparent;
        for (int x = area.Left; x < area.Right && x < frame.Bitmap.Width; x++)
        {
            for (int y = area.Top; y < area.Bottom && y < frame.Bitmap.Height; y++)
            {
                Color pixel = frame.Bitmap.GetPixel(x, y);
                if (pixel.A > best.A)
                {
                    best = pixel;
                }
            }
        }

        return best;
    }

    /// <summary>Non-transparent pixels in a region: how much ink the renderer laid down.</summary>
    private static int CountInk(Frame frame, Rectangle area)
    {
        int ink = 0;
        for (int x = area.Left; x < area.Right && x < frame.Bitmap.Width; x++)
        {
            for (int y = area.Top; y < area.Bottom && y < frame.Bitmap.Height; y++)
            {
                if (frame.Bitmap.GetPixel(x, y).A > 20)
                {
                    ink++;
                }
            }
        }

        return ink;
    }
}
