using System.Globalization;
using System.Text;

namespace HWSuite.Core.Benchmark;

/// <summary>GPU results kept in Documents\HWSuite\gpu-benchmark.csv: "when,version,gpu,gflops".</summary>
public static class GpuBenchmarkHistory
{
    private const string Header = "when,version,gpu,gflops";

    public static void Append(string path, GpuBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var line = string.Join(",",
            result.At.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            result.Version,
            BenchmarkHistory.Quote(result.Adapter),
            result.Gflops.ToString("0.0", CultureInfo.InvariantCulture));
        File.AppendAllText(path, (File.Exists(path) ? "" : Header + "\n") + line + "\n", new UTF8Encoding(false));
    }

    /// <summary>Saved runs, newest first.</summary>
    public static IReadOnlyList<GpuBenchmarkResult> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<GpuBenchmarkResult>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var f = BenchmarkHistory.Split(line);
            if (f.Count == 4
                && DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var gflops))
            {
                entries.Add(new GpuBenchmarkResult(f[1], f[2], gflops, at));
            }
        }

        entries.Reverse();
        return entries;
    }
}
