using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.Tests;

/// <summary>
/// The drawn <see cref="ArrowStyles.Bar"/> arrow is built from geometry rather than taken
/// from a font glyph, so no font hint decides which way it faces: the head has to be built
/// in the right direction explicitly. These tests read the rendered pixels and check the
/// shape a user actually sees, not the intent of the drawing code.
/// </summary>
public class DrawnArrowDirectionTests
{
    private const float Scale = 6f;

    [Fact]
    public void DownloadArrowPointsDown()
    {
        List<int> widths = RowWidths(isDownload: true);

        Assert.True(widths.Count > 3, "the drawn arrow drew no ink");

        // A downward head is widest at its top and narrows to a point at the bottom.
        Assert.True(
            widths[0] > widths[^1] * 2,
            $"download head should narrow downwards, but the first row is {widths[0]} px and the last {widths[^1]} px");
    }

    [Fact]
    public void UploadArrowPointsUp()
    {
        List<int> widths = RowWidths(isDownload: false);

        Assert.True(widths.Count > 3, "the drawn arrow drew no ink");

        // An upward head is narrow at its top and widest at its bottom.
        Assert.True(
            widths[^1] > widths[0] * 2,
            $"upload head should widen downwards, but the first row is {widths[0]} px and the last {widths[^1]} px");
    }

    [Fact]
    public void TheArrowComesToAPointAtTheDirectionOfTravel()
    {
        // Independent of stem length: the sharpest row is the tip. A download arrow tapers
        // to a point at its bottom, an upload arrow at its top.
        Assert.True(PointIsInBottomQuarter(isDownload: true), "download arrow's tip is not at its bottom");
        Assert.False(PointIsInBottomQuarter(isDownload: false), "upload arrow's tip is not at its top");
    }

    [Fact]
    public void TheArrowKeepsItsDirectionInBothOrientations()
    {
        foreach (TaskbarOrientation orientation in new[] { TaskbarOrientation.Horizontal, TaskbarOrientation.Vertical })
        {
            Assert.True(
                PointIsInBottomQuarter(isDownload: true, orientation),
                $"download arrow points the wrong way when the taskbar is {orientation}");
        }
    }

    private static bool PointIsInBottomQuarter(bool isDownload, TaskbarOrientation orientation = TaskbarOrientation.Horizontal)
    {
        List<int> widths = RowWidths(isDownload, orientation);
        int narrowest = 0;
        for (int i = 1; i < widths.Count; i++)
        {
            if (widths[i] < widths[narrowest])
            {
                narrowest = i;
            }
        }

        return narrowest >= (widths.Count * 3) / 4;
    }

    [Fact]
    public void TheTwoDrawnArrowsAreNotIdentical()
    {
        // The original bug: both directions shared one head shape, so the download arrow
        // was drawn as an upward arrow. Identical silhouettes mean the direction flag is
        // being ignored.
        Assert.NotEqual(
            string.Join(",", RowWidths(isDownload: true)),
            string.Join(",", RowWidths(isDownload: false)));
    }

    private static List<int> RowWidths(bool isDownload, TaskbarOrientation orientation = TaskbarOrientation.Horizontal)
        => ScanArrow(isDownload, orientation).Widths;

    /// <summary>
    /// Widths of the arrow's ink, one entry per scan row, limited to the columns the arrow
    /// itself occupies so the text drawn after it cannot distort the measurement.
    /// </summary>
    private static (List<int> Widths, int Columns) ScanArrow(bool isDownload, TaskbarOrientation orientation)
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = new MonitorConfig
        {
            Style = WidgetStyles.Bars,
            ArrowStyle = ArrowStyles.Bar,
            ShowText = true,
            ShowBackdrop = false,
        }.Normalize();

        WidgetLayout layout = renderer.ComputeLayout(config, Scale, 0);
        Rectangle row = isDownload ? layout.DownloadText : layout.UploadText;

        using Bitmap frame = renderer.Render(
            new WidgetVisual(0, 0, 0, 0), config, layout, WidgetTheme.Dark, orientation);

        // The arrow leads its row, so its own box is at most one arrow wide at the left edge.
        int arrowColumns = Math.Max(1, (int)(layout.BarThickness * 3));
        int limit = Math.Min(row.Right, row.Left + arrowColumns);

        var widths = new List<int>();
        for (int y = row.Top; y < row.Bottom; y++)
        {
            int left = -1;
            int right = -1;
            for (int x = row.Left; x < limit; x++)
            {
                if (frame.GetPixel(x, y).A > 200)
                {
                    if (left < 0)
                    {
                        left = x;
                    }

                    right = x;
                }
            }

            if (left >= 0)
            {
                widths.Add(right - left + 1);
            }
        }

        return (widths, arrowColumns);
    }
}
