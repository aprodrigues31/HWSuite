namespace HWSuite.Core.Sensors;

/// <summary>
/// Keeps the last <see cref="Capacity"/> values of every sensor for charts. Missing or invalid
/// values are stored as NaN so the timeline stays aligned across sensors.
/// </summary>
public sealed class SensorHistory
{
    private readonly Dictionary<string, Queue<double>> _series = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public SensorHistory(int capacity = 300)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Add(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            foreach (var reading in snapshot.Readings)
            {
                if (!_series.TryGetValue(reading.Id, out var queue))
                {
                    queue = new Queue<double>(Capacity);
                    _series[reading.Id] = queue;
                }

                if (queue.Count == Capacity)
                {
                    queue.Dequeue();
                }

                queue.Enqueue(reading.Value is { } v && double.IsFinite(v) ? v : double.NaN);
            }
        }
    }

    /// <summary>Oldest-to-newest values for one sensor; empty when it was never seen.</summary>
    public IReadOnlyList<double> Get(string sensorId)
    {
        lock (_gate)
        {
            return _series.TryGetValue(sensorId, out var queue) ? [.. queue] : [];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _series.Clear();
        }
    }
}
