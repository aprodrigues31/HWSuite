using System.Text;
using HWSuite.Core.Collection;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;

namespace HWSuite.Cli;

internal static class ReportCommand
{
    public static int Run(CliOptions options, Strings s)
    {
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
    }
}
