using System.Drawing;
using HASNetMonitor.Core;

namespace HASNetMonitor.Tests;

public class TaskbarGeometryTests
{
    // A 1920x1080 screen with the taskbar across the bottom (like Win11 at 100%).
    private static readonly PixelRect BottomTaskbar = new(0, 1032, 1920, 48);
    private static readonly PixelRect BottomTray = new(1700, 1032, 220, 48);
    private static readonly Size Layout = new(72, 44);

    [Fact]
    public void WidgetSitsJustBeforeTheNotificationAreaAndIsCentredVertically()
    {
        PixelRect? placement = TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, offsetFromTray: 6);

        Assert.NotNull(placement);
        PixelRect rect = placement.Value;
        Assert.Equal(1700 - 6 - 72, rect.Left);
        Assert.Equal(1034, rect.Top);
        Assert.Equal(72, rect.Width);
        Assert.Equal(44, rect.Height);

        // Centred on the 48px taskbar: two pixels spare on each side.
        Assert.Equal(2, rect.Top - BottomTaskbar.Top);
        Assert.Equal(2, BottomTaskbar.Bottom - rect.Bottom);
    }

    [Fact]
    public void PlacementNeverCoversTheNotificationArea()
    {
        foreach (int offset in new[] { 0, 2, 6, 24, 64 })
        {
            PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, offset)!.Value;
            Assert.True(rect.Right <= BottomTray.Left, $"offset {offset} puts the widget on top of the tray");
        }
    }

    [Fact]
    public void TopTaskbarIsPlacedTheSameWay()
    {
        var taskbar = new PixelRect(0, 0, 1920, 48);
        var tray = new PixelRect(1700, 0, 220, 48);

        PixelRect rect = TaskbarGeometry.ComputePlacement(taskbar, tray, Layout, offsetFromTray: 6)!.Value;

        Assert.Equal(1700 - 6 - 72, rect.Left);
        Assert.Equal(2, rect.Top);
    }

    [Fact]
    public void VerticalTaskbarIsPlacedAboveTheTrayWithSwappedSize()
    {
        var taskbar = new PixelRect(0, 0, 48, 1080);
        var tray = new PixelRect(0, 780, 48, 300);

        PixelRect rect = TaskbarGeometry.ComputePlacement(taskbar, tray, Layout, offsetFromTray: 6)!.Value;

        Assert.Equal(44, rect.Width);
        Assert.Equal(72, rect.Height);
        Assert.Equal(780 - 6 - 72, rect.Top);
        Assert.Equal(2, rect.Left);
    }

    [Fact]
    public void OrientationIsDecidedByTheTaskbarShape()
    {
        Assert.Equal(TaskbarOrientation.Horizontal, TaskbarGeometry.OrientationOf(BottomTaskbar));
        Assert.Equal(TaskbarOrientation.Vertical, TaskbarGeometry.OrientationOf(new PixelRect(0, 0, 48, 1080)));
        Assert.Equal(TaskbarOrientation.Horizontal, TaskbarGeometry.OrientationOf(new PixelRect(0, 0, 1920, 40)));
    }

    [Fact]
    public void VerticalWidgetSizeSwapsTheLayoutSize()
    {
        Assert.Equal(Layout, TaskbarGeometry.WidgetSizeFor(TaskbarOrientation.Horizontal, Layout));
        Assert.Equal(new Size(44, 72), TaskbarGeometry.WidgetSizeFor(TaskbarOrientation.Vertical, Layout));
    }

    [Fact]
    public void MissingTrayFallsBackToAFixedInsetFromTheEnd()
    {
        PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, null, Layout, offsetFromTray: 6)!.Value;

        Assert.Equal(BottomTaskbar.Right - TaskbarGeometry.TrayFallbackInset - Layout.Width, rect.Left);
    }

    [Fact]
    public void ATrayFromAnotherTaskbarIsIgnored()
    {
        // Same shape, but sitting on a second monitor far to the right: using it
        // would put the widget outside the taskbar we were handed.
        var foreignTray = new PixelRect(3820, 1032, 220, 48);

        PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, foreignTray, Layout, offsetFromTray: 6)!.Value;

        Assert.Equal(BottomTaskbar.Right - TaskbarGeometry.TrayFallbackInset - Layout.Width, rect.Left);
        Assert.InRange(rect.Right, 0, BottomTaskbar.Right);
    }

    [Fact]
    public void ATrayTallerThanTheTaskbarIsIgnored()
    {
        var oddTray = new PixelRect(1700, 1000, 220, 40);

        PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, oddTray, Layout, offsetFromTray: 6)!.Value;

        Assert.Equal(BottomTaskbar.Right - TaskbarGeometry.TrayFallbackInset - Layout.Width, rect.Left);
    }

    [Fact]
    public void AHugeOffsetIsClampedInsideTheTaskbar()
    {
        PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, offsetFromTray: 5000)!.Value;

        Assert.Equal(BottomTaskbar.Left + TaskbarGeometry.DefaultMargin, rect.Left);
        Assert.True(rect.Right <= BottomTaskbar.Right);
    }

    [Fact]
    public void ANegativeOffsetIsTreatedAsZero()
    {
        PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, offsetFromTray: -40)!.Value;

        Assert.Equal(1700 - 72, rect.Left);
    }

    [Fact]
    public void ATaskbarTooNarrowToHoldTheWidgetReturnsNull()
    {
        var narrow = new PixelRect(0, 1000, 40, 48);

        Assert.Null(TaskbarGeometry.ComputePlacement(narrow, null, Layout, offsetFromTray: 6));
    }

    [Fact]
    public void DegenerateInputsReturnNullInsteadOfThrowing()
    {
        Assert.Null(TaskbarGeometry.ComputePlacement(default, BottomTray, Layout, 6));
        Assert.Null(TaskbarGeometry.ComputePlacement(new PixelRect(0, 0, 1920, 0), BottomTray, Layout, 6));
        Assert.Null(TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Size.Empty, 6));
        Assert.Null(TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, new Size(-10, 5), 6));
    }

    [Fact]
    public void ATrayThatSpansTheWholeTaskbarStillPlacesTheWidgetInsideIt()
    {
        var wholeTaskbarTray = new PixelRect(0, 1032, 1920, 48);

        PixelRect rect = TaskbarGeometry.ComputePlacement(BottomTaskbar, wholeTaskbarTray, Layout, offsetFromTray: 6)!.Value;

        Assert.InRange(rect.Left, BottomTaskbar.Left + TaskbarGeometry.DefaultMargin, BottomTaskbar.Right);
        Assert.True(rect.Right <= BottomTaskbar.Right);
    }

    [Fact]
    public void PlacementIsDeterministic()
    {
        PixelRect? first = TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, 6);
        PixelRect? second = TaskbarGeometry.ComputePlacement(BottomTaskbar, BottomTray, Layout, 6);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheWidgetAlwaysStaysInsideItsTaskbar()
    {
        var taskbars = new[]
        {
            BottomTaskbar,
            new PixelRect(0, 0, 1920, 48),
            new PixelRect(0, 0, 48, 1080),
            new PixelRect(1872, 0, 48, 1080),
            new PixelRect(0, 500, 800, 48),
        };

        foreach (PixelRect taskbar in taskbars)
        {
            foreach (int offset in new[] { 0, 6, 64, 4000 })
            {
                PixelRect? maybe = TaskbarGeometry.ComputePlacement(taskbar, null, Layout, offset);
                if (maybe is not { } rect)
                {
                    continue;
                }

                Assert.InRange(rect.Left, taskbar.Left, taskbar.Right);
                Assert.InRange(rect.Right, taskbar.Left, taskbar.Right);
                Assert.InRange(rect.Top, taskbar.Top, taskbar.Bottom);
                Assert.InRange(rect.Bottom, taskbar.Top, taskbar.Bottom);
            }
        }
    }

    [Fact]
    public void PixelRectBasicsBehaveAsExpected()
    {
        var rect = new PixelRect(10, 20, 30, 40);

        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.False(rect.IsEmpty);
        Assert.True(default(PixelRect).IsEmpty);
        Assert.True(rect.IntersectsWith(new PixelRect(39, 59, 5, 5)));
        Assert.False(rect.IntersectsWith(new PixelRect(40, 60, 5, 5)));
        Assert.Equal(new PixelRect(10, 20, 20, 20), rect.Intersect(new PixelRect(10, 20, 20, 20)));
        Assert.True(rect.Intersect(new PixelRect(500, 500, 10, 10)).IsEmpty);
    }
}
