using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace HWSuite.Core.Benchmark;

/// <summary>Scores of one run. Points are HWSuite's own scale: compare them only with other HWSuite runs of the same version.</summary>
public sealed record BenchmarkResult(string Version, double SingleThread, double MultiThread, int Threads, DateTimeOffset At)
{
    /// <summary>How many times faster all threads are than one ("11.9x" on an 8-core / 16-thread CPU).</summary>
    public double Scaling => SingleThread > 0 ? MultiThread / SingleThread : 0;
}

/// <summary>
/// CPU-Z style processor benchmark: the same fixed workload on one thread, then on every logical
/// processor, each for a fixed time after a short warm-up (so the CPU reaches its boost clock and the JIT
/// has compiled the hot loop). The workload stays in each core's private cache, so it measures the cores
/// rather than RAM: integer mixing (xorshift), dependent table reads and writes, and a floating-point
/// Mandelbrot loop. Changing the workload changes the scale, so it carries a <see cref="Version"/>.
/// </summary>
public static class CpuBenchmark
{
    public const string Version = "CPU v1";

    internal const int TableSize = 16 * 1024; // 64 KB of uint per thread: fits in L2, not in L1
    internal const double PointsPerUnit = 0.01;

    /// <summary>Runs both tests. <paramref name="progress"/> gets 0..1 and the phase ("single" or "multi").</summary>
    public static BenchmarkResult Run(TimeSpan perTest, IProgress<(double Fraction, string Phase)>? progress = null, int? threads = null, CancellationToken cancel = default)
    {
        var count = threads ?? Environment.ProcessorCount;
        var warmUp = TimeSpan.FromMilliseconds(Math.Min(1000, perTest.TotalMilliseconds / 4));
        var total = (warmUp + perTest) * 2;
        var started = Stopwatch.StartNew();
        void Report(string phase) => progress?.Report((Math.Min(1, started.Elapsed / total), phase));

        var single = Measure(1, warmUp, perTest, () => Report("single"), cancel);
        var multi = Measure(count, warmUp, perTest, () => Report("multi"), cancel);
        progress?.Report((1, "done"));
        return new BenchmarkResult(Version, single, multi, count, DateTimeOffset.Now);
    }

    /// <summary>Points per second with <paramref name="threads"/> threads working at once.</summary>
    private static double Measure(int threads, TimeSpan warmUp, TimeSpan duration, Action tick, CancellationToken cancel)
    {
        var units = new long[threads];
        var checksums = new ulong[threads];
        using var ready = new CountdownEvent(threads);
        using var go = new ManualResetEventSlim();
        long startTicks = 0, endTicks = 0;

        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            var state = new State((ulong)(t + 1) * 0x9E3779B97F4A7C15UL);
            ready.Signal();
            go.Wait(CancellationToken.None); // cancellation is checked in the loop, so every thread always gets here and ends
            var startAt = Volatile.Read(ref startTicks);
            var endAt = Volatile.Read(ref endTicks);
            long done = 0;
            ulong sum = 0;
            while (!cancel.IsCancellationRequested)
            {
                var now = Stopwatch.GetTimestamp();
                if (now >= endAt)
                {
                    break;
                }

                sum += Unit(state);
                if (now >= startAt)
                {
                    done++;
                }
            }

            units[t] = done;
            checksums[t] = sum;
        })
        { IsBackground = true, Name = $"HWSuite benchmark {t}" }).ToList();

        workers.ForEach(w => w.Start());
        ready.Wait(CancellationToken.None); // workers signal right away; cancelling is handled in their loop
        var now = Stopwatch.GetTimestamp();
        Volatile.Write(ref startTicks, now + (long)(warmUp.TotalSeconds * Stopwatch.Frequency));
        Volatile.Write(ref endTicks, now + (long)((warmUp + duration).TotalSeconds * Stopwatch.Frequency));
        go.Set();

        // A worker can end between the check and the join, so pick it once and join it whatever its state.
        while (workers.FirstOrDefault(w => w.IsAlive) is { } alive)
        {
            alive.Join(100);
            tick();
        }

        cancel.ThrowIfCancellationRequested();
        GC.KeepAlive(checksums);
        return units.Sum() * PointsPerUnit / duration.TotalSeconds;
    }

    /// <summary>Per-thread working set: the table and the generator state.</summary>
    internal sealed class State(ulong seed)
    {
        public readonly uint[] Table = Enumerable.Range(0, TableSize).Select(i => (uint)(i * 2654435761u)).ToArray();
        public ulong X = seed | 1;

        /// <summary>Back to the starting point, so the same units give the same checksum again.</summary>
        public void Reset(ulong seed)
        {
            for (var i = 0; i < Table.Length; i++)
            {
                Table[i] = (uint)(i * 2654435761u);
            }

            X = seed | 1;
        }
    }

    /// <summary>One unit of work (about 10 microseconds on a current desktop core). Returns a checksum so nothing is optimized away.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static ulong Unit(State state)
    {
        var table = state.Table;
        var x = state.X;
        ulong acc = 0;
        const int Mask = TableSize - 1;

        // Integer and cache: xorshift64, a read-modify-write at a pseudo-random slot, a dependent read.
        for (var i = 0; i < 1024; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            var slot = (int)(x & Mask);
            var value = (table[slot] * 2654435761u) + (uint)i;
            table[slot] = value;
            acc += table[(int)(value & Mask)];
        }

        // Floating point: Mandelbrot iterations for 16 points near the set's boundary.
        var cx0 = -0.75 + ((x & 0xFF) / 4096.0);
        for (var p = 0; p < 16; p++)
        {
            double cx = cx0 + (p * 0.001), cy = 0.1 + (p * 0.0005), zx = 0, zy = 0;
            var n = 0;
            while (n < 64 && (zx * zx) + (zy * zy) < 4)
            {
                var t = (zx * zx) - (zy * zy) + cx;
                zy = (2 * zx * zy) + cy;
                zx = t;
                n++;
            }

            acc += (ulong)n;
        }

        state.X = x;
        return acc;
    }

    /// <summary>"512.3" in the reader's culture.</summary>
    public static string Points(double value, CultureInfo culture) => value.ToString("N1", culture);
}
