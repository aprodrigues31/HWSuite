using System.Globalization;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;

namespace HWSuite.Core.Reporting;

public sealed record ReportRow(string Label, string Value);

/// <summary>A block of rows, optionally titled (e.g. one memory module).</summary>
public sealed record ReportGroup(string? Title, IReadOnlyList<ReportRow> Rows);

public sealed record ReportSection(string Title, IReadOnlyList<ReportGroup> Groups);

/// <summary>
/// Flattens the inventory into localized, display-ready sections. Text and HTML writers
/// render the same sections, so both formats always show the same facts.
/// </summary>
public static class ReportModel
{
    public static IReadOnlyList<ReportSection> Build(HardwareInventory inv, Strings s)
    {
        ArgumentNullException.ThrowIfNull(inv);
        ArgumentNullException.ThrowIfNull(s);
        var c = s.Culture;
        var sections = new List<ReportSection>();

        void Section(string titleKey, params ReportGroup?[] groups)
        {
            var kept = groups.Where(g => g is { Rows.Count: > 0 }).Cast<ReportGroup>().ToList();
            if (kept.Count > 0)
            {
                sections.Add(new ReportSection(s[titleKey], kept));
            }
        }

        if (inv.OperatingSystem is { } os)
        {
            Section("section.os", Group(null,
                (s["field.name"], os.Name),
                (s["field.version"], os.DisplayVersion),
                (s["field.build"], os.Build),
                (s["field.architecture"], os.Architecture)));
        }

        if (inv.System is { } sys)
        {
            Section("section.system", Group(null,
                (s["field.manufacturer"], sys.Manufacturer),
                (s["field.product"], sys.Product),
                (s["field.version"], sys.Version),
                (s["field.family"], sys.Family),
                (s["field.sku"], sys.Sku),
                (s["field.chassis"], sys.ChassisType),
                (s["field.serial"], sys.SerialNumber),
                (s["field.uuid"], sys.Uuid)));
        }

        if (inv.Motherboard is { } mb)
        {
            Section("section.motherboard", Group(null,
                (s["field.manufacturer"], mb.Manufacturer),
                (s["field.product"], mb.Product),
                (s["field.version"], mb.Version),
                (s["field.serial"], mb.SerialNumber)));
        }

        if (inv.Bios is { } bios)
        {
            Section("section.bios", Group(null,
                (s["field.vendor"], bios.Vendor),
                (s["field.version"], bios.Version),
                (s["field.releaseDate"], bios.ReleaseDate),
                (s["field.smbios"], bios.SmbiosVersion)));
        }

        var multiCpu = inv.Processors.Count > 1;
        for (var i = 0; i < inv.Processors.Count; i++)
        {
            var cpu = inv.Processors[i];
            var title = multiCpu ? $"#{i}" : null;
            Section("section.cpu", Group(title,
                (s["field.name"], cpu.Name),
                (s["field.vendor"], cpu.Vendor),
                (s["field.socket"], cpu.Socket),
                (s["field.familyModelStepping"], Join(c, " / ", cpu.Family, Hex(cpu.Model), cpu.Stepping)),
                (s["field.signature"], cpu.Signature),
                (s["field.coresThreads"], Join(c, " / ", cpu.Cores, cpu.Threads)),
                (s["field.clocks"], Join(c, " / ", Mhz(cpu.BusClockMhz), Mhz(cpu.MaxClockMhz), Mhz(cpu.CurrentClockMhz))),
                (s["field.instructions"], cpu.InstructionSets.Count > 0 ? string.Join(", ", cpu.InstructionSets) : null),
                (s["field.hypervisor"], cpu.HypervisorPresent == true ? s["value.yes"] : null)));

            if (cpu.Caches.Count > 0)
            {
                var rows = cpu.Caches
                    .OrderBy(k => k.Level).ThenBy(k => k.Type)
                    .Select(k => (
                        Label: $"L{k.Level} {CacheTypeLabel(k.Type)}".TrimEnd(),
                        Value: (string?)s.Format("cache.detail", Bytes(k.SizeBytes, c), k.Ways, k.LineSizeBytes, k.SharedByThreads == 1 ? s["cache.sharedOne"] : s.Format("cache.shared", k.SharedByThreads))))
                    .ToArray();
                Section("section.caches", Group(title, rows));
            }
        }

        if (inv.Memory is { } mem)
        {
            var summary = Group(null,
                (s["field.total"], mem.TotalInstalledBytes is { } t ? Bytes(t, c) : null),
                (s["field.slots"], mem.SlotCount is { } slots ? string.Create(c, $"{mem.Modules.Count} / {slots}") : null),
                (s["field.maxCapacity"], mem.MaxCapacityBytes is { } max ? Bytes(max, c) : null));
            var modules = mem.Modules.Select(m => Group(m.Slot ?? m.Bank,
                (s["field.size"], Bytes(m.SizeBytes, c)),
                (s["field.type"], Join(c, " ", m.Type, m.FormFactor)),
                (s["field.speed"], Join(c, " / ", Mts(m.SpeedMts), Mts(m.ConfiguredSpeedMts))),
                (s["field.ranks"], m.Ranks?.ToString(c)),
                (s["field.manufacturer"], m.Manufacturer),
                (s["field.partNumber"], m.PartNumber),
                (s["field.serial"], m.SerialNumber)));
            Section("section.memory", [summary, .. modules]);
        }

        Section("section.gpu", [.. inv.Gpus.Select(g => Group(g.Name,
            (s["field.vendor"], g.Vendor),
            (s["field.pciId"], g.PciVendorId is null ? null : $"{g.PciVendorId}:{g.PciDeviceId}" + (g.PciSubsystemId is null ? "" : $" / {g.PciSubsystemId}")),
            (s["field.dedicatedMemory"], g.DedicatedMemoryBytes is { } d ? Bytes(d, c) : null),
            (s["field.sharedMemory"], g.SharedMemoryBytes is { } sh ? Bytes(sh, c) : null),
            (s["field.driver"], Join(c, " / ", g.DriverVersion, g.DriverDate)),
            (s["field.vbios"], g.BiosVersion)))]);

        Section("section.disks", [.. inv.Disks.Select(d => Group($"#{d.Index} {d.Model}".Trim(),
            (s["field.vendor"], d.Vendor),
            (s["field.size"], d.SizeBytes is { } size ? Bytes(size, c) : null),
            (s["field.bus"], d.BusType),
            (s["field.media"], d.MediaType),
            (s["field.firmware"], d.Firmware),
            (s["field.serial"], d.SerialNumber),
            (s["field.removable"], d.Removable == true ? s["value.yes"] : null),
            (s["field.temperature"], d.Health?.TemperatureCelsius is { } temp ? string.Create(c, $"{temp} °C") : null),
            (s["field.wear"], d.Health is { } h ? Join(c, " / ", Pct(h.PercentageUsed), Pct(h.AvailableSparePercent)) : null),
            (s["field.powerOn"], d.Health is { } h2 ? Join(c, " / ", h2.PowerOnHours?.ToString("N0", c), h2.PowerCycles?.ToString("N0", c)) : null),
            (s["field.dataRw"], d.Health is { } h3 ? Join(c, " / ", Bytes(h3.DataReadBytes, c), Bytes(h3.DataWrittenBytes, c)) : null),
            (s["field.unsafeShutdowns"], d.Health?.UnsafeShutdowns?.ToString("N0", c)),
            (s["field.mediaErrors"], d.Health?.MediaErrors?.ToString("N0", c)),
            (s["field.criticalWarning"], d.Health?.CriticalWarning is int w and > 0 ? string.Create(c, $"0x{w:X2}") : null)))]);

        if (inv.Diagnostics.Count > 0)
        {
            Section("section.diagnostics", Group(null, [.. inv.Diagnostics.Select(d =>
                (Label: $"{d.Source} ({s[$"severity.{d.Severity}"]})", Value: (string?)d.Message))]));
        }

        return sections;
    }

    public static string Bytes(long bytes, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 || Math.Abs(value - Math.Round(value)) < 0.05 ? "0" : "0.#";
        return value.ToString(format, culture) + " " + units[unit];
    }

    private static string? Bytes(long? bytes, CultureInfo culture) => bytes is { } b ? Bytes(b, culture) : null;

    private static ReportGroup Group(string? title, params (string Label, string? Value)[] rows) =>
        new(title, [.. rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new ReportRow(r.Label, r.Value!))]);

    private static string? Join(CultureInfo c, string separator, params object?[] parts)
    {
        var present = parts.Select(p => p switch
        {
            null => null,
            IFormattable f => f.ToString(null, c),
            _ => p.ToString(),
        }).ToList();

        return present.All(string.IsNullOrWhiteSpace) ? null : string.Join(separator, present.Select(p => p ?? "—"));
    }

    private static string? Hex(int? value) => value is { } v ? string.Create(CultureInfo.InvariantCulture, $"0x{v:X}") : null;

    private static string? Mhz(int? value) => value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v} MHz") : null;

    private static string? Mts(int? value) => value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v} MT/s") : null;

    private static string? Pct(int? value) => value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v}%") : null;

    private static string CacheTypeLabel(CacheType type) => type switch
    {
        CacheType.Data => "D",
        CacheType.Instruction => "I",
        _ => "",
    };
}
