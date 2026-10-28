using System.Globalization;
using HWSuite.Core.Model;

namespace HWSuite.Core.Gpu;

/// <summary>
/// One sign that a card is not what its name says: a localization key and its arguments. Numbers are
/// formatted in the reader's culture; a <see cref="long"/> argument is a size in bytes.
/// </summary>
public sealed record AuthenticityFinding(string Key, params object[] Args);

/// <summary>Outcome of the counterfeit check of one card.</summary>
/// <param name="Findings">Signs of a relabeled or counterfeit card; empty when everything matches.</param>
/// <param name="Checked">Whether the decisive check (the PCI id against the model's chip) could run.</param>
/// <param name="ModelsById">Without a recognized name: the models the PCI id belongs to.</param>
public sealed record AuthenticityResult(IReadOnlyList<AuthenticityFinding> Findings, bool Checked, IReadOnlyList<string> ModelsById);

/// <summary>
/// GPU-Z style counterfeit check. A fake card is usually an old, cheap chip with a reprogrammed BIOS or a
/// modified driver that makes Windows show the name of a newer model. What the chip cannot fake is checked
/// against the model the name claims: the PCI vendor and device id (the id of the real chip, unless the BIOS
/// was changed too), the DirectX 12 feature level and ray tracing (every model in the database is DirectX 12
/// Ultimate), the PCI Express generation, the memory size and, on AMD, the memory clock.
/// </summary>
public static class GpuAuthenticity
{
    public static AuthenticityResult Check(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var findings = new List<AuthenticityFinding>();
        var vendor = Hex(gpu.PciVendorId);
        var device = Hex(gpu.PciDeviceId);
        var byId = vendor is { } v && device is { } d && GpuDeviceIds.Models.TryGetValue((v, d), out var models) ? models : [];

        if (gpu.Reference is not { } r)
        {
            return new AuthenticityResult(findings, false, byId);
        }

        var expectedVendor = ExpectedVendor(r.Model);
        var checkedId = false;
        if (vendor is { } pciVendor && pciVendor != expectedVendor)
        {
            findings.Add(new("gpu.fake.vendor", r.Model, Pci.PciIds.VendorName(pciVendor) ?? Pci.PciIds.FormatId(pciVendor)));
            checkedId = true;
        }
        else if (vendor is not null && device is { } pciDevice)
        {
            checkedId = true;
            if (!byId.Contains(r.Model))
            {
                var id = $"{Pci.PciIds.FormatId(expectedVendor)}:{Pci.PciIds.FormatId(pciDevice)}";
                findings.Add(byId.Length > 0
                    ? new("gpu.fake.deviceIdOther", id, r.Model, r.Chip, string.Join(" / ", byId))
                    : new("gpu.fake.deviceId", id, r.Model, r.Chip));
            }
        }

        if (gpu.Direct3DFeatureLevel is { } level)
        {
            if (FeatureLevel(level) is { } fl && (fl.Major * 10) + fl.Minor < 122)
            {
                findings.Add(new("gpu.fake.featureLevel", level.Replace('_', '.'), r.Model));
            }
            else if (gpu.RaytracingTier is null)
            {
                findings.Add(new("gpu.fake.raytracing", r.Model));
            }
        }

        var generation = ExpectedPcieSpeed(r.Architecture);
        if (gpu.PcieLink is { MaxSpeed: > 0 } link && link.MaxSpeed < generation)
        {
            findings.Add(new("gpu.fake.pcie", Pci.PcieLink.Generation(link.MaxSpeed) ?? "?", r.Model, Pci.PcieLink.Generation(generation) ?? "?"));
        }

        // Dedicated memory well below the model's (25 % margin for reserved memory).
        if (gpu.DedicatedMemoryBytes is long dedicated and > 0 && dedicated < r.MemoryGb * 1024L * 1024 * 1024 * 0.75)
        {
            findings.Add(new("gpu.memoryMismatch", dedicated, r.MemoryGb));
        }

        if (gpu.CardMemoryClockMhz is { } clock && GpuDatabase.DataRateGbps(r.MemoryType, clock) is { } gbps && gbps < r.MemoryGbps * 0.75)
        {
            findings.Add(new("gpu.fake.memoryClock", Math.Round(gbps, 1), r.Model, r.MemoryGbps));
        }

        return new AuthenticityResult(findings, checkedId, byId);
    }

    /// <summary>PCI vendor of the brand in the model name.</summary>
    internal static ushort ExpectedVendor(string model) =>
        model.StartsWith("RX", StringComparison.Ordinal) ? (ushort)0x1002
        : model.StartsWith("RTX", StringComparison.Ordinal) ? (ushort)0x10DE
        : (ushort)0x8086;

    /// <summary>PCIe link speed code the chip supports: 5.0 (32 GT/s) for RDNA 4 and Blackwell, 4.0 for the rest.</summary>
    internal static int ExpectedPcieSpeed(string architecture) =>
        architecture is "RDNA 4" or "Blackwell" ? 5 : 4;

    private static (int Major, int Minor)? FeatureLevel(string level)
    {
        var parts = level.Split('_', '.');
        return parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var minor)
            ? (major, minor)
            : null;
    }

    private static ushort? Hex(string? text) =>
        ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;
}
