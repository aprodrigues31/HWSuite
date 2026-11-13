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
}
