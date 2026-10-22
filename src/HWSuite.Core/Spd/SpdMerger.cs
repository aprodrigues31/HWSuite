using HWSuite.Core.Model;

namespace HWSuite.Core.Spd;

/// <summary>
/// Attaches decoded SPDs to the modules SMBIOS already listed. The serial number links them (SMBIOS
/// copies it from the SPD); when serials are missing and the counts agree, slot order is used.
/// Fields SMBIOS left empty are filled from the SPD.
/// </summary>
public static class SpdMerger
{
    /// <summary>Returns how many SPDs were attached to a module.</summary>
    public static int Attach(MemoryInfo memory, IReadOnlyList<SpdInfo> spds)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(spds);

        var pending = spds.ToList();
        var attached = 0;

        foreach (var module in memory.Modules)
        {
            var match = pending.FirstOrDefault(spd => SameSerial(module.SerialNumber, spd.SerialNumber));
            if (match is not null)
            {
                Fill(module, match);
                pending.Remove(match);
                attached++;
            }
        }

        var unmatched = memory.Modules.Where(m => m.Spd is null).ToList();
        if (pending.Count > 0 && pending.Count == unmatched.Count)
        {
            for (var i = 0; i < unmatched.Count; i++)
            {
                Fill(unmatched[i], pending[i]);
                attached++;
            }

            pending.Clear();
        }

        if (memory.Modules.Count == 0)
        {
            // No SMBIOS memory table (rare, some VMs): list the modules the SPDs describe.
            foreach (var spd in pending)
            {
                var module = new MemoryModule { SizeBytes = spd.SizeBytes ?? 0, Type = spd.MemoryType, FormFactor = spd.ModuleType };
                Fill(module, spd);
                memory.Modules.Add(module);
                attached++;
            }
        }

        return attached;
    }

    private static void Fill(MemoryModule module, SpdInfo spd)
    {
        module.Spd = spd;
        module.Manufacturer ??= spd.ModuleManufacturer;
        module.PartNumber ??= spd.PartNumber;
        module.SerialNumber ??= spd.SerialNumber;
        module.Ranks ??= spd.Ranks;
    }

    /// <summary>Hex serials compared without case and leading zeros ("0E6A1B2C" = "e6a1b2c").</summary>
    internal static bool SameSerial(string? smbios, string? spd)
    {
        if (string.IsNullOrWhiteSpace(smbios) || string.IsNullOrWhiteSpace(spd))
        {
            return false;
        }

        static string Normalize(string s) => s.Trim().TrimStart('0').ToUpperInvariant();
        return Normalize(smbios) == Normalize(spd);
    }
}
