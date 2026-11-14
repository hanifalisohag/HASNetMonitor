using System.Runtime.InteropServices;
using HASNetMonitor.Core;
using HASNetMonitor.Interop;

namespace HASNetMonitor.UI;

/// <summary>Where the taskbar is right now, and everything derived from it.</summary>
internal readonly record struct TaskbarSnapshot(
    IntPtr TaskbarHandle,
    PixelRect TaskbarRect,
    PixelRect? TrayRect,
    PixelRect MonitorBounds,
    float DpiScale)
{
    public TaskbarOrientation Orientation => TaskbarGeometry.OrientationOf(TaskbarRect);
}

/// <summary>Locates the shell taskbar, the notification area and the DPI to draw at.</summary>
internal static class TaskbarLocator
{
    private const uint MonitorDefaultToNearest = 2;

    public static bool TryLocate(out TaskbarSnapshot snapshot)
    {
        snapshot = default;

        IntPtr taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(taskbar, out NativeRect rect))
        {
            return false;
        }

        var taskbarRect = PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (taskbarRect.IsEmpty)
        {
            return false;
        }

        PixelRect? trayRect = null;
        IntPtr tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out NativeRect trayNative))
        {
            var candidate = PixelRect.FromEdges(trayNative.Left, trayNative.Top, trayNative.Right, trayNative.Bottom);
            if (!candidate.IsEmpty)
            {
                trayRect = candidate;
            }
        }

        float dpiScale = 1f;
        uint dpi = NativeMethods.GetDpiForWindow(taskbar);
        if (dpi > 0)
        {
            dpiScale = dpi / 96f;
        }

        PixelRect monitorBounds = default;
        IntPtr monitor = NativeMethods.MonitorFromWindow(taskbar, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                monitorBounds = PixelRect.FromEdges(
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right,
                    info.Monitor.Bottom);
            }
        }

        snapshot = new TaskbarSnapshot(taskbar, taskbarRect, trayRect, monitorBounds, dpiScale);
        return true;
    }
}
