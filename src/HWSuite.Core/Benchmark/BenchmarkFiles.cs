namespace HWSuite.Core.Benchmark;

/// <summary>Which saved results to clear.</summary>
[Flags]
public enum BenchmarkKinds
{
    None = 0,
    Cpu = 1,
    Gpu = 2,
    Disk = 4,
    Network = 8,
}

/// <summary>Where each kind of benchmark result is kept (Documents\HWSuite), and clearing them.</summary>
public static class BenchmarkFiles
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite");

    public static string Cpu => Path.Combine(Folder, "benchmark.csv");

    public static string Gpu => Path.Combine(Folder, "gpu-benchmark.csv");

    public static string Disk => Path.Combine(Folder, "disk-benchmark.csv");

    public static string Network => Path.Combine(Folder, "net-benchmark.csv");

    /// <summary>Deletes the result files of the chosen kinds only; returns the kinds actually cleared.</summary>
    public static BenchmarkKinds Clear(BenchmarkKinds kinds, string? folder = null)
    {
        var cleared = BenchmarkKinds.None;
        foreach (var (kind, name) in new[] { (BenchmarkKinds.Cpu, "benchmark.csv"), (BenchmarkKinds.Gpu, "gpu-benchmark.csv"), (BenchmarkKinds.Disk, "disk-benchmark.csv"), (BenchmarkKinds.Network, "net-benchmark.csv") })
        {
            var path = Path.Combine(folder ?? Folder, name);
            if (kinds.HasFlag(kind) && File.Exists(path))
            {
                File.Delete(path);
                cleared |= kind;
            }
        }

        return cleared;
    }
}
