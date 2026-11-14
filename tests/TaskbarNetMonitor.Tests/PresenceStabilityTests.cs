using System.Drawing;
using System.Drawing.Imaging;
using TaskbarNetMonitor.Interop;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// The bug these tests pin down: the widget used to treat "some window is above it and
/// overlapping it" as a fault, so the Start menu, a tray flyout or the Task View - all
/// of which are above the taskbar by design - triggered a z-order repair sixteen times
/// a second, which is exactly the blink the user sees. And while the shell had raised
/// the taskbar into a higher Z band, repairing could never win, which is why the widget
/// stayed gone afterwards. Presence is only a fault when the taskbar itself is above the
/// widget, and a repair is only a re-seat directly above that taskbar.
/// </summary>
public class PresenceStabilityTests
{
    [Fact]
    public void OnlyWindowsAboveTheDesktopBandAreDeferred()
    {
        // Start, Task View and the alt-tab switcher raise the taskbar and everything
        // they own into a band above the desktop; nothing may be repaired in there.
        Assert.True(TaskbarWidget.DefersRepair(6, 6));
        Assert.True(TaskbarWidget.DefersRepair(6, 1));
        Assert.True(TaskbarWidget.DefersRepair(1, 6));
        Assert.True(TaskbarWidget.DefersRepair(2, 1));

        // The normal desktop band, and an unavailable band API, are both "repair allowed"
        // so that a Windows without GetWindowBand still gets managed.
        Assert.False(TaskbarWidget.DefersRepair(1, 1));
        Assert.False(TaskbarWidget.DefersRepair(0, 0));
        Assert.False(TaskbarWidget.DefersRepair(0, 1));
    }

    [Fact]
    public void TheFirstRepairIsImmediateAndRepetitionBacksOff()
    {
        Assert.Equal(TimeSpan.Zero, TaskbarWidget.RepairIntervalFor(0));

        TimeSpan first = TaskbarWidget.RepairIntervalFor(1);
        Assert.True(first >= TimeSpan.FromSeconds(1), "a repair must not repeat within a frame");
        Assert.True(first > TimeSpan.FromMilliseconds(60));

        // Every further attempt waits longer, so a shell that keeps winning cannot turn
        // the repair into a flicker, and the wait is capped so it never stops trying.
        TimeSpan previous = first;
        for (int attempt = 2; attempt <= 5; attempt++)
        {
            TimeSpan interval = TaskbarWidget.RepairIntervalFor(attempt);
            Assert.True(interval > previous, $"attempt {attempt} must wait longer than {attempt - 1}");
            previous = interval;
        }

        Assert.Equal(previous, TaskbarWidget.RepairIntervalFor(5));
        Assert.Equal(previous, TaskbarWidget.RepairIntervalFor(500));
    }

    [Fact]
    public void TheBackoffIsForgottenOnlyAfterAStableReading()
    {
        // A single good reading is not proof; the healthy hold has to pass first, which
        // the widget enforces by clearing the attempt count only past HealthyHold.
        Assert.True(TaskbarWidget.ShouldRecoverNow(TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.False(TaskbarWidget.ShouldRecoverNow(TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void OneDesktopReadingIsNeverEnoughToMoveTheWidget()
    {
        Assert.False(TaskbarWidget.IsStrandedOnAnotherDesktop(0, 3));
        Assert.False(TaskbarWidget.IsStrandedOnAnotherDesktop(1, 3));
        Assert.False(TaskbarWidget.IsStrandedOnAnotherDesktop(2, 3));
        Assert.True(TaskbarWidget.IsStrandedOnAnotherDesktop(3, 3));
        Assert.True(TaskbarWidget.IsStrandedOnAnotherDesktop(9, 3));

        // A misconfigured requirement must never mean "move on every tick".
        Assert.False(TaskbarWidget.IsStrandedOnAnotherDesktop(5, 0));
    }

    [Fact]
    public void AWindowAboveTheWidgetIsNotTheSameAsTheTaskbarAboveIt()
    {
        RunSta(() =>
        {
            using var taskbar = new LayeredWindow();
            using var widget = new LayeredWindow();
            using var menu = new LayeredWindow();

            // Bottom to top: taskbar, widget, menu - the everyday state with a flyout open.
            taskbar.Present(Frame(), 10, 10);
            widget.Present(Frame(), 10, 60);
            menu.Present(Frame(), 10, 110);

            Assert.True(LayeredWindow.IsAboveInZOrder(menu.Handle, widget.Handle));
            Assert.False(widget.IsBuriedBehind(taskbar.Handle));
            Assert.False(widget.IsBehindDesktopPeek(taskbar.Handle));
        });
    }

    [Fact]
    public void TheWidgetIsOnlyBuriedWhenTheTaskbarIsAboveIt()
    {
        RunSta(() =>
        {
            using var taskbar = new LayeredWindow();
            using var widget = new LayeredWindow();
            using var menu = new LayeredWindow();

            taskbar.Present(Frame(), 10, 10);
            widget.Present(Frame(), 10, 60);
            menu.Present(Frame(), 10, 110);
            Assert.False(widget.IsBuriedBehind(taskbar.Handle));

            Bury(widget, taskbar);
            Assert.True(widget.IsBuriedBehind(taskbar.Handle));
            Assert.True(widget.IsBuriedBehind(taskbar.Handle)); // A reading is repeatable.
            Assert.False(widget.IsBuriedBehind(IntPtr.Zero));   // No anchor, no burial.
        });
    }

    [Fact]
    public void TheSeatPutsTheWidgetDirectlyAboveTheTaskbarAndKeepsMenusAboveIt()
    {
        RunSta(() =>
        {
            using var taskbar = new LayeredWindow();
            using var widget = new LayeredWindow();
            using var menu = new LayeredWindow();

            taskbar.Present(Frame(), 10, 10);
            widget.Present(Frame(), 10, 60);
            menu.Present(Frame(), 10, 110);
            Bury(widget, taskbar);
            Assert.True(widget.IsBuriedBehind(taskbar.Handle));

            Assert.True(widget.SeatAboveTaskbar(taskbar.Handle));

            // Directly above the taskbar, never at the top of the band: the flyout that
            // was above the taskbar has to stay above the widget, or it is drawn over.
            AssertSeatedDirectlyAbove(widget.Handle, taskbar.Handle);
            Assert.True(LayeredWindow.IsAboveInZOrder(menu.Handle, widget.Handle));
            Assert.False(widget.IsBuriedBehind(taskbar.Handle));
        });
    }

    [Fact]
    public void ASeatThatIsAlreadyInPlaceDoesNothingAndRepeatingItIsStable()
    {
        RunSta(() =>
        {
            using var taskbar = new LayeredWindow();
            using var widget = new LayeredWindow();

            taskbar.Present(Frame(), 10, 10);
            widget.Present(Frame(), 10, 60);
            // Presented right after the taskbar: already directly above it.
            Assert.False(widget.SeatAboveTaskbar(taskbar.Handle));
            AssertSeatedDirectlyAbove(widget.Handle, taskbar.Handle);

            // Several rounds of bury/seat, the way the shell can repeat a bad order.
            for (int round = 0; round < 5; round++)
            {
                Bury(widget, taskbar);
                Assert.True(widget.IsBuriedBehind(taskbar.Handle));
                Assert.True(widget.SeatAboveTaskbar(taskbar.Handle));
                AssertSeatedDirectlyAbove(widget.Handle, taskbar.Handle);
                Assert.False(widget.SeatAboveTaskbar(taskbar.Handle));
                AssertSeatedDirectlyAbove(widget.Handle, taskbar.Handle);
            }
        });
    }

    [Fact]
    public void ASeatMovesNothingResizesNothingAndTakesFocusAwayFromNoOne()
    {
        RunSta(() =>
        {
            using var taskbar = new LayeredWindow();
            using var widget = new LayeredWindow();

            taskbar.Present(Frame(), 10, 10);
            widget.Present(Frame(), 30, 40);
            Assert.True(NativeMethods.GetWindowRect(widget.Handle, out NativeRect before));
            IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();

            Bury(widget, taskbar);
            Assert.True(widget.SeatAboveTaskbar(taskbar.Handle));

            Assert.True(NativeMethods.GetWindowRect(widget.Handle, out NativeRect after));
            Assert.Equal(
                (before.Left, before.Top, before.Right, before.Bottom),
                (after.Left, after.Top, after.Right, after.Bottom));
            Assert.Equal(foregroundBefore, NativeMethods.GetForegroundWindow());
            Assert.NotEqual(widget.Handle, NativeMethods.GetForegroundWindow());
        });
    }

    [Fact]
    public void DockingGivesTheWidgetTheTaskbarAsItsOwnerExactlyOnce()
    {
        RunSta(() =>
        {
            using var taskbar = new LayeredWindow();
            using var widget = new LayeredWindow();

            taskbar.Present(Frame(), 10, 10);
            widget.Present(Frame(), 30, 40);
            Assert.True(NativeMethods.GetWindowRect(widget.Handle, out NativeRect before));

            Assert.True(widget.DockTo(taskbar.Handle));
            Assert.Equal(
                taskbar.Handle,
                NativeMethods.GetWindowLongPtr(widget.Handle, NativeMethods.GWLP_HWNDPARENT));

            // The owner is what carries the widget into the shell's raised band and onto
            // every virtual desktop, and it is set once: re-setting it is not a repair.
            Assert.False(widget.DockTo(taskbar.Handle));
            Assert.False(widget.DockTo(IntPtr.Zero)); // never dropped by accident
            Assert.Equal(
                taskbar.Handle,
                NativeMethods.GetWindowLongPtr(widget.Handle, NativeMethods.GWLP_HWNDPARENT));

            // Docking itself must not move, resize or reorder anything.
            Assert.True(NativeMethods.GetWindowRect(widget.Handle, out NativeRect after));
            Assert.Equal(
                (before.Left, before.Top, before.Right, before.Bottom),
                (after.Left, after.Top, after.Right, after.Bottom));
        });
    }

    [Fact]
    public void ASurfaceRepairIsNotTriggeredByAWindowMerelyOverlappingTheWidget()
    {
        RunSta(() =>
        {
            using var widget = new LayeredWindow();
            using var cover = new LayeredWindow();

            widget.Present(Frame(), 30, 40);
            cover.Present(Frame(), 20, 30); // on top, and overlapping the widget's rect
            Assert.True(LayeredWindow.IsAboveInZOrder(cover.Handle, widget.Handle));

            // Nothing is wrong with the widget: repairing here is the blink.
            Assert.False(widget.RestorePresence());
        });
    }

    /// <summary>
    /// The property that has to hold after every repair: the widget is the window
    /// directly above the taskbar, so nothing of the taskbar covers it.
    /// </summary>
    private static void AssertSeatedDirectlyAbove(IntPtr widget, IntPtr taskbar)
    {
        Assert.Equal(widget, NativeMethods.GetWindow(taskbar, NativeMethods.GW_HWNDPREV));
        Assert.False(LayeredWindow.IsAboveInZOrder(taskbar, widget));
    }

    /// <summary>Puts the widget directly underneath the taskbar, the way the shell can.</summary>
    private static void Bury(LayeredWindow widget, LayeredWindow taskbar)
    {
        Assert.True(NativeMethods.SetWindowPos(
            widget.Handle,
            taskbar.Handle,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE));
    }

    private static Bitmap Frame()
    {
        var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Lime);
        return bitmap;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Native window test timed out.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
