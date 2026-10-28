namespace HWSuite.Core.Model;

/// <summary>
/// Snapshot of everything HWSuite could identify on a machine. Providers fill it in
/// incrementally; any property may stay null when no source could supply it.
/// </summary>
public sealed class HardwareInventory
{
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>HWSuite version that produced this inventory, e.g. "0.2.0 (build 12, a3b8c29)".</summary>
    public string? ToolVersion { get; set; }
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public string? MachineName { get; set; }
    public OperatingSystemInfo? OperatingSystem { get; set; }
    public SystemInfo? System { get; set; }
    public MotherboardInfo? Motherboard { get; set; }
    public BiosInfo? Bios { get; set; }
    public List<ProcessorInfo> Processors { get; } = [];
    public MemoryInfo? Memory { get; set; }
    public List<GpuInfo> Gpus { get; } = [];
    public List<NpuInfo> Npus { get; } = [];
    public List<BatteryInfo> Batteries { get; } = [];
    public List<DiskInfo> Disks { get; } = [];

    /// <summary>What each provider reported about itself: skipped, partial data, failures.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];
}

public sealed class OperatingSystemInfo
{
    public string? Name { get; set; }
    public string? DisplayVersion { get; set; }
    public string? Build { get; set; }
    public string? Architecture { get; set; }
    public string? Description { get; set; }
}

public sealed class SystemInfo
{
    public string? Manufacturer { get; set; }
    public string? Product { get; set; }
    public string? Version { get; set; }
    public string? Family { get; set; }
    public string? Sku { get; set; }
    public string? SerialNumber { get; set; }
    public string? Uuid { get; set; }
    public string? ChassisType { get; set; }
}

public sealed class MotherboardInfo
{
    public string? Manufacturer { get; set; }
    public string? Product { get; set; }
    public string? Version { get; set; }
    public string? SerialNumber { get; set; }
}

public sealed class BiosInfo
{
    public string? Vendor { get; set; }
    public string? Version { get; set; }
    public string? ReleaseDate { get; set; }
    public string? SmbiosVersion { get; set; }
}

public sealed class ProcessorInfo
{
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public string? Socket { get; set; }
    public int? Family { get; set; }
    public int? Model { get; set; }
    public int? Stepping { get; set; }

    /// <summary>Raw CPUID leaf 1 EAX, e.g. "0x000906A3".</summary>
    public string? Signature { get; set; }
    public int? Cores { get; set; }
    public int? Threads { get; set; }
    public int? BusClockMhz { get; set; }
    public int? MaxClockMhz { get; set; }
    public int? CurrentClockMhz { get; set; }
    public bool? HypervisorPresent { get; set; }

    /// <summary>Hypervisor name, e.g. "Microsoft Hyper-V", "VMware".</summary>
    public string? HypervisorVendor { get; set; }

    /// <summary>
    /// True: the physical machine runs Windows on top of Hyper-V (VBS, memory integrity, WSL2).
    /// False with <see cref="HypervisorPresent"/>: this is a virtual machine.
    /// </summary>
    public bool? HypervisorRootPartition { get; set; }
    public List<string> InstructionSets { get; } = [];
    public List<CacheInfo> Caches { get; } = [];
}

public enum CacheType
{
    Data,
    Instruction,
    Unified,
}

public sealed class CacheInfo
{
    public int Level { get; set; }
    public CacheType Type { get; set; }
    public long SizeBytes { get; set; }
    public int Ways { get; set; }
    public int LineSizeBytes { get; set; }

    /// <summary>Logical processors sharing one instance of this cache.</summary>
    public int SharedByThreads { get; set; }
}

public sealed class MemoryInfo
{
    public long? TotalInstalledBytes { get; set; }
    public int? SlotCount { get; set; }
    public long? MaxCapacityBytes { get; set; }
    public List<MemoryModule> Modules { get; } = [];
}

public sealed class MemoryModule
{
    public string? Slot { get; set; }
    public string? Bank { get; set; }
    public long SizeBytes { get; set; }
    public string? Type { get; set; }
    public string? FormFactor { get; set; }
    public int? SpeedMts { get; set; }
    public int? ConfiguredSpeedMts { get; set; }
    public int? Ranks { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? SerialNumber { get; set; }

    /// <summary>Details read from the module's own SPD chip (needs administrator rights and PawnIO).</summary>
    public SpdInfo? Spd { get; set; }
}

/// <summary>Timings of one SPD profile (JEDEC or XMP), in clock cycles at that profile's speed.</summary>
public sealed record SpdTimingProfile(string Name, int SpeedMts, int Cl, int Trcd, int Trp, int Tras, int? Trc, double? VoltageV);

/// <summary>What a memory module's SPD EEPROM says about itself.</summary>
public sealed class SpdInfo
{
    public string MemoryType { get; set; } = "";
    public string? SpdRevision { get; set; }
    public string? ModuleType { get; set; }
    public string? ModuleManufacturer { get; set; }
    public string? DramManufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? SerialNumber { get; set; }
    public int? ManufactureYear { get; set; }
    public int? ManufactureWeek { get; set; }
    public long? SizeBytes { get; set; }
    public int? Ranks { get; set; }
    public int? DeviceWidth { get; set; }
    public long? DieDensityBits { get; set; }
    public bool Ecc { get; set; }
    public bool HasThermalSensor { get; set; }
    public SpdTimingProfile? Jedec { get; set; }
    public string? XmpVersion { get; set; }
    public List<SpdTimingProfile> XmpProfiles { get; } = [];

    /// <summary>AMD EXPO version ("1.0") when the DDR5 module carries EXPO profiles.</summary>
    public string? ExpoVersion { get; set; }

    public List<SpdTimingProfile> ExpoProfiles { get; } = [];
}

public sealed class GpuInfo
{
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public string? PciVendorId { get; set; }
    public string? PciDeviceId { get; set; }
    public string? PciSubsystemId { get; set; }
    public int? Revision { get; set; }
    public long? DedicatedMemoryBytes { get; set; }
    public long? SharedMemoryBytes { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? BiosVersion { get; set; }

    /// <summary>Link to the motherboard slot (through the card's own PCIe switch, if it has one).</summary>
    public Pci.PcieLink? PcieLink { get; set; }

    /// <summary>Manufacturer reference specifications for this model, when HWSuite knows it.</summary>
    public Gpu.GpuSpec? Reference { get; set; }

    /// <summary>Company that built the card (ASRock, ASUS...), from the PCI subsystem vendor.</summary>
    public string? Subvendor { get; set; }

    /// <summary>Driver package the user knows, e.g. "AMD Adrenalin 26.10.1" or "NVIDIA 560.94".</summary>
    public string? DriverPackage { get; set; }

    /// <summary>Highest Direct3D 12 feature level, e.g. "12_2".</summary>
    public string? Direct3DFeatureLevel { get; set; }

    /// <summary>Highest HLSL shader model, e.g. "6.8".</summary>
    public string? ShaderModel { get; set; }

    /// <summary>DirectX Raytracing tier ("1.1"), or null when not supported.</summary>
    public string? RaytracingTier { get; set; }

    /// <summary>Compute and graphics APIs the installed driver provides (OpenCL, CUDA, Vulkan...).</summary>
    public List<string> Apis { get; } = [];

    /// <summary>Largest PCI memory window (BAR) Windows assigned to the card; with Resizable BAR it covers the VRAM.</summary>
    public long? LargestBarBytes { get; set; }

    /// <summary>Video memory chip maker (Samsung, SK Hynix, Micron), when the driver reports it.</summary>
    public string? MemoryVendor { get; set; }

    /// <summary>The card's own default maximum GPU clock in MHz (AMD Overdrive 8), factory overclock included.</summary>
    public int? CardGpuClockMhz { get; set; }

    /// <summary>The card's own default memory clock in MHz (AMD Overdrive 8).</summary>
    public int? CardMemoryClockMhz { get; set; }

    /// <summary>A copy of the card's video BIOS the firmware published (AMD, ACPI VFCT), for saving.</summary>
    public Gpu.VideoBiosCopy? VideoBios { get; set; }

    /// <summary>Counterfeit check against the model the name claims (<see cref="Gpu.GpuAuthenticity"/>).</summary>
    public Gpu.AuthenticityResult? Authenticity { get; set; }
}

/// <summary>
/// A battery Windows knows about: a laptop's, or a UPS connected by USB (<see cref="IsUps"/>). Capacities are
/// mWh, or percentages when <see cref="Relative"/> (common on UPSes).
/// </summary>
public sealed class BatteryInfo
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? SerialNumber { get; set; }
    public string? ManufactureDate { get; set; }
    public string? Chemistry { get; set; }
    public bool Rechargeable { get; set; }
    public bool IsUps { get; set; }
    public bool Relative { get; set; }
    public uint? DesignCapacity { get; set; }
    public uint? FullChargeCapacity { get; set; }
    public uint? RemainingCapacity { get; set; }
    public int? CycleCount { get; set; }
    public uint? VoltageMv { get; set; }

    /// <summary>mW: positive while charging, negative while discharging.</summary>
    public int? RateMw { get; set; }

    public bool OnLine { get; set; }
    public bool Charging { get; set; }
    public bool Discharging { get; set; }
    public bool Critical { get; set; }
    public TimeSpan? EstimatedTime { get; set; }
    public double? TemperatureCelsius { get; set; }
}

/// <summary>A neural processing unit (Windows "Neural processors" device class).</summary>
public sealed class NpuInfo
{
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public string? PciVendorId { get; set; }
    public string? PciDeviceId { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }

    /// <summary>NPU generation, e.g. "AMD XDNA 2 (Strix / Krackan / Strix Halo)".</summary>
    public string? Family { get; set; }

    /// <summary>Peak INT8 TOPS the maker quotes for this NPU (and processor series), when known.</summary>
    public double? Tops { get; set; }
}

public sealed class DiskInfo
{
    public int Index { get; set; }
    public string? Model { get; set; }
    public string? Vendor { get; set; }
    public string? Firmware { get; set; }
    public string? SerialNumber { get; set; }
    public string? BusType { get; set; }
    public string? MediaType { get; set; }
    public long? SizeBytes { get; set; }
    public bool? Removable { get; set; }
    public DiskHealth? Health { get; set; }
}

public enum DiskHealthStatus
{
    Good,
    Caution,
    Bad,
}

/// <summary>Disk health: from the NVMe SMART / Health log page (02h) or the ATA S.M.A.R.T. attributes.</summary>
public sealed class DiskHealth
{
    /// <summary>Overall verdict, CrystalDiskInfo style: Good, Caution (damaged sectors, wear) or Bad.</summary>
    public DiskHealthStatus? Assessment { get; set; }

    /// <summary>ATA drives only: every attribute the drive reports.</summary>
    public IReadOnlyList<Storage.SmartEntry>? SmartAttributes { get; set; }

    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }

    public int? TemperatureCelsius { get; set; }
    public int? AvailableSparePercent { get; set; }
    public int? PercentageUsed { get; set; }
    public long? PowerOnHours { get; set; }
    public long? PowerCycles { get; set; }
    public long? UnsafeShutdowns { get; set; }
    public long? MediaErrors { get; set; }
    public long? DataReadBytes { get; set; }
    public long? DataWrittenBytes { get; set; }
    public int? CriticalWarning { get; set; }
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record Diagnostic(string Source, DiagnosticSeverity Severity, string Message);
