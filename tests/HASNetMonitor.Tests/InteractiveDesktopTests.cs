using HASNetMonitor.Interop;

namespace HASNetMonitor.Tests;

/// <summary>
/// The native window tests may only run when a window can really be seen. This pins the
/// check that decides it: whatever the session state, "the desktop is interactive" must
/// agree with what a freshly shown window actually reports.
/// </summary>
public class InteractiveDesktopTests
{
    [Fact]
    public void TheDesktopCheckAgreesWithWhatAShownWindowReports()
    {
        using var window = ClassWindow.Create("DesktopCheckWindow");

        NativeMethods.SetWindowPos(
            window.Handle,
            NativeMethods.HWND_TOPMOST,
            200, 200, 120, 40,
            NativeMethods.SWP_NOACTIVATE | 0x0040 /* SWP_SHOWWINDOW */);

        // The shell can take a moment to make a new window visible.
        bool visible = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!visible && DateTime.UtcNow < deadline)
        {
            visible = NativeMethods.IsWindowVisible(window.Handle);
            if (!visible)
            {
                Thread.Sleep(20);
            }
        }

        Assert.Equal(InteractiveDesktop.IsAvailable, visible);
    }

}
