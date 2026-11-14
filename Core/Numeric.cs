namespace TaskbarNetMonitor.Core;

/// <summary>
/// The small numeric helpers the app shares. They exist instead of
/// <see cref="Math.Clamp"/> and <c>double.IsFinite</c> so the same source builds for
/// both the modern <c>net11.0-windows</c> target and the <c>net48</c> target that ships
/// as a single small exe on a stock Windows install.
/// </summary>
internal static class Numeric
{
    public static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    public static long Clamp(long value, long min, long max) =>
        value < min ? min : value > max ? max : value;

    public static float Clamp(float value, float min, float max) =>
        value < min ? min : value > max ? max : value;

    public static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    public static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>
    /// True for a real number: not NaN and not either infinity. Matches
    /// <c>double.IsFinite</c>, which .NET Framework does not have.
    /// </summary>
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
