using System.Globalization;

namespace HWSuite.Core.Gpu;

/// <summary>Names users know for GPU drivers and memory makers, from the raw values Windows and drivers report.</summary>
public static class GpuDrivers
{
    /// <summary>
    /// The driver package as the vendor names it: "AMD Software: Adrenalin Edition 26.10.1" (from the
    /// driver's RadeonSoftwareVersion value) or "NVIDIA 560.94" (the last five digits of the Windows
    /// driver version 32.0.15.6094). Intel and others keep the Windows version.
    /// </summary>
    public static string? Package(string? vendorId, string? driverVersion, string? radeonSoftwareVersion)
    {
        if (string.Equals(vendorId, "1002", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(radeonSoftwareVersion))
        {
            return "AMD Software: Adrenalin Edition " + radeonSoftwareVersion.Trim();
        }

        if (string.Equals(vendorId, "10DE", StringComparison.OrdinalIgnoreCase) && NvidiaRelease(driverVersion) is { } release)
        {
            return "NVIDIA " + release;
        }

        return null;
    }

    /// <summary>"32.0.15.6094" -> "560.94".</summary>
    public static string? NvidiaRelease(string? driverVersion)
    {
        var parts = driverVersion?.Split('.');
        if (parts is not { Length: 4 } || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var third)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var fourth))
        {
            return null;
        }

        var digits = string.Create(CultureInfo.InvariantCulture, $"{third % 10}{fourth:D4}");
        return $"{digits[..3]}.{digits[3..]}";
    }

    /// <summary>ADL ADLvRamVendor codes (adl_defines.h); null for unknown codes.</summary>
    public static string? AmdMemoryVendor(int code) => code switch
    {
        1 => "Samsung",
        2 => "Infineon",
        3 => "Elpida",
        4 => "Etron",
        5 => "Nanya",
        6 => "SK Hynix",
        7 => "Mosel",
        8 => "Winbond",
        9 => "ESMT",
        15 => "Micron",
        _ => null,
    };
}
