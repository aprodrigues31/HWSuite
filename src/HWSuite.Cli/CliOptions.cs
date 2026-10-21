namespace HWSuite.Cli;

/// <summary>Bad command line; <see cref="Key"/> is a localized message key.</summary>
public sealed class CliUsageException(string key, string argument) : Exception(argument)
{
    public string Key { get; } = key;

    public string Argument { get; } = argument;
}

/// <summary>
/// Hand-rolled parser: the CLI has a handful of options and this keeps the tool free of
/// third-party dependencies.
/// </summary>
public sealed record CliOptions
{
    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public string Format { get; init; } = "txt";

    public string? OutputPath { get; init; }

    public bool Redact { get; init; }

    public string? Language { get; init; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new CliOptions();
        var i = 0;

        // "report" is optional; plain "hwsuite" prints the text report to the console.
        if (args.Count > 0 && args[0].Equals("report", StringComparison.OrdinalIgnoreCase))
        {
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
                _ => throw new CliUsageException("cli.unknownArgument", arg),
            };
        }

        return options;
    }
}
