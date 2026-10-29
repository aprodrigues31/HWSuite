using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HWSuite.Core.Benchmark;

/// <summary>One CrystalDiskMark-style test: block size, outstanding requests, sequential or random.</summary>
public sealed record DiskTest(string Name, int BlockBytes, int QueueDepth, bool Random)
{
    /// <summary>The four classic tests: SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1, RND4K Q1T1.</summary>
    public static IReadOnlyList<DiskTest> Standard { get; } =
    [
        new("SEQ1M Q8T1", 1 << 20, 8, false),
        new("SEQ1M Q1T1", 1 << 20, 1, false),
        new("RND4K Q32T1", 4096, 32, true),
        new("RND4K Q1T1", 4096, 1, true),
    ];
}

/// <summary>Read and write speed of one test, in MB/s (10^6 bytes, as disk makers and CrystalDiskMark count) and IOPS.</summary>
public sealed record DiskTestResult(string Name, double ReadMBps, double WriteMBps, double ReadIops, double WriteIops);

/// <summary>A disk benchmark run on one drive.</summary>
public sealed record DiskBenchmarkResult(string Version, string Drive, string? Model, long FileBytes, IReadOnlyList<DiskTestResult> Tests, DateTimeOffset At);

/// <summary>
/// Disk benchmark in the style of CrystalDiskMark. A test file is filled with random (incompressible) data,
/// then each test reads and writes it for a fixed time, block-aligned, through handles that bypass the
/// Windows file cache (the opener passes FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH), so the drive
/// itself is measured. Queue depth N is N threads with their own handle, each keeping one request in flight.
/// The test file is deleted at the end, also when cancelled or failed.
/// </summary>
public static class DiskBenchmark
{
    public const string Version = "Disk v1";
    private const int Alignment = 4096; // sector alignment unbuffered I/O needs (512e and 4Kn drives)

    /// <summary>Opens the test file for unbuffered I/O; the Windows provider passes the no-buffering flags.</summary>
    public delegate SafeFileHandle Opener(string path, bool write);

    /// <summary>Runs every test of <paramref name="tests"/>; progress reports 0..1 and the phase ("prepare", or "SEQ1M Q8T1 read"...).</summary>
    public static DiskBenchmarkResult Run(
        string filePath,
        long fileBytes,
        TimeSpan perPhase,
        Opener open,
        string drive,
        string? model,
        IReadOnlyList<DiskTest>? tests = null,
        IProgress<(double Fraction, string Phase)>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(open);
        tests ??= DiskTest.Standard;
        fileBytes = fileBytes / (1 << 20) * (1 << 20); // whole MiB, so every block size divides it
        var phases = 1 + (tests.Count * 2);
        var done = 0;
        void Step(string phase) => progress?.Report(((double)done++ / phases, phase));

        try
        {
            Step("prepare");
            Prepare(filePath, fileBytes, open, cancel);
            var results = new List<DiskTestResult>();
            foreach (var test in tests)
            {
                Step($"{test.Name} read");
                var (readBytes, readOps, readTime) = Measure(filePath, fileBytes, test, write: false, perPhase, open, cancel);
                Step($"{test.Name} write");
                var (writeBytes, writeOps, writeTime) = Measure(filePath, fileBytes, test, write: true, perPhase, open, cancel);
                results.Add(new DiskTestResult(
                    test.Name,
                    readBytes / 1e6 / readTime.TotalSeconds,
                    writeBytes / 1e6 / writeTime.TotalSeconds,
                    readOps / readTime.TotalSeconds,
                    writeOps / writeTime.TotalSeconds));
            }

            progress?.Report((1, "done"));
            return new DiskBenchmarkResult(Version, drive, model, fileBytes, results, DateTimeOffset.Now);
        }
        finally
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // left behind only if another program holds it; it is in a temporary or HWSuite folder
            }
        }
    }

    /// <summary>Writes the whole file once with random data in 1 MiB blocks (also allocates it on the drive).</summary>
    private static void Prepare(string path, long fileBytes, Opener open, CancellationToken cancel)
    {
        using var handle = open(path, write: true);
        using var buffer = new AlignedBuffer(1 << 20);
        System.Random.Shared.NextBytes(buffer.Span);
        for (long offset = 0; offset < fileBytes; offset += buffer.Length)
        {
            cancel.ThrowIfCancellationRequested();
            RandomAccess.Write(handle, buffer.Span, offset);
        }
    }

    /// <summary>Bytes and operations completed by <see cref="DiskTest.QueueDepth"/> threads in <paramref name="duration"/>.</summary>
    private static (long Bytes, long Ops, TimeSpan Elapsed) Measure(string path, long fileBytes, DiskTest test, bool write, TimeSpan duration, Opener open, CancellationToken cancel)
    {
        var blocks = fileBytes / test.BlockBytes;
        var bytes = new long[test.QueueDepth];
        var ops = new long[test.QueueDepth];
        Exception? failure = null;
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var clock = Stopwatch.StartNew();

        var workers = Enumerable.Range(0, test.QueueDepth).Select(t => new Thread(() =>
        {
            try
            {
                using var handle = open(path, write);
                using var buffer = new AlignedBuffer(test.BlockBytes);
                System.Random.Shared.NextBytes(buffer.Span);
                var random = new Random(unchecked((t + 1) * 7919));
                long k = 0;
                while (!cancel.IsCancellationRequested && Stopwatch.GetTimestamp() < deadline)
                {
                    // Sequential: thread t takes blocks t, t+N, t+2N... so together they stream through the file.
                    var block = test.Random ? random.NextInt64(blocks) : (t + (k * test.QueueDepth)) % blocks;
                    var offset = block * test.BlockBytes;
                    if (write)
                    {
                        RandomAccess.Write(handle, buffer.Span, offset);
                    }
                    else if (RandomAccess.Read(handle, buffer.Span, offset) <= 0)
                    {
                        break;
                    }

                    bytes[t] += test.BlockBytes;
                    ops[t]++;
                    k++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
            }
        })
        { IsBackground = true, Name = $"HWSuite disk {t}" }).ToList();

        workers.ForEach(w => w.Start());
        workers.ForEach(w => w.Join());
        var elapsed = clock.Elapsed;
        cancel.ThrowIfCancellationRequested();
        if (failure is not null)
        {
            throw new IOException(failure.Message, failure);
        }

        return (bytes.Sum(), ops.Sum(), elapsed);
    }

    /// <summary>Sector-aligned native memory, as unbuffered reads and writes require.</summary>
    private sealed unsafe class AlignedBuffer : IDisposable
    {
        private void* _memory;

        public AlignedBuffer(int length)
        {
            Length = length;
            _memory = NativeMemory.AlignedAlloc((nuint)length, Alignment);
        }

        public int Length { get; }

        public Span<byte> Span => new(_memory, Length);

        public void Dispose()
        {
            if (_memory != null)
            {
                NativeMemory.AlignedFree(_memory);
                _memory = null;
            }
        }
    }
}
