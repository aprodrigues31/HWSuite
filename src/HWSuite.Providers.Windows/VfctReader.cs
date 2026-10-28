using System.Runtime.Versioning;
using HWSuite.Core.Gpu;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Reads the ACPI VFCT table (the AMD video BIOS copied by UEFI firmware) through GetSystemFirmwareTable,
/// which needs no driver and no administrator rights. Empty when the firmware did not publish one
/// (NVIDIA or Intel card, legacy/CSM boot, or the AMD card is not the boot display).
/// </summary>
[SupportedOSPlatform("windows")]
public static class VfctReader
{
    // The table id is the signature as it sits in memory; both byte orders are tried, as tools disagree.
    private static readonly uint[] TableIds = [0x54434656, 0x56464354];

    public static IReadOnlyList<VfctImage> Read()
    {
        foreach (var id in TableIds)
        {
            try
            {
                var images = VideoBios.ParseVfct(Native.ReadFirmwareTable(Native.AcpiSignature, id));
                if (images.Count > 0)
                {
                    return images;
                }
            }
            catch (InvalidOperationException)
            {
                // table not present under this id
            }
        }

        return [];
    }
}
