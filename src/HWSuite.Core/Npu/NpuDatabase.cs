using System.Globalization;
using System.Text.RegularExpressions;
using HWSuite.Core.Model;

namespace HWSuite.Core.Npu;

/// <summary>
/// NPU generations by PCI id (pci.ids) and the peak TOPS (INT8) their makers quote. One AMD id covers
/// Ryzen 7040 (10 TOPS) and 8040 (16 TOPS), so the processor name settles it. Qualcomm's Hexagon NPU is
/// not a PCI device; it is recognized by name.
/// </summary>
public static partial class NpuDatabase
{
    private sealed record Family(string Name, double? Tops);

    private static readonly Dictionary<(ushort Vendor, ushort Device), Family> ByPciId = new()
    {
        [(0x1022, 0x1502)] = new("AMD XDNA (Phoenix / Hawk Point)", null),
        [(0x1022, 0x17F0)] = new("AMD XDNA 2 (Strix / Krackan / Strix Halo)", 50),
        [(0x8086, 0x7D1D)] = new("Intel NPU 3 (Meteor Lake / Arrow Lake-H)", 11),
        [(0x8086, 0xAD1D)] = new("Intel NPU 3 (Arrow Lake)", 13),
        [(0x8086, 0x643E)] = new("Intel NPU 4 (Lunar Lake)", 48),
        [(0x8086, 0xB03E)] = new("Intel NPU 5 (Panther Lake)", 50),
        [(0x8086, 0xD71D)] = new("Intel NPU (Core Ultra Series 4)", null),
    };

    /// <summary>Fills <see cref="NpuInfo.Family"/> and <see cref="NpuInfo.Tops"/> from the PCI id or the name.</summary>
    public static void Identify(NpuInfo npu, string? processorName)
    {
        ArgumentNullException.ThrowIfNull(npu);
        if (Hex(npu.PciVendorId) is { } vendor && Hex(npu.PciDeviceId) is { } device && ByPciId.TryGetValue((vendor, device), out var family))
        {
            npu.Family = family.Name;
            npu.Tops = family.Tops ?? (vendor == 0x1022 && device == 0x1502 ? PhoenixTops(processorName) : null);
            return;
        }

        if (npu.Name?.Contains("Hexagon", StringComparison.OrdinalIgnoreCase) == true || processorName?.Contains("Snapdragon(R) X", StringComparison.OrdinalIgnoreCase) == true)
        {
            npu.Family = "Qualcomm Hexagon (Snapdragon X)";
            npu.Tops = 45;
        }
    }

    /// <summary>Ryzen 8040 (Hawk Point) quotes 16 TOPS for the same NPU the 7040 (Phoenix) quotes 10 for.</summary>
    private static double? PhoenixTops(string? processor) => processor is null ? null
        : Series8040().IsMatch(processor) ? 16
        : Series7040().IsMatch(processor) ? 10
        : null;

    private static ushort? Hex(string? text) =>
        ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;

    [GeneratedRegex(@"Ryzen\S*\s+[3579]\s+(PRO\s+)?8\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex Series8040();

    [GeneratedRegex(@"Ryzen\S*\s+[3579]\s+(PRO\s+)?7\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex Series7040();
}
