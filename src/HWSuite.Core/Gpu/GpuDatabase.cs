using System.Globalization;
using System.Text.RegularExpressions;

namespace HWSuite.Core.Gpu;

/// <summary>
/// Reference specifications of a desktop GPU model, as published by the manufacturer.
/// <see cref="Units"/> counts compute units (AMD), streaming multiprocessors (NVIDIA) or Xe cores (Intel);
/// <see cref="Shaders"/> are stream processors / CUDA cores / ALUs. Clocks are reference values: partner
/// cards often ship higher.
/// </summary>
public sealed record GpuSpec(
    string Model,
    string Chip,
    string Architecture,
    string Process,
    string Released,
    int? Units,
    int Shaders,
    int Tmus,
    int Rops,
    int BaseMhz,
    int BoostMhz,
    int MemoryGb,
    string MemoryType,
    int BusBits,
    double MemoryGbps,
    int PowerW)
{
    /// <summary>Peak memory bandwidth in GB/s: bus width x data rate per pin.</summary>
    public double BandwidthGBps => BusBits / 8.0 * MemoryGbps;

    /// <summary>"CU" (AMD), "SM" (NVIDIA) or "Xe" (Intel).</summary>
    public string UnitName => Architecture switch
    {
        _ when Architecture.StartsWith("RDNA", StringComparison.Ordinal) => "CU",
        _ when Architecture.StartsWith("Xe", StringComparison.Ordinal) => "Xe",
        _ => "SM",
    };
}

/// <summary>
/// Desktop GPU reference data (Radeon RX 6000/7000/9000, GeForce RTX 30/40/50, Intel Arc A/B).
/// Sources: manufacturer specifications as compiled in the Wikipedia series articles. Models share
/// PCI device ids (AMD uses one id for the RX 7700 XT and 7800 XT), so the model is matched on the
/// adapter name, and memory size picks between variants of the same name (RTX 3060 8 GB / 12 GB).
/// </summary>
public static partial class GpuDatabase
{
    private const string Rdna2 = "RDNA 2";
    private const string Rdna3 = "RDNA 3";
    private const string Rdna4 = "RDNA 4";
    private const string Ampere = "Ampere";
    private const string Ada = "Ada Lovelace";
    private const string Blackwell = "Blackwell";
    private const string Alchemist = "Xe-HPG (Alchemist)";
    private const string Battlemage = "Xe2-HPG (Battlemage)";

    public static IReadOnlyList<GpuSpec> All { get; } =
    [
        // AMD Radeon RX 6000 (RDNA 2)
        new("RX 6400", "Navi 24", Rdna2, "TSMC N6", "2022-01-19", 12, 768, 48, 32, 1923, 2321, 4, "GDDR6", 64, 16, 53),
        new("RX 6500 XT", "Navi 24", Rdna2, "TSMC N6", "2022-01-19", 16, 1024, 64, 32, 2310, 2815, 4, "GDDR6", 64, 18, 107),
        new("RX 6500 XT", "Navi 24", Rdna2, "TSMC N6", "2022-01-19", 16, 1024, 64, 32, 2310, 2815, 8, "GDDR6", 64, 18, 113),
        new("RX 6600", "Navi 23", Rdna2, "TSMC N7", "2021-10-13", 28, 1792, 112, 64, 1626, 2491, 8, "GDDR6", 128, 14, 132),
        new("RX 6600 XT", "Navi 23", Rdna2, "TSMC N7", "2021-08-11", 32, 2048, 128, 64, 1968, 2589, 8, "GDDR6", 128, 16, 160),
        new("RX 6650 XT", "Navi 23", Rdna2, "TSMC N7", "2022-05-10", 32, 2048, 128, 64, 2055, 2635, 8, "GDDR6", 128, 17.5, 180),
        new("RX 6700", "Navi 22", Rdna2, "TSMC N7", "2021-06-09", 36, 2304, 144, 64, 1941, 2450, 10, "GDDR6", 160, 16, 175),
        new("RX 6700 XT", "Navi 22", Rdna2, "TSMC N7", "2021-03-18", 40, 2560, 160, 64, 2321, 2581, 12, "GDDR6", 192, 16, 230),
        new("RX 6750 XT", "Navi 22", Rdna2, "TSMC N7", "2022-05-10", 40, 2560, 160, 64, 2150, 2600, 12, "GDDR6", 192, 18, 250),
        new("RX 6800", "Navi 21", Rdna2, "TSMC N7", "2020-11-18", 60, 3840, 240, 96, 1700, 2105, 16, "GDDR6", 256, 16, 250),
        new("RX 6800 XT", "Navi 21", Rdna2, "TSMC N7", "2020-11-18", 72, 4608, 288, 128, 1825, 2250, 16, "GDDR6", 256, 16, 300),
        new("RX 6900 XT", "Navi 21", Rdna2, "TSMC N7", "2020-12-08", 80, 5120, 320, 128, 1825, 2250, 16, "GDDR6", 256, 16, 300),
        new("RX 6950 XT", "Navi 21", Rdna2, "TSMC N7", "2022-05-10", 80, 5120, 320, 128, 1890, 2310, 16, "GDDR6", 256, 18, 335),

        // AMD Radeon RX 7000 (RDNA 3; Navi 31/32 are chiplets: 5 nm graphics die + 6 nm memory dies)
        new("RX 7600", "Navi 33", Rdna3, "TSMC N6", "2023-05-25", 32, 2048, 128, 64, 1720, 2655, 8, "GDDR6", 128, 18, 165),
        new("RX 7600 XT", "Navi 33", Rdna3, "TSMC N6", "2024-01-24", 32, 2048, 128, 64, 1720, 2755, 16, "GDDR6", 128, 18, 190),
        new("RX 7700 XT", "Navi 32", Rdna3, "TSMC N5 + N6", "2023-09-06", 54, 3456, 216, 96, 1900, 2544, 12, "GDDR6", 192, 18, 245),
        new("RX 7800 XT", "Navi 32", Rdna3, "TSMC N5 + N6", "2023-09-06", 60, 3840, 240, 96, 1800, 2430, 16, "GDDR6", 256, 19.5, 263),
        new("RX 7900 GRE", "Navi 31", Rdna3, "TSMC N5 + N6", "2023-07-27", 80, 5120, 320, 160, 1270, 2245, 16, "GDDR6", 256, 18, 260),
        new("RX 7900 XT", "Navi 31", Rdna3, "TSMC N5 + N6", "2022-12-13", 84, 5376, 336, 192, 1500, 2400, 20, "GDDR6", 320, 20, 315),
        new("RX 7900 XTX", "Navi 31", Rdna3, "TSMC N5 + N6", "2022-12-13", 96, 6144, 384, 192, 1900, 2500, 24, "GDDR6", 384, 20, 355),

        // AMD Radeon RX 9000 (RDNA 4)
        new("RX 9060", "Navi 44", Rdna4, "TSMC N4P", "2025-08-05", 28, 1792, 112, 64, 2400, 2990, 8, "GDDR6", 128, 18, 132),
        new("RX 9060 XT", "Navi 44", Rdna4, "TSMC N4P", "2025-06-05", 32, 2048, 128, 64, 2530, 3130, 8, "GDDR6", 128, 20, 150),
        new("RX 9060 XT", "Navi 44", Rdna4, "TSMC N4P", "2025-06-05", 32, 2048, 128, 64, 2530, 3130, 16, "GDDR6", 128, 20, 160),
        new("RX 9070 GRE", "Navi 48", Rdna4, "TSMC N4P", "2025-05-08", 48, 3072, 192, 96, 2220, 2790, 12, "GDDR6", 192, 18, 220),
        new("RX 9070", "Navi 48", Rdna4, "TSMC N4P", "2025-03-06", 56, 3584, 224, 128, 2070, 2520, 16, "GDDR6", 256, 20, 220),
        new("RX 9070 XT", "Navi 48", Rdna4, "TSMC N4P", "2025-03-06", 64, 4096, 256, 128, 2400, 2970, 16, "GDDR6", 256, 20, 304),

        // NVIDIA GeForce RTX 30 (Ampere, Samsung 8N)
        new("RTX 3050", "GA107", Ampere, "Samsung 8N", "2024-02-02", 18, 2304, 72, 32, 1042, 1470, 6, "GDDR6", 96, 14, 70),
        new("RTX 3050", "GA106 / GA107", Ampere, "Samsung 8N", "2022-01-27", 20, 2560, 80, 32, 1552, 1777, 8, "GDDR6", 128, 14, 130),
        new("RTX 3060", "GA106", Ampere, "Samsung 8N", "2022-10-27", 28, 3584, 112, 48, 1320, 1777, 8, "GDDR6", 128, 15, 170),
        new("RTX 3060", "GA106", Ampere, "Samsung 8N", "2021-02-25", 28, 3584, 112, 48, 1320, 1777, 12, "GDDR6", 192, 15, 170),
        new("RTX 3060 Ti", "GA104", Ampere, "Samsung 8N", "2020-12-02", 38, 4864, 152, 80, 1410, 1665, 8, "GDDR6", 256, 14, 200),
        new("RTX 3070", "GA104", Ampere, "Samsung 8N", "2020-10-29", 46, 5888, 184, 96, 1500, 1725, 8, "GDDR6", 256, 14, 220),
        new("RTX 3070 Ti", "GA104", Ampere, "Samsung 8N", "2021-06-10", 48, 6144, 192, 96, 1580, 1770, 8, "GDDR6X", 256, 19, 290),
        new("RTX 3080", "GA102", Ampere, "Samsung 8N", "2020-09-17", 68, 8704, 272, 96, 1440, 1710, 10, "GDDR6X", 320, 19, 320),
        new("RTX 3080", "GA102", Ampere, "Samsung 8N", "2022-01-11", 70, 8960, 280, 96, 1260, 1710, 12, "GDDR6X", 384, 19, 350),
        new("RTX 3080 Ti", "GA102", Ampere, "Samsung 8N", "2021-06-03", 80, 10240, 320, 112, 1365, 1665, 12, "GDDR6X", 384, 19, 350),
        new("RTX 3090", "GA102", Ampere, "Samsung 8N", "2020-09-24", 82, 10496, 328, 112, 1395, 1695, 24, "GDDR6X", 384, 19.5, 350),
        new("RTX 3090 Ti", "GA102", Ampere, "Samsung 8N", "2022-03-29", 84, 10752, 336, 112, 1560, 1860, 24, "GDDR6X", 384, 21, 450),

        // NVIDIA GeForce RTX 40 (Ada Lovelace, TSMC 4N)
        new("RTX 4060", "AD107", Ada, "TSMC 4N", "2023-06-29", 24, 3072, 96, 48, 1830, 2460, 8, "GDDR6", 128, 17, 115),
        new("RTX 4060 Ti", "AD106", Ada, "TSMC 4N", "2023-05-24", 34, 4352, 136, 48, 2310, 2540, 8, "GDDR6", 128, 18, 160),
        new("RTX 4060 Ti", "AD106", Ada, "TSMC 4N", "2023-07-18", 34, 4352, 136, 48, 2310, 2540, 16, "GDDR6", 128, 18, 165),
        new("RTX 4070", "AD104", Ada, "TSMC 4N", "2023-04-13", 46, 5888, 184, 64, 1920, 2475, 12, "GDDR6X", 192, 21, 200),
        new("RTX 4070 SUPER", "AD104", Ada, "TSMC 4N", "2024-01-17", 56, 7168, 224, 80, 1980, 2475, 12, "GDDR6X", 192, 21, 220),
        new("RTX 4070 Ti", "AD104", Ada, "TSMC 4N", "2023-01-05", 60, 7680, 240, 80, 2310, 2610, 12, "GDDR6X", 192, 21, 285),
        new("RTX 4070 Ti SUPER", "AD103", Ada, "TSMC 4N", "2024-01-24", 66, 8448, 264, 96, 2340, 2610, 16, "GDDR6X", 256, 21, 285),
        new("RTX 4080", "AD103", Ada, "TSMC 4N", "2022-11-16", 76, 9728, 304, 112, 2210, 2505, 16, "GDDR6X", 256, 22.4, 320),
        new("RTX 4080 SUPER", "AD103", Ada, "TSMC 4N", "2024-01-31", 80, 10240, 320, 112, 2205, 2550, 16, "GDDR6X", 256, 23, 320),
        new("RTX 4090", "AD102", Ada, "TSMC 4N", "2022-10-12", 128, 16384, 512, 176, 2230, 2520, 24, "GDDR6X", 384, 21, 450),
        new("RTX 4090 D", "AD102", Ada, "TSMC 4N", "2023-12-28", 114, 14592, 456, 176, 2280, 2520, 24, "GDDR6X", 384, 21, 425),

        // NVIDIA GeForce RTX 50 (Blackwell, TSMC 4N)
        new("RTX 5050", "GB207", Blackwell, "TSMC 4N", "2025-07-01", 20, 2560, 80, 32, 2310, 2570, 8, "GDDR6", 128, 20, 130),
        new("RTX 5060", "GB206", Blackwell, "TSMC 4N", "2025-05-19", 30, 3840, 120, 48, 2280, 2500, 8, "GDDR7", 128, 28, 145),
        new("RTX 5060 Ti", "GB206", Blackwell, "TSMC 4N", "2025-04-16", 36, 4608, 144, 48, 2410, 2570, 8, "GDDR7", 128, 28, 180),
        new("RTX 5060 Ti", "GB206", Blackwell, "TSMC 4N", "2025-04-16", 36, 4608, 144, 48, 2410, 2570, 16, "GDDR7", 128, 28, 180),
        new("RTX 5070", "GB205", Blackwell, "TSMC 4N", "2025-03-05", 48, 6144, 192, 80, 2160, 2510, 12, "GDDR7", 192, 28, 250),
        new("RTX 5070 Ti", "GB203", Blackwell, "TSMC 4N", "2025-02-20", 70, 8960, 280, 96, 2300, 2450, 16, "GDDR7", 256, 28, 300),
        new("RTX 5080", "GB203", Blackwell, "TSMC 4N", "2025-01-30", 84, 10752, 336, 112, 2300, 2620, 16, "GDDR7", 256, 30, 360),
        new("RTX 5090", "GB202", Blackwell, "TSMC 4N", "2025-01-30", 170, 21760, 680, 176, 2010, 2410, 32, "GDDR7", 512, 28, 575),

        // Intel Arc (Alchemist on TSMC N6, Battlemage on TSMC N5)
        new("Arc A310", "ACM-G11", Alchemist, "TSMC N6", "2022-09-28", 6, 768, 32, 16, 2000, 2000, 4, "GDDR6", 64, 15.5, 75),
        new("Arc A380", "ACM-G11", Alchemist, "TSMC N6", "2022-06-14", 8, 1024, 64, 32, 2000, 2050, 6, "GDDR6", 96, 15.5, 75),
        new("Arc A580", "ACM-G10", Alchemist, "TSMC N6", "2023-10-10", 24, 3072, 192, 96, 1700, 1700, 8, "GDDR6", 256, 16, 175),
        new("Arc A750", "ACM-G10", Alchemist, "TSMC N6", "2022-10-14", 28, 3584, 224, 112, 2050, 2400, 8, "GDDR6", 256, 16, 225),
        new("Arc A770", "ACM-G10", Alchemist, "TSMC N6", "2022-10-14", 32, 4096, 256, 128, 2100, 2400, 8, "GDDR6", 256, 16, 225),
        new("Arc A770", "ACM-G10", Alchemist, "TSMC N6", "2022-10-14", 32, 4096, 256, 128, 2100, 2400, 16, "GDDR6", 256, 17.5, 225),
        new("Arc B570", "BMG-G21", Battlemage, "TSMC N5", "2025-01-16", 18, 2304, 144, 72, 1700, 2500, 10, "GDDR6", 160, 19, 150),
        new("Arc B580", "BMG-G21", Battlemage, "TSMC N5", "2024-12-13", 20, 2560, 160, 80, 1700, 2670, 12, "GDDR6", 192, 19, 190),
    ];

    /// <summary>
    /// The entry for an adapter name such as "AMD Radeon RX 7800 XT" or "NVIDIA GeForce RTX 4070 SUPER".
    /// The longest model name contained in the adapter name wins ("RTX 4070 Ti SUPER" over "RTX 4070 Ti");
    /// <paramref name="memoryBytes"/> (dedicated video memory) picks between same-name variants.
    /// </summary>
    public static GpuSpec? Find(string? adapterName, long? memoryBytes = null)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return null;
        }

        var name = Normalize(adapterName);
        var matches = All.Where(s => ContainsModel(name, Normalize(s.Model))).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        var longest = matches.Max(s => s.Model.Length);
        var candidates = matches.Where(s => s.Model.Length == longest).ToList();
        if (candidates.Count == 1 || memoryBytes is not > 0)
        {
            return candidates[0];
        }

        var gb = memoryBytes.Value / (1024.0 * 1024 * 1024);
        return candidates.MinBy(s => Math.Abs(s.MemoryGb - gb));
    }

    /// <summary>Upper case, (R)/(TM) and vendor words removed, single spaces.</summary>
    internal static string Normalize(string text)
    {
        var t = text.ToUpperInvariant().Replace("(R)", " ", StringComparison.Ordinal).Replace("(TM)", " ", StringComparison.Ordinal);
        return Spaces().Replace(t, " ").Trim();
    }

    /// <summary>Whole-word containment, so "RTX 4070" does not match inside "RTX 40700".</summary>
    private static bool ContainsModel(string name, string model)
    {
        var at = 0;
        while ((at = name.IndexOf(model, at, StringComparison.Ordinal)) >= 0)
        {
            var end = at + model.Length;
            var startOk = at == 0 || name[at - 1] == ' ';
            var endOk = end == name.Length || name[end] == ' ';
            if (startOk && endOk)
            {
                return true;
            }

            at = end;
        }

        return false;
    }

    public static string Clock(int mhz, CultureInfo culture) => mhz.ToString("N0", culture) + " MHz";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
