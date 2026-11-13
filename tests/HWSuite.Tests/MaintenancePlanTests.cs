using System.Globalization;
using HWSuite.Core.Maintenance;
using Xunit;

namespace HWSuite.Tests;

public class MaintenancePlanTests
{
    [Fact]
    public void AreasGroupTheTasks()
    {
        Assert.Equal(TuneupArea.Windows, MaintenancePlan.Area(TuneupTasks.SystemHealth));
        Assert.Equal(TuneupArea.Windows, MaintenancePlan.Area(TuneupTasks.WindowsUpdate));
        Assert.Equal(TuneupArea.Drives, MaintenancePlan.Area(TuneupTasks.Optimize));
        Assert.Equal(TuneupArea.Apps, MaintenancePlan.Area(TuneupTasks.DesktopApps));
    }

    [Fact]
    public void TimeLeftCountsTheRest()
    {
        double[] est = [600, 300, 60];
        Assert.Equal(960, MaintenancePlan.Remaining(est, 0, 0));
        Assert.Equal(660, MaintenancePlan.Remaining(est, 0, 300));
        Assert.Equal(60 + 360, MaintenancePlan.Remaining(est, 0, 5000)); // overrun: 10 % left of the current
        Assert.Equal(0, MaintenancePlan.Remaining(est, 3, 0));
        Assert.InRange(MaintenancePlan.Fraction(est, 1, 0), 0.6, 0.7);
    }

    [Fact]
    public void DurationText()
    {
        Assert.Equal("< 1 min", MaintenancePlan.Duration(20, CultureInfo.InvariantCulture));
        Assert.Equal("~ 12 min", MaintenancePlan.Duration(700, CultureInfo.InvariantCulture));
        Assert.Equal("~ 1 h 05 min", MaintenancePlan.Duration(3900, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LearnsTimes()
    {
        var text = MaintenancePlan.LearnTime(null, TuneupTasks.Cleanup, 100);
        text = MaintenancePlan.LearnTime(text, TuneupTasks.Cleanup, 200);
        Assert.Equal(160, MaintenancePlan.ParseTimes(text)[TuneupTasks.Cleanup]);
    }

    [Fact]
    public void ScheduledArguments()
    {
        var plan = new MaintenanceSchedule([DayOfWeek.Monday], new TimeOnly(3, 0), TuneupTasks.WindowsUpdate | TuneupTasks.SystemHealth | TuneupTasks.Cleanup, true);
        Assert.Equal("tuneup --health --cleanup --windows-update --empty-recycle-bin --scheduled", MaintenancePlan.Arguments(plan));
    }

    [Fact]
    public void TaskXmlIsValid()
    {
        var plan = new MaintenanceSchedule([DayOfWeek.Wednesday, DayOfWeek.Monday], new TimeOnly(3, 30), TuneupTasks.Cleanup, false);
        var xml = MaintenancePlan.TaskXml(plan, @"C:\Program Files\HWSuite\hwsuite.exe", @"PC\Alexandre & co", new DateTime(2026, 11, 13), "Maintenance");
        var doc = System.Xml.Linq.XDocument.Parse(xml.Replace("UTF-16", "UTF-8", StringComparison.Ordinal));
        var ns = doc.Root!.Name.Namespace;
        Assert.Equal("2026-11-13T03:30:00", doc.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal(["Monday", "Wednesday"], doc.Descendants(ns + "DaysOfWeek").Single().Elements().Select(e => e.Name.LocalName));
        Assert.Equal(@"PC\Alexandre & co", doc.Descendants(ns + "UserId").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
    }

    [Fact]
    public void NextRun()
    {
        var plan = new MaintenanceSchedule([DayOfWeek.Monday], new TimeOnly(3, 0), TuneupTasks.Cleanup, false);
        var friday = new DateTime(2026, 11, 13, 10, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 16, 3, 0, 0), plan.Next(friday));
    }

    [Fact]
    public void HistoryRoundTrips()
    {
        var a = new MaintenanceRun(new DateTime(2026, 11, 10, 3, 0, 0), new DateTime(2026, 11, 10, 3, 30, 0), true, true,
            [new TuneupOutcome(TuneupTasks.Cleanup, true, "Disk\tcleanup: 3 GB", ["a", "b"]), new TuneupOutcome(TuneupTasks.Optimize, false, "Optimize", [])]);
        var b = a with { Start = a.Start.AddDays(1), End = a.End.AddDays(1), Scheduled = false, RebootPending = false };
        var runs = MaintenancePlan.ParseHistory(MaintenancePlan.FormatRun(a) + MaintenancePlan.FormatRun(b));
        Assert.Equal(2, runs.Count);
        Assert.False(runs[0].Scheduled); // newest first
        Assert.True(runs[1].RebootPending);
        Assert.Equal("Disk cleanup: 3 GB", runs[1].Outcomes[0].Summary);
        Assert.Equal(["a", "b"], runs[1].Outcomes[0].Details);
        Assert.False(runs[1].Succeeded);
    }
}
