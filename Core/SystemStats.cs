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

/// <summary>
/// One chip in the statistics block: a dot colour, the text beside it, the widest text
/// this chip can ever show, and the metric's level as a 0..1 ratio for the level
/// indicators. The column is measured against <see cref="MeasureText"/> rather than
/// <see cref="Text"/> so a value that comes and goes - or a placeholder - cannot change
/// the widget's width, which would force a new surface every tick.
/// </summary>
internal readonly record struct StatsItem(Color Dot, string Text, string MeasureText, double Ratio = 0.0, string Metric = ChipMetrics.Cpu);

/// <summary>
/// The system's load, read from four independent background samplers. The widget owns
/// one instance and calls <see cref="Sample"/> every tick; that call only reads the
/// cached value each sampler last produced, so it never blocks and never runs a read.
///
/// Each metric has its own thread and its own cadence. A slow read - a GPU PDH sweep,
/// a disk query on a spun-down drive - delays only that metric: the others keep
/// advancing on time, and the slow one keeps its previous value until its own read
/// finishes. Sampling every metric in one call made the slowest metric set the pace for
/// all of them, which is exactly the freeze this replaces.
/// </summary>
internal sealed class SystemStats : IDisposable
{
    /// <summary>How often each metric's own thread refreshes it.</summary>
    private const int CpuIntervalMs = 1000;
    private const int RamIntervalMs = 1000;
    private const int GpuIntervalMs = 1000;
    private const int DiskIntervalMs = 2000;
    private const int BatteryIntervalMs = 5000;

    private readonly CpuMonitor _cpu = new();
    private readonly MemoryMonitor _memory = new();
    private readonly GpuMonitor _gpu = new();
    private readonly DiskMonitor _disk = new();
    private readonly BatteryMonitor _battery = new();

    private readonly MetricSampler<double> _cpuSampler;
    private readonly MetricSampler<MemoryMonitor.MemoryReading> _ramSampler;
    private readonly MetricSampler<double?> _gpuSampler;
    private readonly MetricSampler<DiskMonitor.DiskReading> _diskSampler;
    private readonly MetricSampler<BatteryMonitor.BatteryReading> _batterySampler;

    // The last known-good reading for each metric. A single failed read must not blank
    // a chip the user is already looking at, so the last good value is held - briefly.
    // Past the grace the metric is treated as gone and the chip shows its placeholder.
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

    public SystemStats()
    {
        _cpuSampler = new MetricSampler<double>(_cpu.ReadPercent, double.NaN, CpuIntervalMs);
        _ramSampler = new MetricSampler<MemoryMonitor.MemoryReading>(_memory.Read, MemoryMonitor.MemoryReading.Unavailable, RamIntervalMs);
        _gpuSampler = new MetricSampler<double?>(_gpu.ReadPercent, null, GpuIntervalMs);
        _diskSampler = new MetricSampler<DiskMonitor.DiskReading>(_disk.Read, DiskMonitor.DiskReading.Unavailable, DiskIntervalMs);
        _batterySampler = new MetricSampler<BatteryMonitor.BatteryReading>(_battery.Read, BatteryMonitor.BatteryReading.Unavailable, BatteryIntervalMs);
    }

    /// <summary>Starts every metric's background thread. Safe to call more than once.</summary>
    public void Start()
    {
        _cpuSampler.Start();
        _ramSampler.Start();
        _gpuSampler.Start();
        _diskSampler.Start();
        _batterySampler.Start();
    }

    /// <summary>
    /// The latest reading. Every value comes from a cached sampler, so this call never
    /// blocks and never runs a read: a metric whose own read is slow keeps its previous
    /// value while the others advance.
    /// </summary>
    public SystemStatsReading Sample(MonitorConfig config)
    {
        // A caller that never called Start still gets values: the first Sample starts the
        // samplers. Start is idempotent, so the widget's explicit call is harmless.
        Start();

        long now = Elapsed.Now();

        double cpu = double.NaN;
        if (config.ShowCpu)
        {
            cpu = HoldLastGood(_cpuSampler.Latest, ref _cpuLastGood, ref _cpuLastGoodTicks, now, HoldGrace);
        }

        double ramPercent = double.NaN;
        if (config.ShowRam)
        {
            MemoryMonitor.MemoryReading memory = _ramSampler.Latest;
            double fresh = memory.IsKnown ? memory.Percent : double.NaN;
            ramPercent = HoldLastGood(fresh, ref _ramLastGood, ref _ramLastGoodTicks, now, HoldGrace);
        }

        double? gpu = null;
        if (config.ShowGpu)
        {
            gpu = HoldLastGood(_gpuSampler.Latest, ref _gpuLastGood, ref _gpuLastGoodTicks, now, HoldGrace);
        }

        DiskMonitor.DiskReading disk = DiskMonitor.DiskReading.Unavailable;
        if (config.ShowDisk)
        {
            disk = HoldLastGood(_diskSampler.Latest, ref _diskLastGood, ref _diskLastGoodTicks, now, HoldGrace);
        }

        // Battery is never held: it shows only while the machine is on battery power, and
        // is hidden the moment it is plugged in - a stale battery chip would be worse
        // than none.
        BatteryMonitor.BatteryReading battery = config.ShowBattery
            ? _batterySampler.Latest
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

    /// <summary>Stops every metric's background thread and releases the PDH query behind the GPU path.</summary>
    public void Dispose()
    {
        _cpuSampler.Dispose();
        _ramSampler.Dispose();
        _gpuSampler.Dispose();
        _diskSampler.Dispose();
        _batterySampler.Dispose();
        _gpu.Dispose();
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
    /// The text shown for an enabled metric that has no reading yet. It is deliberately
    /// not a number: the chip keeps its slot so the widget's size never changes when a
    /// reading comes and goes, and the user sees that the metric is enabled but has no
    /// value rather than a confident-looking zero.
    /// </summary>
    public const string Placeholder = "--";

    /// <summary>
    /// The chips to draw, in reading order. An enabled metric always gets a chip: when it
    /// has no reading the chip shows <see cref="Placeholder"/> instead of dropping out, so
    /// a metric that flickers between readable and unreadable cannot resize the widget on
    /// every tick. A disabled metric is absent. The battery is the one exception - it is
    /// hidden on AC power, which is a deliberate "nothing to show", not a missing value.
    /// </summary>
    public static StatsItem[] BuildItems(SystemStatsReading reading, MonitorConfig config)
    {
        var items = new List<StatsItem>(5);

        if (config.ShowCpu)
        {
            double ratio = double.IsNaN(reading.CpuPercent) ? 0.0 : Numeric.Clamp(reading.CpuPercent, 0.0, 100.0) / 100.0;
            items.Add(new StatsItem(
                config.ChipColor(ChipMetrics.Cpu),
                double.IsNaN(reading.CpuPercent) ? $"C: {Placeholder}" : $"C: {FormatPercent(reading.CpuPercent)}",
                "C: 100%",
                ratio,
                ChipMetrics.Cpu));
        }

        if (config.ShowRam)
        {
            double ratio = double.IsNaN(reading.RamPercent) ? 0.0 : Numeric.Clamp(reading.RamPercent, 0.0, 100.0) / 100.0;
            items.Add(new StatsItem(
                config.ChipColor(ChipMetrics.Ram),
                double.IsNaN(reading.RamPercent) ? $"R: {Placeholder}" : $"R: {FormatPercent(reading.RamPercent)}",
                "R: 100%",
                ratio,
                ChipMetrics.Ram));
        }

        if (config.ShowGpu)
        {
            double ratio = reading.GpuPercent is double gpu ? Numeric.Clamp(gpu, 0.0, 100.0) / 100.0 : 0.0;
            items.Add(new StatsItem(
                config.ChipColor(ChipMetrics.Gpu),
                reading.GpuPercent is double gpuValue ? $"G: {FormatPercent(gpuValue)}" : $"G: {Placeholder}",
                "G: 100%",
                ratio,
                ChipMetrics.Gpu));
        }

        if (config.ShowBattery && reading.Battery.IsKnown && reading.Battery.OnBattery)
        {
            // The battery keeps its low/charging palette colours unless the user picked one,
            // because the colour is the warning here, not decoration.
            Color batteryDot = config.ChipColorHex(ChipMetrics.Battery).Length > 0
                ? config.ChipColor(ChipMetrics.Battery)
                : reading.Battery.Charging || reading.Battery.Percent >= 20
                    ? StatsColors.BatteryCharging
                    : StatsColors.BatteryLow;
            items.Add(new StatsItem(
                batteryDot,
                $"B: {reading.Battery.Percent}%",
                "B: 100%",
                Numeric.Clamp(reading.Battery.Percent, 0, 100) / 100.0,
                ChipMetrics.Battery));
        }

        if (config.ShowDisk)
        {
            if (reading.Disk.IsKnown && reading.Disk.TotalBytes > 0)
            {
                long used = Math.Max(0, reading.Disk.TotalBytes - reading.Disk.FreeBytes);
                double usage = 100.0 * used / reading.Disk.TotalBytes;
                items.Add(new StatsItem(
                    config.ChipColor(ChipMetrics.Disk),
                    $"D: {FormatPercent(usage)}",
                    "D: 100%",
                    Numeric.Clamp(usage, 0.0, 100.0) / 100.0,
                    ChipMetrics.Disk));
            }
            else
            {
                items.Add(new StatsItem(config.ChipColor(ChipMetrics.Disk), $"D: {Placeholder}", "D: 100%", 0.0, ChipMetrics.Disk));
            }
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





