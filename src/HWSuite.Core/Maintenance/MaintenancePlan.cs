using System.Globalization;
using System.Security;
using System.Text;

namespace HWSuite.Core.Maintenance;

/// <summary>What a maintenance task works on (the filter chips of the Maintenance page).</summary>
public enum TuneupArea
{
    Windows,
    Drives,
    Apps,
}

/// <summary>One maintenance run (manual or scheduled) and what each task did.</summary>
public sealed record MaintenanceRun(DateTime Start, DateTime End, bool Scheduled, bool RebootPending, IReadOnlyList<TuneupOutcome> Outcomes)
{
    public bool Succeeded => Outcomes.All(o => o.Succeeded);
}

/// <summary>When the maintenance runs by itself, and which tasks.</summary>
/// <param name="Days">Days of the week it runs on.</param>
/// <param name="Time">Local time of day.</param>
public sealed record MaintenanceSchedule(IReadOnlyList<DayOfWeek> Days, TimeOnly Time, TuneupTasks Tasks, bool EmptyRecycleBin)
{
    /// <summary>The next time it runs after <paramref name="now"/> (local), or null with no day chosen.</summary>
    public DateTime? Next(DateTime now)
    {
        if (Days.Count == 0)
        {
            return null;
        }

        for (var i = 0; i <= 7; i++)
        {
            var day = now.Date.AddDays(i);
            var at = day + Time.ToTimeSpan();
            if (Days.Contains(day.DayOfWeek) && at > now)
            {
                return at;
            }
        }

        return null;
    }
}

/// <summary>
/// Planning the maintenance: which area a task belongs to, how long it usually takes (for the time-left estimate,
/// refined by the times measured on this computer), the scheduled task for Windows' Task Scheduler and the history
/// of runs.
/// </summary>
public static class MaintenancePlan
{
    /// <summary>The order tasks run in: Windows health first (it repairs what the rest relies on).</summary>
    public static readonly TuneupTasks[] Order =
        [TuneupTasks.SystemHealth, TuneupTasks.Cleanup, TuneupTasks.Optimize, TuneupTasks.WindowsUpdate, TuneupTasks.StoreApps, TuneupTasks.DesktopApps];

    public static TuneupArea Area(TuneupTasks task) => task switch
    {
        TuneupTasks.Cleanup or TuneupTasks.Optimize => TuneupArea.Drives,
        TuneupTasks.StoreApps or TuneupTasks.DesktopApps => TuneupArea.Apps,
        _ => TuneupArea.Windows,
    };

    /// <summary>Typical duration in seconds before this computer has measured its own.</summary>
    public static double DefaultSeconds(TuneupTasks task, int ssdDrives = 1, int hddDrives = 0) => task switch
    {
        TuneupTasks.SystemHealth => 25 * 60,
        TuneupTasks.Cleanup => 4 * 60,
        TuneupTasks.Optimize => (Math.Max(0, ssdDrives) * 60) + (Math.Max(0, hddDrives) * 20 * 60) + 30,
        TuneupTasks.WindowsUpdate => 15 * 60,
        TuneupTasks.StoreApps => 4 * 60,
        TuneupTasks.DesktopApps => 6 * 60,
        _ => 60,
    };

    /// <summary>
    /// Seconds left: what remains of the current task (its estimate minus the time it has run, never below 10 % of
    /// it, since a task that runs long is not almost done) plus the estimates of the tasks after it.
    /// </summary>
    public static double Remaining(IReadOnlyList<double> estimates, int current, double elapsedInCurrent)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        if (current >= estimates.Count)
        {
            return 0;
        }

        var now = Math.Max(estimates[current] - elapsedInCurrent, estimates[current] * 0.1);
        return now + estimates.Skip(current + 1).Sum();
    }

    /// <summary>Fraction done (0..1) with the same estimates.</summary>
    public static double Fraction(IReadOnlyList<double> estimates, int current, double elapsedInCurrent)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        var total = estimates.Sum();
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp(1 - (Remaining(estimates, current, elapsedInCurrent) / total), 0, 0.99);
    }

    /// <summary>"~ 12 min", "~ 1 h 05 min", "< 1 min".</summary>
    public static string Duration(double seconds, CultureInfo culture)
    {
        if (seconds < 60)
        {
            return "< 1 min";
        }

        var minutes = (int)Math.Ceiling(seconds / 60);

        return minutes < 60 ? $"~ {minutes} min" : string.Create(culture, $"~ {minutes / 60} h {minutes % 60:00} min");
    }

    // ---- Learned durations: "Task<TAB>seconds" per line --------------------------------------------------------

    public static Dictionary<TuneupTasks, double> ParseTimes(string? text)
    {
        var times = new Dictionary<TuneupTasks, double>();
        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split('\t');
            if (f.Length == 2 && Enum.TryParse<TuneupTasks>(f[0], out var task)
                && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0)
            {
                times[task] = s;
            }
        }

        return times;
    }

    /// <summary>Blends a new measurement in (60 % new), so one odd run does not throw the estimate off.</summary>
    public static string LearnTime(string? text, TuneupTasks task, double seconds)
    {
        var times = ParseTimes(text);
        times[task] = times.TryGetValue(task, out var old) ? (old * 0.4) + (seconds * 0.6) : seconds;
        return string.Join('\n', times.Select(t => $"{t.Key}\t{t.Value.ToString("0", CultureInfo.InvariantCulture)}"));
    }

    // ---- Scheduled task ----------------------------------------------------------------------------------------

    /// <summary>The app's own argument for a scheduled run without a window (portable copy without hwsuite.exe).</summary>
    public const string AppArgument = "--run-maintenance";

    /// <summary>The command-line arguments of a scheduled run.</summary>
    public static string Arguments(MaintenanceSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var args = new List<string> { "tuneup" };
        foreach (var task in Order.Where(t => schedule.Tasks.HasFlag(t)))
        {
            args.Add(task switch
            {
                TuneupTasks.SystemHealth => "--health",
                TuneupTasks.Cleanup => "--cleanup",
                TuneupTasks.Optimize => "--optimize",
                TuneupTasks.WindowsUpdate => "--windows-update",
                TuneupTasks.StoreApps => "--store",
                _ => "--apps",
            });
        }

        if (schedule.EmptyRecycleBin)
        {
            args.Add("--empty-recycle-bin");
        }

        args.Add("--scheduled");
        return string.Join(' ', args);
    }

    /// <summary>
    /// Task Scheduler XML: weekly on the chosen days at the chosen time, as this user with the highest rights while
    /// signed in (no password is stored), started late if the PC was off, wakes the PC, never on battery limits.
    /// </summary>
    public static string TaskXml(MaintenanceSchedule schedule, string exe, string user, DateTime today, string description, string? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        static string X(string v) => SecurityElement.Escape(v) ?? "";
        var start = today.Date + schedule.Time.ToTimeSpan();
        var days = string.Concat(schedule.Days.Distinct().OrderBy(d => d).Select(d => $"<{d} />"));
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n");
        xml.Append("<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n");
        xml.Append(CultureInfo.InvariantCulture, $"  <RegistrationInfo><Author>HWSuite</Author><Description>{X(description)}</Description></RegistrationInfo>\n");
        xml.Append("  <Triggers><CalendarTrigger>");
        xml.Append(CultureInfo.InvariantCulture, $"<StartBoundary>{start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}</StartBoundary><Enabled>true</Enabled>");
        xml.Append(CultureInfo.InvariantCulture, $"<ScheduleByWeek><DaysOfWeek>{days}</DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>");
        xml.Append("</CalendarTrigger></Triggers>\n");
        xml.Append(CultureInfo.InvariantCulture, $"  <Principals><Principal id=\"Author\"><UserId>{X(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n");
        xml.Append("  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        xml.Append("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><WakeToRun>true</WakeToRun>");
        xml.Append("<ExecutionTimeLimit>PT6H</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n");
        xml.Append(CultureInfo.InvariantCulture, $"  <Actions Context=\"Author\"><Exec><Command>\"{X(exe)}\"</Command><Arguments>{X(arguments ?? Arguments(schedule))}</Arguments>");
        xml.Append(CultureInfo.InvariantCulture, $"<WorkingDirectory>{X(Path.GetDirectoryName(exe) ?? "")}</WorkingDirectory></Exec></Actions>\n");
        xml.Append("</Task>\n");
        return xml.ToString();
    }

    // ---- History: one block per run ----------------------------------------------------------------------------
    // RUN <tab> start <tab> end <tab> scheduled <tab> reboot
    // TASK <tab> task <tab> ok <tab> summary <tab> details joined by " | "

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static string FormatRun(MaintenanceRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string> { $"RUN\t{run.Start.ToString("o", c)}\t{run.End.ToString("o", c)}\t{(run.Scheduled ? 1 : 0)}\t{(run.RebootPending ? 1 : 0)}" };
        lines.AddRange(run.Outcomes.Select(o => $"TASK\t{o.Task}\t{(o.Succeeded ? 1 : 0)}\t{Clean(o.Summary)}\t{Clean(string.Join(" | ", o.Details))}"));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Runs from the history file, newest first; damaged lines are skipped.</summary>
    public static IReadOnlyList<MaintenanceRun> ParseHistory(string? text)
    {
        var runs = new List<MaintenanceRun>();
        MaintenanceRun? current = null;
        var outcomes = new List<TuneupOutcome>();
        void Close()
        {
            if (current is not null)
            {
                runs.Add(current with { Outcomes = [.. outcomes] });
            }

            outcomes.Clear();
        }

        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\t');
            if (f[0] == "RUN" && f.Length >= 5
                && DateTime.TryParse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
                && DateTime.TryParse(f[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end))
            {
                Close();
                current = new MaintenanceRun(start, end, f[3] == "1", f[4] == "1", []);
            }
            else if (f[0] == "TASK" && f.Length >= 5 && current is not null && Enum.TryParse<TuneupTasks>(f[1], out var task))
            {
                var details = f[4].Length == 0 ? [] : f[4].Split(" | ");
                outcomes.Add(new TuneupOutcome(task, f[2] == "1", f[3], details));
            }
        }

        Close();
        runs.Reverse();
        return runs;
    }
}
