using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite startup [list]" lists the programs that start with Windows; "hwsuite startup enable|disable NAME"
/// switches one on or off (the Task Manager way: nothing is deleted). NAME matches the entry name or description.
/// </summary>
internal static class StartupCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows only.");
            return 1;
        }

        var args = options.SnmpArgs;
        var verb = args.Count > 0 ? args[0].ToLowerInvariant() : "list";
        var entries = WindowsStartup.List();
        if (verb == "list")
        {
            foreach (var e in entries)
            {
                var state = e.Enabled ? s["startup.on"] : s["startup.off"];
                Console.WriteLine($"{state,-13} {e.DisplayName}  [{s[$"startup.source.{e.Source}"]}]{(e.FileMissing ? "  ! " + s["startup.missing"] : "")}");
                Console.WriteLine($"              {e.Command}");
            }

            Console.WriteLine();
            Console.WriteLine(s.Format("startup.count", entries.Count(e => e.Enabled), entries.Count));
            return 0;
        }

        if (verb is not ("enable" or "disable") || args.Count < 2)
        {
            Console.Error.WriteLine("hwsuite startup [list] | enable NAME | disable NAME");
            return 2;
        }

        var name = string.Join(' ', args.Skip(1));
        var match = entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || e.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e => e.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || e.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Console.Error.WriteLine(s.Format("startup.cli.notFound", name));
            return 1;
        }

        var enable = verb == "enable";
        try
        {
            WindowsStartup.SetEnabled(match, enable);
            Console.WriteLine(s.Format("startup.cli.changed", match.DisplayName, enable ? s["startup.on"] : s["startup.off"]));
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine(s.Format("startup.failed", match.DisplayName, ex.Message));
            Console.Error.WriteLine(s["startup.needsAdmin"]);
            return 1;
        }
    }
}
