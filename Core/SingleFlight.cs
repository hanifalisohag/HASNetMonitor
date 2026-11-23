namespace HASNetMonitor.Core;

/// <summary>
/// Runs at most one operation at a time. A periodic timer fires its callback every
/// interval whether or not the previous callback finished, so a slow operation would
/// otherwise pile callbacks up on the thread pool until the UI thread starves. A tick
/// that arrives while an operation is running is skipped, not queued: the next tick
/// carries a fresh reading anyway.
/// </summary>
internal sealed class SingleFlight
{
    private int _running;

    /// <summary>Whether an operation is running right now.</summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// Runs <paramref name="operation"/> unless another call is already running, in which
    /// case it is skipped and <c>false</c> is returned. The flag is always cleared, even
    /// when the operation throws, so one failure cannot wedge every later tick.
    /// </summary>
    public bool TryRun(Action operation)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            operation();
            return true;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
