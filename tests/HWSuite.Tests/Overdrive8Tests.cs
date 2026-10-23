using HWSuite.Core.Gpu;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;

namespace HWSuite.Tests;

public class Overdrive8Tests
{
    [Fact]
    public void Default_clocks_come_from_gfxclk_and_uclk_fmax()
    {
        var table = new[]
        {
            new Od8Setting(Overdrive8.GfxClockMax, 500, 3000, 2520),
            new Od8Setting(Overdrive8.GfxClockMin, 500, 3000, 500),
            new Od8Setting(Overdrive8.MemoryClockMax, 2400, 2700, 2438),
            new Od8Setting(Overdrive8.PowerPercentage, -10, 15, 0),
        };

        Assert.Equal((2520, 2438), Overdrive8.DefaultClocks(table));
    }

    [Fact]
    public void Missing_or_implausible_values_are_left_out()
    {
        // Newer drivers report some clocks as offsets (default 0) rather than absolute MHz.
        var table = new[] { new Od8Setting(Overdrive8.GfxClockMax, -500, 500, 0) };
        Assert.Equal((null, null), Overdrive8.DefaultClocks(table));
        Assert.Equal((null, null), Overdrive8.DefaultClocks([]));
    }

    [Fact]
    public void Names_for_the_raw_dump()
    {
        Assert.Equal("GFXCLK_FMAX", Overdrive8.Name(0));
        Assert.Equal("UCLK_FMAX", Overdrive8.Name(8));
        Assert.Equal("ID_200", Overdrive8.Name(200));
    }

    /// <summary>The user's ASRock RX 7800 XT Steel Legend (driver table of 2026-10-22; GPU-Z shows 2438 MHz and 624.1 GB/s).</summary>
    [Fact]
    public void Report_shows_the_card_clocks_of_the_users_card()
    {
        var table = new[]
        {
            new Od8Setting(Overdrive8.GfxClockMax, 500, 5000, 2580),
            new Od8Setting(Overdrive8.GfxClockMin, 500, 5000, 500),
            new Od8Setting(Overdrive8.MemoryClockMax, 2438, 3000, 2438),
            new Od8Setting(Overdrive8.PowerPercentage, -10, 15, 0),
        };
        var (gpu, memory) = Overdrive8.DefaultClocks(table);
        var card = new GpuInfo
        {
            Name = "AMD Radeon RX 7800 XT",
            DedicatedMemoryBytes = 16L << 30,
            CardGpuClockMhz = gpu,
            CardMemoryClockMhz = memory,
        };
        card.Reference = GpuDatabase.Find(card.Name, card.DedicatedMemoryBytes);
        var inventory = new HardwareInventory();
        inventory.Gpus.Add(card);

        var rows = ReportModel.Build(inventory, Strings.For("pt-BR"))
            .Single(x => x.Title == "Vídeo").Groups.Single().Rows.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("memória 2.438 MHz (19,5 Gbps · 624,1 GB/s) · limite da GPU no Adrenalin 2.580 MHz", rows["Clocks da placa (driver)"]);
    }
}
