using HWSuite.Core.Maintenance;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite schedule status | remove | set DAYS HH:MM [--health --cleanup --optimize --windows-update --store --apps]"
/// (DAYS: mon,tue,… or all). The same scheduled maintenance the app's Maintenance page creates.
/// </summary>
internal static class ScheduleCommand
{
    public static int Run(CliOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows only.");
            return 1;
        }

        var args = options.SnmpArgs;
        var verb = args.Count > 0 ? args[0].ToLowerInvariant() : "status";
        try
        {
            switch (verb)
            {
                case "status":
                    Console.WriteLine(WindowsMaintenanceSchedule.Exists() ? "scheduled" : "not scheduled");
                    return 0;
                case "remove":
                    WindowsMaintenanceSchedule.Delete();
                    Console.WriteLine("removed");
                    return 0;
                case "set" when args.Count >= 3 && TimeOnly.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var time):
                    var days = args[1].Equals("all", StringComparison.OrdinalIgnoreCase)
                        ? Enum.GetValues<DayOfWeek>().ToList()
                        : [.. args[1].Split(',').Select(d => Enum.GetValues<DayOfWeek>().First(x => x.ToString().StartsWith(d.Trim(), StringComparison.OrdinalIgnoreCase)))];
                    var tasks = TuneupTasks.None;
                    foreach (var flag in args.Skip(3))
                    {
                        tasks |= flag switch
                        {
                            "--health" => TuneupTasks.SystemHealth,
                            "--cleanup" => TuneupTasks.Cleanup,
                            "--optimize" => TuneupTasks.Optimize,
                            "--windows-update" => TuneupTasks.WindowsUpdate,
                            "--store" => TuneupTasks.StoreApps,
                            "--apps" => TuneupTasks.DesktopApps,
                            _ => TuneupTasks.None,
                        };
                    }

                    var plan = new MaintenanceSchedule(days, time, tasks == TuneupTasks.None ? TuneupTasks.Cleanup : tasks, false);
                    WindowsMaintenanceSchedule.Save(plan, "HWSuite maintenance");
                    Console.WriteLine($"scheduled; next run {plan.Next(DateTime.Now):g}");
                    return 0;
                default:
                    Console.Error.WriteLine("hwsuite schedule status | remove | set DAYS HH:MM [--health --cleanup --optimize --windows-update --store --apps]");
                    return 2;
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
