using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Hosting;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite bench [--duration seconds]": the processor benchmark, one thread then all threads, with the
/// result appended to Documents\HWSuite\benchmark.csv and the previous runs listed for comparison.
/// </summary>
internal static class BenchCommand
{
    private const double DefaultSeconds = 10;

    public static int Run(CliOptions options, Strings s)
    {
        if (options.Gpu)
        {
            return GpuBenchCommand.Run(options, s);
        }

        if (options.Stress)
        {
            return Stress(s);
        }

        var c = s.Culture;
        var processor = ProviderCatalog.ProcessorName();
        var seconds = options.DurationSeconds ?? DefaultSeconds;
        Console.WriteLine(s.Format("bench.title", CpuBenchmark.Version, processor ?? "?"));
        Console.WriteLine(s.Format("bench.running", seconds.ToString("0.#", c), Environment.ProcessorCount));

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var lastPhase = "";
        var progress = new SyncProgress<(double Fraction, string Phase)>(p =>
        {
            if (p.Phase != lastPhase && p.Phase != "done")
            {
                lastPhase = p.Phase;
                Console.WriteLine("  " + s[$"bench.phase.{p.Phase}"] + "...");
            }
        });

        BenchmarkResult result;
        try
        {
            result = CpuBenchmark.Run(TimeSpan.FromSeconds(seconds), progress, cancel: cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(s["bench.cancelled"]);
            return 130;
        }

        var history = HistoryPath();
        var previous = BenchmarkHistory.Read(history).Where(e => e.Version == result.Version).Take(5).ToList();
        Console.WriteLine();
        Console.WriteLine(s.Format("bench.single", CpuBenchmark.Points(result.SingleThread, c)));
        Console.WriteLine(s.Format("bench.multi", result.Threads, CpuBenchmark.Points(result.MultiThread, c), result.Scaling.ToString("0.0", c)));

        try
        {
            BenchmarkHistory.Append(history, result, processor);
            Console.WriteLine(s.Format("bench.saved", history));
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
        }

        if (previous.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(s["bench.previous"]);
            foreach (var e in previous)
            {
                Console.WriteLine($"  {e.At.LocalDateTime.ToString("g", c)}  {CpuBenchmark.Points(e.SingleThread, c),9}  {CpuBenchmark.Points(e.MultiThread, c),10}  {e.Processor}");
            }
        }

        return 0;
    }

    /// <summary>"hwsuite bench --stress": all threads until Ctrl+C, a status line every 5 s, exit code 1 on errors.</summary>
    private static int Stress(Strings s)
    {
        var c = s.Culture;
        Console.WriteLine(s.Format("stress.title", CpuBenchmark.Version, ProviderCatalog.ProcessorName() ?? "?", Environment.ProcessorCount));
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        if (OperatingSystem.IsWindows())
        {
            Providers.Windows.KeepAwake.Start();
        }

        var ticks = 0;
        var status = CpuStressTest.Run(
            st =>
            {
                if (++ticks % 5 == 0)
                {
                    Console.WriteLine(Line(st, s));
                }
            },
            cancel: cancel.Token);

        if (OperatingSystem.IsWindows())
        {
            Providers.Windows.KeepAwake.Stop();
        }

        Console.WriteLine();
        Console.WriteLine(Line(status, s));
        Console.WriteLine(s[status.Errors == 0 ? "stress.passed" : "stress.failed"]);
        return status.Errors == 0 ? 0 : 1;
    }

    private static string Line(StressStatus st, Strings s) =>
        s.Format("stress.line", Elapsed(st.Elapsed), CpuBenchmark.Points(st.PointsPerSecond, s.Culture), st.Errors.ToString("N0", s.Culture), st.Batches.ToString("N0", s.Culture));

    /// <summary>"1:05:09" (hours:minutes:seconds).</summary>
    internal static string Elapsed(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    internal static string HistoryPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite", "benchmark.csv");

    /// <summary>Progress that reports on the calling thread (Progress&lt;T&gt; would post to the thread pool).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
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
