using HWSuite.Core.Model;

namespace HWSuite.Core.Sensors;

/// <summary>
/// Several sensor back-ends read as one. A back-end that fails on a read is skipped for that read
/// (its error is reported on the next <see cref="Diagnostics"/> call) so the others keep working.
/// </summary>
public sealed class CompositeSensorSource(params ISensorSource[] sources) : ISensorSource
{
    private readonly List<Diagnostic> _errors = [];

    public string Name => string.Join(" + ", sources.Select(s => s.Name));

    public IReadOnlyList<Diagnostic> Diagnostics => [.. sources.SelectMany(s => s.Diagnostics), .. _errors];

    public SensorSnapshot Read()
    {
        var readings = new List<SensorReading>();
        DateTimeOffset? timestamp = null;
        foreach (var source in sources)
        {
            try
            {
                var snapshot = source.Read();
                timestamp ??= snapshot.Timestamp;
                readings.AddRange(snapshot.Readings);
            }
#pragma warning disable CA1031 // An optional back-end (e.g. vendor GPU telemetry) must not stop the main one.
            catch (Exception ex) when (sources.Length > 1 && source != sources[0])
#pragma warning restore CA1031
            {
                if (_errors.Count < 10)
                {
                    _errors.Add(new Diagnostic(source.Name, DiagnosticSeverity.Warning, ex.Message));
                }
            }
        }

        return new SensorSnapshot(timestamp ?? DateTimeOffset.Now, readings);
    }

    public void Dispose()
    {
        foreach (var source in sources)
        {
            source.Dispose();
        }
    }
}
