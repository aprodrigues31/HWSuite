using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HWSuite.Core.Network;
using Microsoft.Win32;

namespace HWSuite.Providers.Windows;

/// <summary>
/// The Wi-Fi test's view of the WLAN API: the adapter, its connection (with security), every access point heard in
/// a fresh scan (channel width read from the HT/VHT/HE/EHT elements they broadcast) and, for the stability test,
/// a quick link reading. Windows 11 asks for the location permission before it shows nearby networks: without it
/// the scan is refused and only the connection is shown.
/// </summary>
public static unsafe partial class WlanReader
{
    private const int OpcodeRssi = 0x10000102; // wlan_intf_opcode_rssi: LONG, dBm
    private const uint ErrorAccessDenied = 5;
    private const int InterfaceInfoSize = 532; // WLAN_INTERFACE_INFO: GUID(16) + WCHAR[256] + state(4)
    private const int StateConnected = 1;

    /// <summary>Wi-Fi adapters: interface id (GUID), description and whether it is connected.</summary>
    public static IReadOnlyList<(string Id, string Description, bool Connected)> Interfaces()
    {
        var result = new List<(string, string, bool)>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        try
        {
            if (WlanOpenHandle(2, 0, out _, out var handle) != 0)
            {
                return result;
            }

            try
            {
                if (WlanEnumInterfaces(handle, 0, out var list) != 0 || list == 0)
                {
                    return result;
                }

                try
                {
                    var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>((void*)list, 4));
                    var data = new ReadOnlySpan<byte>((void*)list, 8 + (count * InterfaceInfoSize));
                    for (var i = 0; i < count; i++)
                    {
                        var item = data.Slice(8 + (i * InterfaceInfoSize), InterfaceInfoSize);
                        var guid = new Guid(item[..16]);
                        var description = MemoryMarshal.Cast<byte, char>(item.Slice(16, 512)).ToString().TrimEnd('\0');
                        var nul = description.IndexOf('\0', StringComparison.Ordinal);
                        if (nul >= 0)
                        {
                            description = description[..nul];
                        }

                        var state = BinaryPrimitives.ReadInt32LittleEndian(item[528..]);
                        result.Add((guid.ToString("D"), description, state == StateConnected));
                    }
                }
                finally
                {
                    WlanFreeMemory(list);
                }
            }
            finally
            {
                _ = WlanCloseHandle(handle, 0);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // no WLAN service
        }

        return result;
    }

    /// <summary>
    /// The connected Wi-Fi adapter (or the first one) with its connection and the networks around it. With
    /// <paramref name="scan"/> it asks the adapter for a fresh scan first (about four seconds). Null when this
    /// computer has no Wi-Fi.
    /// </summary>
    public static WifiStatus? Status(bool scan, CancellationToken cancel = default)
    {
        var adapters = Interfaces();
        if (adapters.Count == 0)
        {
            return null;
        }

        var (id, description, connected) = adapters.FirstOrDefault(a => a.Connected) is { Id: not null } c ? c : adapters[0];
        var guid = Guid.Parse(id);
        if (WlanOpenHandle(2, 0, out _, out var handle) != 0)
        {
            return null;
        }

        try
        {
            if (scan)
            {
                var asked = WlanScan(handle, &guid, 0, 0, 0);
                if (asked == 0)
                {
                    cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(4)); // scans finish in 2–4 s
                }
            }

            Connection? connection = null;
            (string? Security, string? Cipher) security = (null, null);
            if (connected && WlanQueryInterface(handle, &guid, OpcodeCurrentConnection, 0, out var size, out var data, 0) == 0 && data != 0)
            {
                try
                {
                    if (size >= ConnectionSize)
                    {
                        var span = new ReadOnlySpan<byte>((void*)data, ConnectionSize);
                        connection = ParseConnection(span);
                        security = ParseSecurity(span);
                    }
                }
                finally
                {
                    WlanFreeMemory(data);
                }
            }

            var networks = new List<WifiBss>();
            var blocked = false;
            var code = WlanGetNetworkBssList(handle, &guid, 0, BssTypeAny, 0, 0, out var list);
            if (code == 0 && list != 0)
            {
                try
                {
                    var total = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>((void*)list, 4));
                    networks.AddRange(ParseBssList(new ReadOnlySpan<byte>((void*)list, (int)total), connection?.Bssid));
                }
                finally
                {
                    WlanFreeMemory(list);
                }
            }
            else if (code == ErrorAccessDenied)
            {
                blocked = true;
            }

            var current = networks.FirstOrDefault(n => n.Connected);
            var rssi = current?.Rssi ?? QueryRssi(handle, guid) ?? (connection is null ? null : WifiMath.RssiFromQuality(connection.SignalPercent));
            var (version, date, provider) = Driver(id);
            return new WifiStatus(
                id,
                description,
                version,
                date,
                provider,
                connection is not null,
                connection?.Ssid,
                connection is null ? null : Mac(connection.Bssid),
                connection?.SignalPercent ?? 0,
                rssi,
                current?.Standard ?? (connection is null ? null : WifiMath.Standard(connection.PhyType)),
                current?.BandGhz,
                current?.Channel,
                current?.WidthMhz,
                connection is { RxKbps: > 0 } ? connection.RxKbps / 1000.0 : null,
                connection is { TxKbps: > 0 } ? connection.TxKbps / 1000.0 : null,
                security.Security,
                security.Cipher,
                WifiAnalysis.GatewayOf(id),
                [.. networks.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Rssi)],
                blocked);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            _ = WlanCloseHandle(handle, 0);
        }
    }

    /// <summary>A quick reading for the stability test: RSSI, access point and receive rate (nulls when disconnected).</summary>
    public static (int? Rssi, string? Bssid, double? RxMbps) Link(string interfaceId)
    {
        if (!OperatingSystem.IsWindows() || !Guid.TryParse(interfaceId, out var guid))
        {
            return (null, null, null);
        }

        try
        {
            if (WlanOpenHandle(2, 0, out _, out var handle) != 0)
            {
                return (null, null, null);
            }

            try
            {
                if (WlanQueryInterface(handle, &guid, OpcodeCurrentConnection, 0, out var size, out var data, 0) != 0 || data == 0)
                {
                    return (null, null, null);
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
                    return (null, null, null);
                }

                var rssi = QueryRssi(handle, guid) ?? WifiMath.RssiFromQuality(connection.SignalPercent);
                return (rssi, Mac(connection.Bssid), connection.RxKbps > 0 ? connection.RxKbps / 1000.0 : null);
            }
            finally
            {
                _ = WlanCloseHandle(handle, 0);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return (null, null, null);
        }
    }

    private static int? QueryRssi(nint handle, Guid guid)
    {
        if (WlanQueryInterface(handle, &guid, OpcodeRssi, 0, out var size, out var data, 0) != 0 || data == 0)
        {
            return null;
        }

        try
        {
            var value = size >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>((void*)data, 4)) : 0;
            return value is < 0 and > -120 ? value : null;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }

    internal static string Mac(byte[] bytes) => string.Join(':', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>WLAN_SECURITY_ATTRIBUTES at 588: enabled(4) 802.1X(4) auth algorithm(4) cipher(4).</summary>
    internal static (string? Security, string? Cipher) ParseSecurity(ReadOnlySpan<byte> connection)
    {
        if (connection.Length < ConnectionSize)
        {
            return (null, null);
        }

        var enabled = BinaryPrimitives.ReadInt32LittleEndian(connection[588..]) != 0;
        var auth = BinaryPrimitives.ReadInt32LittleEndian(connection[596..]);
        var cipher = BinaryPrimitives.ReadInt32LittleEndian(connection[600..]);
        string? security = auth switch
        {
            1 => enabled ? "WEP" : "Open",
            2 => "WEP",
            3 => "WPA",
            4 => "WPA-PSK",
            6 => "WPA2",
            7 => "WPA2-PSK",
            8 or 11 => "WPA3",
            9 => "WPA3-SAE",
            10 => "OWE",
            _ => null,
        };
        string? name = cipher switch
        {
            0 => null,
            1 or 5 or 0x100 => "WEP",
            2 => "TKIP",
            4 => "AES (CCMP)",
            8 => "GCMP",
            9 => "GCMP-256",
            _ => null,
        };
        return (security, name);
    }

    /// <summary>
    /// Every entry of a WLAN_BSS_LIST (see <see cref="FindBss"/>): SSID (length at 0, bytes at 4), BSSID at 40, PHY
    /// type at 52, RSSI at 56, center frequency (kHz) at 92, information elements at offset 352 / size 356.
    /// </summary>
    internal static IEnumerable<WifiBss> ParseBssList(ReadOnlySpan<byte> list, byte[]? connected)
    {
        var result = new List<WifiBss>();
        if (list.Length < 8)
        {
            return result;
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
            var ssidLength = (int)Math.Min(32, BinaryPrimitives.ReadUInt32LittleEndian(entry));
            var ssid = ssidLength > 0 ? Encoding.UTF8.GetString(entry.Slice(4, ssidLength)).TrimEnd('\0') : null;
            var bssid = entry.Slice(40, 6).ToArray();
            var phy = BinaryPrimitives.ReadInt32LittleEndian(entry[52..]);
            var rssi = BinaryPrimitives.ReadInt32LittleEndian(entry[56..]);
            var mhz = (int)(BinaryPrimitives.ReadUInt32LittleEndian(entry[92..]) / 1000);
            var ieOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[352..]);
            var ieSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[356..]);
            var ies = offset + ieOffset + ieSize <= list.Length && ieOffset > 0 ? list.Slice(offset + ieOffset, ieSize) : [];
            var (width, generation) = Elements(ies);
            var standard = WifiMath.Standard(phy);
            if (generation == 7 || (generation == 6 && (standard is null || !standard.Contains("Wi-Fi", StringComparison.Ordinal) || standard.Contains("Wi-Fi 4", StringComparison.Ordinal) || standard.Contains("Wi-Fi 5", StringComparison.Ordinal))))
            {
                standard = generation == 7 ? "802.11be (Wi-Fi 7)" : "802.11ax (Wi-Fi 6)";
            }

            result.Add(new WifiBss(
                string.IsNullOrEmpty(ssid) ? null : ssid,
                Mac(bssid),
                rssi,
                mhz,
                WifiMath.Channel(mhz),
                WifiMath.Band(mhz),
                standard,
                width,
                connected is not null && bssid.AsSpan().SequenceEqual(connected)));
        }

        return result;
    }

    /// <summary>
    /// Channel width and Wi-Fi generation from the broadcast elements: HT Operation (61, 40 MHz flag), VHT Operation
    /// (192, 80/160), HE Operation (extension 36, the 6 GHz information carries the width there) and EHT Operation
    /// (extension 106, up to 320 MHz).
    /// </summary>
    internal static (int WidthMhz, int Generation) Elements(ReadOnlySpan<byte> ies)
    {
        var width = 20;
        var generation = 0;
        var i = 0;
        while (i + 2 <= ies.Length)
        {
            int id = ies[i];
            int length = ies[i + 1];
            if (i + 2 + length > ies.Length)
            {
                break;
            }

            var body = ies.Slice(i + 2, length);
            switch (id)
            {
                case 61 when body.Length >= 2:
                    generation = Math.Max(generation, 4);
                    if ((body[1] & 0x04) != 0)
                    {
                        width = Math.Max(width, 40);
                    }

                    break;
                case 192 when body.Length >= 3:
                    generation = Math.Max(generation, 5);
                    width = body[0] switch
                    {
                        1 when body[2] != 0 && Math.Abs(body[2] - body[1]) == 8 => Math.Max(width, 160),
                        1 => Math.Max(width, 80),
                        2 or 3 => Math.Max(width, 160),
                        _ => width,
                    };
                    break;
                case 255 when body.Length >= 1 && body[0] == 36 && body.Length >= 7:
                {
                    generation = Math.Max(generation, 6);
                    var parameters = body[1] | (body[2] << 8) | (body[3] << 16);
                    var at = 1 + 3 + 1 + 2; // ext id, parameters, BSS color, basic HE-MCS
                    if ((parameters & (1 << 14)) != 0)
                    {
                        at += 3; // VHT operation information
                    }

                    if ((parameters & (1 << 15)) != 0)
                    {
                        at += 1; // max co-hosted BSSID indicator
                    }

                    if ((parameters & (1 << 17)) != 0 && body.Length >= at + 2)
                    {
                        width = Math.Max(width, (body[at + 1] & 0x03) switch { 1 => 40, 2 => 80, 3 => 160, _ => 20 });
                    }

                    break;
                }

                case 255 when body.Length >= 1 && body[0] == 106 && body.Length >= 7:
                    generation = Math.Max(generation, 7);
                    if ((body[1] & 0x01) != 0 && body.Length >= 7)
                    {
                        width = Math.Max(width, (body[6] & 0x07) switch { 1 => 40, 2 => 80, 3 => 160, 4 => 320, _ => 20 });
                    }

                    break;
            }

            i += 2 + length;
        }

        return (width, generation);
    }

    /// <summary>Driver version, date and maker of the adapter (network class key with this interface id).</summary>
    private static (string? Version, DateTime? Date, string? Provider) Driver(string interfaceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (null, null, null);
        }

        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
            if (cls is null)
            {
                return (null, null, null);
            }

            foreach (var name in cls.GetSubKeyNames())
            {
                using var key = cls.OpenSubKey(name);
                if (key?.GetValue("NetCfgInstanceId") is string id && string.Equals(id.Trim('{', '}'), interfaceId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase))
                {
                    DateTime? date = key.GetValue("DriverDate") is string d && DateTime.TryParseExact(d, ["M-d-yyyy", "MM-dd-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                        ? parsed
                        : null;
                    return (key.GetValue("DriverVersion") as string, date, key.GetValue("ProviderName") as string);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // unknown driver
        }

        return (null, null, null);
    }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanEnumInterfaces(nint handle, nint reserved, out nint list);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanScan(nint handle, Guid* interfaceGuid, nint ssid, nint ieData, nint reserved);
}
