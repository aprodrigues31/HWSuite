using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Npu;
using HWSuite.Core.Reporting;

namespace HWSuite.Tests;

public class NpuTests
{
    [Theory]
    [InlineData("1022", "17F0", "AMD Ryzen AI 9 HX 370 w/ Radeon 890M", "AMD XDNA 2 (Strix / Krackan / Strix Halo)", 50.0)]
    [InlineData("1022", "1502", "AMD Ryzen 7 8845HS w/ Radeon 780M Graphics", "AMD XDNA (Phoenix / Hawk Point)", 16.0)]
    [InlineData("1022", "1502", "AMD Ryzen 9 7940HS w/ Radeon 780M Graphics", "AMD XDNA (Phoenix / Hawk Point)", 10.0)]
    [InlineData("8086", "7D1D", "Intel(R) Core(TM) Ultra 7 155H", "Intel NPU 3 (Meteor Lake / Arrow Lake-H)", 11.0)]
    [InlineData("8086", "643E", "Intel(R) Core(TM) Ultra 7 258V", "Intel NPU 4 (Lunar Lake)", 48.0)]
    [InlineData("8086", "AD1D", "Intel(R) Core(TM) Ultra 9 285K", "Intel NPU 3 (Arrow Lake)", 13.0)]
    public void Npus_are_identified_by_pci_id(string vendor, string device, string processor, string family, double tops)
    {
        var npu = new NpuInfo { PciVendorId = vendor, PciDeviceId = device };
        NpuDatabase.Identify(npu, processor);
        Assert.Equal(family, npu.Family);
        Assert.Equal(tops, npu.Tops);
    }

    [Fact]
    public void Hexagon_is_recognized_by_name_and_unknown_ids_stay_empty()
    {
        var hexagon = new NpuInfo { Name = "Snapdragon(R) X Elite - X1E80100 - Qualcomm(R) Hexagon(TM) NPU" };
        NpuDatabase.Identify(hexagon, "Snapdragon(R) X Elite - X1E80100 - Qualcomm(R) Oryon(TM) CPU");
        Assert.Equal((45.0, "Qualcomm Hexagon (Snapdragon X)"), (hexagon.Tops!.Value, hexagon.Family));

        var unknown = new NpuInfo { PciVendorId = "8086", PciDeviceId = "0001" };
        NpuDatabase.Identify(unknown, null);
        Assert.Null(unknown.Family);
        Assert.Null(unknown.Tops);
    }

    [Fact]
    public void Report_has_an_npu_section_only_when_there_is_one()
    {
        var inventory = new HardwareInventory();
        Assert.DoesNotContain(ReportModel.Build(inventory, Strings.For("pt-BR")), s => s.Title == "NPU (acelerador de IA)");

        var npu = new NpuInfo { Name = "AMD NPU Compute Accelerator Device", Vendor = "AMD", PciVendorId = "1022", PciDeviceId = "17F0", DriverVersion = "32.0.203.258", DriverDate = "2025-05-12" };
        NpuDatabase.Identify(npu, "AMD Ryzen AI 9 HX 370");
        inventory.Npus.Add(npu);
        var rows = ReportModel.Build(inventory, Strings.For("pt-BR")).Single(s => s.Title == "NPU (acelerador de IA)").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("até 50 TOPS (INT8)", rows["Desempenho de IA (fabricante)"]);
        Assert.Equal("AMD XDNA 2 (Strix / Krackan / Strix Halo)", rows["Geração"]);
        Assert.Equal("32.0.203.258 / 12/05/2025", rows["Driver (versão / data)"]);
    }

    /// <summary>The real device query runs on Windows (the CI machine has no NPU, so it finds none) without failing.</summary>
    [Fact]
    public void Windows_provider_runs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inventory = new HardwareInventory();
        new HWSuite.Providers.Windows.WindowsNpuProvider().Contribute(inventory, null!);
        Assert.All(inventory.Npus, n => Assert.False(string.IsNullOrEmpty(n.Name)));
    }
}
