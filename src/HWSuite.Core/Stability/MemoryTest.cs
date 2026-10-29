using System.Diagnostics;

namespace HWSuite.Core.Stability;

/// <summary>A word that did not read back what was written.</summary>
public sealed record MemoryError(long Offset, ulong Expected, ulong Actual, string Pattern)
{
    /// <summary>Which bits were wrong (a single flipping bit points at a cell, many at a module or its timings).</summary>
    public ulong Bits => Expected ^ Actual;
}

/// <summary>Progress and outcome of <see cref="MemoryTest"/>.</summary>
public sealed record MemoryTestStatus(
    TimeSpan Elapsed,
    long BytesUnderTest,
    int Pass,
    string Pattern,
    long BytesVerified,
    long Errors,
    double GigabytesPerSecond,
    IReadOnlyList<MemoryError> FirstErrors)
{
    public bool Passed => Errors == 0;
}

/// <summary>
/// RAM test from inside Windows: fills most of the free memory with patterns (solid, checkerboard, walking ones,
/// address in address, pseudo-random), reads every word back and counts mismatches, pass after pass, on all cores.
/// Data stays in memory while every block is written before any is checked, so cells must hold their value. It cannot
/// reach memory Windows itself uses (a boot-time tester such as MemTest86 can), but it catches the usual faults: a bad
/// module, unstable XMP/EXPO timings or voltage.
/// </summary>
public static class MemoryTest
{
    private const int BlockWords = 8 * 1024 * 1024; // 64 MB per block
    private const int MaxReported = 20;

    private static readonly string[] Patterns = ["solid", "checker", "walking-ones", "address", "random"];

    /// <summary>Bytes to test by default: 80% of the memory that is free now, keeping at least 768 MB for Windows.</summary>
    public static long DefaultSize()
    {
        var info = GC.GetGCMemoryInfo();
        var free = info.TotalAvailableMemoryBytes - info.MemoryLoadBytes;
        return Math.Max(256L << 20, (long)(free * 0.8) - (768L << 20));
    }

    /// <summary>
    /// Runs until <paramref name="duration"/> passes (or <paramref name="passes"/> complete) or <paramref name="cancel"/>
    /// fires; returns the final status (no exception on cancel). <paramref name="corrupt"/> lets tests inject a fault.
    /// </summary>
    public static MemoryTestStatus Run(long bytes, TimeSpan? duration, int? passes, Action<MemoryTestStatus>? onTick,
        int? threads = null, CancellationToken cancel = default) => Run(bytes, duration, passes, onTick, threads, null, cancel);

    internal static MemoryTestStatus Run(long bytes, TimeSpan? duration, int? passes, Action<MemoryTestStatus>? onTick,
        int? threads, Func<long, ulong, ulong>? corrupt, CancellationToken cancel)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        if (duration is { } d)
        {
            stop.CancelAfter(d);
        }

        var clock = Stopwatch.StartNew();
        var blocks = Allocate(bytes, stop.Token);
        var size = (long)blocks.Count * BlockWords * sizeof(ulong);
        var workers = Math.Clamp(threads ?? Environment.ProcessorCount, 1, Math.Max(1, blocks.Count));
        long verified = 0, errors = 0;
        var found = new List<MemoryError>();
        var pass = 0;
        var pattern = Patterns[0];

        MemoryTestStatus Status() => new(clock.Elapsed, size, pass, pattern, Interlocked.Read(ref verified), Interlocked.Read(ref errors),
            clock.Elapsed.TotalSeconds > 0 ? Interlocked.Read(ref verified) * 2 / clock.Elapsed.TotalSeconds / 1e9 : 0, [.. Snapshot(found)]);

        using var ticker = new Timer(_ => onTick?.Invoke(Status()), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        try
        {
            while (!stop.IsCancellationRequested && (passes is null || pass < passes))
            {
                pass++;
                for (var p = 0; p < Patterns.Length && !stop.IsCancellationRequested; p++)
                {
                    pattern = Patterns[p];
                    var seed = (ulong)(pass * 7919 + p) * 0x9E3779B97F4A7C15UL;

                    // Write everything first, then verify everything: each value has to survive in RAM meanwhile.
                    Parallel.For(0, blocks.Count, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = CancellationToken.None }, b =>
                    {
                        if (!stop.IsCancellationRequested)
                        {
                            Fill(blocks[b], (long)b * BlockWords, p, seed);
                        }
                    });

                    Parallel.For(0, blocks.Count, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = CancellationToken.None }, b =>
                    {
                        if (stop.IsCancellationRequested)
                        {
                            return;
                        }

                        var block = blocks[b];
                        var origin = (long)b * BlockWords;
                        if (corrupt is not null)
                        {
                            block[b % BlockWords] = corrupt(origin, block[b % BlockWords]);
                        }

                        var bad = Verify(block, origin, p, seed, pattern, found);
                        Interlocked.Add(ref errors, bad);
                        Interlocked.Add(ref verified, (long)block.Length * sizeof(ulong));
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopped between blocks
        }

        var final = Status();
        onTick?.Invoke(final);
        return final;
    }

    /// <summary>64 MB blocks up to <paramref name="bytes"/>; fewer if Windows runs out (the test uses what it got).</summary>
    private static List<ulong[]> Allocate(long bytes, CancellationToken cancel)
    {
        var blocks = new List<ulong[]>();
        var wanted = Math.Max(1, bytes / (BlockWords * sizeof(ulong)));
        try
        {
            for (var i = 0; i < wanted && !cancel.IsCancellationRequested; i++)
            {
                blocks.Add(GC.AllocateUninitializedArray<ulong>(BlockWords));
            }
        }
        catch (OutOfMemoryException)
        {
            // keep what was allocated, minus one block of slack so Windows is not squeezed
            if (blocks.Count > 1)
            {
                blocks.RemoveAt(blocks.Count - 1);
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(GC.AllocateUninitializedArray<ulong>(BlockWords));
        }

        return blocks;
    }

    /// <summary>The value pattern <paramref name="pattern"/> puts at word <paramref name="index"/> (global word number).</summary>
    internal static ulong Value(int pattern, long index, ulong seed) => pattern switch
    {
        0 => (seed & 1) == 0 ? 0UL : ~0UL,
        1 => (index & 1) == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL,
        2 => 1UL << (int)((index + (long)(seed % 64)) % 64),
        3 => (ulong)index ^ seed,
        _ => Mix((ulong)index + seed),
    };

    /// <summary>SplitMix64: a reproducible pseudo-random word for any index.</summary>
    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private static void Fill(ulong[] block, long origin, int pattern, ulong seed)
    {
        for (var i = 0; i < block.Length; i++)
        {
            block[i] = Value(pattern, origin + i, seed);
        }
    }

    private static long Verify(ulong[] block, long origin, int pattern, ulong seed, string name, List<MemoryError> found)
    {
        long bad = 0;
        for (var i = 0; i < block.Length; i++)
        {
            var expected = Value(pattern, origin + i, seed);
            if (block[i] != expected)
            {
                bad++;
                lock (found)
                {
                    if (found.Count < MaxReported)
                    {
                        found.Add(new MemoryError((origin + i) * sizeof(ulong), expected, block[i], name));
                    }
                }
            }
        }

        return bad;
    }

    private static List<MemoryError> Snapshot(List<MemoryError> found)
    {
        lock (found)
        {
            return [.. found];
        }
    }
}
