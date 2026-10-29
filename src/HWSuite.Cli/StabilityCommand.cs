using System.Globalization;
using System.Text;
using HWSuite.Core;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Collection;
using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Sensors;
using HWSuite.Core.Stability;
using HWSuite.Hosting;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite stability" (processor, graphics card and RAM loaded together, results checked) and "hwsuite memtest"
/// (RAM only): live line on the console, then the certificate as HTML in the reports folder, with its history record.
/// </summary>
internal static class StabilityCommand
{
    public static int Run(CliOptions options, Strings s, bool memoryOnly)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        var access = AccessStatus.Detect();
        SensorsCommand.WriteAccessNotes(access, s, Console.Out);
        var store = MaintenanceCommand.SettingsFile();
        var (folder, settings) = MaintenanceCommand.AskFiles(store, store.Load(), options.OutputPath is null, s);

        Console.WriteLine(s["rep.step.inventory"]);
        var inventory = new InventoryCollector(ProviderCatalog.Default()).Collect();
        using var source = ProviderCatalog.CreateSensorSource(access, raw: false);
        var gate = new object();
        SensorSnapshot? Read()
        {
            lock (gate)
            {
                return source.Read();
            }
        }

        Read();
        var minutes = options.Minutes ?? (memoryOnly ? 10 : 15);
        var stabilityOptions = memoryOnly
            ? new StabilityOptions(TimeSpan.FromMinutes(minutes), Cpu: false, Gpu: false, Memory: true) { MemoryShare = 1.0 }
            : new StabilityOptions(TimeSpan.FromMinutes(minutes), Cpu: true, Gpu: !options.NoGpu, Memory: !options.NoMemory);
        Func<IGpuComputeRunner>? gpu = stabilityOptions.Gpu && D3D11ComputeRunner.Adapters() is { Count: > 0 } adapters
            ? () => OperatingSystem.IsWindows() ? D3D11ComputeRunner.Create(adapters[0]) : throw new PlatformNotSupportedException()
            : null;

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var c = s.Culture;
        Console.WriteLine(s.Format(memoryOnly ? "cli.memtest.start" : "cli.stab.start", minutes));
        var progress = new SyncProgress(p =>
        {
            var line = s.Format("cli.stab.line", Clock(p.Elapsed), Clock(p.Duration), p.CpuErrors + p.GpuErrors + p.MemoryErrors,
                p.CpuTemperature?.ToString("0", c) ?? "—", p.GpuTemperature?.ToString("0", c) ?? "—",
                p.Memory is { } m ? (m.BytesVerified / (double)(1L << 30)).ToString("N0", c) : "—");
            Console.Write("\r" + line.PadRight(Math.Max(line.Length, 100)));
        });
        var result = StabilityTest.Run(stabilityOptions, Read, gpu, progress, cancel.Token);
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine(s[result.Passed ? "stab.passed" : "stab.failed"]);

        var order = new ServiceOrder(options.Client, options.ClientContact, options.Equipment, options.OrderNumber, null, options.Notes);
        var company = MaintenanceSession.Company(settings);
        var path = options.OutputPath is { } output
            ? Path.GetFullPath(output)
            : Path.Combine(settings.ReportsPerClient && !string.IsNullOrWhiteSpace(order.Client) ? Path.Combine(folder, Safe(order.Client)) : folder,
                StabilityReportWriter.FileName(result, order, inventory.MachineName ?? "pc", "html"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, StabilityReportWriter.ToHtml(result, inventory, order, company, s), new UTF8Encoding(false));
        AttendanceRecord.From(result, inventory, order, company, BuildInfo.Current.Display, Path.GetFileName(path)).SaveBeside(path);
        Console.WriteLine(s.Format("cli.written", path));
        return result.Passed ? 0 : 1;
    }

    private static string Clock(TimeSpan t) => Core.Reporting.SvgChart.Elapsed(t);

    private static string Safe(string text) =>
        new string(text.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');

    /// <summary>Reports on the calling thread (Progress&lt;T&gt; would post to the thread pool and lines could overlap).</summary>
    private sealed class SyncProgress(Action<StabilityProgress> report) : IProgress<StabilityProgress>
    {
        public void Report(StabilityProgress value) => report(value);
    }
}

/// <summary>"hwsuite batch &lt;folder&gt;": one summary page of every computer whose reports are in the folder (a client's pen drive folder).</summary>
internal static class BatchCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        var folder = options.Folder ?? MaintenanceSession.ReportsFolder;
        var records = AttendanceRecord.Scan(folder);
        if (records.Count == 0)
        {
            Console.Error.WriteLine(s.Format("cli.batch.none", folder));
            return 1;
        }

        var store = MaintenanceCommand.SettingsFile();
        var company = MaintenanceSession.Company(store.Load());
        var html = BatchSummary.ToHtml([.. records.Select(r => r.Record)], s, company);
        var path = options.OutputPath is { } output
            ? Path.GetFullPath(output)
            : Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"hwsuite-resumo-{DateTime.Now:yyyy-MM-dd-HHmm}.html"));
        File.WriteAllText(path, html, new UTF8Encoding(false));
        var machines = records.Select(r => r.Record.MachineKey).Distinct().Count();
        Console.WriteLine(s.Format("cli.batch.done", records.Count, machines));
        Console.WriteLine(s.Format("cli.written", path));
        return 0;
    }
}
