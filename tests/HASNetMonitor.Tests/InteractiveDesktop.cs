using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Tests;

/// <summary>
/// Whether this session can actually show a window. While the desktop is locked or
/// disconnected the shell keeps a lock-screen frame on top of everything, every
/// window reports itself invisible, and hit-testing answers that frame - so the
/// native checks cannot observe anything and would fail for a reason that has
/// nothing to do with the widget.
/// </summary>
internal static class InteractiveDesktop
{
    public const string Reason =
        "the desktop is locked or not interactive, so window visibility and hit-testing cannot be observed";

    public static bool IsAvailable { get; } = Detect();

    private static bool Detect()
    {
        // OpenInputDesktop is refused while a secure desktop (the lock screen) is up.
        IntPtr desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (CloseDesktop(desktop) == false)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        // A locked session has no foreground window at all.
        return NativeMethods.GetForegroundWindow() != IntPtr.Zero;
    }

    private const uint DesktopReadObjects = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
