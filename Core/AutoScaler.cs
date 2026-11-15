namespace HASNetMonitor.Core;

/// <summary>
/// Keeps a reference "full scale" rate so a small bar still shows movement on a
/// fast link and a busy one does not sit permanently pinned.
///
/// Two modes exist, because they answer different questions. In the default shared
/// mode both bars are measured against one full-scale rate - the larger of the two
/// directions - so the rows can be compared at a glance: 10 MB/s of downloads towers
/// over 14 KB/s of uploads exactly as the numbers say. In per-direction mode each
/// bar has its own reference, which keeps a trickle of upload visible beside a flood
/// of download, at the cost of both rows reading "full" no matter their ratio.
/// </summary>
internal sealed class AutoScaler
{
    /// <summary>Never scale below this, or an idle sample would divide by ~0.</summary>
    public const double MinimumPeakBytesPerSecond = 16 * 1024;

    /// <summary>Time constant of the decay towards the current rate, in seconds.</summary>
    public const double DecaySeconds = 4.0;

    /// <summary>
    /// How long a rate may sit below the current scale before it drops a step. Rising is
    /// immediate so a burst is never clipped; falling waits, so a rate hovering on a step
    /// boundary cannot flicker the whole row between two lengths every sample.
    /// </summary>
    public const double StepDownHoldSeconds = 3.0;

    /// <summary>
    /// The largest step the ramp will climb to. Nothing on a network counter reaches it;
    /// it exists so a nonsense rate cannot spin <see cref="NextScaleStep"/>.
    /// </summary>
    public const double MaximumScaleStep = 1024.0 * 1024 * 1024 * 1024;

    private double _downloadPeak = MinimumPeakBytesPerSecond;
    private double _uploadPeak = MinimumPeakBytesPerSecond;
    private double _sharedPeak = MinimumPeakBytesPerSecond;
    private double _scaleStep = MinimumPeakBytesPerSecond;
    private double _belowSeconds;

    public double DownloadPeak => Math.Max(_downloadPeak, MinimumPeakBytesPerSecond);

    public double UploadPeak => Math.Max(_uploadPeak, MinimumPeakBytesPerSecond);

    /// <summary>
    /// The full-scale rate used when both bars share one scale: the faster direction.
    /// </summary>
    public double SharedPeak => Math.Max(_sharedPeak, MinimumPeakBytesPerSecond);

    public bool AutoScale { get; private set; } = true;

    /// <summary>
    /// Whether both rows are drawn against one full-scale rate. Off, each row fills
    /// against its own peak, so a trickle stays visible beside a flood.
    /// </summary>
    public bool SharedScale { get; set; }

    public void Reset()
    {
        _downloadPeak = MinimumPeakBytesPerSecond;
        _uploadPeak = MinimumPeakBytesPerSecond;
        _sharedPeak = MinimumPeakBytesPerSecond;
        _scaleStep = MinimumPeakBytesPerSecond;
        _belowSeconds = 0;
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
            _sharedPeak = manual;
            _scaleStep = manual;
            _belowSeconds = 0;
            return;
        }

        double decay = elapsedSeconds > 0
            ? Math.Exp(-elapsedSeconds / DecaySeconds)
            : 1.0;

        _downloadPeak = NextPeak(_downloadPeak, downloadBytesPerSecond, decay);
        _uploadPeak = NextPeak(_uploadPeak, uploadBytesPerSecond, decay);

        // One reference for both rows: whichever direction is moving faster is full
        // scale, so the other row sits where its rate really puts it.
        double fastest = Math.Max(ToRate(downloadBytesPerSecond), ToRate(uploadBytesPerSecond));
        _sharedPeak = NextPeak(_sharedPeak, fastest, decay);

        AdvanceScaleStep(fastest, elapsedSeconds);
    }

    /// <summary>
    /// Moves the drawn scale to the next round step for the fastest direction, up at
    /// once and down only after <see cref="StepDownHoldSeconds"/>.
    /// </summary>
    private void AdvanceScaleStep(double fastestBytesPerSecond, double elapsedSeconds)
    {
        double target = NextScaleStep(fastestBytesPerSecond);

        if (target > _scaleStep)
        {
            _scaleStep = target;
            _belowSeconds = 0;
            return;
        }

        if (target < _scaleStep)
        {
            _belowSeconds += Math.Max(elapsedSeconds, 0);
            if (_belowSeconds >= StepDownHoldSeconds)
            {
                _scaleStep = target;
                _belowSeconds = 0;
            }

            return;
        }

        _belowSeconds = 0;
    }

    /// <summary>
    /// The next round full-scale step at or above a rate: the scale only ever shows
    /// 16 KB/s, 32 KB/s, 64 KB/s ... 512 KB/s, 1 MB/s, 2 MB/s and so on, so a bar length
    /// means a rate the user can name. 15 KB/s fills against 16 KB/s, 345 KB/s against
    /// 512 KB/s, 564 KB/s against 1 MB/s.
    /// </summary>
    public static double NextScaleStep(double bytesPerSecond)
    {
        double rate = ToRate(bytesPerSecond);
        double step = MinimumPeakBytesPerSecond;

        while (step < rate && step < MaximumScaleStep)
        {
            step *= 2;
        }

        return step;
    }

    private static double NextPeak(double peak, double rate, double decay)
    {
        double decayed = peak * decay;
        return Math.Max(Math.Max(ToRate(rate), decayed), MinimumPeakBytesPerSecond);
    }

    /// <summary>Garbage from a broken counter reads as "no traffic".</summary>
    private static double ToRate(double rate) =>
        double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0 ? 0 : rate;

    public double DownloadRatio(double bytesPerSecond) => Ratio(bytesPerSecond, DownloadPeak);

    public double UploadRatio(double bytesPerSecond) => Ratio(bytesPerSecond, UploadPeak);

    /// <summary>
    /// The full-scale rate the bars are drawn against: the round step the traffic has
    /// climbed to, or the manual peak when auto-scaling is off.
    /// </summary>
    public double ScaleBytesPerSecond => AutoScale ? ScaleStepBytesPerSecond : SharedPeak;

    /// <summary>The round step the scale has climbed to, in bytes per second.</summary>
    public double ScaleStepBytesPerSecond => Math.Max(_scaleStep, MinimumPeakBytesPerSecond);

    /// <summary>
    /// The ratio of a rate against the drawn full-scale rate, whatever the mode: the
    /// same rate gives the same number for either direction, which is what makes the
    /// two rows comparable.
    /// </summary>
    public double SharedRatio(double bytesPerSecond) => Ratio(bytesPerSecond, ScaleBytesPerSecond);

    /// <summary>
    /// The ratio to draw for a row. With <see cref="SharedScale"/> on this is the shared
    /// reading, so the rows compare; off, it falls back to that row's own peak.
    /// </summary>
    public double BarRatio(bool isDownload, double bytesPerSecond) =>
        SharedScale
            ? SharedRatio(bytesPerSecond)
            : Ratio(bytesPerSecond, isDownload ? DownloadPeak : UploadPeak);

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
