using HWSuite.Cli;
using HWSuite.Core.Benchmark;
using HWSuite.Providers.Windows;

namespace HWSuite.Tests;

[Collection(TimingCollection.Name)]
public class GpuBenchmarkTests
{
    /// <summary>Each batch "takes" 10 ms for 1 GFLOP (100 GFLOPS); <see cref="WrongAt"/> returns a bad checksum once.</summary>
    private sealed class FakeRunner : IGpuComputeRunner
    {
        public long Batches { get; private set; }

        public long WrongAt { get; init; } = -1;

        public string AdapterName => "Fake GPU";

        public double FlopsPerBatch => 1e9;

        public void Calibrate(TimeSpan target)
        {
        }

        public (TimeSpan Elapsed, ulong Checksum) RunBatch()
        {
            Batches++;
            Thread.Sleep(2);
            return (TimeSpan.FromMilliseconds(10), Batches == WrongAt ? 0xBADUL : 0x600DUL);
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Gflops_are_operations_over_gpu_time()
    {
        var result = GpuBenchmark.Run(new FakeRunner(), TimeSpan.FromMilliseconds(200));
        Assert.Equal(100, result.Gflops, 3);
        Assert.Equal("Fake GPU", result.Adapter);
        Assert.Equal(GpuBenchmark.Version, result.Version);
    }

    [Fact]
    public void Stress_counts_a_wrong_batch()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var ticks = 0;
        var final = GpuBenchmark.Stress(new FakeRunner { WrongAt = 10 }, _ => ticks++, TimeSpan.FromMilliseconds(50), cancel.Token);
        Assert.Equal(1, final.Errors);
        Assert.NotNull(final.FirstErrorAt);
        Assert.True(ticks > 0);
        Assert.Equal(100, final.Gflops, 3);
    }

    [Fact]
    public void Stress_without_errors()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var final = GpuBenchmark.Stress(new FakeRunner(), null, cancel: cancel.Token);
        Assert.Equal(0, final.Errors);
        Assert.Null(final.FirstErrorAt);
    }

    [Fact]
    public void History_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hwsuite-gpubench-{Guid.NewGuid():N}", "gpu-benchmark.csv");
        try
        {
            var at = new DateTimeOffset(2026, 10, 28, 16, 30, 0, TimeSpan.FromHours(-3));
            GpuBenchmarkHistory.Append(path, new GpuBenchmarkResult("GPU v1", "AMD Radeon RX 7800 XT", 31234.5, at));
            Assert.Equal(new GpuBenchmarkResult("GPU v1", "AMD Radeon RX 7800 XT", 31234.5, at), Assert.Single(GpuBenchmarkHistory.Read(path)));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Cli_parses_gpu_bench()
    {
        var options = CliOptions.Parse(["bench", "--gpu", "--stress"]);
        Assert.True(options.Gpu);
        Assert.True(options.Stress);
    }

    /// <summary>
    /// The real Direct3D 11 compute path, on WARP (the CI machines have no GPU): the shader compiles, runs,
    /// reads back, gives the same checksum every time and a positive score. Skipped off Windows.
    /// </summary>
    [Fact]
    public void Direct3D_11_compute_shader_runs_on_warp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var runner = D3D11ComputeRunner.CreateWarp(groups: 4);
        runner.Calibrate(TimeSpan.FromMilliseconds(20));
        var first = runner.RunBatch();
        var second = runner.RunBatch();
        Assert.Equal(first.Checksum, second.Checksum);
        Assert.NotEqual(14695981039346656037UL, first.Checksum); // the buffer was written
        Assert.True(runner.FlopsPerBatch > 0);
        Assert.True(GpuBenchmark.Run(runner, TimeSpan.FromMilliseconds(300)).Gflops > 0);
        _ = D3D11ComputeRunner.Adapters(); // enumerates without throwing
    }
}
