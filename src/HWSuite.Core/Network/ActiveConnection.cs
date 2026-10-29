using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HWSuite.Core.Network;

/// <summary>
/// Finds the adapter the operating system uses to reach the Internet: a UDP socket "connected" to a public
/// address (no packet is sent) gets the local address of the default route, and the adapter holding that
/// address is the one the test goes through.
/// </summary>
public static class ActiveConnection
{
    private static readonly IPAddress ProbeV4 = IPAddress.Parse("1.1.1.1");
    private static readonly IPAddress ProbeV6 = IPAddress.Parse("2606:4700:4700::1111");

    /// <param name="family">IP version of the test.</param>
    /// <param name="wifi">Wi-Fi link details for an adapter id (Windows: the interface GUID), or null.</param>
    public static NetConnection? Find(IpFamily family, Func<string, WifiLink?>? wifi = null)
    {
        IPAddress? local;
        try
        {
            using var socket = new Socket(family == IpFamily.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(family == IpFamily.IPv6 ? ProbeV6 : ProbeV4, 443);
            local = (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            local = null;
        }

        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return null;
        }

        var adapter = local is null ? null : adapters.FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(local)));
        adapter ??= adapters.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && n.GetIPProperties().GatewayAddresses.Count > 0);
        if (adapter is null)
        {
            return null;
        }

        var isWifi = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        var isEthernet = adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;
        double? link = adapter.Speed > 0 ? adapter.Speed / 1e6 : null;
        return new NetConnection(adapter.Name, adapter.Description, isWifi, isEthernet, link, isWifi ? wifi?.Invoke(adapter.Id) : null);
    }
}

/// <summary>Wi-Fi numbers: channel from frequency, band, and the 802.11 standard of a PHY type.</summary>
public static class WifiMath
{
    /// <summary>Band in GHz (2.4, 5 or 6) of a center frequency in MHz.</summary>
    public static double? Band(int mhz) => mhz switch
    {
        >= 2400 and < 2500 => 2.4,
        >= 4900 and < 5925 => 5,
        >= 5925 and < 7200 => 6,
        _ => null,
    };

    /// <summary>802.11 channel number of a center frequency in MHz.</summary>
    public static int? Channel(int mhz) => mhz switch
    {
        2484 => 14,
        >= 2412 and < 2484 => (mhz - 2407) / 5,
        >= 4900 and < 5925 => (mhz - 5000) / 5,
        >= 5925 and < 7200 => (mhz - 5950) / 5,
        _ => null,
    };

    /// <summary>DOT11_PHY_TYPE (wlantypes.h) as the standard's name and its Wi-Fi Alliance generation.</summary>
    public static string? Standard(int phyType) => phyType switch
    {
        4 => "802.11a",
        5 => "802.11b",
        6 => "802.11g",
        7 => "802.11n (Wi-Fi 4)",
        8 => "802.11ac (Wi-Fi 5)",
        9 => "802.11ad",
        10 => "802.11ax (Wi-Fi 6)",
        11 => "802.11be (Wi-Fi 7)",
        _ => null,
    };

    /// <summary>Signal quality (0–100%) to an approximate RSSI, as Windows maps it linearly between −100 and −50 dBm.</summary>
    public static int RssiFromQuality(int percent) => (Math.Clamp(percent, 0, 100) / 2) - 100;
}
