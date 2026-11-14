namespace HASNetMonitor.Core;

/// <summary>
/// Keeps a reference "full scale" rate so a small bar still shows movement on a
/// fast link and a busy one does not sit permanently pinned. Each direction gets
/// its own reference: uploads and downloads usually differ by an order of magnitude
/// and a shared scale would hide one of them completely.
/// </summary>
internal sealed class AutoScaler
{
    /// <summary>Never scale below this, or an idle sample would divide by ~0.</summary>
    public const double MinimumPeakBytesPerSecond = 16 * 1024;

    /// <summary>Time constant of the decay towards the current rate, in seconds.</summary>
    public const double DecaySeconds = 4.0;

    private double _downloadPeak = MinimumPeakBytesPerSecond;
    private double _uploadPeak = MinimumPeakBytesPerSecond;

    public double DownloadPeak => Math.Max(_downloadPeak, MinimumPeakBytesPerSecond);

    public double UploadPeak => Math.Max(_uploadPeak, MinimumPeakBytesPerSecond);

    public bool AutoScale { get; private set; } = true;

    public void Reset()
    {
        _downloadPeak = MinimumPeakBytesPerSecond;
        _uploadPeak = MinimumPeakBytesPerSecond;
    }

    public void Update(
        double downloadBytesPerSecond,
        double uploadBytesPerSecond,
        double elapsedSeconds,
        bool autoScale,
        double manualPeakBytesPerSecond)
    {
        AutoScale = autoScale;

        if (!autoScale)
        {
            double manual = Math.Max(manualPeakBytesPerSecond, MinimumPeakBytesPerSecond);
            _downloadPeak = manual;
            _uploadPeak = manual;
            return;
        }

        double decay = elapsedSeconds > 0
            ? Math.Exp(-elapsedSeconds / DecaySeconds)
            : 1.0;

        _downloadPeak = NextPeak(_downloadPeak, downloadBytesPerSecond, decay);
        _uploadPeak = NextPeak(_uploadPeak, uploadBytesPerSecond, decay);
    }

    private static double NextPeak(double peak, double rate, double decay)
    {
        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0)
        {
            rate = 0;
        }

        double decayed = peak * decay;
        return Math.Max(Math.Max(rate, decayed), MinimumPeakBytesPerSecond);
    }

    public double DownloadRatio(double bytesPerSecond) => Ratio(bytesPerSecond, DownloadPeak);

    public double UploadRatio(double bytesPerSecond) => Ratio(bytesPerSecond, UploadPeak);

    private static double Ratio(double bytesPerSecond, double peak)
    {
        // Non-finite input comes from a broken counter or a division by a bogus
        // interval; treat it as "no traffic" instead of painting a full bar.
        if (!Numeric.IsFinite(bytesPerSecond) || bytesPerSecond <= 0 || !Numeric.IsFinite(peak) || peak <= 0)
        {
            return 0;
        }

        return Numeric.Clamp(bytesPerSecond / peak, 0, 1);
    }
}
