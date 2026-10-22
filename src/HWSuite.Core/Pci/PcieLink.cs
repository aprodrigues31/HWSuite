using System.Globalization;

namespace HWSuite.Core.Pci;

/// <summary>
/// A PCI Express link: what the device supports (<see cref="MaxSpeed"/>, <see cref="MaxWidth"/>) and
/// what it negotiated (<see cref="CurrentSpeed"/>, <see cref="CurrentWidth"/>). Speeds use the link
/// status register encoding: 1 = 2.5 GT/s (PCIe 1.x), 2 = 5, 3 = 8, 4 = 16, 5 = 32, 6 = 64 GT/s.
/// </summary>
public sealed record PcieLink(int CurrentSpeed, int CurrentWidth, int MaxSpeed, int MaxWidth)
{
    public static double? GigaTransfers(int speed) => speed switch
    {
        1 => 2.5,
        2 => 5,
        3 => 8,
        4 => 16,
        5 => 32,
        6 => 64,
        _ => null,
    };

    /// <summary>"4.0" for 16 GT/s; null when unknown.</summary>
    public static string? Generation(int speed) => speed switch
    {
        1 => "1.1",
        >= 2 and <= 6 => string.Create(CultureInfo.InvariantCulture, $"{speed}.0"),
        _ => null,
    };

    /// <summary>"PCIe 4.0 x16", or null if speed or width is unknown.</summary>
    public static string? Describe(int speed, int width) =>
        Generation(speed) is { } gen && width > 0 ? $"PCIe {gen} x{width}" : null;

    public bool RunsBelowMaxSpeed => CurrentSpeed > 0 && MaxSpeed > 0 && CurrentSpeed < MaxSpeed;

    public bool RunsBelowMaxWidth => CurrentWidth > 0 && MaxWidth > 0 && CurrentWidth < MaxWidth;
}

/// <summary>PCI device types reported by Windows (DEVPKEY_PciDevice_DeviceType, pciprop.h).</summary>
public enum PciDeviceType
{
    Conventional = 0,
    PciX = 1,
    ExpressEndpoint = 2,
    ExpressLegacyEndpoint = 3,
    ExpressRootComplexIntegratedEndpoint = 4,
    ExpressTreatedAsPci = 5,
    BridgeConventional = 6,
    BridgePciX = 7,
    BridgeExpressRootPort = 8,
    BridgeExpressUpstreamSwitchPort = 9,
    BridgeExpressDownstreamSwitchPort = 10,
}

/// <summary>One device on the path from a card up to the CPU: type, PCI vendor and link (if reported).</summary>
public sealed record PcieNode(PciDeviceType? Type, ushort? VendorId, PcieLink? Link);

public static class PcieTopology
{
    /// <summary>
    /// The link between the card and the motherboard slot. Recent GPUs (AMD RDNA 2/3, for example)
    /// carry a PCIe switch from the same vendor on the card, so the GPU itself only reports the
    /// internal link to that switch; the slot link is the one of the topmost of those switch ports.
    /// Switches from another vendor (a chipset) are motherboard-side and end the climb.
    /// <paramref name="chain"/> starts at the device and goes up through its parents.
    /// </summary>
    public static PcieLink? SlotLink(IReadOnlyList<PcieNode> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            return null;
        }

        var vendor = chain[0].VendorId;
        var top = 0;
        for (var i = 1; i < chain.Count; i++)
        {
            var node = chain[i];
            var cardSwitch = node.Type is PciDeviceType.BridgeExpressUpstreamSwitchPort or PciDeviceType.BridgeExpressDownstreamSwitchPort
                && vendor is not null && node.VendorId == vendor;
            if (!cardSwitch)
            {
                break;
            }

            top = i;
        }

        return chain[top].Link ?? chain[0].Link;
    }
}
