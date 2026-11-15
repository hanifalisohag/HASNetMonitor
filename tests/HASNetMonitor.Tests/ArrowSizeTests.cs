using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

/// <summary>
/// The arrow has to read as a marker of the figure's size rather than a small tick
/// beside it. U+2191/U+2193 are drawn shorter than the digits in every UI font, so the
/// arrow gets its own, larger font whose ink matches the digit height.
/// </summary>
public class ArrowSizeTests
{
    private static readonly WidgetVisual HalfAndQuarter = new(1024, 2048, 0.5, 0.25);

    private static MonitorConfig TextConfig(string family = "Segoe UI", bool bold = false) => new MonitorConfig
    {
        DownloadColorHex = "#00FF00",
        UploadColorHex = "#FF0000",
        ShowText = true,
        ShowBackdrop = false,
        BarThicknessPx = 4,
        FontFamily = family,
        FontBold = bold,
    }.Normalize();

    /// <summary>Ink height of a string, measured the way the renderer measures it.</summary>
    private static float InkHeight(Font font, string text)
    {
        using var surface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(surface);
        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericTypographic);
        return path.GetBounds().Height;
    }

    private static float InkHeight(string family, int emPixels, string text)
    {
        using var surface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(emPixels, TextConfig(family));
        return InkHeight(font, text);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    public void TheArrowIsDrawnNoSmallerThanADigit(int emPixels)
    {
        foreach (string family in new[] { "Segoe UI", "Tahoma", "Consolas", "Verdana" })
        {
            int arrowEm = WidgetRenderer.ArrowEmPixels(emPixels, TextConfig(family));

            Assert.True(arrowEm >= emPixels, $"{family} at {emPixels}: arrow em {arrowEm} shrank");
            Assert.True(
                InkHeight(family, arrowEm, SpeedFormatter.DownloadArrow) >= InkHeight(family, emPixels, "8") - 0.01f,
                $"{family} at em {emPixels}: arrow ink {InkHeight(family, arrowEm, SpeedFormatter.DownloadArrow):F2} " +
                $"is below digit ink {InkHeight(family, emPixels, "8"):F2}");
        }
    }

    [Fact]
    public void TheArrowIsGrownNotMerelyMatched()
    {
        // The reported complaint: at the sizes the taskbar uses, the arrow was visibly
        // shorter than the figure. It must actually get bigger, not stay put.
        foreach (int em in new[] { 9, 10, 12, 13, 15 })
        {
            int arrowEm = WidgetRenderer.ArrowEmPixels(em, TextConfig());
            Assert.True(arrowEm > em, $"em {em} left the arrow at {arrowEm}");
        }
    }

    [Fact]
    public void TheArrowScaleCannotRunAway()
    {
        // A font with a tiny or missing arrow glyph must not produce an absurd arrow.
        foreach (int em in new[] { 8, 12, 20, 44 })
        {
            int arrowEm = WidgetRenderer.ArrowEmPixels(em, TextConfig());

            // The cap is on the total growth: the family correction times the user's
            // percentage, whatever that percentage is.
            Assert.True(
                arrowEm <= (int)Math.Ceiling(em * WidgetRenderer.MaximumArrowTotalScale),
                $"em {em} gave {arrowEm}");
        }
    }

    [Fact]
    public void TheArrowFontKeepsTheFamilyAndTheStyle()
    {
        if (!new FontFamily("Segoe UI").IsStyleAvailable(FontStyle.Bold))
        {
            return;
        }

        using Font text = WidgetRenderer.CreateFont(13, TextConfig("Segoe UI", bold: true));
        using Font arrow = WidgetRenderer.CreateArrowFont(text);

        Assert.Equal(text.FontFamily.Name, arrow.FontFamily.Name);
        Assert.Equal(FontStyle.Bold, arrow.Style);
        Assert.True(arrow.Size >= text.Size, $"arrow {arrow.Size} is below text {text.Size}");
    }

    /// <summary>The rows of the frame, in reading order, so each can be measured.</summary>
    private static IEnumerable<(string Row, Rectangle Bounds)> Rows(WidgetLayout layout)
    {
        yield return ("download", layout.DownloadText);
        yield return ("upload", layout.UploadText);
    }

    private static (int MinY, int MaxY) ArrowInk(Frame frame, Rectangle row)
    {
        int min = int.MaxValue, max = int.MinValue;
        for (int y = row.Top; y < row.Bottom; y++)
        {
            for (int x = row.Left; x < row.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                if (c.A < 60)
                {
                    continue;
                }

                bool grey = Math.Abs(c.R - c.G) < 12 && Math.Abs(c.G - c.B) < 12;
                bool tinted = !grey && (c.R > c.B + 40 || c.G > c.B + 40);
                if (tinted)
                {
                    min = Math.Min(min, y);
                    max = Math.Max(max, y);
                }
            }
        }

        return (min, max);
    }

    private static (int MinY, int MaxY) FigureInk(Frame frame, Rectangle row, float numberOffset)
    {
        int min = int.MaxValue, max = int.MinValue;
        int start = row.Left + (int)Math.Ceiling(numberOffset);
        for (int y = row.Top; y < row.Bottom; y++)
        {
            for (int x = start; x < row.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                if (c.A < 60)
                {
                    continue;
                }

                bool grey = Math.Abs(c.R - c.G) < 28 && Math.Abs(c.G - c.B) < 28;
                if (grey)
                {
                    min = Math.Min(min, y);
                    max = Math.Max(max, y);
                }
            }
        }

        return (min, max);
    }

    [Fact]
    public void TheDrawnArrowIsAsTallAsTheDrawnFigure()
    {
        using Frame frame = Frame.Render(TextConfig(), HalfAndQuarter);

        using var surface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(frame.Layout.FontEmPixels, frame.Config);
        using StringFormat format = StringFormat.GenericTypographic;
        float offset = WidgetRenderer.RowNumberOffset(graphics, font, format, frame.Layout.Scale);

        foreach ((string name, Rectangle row) in Rows(frame.Layout))
        {
            (int arrowTop, int arrowBottom) = ArrowInk(frame, row);
            (int figureTop, int figureBottom) = FigureInk(frame, row, offset);

            Assert.True(arrowTop <= arrowBottom, $"{name}: no arrow ink found");
            Assert.True(figureTop <= figureBottom, $"{name}: no figure ink found");

            int arrowHeight = arrowBottom - arrowTop + 1;
            int figureHeight = figureBottom - figureTop + 1;
            Assert.True(
                arrowHeight >= figureHeight - 1,
                $"{name}: arrow ink {arrowHeight}px is shorter than figure ink {figureHeight}px");
        }
    }

    [Fact]
    public void TheDrawnArrowSitsOnTheFiguresLine()
    {
        using Frame frame = Frame.Render(TextConfig(), HalfAndQuarter);

        using var surface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(frame.Layout.FontEmPixels, frame.Config);
        using StringFormat format = StringFormat.GenericTypographic;
        float offset = WidgetRenderer.RowNumberOffset(graphics, font, format, frame.Layout.Scale);

        foreach ((string name, Rectangle row) in Rows(frame.Layout))
        {
            (int arrowTop, int arrowBottom) = ArrowInk(frame, row);
            (int figureTop, int figureBottom) = FigureInk(frame, row, offset);

            double arrowCentre = (arrowTop + arrowBottom) / 2.0;
            double figureCentre = (figureTop + figureBottom) / 2.0;
            Assert.True(
                Math.Abs(arrowCentre - figureCentre) <= 2.0,
                $"{name}: arrow centred at {arrowCentre:F1} but the figure at {figureCentre:F1}");

            // The arrowhead must sit inside the figure's band, not above or below it.
            Assert.True(arrowTop >= figureTop - 2, $"{name}: arrow starts at {arrowTop}, figure at {figureTop}");
            Assert.True(arrowBottom <= figureBottom + 2, $"{name}: arrow ends at {arrowBottom}, figure at {figureBottom}");
        }
    }

    [Fact]
    public void TheWiderArrowStillLeavesRoomForTheWholeFigure()
    {
        // A bigger arrow takes more of the row, so the layout has to reserve it: the
        // figure must still finish inside the widget.
        foreach (string family in new[] { "Segoe UI", "Consolas" })
        {
            using Frame frame = Frame.Render(TextConfig(family), HalfAndQuarter);

            foreach ((string name, Rectangle row) in Rows(frame.Layout))
            {
                int rightmost = row.Left;
                for (int y = row.Top; y < row.Bottom; y++)
                {
                    for (int x = row.Right - 1; x >= row.Left; x--)
                    {
                        if (frame.Bitmap.GetPixel(x, y).A > 100)
                        {
                            rightmost = Math.Max(rightmost, x);
                            break;
                        }
                    }
                }

                Assert.True(
                    rightmost < frame.Layout.Size.Width,
                    $"{family} {name}: ink reaches {rightmost} of {frame.Layout.Size.Width}");
            }
        }
    }

    [Fact]
    public void TheArrowSizeIsStableForTheSameFont()
    {
        // Measured once per family and cached: repeated calls must not drift, or the row
        // width would change between two paints of the same frame.
        int first = WidgetRenderer.ArrowEmPixels(12, TextConfig("Segoe UI"));
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(first, WidgetRenderer.ArrowEmPixels(12, TextConfig("Segoe UI")));
        }
    }

    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Minimal)]
    [InlineData(WidgetStyles.Sidebar)]
    public void TheArrowIsScaledInEveryTextStyle(string style)
    {
        MonitorConfig config = TextConfig();
        config.Style = style;
        config = config.Normalize();

        using Frame frame = Frame.Render(config, HalfAndQuarter);
        using var surface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(frame.Layout.FontEmPixels, frame.Config);

        int arrowEm = WidgetRenderer.ArrowEmPixels(frame.Layout.FontEmPixels, frame.Config);
        Assert.True(arrowEm >= frame.Layout.FontEmPixels);
        Assert.True(WidgetRenderer.RowNumberOffset(graphics, font, StringFormat.GenericTypographic, frame.Layout.Scale) >
                    WidgetRenderer.ArrowGap(frame.Layout.Scale));
    }
}
