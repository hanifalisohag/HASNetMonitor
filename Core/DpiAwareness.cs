using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>What the process managed to tell Windows about itself at start-up.</summary>
internal enum DpiAwarenessResult
{
    /// <summary>Per-monitor v2: the settings dialog stays crisp on a scaled display.</summary>
    PerMonitorV2,

    /// <summary>System DPI only, from the older API on a Windows that lacks v2.</summary>
    SystemAware,

    /// <summary>Neither call was available, so Windows stretches the window.</summary>
    Unavailable,
}

/// <summary>
/// Declares the process DPI-aware in code. .NET Framework cannot read
/// <c>ApplicationHighDpiMode</c> from the project file, so the usual answer is an
/// App.config carrying a DpiAwareness key - which means shipping and keeping a second
/// file beside the exe. Asking Windows directly does the same thing and keeps the app a
/// single file. It must run before the first window is created, so Main calls it first.
/// </summary>
internal static class DpiAwareness
{
    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, -4 as a handle.</summary>
    private static readonly IntPtr PerMonitorV2Context = new(-4);

    private const int AwarenessSystem = 1;
    private const int AwarenessPerMonitor = 2;

    public static DpiAwarenessResult Apply()
    {
        // Windows 10 1703 and later. Fails harmlessly if the process is already aware.
        if (NativeMethods.TrySetPerMonitorV2Awareness())
        {
            return DpiAwarenessResult.PerMonitorV2;
        }

        // A failure usually means something already set it, so ask what it is rather
        // than falling through to a call that would only be rejected too.
        if (NativeMethods.TryGetProcessDpiAwareness(out int awareness))
        {
            if (awareness == AwarenessPerMonitor)
            {
                return DpiAwarenessResult.PerMonitorV2;
            }

            if (awareness == AwarenessSystem)
            {
                return DpiAwarenessResult.SystemAware;
            }
        }

        return NativeMethods.TrySetSystemAwareness()
            ? DpiAwarenessResult.SystemAware
            : DpiAwarenessResult.Unavailable;
    }

    /// <summary>True when the process is aware of scaling, so no window is stretched.</summary>
    public static bool IsAware(this DpiAwarenessResult result)
        => result is DpiAwarenessResult.PerMonitorV2 or DpiAwarenessResult.SystemAware;
}
