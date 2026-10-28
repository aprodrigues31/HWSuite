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
        foreach (var gpu in inventory.Gpus)
        {
            gpu.Authenticity = GpuAuthenticity.Check(gpu);
        }

        var groups = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Vídeo").Groups;
        var amd = groups[0].Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Navi 32 · RDNA 3 · TSMC N5 + N6 · 346 mm² · 28.100 milhões de transistores", amd["Chip (referência)"]); // GPU-Z: 346 mm², 28100M
        Assert.Equal("60 CU · 3.840 shaders · 240 TMUs · 96 ROPs", amd["Unidades (referência)"]);
        Assert.Equal("1.800 MHz / 2.430 MHz", amd["Clock base / boost (referência)"]);
        Assert.Equal("16 GB GDDR6 · 256 bits · 19,5 Gbps · 624 GB/s", amd["Memória (referência)"]);
        Assert.Equal("263 W", amd["Consumo da placa (referência)"]);
        Assert.False(amd.ContainsKey("Autenticidade")); // no PCI id or other data to check against

        var fake = groups[1].Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("possível placa remarcada ou falsificada: a placa informa 4 GB, mas o modelo tem 24 GB (ou é outra variante)", fake["Autenticidade"]);
    }

    [Fact]
    public void Gpuz_fields_for_the_users_rx_7800_xt()
    {
        // Values from GPU-Z 2.71 on the user's ASRock RX 7800 XT Steel Legend.
        var gpu = new GpuInfo
        {
            Name = "AMD Radeon RX 7800 XT",
            PciVendorId = "1002",
            PciDeviceId = "747E",
            PciSubsystemId = "53271849",
            Revision = 0xC8,
            DedicatedMemoryBytes = 16 * Gb,
            MemoryVendor = HWSuite.Core.Gpu.GpuDrivers.AmdMemoryVendor(1),
            DriverPackage = HWSuite.Core.Gpu.GpuDrivers.Package("1002", "32.0.32015.7029", "26.10.1"),
            Direct3DFeatureLevel = "12_2",
            ShaderModel = "6.8",
            RaytracingTier = "1.1",
            LargestBarBytes = 16 * Gb,
        };
        gpu.Apis.AddRange(["OpenCL", "DirectCompute", "DirectML", "Vulkan", "OpenGL"]);
        var inventory = new HardwareInventory();
        inventory.Gpus.Add(gpu);
        gpu.Reference = GpuDatabase.Find(gpu.Name, gpu.DedicatedMemoryBytes);
        gpu.Subvendor = HWSuite.Core.Pci.PciIds.CardMaker(0x1849);

        var rows = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Vídeo").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("ASRock", rows["Fabricante da placa"]);
        Assert.Equal("1002:747E / 53271849 · rev C8", rows["ID PCI (fornecedor:dispositivo / subsistema)"]); // GPU-Z: 1002 747E - 1849 5327, rev C8
        Assert.Equal("Samsung", rows["Fabricante da memória"]);
        Assert.Equal("AMD Software: Adrenalin Edition 26.10.1", rows["Pacote do driver"]);
        Assert.Equal("12 (12_2) · Shader Model 6.8", rows["DirectX"]);
        Assert.Equal("Tier 1.1", rows["Ray tracing (DXR)"]);
        Assert.Equal("OpenCL · DirectCompute · DirectML · Vulkan · OpenGL", rows["APIs"]);
        Assert.Equal("Ativado (janela de 16 GB)", rows["Resizable BAR"]);
        Assert.Equal("233,3 GPixel/s · 583,2 GTexel/s", rows["Taxa de preenchimento (referência)"]); // reference boost 2430 MHz
    }

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.5222", "552.22")]
    [InlineData("10.0", null)]
    [InlineData(null, null)]
    public void Nvidia_release_from_the_windows_driver_version(string? version, string? expected)
    {
        Assert.Equal(expected, HWSuite.Core.Gpu.GpuDrivers.NvidiaRelease(version));
    }

    [Fact]
    public void Driver_package_names_and_card_makers()
    {
        Assert.Equal("NVIDIA 560.94", HWSuite.Core.Gpu.GpuDrivers.Package("10DE", "32.0.15.6094", null));
        Assert.Null(HWSuite.Core.Gpu.GpuDrivers.Package("8086", "32.0.101.6078", null));
        Assert.Equal("SK Hynix", HWSuite.Core.Gpu.GpuDrivers.AmdMemoryVendor(6));
        Assert.Null(HWSuite.Core.Gpu.GpuDrivers.AmdMemoryVendor(99));
        Assert.Equal("Sapphire", HWSuite.Core.Pci.PciIds.CardMaker(0x1DA2));
        Assert.Null(HWSuite.Core.Pci.PciIds.CardMaker(0x0001));
        Assert.Equal((628d, 28_300), GpuDatabase.Die("GA102"));
        Assert.Equal((276d, 12_000), GpuDatabase.Die("GA106 / GA107")); // shared entry: first chip
    }

    [Fact]
    public void Collector_fills_subvendor_and_reference()
    {
        var provider = new FixedGpu(new GpuInfo { Name = "AMD Radeon RX 7800 XT", PciSubsystemId = "53271849", DedicatedMemoryBytes = 16 * Gb });
        var inventory = new HWSuite.Core.Collection.InventoryCollector([provider]).Collect();
        var gpu = Assert.Single(inventory.Gpus);
        Assert.Equal("ASRock", gpu.Subvendor);
        Assert.Equal("RX 7800 XT", gpu.Reference!.Model);
    }

    private sealed class FixedGpu(GpuInfo gpu) : HWSuite.Core.Collection.IInventoryProvider
    {
        public string Name => "fixed";

        public bool IsSupported => true;

        public void Contribute(HardwareInventory inventory, HWSuite.Core.Collection.ProviderContext context) => inventory.Gpus.Add(gpu);
    }
}
