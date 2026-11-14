using System.Drawing;
using System.Drawing.Imaging;
using TaskbarNetMonitor.Interop;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

public class NativePresenceTests
{
    [Fact]
    public void NativeTopmostDemotionIsRepairedWithoutMovingOrHidingTheWidget()
    {
        RunSta(() =>
        {
            using var window = new LayeredWindow();
            using var bitmap = new Bitmap(80, 32, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.Lime);
            window.Present(bitmap, 30, 40);
            Assert.True(NativeMethods.SetWindowPos(window.Handle, NativeMethods.HWND_NOTOPMOST,
                0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE));
            Assert.Equal(0, NativeMethods.GetWindowLong(window.Handle, -20) & NativeMethods.WS_EX_TOPMOST);
            Assert.True(window.RestorePresence());
            Assert.NotEqual(0, NativeMethods.GetWindowLong(window.Handle, -20) & NativeMethods.WS_EX_TOPMOST);
            Assert.True(window.IsActuallyVisible);
            Assert.True(NativeMethods.GetWindowRect(window.Handle, out NativeRect rect));
            Assert.Equal((30, 40, 110, 72), (rect.Left, rect.Top, rect.Right, rect.Bottom));
        });
    }

    [Fact]
    public void NativeHideAndDisplacementAreRepairedWithoutNewTraffic()
    {
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