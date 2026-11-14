namespace TaskbarNetMonitor.Core;

/// <summary>
/// Keeps a bar from jumping. The auto-scaled ratio follows the peak, and after a
/// burst the peak collapses within a single tick, which makes the bar slam from full
/// width to a sliver and back again on every burst — that reads as flicker.
///
/// Rising values pass straight through (a burst must be visible at once); falling
/// values ease out towards the new target, so the bar shrinks smoothly instead.
/// </summary>
internal sealed class BarSmoother
{
    /// <summary>Time constant for the way down, in seconds.</summary>
    public const double ReleaseSeconds = 1.6;

    /// <summary>Below this change the bar is left exactly as it is.</summary>
    public const double DeadBand = 0.004;

    private double _current;

    public double Current => _current;

    public void Reset()
    {
        _current = 0;
    }

    /// <summary>
    /// Feeds one target ratio and returns the ratio to draw. <paramref name="elapsedSeconds"/>
    /// is the time since the previous call; non-positive or garbage values leave the
    /// value untouched, so a stall cannot make the bar jump.
    /// </summary>
    public double Update(double target, double elapsedSeconds)
    {
        if (double.IsNaN(target) || double.IsInfinity(target))
        {
            target = 0;
        }

        target = Numeric.Clamp(target, 0, 1);

        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds <= 0)
        {
            return _current;
        }

        if (double.IsNaN(_current) || double.IsInfinity(_current))
        {
            _current = 0;
        }

        double delta = target - _current;

        // Rising: show it now. A burst that appears late looks broken.
        if (delta > 0)
        {
            _current = target;
            return _current;
        }

        if (Math.Abs(delta) <= DeadBand)
        {
            // Small changes are left alone: a bar that twitches by a pixel a second
            // is exactly the flicker this exists to prevent.
            return _current;
        }

        double decay = Math.Exp(-elapsedSeconds / ReleaseSeconds);
        _current = target + (_current - target) * decay;
        return Numeric.Clamp(_current, 0, 1);
    }
}
