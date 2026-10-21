using System.Text;
using HWSuite.Cli;
using HWSuite.Core;
using HWSuite.Core.Localization;

Console.OutputEncoding = Encoding.UTF8;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (CliUsageException ex)
{
    var strings = Strings.For((string?)null);
    Console.Error.WriteLine(strings.Format(ex.Key, [.. ex.Arguments]));
    Console.Error.WriteLine();
    Console.Error.WriteLine(strings["cli.usage"]);
    return 2;
}

var s = Strings.For(options.Language);

if (options.ShowHelp)
{
    Console.WriteLine(s["cli.usage"]);
    return 0;
}

if (options.ShowVersion)
{
    Console.WriteLine($"hwsuite {BuildInfo.Current.Display}");
    return 0;
}

return options.Command switch
{
    CliCommand.Sensors => SensorsCommand.Run(options, s),
    _ => ReportCommand.Run(options, s),
};
