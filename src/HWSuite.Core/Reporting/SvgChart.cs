using System.Globalization;
using System.Text;
using HWSuite.Core.Sensors;

namespace HWSuite.Core.Reporting;

/// <summary>
/// Draws a timeline as an inline SVG line chart for printed reports (light background): value grid with its
/// labels, time along the bottom, one colored line per series and a legend with min / average / max.
/// </summary>
public static class SvgChart
{
    /// <summary>Print-friendly line colors, distinguishable in grayscale order too.</summary>
    public static readonly IReadOnlyList<string> Palette = ["#0a66c2", "#d9480f", "#2b8a3e", "#ae3ec9", "#e8a400", "#0b7285", "#c92a2a", "#495057"];

    /// <param name="labels">Legend text per series (defaults to the sensor name).</param>
    /// <param name="elapsed">Bottom axis as time since the start (m:ss) instead of the clock.</param>
    public static string Render(TimelineData data, string unit, CultureInfo culture, IReadOnlyList<string>? labels = null,
        bool elapsed = false, int width = 720, int height = 220, bool fromZero = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(culture);
        const int Left = 52, Right = 12, Top = 22, Bottom = 26;
        var legendRows = (data.Series.Count + 1) / 2;
        var legendHeight = 8 + (legendRows * 18);
        var plotW = width - Left - Right;
        var plotH = height - Top - Bottom;
        var all = data.Series.SelectMany(s => s.Values).Where(double.IsFinite).ToList();
        var (low, high) = all.Count > 0 ? ChartReducer.NiceRange(all.Min(), all.Max(), 4, fromZero) : (0, 100);
        string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"chart\" viewBox=\"0 0 {width} {height + legendHeight}\" width=\"100%\" role=\"img\">");
        svg.Append("<style>.chart text{font:11px 'Segoe UI',system-ui,sans-serif;fill:#6e6e73}.chart .lg{fill:#1d1d1f}</style>");

        // value grid
        for (var i = 0; i <= 4; i++)
        {
            var y = Top + (plotH * i / 4.0);
            var value = high - ((high - low) * i / 4);
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{Left}\" x2=\"{width - Right}\" y1=\"{N(y)}\" y2=\"{N(y)}\" stroke=\"#e5e5ea\" stroke-width=\"1\"/>");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{Left - 6}\" y=\"{N(y + 4)}\" text-anchor=\"end\">{Escape(Value(value, high - low, culture))}</text>");
        }

        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"4\" y=\"11\">{Escape(unit)}</text>");

        // time axis
        var count = data.Times.Count;
        if (count > 1)
        {
            var start = data.Times[0];
            var span = (data.Times[^1] - start).TotalSeconds;
            for (var i = 0; i <= 4; i++)
            {
                var x = Left + (plotW * i / 4.0);
                var at = TimeSpan.FromSeconds(span * i / 4);
                var text = elapsed ? Elapsed(at) : (start + at).ToLocalTime().ToString(span > 3600 * 6 ? "dd/MM HH:mm" : "HH:mm:ss", culture);
                var anchor = i == 0 ? "start" : i == 4 ? "end" : "middle";
                svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{N(x)}\" y=\"{height - 8}\" text-anchor=\"{anchor}\">{Escape(text)}</text>");
            }

            // lines
            for (var s = 0; s < data.Series.Count; s++)
            {
                var values = data.Series[s].Values;
                var points = new StringBuilder();
                foreach (var i in ChartReducer.Indexes(values, plotW / 2))
                {
                    var x = Left + ((data.Times[i] - start).TotalSeconds / Math.Max(span, 1e-9) * plotW);
                    var y = Top + plotH - ((Math.Clamp(values[i], low, high) - low) / (high - low) * plotH);
                    points.Append(N(x)).Append(',').Append(N(y)).Append(' ');
                }

                if (points.Length > 0)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<polyline fill=\"none\" stroke=\"{Palette[s % Palette.Count]}\" stroke-width=\"1.6\" stroke-linejoin=\"round\" points=\"{points.ToString().TrimEnd()}\"/>");
                }
            }
        }

        // legend: color, name, min / avg / max
        for (var s = 0; s < data.Series.Count; s++)
        {
            var x = s % 2 == 0 ? Left : Left + (plotW / 2);
            var y = height + 8 + (s / 2 * 18);
            var series = data.Series[s];
            var name = labels is not null && s < labels.Count ? labels[s] : series.Sensor.Name;
            var stats = SeriesStats.Of(series.Values);
            var range = stats is null ? "—" : $"{Value(stats.Min, high - low, culture)} / {Value(stats.Average, high - low, culture)} / {Value(stats.Max, high - low, culture)}";
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(x)}\" y=\"{y - 9}\" width=\"12\" height=\"3\" fill=\"{Palette[s % Palette.Count]}\"/>");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{N(x + 18)}\" y=\"{y}\"><tspan class=\"lg\">{Escape(Shorten(name, 38))}</tspan>  {Escape(range)}</text>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>Precision that suits the range: volts get decimals, RPM do not.</summary>
    public static string Value(double value, double range, CultureInfo culture) =>
        value.ToString(range < 2 ? "0.000" : range < 20 ? "0.0" : "0", culture);

    public static string Elapsed(TimeSpan at) =>
        at.TotalHours >= 1 ? $"{(int)at.TotalHours}:{at.Minutes:00}:{at.Seconds:00}" : $"{(int)at.TotalMinutes}:{at.Seconds:00}";

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
