using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HASNetMonitor.Core;

namespace HASNetMonitor.Rendering;

/// <summary>
/// The picture shown in the settings dialog: the widget drawn at twice normal size on
/// a mock dark taskbar, so a style or font can be judged before it is applied.
/// Kept out of the dialog so it can be checked without a window.
/// </summary>
internal static class TaskbarPreview
{
    /// <summary>Sample traffic that shows both bars at clearly different lengths.</summary>
    public static readonly WidgetVisual Sample = new(
        DownloadBytesPerSecond: 1.85 * 1024 * 1024,
        UploadBytesPerSecond: 340 * 1024,
        DownloadRatio: 0.55,
        UploadRatio: 0.2);

    public static Bitmap Render(MonitorConfig config, Size canvas, float scale, int maxHeight)
    {
        var renderer = new WidgetRenderer();
        WidgetLayout layout = renderer.ComputeLayout(config, scale, maxHeight);

        using Bitmap widget = renderer.Render(Sample, config, layout, WidgetTheme.Dark, TaskbarOrientation.Horizontal);

        var picture = new Bitmap(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height));
        using (Graphics graphics = Graphics.FromImage(picture))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Same hint as the widget, so the mock clock and the widget's own glyphs are
            // rasterised alike and the font can honestly be judged here.
            graphics.TextRenderingHint = WidgetRenderer.GlyphHint;

            var strip = new Rectangle(0, 0, picture.Width, picture.Height);
            using (var backdrop = new LinearGradientBrush(
                       strip,
                       Color.FromArgb(38, 38, 42),
                       Color.FromArgb(22, 22, 25),
                       90f))
            {
                graphics.FillRectangle(backdrop, strip);
            }

            using (var edge = new Pen(Color.FromArgb(60, config.DownloadColor), 1))
            {
                graphics.DrawLine(edge, 0, 0, picture.Width, 0);
            }

            using (var clockFont = new Font(FontCatalog.Resolve(FontCatalog.Automatic), 9.5f))
            using (var clockBrush = new SolidBrush(Color.FromArgb(220, 235, 235, 235)))
            {
                const string Clock = "10:42";
                SizeF clockSize = graphics.MeasureString(Clock, clockFont);
                graphics.DrawString(
                    Clock,
                    clockFont,
                    clockBrush,
                    picture.Width - clockSize.Width - 12,
                    (picture.Height - clockSize.Height) / 2);
            }

            int y = Math.Max(0, (picture.Height - widget.Height) / 2);
            graphics.DrawImage(widget, 16, y, widget.Width, widget.Height);
        }

        return picture;
    }
}
