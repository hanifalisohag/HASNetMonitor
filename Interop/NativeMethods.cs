using System.Runtime.InteropServices;

namespace HASNetMonitor.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;

    public NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int Cx;
    public int Cy;

    public NativeSize(int cx, int cy)
    {
        Cx = cx;
        Cy = cy;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BlendFunction
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ColorsUsed;
    public uint ColorsImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfo
{
    public BitmapInfoHeader Header;
    public uint Colors;
}

/// <summary>
/// Thin, allocation-free wrappers over the Win32 entry points the widget needs.
/// Everything here works on window handles only, so the process never relies on
/// the WinForms paint stack (that stack is what made the previous version flicker).
/// </summary>
internal static class NativeMethods
{
    internal delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr window,
        int objectId, int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr window, int index);

    /// <summary>
    /// Reads a pointer-sized window long. The owner of a window lives in
    /// GWLP_HWNDPARENT, which needs the 64-bit entry point on 64-bit Windows.
    /// </summary>
    internal static IntPtr GetWindowLongPtr(IntPtr window, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new IntPtr(GetWindowLong32(window, index));

    /// <summary>
    /// Writes a pointer-sized window long (the owner, in this app). Failing to set it
    /// is not fatal: everything else still works, the widget just stays unowned.
    /// </summary>
    internal static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr window);

    private delegate bool GetWindowBand(IntPtr window, out uint band);

    /// <summary>
    /// Probed once at startup: GetWindowBand is exported by user32 but undocumented, so
    /// it must not be a hard import. A missing export simply means "band unknown".
    /// </summary>
    private static readonly GetWindowBand? WindowBandProbe = FindWindowBand();

    /// <summary>The Z band of a window, or false when the running Windows has no such API.</summary>
    internal static bool TryGetWindowBand(IntPtr window, out uint band)
    {
        band = 0;
        if (window == IntPtr.Zero || WindowBandProbe is null)
        {
            return false;
        }

        try
        {
            return WindowBandProbe(window, out band);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    private static GetWindowBand? FindWindowBand()
    {
        try
        {
            if (TryResolveGetWindowBand(out IntPtr entry))
            {
                return Marshal.GetDelegateForFunctionPointer<GetWindowBand>(entry);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // No band information: every check falls back to comparing z-order alone.
        }

        return null;
    }

    /// <summary>
    /// Finds <c>GetWindowBand</c>, which only some builds of user32 export. .NET
    /// Framework has no NativeLibrary, so each target loads it its own way.
    /// </summary>
    private static bool TryResolveGetWindowBand(out IntPtr entry)
    {
        entry = IntPtr.Zero;
#if NETFRAMEWORK
        return Core.Compat.TryGetExport("user32.dll", "GetWindowBand", out entry);
#else
        if (!NativeLibrary.TryLoad("user32.dll", out IntPtr user32))
        {
            return false;
        }

        return NativeLibrary.TryGetExport(user32, "GetWindowBand", out entry) && entry != IntPtr.Zero;
#endif
    }
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    // Window styles and flags, spelled exactly as in the Win32 headers so the
    // layer this app builds on stays recognisable.
    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal const int AC_SRC_OVER = 0x00;
    internal const int AC_SRC_ALPHA = 0x01;
    internal const int ULW_ALPHA = 0x02;

    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>
    /// Reordering must never drag the owner along: the widget is owned by the taskbar,
    /// and moving the taskbar with us is exactly the fight this code avoids.
    /// </summary>
    internal const uint SWP_NOOWNERZORDER = 0x0200;

    /// <summary>GWLP_HWNDPARENT: the owner of a top-level window.</summary>
    internal const int GWLP_HWNDPARENT = -8;

    /// <summary>
    /// The Z band a normal desktop window lives in. Anything above it means the shell
    /// has raised a window (Start, Task View, the alt-tab switcher) on top of the
    /// whole desktop, which is a state the widget must not repair inside.
    /// </summary>
    internal const uint ZBID_DESKTOP = 1;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;

    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const uint MONITOR_DEFAULTTONULL = 1;

    internal const int BI_RGB = 0;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo pbmi,
        uint iUsage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint dwOffset);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetDC(IntPtr hwnd);

    /// <summary>Pixel colour of a device at a point (COLORREF).</summary>
    [DllImport("gdi32.dll")]
    internal static extern uint GetPixel(IntPtr hdc, int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    /// <summary>
    /// Topmost window under a screen point. Used to tell whether anything (a menu, a
    /// full-screen app, the show-desktop peek) is currently covering the widget.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>The window's class name, or an empty string when it cannot be read.</summary>
    internal static string GetClassNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new char[256];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] name, int maxCount);

    /// <summary>The shell's desktop window; it owns the foreground during show-desktop.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    internal const uint GA_ROOT = 2;

    /// <summary>Top window in the z-order, start of the GW_HWNDNEXT chain.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetTopWindow(IntPtr hwnd);

    /// <summary>Walks the z-order with GW_HWNDNEXT.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    internal const uint GW_HWNDNEXT = 2;
    internal const uint GW_HWNDPREV = 3;

    [DllImport("user32.dll")]
    internal static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    internal static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    /// <summary>IDC_ARROW as a resource name; see MAKEINTRESOURCE.</summary>
    internal static readonly IntPtr IDC_ARROW = new IntPtr(32512);

    internal const int WM_SETCURSOR = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Broadcast by explorer whenever it recreates the taskbar.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllocConsole();

    // --- Window creation, used to check the presence rules against real windows. ---

    internal const uint WS_CHILD = 0x40000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProc;        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    internal delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    // --- DPI awareness, set in code so no App.config has to ship beside the exe. ---

    /// <summary>
    /// SetProcessDpiAwarenessContext is Windows 10 1703+. It is probed rather than
    /// hard-imported, because a missing export on an older Windows must fall through to
    /// the system-aware call instead of crashing at start-up.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();

    /// <summary>shcore!GetProcessDpiAwareness; absent before Windows 8.1.</summary>
    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);

    public static bool TrySetPerMonitorV2Awareness()
    {
        try
        {
            return SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    public static bool TrySetSystemAwareness()
    {
        try
        {
            return SetProcessDPIAware();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Current awareness: 0 unaware, 1 system-aware, 2 per-monitor. Returns false when
    /// the API is missing, so "unknown" is never mistaken for "nothing set".
    /// </summary>
    public static bool TryGetProcessDpiAwareness(out int awareness)
    {
        try
        {
            return GetProcessDpiAwareness(IntPtr.Zero, out awareness) == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            awareness = 0;
            return false;
        }
    }

    // ---- System statistics ────────────────────────────────────────────────────
    // Pure counter reads: no WMI, no performance-counter helper libraries, nothing
    // from the framework beyond what the whole app already uses. Every one of them
    // is allowed to fail; "unknown" is reported, never guessed.

    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMemoryStatusEx
    {
        public int Size;
        public uint Load;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    internal static extern bool GlobalMemoryStatusEx(ref NativeMemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode)]
    internal static extern bool GetDiskFreeSpaceExW(string? directory, out long lpFreeBytesAvailable, out long lpTotalNumberOfBytes, out long lpTotalNumberOfFreeBytes);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemPowerStatus(out NativePowerStatus lpSystemPowerStatus);

    /// <summary>PDH_FMT_DOUBLE | PDH_FMT_NOCAP100: read a counter as a raw double.</summary>
    internal const uint PdhFmtDouble = 0x00000200 | 0x00008000;

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    internal static extern int PdhOpenQueryW(string? szDataSource, long dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    internal static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, long dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
    internal static extern int PdhCollectQueryData(IntPtr hQuery);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue")]
    internal static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, IntPtr lpdwType, ref PdhFmtCounterValue pValue);

    [DllImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
    internal static extern int PdhCloseQuery(IntPtr hQuery);

    // ---- NVML (NVIDIA) ─────────────────────────────────────────────────────────
    // The generic PDH counters above are not universal - some NVIDIA drivers expose
    // no GPU Engine counters at all, so this is the fallback that reads the NVIDIA
    // Management Library directly. Anything that fails here is treated as "unknown",
    // never as a value.

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    internal static extern int nvmlInitV2();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
    internal static extern int nvmlDeviceGetCountV2(out uint deviceCount);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    internal static extern int nvmlDeviceGetHandleByIndexV2(uint deviceIndex, out IntPtr device);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlUtilizationData
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    internal static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilizationData rates);
}
