using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace HWSuite.Providers.Windows;

public enum DriverStep
{
    UsingWinget,
    WingetFailed,
    Downloading,
    VerifyingSignature,
    RunningInstaller,
    Uninstalling,
}

public enum DriverOutcome
{
    Installed,
    AlreadyInstalled,
    Uninstalled,
    NotInstalled,
    NotElevated,
    DownloadFailed,
    SignatureRejected,
    InstallerFailed,
}

public sealed record DriverResult(DriverOutcome Outcome, string? Detail = null)
{
    public bool Succeeded => Outcome is DriverOutcome.Installed or DriverOutcome.AlreadyInstalled or DriverOutcome.Uninstalled or DriverOutcome.NotInstalled;
}

public sealed record SignatureCheck(bool Valid, string? Subject);

/// <summary>
/// Installs and removes the PawnIO kernel driver using its official, signed installer. HWSuite
/// does not redistribute the driver: winget fetches the current release, and when winget is
/// unavailable the installer is downloaded from the author's GitHub releases and its Authenticode
/// signature is verified before it runs. Every operation requires an elevated process.
/// </summary>
public static partial class PawnIoInstaller
{
    public const string WingetId = "namazso.PawnIO";
    public const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    public const string Homepage = "https://pawnio.eu/";

    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(10);

    public static DriverResult Install(Action<DriverStep, string?> progress, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!OperatingSystem.IsWindows())
        {
            return new DriverResult(DriverOutcome.InstallerFailed, "Windows only");
        }

        var access = AccessStatus.Detect();
        if (access.PawnIoInstalled)
        {
            return new DriverResult(DriverOutcome.AlreadyInstalled);
        }

        if (!access.IsElevated)
        {
            return new DriverResult(DriverOutcome.NotElevated);
        }

        if (FindWinget() is { } winget)
        {
            progress(DriverStep.UsingWinget, winget);
            var code = Run(winget, $"install --id {WingetId} --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
            if (AccessStatus.Detect().PawnIoInstalled)
            {
                return new DriverResult(DriverOutcome.Installed, "winget");
            }

            progress(DriverStep.WingetFailed, FormatExitCode(code));
        }

        return InstallFromGitHub(progress, http);
    }

    public static DriverResult Uninstall(Action<DriverStep, string?> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!OperatingSystem.IsWindows())
        {
            return new DriverResult(DriverOutcome.InstallerFailed, "Windows only");
        }

        var access = AccessStatus.Detect();
        if (!access.PawnIoInstalled)
        {
            return new DriverResult(DriverOutcome.NotInstalled);
        }

        if (!access.IsElevated)
        {
            return new DriverResult(DriverOutcome.NotElevated);
        }

        if (FindWinget() is not { } winget)
        {
            return new DriverResult(DriverOutcome.InstallerFailed, "winget not found; remove PawnIO from Settings > Apps > Installed apps");
        }

        progress(DriverStep.Uninstalling, winget);
        var code = Run(winget, $"uninstall --id {WingetId} --exact --source winget --silent --disable-interactivity");
        if (code == 0 || !AccessStatus.Detect().PawnIoInstalled)
        {
            return new DriverResult(DriverOutcome.Uninstalled);
        }

        return new DriverResult(DriverOutcome.InstallerFailed, $"winget {FormatExitCode(code)}");
    }

    /// <summary>
    /// Only installers signed by the PawnIO author are run. The certificate subject is matched
    /// on the author's name; a valid signature from anyone else is rejected.
    /// </summary>
    internal static bool IsTrustedPublisher(string? subject) =>
        subject is not null
        && (subject.Contains("namazso", StringComparison.OrdinalIgnoreCase)
            || subject.Contains("PawnIO", StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows")]
    private static DriverResult InstallFromGitHub(Action<DriverStep, string?> progress, HttpClient? http)
    {
        var folder = Path.Combine(Path.GetTempPath(), "hwsuite-pawnio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var installer = Path.Combine(folder, "PawnIO_setup.exe");

        try
        {
            progress(DriverStep.Downloading, InstallerUrl);
            try
            {
                var client = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                try
                {
                    using var response = client.GetAsync(new Uri(InstallerUrl), HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    using var file = File.Create(installer);
                    response.Content.CopyToAsync(file).GetAwaiter().GetResult();
                }
                finally
                {
                    if (http is null)
                    {
                        client.Dispose();
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                return new DriverResult(DriverOutcome.DownloadFailed, ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                return new DriverResult(DriverOutcome.DownloadFailed, ex.Message);
            }

            var signature = VerifySignature(installer);
            progress(DriverStep.VerifyingSignature, signature.Subject);
            if (!signature.Valid || !IsTrustedPublisher(signature.Subject))
            {
                return new DriverResult(DriverOutcome.SignatureRejected, signature.Subject ?? "unsigned");
            }

            progress(DriverStep.RunningInstaller, null);
            var code = Run(installer, "-install -silent");
            return AccessStatus.Detect().PawnIoInstalled
                ? new DriverResult(DriverOutcome.Installed, "PawnIO_setup.exe")
                : new DriverResult(DriverOutcome.InstallerFailed, $"PawnIO_setup.exe {FormatExitCode(code)}");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A locked temp file is harmless; Windows cleans %TEMP% eventually.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Authenticode check through WinVerifyTrust (full chain, no UI), plus the signer subject.</summary>
    [SupportedOSPlatform("windows")]
    public static SignatureCheck VerifySignature(string path)
    {
        var valid = WinTrust.Verify(path);
        string? subject = null;
        try
        {
            // No X509CertificateLoader equivalent exists for reading the signer of a PE file.
#pragma warning disable SYSLIB0057
            subject = X509Certificate.CreateFromSignedFile(path).Subject;
#pragma warning restore SYSLIB0057
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Unsigned file: subject stays null.
        }

        return new SignatureCheck(valid, subject);
    }

    /// <summary>winget is an app execution alias under %LOCALAPPDATA%\Microsoft\WindowsApps.</summary>
    private static string? FindWinget()
    {
        var candidates = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0)
        {
            candidates.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe"));
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir.Trim(), "winget.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static int Run(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        }) ?? throw new InvalidOperationException($"could not start {fileName}");

        if (!process.WaitForExit(InstallerTimeout))
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }

        return process.ExitCode;
    }

    private static string FormatExitCode(int code) => code == -1 ? "timed out" : $"exit code 0x{code:X8}";

    [SupportedOSPlatform("windows")]
    private static unsafe partial class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustFileInfo
        {
            public uint CbStruct;
            public char* FilePath;
            public nint File;
            public Guid* KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint CbStruct;
            public nint PolicyCallbackData;
            public nint SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public WintrustFileInfo* FileInfo;
            public uint StateAction;
            public nint StateData;
            public char* UrlReference;
            public uint ProvFlags;
            public uint UiContext;
            public nint SignatureSettings;
        }

        private const uint WtdUiNone = 2;
        private const uint WtdRevokeWholeChain = 1;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;

        [LibraryImport("wintrust.dll")]
        private static partial int WinVerifyTrust(nint hwnd, Guid* action, WintrustData* data);

        public static bool Verify(string path)
        {
            fixed (char* filePath = path)
            {
                var file = new WintrustFileInfo { CbStruct = (uint)sizeof(WintrustFileInfo), FilePath = filePath };
                var data = new WintrustData
                {
                    CbStruct = (uint)sizeof(WintrustData),
                    UiChoice = WtdUiNone,
                    RevocationChecks = WtdRevokeWholeChain,
                    UnionChoice = WtdChoiceFile,
                    FileInfo = &file,
                    StateAction = WtdStateActionVerify,
                };

                var action = GenericVerifyV2;
                var result = WinVerifyTrust(-1, &action, &data); // INVALID_HANDLE_VALUE: no UI
                data.StateAction = WtdStateActionClose;
                _ = WinVerifyTrust(-1, &action, &data);
                return result == 0;
            }
        }
    }
}
