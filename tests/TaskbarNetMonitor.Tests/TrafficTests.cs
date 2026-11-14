using System.Drawing;
using TaskbarNetMonitor.Core;

namespace TaskbarNetMonitor.Tests;

public class RateCalculatorTests
{
    [Fact]
    public void FirstSampleProducesNoBogusSpike()
    {
        var rate = new RateCalculator();

        Assert.Equal(0, rate.Update(1_000_000, 1.0));
        Assert.Equal(0, rate.SmoothedBytesPerSecond);
    }

    [Fact]
    public void SeededBaselineDoesNotReportTheWholeCounter()
    {
        var rate = new RateCalculator();
        rate.Seed(5_000_000);

        Assert.Equal(0, rate.Update(5_000_000, 1.0));
        Assert.Equal(2000, rate.Update(5_002_000, 1.0), 6);
    }

    [Fact]
    public void RateIsDeltaOverElapsedTime()
    {
        var rate = new RateCalculator();
        rate.Seed(0);

        Assert.Equal(2000, rate.Update(1000, 0.5), 6);
        Assert.Equal(4000, rate.Update(2000, 0.25), 6);
        Assert.Equal(0, rate.Update(2000, 1.0), 6);
    }

    [Fact]
    public void ABackwardsCounterReportsNoTrafficInsteadOfNegative()
    {
        var rate = new RateCalculator();
        rate.Seed(9_000);

        Assert.Equal(0, rate.Update(1_000, 1.0));
        Assert.Equal(0, rate.SmoothedBytesPerSecond);

        // And it recovers on the next sample.
        Assert.Equal(500, rate.Update(1_500, 1.0), 6);
    }

    [Fact]
    public void ABackwardsCounterKeepsTheSmoothedValueInsteadOfJumpingToZero()
    {
        var rate = new RateCalculator(smoothingFactor: 0.5);
        rate.Seed(0);
        rate.Update(1_000, 1.0);
        double smoothed = rate.SmoothedBytesPerSecond;

        rate.Update(10, 1.0);

        Assert.Equal(smoothed, rate.SmoothedBytesPerSecond, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void AZeroOrNegativeIntervalDoesNotDivideByZero(double elapsed)
    {
        var rate = new RateCalculator();
        rate.Seed(0);

        double value = rate.Update(1_000, elapsed);

        Assert.Equal(0, value);
        Assert.False(double.IsNaN(value));
        Assert.False(double.IsInfinity(value));
    }

    [Fact]
    public void SmoothingMovesTowardsTheNewRateWithoutJumping()
    {
        var rate = new RateCalculator(smoothingFactor: 0.5);
        rate.Seed(0);

        rate.Update(1_000, 1.0);
        Assert.Equal(1000, rate.SmoothedBytesPerSecond, 6);

        // The counters are cumulative: 1000 -> 4000 in one second is a 3000 B/s rate.
        rate.Update(4_000, 1.0);
        Assert.Equal(2000, rate.SmoothedBytesPerSecond, 6);

        rate.Update(7_000, 1.0);
        Assert.Equal(2500, rate.SmoothedBytesPerSecond, 6);
    }

    [Fact]
    public void SmoothedValueNeverExceedsTheLargestRawRate()
    {
        var rate = new RateCalculator(smoothingFactor: 0.9);
        rate.Seed(0);
        double max = 0;

        for (int i = 1; i <= 50; i++)
        {
            double raw = rate.Update(i * 10_000L, 1.0);
            max = Math.Max(max, raw);
            Assert.InRange(rate.SmoothedBytesPerSecond, 0, max + 1e-9);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void AnInvalidSmoothingFactorIsRejected(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateCalculator(factor));
    }

    [Fact]
    public void ResetForgetsTheHistory()
    {
        var rate = new RateCalculator();
        rate.Seed(0);
        rate.Update(5_000, 1.0);

        rate.Reset();

        Assert.Equal(0, rate.SmoothedBytesPerSecond);
        Assert.Equal(0, rate.RawBytesPerSecond);
    }
}

public class AutoScalerTests
{
    private const double Second = 1.0;

    [Fact]
    public void AnIdleLinkHasAZeroRatioAndAFloorPeak()
    {
        var scaler = new AutoScaler();

        scaler.Update(0, 0, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.Equal(0, scaler.DownloadRatio(0));
        Assert.Equal(0, scaler.UploadRatio(0));
        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.DownloadPeak, 6);
        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.UploadPeak, 6);
    }

    [Fact]
    public void AFreshPeakIsReachedImmediatelySoTheFirstBurstIsVisible()
    {
        var scaler = new AutoScaler();
        scaler.Update(1_000_000, 500_000, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.Equal(1.0, scaler.DownloadRatio(1_000_000), 6);
        Assert.Equal(1.0, scaler.UploadRatio(500_000), 6);
    }

    [Fact]
    public void DownAndUploadAreScaledIndependently()
    {
        var scaler = new AutoScaler();
        scaler.Update(10_000_000, 100_000, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        // A tiny upload is still visible next to a huge download.
        Assert.Equal(1.0, scaler.UploadRatio(100_000), 6);
        Assert.Equal(0.01, scaler.DownloadRatio(100_000), 6);
    }

    [Fact]
    public void ThePeakDecaysTowardsTheCurrentRateButNeverBelowTheFloor()
    {
        var scaler = new AutoScaler();
        scaler.Update(10_000_000, 0, Second, autoScale: true, manualPeakBytesPerSecond: 0);
        double first = scaler.DownloadPeak;

        scaler.Update(0, 0, Second, autoScale: true, manualPeakBytesPerSecond: 0);
        double second = scaler.DownloadPeak;

        Assert.True(second < first, "the peak does not decay");
        Assert.True(second > AutoScaler.MinimumPeakBytesPerSecond);

        for (int i = 0; i < 100; i++)
        {
            scaler.Update(0, 0, Second, autoScale: true, manualPeakBytesPerSecond: 0);
        }

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.DownloadPeak, 6);
    }

    [Fact]
    public void ThePeakFollowsATrafficSpikeUpward()
    {
        var scaler = new AutoScaler();
        scaler.Update(1000, 1000, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        scaler.Update(50_000_000, 1000, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.Equal(50_000_000, scaler.DownloadPeak, 6);
        Assert.Equal(1.0, scaler.DownloadRatio(50_000_000), 6);
    }

    [Fact]
    public void WithAutoScaleOffTheManualPeakIsUsedForBothBars()
    {
        var scaler = new AutoScaler();

        scaler.Update(1_000_000, 1_000_000, Second, autoScale: false, manualPeakBytesPerSecond: 2_000_000);

        Assert.Equal(2_000_000, scaler.DownloadPeak, 6);
        Assert.Equal(2_000_000, scaler.UploadPeak, 6);
        Assert.Equal(0.5, scaler.DownloadRatio(1_000_000), 6);
        Assert.False(scaler.AutoScale);
    }

    [Fact]
    public void ARidiculouslySmallManualPeakStillWorks()
    {
        var scaler = new AutoScaler();

        scaler.Update(0, 0, Second, autoScale: false, manualPeakBytesPerSecond: 0);

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.DownloadPeak, 6);
        Assert.Equal(0.5, scaler.DownloadRatio(AutoScaler.MinimumPeakBytesPerSecond / 2), 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GarbageRatesDoNotPoisonThePeak(double rate)
    {
        var scaler = new AutoScaler();

        scaler.Update(rate, rate, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.False(double.IsNaN(scaler.DownloadPeak));
        Assert.False(double.IsInfinity(scaler.DownloadPeak));
        Assert.Equal(0, scaler.DownloadRatio(rate));
    }

    [Fact]
    public void RatiosAlwaysStayBetweenZeroAndOne()
    {
        var scaler = new AutoScaler();

        for (int i = 0; i < 200; i++)
        {
            double rate = i % 7 == 0 ? 0 : Math.Pow(10, i % 10);
            scaler.Update(rate, rate / 3, 0.5, autoScale: true, manualPeakBytesPerSecond: 0);

            Assert.InRange(scaler.DownloadRatio(rate), 0, 1);
            Assert.InRange(scaler.UploadRatio(rate / 3), 0, 1);
            Assert.InRange(scaler.DownloadRatio(rate * 10), 0, 1);
        }
    }

    [Fact]
    public void AZeroElapsedTimeDoesNotCollapseThePeak()
    {
        var scaler = new AutoScaler();
        scaler.Update(1_000_000, 1_000_000, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        scaler.Update(0, 0, 0, autoScale: true, manualPeakBytesPerSecond: 0);

        Assert.Equal(1_000_000, scaler.DownloadPeak, 6);
    }

    [Fact]
    public void ResetReturnsToTheFloor()
    {
        var scaler = new AutoScaler();
        scaler.Update(9_000_000, 9_000_000, Second, autoScale: true, manualPeakBytesPerSecond: 0);

        scaler.Reset();

        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.DownloadPeak, 6);
        Assert.Equal(AutoScaler.MinimumPeakBytesPerSecond, scaler.UploadPeak, 6);
    }
}

public class SpeedFormatterTests
{
    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(1, "1 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(1023, "1023 B/s")]
    [InlineData(1024, "1.0 KB/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(10240, "10.0 KB/s")]
    [InlineData(1048576, "1.0 MB/s")]
    [InlineData(1048576 * 12.5, "12.5 MB/s")]
    [InlineData(1048576 * 150, "150 MB/s")]
    [InlineData(1073741824, "1.0 GB/s")]
    public void SpeedsAreFormattedForTheTaskbar(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, SpeedFormatter.Format(bytesPerSecond));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GarbageSpeedsFormatAsZero(double value)
    {
        Assert.Equal("0 B/s", SpeedFormatter.Format(value));
    }

    [Fact]
    public void RowsCarryAnArrowAndNeverAsterisks()
    {
        string down = SpeedFormatter.FormatRow(isDownload: true, 2048);
        string up = SpeedFormatter.FormatRow(isDownload: false, 2048);

        Assert.Equal("\u2193 2.0 KB/s", down);
        Assert.Equal("\u2191 2.0 KB/s", up);
        Assert.DoesNotContain('*', down);
    }

    [Fact]
    public void NoFormattedValueGrowsUnboundedly()
    {
        double[] samples =
        [
            0, 1, 1023, 1024, 1024 * 99.9, 1024 * 100, 1048576, 1048576 * 9.9,
            1048576 * 99.9, 1048576 * 100, 1048576 * 999, 1048576 * 1023.9,
            1073741824.0, 1073741824.0 * 99.9, 1073741824.0 * 100, 1073741824.0 * 1023,
            double.MaxValue,
        ];

        // A widget whose text keeps growing would have to be resized, which is
        // exactly what makes a taskbar item flicker. 11 characters is the budget.
        Assert.All(samples, value => Assert.True(
            SpeedFormatter.Format(value).Length <= 11,
            $"'{SpeedFormatter.Format(value)}' is wider than the layout allows"));
    }
}

public class ColorHelperTests
{
    [Theory]
    [InlineData("#2ECC71", 0x2E, 0xCC, 0x71)]
    [InlineData("2ECC71", 0x2E, 0xCC, 0x71)]
    [InlineData("#2ecc71", 0x2E, 0xCC, 0x71)]
    [InlineData("  #000000  ", 0x00, 0x00, 0x00)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    public void HexColoursAreParsed(string text, int r, int g, int b)
    {
        Assert.True(ColorHelper.TryParse(text, out Color colour));
        Assert.Equal(Color.FromArgb(r, g, b), colour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void BadColoursAreRejected(string? text)
    {
        Assert.False(ColorHelper.TryParse(text, out _));
    }

    [Fact]
    public void ParseFallsBackToTheGivenColour()
    {
        Assert.Equal(Color.Orange, ColorHelper.Parse("nonsense", Color.Orange));
        Assert.Equal(Color.Orange, ColorHelper.Parse(null, Color.Orange));
        Assert.Equal(Color.FromArgb(0, 255, 0), ColorHelper.Parse("00ff00", Color.Orange));
    }

    [Fact]
    public void ToHexRoundTripsAndDropsAlpha()
    {
        Assert.Equal("#2ECC71", ColorHelper.ToHex(Color.FromArgb(0x2E, 0xCC, 0x71)));
        Assert.Equal("#000000", ColorHelper.ToHex(Color.FromArgb(0, 0, 0)));
        Assert.Equal("#FFFFFF", ColorHelper.ToHex(Color.FromArgb(255, 255, 255, 255)));

        Color parsed = ColorHelper.Parse(ColorHelper.ToHex(Color.FromArgb(1, 2, 3)), Color.Empty);
        Assert.Equal(Color.FromArgb(1, 2, 3), parsed);
    }

    [Fact]
    public void AlphaOnTheInputColourIsIgnoredSoTheBarStaysSolid()
    {
        Color colour = Color.FromArgb(10, 20, 30, 40);

        Assert.Equal("#141E28", ColorHelper.ToHex(colour));
        Assert.Equal(255, ColorHelper.Parse(ColorHelper.ToHex(colour), Color.Empty).A);
    }
}

public class TrafficSamplerTests
{
    [Theory]
    [InlineData(System.Net.NetworkInformation.OperationalStatus.Up, System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, true)]
    [InlineData(System.Net.NetworkInformation.OperationalStatus.Up, System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211, true)]
    [InlineData(System.Net.NetworkInformation.OperationalStatus.Up, System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet, true)]
    [InlineData(System.Net.NetworkInformation.OperationalStatus.Down, System.Net.NetworkInformation.NetworkInterfaceType.Ethernet, false)]
    [InlineData(System.Net.NetworkInformation.OperationalStatus.Up, System.Net.NetworkInformation.NetworkInterfaceType.Loopback, false)]
    [InlineData(System.Net.NetworkInformation.OperationalStatus.Up, System.Net.NetworkInformation.NetworkInterfaceType.Tunnel, false)]
    public void OnlyRealActiveAdaptersAreCounted(
        System.Net.NetworkInformation.OperationalStatus status,
        System.Net.NetworkInformation.NetworkInterfaceType type,
        bool expected)
    {
        Assert.Equal(expected, TrafficSampler.ShouldCount(status, type));
    }

    [Fact]
    public void ReadingTotalsNeverReturnsNegativeCounters()
    {
        var sampler = new TrafficSampler();

        var totals = sampler.ReadTotals();

        Assert.True(totals.Received >= 0);
        Assert.True(totals.Sent >= 0);

        var second = sampler.ReadTotals();
        Assert.True(second.Received >= totals.Received - 1024);
    }
}

public class StartupRegistrationTests
{
    [Fact]
    public void TheRunCommandQuotesTheExecutableSoSpacesSurvive()
    {
        string command = StartupRegistration.BuildCommand(@"C:\Program Files\Taskbar Net Monitor\TaskbarNetMonitor.exe");

        Assert.Equal("\"C:\\Program Files\\Taskbar Net Monitor\\TaskbarNetMonitor.exe\"", command);
    }

    [Fact]
    public void TheRunCommandUsesTheGivenPath()
    {
        string command = StartupRegistration.BuildCommand(@"C:\apps\tnm.exe");

        Assert.Contains("tnm.exe", command, StringComparison.Ordinal);
        Assert.StartsWith("\"", command, StringComparison.Ordinal);
        Assert.EndsWith("\"", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPathIsRejected(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => StartupRegistration.BuildCommand(path));
    }

    [Fact]
    public void TheRunKeyNameIsStable()
    {
        // Changing this would silently leave two entries in HKCU\...\Run.
        Assert.Equal("TaskbarNetMonitor", StartupRegistration.ValueName);
    }
}
