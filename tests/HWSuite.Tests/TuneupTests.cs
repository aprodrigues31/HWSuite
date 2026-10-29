using HWSuite.Core.Maintenance;

namespace HWSuite.Tests;

public sealed class TuneupTests
{
    [Fact]
    public void Cleanup_never_touches_downloads_and_asks_for_the_rest()
    {
        var plain = Tuneup.CleanupHandlers(new CleanupOptions());
        Assert.Contains("Temporary Files", plain);
        Assert.Contains("Update Cleanup", plain);
        Assert.DoesNotContain("DownloadsFolder", plain);
        Assert.DoesNotContain("Recycle Bin", plain);
        Assert.DoesNotContain("Previous Installations", plain);
        var all = Tuneup.CleanupHandlers(new CleanupOptions(EmptyRecycleBin: true, RemovePreviousWindows: true));
        Assert.Contains("Recycle Bin", all);
        Assert.Contains("Previous Installations", all);
    }

    [Fact]
    public void Update_and_winget_texts_are_read()
    {
        Assert.Equal("KB5044284", Tuneup.KbOf("2026-10 Cumulative Update for Windows 11 Version 25H2 (kb5044284)"));
        Assert.Null(Tuneup.KbOf("Security Intelligence Update"));
        Assert.Equal("Succeeded", Tuneup.ResultName(2));
        const string Output = "Found Google Chrome [Google.Chrome] Version 131\nSuccessfully installed\nFound 7-Zip\nSuccessfully installed\n";
        Assert.Equal(2, Tuneup.WingetInstalled(Output));
        Assert.True(Tuneup.WingetNothingToDo("No available upgrade found."));
        Assert.False(Tuneup.WingetNothingToDo(Output));
    }

    [Fact]
    public void Cli_tuneup_options()
    {
        var o = HWSuite.Cli.CliOptions.Parse(["tuneup", "--cleanup", "--windows-update", "--empty-recycle-bin"]);
        Assert.Equal(HWSuite.Cli.CliCommand.Tuneup, o.Command);
        Assert.Equal(TuneupTasks.Cleanup | TuneupTasks.WindowsUpdate, o.Tasks);
        Assert.True(o.EmptyRecycleBin);
        Assert.Equal(TuneupTasks.None, HWSuite.Cli.CliOptions.Parse(["tuneup"]).Tasks); // none = the default set
        Assert.Throws<HWSuite.Cli.CliUsageException>(() => HWSuite.Cli.CliOptions.Parse(["report", "--cleanup"]));
    }
}
