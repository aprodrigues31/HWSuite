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
        var cleanup = new CleanupOptions(options.EmptyRecycleBin, Drives: options.CleanupDrives);
        void Log(string line) => Console.WriteLine("  " + line);
        var failed = false;
        var reboot = false;
        var started = DateTime.Now;
        var outcomes = new List<TuneupOutcome>();
        foreach (var task in MaintenancePlan.Order.Where(t => tasks.HasFlag(t)))
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var outcome = task switch
            {
                TuneupTasks.Cleanup => WindowsTuneup.Cleanup(cleanup, s, Log, cancel.Token),
                TuneupTasks.Optimize => WindowsTuneup.Optimize(options.OptimizeDrives, s, Log, cancel.Token),
                TuneupTasks.WindowsUpdate => WindowsTuneup.WindowsUpdate(s, Log, cancel.Token),
                TuneupTasks.StoreApps => WindowsTuneup.StoreApps(s, Log, cancel.Token),
                TuneupTasks.SystemHealth => WindowsTuneup.SystemHealth(s, Log, cancel.Token),
                _ => WindowsTuneup.DesktopApps(s, Log, cancel.Token),
            };
            outcomes.Add(outcome);
            if (!cancel.IsCancellationRequested)
            {
                WindowsMaintenanceSchedule.LearnTime(task, clock.Elapsed.TotalSeconds);
            }

            Console.WriteLine((outcome.Succeeded ? "[OK] " : "[!]  ") + outcome.Summary);
            foreach (var detail in outcome.Details)
            {
                Console.WriteLine("       " + detail);
            }

            failed |= !outcome.Succeeded;
            reboot |= outcome.RebootRequired;
        }

        var restartPending = reboot || WindowsTuneup.RestartPending();
        WindowsMaintenanceSchedule.Record(new MaintenanceRun(started, DateTime.Now, options.Scheduled, restartPending, outcomes));
        if (restartPending && options.Scheduled)
        {
            // A scheduled run never restarts the computer and never waits for an answer.
            Console.WriteLine(s["tune.restart.pending"]);
        }
        else if (restartPending)
        {
            // Restarting is the technician's call: asked here, never done on its own.
            Console.WriteLine(s["tune.rebootNow"]);
            if (!Console.IsInputRedirected)
            {
                Console.Write(s["tune.restart.ask"] + " ");
                var answer = Console.ReadLine()?.Trim();
                if (answer is { Length: > 0 } && (answer[0] is 's' or 'S' or 'y' or 'Y'))
                {
                    Console.WriteLine(WindowsTuneup.Restart(TimeSpan.FromSeconds(15), s["tune.restart.comment"]) ? s["tune.restart.starting"] : s["tune.restart.failed"]);
                }
                else
                {
                    Console.WriteLine(s["tune.restart.pending"]);
                }
            }
        }

        // 3 = some task did not complete, as for the other commands.
        return failed ? 3 : 0;
    }
}
