using System.Globalization;
using HWSuite.Core.Localization;
using HWSuite.Core.Reporting;

namespace HWSuite.Core.Sensors;

/// <summary>Units and number formats per sensor kind (values arrive in the source library's base units).</summary>
public static class SensorFormat
{
    public static string Unit(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Voltage => "V",
        SensorKind.Current => "A",
        SensorKind.Power => "W",
        SensorKind.Clock => "MHz",
        SensorKind.Load or SensorKind.Control or SensorKind.Level => "%",
        SensorKind.Fan => "RPM",
        SensorKind.Frequency => "Hz",
        SensorKind.Flow => "L/h",
        SensorKind.Data => "GB",
        SensorKind.SmallData => "MB",
        SensorKind.Throughput => "B/s",
        SensorKind.TimeSpan => "s",
        SensorKind.Energy => "mWh",
        SensorKind.Noise => "dBA",
        _ => "",
    };

    public static string Format(double? value, SensorKind kind, CultureInfo culture)
    {
        if (value is not { } v || double.IsNaN(v) || double.IsInfinity(v))
        {
            return "—";
        }

        if (kind == SensorKind.Throughput)
        {
            return ReportModel.Bytes((long)Math.Max(0, v), culture) + "/s";
        }

        var format = kind switch
        {
            SensorKind.Voltage => "0.000",
            SensorKind.Current or SensorKind.Factor => "0.00",
            SensorKind.Clock or SensorKind.Fan or SensorKind.Frequency or SensorKind.SmallData or SensorKind.TimeSpan or SensorKind.Energy => "0",
            _ => "0.0",
        };

        var unit = Unit(kind);
        var text = v.ToString(format, culture);
        return unit.Length == 0 ? text : $"{text} {unit}";
    }
}

/// <summary>Console table grouped by device, with current, minimum and maximum values.</summary>
public static class SensorTextWriter
{
    public static void Write(SensorSnapshot snapshot, Strings strings, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(output);

        var c = strings.Culture;
        if (snapshot.Readings.Count == 0)
        {
            output.WriteLine(strings["sensors.none"]);
            return;
        }

        var nameWidth = Math.Max(24, snapshot.Readings.Max(r => r.Name.Length) + 2);
        const int ValueWidth = 14;
        var header = $"    {strings["sensors.sensor"].PadRight(nameWidth)}{strings["sensors.value"],ValueWidth}{strings["sensors.min"],ValueWidth}{strings["sensors.max"],ValueWidth}";

        foreach (var device in snapshot.Readings.GroupBy(r => (r.Parent, r.Hardware, r.HardwareKind)))
        {
            output.WriteLine();
            var title = device.Key.Parent is null ? device.Key.Hardware : $"{device.Key.Parent} › {device.Key.Hardware}";
            output.WriteLine($"{title}  [{device.Key.HardwareKind}]");
            output.WriteLine(header);

            foreach (var kindGroup in device.GroupBy(r => r.Kind).OrderBy(g => g.Key))
            {
                output.WriteLine($"  {strings[$"sensorKind.{kindGroup.Key}"]}");
                foreach (var r in kindGroup)
                {
                    output.WriteLine(
                        $"    {r.Name.PadRight(nameWidth)}" +
                        $"{SensorFormat.Format(r.Value, r.Kind, c),ValueWidth}" +
                        $"{SensorFormat.Format(r.Min, r.Kind, c),ValueWidth}" +
                        $"{SensorFormat.Format(r.Max, r.Kind, c),ValueWidth}");
                }
            }
        }
    }
}

/// <summary>
/// Appends snapshots as CSV rows: one timestamp column, then one column per sensor fixed by the
/// first snapshot (sensors that appear later are ignored so columns never shift). Numbers use
/// the invariant culture so the file opens the same everywhere.
/// </summary>
public sealed class SensorCsvLogger
{
    private readonly TextWriter _output;
    private List<string>? _columns;

    public SensorCsvLogger(TextWriter output) => _output = output ?? throw new ArgumentNullException(nameof(output));

    public void Append(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_columns is null)
        {
            _columns = [.. snapshot.Readings.Select(r => r.Id)];
            var names = snapshot.Readings.Select(r =>
            {
                var unit = SensorFormat.Unit(r.Kind);
                return Escape($"{r.Hardware} / {r.Name}" + (unit.Length > 0 ? $" [{unit}]" : ""));
            });
            _output.WriteLine("timestamp," + string.Join(",", names));
        }

        var byId = snapshot.Readings.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Value);
        var cells = _columns.Select(id =>
            byId.TryGetValue(id, out var v) && v is { } value && double.IsFinite(value)
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : "");

        _output.WriteLine(snapshot.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture) + "," + string.Join(",", cells));
        _output.Flush();
    }

    private static string Escape(string text) =>
        text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
}
