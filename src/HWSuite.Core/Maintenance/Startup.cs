namespace HWSuite.Core.Maintenance;

/// <summary>Where Windows keeps a startup entry.</summary>
public enum StartupSource
{
    /// <summary>HKCU\...\Run: this user only.</summary>
    UserRun,

    /// <summary>HKLM\...\Run: every user (64-bit programs).</summary>
    MachineRun,

    /// <summary>HKLM\...\WOW6432Node\...\Run: every user (32-bit programs).</summary>
    MachineRun32,

    /// <summary>The user's Startup folder.</summary>
    UserFolder,

    /// <summary>The Startup folder of all users.</summary>
    CommonFolder,
}

/// <summary>
/// One program that starts with Windows, as Task Manager shows it on its Startup apps tab.
/// </summary>
/// <param name="Name">Registry value name or shortcut file name (also the key of its on/off switch).</param>
/// <param name="Command">The command line (or the shortcut's file path).</param>
/// <param name="Executable">The program file, when it could be worked out from the command.</param>
/// <param name="Publisher">Company name from the program's version information.</param>
/// <param name="Description">File description from the program's version information.</param>
/// <param name="Enabled">Starts at sign-in (off = disabled the way Task Manager does it, nothing deleted).</param>
/// <param name="FileMissing">The program file no longer exists (left over by an uninstalled app).</param>
public sealed record StartupEntry(
    StartupSource Source,
    string Name,
    string Command,
    string? Executable,
    string? Publisher,
    string? Description,
    bool Enabled,
    bool FileMissing)
{
    /// <summary>For every user: changing it needs administrator rights.</summary>
    public bool NeedsAdmin => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder;

    /// <summary>Name shown to people: the program's description when it has one, else the entry name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description!;

    /// <summary>Stable id for the command line: "user-run:OneDrive".</summary>
    public string Id => $"{Source}:{Name}";
}

/// <summary>
/// The rules Windows uses for startup entries, kept apart from the registry so they can be tested:
/// the "StartupApproved" switch (what Task Manager writes) and finding the program in a command line.
/// </summary>
public static class StartupRules
{
    /// <summary>
    /// A StartupApproved value: 12 bytes, the first one even (02, 06) = enabled, odd (03, 07…) = disabled, the
    /// last 8 = when it was disabled (FILETIME). No value at all = enabled.
    /// </summary>
    public static bool IsEnabled(byte[]? approved) => approved is not { Length: > 0 } || (approved[0] & 1) == 0;

    /// <summary>The value to write to switch an entry on or off (Task Manager's own format).</summary>
    public static byte[] Approved(bool enabled, DateTime utcNow)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled)
        {
            BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(value, 4);
        }

        return value;
    }

    /// <summary>
    /// The program in a command line: the quoted part, or the text up to ".exe" (paths with spaces and no
    /// quotes are common in Run keys), or the first word. Environment variables are expanded by the caller.
    /// </summary>
    public static string? Executable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text.Trim('"');
        }

        foreach (var ext in new[] { ".exe", ".cmd", ".bat", ".lnk", ".com" })
        {
            var i = text.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (i > 0 && (i + ext.Length == text.Length || text[i + ext.Length] is ' ' or '"' or ','))
            {
                return text[..(i + ext.Length)];
            }
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? text[..space] : text;
    }

    /// <summary>Sorted for the list: enabled first, then by name.</summary>
    public static IReadOnlyList<StartupEntry> Sort(IEnumerable<StartupEntry> entries) =>
        [.. entries.OrderByDescending(e => e.Enabled).ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
}
