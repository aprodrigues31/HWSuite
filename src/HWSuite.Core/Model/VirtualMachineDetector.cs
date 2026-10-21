namespace HWSuite.Core.Model;

/// <summary>
/// Recognizes virtual machines from the firmware identity hypervisors publish in SMBIOS.
/// CPUID alone cannot tell a physical PC running Windows with VBS from a VM with nested
/// virtualization (both are a Hyper-V root partition), so firmware has the final word.
/// </summary>
public static class VirtualMachineDetector
{
    private static readonly (string Needle, string Platform)[] Signatures =
    [
        ("VMware", "VMware"),
        ("VirtualBox", "VirtualBox"),
        ("innotek GmbH", "VirtualBox"),
        ("QEMU", "QEMU"),
        ("KVM", "KVM"),
        ("Xen", "Xen"),
        ("Amazon EC2", "Amazon EC2"),
        ("Google Compute Engine", "Google Compute Engine"),
        ("Parallels", "Parallels"),
        ("Hyper-V", "Microsoft Hyper-V"),
        ("Bochs", "Bochs"),
    ];

    /// <summary>Returns the virtualization platform name, or null when the firmware looks physical.</summary>
    public static string? FromFirmware(SystemInfo? system, BiosInfo? bios)
    {
        if (system?.Manufacturer?.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) == true
            && system.Product?.Equals("Virtual Machine", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Microsoft Hyper-V";
        }

        string?[] fields = [system?.Manufacturer, system?.Product, system?.Version, system?.Family, bios?.Vendor, bios?.Version];
        foreach (var field in fields)
        {
            if (field is null)
            {
                continue;
            }

            foreach (var (needle, platform) in Signatures)
            {
                if (field.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return platform;
                }
            }
        }

        return null;
    }

    /// <summary>Corrects CPUID's root-partition verdict when the firmware says this is a VM.</summary>
    public static void Reconcile(HardwareInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var platform = FromFirmware(inventory.System, inventory.Bios);
        if (platform is null)
        {
            return;
        }

        foreach (var cpu in inventory.Processors.Where(p => p.HypervisorPresent == true))
        {
            cpu.HypervisorRootPartition = false;
            cpu.HypervisorVendor ??= platform;
        }
    }
}
