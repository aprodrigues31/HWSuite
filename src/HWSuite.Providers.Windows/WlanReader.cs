using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using HWSuite.Core.Network;

namespace HWSuite.Providers.Windows;

/// <summary>
/// The current Wi-Fi link of an adapter through the WLAN API (wlanapi.dll, no administrator rights): network
/// name, signal quality, PHY type and link rates from WlanQueryInterface(wlan_intf_opcode_current_connection),
/// then RSSI and center frequency of the connected access point from the cached BSS list.
/// </summary>
public static unsafe partial class WlanReader
{
    private const int OpcodeCurrentConnection = 7;
    private const int ConnectionSize = 604; // WLAN_CONNECTION_ATTRIBUTES
    private const int BssEntrySize = 360;   // WLAN_BSS_ENTRY
    private const int BssTypeAny = 3;       // dot11_BSS_type_any (required when no SSID is given)

    /// <summary>The link of the adapter with this id (interface GUID, as NetworkInterface.Id gives it), or null.</summary>
    public static WifiLink? Query(string adapterId)
    {
        if (!OperatingSystem.IsWindows() || !Guid.TryParse(adapterId, out var guid))
        {
            return null;
        }

        try
        {
            if (WlanOpenHandle(2, 0, out _, out var handle) != 0)
            {
                return null;
            }

            try
            {
                if (WlanQueryInterface(handle, &guid, OpcodeCurrentConnection, 0, out var size, out var data, 0) != 0 || data == 0)
                {
                    return null;
                }

                Connection? connection;
                try
                {
                    connection = size >= ConnectionSize ? ParseConnection(new ReadOnlySpan<byte>((void*)data, ConnectionSize)) : null;
                }
                finally
                {
                    WlanFreeMemory(data);
                }

                if (connection is null)
                {
                    return null;
                }

                int? rssi = null;
                int? mhz = null;
                if (WlanGetNetworkBssList(handle, &guid, 0, BssTypeAny, 0, 0, out var list) == 0 && list != 0)
                {
                    try
                    {
                        var total = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>((void*)list, 4));
                        var match = FindBss(new ReadOnlySpan<byte>((void*)list, (int)total), connection.Bssid);
                        (rssi, mhz) = (match?.Rssi, match?.FrequencyMhz);
                    }
                    finally
                    {
                        WlanFreeMemory(list);
                    }
                }

                return new WifiLink(
                    connection.Ssid,
                    connection.SignalPercent,
                    rssi ?? WifiMath.RssiFromQuality(connection.SignalPercent),
                    WifiMath.Standard(connection.PhyType),
                    mhz is { } f ? WifiMath.Band(f) : null,
                    mhz is { } g ? WifiMath.Channel(g) : null,
                    connection.RxKbps > 0 ? connection.RxKbps / 1000.0 : null,
                    connection.TxKbps > 0 ? connection.TxKbps / 1000.0 : null);
            }
            finally
            {
                _ = WlanCloseHandle(handle, 0);
            }
        }
        catch (DllNotFoundException)
        {
            return null; // WLAN service components not installed (Windows Server, no Wi-Fi)
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal sealed record Connection(string? Ssid, byte[] Bssid, int PhyType, int SignalPercent, long RxKbps, long TxKbps);

    internal sealed record Bss(byte[] Bssid, int Rssi, int FrequencyMhz);

    /// <summary>
    /// WLAN_CONNECTION_ATTRIBUTES: isState(4) mode(4) profile WCHAR[256] (to 520), then WLAN_ASSOCIATION_ATTRIBUTES:
    /// DOT11_SSID {len(4) bytes[32]} at 520, BSS type at 556, BSSID[6] at 560, PHY type at 568, PHY index at 572,
    /// signal quality at 576, rx rate (kbps) at 580, tx rate at 584; security attributes follow (16 bytes).
    /// </summary>
    internal static Connection? ParseConnection(ReadOnlySpan<byte> data)
    {
        if (data.Length < ConnectionSize)
        {
            return null;
        }

        var ssidLength = (int)Math.Min(32, BinaryPrimitives.ReadUInt32LittleEndian(data[520..]));
        var ssid = ssidLength > 0 ? Encoding.UTF8.GetString(data.Slice(524, ssidLength)) : null;
        return new Connection(
            ssid,
            data.Slice(560, 6).ToArray(),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data[568..]),
            (int)Math.Min(100, BinaryPrimitives.ReadUInt32LittleEndian(data[576..])),
            BinaryPrimitives.ReadUInt32LittleEndian(data[580..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[584..]));
    }

    /// <summary>
    /// WLAN_BSS_LIST: total size(4), count(4), then WLAN_BSS_ENTRY[count] of 360 bytes: BSSID at 40, RSSI (LONG,
    /// dBm) at 56, center frequency (kHz) at 92. Returns the entry of the connected access point.
    /// </summary>
    internal static Bss? FindBss(ReadOnlySpan<byte> list, byte[] bssid)
    {
        if (list.Length < 8)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(list[4..]);
        for (var i = 0; i < count; i++)
        {
            var offset = 8 + (i * BssEntrySize);
            if (offset + BssEntrySize > list.Length)
            {
                break;
            }

            var entry = list.Slice(offset, BssEntrySize);
            if (entry.Slice(40, 6).SequenceEqual(bssid))
            {
                return new Bss(bssid, BinaryPrimitives.ReadInt32LittleEndian(entry[56..]), (int)(BinaryPrimitives.ReadUInt32LittleEndian(entry[92..]) / 1000));
            }
        }

        return null;
    }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint handle);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanCloseHandle(nint handle, nint reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(nint handle, Guid* interfaceGuid, int opcode, nint reserved, out uint dataSize, out nint data, nint opcodeValueType);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanGetNetworkBssList(nint handle, Guid* interfaceGuid, nint ssid, int bssType, int securityEnabled, nint reserved, out nint list);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(nint memory);
}
