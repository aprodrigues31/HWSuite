using HWSuite.Core.Sensors;

namespace HWSuite.Tests;

public sealed class DashboardTests
{
    private static SensorSnapshot Demo(double seconds = 0)
    {
        var start = new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero);
        var now = start;
        using var source = new DemoSensorSource(() => now);
        now = start.AddSeconds(seconds);
        return source.Read();
    }

    [Fact]
    public void Picks_cpu_values_by_library_names()
    {
        var d = Dashboard.From(Demo());
        Assert.Equal("AMD Ryzen 7 5800XT", d.Cpu.Name);
        Assert.Equal("/cpu/temp/tctl", d.Cpu.TemperatureId);
        Assert.InRange(d.Cpu.Temperature!.Value, 40, 90);
        Assert.InRange(d.Cpu.ClockMhz!.Value, 4490, 4510);
        Assert.NotNull(d.Cpu.PowerW);
        Assert.InRange(d.Cpu.CoreVolts!.Value, 1.2, 1.4);
        Assert.Equal(16, d.Cpu.ThreadLoads.Count);
        Assert.All(d.Cpu.ThreadLoads, l => Assert.InRange(l, 0, 100));
    }

    [Fact]
    public void Picks_gpu_memory_drives_fans_and_rails()
    {
        var d = Dashboard.From(Demo(), pumpSensorId: "/lpc/fan/1");
        Assert.Equal("AMD Radeon RX 7800 XT", d.Gpu.Name);
        Assert.Equal("/gpu/temp/core", d.Gpu.TemperatureId);
        Assert.True(d.Gpu.HotSpot > d.Gpu.Temperature);
        Assert.Equal(16368, d.Gpu.MemoryTotalMb);
        Assert.InRange(d.Memory.TotalGb!.Value, 31.8, 32);
        Assert.InRange(d.Memory.Load!.Value, 30, 60);
        Assert.Equal(3, d.Drives.Count);
        Assert.Equal(88, d.Drives.Single(x => x.Name.StartsWith("Patriot", StringComparison.Ordinal)).UsedPercent);
        Assert.Equal("/lpc/fan/1", d.Fans[0].Id);
        Assert.DoesNotContain(d.Fans, f => f.Rpm <= 0);
        Assert.Equal(3, d.Rails.Count);
    }

    [Fact]
    public void Missing_sensors_leave_values_empty()
    {
        var d = Dashboard.From(new SensorSnapshot(DateTimeOffset.Now, []));
        Assert.Null(d.Cpu.Temperature);
        Assert.Empty(d.Cpu.ThreadLoads);
        Assert.Null(d.Gpu.Name);
        Assert.Null(d.Memory.UsedGb);
        Assert.Empty(d.Drives);
        Assert.Empty(d.Fans);
    }

    [Fact]
    public void Memory_load_is_derived_when_the_library_has_no_load_sensor()
    {
        var d = Dashboard.From(new SensorSnapshot(DateTimeOffset.Now,
        [
            new("/ram/u", "Memory", "Memory", "Memory Used", SensorKind.Data, 8, null, null),
            new("/ram/a", "Memory", "Memory", "Memory Available", SensorKind.Data, 24, null, null),
        ]));
        Assert.Equal(32, d.Memory.TotalGb);
        Assert.Equal(25, d.Memory.Load);
    }

    [Fact]
    public void Thread_loads_follow_core_then_thread_order()
    {
        var d = Dashboard.From(new SensorSnapshot(DateTimeOffset.Now,
        [
            new("a", "CPU", "CPU", "CPU Core #10 Thread #1", SensorKind.Load, 10, null, null),
            new("b", "CPU", "CPU", "CPU Core #2 Thread #2", SensorKind.Load, 22, null, null),
            new("c", "CPU", "CPU", "CPU Core #2 Thread #1", SensorKind.Load, 21, null, null),
            new("d", "CPU", "CPU", "CPU Total", SensorKind.Load, 18, null, null),
        ]));
        Assert.Equal([21.0, 22, 10], d.Cpu.ThreadLoads);
        Assert.Equal(18, d.Cpu.Load);
    }

    [Fact]
    public void Demo_values_change_over_time()
    {
        var a = Dashboard.From(Demo(0));
        var b = Dashboard.From(Demo(37));
        Assert.NotEqual(a.Cpu.Temperature, b.Cpu.Temperature);
    }

    [Theory]
    [InlineData(SensorKind.Temperature, 55, null, 0.55)]
    [InlineData(SensorKind.Load, 120, null, 1)]
    [InlineData(SensorKind.Fan, 900, 1800.0, 0.5)]
    [InlineData(SensorKind.Fan, 900, null, 0)]
    public void Scale_fraction(SensorKind kind, double value, double? max, double expected)
    {
        Assert.Equal(expected, SensorScale.Fraction(new SensorReading("x", "h", "CPU", "n", kind, value, null, max)), 3);
    }

    [Fact]
    public void Scale_of_a_rail_is_centred_on_nominal()
    {
        var rail = new SensorReading("v", "chip", "Super I/O", "+12V", SensorKind.Voltage, 12, null, null);
        Assert.Equal(0.5, SensorScale.Fraction(rail), 3);
    }
}
