using HASNetMonitor.Interop;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The Win+Tab bug that survived the first attempt, and the two rules that fix it.
///
/// Pressing Win+Tab raises the taskbar and the owned widget into the same raised band,
/// and inside that band the shell then lifts the taskbar on top of the widget: measured
/// on a live session, both windows at band 6 and <c>WindowFromPoint</c> over the widget
/// answering <c>Shell_TrayWnd</c>. So the widget is not "deferred"; it is covered.
///
/// 1. The seat must not be deferred while the band is raised - deferring on the raised
///    band is exactly what left the widget invisible for the whole of Task View. One
///    SetWindowPos directly above the taskbar brings it back and it stays there.
/// 2. Burial cannot be read off the z-order chain: the widget is an <em>owned</em> window
///    of the taskbar, so the chain above it ends without ever naming its owner even while
///    the owner covers it completely. Only asking which window is drawn at the widget's
///    own centre tells the truth.
/// </summary>
public class TaskViewSeatTests
{
    [Fact]
    public void ABuriedWidgetIsSeatedEvenWhileTheShellHoldsARaisedBand()
    {
        // The Task View case: the taskbar is topmost and it is drawn over the widget.
        Assert.True(TaskbarWidget.ShouldSeat(buried: true, taskbarIsTopMost: true));
    }

    [Fact]
    public void AWidgetThatIsNotBuriedIsNeverReordered()
    {
        // Each of these would be a z-order change, and a z-order change is the blink the
        // user sees when the taskbar's own menus open and close.
        Assert.False(TaskbarWidget.ShouldSeat(buried: false, taskbarIsTopMost: true));
        Assert.False(TaskbarWidget.ShouldSeat(buried: false, taskbarIsTopMost: false));
    }

    [Fact]
    public void ATaskbarThatIsNotTopMostIsNotFollowed()
    {
        // A full-screen app takes the taskbar out of the topmost band; seating the
        // widget there would draw it over that app.
        Assert.False(TaskbarWidget.ShouldSeat(buried: true, taskbarIsTopMost: false));
    }

    [Fact]
    public void TheRaisedBandDoesNotSuppressTheSeat()
    {
        // The regression guard: the shell's raised bands (Start, Task View) must not be
        // able to talk the widget out of the seat it needs.
        foreach (uint band in new[] { 1u, 2u, 6u, 16u, 18u })
        {
            Assert.True(
                TaskbarWidget.ShouldSeat(buried: true, taskbarIsTopMost: true),
                $"a buried widget must be seated in band {band} too");
        }
    }

    [SkippableFact]
    public void AShellWindowDrawnAtAPaintedPixelCountsAsCovering()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        using var widget = ClassWindow.Create("SeatTestWidget");
        using var shell = ClassWindow.Create("SeatTestShell");

        Place(widget, 200, 200, 120, 40);
        Place(shell, 150, 150, 300, 200);        // sits over the widget
        Raise(shell);

        Assert.True(
            LayeredWindow.IsShellAtPoint(260, 220, widget.Handle, shell.Handle),
            "a shell window over the sampled pixel is covering the widget");
    }

    [SkippableFact]
    public void AMovedAwayShellIsNotCovering()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        using var widget = ClassWindow.Create("SeatTestWidget2");
        using var shell = ClassWindow.Create("SeatTestShell2");

        Place(widget, 200, 200, 120, 40);
        Place(shell, 150, 150, 300, 200);
        Raise(shell);
        Assert.True(LayeredWindow.IsShellAtPoint(260, 220, widget.Handle, shell.Handle));

        Place(shell, 900, 500, 300, 200);        // off the widget
        Assert.False(
            LayeredWindow.IsShellAtPoint(260, 220, widget.Handle, shell.Handle),
            "nothing else was touched, so the widget is no longer covered");
    }

    [SkippableFact]
    public void AnotherWindowOverTheWidgetIsNotTheTaskbar()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        // A menu or flyout overlapping the widget must never be mistaken for the taskbar
        // covering it, or the widget fights the menu and blinks.
        using var widget = ClassWindow.Create("SeatTestWidget3");
        using var shell = ClassWindow.Create("SeatTestShell3");
        using var menu = ClassWindow.Create("SeatTestMenu");

        Place(widget, 200, 200, 120, 40);
        Place(menu, 150, 150, 300, 200);
        Raise(menu);                              // the menu is over the widget, not the shell

        Assert.False(
            LayeredWindow.IsShellAtPoint(260, 220, widget.Handle, shell.Handle),
            "only the taskbar itself may be read as covering the widget");
    }

    [Fact]
    public void AMissingWindowIsNeverCovering()
    {
        using var widget = ClassWindow.Create("SeatTestWidget4");
        Place(widget, 200, 200, 120, 40);

        Assert.False(LayeredWindow.IsShellAtPoint(260, 220, IntPtr.Zero, widget.Handle));
        Assert.False(LayeredWindow.IsShellAtPoint(260, 220, widget.Handle, IntPtr.Zero));
        Assert.False(LayeredWindow.IsShellAtPoint(260, 220, IntPtr.Zero, IntPtr.Zero));
    }

    [Fact]
    public void AnOpaqueFrameIsSampledAtItsMiddle()
    {
        // The common case: the whole frame is painted, so the middle is used as is.
        Assert.True(LayeredWindow.TryFindPaintedPoint(72, 44, (_, _) => 255, out int x, out int y));
        Assert.Equal(36, x);
        Assert.Equal(22, y);
    }

    [Fact]
    public void ATransparentMiddleIsNeverSampled()
    {
        // The trap that caused the old blink loop: a layered window's transparent pixels
        // hit-test as whatever is underneath. A transparent middle must be skipped for a
        // painted pixel, not hit-tested.
        static byte Alpha(int x, int y)
            => x is >= 10 and <= 20 && y is >= 10 and <= 20 ? (byte)255 : (byte)0;

        Assert.True(LayeredWindow.TryFindPaintedPoint(72, 44, Alpha, out int x, out int y));
        Assert.InRange(x, 10, 20);
        Assert.InRange(y, 10, 20);
        Assert.NotEqual(36, x);
    }

    [Fact]
    public void AFrameWithNothingPaintedGivesNoAnswer()
    {
        // Never repair on a guess: no painted pixel means "not covered".
        Assert.False(LayeredWindow.TryFindPaintedPoint(72, 44, (_, _) => 0, out _, out _));
        Assert.False(LayeredWindow.TryFindPaintedPoint(0, 0, (_, _) => 255, out _, out _));
        Assert.False(LayeredWindow.TryFindPaintedPoint(-4, 10, (_, _) => 255, out _, out _));
        Assert.False(LayeredWindow.TryFindPaintedPoint(72, 0, (_, _) => 255, out _, out _));
    }

    private static void Place(ClassWindow window, int x, int y, int width, int height)
    {
        Assert.True(NativeMethods.SetWindowPos(
            window.Handle,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            NativeMethods.SWP_NOACTIVATE | 0x0040 /* SWP_SHOWWINDOW */));
    }

    private static void Raise(ClassWindow window)
    {
        Assert.True(NativeMethods.SetWindowPos(
            window.Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE));
    }
}
