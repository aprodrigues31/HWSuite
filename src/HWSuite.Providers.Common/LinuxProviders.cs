using System.Runtime.InteropServices;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Smbios;

namespace HWSuite.Providers.Common;

/// <summary>
/// Reads SMBIOS from Linux sysfs. HWSuite targets Windows; this exists so the core can be
/// exercised on Linux development machines and CI runners. Needs root to read the table.
/// </summary>
public sealed class LinuxSmbiosProvider : IInventoryProvider
{
    private const string EntryPointPath = "/sys/firmware/dmi/tables/smbios_entry_point";
    private const string TablePath = "/sys/firmware/dmi/tables/DMI";

    public string Name => "SMBIOS (Linux sysfs)";

    public bool IsSupported => OperatingSystem.IsLinux() && File.Exists(EntryPointPath);

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] entry, table;
        try
        {
            entry = File.ReadAllBytes(EntryPointPath);
            table = File.ReadAllBytes(TablePath);
        }
        catch (UnauthorizedAccessException)
        {
            context.Warn("reading the SMBIOS table requires root; run with sudo for firmware details");
            return;
        }

        var (major, minor) = entry.AsSpan().StartsWith("_SM3_"u8) ? (entry[7], entry[8])
            : entry.AsSpan().StartsWith("_SM_"u8) ? (entry[6], entry[7])
            : ((byte)0, (byte)0);

        SmbiosDecoder.Apply(SmbiosTable.ParseTable(major, minor, table), inventory);
    }
}

/// <summary>Basic OS identification for non-Windows hosts.</summary>
public sealed class RuntimeOsProvider : IInventoryProvider
{
    public string Name => "OS (runtime)";

    public bool IsSupported => !OperatingSystem.IsWindows();

    public void Contribute(HardwareInventory inventory, ProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        string? name = null, version = null;
        if (OperatingSystem.IsLinux() && File.Exists("/etc/os-release"))
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var value = parts[1].Trim('"');
                if (parts[0] == "NAME")
                {
                    name = value;
                }
                else if (parts[0] == "VERSION_ID")
                {
                    version = value;
                }
            }
        }

        inventory.OperatingSystem = new OperatingSystemInfo
        {
            Name = name ?? RuntimeInformation.OSDescription,
            DisplayVersion = version,
            Build = Environment.OSVersion.Version.ToString(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Description = RuntimeInformation.OSDescription,
        };
    }
}
