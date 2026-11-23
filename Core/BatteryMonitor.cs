using System.Runtime.InteropServices;
using HASNetMonitor.Interop;

namespace HASNetMonitor.Core;

/// <summary>
/// Battery state, straight from kernel32's <c>GetSystemPowerStatus</c>: on battery or
/// on AC, the charge percentage and whether it is charging. Unknown is reported, never
/// guessed: 255 is the SDK's "unknown" marker, not 100 percent.
/// </summary>
internal sealed class BatteryMonitor
{
    /// <summary>The reading: <see cref="OnBattery"/> false when on AC — unknown is -1.</summary>
    internal readonly record struct BatteryReading(bool OnBattery, int Percent, bool Charging)
    {
        public bool IsKnown => Percent >= 0 && Percent <= 100;

        public static BatteryReading Unavailable { get; } = new(false, -1, false);
    }

    /// <summary>AC line status: offline.</summary>
    private const byte AcOffline = 0;

    /// <summary>No system battery present (a desktop, most servers).</summary>
    private const byte FlagNoBattery = 0x80;

    /// <summary>Charging flag.</summary>
    private const byte FlagCharging = 0x08;

    /// <summary>The SDK's "unknown" marker for battery percent.</summary>
    private const byte PercentUnknown = 0xFF;

    public BatteryReading Read()
    {
        try
        {
            if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.NativePowerStatus status))
            {
                return BatteryReading.Unavailable;
            }

            // On AC power there is no battery worth showing and none to read; the caller
            // hides the chip entirely in that case, which is what the clock does.
            if (status.ACLineStatus == 0x01)
            {
                return BatteryReading.Unavailable;
            }

            // Anything other than a known offline reading is not evidence of a battery
            // worth showing, so the chip hides rather than guess.
            if (status.ACLineStatus != AcOffline)
            {
                return BatteryReading.Unavailable;
            }

            bool hasFlag = (status.BatteryFlag & FlagNoBattery) == FlagNoBattery;
            if (hasFlag)
            {
                return BatteryReading.Unavailable;
            }

            if (status.BatteryLifePercent == PercentUnknown)
            {
                return BatteryReading.Unavailable;
            }

            int percent = (int)status.BatteryLifePercent;
            bool charging = (status.BatteryFlag & FlagCharging) == FlagCharging;
            return new BatteryReading(OnBattery: true, percent, charging);
        }
        catch (Exception ex) when (ex is MarshalDirectiveException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return BatteryReading.Unavailable;
        }
    }
}
