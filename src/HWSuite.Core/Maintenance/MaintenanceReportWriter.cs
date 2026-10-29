using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;

namespace HWSuite.Core.Maintenance;

/// <summary>
/// Writes the maintenance report as one self-contained HTML page made for A4 paper (and for "Save as PDF"):
/// the company header with its logo, the client and service order, the overall verdict, every check grouped by
/// area with its recommendation, the services performed, the equipment summary, an optional full inventory,
/// and signature lines. Light background and real colors, since it is printed and handed to the client.
/// </summary>
public static class MaintenanceReportWriter
{
    public static void Write(MaintenanceReport report, Strings s, TextWriter w)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(w);
        var c = s.Culture;
        static string E(string? text) => Escape(text);
        string Lines(string? text) => E(text).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
        var company = report.Company;
        var order = report.Order;
        var inv = report.Inventory;

        w.Write($$"""
            <!doctype html>
            <html lang="{{(c.Name.StartsWith("pt", StringComparison.Ordinal) ? "pt-BR" : "en")}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{E(s["mnt.report.title"])}} — {{E(order.Client ?? inv.MachineName)}}</title>
            <style>
            @page { size: A4; margin: 14mm 13mm 16mm 13mm; }
            * { box-sizing: border-box; }
            body { margin: 0; font: 10.5pt/1.45 "Segoe UI", system-ui, -apple-system, Arial, sans-serif; color: #18222c; background: #eef2f5; }
            .page { max-width: 210mm; margin: 0 auto; background: #fff; padding: 14mm 13mm; }
            @media print { body { background: #fff; } .page { padding: 0; max-width: none; } }
            header.top { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; padding-bottom: 12px; border-bottom: 3px solid #0e7490; }
            .brand { display: flex; gap: 14px; align-items: center; }
            .brand img { max-height: 58px; max-width: 160px; object-fit: contain; }
            .brand .name { font-size: 15pt; font-weight: 700; color: #0b3b4a; }
            .brand .meta { color: #4a5a68; font-size: 9pt; }
            .doc { text-align: right; }
            .doc h1 { margin: 0; font-size: 15pt; letter-spacing: .02em; color: #0b3b4a; text-transform: uppercase; }
            .doc .meta { color: #4a5a68; font-size: 9pt; }
            .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin: 14px 0; }
            .box { border: 1px solid #d5dee6; border-radius: 6px; padding: 9px 12px; }
            .box h3 { margin: 0 0 4px 0; font-size: 8.5pt; letter-spacing: .08em; text-transform: uppercase; color: #5b6b78; }
            .box p { margin: 1px 0; }
            .verdict { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 12px 16px; border-radius: 8px; margin: 14px 0 6px 0; color: #fff; }
            .verdict .big { font-size: 14pt; font-weight: 700; }
            .v-ok { background: #0f8a5f; } .v-attention { background: #c27803; } .v-problem { background: #c0262d; } .v-info { background: #4a5a68; }
            .counts { display: flex; gap: 14px; font-size: 9.5pt; }
            .counts span b { font-size: 12pt; }
            h2 { font-size: 11pt; margin: 18px 0 6px 0; color: #0b3b4a; text-transform: uppercase; letter-spacing: .05em; border-bottom: 1px solid #d5dee6; padding-bottom: 4px; }
            table { width: 100%; border-collapse: collapse; }
            th { text-align: left; font-size: 8.5pt; color: #5b6b78; text-transform: uppercase; letter-spacing: .06em; padding: 4px 6px; border-bottom: 1px solid #c9d4dc; }
            td { padding: 6px; border-bottom: 1px solid #e6ecf0; vertical-align: top; }
            tr { page-break-inside: avoid; }
            td.item { width: 26%; font-weight: 600; }
            td.badge { width: 92px; }
            .tag { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 8.5pt; font-weight: 700; color: #fff; white-space: nowrap; }
            .advice { color: #7a3d00; margin-top: 3px; font-size: 9.5pt; }
            .advice::before { content: "→ "; }
            .area td { background: #f3f7fa; font-weight: 700; color: #0b3b4a; font-size: 9pt; text-transform: uppercase; letter-spacing: .06em; }
            .text { white-space: normal; }
            .inv td:first-child { width: 34%; color: #4a5a68; }
            .sign { display: grid; grid-template-columns: 1fr 1fr; gap: 40px; margin-top: 46px; page-break-inside: avoid; }
            .sign div { border-top: 1px solid #18222c; padding-top: 4px; text-align: center; font-size: 9pt; color: #4a5a68; }
            footer { margin-top: 22px; font-size: 8pt; color: #7b8a96; text-align: center; }
            .appendix { page-break-before: always; }
            ol.actions { margin: 4px 0 0 0; padding-left: 22px; }
            ol.actions li { margin: 5px 0; page-break-inside: avoid; }
            ol.actions .tag { margin-right: 4px; vertical-align: 1px; }
            .charts svg { display: block; margin: 4px 0 10px 0; page-break-inside: avoid; }
            .keep { page-break-inside: avoid; }
            .note { color: #4a5a68; font-size: 9.5pt; margin: 2px 0 6px 0; }
            </style>
            </head>
            <body><div class="page">
            """);

        // Header: company left, document right.
        w.Write("<header class=\"top\"><div class=\"brand\">");
        if (!string.IsNullOrEmpty(company.LogoDataUri))
        {
            w.Write($"<img src=\"{E(company.LogoDataUri)}\" alt=\"\">");
        }

        w.Write("<div>");
        w.Write($"<div class=\"name\">{E(company.Name ?? s["mnt.report.noCompany"])}</div>");
        foreach (var line in new[] { company.Document, company.Contact, company.Address }.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            w.Write($"<div class=\"meta\">{E(line)}</div>");
        }

        w.Write("</div></div><div class=\"doc\">");
        w.Write($"<h1>{E(s["mnt.report.title"])}</h1>");
        if (!string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            w.Write($"<div class=\"meta\">{E(s["mnt.report.order"])}: <b>{E(order.OrderNumber)}</b></div>");
        }

        w.Write($"<div class=\"meta\">{E(report.At.LocalDateTime.ToString("f", c))}</div>");
        if (!string.IsNullOrWhiteSpace(company.Technician))
        {
            w.Write($"<div class=\"meta\">{E(s["mnt.report.technician"])}: {E(company.Technician)}</div>");
        }

        w.Write("</div></header>");

        // Client and equipment.
        w.Write("<div class=\"grid2\"><div class=\"box\">");
        w.Write($"<h3>{E(s["mnt.report.client"])}</h3><p><b>{E(order.Client ?? "—")}</b></p>");
        if (!string.IsNullOrWhiteSpace(order.ClientContact))
        {
            w.Write($"<p>{E(order.ClientContact)}</p>");
        }

        w.Write("</div><div class=\"box\">");
        w.Write($"<h3>{E(s["mnt.report.equipment"])}</h3>");
        if (!string.IsNullOrWhiteSpace(order.Equipment))
        {
            w.Write($"<p><b>{E(order.Equipment)}</b></p>");
        }

        var machine = string.Join(" ", new[] { inv.System?.Manufacturer, inv.System?.Product }.Where(Real));
        var board = string.Join(" ", new[] { inv.Motherboard?.Manufacturer, inv.Motherboard?.Product }.Where(Real));
        w.Write($"<p>{E(inv.MachineName)}{(machine.Length > 0 ? " · " + E(machine) : "")}</p>");
        if (board.Length > 0 && board != machine)
        {
            w.Write($"<p>{E(board)}</p>");
        }

        if (Real(inv.System?.SerialNumber))
        {
            w.Write($"<p>{E(s["mnt.report.serial"])}: {E(inv.System!.SerialNumber)}</p>");
        }

        w.Write("</div></div>");

        // Overall verdict.
        var overall = report.Overall;
        w.Write($"<div class=\"verdict {Css(overall)}\"><div><div class=\"big\">{E(s[$"mnt.overall.{overall}"])}</div><div>{E(s[$"mnt.overall.{overall}.text"])}</div></div>");
        w.Write("<div class=\"counts\">");
        foreach (var v in new[] { Verdict.Ok, Verdict.Attention, Verdict.Problem })
        {
            w.Write($"<span><b>{report.Count(v)}</b> {E(s[$"mnt.verdict.{v}"])}</span>");
        }

        w.Write("</div></div>");

        // What to do, most urgent first: the part of the report the client reads.
        var actions = report.Checks.Where(x => x.Verdict >= Verdict.Attention && !string.IsNullOrWhiteSpace(x.Advice))
            .OrderByDescending(x => x.Verdict).ToList();
        if (actions.Count > 0)
        {
            w.Write($"<h2>{E(s["mnt.report.actions"])}</h2><ol class=\"actions\">");
            foreach (var a in actions)
            {
                w.Write($"<li><span class=\"tag {Css(a.Verdict)}\">{E(s[$"mnt.verdict.{a.Verdict}"])}</span> <b>{E(a.Item)}</b>: {E(a.Advice)}</li>");
            }

            w.Write("</ol>");
        }

        // Checks.
        w.Write($"<h2>{E(s["mnt.report.checks"])}</h2><table><thead><tr><th>{E(s["mnt.report.item"])}</th><th>{E(s["mnt.report.finding"])}</th><th>{E(s["mnt.report.result"])}</th></tr></thead><tbody>");
        foreach (var area in report.Checks.GroupBy(x => x.Area))
        {
            w.Write($"<tr class=\"area\"><td colspan=\"3\">{E(s[$"mnt.area.{area.Key}"])}</td></tr>");
            foreach (var check in area)
            {
                w.Write($"<tr><td class=\"item\">{E(check.Item)}</td><td class=\"text\">{E(check.Finding)}");
                if (!string.IsNullOrWhiteSpace(check.Advice))
                {
                    w.Write($"<div class=\"advice\">{E(check.Advice)}</div>");
                }

                w.Write($"</td><td class=\"badge\"><span class=\"tag {Css(check.Verdict)}\">{E(s[$"mnt.verdict.{check.Verdict}"])}</span></td></tr>");
            }
        }

        w.Write("</tbody></table>");

        // Load test: what the processor did over time.
        if (report.Load?.Timeline is { Times.Count: > 2 } timeline)
        {
            var load = report.Load;
            // The title stays with the first chart (no heading alone at the bottom of a page).
            w.Write($"<div class=\"charts\"><div class=\"keep\"><h2>{E(s["mnt.report.loadChart"])}</h2><p class=\"note\">{E(s.Format("mnt.report.loadChartText", Reporting.SvgChart.Elapsed(load.Duration), load.Errors))}</p>");
            var first = true;
            foreach (var (key, label) in new[] { ("cpu.temperature", "mnt.report.chartTemp"), ("cpu.power", "mnt.report.chartPower"), ("cpu.clock", "mnt.report.chartClock") })
            {
                if (timeline.Find(key) is { } series && Sensors.SeriesStats.Of(series.Values) is not null)
                {
                    w.Write(Reporting.SvgChart.Render(timeline with { Series = [series] }, series.Sensor.Unit, c, [s[label] + $" ({s["mnt.report.chartRange"]})"], elapsed: true, height: 150));
                    if (first)
                    {
                        w.Write("</div>");
                        first = false;
                    }
                }
            }

            w.Write(first ? "</div></div>" : "</div>");
        }

        // Services performed and notes.
        if (!string.IsNullOrWhiteSpace(order.ServicesDone))
        {
            w.Write($"<h2>{E(s["mnt.report.services"])}</h2><p>{Lines(order.ServicesDone)}</p>");
        }

        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            w.Write($"<h2>{E(s["mnt.report.notes"])}</h2><p>{Lines(order.Notes)}</p>");
        }

        // Equipment summary: the main parts in one table.
        w.Write($"<h2>{E(s["mnt.report.summary"])}</h2><table class=\"inv\"><tbody>");
        void Row(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                w.Write($"<tr><td>{E(label)}</td><td>{E(value)}</td></tr>");
            }
        }

        Row(s["mnt.sum.os"], string.Join(" ", new[] { inv.OperatingSystem?.Name, inv.OperatingSystem?.DisplayVersion, inv.OperatingSystem?.Build is { } bld ? $"({bld})" : null }.OfType<string>()));
        foreach (var cpu in inv.Processors)
        {
            Row(s["mnt.sum.cpu"], $"{cpu.Name} · {cpu.Cores}C/{cpu.Threads}T");
        }

        Row(s["mnt.sum.board"], board);
        Row(s["mnt.sum.bios"], string.Join(" · ", new[] { inv.Bios?.Version, DateText.Display(inv.Bios?.ReleaseDate, c) }.OfType<string>()));
        if (inv.Memory is { } mem)
        {
            var modules = string.Join(", ", mem.Modules.Select(m => $"{ReportModel.Bytes(m.SizeBytes, c)} {m.Type} {m.ConfiguredSpeedMts ?? m.SpeedMts} MT/s".Trim()));
            Row(s["mnt.sum.memory"], $"{ReportModel.Bytes(mem.TotalInstalledBytes ?? mem.Modules.Sum(m => m.SizeBytes), c)}" + (modules.Length > 0 ? $" ({modules})" : ""));
        }

        foreach (var gpu in inv.Gpus.Where(g => g.Name is not null && !g.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)))
        {
            Row(s["mnt.sum.gpu"], gpu.Name + (gpu.DedicatedMemoryBytes is > 0 ? $" · {ReportModel.Bytes(gpu.DedicatedMemoryBytes.Value, c)}" : ""));
        }

        foreach (var disk in inv.Disks)
        {
            Row(s["mnt.sum.disk"], string.Join(" · ", new[] { disk.Model, disk.SizeBytes is { } sz ? ReportModel.Bytes(sz, c) : null, disk.BusType, disk.MediaType }.Where(x => !string.IsNullOrWhiteSpace(x))));
        }

        w.Write("</tbody></table>");

        // Signatures.
        w.Write($"<div class=\"sign\"><div>{E(company.Technician ?? s["mnt.report.technician"])}<br>{E(company.Name)}</div><div>{E(order.Client ?? s["mnt.report.client"])}</div></div>");
        w.Write($"<footer>{E(s.Format("mnt.report.footer", report.AppVersion))}</footer>");

        // Full inventory as an appendix.
        if (report.IncludeInventory)
        {
            w.Write($"<div class=\"appendix\"><h2>{E(s["mnt.report.appendix"])}</h2>");
            foreach (var section in ReportModel.Build(inv, s))
            {
                w.Write($"<h2 style=\"font-size:10pt\">{E(section.Title)}</h2><table class=\"inv\"><tbody>");
                foreach (var group in section.Groups)
                {
                    if (group.Title is not null)
                    {
                        w.Write($"<tr class=\"area\"><td colspan=\"2\">{E(group.Title)}</td></tr>");
                    }

                    foreach (var row in group.Rows)
                    {
                        w.Write($"<tr><td>{E(row.Label)}</td><td>{E(row.Value)}</td></tr>");
                    }
                }

                w.Write("</tbody></table>");
            }

            w.Write("</div>");
        }

        w.Write("</div></body></html>");
    }

    public static string ToHtml(MaintenanceReport report, Strings s)
    {
        using var writer = new StringWriter();
        Write(report, s, writer);
        return writer.ToString();
    }

    /// <summary>"report-2026-10-29-1430-Acme Ltda.html": the date first so a folder lists them in order.</summary>
    public static string FileName(MaintenanceReport report, string extension)
    {
        ArgumentNullException.ThrowIfNull(report);
        var who = report.Order.Client ?? report.Inventory.MachineName ?? "pc";
        var safe = new string(who.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' or ':' ? '_' : ch).ToArray()).Trim();
        if (safe.Length > 60)
        {
            safe = safe[..60];
        }

        return $"hwsuite-{report.At:yyyy-MM-dd-HHmm}-{safe}.{extension.TrimStart('.')}";
    }

    /// <summary>False for empty values and the placeholders boards ship with ("System Product Name", "To be filled by O.E.M.").</summary>
    private static bool Real(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.All(ch => ch is '0' or ' ' or '-')
        && !Placeholders.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] Placeholders = ["System Product Name", "System Serial Number", "To be filled", "Default string", "System manufacturer", "Not Applicable", "O.E.M."];

    /// <summary>HTML-escapes the five special characters only, so accented text stays readable in the file (it is UTF-8).</summary>
    public static string Escape(string? text) => (text ?? "")
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    private static string Css(Verdict v) => v switch
    {
        Verdict.Ok => "v-ok",
        Verdict.Attention => "v-attention",
        Verdict.Problem => "v-problem",
        _ => "v-info",
    };
}
