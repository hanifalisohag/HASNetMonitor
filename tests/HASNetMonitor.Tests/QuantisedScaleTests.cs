using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The drawn scale is a round step - 16 KB/s, 32 KB/s, 64 KB/s ... 512 KB/s, 1 MB/s,
/// 2 MB/s - chosen from the faster of the two directions. 15 KB/s fills against 16 KB/s,
/// 345 KB/s against 512 KB/s, 564 KB/s against 1 MB/s, so a bar length always means a
/// rate that can be named.
/// </summary>
public class QuantisedScaleTests
{
    private const double Kb = 1024;
    private const double Mb = 1024 * 1024;

    private static AutoScaler Settled(double download, double upload, int ticks = 10, double seconds = 1.0)
    {
        var scaler = new AutoScaler { SharedScale = true };
        for (int i = 0; i < ticks; i++)
        {
            scaler.Update(download, upload, seconds, autoScale: true, manualPeakBytesPerSecond: 0);
        }

        return scaler;
    }

    /// <summary>
    /// The rules that must hold for every scaler this file produces: the scale is on the
    /// round ramp, both rows read against it, and no bar can overflow its track.
    /// </summary>
    private static void CheckInvariants(AutoScaler scaler, double download, double upload)
    {
        double scale = scaler.ScaleBytesPerSecond;

        Assert.True(scale >= AutoScaler.MinimumPeakBytesPerSecond, $"scale {scale} is below the floor");
        Assert.True(
            IsOnRamp(scale),
            $"scale {scale} is not a round step above {AutoScaler.MinimumPeakBytesPerSecond}");

        if (scaler.AutoScale)
        {
            // The step must cover the faster direction, or a bar would overflow.
            Assert.True(
                scale >= Math.Max(download, upload) || scale == AutoScaler.MaximumScaleStep,
                $"scale {scale} does not cover the faster rate {Math.Max(download, upload)}");

            // ... and it must be the *next* one: half of it is too small.
            Assert.True(
                scale == AutoScaler.MinimumPeakBytesPerSecond || scale / 2 < Math.Max(download, upload),
                $"scale {scale} is not the nearest step above {Math.Max(download, upload)}");
        }

        foreach (double rate in new[] { download, upload })
        {
            double ratio = scaler.BarRatio(true, rate);
            Assert.InRange(ratio, 0, 1);

            // One scale for both rows: the row does not matter.
            Assert.Equal(ratio, scaler.BarRatio(false, rate), 12);
            Assert.Equal(ratio, scaler.SharedRatio(rate), 12);
        }
    }

    /// <summary>Whether a rate is one of the round steps the scale may take.</summary>
    private static bool IsOnRamp(double scale) => AutoScaler.NextScaleStep(scale) == scale;

    [Theory]
    [InlineData(0, 16 * 1024)]
    [InlineData(1, 16 * 1024)]
    [InlineData(15 * 1024, 16 * 1024)]
    [InlineData(16 * 1024, 16 * 1024)]
    [InlineData(16 * 1024 + 1, 32 * 1024)]
    [InlineData(345 * 1024, 512 * 1024)]
    [InlineData(512 * 1024, 512 * 1024)]
    [InlineData(564 * 1024, 1024 * 1024)]
    [InlineData(1024 * 1024, 1024 * 1024)]
    [InlineData(1024 * 1024 + 1, 2 * 1024 * 1024)]
    [InlineData(3 * 1024 * 1024, 4 * 1024 * 1024)]
    [InlineData(4 * 1024 * 1024, 4 * 1024 * 1024)]
    [InlineData(10 * 1024 * 1024, 10 * 1024 * 1024)]
    [InlineData(11 * 1024 * 1024, 16 * 1024 * 1024)]
    [InlineData(20 * 1024 * 1024, 20 * 1024 * 1024)]
    [InlineData(64 * 1024 * 1024, 64 * 1024 * 1024)]
    public void TheScaleSnapsToTheNextRoundStepAboveTheRate(double rate, double expected)
    {
        Assert.Equal(expected, AutoScaler.NextScaleStep(rate), 6);
    }

    [Fact]
    public void TheStepsClimbInRoundOrderAndNeverOvershootTwice()
    {
        double previous = AutoScaler.MinimumPeakBytesPerSecond;

        for (double rate = 0; rate < 2000 * Mb; rate = rate * 1.5 + 1)
        {
            double step = AutoScaler.NextScaleStep(rate);

            Assert.True(step >= rate || step == AutoScaler.MaximumScaleStep);
            Assert.True(IsOnRamp(step), $"{step} is not a round step");
            Assert.True(step >= previous, $"{step} went below the previous step {previous}");

            // Consecutive steps at most double, so rounding alone never squashes a bar
            // below half the track.
            Assert.True(step <= previous * 2 + 1, $"{previous} -> {step} is too big a jump");

            // Nearest: the step below this one would not have covered the rate.
            if (step > AutoScaler.MinimumPeakBytesPerSecond)
            {
                double below = AutoScaler.NextScaleStep(step / 2);
                Assert.True(below < step, $"{below} is not below {step}");
                Assert.True(below < rate, $"{step} is not the nearest step above {rate}");
            }

            previous = step;
        }
    }

    [Theory]
    [InlineData(9 * 1024 * 1024, 10 * 1024 * 1024)]
    [InlineData(19 * 1024 * 1024, 20 * 1024 * 1024)]
    [InlineData(39 * 1024 * 1024, 40 * 1024 * 1024)]
    [InlineData(79 * 1024 * 1024, 80 * 1024 * 1024)]
    public void TheRoundMegabyteMarksKeepABusyLinkOutOfTheBottomThird(double rate, double expected)
    {
        // Without these marks 9 MB/s would fill against 16 MB/s and the bar would sit at
        // 56% while the link was flat out. The mark gives it a 10 MB/s scale instead.
        Assert.Equal(expected, AutoScaler.NextScaleStep(rate), 0);

        // Whatever the row, the mark keeps the bar out of the bottom third of its scale.
        Assert.True(rate / expected > 0.55, $"{rate} fills only {rate / expected:F2} of {expected}");
    }

    [Fact]
    public void TheFasterDirectionPicksTheStepAndBothRowsUseIt()
    {
        // 10 KB/s up beside 15 KB/s down: the scale is 16 KB/s and each row sits where
        // its own rate puts it inside that scale.
        AutoScaler scaler = Settled(15 * Kb, 10 * Kb);

        Assert.Equal(16 * Kb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(15.0 / 16.0, scaler.BarRatio(true, 15 * Kb), 6);
        Assert.Equal(10.0 / 16.0, scaler.BarRatio(false, 10 * Kb), 6);
        Assert.Equal(56, WidgetRenderer.FillWidth(60, scaler.BarRatio(true, 15 * Kb)));
        Assert.Equal(38, WidgetRenderer.FillWidth(60, scaler.BarRatio(false, 10 * Kb)));
        CheckInvariants(scaler, 15 * Kb, 10 * Kb);
    }

    [Fact]
    public void TheReportedPairOfHundredsOfKilobytesSharesOneMegabyte()
    {
        AutoScaler scaler = Settled(345 * Kb, 564 * Kb);

        Assert.Equal(1024 * Kb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(345.0 / 1024.0, scaler.BarRatio(true, 345 * Kb), 6);
        Assert.Equal(564.0 / 1024.0, scaler.BarRatio(false, 564 * Kb), 6);
        Assert.True(
            scaler.BarRatio(false, 564 * Kb) > scaler.BarRatio(true, 345 * Kb),
            "the faster upload must draw the longer bar");
        CheckInvariants(scaler, 345 * Kb, 564 * Kb);
    }

    [Theory]
    [InlineData(16 * 1024)]
    [InlineData(32 * 1024)]
    [InlineData(1 * 1024 * 1024)]
    [InlineData(4 * 1024 * 1024)]
    [InlineData(512 * 1024 * 1024)]
    public void ARateSittingExactlyOnAStepFillsTheBar(double step)
    {
        AutoScaler scaler = Settled(step, 0);

        Assert.Equal(step, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(1.0, scaler.BarRatio(true, step), 6);
        CheckInvariants(scaler, step, 0);
    }

    [Fact]
    public void AFreshBurstRaisesTheScaleAtOnceAndNoBarOverflows()
    {
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(0, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(16 * Kb, scaler.ScaleBytesPerSecond, 0);

        scaler.Update(40 * Kb, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.Equal(64 * Kb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(40.0 / 64.0, scaler.BarRatio(true, 40 * Kb), 6);
        CheckInvariants(scaler, 40 * Kb, 0);
    }

    [Fact]
    public void TheScaleStepsDownOnlyAfterTheRateHasStayedLow()
    {
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(1 * Mb, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(1 * Mb, scaler.ScaleBytesPerSecond, 0);

        // One tick below the boundary is not enough to drop a step...
        scaler.Update(16 * Kb, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(1 * Mb, scaler.ScaleBytesPerSecond, 0);

        // ... but after the hold the scale comes down to the round step for 16 KB/s.
        scaler.Update(16 * Kb, 0, AutoScaler.StepDownHoldSeconds, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(16 * Kb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(1.0, scaler.BarRatio(true, 16 * Kb), 6);
        CheckInvariants(scaler, 16 * Kb, 0);
    }

    [Fact]
    public void ARateJustUnderTheStepNeverTogglesIt()
    {
        // Quantising is the hysteresis: a rate below the current step still picks that
        // same step, so the row cannot flicker between two scales.
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(64 * Kb, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(64 * Kb, scaler.ScaleBytesPerSecond, 0);

        foreach (double rate in new[] { 63.9 * Kb, 63 * Kb, 33 * Kb, 32.1 * Kb })
        {
            for (int i = 0; i < 20; i++)
            {
                scaler.Update(rate, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
                Assert.Equal(64 * Kb, scaler.ScaleBytesPerSecond, 0);
            }
        }

        CheckInvariants(scaler, 32.1 * Kb, 0);
    }

    [Fact]
    public void OnceTheHoldExpiresTheScaleSnapsStraightToTheStepForTheCurrentRate()
    {
        AutoScaler scaler = Settled(100 * Mb, 0);
        Assert.Equal(128 * Mb, scaler.ScaleBytesPerSecond, 0);

        double previous = scaler.ScaleBytesPerSecond;

        // Each tick that completes the hold re-picks the step from the rate actually
        // being drawn. The scale may only ever fall, and only to a real step.
        for (int i = 0; i < 40; i++)
        {
            scaler.Update(0, 0, AutoScaler.StepDownHoldSeconds, autoScale: true, manualPeakBytesPerSecond: 0);

            double now = scaler.ScaleBytesPerSecond;
            Assert.True(now <= previous, $"scale went up from {previous} to {now} while idle");
            Assert.True(IsOnRamp(now), $"{now} is not a round step");
            previous = now;
        }

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(0, scaler.BarRatio(true, 0));
        CheckInvariants(scaler, 0, 0);
    }

    [Fact]
    public void TheScaleNeverExceedsFullOnAnyRate()
    {
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(0, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);

        foreach (double rate in new[] { 1.0, 17 * Kb, 900 * Kb, 12 * Mb, 700 * Mb, 100 * 1024 * Mb })
        {
            scaler.Update(rate, rate, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);

            Assert.InRange(scaler.BarRatio(true, rate), 0, 1);
            Assert.InRange(scaler.BarRatio(false, rate), 0, 1);
            CheckInvariants(scaler, rate, rate);
        }
    }

    [Fact]
    public void TheFasterDirectionAlwaysPicksTheStepWhicheverRowItIsIn()
    {
        foreach (double download in new[] { 5 * Kb, 300 * Kb, 4 * Mb })
        {
            foreach (double upload in new[] { 5 * Kb, 300 * Kb, 4 * Mb })
            {
                AutoScaler scaler = Settled(download, upload);

                Assert.Equal(AutoScaler.NextScaleStep(Math.Max(download, upload)), scaler.ScaleBytesPerSecond, 0);
                CheckInvariants(scaler, download, upload);
            }
        }
    }

    [Fact]
    public void TheManualPeakReplacesTheStepWhenAutoScalingIsOff()
    {
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(4 * Mb, 4 * Kb, 1.0, autoScale: false, manualPeakBytesPerSecond: 3 * Mb);

        Assert.Equal(3 * Mb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(1.0, scaler.BarRatio(true, 4 * Mb), 6);
        Assert.True(scaler.BarRatio(false, 4 * Kb) < 0.01);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1000)]
    public void GarbageRatesLeaveTheScaleAlone(double rate)
    {
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(512 * Kb, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(512 * Kb, scaler.ScaleBytesPerSecond, 0);

        scaler.Update(rate, rate, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.Equal(512 * Kb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(0, scaler.SharedRatio(rate));
        Assert.False(double.IsNaN(scaler.ScaleBytesPerSecond));
    }

    [Fact]
    public void ResetReturnsTheScaleToTheFloor()
    {
        AutoScaler scaler = Settled(50 * Mb, 0);
        Assert.True(scaler.ScaleBytesPerSecond > AutoScaler.MinimumPeakBytesPerSecond);

        scaler.Reset();

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.ScaleBytesPerSecond, 0);
    }

    [Fact]
    public void ASqueezedTimerStillAdvancesTheHoldRatherThanStalling()
    {
        // A tick with no elapsed time must not count towards the hold, or a paused clock
        // would drop the scale on the next sample.
        var scaler = new AutoScaler { SharedScale = true };
        scaler.Update(1 * Mb, 0, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);

        for (int i = 0; i < 10; i++)
        {
            scaler.Update(0, 0, 0, autoScale: true, manualPeakBytesPerSecond: 0);
        }

        Assert.Equal(1 * Mb, scaler.ScaleBytesPerSecond, 0);

        scaler.Update(0, 0, AutoScaler.StepDownHoldSeconds, autoScale: true, manualPeakBytesPerSecond: 0);
        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.ScaleBytesPerSecond, 0);
    }
}
