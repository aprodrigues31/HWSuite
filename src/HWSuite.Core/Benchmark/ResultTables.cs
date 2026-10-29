using System.Globalization;
using HWSuite.Core.Localization;

namespace HWSuite.Core.Benchmark;

/// <summary>A column of a results table: header and whether its values are numbers (right-aligned).</summary>
public sealed record ResultColumn(string Header, bool Numeric);

/// <summary>
/// One saved run as a table row. <see cref="Bar"/> is the main score relative to the best run in the table
/// (0..1, drawn as a bar in that column); <see cref="IsBest"/> marks the best run.
/// </summary>
public sealed record ResultRow(IReadOnlyList<string> Cells, double Bar, bool IsBest);

/// <summary>A results table: columns, which column carries the bar, and the rows (newest first).</summary>
public sealed record ResultTable(IReadOnlyList<ResultColumn> Columns, int BarColumn, IReadOnlyList<ResultRow> Rows);

/// <summary>Turns the saved CSV results into clear tables for the Results page (one per kind of test).</summary>
public static class ResultTables
{
    public static ResultTable Cpu(IReadOnlyList<BenchmarkEntry> runs, Strings s)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        var best = runs.Count == 0 ? 0 : runs.Max(r => r.MultiThread);
        return new ResultTable(
            [
                new(s["results.when"], false),
                new(s["results.processor"], false),
                new(s["results.allCores"], true),
                new(s["results.oneCore"], true),
                new(s["results.scaling"], true),
                new(s["results.test"], false),
            ],
            BarColumn: 2,
            [.. runs.Select(r => new ResultRow(
                [
                    When(r.At, c),
                    BenchmarkComparison.Normalize(r.Processor),
                    CpuBenchmark.Points(r.MultiThread, c),
                    CpuBenchmark.Points(r.SingleThread, c),
                    r.SingleThread > 0 ? (r.MultiThread / r.SingleThread).ToString("0.0", c) + "x" : "—",
                    r.Version,
                ],
                Fraction(r.MultiThread, best),
                r.MultiThread == best && best > 0))]);
    }

    public static ResultTable Gpu(IReadOnlyList<GpuBenchmarkResult> runs, Strings s)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        var best = runs.Count == 0 ? 0 : runs.Max(r => r.Gflops);
        return new ResultTable(
            [
                new(s["results.when"], false),
                new(s["results.gpu"], false),
                new("GFLOPS (FP32)", true),
                new("TFLOPS", true),
                new(s["results.test"], false),
            ],
            BarColumn: 2,
            [.. runs.Select(r => new ResultRow(
                [When(r.At, c), r.Adapter, GpuBenchmark.Gflops(r.Gflops, c), (r.Gflops / 1000).ToString("0.00", c), r.Version],
                Fraction(r.Gflops, best),
                r.Gflops == best && best > 0))]);
    }

    /// <summary>Read / write MB/s per test; the bar follows SEQ1M Q8T1 read, the headline speed of a drive.</summary>
    public static ResultTable Disk(IReadOnlyList<DiskBenchmarkResult> runs, Strings s)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        static double Headline(DiskBenchmarkResult r) => r.Tests.FirstOrDefault(t => t.Name == "SEQ1M Q8T1")?.ReadMBps ?? 0;
        var best = runs.Count == 0 ? 0 : runs.Max(Headline);
        List<ResultColumn> columns = [new(s["results.when"], false), new(s["results.drive"], false)];
        columns.AddRange(DiskTest.Standard.Select(t => new ResultColumn(s.Format("results.diskTest", t.Name), true)));
        return new ResultTable(
            columns,
            BarColumn: 2,
            [.. runs.Select(r =>
            {
                List<string> cells = [When(r.At, c), string.Join(" ", new[] { r.Drive, r.Model }.OfType<string>())];
                cells.AddRange(DiskTest.Standard.Select(t => r.Tests.FirstOrDefault(x => x.Name == t.Name) is { } x
                    ? $"{x.ReadMBps.ToString(x.ReadMBps >= 100 ? "N0" : "N1", c)} / {x.WriteMBps.ToString(x.WriteMBps >= 100 ? "N0" : "N1", c)}"
                    : "—"));
                return new ResultRow(cells, Fraction(Headline(r), best), Headline(r) == best && best > 0);
            })]);
    }

    private static string When(DateTimeOffset at, CultureInfo c) => at.LocalDateTime.ToString("g", c);

    private static double Fraction(double value, double best) => best > 0 ? Math.Clamp(value / best, 0, 1) : 0;
}
