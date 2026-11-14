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

    /// <summary>Which visual style this layout was built for.</summary>
    public string Style { get; init; } = WidgetStyles.Bars;

    /// <summary>True when the upload row/column comes first in reading order.</summary>
    public bool UploadOnTop { get; init; }

    /// <summary>Gap between a track and its neighbour, also the level-meter gap.</summary>
    public int BarGap { get; init; }

    /// <summary>Empty space between the blocks of a segmented style.</summary>
    public int SegmentGap { get; init; }
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

    private static readonly string[] ProbeTexts = BuildProbeTexts();

    public WidgetLayout ComputeLayout(MonitorConfig config, float scale, int maxHeight)
    {
        if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            scale = 1f;
        }

        string style = WidgetStyles.Normalize(config.Style);
        bool uploadOnTop = StackOrder.UploadOnTop(config.SortOrder);

        int maxHeightPx = maxHeight > 0 ? maxHeight : (int)Math.Round(48 * scale);

        float points = (float)Math.Clamp(config.FontSizePt, MinimumFontSizePt, 20);
        int barThickness = Math.Max(1, (int)Math.Round(Math.Clamp(config.BarThicknessPx, 1, MonitorConfig.MaxBarThicknessPx) * scale));
        int paddingX = Math.Max(2, (int)Math.Round(4 * scale));
        int paddingY = Math.Max(1, (int)Math.Round(1 * scale));
        int rowGap = Math.Max(1, (int)Math.Round(2 * scale));
        int barGap = Math.Max(0, (int)Math.Round(1 * scale));
        int segmentGap = Math.Max(0, (int)Math.Round(Math.Clamp(config.SegmentGapPx, 0, MonitorConfig.MaxSegmentGapPx) * scale));
        bool vertical = WidgetStyles.IsVertical(style);

        using var measureSurface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var measureGraphics = Graphics.FromImage(measureSurface);

        for (int attempt = 0; ; attempt++)
        {
            using Font font = CreateFont(points, scale, config);
            int textHeight = Math.Max(1, (int)Math.Ceiling(font.GetHeight(measureGraphics)));
            int contentWidth = Math.Max(1, (int)Math.Ceiling(MeasureWidestProbe(measureGraphics, font)));

            int height;
            int width;
            if (style == WidgetStyles.Sidebar || vertical)
            {
                int textBlock = (textHeight * 2) + rowGap;
                int barsWidth = (barThickness * 2) + barGap;

                // Without text the bars must fill the height that is actually there,
                // otherwise a short taskbar would leave them needlessly tiny.
                int barHeight = config.ShowText
                    ? Math.Max(textBlock, Math.Max(barThickness * 2, (int)Math.Round(10 * scale)))
                    : Math.Clamp(
                        (int)Math.Round(14 * scale),
                        barThickness,
                        Math.Max(barThickness, maxHeightPx - (paddingY * 2)));
                height = paddingY + barHeight + paddingY;
                width = paddingX + (config.ShowText ? contentWidth + barGap : 0) + barsWidth + paddingX;
            }
            else
            {
                int rowBlock = textHeight + barGap + barThickness;
                height = paddingY + rowBlock + rowGap + rowBlock + paddingY;
                width = contentWidth + (paddingX * 2);
            }

            bool tooTall = height > maxHeightPx;
            float next = Math.Max(MinimumFontSizePt, points * 0.94f);
            bool canShrink = tooTall && next < points - 0.01f && attempt < 24;
            if (canShrink)
            {
                points = next;
                continue;
            }

            WidgetLayout layout = vertical
                ? BuildSidebarLayout(
                    config, style, uploadOnTop, new Size(width, height), contentWidth, textHeight,
                    barThickness, barGap, points, paddingX, paddingY, rowGap, scale, maxHeightPx)
                : BuildRowsLayout(
                    style, uploadOnTop, new Size(width, height), contentWidth, textHeight,
                    barThickness, barGap, points, paddingX, paddingY, rowGap, scale, maxHeightPx);

            return layout with { SegmentGap = segmentGap };
        }
    }

    private static WidgetLayout BuildRowsLayout(
        string style,
        bool uploadOnTop,
        Size size,
        int contentWidth,
        int textHeight,
        int barThickness,
        int barGap,
        float points,
        int paddingX,
        int paddingY,
        int rowGap,
        float scale,
        int maxHeightPx)
    {
        int y = paddingY;
        var firstText = new Rectangle(paddingX, y, contentWidth, textHeight);
        y += textHeight + barGap;
        var firstBar = new Rectangle(paddingX, y, contentWidth, barThickness);
        y += barThickness + rowGap;
        var secondText = new Rectangle(paddingX, y, contentWidth, textHeight);
        y += textHeight + barGap;
        var secondBar = new Rectangle(paddingX, y, contentWidth, barThickness);

        return new WidgetLayout(
            size,
            uploadOnTop ? secondText : firstText,
            uploadOnTop ? secondBar : firstBar,
            uploadOnTop ? firstText : secondText,
            uploadOnTop ? firstBar : secondBar,
            contentWidth,
            barThickness,
            points,
            paddingX,
            scale,
            maxHeightPx)
        {
            Style = style,
            UploadOnTop = uploadOnTop,
            BarGap = barGap,
        };
    }

    private static WidgetLayout BuildSidebarLayout(
        MonitorConfig config,
        string style,
        bool uploadOnTop,
        Size size,
        int contentWidth,
        int textHeight,
        int barThickness,
        int barGap,
        float points,
        int paddingX,
        int paddingY,
        int rowGap,
        float scale,
        int maxHeightPx)
    {
        int barHeight = Math.Max(1, size.Height - (paddingY * 2));
        int barsWidth = (barThickness * 2) + barGap;
        int barLeft = size.Width - paddingX - barsWidth;

        Rectangle leftBar = new(barLeft, paddingY, barThickness, barHeight);
        var rightBar = new Rectangle(barLeft + barThickness + barGap, paddingY, barThickness, barHeight);

        Rectangle downloadText = Rectangle.Empty;
        Rectangle uploadText = Rectangle.Empty;
        if (config.ShowText)
        {
            int textBlock = (textHeight * 2) + rowGap;
            int textTop = paddingY + Math.Max(0, (barHeight - textBlock) / 2);
            var firstText = new Rectangle(paddingX, textTop, contentWidth, textHeight);
            var secondText = new Rectangle(paddingX, textTop + textHeight + rowGap, contentWidth, textHeight);
            (downloadText, uploadText) = uploadOnTop ? (secondText, firstText) : (firstText, secondText);
        }

        return new WidgetLayout(
            size,
            downloadText,
            uploadOnTop ? rightBar : leftBar,
            uploadText,
            uploadOnTop ? leftBar : rightBar,
            contentWidth,
            barThickness,
            points,
            paddingX,
            scale,
            maxHeightPx)
        {
            Style = style,
            UploadOnTop = uploadOnTop,
            BarGap = barGap,
        };
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

    /// <summary>Width (or height, for vertical tracks) of the coloured part, in whole pixels.</summary>
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

    // ---- Level meter geometry -------------------------------------------------
    // The level style paints a track as a row of blocks. The geometry lives here so
    // the painter and the tests use exactly the same numbers.

    /// <summary>Empty space between two blocks of a level meter.</summary>
    public static int LevelSegmentGap(int barThickness) => Math.Max(1, barThickness);

    /// <summary>
    /// How many blocks fit in a track. At least one, so a very thin track still
    /// shows traffic instead of drawing nothing at all.
    /// </summary>
    public static int LevelSegmentCount(int trackWidth, int barThickness)
    {
        if (trackWidth <= 0)
        {
            return 0;
        }

        int gap = LevelSegmentGap(barThickness);
        int nominal = Math.Max(2, barThickness * 2);
        int count = (trackWidth + gap) / (nominal + gap);
        return Math.Max(1, count);
    }

    /// <summary>The blocks of one track, left to right.</summary>
    public static Rectangle[] LevelSegments(Rectangle track, int barThickness)
    {
        int count = LevelSegmentCount(track.Width, barThickness);
        if (count <= 0)
        {
            return [];
        }

        int gap = LevelSegmentGap(barThickness);
        int segmentWidth = Math.Max(1, (track.Width - ((count - 1) * gap)) / count);
        var segments = new Rectangle[count];
        int x = track.Left;
        for (int i = 0; i < count; i++)
        {
            segments[i] = new Rectangle(x, track.Top, segmentWidth, track.Height);
            x += segmentWidth + gap;
        }

        return segments;
    }

    /// <summary>
    /// How many blocks are lit for a ratio: proportional, at least one for any
    /// traffic, never more than the track holds.
    /// </summary>
    public static int LevelLitSegments(int segmentCount, double ratio)    {
        if (segmentCount <= 0 || double.IsNaN(ratio) || ratio <= 0)
        {
            return 0;
        }

        double clamped = Math.Min(ratio, 1.0);
        int lit = (int)Math.Round(segmentCount * clamped, MidpointRounding.AwayFromZero);
        if (lit < MinimumVisibleFillPx)
        {
            lit = MinimumVisibleFillPx;
        }

        return Math.Min(lit, segmentCount);
    }

    /// <summary>
    /// Fits blocks of roughly <paramref name="block"/> along <paramref name="extent"/>.
    /// A block is shrunk when only one would fit but the track has room for two: the
    /// gaps are the point of a segmented style, so a single solid strip is wrong.
    /// </summary>
    private static (int Count, int Fitted) FitBlocks(int extent, int block, int gap)
    {
        block = Math.Max(1, block);
        gap = Math.Max(0, gap);

        // Room for two blocks and the gap between them? Then no block may be taller
        // than half the leftover.
        int twoBlockLimit = (extent - gap) / 2;
        if (twoBlockLimit >= 2 && block > twoBlockLimit)
        {
            block = twoBlockLimit;
        }

        int count = Math.Max(1, (extent + gap) / (block + gap));
        int fitted = Math.Max(1, (extent - ((count - 1) * gap)) / count);
        return (count, fitted);
    }

    /// <summary>Width of one block in the chunky segmented styles.</summary>
    public static int BlockWidth(int barThickness) => Math.Max(2, barThickness * 2);

    /// <summary>
    /// Blocks laid left to right across a track, separated by <paramref name="gap"/>.
    /// A track narrower than one block still yields a single block, so traffic is
    /// never invisible.
    /// </summary>
    public static Rectangle[] HorizontalSegments(Rectangle track, int blockWidth, int gap)
    {
        if (track.Width <= 0 || track.Height <= 0)
        {
            return [];
        }

        (int count, int fitted) = FitBlocks(track.Width, blockWidth, gap);
        gap = Math.Max(0, gap);
        var blocks = new Rectangle[count];
        int x = track.Left;
        for (int i = 0; i < count; i++)
        {
            blocks[i] = new Rectangle(x, track.Top, Math.Min(fitted, track.Right - x), track.Height);
            x += fitted + gap;
        }

        return blocks;
    }

    /// <summary>
    /// Blocks stacked up a vertical track, separated by <paramref name="gap"/>.
    /// Ordered from the bottom of the track upwards, so block 0 is the one nearest
    /// the taskbar edge and lighting "the first n" grows away from it.
    /// </summary>
    public static Rectangle[] VerticalSegments(Rectangle track, int blockHeight, int gap)
    {
        if (track.Width <= 0 || track.Height <= 0)
        {
            return [];
        }

        (int count, int fitted) = FitBlocks(track.Height, blockHeight, gap);
        gap = Math.Max(0, gap);
        var blocks = new Rectangle[count];
        int bottom = track.Bottom;
        for (int i = 0; i < count; i++)
        {
            int top = Math.Max(track.Top, bottom - fitted);
            blocks[i] = new Rectangle(track.Left, top, track.Width, bottom - top);
            bottom = top - gap;
        }

        return blocks;
    }

    /// <summary>
    /// Round dots centred along a track, left to right. The returned rectangles are
    /// the square bounds of each circle.
    /// </summary>
    public static Rectangle[] DotCells(Rectangle track, int diameter, int gap)
    {
        if (track.Width <= 0 || track.Height <= 0)
        {
            return [];
        }

        diameter = Math.Max(1, Math.Min(diameter, Math.Min(track.Width, track.Height)));
        gap = Math.Max(0, gap);
        int count = Math.Max(1, (track.Width + gap) / (diameter + gap));
        int fitted = Math.Max(1, Math.Min(diameter, (track.Width - ((count - 1) * gap)) / count));
        int top = track.Top + ((track.Height - fitted) / 2);
        var cells = new Rectangle[count];
        int x = track.Left;
        for (int i = 0; i < count; i++)
        {
            cells[i] = new Rectangle(x, top, fitted, fitted);
            x += fitted + gap;
        }

        return cells;
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

        switch (layout.Style)
        {
            case WidgetStyles.Level:
                DrawLevelMeters(graphics, visual, config, layout, theme);
                break;
            case WidgetStyles.Sidebar:
                DrawSidebarMeters(graphics, visual, config, layout, theme);
                break;
            case WidgetStyles.Neon:
                DrawNeonMeters(graphics, visual, config, layout, theme);
                break;
            case WidgetStyles.Segments:
                DrawSegmentMeters(graphics, visual, config, layout, theme, vertical: false);
                break;
            case WidgetStyles.SidebarSegments:
                DrawSegmentMeters(graphics, visual, config, layout, theme, vertical: true);
                break;
            case WidgetStyles.Dots:
                DrawDotMeters(graphics, visual, config, layout, theme);
                break;
            case WidgetStyles.Minimal:
                DrawMinimalMeters(graphics, visual, config, layout, theme);
                break;
            default:
                DrawSolidMeters(graphics, visual, config, layout, theme);
                break;
        }

        if (!config.ShowText)
        {
            return;
        }

        using var textBrush = new SolidBrush(theme.TextColor);
        using Font font = CreateFont(layout.FontSizePoints, layout.Scale, config);
        using StringFormat format = CreateTightFormat();

        DrawRowText(graphics, font, textBrush, format, layout.DownloadText, SpeedFormatter.FormatRow(true, visual.DownloadBytesPerSecond));
        DrawRowText(graphics, font, textBrush, format, layout.UploadText, SpeedFormatter.FormatRow(false, visual.UploadBytesPerSecond));
    }

    /// <summary>The classic look: solid tracks with a solid fill from the left.</summary>
    private static void DrawSolidMeters(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        graphics.FillRectangle(trackBrush, layout.DownloadBar);
        graphics.FillRectangle(trackBrush, layout.UploadBar);

        DrawFill(graphics, layout, config, visual, downloadBrush, uploadBrush);
    }

    /// <summary>Solid tracks, glow halo (always on for this style) and rounded caps.</summary>
    private static void DrawNeonMeters(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        graphics.FillRectangle(trackBrush, layout.DownloadBar);
        graphics.FillRectangle(trackBrush, layout.UploadBar);

        DrawFill(graphics, layout, config, visual, downloadBrush, uploadBrush, glow: true, rounded: true);
    }

    /// <summary>
    /// Chunky blocks instead of one continuous bar: unlit blocks in the track colour,
    /// lit ones in the traffic colour, with the user's segment gap between them.
    /// Vertical blocks grow upwards from the taskbar edge.
    /// </summary>
    private static void DrawSegmentMeters(
        Graphics graphics,
        WidgetVisual visual,
        MonitorConfig config,
        WidgetLayout layout,
        WidgetTheme theme,
        bool vertical)
    {
        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        DrawSegmentTrack(graphics, layout.DownloadBar, layout, config, visual.DownloadRatio, trackBrush, downloadBrush, vertical);
        DrawSegmentTrack(graphics, layout.UploadBar, layout, config, visual.UploadRatio, trackBrush, uploadBrush, vertical);
    }

    private static void DrawSegmentTrack(
        Graphics graphics,
        Rectangle track,
        WidgetLayout layout,
        MonitorConfig config,
        double ratio,
        Brush trackBrush,
        Brush fillBrush,
        bool vertical)
    {
        Rectangle[] blocks = vertical
            ? VerticalSegments(track, BlockWidth(layout.BarThickness), layout.SegmentGap)
            : HorizontalSegments(track, BlockWidth(layout.BarThickness), layout.SegmentGap);

        int lit = LevelLitSegments(blocks.Length, ratio);
        for (int i = 0; i < blocks.Length; i++)
        {
            bool isLit = i < lit;
            if (isLit && config.AccentGlow)
            {
                DrawGlow(graphics, blocks[i], ((SolidBrush)fillBrush).Color, layout.Scale);
            }

            FillShape(graphics, isLit ? fillBrush : trackBrush, blocks[i], config.RoundedBars, layout.Scale);
        }
    }

    /// <summary>A row of dots that light up from the left as traffic rises.</summary>
    private static void DrawDotMeters(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        DrawDotTrack(graphics, layout.DownloadBar, layout, visual.DownloadRatio, trackBrush, downloadBrush);
        DrawDotTrack(graphics, layout.UploadBar, layout, visual.UploadRatio, trackBrush, uploadBrush);
    }

    private static void DrawDotTrack(
        Graphics graphics,
        Rectangle track,
        WidgetLayout layout,
        double ratio,
        Brush trackBrush,
        Brush fillBrush)
    {
        Rectangle[] cells = DotCells(track, Math.Max(2, layout.BarThickness * 2), layout.SegmentGap);
        int lit = LevelLitSegments(cells.Length, ratio);
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            for (int i = 0; i < cells.Length; i++)
            {
                graphics.FillEllipse(i < lit ? fillBrush : trackBrush, cells[i]);
            }
        }
        finally
        {
            graphics.SmoothingMode = previous;
        }
    }

    /// <summary>Hairline fills with no track behind them.</summary>
    private static void DrawMinimalMeters(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        int downloadWidth = FillWidth(layout.DownloadBar.Width, visual.DownloadRatio);
        if (downloadWidth > 0)
        {
            FillShape(graphics, downloadBrush, Slice(layout.DownloadBar, downloadWidth), config.RoundedBars, layout.Scale);
        }

        int uploadWidth = FillWidth(layout.UploadBar.Width, visual.UploadRatio);
        if (uploadWidth > 0)
        {
            FillShape(graphics, uploadBrush, Slice(layout.UploadBar, uploadWidth), config.RoundedBars, layout.Scale);
        }
    }

    /// <summary>Vertical bars for the sidebar style, filled from the taskbar edge upwards.</summary>
    private static void DrawSidebarMeters(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        FillTrack(graphics, trackBrush, layout.DownloadBar, config, layout);
        FillTrack(graphics, trackBrush, layout.UploadBar, config, layout);

        int downloadHeight = FillWidth(layout.DownloadBar.Height, visual.DownloadRatio);
        if (downloadHeight > 0)
        {
            Rectangle fill = BottomSlice(layout.DownloadBar, downloadHeight);
            if (config.AccentGlow)
            {
                DrawGlow(graphics, fill, config.DownloadColor, layout.Scale);
            }

            FillShape(graphics, downloadBrush, fill, config.RoundedBars, layout.Scale);
        }

        int uploadHeight = FillWidth(layout.UploadBar.Height, visual.UploadRatio);
        if (uploadHeight > 0)
        {
            Rectangle fill = BottomSlice(layout.UploadBar, uploadHeight);
            if (config.AccentGlow)
            {
                DrawGlow(graphics, fill, config.UploadColor, layout.Scale);
            }

            FillShape(graphics, uploadBrush, fill, config.RoundedBars, layout.Scale);
        }
    }

    /// <summary>Segmented level meter: unlit blocks in the track colour, lit ones in the traffic colour.</summary>
    private static void DrawLevelMeters(Graphics graphics, WidgetVisual visual, MonitorConfig config, WidgetLayout layout, WidgetTheme theme)
    {
        using var trackBrush = new SolidBrush(theme.TrackColor);
        using var downloadBrush = new SolidBrush(config.DownloadColor);
        using var uploadBrush = new SolidBrush(config.UploadColor);

        DrawLevelTrack(graphics, layout.DownloadBar, layout, trackBrush, downloadBrush, config, visual.DownloadRatio);
        DrawLevelTrack(graphics, layout.UploadBar, layout, trackBrush, uploadBrush, config, visual.UploadRatio);
    }

    private static void DrawLevelTrack(
        Graphics graphics,
        Rectangle track,
        WidgetLayout layout,
        Brush trackBrush,
        Brush fillBrush,
        MonitorConfig config,
        double ratio)
    {
        Rectangle[] segments = LevelSegments(track, layout.BarThickness);
        int lit = LevelLitSegments(segments.Length, ratio);
        for (int i = 0; i < segments.Length; i++)
        {
            Rectangle block = segments[i];
            bool isLit = i < lit;
            if (isLit && config.AccentGlow)
            {
                DrawGlow(graphics, block, ((SolidBrush)fillBrush).Color, layout.Scale);
            }

            FillShape(graphics, isLit ? fillBrush : trackBrush, block, config.RoundedBars, layout.Scale);
        }
    }

    private static void DrawFill(
        Graphics graphics,
        WidgetLayout layout,
        MonitorConfig config,
        WidgetVisual visual,
        Brush downloadBrush,
        Brush uploadBrush,
        bool glow = false,
        bool rounded = false)
    {
        int downloadWidth = FillWidth(layout.DownloadBar.Width, visual.DownloadRatio);
        if (downloadWidth > 0)
        {
            Rectangle fill = Slice(layout.DownloadBar, downloadWidth);
            if (glow || config.AccentGlow)
            {
                DrawGlow(graphics, fill, config.DownloadColor, layout.Scale);
            }

            FillShape(graphics, downloadBrush, fill, rounded || config.RoundedBars, layout.Scale);
        }

        int uploadWidth = FillWidth(layout.UploadBar.Width, visual.UploadRatio);
        if (uploadWidth > 0)
        {
            Rectangle fill = Slice(layout.UploadBar, uploadWidth);
            if (glow || config.AccentGlow)
            {
                DrawGlow(graphics, fill, config.UploadColor, layout.Scale);
            }

            FillShape(graphics, uploadBrush, fill, rounded || config.RoundedBars, layout.Scale);
        }
    }

    /// <summary>Draws a track, rounded if the user asked for rounded caps.</summary>
    private static void FillTrack(Graphics graphics, Brush brush, Rectangle track, MonitorConfig config, WidgetLayout layout)
        => FillShape(graphics, brush, track, config.RoundedBars, layout.Scale);

    private static void FillShape(Graphics graphics, Brush brush, Rectangle bounds, bool rounded, float scale)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (!rounded)
        {
            graphics.FillRectangle(brush, bounds);
            return;
        }

        int radius = Math.Max(1, Math.Min(bounds.Height / 2, (int)Math.Round(2 * scale)));
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using GraphicsPath path = CreateRoundedPath(bounds, radius);
            graphics.FillPath(brush, path);
        }
        finally
        {
            graphics.SmoothingMode = previous;
        }
    }

    /// <summary>
    /// A soft halo around the fill. Drawn before the fill itself, in a few
    /// decreasing-alpha rings, so the edges glow instead of turning into a blob.
    /// </summary>
    private static void DrawGlow(Graphics graphics, Rectangle fill, Color color, float scale)
    {
        if (fill.Width <= 0 || fill.Height <= 0)
        {
            return;
        }

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingMode = CompositingMode.SourceOver;
        try
        {
            int baseSpread = Math.Max(1, (int)Math.Round(1.5 * scale));
            for (int ring = 2; ring >= 1; ring--)
            {
                int spread = baseSpread * ring;
                var halo = Rectangle.Inflate(fill, spread, spread);
                byte alpha = (byte)(ring == 2 ? 46 : 88);
                using var brush = new SolidBrush(Color.FromArgb(alpha, color));
                int radius = Math.Max(1, Math.Min(halo.Height / 2, (int)Math.Round(3 * scale)));
                using GraphicsPath path = CreateRoundedPath(halo, radius);
                graphics.FillPath(brush, path);
            }
        }
        finally
        {
            graphics.SmoothingMode = previous;
        }
    }

    private static void DrawRowText(Graphics graphics, Font font, Brush brush, StringFormat format, Rectangle bounds, string text)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        float textHeight = font.GetHeight(graphics);
        float top = bounds.Y + (bounds.Height - textHeight) / 2f;
        graphics.DrawString(text, font, brush, bounds.X, top, format);
    }

    private static Rectangle Slice(Rectangle source, int width)
        => new(source.X, source.Y, width, source.Height);

    /// <summary>The bottom part of a vertical track, so fills grow away from the taskbar edge.</summary>
    private static Rectangle BottomSlice(Rectangle source, int height)
        => new(source.X, source.Bottom - height, source.Width, height);

    private static Font CreateFont(float points, float scale, MonitorConfig? config = null)
    {
        float size = Math.Max(points * scale, MinimumFontSizePt);
        string family = FontCatalog.Resolve(config?.FontFamily);
        FontStyle style = FontCatalog.StyleFor(config?.FontBold ?? false, config?.FontItalic ?? false);

        try
        {
            return new Font(family, size, style, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            // A family that reports itself installed but has no such style, e.g. a
            // bold-only display font: fall back to the plain face rather than throw
            // on the render thread.
            return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

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
}
