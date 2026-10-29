using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;
using HWSuite.Core.Stability;

namespace HWSuite.Core.Maintenance;

public sealed record DiskRecord(string Model, string? Serial, int? WearPercent, int? Temperature, long? Reallocated, long? PowerOnHours, string? Health);

public sealed record VolumeRecord(string Name, double FreePercent, long FreeBytes, long TotalBytes);

/// <summary>A stability or memory test, summarised for the history.</summary>
public sealed record TestRecord(bool Passed, double Minutes, long CpuErrors, long GpuErrors, long MemoryErrors, string? Stop, double? MaxCpuTemperature, double? MaxGpuTemperature, double? MemoryGb);

/// <summary>
/// One service, as kept for the history and the batch summary: saved as "report-name.hwsuite.json" beside each report,
/// so the reports folder (or a pen drive's) is the history itself, with no database.
/// </summary>
public sealed record AttendanceRecord
{
    public const string Extension = ".hwsuite.json";

    /// <summary>"maintenance", "stability" or "memory".</summary>
    public string Kind { get; init; } = "maintenance";

    public DateTimeOffset At { get; init; }

    public string? Client { get; init; }

    public string? Order { get; init; }

    public string? Equipment { get; init; }

    public string Machine { get; init; } = "";

    public string? Serial { get; init; }

    public string? Technician { get; init; }

    public string? AppVersion { get; init; }

    public Verdict Overall { get; init; }

    public int Ok { get; init; }

    public int Attention { get; init; }

    public int Problem { get; init; }

    public string? Os { get; init; }

    public string? Cpu { get; init; }

    public string? Gpu { get; init; }

    public double? MemoryGb { get; init; }

    public double? CpuIdleTemperature { get; init; }

    public double? CpuLoadTemperature { get; init; }

    public double? GpuIdleTemperature { get; init; }

    public IReadOnlyList<DiskRecord> Disks { get; init; } = [];

    public IReadOnlyList<VolumeRecord> Volumes { get; init; } = [];

    /// <summary>"Item: finding" of every attention and problem item, problems first.</summary>
    public IReadOnlyList<string> Findings { get; init; } = [];

    public TestRecord? Test { get; init; }

    /// <summary>The report's file name (in the same folder), to open it from the history.</summary>
    public string? ReportFile { get; init; }

    /// <summary>Which computer this was: the serial number when real, else the machine name.</summary>
    [JsonIgnore]
    public string MachineKey => string.IsNullOrWhiteSpace(Serial) ? Machine : $"{Machine}|{Serial}";

    public static AttendanceRecord From(MaintenanceReport report, MaintenanceInput? input, string? reportFile)
    {
        ArgumentNullException.ThrowIfNull(report);
        var inv = report.Inventory;
        var dash = input?.Sensors is { } snapshot ? Dashboard.From(snapshot) : null;
        return Base(inv, report.Order, report.Company, report.At, report.AppVersion, reportFile) with
        {
            Kind = "maintenance",
            Overall = report.Overall,
            Ok = report.Count(Verdict.Ok),
            Attention = report.Count(Verdict.Attention),
            Problem = report.Count(Verdict.Problem),
            CpuIdleTemperature = dash?.Cpu.Temperature,
            GpuIdleTemperature = dash?.Gpu.Temperature,
            CpuLoadTemperature = report.Load?.MaxCpuTemperature,
            Volumes = [.. (input?.Volumes ?? []).Select(v => new VolumeRecord(v.Name, Math.Round(v.FreePercent, 1), v.FreeBytes, v.TotalBytes))],
            Findings = [.. report.Checks.Where(c => c.Verdict >= Verdict.Attention).OrderByDescending(c => c.Verdict).Select(c => $"{c.Item}: {c.Finding}")],
        };
    }

    public static AttendanceRecord From(StabilityResult result, HardwareInventory inv, ServiceOrder order, CompanyProfile company, string appVersion, string? reportFile)
    {
        ArgumentNullException.ThrowIfNull(result);
        var memoryOnly = !result.Options.Cpu && !result.Options.Gpu;
        return Base(inv, order, company, result.StartedAt, appVersion, reportFile) with
        {
            Kind = memoryOnly ? "memory" : "stability",
            Overall = result.Passed ? Verdict.Ok : Verdict.Problem,
            Test = new TestRecord(result.Passed, Math.Round(result.Elapsed.TotalMinutes, 1), result.CpuErrors, result.GpuErrors, result.MemoryErrors,
                result.Stop.ToString(), result.MaxCpuTemperature, result.MaxGpuTemperature,
                result.Memory is { } m ? Math.Round(m.BytesUnderTest / (double)(1L << 30), 1) : null),
            CpuLoadTemperature = result.MaxCpuTemperature,
        };
    }

    private static AttendanceRecord Base(HardwareInventory inv, ServiceOrder order, CompanyProfile company, DateTimeOffset at, string? version, string? reportFile)
    {
        var serial = inv.System?.SerialNumber;
        var realSerial = !string.IsNullOrWhiteSpace(serial) && !serial.Contains("Serial", StringComparison.OrdinalIgnoreCase)
            && !serial.Contains("O.E.M", StringComparison.OrdinalIgnoreCase) && !serial.All(ch => ch is '0' or ' ');
        return new AttendanceRecord
        {
            At = at,
            Client = order.Client,
            Order = order.OrderNumber,
            Equipment = order.Equipment,
            Machine = inv.MachineName ?? "",
            Serial = realSerial ? serial : null,
            Technician = company.Technician,
            AppVersion = version,
            Os = string.Join(" ", new[] { inv.OperatingSystem?.Name, inv.OperatingSystem?.DisplayVersion }.OfType<string>()),
            Cpu = inv.Processors.FirstOrDefault()?.Name,
            Gpu = inv.Gpus.FirstOrDefault(g => g.Name is not null && !g.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))?.Name,
            MemoryGb = inv.Memory?.TotalInstalledBytes is { } b ? Math.Round(b / (double)(1L << 30), 1) : null,
            Disks = [.. inv.Disks.Select(d => new DiskRecord(d.Model ?? $"#{d.Index}", d.SerialNumber, d.Health?.PercentageUsed, d.Health?.TemperatureCelsius,
                d.Health?.ReallocatedSectors, d.Health?.PowerOnHours, d.Health?.Assessment?.ToString()))],
            ReportFile = reportFile,
        };
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AttendanceRecord? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AttendanceRecord>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the record beside the report ("hwsuite-...-Client.hwsuite.json").</summary>
    public void SaveBeside(string reportPath)
    {
        ArgumentNullException.ThrowIfNull(reportPath);
        var path = Path.Combine(Path.GetDirectoryName(reportPath) ?? "", Path.GetFileNameWithoutExtension(reportPath) + Extension);
        File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
    }

    /// <summary>Every record under <paramref name="folder"/> (subfolders included), newest first; unreadable files are skipped.</summary>
    public static IReadOnlyList<(AttendanceRecord Record, string Path)> Scan(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var list = new List<(AttendanceRecord, string)>();
        foreach (var file in Directory.EnumerateFiles(folder, "*" + Extension, SearchOption.AllDirectories))
        {
            try
            {
                if (Parse(File.ReadAllText(file)) is { } record)
                {
                    list.Add((record, file));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a file being written or locked is skipped
            }
        }

        return [.. list.OrderByDescending(x => x.Item1.At)];
    }
}

/// <summary>One line of a before / after comparison of two services on the same computer.</summary>
public sealed record ComparisonRow(string Item, string Before, string After, int Trend);

/// <summary>Compares two services of the same computer: what improved (+1), got worse (-1) or stayed (0).</summary>
public static class AttendanceComparison
{
    public static IReadOnlyList<ComparisonRow> Compare(AttendanceRecord before, AttendanceRecord after, Strings s)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        var rows = new List<ComparisonRow>();
        string N(double? v, string unit, string format = "0") => v is { } x ? x.ToString(format, c) + unit : "—";

        // Lower is better for temperatures, wear and bad sectors; higher for free space.
        void Lower(string item, double? a, double? b, string unit, double tolerance)
        {
            if (a is null && b is null)
            {
                return;
            }

            var trend = a is { } x && b is { } y ? (y < x - tolerance ? 1 : y > x + tolerance ? -1 : 0) : 0;
            rows.Add(new ComparisonRow(item, N(a, unit), N(b, unit), trend));
        }

        Lower(s["hist.cmp.cpuIdle"], before.CpuIdleTemperature, after.CpuIdleTemperature, " °C", 2);
        Lower(s["hist.cmp.cpuLoad"], before.CpuLoadTemperature, after.CpuLoadTemperature, " °C", 2);
        Lower(s["hist.cmp.gpuIdle"], before.GpuIdleTemperature, after.GpuIdleTemperature, " °C", 2);
        foreach (var disk in after.Disks)
        {
            var old = before.Disks.FirstOrDefault(d => d.Serial is not null ? d.Serial == disk.Serial : d.Model == disk.Model);
            if (old is null)
            {
                continue;
            }

            Lower(s.Format("hist.cmp.wear", disk.Model), old.WearPercent, disk.WearPercent, "%", 0);
            if (old.Reallocated is not null || disk.Reallocated is not null)
            {
                Lower(s.Format("hist.cmp.sectors", disk.Model), old.Reallocated, disk.Reallocated, "", 0);
            }
        }

        foreach (var volume in after.Volumes)
        {
            if (before.Volumes.FirstOrDefault(v => v.Name == volume.Name) is { } old)
            {
                var trend = volume.FreePercent > old.FreePercent + 2 ? 1 : volume.FreePercent < old.FreePercent - 2 ? -1 : 0;
                rows.Add(new ComparisonRow(s.Format("hist.cmp.free", volume.Name), N(old.FreePercent, "%"), N(volume.FreePercent, "%"), trend));
            }
        }

        rows.Add(new ComparisonRow(s["hist.cmp.problems"], $"{before.Problem} / {before.Attention}", $"{after.Problem} / {after.Attention}",
            Math.Sign((before.Problem * 10) + before.Attention - (after.Problem * 10) - after.Attention)));
        return rows;
    }
}

/// <summary>
/// The batch summary: one table of every computer serviced (latest record of each) for a client with many machines,
/// from the records the pen drive collected, with their state and what needs action.
/// </summary>
public static class BatchSummary
{
    public static string ToHtml(IReadOnlyList<AttendanceRecord> records, Strings s, CompanyProfile? company, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        static string E(string? text) => MaintenanceReportWriter.Escape(text);
        var latest = records.Where(r => r.Kind == "maintenance").GroupBy(r => r.MachineKey).Select(g => g.OrderByDescending(r => r.At).First())
            .OrderByDescending(r => r.Overall).ThenBy(r => r.Machine, StringComparer.OrdinalIgnoreCase).ToList();
        var tests = records.Where(r => r.Test is not null).GroupBy(r => r.MachineKey).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.At).First());
        var clients = latest.Select(r => r.Client).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var heading = title ?? s["batch.title"];

        var w = new StringBuilder();
        w.Append(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="{{(c.Name.StartsWith("pt", StringComparison.Ordinal) ? "pt-BR" : "en")}}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1"><title>{{E(heading)}}</title>
            <style>
            @page { size: A4 landscape; margin: 12mm; }
            body { margin: 0; padding: 18px; font: 10pt/1.4 "Segoe UI", system-ui, Arial, sans-serif; color: #18222c; }
            header { display: flex; justify-content: space-between; align-items: flex-end; border-bottom: 3px solid #0e7490; padding-bottom: 8px; margin-bottom: 12px; }
            header img { max-height: 46px; max-width: 150px; margin-right: 12px; vertical-align: middle; }
            h1 { margin: 0; font-size: 15pt; color: #0b3b4a; text-transform: uppercase; }
            .meta { color: #4a5a68; font-size: 9pt; }
            .totals { display: flex; gap: 10px; margin: 10px 0 14px; }
            .totals div { border-radius: 6px; padding: 8px 14px; color: #fff; font-weight: 700; }
            table { width: 100%; border-collapse: collapse; }
            th { text-align: left; font-size: 8pt; color: #5b6b78; text-transform: uppercase; letter-spacing: .05em; padding: 5px; border-bottom: 1px solid #c9d4dc; }
            td { padding: 6px 5px; border-bottom: 1px solid #e6ecf0; vertical-align: top; font-size: 9pt; }
            tr { page-break-inside: avoid; }
            .tag { display: inline-block; padding: 2px 8px; border-radius: 10px; font-size: 8pt; font-weight: 700; color: #fff; white-space: nowrap; }
            .v-ok { background: #0f8a5f; } .v-attention { background: #c27803; } .v-problem { background: #c0262d; } .v-info { background: #4a5a68; }
            ul { margin: 0; padding-left: 16px; } li { margin: 1px 0; }
            .dim { color: #6e7c88; }
            footer { margin-top: 16px; font-size: 8pt; color: #7b8a96; text-align: center; }
            </style></head><body>
            """);
        w.Append("<header><div>");
        if (!string.IsNullOrEmpty(company?.LogoDataUri))
        {
            w.Append(CultureInfo.InvariantCulture, $"<img src=\"{E(company.LogoDataUri)}\" alt=\"\">");
        }

        w.Append(CultureInfo.InvariantCulture, $"<b>{E(company?.Name)}</b></div><div style=\"text-align:right\"><h1>{E(heading)}</h1>");
        w.Append(CultureInfo.InvariantCulture, $"<div class=\"meta\">{E(string.Join(" · ", clients))}</div>");
        w.Append(CultureInfo.InvariantCulture, $"<div class=\"meta\">{E(DateTimeOffset.Now.LocalDateTime.ToString("f", c))}</div></div></header>");

        w.Append("<div class=\"totals\">");
        w.Append(CultureInfo.InvariantCulture, $"<div class=\"v-info\">{E(s.Format("batch.machines", latest.Count))}</div>");
        foreach (var v in new[] { Verdict.Ok, Verdict.Attention, Verdict.Problem })
        {
            w.Append(CultureInfo.InvariantCulture, $"<div class=\"{Css(v)}\">{latest.Count(r => r.Overall == v)} {E(s[$"mnt.overall.{v}"])}</div>");
        }

        w.Append("</div><table><thead><tr>");
        foreach (var head in new[] { "batch.col.machine", "batch.col.state", "batch.col.hardware", "batch.col.disks", "batch.col.test", "batch.col.findings" })
        {
            w.Append(CultureInfo.InvariantCulture, $"<th>{E(s[head])}</th>");
        }

        w.Append("</tr></thead><tbody>");
        foreach (var r in latest)
        {
            w.Append(CultureInfo.InvariantCulture, $"<tr><td><b>{E(r.Equipment ?? r.Machine)}</b><div class=\"dim\">{E(r.Machine)}{(r.Serial is null ? "" : " · " + E(r.Serial))}</div>");
            w.Append(CultureInfo.InvariantCulture, $"<div class=\"dim\">{E(r.At.LocalDateTime.ToString("g", c))}{(r.Order is null ? "" : " · " + E(s["mnt.report.order"]) + " " + E(r.Order))}</div></td>");
            w.Append(CultureInfo.InvariantCulture, $"<td><span class=\"tag {Css(r.Overall)}\">{E(s[$"mnt.verdict.{r.Overall}"])}</span><div class=\"dim\">{r.Problem} {E(s["mnt.verdict.Problem"])} · {r.Attention} {E(s["mnt.verdict.Attention"])}</div></td>");
            w.Append(CultureInfo.InvariantCulture, $"<td>{E(r.Cpu)}<div class=\"dim\">{(r.MemoryGb is { } gb ? gb.ToString("0", c) + " GB · " : "")}{E(r.Gpu)}</div><div class=\"dim\">{E(r.Os)}</div></td>");
            w.Append("<td>");
            foreach (var d in r.Disks)
            {
                w.Append(CultureInfo.InvariantCulture, $"<div>{E(d.Model)}{(d.WearPercent is { } wear ? $" · {wear}%" : "")}{(d.Temperature is { } t ? $" · {t} °C" : "")}</div>");
            }

            foreach (var v in r.Volumes)
            {
                w.Append(CultureInfo.InvariantCulture, $"<div class=\"dim\">{E(v.Name)} {E(s.Format("batch.free", v.FreePercent.ToString("0", c)))}</div>");
            }

            w.Append("</td><td>");
            if (tests.TryGetValue(r.MachineKey, out var test) && test.Test is { } t2)
            {
                w.Append(CultureInfo.InvariantCulture, $"<span class=\"tag {Css(t2.Passed ? Verdict.Ok : Verdict.Problem)}\">{E(s[t2.Passed ? "batch.test.passed" : "batch.test.failed"])}</span>");
                w.Append(CultureInfo.InvariantCulture, $"<div class=\"dim\">{E(s[$"batch.kind.{test.Kind}"])} · {t2.Minutes.ToString("0", c)} min</div>");
            }
            else
            {
                w.Append("<span class=\"dim\">—</span>");
            }

            w.Append("</td><td>");
            if (r.Findings.Count > 0)
            {
                w.Append("<ul>");
                foreach (var f in r.Findings.Take(6))
                {
                    w.Append(CultureInfo.InvariantCulture, $"<li>{E(f)}</li>");
                }

                w.Append("</ul>");
            }
            else
            {
                w.Append(CultureInfo.InvariantCulture, $"<span class=\"dim\">{E(s["batch.noFindings"])}</span>");
            }

            w.Append("</td></tr>");
        }

        w.Append(CultureInfo.InvariantCulture, $"</tbody></table><footer>{E(s.Format("mnt.report.footer", BuildInfo.Current.Display))}</footer></body></html>");
        return w.ToString();
    }

    private static string Css(Verdict v) => v switch
    {
        Verdict.Ok => "v-ok",
        Verdict.Attention => "v-attention",
        Verdict.Problem => "v-problem",
        _ => "v-info",
    };
}
