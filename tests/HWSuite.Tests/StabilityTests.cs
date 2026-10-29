using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;
using HWSuite.Core.Stability;

namespace HWSuite.Tests;

[Collection(SerialTests.Name)]
public sealed class StabilityTests
{
    private static readonly Strings Pt = Strings.For("pt-BR");

    [Fact]
    public void Memory_test_passes_on_good_memory_and_catches_a_flipped_bit()
    {
        var good = MemoryTest.Run(64L << 20, null, passes: 1, onTick: null, threads: 2);
        Assert.True(good.Passed);
        Assert.Equal(1, good.Pass);
        Assert.Equal(5L * (64L << 20), good.BytesVerified); // five patterns, each block checked once

        var bad = MemoryTest.Run(64L << 20, null, 1, null, 2, (offset, value) => value ^ 0x10, CancellationToken.None);
        Assert.False(bad.Passed);
        Assert.Equal(5, bad.Errors); // one flipped bit per pattern
        Assert.Equal(0x10UL, bad.FirstErrors[0].Bits);
    }

    [Fact]
    public void Memory_patterns_differ_between_neighbours()
    {
        Assert.NotEqual(MemoryTest.Value(1, 0, 0), MemoryTest.Value(1, 1, 0));
        Assert.Equal(1UL, MemoryTest.Value(2, 0, 0));
        Assert.Equal(2UL, MemoryTest.Value(2, 1, 0));
        Assert.Equal(MemoryTest.Value(4, 123, 7), MemoryTest.Value(4, 123, 7)); // random pattern is reproducible
    }

    private static SensorSnapshot Snap(double cpuTemp) => new(DateTimeOffset.Now,
    [
        new("/cpu/t", "AMD Ryzen 7 5800XT", "CPU", "Core (Tctl/Tdie)", SensorKind.Temperature, cpuTemp, null, null),
        new("/cpu/p", "AMD Ryzen 7 5800XT", "CPU", "Package", SensorKind.Power, 110, null, null),
    ]);

    [Fact]
    public void Stability_test_runs_and_reports_peaks()
    {
        var result = StabilityTest.Run(new StabilityOptions(TimeSpan.FromSeconds(3), Cpu: true, Gpu: false, Memory: false), () => Snap(70), gpu: null);
        Assert.True(result.Passed);
        Assert.Equal(StabilityStop.Completed, result.Stop);
        Assert.Equal(0, result.CpuErrors);
        Assert.Equal(70, result.MaxCpuTemperature);
        Assert.Equal(110, result.MaxCpuPower);
        Assert.True(result.Timeline.Times.Count >= 2);
    }

    [Fact]
    public void Overheating_stops_the_test()
    {
        var result = StabilityTest.Run(new StabilityOptions(TimeSpan.FromSeconds(30), Cpu: true, Gpu: false, Memory: false), () => Snap(99), gpu: null);
        Assert.Equal(StabilityStop.CpuTemperature, result.Stop);
        Assert.False(result.Passed);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(15));
    }

    private static HardwareInventory Inventory(string machine = "PC-01", string serial = "ABC123")
    {
        var inv = new HardwareInventory { MachineName = machine };
        inv.System = new() { Manufacturer = "Dell", Product = "OptiPlex 7010", SerialNumber = serial };
        inv.OperatingSystem = new() { Name = "Windows 11 Pro", DisplayVersion = "25H2", Build = "26200.1" };
        inv.Processors.Add(new ProcessorInfo { Name = "Intel Core i5-13500", Cores = 14, Threads = 20 });
        inv.Memory = new() { TotalInstalledBytes = 16L << 30 };
        inv.Disks.Add(new DiskInfo { Index = 0, Model = "Samsung 980", SerialNumber = "S1", Health = new DiskHealth { PercentageUsed = 12, TemperatureCelsius = 41 } });
        return inv;
    }

    [Fact]
    public void Stability_report_and_record()
    {
        var result = StabilityTest.Run(new StabilityOptions(TimeSpan.FromSeconds(2), Cpu: true, Gpu: false, Memory: false), () => Snap(65), gpu: null);
        var order = new ServiceOrder("Escritório Exemplo", null, "Recepção", "77", null, null);
        var company = new CompanyProfile("MML", null, null, null, "Técnico", null);
        var html = StabilityReportWriter.ToHtml(result, Inventory(), order, company, Pt);
        Assert.Contains("Teste de estabilidade", html, StringComparison.Ordinal);
        Assert.Contains("Aprovado", html, StringComparison.Ordinal);
        Assert.StartsWith("hwsuite-estabilidade-", StabilityReportWriter.FileName(result, order, "PC-01", "html"), StringComparison.Ordinal);

        var record = AttendanceRecord.From(result, Inventory(), order, company, "0.21.0", "x.html");
        Assert.Equal("stability", record.Kind);
        Assert.True(record.Test!.Passed);
        var back = AttendanceRecord.Parse(record.ToJson())!;
        Assert.Equal(record.Test, back.Test);
        Assert.Equal("Escritório Exemplo", back.Client);
    }

    private static AttendanceRecord Visit(DateTimeOffset at, double idle, int wear, double free, int problems, string machine = "PC-01") => new()
    {
        At = at,
        Client = "Escritório Exemplo",
        Machine = machine,
        Serial = machine + "-SN",
        Overall = problems > 0 ? Verdict.Problem : Verdict.Ok,
        Problem = problems,
        CpuIdleTemperature = idle,
        Disks = [new DiskRecord("Samsung 980", "S1", wear, 40, 0, 1000, "Good")],
        Volumes = [new VolumeRecord("C:", free, 1, 100)],
        Findings = problems > 0 ? ["Espaço livre · C:: 5% livre"] : [],
    };

    [Fact]
    public void Comparison_shows_what_improved_and_what_got_worse()
    {
        var now = DateTimeOffset.Now;
        var rows = AttendanceComparison.Compare(Visit(now.AddMonths(-6), 62, 10, 5, 1), Visit(now, 45, 14, 40, 0), Pt);
        Assert.Equal(1, rows.Single(r => r.Item == "Processador em repouso").Trend); // cleaning cooled it down
        Assert.Equal(-1, rows.Single(r => r.Item.StartsWith("Desgaste", StringComparison.Ordinal)).Trend);
        Assert.Equal(1, rows.Single(r => r.Item.StartsWith("Espaço livre", StringComparison.Ordinal)).Trend);
        Assert.Equal(1, rows.Single(r => r.Item == "Problemas / atenção").Trend);
    }

    [Fact]
    public void Records_are_found_in_subfolders_and_summarised()
    {
        var root = Directory.CreateTempSubdirectory("hwsuite-history");
        try
        {
            var now = DateTimeOffset.Now;
            var a = Path.Combine(root.FullName, "Escritório Exemplo", "hwsuite-a.html");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Visit(now.AddDays(-30), 60, 10, 30, 0).SaveBeside(a);
            Visit(now, 50, 11, 30, 1).SaveBeside(Path.Combine(root.FullName, "Escritório Exemplo", "hwsuite-b.html"));
            Visit(now, 50, 11, 30, 0, "PC-02").SaveBeside(Path.Combine(root.FullName, "hwsuite-c.pdf"));
            File.WriteAllText(Path.Combine(root.FullName, "broken" + AttendanceRecord.Extension), "{ not json");

            var found = AttendanceRecord.Scan(root.FullName);
            Assert.Equal(3, found.Count);
            Assert.True(found[0].Record.At >= found[^1].Record.At); // newest first

            var html = BatchSummary.ToHtml([.. found.Select(f => f.Record)], Pt, new CompanyProfile("MML", null, null, null, null, null));
            Assert.Contains("2 computadores", html, StringComparison.Ordinal); // latest visit of each machine
            Assert.Contains("PC-02", html, StringComparison.Ordinal);
            Assert.Contains("Espaço livre", html, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void Cli_stability_memtest_and_batch_options()
    {
        var o = HWSuite.Cli.CliOptions.Parse(["stability", "--minutes", "30", "--no-gpu", "--client", "Ana"]);
        Assert.Equal((HWSuite.Cli.CliCommand.Stability, 30.0, true, false, "Ana"), (o.Command, o.Minutes!.Value, o.NoGpu, o.NoMemory, o.Client));
        Assert.Equal(HWSuite.Cli.CliCommand.MemTest, HWSuite.Cli.CliOptions.Parse(["memtest", "--minutes", "5"]).Command);
        var b = HWSuite.Cli.CliOptions.Parse(["batch", @"E:\HWSuite Portatil\HWSuite\Reports\Cliente", "-o", "resumo.html"]);
        Assert.Equal((HWSuite.Cli.CliCommand.Batch, @"E:\HWSuite Portatil\HWSuite\Reports\Cliente", "resumo.html"), (b.Command, b.Folder, b.OutputPath));
        Assert.Throws<HWSuite.Cli.CliUsageException>(() => HWSuite.Cli.CliOptions.Parse(["memtest", "--no-gpu"]));
    }
}
