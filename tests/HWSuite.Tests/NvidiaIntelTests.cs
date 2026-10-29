using HWSuite.Core.Cpuid;
using HWSuite.Core.Gpu;
using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Core.Sensors;

namespace HWSuite.Tests;

public sealed class NvidiaIntelTests
{
    private static readonly Strings Pt = Strings.For("pt-BR");

    [Fact]
    public void Nvml_values_are_decoded()
    {
        Assert.Equal("Ada Lovelace", Nvml.Architecture(8));
        Assert.Equal("Ampere", Nvml.Architecture(7));
        Assert.Null(Nvml.Architecture(0xFFFFFFFF));
        Assert.Equal("CUDA 12.6", Nvml.CudaVersion(12060));
        Assert.Null(Nvml.CudaVersion(0));
        Assert.Equal(("10DE", "2684"), Nvml.PciIds(0x268410DE)); // RTX 4090
        Assert.True(Nvml.Alarming.HasFlag(NvidiaClockLimit.HardwareSlowdown));
        Assert.False(Nvml.Alarming.HasFlag(NvidiaClockLimit.PowerCap)); // under load, the power cap is normal
    }

    [Fact]
    public void Hybrid_cores_are_counted()
    {
        Assert.Equal((8, 16), CoreTopology.Split([.. Enumerable.Repeat((byte)1, 8), .. Enumerable.Repeat((byte)0, 16)]));
        Assert.Null(CoreTopology.Split([.. Enumerable.Repeat((byte)0, 8)])); // Ryzen 7: all the same
        Assert.Null(CoreTopology.Split([]));
    }

    [Theory]
    [InlineData("8086", "32.0.101.6078", "Intel Graphics 101.6078")]
    [InlineData("8086", "31.0.101.5590", "Intel Graphics 101.5590")]
    [InlineData("8086", "27.20.100.9616", "Intel Graphics 100.9616")]
    [InlineData("8086", "10.18.15.4256", null)]
    [InlineData("10DE", "32.0.15.6094", "NVIDIA 560.94")]
    public void Driver_package_names(string vendor, string version, string? expected) =>
        Assert.Equal(expected, GpuDrivers.Package(vendor, version, null));

    private static HardwareInventory WithNvidia(double limit, double standard)
    {
        var inv = new HardwareInventory { MachineName = "PC" };
        inv.Processors.Add(new ProcessorInfo { Name = "Intel Core i7-12700K", Cores = 12, Threads = 20, PerformanceCores = 8, EfficiencyCores = 4 });
        inv.Gpus.Add(new GpuInfo
        {
            Name = "NVIDIA GeForce RTX 3060",
            PciVendorId = "10DE",
            PciDeviceId = "2504",
            Architecture = "Ampere",
            ShaderUnits = 3584,
            MemoryBusBits = 192,
            MaxGpuClockMhz = 2100,
            MaxMemoryClockMhz = 7501,
            PowerLimitW = limit,
            PowerLimitDefaultW = standard,
            PowerLimitMinW = 100,
            PowerLimitMaxW = 182,
            SlowdownTemperatureC = 98,
            ShutdownTemperatureC = 101,
            ComputeVersion = "CUDA 12.6",
        });
        return inv;
    }

    private static SensorSnapshot Limits(bool thermalHw) => new(DateTimeOffset.Now,
    [
        new("/nvml/0/limit-powercap", "NVIDIA GeForce RTX 3060", "GPU", "Limit: Power", SensorKind.Load, 100, null, null),
        new("/nvml/0/limit-thermalhardware", "NVIDIA GeForce RTX 3060", "GPU", "Limit: Thermal (HW)", SensorKind.Load, thermalHw ? 100 : 0, null, null),
    ]);

    [Fact]
    public void Report_shows_nvidia_and_hybrid_details()
    {
        var sections = ReportModel.Build(WithNvidia(170, 170), Pt);
        var rows = sections.SelectMany(x => x.Groups).SelectMany(g => g.Rows).ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("8 P-cores + 4 E-cores", rows["Núcleos de desempenho / eficiência"]);
        Assert.Equal("Ampere", rows["Arquitetura"]);
        Assert.Equal("3.584", rows["Núcleos CUDA"]);
        Assert.Equal("192 bits", rows["Barramento de memória"]);
        Assert.Equal("170 W (padrão 170 W · ajustável 100 W–182 W)", rows["Limite de potência"]);
        Assert.Equal("reduz o clock a 98 °C · desliga a 101 °C", rows["Limites de temperatura"]);
        Assert.Equal("CUDA 12.6", rows["Computação"]);
    }

    [Fact]
    public void Maintenance_flags_lowered_power_and_limits_at_rest()
    {
        var now = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.FromHours(-3));
        var bad = MaintenanceChecks.Run(new MaintenanceInput(WithNvidia(120, 170), now) { Sensors = Limits(thermalHw: true) }, Pt);
        Assert.Contains(bad, x => x.Area == CheckArea.Graphics && x.Item.StartsWith("Limite de potência", StringComparison.Ordinal) && x.Verdict == Verdict.Attention);
        Assert.Contains(bad, x => x.Item.StartsWith("Limitação de clock", StringComparison.Ordinal) && x.Verdict == Verdict.Attention && x.Finding.Contains("Thermal (HW)", StringComparison.Ordinal));

        var good = MaintenanceChecks.Run(new MaintenanceInput(WithNvidia(170, 170), now) { Sensors = Limits(thermalHw: false) }, Pt);
        Assert.DoesNotContain(good, x => x.Item.StartsWith("Limite de potência", StringComparison.Ordinal));
        Assert.Contains(good, x => x.Item.StartsWith("Limitação de clock", StringComparison.Ordinal) && x.Verdict == Verdict.Ok); // the power cap alone is fine
    }
}
