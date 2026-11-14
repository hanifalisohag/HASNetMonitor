using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.UI;

/// <summary>Uses only the public shell interface; no build-specific desktop pinning APIs.</summary>
internal sealed class DesktopPresence : IDisposable
{
    private IVirtualDesktopManager? _manager;

    /// <summary>
    /// Whether the window is on the active virtual desktop. Anything that cannot be
    /// read counts as "on it": a failed shell call must never make the widget move.
    /// </summary>
    internal bool IsOnCurrentDesktop(IntPtr window)
    {
        try
        {
            if (window == IntPtr.Zero)
            {
                return true;
            }

            return Manager().IsWindowOnCurrentVirtualDesktop(window, out bool current) < 0 || current;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Explorer may be restarting. Retry with a fresh interface on the next tick.
            Dispose();
            return true;
        }
    }

    /// <summary>
    /// Moves <paramref name="window"/> onto the desktop of <paramref name="authority"/>,
    /// which must be a real window confirmed on the active desktop. The move is verified:
    /// when the widget still is not on the active desktop afterwards, it is put back on
    /// the desktop it came from and reported as a failure, so the caller can back off.
    /// A move that is silently rejected is what left the widget alive but on a desktop
    /// nobody is looking at.
    /// </summary>
    /// <returns>True only when the widget is on the active desktop afterwards.</returns>
    internal bool FollowCurrentDesktop(IntPtr window, IntPtr authority)
    {
        try
        {
            IVirtualDesktopManager manager = Manager();
            if (window == IntPtr.Zero || authority == IntPtr.Zero || authority == window)
            {
                return false;
            }

            if (manager.IsWindowOnCurrentVirtualDesktop(window, out bool current) < 0)
            {
                return false;
            }

            if (current)
            {
                return true;
            }

            // Only a window that can name the active desktop may be followed: Task View,
            // Start and the shell's other surfaces report the desktop being left.
            if (!ShellSurface.CanNameActiveDesktop(authority)
                || manager.IsWindowOnCurrentVirtualDesktop(authority, out bool authorityCurrent) < 0
                || !authorityCurrent)
            {
                return false;
            }

            if (manager.GetWindowDesktopId(authority, out Guid target) < 0 || target == Guid.Empty)
            {
                return false;
            }

            if (manager.GetWindowDesktopId(window, out Guid previous) < 0)
            {
                previous = Guid.Empty;
            }

            if (manager.MoveWindowToDesktop(window, ref target) < 0)
            {
                return false;
            }

            if (manager.IsWindowOnCurrentVirtualDesktop(window, out bool moved) >= 0 && moved)
            {
                return true;
            }

            if (previous != Guid.Empty && previous != target)
            {
                manager.MoveWindowToDesktop(window, ref previous);
            }

            return false;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // Explorer may be restarting. Retry with a fresh interface on the next tick.
            Dispose();
            return false;
        }
    }

    private IVirtualDesktopManager Manager()
        => _manager ??= (IVirtualDesktopManager)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!)!;

    public void Dispose()
    {
        if (_manager is not null)
        {
            Marshal.ReleaseComObject(_manager);
            _manager = null;
        }
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(IntPtr window, ref Guid desktop);
    }
}