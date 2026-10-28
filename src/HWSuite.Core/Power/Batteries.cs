using System.Buffers.Binary;
using System.Text;
using HWSuite.Core.Model;

namespace HWSuite.Core.Power;

/// <summary>
/// Decodes the Windows battery driver structures (batclass.h): BATTERY_INFORMATION and BATTERY_STATUS,
/// read with IOCTL_BATTERY_QUERY_INFORMATION / IOCTL_BATTERY_QUERY_STATUS. A UPS on USB appears here too,
/// flagged as a short-term battery.
/// </summary>
public static class Batteries
{
    public const uint CapabilitySystemBattery = 0x80000000;
    public const uint CapabilityRelative = 0x40000000; // capacities are percentages, not mWh
    public const uint CapabilityShortTerm = 0x20000000; // a UPS

    public const uint PowerOnLine = 0x1;
    public const uint PowerDischarging = 0x2;
    public const uint PowerCharging = 0x4;
    public const uint PowerCritical = 0x8;

    private const uint Unknown = 0xFFFFFFFF;

    /// <summary>BATTERY_INFORMATION (36 bytes): capabilities, technology, chemistry, capacities, cycle count.</summary>
    public static void ApplyInformation(BatteryInfo battery, ReadOnlySpan<byte> info)
    {
        ArgumentNullException.ThrowIfNull(battery);
        if (info.Length < 36)
        {
            return;
        }

        var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(info);
        battery.Relative = (capabilities & CapabilityRelative) != 0;
        battery.IsUps = (capabilities & CapabilityShortTerm) != 0;
        battery.Rechargeable = info[4] == 1;
        battery.Chemistry = Chemistry(Encoding.ASCII.GetString(info.Slice(8, 4)).TrimEnd('\0', ' '));
        battery.DesignCapacity = Value(BinaryPrimitives.ReadUInt32LittleEndian(info[12..]));
        battery.FullChargeCapacity = Value(BinaryPrimitives.ReadUInt32LittleEndian(info[16..]));
        battery.CycleCount = Value(BinaryPrimitives.ReadUInt32LittleEndian(info[32..])) is { } cycles and > 0 ? (int)cycles : null;
    }

    /// <summary>BATTERY_STATUS (16 bytes): power state, remaining capacity, voltage (mV), rate (mW, negative when discharging).</summary>
    public static void ApplyStatus(BatteryInfo battery, ReadOnlySpan<byte> status)
    {
        ArgumentNullException.ThrowIfNull(battery);
        if (status.Length < 16)
        {
            return;
        }

        var state = BinaryPrimitives.ReadUInt32LittleEndian(status);
        battery.OnLine = (state & PowerOnLine) != 0;
        battery.Charging = (state & PowerCharging) != 0;
        battery.Discharging = (state & PowerDischarging) != 0;
        battery.Critical = (state & PowerCritical) != 0;
        battery.RemainingCapacity = Value(BinaryPrimitives.ReadUInt32LittleEndian(status[4..]));
        battery.VoltageMv = Value(BinaryPrimitives.ReadUInt32LittleEndian(status[8..]));
        var rate = BinaryPrimitives.ReadInt32LittleEndian(status[12..]);
        battery.RateMw = rate is int.MinValue or 0 ? null : rate; // BATTERY_UNKNOWN_RATE = 0x80000000
    }

    /// <summary>Charge in percent: remaining over full-charge capacity (or the value itself for relative batteries).</summary>
    public static double? ChargePercent(BatteryInfo battery)
    {
        ArgumentNullException.ThrowIfNull(battery);
        if (battery.RemainingCapacity is not { } remaining)
        {
            return null;
        }

        if (battery.Relative)
        {
            return Math.Min(100, remaining);
        }

        return battery.FullChargeCapacity is { } full and > 0 ? Math.Min(100, remaining * 100.0 / full) : null;
    }

    /// <summary>Capacity left compared with new (full-charge over design capacity), in percent; null for relative batteries.</summary>
    public static double? HealthPercent(BatteryInfo battery)
    {
        ArgumentNullException.ThrowIfNull(battery);
        return !battery.Relative && battery.DesignCapacity is { } design and > 0 && battery.FullChargeCapacity is { } full and > 0
            ? Math.Min(100, full * 100.0 / design)
            : null;
    }

    /// <summary>BatteryEstimatedTime is seconds, BATTERY_UNKNOWN_TIME (0xFFFFFFFF) when it cannot tell (on AC, for example).</summary>
    public static TimeSpan? EstimatedTime(uint seconds) => seconds is Unknown or 0 or > 7 * 24 * 3600 ? null : TimeSpan.FromSeconds(seconds);

    /// <summary>BatteryTemperature is tenths of a kelvin.</summary>
    public static double? Celsius(uint tenthsKelvin) => tenthsKelvin is 0 or Unknown ? null : Math.Round((tenthsKelvin / 10.0) - 273.15, 1);

    /// <summary>BATTERY_MANUFACTURE_DATE { Day; Month; Year } as "yyyy-MM-dd".</summary>
    public static string? ManufactureDate(ReadOnlySpan<byte> date)
    {
        if (date.Length < 4)
        {
            return null;
        }

        var (day, month, year) = (date[0], date[1], BinaryPrimitives.ReadUInt16LittleEndian(date[2..]));
        return day is >= 1 and <= 31 && month is >= 1 and <= 12 && year is >= 1990 and <= 2100 ? $"{year:0000}-{month:00}-{day:00}" : null;
    }

    /// <summary>"LION" -> "Li-ion", following the chemistry codes in batclass.h.</summary>
    public static string? Chemistry(string code) => code.ToUpperInvariant() switch
    {
        "" => null,
        "PBAC" => "Lead acid",
        "LION" or "LI-I" => "Li-ion",
        "LIP" or "LIPO" => "Li-polymer",
        "NICD" => "NiCd",
        "NIMH" => "NiMH",
        "NIZN" => "NiZn",
        "RAM" => "Rechargeable alkaline-manganese",
        _ => code,
    };

    private static uint? Value(uint raw) => raw == Unknown ? null : raw;
}
