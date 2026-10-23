using HWSuite.Core.Gpu;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;

namespace HWSuite.Tests;

public class GpuDatabaseTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData("AMD Radeon RX 7800 XT", 16, "RX 7800 XT", 16)]
    [InlineData("AMD Radeon RX 7900 XTX", 24, "RX 7900 XTX", 24)]
    [InlineData("AMD Radeon RX 7900 XT", 20, "RX 7900 XT", 20)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 16, "RTX 4070 Ti SUPER", 16)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti", 12, "RTX 4070 Ti", 12)]
    [InlineData("NVIDIA GeForce RTX 4070", 12, "RTX 4070", 12)]
    [InlineData("NVIDIA GeForce RTX 3060", 12, "RTX 3060", 12)]
    [InlineData("NVIDIA GeForce RTX 3060", 8, "RTX 3060", 8)]
    [InlineData("NVIDIA GeForce RTX 4060 Ti", 16, "RTX 4060 Ti", 16)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", 16, "Arc A770", 16)]
    [InlineData("AMD Radeon RX 9070 XT", 16, "RX 9070 XT", 16)]
    public void Finds_the_model_and_variant(string adapter, int memoryGb, string model, int expectedGb)
    {
        // DXGI reports a little less than the full memory (reserved by the driver).
        var spec = GpuDatabase.Find(adapter, (long)(memoryGb * Gb * 0.98));
        Assert.NotNull(spec);
        Assert.Equal(model, spec.Model);
        Assert.Equal(expectedGb, spec.MemoryGb);
    }

    [Theory]
    [InlineData("AMD Radeon(TM) Graphics")]
    [InlineData("Microsoft Basic Render Driver")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB")]
    [InlineData("")]
    public void Unknown_adapters_have_no_reference(string adapter)
    {
        Assert.Null(GpuDatabase.Find(adapter));
        Assert.Null(GpuDatabase.Find(null));
    }

    [Fact]
    public void Units_match_shaders_for_every_entry()
    {
        // AMD: 64 stream processors per CU; NVIDIA Ampere and later: 128 CUDA cores per SM; Intel: 128 ALUs per Xe core.
        Assert.All(GpuDatabase.All, s =>
        {
            Assert.Equal(s.Shaders, s.Units!.Value * (s.UnitName == "CU" ? 64 : 128));
            Assert.True(s.BoostMhz >= s.BaseMhz, s.Model);
            Assert.True(s.BusBits % 32 == 0, s.Model);
        });
    }

    [Fact]
    public void Rx_7800_xt_matches_gpuz_and_the_amd_spec()
    {
        var spec = GpuDatabase.Find("AMD Radeon RX 7800 XT")!;
        Assert.Equal("Navi 32", spec.Chip);
        Assert.Equal(60, spec.Units);
        Assert.Equal(3840, spec.Shaders);
        Assert.Equal(624, spec.BandwidthGBps); // 256 bits x 19.5 Gbps
    }

    [Fact]
    public void Report_shows_the_reference_and_flags_missing_memory()
    {
        var inventory = new HardwareInventory();
        inventory.Gpus.Add(new GpuInfo { Name = "AMD Radeon RX 7800 XT", DedicatedMemoryBytes = 16 * Gb, Reference = GpuDatabase.Find("AMD Radeon RX 7800 XT") });
        inventory.Gpus.Add(new GpuInfo { Name = "NVIDIA GeForce RTX 4090", DedicatedMemoryBytes = 4 * Gb, Reference = GpuDatabase.Find("NVIDIA GeForce RTX 4090") });

        var groups = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Vídeo").Groups;
        var amd = groups[0].Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Navi 32 · RDNA 3 · TSMC N5 + N6", amd["Chip (referência)"]);
        Assert.Equal("60 CU · 3.840 shaders · 240 TMUs · 96 ROPs", amd["Unidades (referência)"]);
        Assert.Equal("1.800 MHz / 2.430 MHz", amd["Clock base / boost (referência)"]);
        Assert.Equal("16 GB GDDR6 · 256 bits · 19,5 Gbps · 624 GB/s", amd["Memória (referência)"]);
        Assert.Equal("263 W", amd["Consumo da placa (referência)"]);
        Assert.False(amd.ContainsKey("Conferência"));

        var fake = groups[1].Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.StartsWith("a placa informa 4 GB, mas este modelo tem 24 GB", fake["Conferência"], StringComparison.Ordinal);
    }
}
