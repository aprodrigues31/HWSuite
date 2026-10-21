using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Smbios;
using Microsoft.Win32;

namespace HWSuite.Providers.Windows;

/// <summary>Firmware data (system, board, BIOS, sockets, memory slots) from the SMBIOS table.</summary>
public sealed class WindowsSmbiosProvider : IInventoryProvider
{
    public string Name => "SMBIOS";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var raw = Native.ReadFirmwareTable(Native.RsmbSignature, 0);
        SmbiosDecoder.Apply(SmbiosTable.ParseWindowsRaw(raw), inventory);
    }
}

/// <summary>Windows edition, version and build from the registry.</summary>
public sealed class WindowsOsProvider : IInventoryProvider
{
    public string Name => "Windows";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        inventory.OperatingSystem = Read();
    }

    [SupportedOSPlatform("windows")]
    private static OperatingSystemInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string;
        var display = key?.GetValue("DisplayVersion") as string;
        var buildText = key?.GetValue("CurrentBuildNumber") as string;
        var ubr = key?.GetValue("UBR") is int u ? u : (int?)null;

        _ = int.TryParse(buildText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var build);

        return new OperatingSystemInfo
        {
            Name = WindowsProductName(product, build),
            DisplayVersion = display,
            Build = ubr is null ? buildText : string.Create(CultureInfo.InvariantCulture, $"{buildText}.{ubr}"),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Description = RuntimeInformation.OSDescription,
        };
    }

    /// <summary>
    /// Windows 11 still reports "Windows 10 ..." in ProductName; build 22000 and later is Windows 11.
    /// </summary>
    internal static string? WindowsProductName(string? productName, int build)
    {
        if (productName is null)
        {
            return null;
        }

        return build >= 22000 && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? "Windows 11" + productName["Windows 10".Length..]
            : productName;
    }
}
