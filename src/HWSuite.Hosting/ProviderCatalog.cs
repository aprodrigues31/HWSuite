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
    public static IReadOnlyList<IInventoryProvider> Default() =>
    [
        new WindowsOsProvider(),
        new RuntimeOsProvider(),
        new WindowsSmbiosProvider(),
        new LinuxSmbiosProvider(),
        new CpuidProvider(),
        new DxgiGpuProvider(),
        new WindowsStorageProvider(),
    ];

    /// <summary>Live sensor source; the single place to swap or combine sensor back-ends.</summary>
    public static ISensorSource CreateSensorSource(AccessStatus access) => new MinMaxTrackingSource(new LhmSensorSource(access));
}
