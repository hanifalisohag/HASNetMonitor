namespace HASNetMonitor.Core;

/// <summary>
/// Turns a cumulative byte counter into a rate. Handles the two situations that
/// make naive rate code misbehave: the counter going backwards (adapter reset,
/// interface added or removed) and a zero or negative sample interval.
/// </summary>
internal sealed class RateCalculator
{
    private readonly double _smoothing;
    private long _previous;
    private bool _hasPrevious;
    private double _smoothed;
    private bool _hasSmoothed;

    public RateCalculator(double smoothingFactor = 0.45)
    {
        if (smoothingFactor <= 0 || smoothingFactor > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(smoothingFactor),
                smoothingFactor,
                "Smoothing factor must be in the range (0, 1].");
        }

        _smoothing = smoothingFactor;
    }

    public double RawBytesPerSecond { get; private set; }

    public double SmoothedBytesPerSecond => _smoothed;

    /// <summary>"Forgets" the smoothing history, e.g. after a config change.</summary>
    public void Reset()
    {
        _hasSmoothed = false;
        _smoothed = 0;
        RawBytesPerSecond = 0;
    }

    /// <summary>Seeds the baseline counter without producing a bogus first rate.</summary>
    public void Seed(long cumulativeBytes)
    {
        _previous = cumulativeBytes;
        _hasPrevious = true;
        RawBytesPerSecond = 0;
    }

    public double Update(long cumulativeBytes, double elapsedSeconds)
    {
        if (!_hasPrevious)
        {
            Seed(cumulativeBytes);
            RawBytesPerSecond = 0;
            return RawBytesPerSecond;
        }

        long delta = cumulativeBytes - _previous;
        _previous = cumulativeBytes;

        if (!(elapsedSeconds > 0) || delta < 0)
        {
            // A backwards counter means the underlying adapter set changed.
            // Report no traffic rather than a negative or absurd spike.
            RawBytesPerSecond = 0;
            _smoothed = _hasSmoothed ? _smoothed : 0;
            return RawBytesPerSecond;
        }

        RawBytesPerSecond = delta / elapsedSeconds;
        if (_hasSmoothed)
        {
            _smoothed += (RawBytesPerSecond - _smoothed) * _smoothing;
        }
        else
        {
            _smoothed = RawBytesPerSecond;
            _hasSmoothed = true;
        }

        return RawBytesPerSecond;
    }
}
