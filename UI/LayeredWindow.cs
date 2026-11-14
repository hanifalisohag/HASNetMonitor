using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TaskbarNetMonitor.Interop;

namespace TaskbarNetMonitor.UI;

/// <summary>
/// A popup window whose content is presented exclusively through
/// <c>UpdateLayeredWindow</c>: one call moves, resizes and blits the finished frame
/// atomically, so no WM_PAINT ever runs and the window can never show a half-painted
/// state (the flicker of the previous implementation). Per-pixel alpha means the
/// taskbar really shows through instead of a colour-keyed hole.
/// </summary>
internal class LayeredWindow : NativeWindow, IDisposable
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    /// <summary>
    /// How far the z-order walk may travel before giving up. A window that is truly
    /// above ours is a step or two away; a chain this long means the walk failed, and a
    /// failed walk must never be read as "we are buried".
    /// </summary>
    private const int MaxZOrderWalk = 512;

    /// <summary>
    /// WM_SETCURSOR. Handled explicitly because our window class carries no cursor
    /// of its own: without this, Windows shows the "wait" pointer on hover.
    /// </summary>
    private const int WmSetCursor = NativeMethods.WM_SETCURSOR;

    private IntPtr _dibSection;
    private IntPtr _dibBits;
    private IntPtr _memoryDc;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _surfaceX;
    private int _surfaceY;
    private bool _isVisible;
    private bool _disposed;

    /// <summary>Serialises Present and Represent; they may run on different threads.</summary>
    private readonly object _surfaceGate = new();

    /// <summary>Handle captured at creation; safe to read from other threads.</summary>
    private volatile IntPtr _windowHandle;

    internal LayeredWindow()
    {
        var createParams = new CreateParams
        {
            Caption = string.Empty,
            X = -32000,
            Y = -32000,
            Width = 1,
            Height = 1,
            Style = NativeMethods.WS_POPUP,
            ExStyle = NativeMethods.WS_EX_LAYERED
                | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TOPMOST
                | NativeMethods.WS_EX_NOACTIVATE,
            ClassStyle = 0,
            Parent = IntPtr.Zero,
        };

        CreateHandle(createParams);
        _windowHandle = Handle;
        int enabled = 1;
        PeekExclusionResult = NativeMethods.DwmSetWindowAttribute(Handle, 12, ref enabled, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(Handle, 11, ref enabled, sizeof(int)); // Disallow Peek
    }

    /// <summary>Point that can be posted to from any thread.</summary>
    protected IntPtr HandleForPosting => _windowHandle;

    public bool IsShown => _isVisible;

    /// <summary>
    /// Our own idea of visibility. The window can be taken off screen by Windows
    /// without telling us, so never trust this alone.
    /// </summary>
    public bool IsActuallyVisible => !_disposed && _windowHandle != IntPtr.Zero
        && NativeMethods.IsWindowVisible(_windowHandle);

    public bool IsDisposed => _disposed;
    internal int PeekExclusionResult { get; }

    /// <summary>
    /// Repairs the window's own state: displaced, hidden, or no longer "always on top".
    /// Deliberately knows nothing about z-order relative to other windows: being under
    /// the Start menu, a flyout or a context menu is correct and must be left alone,
    /// and only the widget knows which window it has to clear (see
    /// <see cref="SeatAboveTaskbar"/>). Repairing on "something overlaps us" is what
    /// made the widget blink every time the shell opened a menu over it.
    /// </summary>
    internal bool RestorePresence()
    {
        if (_disposed || !_isVisible || _dibBits == IntPtr.Zero)
        {
            return false;
        }

        bool displaced = !NativeMethods.GetWindowRect(_windowHandle, out NativeRect rect)
            || rect.Left != _surfaceX || rect.Top != _surfaceY
            || rect.Right - rect.Left != _surfaceWidth || rect.Bottom - rect.Top != _surfaceHeight;
        bool demoted = (NativeMethods.GetWindowLong(_windowHandle, -20) & NativeMethods.WS_EX_TOPMOST) == 0;
        if (!displaced && !demoted && IsActuallyVisible)
            return false;

        FrameTrace.Write($"presence repair: displaced={displaced} demoted={demoted} visible={IsActuallyVisible}");
        AssertTopMost();
        return Represent();
    }

    /// <summary>
    /// Whether <paramref name="upper"/> is somewhere above <paramref name="lower"/> in
    /// the z-order. A plain walk up the window chain, so a window that merely overlaps
    /// us (any menu, flyout or tooltip) is not mistaken for one that is really above.
    /// A chain that cannot be walked to its end is not a burial: never repair on a guess.
    /// </summary>
    internal static bool IsAboveInZOrder(IntPtr upper, IntPtr lower)
    {
        if (upper == IntPtr.Zero || lower == IntPtr.Zero || upper == lower)
        {
            return false;
        }

        IntPtr walk = lower;
        for (int step = 0; step < MaxZOrderWalk; step++)
        {
            walk = NativeMethods.GetWindow(walk, NativeMethods.GW_HWNDPREV);
            if (walk == IntPtr.Zero)
            {
                return false;
            }

            if (walk == upper)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="anchor"/> sits above this window.</summary>
    internal bool IsBuriedBehind(IntPtr anchor)
        => !_disposed && _windowHandle != IntPtr.Zero && IsAboveInZOrder(anchor, _windowHandle);

    /// <summary>
    /// Whether the shell is drawn over this window right now. The point that is
    /// hit-tested must be a pixel this window really paints: hit-testing a transparent
    /// pixel of a layered window answers with the window underneath, and that is how an
    /// endless "always on top" re-assert (and the blink it caused) used to start.
    /// No painted pixel at all means no answer, never a repair on a guess.
    /// </summary>
    internal bool IsCoveredBy(IntPtr shell)
    {
        if (_disposed || _windowHandle == IntPtr.Zero || _dibBits == IntPtr.Zero || !_isVisible)
        {
            return false;
        }

        if (!TryFindPaintedPoint(_surfaceWidth, _surfaceHeight, PaintedAlpha, out int x, out int y))
        {
            return false;
        }

        return IsShellAtPoint(_surfaceX + x, _surfaceY + y, _windowHandle, shell);
    }

    /// <summary>Diagnostic: what is drawn at the sampled painted pixel.</summary>
    internal string CoveringWindowClassName()
    {
        if (_disposed || _windowHandle == IntPtr.Zero || _dibBits == IntPtr.Zero
            || !TryFindPaintedPoint(_surfaceWidth, _surfaceHeight, PaintedAlpha, out int x, out int y))
        {
            return "none";
        }

        IntPtr at = NativeMethods.WindowFromPoint(new NativePoint(_surfaceX + x, _surfaceY + y));
        return at == IntPtr.Zero
            ? "none"
            : (at == _windowHandle ? "self" : NativeMethods.GetClassNameOf(at));
    }

    private byte PaintedAlpha(int x, int y)
        => Marshal.ReadByte(_dibBits, (((y * _surfaceWidth) + x) * 4) + 3);

    internal const byte PaintedAlphaValue = 255;

    /// <summary>
    /// The fully painted pixel nearest the middle of a surface, or false when the
    /// surface has none. Scanning starts at the middle, so the common case (an opaque
    /// frame) costs one read.
    /// </summary>
    internal static bool TryFindPaintedPoint(
        int width, int height, Func<int, int, byte> alpha, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        int centreX = width / 2;
        int centreY = height / 2;
        if (alpha(centreX, centreY) == PaintedAlphaValue)
        {
            x = centreX;
            y = centreY;
            return true;
        }

        long best = long.MaxValue;
        bool found = false;
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                if (alpha(px, py) != PaintedAlphaValue)
                {
                    continue;
                }

                long dx = px - centreX;
                long dy = py - centreY;
                long distance = (dx * dx) + (dy * dy);
                if (distance < best)
                {
                    best = distance;
                    x = px;
                    y = py;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Whether <paramref name="shell"/> is the window drawn over the middle of
    /// <paramref name="window"/>. Used where the window has no surface of ours to sample
    /// (tests, and as the definition of "covering" for a plain window).
    /// </summary>
    internal static bool IsShellAtCentreOf(IntPtr window, IntPtr shell)
    {
        if (window == IntPtr.Zero || shell == IntPtr.Zero)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(window, out NativeRect rect))
        {
            return false;
        }

        return IsShellAtPoint(
            rect.Left + ((rect.Right - rect.Left) / 2),
            rect.Top + ((rect.Bottom - rect.Top) / 2),
            window,
            shell);
    }

    /// <summary>
    /// Whether a window of <paramref name="shell"/> is the window drawn at a screen
    /// point. Only the shell itself counts, so a menu or flyout merely overlapping the
    /// widget is never mistaken for the shell covering it.
    /// </summary>
    internal static bool IsShellAtPoint(int screenX, int screenY, IntPtr window, IntPtr shell)
    {
        if (window == IntPtr.Zero || shell == IntPtr.Zero)
        {
            return false;
        }

        IntPtr at = NativeMethods.WindowFromPoint(new NativePoint(screenX, screenY));
        return at != IntPtr.Zero && NativeMethods.GetAncestor(at, NativeMethods.GA_ROOT) == shell;
    }

    /// <summary>The window immediately above this one, or zero when nothing is.</summary>
    internal IntPtr WindowDirectlyAbove()
        => _disposed || _windowHandle == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetWindow(_windowHandle, NativeMethods.GW_HWNDPREV);

    /// <summary>
    /// Whether the show-desktop peek is above the widget. Win+D raises a desktop
    /// surface over every window on the desktop without hiding ours or telling us.
    /// </summary>
    internal bool IsBehindDesktopPeek(IntPtr taskbar)
    {
        IntPtr above = WindowDirectlyAbove();
        return above != IntPtr.Zero && above != taskbar && IsShellSurface(above, taskbar);
    }

    /// <summary>
    /// Makes <paramref name="owner"/> this window's owner, once. An owned window is
    /// composited with its owner, so the widget follows the taskbar into the higher Z
    /// band that Start and Task View raise, and shows on every virtual desktop the
    /// taskbar shows on. Nothing is moved, resized or reordered, so this cannot blink.
    /// </summary>
    /// <returns>True only when the owner actually changed.</returns>
    internal bool DockTo(IntPtr owner)
    {
        if (_disposed || _windowHandle == IntPtr.Zero || owner == IntPtr.Zero || owner == _windowHandle)
        {
            return false;
        }

        if (NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GWLP_HWNDPARENT) == owner)
        {
            return false;
        }

        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWLP_HWNDPARENT, owner);
        FrameTrace.Write("presence: docked to the shell owner");
        return true;
    }

    /// <summary>
    /// Puts the widget back directly above <paramref name="taskbar"/>: it is inserted
    /// after whatever currently sits right above the taskbar, never at the top of the
    /// band, so every menu, flyout and tooltip that was already above the taskbar stays
    /// above the widget. No move, no size change, no activation, and the owner is not
    /// dragged along.
    /// </summary>
    /// <returns>True only when a re-seat was actually performed.</returns>
    internal bool SeatAboveTaskbar(IntPtr taskbar)
    {
        if (_disposed || _windowHandle == IntPtr.Zero || taskbar == IntPtr.Zero)
        {
            return false;
        }

        IntPtr neighbour = NativeMethods.GetWindow(taskbar, NativeMethods.GW_HWNDPREV);
        if (neighbour == _windowHandle)
        {
            return false; // Already directly above it: touching the z-order would be a no-op blink.
        }

        // A hole above the taskbar means it is the first window in its band; only then
        // may the top of the band be used.
        IntPtr insertAfter = neighbour == IntPtr.Zero ? NativeMethods.HWND_TOPMOST : neighbour;
        if (!NativeMethods.SetWindowPos(
            _windowHandle,
            insertAfter,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER))
        {
            FrameTrace.Write($"SetWindowPos(seat) failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        FrameTrace.Write("presence: seated directly above the taskbar");
        return true;
    }

    /// <summary>Centre of what is currently on screen, or null before the first frame.</summary>
    /// <summary>Temporary: the surface rectangle, for diagnostics.</summary>
    internal (int X, int Y, int W, int H)? SurfaceRect
        => _dibBits == IntPtr.Zero || !_isVisible
            ? null
            : (_surfaceX, _surfaceY, _surfaceWidth, _surfaceHeight);

    internal System.Drawing.Point? SurfaceCentre
        => _dibBits == IntPtr.Zero || !_isVisible
            ? null
            : new System.Drawing.Point(_surfaceX + (_surfaceWidth / 2), _surfaceY + (_surfaceHeight / 2));

    /// <summary>Topmost window under a screen point, root ancestor included.</summary>
    internal static IntPtr WindowAt(System.Drawing.Point point)
        => NativeMethods.WindowFromPoint(new NativePoint(point.X, point.Y));

    /// <summary>
    /// Whether the window at <paramref name="point"/> is this widget. Windows only
    /// answers with our handle when our layered surface is actually being composited,
    /// so this doubles as "is our frame really on screen".
    /// </summary>
    internal bool IsOnTopAt(System.Drawing.Point point)
        => !_disposed && _windowHandle != IntPtr.Zero && WindowAt(point) == _windowHandle;

    /// <summary>
    /// Whether the window under a point is a piece of the shell surface (the taskbar,
    /// the desktop, or the show-desktop backdrop) rather than an app window or a menu.
    /// Those are the only things the widget is allowed to put itself back above.
    /// </summary>
    internal static bool IsShellSurface(IntPtr window, IntPtr taskbar)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        IntPtr root = NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
        {
            root = window;
        }

        if (root == taskbar || root == NativeMethods.GetShellWindow())
        {
            return true;
        }

        return NativeMethods.GetClassNameOf(root) is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> sits above this widget in the z-order.
    /// This is a plain walk of the window chain, so unlike a pixel probe it does not
    /// depend on where the (transparent) pixels happen to be drawn.
    /// </summary>
    internal bool SitsAboveInZOrder(IntPtr candidate)
    {
        if (_disposed || _windowHandle == IntPtr.Zero || candidate == IntPtr.Zero)
        {
            return false;
        }

        for (IntPtr h = NativeMethods.GetTopWindow(IntPtr.Zero);
             h != IntPtr.Zero;
             h = NativeMethods.GetWindow(h, NativeMethods.GW_HWNDNEXT))
        {
            if (h == _windowHandle)
            {
                return false;
            }

            if (h == candidate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Presents <paramref name="bitmap"/> with its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>). Unsigned pixels are fully
    /// transparent; the blit is atomic.
    /// </summary>
    public void Present(Bitmap bitmap, int x, int y)
    {
        if (_disposed || _windowHandle == IntPtr.Zero || bitmap is null)
        {
            return;
        }

        // The presence timer re-blits from another thread; the surface and its device
        // context are shared, so every write to them is serialised here.
        lock (_surfaceGate)
        {
            if (_disposed)
            {
                return;
            }

            if (!EnsureSurface(bitmap.Width, bitmap.Height))
            {
                return;
            }

            if (!CopyToSurface(bitmap))
            {
                return;
            }

            _surfaceX = x;
            _surfaceY = y;
            Blit();
        }
    }

    public bool Represent()
    {
        if (_disposed || _windowHandle == IntPtr.Zero)
        {
            return false;
        }

        lock (_surfaceGate)
        {
            if (_disposed || _dibBits == IntPtr.Zero)
            {
                return false;
            }

            return Blit();
        }
    }

    private bool Blit()
    {
        var destination = new NativePoint(_surfaceX, _surfaceY);
        var size = new NativeSize(_surfaceWidth, _surfaceHeight);
        var source = new NativePoint(0, 0);
        var blend = new BlendFunction
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA,
        };

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            if (!NativeMethods.UpdateLayeredWindow(
                _windowHandle,
                screenDc,
                ref destination,
                ref size,
                _memoryDc,
                ref source,
                0,
                ref blend,
                NativeMethods.ULW_ALPHA))
            {
                FrameTrace.Write($"UpdateLayeredWindow failed: {Marshal.GetLastWin32Error()}");
                return false;
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        // Something outside may have hidden us while our flag still said "shown",
        // so ask Windows rather than trusting the flag.
        if (!_isVisible || !NativeMethods.IsWindowVisible(_windowHandle))
        {
            FrameTrace.Write("LayeredWindow: SW_SHOWNOACTIVATE");
            NativeMethods.ShowWindow(_windowHandle, NativeMethods.SW_SHOWNOACTIVATE);
            _isVisible = true;

            // Re-assert topmost only when we actually appear; doing this per frame
            // is what makes a widget fight with the taskbar.
            AssertTopMost();
        }
        return true;
    }

    public void HideIfShown()
    {
        if (_disposed || !_isVisible)
        {
            return;
        }

        FrameTrace.Write("LayeredWindow: SW_HIDE");
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SW_HIDE);
        _isVisible = false;
    }

    /// <summary>
    /// Re-asserts "always on top". Only needed when explorer recreates the taskbar,
    /// whose new window would otherwise sit above us.
    /// </summary>
    protected void AssertTopMost()
    {
        if (_disposed || _windowHandle == IntPtr.Zero || !_isVisible)
        {
            return;
        }

        FrameTrace.Write("LayeredWindow: AssertTopMost");
        if (!NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER))
            FrameTrace.Write($"SetWindowPos failed: {Marshal.GetLastWin32Error()}");
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmSetCursor:
                // A window of a class without a cursor falls back to the wait
                // pointer, which is why hovering the widget showed a spinning
                // "working" icon. It is an ordinary clickable taskbar item.
                NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
                m.Result = (IntPtr)1;
                return;

            case WmNcHitTest:
                // Whole widget rectangle accepts clicks, including the transparent gaps.
                m.Result = (IntPtr)HtClient;
                return;

            case WmMouseActivate:
                m.Result = (IntPtr)MaNoActivate;
                return;
        }

        base.WndProc(ref m);
    }

    private bool EnsureSurface(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (_dibBits != IntPtr.Zero && _surfaceWidth == width && _surfaceHeight == height)
        {
            return true;
        }

        ReleaseSurface();

        var header = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // top-down, so it matches the GDI+ scan order
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = 0,
            },
        };

        IntPtr dib = NativeMethods.CreateDIBSection(
            IntPtr.Zero,
            ref header,
            NativeMethods.BI_RGB,
            out IntPtr bits,
            IntPtr.Zero,
            0);

        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            return false;
        }

        IntPtr memoryDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc == IntPtr.Zero)
        {
            NativeMethods.DeleteObject(dib);
            return false;
        }

        NativeMethods.SelectObject(memoryDc, dib);

        _dibSection = dib;
        _dibBits = bits;
        _memoryDc = memoryDc;
        _surfaceWidth = width;
        _surfaceHeight = height;
        return true;
    }

    private bool CopyToSurface(Bitmap bitmap)
    {
        BitmapData data;
        try
        {
            data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            return false;
        }

        try
        {
            int rowBytes = bitmap.Width * 4;
            if (data.Stride == rowBytes)
            {
                var buffer = new byte[rowBytes * bitmap.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                Marshal.Copy(buffer, 0, _dibBits, buffer.Length);
                return true;
            }

            for (int row = 0; row < bitmap.Height; row++)
            {
                IntPtr rowSource = IntPtr.Add(data.Scan0, row * data.Stride);
                IntPtr rowDestination = IntPtr.Add(_dibBits, row * rowBytes);
                var buffer = new byte[rowBytes];
                Marshal.Copy(rowSource, buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, rowDestination, rowBytes);
            }

            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private void ReleaseSurface()
    {
        if (_memoryDc != IntPtr.Zero)
        {
            NativeMethods.DeleteDC(_memoryDc);
            _memoryDc = IntPtr.Zero;
        }

        if (_dibSection != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_dibSection);
            _dibSection = IntPtr.Zero;
        }

        _dibBits = IntPtr.Zero;
        _surfaceWidth = 0;
        _surfaceHeight = 0;
    }

    protected virtual void OnHandleDestroyed()
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isVisible = false;

        OnHandleDestroyed();
        ReleaseSurface();

        if (_windowHandle != IntPtr.Zero)
        {
            _windowHandle = IntPtr.Zero;
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
