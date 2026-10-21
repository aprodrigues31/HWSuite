using System.Reflection;
using System.Text;
using HWSuite.Cli;
using HWSuite.Core.Collection;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;

Console.OutputEncoding = Encoding.UTF8;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (CliUsageException ex)
{
    var strings = Strings.For((string?)null);
    Console.Error.WriteLine(strings.Format(ex.Key, ex.Argument));
    Console.Error.WriteLine();
    Console.Error.WriteLine(strings["cli.usage"]);
    return 2;
}

var s = Strings.For(options.Language);

if (options.ShowHelp)
{
    Console.WriteLine(s["cli.usage"]);
    return 0;
}

if (options.ShowVersion)
{
    var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    Console.WriteLine($"hwsuite {version.Split('+')[0]}");
    return 0;
}

var writer = ReportWriters.Find(options.Format);
if (writer is null)
{
    Console.Error.WriteLine(s.Format("cli.badFormat", options.Format));
    return 2;
}

var inventory = new InventoryCollector(ProviderCatalog.Default()).Collect();
if (options.Redact)
{
    InventoryRedactor.Redact(inventory);
}

if (options.OutputPath is null)
{
    writer.Write(inventory, s, Console.Out);
}
else
{
    var path = Path.GetFullPath(options.OutputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using (var file = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
    {
        writer.Write(inventory, s, file);
    }

    Console.WriteLine(s.Format("cli.written", path));
}

// Exit code 3 signals "report produced, but some provider failed" for scripts.
return inventory.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? 3 : 0;
