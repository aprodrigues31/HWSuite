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

    public double IntervalSeconds { get; init; } = 1;

    public double? DurationSeconds { get; init; }

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

        string Value(string name)
        {
            if (i + 1 >= args.Count || args[i + 1].StartsWith('-'))
            {
                throw new CliUsageException("cli.missingValue", name);
            }

            return args[++i];
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
                "-i" or "--interval" when options.Command == CliCommand.Sensors => options with { IntervalSeconds = Seconds(arg) },
                "--duration" when options.Command == CliCommand.Sensors => options with { DurationSeconds = Seconds(arg) },
                _ => throw new CliUsageException("cli.unknownArgument", arg),
            };
        }

        return options;
    }
}
