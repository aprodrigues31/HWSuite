using HWSuite.Cli;
using HWSuite.Providers.Windows;

namespace HWSuite.Tests;

public class DriverTests
{
    [Fact]
    public void Cli_parses_driver_actions()
    {
        Assert.Equal(DriverAction.Status, CliOptions.Parse(["driver"]).DriverAction);
        Assert.Equal(DriverAction.Status, CliOptions.Parse(["driver", "status"]).DriverAction);

        var install = CliOptions.Parse(["driver", "install", "--yes"]);
        Assert.Equal((CliCommand.Driver, DriverAction.Install, true), (install.Command, install.DriverAction, install.AssumeYes));

        var child = CliOptions.Parse(["driver", "uninstall", "-y", "--result", @"C:\Temp\r.txt", "--lang", "pt-BR"]);
        Assert.Equal((DriverAction.Uninstall, @"C:\Temp\r.txt", "pt-BR"), (child.DriverAction, child.ResultPath, child.Language));

        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["driver", "reinstall"]));
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["report", "--yes"]));
        Assert.True(CliOptions.Parse(["sensors", "--yes"]).AssumeYes);
    }

    [Theory]
    [InlineData("s", true)]
    [InlineData(" Sim ", true)]
    [InlineData("y", true)]
    [InlineData("YES", true)]
    [InlineData("", false)]
    [InlineData("n", false)]
    [InlineData(null, false)]
    public void Only_explicit_yes_confirms(string? answer, bool expected) =>
        Assert.Equal(expected, DriverCommand.IsYes(answer));

    [Theory]
    [InlineData("CN=namazso, O=namazso", true)]
    [InlineData("CN=PawnIO Project", true)]
    [InlineData("CN=Some Other Vendor", false)]
    [InlineData(null, false)]
    public void Installer_signature_must_come_from_the_pawnio_author(string? subject, bool trusted) =>
        Assert.Equal(trusted, PawnIoInstaller.IsTrustedPublisher(subject));

    [Theory]
    [InlineData(DriverOutcome.Installed, true)]
    [InlineData(DriverOutcome.AlreadyInstalled, true)]
    [InlineData(DriverOutcome.NotElevated, false)]
    [InlineData(DriverOutcome.SignatureRejected, false)]
    public void Driver_results_report_success(DriverOutcome outcome, bool succeeded) =>
        Assert.Equal(succeeded, new DriverResult(outcome).Succeeded);

    [Theory]
    [InlineData(@"\??\C:\Program Files\PawnIO\PawnIO.sys", @"C:\Program Files\PawnIO\PawnIO.sys")]
    [InlineData(@"\SystemRoot\System32\drivers\PawnIO.sys", @"C:\Windows\System32\drivers\PawnIO.sys")]
    [InlineData(@"System32\drivers\PawnIO.sys", @"C:\Windows\System32\drivers\PawnIO.sys")]
    [InlineData("", null)]
    public void Resolves_service_image_paths(string imagePath, string? expected)
    {
        var resolved = AccessStatus.ResolveDriverPath(imagePath, @"C:\Windows");
        Assert.Equal(expected?.Replace('\\', Path.DirectorySeparatorChar), resolved?.Replace('\\', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Self_invocation_reuses_the_running_program()
    {
        var (fileName, arguments) = Elevation.SelfInvocation("driver install --yes");

        Assert.False(string.IsNullOrEmpty(fileName));
        Assert.EndsWith("driver install --yes", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_comes_from_the_official_sources()
    {
        Assert.Equal("namazso.PawnIO", PawnIoInstaller.WingetId);
        Assert.StartsWith("https://github.com/namazso/PawnIO.Setup/", PawnIoInstaller.InstallerUrl, StringComparison.Ordinal);
    }
}
