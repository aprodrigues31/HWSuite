using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HWSuite.Core.Model;

namespace HWSuite.Core.Cpuid;

/// <summary>The four registers returned by one CPUID invocation.</summary>
public readonly record struct CpuidRegisters(uint Eax, uint Ebx, uint Ecx, uint Edx);

/// <summary>
/// Source of CPUID results. The real implementation executes the instruction; tests feed
/// recorded dumps so decoding can be validated without the physical CPU.
/// </summary>
public interface ICpuidReader
{
    CpuidRegisters Read(uint leaf, uint subleaf = 0);
}

/// <summary>Result of decoding CPUID, before it is merged into a <see cref="ProcessorInfo"/>.</summary>
public sealed class CpuidResult
{
    public string Vendor { get; init; } = "";
    public string? BrandString { get; init; }
    public int Family { get; init; }
    public int Model { get; init; }
    public int Stepping { get; init; }
    public uint Signature { get; init; }
    public bool HypervisorPresent { get; init; }
    public IReadOnlyList<string> InstructionSets { get; init; } = [];
    public IReadOnlyList<CacheInfo> Caches { get; init; } = [];
}

public static class CpuidDecoder
{
    public static CpuidResult Decode(ICpuidReader cpuid)
    {
        ArgumentNullException.ThrowIfNull(cpuid);

        var leaf0 = cpuid.Read(0);
        var maxLeaf = leaf0.Eax;
        var vendor = RegistersToAscii(leaf0.Ebx, leaf0.Edx, leaf0.Ecx);
        var maxExtended = cpuid.Read(0x8000_0000).Eax;

        var leaf1 = maxLeaf >= 1 ? cpuid.Read(1) : default;
        var (family, model, stepping) = DecodeSignature(leaf1.Eax);

        var leaf7 = maxLeaf >= 7 ? cpuid.Read(7) : default;
        var leaf7Sub1 = maxLeaf >= 7 && leaf7.Eax >= 1 ? cpuid.Read(7, 1) : default;
        var ext1 = maxExtended >= 0x8000_0001 ? cpuid.Read(0x8000_0001) : default;

        return new CpuidResult
        {
            Vendor = vendor,
            BrandString = maxExtended >= 0x8000_0004 ? ReadBrandString(cpuid) : null,
            Family = family,
            Model = model,
            Stepping = stepping,
            Signature = leaf1.Eax,
            HypervisorPresent = Bit(leaf1.Ecx, 31),
            InstructionSets = DecodeInstructionSets(leaf1, leaf7, leaf7Sub1, ext1),
            Caches = DecodeCaches(cpuid, vendor, maxLeaf, maxExtended, ext1),
        };
    }

    /// <summary>Merges a decoded result into every processor package (packages are identical).</summary>
    public static void Apply(CpuidResult result, ProcessorInfo cpu)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(cpu);

        // CPUID is the authoritative source for these; SMBIOS strings are often generic.
        if (!string.IsNullOrWhiteSpace(result.BrandString))
        {
            cpu.Name = result.BrandString;
        }

        cpu.Vendor = VendorDisplayName(result.Vendor) ?? cpu.Vendor;
        cpu.Family = result.Family;
        cpu.Model = result.Model;
        cpu.Stepping = result.Stepping;
        cpu.Signature = string.Create(CultureInfo.InvariantCulture, $"0x{result.Signature:X8}");
        cpu.HypervisorPresent = result.HypervisorPresent;
        cpu.InstructionSets.Clear();
        cpu.InstructionSets.AddRange(result.InstructionSets);
        cpu.Caches.Clear();
        cpu.Caches.AddRange(result.Caches);
    }

    internal static (int Family, int Model, int Stepping) DecodeSignature(uint eax)
    {
        var stepping = (int)(eax & 0xF);
        var baseModel = (int)((eax >> 4) & 0xF);
        var baseFamily = (int)((eax >> 8) & 0xF);
        var extModel = (int)((eax >> 16) & 0xF);
        var extFamily = (int)((eax >> 20) & 0xFF);

        var family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;
        var model = baseFamily is 0x6 or 0xF ? (extModel << 4) + baseModel : baseModel;
        return (family, model, stepping);
    }

    internal static string? VendorDisplayName(string vendor) => vendor switch
    {
        "GenuineIntel" => "Intel",
        "AuthenticAMD" => "AMD",
        "HygonGenuine" => "Hygon",
        "CentaurHauls" or "  Shanghai  " => "Zhaoxin",
        "" => null,
        _ => vendor,
    };

    private static List<string> DecodeInstructionSets(CpuidRegisters l1, CpuidRegisters l7, CpuidRegisters l7s1, CpuidRegisters e1)
    {
        var sets = new List<string>();
        void Add(bool present, string name)
        {
            if (present)
            {
                sets.Add(name);
            }
        }

        Add(Bit(l1.Edx, 23), "MMX");
        Add(Bit(l1.Edx, 25), "SSE");
        Add(Bit(l1.Edx, 26), "SSE2");
        Add(Bit(l1.Ecx, 0), "SSE3");
        Add(Bit(l1.Ecx, 9), "SSSE3");
        Add(Bit(l1.Ecx, 19), "SSE4.1");
        Add(Bit(l1.Ecx, 20), "SSE4.2");
        Add(Bit(e1.Ecx, 6), "SSE4A");
        Add(Bit(e1.Edx, 29), "x86-64");
        Add(Bit(l1.Ecx, 5), "VT-x");
        Add(Bit(e1.Ecx, 2), "AMD-V");
        Add(Bit(l1.Ecx, 25), "AES");
        Add(Bit(l1.Ecx, 1), "PCLMULQDQ");
        Add(Bit(l7.Ebx, 29), "SHA");
        Add(Bit(l1.Ecx, 28), "AVX");
        Add(Bit(l7.Ebx, 5), "AVX2");
        Add(Bit(l7s1.Eax, 4), "AVX-VNNI");
        Add(Bit(l1.Ecx, 12), "FMA3");
        Add(Bit(l1.Ecx, 29), "F16C");
        Add(Bit(l7.Ebx, 3), "BMI1");
        Add(Bit(l7.Ebx, 8), "BMI2");
        Add(Bit(l7.Ebx, 19), "ADX");
        Add(Bit(l1.Ecx, 30), "RDRAND");
        Add(Bit(l7.Ebx, 18), "RDSEED");
        Add(Bit(l7.Ebx, 16), "AVX-512F");
        Add(Bit(l7.Ebx, 17), "AVX-512DQ");
        Add(Bit(l7.Ebx, 28), "AVX-512CD");
        Add(Bit(l7.Ebx, 30), "AVX-512BW");
        Add(Bit(l7.Ebx, 31), "AVX-512VL");
        Add(Bit(l7.Ecx, 11), "AVX-512VNNI");
        Add(Bit(l7.Edx, 24), "AMX-TILE");
        Add(Bit(l7.Edx, 22), "AMX-BF16");
        Add(Bit(l7.Edx, 25), "AMX-INT8");
        return sets;
    }

    /// <summary>
    /// Deterministic cache parameters: leaf 4 on Intel, leaf 8000_001Dh on AMD/Hygon
    /// (when topology extensions are reported). Both share the same register layout.
    /// </summary>
    private static List<CacheInfo> DecodeCaches(ICpuidReader cpuid, string vendor, uint maxLeaf, uint maxExtended, CpuidRegisters ext1)
    {
        uint leaf;
        if (vendor is "AuthenticAMD" or "HygonGenuine")
        {
            var topologyExtensions = Bit(ext1.Ecx, 22);
            if (!topologyExtensions || maxExtended < 0x8000_001D)
            {
                return [];
            }

            leaf = 0x8000_001D;
        }
        else
        {
            if (maxLeaf < 4)
            {
                return [];
            }

            leaf = 4;
        }

        var caches = new List<CacheInfo>();
        for (uint sub = 0; sub < 16; sub++)
        {
            var r = cpuid.Read(leaf, sub);
            var type = (int)(r.Eax & 0x1F);
            if (type == 0)
            {
                break;
            }

            var ways = (int)((r.Ebx >> 22) & 0x3FF) + 1;
            var partitions = (int)((r.Ebx >> 12) & 0x3FF) + 1;
            var lineSize = (int)(r.Ebx & 0xFFF) + 1;
            var sets = (long)r.Ecx + 1;

            caches.Add(new CacheInfo
            {
                Level = (int)((r.Eax >> 5) & 0x7),
                Type = type switch { 1 => CacheType.Data, 2 => CacheType.Instruction, _ => CacheType.Unified },
                Ways = ways,
                LineSizeBytes = lineSize,
                SizeBytes = ways * partitions * lineSize * sets,
                SharedByThreads = (int)((r.Eax >> 14) & 0xFFF) + 1,
            });
        }

        return caches;
    }

    private static string? ReadBrandString(ICpuidReader cpuid)
    {
        var builder = new StringBuilder(48);
        for (uint leaf = 0x8000_0002; leaf <= 0x8000_0004; leaf++)
        {
            var r = cpuid.Read(leaf);
            builder.Append(RegistersToAscii(r.Eax, r.Ebx, r.Ecx, r.Edx));
        }

        var brand = builder.ToString().Trim();
        while (brand.Contains("  ", StringComparison.Ordinal))
        {
            brand = brand.Replace("  ", " ", StringComparison.Ordinal);
        }

        return brand.Length == 0 ? null : brand;
    }

    private static string RegistersToAscii(params uint[] registers)
    {
        var bytes = new byte[registers.Length * 4];
        for (var i = 0; i < registers.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), registers[i]);
        }

        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static bool Bit(uint value, int bit) => ((value >> bit) & 1) != 0;
}
