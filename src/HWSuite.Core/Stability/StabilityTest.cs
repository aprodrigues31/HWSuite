using System.Diagnostics;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Sensors;

namespace HWSuite.Core.Stability;

/// <summary>What to load and for how long. Temperatures above the limits stop the test (protection).</summary>
public sealed record StabilityOptions(TimeSpan Duration, bool Cpu = true, bool Gpu = true, bool Memory = true)
{
    public double CpuTemperatureLimit { get; init; } = 98;

    public double GpuTemperatureLimit { get; init; } = 100;

    /// <summary>Share of the free memory the RAM part uses while the processor is also loaded.</summary>
    public double MemoryShare { get; init; } = 0.6;
}

public enum StabilityStop
{
    /// <summary>Ran the whole time.</summary>
    Completed,

    /// <summary>Stopped by the user.</summary>
    Cancelled,

    /// <summary>Stopped because the processor went over its temperature limit.</summary>
    CpuTemperature,

    /// <summary>Stopped because the graphics card went over its temperature limit.</summary>
    GpuTemperature,
}

/// <summary>Live values while the test runs.</summary>
public sealed record StabilityProgress(TimeSpan Elapsed, TimeSpan Duration, long CpuErrors, long GpuErrors, long MemoryErrors,
    double? CpuTemperature, double? GpuTemperature, double? CpuPower, double? GpuPower, double? CpuClockMhz, MemoryTestStatus? Memory);

/// <summary>The outcome: errors per component, peaks, the clock under load and the timeline for charts.</summary>
public sealed record StabilityResult(
    StabilityOptions Options,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed,
    StabilityStop Stop,
    long CpuErrors,
    long GpuErrors,
    long MemoryErrors,
    MemoryTestStatus? Memory,
    string? GpuName,
    string? GpuError,
    TimelineData Timeline)
{
    public double? MaxCpuTemperature => Peak("cpu.temperature");

    public double? MaxGpuTemperature => Peak("gpu.temperature");

    public double? MaxCpuPower => Peak("cpu.power");

    public double? MaxGpuPower => Peak("gpu.power");

    /// <summary>Typical (median) processor clock under load, after the first 30 seconds.</summary>
    public double? CpuClockUnderLoad => Median("cpu.clock", fromSecond: 30);

    /// <summary>Lowest processor clock under load after the first 30 seconds (a deep drop means throttling).</summary>
    public double? MinCpuClock => Loaded("cpu.clock", 30) is { Count: > 0 } values ? values.Min() : null;

    /// <summary>The clock fell more than 15% below its typical value under load: the processor throttled.</summary>
    public bool CpuThrottled => CpuClockUnderLoad is { } typical && MinCpuClock is { } low && low < typical * 0.85;

    public long Errors => CpuErrors + GpuErrors + MemoryErrors;

    /// <summary>Passed: no error in any part and not stopped for temperature.</summary>
    public bool Passed => Errors == 0 && GpuError is null && Stop is StabilityStop.Completed or StabilityStop.Cancelled;

    private double? Peak(string key) => Timeline.Find(key) is { } series && SeriesStats.Of(series.Values) is { } st ? st.Max : null;

    private List<double>? Loaded(string key, int fromSecond)
    {
        if (Timeline.Find(key) is not { } series || Timeline.Times.Count == 0)
        {
            return null;
        }

        var start = Timeline.Times[0].AddSeconds(fromSecond);
        return [.. series.Values.Where((v, i) => Timeline.Times[i] >= start && double.IsFinite(v))];
    }

    private double? Median(string key, int fromSecond)
    {
        if (Loaded(key, fromSecond) is not { Count: > 0 } values)
        {
            return null;
        }

        values.Sort();
        return values[values.Count / 2];
    }
}

/// <summary>
/// Full stability test: processor (every thread, results checked), graphics card (compute shader, results checked)
/// and RAM (<see cref="MemoryTest"/>) loaded together for the chosen time, while temperatures, power and clocks are
/// recorded every second. Any calculation or memory error fails it; an overheating part stops it.
/// </summary>
public static class StabilityTest
{
    public static StabilityResult Run(StabilityOptions options, Func<SensorSnapshot?> read, Func<IGpuComputeRunner>? gpu,
        IProgress<StabilityProgress>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(read);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stop.CancelAfter(options.Duration);
        var started = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();

        // CPU: all threads but one when the RAM test also runs (it brings its own threads).
        StressStatus? cpu = null;
        var cpuThreads = options.Memory ? Math.Max(1, Environment.ProcessorCount - 1) : Environment.ProcessorCount;
        var cpuTask = options.Cpu
            ? Task.Factory.StartNew(() => cpu = CpuStressTest.Run(null, cpuThreads, cancel: stop.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            : Task.CompletedTask;

        GpuStressStatus? gpuStatus = null;
        string? gpuName = null, gpuError = null;
        var gpuTask = options.Gpu && gpu is not null
            ? Task.Factory.StartNew(() =>
            {
                try
                {
                    using var runner = gpu();
                    gpuName = runner.AdapterName;
                    gpuStatus = GpuBenchmark.Stress(runner, null, cancel: stop.Token);
                }
                catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or System.Runtime.InteropServices.COMException or NotSupportedException)
                {
                    gpuError = ex.Message;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            : Task.CompletedTask;

        MemoryTestStatus? memory = null;
        var memoryTask = options.Memory
            ? Task.Factory.StartNew(() => memory = MemoryTest.Run((long)(MemoryTest.DefaultSize() * options.MemoryShare), null, null, st => memory = st,
                threads: Math.Max(1, Environment.ProcessorCount / 4), cancel: stop.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            : Task.CompletedTask;

        var times = new List<DateTimeOffset>();
        var keys = new[] { "cpu.temperature", "cpu.power", "cpu.clock", "gpu.temperature", "gpu.hotspot", "gpu.power", "gpu.clock" };
        var columns = keys.ToDictionary(k => k, _ => new List<double>());
        var why = StabilityStop.Completed;
        int cpuHot = 0, gpuHot = 0;
        var all = Task.WhenAll(cpuTask, gpuTask, memoryTask);
        while (!all.IsCompleted)
        {
            all.Wait(TimeSpan.FromSeconds(1), CancellationToken.None);
            if (read() is not { } snapshot)
            {
                continue;
            }

            var d = Dashboard.From(snapshot);
            times.Add(snapshot.Timestamp);
            void Put(string key, double? value) => columns[key].Add(value ?? double.NaN);
            Put("cpu.temperature", d.Cpu.Temperature);
            Put("cpu.power", d.Cpu.PowerW);
            Put("cpu.clock", d.Cpu.ClockMhz);
            Put("gpu.temperature", d.Gpu.Temperature);
            Put("gpu.hotspot", d.Gpu.HotSpot);
            Put("gpu.power", d.Gpu.PowerW);
            Put("gpu.clock", d.Gpu.ClockMhz);

            // Protection: three readings in a row over the limit stop everything.
            cpuHot = d.Cpu.Temperature >= options.CpuTemperatureLimit ? cpuHot + 1 : 0;
            gpuHot = (d.Gpu.HotSpot ?? d.Gpu.Temperature) >= options.GpuTemperatureLimit ? gpuHot + 1 : 0;
            if (why == StabilityStop.Completed && (cpuHot >= 3 || gpuHot >= 3))
            {
                why = cpuHot >= 3 ? StabilityStop.CpuTemperature : StabilityStop.GpuTemperature;
                stop.Cancel();
            }

            progress?.Report(new StabilityProgress(clock.Elapsed, options.Duration, cpu?.Errors ?? 0, gpuStatus?.Errors ?? 0, memory?.Errors ?? 0,
                d.Cpu.Temperature, d.Gpu.Temperature, d.Cpu.PowerW, d.Gpu.PowerW, d.Cpu.ClockMhz, memory));
        }

        if (why == StabilityStop.Completed && cancel.IsCancellationRequested)
        {
            why = StabilityStop.Cancelled;
        }

        var sensors = new Dictionary<string, (string Hardware, string Name, SensorKind Kind)>
        {
            ["cpu.temperature"] = ("CPU", "CPU", SensorKind.Temperature),
            ["cpu.power"] = ("CPU", "CPU", SensorKind.Power),
            ["cpu.clock"] = ("CPU", "CPU", SensorKind.Clock),
            ["gpu.temperature"] = ("GPU", "GPU", SensorKind.Temperature),
            ["gpu.hotspot"] = ("GPU", "GPU Hot Spot", SensorKind.Temperature),
            ["gpu.power"] = ("GPU", "GPU", SensorKind.Power),
            ["gpu.clock"] = ("GPU", "GPU", SensorKind.Clock),
        };
        var timeline = new TimelineData(times,
            [.. keys.Where(k => SeriesStats.Of(columns[k]) is not null)
                .Select(k => new TimelineSeries(new TimelineSensor(k, sensors[k].Hardware, sensors[k].Name, sensors[k].Kind), columns[k]))]);
        return new StabilityResult(options, started, clock.Elapsed, why, cpu?.Errors ?? 0, gpuStatus?.Errors ?? 0, memory?.Errors ?? 0, memory, gpuName, gpuError, timeline);
    }
}
