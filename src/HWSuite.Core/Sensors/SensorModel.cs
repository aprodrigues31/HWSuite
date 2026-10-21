using HWSuite.Core.Model;

namespace HWSuite.Core.Sensors;

/// <summary>What a sensor measures; mirrors the categories hardware monitoring libraries expose.</summary>
public enum SensorKind
{
    Temperature,
    Voltage,
    Current,
    Power,
    Clock,
    Load,
    Fan,
    Control,
    Level,
    Frequency,
    Flow,
    Factor,
    Data,
    SmallData,
    Throughput,
    TimeSpan,
    Energy,
    Noise,
    Timing,
    Humidity,
    Conductivity,
    Other,
}

/// <summary>One sensor value at one point in time. Min/max are tracked since monitoring started.</summary>
public sealed record SensorReading(
    string Id,
    string Hardware,
    string HardwareKind,
    string Name,
    SensorKind Kind,
    double? Value,
    double? Min,
    double? Max)
{
    /// <summary>Parent device when <see cref="Hardware"/> is a sub-device (e.g. Super I/O chip on a motherboard).</summary>
    public string? Parent { get; init; }
}

public sealed record SensorSnapshot(DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings);

/// <summary>
/// Wraps a source and computes min/max itself. The first read only primes the hardware (several
/// sensors report 0 before their first real sample), so it is not counted; min/max start with the
/// second read, which is why libraries' own min/max (that include those zeros) are replaced.
/// </summary>
public sealed class MinMaxTrackingSource(ISensorSource inner) : ISensorSource
{
    private readonly Dictionary<string, (double Min, double Max)> _range = new(StringComparer.Ordinal);
    private int _reads;

    public string Name => inner.Name;

    public IReadOnlyList<Diagnostic> Diagnostics => inner.Diagnostics;

    public SensorSnapshot Read()
    {
        var snapshot = inner.Read();
        var counting = ++_reads > 1;
        var readings = new List<SensorReading>(snapshot.Readings.Count);
        foreach (var r in snapshot.Readings)
        {
            if (!counting || r.Value is not { } value || !double.IsFinite(value))
            {
                var known = _range.TryGetValue(r.Id, out var kept);
                readings.Add(r with { Min = known ? kept.Min : null, Max = known ? kept.Max : null });
                continue;
            }

            var range = _range.TryGetValue(r.Id, out var current)
                ? (Math.Min(current.Min, value), Math.Max(current.Max, value))
                : (value, value);
            _range[r.Id] = range;
            readings.Add(r with { Min = range.Item1, Max = range.Item2 });
        }

        return snapshot with { Readings = readings };
    }

    public void Dispose() => inner.Dispose();
}

/// <summary>
/// A live source of sensor readings. Implementations keep hardware handles open between reads,
/// so callers create one, read it repeatedly and dispose it when monitoring stops.
/// </summary>
public interface ISensorSource : IDisposable
{
    string Name { get; }

    /// <summary>Notes about access level (elevation, kernel driver) that limit what can be read.</summary>
    IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Refreshes every sensor and returns the current values.</summary>
    SensorSnapshot Read();
}
