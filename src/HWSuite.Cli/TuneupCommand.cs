using HWSuite.Core.Localization;
using HWSuite.Core.Maintenance;
using HWSuite.Providers.Windows;

namespace HWSuite.Cli;

/// <summary>"hwsuite tuneup": disk cleanup, drive optimization, Windows Update and app updates, as administrator.</summary>
internal static class TuneupCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(s["sensors.unsupported"]);
            return 2;
        }

        if (!AccessStatus.Detect().IsElevated)
        {
            Console.Error.WriteLine(s["tune.needsAdmin"]);
            return 2;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        var tasks = options.Tasks == TuneupTasks.None ? TuneupTasks.Default : options.Tasks;
        var cleanup = new CleanupOptions(options.EmptyRecycleBin);
        void Log(string line) => Console.WriteLine("  " + line);
        var failed = false;
        var reboot = false;
        foreach (var task in Enum.GetValues<TuneupTasks>().Where(t => t is not (TuneupTasks.None or TuneupTasks.Default) && tasks.HasFlag(t)))
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            var outcome = task switch
            {
                TuneupTasks.Cleanup => WindowsTuneup.Cleanup(cleanup, s, Log, cancel.Token),
                TuneupTasks.Optimize => WindowsTuneup.Optimize(s, Log, cancel.Token),
                TuneupTasks.WindowsUpdate => WindowsTuneup.WindowsUpdate(s, Log, cancel.Token),
                TuneupTasks.StoreApps => WindowsTuneup.StoreApps(s, Log, cancel.Token),
                _ => WindowsTuneup.DesktopApps(s, Log, cancel.Token),
            };
            Console.WriteLine((outcome.Succeeded ? "[OK] " : "[!]  ") + outcome.Summary);
            foreach (var detail in outcome.Details)
            {
                Console.WriteLine("       " + detail);
            }

            failed |= !outcome.Succeeded;
            reboot |= outcome.RebootRequired;
        }

        if (reboot)
        {
            Console.WriteLine(s["tune.rebootNow"]);
        }

        // 3 = some task did not complete, as for the other commands.
        return failed ? 3 : 0;
    }
}
