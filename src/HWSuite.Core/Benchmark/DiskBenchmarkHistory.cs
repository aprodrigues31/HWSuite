using System.Globalization;
using System.Text;

namespace HWSuite.Core.Benchmark;

/// <summary>
/// Disk results kept in Documents\HWSuite\disk-benchmark.csv: when, version, drive, model, test file size, then
/// read and write MB/s of each standard test in order (SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1, RND4K Q1T1).
/// </summary>
public static class DiskBenchmarkHistory
{
    private static readonly string Header = "when,version,drive,model,file_mb,"
        + string.Join(",", DiskTest.Standard.Select(t => t.Name.Replace(' ', '_').ToLowerInvariant()).SelectMany(n => new[] { n + "_read", n + "_write" }));

    public static void Append(string path, DiskBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var c = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var speeds = DiskTest.Standard.SelectMany(t => result.Tests.FirstOrDefault(r => r.Name == t.Name) is { } r
            ? new[] { r.ReadMBps.ToString("0.0", c), r.WriteMBps.ToString("0.0", c) }
            : ["", ""]);
        var line = string.Join(",", new[]
        {
            result.At.ToString("yyyy-MM-ddTHH:mm:sszzz", c),
            result.Version,
            BenchmarkHistory.Quote(result.Drive),
            BenchmarkHistory.Quote(result.Model ?? ""),
            (result.FileBytes >> 20).ToString(c),
        }.Concat(speeds));
        File.AppendAllText(path, (File.Exists(path) ? "" : Header + "\n") + line + "\n", new UTF8Encoding(false));
    }

    /// <summary>Saved runs, newest first (IOPS are not kept: MB/s of 4 KiB blocks gives them back).</summary>
    public static IReadOnlyList<DiskBenchmarkResult> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var c = CultureInfo.InvariantCulture;
        var entries = new List<DiskBenchmarkResult>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var f = BenchmarkHistory.Split(line);
            if (f.Count != 5 + (DiskTest.Standard.Count * 2) || !DateTimeOffset.TryParse(f[0], c, DateTimeStyles.None, out var at) || !long.TryParse(f[4], c, out var mb))
            {
                continue;
            }

            var tests = new List<DiskTestResult>();
            for (var i = 0; i < DiskTest.Standard.Count; i++)
            {
                var test = DiskTest.Standard[i];
                if (double.TryParse(f[5 + (i * 2)], NumberStyles.Float, c, out var read) && double.TryParse(f[6 + (i * 2)], NumberStyles.Float, c, out var write))
                {
                    var perOp = test.BlockBytes / 1e6;
                    tests.Add(new DiskTestResult(test.Name, read, write, read / perOp, write / perOp));
                }
            }

            entries.Add(new DiskBenchmarkResult(f[1], f[2], f[3].Length > 0 ? f[3] : null, mb << 20, tests, at));
        }

        entries.Reverse();
        return entries;
    }
}
