using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;

namespace HWSuite.Core.Reporting;

public interface IReportWriter
{
    /// <summary>Format id used on the command line: "txt", "html", "json".</summary>
    string Format { get; }

    string FileExtension { get; }

    void Write(HardwareInventory inventory, Strings strings, TextWriter output);
}

public static class ReportWriters
{
    public static IReadOnlyList<IReportWriter> All { get; } =
        [new TextReportWriter(), new HtmlReportWriter(), new JsonReportWriter()];

    public static IReportWriter? Find(string format) =>
        All.FirstOrDefault(w => w.Format.Equals(format, StringComparison.OrdinalIgnoreCase));
}

public sealed class TextReportWriter : IReportWriter
{
    public string Format => "txt";

    public string FileExtension => ".txt";

    public void Write(HardwareInventory inventory, Strings strings, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(output);

        var sections = ReportModel.Build(inventory, strings);
        var width = sections.SelectMany(s => s.Groups).SelectMany(g => g.Rows)
            .Select(r => r.Label.Length).DefaultIfEmpty(10).Max() + 2;

        output.WriteLine(strings["report.title"]);
        if (inventory.MachineName is not null)
        {
            output.WriteLine($"{strings["report.machine"]}: {inventory.MachineName}");
        }

        output.WriteLine($"{strings["report.generated"]}: {inventory.CollectedAt.ToString("g", strings.Culture)}");
        if (inventory.ToolVersion is not null)
        {
            output.WriteLine($"{strings["report.tool"]}: HWSuite {inventory.ToolVersion}");
        }


        foreach (var section in sections)
        {
            output.WriteLine();
            output.WriteLine(section.Title.ToUpper(strings.Culture));
            output.WriteLine(new string('-', section.Title.Length));
            foreach (var group in section.Groups)
            {
                var indent = "";
                if (group.Title is not null)
                {
                    output.WriteLine($"  [{group.Title}]");
                    indent = "  ";
                }

                foreach (var row in group.Rows)
                {
                    output.WriteLine($"  {indent}{(row.Label + ":").PadRight(width)} {row.Value}");
                }
            }
        }
    }
}

public sealed class HtmlReportWriter : IReportWriter
{
    private const string Style = """
        :root { --bg:#ffffff; --fg:#1d1d1f; --muted:#6e6e73; --line:#e5e5ea; --accent:#0a66c2; --panel:#f6f8fa; }
        @media (prefers-color-scheme: dark) { :root { --bg:#161618; --fg:#f2f2f7; --muted:#a1a1a6; --line:#2c2c2e; --accent:#4c9aff; --panel:#1f1f22; } }
        * { box-sizing: border-box; }
        body { margin:0; padding:24px 16px; background:var(--bg); color:var(--fg); font:14px/1.5 "Segoe UI", system-ui, sans-serif; }
        main { max-width: 920px; margin: 0 auto; }
        h1 { font-size: 22px; margin: 0 0 4px; }
        .meta { color: var(--muted); margin-bottom: 24px; }
        section { border: 1px solid var(--line); border-radius: 8px; margin: 0 0 16px; overflow: hidden; }
        h2 { font-size: 15px; margin: 0; padding: 10px 14px; background: var(--panel); border-bottom: 1px solid var(--line); color: var(--accent); }
        h3 { font-size: 13px; margin: 12px 14px 4px; color: var(--muted); font-weight: 600; }
        table { width: 100%; border-collapse: collapse; }
        th, td { text-align: left; vertical-align: top; padding: 6px 14px; border-bottom: 1px solid var(--line); }
        tr:last-child th, tr:last-child td { border-bottom: none; }
        th { width: 36%; font-weight: 500; color: var(--muted); }
        td { word-break: break-word; }
        footer { color: var(--muted); font-size: 12px; margin-top: 24px; }
        """;

    public string Format => "html";

    public string FileExtension => ".html";

    public void Write(HardwareInventory inventory, Strings strings, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(output);

        static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

        var title = strings["report.title"];
        output.WriteLine("<!doctype html>");
        output.WriteLine($"<html lang=\"{E(strings.Culture.Name)}\"><head><meta charset=\"utf-8\">");
        output.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        output.WriteLine($"<title>{E(title)}{(inventory.MachineName is null ? "" : " — " + E(inventory.MachineName))}</title><style>{Style}</style></head><body><main>");
        output.WriteLine($"<h1>{E(title)}</h1>");
        var machine = inventory.MachineName is null ? "" : $"{E(strings["report.machine"])}: {E(inventory.MachineName)} · ";
        output.WriteLine($"<div class=\"meta\">{machine}{E(strings["report.generated"])}: {E(inventory.CollectedAt.ToString("g", strings.Culture))}</div>");

        foreach (var section in ReportModel.Build(inventory, strings))
        {
            output.WriteLine($"<section><h2>{E(section.Title)}</h2>");
            foreach (var group in section.Groups)
            {
                if (group.Title is not null)
                {
                    output.WriteLine($"<h3>{E(group.Title)}</h3>");
                }

                output.WriteLine("<table>");
                foreach (var row in group.Rows)
                {
                    output.WriteLine($"<tr><th>{E(row.Label)}</th><td>{E(row.Value)}</td></tr>");
                }

                output.WriteLine("</table>");
            }

            output.WriteLine("</section>");
        }

        output.WriteLine($"<footer>HWSuite {E(inventory.ToolVersion)} · schema {E(inventory.SchemaVersion)}</footer>");
        output.WriteLine("</main></body></html>");
    }
}

public sealed class JsonReportWriter : IReportWriter
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Format => "json";

    public string FileExtension => ".json";

    /// <summary>JSON keys and values are language-neutral; <paramref name="strings"/> is unused.</summary>
    public void Write(HardwareInventory inventory, Strings strings, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.WriteLine(JsonSerializer.Serialize(inventory, Options));
    }
}
