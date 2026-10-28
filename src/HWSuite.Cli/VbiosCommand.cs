using HWSuite.Core.Collection;
using HWSuite.Core.Gpu;
using HWSuite.Core.Localization;
using HWSuite.Hosting;

namespace HWSuite.Cli;

/// <summary>
/// "hwsuite vbios [-o folder|file.rom]": saves the video BIOS of each card whose firmware published a
/// copy (AMD cards through the ACPI VFCT table). Needs no administrator rights.
/// </summary>
internal static class VbiosCommand
{
    public static int Run(CliOptions options, Strings s)
    {
        var inventory = new InventoryCollector(ProviderCatalog.Default()).Collect();
        var cards = inventory.Gpus.Where(g => g.VideoBios is not null).ToList();
        if (cards.Count == 0)
        {
            Console.Error.WriteLine(s["vbios.none"]);
            return 1;
        }

        foreach (var gpu in cards)
        {
            var copy = gpu.VideoBios!;
            var path = Target(options.OutputPath, VideoBios.FileName(gpu.Reference?.Model ?? gpu.Name, copy), cards.Count);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, copy.Image);
            Console.WriteLine(s.Format("vbios.saved", gpu.Name, path, Describe(copy, s)));
        }

        return 0;
    }

    /// <summary>A .rom path is used as is (one card); anything else is a folder.</summary>
    private static string Target(string? output, string fileName, int cards) =>
        output is null ? Path.GetFullPath(fileName)
        : cards == 1 && output.EndsWith(".rom", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(output)
        : Path.GetFullPath(Path.Combine(output, fileName));

    internal static string Describe(VideoBiosCopy copy, Strings s) =>
        Core.Reporting.ReportModel.VideoBiosText(copy, s);
}
