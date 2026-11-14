using System.Runtime.InteropServices;
using TaskbarNetMonitor.Interop;
using TaskbarNetMonitor.UI;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// The Win+Tab bug these tests pin down: pressing Win+Tab opens Task View, and the
/// widget vanished and stayed gone. Two rules were missing.
///
/// 1. The desktop the widget follows must come from the user's own window. While Task
///    View or Start is open the foreground window is a shell surface
///    (<c>MultitaskingViewFrame</c>, the XAML island, the tray), and the desktop it
///    reports is the one being left - so following it left the widget on the old
///    desktop, alive and running but on a desktop nobody was looking at.
/// 2. No virtual-desktop move may happen while the shell holds the taskbar in a raised
///    band, which is exactly what Task View and Start do. The desktop is only decided
///    when the user picks it. (That rule is about desktop moves only - the z-order seat
///    must still happen then, see <see cref="TaskViewSeatTests"/>.)
/// </summary>
public class TaskViewPresenceTests
{
    [Theory]
    [InlineData("MultitaskingViewFrame")]      // Task View itself (Win+Tab)
    [InlineData("Windows.UI.Core.CoreWindow")] // the XAML host Start and Task View run in
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("TaskListThumbnailWnd")]
    [InlineData("ForegroundStaging")]
    public void ShellSurfacesNeverNameTheActiveDesktop(string shellClass)
    {
        Assert.True(ShellSurface.IsShellClassName(shellClass));
        Assert.False(
            ShellSurface.CanNameActiveDesktop(shellClass),
            $"{shellClass} is part of the shell and cannot say which desktop the user is on");
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("ApplicationFrameWindow")]
    public void RealAppWindowsDoNameTheActiveDesktop(string appClass)
    {
        Assert.False(ShellSurface.IsShellClassName(appClass));
        Assert.True(ShellSurface.CanNameActiveDesktop(appClass));
    }

    [Fact]
    public void AnUnreadableClassNameIsNotAnAuthority()
    {
        // A window that has gone, or a class name that cannot be read, must never be
        // taken as "the user is on this desktop" - that is how a widget gets moved on a
        // guess. Empty and null are the shapes a failed read has.
        Assert.False(ShellSurface.IsShellClassName(null));
        Assert.False(ShellSurface.IsShellClassName(string.Empty));
        Assert.False(ShellSurface.CanNameActiveDesktop((string?)null));
        Assert.False(ShellSurface.CanNameActiveDesktop(string.Empty));
    }

    [Fact]
    public void NoWindowAtAllIsNotAnAuthority()
    {
        Assert.False(ShellSurface.CanNameActiveDesktop(IntPtr.Zero));
        Assert.False(ShellSurface.CanNameActiveDesktop(new IntPtr(0x1234))); // not a window
    }

    [Fact]
    public void ARealTaskViewWindowIsRejectedAndARealAppWindowAccepted()
    {
        // The class-name rule is checked against genuine windows, not just strings: the
        // root-ancestor lookup is what sees a child of Task View, so both are created for
        // real here.
        using var taskView = ClassWindow.Create("MultitaskingViewFrame");
        using var app = ClassWindow.Create("TaskViewPresenceTests_AppWindow");

        Assert.False(ShellSurface.CanNameActiveDesktop(taskView.Handle));
        Assert.True(ShellSurface.CanNameActiveDesktop(app.Handle));
    }

    [Fact]
    public void AChildOfAShellSurfaceCannotNameTheDesktopEither()
    {
        // A window that lives inside the taskbar (or Task View) is composited with it and
        // is on whatever desktop the shell is showing - never a desktop to follow.
        using var taskbar = ClassWindow.Create("Shell_TrayWnd");
        using var child = ClassWindow.CreateChild("TaskViewPresenceTests_Child", taskbar.Handle);

        Assert.False(ShellSurface.CanNameActiveDesktop(child.Handle));
        Assert.True(ShellSurface.CanNameActiveDesktop(taskbar.Handle) == false);
    }

    [Fact]
    public void NothingIsMovedOntoAnotherDesktopWhileTheShellHoldsTheRaisedBand()
    {
        // This is the Win+Tab rule. The band is raised for as long as Task View or Start
        // is open, and the widget must sit still for all of it, however stranded the
        // shells half-finished switch makes it look.
        Assert.False(TaskbarWidget.ShouldFollowDesktop(shellBandRaised: true, consecutiveMisses: 3, requiredMisses: 3));
        Assert.False(TaskbarWidget.ShouldFollowDesktop(shellBandRaised: true, consecutiveMisses: 99, requiredMisses: 3));

        // The band is back down: a real switch still needs its repeated readings.
        Assert.False(TaskbarWidget.ShouldFollowDesktop(shellBandRaised: false, consecutiveMisses: 2, requiredMisses: 3));
        Assert.True(TaskbarWidget.ShouldFollowDesktop(shellBandRaised: false, consecutiveMisses: 3, requiredMisses: 3));

        // A misconfigured requirement must never mean "move on every tick".
        Assert.False(TaskbarWidget.ShouldFollowDesktop(shellBandRaised: false, consecutiveMisses: 9, requiredMisses: 0));
    }
}

/// <summary>A real top-level (or child) window of a chosen class, for native-rule tests.</summary>
internal sealed class ClassWindow : IDisposable
{
    // The window procedure must outlive every window created with it.
    private static readonly NativeMethods.WndProcDelegate WndProc =
        (h, m, w, l) => NativeMethods.DefWindowProc(h, m, w, l);

    private static readonly IntPtr WndProcPointer =
        Marshal.GetFunctionPointerForDelegate(WndProc);

    private ClassWindow(IntPtr handle) => Handle = handle;

    public IntPtr Handle { get; private set; }

    public static ClassWindow Create(string className)
        => Make(className, IntPtr.Zero, unchecked((uint)NativeMethods.WS_POPUP));

    public static ClassWindow CreateChild(string className, IntPtr parent)
        => Make(className, parent, NativeMethods.WS_CHILD);

    private static ClassWindow Make(string className, IntPtr parent, uint style)
    {
        var wc = new NativeMethods.WndClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            WindowProc = WndProcPointer,
            ClassName = className,
            Instance = NativeMethods.GetModuleHandle(null),
        };

        // A class can only be registered once per process; a duplicate is not an error.
        NativeMethods.RegisterClassEx(ref wc);

        IntPtr handle = NativeMethods.CreateWindowEx(
            0, className, className, style, 0, 0, 120, 40,            parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, handle);
        return new ClassWindow(handle);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }
    }
}
