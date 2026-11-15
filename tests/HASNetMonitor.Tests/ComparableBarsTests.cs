using HASNetMonitor.Core;
using HASNetMonitor.Rendering;
using Xunit;

namespace HASNetMonitor.Tests;

/// <summary>
/// The two rows are read against each other, so they must be measured against one
/// full-scale rate. The case that started this: 10 MB/s down beside 14 KB/s up used to
/// draw the upload bar at 53 of 60 px because each row had its own peak.
/// </summary>
public class ComparableBarsTests
{
    private const double Mb = 1024 * 1024;
    private const double Kb = 1024;

    /// <summary>A scaler that has seen the same sample a few times, as the widget would.</summary>
    private static AutoScaler Settled(double download, double upload, bool sharedScale = true, int ticks = 10)
    {
        var scaler = new AutoScaler { SharedScale = sharedScale };
        for (int i = 0; i < ticks; i++)
        {
            scaler.Update(download, upload, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        }

        scaler.Update(download, upload, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        return scaler;
    }

    private static int DrawnPixels(double ratio) => WidgetRenderer.FillWidth(60, ratio);

    [Fact]
    public void ATrickleOfUploadBesideAFloodOfDownloadDrawsASliver()
    {
        AutoScaler scaler = Settled(10 * Mb, 14 * Kb);

        double download = scaler.BarRatio(true, 10 * Mb);
        double upload = scaler.BarRatio(false, 14 * Kb);

        // The 10 MB/s direction picks the next round step, 16 MB/s, and both rows fill
        // against it.
        Assert.Equal(16 * Mb, scaler.ScaleBytesPerSecond, 0);
        Assert.Equal(10.0 / 16.0, download, 6);
        Assert.True(upload < 0.01, $"upload ratio {upload} is not a sliver");
        Assert.Equal(38, DrawnPixels(download));
        Assert.True(DrawnPixels(upload) <= 2, $"upload drew {DrawnPixels(upload)} px next to a 38 px download");
    }

    [Fact]
    public void TheFasterDirectionAlwaysDrawsTheLongerBar()
    {
        // The rule the user asked for: the bars are comparable in both directions, and
        // their order follows the rates through every order of magnitude.
        double[] rates = [0, 500, 14 * Kb, 100 * Kb, Mb, 10 * Mb, 100 * Mb, 1024 * Mb];

        foreach (double download in rates)
        {
            foreach (double upload in rates)
            {
                AutoScaler scaler = Settled(download, upload);

                double dl = scaler.BarRatio(true, download);
                double ul = scaler.BarRatio(false, upload);

                Assert.InRange(dl, 0, 1);
                Assert.InRange(ul, 0, 1);

                if (download > upload)
                {
                    Assert.True(dl > ul, $"download {download} drew {dl} but upload {upload} drew {ul}");
                }
                else if (upload > download)
                {
                    Assert.True(ul > dl, $"upload {upload} drew {ul} but download {download} drew {dl}");
                }
                else
                {
                    Assert.Equal(dl, ul, 9);
                }

                // And the ratio is honestly relative: the same rate gives the same bar
                // whichever row it appears in.
                Assert.Equal(scaler.SharedRatio(download), scaler.BarRatio(true, download), 9);
                Assert.Equal(scaler.SharedRatio(upload), scaler.BarRatio(false, upload), 9);
            }
        }
    }

    [Fact]
    public void EveryStyleTurnsTheRatioIntoBlocksInTheSameOrder()
    {
        // Styles whose bar is not one filled run - level, dots and segmented - round the
        // ratio to whole blocks. A bigger ratio must never light fewer blocks, or the
        // comparison the user asked for would invert in those styles.
        const int pieces = 20;
        double previousWidth = -1;
        int previousSegments = -1;

        for (double ratio = 0; ratio <= 1.0001; ratio += 0.02)
        {
            double width = WidgetRenderer.FillWidth(60, ratio);
            int segments = WidgetRenderer.LevelLitSegments(pieces, ratio);

            Assert.True(width >= previousWidth, $"fill width fell from {previousWidth} to {width} at {ratio}");
            Assert.True(segments >= previousSegments, $"lit blocks fell from {previousSegments} to {segments} at {ratio}");
            Assert.InRange(segments, 0, pieces);
            Assert.InRange(width, 0, 60);

            previousWidth = width;
            previousSegments = segments;
        }

        // The ends are exact: nothing lit at zero, everything at one.
        Assert.Equal(0, WidgetRenderer.FillWidth(60, 0));
        Assert.Equal(0, WidgetRenderer.LevelLitSegments(pieces, 0));
        Assert.Equal(60, WidgetRenderer.FillWidth(60, 1));
        Assert.Equal(pieces, WidgetRenderer.LevelLitSegments(pieces, 1));
    }

    [Fact]
    public void OneScaleIsTheFasterDirection()
    {
        AutoScaler scaler = Settled(10 * Mb, 14 * Kb);

        Assert.Equal(10 * Mb, scaler.SharedPeak, 0);
    }

    [Fact]
    public void EqualRatesDrawEqualBars()
    {
        AutoScaler scaler = Settled(2 * Mb, 2 * Mb);

        Assert.Equal(scaler.BarRatio(true, 2 * Mb), scaler.BarRatio(false, 2 * Mb), 9);
        Assert.Equal(1.0, scaler.BarRatio(true, 2 * Mb), 6);
    }

    [Fact]
    public void WhenTheDirectionsSwapTheBarsSwapToo()
    {
        AutoScaler scaler = Settled(20 * Kb, 5 * Mb);

        Assert.True(
            scaler.BarRatio(false, 5 * Mb) > scaler.BarRatio(true, 20 * Kb),
            "an upload upload hundreds of times faster than the download did not draw the longer bar");
    }

    [Fact]
    public void ADirectionThatStopsReleasesTheSharedScaleForTheOther()
    {
        AutoScaler scaler = Settled(10 * Mb, 1 * Mb);
        Assert.True(scaler.BarRatio(false, 1 * Mb) < 0.2, "upload should be small beside 10 MB/s");

        // Download stops; the shared peak must decay down to the upload so the upload bar
        // grows back instead of staying crushed by history.
        for (int i = 0; i < 20; i++)
        {
            scaler.Update(0, 1 * Mb, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);
        }

        Assert.True(scaler.BarRatio(false, 1 * Mb) > 0.9, $"upload ratio {scaler.BarRatio(false, 1 * Mb)} did not recover");
    }

    [Fact]
    public void PerDirectionModeStillFillsBothRowsForAFamiliarWidget()
    {
        // The old reading is still one setting away, unchanged.
        AutoScaler scaler = Settled(10 * Mb, 14 * Kb, sharedScale: false);

        Assert.Equal(1.0, scaler.BarRatio(true, 10 * Mb), 6);
        Assert.True(scaler.BarRatio(false, 14 * Kb) > 0.5, "per-direction mode should still fill the upload row");
        Assert.False(scaler.SharedScale);
    }

    [Fact]
    public void TheManualPeakIsOneScaleInEitherMode()
    {
        foreach (bool sharedScale in new[] { false, true })
        {
            var scaler = new AutoScaler { SharedScale = sharedScale };
            scaler.Update(
                4 * Mb,
                4 * Kb,
                1.0,
                autoScale: false,
                manualPeakBytesPerSecond: 8 * Mb);

            Assert.Equal(8 * Mb, scaler.SharedPeak, 0);
            Assert.Equal(0.5, scaler.BarRatio(true, 4 * Mb), 6);
            Assert.True(scaler.BarRatio(false, 4 * Kb) < 0.01);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-5)]
    public void GarbageRatesCannotBreakTheSharedScale(double rate)
    {
        var scaler = new AutoScaler();
        scaler.Update(rate, rate, 1.0, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.False(double.IsNaN(scaler.SharedPeak));
        Assert.False(double.IsInfinity(scaler.SharedPeak));
        Assert.True(scaler.SharedPeak >= AutoScaler.MinimumPeakBytesPerSecond);
        Assert.Equal(0, scaler.SharedRatio(rate));
    }

    [Fact]
    public void TheSharedScaleNeverDropsBelowTheFloor()
    {
        AutoScaler scaler = Settled(0, 0);

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.SharedPeak, 6);
        Assert.Equal(0, scaler.SharedRatio(0));
    }

    [Fact]
    public void ResetForgetsTheSharedScale()
    {
        AutoScaler scaler = Settled(500 * Mb, 0);

        scaler.Reset();

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.SharedPeak, 6);
    }

    [Fact]
    public void TheSettingIsOnByDefaultAndSurvivesTheConfig()
    {
        var config = new MonitorConfig();

        Assert.True(config.SharedScale, "comparable bars should be what a new install shows");

        config.SharedScale = false;
        Assert.False(config.Clone().SharedScale);
        Assert.False(config.ToJson().Contains("\"SharedScale\": true", StringComparison.Ordinal));

        var copy = new MonitorConfig();
        copy.CopyFrom(config);
        Assert.False(copy.SharedScale);

        MonitorConfig reloaded = MonitorConfig.FromJson(config.ToJson());
        Assert.False(reloaded.SharedScale);

        config.SharedScale = true;
        Assert.True(MonitorConfig.FromJson(config.ToJson()).SharedScale);
    }

    [Fact]
    public void TheWidgetAsksForOneScaleFromTheConfig()
    {
        // The scaler is only comparable because the widget passes the setting in and
        // reads the rows through the one-scale accessors; either half going missing
        // would silently restore the old per-row reading.
        string source = SourceText.Read("UI", "TaskbarWidget.cs");

        Assert.Contains("_scaler.SharedScale = _config.SharedScale", source, StringComparison.Ordinal);
        Assert.Contains("BarRatio(isDownload: true", source, StringComparison.Ordinal);
        Assert.Contains("BarRatio(isDownload: false", source, StringComparison.Ordinal);
    }
}
