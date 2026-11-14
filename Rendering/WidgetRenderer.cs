using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using TaskbarNetMonitor.Core;

namespace TaskbarNetMonitor.Rendering;

/// <summary>Everything needed to draw one frame.</summary>
internal readonly record struct WidgetVisual(
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    double DownloadRatio,
    double UploadRatio)
{
    public static WidgetVisual Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Pixel geometry of one frame, in the widget's own coordinates. Exposed so the
/// tests can assert on bar positions and so the same numbers are used both to
/// measure the window and to paint it.
/// </summary>
internal readonly record struct WidgetLayout(
    Size Size,
    Rectangle DownloadText,
    Rectangle DownloadBar,
    Rectangle UploadText,
    Rectangle UploadBar,
    int ContentWidth,
    int BarThickness,
    float FontSizePoints,
    int PaddingX,
    float Scale,
    int MaxHeight)
{
    /// <summary>
    /// False when even the smallest legible font is too big for the taskbar. The
    /// widget then hides itself instead of drawing something sticking out of the bar.
    /// </summary>
    public bool FitsBounds => Size.Height <= MaxHeight;
}

/// <summary>
/// Draws the widget into a 32bppPArgb bitmap. Nothing here talks to a window:
/// the bitmap is handed to UpdateLayeredWindow as one atomic blit, which is what
/// removes the flicker entirely. The background is (almost) fully transparent so
/// the real taskbar shows through, exactly like the clock does.
/// </summary>
internal sealed class WidgetRenderer
{
    /// <summary>Any traffic at all shows at least one pixel of bar.</summary>
    public const int MinimumVisibleFillPx = 1;

    public const float MinimumFontSizePt = 6f;

    /// <summary>
    /// Near-invisible wash covering the whole widget. Layered windows only receive
    /// mouse input where the alpha is non-zero, so this keeps right-click working
    /// over the empty space between the bars.
    /// </summary>
    public const byte HitWashAlpha = 1;

    private static readonly string FontFamilyName = ResolveFontFamily();

    private static readonly string[] ProbeTexts = BuildProbeTexts();

    public WidgetLayout ComputeLayout(MonitorConfig config, float scale, int maxHeight)
    {
        if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            scale = 1f;
        }

        int maxHeightPx = maxHeight > 0 ? maxHeight : (int)Math.Round(48 * scale);

        float points = (float)Math.Clamp(config.FontSizePt, MinimumFontSizePt, 20);
        int barThickness = Math.Max(1, (int)Math.Round(Math.Clamp(config.BarThicknessPx, 1, 12) * scale));
        int paddingX = Math.Max(2, (int)Math.Round(4 * scale));
        int paddingY = Math.Max(1, (int)Math.Round(1 * scale));
        int rowGap = Math.Max(1, (int)Math.Round(2 * scale));
        int barGap = Math.Max(0, (int)Math.Round(1 * scale));

        using var measureSurface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var measureGraphics = Graphics.FromImage(measureSurface);

        for (int attempt = 0; ; attempt++)
        {
            using Font font = CreateFont(points, scale);
            int textHeight = Math.Max(1, (int)Math.Ceiling(font.GetHeight(measureGraphics)));
            int contentWidth = Math.Max(1, (int)Math.Ceiling(MeasureWidestProbe(measureGraphics, font)));

            int height = paddingY
                + textHeight + barGap + barThickness
                + rowGap
                + textHeight + barGap + barThickness
                + paddingY;
            int width = contentWidth + paddingX * 2;

            bool tooTall = height > maxHeightPx;
            float next = Math.Max(MinimumFontSizePt, points * 0.94f);
            bool canShrink = tooTall && next < points - 0.01f && attempt < 24;
            if (!canShrink)
            {
                int y = paddingY;
                var downloadText = new Rectangle(paddingX, y, contentWidth, textHeight);
                y += textHeight + barGap;
                var downloadBar = new Rectangle(paddingX, y, contentWidth, barThickness);
                y += barThickness + rowGap;
                var uploadText = new Rectangle(paddingX, y, contentWidth, textHeight);
                y += textHeight + barGap;
                var uploadBar = new Rectangle(paddingX, y, contentWidth, barThickness);
                y += barThickness + paddingY;

                return new WidgetLayout(
                    new Size(width, y),
                    downloadText,
                    downloadBar,
                    uploadText,
                    uploadBar,
                    contentWidth,
                    barThickness,
                    points,
                    paddingX,
                    scale,
                    maxHeightPx);
            }

            points = next;
        }
    }

    /// <summary>
    /// Paints one frame. The caller owns (and must dispose) the returned bitmap.
    /// </summary>
    public Bitmap Render(
        WidgetVisual visual,
        MonitorConfig config,
        WidgetLayout layout,
        WidgetTheme theme,
        TaskbarOrientation orientation)
    {
        Size size = layout.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            size = new Size(1, 1);
        }

        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            Draw(graphics, visual, config, layout, theme);
        }

        if (orientation == TaskbarOrientation.Vertical)
        {
            return RotateClockwise(bitmap);
        }

        return bitmap;
    }

    /// <summary>Width of the coloured part of a bar, in whole pixels.</summary>
    public static int FillWidth(int trackWidth, double ratio)
    {
        if (trackWidth <= 0 || double.IsNaN(ratio) || ratio <= 0)
        {
            return 0;
        }

        double clamped = Math.Min(ratio, 1.0);
        int width = (int)Math.Round(trackWidth * clamped, MidpointRounding.AwayFromZero);
        if (width < MinimumVisibleFillPx)
        {
            width = MinimumVisibleFillPx;
        }

        return Math.Min(width, trackWidth);
    }

    private static void Draw(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

        using (var hitWash = new SolidBrush(Color.FromArgb(HitWashAlpha, 255, 255, 255)))
        {
            graphics.FillRectangle(hitWash, new Rectangle(Point.Empty, layout.Size));
        }

        if (config.ShowBackdrop && config.BackdropOpacity > 0)
        {
            int radius = Math.Max(1, (int)Math.Round(5 * layout.Scale));
            using var backdrop = new SolidBrush(theme.BackdropColor((byte)Math.Clamp(config.BackdropOpacity, 0, 255)));
            using GraphicsPath path = CreateRoundedPath(new Rectangle(Point.Empty, layout.Size), radius);
            graphics.FillPath(backdrop, path);
        }

        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        graphics.FillRectangle(trackBrush, layout.DownloadBar);
        graphics.FillRectangle(trackBrush, layout.UploadBar);

        int downloadWidth = FillWidth(layout.DownloadBar.Width, visual.DownloadRatio);
        if (downloadWidth > 0)
        {
            graphics.FillRectangle(downloadBrush, Slice(layout.DownloadBar, downloadWidth));
        }

        int uploadWidth = FillWidth(layout.UploadBar.Width, visual.UploadRatio);
        if (uploadWidth > 0)
        {
            graphics.FillRectangle(uploadBrush, Slice(layout.UploadBar, uploadWidth));
        }

        if (!config.ShowText)
        {
            return;
        }

        using var textBrush = new SolidBrush(theme.TextColor);
        using Font font = CreateFont(layout.FontSizePoints, layout.Scale);
        using StringFormat format = CreateTightFormat();

        DrawRowText(graphics, font, textBrush, format, layout.DownloadText, SpeedFormatter.FormatRow(true, visual.DownloadBytesPerSecond));
        DrawRowText(graphics, font, textBrush, format, layout.UploadText, SpeedFormatter.FormatRow(false, visual.UploadBytesPerSecond));
    }

    private static void DrawRowText(Graphics graphics, Font font, Brush brush, StringFormat format, Rectangle bounds, string text)
    {
        float textHeight = font.GetHeight(graphics);
        float top = bounds.Y + (bounds.Height - textHeight) / 2f;
        graphics.DrawString(text, font, brush, bounds.X, top, format);
    }

    private static Rectangle Slice(Rectangle source, int width)
        => new(source.X, source.Y, width, source.Height);

    private static Font CreateFont(float points, float scale)
        => new(FontFamilyName, Math.Max(points * scale, MinimumFontSizePt), FontStyle.Regular, GraphicsUnit.Point);

    private static StringFormat CreateTightFormat() => new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        Trimming = StringTrimming.None,
    };

    private static float MeasureWidestProbe(Graphics graphics, Font font)
    {
        using StringFormat format = CreateTightFormat();
        float widest = 0;
        foreach (string probe in ProbeTexts)
        {
            widest = Math.Max(widest, graphics.MeasureString(probe, font, PointF.Empty, format).Width);
        }

        return widest;
    }

    /// <summary>
    /// Width probes come from the real formatter, so the widget width stays stable
    /// (no jitter as numbers change) and always matches the widest string we can
    /// actually produce.
    /// </summary>
    private static string[] BuildProbeTexts()
    {
        double[] samples =
        [
            0,
            999,
            1024,
            99 * 1024,
            100 * 1024,
            888.8 * 1024 * 1024,
            1023 * 1024 * 1024,
            100.0 * 1024 * 1024 * 1024,
        ];

        var probes = new List<string>(samples.Length * 2);
        foreach (double sample in samples)
        {
            probes.Add(SpeedFormatter.FormatRow(true, sample));
            probes.Add(SpeedFormatter.FormatRow(false, sample));
        }

        return [.. probes];
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Bitmap RotateClockwise(Bitmap source)
    {
        var rotated = new Bitmap(source.Height, source.Width, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(rotated))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.TranslateTransform(rotated.Width, 0);
            graphics.RotateTransform(90);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        source.Dispose();
        return rotated;
    }

    private static string ResolveFontFamily()
    {
        foreach (string candidate in new[] { "Segoe UI Variable Text", "Segoe UI" })
        {
            try
            {
                using var family = new FontFamily(candidate);
                return candidate;
            }
            catch (ArgumentException)
            {
            }
        }

        return FontFamily.GenericSansSerif.Name;
    }
}
