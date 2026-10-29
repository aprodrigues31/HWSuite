using HWSuite.Core.Cpuid;
using HWSuite.Core.Gpu;
using HWSuite.Core.Model;

namespace HWSuite.Tests;

public sealed class HardwareDatabaseTests
{
    [Theory]
    [InlineData("AMD Ryzen 7 5800XT 8-Core Processor", "Ryzen 7 5800XT", false)]
    [InlineData("AMD Ryzen 7 5800X 8-Core Processor", "Ryzen 7 5800X", false)]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", "Ryzen 7 5800X3D", false)]
    [InlineData("AMD Ryzen 5 5600G with Radeon Graphics", "Ryzen 5 5600G", false)]
    [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", "Ryzen 9 9950X3D", false)]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600KF", "Core i5-13600K", true)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12400F", "Core i5-12400", true)]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", "Core i7-8700K", false)]
    [InlineData("Intel(R) Core(TM) Ultra 7 265K", "Core Ultra 7 265K", false)]
    public void Processors_are_found(string name, string model, bool noGraphics)
    {
        var spec = CpuDatabase.Find(name, out var f);
        Assert.NotNull(spec);
        Assert.Equal(model, spec.Model);
        Assert.Equal(noGraphics, f);
    }

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900KS")] // other clocks: not guessed
    [InlineData("Intel(R) Xeon(R) Processor @ 2.80GHz")]
    [InlineData("AMD EPYC 9V74 80-Core Processor")]
    [InlineData("")]
    public void Unknown_processors_have_no_reference(string name) => Assert.Null(CpuDatabase.Find(name, out _));

    [Fact]
    public void Processor_entries_are_consistent()
    {
        Assert.All(CpuDatabase.All, c =>
        {
            Assert.True(c.BoostMhz >= c.BaseMhz, c.Model);
            Assert.True(c.Threads >= c.Cores && c.Threads <= c.Cores * 2, c.Model);
            if (c.PerformanceCores is { } p && c.EfficiencyCores is { } e)
            {
                Assert.Equal(c.Cores, p + e);
                Assert.True(c.Threads == (p * 2) + e || c.Threads == c.Cores, c.Model); // Hyper-Threading on P-cores only (none on Arrow Lake)
            }
        });
        Assert.Equal(CpuDatabase.All.Count, CpuDatabase.All.Select(c => c.Model).Distinct().Count());
    }

    [Theory]
    [InlineData("TUF GAMING B550M-PLUS", "B550", "AM4")]
    [InlineData("ROG STRIX X870E-E GAMING WIFI", "X870E", "AM5")]
    [InlineData("PRO B760M-A DDR4 (MS-7D99)", "B760", "LGA 1700")]
    [InlineData("Z390 AORUS PRO", "Z390", "LGA 1151")]
    [InlineData("B650 EAGLE AX", "B650", "AM5")]
    [InlineData("A320M-K", "A320", "AM4")]
    public void Chipsets_are_found_in_board_names(string board, string chipset, string socket)
    {
        var chip = Chipsets.Find(board);
        Assert.NotNull(chip);
        Assert.Equal(chipset, chip.Name);
        Assert.Equal(socket, chip.Socket);
    }

    [Theory]
    [InlineData("Virtual Machine")]
    [InlineData("System Product Name")]
    [InlineData("")]
    public void Boards_without_a_known_chipset(string? board) => Assert.Null(Chipsets.Find(board));

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1050 Ti", "GTX 1050 Ti", "GP107")]
    [InlineData("AMD Radeon RX 580 2048SP", "RX 580", "Polaris 20")]
    [InlineData("NVIDIA GeForce RTX 2060 SUPER", "RTX 2060 SUPER", "TU106")]
    [InlineData("AMD Radeon RX 5700 XT", "RX 5700 XT", "Navi 10")]
    [InlineData("Radeon RX Vega 64", "RX Vega 64", "Vega 10")]
    public void Older_graphics_cards_are_found(string adapter, string model, string chip)
    {
        var spec = GpuDatabase.Find(adapter);
        Assert.NotNull(spec);
        Assert.Equal(model, spec.Model);
        Assert.Equal(chip, spec.Chip);
        Assert.NotNull(GpuDatabase.Die(spec.Chip));
    }

    [Fact]
    public void Gtx_1050_ti_device_id_is_known() =>
        Assert.Contains("GTX 1050 Ti", GpuDeviceIds.Models[(0x10DE, 0x1C82)]);
}
