using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

/// <summary>One rendered frame plus the small helpers every rendering test needs.</summary>
internal sealed class Frame : IDisposable
{
    private Frame(Bitmap bitmap, WidgetLayout layout, MonitorConfig config, WidgetVisual visual, float scale, int maxHeight, TaskbarOrientation orientation)
    {
        Bitmap = bitmap;
        Layout = layout;
        Config = config;
        Visual = visual;
        Scale = scale;
        MaxHeight = maxHeight;
        Orientation = orientation;
    }

    public Bitmap Bitmap { get; }

    public WidgetLayout Layout { get; }

    public MonitorConfig Config { get; }

    public WidgetVisual Visual { get; }

    public float Scale { get; }

    public int MaxHeight { get; }

    public TaskbarOrientation Orientation { get; }

    public static Frame Render(
        MonitorConfig? config = null,
        WidgetVisual? visual = null,
        bool dark = true,
        TaskbarOrientation orientation = TaskbarOrientation.Horizontal,
        float scale = 1f,
        int maxHeight = 48)
    {
        config ??= TestSupport.BarsOnly();
        visual ??= new WidgetVisual(0, 0, 0, 0);
        var renderer = new WidgetRenderer();
        // The layout reserves the chip block from the same reading the frame draws, so a
        // visual that carries statistics gets chips and one that does not gets none.
        WidgetLayout layout = renderer.ComputeLayout(config, scale, maxHeight, visual.Value.Stats);
        Bitmap bitmap = renderer.Render(
            visual.Value,
            config,
            layout,
            new WidgetTheme(dark),
            orientation);

        return new Frame(bitmap, layout, config, visual.Value, scale, maxHeight, orientation);
    }

    public int RowCentre(Rectangle bar) => bar.Y + (bar.Height / 2);

    public Color BarPixel(Rectangle bar, int x) => Bitmap.GetPixel(x, RowCentre(bar));

    /// <summary>Pixels of exactly this colour in the bar's row: the painted fill length.</summary>
    public int CountFillPixels(Rectangle bar, Color fill)
    {
        int y = RowCentre(bar);
        int count = 0;
        for (int x = bar.Left; x < bar.Right; x++)
        {
            count += Pixels.IsOpaque(Bitmap.GetPixel(x, y), fill) ? 1 : 0;
        }

        return count;
    }

    public Frame Rerender() => Render(Config, Visual, true, Orientation, Scale, MaxHeight);

    public void Dispose() => Bitmap.Dispose();
}

internal static class Pixels
{
    public static bool SameRgb(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    /// <summary>
    /// True when the pixel's colour matches within a few steps. The widget paints into a
    /// premultiplied bitmap, so an opaque colour drawn at 254/255 comes back one or two
    /// steps darker than the brush that painted it.
    /// </summary>
    public static bool CloseRgb(Color a, Color b, int tolerance = 4)
        => Math.Abs(a.R - b.R) <= tolerance
            && Math.Abs(a.G - b.G) <= tolerance
            && Math.Abs(a.B - b.B) <= tolerance;

    /// <summary>True when the pixel is exactly this solid colour (no alpha blending).</summary>
    public static bool IsOpaque(Color actual, Color expected)
        => actual.A == 255 && SameRgb(actual, expected);
}

/// <summary>
/// The invariants every frame must satisfy, checked at the end of each rendering test.
/// A frame that breaks one of these is a bug in the drawing code, not in the test.
/// </summary>
internal static class Invariants
{
    public static void AssertFrame(Frame frame, WidgetVisual visual)
    {
        if (frame.Orientation == TaskbarOrientation.Vertical)
        {
            // A vertical taskbar gets the same picture rotated, so the frame is the
            // layout with its axes swapped.
            Assert.Equal(new Size(frame.Layout.Size.Height, frame.Layout.Size.Width), frame.Bitmap.Size);
            using Frame horizontal = Frame.Render(
                frame.Config, visual, dark: true, maxHeight: frame.MaxHeight, scale: frame.Scale);
            AssertCore(horizontal, visual);
            return;
        }

        AssertCore(frame, visual);
    }

    private static void AssertCore(Frame frame, WidgetVisual visual)
    {
        Assert.Equal(frame.Layout.Size, frame.Bitmap.Size);

        // Nothing is drawn outside the frame, and the bars read top to bottom.
        Assert.True(frame.Layout.DownloadBar.X >= 0 && frame.Layout.DownloadBar.Right <= frame.Bitmap.Width);
        Assert.True(frame.Layout.UploadBar.X >= 0 && frame.Layout.UploadBar.Bottom <= frame.Bitmap.Height);
        Assert.True(frame.Layout.DownloadBar.Y < frame.Layout.UploadBar.Y);

        // What is painted is exactly what the ratio asks for: never wider than the
        // track, never wider than a full bar.
        AssertFillWithinTrack(frame, frame.Layout.DownloadBar, visual.DownloadRatio, frame.Config.DownloadColor);
        AssertFillWithinTrack(frame, frame.Layout.UploadBar, visual.UploadRatio, frame.Config.UploadColor);

        // The background must stay transparent like the clock so the real taskbar
        // shows through. The hit wash is the only thing allowed on the padding.
        if (!frame.Config.ShowBackdrop)
        {
            Color corner = frame.Bitmap.GetPixel(0, 0);
            Assert.True(
                corner.A <= WidgetRenderer.HitWashAlpha + 1,
                $"background alpha {corner.A} makes the widget opaque over the taskbar");
        }

        // Rendering the same state twice must give the same picture, otherwise the
        // widget would shimmer between frames.
        using Frame again = frame.Rerender();
        Assert.Equal(frame.Layout.Size, again.Layout.Size);
        for (int x = 0; x < frame.Bitmap.Width; x += 7)
        {
            for (int y = 0; y < frame.Bitmap.Height; y += 5)
            {
                Assert.Equal(frame.Bitmap.GetPixel(x, y).ToArgb(), again.Bitmap.GetPixel(x, y).ToArgb());
            }
        }
    }

    private static void AssertFillWithinTrack(Frame frame, Rectangle bar, double ratio, Color fill)
    {
        int expected = WidgetRenderer.FillWidth(bar.Width, ratio);
        int actual = frame.CountFillPixels(bar, fill);

        Assert.InRange(expected, 0, bar.Width);
        Assert.Equal(expected, actual);

        if (expected == 0)
        {
            return;
        }

        // The fill is a solid block that starts at the left edge of the track.
        Assert.True(Pixels.IsOpaque(frame.BarPixel(bar, bar.Left), fill));

        if (expected < bar.Width)
        {
            Color past = frame.BarPixel(bar, bar.Left + expected);
            Assert.False(
                Pixels.IsOpaque(past, fill),
                "the bar is painted past the width its ratio asks for");
        }
    }
}

internal static class TestSupport
{
    public static MonitorConfig BarsOnly(string download = "#00FF00", string upload = "#FF0000") => new MonitorConfig
    {
        DownloadColorHex = download,
        UploadColorHex = upload,
        ShowText = false,
        ShowBackdrop = false,
        BarThicknessPx = 4,
    }.Normalize();
}
