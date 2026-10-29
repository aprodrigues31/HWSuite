using System.Text.RegularExpressions;

namespace HWSuite.Core.Model;

/// <summary>What a motherboard chipset supports (AMD and Intel desktop platforms since 2017).</summary>
/// <param name="Processors">Processor generations the platform takes (some need a BIOS update).</param>
/// <param name="CpuOverclock">Unlocked processor multipliers can be raised on this chipset.</param>
/// <param name="MemoryOverclock">XMP / EXPO memory profiles above the official speed are allowed.</param>
/// <param name="Pcie">PCI Express generation for the graphics card slot and the main M.2 (from the processor).</param>
public sealed record ChipsetSpec(string Name, string Vendor, string Socket, string Memory, string Processors, bool CpuOverclock, bool MemoryOverclock, string Pcie, int Year);

/// <summary>Chipset reference, found from the board's model name ("TUF GAMING B550M-PLUS" -> B550).</summary>
public static partial class Chipsets
{
    private const string Am4Early = "Ryzen 1000–5000 (BIOS)";

    public static IReadOnlyList<ChipsetSpec> All { get; } =
    [
        // AMD AM4 (DDR4)
        new("A320", "AMD", "AM4", "DDR4", Am4Early, false, true, "PCIe 3.0", 2017),
        new("B350", "AMD", "AM4", "DDR4", Am4Early, true, true, "PCIe 3.0", 2017),
        new("X370", "AMD", "AM4", "DDR4", Am4Early, true, true, "PCIe 3.0", 2017),
        new("B450", "AMD", "AM4", "DDR4", "Ryzen 1000–5000 (BIOS)", true, true, "PCIe 3.0", 2018),
        new("X470", "AMD", "AM4", "DDR4", "Ryzen 1000–5000 (BIOS)", true, true, "PCIe 3.0", 2018),
        new("A520", "AMD", "AM4", "DDR4", "Ryzen 3000–5000", false, true, "PCIe 3.0", 2020),
        new("B550", "AMD", "AM4", "DDR4", "Ryzen 3000–5000 (3000G ✕)", true, true, "PCIe 4.0", 2020),
        new("X570", "AMD", "AM4", "DDR4", "Ryzen 2000–5000", true, true, "PCIe 4.0", 2019),

        // AMD AM5 (DDR5)
        new("A620", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", false, true, "PCIe 4.0", 2023),
        new("B650", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 4.0 GPU · 5.0 M.2", 2022),
        new("B650E", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 5.0", 2022),
        new("X670", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 4.0 GPU · 5.0 M.2", 2022),
        new("X670E", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 5.0", 2022),
        new("B840", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", false, true, "PCIe 4.0 GPU · 4.0 M.2", 2025),
        new("B850", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 4.0 GPU · 5.0 M.2", 2025),
        new("X870", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 5.0 · USB4", 2024),
        new("X870E", "AMD", "AM5", "DDR5", "Ryzen 7000–9000", true, true, "PCIe 5.0 · USB4", 2024),

        // Intel LGA 1151 v2 (300 series, DDR4)
        new("H310", "Intel", "LGA 1151", "DDR4", "Core 8000–9000", false, false, "PCIe 3.0", 2018),
        new("B360", "Intel", "LGA 1151", "DDR4", "Core 8000–9000", false, false, "PCIe 3.0", 2018),
        new("H370", "Intel", "LGA 1151", "DDR4", "Core 8000–9000", false, false, "PCIe 3.0", 2018),
        new("Z370", "Intel", "LGA 1151", "DDR4", "Core 8000–9000 (BIOS)", true, true, "PCIe 3.0", 2017),
        new("Z390", "Intel", "LGA 1151", "DDR4", "Core 8000–9000", true, true, "PCIe 3.0", 2018),

        // Intel LGA 1200 (400 / 500 series, DDR4)
        new("H410", "Intel", "LGA 1200", "DDR4", "Core 10000", false, false, "PCIe 3.0", 2020),
        new("B460", "Intel", "LGA 1200", "DDR4", "Core 10000", false, false, "PCIe 3.0", 2020),
        new("H470", "Intel", "LGA 1200", "DDR4", "Core 10000–11000", false, false, "PCIe 3.0", 2020),
        new("Z490", "Intel", "LGA 1200", "DDR4", "Core 10000–11000", true, true, "PCIe 3.0 (4.0: Core 11000)", 2020),
        new("H510", "Intel", "LGA 1200", "DDR4", "Core 10000–11000", false, false, "PCIe 3.0 (4.0: Core 11000)", 2021),
        new("B560", "Intel", "LGA 1200", "DDR4", "Core 10000–11000", false, true, "PCIe 3.0 (4.0: Core 11000)", 2021),
        new("H570", "Intel", "LGA 1200", "DDR4", "Core 10000–11000", false, true, "PCIe 3.0 (4.0: Core 11000)", 2021),
        new("Z590", "Intel", "LGA 1200", "DDR4", "Core 10000–11000", true, true, "PCIe 3.0 (4.0: Core 11000)", 2021),

        // Intel LGA 1700 (600 / 700 series; DDR4 or DDR5, per board)
        new("H610", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000 (BIOS)", false, false, "PCIe 4.0", 2022),
        new("B660", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000 (BIOS)", false, true, "PCIe 4.0", 2022),
        new("H670", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000 (BIOS)", false, true, "PCIe 5.0 GPU · 4.0 M.2", 2022),
        new("Z690", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000 (BIOS)", true, true, "PCIe 5.0 GPU · 4.0 M.2", 2021),
        new("B760", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000", false, true, "PCIe 4.0", 2023),
        new("H770", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000", false, true, "PCIe 5.0 GPU · 4.0 M.2", 2023),
        new("Z790", "Intel", "LGA 1700", "DDR4 / DDR5", "Core 12000–14000", true, true, "PCIe 5.0 GPU · 4.0 M.2", 2022),

        // Intel LGA 1851 (800 series, DDR5)
        new("H810", "Intel", "LGA 1851", "DDR5", "Core Ultra 200S", false, false, "PCIe 4.0", 2025),
        new("B860", "Intel", "LGA 1851", "DDR5", "Core Ultra 200S", false, true, "PCIe 5.0 GPU · 4.0 M.2", 2025),
        new("Z890", "Intel", "LGA 1851", "DDR5", "Core Ultra 200S", true, true, "PCIe 5.0", 2024),
    ];

    /// <summary>The chipset named in a board model ("ROG STRIX X870E-E GAMING WIFI", "PRO B760M-A DDR4"), or null.</summary>
    public static ChipsetSpec? Find(string? board)
    {
        if (string.IsNullOrWhiteSpace(board))
        {
            return null;
        }

        var match = ChipsetName().Match(board.ToUpperInvariant());
        return match.Success ? All.FirstOrDefault(c => c.Name == match.Groups["chip"].Value) : null;
    }

    // The chipset code, maybe followed by M/I (micro-ATX / mini-ITX) or E, then a separator.
    [GeneratedRegex(@"(?<![A-Z0-9])(?<chip>[ABHXZ][2-8][1-9]0E?)(?:M|I|-|\s|$)")]
    private static partial Regex ChipsetName();
}
