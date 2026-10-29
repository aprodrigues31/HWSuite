using System.Globalization;
using System.Text;

namespace HWSuite.Core.Sensors;

/// <summary>
/// A sensor in a timeline. <see cref="Key"/> ("Hardware / Name") is the same for a live reading and for its column in
/// a CSV log, so a choice of sensors carries over between the two.
/// </summary>
public sealed record TimelineSensor(string Key, string Hardware, string Name, SensorKind Kind)
{
    public string Unit => SensorFormat.Unit(Kind);

    public static string KeyOf(string hardware, string name) => $"{hardware} / {name}";

    /// <summary>The device as logs name it: "Parent › Device" for a sub-device (a Super I/O chip on a board).</summary>
    public static string HardwareOf(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Parent is null ? reading.Hardware : $"{reading.Parent} › {reading.Hardware}";
    }

    public static string KeyOf(SensorReading reading) => KeyOf(HardwareOf(reading), reading.Name);
}

public sealed record TimelineSeries(TimelineSensor Sensor, IReadOnlyList<double> Values);

/// <summary>Samples over time: one timestamp per sample, every series aligned to them (NaN where a value is missing).</summary>
public sealed record TimelineData(IReadOnlyList<DateTimeOffset> Times, IReadOnlyList<TimelineSeries> Series)
{
    public static TimelineData Empty { get; } = new([], []);

    public TimeSpan Span => Times.Count > 1 ? Times[^1] - Times[0] : TimeSpan.Zero;

    public TimelineSeries? Find(string key) => Series.FirstOrDefault(s => s.Sensor.Key == key);

    /// <summary>The last <paramref name="span"/> of samples (all of them when null).</summary>
    public TimelineData Last(TimeSpan? span)
    {
        if (span is not { } window || Times.Count == 0)
        {
            return this;
        }

        var from = Times[^1] - window;
        var start = 0;
        while (start < Times.Count && Times[start] < from)
        {
            start++;
        }

        return start == 0 ? this : new([.. Times.Skip(start)], [.. Series.Select(s => s with { Values = [.. s.Values.Skip(start)] })]);
    }

    /// <summary>Only these sensors, in this order (keys not present are skipped).</summary>
    public TimelineData Select(IEnumerable<string> keys) =>
        this with { Series = [.. keys.Select(Find).OfType<TimelineSeries>()] };
}

/// <summary>Minimum, average, maximum and last value of a series, ignoring missing samples.</summary>
public sealed record SeriesStats(double Min, double Average, double Max, double Last, int Count)
{
    public static SeriesStats? Of(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double min = double.MaxValue, max = double.MinValue, sum = 0, last = double.NaN;
        var count = 0;
        foreach (var v in values)
        {
            if (!double.IsFinite(v))
            {
                continue;
            }

            min = Math.Min(min, v);
            max = Math.Max(max, v);
            sum += v;
            last = v;
            count++;
        }

        return count == 0 ? null : new SeriesStats(min, sum / count, max, last, count);
    }
}

/// <summary>
/// Keeps the recent samples of every sensor with their time, for the Charts page (an hour at one read per second).
/// Thread-safe: the monitor adds from its polling thread, pages copy a <see cref="TimelineData"/>.
/// </summary>
public sealed class SensorTimeline(int capacity = 3600)
{
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _times = new();
    private readonly Dictionary<string, (TimelineSensor Sensor, List<double> Values)> _series = new(StringComparer.Ordinal);
    private int _dropped; // samples removed from the front since the last compaction

    public int Capacity { get; } = capacity >= 2 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public void Add(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var index = _times.Count;
            _times.Enqueue(snapshot.Timestamp);
            foreach (var r in snapshot.Readings)
            {
                var key = TimelineSensor.KeyOf(r);
                if (!_series.TryGetValue(key, out var entry))
                {
                    entry = (new TimelineSensor(key, TimelineSensor.HardwareOf(r), r.Name, r.Kind), []);
                    _series[key] = entry;
                }

                var values = entry.Values;
                while (values.Count - _dropped < index)
                {
                    values.Add(double.NaN); // sensor appeared late or skipped a read
                }

                if (values.Count - _dropped == index)
                {
                    values.Add(r.Value is { } v && double.IsFinite(v) ? v : double.NaN);
                }
            }

            if (_times.Count > Capacity)
            {
                _times.Dequeue();
                _dropped++;
                if (_dropped >= Capacity / 4)
                {
                    Compact();
                }
            }
        }
    }

    /// <summary>The sensors seen so far, in the order they first appeared.</summary>
    public IReadOnlyList<TimelineSensor> Sensors()
    {
        lock (_gate)
        {
            return [.. _series.Values.Select(e => e.Sensor)];
        }
    }

    /// <summary>A copy of the samples; only the sensors in <paramref name="keys"/> (in that order) when given.</summary>
    public TimelineData Snapshot(IReadOnlyCollection<string>? keys = null)
    {
        lock (_gate)
        {
            var count = _times.Count;
            var entries = keys is null
                ? _series.Values.ToList()
                : [.. keys.Where(_series.ContainsKey).Select(k => _series[k])];
            var series = entries
                .Select(e =>
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        var at = _dropped + i;
                        values[i] = at < e.Values.Count ? e.Values[at] : double.NaN;
                    }

                    return new TimelineSeries(e.Sensor, values);
                })
                .ToList();
            return new TimelineData([.. _times], series);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _times.Clear();
            _series.Clear();
            _dropped = 0;
        }
    }

    private void Compact()
    {
        foreach (var entry in _series.Values)
        {
            entry.Values.RemoveRange(0, Math.Min(_dropped, entry.Values.Count));
        }

        _dropped = 0;
    }
}

/// <summary>Reads the CSV logs HWSuite writes (app and "hwsuite sensors --log") back into a timeline.</summary>
public static class SensorLogReader
{
    private static readonly Dictionary<string, SensorKind> Units = new(StringComparer.Ordinal)
    {
        ["°C"] = SensorKind.Temperature,
        ["V"] = SensorKind.Voltage,
        ["A"] = SensorKind.Current,
        ["W"] = SensorKind.Power,
        ["MHz"] = SensorKind.Clock,
        ["%"] = SensorKind.Load,
        ["RPM"] = SensorKind.Fan,
        ["Hz"] = SensorKind.Frequency,
        ["L/h"] = SensorKind.Flow,
        ["GB"] = SensorKind.Data,
        ["MB"] = SensorKind.SmallData,
        ["B/s"] = SensorKind.Throughput,
        ["s"] = SensorKind.TimeSpan,
        ["mWh"] = SensorKind.Energy,
        ["dBA"] = SensorKind.Noise,
        ["ns"] = SensorKind.Timing,
        ["µS/cm"] = SensorKind.Conductivity,
    };

    public static TimelineData Read(string path)
    {
        // The app may still be writing this log: share it for writing.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Read(reader);
    }

    /// <summary>Rows that do not parse (a line cut by a crash, say) are skipped.</summary>
    public static TimelineData Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadLine() is not { } header || !header.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("not an HWSuite sensor log (missing the timestamp header)");
        }

        var sensors = Split(header).Skip(1).Select(Sensor).ToList();
        var times = new List<DateTimeOffset>();
        var columns = sensors.Select(_ => new List<double>()).ToList();
        while (reader.ReadLine() is { } line)
        {
            var cells = Split(line);
            if (cells.Count == 0 || !DateTimeOffset.TryParse(cells[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                continue;
            }

            times.Add(at);
            for (var i = 0; i < columns.Count; i++)
            {
                columns[i].Add(i + 1 < cells.Count && double.TryParse(cells[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN);
            }
        }

        return new TimelineData(times, [.. sensors.Select((s, i) => new TimelineSeries(s, columns[i]))]);
    }

    /// <summary>"Hardware / Name [unit]" back into its parts; the unit gives the kind.</summary>
    internal static TimelineSensor Sensor(string column)
    {
        var text = column.Trim();
        var kind = SensorKind.Other;
        if (text.EndsWith(']') && text.LastIndexOf(" [", StringComparison.Ordinal) is var open and > 0)
        {
            var unit = text[(open + 2)..^1];
            kind = Units.GetValueOrDefault(unit, SensorKind.Other);
            text = text[..open];
        }

        var split = text.LastIndexOf(" / ", StringComparison.Ordinal);
        return split > 0
            ? new TimelineSensor(text, text[..split], text[(split + 3)..], kind)
            : new TimelineSensor(text, "", text, kind);
    }

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(ch);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }
}

/// <summary>Thins a long series for drawing: per pixel column, the lowest and highest sample (peaks stay visible).</summary>
public static class ChartReducer
{
    /// <summary>Indexes (ascending) to draw so that at most about 2 × <paramref name="buckets"/> points remain.</summary>
    public static IReadOnlyList<int> Indexes(IReadOnlyList<double> values, int buckets)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count <= Math.Max(2, buckets * 2))
        {
            return [.. Enumerable.Range(0, values.Count).Where(i => double.IsFinite(values[i]))];
        }

        var result = new List<int>(buckets * 2);
        var size = (double)values.Count / buckets;
        for (var b = 0; b < buckets; b++)
        {
            int from = (int)(b * size), to = Math.Min(values.Count, (int)((b + 1) * size));
            int lo = -1, hi = -1;
            for (var i = from; i < to; i++)
            {
                if (!double.IsFinite(values[i]))
                {
                    continue;
                }

                if (lo < 0 || values[i] < values[lo])
                {
                    lo = i;
                }

                if (hi < 0 || values[i] > values[hi])
                {
                    hi = i;
                }
            }

            if (lo >= 0)
            {
                result.Add(Math.Min(lo, hi));
                if (hi != lo)
                {
                    result.Add(Math.Max(lo, hi));
                }
            }
        }

        return result;
    }

    /// <summary>A "nice" axis range around the data (steps of 1, 2, 2.5 or 5 × 10ⁿ), with <paramref name="lines"/> intervals.</summary>
    public static (double Low, double High) NiceRange(double min, double max, int lines = 4, bool fromZero = false)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            return (0, 100);
        }

        if (fromZero)
        {
            min = Math.Min(0, min);
        }

        // A nearly flat line (a fixed clock, say) stays flat instead of turning tiny ripples into spikes.
        var least = Math.Max(Math.Abs(max), Math.Abs(min)) * 0.1;
        least = least > 1e-9 ? least : 1;
        if (max - min < least)
        {
            var mid = (max + min) / 2;
            (min, max) = (mid - (least / 2), mid + (least / 2));
            if (fromZero && min < 0 && mid >= 0)
            {
                (min, max) = (0, least);
            }
        }

        var raw = (max - min) / lines;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= raw);
        var low = Math.Floor(min / step) * step;
        var high = low + (step * lines);
        while (high < max)
        {
            high += step;
        }

        return (low, high);
    }
}
