using HWSuite.Core.Benchmark;

namespace HWSuite.Tests;

public class BenchmarkComparisonTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 19, 0, 0, TimeSpan.FromHours(-3));

    private static BenchmarkEntry Run(string cpu, double single, double multi, int threads = 16, string version = "CPU v1") =>
        new(At, version, cpu, single, multi, threads);

    [Theory]
    [InlineData("AMD Ryzen 7 5800XT 8-Core Processor", "AMD Ryzen 7 5800XT")]
    [InlineData("AMD Ryzen(TM) 9 7950X 16-Core Processor", "AMD Ryzen 9 7950X")]
    [InlineData("Intel(R) Core(TM) i7-8700 CPU @ 3.20GHz", "Intel Core i7-8700 @ 3.20GHz")]
    [InlineData("AMD Ryzen 5 5600G with Radeon Graphics", "AMD Ryzen 5 5600G")]
    [InlineData("Intel(R) Xeon(R) Processor @ 2.80GHz (2 vCPU)", "Intel Xeon @ 2.80GHz (2 vCPU)")]
    public void Processor_names_are_normalized(string raw, string expected) =>
        Assert.Equal(expected, BenchmarkComparison.Normalize(raw));

    [Fact]
    public void Same_model_median_gives_the_verdict()
    {
        var current = new BenchmarkResult("CPU v1", 700, 8100, 16, At);
        var local = new[]
        {
            Run("AMD Ryzen 7 5800XT 8-Core Processor", 690, 8000),
            Run("AMD Ryzen 7 5800XT 8-Core Processor", 710, 8200),
            Run("AMD Ryzen 7 5800XT 8-Core Processor", 705, 8100),
            Run("AMD Ryzen 9 7950X 16-Core Processor", 900, 19000, 32),
        };
        var comparison = BenchmarkComparison.Build(current, "AMD Ryzen 7 5800XT 8-Core Processor", [], local);

        Assert.Equal(ComparisonVerdict.AsExpected, comparison.Verdict);
        Assert.Equal(3, comparison.SameModelRuns);
        Assert.Equal(100, comparison.MultiPercent!.Value, 1); // median 8100
        Assert.Equal(["AMD Ryzen 9 7950X", "AMD Ryzen 7 5800XT", "AMD Ryzen 7 5800XT"], comparison.Rows.Select(r => r.Processor));
        Assert.Equal(ReferenceSource.Current, comparison.Rows.Single(r => r.Source == ReferenceSource.Current).Source);
    }

    [Theory]
    [InlineData(7600, ComparisonVerdict.SlightlyBelow)] // 93.8 %
    [InlineData(6500, ComparisonVerdict.Below)]
    public void A_slow_run_is_flagged(double multi, ComparisonVerdict expected)
    {
        var comparison = BenchmarkComparison.Build(new BenchmarkResult("CPU v1", 700, multi, 16, At), "AMD Ryzen 7 5800XT 8-Core Processor", [Run("AMD Ryzen 7 5800XT 8-Core Processor", 700, 8100)], []);
        Assert.Equal(expected, comparison.Verdict);
    }

    [Fact]
    public void Other_test_versions_and_unknown_models_give_no_verdict()
    {
        var comparison = BenchmarkComparison.Build(new BenchmarkResult("CPU v1", 700, 8100, 16, At), "AMD Ryzen 7 5800XT", [Run("AMD Ryzen 7 5800XT", 1, 1, version: "CPU v0")], []);
        Assert.Equal(ComparisonVerdict.NoReference, comparison.Verdict);
        Assert.Single(comparison.Rows);
    }

    [Fact]
    public void Builtin_references_load_and_local_csvs_are_read()
    {
        Assert.Contains(BenchmarkComparison.Builtin(), e => e.Version == CpuBenchmark.Version);
        var folder = Path.Combine(Path.GetTempPath(), $"hwsuite-ref-{Guid.NewGuid():N}");
        try
        {
            BenchmarkHistory.Append(Path.Combine(folder, "cliente-a.csv"), new BenchmarkResult("CPU v1", 600, 7000, 12, At), "AMD Ryzen 5 5600X 6-Core Processor");
            BenchmarkHistory.Append(Path.Combine(folder, "cliente-b.csv"), new BenchmarkResult("CPU v1", 650, 7400, 12, At), "AMD Ryzen 5 5600X 6-Core Processor");
            Assert.Equal(2, BenchmarkComparison.Local(folder).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Submission_link_carries_the_csv_line()
    {
        var link = BenchmarkComparison.SubmissionLink(new BenchmarkResult("CPU v1", 700.4, 8123.4, 16, At), "AMD Ryzen 7 5800XT 8-Core Processor", "32 GB DDR4-3200", "0.13.0").ToString();
        Assert.StartsWith("https://github.com/aprodrigues31/HWSuite/issues/new?", link, StringComparison.Ordinal);
        var body = Uri.UnescapeDataString(link);
        Assert.Contains("[benchmark] AMD Ryzen 7 5800XT · CPU v1", body, StringComparison.Ordinal);
        Assert.Contains("2026-10-28T19:00:00-03:00,CPU v1,AMD Ryzen 7 5800XT 8-Core Processor,700.4,8123.4,16", body, StringComparison.Ordinal);
    }
}
