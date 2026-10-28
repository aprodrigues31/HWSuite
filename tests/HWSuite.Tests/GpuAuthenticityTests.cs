using HWSuite.Core.Gpu;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Pci;
using HWSuite.Core.Reporting;

namespace HWSuite.Tests;

public class GpuAuthenticityTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static GpuInfo Card(string name, string vendor, string device, long memoryGb, string? featureLevel = "12_2", string? raytracing = "1.1", int maxSpeed = 4)
    {
        var gpu = new GpuInfo
        {
            Name = name,
            PciVendorId = vendor,
            PciDeviceId = device,
            DedicatedMemoryBytes = memoryGb * Gb,
            Direct3DFeatureLevel = featureLevel,
            RaytracingTier = raytracing,
            PcieLink = new PcieLink(maxSpeed, 16, maxSpeed, 16),
        };
        gpu.Reference = GpuDatabase.Find(gpu.Name, gpu.DedicatedMemoryBytes);
        gpu.Authenticity = GpuAuthenticity.Check(gpu);
        return gpu;
    }

    private static Dictionary<string, string> Rows(GpuInfo gpu)
    {
        var inventory = new HardwareInventory();
        inventory.Gpus.Add(gpu);
        return ReportModel.Build(inventory, Strings.For("pt-BR")).Single(x => x.Title == "Vídeo").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
    }

    [Fact]
    public void The_users_rx_7800_xt_is_genuine()
    {
        var gpu = Card("AMD Radeon RX 7800 XT", "1002", "747E", 16);
        gpu.CardMemoryClockMhz = 2438;
        gpu.Authenticity = GpuAuthenticity.Check(gpu);

        Assert.Empty(gpu.Authenticity.Findings);
        Assert.True(gpu.Authenticity.Checked);
        Assert.StartsWith("nenhum sinal de falsificação", Rows(gpu)["Autenticidade"], StringComparison.Ordinal);
    }

    [Fact]
    public void An_old_chip_sold_as_an_rtx_3060_is_flagged_on_every_count()
    {
        // GT 730 (GK208, 10DE:1287): DirectX 12 feature level 11_0, no ray tracing, PCIe 2.0, 2 GB
        // (the memory picks the closest variant, the 8 GB RTX 3060).
        var gpu = Card("NVIDIA GeForce RTX 3060", "10DE", "1287", 2, featureLevel: "11_0", raytracing: null, maxSpeed: 2);

        Assert.Equal(
            ["gpu.fake.deviceId", "gpu.fake.featureLevel", "gpu.fake.pcie", "gpu.memoryMismatch"],
            gpu.Authenticity!.Findings.Select(f => f.Key));
        var row = Rows(gpu)["Autenticidade"];
        Assert.StartsWith("possível placa remarcada ou falsificada: o ID PCI 10DE:1287 não é um ID conhecido do chip GA106 da RTX 3060", row, StringComparison.Ordinal);
        Assert.Contains("nível de recursos DirectX 12 11.0, mas a RTX 3060 suporta 12.2", row, StringComparison.Ordinal);
        Assert.Contains("a placa suporta PCIe 2.0, mas a RTX 3060 é PCIe 4.0", row, StringComparison.Ordinal);
        Assert.Contains("a placa informa 2 GB, mas o modelo tem 8 GB", row, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_of_another_model_names_that_model()
    {
        var gpu = Card("NVIDIA GeForce RTX 4090", "10DE", "2487", 24); // GA104 [GeForce RTX 3060 Ti]
        var finding = Assert.Single(gpu.Authenticity!.Findings);
        Assert.Equal("gpu.fake.deviceIdOther", finding.Key);
        Assert.Contains("RTX 3060 Ti", (string)finding.Args[3], StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_and_pci_vendor_must_agree()
    {
        var gpu = Card("AMD Radeon RX 6600", "10DE", "2504", 8);
        Assert.Equal("gpu.fake.vendor", gpu.Authenticity!.Findings[0].Key);
        Assert.Contains("o nome diz RX 6600, mas o fabricante PCI é NVIDIA", Rows(gpu)["Autenticidade"], StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_ray_tracing_alone_is_reported()
    {
        var gpu = Card("AMD Radeon RX 7800 XT", "1002", "747E", 16, raytracing: null);
        Assert.Equal("gpu.fake.raytracing", Assert.Single(gpu.Authenticity!.Findings).Key);
    }

    [Fact]
    public void Rdna4_and_blackwell_are_pcie_5()
    {
        Assert.Empty(Card("NVIDIA GeForce RTX 5070", "10DE", "2F04", 12, maxSpeed: 5).Authenticity!.Findings);
        Assert.Equal("gpu.fake.pcie", Assert.Single(Card("AMD Radeon RX 9070 XT", "1002", "7550", 16, maxSpeed: 4).Authenticity!.Findings).Key);
    }

    [Fact]
    public void A_card_on_the_generic_driver_is_identified_by_its_pci_id()
    {
        var gpu = Card("Microsoft Basic Display Adapter", "1002", "747E", 0, featureLevel: null);
        Assert.Null(gpu.Reference);
        Assert.Equal("RX 7700 XT / RX 7800 XT", Rows(gpu)["Modelo pelo ID PCI"]);
    }

    [Fact]
    public void Every_model_has_pci_ids_including_laptop_variants()
    {
        var known = GpuDeviceIds.Models.Values.SelectMany(m => m).ToHashSet();
        Assert.All(GpuDatabase.All, spec => Assert.Contains(spec.Model, known));
        Assert.Contains("RTX 4060", GpuDeviceIds.Models[(0x10DE, 0x28E0)]); // AD107M, laptop
        Assert.Contains("Arc A770", GpuDeviceIds.Models[(0x8086, 0x56A0)]);
        Assert.DoesNotContain(GpuDeviceIds.Models.Keys, k => k == (0x1002, 0xAB30)); // HDMI audio function
    }
}
