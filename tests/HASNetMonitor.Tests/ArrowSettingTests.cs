using System.Drawing;
using System.Drawing.Imaging;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The arrow's size is a setting now, and the setting has to do what it says: bigger arrows
/// than the figure by default, a choice that visibly changes the drawing, and a hard stop at
/// the row's own height so a large choice cannot overlap the bar below it.
/// </summary>
public class ArrowSettingTests
{
    private static readonly WidgetVisual Sample = new(1024, 2048, 0.5, 0.25);

    private static MonitorConfig Config(int arrowPercent) => new MonitorConfig
    {
        DownloadColorHex = "#00FF00",
        UploadColorHex = "#FF0000",
        ShowText = true,
        ShowBackdrop = false,
        BarThicknessPx = 4,
        FontFamily = "Segoe UI",
        ArrowSizePercent = arrowPercent,
    }.Normalize();

    /// <summary>
    /// Vertical ink extent of the tinted arrow pixels inside a row. Kept strictly inside
    /// the row so the bar below cannot be mistaken for arrow ink.
    /// </summary>
    private static (int Top, int Bottom) ArrowInk(Frame frame, Rectangle row)
    {
        int top = int.MaxValue, bottom = int.MinValue;
        int first = row.Top;
        int last = row.Bottom;
        for (int y = first; y < last; y++)
        {
            for (int x = Math.Max(0, row.Left); x < Math.Min(frame.Bitmap.Width, row.Right); x++)
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
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (top, bottom);
    }

    /// <summary>Vertical ink extent of the grey figure pixels, past the arrow's column.</summary>
    private static (int Top, int Bottom) FigureInk(Frame frame, Rectangle row, int startX)
    {
        int top = int.MaxValue, bottom = int.MinValue;
        for (int y = row.Top; y < row.Bottom; y++)
        {
            for (int x = Math.Max(row.Left, startX); x < row.Right; x++)
            {
                Color c = frame.Bitmap.GetPixel(x, y);
                if (c.A < 60)
                {
                    continue;
                }

                if (Math.Abs(c.R - c.G) < 28 && Math.Abs(c.G - c.B) < 28)
                {
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (top, bottom);
    }

    /// <summary>The arrow's drawn ink height in the download row of a rendered frame.</summary>
    private static int DrawnArrowHeight(int arrowPercent)
    {
        using Frame frame = Frame.Render(Config(arrowPercent), Sample);
        (int top, int bottom) = ArrowInk(frame, frame.Layout.DownloadText);
        Assert.True(top <= bottom, $"no arrow ink at {arrowPercent}%");
        return bottom - top + 1;
    }

    [Fact]
    public void TheDefaultArrowIsVisiblyBiggerThanTheFigure()
    {
        // The complaint that started this: the arrow read as a small tick beside the
        // figure. "At least as tall" was not enough, so the default must be clearly bigger.
        using Frame frame = Frame.Render(Config(MonitorConfig.DefaultArrowSizePercent), Sample);
        Rectangle row = frame.Layout.DownloadText;

        using var surface = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(surface);
        using Font font = WidgetRenderer.CreateFont(frame.Layout.FontEmPixels, frame.Config);
        float offset = WidgetRenderer.RowNumberOffset(
            graphics,
            font,
            StringFormat.GenericTypographic,
            frame.Layout.Scale,
            frame.Layout.ArrowSizePercent,
            row.Height);

        (int arrowTop, int arrowBottom) = ArrowInk(frame, row);
        (int figureTop, int figureBottom) = FigureInk(frame, row, row.Left + (int)Math.Ceiling(offset));

        int arrowHeight = arrowBottom - arrowTop + 1;
        int figureHeight = figureBottom - figureTop + 1;
        Assert.True(
            arrowHeight >= figureHeight * 1.15,
            $"the default arrow ink {arrowHeight}px is not visibly bigger than the figure's {figureHeight}px");
    }

    [Fact]
    public void ABiggerSettingDrawsABiggerArrow()
    {
        int small = DrawnArrowHeight(100);
        int normal = DrawnArrowHeight(MonitorConfig.DefaultArrowSizePercent);
        int large = DrawnArrowHeight(MonitorConfig.MaxArrowSizePercent);

        Assert.True(normal > small, $"default {normal}px does not beat 100% {small}px");
        Assert.True(large > normal, $"200% {large}px does not beat the default {normal}px");
    }

    [Fact]
    public void TheArrowNeverTakesMoreRoomThanItsRow()
    {
        // The arrow is drawn in the figure's row, so a tall arrow must stop there; growing
        // past it would put the glyph over the bar beneath. Checked at the sizing API (the
        // em it may take for a given row) and in the drawn frame.
        foreach (int percent in new[] { 100, 150, 200, 300 })
        {
            MonitorConfig config = Config(percent);
            const int rowHeight = 16;

            int arrowEm = WidgetRenderer.ArrowEmPixels(12, config, rowHeight);
            Assert.True(
                WidgetRenderer.ArrowInkHeight(config, arrowEm) <= rowHeight + 0.5,
                $"{percent}%: em {arrowEm} gives arrow ink taller than the {rowHeight}px row");
            Assert.True(arrowEm >= 12, $"{percent}%: arrow em {arrowEm} shrank below the figure");
        }

        using Frame frame = Frame.Render(Config(MonitorConfig.MaxArrowSizePercent), Sample);
        foreach (Rectangle row in new[] { frame.Layout.DownloadText, frame.Layout.UploadText })
        {
            (int top, int bottom) = ArrowInk(frame, row);
            Assert.True(top <= bottom, "no arrow ink");
            Assert.True(
                top >= row.Top && bottom < row.Bottom,
                $"arrow ink {top}..{bottom} leaves its row {row.Top}..{row.Bottom}");
        }
    }

    [Fact]
    public void TheArrowSizeNeverShrinksTheFigureToMakeRoom()
    {
        // The arrow must not cost the figure a font size: the row is the figure's row and
        // the arrow fits inside it.
        using Frame baseline = Frame.Render(Config(MonitorConfig.MinArrowSizePercent), Sample);
        using Frame big = Frame.Render(Config(MonitorConfig.MaxArrowSizePercent), Sample);

        Assert.Equal(baseline.Layout.FontEmPixels, big.Layout.FontEmPixels);
        Assert.Equal(baseline.Layout.Size.Height, big.Layout.Size.Height);
    }

    [Fact]
    public void TheSettingAppliesToTheLayoutThatWasBuilt()
    {
        WidgetLayout layout = new WidgetRenderer().ComputeLayout(Config(160), 1f, 48);
        Assert.Equal(160, layout.ArrowSizePercent);

        // Clamped at both ends, so a hand-edited config cannot leave the layout lying.
        Assert.Equal(
            MonitorConfig.MaxArrowSizePercent,
            new WidgetRenderer().ComputeLayout(Config(9999), 1f, 48).ArrowSizePercent);
        Assert.Equal(
            MonitorConfig.MinArrowSizePercent,
            new WidgetRenderer().ComputeLayout(Config(1), 1f, 48).ArrowSizePercent);
    }

    [Fact]
    public void TheBiggerArrowStillLeavesTheFigureInsideTheWidget()
    {
        using Frame frame = Frame.Render(Config(MonitorConfig.MaxArrowSizePercent), Sample);
        WidgetLayout layout = frame.Layout;

        Assert.True(layout.DownloadText.Right <= layout.Size.Width);
        Assert.True(layout.UploadText.Right <= layout.Size.Width);
        Assert.Equal(layout.Size.Width, frame.Bitmap.Width);
    }

    [Fact]
    public void TheSettingDefaultsToBiggerAndSurvivesTheConfigFile()
    {
        var fresh = new MonitorConfig().Normalize();
        Assert.Equal(MonitorConfig.DefaultArrowSizePercent, fresh.ArrowSizePercent);
        Assert.True(fresh.ArrowSizePercent > MonitorConfig.MinArrowSizePercent, "the default is not bigger than the figure");

        fresh.ArrowSizePercent = 200;
        Assert.Equal(200, MonitorConfig.FromJson(fresh.ToJson()).ArrowSizePercent);
        Assert.Equal(200, fresh.Clone().ArrowSizePercent);

        var copy = new MonitorConfig();
        copy.CopyFrom(fresh);
        Assert.Equal(200, copy.ArrowSizePercent);

        // Out-of-range values are clamped, not trusted.
        Assert.Equal(MonitorConfig.MaxArrowSizePercent, new MonitorConfig { ArrowSizePercent = 5000 }.Normalize().ArrowSizePercent);
        Assert.Equal(MonitorConfig.MinArrowSizePercent, new MonitorConfig { ArrowSizePercent = -20 }.Normalize().ArrowSizePercent);
    }

    [Fact]
    public void TheSettingsListOffersTheDefaultAndKeepsACustomValue()
    {
        IReadOnlyList<int> presets = SettingsForm.ArrowSizeChoiceKeys(MonitorConfig.DefaultArrowSizePercent);
        Assert.Contains(MonitorConfig.DefaultArrowSizePercent, presets);
        Assert.Contains(MonitorConfig.MinArrowSizePercent, presets);
        Assert.Contains(MonitorConfig.MaxArrowSizePercent, presets);
        Assert.Equal(presets.OrderBy(p => p), presets);

        // A percentage set by hand is kept as its own entry instead of being reset.
        IReadOnlyList<int> custom = SettingsForm.ArrowSizeChoiceKeys(142);
        Assert.Contains(142, custom);
        Assert.Equal(custom.OrderBy(p => p), custom);

        // Out of range selections are clamped into the list.
        Assert.Contains(MonitorConfig.MaxArrowSizePercent, SettingsForm.ArrowSizeChoiceKeys(int.MaxValue));
        Assert.Contains(MonitorConfig.MinArrowSizePercent, SettingsForm.ArrowSizeChoiceKeys(int.MinValue));
    }

    [Fact]
    public void TheSettingsDialogCarriesTheArrowChoice()
    {
        // The dialog is a picker, so the plumbing is checked at the source: the box is
        // built from the presets and the edited value leaves through ArrowSizePercent.
        string source = SourceText.Read("UI", "SettingsForm.cs");

        Assert.Contains("ArrowSizePercent = SelectedArrowSize()", source, StringComparison.Ordinal);
        Assert.Contains("CreateArrowSizeBox(config.ArrowSizePercent)", source, StringComparison.Ordinal);
        Assert.Contains("AddRow(look, \"Arrow size:\", _arrowSize)", source, StringComparison.Ordinal);
    }
}
