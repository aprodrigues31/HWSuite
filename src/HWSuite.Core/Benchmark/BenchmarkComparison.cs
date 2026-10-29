using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HWSuite.Core.Benchmark;

/// <summary>Where a reference result came from.</summary>
public enum ReferenceSource
{
    /// <summary>Shipped with HWSuite (results sent by users and checked by the project).</summary>
    Builtin,

    /// <summary>A benchmark.csv the user copied into Documents\HWSuite\referencias (their own PCs or customers').</summary>
    Local,

    /// <summary>The run being compared.</summary>
    Current,
}

/// <summary>One processor model in the comparison: the median of its runs.</summary>
public sealed record ComparisonRow(string Processor, double SingleThread, double MultiThread, int Threads, int Runs, ReferenceSource Source);

/// <summary>How the run compares with other runs of the same processor model.</summary>
public enum ComparisonVerdict
{
    /// <summary>No reference for this model yet.</summary>
    NoReference,

    /// <summary>95 % of the model's median or more.</summary>
    AsExpected,

    /// <summary>85 to 95 %.</summary>
    SlightlyBelow,

    /// <summary>Below 85 %: heat, power limits, memory settings or background load.</summary>
    Below,
}

/// <summary>The comparison table and, when the same model has references, the verdict.</summary>
public sealed partial record BenchmarkComparison(IReadOnlyList<ComparisonRow> Rows, ComparisonVerdict Verdict, double? SinglePercent, double? MultiPercent, int SameModelRuns)
{
    /// <summary>Folder for results of other PCs: any *.csv there in the benchmark.csv format is read.</summary>
    public static string LocalFolder =>
        Path.Combine(BenchmarkFiles.Folder, "referencias");

    /// <summary>The runs shipped with HWSuite (the embedded cpu-reference.csv).</summary>
    public static IReadOnlyList<BenchmarkEntry> Builtin()
    {
        using var stream = typeof(BenchmarkComparison).Assembly.GetManifestResourceStream("HWSuite.Core.cpu-reference.csv");
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return BenchmarkHistory.Parse(reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0));
    }

    /// <summary>Runs in every CSV of <paramref name="folder"/>; unreadable files are skipped.</summary>
    public static IReadOnlyList<BenchmarkEntry> Local(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var entries = new List<BenchmarkEntry>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.csv"))
        {
            try
            {
                entries.AddRange(BenchmarkHistory.Read(file));
            }
            catch (IOException)
            {
                // in use or unreadable: skip
            }
        }

        return entries;
    }

    /// <summary>
    /// Builds the table: one row per processor model (median of its runs, same test version only),
    /// the current run included, sorted by the all-cores score.
    /// </summary>
    public static BenchmarkComparison Build(BenchmarkResult current, string? processor, IEnumerable<BenchmarkEntry> builtin, IEnumerable<BenchmarkEntry> local)
    {
        ArgumentNullException.ThrowIfNull(current);
        var model = Normalize(processor ?? "?");
        var groups = builtin.Select(e => (Entry: e, Source: ReferenceSource.Builtin))
            .Concat(local.Select(e => (Entry: e, Source: ReferenceSource.Local)))
            .Where(x => x.Entry.Version == current.Version)
            .GroupBy(x => Normalize(x.Entry.Processor), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ComparisonRow(
                g.Key,
                Median(g.Select(x => x.Entry.SingleThread)),
                Median(g.Select(x => x.Entry.MultiThread)),
                g.First().Entry.Threads,
                g.Count(),
                g.Any(x => x.Source == ReferenceSource.Builtin) ? ReferenceSource.Builtin : ReferenceSource.Local))
            .ToList();

        var same = groups.FirstOrDefault(r => string.Equals(r.Processor, model, StringComparison.OrdinalIgnoreCase));
        double? singlePct = same is null ? null : current.SingleThread / same.SingleThread * 100;
        double? multiPct = same is null ? null : current.MultiThread / same.MultiThread * 100;
        var worst = Math.Min(singlePct ?? 100, multiPct ?? 100);
        var verdict = same is null ? ComparisonVerdict.NoReference
            : worst >= 95 ? ComparisonVerdict.AsExpected
            : worst >= 85 ? ComparisonVerdict.SlightlyBelow
            : ComparisonVerdict.Below;

        var rows = groups.Append(new ComparisonRow(model, current.SingleThread, current.MultiThread, current.Threads, 1, ReferenceSource.Current))
            .OrderByDescending(r => r.MultiThread)
            .ToList();
        return new BenchmarkComparison(rows, verdict, singlePct, multiPct, same?.Runs ?? 0);
    }

    /// <summary>"AMD Ryzen(TM) 7 5800XT 8-Core Processor" -> "AMD Ryzen 7 5800XT".</summary>
    public static string Normalize(string processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        var t = processor.Replace("(R)", " ", StringComparison.OrdinalIgnoreCase).Replace("(TM)", " ", StringComparison.OrdinalIgnoreCase);
        t = Noise().Replace(t, " ");
        return Spaces().Replace(t, " ").Trim();
    }

    /// <summary>
    /// A GitHub "new issue" link with the result filled in, for the user to review and submit in the browser.
    /// The body holds the CSV line the project adds to cpu-reference.csv; nothing personal is included.
    /// </summary>
    public static Uri SubmissionLink(BenchmarkResult result, string? processor, string? memory, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        var name = Normalize(processor ?? "?");
        var c = CultureInfo.InvariantCulture;
        var line = string.Join(",",
            result.At.ToString("yyyy-MM-ddTHH:mm:sszzz", c),
            result.Version,
            BenchmarkHistory.Quote(processor ?? "?"),
            result.SingleThread.ToString("0.0", c),
            result.MultiThread.ToString("0.0", c),
            result.Threads.ToString(c));
        var body = $"""
            Resultado do benchmark de processador do HWSuite / HWSuite processor benchmark result

            - Processador / Processor: {processor}
            - Um núcleo / One core: {result.SingleThread.ToString("0.0", c)}
            - Todos os núcleos / All cores ({result.Threads} threads): {result.MultiThread.ToString("0.0", c)}
            - Memória / Memory: {memory}
            - HWSuite: {appVersion}

            Configuração (clock fixo, PBO, overclock...) / Settings (fixed clock, PBO, overclock...):


            ```csv
            {line}
            ```
            """;
        var title = $"[benchmark] {name} · {result.Version}";
        return new Uri("https://github.com/aprodrigues31/HWSuite/issues/new?labels=benchmark&title="
            + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0
            : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    [GeneratedRegex(@"\b\d+-Core\b|\bProcessor\b|\bCPU\b|\bwith Radeon Graphics\b", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
