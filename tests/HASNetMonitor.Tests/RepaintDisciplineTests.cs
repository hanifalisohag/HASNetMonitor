using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The repaint discipline: what actually goes on screen decides when the widget
/// blits, and the widget is only ever repaired when the taskbar or the desktop itself
/// is above it.
///
/// The rules here exist because of real defects: re-blitting identical pixels, hiding
/// the widget whenever a measurement failed for a moment, and repairing it whenever a
/// pixel hit-test answered "taskbar" (which happens for any transparent pixel) all made
/// the taskbar item blink or vanish. The widget must be simply always there.
/// </summary>
public class RepaintDisciplineTests
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(1.5);

    private static TimeSpan Never => TimeSpan.Zero;
    private static TimeSpan Long => TimeSpan.FromMinutes(1);

    private static MonitorConfig TextConfig() => new MonitorConfig
    {
        DownloadColorHex = "#00FF00",
        UploadColorHex = "#FF0000",
        ShowText = true,
        ShowBackdrop = false,
        BarThicknessPx = 4,
    }.Normalize();

    [Fact]
    public void TheFirstFramesWinTheRaceWithTheTaskbar()
    {
        // Explorer raises the taskbar a moment after the widget appears; without this
        // the widget is drawn but covered until the next repair tick.
        Assert.True(TaskbarWidget.NeedsStartupTopMost(1));
        Assert.True(TaskbarWidget.NeedsStartupTopMost(3));
        Assert.False(TaskbarWidget.NeedsStartupTopMost(0));
        Assert.False(TaskbarWidget.NeedsStartupTopMost(4));
    }

    [Fact]
    public void AHealthyWidgetAboveEverythingIsNeverRepaired()
    {
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: true, coveredByTaskbar: false, peekActive: false));
    }

    [Fact]
    public void TheTaskbarAboveTheWidgetIsRepaired()
    {
        // Task view puts Shell_TrayWnd above us without hiding us or telling us.
        Assert.True(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: false, coveredByTaskbar: true, peekActive: false));
    }

    [Fact]
    public void TheShowDesktopPeekAboveTheWidgetIsRepaired()
    {
        Assert.True(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: false, coveredByTaskbar: false, peekActive: true));
    }

    [Fact]
    public void AMenuAboveUsIsNeverFoughtOver()
    {
        // The Start menu or a context menu covers us, but the taskbar and the desktop
        // did not move, so we must not raise ourselves over it.
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: true, isOnTop: false, coveredByTaskbar: false, peekActive: false));
    }

    [Fact]
    public void AHiddenWidgetIsNeverRepaired()
    {
        Assert.False(TaskbarWidget.NeedsRecovery(
            isShown: false, isOnTop: false, coveredByTaskbar: true, peekActive: true));
    }

    [Fact]
    public void TheRepairIsRateLimitedSoItCannotBecomeFlicker()
    {
        Assert.False(TaskbarWidget.ShouldRecoverNow(Never, RecoveryInterval));
        Assert.False(TaskbarWidget.ShouldRecoverNow(
            RecoveryInterval - TimeSpan.FromMilliseconds(1), RecoveryInterval));
        Assert.True(TaskbarWidget.ShouldRecoverNow(RecoveryInterval, RecoveryInterval));
        Assert.True(TaskbarWidget.ShouldRecoverNow(Long, RecoveryInterval));
    }

    [Fact]
    public void TwoHundredCoveredTicksRecoverAtMostOncePerInterval()
    {
        int recoveries = 0;
        var sinceRecovery = TimeSpan.Zero;
        var tick = TimeSpan.FromSeconds(1);

        for (int i = 0; i < 200; i++)
        {
            bool covered = true;
            if (TaskbarWidget.NeedsRecovery(true, isOnTop: !covered, covered, peekActive: false)
                && TaskbarWidget.ShouldRecoverNow(sinceRecovery, RecoveryInterval))
            {
                recoveries++;
                sinceRecovery = TimeSpan.Zero;
            }

            sinceRecovery += tick;
        }

        // 200 ticks of one second with the taskbar permanently above us: one recovery
        // every 1.5 s at most, and never a recovery on consecutive ticks.
        Assert.InRange(recoveries, 90, 134);
    }

    [Fact]
    public void ARateWobbleThatPaintsTheSamePixelsIsNotPushed()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = TextConfig();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
        var rect = new PixelRect(1614, 1034, layout.Size.Width, layout.Size.Height);

        TaskbarWidget.PresentationState first = TaskbarWidget.PresentationState.For(
            new WidgetVisual(10_000_000, 40_000, 0.5000, 0.2500), layout, rect, true, 1, 1f);
        TaskbarWidget.PresentationState wobbled = TaskbarWidget.PresentationState.For(
            new WidgetVisual(10_000_100, 40_010, 0.5001, 0.2501), layout, rect, true, 1, 1f);

        Assert.Equal(first, wobbled);
        Assert.False(TaskbarWidget.NeedsPresent(isShown: true, presented: first, wobbled));

        TaskbarWidget.PresentationState grown = TaskbarWidget.PresentationState.For(
            new WidgetVisual(9_900_000, 40_000, 0.95, 0.2500), layout, rect, true, 1, 1f);

        Assert.NotEqual(first, grown);
        Assert.True(TaskbarWidget.NeedsPresent(isShown: true, presented: first, grown));
    }

    [Fact]
    public void FortyTicksOfWobblingTrafficProduceAtMostOneFrame()
    {
        var renderer = new WidgetRenderer();
        MonitorConfig config = TextConfig();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
        var rect = new PixelRect(1614, 1034, layout.Size.Width, layout.Size.Height);

        TaskbarWidget.PresentationState? presented = null;
        int pushes = 0;
        var random = new Random(1234);

        for (int tick = 0; tick < 40; tick++)
        {
            double jitter = 1.0 + (random.NextDouble() - 0.5) * 0.0002;
            TaskbarWidget.PresentationState next = TaskbarWidget.PresentationState.For(
                new WidgetVisual(10_000_000 * jitter, 5_000_000 * jitter, 0.5, 0.25),
                layout,
                rect,
                true,
                1,
                1f);

            if (TaskbarWidget.NeedsPresent(isShown: true, presented: presented, next))
            {
                pushes++;
            }

            presented = next;
        }

        // Sub-0.02% jitter cannot move a rounded speed or a bar by a pixel, so the
        // only blit allowed is the first frame.
        Assert.Equal(1, pushes);
    }

    [Fact]
    public void TheFramePathNeverHidesTheWidget()
    {
        // Hiding it whenever the taskbar could not be located for one tick - which
        // happens during explorer restarts and display or DPI changes - is what made
        // the widget blink away and come back. It must keep its last frame instead.
        string render = SourceText.ExtractMethod("UI", "TaskbarWidget.cs", "private void Render()");

        Assert.DoesNotContain("HideIfShown", render, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowWindow", render, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRepairPathNeverHitTestsPixels()
    {
        // A hit-test answers "the taskbar" for any transparent pixel inside the widget,
        // which used to trigger an endless re-assert of "always on top" and a blink.
        string repair = SourceText.ExtractMethod("UI", "TaskbarWidget.cs", "private void RecoverIfCovered(");

        Assert.DoesNotContain("WindowAt", repair, StringComparison.Ordinal);
        Assert.DoesNotContain("SurfaceCentre", repair, StringComparison.Ordinal);
    }
}
