using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Core.Reporting;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>"hwsuite bench --disk C: [--size GB] [--duration s]": the CrystalDiskMark-style disk benchmark.</summary>
internal static class DiskBenchCommand
{
    private const double DefaultSeconds = 5;

    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        var c = s.Culture;
        var drive = DiskBenchmarkTargets.Drives().FirstOrDefault(d => string.Equals(d.Letter, options.Disk, StringComparison.OrdinalIgnoreCase));
        if (drive is null)
        {
            Console.Error.WriteLine(s["app.disk.none"]);
            return 1;
        }

        var bytes = (long)options.DiskSizeGb << 30;
        if (drive.FreeBytes < bytes + (1L << 30))
        {
            Console.Error.WriteLine(s.Format("app.disk.noSpace", drive.Letter, ReportModel.Bytes(bytes, c)));
            return 1;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        Console.WriteLine(s.Format("diskbench.title", DiskBenchmark.Version, drive.Letter, drive.Model ?? "", ReportModel.Bytes(bytes, c)));
        var path = Path.Combine(DiskBenchmarkTargets.TestFolder(drive.Letter), "hwsuite-disk-test.tmp");
        var last = "";
        var progress = new Inline<(double Fraction, string Phase)>(p =>
        {
            if (p.Phase != last && p.Phase != "done")
            {
                last = p.Phase;
                Console.WriteLine("  " + p.Phase + "...");
            }
        });

        DiskBenchmarkResult result;
        try
        {
            result = DiskBenchmark.Run(path, bytes, TimeSpan.FromSeconds(options.DurationSeconds ?? DefaultSeconds), DiskBenchmarkTargets.Open, drive.Letter, drive.Model, progress: progress, cancel: cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(s["bench.cancelled"]);
            return 130;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.WriteLine();
        foreach (var test in result.Tests)
        {
            var line = s.Format("diskbench.line", test.Name, test.ReadMBps.ToString("N1", c), test.WriteMBps.ToString("N1", c));
            if (test.Name.StartsWith("RND", StringComparison.Ordinal))
            {
                line += s.Format("diskbench.iops", test.ReadIops.ToString("N0", c), test.WriteIops.ToString("N0", c));
            }

            Console.WriteLine(line);
        }

        DiskBenchmarkHistory.Append(BenchmarkFiles.Disk, result);
        Console.WriteLine(s.Format("bench.saved", BenchmarkFiles.Disk));
        return 0;
    }

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        private readonly Lock _gate = new();

        public void Report(T value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}
