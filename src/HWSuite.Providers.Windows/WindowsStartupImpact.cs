using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HWSuite.Core.Maintenance;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Startup impact: what each startup program's running process used (processor time, disk reads, memory), and
/// the last boot as Windows itself measured it (Diagnostics-Performance log).
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsStartupImpact
{
    /// <summary>When Windows started (UTC).</summary>
    public static DateTime BootUtc => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// One sample per program file that is running now, keyed by full path. Processes of other users or
    /// protected ones that cannot be opened are skipped.
    /// </summary>
    public static IReadOnlyList<ImpactSample> Measure(IEnumerable<string> executables)
    {
        var wanted = new HashSet<string>(executables.Where(e => !string.IsNullOrEmpty(e)), StringComparer.OrdinalIgnoreCase);
        var byName = wanted.GroupBy(e => Path.GetFileNameWithoutExtension(e), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, (double Cpu, long Disk, double Mem, double After)>(StringComparer.OrdinalIgnoreCase);
        var boot = BootUtc;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!byName.TryGetValue(process.ProcessName, out var candidates))
                {
                    continue;
                }

                try
                {
                    var path = process.MainModule?.FileName;
                    var exe = path is not null ? candidates.FirstOrDefault(c => c.Equals(path, StringComparison.OrdinalIgnoreCase)) : null;
                    exe ??= candidates.Count == 1 ? candidates[0] : null;
                    if (exe is null)
                    {
                        continue;
                    }

                    long read = 0;
                    if (GetProcessIoCounters(process.Handle, out var io))
                    {
                        read = (long)io.ReadTransferCount;
                    }

                    var after = (process.StartTime.ToUniversalTime() - boot).TotalSeconds;
                    var t = totals.GetValueOrDefault(exe, (0, 0, 0, double.MaxValue));
                    totals[exe] = (t.Cpu + process.TotalProcessorTime.TotalMilliseconds, t.Disk + read, t.Mem + (process.WorkingSet64 / 1048576.0), Math.Min(t.After, after));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // protected process or it just exited
                }
            }
        }

        return [.. totals.Select(kv => new ImpactSample(kv.Key, kv.Value.Cpu, kv.Value.Disk, kv.Value.Mem, Math.Max(0, kv.Value.After)))];
    }

    /// <summary>The last boot from Windows' own measurement; null when the log is empty or unreadable (needs admin).</summary>
    public static BootReport? LastBoot()
    {
        try
        {
            var info = new ProcessStartInfo("wevtutil.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "qe", "Microsoft-Windows-Diagnostics-Performance/Operational", "/q:*[System[(EventID=100 or EventID=101)]]", "/c:60", "/rd:true", "/f:xml" })
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var xml = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            return process.ExitCode == 0 ? StartupRules.ParseBoot(xml) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Where the measurement of the current boot is kept.</summary>
    public static string SamplesPath(string folder) => Path.Combine(folder, "startup-impact.txt");

    /// <summary>The saved measurement, if it belongs to this boot (boots within a minute count as the same).</summary>
    public static IReadOnlyList<ImpactSample>? LoadThisBoot(string folder)
    {
        try
        {
            var path = SamplesPath(folder);
            if (!File.Exists(path) || StartupRules.ParseSamples(File.ReadAllText(path)) is not { } saved)
            {
                return null;
            }

            return Math.Abs((saved.BootUtc - BootUtc).TotalSeconds) < 60 ? saved.Samples : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Measures every enabled startup program now and saves it as this boot's measurement.</summary>
    public static void SaveThisBoot(string folder)
    {
        var entries = WindowsStartup.List();
        var samples = Measure(entries.Where(e => e.Enabled && e.Executable is not null).Select(e => e.Executable!));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(SamplesPath(folder), StartupRules.FormatSamples(BootUtc, samples));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not saved: the page measures live instead
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
