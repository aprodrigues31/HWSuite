using System.Globalization;
using System.Text;

namespace HWSuite.Core.Benchmark;

/// <summary>One saved run: when, on which processor, and the scores.</summary>
public sealed record BenchmarkEntry(DateTimeOffset At, string Version, string Processor, double SingleThread, double MultiThread, int Threads);

/// <summary>
/// Results kept in a CSV file (Documents\HWSuite\benchmark.csv), so runs can be compared over time, after a
/// cooler or BIOS change, and opened in a spreadsheet. Invariant culture: "2026-10-28T15:50:00-03:00,CPU v1,...".
/// </summary>
public static class BenchmarkHistory
{
    private const string Header = "when,version,processor,single_thread,multi_thread,threads";

    public static void Append(string path, BenchmarkResult result, string? processor)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var line = string.Join(",",
            result.At.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            result.Version,
            Quote(processor ?? ""),
            result.SingleThread.ToString("0.0", CultureInfo.InvariantCulture),
            result.MultiThread.ToString("0.0", CultureInfo.InvariantCulture),
            result.Threads.ToString(CultureInfo.InvariantCulture));
        var text = (File.Exists(path) ? "" : Header + "\n") + line + "\n";
        File.AppendAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>Saved runs, newest first; lines it cannot read are skipped.</summary>
    public static IReadOnlyList<BenchmarkEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<BenchmarkEntry>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var f = Split(line);
            if (f.Count == 6
                && DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var single)
                && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var multi)
                && int.TryParse(f[5], CultureInfo.InvariantCulture, out var threads))
            {
                entries.Add(new BenchmarkEntry(at, f[1], f[2], single, multi, threads));
            }
        }

        entries.Reverse();
        return entries;
    }

    internal static string Quote(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    internal static List<string> Split(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
