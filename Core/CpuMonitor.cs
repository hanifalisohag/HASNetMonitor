using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// Total processor load, straight from kernel32's <c>GetSystemTimes</c>: the deltas of
/// idle time against the total time across the two calls. No polling abstraction and
/// no extra library - the same counters perfmon reads. An EMA smooths the reading so a
/// one-second burst cannot slam the widget's figure around.
/// </summary>
internal sealed class CpuMonitor
{
    private bool _primed;
    private long _lastIdle;
    private long _lastKernel;
    private long _lastUser;
    private double _smoothed;
    private bool _hasReal;

    /// <summary>The fraction of the reading kept from the previous sample (EMA).</summary>
    internal const double SmoothingFactor = 0.65;

    /// <summary>
    /// The smoothed percent in use (0-100), or NaN when the counters cannot be read.
    /// The first call primes the baseline and always reports NaN: there is no
    /// meaningful instantaneous value from one counter read.
    /// </summary>
    public double ReadPercent()
    {
        if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            return double.NaN;
        }

        if (!_primed)
        {
            _primed = true;
            _lastIdle = idle;
            _lastKernel = kernel;
            _lastUser = user;
            return double.NaN;
        }

        long idleDelta = idle - _lastIdle;
        long totalDelta = (kernel - _lastKernel) + (user - _lastUser);
        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;

        if (totalDelta <= 0)
        {
            return _hasReal ? _smoothed : double.NaN;
        }

        double instant = 100.0 * (1.0 - (double)idleDelta / (double)totalDelta);
        if (!Numeric.IsFinite(instant))
        {
            return _hasReal ? _smoothed : double.NaN;
        }

        instant = Numeric.Clamp(instant, 0.0, 100.0);
        if (!_hasReal)
        {
            _smoothed = instant;
            _hasReal = true;
            return _smoothed;
        }

        _smoothed = (_smoothed * SmoothingFactor) + (instant * (1.0 - SmoothingFactor));
        return _smoothed;
    }

    /// <summary>Drops both the baseline and the smoothed value.</summary>
    public void Reset()
    {
        _primed = false;
        _lastIdle = 0;
        _lastKernel = 0;
        _lastUser = 0;
        _smoothed = 0;
        _hasReal = false;
    }
}
