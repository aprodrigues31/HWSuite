namespace HWSuite.Core.Sensors;

/// <summary>
/// Plausible, slowly changing readings of a typical desktop (Ryzen 7 5800XT, Radeon RX 7800 XT, B550 board,
/// three NVMe drives), named the way the real libraries name them. Used by the app's "--demo" mode for
/// screenshots and for showing the interface on a machine without sensor access; never mixed with real data.
/// </summary>
public sealed class DemoSensorSource : ISensorSource
{
    private const string Cpu = "AMD Ryzen 7 5800XT";
    private const string Gpu = "AMD Radeon RX 7800 XT";
    private const string Board = "ASUS TUF GAMING B550M-PLUS";
    private const string Chip = "Nuvoton NCT6798D";
    private readonly DateTimeOffset _start;
    private readonly Func<DateTimeOffset> _clock;

    /// <param name="clock">Time source (tests pass a fixed one).</param>
    /// <param name="elapsedAtStart">Pretend the machine has already been running this long (continues a seeded history).</param>
    public DemoSensorSource(Func<DateTimeOffset>? clock = null, TimeSpan elapsedAtStart = default)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
        _start = _clock() - elapsedAtStart;
    }

    /// <summary>Readings for the last <paramref name="span"/>, one per <paramref name="interval"/>, oldest first.</summary>
    public static IReadOnlyList<SensorSnapshot> History(TimeSpan span, TimeSpan interval)
    {
        var end = DateTimeOffset.Now;
        var now = end - span;
        using var source = new DemoSensorSource(() => now);
        var list = new List<SensorSnapshot>();
        for (; now < end; now += interval)
        {
            list.Add(source.Read());
        }

        return list;
    }

    public string Name => "Demo";

    public IReadOnlyList<Model.Diagnostic> Diagnostics => [];

    public SensorSnapshot Read()
    {
        var now = _clock();
        var t = (now - _start).TotalSeconds;

        // A burst of load every couple of minutes, so the charts have something to show.
        var burst = Math.Max(0, Math.Sin(t / 22)) * Math.Max(0, Math.Sin(t / 7.3));
        double Wave(double period, double phase = 0) => Math.Sin((t / period) + phase);

        var cpuLoad = 8 + (70 * burst) + (3 * Wave(3.1));
        var cpuTemp = 52 + (24 * burst) + (1.2 * Wave(5));
        var gpuLoad = 3 + (40 * burst * burst);
        var gpuTemp = 44 + (16 * burst) + (0.6 * Wave(7));
        var readings = new List<SensorReading>
        {
            R("/cpu/temp/tctl", Cpu, "CPU", "Core (Tctl/Tdie)", SensorKind.Temperature, cpuTemp),
            R("/cpu/temp/ccd1", Cpu, "CPU", "CCD1 (Tdie)", SensorKind.Temperature, cpuTemp - 4),
            R("/cpu/load/total", Cpu, "CPU", "CPU Total", SensorKind.Load, cpuLoad),
            R("/cpu/power/package", Cpu, "CPU", "Package", SensorKind.Power, 32 + (78 * burst) + Wave(2)),
            R("/cpu/voltage/core", Cpu, "CPU", "Core (SVI2 TFN)", SensorKind.Voltage, 1.288 + (0.03 * Wave(4))),
        };

        for (var core = 1; core <= 8; core++)
        {
            readings.Add(R($"/cpu/clock/{core}", Cpu, "CPU", $"Core #{core}", SensorKind.Clock, 4500 + (2 * Wave(1.7, core))));
            for (var thread = 1; thread <= 2; thread++)
            {
                var own = Math.Abs(Wave(2.3 + (core * 0.7), thread)) * 18;
                readings.Add(R($"/cpu/load/{core}/{thread}", Cpu, "CPU", $"CPU Core #{core} Thread #{thread}", SensorKind.Load,
                    Math.Clamp((cpuLoad * (0.6 + (0.1 * thread))) + own - 6, 0, 100)));
            }
        }

        readings.AddRange(
        [
            R("/gpu/temp/core", Gpu, "GPU", "GPU Core", SensorKind.Temperature, gpuTemp),
            R("/gpu/temp/hotspot", Gpu, "GPU", "GPU Hot Spot", SensorKind.Temperature, gpuTemp + 9 + (6 * burst)),
            R("/gpu/temp/memory", Gpu, "GPU", "GPU Memory", SensorKind.Temperature, gpuTemp + 4),
            R("/gpu/load/core", Gpu, "GPU", "GPU Core", SensorKind.Load, gpuLoad),
            R("/gpu/clock/core", Gpu, "GPU", "GPU Core", SensorKind.Clock, 1900 + (500 * burst) + (8 * Wave(1.3))),
            R("/gpu/clock/memory", Gpu, "GPU", "GPU Memory", SensorKind.Clock, burst > 0.1 ? 2438 : 96),
            R("/gpu/power/package", Gpu, "GPU", "GPU Package", SensorKind.Power, 22 + (180 * burst * burst)),
            R("/gpu/fan/1", Gpu, "GPU", "GPU Fan", SensorKind.Fan, burst > 0.25 ? 900 + (600 * burst) : 0),
            R("/gpu/data/used", Gpu, "GPU", "GPU Memory Used", SensorKind.SmallData, 1900 + (3000 * burst)),
            R("/gpu/data/total", Gpu, "GPU", "GPU Memory Total", SensorKind.SmallData, 16368),
            R("/ram/data/used", "Generic Memory", "Memory", "Memory Used", SensorKind.Data, 13.1 + (2.4 * burst)),
            R("/ram/data/available", "Generic Memory", "Memory", "Memory Available", SensorKind.Data, 18.8 - (2.4 * burst)),
            R("/ram/load", "Generic Memory", "Memory", "Memory", SensorKind.Load, (13.1 + (2.4 * burst)) * 100 / 31.9),
            S("/lpc/fan/1", "Pump · CPU_FAN", SensorKind.Fan, 1820 + (14 * Wave(3))),
            S("/lpc/fan/2", "CPU_OPT", SensorKind.Fan, 900 + (520 * burst) + (12 * Wave(4))),
            S("/lpc/fan/3", "CHA_FAN1", SensorKind.Fan, 760 + (5 * Wave(6))),
            S("/lpc/voltage/12", "+12V", SensorKind.Voltage, 12.10 - (0.04 * burst)),
            S("/lpc/voltage/5", "+5V", SensorKind.Voltage, 5.04 - (0.01 * burst)),
            S("/lpc/voltage/3", "+3.3V", SensorKind.Voltage, 3.34),
            S("/lpc/voltage/vcore", "Vcore", SensorKind.Voltage, 1.29 + (0.02 * Wave(4))),
            S("/lpc/temp/vrm", "VRM", SensorKind.Temperature, 46 + (9 * burst)),
            S("/lpc/temp/chipset", "Chipset", SensorKind.Temperature, 63 + (2 * burst)),
            S("/lpc/temp/motherboard", "Motherboard", SensorKind.Temperature, 34 + (2 * burst)),
            R("/nvme/0/temp", "WD Blue SN350 1TB", "Storage", "Composite Temperature", SensorKind.Temperature, 41 + (4 * burst)),
            R("/nvme/0/used", "WD Blue SN350 1TB", "Storage", "Used Space", SensorKind.Load, 62),
            R("/nvme/1/temp", "UP GAMER UP2000 1TB", "Storage", "Composite Temperature", SensorKind.Temperature, 38 + (2 * burst)),
            R("/nvme/1/used", "UP GAMER UP2000 1TB", "Storage", "Used Space", SensorKind.Load, 35),
            R("/nvme/2/temp", "Patriot P300 512GB", "Storage", "Composite Temperature", SensorKind.Temperature, 55 + (3 * burst)),
            R("/nvme/2/used", "Patriot P300 512GB", "Storage", "Used Space", SensorKind.Load, 88),
        ]);

        return new SensorSnapshot(now, readings);
    }

    public void Dispose()
    {
    }

    private static SensorReading R(string id, string hardware, string kind, string name, SensorKind sensor, double value) =>
        new(id, hardware, kind, name, sensor, Math.Round(value, 3), null, null);

    private static SensorReading S(string id, string name, SensorKind sensor, double value) =>
        R(id, Chip, "Super I/O", name, sensor, value) with { Parent = Board };
}
