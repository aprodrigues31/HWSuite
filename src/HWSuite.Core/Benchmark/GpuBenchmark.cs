using System.Diagnostics;

namespace HWSuite.Core.Benchmark;

/// <summary>
/// Runs batches of the GPU workload on one adapter. The Windows implementation is a Direct3D 11 compute
/// shader; tests use a fake. A batch is one dispatch plus the read-back of its results, so it is complete
/// when <see cref="RunBatch"/> returns.
/// </summary>
public interface IGpuComputeRunner : IDisposable
{
    string AdapterName { get; }

    /// <summary>Floating-point operations one batch performs (an FMA counts as two).</summary>
    double FlopsPerBatch { get; }

    /// <summary>Sizes a batch to take about <paramref name="target"/> (well below Windows' 2 s GPU timeout).</summary>
    void Calibrate(TimeSpan target);

    /// <summary>One batch: how long it took and a checksum of everything it computed.</summary>
    (TimeSpan Elapsed, ulong Checksum) RunBatch();
}

/// <summary>Score of one GPU benchmark run, in GFLOPS (billions of floating-point operations per second).</summary>
public sealed record GpuBenchmarkResult(string Version, string Adapter, double Gflops, DateTimeOffset At);

/// <summary>Where a GPU stress test stands, reported about once a second.</summary>
public sealed record GpuStressStatus(TimeSpan Elapsed, double Gflops, long Errors, long Batches, TimeSpan? FirstErrorAt);

/// <summary>
/// GPU benchmark and stress test. The workload is chains of fused multiply-adds in a compute shader: it
/// keeps every shader unit busy and measures their floating-point throughput, the figure GPU makers
/// quote as TFLOPS (FP32). Results are deterministic, so the stress test checks every batch against a
/// reference: a stable card never differs; a wrong result means an unstable overclock, undervolt or overheating.
/// </summary>
public static class GpuBenchmark
{
    public const string Version = "GPU v1";

    public static GpuBenchmarkResult Run(IGpuComputeRunner runner, TimeSpan duration, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        runner.Calibrate(TimeSpan.FromMilliseconds(100));
        var warmUp = TimeSpan.FromMilliseconds(Math.Min(2000, duration.TotalMilliseconds / 4));
        var total = warmUp + duration;
        var clock = Stopwatch.StartNew();
        var gpuTime = TimeSpan.Zero;
        long batches = 0;
        while (clock.Elapsed < total)
        {
            cancel.ThrowIfCancellationRequested();
            var measuring = clock.Elapsed >= warmUp;
            var (elapsed, _) = runner.RunBatch();
            if (measuring)
            {
                gpuTime += elapsed;
                batches++;
            }

            progress?.Report(Math.Min(1, clock.Elapsed / total));
        }

        var gflops = gpuTime > TimeSpan.Zero ? batches * runner.FlopsPerBatch / gpuTime.TotalSeconds / 1e9 : 0;
        progress?.Report(1);
        return new GpuBenchmarkResult(Version, runner.AdapterName, gflops, DateTimeOffset.Now);
    }

    /// <summary>Runs until cancelled; returns the final status (does not throw on cancel).</summary>
    public static GpuStressStatus Stress(IGpuComputeRunner runner, Action<GpuStressStatus>? onTick, TimeSpan? tick = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        runner.Calibrate(TimeSpan.FromMilliseconds(100));
        var interval = tick ?? TimeSpan.FromSeconds(1);

        // The reference: two batches must agree before anything is compared with them.
        var reference = runner.RunBatch().Checksum;
        var second = runner.RunBatch().Checksum;
        long errors = second == reference ? 0 : 1;
        var clock = Stopwatch.StartNew();
        TimeSpan? firstError = errors > 0 ? TimeSpan.Zero : null;
        long batches = 2;
        var windowStart = clock.Elapsed;
        var windowGpu = TimeSpan.Zero;
        long windowBatches = 0;
        var gflops = 0.0;

        GpuStressStatus Status() => new(clock.Elapsed, gflops, errors, batches, firstError);

        while (!cancel.IsCancellationRequested)
        {
            var (elapsed, checksum) = runner.RunBatch();
            batches++;
            windowBatches++;
            windowGpu += elapsed;
            if (checksum != reference)
            {
                errors++;
                firstError ??= clock.Elapsed;
            }

            if (clock.Elapsed - windowStart >= interval)
            {
                gflops = windowGpu > TimeSpan.Zero ? windowBatches * runner.FlopsPerBatch / windowGpu.TotalSeconds / 1e9 : 0;
                (windowStart, windowGpu, windowBatches) = (clock.Elapsed, TimeSpan.Zero, 0);
                onTick?.Invoke(Status());
            }
        }

        return Status();
    }

    /// <summary>"35,230" GFLOPS in the reader's culture; above 1,000 also as TFLOPS by the caller if wanted.</summary>
    public static string Gflops(double value, System.Globalization.CultureInfo culture) => value.ToString(value >= 100 ? "N0" : "N1", culture);
}
