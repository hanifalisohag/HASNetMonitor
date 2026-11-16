#if NETFRAMEWORK
// The test sources use Math.Clamp, which .NET Framework's System.Math does not have.
// The project aliases `Math` to this type for the net48 target, so the same test source
// compiles unchanged for both targets. Every member delegates to System.Math; only
// Clamp is new. Members are added here as the tests need them, and a missing one shows
// up as a compile error rather than a silent difference in behaviour.
namespace HASNetMonitor.Tests;

internal static class MathBridge
{
    public const double PI = System.Math.PI;
    public const double E = System.Math.E;

    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    public static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;

    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

    public static decimal Clamp(decimal value, decimal min, decimal max) => value < min ? min : value > max ? max : value;

    public static int Abs(int value) => System.Math.Abs(value);

    public static double Sqrt(double value) => System.Math.Sqrt(value);

    public static long Abs(long value) => System.Math.Abs(value);

    public static double Abs(double value) => System.Math.Abs(value);

    public static float Abs(float value) => System.Math.Abs(value);

    public static decimal Abs(decimal value) => System.Math.Abs(value);

    public static int Max(int a, int b) => System.Math.Max(a, b);

    public static long Max(long a, long b) => System.Math.Max(a, b);

    public static double Max(double a, double b) => System.Math.Max(a, b);

    public static float Max(float a, float b) => System.Math.Max(a, b);

    public static decimal Max(decimal a, decimal b) => System.Math.Max(a, b);

    public static int Min(int a, int b) => System.Math.Min(a, b);

    public static long Min(long a, long b) => System.Math.Min(a, b);

    public static double Min(double a, double b) => System.Math.Min(a, b);

    public static float Min(float a, float b) => System.Math.Min(a, b);

    public static decimal Min(decimal a, decimal b) => System.Math.Min(a, b);

    public static double Pow(double x, double y) => System.Math.Pow(x, y);

    public static double Ceiling(double value) => System.Math.Ceiling(value);

    public static decimal Ceiling(decimal value) => System.Math.Ceiling(value);

    public static double Round(double value) => System.Math.Round(value);

    public static double Round(double value, int digits) => System.Math.Round(value, digits);

    public static double Round(double value, MidpointRounding mode) => System.Math.Round(value, mode);

    public static decimal Round(decimal value) => System.Math.Round(value);

    public static decimal Round(decimal value, int digits) => System.Math.Round(value, digits);
}
#endif
