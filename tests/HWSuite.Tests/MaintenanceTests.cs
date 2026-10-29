using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;

namespace HWSuite.Tests;

public sealed class MaintenanceTests
{
    private static readonly Strings Pt = Strings.For("pt-BR");
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 10, 0, 0, TimeSpan.FromHours(-3));

    private static HardwareInventory Inventory(string build = "26200.1000", string edition = "Windows 11 Pro")
    {
        var inv = new HardwareInventory { MachineName = "PC" };
        inv.OperatingSystem = new() { Name = edition, DisplayVersion = "25H2", Build = build };
        inv.Bios = new() { Vendor = "AMI", Version = "1.0", ReleaseDate = "2025-01-10" };
        inv.Memory = new() { TotalInstalledBytes = 16L << 30, SlotCount = 4 };
        inv.Memory.Modules.Add(new() { SizeBytes = 8L << 30, ConfiguredSpeedMts = 3200, PartNumber = "A" });
        inv.Memory.Modules.Add(new() { SizeBytes = 8L << 30, ConfiguredSpeedMts = 3200, PartNumber = "A" });
        return inv;
    }

    private static IReadOnlyList<CheckResult> Run(HardwareInventory inv, Func<MaintenanceInput, MaintenanceInput>? with = null)
    {
        var input = new MaintenanceInput(inv, Now);
        return MaintenanceChecks.Run(with?.Invoke(input) ?? input, Pt);
    }

    private static CheckResult One(IReadOnlyList<CheckResult> checks, CheckArea area, string itemStart) =>
        checks.Single(c => c.Area == area && c.Item.StartsWith(itemStart, StringComparison.Ordinal));

    [Theory]
    [InlineData("19045.1", "Windows 10 Pro", Verdict.Problem)]
    [InlineData("26100.6899", "Windows 11 Pro", Verdict.Problem)]
    [InlineData("26100.6899", "Windows 11 Enterprise", Verdict.Ok)]
    [InlineData("26200.1000", "Windows 11 Home", Verdict.Ok)]
    [InlineData("22631.1", "Windows 11 Education", Verdict.Attention)]
    [InlineData("26100.1", "Windows Server 2025 Datacenter", Verdict.Info)]
    [InlineData("26100.1", "Windows 11 IoT Enterprise LTSC", Verdict.Info)]
    public void Windows_support(string build, string edition, Verdict expected) =>
        Assert.Equal(expected, One(Run(Inventory(build, edition)), CheckArea.System, "Windows").Verdict);

    [Fact]
    public void Lifecycle_dates()
    {
        Assert.Equal(new DateOnly(2026, 10, 13), WindowsLifecycle.EndOfUpdates(26100, "Windows 11 Pro"));
        Assert.Equal(new DateOnly(2027, 10, 12), WindowsLifecycle.EndOfUpdates(26100, "Windows 11 Enterprise"));
        Assert.Equal(WindowsLifecycle.Windows10End, WindowsLifecycle.EndOfUpdates(19045, "Windows 10 Home"));
        Assert.Null(WindowsLifecycle.EndOfUpdates(99999, "Windows 11 Pro"));
    }

    [Fact]
    public void Old_bios_is_attention()
    {
        var inv = Inventory();
        inv.Bios!.ReleaseDate = "2019-03-01";
        Assert.Equal(Verdict.Attention, One(Run(inv), CheckArea.System, "BIOS").Verdict);
    }

    [Fact]
    public void Memory_single_channel_mixed_and_xmp()
    {
        var single = Inventory();
        single.Memory!.Modules.RemoveAt(1);
        Assert.Equal(Verdict.Attention, One(Run(single), CheckArea.Memory, "Configuração").Verdict);

        var mixed = Inventory();
        mixed.Memory!.Modules[1].PartNumber = "B";
        Assert.Contains("diferentes", One(Run(mixed), CheckArea.Memory, "Configuração").Finding, StringComparison.Ordinal);

        var slow = Inventory();
        foreach (var m in slow.Memory!.Modules)
        {
            m.ConfiguredSpeedMts = 2133;
            m.Spd = new SpdInfo();
            m.Spd.XmpProfiles.Add(new("XMP 1", 3600, 16, 18, 18, 38, null, 1.35));
        }

        var speed = One(Run(slow), CheckArea.Memory, "Velocidade");
        Assert.Equal(Verdict.Attention, speed.Verdict);
        Assert.Contains("3600", speed.Finding, StringComparison.Ordinal);

        var small = Inventory();
        small.Memory!.TotalInstalledBytes = 4L << 30;
        Assert.Equal(Verdict.Attention, One(Run(small), CheckArea.Memory, "Capacidade").Verdict);
    }

    [Fact]
    public void Disk_health_wear_temperature_and_space()
    {
        var inv = Inventory();
        inv.Disks.Add(new() { Model = "Good", Health = new() { Assessment = DiskHealthStatus.Good, PercentageUsed = 3, TemperatureCelsius = 40 } });
        inv.Disks.Add(new() { Model = "Worn", Health = new() { PercentageUsed = 93 } });
        inv.Disks.Add(new() { Model = "Hot", Health = new() { TemperatureCelsius = 66 } });
        inv.Disks.Add(new() { Model = "Bad", Health = new() { Assessment = DiskHealthStatus.Bad, ReallocatedSectors = 120 } });
        inv.Disks.Add(new() { Model = "Old", MediaType = "HDD" });
        var checks = Run(inv, i => i with { Volumes = [new("C:", 100, 5, true), new("D:", 100, 15, false), new("E:", 100, 60, false)] });
        Assert.Equal(Verdict.Ok, One(checks, CheckArea.Storage, "Good").Verdict);
        Assert.Equal(Verdict.Problem, One(checks, CheckArea.Storage, "Worn").Verdict);
        Assert.Equal(Verdict.Attention, One(checks, CheckArea.Storage, "Hot").Verdict);
        var bad = One(checks, CheckArea.Storage, "Bad");
        Assert.Equal(Verdict.Problem, bad.Verdict);
        Assert.Contains("backup", bad.Advice, StringComparison.Ordinal);
        Assert.Contains(checks, c => c.Item.StartsWith("Old", StringComparison.Ordinal) && c.Finding.Contains("HD", StringComparison.Ordinal));
        Assert.Equal(Verdict.Problem, One(checks, CheckArea.Storage, "Espaço livre · C:").Verdict);
        Assert.Equal(Verdict.Attention, One(checks, CheckArea.Storage, "Espaço livre · D:").Verdict);
        Assert.Equal(Verdict.Ok, One(checks, CheckArea.Storage, "Espaço livre · E:").Verdict);
    }

    [Fact]
    public void Sensors_feed_temperature_cooling_and_power()
    {
        var start = DateTimeOffset.Now;
        using var demo = new DemoSensorSource(() => start);
        var snapshot = demo.Read();
        var checks = Run(Inventory(), i => i with { Sensors = snapshot, PumpSensorId = "/lpc/fan/1", PumpMinRpm = 2000 });
        Assert.Equal(Verdict.Ok, One(checks, CheckArea.Processor, "Temperatura").Verdict);
        Assert.Equal(Verdict.Problem, One(checks, CheckArea.Cooling, "Bomba").Verdict); // 1 820 RPM < 2 000
        Assert.Equal(Verdict.Ok, One(checks, CheckArea.Power, "Tensões").Verdict);

        var low = snapshot with { Readings = [.. snapshot.Readings.Select(r => r.Name == "+12V" ? r with { Value = 11.2 } : r)] };
        var power = One(Run(Inventory(), i => i with { Sensors = low }), CheckArea.Power, "Tensões");
        Assert.Equal(Verdict.Problem, power.Verdict);
        Assert.Contains("+12V", power.Advice, StringComparison.Ordinal);
    }

    [Fact]
    public void No_sensor_reading_is_info_not_ok()
    {
        Assert.Equal(Verdict.Info, One(Run(Inventory()), CheckArea.Processor, "Temperatura").Verdict);
    }

    [Fact]
    public void Load_test_errors_and_heat()
    {
        Assert.Equal(Verdict.Problem, One(Run(Inventory(), i => i with { Load = new(TimeSpan.FromSeconds(60), 3, 70, 100, 4400) }), CheckArea.Processor, "Teste").Verdict);
        Assert.Equal(Verdict.Attention, One(Run(Inventory(), i => i with { Load = new(TimeSpan.FromSeconds(60), 0, 88, 100, 4400) }), CheckArea.Processor, "Teste").Verdict);
        Assert.Equal(Verdict.Ok, One(Run(Inventory(), i => i with { Load = new(TimeSpan.FromSeconds(60), 0, 75, 100, 4400) }), CheckArea.Processor, "Teste").Verdict);
    }

    [Fact]
    public void Battery_wear()
    {
        var inv = Inventory();
        inv.Batteries.Add(new() { Name = "Main", DesignCapacity = 50000, FullChargeCapacity = 27000, CycleCount = 600 });
        inv.Batteries.Add(new() { Name = "New", DesignCapacity = 50000, FullChargeCapacity = 49000 });
        var checks = Run(inv);
        Assert.Equal(Verdict.Problem, One(checks, CheckArea.Battery, "Bateria Main").Verdict);
        Assert.Equal(Verdict.Ok, One(checks, CheckArea.Battery, "Bateria New").Verdict);
    }

    [Fact]
    public void Benchmark_against_references()
    {
        var result = new BenchmarkResult(CpuBenchmark.Version, 100, 1000, 16, Now);
        var below = new BenchmarkComparison([], ComparisonVerdict.Below, 80, 78, 5);
        Assert.Equal(Verdict.Problem, One(Run(Inventory(), i => i with { BenchmarkResult = result, Benchmark = below }), CheckArea.Performance, "Benchmark").Verdict);
        Assert.Equal(Verdict.Info, One(Run(Inventory(), i => i with { BenchmarkResult = result }), CheckArea.Performance, "Benchmark").Verdict);
    }

    [Fact]
    public void Checks_are_in_area_order()
    {
        var inv = Inventory();
        inv.Disks.Add(new() { Model = "X", Health = new() { PercentageUsed = 1 } });
        var areas = Run(inv, i => i with { Volumes = [new("C:", 100, 50, true)] }).Select(c => c.Area).ToList();
        Assert.Equal(areas.Order(), areas);
    }

    [Fact]
    public void Report_html_has_verdict_actions_and_escapes_text()
    {
        var inv = Inventory("19045.1", "Windows 10 Pro");
        inv.System = new() { Product = "System Product Name", SerialNumber = "System Serial Number" };
        var checks = MaintenanceChecks.Run(new MaintenanceInput(inv, Now), Pt);
        var report = new MaintenanceReport(
            new CompanyProfile("MML <Serviços>", "CNPJ 1", "contato", null, "Técnico A", "data:image/png;base64,AAAA"),
            new ServiceOrder("Cliente & Cia", null, "PC 42", "OS-7", "Limpeza\nPasta térmica", null),
            checks, inv, Now, "0.19.0");
        var html = MaintenanceReportWriter.ToHtml(report, Pt);
        Assert.Equal(Verdict.Problem, report.Overall);
        Assert.Contains("Equipamento com problemas", html, StringComparison.Ordinal);
        Assert.Contains("Ações recomendadas", html, StringComparison.Ordinal);
        Assert.Contains("MML &lt;Serviços&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Cliente &amp; Cia", html, StringComparison.Ordinal);
        Assert.Contains("Limpeza<br>Pasta térmica", html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,AAAA", html, StringComparison.Ordinal);
        Assert.DoesNotContain("System Serial Number", html.Split("Anexo")[0], StringComparison.Ordinal);
        Assert.Contains("Anexo", html, StringComparison.Ordinal);
        Assert.StartsWith("hwsuite-2026-10-29-1000-Cliente & Cia", MaintenanceReportWriter.FileName(report, "pdf"), StringComparison.Ordinal);
        Assert.EndsWith(".pdf", MaintenanceReportWriter.FileName(report, "pdf"), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_test_keeps_the_peaks()
    {
        var start = DateTimeOffset.Now;
        var reads = 0;
        using var demo = new DemoSensorSource(() => start.AddSeconds(reads * 9));
        var load = MaintenanceSession.RunLoad(TimeSpan.FromSeconds(2.5), () =>
        {
            reads++;
            return demo.Read();
        });
        Assert.Equal(0, load.Errors);
        Assert.NotNull(load.MaxCpuTemperature);
        Assert.NotNull(load.MaxCpuPower);
        Assert.InRange(load.Duration.TotalSeconds, 2, 8);
        Assert.True(reads >= 2);
    }

    [Fact]
    public void Logo_becomes_a_data_uri()
    {
        var dir = Directory.CreateTempSubdirectory("hwsuite-logo");
        try
        {
            Assert.Null(MaintenanceSession.LogoDataUri(dir.FullName));
            File.WriteAllBytes(Path.Combine(dir.FullName, "logo.png"), [0x89, 0x50, 0x4E, 0x47]);
            Assert.Equal("data:image/png;base64,iVBORw==", MaintenanceSession.LogoDataUri(dir.FullName));
            var company = MaintenanceSession.Company(new HWSuite.Core.Settings.AppSettings { CompanyName = "MML", TechnicianName = "Alex" }, dir.FullName);
            Assert.Equal("MML", company.Name);
            Assert.False(company.IsEmpty);
            Assert.NotNull(company.LogoDataUri);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Volumes_are_real_drives()
    {
        Assert.All(MaintenanceSession.Volumes(), v => Assert.True(v.TotalBytes > 0 && v.FreeBytes <= v.TotalBytes));
    }

    [Fact]
    public void Cli_maintenance_options()
    {
        var o = HWSuite.Cli.CliOptions.Parse(["maintenance", "--client", "Ana", "--order", "42", "--services", "Limpeza\\nPasta", "--load", "30", "--bench", "--no-inventory", "-o", "r.html"]);
        Assert.Equal(HWSuite.Cli.CliCommand.Maintenance, o.Command);
        Assert.Equal("Ana", o.Client);
        Assert.Equal("42", o.OrderNumber);
        Assert.Equal("Limpeza\nPasta", o.Services);
        Assert.Equal(30, o.LoadSeconds);
        Assert.True(o.Bench && o.NoInventory);
        Assert.Equal("r.html", o.OutputPath);
        Assert.Null(HWSuite.Cli.CliOptions.Parse(["maintenance"]).LoadSeconds);
        Assert.Throws<HWSuite.Cli.CliUsageException>(() => HWSuite.Cli.CliOptions.Parse(["report", "--client", "x"]));
    }

    [Fact]
    public void Installed_copies_use_documents_and_others_are_portable()
    {
        var roots = new[] { Path.Combine(Path.GetTempPath(), "pf"), Path.Combine(Path.GetTempPath(), "lad", "Programs") };
        Assert.True(HWSuite.Core.Settings.DataLocation.IsInstalled(Path.Combine(roots[1], "HWSuite"), roots));
        Assert.False(HWSuite.Core.Settings.DataLocation.IsInstalled(Path.Combine(Path.GetTempPath(), "pendrive", "hwsuite"), roots));
        Assert.False(HWSuite.Core.Settings.DataLocation.IsInstalled(Path.Combine(Path.GetTempPath(), "pfx"), roots)); // prefix of a name is not the folder

        var usb = Path.Combine(Path.GetTempPath(), "hwsuite-portable-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (folder, portable) = HWSuite.Core.Settings.DataLocation.Resolve(usb);
            Assert.True(portable);
            Assert.Equal(Path.Combine(usb, "HWSuite"), folder);
            Assert.True(Directory.Exists(folder));
            Assert.Empty(Directory.GetFiles(folder)); // the write probe is removed
        }
        finally
        {
            Directory.Delete(usb, recursive: true);
        }
    }
}
