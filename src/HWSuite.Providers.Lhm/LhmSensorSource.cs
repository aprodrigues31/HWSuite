using HWSuite.Core.Model;
using HWSuite.Core.Sensors;
using HWSuite.Providers.Windows;
using LibreHardwareMonitor.Hardware;

namespace HWSuite.Providers.Lhm;

/// <summary>
/// Live sensors from LibreHardwareMonitorLib: CPU (temperatures, clocks, power), motherboard
/// Super I/O (voltages, fans), memory, GPUs (NVIDIA/AMD/Intel APIs), storage, PSUs and batteries.
/// CPU and Super I/O readings need administrator rights plus the PawnIO driver; GPU and storage
/// readings work without them.
/// </summary>
public sealed class LhmSensorSource : ISensorSource
{
    private readonly Computer _computer;
    private readonly TimeProvider _time;
    private bool _disposed;

    public LhmSensorSource(AccessStatus access, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        _time = time ?? TimeProvider.System;
        Diagnostics = AccessDiagnostics(access);

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsBatteryEnabled = true,
            IsPsuEnabled = true,
            IsNetworkEnabled = false,
        };
        LhmRuntime.Open(_computer);
    }

    public string Name => "LibreHardwareMonitor";

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public SensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var readings = new List<SensorReading>();
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            Collect(hardware, parent: null, readings);
            foreach (var sub in hardware.SubHardware)
            {
                sub.Update();
                Collect(sub, hardware.Name, readings);
            }
        }

        return new SensorSnapshot(_time.GetLocalNow(), readings);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LhmRuntime.Close(_computer);
    }

    private static void Collect(IHardware hardware, string? parent, List<SensorReading> readings)
    {
        foreach (var sensor in hardware.Sensors)
        {
            readings.Add(new SensorReading(
                Id: sensor.Identifier.ToString(),
                Hardware: hardware.Name,
                HardwareKind: HardwareKindName(hardware.HardwareType.ToString()),
                Name: sensor.Name,
                Kind: MapKind(sensor.SensorType.ToString()),
                Value: sensor.Value,
                Min: sensor.Min,
                Max: sensor.Max)
            {
                Parent = parent,
            });
        }
    }

    /// <summary>Maps by name so new LibreHardwareMonitor sensor types degrade to "Other" instead of breaking.</summary>
    internal static SensorKind MapKind(string sensorType) =>
        Enum.TryParse<SensorKind>(sensorType, ignoreCase: false, out var kind) ? kind : SensorKind.Other;

    internal static string HardwareKindName(string hardwareType) => hardwareType switch
    {
        "Cpu" => "CPU",
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => "GPU",
        "SuperIO" => "Super I/O",
        "EmbeddedController" => "EC",
        "Psu" => "PSU",
        _ => hardwareType,
    };

    internal static IReadOnlyList<Diagnostic> AccessDiagnostics(AccessStatus access)
    {
        const string Source = "Sensors";
        var notes = new List<Diagnostic>();
        if (!access.IsElevated)
        {
            notes.Add(new Diagnostic(Source, DiagnosticSeverity.Warning,
                "not running as administrator: CPU, motherboard (Super I/O) and memory sensors are limited"));
        }

        if (!access.PawnIoInstalled)
        {
            notes.Add(new Diagnostic(Source, DiagnosticSeverity.Warning,
                $"PawnIO driver not found: CPU and motherboard sensors need it ({AccessStatus.PawnIoInstallCommand})"));
        }

        return notes;
    }
}
