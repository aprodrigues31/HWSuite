using HWSuite.Core.Collection;
using HWSuite.Core.Sensors;
using HWSuite.Providers.Lhm;
using HWSuite.Providers.Common;
using HWSuite.Providers.Windows;

namespace HWSuite.Hosting;

/// <summary>
/// Provider order matters: firmware first (sockets, slots), then CPUID refines the processor,
/// then devices. Unsupported providers are skipped by the collector.
/// </summary>
public static class ProviderCatalog
{
    /// <summary>Processor name from CPUID alone (fast), for benchmark results.</summary>
    public static string? ProcessorName() =>
        new InventoryCollector([new CpuidProvider()]).Collect().Processors.FirstOrDefault()?.Name?.Trim();

    public static IReadOnlyList<IInventoryProvider> Default() =>
    [
        new WindowsOsProvider(),
        new RuntimeOsProvider(),
        new WindowsSmbiosProvider(),
        new LinuxSmbiosProvider(),
        new CpuidProvider(),
        new DxgiGpuProvider(),
        new WindowsNpuProvider(),
        new WindowsStorageProvider(),
        new LhmSpdProvider(), // after SMBIOS: attaches SPD details to the modules it listed
    ];

    /// <summary>
    /// Live sensor source; the single place to swap or combine sensor back-ends. Board profiles name
    /// and scale motherboard inputs before min/max are tracked; <paramref name="raw"/> skips them,
    /// to compare the chip's own numbered inputs with the BIOS when writing a new profile.
    /// </summary>
    public static ISensorSource CreateSensorSource(AccessStatus access, bool raw = false)
    {
        ISensorSource source = new LhmSensorSource(access);
        if (OperatingSystem.IsWindows())
        {
            source = new CompositeSensorSource(source, new AdlTelemetrySource(raw));
        }

        if (!raw)
        {
            source = new BoardProfileSource(source);
        }

        return new MinMaxTrackingSource(source);
    }
}
