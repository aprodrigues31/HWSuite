using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HWSuite.Core.Collection;
using HWSuite.Core.Cpuid;
using HWSuite.Core.Model;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Counts performance and efficiency cores on hybrid processors (Intel 12th gen and later) from
/// GetLogicalProcessorInformationEx: each physical core carries the efficiency class Windows schedules by.
/// </summary>
public sealed unsafe partial class CoreTopologyProvider : IInventoryProvider
{
    private const int RelationProcessorCore = 0;

    public string Name => "CPU topology";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!OperatingSystem.IsWindows() || inventory.Processors.Count == 0 || CoreTopology.Split(EfficiencyClasses()) is not { } split)
        {
            return;
        }

        // Windows lists the cores of every package; split them evenly (multi-socket hybrid systems do not exist today).
        var packages = inventory.Processors.Count;
        foreach (var cpu in inventory.Processors)
        {
            cpu.PerformanceCores = split.Performance / packages;
            cpu.EfficiencyCores = split.Efficiency / packages;
        }
    }

    /// <summary>One efficiency class per physical core.</summary>
    [SupportedOSPlatform("windows")]
    internal static IReadOnlyList<byte> EfficiencyClasses()
    {
        uint length = 0;
        _ = GetLogicalProcessorInformationEx(RelationProcessorCore, null, &length);
        if (length == 0)
        {
            return [];
        }

        var buffer = new byte[length];
        fixed (byte* start = buffer)
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, start, &length))
            {
                return [];
            }

            // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship (4), Size (4), then PROCESSOR_RELATIONSHIP:
            // Flags (1), EfficiencyClass (1), Reserved (20), GroupCount (2), GroupMask[].
            var classes = new List<byte>();
            for (uint offset = 0; offset + 10 <= length;)
            {
                var entry = start + offset;
                var relationship = *(int*)entry;
                var size = *(uint*)(entry + 4);
                if (size == 0)
                {
                    break;
                }

                if (relationship == RelationProcessorCore)
                {
                    classes.Add(entry[9]);
                }

                offset += size;
            }

            return classes;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, uint* length);
}
