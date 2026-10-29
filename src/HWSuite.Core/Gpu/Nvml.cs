namespace HWSuite.Core.Gpu;

/// <summary>Why an NVIDIA card is running below its maximum clock (NVML clock event / throttle reasons).</summary>
[Flags]
public enum NvidiaClockLimit : ulong
{
    None = 0,
    Idle = 0x1,
    ApplicationClocks = 0x2,
    PowerCap = 0x4,
    HardwareSlowdown = 0x8,
    SyncBoost = 0x10,
    ThermalSoftware = 0x20,
    ThermalHardware = 0x40,
    PowerBrake = 0x80,
    DisplayClocks = 0x100,
}

/// <summary>Decoding of NVIDIA Management Library (nvml.dll, installed with the driver) values.</summary>
public static class Nvml
{
    /// <summary>The limits shown as sensors ("Limit: ..." at 100 while active, 0 otherwise), in display order.</summary>
    public static readonly IReadOnlyList<(NvidiaClockLimit Flag, string Name)> LimitSensors =
    [
        (NvidiaClockLimit.PowerCap, "Limit: Power"),
        (NvidiaClockLimit.ThermalSoftware, "Limit: Thermal"),
        (NvidiaClockLimit.ThermalHardware, "Limit: Thermal (HW)"),
        (NvidiaClockLimit.HardwareSlowdown, "Limit: HW Slowdown"),
        (NvidiaClockLimit.PowerBrake, "Limit: Power Brake"),
    ];

    /// <summary>Limits that mean a problem when they act on a card at rest (cooling, power cables, power supply).</summary>
    public const NvidiaClockLimit Alarming = NvidiaClockLimit.ThermalHardware | NvidiaClockLimit.HardwareSlowdown | NvidiaClockLimit.PowerBrake;

    /// <summary>nvmlDeviceArchitecture_t.</summary>
    public static string? Architecture(uint code) => code switch
    {
        2 => "Kepler",
        3 => "Maxwell",
        4 => "Pascal",
        5 => "Volta",
        6 => "Turing",
        7 => "Ampere",
        8 => "Ada Lovelace",
        9 => "Hopper",
        10 => "Blackwell",
        _ => null,
    };

    /// <summary>nvmlSystemGetCudaDriverVersion: 12060 -> "CUDA 12.6".</summary>
    public static string? CudaVersion(int version) =>
        version > 0 ? $"CUDA {version / 1000}.{version % 1000 / 10}" : null;

    /// <summary>pciDeviceId packs the device id in the high 16 bits and the vendor in the low ones.</summary>
    public static (string Vendor, string Device) PciIds(uint pciDeviceId) =>
        ((pciDeviceId & 0xFFFF).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
         (pciDeviceId >> 16).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
}
