using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// When Windows puts something over the widget without hiding it or sending a
/// message, the widget must put itself back. These are the cases that decide it:
/// being covered by the taskbar or by the show-desktop peek is a bug, being covered
/// by the Start menu, a context menu or a full-screen app is correct.
/// </summary>
public class RecoveryTests
{
    [Fact]
    public void AVisibleWidgetIsNeverTouched()
    {
        // The common case: the z-order must not be touched while nothing is wrong,
        // otherwise the widget fights every other window on the desktop.
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: true, coveredByTaskbar: false, peekActive: false));
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: true, coveredByTaskbar: false, peekActive: true));
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: true, coveredByTaskbar: true, peekActive: true));
    }

    [Fact]
    public void AHiddenWidgetStaysHidden()
    {
        // Auto-hidden or missing taskbar: the caller hid us on purpose.
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: false, isOnTop: false, coveredByTaskbar: true, peekActive: true));
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: false, isOnTop: false, coveredByTaskbar: false, peekActive: false));
    }

    [Fact]
    public void TheShowDesktopPeekIsUndone()
    {
        // Win+D: the desktop goes above every window, and nothing tells us, so the
        // widget has to notice on its own.
        Assert.True(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: false, coveredByTaskbar: false, peekActive: true));
    }

    [Fact]
    public void TheTaskbarSittingOnTopOfUsIsUndone()
    {
        // Task View and taskbar redraws move Shell_TrayWnd above the widget.
        Assert.True(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: false, coveredByTaskbar: true, peekActive: false));
    }

    [Fact]
    public void TheStartMenuAndOtherOverlaysAreLeftAlone()
    {
        // Covered by a shell flyout, a context menu or a full-screen app: drawing
        // over them would be the bug, so nothing may be re-asserted.
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: false, coveredByTaskbar: false, peekActive: false));
    }

    [Fact]
    public void RecoveryIsIdempotentAcrossManyTicks()
    {
        // A run of ticks in the peeked state must keep asking for recovery (the
        // shell can drop us again), and the moment we are on top it must stop.
        int asked = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            bool peeked = tick < 10;
            bool onTop = tick >= 10;
            if (TaskbarWidget.NeedsRecovery(true, onTop, coveredByTaskbar: false, peekActive: peeked && !onTop))
            {
                asked++;
            }
        }

        Assert.Equal(10, asked);
    }
}
