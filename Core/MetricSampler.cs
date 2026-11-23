namespace HASNetMonitor.Core;

/// <summary>
/// Runs one metric's read on its own background thread, on its own cadence, and holds
/// the latest value. The sample loop only ever reads <see cref="Latest"/>, which is a
/// cached value and never blocks: a metric whose read is slow - a GPU PDH sweep, a disk
/// query on a spun-down drive - simply keeps its previous value until its own thread
/// finishes, while every other metric keeps updating on time.
///
/// This is the whole point of the class. Sampling every metric in one call made the
/// slowest metric set the pace for all of them: one stalled read froze every chip. Each
/// metric now has its own thread, so no metric can hold back another.
/// </summary>
/// <typeparam name="T">The reading type. <paramref name="unavailable"/> is the value
/// reported before the first read completes and after a failed one.</typeparam>
internal sealed class MetricSampler<T> : IDisposable
{
    private readonly Func<T> _read;
    private readonly T _unavailable;
    private readonly int _intervalMs;
    private readonly object _gate = new();

    private T _latest;
    private bool _hasValue;
    private Thread? _worker;
    private bool _stop;
    private bool _disposed;

    /// <param name="read">The read to run on the background thread. Must not throw.</param>
    /// <param name="unavailable">Reported until the first read completes.</param>
    /// <param name="intervalMs">How long to wait between reads.</param>
    public MetricSampler(Func<T> read, T unavailable, int intervalMs)
    {
        _read = read;
        _unavailable = unavailable;
        _intervalMs = Math.Max(1, intervalMs);
        _latest = unavailable;
    }

    /// <summary>
    /// The most recent value, or <c>unavailable</c> before the first read completes.
    /// Never blocks and never runs the read: the caller gets whatever the background
    /// thread last produced.
    /// </summary>
    public T Latest
    {
        get
        {
            lock (_gate)
            {
                return _hasValue ? _latest : _unavailable;
            }
        }
    }

    /// <summary>Whether a read has completed at least once.</summary>
    public bool HasValue
    {
        get
        {
            lock (_gate)
            {
                return _hasValue;
            }
        }
    }

    /// <summary>Starts the background thread. Safe to call more than once.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _worker is not null)
            {
                return;
            }

            _worker = new Thread(Loop)
            {
                IsBackground = true,
                Name = $"HASNetMonitor.Metric.{typeof(T).Name}",
            };
            _worker.Start();
        }
    }

    private void Loop()
    {
        while (true)
        {
            T value;
            try
            {
                value = _read();
            }
            catch (Exception)
            {
                // A metric that throws must not kill its thread: the previous value
                // stays, and the next tick tries again.
                value = _unavailable;
            }

            lock (_gate)
            {
                if (_stop)
                {
                    return;
                }

                _latest = value;
                _hasValue = true;
            }

            // Sleep in short slices so Dispose is prompt even with a long interval.
            int slept = 0;
            while (slept < _intervalMs)
            {
                lock (_gate)
                {
                    if (_stop)
                    {
                        return;
                    }
                }

                int slice = Math.Min(50, _intervalMs - slept);
                Thread.Sleep(slice);
                slept += slice;
            }
        }
    }

    public void Dispose()
    {
        Thread? worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stop = true;
            worker = _worker;
            _worker = null;
        }

        if (worker is not null && worker.IsAlive)
        {
            worker.Join(TimeSpan.FromSeconds(2));
        }
    }
}
