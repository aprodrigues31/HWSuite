using System.Globalization;
using System.Text.Json;
using HWSuite.Cli;
using HWSuite.Core.Collection;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Providers.Windows;

namespace HWSuite.Tests;

public class ReportAndCollectionTests
{
    private static HardwareInventory Sample()
    {
        var inv = new HardwareInventory
        {
            MachineName = "PC-RECEPCAO",
            CollectedAt = new DateTimeOffset(2026, 10, 21, 17, 0, 0, TimeSpan.FromHours(-3)),
            System = new SystemInfo { Manufacturer = "Positivo", Product = "<script>alert(1)</script>", SerialNumber = "SN123", Uuid = "UUID-1" },
            Motherboard = new MotherboardInfo { Manufacturer = "ASUS", SerialNumber = "MB-SN" },
            Memory = new MemoryInfo { SlotCount = 2 },
        };
        inv.Memory.Modules.Add(new MemoryModule { Slot = "DIMM_A1", SizeBytes = 16L << 30, Type = "DDR5", SpeedMts = 5600, SerialNumber = "MEM-SN" });
        var cpu = new ProcessorInfo { Name = "Test CPU", Cores = 6, Threads = 12 };
        cpu.Caches.Add(new CacheInfo { Level = 1, Type = CacheType.Data, SizeBytes = 48 * 1024, Ways = 12, LineSizeBytes = 64, SharedByThreads = 2 });
        inv.Processors.Add(cpu);
        inv.Disks.Add(new DiskInfo { Index = 0, Model = "NVMe SSD", SerialNumber = "DISK-SN", SizeBytes = 1_000_204_886_016, BusType = "NVMe" });
        inv.Diagnostics.Add(new Diagnostic("Storage", DiagnosticSeverity.Warning, "partial"));
        return inv;
    }

    private static string Render(IReportWriter writer, HardwareInventory inv, string culture)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        writer.Write(inv, Strings.For(culture), text);
        return text.ToString();
    }

    [Fact]
    public void Text_report_is_localized()
    {
        var pt = Render(new TextReportWriter(), Sample(), "pt-BR");
        var en = Render(new TextReportWriter(), Sample(), "en");

        Assert.Contains("MEMÓRIA", pt);
        Assert.Contains("16 GB", pt);
        Assert.Contains("compartilhado por 2 threads", pt);
        Assert.Contains("MEMORY", en);
        Assert.Contains("shared by 2 threads", en);
    }

    [Theory]
    [InlineData(true, "Máquina física com Hyper-V ativo")]
    [InlineData(false, "Máquina virtual (Microsoft Hyper-V)")]
    public void Virtualization_row_separates_vbs_hosts_from_virtual_machines(bool root, string expected)
    {
        var inv = Sample();
        inv.Processors[0].HypervisorPresent = true;
        inv.Processors[0].HypervisorVendor = "Microsoft Hyper-V";
        inv.Processors[0].HypervisorRootPartition = root;

        Assert.Contains(expected, Render(new TextReportWriter(), inv, "pt-BR"), StringComparison.Ordinal);
    }

    [Fact]
    public void Dates_are_rendered_in_the_report_language()
    {
        var inv = Sample();
        inv.Bios = new BiosInfo { ReleaseDate = DateText.Normalize("09/10/2026") };

        Assert.Equal("2026-09-10", inv.Bios.ReleaseDate);
        Assert.Contains("10/09/2026", Render(new TextReportWriter(), inv, "pt-BR"), StringComparison.Ordinal);
        Assert.Contains("9/10/2026", Render(new TextReportWriter(), inv, "en-US"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10-18-2026", "2026-10-18")] // display driver registry format
    [InlineData("12/31/99", "1999-12-31")]
    [InlineData("2026-01-05", "2026-01-05")]
    [InlineData("not a date", "not a date")]
    public void Normalizes_firmware_and_driver_dates(string raw, string expected) =>
        Assert.Equal(expected, DateText.Normalize(raw));

    [Fact]
    public void Html_report_encodes_values()
    {
        var html = Render(new HtmlReportWriter(), Sample(), "pt-BR");

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"pt-BR\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_report_is_camel_case_with_string_enums_and_no_nulls()
    {
        using var doc = JsonDocument.Parse(Render(new JsonReportWriter(), Sample(), "en"));
        var root = doc.RootElement;

        Assert.Equal("PC-RECEPCAO", root.GetProperty("machineName").GetString());
        Assert.Equal("data", root.GetProperty("processors")[0].GetProperty("caches")[0].GetProperty("type").GetString());
        Assert.Equal("warning", root.GetProperty("diagnostics")[0].GetProperty("severity").GetString());
        Assert.False(root.TryGetProperty("operatingSystem", out _));
    }

    [Fact]
    public void Redactor_removes_identifying_values()
    {
        var inv = Sample();
        InventoryRedactor.Redact(inv);

        Assert.Null(inv.MachineName);
        Assert.Null(inv.System!.SerialNumber);
        Assert.Null(inv.System.Uuid);
        Assert.Null(inv.Motherboard!.SerialNumber);
        Assert.Null(inv.Memory!.Modules[0].SerialNumber);
        Assert.Null(inv.Disks[0].SerialNumber);
        Assert.Equal("Positivo", inv.System.Manufacturer);
    }

    [Fact]
    public void Both_languages_define_the_same_keys()
    {
        var missingInPortuguese = string.Join(", ", Strings.EnglishKeys.Except(Strings.PortugueseKeys));
        var missingInEnglish = string.Join(", ", Strings.PortugueseKeys.Except(Strings.EnglishKeys));

        Assert.Equal("", missingInPortuguese);
        Assert.Equal("", missingInEnglish);
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(48L * 1024, "48 KB")]
    [InlineData(16L << 30, "16 GB")]
    [InlineData(1_000_204_886_016L, "932 GB")]
    [InlineData(1536L << 20, "1.5 GB")]
    public void Byte_sizes_use_binary_units(long bytes, string expected) =>
        Assert.Equal(expected, ReportModel.Bytes(bytes, CultureInfo.InvariantCulture));

    private sealed class StubProvider(string name, bool supported, Action<HardwareInventory> action) : IInventoryProvider
    {
        public string Name => name;

        public bool IsSupported => supported;

        public void Contribute(HardwareInventory inventory, ProviderContext context) => action(inventory);
    }

    [Fact]
    public void Collector_survives_a_failing_provider_and_skips_unsupported_ones()
    {
        var ranAfterFailure = false;
        var unsupportedRan = false;
        var collector = new InventoryCollector(
        [
            new StubProvider("Broken", true, _ => throw new InvalidOperationException("boom")),
            new StubProvider("Unsupported", false, _ => unsupportedRan = true),
            new StubProvider("Good", true, _ => ranAfterFailure = true),
        ]);

        var inv = collector.Collect();

        Assert.True(ranAfterFailure);
        Assert.False(unsupportedRan);
        var error = Assert.Single(inv.Diagnostics);
        Assert.Equal(("Broken", DiagnosticSeverity.Error), (error.Source, error.Severity));
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_totals_memory_from_modules()
    {
        var inv = new InventoryCollector(
        [
            new StubProvider("Mem", true, i =>
            {
                i.Memory = new MemoryInfo();
                i.Memory.Modules.Add(new MemoryModule { SizeBytes = 8L << 30 });
                i.Memory.Modules.Add(new MemoryModule { SizeBytes = 8L << 30 });
            }),
        ]).Collect();

        Assert.Equal(16L << 30, inv.Memory!.TotalInstalledBytes);
    }

    [Fact]
    public void Cli_parses_report_options()
    {
        var o = CliOptions.Parse(["report", "-f", "JSON", "--out", "pc.json", "--redact", "--lang", "en"]);

        Assert.Equal(("json", "pc.json", true, "en"), (o.Format, o.OutputPath, o.Redact, o.Language));
        Assert.Equal("txt", CliOptions.Parse([]).Format);
        Assert.True(CliOptions.Parse(["-h"]).ShowHelp);

        var missing = Assert.Throws<CliUsageException>(() => CliOptions.Parse(["report", "--format"]));
        Assert.Equal("cli.missingValue", missing.Key);
        var unknown = Assert.Throws<CliUsageException>(() => CliOptions.Parse(["--xyz"]));
        Assert.Equal("--xyz", unknown.Argument);
    }

    [Theory]
    [InlineData("Windows 10 Pro", 22631, "Windows 11 Pro")]
    [InlineData("Windows 10 Pro", 19045, "Windows 10 Pro")]
    [InlineData("Windows Server 2025 Standard", 26100, "Windows Server 2025 Standard")]
    public void Windows_11_is_detected_from_the_build_number(string product, int build, string expected) =>
        Assert.Equal(expected, WindowsOsProvider.WindowsProductName(product, build));

    [Fact]
    public void Dxgi_driver_version_unpacks_four_words() =>
        Assert.Equal("32.0.15.6094", DxgiGpuProvider.FormatDriverVersion((32L << 48) | (0L << 32) | (15L << 16) | 6094L));
}
