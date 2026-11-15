using System.Drawing;
using System.Drawing.Text;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

/// <summary>
/// The two things the taskbar row has to do to look like the clock beside it: the
/// arrival arrow reads green and the departure arrow red, and the glyphs are drawn
/// smoothed rather than as hard aliased steps.
/// </summary>
public class ArrowAndTextTests
{
    private static readonly WidgetVisual HalfAndQuarter = new(1024, 2048, 0.5, 0.25);

    private static MonitorConfig TextConfig(string download = "#00FF00", string upload = "#FF0000") => new MonitorConfig
    {
        DownloadColorHex = download,
        UploadColorHex = upload,
        ShowText = true,
        ShowBackdrop = false,
        BarThicknessPx = 4,
    }.Normalize();

    // The dark theme writes the figure in white, so a pure white pixel is "the figure"
    // and a strongly tinted one is the arrow. Pure channel colours keep the two apart:
    // an antialiased green glyph blended towards white stays R == B with G on top.
    private static int CountDominant(Frame frame, Rectangle rect, Channel channel)
    {
        int count = 0;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                if (c.A < 100)
                {
                    continue;
                }

                bool hit = channel switch
                {
                    Channel.Green => c.G > c.R + 20 && c.G > c.B + 20,
                    Channel.Red => c.R > c.G + 20 && c.R > c.B + 20,
                    Channel.Blue => c.B > c.R + 20 && c.B > c.G + 20,
                    _ => false,
                };

                count += hit ? 1 : 0;
            }
        }

        return count;
    }

    private static int CountGrey(Frame frame, Rectangle rect)
    {
        int count = 0;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                int spread = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                count += c.A > 200 && spread < 8 ? 1 : 0;
            }
        }

        return count;
    }

    private static int CountPartiallyTransparent(Frame frame, Rectangle rect)
    {
        int count = 0;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                count += c.A > 40 && c.A < 210 ? 1 : 0;
            }
        }

        return count;
    }

    private enum Channel
    {
        Green,
        Red,
        Blue,
    }

    [Fact]
    public void DownloadArrowIsGreenAndUploadArrowIsRed()
    {
        using Frame frame = Frame.Render(TextConfig(), HalfAndQuarter);

        int downloadGreen = CountDominant(frame, frame.Layout.DownloadText, Channel.Green);
        int downloadRed = CountDominant(frame, frame.Layout.DownloadText, Channel.Red);
        int uploadRed = CountDominant(frame, frame.Layout.UploadText, Channel.Red);
        int uploadGreen = CountDominant(frame, frame.Layout.UploadText, Channel.Green);

        Assert.True(downloadGreen > 0, "the download arrow is not drawn in the download colour");
        Assert.True(uploadRed > 0, "the upload arrow is not drawn in the upload colour");
        Assert.Equal(0, downloadRed);
        Assert.Equal(0, uploadGreen);

        Invariants.AssertFrame(frame, HalfAndQuarter);
    }

    [Fact]
    public void TheArrowColoursComeFromTheConfigNotFromConstants()
    {
        // Blue arrival, red departure: a hardcoded green would show up here.
        using Frame frame = Frame.Render(TextConfig(download: "#0000FF"), HalfAndQuarter);

        Assert.True(CountDominant(frame, frame.Layout.DownloadText, Channel.Blue) > 0);
        Assert.Equal(0, CountDominant(frame, frame.Layout.DownloadText, Channel.Green));

        Invariants.AssertFrame(frame, HalfAndQuarter);
    }

    [Fact]
    public void TheFigureStaysInTheTextColour()
    {
        using Frame frame = Frame.Render(TextConfig(), HalfAndQuarter);

        // Only the arrow takes the traffic colour; the number must stay legible text,
        // otherwise a dark config colour would make the figure unreadable.
        Assert.True(CountGrey(frame, frame.Layout.DownloadText) > 0);
        Assert.True(CountGrey(frame, frame.Layout.UploadText) > 0);
    }

    [Fact]
    public void TheArrowAndTheFigureDoNotTouch()
    {
        using Frame frame = Frame.Render(TextConfig(), HalfAndQuarter);
        AssertSeparated(frame, frame.Layout.DownloadText, Channel.Green);
        AssertSeparated(frame, frame.Layout.UploadText, Channel.Red);
    }

    private static void AssertSeparated(Frame frame, Rectangle rect, Channel arrowChannel)
    {
        int lastArrow = -1;
        int firstFigure = int.MaxValue;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                if (c.A < 100)
                {
                    continue;
                }

                bool arrow = arrowChannel switch
                {
                    Channel.Green => c.G > c.R + 20 && c.G > c.B + 20,
                    Channel.Red => c.R > c.G + 20 && c.R > c.B + 20,
                    _ => c.B > c.R + 20 && c.B > c.G + 20,
                };

                if (arrow)
                {
                    lastArrow = Math.Max(lastArrow, x);
                    continue;
                }

                int spread = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                if (c.A > 200 && spread < 8)
                {
                    firstFigure = Math.Min(firstFigure, x);
                }
            }
        }

        Assert.True(lastArrow >= 0, "no arrow was drawn");
        Assert.True(firstFigure != int.MaxValue, "no figure was drawn");
        Assert.True(
            firstFigure > lastArrow,
            $"the figure starts at x={firstFigure} but the arrow still paints at x={lastArrow}");
    }

    [Fact]
    public void GlyphsAreSmoothlyAntialiased()
    {
        using Frame frame = Frame.Render(TextConfig(), HalfAndQuarter);

        // A hard-aliased glyph has only fully transparent and fully opaque pixels.
        // Partial alpha in both rows is the evidence that the edges are smoothed.
        Assert.True(CountPartiallyTransparent(frame, frame.Layout.DownloadText) > 4);
        Assert.True(CountPartiallyTransparent(frame, frame.Layout.UploadText) > 4);
    }

    [Fact]
    public void TextUsesGridFittedAntialiasing()
    {
        // The clock is drawn grid-fitted; plain AntiAlias renders thinner, washed-out
        // stems beside it. This pins the hint the renderer actually uses.
        Assert.Equal(TextRenderingHint.AntiAliasGridFit, WidgetRenderer.GlyphHint);
    }

    [Fact]
    public void TheRowIsWideEnoughForArrowAndFigure()
    {
        MonitorConfig config = TextConfig();
        var renderer = new WidgetRenderer();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);

        Assert.True(layout.ContentWidth > 0);

        using var surface = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(layout.FontEmPixels, config);
        using StringFormat format = new StringFormat(StringFormat.GenericTypographic);

        // The widest figure we can produce must still fit after the arrow and gap.
        float offset = WidgetRenderer.RowNumberOffset(graphics, font, format, layout.Scale);
        Assert.True(offset > WidgetRenderer.ArrowGap(layout.Scale), "the arrow width is included in the offset");
        Assert.True(offset < layout.ContentWidth, "the figure would be pushed out of the row");

        Assert.True(layout.DownloadText.Right <= layout.Size.Width);
        Assert.True(layout.UploadText.Right <= layout.Size.Width);
    }
}
