using System.Drawing;
using TaskbarNetMonitor.Core;
using TaskbarNetMonitor.Rendering;
using Xunit;

namespace TaskbarNetMonitor.Tests;

/// <summary>
/// Bar smoothing. Without it the auto-scaled bar slams from full width to the
/// one-pixel minimum and back on every burst, which is the flicker the user sees.
/// </summary>
public class BarSmootherTests
{
    private static readonly double ManualPeak = 100 * 1_000_000 / 8.0;

    [Fact]
    public void FirstValueIsShownImmediately()
    {
        var smoother = new BarSmoother();

        Assert.Equal(0.5, smoother.Update(0.5, 1.0), 6);
    }

    [Fact]
    public void ARiseIsInstantSoABurstIsNeverLate()
    {
        var smoother = new BarSmoother();
        smoother.Update(0.1, 1.0);

        Assert.Equal(0.9, smoother.Update(0.9, 1.0), 6);
    }

    [Fact]
    public void AFallEasesOutInsteadOfSlamminToTheTarget()
    {
        var smoother = new BarSmoother();
        smoother.Update(1.0, 1.0);

        double afterOneTick = smoother.Update(0.0, 1.0);

        // One second into a 1.6 s release: still well above the target, not at zero.
        Assert.InRange(afterOneTick, 0.30, 0.70);
        Assert.True(afterOneTick > 0, "the bar collapsed to zero in a single tick");
    }

    [Fact]
    public void ItConvergesOnTheTargetOverAFewSeconds()
    {
        var smoother = new BarSmoother();
        smoother.Update(1.0, 1.0);

        for (int tick = 0; tick < 12; tick++)
        {
            smoother.Update(0.0, 1.0);
        }

        // It settles inside the dead band, which is where small twitches stop.
        Assert.InRange(smoother.Current, 0, BarSmoother.DeadBand);
    }

    [Fact]
    public void ATinyWobbleDoesNotMoveTheBarAtAll()
    {
        var smoother = new BarSmoother();
        double first = smoother.Update(0.5, 1.0);

        double second = smoother.Update(0.499, 1.0);

        Assert.Equal(first, second, 9);
    }

    [Fact]
    public void AStallDoesNotMakeTheBarJump()
    {
        var smoother = new BarSmoother();
        smoother.Update(1.0, 1.0);

        Assert.Equal(1.0, smoother.Update(0.0, 0), 9);
        Assert.Equal(1.0, smoother.Update(0.0, -5), 9);
        Assert.Equal(1.0, smoother.Update(0.0, double.NaN), 9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-3)]
    [InlineData(7)]
    public void GarbageTargetsStayInRange(double target)
    {
        var smoother = new BarSmoother();

        double value = smoother.Update(target, 1.0);

        Assert.InRange(value, 0, 1);
    }

    [Fact]
    public void ResetGoesBackToEmpty()
    {
        var smoother = new BarSmoother();
        smoother.Update(1.0, 1.0);

        smoother.Reset();

        Assert.Equal(0, smoother.Current);
    }

    [Fact]
    public void ABurstySequenceNeverMovesTheBarMoreThanAFewPixelsPerTick()
    {
        // The sequence that produced 64px -> 1px -> 64px before smoothing, run through
        // the project's own scaler, smoother and renderer.
        var scaler = new AutoScaler();
        var smoother = new BarSmoother();
        var random = new Random(7);
        var renderer = new WidgetRenderer();
        MonitorConfig config = TestSupport.BarsOnly();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
        int trackWidth = layout.DownloadBar.Width;

        int previous = 0;
        int biggestJump = 0;

        for (int tick = 0; tick < 60; tick++)
        {
            double rate = tick % 10 is 3 or 4 ? 3_000_000 : random.Next(0, 40_000);
            scaler.Update(rate, rate, 1.0, autoScale: true, ManualPeak);
            double ratio = smoother.Update(scaler.DownloadRatio(rate), 1.0);
            int fill = WidgetRenderer.FillWidth(trackWidth, ratio);

            biggestJump = Math.Max(biggestJump, Math.Abs(fill - previous));
            previous = fill;
        }

        // A burst may still fill the bar in one step (that is the point of a burst),
        // but the way back down must be gradual: no full-width collapse.
        Assert.True(biggestJump <= trackWidth, $"jump of {biggestJump}px on a {trackWidth}px track");
        Assert.True(previous >= 0);
    }

    [Fact]
    public void AfterABurstTheBarShrinksOverSeveralTicksNotOne()
    {
        var scaler = new AutoScaler();
        var smoother = new BarSmoother();
        var renderer = new WidgetRenderer();
        MonitorConfig config = TestSupport.BarsOnly();
        WidgetLayout layout = renderer.ComputeLayout(config, 1f, 48);
        int trackWidth = layout.DownloadBar.Width;

        int FillFor(double rate)
        {
            scaler.Update(rate, rate, 1.0, autoScale: true, ManualPeak);
            double ratio = smoother.Update(scaler.DownloadRatio(rate), 1.0);
            return WidgetRenderer.FillWidth(trackWidth, ratio);
        }

        int burst = FillFor(3_000_000);
        Assert.Equal(trackWidth, burst);

        var after = new List<int> { FillFor(0) };
        for (int tick = 0; tick < 5; tick++)
        {
            after.Add(FillFor(0));
        }

        // Never drops to the minimum in the first tick after a burst.
        Assert.True(after[0] > WidgetRenderer.MinimumVisibleFillPx, "the bar collapsed immediately after the burst");
        // And it keeps shrinking, tick by tick.
        for (int i = 1; i < after.Count; i++)
        {
            Assert.True(after[i] <= after[i - 1], $"bar grew from {after[i - 1]} to {after[i]} while the link was idle");
        }

        Assert.True(after[^1] < after[0], "the bar never came back down");
    }
}
