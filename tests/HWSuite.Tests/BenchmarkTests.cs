using HWSuite.Cli;
using HWSuite.Core.Benchmark;

namespace HWSuite.Tests;

[Collection(TimingCollection.Name)]
public class BenchmarkTests
{
    [Fact]
    public void A_short_run_scores_both_tests()
    {
        var phases = new List<string>();
        var result = CpuBenchmark.Run(TimeSpan.FromMilliseconds(300), new InlineProgress(p => phases.Add(p.Phase)), threads: 2);

        Assert.Equal(CpuBenchmark.Version, result.Version);
        Assert.Equal(2, result.Threads);
        Assert.True(result.SingleThread > 0);
        Assert.True(result.MultiThread > 0);
        Assert.Contains("single", phases);
        Assert.Contains("multi", phases);
        Assert.Equal("done", phases[^1]);
    }

    [Fact]
    public void Cancelling_stops_the_run()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => CpuBenchmark.Run(TimeSpan.FromSeconds(30), cancel: cancel.Token, threads: 2));
    }

    [Fact]
    public void History_round_trips_newest_first()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hwsuite-bench-{Guid.NewGuid():N}", "benchmark.csv");
        try
        {
            var at = new DateTimeOffset(2026, 10, 28, 15, 50, 0, TimeSpan.FromHours(-3));
            BenchmarkHistory.Append(path, new BenchmarkResult("CPU v1", 512.3, 6120.4, 16, at), "AMD Ryzen 7 5800XT 8-Core Processor");
            BenchmarkHistory.Append(path, new BenchmarkResult("CPU v1", 520, 6200, 16, at.AddHours(1)), "Name, with \"quotes\"");

            var entries = BenchmarkHistory.Read(path);
            Assert.Equal(2, entries.Count);
            Assert.Equal("Name, with \"quotes\"", entries[0].Processor);
            Assert.Equal(new BenchmarkEntry(at, "CPU v1", "AMD Ryzen 7 5800XT 8-Core Processor", 512.3, 6120.4, 16), entries[1]);
            Assert.StartsWith("when,version,processor", File.ReadAllLines(path)[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Scaling_is_multi_over_single() =>
        Assert.Equal(12, new BenchmarkResult("CPU v1", 500, 6000, 16, DateTimeOffset.Now).Scaling);

    [Fact]
    public void Cli_parses_bench()
    {
        var options = CliOptions.Parse(["bench", "--duration", "5"]);
        Assert.Equal(CliCommand.Bench, options.Command);
        Assert.Equal(5, options.DurationSeconds);
    }

    [Fact]
    public void A_stable_stress_run_has_no_errors_and_reports_ticks()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var ticks = new List<StressStatus>();
        var final = CpuStressTest.Run(st => { lock (ticks) { ticks.Add(st); } }, threads: 2, tick: TimeSpan.FromMilliseconds(100), cancel: cancel.Token);

        Assert.Equal(0, final.Errors);
        Assert.Null(final.FirstErrorAt);
        Assert.True(final.Batches > 0);
        Assert.NotEmpty(ticks);
        Assert.Contains(ticks, t => t.PointsPerSecond > 0);
    }

    [Fact]
    public void A_wrong_result_is_counted_as_an_error()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var final = CpuStressTest.Run(null, 2, TimeSpan.FromMilliseconds(100), (n, sum) => n == 3 ? sum ^ 1 : sum, cancel.Token);

        Assert.Equal(1, final.Errors);
        Assert.NotNull(final.FirstErrorAt);
    }

    [Fact]
    public void Cli_parses_stress() => Assert.True(CliOptions.Parse(["bench", "--stress"]).Stress);

    private sealed class InlineProgress(Action<(double Fraction, string Phase)> report) : IProgress<(double Fraction, string Phase)>
    {
        private readonly Lock _gate = new();

        public void Report((double Fraction, string Phase) value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}
