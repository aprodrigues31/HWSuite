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
