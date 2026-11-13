using HWSuite.Core.Maintenance;
using Xunit;

namespace HWSuite.Tests;

public class StartupTests
{
    [Fact]
    public void NoApprovedValueMeansEnabled() => Assert.True(StartupRules.IsEnabled(null));

    [Theory]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    public void FirstByteDecides(byte first, bool enabled) =>
        Assert.Equal(enabled, StartupRules.IsEnabled([first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

    [Fact]
    public void ApprovedRoundTrips()
    {
        var now = new DateTime(2026, 11, 12, 21, 0, 0, DateTimeKind.Utc);
        var off = StartupRules.Approved(false, now);
        Assert.Equal(12, off.Length);
        Assert.Equal(3, off[0]);
        Assert.Equal(now.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.False(StartupRules.IsEnabled(off));
        var on = StartupRules.Approved(true, now);
        Assert.Equal(2, on[0]);
        Assert.True(StartupRules.IsEnabled(on));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" /background", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Program Files (x86)\\Steam\\steam.exe -silent", "C:\\Program Files (x86)\\Steam\\steam.exe")]
    [InlineData("C:\\Windows\\system32\\SecurityHealthSystray.exe", "C:\\Windows\\system32\\SecurityHealthSystray.exe")]
    [InlineData("rundll32.exe C:\\x.dll,Start", "rundll32.exe")]
    [InlineData("tool /x", "tool")]
    public void FindsTheProgram(string command, string expected) => Assert.Equal(expected, StartupRules.Executable(command));

    [Fact]
    public void EmptyCommandHasNoProgram() => Assert.Null(StartupRules.Executable("  "));

    [Fact]
    public void ReadsALogonTask()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Author>Vendor</Author><Description>Updater</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
              <Settings><Enabled>false</Enabled></Settings>
              <Actions Context="Author"><Exec><Command>"C:\App\up.exe"</Command><Arguments>/silent</Arguments></Exec></Actions>
            </Task>
            """;
        var t = StartupRules.ParseTask(xml.Replace("UTF-16", "UTF-8", StringComparison.Ordinal).Trim());
        Assert.True(t.AtStartup);
        Assert.False(t.Enabled);
        Assert.Equal("\"C:\\App\\up.exe\"", t.Command);
        Assert.Equal("/silent", t.Arguments);
        Assert.Equal("Vendor", t.Author);
    }

    [Fact]
    public void ADailyTaskIsNotAStartupTask()
    {
        const string xml = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers><CalendarTrigger /></Triggers><Actions><Exec><Command>a.exe</Command></Exec></Actions></Task>";
        Assert.False(StartupRules.ParseTask(xml).AtStartup);
    }

    [Fact]
    public void ReadsTheLastBoot()
    {
        const string ev = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System><EventID>101</EventID><TimeCreated SystemTime="2026-11-12T10:00:30.0Z"/></System><EventData><Data Name="FileName">C:\Users\a\Discord.exe</Data><Data Name="DegradationTime">3400</Data></EventData></Event>
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event"><System><EventID>100</EventID><TimeCreated SystemTime="2026-11-12T10:00:40.0Z"/></System><EventData><Data Name="BootTime">41200</Data><Data Name="MainPathBootTime">19800</Data><Data Name="BootPostBootTime">21400</Data></EventData></Event>
            """;
        var boot = StartupRules.ParseBoot(ev);
        Assert.NotNull(boot);
        Assert.Equal(41200, boot!.TotalMs);
        Assert.Equal(19800, boot.MainPathMs);
        Assert.Equal(21400, boot.PostBootMs);
        Assert.Equal(3400, boot.SlowApps["Discord.exe"]);
    }

    [Theory]
    [InlineData(200, 100_000, StartupImpact.Low)]
    [InlineData(400, 100_000, StartupImpact.Medium)]
    [InlineData(100, 1_000_000, StartupImpact.Medium)]
    [InlineData(1500, 0, StartupImpact.High)]
    [InlineData(10, 5_000_000, StartupImpact.High)]
    public void TaskManagerThresholds(double cpu, long disk, StartupImpact expected) =>
        Assert.Equal(expected, new ImpactSample("x", cpu, disk, 10, 5).Impact);

    [Fact]
    public void SamplesRoundTrip()
    {
        var boot = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        var text = StartupRules.FormatSamples(boot, [new ImpactSample(@"C:\a b\x.exe", 640, 1900000, 92.5, 14)]);
        var back = StartupRules.ParseSamples(text);
        Assert.NotNull(back);
        Assert.Equal(boot, back!.Value.BootUtc);
        var s = Assert.Single(back.Value.Samples);
        Assert.Equal(@"C:\a b\x.exe", s.Executable);
        Assert.Equal(1900000, s.DiskReadBytes);
    }
}
