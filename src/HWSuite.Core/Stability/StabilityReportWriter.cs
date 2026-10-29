using System.Globalization;
using System.Text;
using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Core.Sensors;

namespace HWSuite.Core.Stability;

/// <summary>
/// The stability (or memory) test certificate for the client: same header as the maintenance report, the verdict,
/// what was loaded and for how long, errors per part, peak temperatures and power, and the charts.
/// </summary>
public static class StabilityReportWriter
{
    public static string ToHtml(StabilityResult result, HardwareInventory inv, ServiceOrder order, CompanyProfile company, Strings s)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        static string E(string? text) => MaintenanceReportWriter.Escape(text);
        var memoryOnly = !result.Options.Cpu && !result.Options.Gpu;
        var title = s[memoryOnly ? "stab.report.memTitle" : "stab.report.title"];
        using var w = new StringWriter(CultureInfo.InvariantCulture);
        MaintenanceReportWriter.WriteStart(w, s, title, company, order, inv, result.StartedAt);

        var css = result.Passed ? "v-ok" : "v-problem";
        var headline = s[result.Passed ? "stab.passed" : "stab.failed"];
        var detail = result.Stop switch
        {
            StabilityStop.CpuTemperature => s.Format("stab.stop.cpu", result.Options.CpuTemperatureLimit.ToString("0", c)),
            StabilityStop.GpuTemperature => s.Format("stab.stop.gpu", result.Options.GpuTemperatureLimit.ToString("0", c)),
            StabilityStop.Cancelled => s["stab.stop.cancelled"],
            _ => result.Passed ? s["stab.passed.text"] : s["stab.failed.text"],
        };
        w.Write($"<div class=\"verdict {css}\"><div><div class=\"big\">{E(headline)}</div><div>{E(detail)}</div></div>");
        w.Write($"<div class=\"counts\"><span><b>{E(SvgChart.Elapsed(result.Elapsed))}</b> {E(s["stab.duration"])}</span><span><b>{result.Errors.ToString("N0", c)}</b> {E(s["stab.errors"])}</span></div></div>");

        // What was tested.
        w.Write($"<h2>{E(s["stab.report.parts"])}</h2><table><thead><tr><th>{E(s["stab.col.part"])}</th><th>{E(s["stab.col.load"])}</th><th>{E(s["stab.col.errors"])}</th><th>{E(s["stab.col.peak"])}</th></tr></thead><tbody>");
        void Row(string part, string load, long? errors, string peak)
        {
            var tag = errors is null ? "" : $"<span class=\"tag {(errors == 0 ? "v-ok" : "v-problem")}\">{(errors == 0 ? E(s["stab.none"]) : errors.Value.ToString("N0", c))}</span>";
            w.Write($"<tr><td class=\"item\">{E(part)}</td><td class=\"text\">{E(load)}</td><td class=\"badge\">{tag}</td><td>{E(peak)}</td></tr>");
        }

        string Deg(double? v) => v is { } x ? x.ToString("0", c) + " °C" : "—";
        string Watts(double? v) => v is { } x ? x.ToString("0", c) + " W" : "";
        if (result.Options.Cpu)
        {
            var clock = result.CpuClockUnderLoad is { } mhz ? " · " + s.Format("stab.clock", (mhz / 1000).ToString("0.00", c)) : "";
            var throttle = result.CpuThrottled ? " · " + s["stab.throttled"] : "";
            Row(inv.Processors.FirstOrDefault()?.Name ?? s["mnt.area.Processor"], s.Format("stab.load.cpu", Environment.ProcessorCount),
                result.CpuErrors, Deg(result.MaxCpuTemperature) + (result.MaxCpuPower is null ? "" : " · " + Watts(result.MaxCpuPower)) + clock + throttle);
        }

        if (result.Options.Gpu)
        {
            Row(result.GpuName ?? s["mnt.area.Graphics"], result.GpuError is { } err ? s.Format("stab.load.gpuFailed", err) : s["stab.load.gpu"],
                result.GpuError is null ? result.GpuErrors : null, Deg(result.MaxGpuTemperature) + (result.MaxGpuPower is null ? "" : " · " + Watts(result.MaxGpuPower)));
        }

        if (result.Memory is { } m)
        {
            var gb = (m.BytesUnderTest / (double)(1L << 30)).ToString("0.0", c);
            Row(s["mnt.area.Memory"], s.Format("stab.load.memory", gb, m.Pass, (m.BytesVerified / (double)(1L << 30)).ToString("N0", c)), result.MemoryErrors, "");
        }

        w.Write("</tbody></table>");

        if (result.Memory is { FirstErrors.Count: > 0 } bad)
        {
            w.Write($"<h2>{E(s["stab.report.memErrors"])}</h2><table><thead><tr><th>{E(s["stab.col.offset"])}</th><th>{E(s["stab.col.expected"])}</th><th>{E(s["stab.col.read"])}</th><th>{E(s["stab.col.pattern"])}</th></tr></thead><tbody>");
            foreach (var e in bad.FirstErrors)
            {
                w.Write(string.Create(CultureInfo.InvariantCulture, $"<tr><td>0x{e.Offset:X}</td><td><code>{e.Expected:X16}</code></td><td><code>{e.Actual:X16}</code></td><td>{E(e.Pattern)}</td></tr>"));
            }

            w.Write("</tbody></table>");
            w.Write($"<p class=\"note\">{E(s["stab.memErrors.advice"])}</p>");
        }

        // Charts: temperatures together, then power, then clocks.
        var timeline = result.Timeline;
        if (timeline.Times.Count > 2)
        {
            w.Write($"<div class=\"charts\"><div class=\"keep\"><h2>{E(s["stab.report.charts"])}</h2>");
            var first = true;
            foreach (var (kind, unit, fromZero) in new[] { (SensorKind.Temperature, "°C", false), (SensorKind.Power, "W", true), (SensorKind.Clock, "MHz", false) })
            {
                var series = timeline.Series.Where(x => x.Sensor.Kind == kind).ToList();
                if (series.Count == 0)
                {
                    continue;
                }

                var labels = series.Select(x => s[$"stab.series.{x.Sensor.Key}"] + $" ({s["mnt.report.chartRange"]})").ToList();
                w.Write(SvgChart.Render(timeline with { Series = series }, unit, c, labels, elapsed: true, height: 160, fromZero: fromZero));
                if (first)
                {
                    w.Write("</div>");
                    first = false;
                }
            }

            w.Write(first ? "</div></div>" : "</div>");
        }

        w.Write($"<p class=\"note\">{E(s[memoryOnly ? "stab.report.memNote" : "stab.report.note"])}</p>");
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            w.Write($"<h2>{E(s["mnt.report.notes"])}</h2><p>{E(order.Notes)}</p>");
        }

        w.Write($"<div class=\"sign\"><div>{E(company.Technician ?? s["mnt.report.technician"])}<br>{E(company.Name)}</div><div>{E(order.Client ?? s["mnt.report.client"])}</div></div>");
        w.Write($"<footer>{E(s.Format("mnt.report.footer", BuildInfo.Current.Display))}</footer></div></body></html>");
        return w.ToString();
    }

    /// <summary>"hwsuite-estabilidade-2026-10-29-1430-Client.html" (memory: "hwsuite-memoria-...").</summary>
    public static string FileName(StabilityResult result, ServiceOrder order, string machine, string extension)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(order);
        var kind = !result.Options.Cpu && !result.Options.Gpu ? "memoria" : "estabilidade";
        var fake = new MaintenanceReport(new CompanyProfile(null, null, null, null, null, null), order, [], new HardwareInventory { MachineName = machine }, result.StartedAt, "");
        return MaintenanceReportWriter.FileName(fake, extension).Replace("hwsuite-", $"hwsuite-{kind}-", StringComparison.Ordinal);
    }
}
