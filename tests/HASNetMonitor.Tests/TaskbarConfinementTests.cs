using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The confinement rule: the widget draws inside the taskbar and nowhere else. A
/// full-screen app hides the taskbar, auto-hide parks it off screen, and even a
/// taskbar that is on screen can be too small to hold the widget honestly; in every
/// one of those cases the widget must hide instead of floating over whatever owns
/// the screen.
/// </summary>
public class TaskbarConfinementTests
{
    private static PixelRect Taskbar() => new(0, 1040, 1920, 40);

    private static PixelRect Monitor() => new(0, 0, 1920, 1080);

    [Fact]
    public void APlacementInsideTheTaskbarIsConfined()
    {
        Assert.True(TaskbarWidget.IsConfinedToTaskbar(new PixelRect(1500, 1044, 280, 32), Taskbar()));
    }

    [Fact]
    public void APlacementExactlyFillingTheTaskbarIsConfined()
    {
        Assert.True(TaskbarWidget.IsConfinedToTaskbar(Taskbar(), Taskbar()));
    }

    [Theory]
    [InlineData(-1, 1044, 100, 32)]
    [InlineData(1821, 1044, 100, 32)]
    [InlineData(100, 1039, 100, 32)]
    [InlineData(100, 1049, 100, 40)]
    [InlineData(100, 1044, 0, 32)]
    [InlineData(100, 1044, 100, 0)]
    public void APlacementReachingOutsideTheTaskbarIsNotConfined(int left, int top, int width, int height)
    {
        Assert.False(TaskbarWidget.IsConfinedToTaskbar(new PixelRect(left, top, width, height), Taskbar()));
    }

    [Fact]
    public void ComputedPlacementsStayInsideTheTaskbar()
    {
        // The placement the product actually computes must always satisfy the rule it
        // is presented under - otherwise every healthy frame would be hidden.
        Size widget = new Size(200, 32);
        PixelRect bar = Taskbar();
        PixelRect? tray = new PixelRect(1800, 1040, 120, 40);
        PixelRect verticalBar = new PixelRect(1880, 0, 40, 1080);

        PixelRect? horizontal = TaskbarGeometry.ComputePlacement(bar, tray, widget, 8);
        Assert.NotNull(horizontal);
        Assert.True(TaskbarWidget.IsConfinedToTaskbar(horizontal!.Value, bar));

        PixelRect? nudged = TaskbarGeometry.ComputePlacement(bar, tray, widget, 8, nudgeX: -500, nudgeY: 500);
        Assert.NotNull(nudged);
        Assert.True(TaskbarWidget.IsConfinedToTaskbar(nudged!.Value, bar));

        PixelRect? vertical = TaskbarGeometry.ComputePlacement(verticalBar, null, widget, 8);
        Assert.NotNull(vertical);
        Assert.True(TaskbarWidget.IsConfinedToTaskbar(vertical!.Value, verticalBar));
    }

    [Fact]
    public void AHiddenTaskbarIsNeverPresented()
    {
        // The full-screen case: the taskbar is away, so nothing may be drawn - not even
        // a placement that would be geometrically inside the bar's old rectangle.
        Assert.False(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: false,
            layoutFits: true,
            placement: new PixelRect(1500, 1044, 280, 32),
            taskbar: Taskbar(),
            monitorBounds: Monitor()));
    }

    [Fact]
    public void ALayoutThatDoesNotFitIsNeverPresented()
    {
        Assert.False(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: true,
            layoutFits: false,
            placement: null,
            taskbar: Taskbar(),
            monitorBounds: Monitor()));
    }

    [Fact]
    public void ANullPlacementIsNeverPresented()
    {
        Assert.False(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: true,
            layoutFits: true,
            placement: null,
            taskbar: Taskbar(),
            monitorBounds: Monitor()));
    }

    [Fact]
    public void APlacementOutsideTheTaskbarIsNeverPresented()
    {
        Assert.False(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: true,
            layoutFits: true,
            placement: new PixelRect(100, 100, 280, 32),
            taskbar: Taskbar(),
            monitorBounds: Monitor()));
    }

    [Fact]
    public void APlacementOffTheScreenIsNeverPresented()
    {
        // A stale monitor-bounds record must not rescue a placement that is not on the
        // screen at all: the widget would draw nothing useful and cover the desktop.
        Assert.False(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: true,
            layoutFits: true,
            placement: new PixelRect(1500, 1044, 280, 32),
            taskbar: Taskbar(),
            monitorBounds: new PixelRect(0, 0, 100, 100)));
    }

    [Fact]
    public void AnUnknownMonitorIsNotUsedToRejectAFrame()
    {
        // With no monitor bounds recorded the confinement to the taskbar is all the
        // evidence there is; rejecting on a guess would hide a perfectly healthy widget.
        Assert.True(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: true,
            layoutFits: true,
            placement: new PixelRect(1500, 1044, 280, 32),
            taskbar: Taskbar(),
            monitorBounds: default));
    }

    [Fact]
    public void AValidFrameIsPresented()
    {
        Assert.True(TaskbarWidget.WidgetMayPresent(
            taskbarVisible: true,
            layoutFits: true,
            placement: new PixelRect(1500, 1044, 280, 32),
            taskbar: Taskbar(),
            monitorBounds: Monitor()));
    }

    [Fact]
    public void TheFramePathHidesWhenTheRuleFails()
    {
        // The frame path must not keep a stale frame when the widget may not present;
        // it goes with the taskbar instead.
        string render = SourceText.ExtractMethod("UI", "TaskbarWidget.cs", "private void Render()");

        Assert.Contains("WidgetMayPresent(", render, StringComparison.Ordinal);
        Assert.Contains("HideIfShown()", render, StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowFillingItsMonitorIsFullScreen()
    {
        PixelRect monitor = Monitor();

        // The whole monitor, exactly and a couple of pixels short of its edges.
        Assert.True(TaskbarWidget.IsFullScreenWindow(new PixelRect(0, 0, 1920, 1080), monitor));
        Assert.True(TaskbarWidget.IsFullScreenWindow(new PixelRect(0, 0, 1918, 1078), monitor));
        // On the second monitor, not just the primary.
        Assert.True(TaskbarWidget.IsFullScreenWindow(new PixelRect(1920, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1080)));
    }

    [Fact]
    public void AWindowStoppingAtTheWorkAreaIsNotFullScreen()
    {
        PixelRect monitor = Monitor();

        // A maximized window stops at the work area under the taskbar: the widget is
        // expected to stay up there, so it must never hide for it.
        Assert.False(TaskbarWidget.IsFullScreenWindow(new PixelRect(0, 0, 1920, 1040), monitor));
        Assert.False(TaskbarWidget.IsFullScreenWindow(new PixelRect(100, 100, 1700, 900), monitor));
        // No monitor evidence is no evidence at all.
        Assert.False(TaskbarWidget.IsFullScreenWindow(new PixelRect(0, 0, 1920, 1080), default));
        Assert.False(TaskbarWidget.IsFullScreenWindow(default, monitor));
    }

    [Fact]
    public void AFullScreenWindowCoveringTheTaskbarIsNeverShownOver()
    {
        PixelRect window = new PixelRect(0, 0, 1920, 1080);

        // A full-screen window over the monitor the widget sits on covers the bar too.
        Assert.True(TaskbarWidget.FullScreenWindowCoversTaskbar(window, Taskbar()));
        // A window covering only part of the bar is not taken over by the widget either:
        // any overlap means the widget would be drawn over the video.
        Assert.True(TaskbarWidget.FullScreenWindowCoversTaskbar(
            new PixelRect(0, 980, 1920, 100), Taskbar()));
    }

    [Fact]
    public void AFullScreenWindowOverAnotherMonitorLeavesTheWidgetUp()
    {
        // The widget lives on the primary taskbar; a full-screen window on the other
        // monitor must not hide it off the screen it sits on.
        Assert.False(TaskbarWidget.FullScreenWindowCoversTaskbar(
            new PixelRect(1920, 0, 1920, 1080), Taskbar()));
        // A taskbar that spans both monitors is covered by a full-screen window on either.
        Assert.True(TaskbarWidget.FullScreenWindowCoversTaskbar(
            new PixelRect(1920, 0, 1920, 1080), new PixelRect(0, 1040, 3840, 40)));
        // No bounds on either side is no evidence: keep the widget.
        Assert.False(TaskbarWidget.FullScreenWindowCoversTaskbar(default, Taskbar()));
        Assert.False(TaskbarWidget.FullScreenWindowCoversTaskbar(new PixelRect(0, 0, 1920, 1080), default));
    }

    [Fact]
    public void TheFramePathHidesWhenAForegroundWindowIsFullScreen()
    {
        // The frame path must check the foreground window before it keeps or draws
        // anything: the taskbar can stay alive and visible behind that window.
        string render = SourceText.ExtractMethod("UI", "TaskbarWidget.cs", "private void Render()");

        Assert.Contains("FullScreenCoversTaskbar(", render, StringComparison.Ordinal);
        Assert.Contains("HideIfShown()", render, StringComparison.Ordinal);
    }
}

