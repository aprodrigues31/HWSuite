using System.Globalization;
using System.Text;
using HWSuite.Core;
using HWSuite.Core.Localization;
using HWSuite.Core.Reporting;
using HWSuite.Core.Sensors;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite log file.csv": summary of a sensor log (min / average / max of each sensor); with -o, an HTML page with
/// one chart per device and unit, to read a test recorded at a client's machine afterwards.
/// </summary>
internal static class LogCommand
{
    /// <summary>Kinds charted when no --filter is given (the rest stay in the table).</summary>
    private static readonly SensorKind[] Charted = [SensorKind.Temperature, SensorKind.Power, SensorKind.Fan, SensorKind.Clock, SensorKind.Voltage, SensorKind.Load];

    private const int LinesPerChart = 8;

    public static int Run(CliOptions options, Strings s)
    {
        if (options.LogFile is not { } file)
        {
            Console.Error.WriteLine(s.Format("cli.missingValue", "log <file.csv>"));
            return 2;
        }

        TimelineData data;
        try
        {
            data = SensorLogReader.Read(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine(s.Format("log.cannotRead", file, ex.Message));
            return 2;
        }

        var series = data.Series
            .Where(x => options.Filter is null || x.Sensor.Key.Contains(options.Filter, StringComparison.OrdinalIgnoreCase))
            .Where(x => SeriesStats.Of(x.Values) is not null)
            .ToList();
        data = data with { Series = series };
        var c = s.Culture;

        Console.WriteLine(Header(data, file, s));
        foreach (var device in series.GroupBy(x => x.Sensor.Hardware))
        {
            Console.WriteLine();
            Console.WriteLine(device.Key);
            var width = Math.Max(24, device.Max(x => x.Sensor.Name.Length) + 2);
            Console.WriteLine($"    {s["sensors.sensor"].PadRight(width)}{s["log.min"],12}{s["log.avg"],12}{s["log.max"],12}");
            foreach (var x in device)
            {
                var st = SeriesStats.Of(x.Values)!;
                Console.WriteLine($"    {x.Sensor.Name.PadRight(width)}{SensorFormat.Format(st.Min, x.Sensor.Kind, c),12}{SensorFormat.Format(st.Average, x.Sensor.Kind, c),12}{SensorFormat.Format(st.Max, x.Sensor.Kind, c),12}");
            }
        }

        if (options.OutputPath is { } output)
        {
            var path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Html(data, file, options.Filter is not null, s), new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine(s.Format("cli.written", path));
        }

        return 0;
    }

    private static string Header(TimelineData data, string file, Strings s)
    {
        var c = s.Culture;
        return data.Times.Count == 0
            ? s.Format("log.empty", Path.GetFileName(file))
            : s.Format("log.header", Path.GetFileName(file), data.Times[0].ToLocalTime().ToString("g", c), SvgChart.Elapsed(data.Span), data.Times.Count, data.Series.Count);
    }

    internal static string Html(TimelineData data, string file, bool everything, Strings s)
    {
        static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
        var c = s.Culture;
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="{{c.Name}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{E(s["log.title"])}} — {{E(Path.GetFileName(file))}}</title>
            <style>
            body { margin: 0; padding: 24px 16px; font: 13px/1.45 "Segoe UI", system-ui, sans-serif; color: #1d1d1f; background: #fff; }
            main { max-width: 900px; margin: 0 auto; }
            h1 { font-size: 20px; margin: 0 0 4px; } .meta { color: #6e6e73; margin-bottom: 18px; }
            h2 { font-size: 14px; margin: 22px 0 4px; color: #0b3b4a; border-bottom: 1px solid #e5e5ea; padding-bottom: 4px; }
            svg { display: block; margin: 6px 0; page-break-inside: avoid; }
            footer { color: #6e6e73; font-size: 11px; margin-top: 24px; }
            </style></head><body><main>
            <h1>{{E(s["log.title"])}}</h1><div class="meta">{{E(Header(data, file, s))}}</div>
            """);
        foreach (var group in data.Series
            .Where(x => everything || Charted.Contains(x.Sensor.Kind))
            .GroupBy(x => (x.Sensor.Hardware, x.Sensor.Unit, x.Sensor.Kind)))
        {
            var lines = group.ToList();
            for (var part = 0; part < lines.Count; part += LinesPerChart)
            {
                var chunk = lines.Skip(part).Take(LinesPerChart).ToList();
                var kind = s[$"sensorKind.{group.Key.Kind}"];
                html.Append(CultureInfo.InvariantCulture, $"<h2>{E(group.Key.Hardware)} · {E(kind)}</h2>");
                html.Append(SvgChart.Render(data with { Series = chunk }, group.Key.Unit, c, fromZero: group.Key.Kind is SensorKind.Fan or SensorKind.Load or SensorKind.Power,
                    height: 200));
            }
        }

        html.Append(CultureInfo.InvariantCulture, $"<footer>HWSuite {E(BuildInfo.Current.Display)}</footer></main></body></html>");
        return html.ToString();
    }
}
