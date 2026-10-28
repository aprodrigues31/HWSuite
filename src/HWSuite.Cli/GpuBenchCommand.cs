using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>"hwsuite bench --gpu [--stress] [--duration s]": the graphics card benchmark or stress test.</summary>
internal static class GpuBenchCommand
{
    private const double DefaultSeconds = 10;

    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        var adapters = D3D11ComputeRunner.Adapters();
        if (adapters.Count == 0)
        {
            Console.Error.WriteLine(s["gpubench.noAdapter"]);
            return 1;
        }

        var adapter = adapters[0];
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        using var runner = D3D11ComputeRunner.Create(adapter);
        var c = s.Culture;
        if (options.Stress)
        {
            Console.WriteLine(s.Format("gpustress.title", GpuBenchmark.Version, adapter.Name));
            KeepAwake.Start();
            var ticks = 0;
            var status = GpuBenchmark.Stress(runner, st =>
            {
                if (++ticks % 5 == 0)
                {
                    Console.WriteLine(Line(st, s));
                }
            }, cancel: cancel.Token);
            KeepAwake.Stop();
            Console.WriteLine();
            Console.WriteLine(Line(status, s));
            Console.WriteLine(s[status.Errors == 0 ? "gpustress.passed" : "gpustress.failed"]);
            return status.Errors == 0 ? 0 : 1;
        }

        var seconds = options.DurationSeconds ?? DefaultSeconds;
        Console.WriteLine(s.Format("gpubench.title", GpuBenchmark.Version, adapter.Name, seconds.ToString("0.#", c)));
        GpuBenchmarkResult result;
        try
        {
            result = GpuBenchmark.Run(runner, TimeSpan.FromSeconds(seconds), cancel: cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(s["bench.cancelled"]);
            return 130;
        }

        Console.WriteLine(s.Format("gpubench.result", GpuBenchmark.Gflops(result.Gflops, c)));
        var path = HistoryPath();
        var previous = GpuBenchmarkHistory.Read(path).Where(e => e.Version == result.Version).Take(5).ToList();
        try
        {
            GpuBenchmarkHistory.Append(path, result);
            Console.WriteLine(s.Format("bench.saved", path));
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
        }

        if (previous.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(s["gpubench.previous"]);
            foreach (var e in previous)
            {
                Console.WriteLine($"  {e.At.LocalDateTime.ToString("g", c)}  {GpuBenchmark.Gflops(e.Gflops, c),10} GFLOPS  {e.Adapter}");
            }
        }

        return 0;
    }

    private static string Line(GpuStressStatus st, Strings s) =>
        s.Format("gpustress.line", BenchCommand.Elapsed(st.Elapsed), GpuBenchmark.Gflops(st.Gflops, s.Culture), st.Errors.ToString("N0", s.Culture), st.Batches.ToString("N0", s.Culture));

    internal static string HistoryPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite", "gpu-benchmark.csv");
}
