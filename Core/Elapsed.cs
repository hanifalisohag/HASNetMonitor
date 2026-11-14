using System.Diagnostics;

namespace HASNetMonitor.Core;

/// <summary>
/// Time since a <see cref="Stopwatch.GetTimestamp"/> reading. This wraps
/// <c>Stopwatch.GetElapsedTime</c>, which .NET Framework does not have, so the widget's
/// timing source is the same on both targets.
/// </summary>
internal static class Elapsed
{
    public static TimeSpan Since(long timestamp)
    {
#if NETFRAMEWORK
        return Compat.GetElapsedTime(timestamp);
#else
        return Stopwatch.GetElapsedTime(timestamp);
#endif
    }

    /// <summary>The current <see cref="Stopwatch"/> reading, for the next <see cref="Since"/>.</summary>
    public static long Now() => Stopwatch.GetTimestamp();
}
