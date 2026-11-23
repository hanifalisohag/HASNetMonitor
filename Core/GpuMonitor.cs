using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// GPU utilization, first through the performance-counter API and then, when the
/// counters do not resolve, through the NVIDIA Management Library directly. Windows
/// exposes GPU counters only for the "GPU Engine" object, and the instance name is
/// not documented as a constant, so the counter sweep keeps the first path that
/// parses, collects and returns sane data. That does not work on every machine - some
/// NVIDIA drivers expose no GPU Engine counters at all - so failing the sweep means
/// trying <c>nvml.dll</c>, the library the NVIDIA driver ships for exactly this. When
/// neither path resolves - a machine with no display adapter, an unusual driver, a
/// virtualised environment - the chip hides instead of showing a confident-looking
/// lie.
/// </summary>
internal sealed class GpuMonitor
{
    /// <summary>
    /// Candidate English counter paths, in order. "engtype_3D" is the instance name
    /// Windows uses for the graphics pipeline; the rest are harmless fallbacks.
    /// </summary>
    private static readonly string[] Candidates =
    [
        "\\GPU Engine(engtype_3D)\\Utilization Percentage",
        "\\GPU Engine(engtype_3D)\\% Utilization",
        "\\GPU Engine(*)\\Utilization Percentage",
        "\\GPU Engine(*)\\% Utilization",
    ];

    /// <summary>
    /// The percent the busiest engine reports (0-100), or null when unavailable.
    /// </summary>
    public double? ReadPercent()
    {
        // The path that worked most recently is tried first: once a counter is found,
        // the other three are never opened at all, which keeps every tick cheap and
        // avoids the four-query sweep that a cold start pays once.
        if (_workingPath is not null && TryRead(_workingPath, out double cached))
        {
            return cached;
        }

        _workingPath = null;
        foreach (string path in Candidates)
        {
            if (TryRead(path, out double percent))
            {
                _workingPath = path;
                return percent;
            }
        }

        return TryReadNvml();
    }

    /// <summary>The counter path that produced the last good reading, if any.</summary>
    private string? _workingPath;

    private static bool _nvmlChecked;
    private static bool _nvmlAvailable;

    private static double? TryReadNvml()
    {
        if (!EnsureNvml())
        {
            return null;
        }

        try
        {
            if (NativeMethods.nvmlDeviceGetCountV2(out uint deviceCount) != 0 || deviceCount == 0)
            {
                return null;
            }

            if (NativeMethods.nvmlDeviceGetHandleByIndexV2(0, out IntPtr device) != 0 || device == IntPtr.Zero)
            {
                return null;
            }

            var rates = new NativeMethods.NvmlUtilizationData();
            if (NativeMethods.nvmlDeviceGetUtilizationRates(device, out rates) != 0)
            {
                return null;
            }

            // NVML_VALUE_NOT_AVAILABLE is -1, read here as 0xFFFFFFFF; anything above
            // 100 is a broken reading, not a zero percent.
            if (rates.Gpu == 0xFFFFFFFFu || rates.Gpu > 100u)
            {
                return null;
            }

            return Numeric.Clamp(rates.Gpu, 0.0, 100.0);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException or TypeLoadException)
        {
            return null;
        }
    }

    private static bool EnsureNvml()
    {
        if (_nvmlChecked)
        {
            return _nvmlAvailable;
        }

        _nvmlChecked = true;
        try
        {
            _nvmlAvailable = NativeMethods.nvmlInitV2() == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException or TypeLoadException)
        {
            _nvmlAvailable = false;
        }

        return _nvmlAvailable;
    }

    private static bool TryRead(string path, out double percent)
    {
        percent = 0.0;
        IntPtr query = IntPtr.Zero;

        try
        {
            if (NativeMethods.PdhOpenQueryW(null, 0, out query) != 0 || query == IntPtr.Zero)
            {
                return false;
            }

            if (NativeMethods.PdhAddEnglishCounterW(query, path, 0, out IntPtr counter) != 0 || counter == IntPtr.Zero)
            {
                return false;
            }

            if (NativeMethods.PdhCollectQueryData(query) != 0)
            {
                return false;
            }

            var value = new NativeMethods.PdhFmtCounterValue();
            if (NativeMethods.PdhGetFormattedCounterValue(counter, NativeMethods.PdhFmtDouble, IntPtr.Zero, ref value) != 0)
            {
                return false;
            }

            // The struct carries the counter status; a nonzero CStatus is "no data".
            if (value.CStatus != 0)
            {
                return false;
            }

            double raw = value.DoubleValue;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < -10.0)
            {
                return false;
            }

            // NOCAP100 means the raw value is uncapped, so a busiest-engine reading
            // slightly above 100 is a real reading, not an error: clamp it rather
            // than discard it and blank the chip.
            percent = Numeric.Clamp(raw, 0.0, 100.0);
            return true;
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or System.ComponentModel.Win32Exception or TypeLoadException)
        {
            return false;
        }
        finally
        {
            if (query != IntPtr.Zero)
            {
                NativeMethods.PdhCloseQuery(query);
            }
        }
    }
}
