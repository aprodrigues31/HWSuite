using HWSuite.Core.Maintenance;
using Xunit;

namespace HWSuite.Tests;

public class HealthTests
{
    [Theory]
    [InlineData("Healthy\r\n", ImageHealth.Healthy)]
    [InlineData("WARNING: something\r\nRepairable\r\n", ImageHealth.Repairable)]
    [InlineData("NonRepairable", ImageHealth.NonRepairable)]
    [InlineData("Repair-WindowsImage : Access denied", ImageHealth.Unknown)]
    [InlineData("", ImageHealth.Unknown)]
    public void ReadsTheImageState(string output, ImageHealth expected) => Assert.Equal(expected, Tuneup.ParseImageHealth(output));

    [Fact]
    public void CleanSfcRun()
    {
        const string log = "2026-11-12 21:00:00, Info CSI 00000001 [SR] Verifying 100 components\n2026-11-12 21:10:00, Info CSI 00000002 [SR] Verify complete\n";
        Assert.Equal(new SfcResult(true, 0, 0), Tuneup.ParseSfc(log));
    }

    [Fact]
    public void RepairedAndBrokenFiles()
    {
        const string log = """
            2026-11-12, Info CSI 00000010 [SR] Repairing corrupted file \??\C:\WINDOWS\System32\a.dll from store
            2026-11-12, Info CSI 00000011 [SR] Repaired file \SystemRoot\WinSxS\x\a.dll by copying from backup
            2026-11-12, Info CSI 00000012 [SR] Cannot repair member file [l:10]"b.dll" of Package, version 10.0
            2026-11-12, Info CSI 00000013 [SR] Repair complete
            """;
        var r = Tuneup.ParseSfc(log);
        Assert.True(r.Completed);
        Assert.Equal(1, r.Unrepaired);
        Assert.True(r.Repaired >= 1);
    }

    [Fact]
    public void UnfinishedSfc() => Assert.False(Tuneup.ParseSfc("2026, Info CSI [SR] Verifying 100 components").Completed);
}
