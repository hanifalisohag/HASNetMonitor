using TaskbarNetMonitor.Interop;

namespace TaskbarNetMonitor.UI;

/// <summary>
/// Which windows belong to the shell itself. Two rules depend on this:
///
/// 1. The widget may only ever put itself back above a shell surface (the taskbar, the
///    desktop, the show-desktop backdrop), never above an app or a menu.
/// 2. A window on the shell's own surfaces is never an authority on "which virtual
///    desktop are we on". While Task View (Win+Tab) or Start is open the foreground
///    window is one of these surfaces, not the desktop the user is looking at, so
///    moving the widget to that window's desktop strands it on the desktop being left
///    - which is exactly how the widget vanished and stayed gone.
/// </summary>
internal static class ShellSurface
{
    /// <summary>
    /// Class names of shell surfaces. <c>MultitaskingViewFrame</c> is Task View itself,
    /// the view the user opens with Win+Tab; the XAML host and CoreWindow classes carry
    /// Start, Search and the flyouts.
    /// </summary>
    private static readonly HashSet<string> ShellClassNames = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "MultitaskingViewFrame",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "Windows.Internal.Shell.TabProxyWindow",
        "TaskListThumbnailWnd",
        "ForegroundStaging",
        "Shell_InputSwitchTopLevelWindow",
        "EdgeUiInputTopWndClass",
        "SysShadow",
        "TaskSwitcherWnd",
    };

    /// <summary>Whether a window class name is one of the shell's own surfaces.</summary>
    internal static bool IsShellClassName(string? className)
        => !string.IsNullOrEmpty(className) && ShellClassNames.Contains(className);

    /// <summary>
    /// Whether the window may be asked which virtual desktop is the active one. Only a
    /// real, non-shell window on a desktop can answer that; anything else - Task View,
    /// Start, the desktop itself, a window that has gone - must be ignored instead of
    /// followed.
    /// </summary>
    internal static bool CanNameActiveDesktop(string? className)
        => !string.IsNullOrEmpty(className) && !ShellClassNames.Contains(className);

    /// <summary>
    /// Whether <paramref name="window"/> can name the active desktop: it exists, it is
    /// not a shell surface, and its root window is not a shell surface either (a child
    /// of the taskbar is still the taskbar's desktop).
    /// </summary>
    internal static bool CanNameActiveDesktop(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            return false;
        }

        IntPtr root = NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
        {
            root = window;
        }

        return CanNameActiveDesktop(NativeMethods.GetClassNameOf(root));
    }
}
