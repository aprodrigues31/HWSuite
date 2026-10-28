using System.Diagnostics;

namespace HWSuite.Core.Benchmark;

/// <summary>Where a stress test stands, reported about once a second.</summary>
/// <param name="Elapsed">Time since the start.</param>
/// <param name="PointsPerSecond">Throughput over the last interval, on the benchmark's all-threads scale: a fall over time means the CPU is slowing down (heat or power limit).</param>
/// <param name="Errors">Batches whose result differed from the reference: the CPU (or its cache or memory) computed wrongly, a sign of an unstable overclock, undervolt or overheating.</param>
/// <param name="Batches">Batches checked so far.</param>
/// <param name="FirstErrorAt">Time of the first error, if any.</param>
public sealed record StressStatus(TimeSpan Elapsed, double PointsPerSecond, long Errors, long Batches, int Threads, TimeSpan? FirstErrorAt);

/// <summary>
/// Stability test: the benchmark workload on every logical processor until cancelled, with every result
/// checked. Each batch starts from the same state, so it must always produce the same checksum; the
/// reference is computed before the workers start. A stable system never reports an error, however long it runs.
/// </summary>
public static class CpuStressTest
{
    internal const int BatchUnits = 2000; // about 20 ms on a current desktop core
    private const ulong Seed = 0x9E3779B97F4A7C15UL;

    /// <summary>Runs until <paramref name="cancel"/> is cancelled and returns the final status (it does not throw on cancel).</summary>
    public static StressStatus Run(Action<StressStatus>? onTick, int? threads = null, TimeSpan? tick = null, CancellationToken cancel = default)
        => Run(onTick, threads, tick, corrupt: null, cancel);

    /// <summary><paramref name="corrupt"/> lets tests simulate a computation error.</summary>
    internal static StressStatus Run(Action<StressStatus>? onTick, int? threads, TimeSpan? tick, Func<long, ulong, ulong>? corrupt, CancellationToken cancel)
    {
        var count = threads ?? Environment.ProcessorCount;
        var interval = tick ?? TimeSpan.FromSeconds(1);
        var reference = Batch(new CpuBenchmark.State(Seed));
        long batches = 0, errors = 0, firstErrorTicks = -1;
        var clock = Stopwatch.StartNew();

        var workers = Enumerable.Range(0, count).Select(t => new Thread(() =>
        {
            var state = new CpuBenchmark.State(Seed);
            while (!cancel.IsCancellationRequested)
            {
                state.Reset(Seed);
                var sum = Batch(state);
                var n = Interlocked.Increment(ref batches);
                if (corrupt is not null)
                {
                    sum = corrupt(n, sum);
                }

                if (sum != reference)
                {
                    Interlocked.Increment(ref errors);
                    Interlocked.CompareExchange(ref firstErrorTicks, clock.Elapsed.Ticks, -1);
                }
            }
        })
        { IsBackground = true, Name = $"HWSuite stress {t}" }).ToList();
        workers.ForEach(w => w.Start());

        StressStatus Status(double rate)
        {
            var first = Interlocked.Read(ref firstErrorTicks);
            return new StressStatus(clock.Elapsed, rate, Interlocked.Read(ref errors), Interlocked.Read(ref batches), count, first >= 0 ? TimeSpan.FromTicks(first) : null);
        }

        var lastBatches = 0L;
        var lastTime = TimeSpan.Zero;
        var rate = 0.0;
        while (!cancel.WaitHandle.WaitOne(interval))
        {
            var now = clock.Elapsed;
            var done = Interlocked.Read(ref batches);
            rate = (done - lastBatches) * BatchUnits * CpuBenchmark.PointsPerUnit / (now - lastTime).TotalSeconds;
            (lastBatches, lastTime) = (done, now);
            onTick?.Invoke(Status(rate));
        }

        workers.ForEach(w => w.Join());
        return Status(rate);
    }

    private static ulong Batch(CpuBenchmark.State state)
    {
        ulong sum = 0;
        for (var i = 0; i < BatchUnits; i++)
        {
            sum = (sum * 31) + CpuBenchmark.Unit(state);
        }

        return sum;
    }
}
