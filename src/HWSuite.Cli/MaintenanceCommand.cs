using System.Text;
using HWSuite.Core;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Collection;
using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Network;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;
using HWSuite.Hosting;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite maintenance": the same diagnosis as the app's Report page, printed to the console and saved as an
/// HTML report (Documents\HWSuite\Reports, or -o). The company comes from the app's settings and logo.
/// </summary>
internal static class MaintenanceCommand
{
    /// <summary>A portable copy's own settings (company, pump, folders) when it has them, else the installed app's.</summary>
    private static SettingsStore SettingsFile() =>
        new(File.Exists(DataLocation.SettingsPath) || !File.Exists(SettingsStore.DefaultPath) ? DataLocation.SettingsPath : SettingsStore.DefaultPath);

    /// <summary>
    /// Where the report goes and which logo it carries come from the settings. When never chosen and someone is at
    /// the keyboard, they are asked once here (before the tests) and can have the answer remembered.
    /// </summary>
    private static (string Folder, AppSettings Settings) AskFiles(SettingsStore store, AppSettings settings, bool needFolder, Strings s)
    {
        var interactive = !Console.IsInputRedirected;
        var folder = DataLocation.ResolvePath(settings.ReportsFolder) is { Length: > 0 } saved ? saved : MaintenanceSession.ReportsFolder;
        if (needFolder && settings.ReportsFolder is null && interactive)
        {
            Console.Write(s.Format("cli.files.folder", folder));
            if (Console.ReadLine()?.Trim().Trim('"') is { Length: > 0 } typed)
            {
                folder = Path.GetFullPath(typed);
            }

            if (Confirm(s["cli.files.remember"]))
            {
                settings = settings with { ReportsFolder = DataLocation.StorePath(folder) };
                Save(store, settings);
            }
        }

        if (settings.LogoFile is null && interactive)
        {
            var found = MaintenanceSession.LogoPath();
            Console.Write(s.Format("cli.files.logo", found ?? s["files.logo.without"]));
            var typed = Console.ReadLine()?.Trim().Trim('"');
            var logo = typed is { Length: > 0 } ? (typed == "-" ? "" : Path.GetFullPath(typed)) : found ?? "";
            var stored = logo.Length == 0 ? "" : DataLocation.StorePath(logo);
            if (logo.Length > 0 && MaintenanceSession.LogoFromFile(logo) is null)
            {
                Console.WriteLine(s.Format("cli.files.badLogo", logo));
                logo = "";
            }

            var remember = Confirm(s["cli.files.remember"]);
            settings = settings with { LogoFile = stored };
            if (remember)
            {
                Save(store, settings);
            }
        }

        return (folder, settings);
    }

    private static bool Confirm(string question)
    {
        Console.Write(question);
        var answer = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) || answer.StartsWith('s') || answer.StartsWith('S') || answer.StartsWith('y') || answer.StartsWith('Y');
    }

    private static void Save(SettingsStore store, AppSettings settings)
    {
        try
        {
            store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }

    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        var access = AccessStatus.Detect();
        SensorsCommand.WriteAccessNotes(access, s, Console.Out);
        var store = SettingsFile();
        var (folder, settings) = AskFiles(store, store.Load(), options.OutputPath is null, s);

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

        Read(); // primes rate-based sensors
        Thread.Sleep(TimeSpan.FromSeconds(1));
        var input = new MaintenanceInput(inventory, DateTimeOffset.Now)
        {
            Sensors = Read(),
            Volumes = MaintenanceSession.Volumes(),
            PumpSensorId = settings.PumpSensorId,
            PumpMinRpm = settings.PumpMinRpm,
            Network = NetworkHistory.Read(BenchmarkFiles.Network) is [var lastNet, ..] ? lastNet : null,
        };

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        try
        {
            if (options.LoadSeconds is { } seconds)
            {
                Console.WriteLine(s.Format("rep.step.load", seconds));
                var load = MaintenanceSession.RunLoad(TimeSpan.FromSeconds(seconds), Read, cancel: stop.Token);
                input = input with { Load = load, Sensors = Read() };
            }

            if (options.Bench)
            {
                Console.WriteLine(s["rep.step.bench"]);
                var result = CpuBenchmark.Run(TimeSpan.FromSeconds(10), cancel: stop.Token);
                var processor = inventory.Processors.FirstOrDefault()?.Name;
                input = input with
                {
                    BenchmarkResult = result,
                    Benchmark = BenchmarkComparison.Build(result, processor, BenchmarkComparison.Builtin(), BenchmarkComparison.Local(BenchmarkComparison.LocalFolder)),
                };
            }
        }
        catch (OperationCanceledException)
        {
            return 130;
        }

        var checks = MaintenanceChecks.Run(input, s);
        var order = new ServiceOrder(options.Client, options.ClientContact, options.Equipment, options.OrderNumber, options.Services, options.Notes);
        var report = new MaintenanceReport(MaintenanceSession.Company(settings), order, checks, inventory, input.Now, BuildInfo.Current.Display, !options.NoInventory) { Load = input.Load };

        Console.WriteLine();
        Console.WriteLine($"{s[$"mnt.overall.{report.Overall}"]}  ({report.Count(Verdict.Ok)} OK, {report.Count(Verdict.Attention)} {s["mnt.verdict.Attention"]}, {report.Count(Verdict.Problem)} {s["mnt.verdict.Problem"]})");
        foreach (var check in checks)
        {
            Console.WriteLine($"  [{s[$"mnt.verdict.{check.Verdict}"],-9}] {s[$"mnt.area.{check.Area}"]} · {check.Item}: {check.Finding}");
            if (!string.IsNullOrWhiteSpace(check.Advice) && check.Verdict >= Verdict.Attention)
            {
                Console.WriteLine($"              → {check.Advice}");
            }
        }

        var path = options.OutputPath is { } output
            ? Path.GetFullPath(output)
            : Path.Combine(MaintenanceReportWriter.Folder(report, folder, settings.ReportsPerClient), MaintenanceReportWriter.FileName(report, "html"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, MaintenanceReportWriter.ToHtml(report, s), new UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine(s.Format("cli.written", path));

        // 1 when something needs action, so scripts can flag the machine.
        return report.Overall == Verdict.Problem ? 1 : 0;
    }
}
