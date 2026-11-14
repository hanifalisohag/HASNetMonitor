using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using HASNetMonitor.UI;

namespace HASNetMonitor.Tests;

/// <summary>
/// The flicker rule: only the frames that actually look different may be pushed.
/// A widget that re-blits identical pixels is what makes a taskbar item flicker.
/// </summary>
public class PresentationGateTests
{
    private static TaskbarWidget.PresentationState State(
        double download = 1250,
        double upload = 500,
        int left = 1614,
        bool dark = true,
        int version = 1,
        float dpi = 1f)
        => new(
            new WidgetVisual(download, upload, 0.5, 0.25),
            new PixelRect(left, 1034, 72, 44),
            dark,
            version,
            dpi);

    [Fact]
    public void AFirstFrameIsAlwaysPushed()
    {
        TaskbarWidget.PresentationState state = State();

        Assert.True(TaskbarWidget.NeedsPresent(isShown: false, presented: null, state));
        Assert.True(TaskbarWidget.NeedsPresent(isShown: false, presented: state, state));
    }

    [Fact]
    public void AnIdenticalFrameIsNotPushedAgain()
    {
        TaskbarWidget.PresentationState state = State();
        TaskbarWidget.PresentationState same = State();

        Assert.False(TaskbarWidget.NeedsPresent(isShown: true, presented: state, same));
    }

    [Fact]
    public void NewTrafficIsPushed()
    {
        Assert.True(TaskbarWidget.NeedsPresent(true, State(download: 1250), State(download: 9999)));
        Assert.True(TaskbarWidget.NeedsPresent(true, State(upload: 500), State(upload: 9999)));
    }

    [Fact]
    public void AMovedWidgetOrANewTaskbarLayoutIsPushed()
    {
        Assert.True(TaskbarWidget.NeedsPresent(true, State(left: 1614), State(left: 1500)));
    }

    [Fact]
    public void AThemeOrDpiOrSettingsChangeIsPushed()
    {
        TaskbarWidget.PresentationState baseline = State();

        Assert.True(TaskbarWidget.NeedsPresent(true, baseline, State(dark: false)));
        Assert.True(TaskbarWidget.NeedsPresent(true, baseline, State(dpi: 1.5f)));
        Assert.True(TaskbarWidget.NeedsPresent(true, baseline, State(version: 2)));
    }

    [Fact]
    public void ASteadyNetworkNeverPushesTwelveFramesInTwelveTicks()
    {
        TaskbarWidget.PresentationState? presented = null;
        int pushes = 0;

        for (int tick = 0; tick < 12; tick++)
        {
            TaskbarWidget.PresentationState next = State();
            if (TaskbarWidget.NeedsPresent(isShown: pushes > 0, presented, next))
            {
                pushes++;
                presented = next;
            }
        }

        Assert.Equal(1, pushes);
    }

    [Fact]
    public void RatioOnlyChangesStillCountAsNewFrames()
    {
        var presented = new TaskbarWidget.PresentationState(
            new WidgetVisual(1250, 500, 0.5, 0.25),
            new PixelRect(1614, 1034, 72, 44),
            true,
            1,
            1f);
        var next = new TaskbarWidget.PresentationState(
            new WidgetVisual(1250, 500, 0.75, 0.25),
            new PixelRect(1614, 1034, 72, 44),
            true,
            1,
            1f);

        Assert.True(TaskbarWidget.NeedsPresent(isShown: true, presented, next));
    }
}
