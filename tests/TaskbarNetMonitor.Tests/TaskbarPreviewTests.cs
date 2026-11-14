using System.Drawing;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.Rendering;
using Xunit;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// The settings preview must show every option the dialog offers, and must never be
/// the reason the dialog fails to open.
/// </summary>
public sealed class TaskbarPreviewTests
{
    private static readonly Size Canvas = new(420, 96);

    private static MonitorConfig Config(string style, string order)
    {
        MonitorConfig config = TestSupport.BarsOnly().Clone();
        config.Style = style;
        config.SortOrder = order;
        config.ShowText = true;
        return config.Normalize();
    }

    [Theory]
    [InlineData(WidgetStyles.Bars)]
    [InlineData(WidgetStyles.Level)]
    [InlineData(WidgetStyles.Sidebar)]
    [InlineData(WidgetStyles.Neon)]
    public void EveryStyleHasAVisiblePreview(string style)
    {
        foreach (string order in StackOrder.All)
        {
            using Bitmap preview = TaskbarPreview.Render(Config(style, order), Canvas, 2f, 128);

            Assert.Equal(Canvas, preview.Size);

            // The widget really is in the picture: both configured fill colours appear.
            MonitorConfig config = Config(style, order);
            Assert.True(CountPixels(preview, config.DownloadColor) > 0, $"{style}/{order}: download colour missing");
            Assert.True(CountPixels(preview, config.UploadColor) > 0, $"{style}/{order}: upload colour missing");

            // And it is not a blank strip: the mock taskbar has its own shades.
            Assert.True(preview.GetPixel(preview.Width - 2, preview.Height / 2).A == 255);
        }
    }

    [Fact]
    public void ThePreviewFollowsTheStackingOrder()
    {
        using Bitmap down = TaskbarPreview.Render(Config(WidgetStyles.Bars, StackOrder.DownloadFirst), Canvas, 2f, 128);
        using Bitmap up = TaskbarPreview.Render(Config(WidgetStyles.Bars, StackOrder.UploadFirst), Canvas, 2f, 128);

        int downloadRow = FirstRowWith(down, Color.FromArgb(0, 255, 0));
        int uploadRow = FirstRowWith(down, Color.FromArgb(255, 0, 0));
        Assert.True(downloadRow >= 0 && uploadRow >= 0);
        Assert.True(downloadRow < uploadRow, "download first puts the download row on top");

        int uploadRowUp = FirstRowWith(up, Color.FromArgb(255, 0, 0));
        int downloadRowUp = FirstRowWith(up, Color.FromArgb(0, 255, 0));
        Assert.True(uploadRowUp < downloadRowUp, "upload first puts the upload row on top");
    }

    [Fact]
    public void ThePreviewSurvivesExtremeSettings()
    {
        MonitorConfig config = Config(WidgetStyles.Sidebar, StackOrder.UploadFirst);
        config.FontFamily = FontCatalog.InstalledFamilies[0];
        config.FontBold = true;
        config.FontItalic = true;
        config.RoundedBars = true;
        config.AccentGlow = true;
        config.FontSizePt = 24;
        config.BarThicknessPx = 4;
        config.ShowBackdrop = true;
        config.Normalize();

        using Bitmap preview = TaskbarPreview.Render(config, Canvas, 2f, 128);
        Assert.Equal(Canvas, preview.Size);

        // A canvas too small for anything must still give a picture, not an exception.
        using Bitmap tiny = TaskbarPreview.Render(config, new Size(20, 8), 2f, 128);
        Assert.Equal(new Size(20, 8), tiny.Size);

        using Bitmap zero = TaskbarPreview.Render(config, Size.Empty, 2f, 128);
        Assert.True(zero.Width >= 1 && zero.Height >= 1);
    }

    [Fact]
    public void ThePreviewIsDeterministic()
    {
        using Bitmap first = TaskbarPreview.Render(Config(WidgetStyles.Level, StackOrder.DownloadFirst), Canvas, 2f, 128);
        using Bitmap second = TaskbarPreview.Render(Config(WidgetStyles.Level, StackOrder.DownloadFirst), Canvas, 2f, 128);

        for (int x = 0; x < Canvas.Width; x += 5)
        {
            for (int y = 0; y < Canvas.Height; y += 3)
            {
                Assert.Equal(first.GetPixel(x, y).ToArgb(), second.GetPixel(x, y).ToArgb());
            }
        }
    }

    private static int CountPixels(Bitmap bitmap, Color colour)
    {
        int count = 0;
        for (int x = 0; x < bitmap.Width; x++)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 255 && pixel.R == colour.R && pixel.G == colour.G && pixel.B == colour.B)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int FirstRowWith(Bitmap bitmap, Color colour)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 255 && pixel.R == colour.R && pixel.G == colour.G && pixel.B == colour.B)
                {
                    return y;
                }
            }
        }

        return -1;
    }
}
