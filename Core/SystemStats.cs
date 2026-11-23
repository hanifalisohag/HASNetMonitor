using System.Drawing;
using System.Globalization;

namespace HASNetMonitor.Core;

/// <summary>
/// Everything the system can tell the widget, captured in one place so a frame draws
/// from a single consistent snapshot instead of four separate reads. Disabled or
/// unavailable metrics are marked as such (NaN / null / -1 / Unavailable) and the
/// widget simply does not paint them.
/// </summary>
internal readonly record struct SystemStatsReading(
    double CpuPercent,
    double RamPercent,
    double? GpuPercent,
    long DiskFreeBytes,
    DiskMonitor.DiskReading Disk,
    BatteryMonitor.BatteryReading Battery)
{
    public static SystemStatsReading Empty { get; } = new(
        double.NaN,
        double.NaN,
        null,
        -1,
        DiskMonitor.DiskReading.Unavailable,
        BatteryMonitor.BatteryReading.Unavailable);

    public bool AnyMetricEnabled(MonitorConfig config) =>
        config.ShowCpu || config.ShowGpu || config.ShowRam || config.ShowDisk || config.ShowBattery;
}

/// <summary>One chip in the statistics block: a dot colour and the text beside it.</summary>
internal readonly record struct StatsItem(Color Dot, string Text);

/// <summary>
/// Reads the system's load on every call. The widget owns one instance and calls
/// <see cref="Sample"/> every tick, so the chips advance at the same rate as the traffic
/// figures rather than on a slower, built-in cadence. The sampling is deliberately
/// cheap and exception-proof, so a broken counter can never take the UI thread down.
/// </summary>
internal sealed class SystemStats
{
    private readonly CpuMonitor _cpu = new();
    private readonly MemoryMonitor _memory = new();
    private readonly GpuMonitor _gpu = new();
    private readonly DiskMonitor _disk = new();
    private readonly BatteryMonitor _battery = new();

    // The last known-good reading for each metric. A single failed read must not blank
    // a chip the user is already looking at, so the last good value is held - briefly.
    // Past the grace the metric is treated as gone and the chip disappears. A metric
    // that has never produced a reading stays absent.
    private const double HoldGraceSeconds = 5.0;
    private static readonly TimeSpan HoldGrace = TimeSpan.FromSeconds(HoldGraceSeconds);

    private double _cpuLastGood = double.NaN;
    private long _cpuLastGoodTicks;
    private double _ramLastGood = double.NaN;
    private long _ramLastGoodTicks;
    private double? _gpuLastGood;
    private long _gpuLastGoodTicks;
    private DiskMonitor.DiskReading _diskLastGood = DiskMonitor.DiskReading.Unavailable;
    private long _diskLastGoodTicks;

    /// <summary>
    /// The latest reading, taken fresh on every call. Cheap to call every tick; the
    /// values advance at the same rate as the figures, which is what the user sees.
    /// </summary>
    public SystemStatsReading Sample(MonitorConfig config)
    {
        long now = Elapsed.Now();

        double cpu = double.NaN;
        if (config.ShowCpu)
        {
            cpu = HoldLastGood(_cpu.ReadPercent(), ref _cpuLastGood, ref _cpuLastGoodTicks, now, HoldGrace);
        }

        double ramPercent = double.NaN;
        if (config.ShowRam)
        {
            MemoryMonitor.MemoryReading memory = _memory.Read();
            double fresh = memory.IsKnown ? memory.Percent : double.NaN;
            ramPercent = HoldLastGood(fresh, ref _ramLastGood, ref _ramLastGoodTicks, now, HoldGrace);
        }

        double? gpu = null;
        if (config.ShowGpu)
        {
            gpu = HoldLastGood(_gpu.ReadPercent(), ref _gpuLastGood, ref _gpuLastGoodTicks, now, HoldGrace);
        }

        DiskMonitor.DiskReading disk = DiskMonitor.DiskReading.Unavailable;
        if (config.ShowDisk)
        {
            disk = HoldLastGood(_disk.Read(), ref _diskLastGood, ref _diskLastGoodTicks, now, HoldGrace);
        }

        // Battery is never held: it shows only while the machine is on battery power, and
        // is hidden the moment it is plugged in - a stale battery chip would be worse
        // than none.
        BatteryMonitor.BatteryReading battery = config.ShowBattery
            ? _battery.Read()
            : BatteryMonitor.BatteryReading.Unavailable;

        return new SystemStatsReading(
            cpu,
            ramPercent,
            gpu,
            disk.IsKnown ? disk.FreeBytes : -1,
            disk,
            battery);
    }

    /// <summary>
    /// The last good value survives a transient failure for <see cref="HoldGrace"/>: NaN /
    /// Infinity mean "unavailable" and return the held value while it is fresh enough,
    /// anything else is a reading and refreshes the hold. Past the grace the metric is
    /// treated as gone and nothing is returned - never a confident-looking stale value.
    /// </summary>
    internal static double HoldLastGood(double fresh, ref double lastGood, ref long lastGoodTicks, long now, TimeSpan grace)
    {
        if (!double.IsNaN(fresh) && !double.IsInfinity(fresh))
        {
            lastGood = fresh;
            lastGoodTicks = now;
            return fresh;
        }

        if (lastGoodTicks != 0L && Elapsed.Since(lastGoodTicks) < grace)
        {
            return lastGood;
        }

        return double.NaN;
    }

    /// <summary><see cref="HoldLastGood(double, ref double, ref long, long, TimeSpan)"/> for a nullable percent.</summary>
    internal static double? HoldLastGood(double? fresh, ref double? lastGood, ref long lastGoodTicks, long now, TimeSpan grace)
    {
        if (fresh is double value && !double.IsNaN(value) && !double.IsInfinity(value))
        {
            lastGood = value;
            lastGoodTicks = now;
            return value;
        }

        if (lastGoodTicks != 0L && Elapsed.Since(lastGoodTicks) < grace)
        {
            return lastGood;
        }

        return null;
    }

    /// <summary><see cref="HoldLastGood(double, ref double, ref long, long, TimeSpan)"/> for a disk reading.</summary>
    internal static DiskMonitor.DiskReading HoldLastGood(DiskMonitor.DiskReading fresh, ref DiskMonitor.DiskReading lastGood, ref long lastGoodTicks, long now, TimeSpan grace)
    {
        if (fresh.IsKnown)
        {
            lastGood = fresh;
            lastGoodTicks = now;
            return fresh;
        }

        if (lastGoodTicks != 0L && Elapsed.Since(lastGoodTicks) < grace)
        {
            return lastGood;
        }

        return DiskMonitor.DiskReading.Unavailable;
    }

    /// <summary>Drops all accumulated state - the CPU baseline and every held reading - so the next sample starts fresh.</summary>
    public void Reset()
    {
        _cpu.Reset();
        _cpuLastGood = double.NaN;
        _cpuLastGoodTicks = 0L;
        _ramLastGood = double.NaN;
        _ramLastGoodTicks = 0L;
        _gpuLastGood = null;
        _gpuLastGoodTicks = 0L;
        _diskLastGood = DiskMonitor.DiskReading.Unavailable;
        _diskLastGoodTicks = 0L;
    }

    /// <summary>The accent colours the chips are drawn with, keyed by metric.</summary>
    internal static class StatsColors
    {
        public static Color Cpu => Color.FromArgb(0xFF, 0xB1, 0x3C);
        public static Color Ram => Color.FromArgb(0x3F, 0xC1, 0xC9);
        public static Color Gpu => Color.FromArgb(0xC7, 0x7D, 0xFF);
        public static Color Disk => Color.FromArgb(0xA8, 0xB2, 0xC1);
        public static Color BatteryLow => Color.FromArgb(0xF8, 0x71, 0x71);
        public static Color BatteryCharging => Color.FromArgb(0x4A, 0xDE, 0x80);
    }

    /// <summary>
    /// The chips to draw, in reading order. Metrics that are disabled, unavailable or
    /// hidden by the caller are simply absent - never rendered as a placeholder.
    /// </summary>
    public static StatsItem[] BuildItems(SystemStatsReading reading, MonitorConfig config)
    {
        var items = new List<StatsItem>(5);

        if (config.ShowCpu && !double.IsNaN(reading.CpuPercent))
        {
            items.Add(new StatsItem(StatsColors.Cpu, $"C: {FormatPercent(reading.CpuPercent)}"));
        }

        if (config.ShowRam && !double.IsNaN(reading.RamPercent))
        {
            items.Add(new StatsItem(StatsColors.Ram, $"R: {FormatPercent(reading.RamPercent)}"));
        }

        if (config.ShowGpu && reading.GpuPercent is double gpu)
        {
            items.Add(new StatsItem(StatsColors.Gpu, $"G: {FormatPercent(gpu)}"));
        }

        if (config.ShowBattery && reading.Battery.IsKnown && reading.Battery.OnBattery)
        {
            Color batteryDot = reading.Battery.Charging || reading.Battery.Percent >= 20
                ? StatsColors.BatteryCharging
                : StatsColors.BatteryLow;
            items.Add(new StatsItem(batteryDot, $"B: {reading.Battery.Percent}%"));
        }

        if (config.ShowDisk && reading.Disk.IsKnown && reading.Disk.TotalBytes > 0)
        {
            long used = Math.Max(0, reading.Disk.TotalBytes - reading.Disk.FreeBytes);
            double usage = 100.0 * used / reading.Disk.TotalBytes;
            items.Add(new StatsItem(StatsColors.Disk, $"D: {FormatPercent(usage)}"));
        }

        return items.ToArray();
    }

    /// <summary>The joined text line ("C: 42% · R: 71%") used for signatures and tooltips.</summary>
    public static string JoinedText(SystemStatsReading reading, MonitorConfig config)
    {
        var items = BuildItems(reading, config);
        if (items.Length == 0)
        {
            return string.Empty;
        }

        return string.Join("  ·  ", items.Select(item => item.Text));
    }

    public static string FormatPercent(double percent)
        => $"{Math.Round(Numeric.Clamp(percent, 0.0, 100.0), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}%";
}





