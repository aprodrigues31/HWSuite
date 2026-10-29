using System.Globalization;

namespace HWSuite.Cli;

/// <summary>Bad command line; <see cref="Key"/> is a localized message key, <see cref="Arguments"/> its values.</summary>
public sealed class CliUsageException(string key, params string[] arguments) : Exception(string.Join(" ", arguments))
{
    public string Key { get; } = key;

    public IReadOnlyList<string> Arguments { get; } = arguments;

    public string Argument => Arguments.Count > 0 ? Arguments[0] : "";
}

public enum CliCommand
{
    Report,
    Sensors,
    Driver,
    Vbios,
    Bench,
}

public enum DriverAction
{
    Status,
    Install,
    Uninstall,
}

/// <summary>
/// Hand-rolled parser: the CLI has a handful of options and this keeps the tool free of
/// third-party dependencies.
/// </summary>
public sealed record CliOptions
{
    public CliCommand Command { get; init; } = CliCommand.Report;

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public string Format { get; init; } = "txt";

    public string? OutputPath { get; init; }

    public bool Redact { get; init; }

    public string? Language { get; init; }

    public bool Watch { get; init; }

    public string? LogPath { get; init; }

    /// <summary>bench: run the stability test until Ctrl+C instead of the timed benchmark.</summary>
    public bool Stress { get; init; }

    /// <summary>bench: test the graphics card instead of the processor.</summary>
    public bool Gpu { get; init; }

    /// <summary>bench: drive letter to test ("C:"), for the disk benchmark.</summary>
    public string? Disk { get; init; }

    /// <summary>bench --disk: test file size in GB (default 1).</summary>
    public int DiskSizeGb { get; init; } = 1;

    /// <summary>Show the chip's own numbered motherboard inputs, without board profiles.</summary>
    public bool Raw { get; init; }

    public double IntervalSeconds { get; init; } = 1;

    public double? DurationSeconds { get; init; }

    public DriverAction DriverAction { get; init; } = DriverAction.Status;

    /// <summary>Skip confirmation prompts (driver install/uninstall, sensor driver offer).</summary>
    public bool AssumeYes { get; init; }

    /// <summary>Internal: the elevated child writes its output here for the parent to show.</summary>
    public string? ResultPath { get; init; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new CliOptions();
        var i = 0;

        // The command is optional; plain "hwsuite" prints the text report to the console.
        if (args.Count > 0 && args[0].Equals("report", StringComparison.OrdinalIgnoreCase))
        {
            i = 1;
        }
        else if (args.Count > 0 && args[0].Equals("sensors", StringComparison.OrdinalIgnoreCase))
        {
            options = options with { Command = CliCommand.Sensors };
            i = 1;
        }
        else if (args.Count > 0 && args[0].Equals("bench", StringComparison.OrdinalIgnoreCase))
        {
            options = options with { Command = CliCommand.Bench };
            i = 1;
        }
        else if (args.Count > 0 && args[0].Equals("vbios", StringComparison.OrdinalIgnoreCase))
        {
            options = options with { Command = CliCommand.Vbios };
            i = 1;
        }
        else if (args.Count > 0 && args[0].Equals("driver", StringComparison.OrdinalIgnoreCase))
        {
            options = options with { Command = CliCommand.Driver };
            i = 1;
            if (args.Count > 1 && !args[1].StartsWith('-'))
            {
                options = options with
                {
                    DriverAction = args[1].ToLowerInvariant() switch
                    {
                        "status" => DriverAction.Status,
                        "install" => DriverAction.Install,
                        "uninstall" => DriverAction.Uninstall,
                        _ => throw new CliUsageException("cli.unknownArgument", args[1]),
                    },
                };
                i = 2;
            }
        }

        string Value(string name)
        {
            if (i + 1 >= args.Count || args[i + 1].StartsWith('-'))
            {
                throw new CliUsageException("cli.missingValue", name);
            }

            return args[++i];
        }

        int SizeGb(string name)
        {
            var text = Value(name);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gb) && gb is >= 1 and <= 64
                ? gb
                : throw new CliUsageException("cli.badNumber", name, text);
        }

        static string DriveLetter(string text)
        {
            var t = text.Trim().TrimEnd('\\', '/').ToUpperInvariant();
            return t.Length is 1 or 2 && char.IsAsciiLetter(t[0]) && (t.Length == 1 || t[1] == ':')
                ? $"{t[0]}:"
                : throw new CliUsageException("cli.unknownArgument", text);
        }

        double Seconds(string name)
        {
            var text = Value(name);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0 || value > 86_400)
            {
                throw new CliUsageException("cli.badNumber", name, text);
            }

            return value;
        }

        for (; i < args.Count; i++)
        {
            var arg = args[i];
            options = arg switch
            {
                "-h" or "--help" or "/?" => options with { ShowHelp = true },
                "-v" or "--version" => options with { ShowVersion = true },
                "-f" or "--format" => options with { Format = Value(arg).ToLowerInvariant() },
                "-o" or "--out" => options with { OutputPath = Value(arg) },
                "--redact" => options with { Redact = true },
                "--lang" => options with { Language = Value(arg) },
                "-w" or "--watch" when options.Command == CliCommand.Sensors => options with { Watch = true },
                "--log" when options.Command == CliCommand.Sensors => options with { LogPath = Value(arg) },
                "--raw" when options.Command == CliCommand.Sensors => options with { Raw = true },
                "--stress" when options.Command == CliCommand.Bench => options with { Stress = true },
                "--gpu" when options.Command == CliCommand.Bench => options with { Gpu = true },
                "--disk" when options.Command == CliCommand.Bench => options with { Disk = DriveLetter(Value(arg)) },
                "--size" when options.Command == CliCommand.Bench => options with { DiskSizeGb = SizeGb(arg) },
                "-i" or "--interval" when options.Command == CliCommand.Sensors => options with { IntervalSeconds = Seconds(arg) },
                "--duration" when options.Command is CliCommand.Sensors or CliCommand.Bench => options with { DurationSeconds = Seconds(arg) },
                "-y" or "--yes" when options.Command is CliCommand.Driver or CliCommand.Sensors => options with { AssumeYes = true },
                "--result" when options.Command is CliCommand.Driver or CliCommand.Sensors => options with { ResultPath = Value(arg) },
                _ => throw new CliUsageException("cli.unknownArgument", arg),
            };
        }

        return options;
    }
}
