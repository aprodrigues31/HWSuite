using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace HWSuite.Core.Pci;

/// <summary>PCI identifiers parsed from a Windows hardware id such as "PCI\VEN_10DE&amp;DEV_2684&amp;SUBSYS_16F110DE&amp;REV_A1".</summary>
public sealed record PciHardwareId(ushort VendorId, ushort DeviceId, uint? SubsystemId, byte? Revision);

public static partial class PciIds
{
    public static PciHardwareId? Parse(string? hardwareId)
    {
        if (string.IsNullOrEmpty(hardwareId))
        {
            return null;
        }

        var match = HardwareIdPattern().Match(hardwareId);
        if (!match.Success)
        {
            return null;
        }

        static T? Hex<T>(Group g)
            where T : struct, IBinaryInteger<T> =>
            g.Success ? T.Parse(g.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;

        return new PciHardwareId(
            Hex<ushort>(match.Groups["ven"]) ?? 0,
            Hex<ushort>(match.Groups["dev"]) ?? 0,
            Hex<uint>(match.Groups["subsys"]),
            Hex<byte>(match.Groups["rev"]));
    }

    public static string FormatId(uint id, int digits = 4) =>
        id.ToString("X" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public static string? VendorName(ushort vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x1414 => "Microsoft",
        0x5143 => "Qualcomm",
        0x15AD => "VMware",
        0x80EE => "Oracle VirtualBox",
        0x1234 => "QEMU",
        0x1AF4 => "Red Hat (virtio)",
        0x1A03 => "ASPEED",
        0x102B => "Matrox",
        0x18CA => "XGI",
        0x1D17 => "Zhaoxin",
        0x1ED5 => "Moore Threads",
        _ => null,
    };

    [GeneratedRegex(@"VEN_(?<ven>[0-9A-F]{4})&DEV_(?<dev>[0-9A-F]{4})(?:&SUBSYS_(?<subsys>[0-9A-F]{8}))?(?:&REV_(?<rev>[0-9A-F]{2}))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HardwareIdPattern();

    /// <summary>Short brand of the company that built a card, from its PCI subsystem vendor id (names per pci.ids).</summary>
    public static string? CardMaker(ushort subsystemVendor) => subsystemVendor switch
    {
        0x1849 => "ASRock",
        0x1043 => "ASUS",
        0x1462 => "MSI",
        0x1458 => "Gigabyte",
        0x1DA2 => "Sapphire",
        0x174B => "Sapphire / PC Partner",
        0x148C => "PowerColor",
        0x1682 => "XFX",
        0x3842 => "EVGA",
        0x19DA => "Zotac",
        0x1569 => "Palit",
        0x10B0 => "Gainward",
        0x196E => "PNY",
        0x1B4C => "GALAX",
        0x7377 => "Colorful",
        0x1787 => "HIS",
        0x1028 => "Dell",
        0x103C => "HP",
        0x17AA => "Lenovo",
        0x1025 => "Acer",
        0x1002 => "AMD",
        0x10DE => "NVIDIA",
        0x8086 => "Intel",
        _ => null,
    };
}
