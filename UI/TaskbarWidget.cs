using System.Diagnostics;
using System.Drawing;
using HASNetMonitor.Core;
using HASNetMonitor.Interop;
using HASNetMonitor.Rendering;

namespace HASNetMonitor.UI;

/// <summary>
/// The taskbar widget itself: samples the counters on a background thread, then
/// draws one frame and shows it. Rendering is skipped entirely when nothing that
/// can be seen has changed, so an idle network costs no window updates at all.
/// </summary>
internal sealed class TaskbarWidget : LayeredWindow
{
    private const int WmSampleReady = 0x8000 + 1;
    private const int WmForceRedraw = 0x8000 + 2;
    private const int WmRestorePresence = 0x8000 + 3;
    private NativeMethods.WinEventCallback? _shellEventCallback;
    private IntPtr _shellEventHook;
    private int _presenceQueued;

    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int WmThemeChanged = 0x031A;
    private const int WmDpiChanged = 0x02E0;
    private const int WmActivateApp = 0x001C;


    /// <summary>
    /// How often the frame already in memory is pushed back to the screen. Small
    /// enough that a surface dropped by the shell is restored below the threshold of
    /// noticing, cheap enough to be free: one 72x44 blit, no rendering, no z-order.
    /// </summary>
    private const int PresenceIntervalMs = 60;

    /// <summary>
    /// Minimum gap between unconditional re-blits. Windows can drop a layered
    /// window's surface (show desktop, Task View, a driver reset) and still report the
    /// window as visible and on top, so the only reliable cure is to keep re-presenting
    /// what we already drew. Re-presenting the same content does not change z-order, so
    /// it cannot draw over the Start menu or a full-screen app.
    /// </summary>
    private static readonly TimeSpan RepresentInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The first z-order repair after a fault is immediate; a repeat within this window
    /// means the shell is winning, and each further attempt doubles up to the cap.
    /// </summary>
    private const double MinRepairIntervalSeconds = 1;
    private const double MaxRepairIntervalSeconds = 16;
    private const int MaxRepairAttempts = 5;

    /// <summary>How long after the band drops a re-seat still counts as the shell's doing.</summary>
    private static readonly TimeSpan BandReturnGrace = TimeSpan.FromSeconds(3);

    /// <summary>How long the widget has to hold its place before the backoff is forgotten.</summary>
    private static readonly TimeSpan HealthyHold = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the widget may ask the shell whether it is still on the active virtual
    /// desktop. Far cheaper than the presence tick and useless at a higher rate.
    /// </summary>
    private static readonly TimeSpan DesktopCheckInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Readings in a row before the widget believes it is on another desktop.</summary>
    private const int DesktopMissesBeforeFollowing = 3;


    private readonly MonitorConfig _config;
    private MonitorConfig _liveConfig;
    private readonly WidgetRenderer _renderer = new();
    private readonly TrafficSampler _sampler = new();
    private readonly SystemStats _stats = new();
    private readonly RateCalculator _download = new();
    private readonly RateCalculator _upload = new();
    private readonly AutoScaler _scaler = new();
    private readonly BarSmoother _downloadBar = new();
    private readonly BarSmoother _uploadBar = new();
    private readonly object _statsGate = new();
    private readonly object _pendingGate = new();
    private readonly object _timerGate = new();
    private readonly int _taskbarCreatedMessage =
        unchecked((int)NativeMethods.RegisterWindowMessage("TaskbarCreated"));

    /// <summary>
    /// Whether the widget has to put itself back on top. Explorer moves windows above
    /// it without hiding it, moving it or sending it anything, so this is evaluated on
    /// a tick: being covered by a menu, the Start menu or a full-screen app is correct
    /// and must be left alone.
    /// </summary>
    /// <param name="isOnTop">Nothing but our own windows is above us.</param>
    /// <param name="coveredByTaskbar">The taskbar is above the widget in the z-order.</param>
    /// <param name="peekActive">The shell desktop is above it (show-desktop).</param>
    internal static bool NeedsRecovery(bool isShown, bool isOnTop, bool coveredByTaskbar, bool peekActive)
        => isShown && !isOnTop && (coveredByTaskbar || peekActive);

    /// <summary>
    /// Rate limit for the z-order re-assert. Recovering on every tick would itself
    /// look like flicker, so at most one attempt is made per interval.
    /// </summary>
    internal static bool ShouldRecoverNow(TimeSpan sinceRecovery, TimeSpan interval)
        => sinceRecovery >= interval;

    /// <summary>
    /// Whether the compositor has stopped drawing the widget. Windows can leave a
    /// layered window reporting "visible, topmost, correctly placed" while nothing of
    /// it is on screen, and no amount of re-blitting brings it back: only putting the
    /// window back in place does. Hit-testing is the cheap way to notice, because
    /// Windows only answers with our handle while our surface is really composited.
    /// </summary>
    /// <param name="hitTestIsOurs">WindowFromPoint over our own centre answered us.</param>
    /// <param name="coveringIsShellSurface">
    /// What is under that point is the taskbar, the desktop or the show-desktop
    /// backdrop rather than a menu or an app window.
    /// </param>
    /// <summary>
    /// The taskbar raises its own window a moment after ours appears, so the first
    /// couple of frames must re-assert "always on top" or the widget is drawn but
    /// covered until the next repair tick.
    /// </summary>
    internal static bool NeedsStartupTopMost(int presentedFrames) => presentedFrames is > 0 and <= 3;

    private (long Received, long Sent, long Timestamp, SystemStatsReading Stats)? _pendingSample;
    private System.Threading.Timer? _timer;
    private long _lastTimestamp;
    private WidgetVisual _visual = WidgetVisual.Empty;
    private PresentationState? _presented;
    private long _lastPresentTicks;
    private int _presentedFrames;
    private long _lastRepairTicks;
    private int _repairAttempts;
    private long _bandRaisedTicks;
    private long _bandDroppedTicks;
    private long _lastDesktopCheckTicks;
    private int _desktopMisses;
    private int _desktopFollowFailures;
    private long _lastDesktopFollowTicks;
    private readonly DesktopPresence _desktopPresence = new();
    private IntPtr _taskbarHandle;
    private System.Windows.Forms.Timer? _presenceTimer;
    private int _configVersion;
    private volatile bool _stopped;

    public TaskbarWidget(MonitorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        // The settings dialog mutates _config in place on the UI thread while the
        // sample thread reads it, so the background thread gets its own consistent
        // snapshot that is refreshed on every ApplyConfig.
        _liveConfig = config.Clone();

        (long received, long sent) totals = _sampler.ReadTotals();
        _download.Seed(totals.received);
        _upload.Seed(totals.sent);
        _lastTimestamp = Stopwatch.GetTimestamp();

        StartTimer();
        _shellEventCallback = OnShellEvent;
        // Foreground, menu/popup, desktop-switch and object reorder/show events.
        // Out-of-context callbacks are delivered to this UI thread; never repaint inside them.
        _shellEventHook = NativeMethods.SetWinEventHook(0x0003, 0x8017, IntPtr.Zero,
            _shellEventCallback, 0, 0, 2); // WINEVENT_SKIPOWNPROCESS
        FrameTrace.StartBlinkWatch(() => SurfaceRect);
    }

    public event Action<Point>? MenuRequested;

    /// <summary>
    /// Raised on every sample tick with the one-line caption for the tray tooltip:
    /// the traffic figures followed by the statistics, when any are shown.
    /// </summary>
    public event Action<string>? CaptionChanged;

    /// <summary>The one-line caption for the tray tooltip.</summary>
    internal static string BuildCaption(SystemStatsReading reading, double downloadRate, double uploadRate, MonitorConfig config)
    {
        string stats = SystemStats.JoinedText(reading, config);
        string traffic = $"\u2193 {SpeedFormatter.Format(downloadRate)}  \u2191 {SpeedFormatter.Format(uploadRate)}";
        return string.IsNullOrEmpty(stats) ? traffic : $"{traffic}  \u00b7  {stats}";
    }

    private void OnShellEvent(IntPtr hook, uint eventId, IntPtr window,
        int objectId, int childId, uint threadId, uint time)
    {
        if (_stopped || eventId is not (0x0003 or 0x0007 or 0x0017 or 0x0020
            or 0x8002 or 0x8003 or 0x8004 or 0x800B))
            return;
        if (Interlocked.Exchange(ref _presenceQueued, 1) == 0)
            NativeMethods.PostMessage(HandleForPosting, WmRestorePresence, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Changed settings: redraw immediately and, if needed, resample faster.</summary>
    public void ApplyConfig()
    {
        Interlocked.Increment(ref _configVersion);
        // Refresh the background thread's snapshot of the config: every live dialog
        // change mutates _config in place on the UI thread.
        _liveConfig = _config.Clone();
        // The statistics are not reset here: dropping the baseline and every held
        // reading on every live settings change makes the chips vanish for a tick and
        // makes every dialog edit re-prime the CPU counter. The held values age out
        // naturally and re-arm on the next sample, which is enough.
        StartTimer();
        ForceRedraw();
    }

    /// <summary>Drops the auto-scale reference so the bars re-fit the current traffic.</summary>
    public void ResetScale()
    {
        _scaler.Reset();
        _download.Reset();
        _upload.Reset();
        lock (_statsGate)
        {
            _stats.Reset();
        }
        ForceRedraw();
    }

    public void ForceRedraw()
    {
        IntPtr handle = HandleForPosting;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.PostMessage(handle, WmForceRedraw, IntPtr.Zero, IntPtr.Zero);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmRestorePresence:
                Interlocked.Exchange(ref _presenceQueued, 0);
                KeepPresent();
                return;
            case WmSampleReady:
                ConsumeSample();
                return;

            case WmForceRedraw:
                Render();
                return;

            case WmContextMenu:
            case WmRButtonUp:
                MenuRequested?.Invoke(Cursor.Position);
                return;

            case WmSettingChange:
            case WmDisplayChange:
            case WmThemeChanged:
            case WmDpiChanged:
                FrameTrace.Write($"WndProc: msg 0x{m.Msg:X4} -> render");
                Render();
                break;

            case WmActivateApp:
                // Deliberately nothing. This message arrives on every foreground change
                // anywhere on the desktop; throwing the cached frame away here caused a
                // re-present (a visible blink) whenever the user clicked anything.
                return;
        }

        // Explorer recreated the taskbar (crash, update, theme switch): its fresh
        // window is above ours, so re-assert and repaint.
        if (m.Msg == _taskbarCreatedMessage)
        {
            _presented = null;
            AssertTopMost();
            Render();
            return;
        }

        base.WndProc(ref m);
    }

    private void StartTimer()
    {
        lock (_timerGate)
        {
            _timer?.Dispose();
            int interval = _config.EffectiveRefreshIntervalMs;
            _timer = new System.Threading.Timer(static state => ((TaskbarWidget)state!).SampleOnce(), this, 100, interval);

            // Native window operations belong to the creating UI thread.
            _presenceTimer?.Dispose();
            _presenceTimer = new System.Windows.Forms.Timer { Interval = PresenceIntervalMs };
            _presenceTimer.Tick += (_, _) => KeepPresent();
            _presenceTimer.Start();
        }
    }

    private void KeepPresent()
    {
        if (_stopped)
            return;

        IntPtr taskbar = ResolveTaskbarHandle();

        // An auto-hide bar, or a full-screen app that hides the taskbar, takes it away.
        // This widget is topmost, so left alone it would float over whatever owns the
        // screen; it goes with the taskbar instead. The repair paths are skipped too:
        // re-blitting a hidden widget would bring it straight back.
        if (!NativeMethods.IsWindowVisible(taskbar))
        {
            if (IsShown)
                HideIfShown();
            return;
        }

        // The taskbar does not have to go away to be covered: a browser in fullscreen
        // (YouTube, video players) fills its own monitor while the taskbar itself stays
        // alive and visible behind it. The widget must not float over that video either,
        // and it leaves the repair paths alone for the same reason.
        if (FullScreenCoversTaskbar(taskbar))
        {
            if (IsShown)
                HideIfShown();
            return;
        }

        // The taskbar is back (full-screen exited, auto-hide expanded) and the widget
        // is not up: present it again.
        if (!IsShown)
        {
            Render();
            return;
        }

        FollowDesktopIfStranded(DefersRepair(BandOf(taskbar), BandOf(HandleForPosting)));
        RecoverIfCovered(taskbar);
    }

    /// <summary>The taskbar handle from the last frame, re-resolved after explorer restarts.</summary>
    private IntPtr ResolveTaskbarHandle()
    {
        IntPtr taskbar = _taskbarHandle;
        if (!NativeMethods.IsWindow(taskbar))
            taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        return taskbar;
    }

    /// <summary>
    /// The foreground window, when it is a full-screen app owning the monitor the widget
    /// sits on. Shell surfaces never count - the desktop, Task View and Start are not
    /// full-screen apps, and the widget legitimately stays for them. Returns false for
    /// anything that cannot name its monitor or its root window: insufficient evidence,
    /// so nothing is hidden on a guess.
    /// </summary>
    private bool FullScreenCoversTaskbar(IntPtr taskbar)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !NativeMethods.IsWindow(foreground))
        {
            return false;
        }

        IntPtr root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
        {
            root = foreground;
        }

        if (ShellSurface.IsShellClassName(NativeMethods.GetClassNameOf(root)))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(root, out NativeRect rect))
        {
            return false;
        }

        var window = PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (window.IsEmpty)
        {
            return false;
        }

        if (!TaskbarLocator.TryGetMonitorBounds(root, out PixelRect monitor))
        {
            return false;
        }

        if (!IsFullScreenWindow(window, monitor))
        {
            return false;
        }

        // The coverage rule: hide only when the window would actually cover the taskbar
        // the widget lives on, so a full-screen window on another monitor leaves it up.
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out NativeRect barRect))
        {
            return false;
        }

        var bar = PixelRect.FromEdges(barRect.Left, barRect.Top, barRect.Right, barRect.Bottom);
        return FullScreenWindowCoversTaskbar(window, bar);
    }

    /// <summary>
    /// Moves the widget back to the active virtual desktop, but only once it is really
    /// stranded there: during a switch the shell keeps reporting the old desktop for a
    /// moment, and reacting to that reading moved the widget back and forth (a blink per
    /// keystroke). Three readings in a row at half a second apart cannot be a transition.
    ///
    /// Nothing is moved while the shell has the taskbar in a raised band (Task View from
    /// Win+Tab, Start), and nothing is moved on the word of a shell surface: the desktop
    /// to follow is the one the user's own window is on, never the Task View frame's.
    /// Following the frame put the widget onto the desktop being left, where it stayed
    /// even after the switch - alive, running, and on a desktop nobody was looking at.
    /// </summary>
    private void FollowDesktopIfStranded(bool shellBandRaised)
    {
        if (Elapsed.Since(_lastDesktopCheckTicks) < DesktopCheckInterval)
            return;

        _lastDesktopCheckTicks = Stopwatch.GetTimestamp();
        if (_desktopPresence.IsOnCurrentDesktop(HandleForPosting))
        {
            _desktopMisses = 0;
            _desktopFollowFailures = 0;
            return;
        }

        // Task View (Win+Tab) and Start are open: the desktop the user is about to land
        // on is decided when they pick it, so any move made now is a move to the wrong
        // desktop. Wait for the shell to hand the desktop back.
        if (shellBandRaised)
            return;

        _desktopMisses++;
        if (!ShouldFollowDesktop(shellBandRaised, _desktopMisses, DesktopMissesBeforeFollowing))
            return;

        // A rejected move is also the shell's transition. Waiting longer after each one
        // keeps a stubborn desktop from turning into a move every half second.
        if (_desktopFollowFailures > 0
            && Elapsed.Since(_lastDesktopFollowTicks)
                < RepairIntervalFor(_desktopFollowFailures))
            return;

        _desktopMisses = 0;
        _lastDesktopFollowTicks = Stopwatch.GetTimestamp();
        if (_desktopPresence.FollowCurrentDesktop(HandleForPosting, DesktopAuthority()))
        {
            _desktopFollowFailures = 0;
            return;
        }

        _desktopFollowFailures = Math.Min(_desktopFollowFailures + 1, MaxRepairAttempts);
    }

    /// <summary>
    /// The window whose desktop the widget should join: the user's foreground window,
    /// when that is a real app window. The taskbar and Task View are not candidates -
    /// they are shell surfaces and report a desktop the user is not on.
    /// Zero when nothing can name the active desktop this tick; moving then is a guess.
    /// </summary>
    private static IntPtr DesktopAuthority()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        return ShellSurface.CanNameActiveDesktop(foreground) ? foreground : IntPtr.Zero;
    }

    /// <summary>
    /// Runs on a thread-pool thread. Both the traffic totals and the system statistics
    /// are sampled here - the figures and the chips travel in the same sample - so the
    /// UI thread never waits on a PDH sweep or a network enumeration.
    /// </summary>
    private void SampleOnce()
    {
        if (_stopped)
        {
            return;
        }

        (long Received, long Sent) totals;
        try
        {
            totals = _sampler.ReadTotals();
        }
        catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException
                                       or PlatformNotSupportedException
                                       or InvalidOperationException)
        {
            return;
        }

        // The chips are sampled here too, on this thread, rather than on the UI thread:
        // the GPU sweep is a chain of PDH queries that can take hundreds of milliseconds
        // on a busy GPU, and the UI thread is not the place for that. The chips advance
        // at exactly the same rate as the figures because they travel inside the same
        // sample - every tick one reading feeds both.
        SystemStatsReading stats;
        try
        {
            lock (_statsGate)
            {
                stats = _stats.Sample(_liveConfig);
            }
        }
        catch (Exception)
        {
            // A statistics failure must never kill the sample message: the figures
            // still advance, the chips just show nothing.
            stats = SystemStatsReading.Empty;
        }

        long timestamp = Stopwatch.GetTimestamp();
        lock (_pendingGate)
        {
            _pendingSample = (totals.Received, totals.Sent, timestamp, stats);
        }

        IntPtr handle = HandleForPosting;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.PostMessage(handle, WmSampleReady, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void ConsumeSample()
    {
        (long Received, long Sent, long Timestamp, SystemStatsReading Stats) sample;
        lock (_pendingGate)
        {
            if (_pendingSample is null)
            {
                return;
            }

            sample = _pendingSample.Value;
            _pendingSample = null;
        }

        if (_lastTimestamp == 0)
        {
            _lastTimestamp = sample.Timestamp;
            return;
        }

        double elapsed = (sample.Timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed <= 0)
        {
            return;
        }

        _lastTimestamp = sample.Timestamp;
        _download.Update(sample.Received, elapsed);
        _upload.Update(sample.Sent, elapsed);

        double downloadRate = _download.SmoothedBytesPerSecond;
        double uploadRate = _upload.SmoothedBytesPerSecond;

        _scaler.SharedScale = _config.SharedScale;
        _scaler.Update(downloadRate, uploadRate, elapsed, _config.AutoScale, _config.ManualPeakBytesPerSecond);

        // Bars follow the scaled ratios, eased so a burst cannot slam them from full
        // width to a sliver and back on every tick. With SharedScale on, both ratios
        // come from one full-scale rate, so the rows stay comparable.
        SystemStatsReading stats = sample.Stats;

        _downloadBar.Update(_scaler.BarRatio(isDownload: true, downloadRate), elapsed);
        _uploadBar.Update(_scaler.BarRatio(isDownload: false, uploadRate), elapsed);

        _visual = new WidgetVisual(
            downloadRate,
            uploadRate,
            _downloadBar.Current,
            _uploadBar.Current,
            stats);

        CaptionChanged?.Invoke(BuildCaption(stats, downloadRate, uploadRate, _config));

        Render();
    }

    private void Render()
    {
        if (IsDisposed)
        {
            return;
        }

        // A full-screen window owns every pixel the widget would draw on. The taskbar can
        // stay alive and visible behind it - a browser in fullscreen does not hide it - so
        // the confinement gate below cannot see this case. The widget hides instead of
        // drawing a stale frame over whatever owns the screen, whatever the locate path says.
        if (FullScreenCoversTaskbar(ResolveTaskbarHandle()))
        {
            FrameTrace.Write("render: full-screen window covers the taskbar -> hide");
            HideIfShown();
            return;
        }

        if (!TaskbarLocator.TryLocate(out TaskbarSnapshot snapshot))
        {
            // The taskbar is being recreated, or the display is changing. Keep the last
            // frame on screen instead of hiding: a widget that blinks away while the
            // shell redraws is exactly what must not happen.
            FrameTrace.Write("render: locate failed -> keep last frame");
            return;
        }

        var orientation = snapshot.Orientation;
        int maxExtent = orientation == TaskbarOrientation.Horizontal
            ? snapshot.TaskbarRect.Height
            : snapshot.TaskbarRect.Width;

        WidgetLayout layout = _renderer.ComputeLayout(_config, snapshot.DpiScale, maxExtent, _visual.Stats);
        PixelRect? placement = layout.FitsBounds
            ? TaskbarGeometry.ComputePlacement(
                snapshot.TaskbarRect,
                snapshot.TrayRect,
                layout.Size,
                _config.OffsetFromTrayPx,
                nudgeX: _config.NudgeXPx,
                nudgeY: _config.NudgeYPx)
            : null;

        // The widget lives in the taskbar and nowhere else. An auto-hide bar parks it off
        // screen, and even a visible taskbar that cannot hold the widget honestly - too
        // small, or lying about where it is - leaves the widget nothing to sit on (the
        // "covers the screen outright" case is handled above). Keeping the last frame
        // would draw a stale snapshot over whatever owns the screen, so the widget hides
        // instead.
        if (!WidgetMayPresent(
                NativeMethods.IsWindowVisible(snapshot.TaskbarHandle),
                layout.FitsBounds,
                placement,
                snapshot.TaskbarRect,
                snapshot.MonitorBounds))
        {
            FrameTrace.Write($"render: not presenting (layout {layout.Size}, placement {placement?.ToString() ?? "none"}) -> hide");
            HideIfShown();
            return;
        }

        PixelRect rect = placement!.Value;
        WidgetTheme theme = WidgetTheme.FromRegistry();
        PresentationState state = PresentationState.For(
            _visual, layout, rect, _config, theme.IsDark, _configVersion, snapshot.DpiScale);

        if (!NeedsPresent(IsShown, _presented, state))
        {
            RecoverIfCovered(snapshot.TaskbarHandle);
            return;
        }

        FrameTrace.Write($"present rect={rect} size={layout.Size}");
        using Bitmap frame = _renderer.Render(_visual, _config, layout, theme, orientation);
        Present(frame, rect.Left, rect.Top);
        _lastPresentTicks = Stopwatch.GetTimestamp();
        _presented = state;
        _presentedFrames++;

        // At startup the taskbar raises its own window a moment after we appear, which
        // leaves us drawn-but-covered for the first tick or two. Winning that race
        // explicitly makes the widget appear at once instead of a second later.
        if (NeedsStartupTopMost(_presentedFrames))
        {
            AssertTopMost();
        }

        Volatile.Write(ref _taskbarHandle, snapshot.TaskbarHandle);
        RecoverIfCovered(snapshot.TaskbarHandle);
    }

    /// <summary>
    /// Keeps the widget where it belongs, using the same recipe the reference widget
    /// follows: own the taskbar, so the shell carries the widget along whenever it
    /// raises the taskbar (Start, Task View, alt-tab), and put the widget back directly
    /// above the taskbar whenever it has provably slipped below it.
    /// The raised band does not defer that seat: during Task View the shell lifts the
    /// taskbar above the widget inside the raised band, and only a seat puts it back.
    /// Nothing is touched while a menu or flyout merely overlaps the widget, or while
    /// the widget is healthy: each of those "repairs" is a z-order change, and a
    /// z-order change is a visible blink.
    /// </summary>
    private void RecoverIfCovered(IntPtr taskbar)
    {
        if (!IsShown || IsDisposed)
        {
            return;
        }

        // Owning the taskbar is what makes the widget follow it into a raised band and
        // onto every virtual desktop. Setting an owner moves nothing, so this is safe
        // to do on any tick; it only ever runs once per taskbar.
        if (taskbar != IntPtr.Zero && DockTo(taskbar))
        {
            _repairAttempts = 0;
        }

        long now = Stopwatch.GetTimestamp();
        uint taskbarBand = BandOf(taskbar);
        uint widgetBand = BandOf(HandleForPosting);
        if (DefersRepair(taskbarBand, widgetBand))
        {
            if (_bandRaisedTicks == 0)
            {
                _bandRaisedTicks = now;
                FrameTrace.Write($"presence: shell raised the taskbar band ({taskbarBand}) -> keeping the seat");
            }
        }
        else if (_bandRaisedTicks != 0)
        {
            _bandRaisedTicks = 0;
            _bandDroppedTicks = now;
        }

        bool buried = taskbar != IntPtr.Zero && IsCoveredBy(taskbar);
        if (buried)
        {
            FrameTrace.Write($"presence: {CoveringWindowClassName()} is drawn over a painted pixel -> seating");
        }

        bool peekActive = !buried && IsBehindDesktopPeek(taskbar);
        if (!NeedsRecovery(isShown: true, isOnTop: !buried && !peekActive, coveredByTaskbar: buried, peekActive: peekActive))
        {
            if (Elapsed.Since(_lastRepairTicks) >= HealthyHold)
            {
                _repairAttempts = 0;
            }

            RepairSurface(now);
            return;
        }

        // A re-seat right after the shell dropped its band back to the desktop is the
        // shell's own doing, not a losing fight, so it must not be delayed by a backoff
        // the previous fight left behind.
        bool afterBandReturn = _bandDroppedTicks != 0
            && Elapsed.Since(_bandDroppedTicks) < BandReturnGrace;
        if (afterBandReturn)
        {
            _repairAttempts = 0;
        }

        if (!ShouldRecoverNow(Elapsed.Since(_lastRepairTicks), RepairIntervalFor(_repairAttempts)))
        {
            RepairSurface(now);
            return;
        }

        _lastRepairTicks = now;
        _repairAttempts = Math.Min(_repairAttempts + 1, MaxRepairAttempts);

        if (ShouldSeat(buried, IsTopMostWindow(taskbar)))
        {
            SeatAboveTaskbar(taskbar);
        }
        else if (buried)
        {
            // A full-screen app takes the taskbar out of the topmost band. Following it
            // there would draw the widget over that app, so the order is left alone.
            FrameTrace.Write("presence: taskbar is not topmost -> leaving the order alone");
        }
        else
        {
            AssertTopMost();
        }

        RepairSurface(now);
    }

    /// <summary>
    /// Whether the widget must be put back directly above the taskbar right now.
    /// Being buried is the whole test: the shell's raised bands are deliberately absent
    /// from this rule, because deferring on them is what made the widget vanish.
    /// Measured with Win+Tab pressed for real: Start and Task View raise the taskbar and
    /// the owned widget into the same higher band, and inside that band the shell then
    /// lifts the taskbar above the widget (both at band 6, and WindowFromPoint over the
    /// widget answers "Shell_TrayWnd"). One SetWindowPos puts the widget back on top and
    /// it stays there for as long as Task View is open, so the seat has to happen while
    /// the band is raised, not after it drops.
    /// </summary>
    internal static bool ShouldSeat(bool buried, bool taskbarIsTopMost)
        => buried && taskbarIsTopMost;

    /// <summary>Re-shows a dropped frame and re-blits it now and then; never reorders.</summary>
    private void RepairSurface(long now)
    {
        if (RestorePresence())
        {
            _lastPresentTicks = now;
        }
        else if (ShouldRepresentNow(Elapsed.Since(_lastPresentTicks), RepresentInterval))
        {
            _lastPresentTicks = now;
            Represent();
        }
    }

    private static uint BandOf(IntPtr window)
        => NativeMethods.TryGetWindowBand(window, out uint band) ? band : 0u;

    /// <summary>
    /// Whether the shell currently owns a raised Z band (Start, Task View, the alt-tab
    /// switcher). This gates the <em>virtual desktop</em> decision only: while the band
    /// is raised neither the desktop being left nor the one being chosen can be read, so
    /// a move then is a move to the wrong desktop. It must not gate the z-order seat -
    /// measured on a live Win+Tab, the shell lifts the taskbar above the widget inside
    /// the raised band, and the seat is what puts it back.
    /// </summary>
    internal static bool DefersRepair(uint taskbarBand, uint widgetBand)
        => taskbarBand > NativeMethods.ZBID_DESKTOP || widgetBand > NativeMethods.ZBID_DESKTOP;

    /// <summary>
    /// How long to wait before the next z-order repair. The first attempt is immediate;
    /// after that each attempt doubles up to the cap, so a shell that keeps winning the
    /// order costs a handful of repairs a minute instead of sixteen a second.
    /// </summary>
    internal static TimeSpan RepairIntervalFor(int attempts)
    {
        if (attempts <= 0)
        {
            return TimeSpan.Zero;
        }

        double seconds = MinRepairIntervalSeconds * Math.Pow(2, Math.Min(attempts - 1, 8));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRepairIntervalSeconds));
    }

    /// <summary>
    /// Whether the widget has really been left on another virtual desktop. A single
    /// reading is not enough: mid-switch the shell still answers with the old desktop.
    /// </summary>
    internal static bool IsStrandedOnAnotherDesktop(int consecutiveMisses, int requiredMisses)
        => requiredMisses > 0 && consecutiveMisses >= requiredMisses;

    /// <summary>
    /// Whether the widget may be moved to the active virtual desktop on this reading.
    /// Never while the shell has the taskbar in a raised band: that is Task View
    /// (Win+Tab) or Start, and during it neither the desktop the user is leaving nor the
    /// one they will pick can be read, so every move is a move to the wrong desktop -
    /// the widget then stays there, running and invisible, after the switch. Otherwise a
    /// move needs the same repeated readings a plain switch needs.
    /// </summary>
    internal static bool ShouldFollowDesktop(
        bool shellBandRaised, int consecutiveMisses, int requiredMisses)
        => !shellBandRaised && IsStrandedOnAnotherDesktop(consecutiveMisses, requiredMisses);

    /// <summary>Whether the window still carries WS_EX_TOPMOST.</summary>
    internal static bool IsTopMostWindow(IntPtr window)
        => window != IntPtr.Zero
           && (NativeMethods.GetWindowLong(window, -20) & NativeMethods.WS_EX_TOPMOST) != 0;
    /// <summary>
    /// The background re-blit has to happen even while nothing appears to have changed,
    /// or a surface dropped by the shell is never restored.
    /// </summary>
    internal static bool ShouldRepresentNow(TimeSpan sincePresent, TimeSpan interval)
        => sincePresent >= interval;


    /// <summary>
    /// Decides the cheapest repair that keeps the widget visible. Two independent
    /// faults are covered: the surface being dropped (fixed by re-presenting, which
    /// never changes z-order and so cannot cover a menu) and the taskbar or desktop
    /// being moved above us (fixed by restoring z-order).
    /// </summary>

    /// <summary>
    /// Tolerance for the edge of the monitor a full-screen window must cover before it
    /// counts as full-screen. Wide enough for 1-2px borders and slightly off-by-one
    /// monitor readings, narrow enough that a window stopping at the work area - a
    /// maximized window, which is where the widget legitimately stays - never qualifies.
    /// </summary>
    private const int FullScreenTolerancePx = 4;

    /// <summary>
    /// The full-screen rule: a window that covers its own monitor outright owns the whole
    /// screen the widget lives on. A normal maximized window never matches - it stops at
    /// the work area under the taskbar, which is exactly where the widget belongs.
    /// </summary>
    internal static bool IsFullScreenWindow(PixelRect window, PixelRect monitor)
        => !window.IsEmpty
            && !monitor.IsEmpty
            && window.Left <= monitor.Left + FullScreenTolerancePx
            && window.Top <= monitor.Top + FullScreenTolerancePx
            && window.Right >= monitor.Right - FullScreenTolerancePx
            && window.Bottom >= monitor.Bottom - FullScreenTolerancePx;

    /// <summary>
    /// The coverage rule: a full-screen window hides the widget only when it covers the
    /// taskbar the widget lives on. A full-screen window over another monitor must not
    /// hide the widget off the screen the taskbar is on.
    /// </summary>
    internal static bool FullScreenWindowCoversTaskbar(PixelRect window, PixelRect taskbar)
        => !window.IsEmpty && !taskbar.IsEmpty && !window.Intersect(taskbar).IsEmpty;

    /// <summary>
    /// Whether the widget may be shown at <paramref name="placement"/> against the taskbar
    /// that was located, given whether the layout fit it and which monitor is in force.
    /// This is the whole "the widget lives in the taskbar" rule: only a placement fully
    /// inside a visible, on-screen taskbar is ever presented. Everything else means the
    /// widget hides instead of drawing over the desktop.
    /// </summary>
    internal static bool WidgetMayPresent(
        bool taskbarVisible,
        bool layoutFits,
        PixelRect? placement,
        PixelRect taskbar,
        PixelRect monitorBounds)
        => taskbarVisible
            && layoutFits
            && placement is { } rect
            && IsConfinedToTaskbar(rect, taskbar)
            && (monitorBounds.IsEmpty || !rect.Intersect(monitorBounds).IsEmpty);

    /// <summary>
    /// The confinement rule: the presentation rect must sit entirely within the taskbar.
    /// Everything the widget draws stays inside the bar's bounds; anything that reaches
    /// outside is not drawn.
    /// </summary>
    internal static bool IsConfinedToTaskbar(PixelRect placement, PixelRect taskbar)
        => placement.Width > 0
            && placement.Height > 0
            && placement.Left >= taskbar.Left
            && placement.Right <= taskbar.Right
            && placement.Top >= taskbar.Top
            && placement.Bottom <= taskbar.Bottom;

    /// <summary>
    /// The anti-flicker rule: push a frame only when something visible changed.
    /// A steady network must not re-blit the window every tick.
    /// </summary>
    internal static bool NeedsPresent(bool isShown, PresentationState? presented, PresentationState next)
        => !isShown || presented != next;

    protected override void OnHandleDestroyed()
    {
        _stopped = true;
        if (_shellEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_shellEventHook);
            _shellEventHook = IntPtr.Zero;
        }
        _desktopPresence.Dispose();
        lock (_timerGate)
        {
            _timer?.Dispose();
            _timer = null;
            _presenceTimer?.Dispose();
            _presenceTimer = null;
        }
    }

    /// <summary>
    /// What is currently on screen. <see cref="Visual"/> and the rest are the values a
    /// frame was built from; the <c>Drawn*</c> members are what the renderer actually
    /// paints, and they are what stops a wobbling rate that rounds to the same text and
    /// the same bar width from causing a blit at all.
    /// </summary>
    internal readonly record struct PresentationState(
        WidgetVisual Visual,
        PixelRect Rect,
        bool IsDark,
        int ConfigVersion,
        float DpiScale)
    {
        /// <summary>The download row's text exactly as drawn.</summary>
        public string DrawnDownloadText { get; init; } = string.Empty;

        /// <summary>The upload row's text exactly as drawn.</summary>
        public string DrawnUploadText { get; init; } = string.Empty;

        /// <summary>Download bar fill in whole pixels, exactly as drawn.</summary>
        public int DrawnDownloadFillPx { get; init; }

        /// <summary>Upload bar fill in whole pixels, exactly as drawn.</summary>
        public int DrawnUploadFillPx { get; init; }

        /// <summary>The statistics line exactly as it would be drawn.</summary>
        public string DrawnStatsText { get; init; } = string.Empty;

        /// <summary>Signature of everything the renderer will actually paint.</summary>
        public static PresentationState For(
            WidgetVisual visual,
            WidgetLayout layout,
            PixelRect rect,
            MonitorConfig config,
            bool isDark,
            int configVersion,
            float dpiScale)
            => new(WidgetVisual.Empty, rect, isDark, configVersion, dpiScale)
            {
                DrawnDownloadText = SpeedFormatter.FormatRow(true, visual.DownloadBytesPerSecond),
                DrawnUploadText = SpeedFormatter.FormatRow(false, visual.UploadBytesPerSecond),
                DrawnDownloadFillPx = WidgetRenderer.FillWidth(layout.DownloadBar.Width, visual.DownloadRatio),
                DrawnUploadFillPx = WidgetRenderer.FillWidth(layout.UploadBar.Width, visual.UploadRatio),
                DrawnStatsText = SystemStats.JoinedText(visual.Stats, config),
            };
    }
}
