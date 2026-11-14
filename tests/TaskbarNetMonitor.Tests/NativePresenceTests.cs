using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TaskbarNetMonitor.Interop;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

public class NativePresenceTests
{
    [SkippableFact]
    public void NativeTopmostDemotionIsRepairedWithoutMovingOrHidingTheWidget()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        RunSta(() =>
        {
            using var window = new LayeredWindow();
            using var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Lime);
            window.Present(bitmap, 30, 40);

            // The shell can apply an extended-style change late, or drop a single
            // request on a busy desktop, so the demotion is requested until the flag
            // really clears (bounded). The assertion is unchanged: the flag must be
            // gone afterwards, and a window that never demotes still fails.
            long demoted = DemoteUntilCleared(window.Handle, out int lastError);
            Assert.True(demoted == 0, $"the window stayed topmost (last Win32 error {lastError})");

            // Repair must put it back; that is synchronous inside RestorePresence.
            Assert.True(window.RestorePresence());
            Assert.NotEqual(0, WaitForTopMostFlag(window.Handle, expected: true));
            Assert.True(window.IsActuallyVisible);
            Assert.True(NativeMethods.GetWindowRect(window.Handle, out NativeRect rect));
            Assert.Equal((30, 40, 110, 72), (rect.Left, rect.Top, rect.Right, rect.Bottom));
        });
    }

    [SkippableFact]
    public void NativeHideAndDisplacementAreRepairedWithoutNewTraffic()
    {
        Skip.IfNot(InteractiveDesktop.IsAvailable, InteractiveDesktop.Reason);

        RunSta(() =>
        {
            using var window = new LayeredWindow();
            using var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Lime);
            window.Present(bitmap, 30, 40);
            NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_HIDE);
            Assert.True(window.IsShown); // Reproduce the stale managed visibility flag.
            Assert.False(window.IsActuallyVisible);
            Assert.True(window.RestorePresence());
            Assert.True(window.IsActuallyVisible);

            NativeMethods.SetWindowPos(window.Handle, IntPtr.Zero, 400, 300, 10, 10,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Assert.True(window.RestorePresence());
            Assert.True(NativeMethods.GetWindowRect(window.Handle, out NativeRect rect));
            Assert.Equal((30, 40, 110, 72), (rect.Left, rect.Top, rect.Right, rect.Bottom));
            Assert.False(window.RestorePresence());
        });
    }

    [Fact]
    public void PeekExclusionIsAppliedToTheRealWindow()
    {
        RunSta(() =>
        {
            using var window = new LayeredWindow();
            Assert.True(window.PeekExclusionResult >= 0);
            Assert.False(window.RestorePresence()); // No surface before first presentation.
        });
    }

    /// <summary>
    /// Requests WS_EX_TOPMOST be cleared until it is, returning the final flag value.
    /// A single request can be applied late or dropped when the desktop is busy; a
    /// window that never demotes still comes back with the flag set.
    /// </summary>
    private static long DemoteUntilCleared(IntPtr handle, out int lastError)
    {
        lastError = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            if (!NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
            {
                lastError = Marshal.GetLastWin32Error();
            }

            long flag = NativeMethods.GetWindowLong(handle, -20) & NativeMethods.WS_EX_TOPMOST;
            if (flag == 0 || DateTime.UtcNow >= deadline)
            {
                return flag;
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Reads WS_EX_TOPMOST, waiting up to a moment for the shell to settle. Returns the
    /// final flag value, so the caller's assertion still checks the real state.
    /// </summary>
    private static long WaitForTopMostFlag(IntPtr handle, bool expected)
    {
        long flag = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (true)
        {
            flag = NativeMethods.GetWindowLong(handle, -20) & NativeMethods.WS_EX_TOPMOST;
            if ((flag != 0) == expected || DateTime.UtcNow >= deadline)
            {
                return flag;
            }

            Thread.Sleep(10);
        }
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Native window test timed out.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}