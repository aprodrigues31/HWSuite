using System.Text;
using HWSuite.Core.Cpuid;
using HWSuite.Core.Model;

namespace HWSuite.Tests;

/// <summary>CPUID decoding against hand-built register dumps (no real CPU involved).</summary>
public class CpuidDecoderTests
{
    private sealed class FakeCpuid : ICpuidReader
    {
        private readonly Dictionary<(uint, uint), CpuidRegisters> _leaves = [];

        public FakeCpuid Set(uint leaf, uint subleaf, uint eax = 0, uint ebx = 0, uint ecx = 0, uint edx = 0)
        {
            _leaves[(leaf, subleaf)] = new CpuidRegisters(eax, ebx, ecx, edx);
            return this;
        }

        public FakeCpuid Vendor(string vendor, uint maxLeaf)
        {
            var b = Encoding.ASCII.GetBytes(vendor);
            return Set(0, 0, maxLeaf, BitConverter.ToUInt32(b, 0), BitConverter.ToUInt32(b, 8), BitConverter.ToUInt32(b, 4));
        }

        public FakeCpuid Brand(string brand)
        {
            var b = Encoding.ASCII.GetBytes(brand.PadRight(48, '\0'));
            for (var i = 0; i < 3; i++)
            {
                Set(0x8000_0002 + (uint)i, 0,
                    BitConverter.ToUInt32(b, i * 16), BitConverter.ToUInt32(b, i * 16 + 4),
                    BitConverter.ToUInt32(b, i * 16 + 8), BitConverter.ToUInt32(b, i * 16 + 12));
            }

            return this;
        }

        public CpuidRegisters Read(uint leaf, uint subleaf = 0) =>
            _leaves.TryGetValue((leaf, subleaf), out var r) ? r : default;
    }

    private static uint Bits(params int[] bits) => bits.Aggregate(0u, (acc, bit) => acc | (1u << bit));

    /// <summary>Deterministic cache leaf layout shared by Intel leaf 4 and AMD 8000_001Dh.</summary>
    private static (uint Eax, uint Ebx, uint Ecx) CacheLeaf(int type, int level, int sharedThreads, int ways, int lineSize, long sizeBytes)
    {
        var sets = sizeBytes / (ways * lineSize);
        var eax = (uint)(type | (level << 5) | ((sharedThreads - 1) << 14));
        var ebx = (uint)(((ways - 1) << 22) | (lineSize - 1));
        return (eax, ebx, (uint)(sets - 1));
    }

    [Fact]
    public void Decodes_intel_signature_brand_features_and_caches()
    {
        var l1d = CacheLeaf(type: 1, level: 1, sharedThreads: 2, ways: 12, lineSize: 64, sizeBytes: 48 * 1024);
        var l3 = CacheLeaf(type: 3, level: 3, sharedThreads: 12, ways: 12, lineSize: 64, sizeBytes: 12 * 1024 * 1024);

        var cpu = new FakeCpuid()
            .Vendor("GenuineIntel", 0x20)
            .Set(1, 0, eax: 0x000906A3, ecx: Bits(0, 1, 9, 12, 19, 20, 25, 28, 29, 30), edx: Bits(23, 25, 26))
            .Set(4, 0, l1d.Eax, l1d.Ebx, l1d.Ecx)
            .Set(4, 1, l3.Eax, l3.Ebx, l3.Ecx)
            .Set(7, 0, eax: 1, ebx: Bits(3, 5, 8, 29))
            .Set(7, 1, eax: Bits(4))
            .Set(0x8000_0000, 0, eax: 0x8000_0008)
            .Set(0x8000_0001, 0, edx: Bits(29))
            .Brand("12th Gen Intel(R) Core(TM) i7-1265U");

        var result = CpuidDecoder.Decode(cpu);

        Assert.Equal("GenuineIntel", result.Vendor);
        Assert.Equal("12th Gen Intel(R) Core(TM) i7-1265U", result.BrandString);
        Assert.Equal((6, 0x9A, 3), (result.Family, result.Model, result.Stepping));
        Assert.Contains("AVX2", result.InstructionSets);
        Assert.Contains("AVX-VNNI", result.InstructionSets);
        Assert.Contains("SHA", result.InstructionSets);
        Assert.Contains("x86-64", result.InstructionSets);
        Assert.DoesNotContain("AVX-512F", result.InstructionSets);
        Assert.False(result.HypervisorPresent);

        Assert.Equal(2, result.Caches.Count);
        Assert.Equal(48 * 1024, result.Caches[0].SizeBytes);
        Assert.Equal(CacheType.Data, result.Caches[0].Type);
        Assert.Equal(2, result.Caches[0].SharedByThreads);
        Assert.Equal(12L * 1024 * 1024, result.Caches[1].SizeBytes);
        Assert.Equal(3, result.Caches[1].Level);
    }

    [Fact]
    public void Decodes_amd_extended_family_and_topology_extension_caches()
    {
        var l3 = CacheLeaf(type: 3, level: 3, sharedThreads: 16, ways: 16, lineSize: 64, sizeBytes: 32 * 1024 * 1024);

        var cpu = new FakeCpuid()
            .Vendor("AuthenticAMD", 0x10)
            .Set(1, 0, eax: 0x00A20F12, ecx: Bits(0, 28, 31), edx: Bits(25, 26))
            .Set(0x8000_0000, 0, eax: 0x8000_0021)
            .Set(0x8000_0001, 0, ecx: Bits(2, 6, 22), edx: Bits(29))
            .Set(0x8000_001D, 0, l3.Eax, l3.Ebx, l3.Ecx);

        var result = CpuidDecoder.Decode(cpu);

        Assert.Equal((0x19, 0x21, 2), (result.Family, result.Model, result.Stepping));
        Assert.True(result.HypervisorPresent);
        Assert.Contains("AMD-V", result.InstructionSets);
        Assert.Contains("SSE4A", result.InstructionSets);
        Assert.Single(result.Caches);
        Assert.Equal(32L * 1024 * 1024, result.Caches[0].SizeBytes);
        Assert.Equal(16, result.Caches[0].SharedByThreads);
        Assert.Null(result.BrandString);
    }

    [Theory]
    [InlineData(0x000306C3u, 6, 0x3C, 3)] // Haswell: extended model applies to family 6
    [InlineData(0x00000F43u, 15, 4, 3)] // Pentium 4: family F with zero extended family
    [InlineData(0x00000533u, 5, 3, 3)] // Pentium MMX: no extended fields
    public void Signature_rules_follow_the_vendor_manuals(uint eax, int family, int model, int stepping) =>
        Assert.Equal((family, model, stepping), CpuidDecoder.DecodeSignature(eax));

    [Fact]
    public void Apply_overwrites_generic_smbios_name_with_brand_string()
    {
        var cpu = new ProcessorInfo { Name = "Intel(R) Core(TM) i7", Socket = "U3E1" };
        var result = new CpuidResult { Vendor = "GenuineIntel", BrandString = "Intel(R) Core(TM) i7-1265U", Signature = 0x906A3 };

        CpuidDecoder.Apply(result, cpu);

        Assert.Equal("Intel(R) Core(TM) i7-1265U", cpu.Name);
        Assert.Equal("Intel", cpu.Vendor);
        Assert.Equal("U3E1", cpu.Socket);
        Assert.Equal("0x000906A3", cpu.Signature);
    }
}
