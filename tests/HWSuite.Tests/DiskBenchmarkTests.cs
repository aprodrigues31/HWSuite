using HWSuite.Cli;
using HWSuite.Core.Benchmark;
using Microsoft.Win32.SafeHandles;

namespace HWSuite.Tests;

[Collection(SerialTests.Name)]
public class DiskBenchmarkTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 22, 30, 0, TimeSpan.FromHours(-3));

    /// <summary>A buffered opener (the logic does not depend on unbuffered I/O; the Windows opener adds the flags).</summary>
    private static SafeFileHandle Buffered(string path, bool write) =>
        File.OpenHandle(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

    [Fact]
    public void All_four_tests_run_and_the_test_file_is_deleted()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hwsuite-disk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "test.tmp");
        try
        {
            var phases = new List<string>();
            var result = DiskBenchmark.Run(path, 8L << 20, TimeSpan.FromMilliseconds(80), Buffered, "T:", "Test disk",
                progress: new Collect(phases));

            Assert.Equal(["SEQ1M Q8T1", "SEQ1M Q1T1", "RND4K Q32T1", "RND4K Q1T1"], result.Tests.Select(t => t.Name));
            Assert.All(result.Tests, t => Assert.True(t.ReadMBps > 0 && t.WriteMBps > 0 && t.ReadIops > 0 && t.WriteIops > 0));
            Assert.Equal(8L << 20, result.FileBytes);
            Assert.Equal("prepare", phases[0]);
            Assert.Contains("RND4K Q1T1 write", phases);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The real Windows path: FILE_FLAG_NO_BUFFERING needs sector-aligned buffers, offsets and sizes; a mistake
    /// there fails with "invalid parameter". Also lists the drives with their disk model. Skipped off Windows.
    /// </summary>
    [Fact]
    public void Unbuffered_io_works_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var drives = HWSuite.Providers.Windows.DiskBenchmarkTargets.Drives();
        Assert.NotEmpty(drives);
        var system = drives.First(d => Environment.SystemDirectory.StartsWith(d.Letter, StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(HWSuite.Providers.Windows.DiskBenchmarkTargets.TestFolder(system.Letter), $"hwsuite-test-{Guid.NewGuid():N}.tmp");
        var result = DiskBenchmark.Run(path, 16L << 20, TimeSpan.FromMilliseconds(150), HWSuite.Providers.Windows.DiskBenchmarkTargets.Open, system.Letter, system.Model);
        Assert.All(result.Tests, r => Assert.True(r.ReadMBps > 0 && r.WriteMBps > 0));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Cancelling_also_removes_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hwsuite-disk-{Guid.NewGuid():N}.tmp");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DiskBenchmark.Run(path, 4L << 20, TimeSpan.FromSeconds(5), Buffered, "T:", null, cancel: cancel.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void History_round_trips_and_iops_come_back_from_mbps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hwsuite-diskhist-{Guid.NewGuid():N}", "disk-benchmark.csv");
        try
        {
            var tests = new[]
            {
                new DiskTestResult("SEQ1M Q8T1", 3500.2, 3000.1, 3338, 2861),
                new DiskTestResult("SEQ1M Q1T1", 2100, 2500, 2002, 2384),
                new DiskTestResult("RND4K Q32T1", 409.6, 368.6, 100000, 90000),
                new DiskTestResult("RND4K Q1T1", 61.4, 163.8, 15000, 40000),
            };
            DiskBenchmarkHistory.Append(path, new DiskBenchmarkResult("Disk v1", "C:", "WD Green SN350 1TB", 1L << 30, tests, At));
            var back = Assert.Single(DiskBenchmarkHistory.Read(path));
            Assert.Equal("WD Green SN350 1TB", back.Model);
            Assert.Equal(1L << 30, back.FileBytes);
            Assert.Equal(3500.2, back.Tests[0].ReadMBps);
            Assert.Equal(100000, back.Tests[2].ReadIops, 0); // 409.6 MB/s of 4 KiB blocks
            Assert.StartsWith("when,version,drive,model,file_mb,seq1m_q8t1_read,seq1m_q8t1_write", File.ReadAllLines(path)[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Clearing_deletes_only_the_chosen_results()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hwsuite-clear-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var name in new[] { "benchmark.csv", "gpu-benchmark.csv", "disk-benchmark.csv" })
            {
                File.WriteAllText(Path.Combine(folder, name), "x");
            }

            Assert.Equal(BenchmarkKinds.Gpu, BenchmarkFiles.Clear(BenchmarkKinds.Gpu, folder));
            Assert.True(File.Exists(Path.Combine(folder, "benchmark.csv")));
            Assert.False(File.Exists(Path.Combine(folder, "gpu-benchmark.csv")));
            Assert.True(File.Exists(Path.Combine(folder, "disk-benchmark.csv")));

            Assert.Equal(BenchmarkKinds.Cpu | BenchmarkKinds.Disk, BenchmarkFiles.Clear(BenchmarkKinds.Cpu | BenchmarkKinds.Gpu | BenchmarkKinds.Disk, folder));
            Assert.Empty(Directory.GetFiles(folder));
            Assert.Equal(BenchmarkKinds.None, BenchmarkFiles.Clear(BenchmarkKinds.None, folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("c:", "C:")]
    [InlineData("D", "D:")]
    [InlineData("e:\\", "E:")]
    public void Cli_parses_the_drive(string arg, string expected)
    {
        var options = CliOptions.Parse(["bench", "--disk", arg, "--size", "4"]);
        Assert.Equal(expected, options.Disk);
        Assert.Equal(4, options.DiskSizeGb);
    }

    [Fact]
    public void Cli_rejects_bad_drives_and_sizes()
    {
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["bench", "--disk", "1:"]));
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["bench", "--disk", "C:", "--size", "0"]));
    }

    private sealed class Collect(List<string> phases) : IProgress<(double Fraction, string Phase)>
    {
        private readonly Lock _gate = new();

        public void Report((double Fraction, string Phase) value)
        {
            lock (_gate)
            {
                phases.Add(value.Phase);
            }
        }
    }
}
