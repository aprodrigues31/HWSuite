using System.Globalization;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Localization;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Core.Sensors;

namespace HWSuite.Core.Maintenance;

/// <summary>
/// The maintenance diagnosis: each check looks at one thing (Windows support, BIOS age, temperatures, the load
/// test, memory, every drive and volume, the graphics card, cooling, the power supply rails, batteries, the
/// benchmark, the Internet) and gives a verdict with advice a client can follow. Thresholds are the usual
/// ones for desktop and notebook maintenance; a check without data says so instead of guessing.
/// </summary>
public static class MaintenanceChecks
{
    public const double CpuIdleWarm = 65;
    public const double CpuIdleHot = 80;
    public const double CpuLoadWarm = 85;
    public const double CpuLoadHot = 95;
    public const double GpuIdleWarm = 70;
    public const double DiskWarm = 60;
    public const double DiskHot = 70;
    public const double FreeLow = 20;
    public const double FreeCritical = 10;
    public const int WearHigh = 75;
    public const int WearCritical = 90;
    public const double BatteryWorn = 20;
    public const double BatteryDead = 40;
    public const int BiosOldYears = 4;
    public const int DriverOldMonths = 12;

    public static IReadOnlyList<CheckResult> Run(MaintenanceInput input, Strings s)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(s);
        var list = new List<CheckResult>();
        var inv = input.Inventory;
        var dash = input.Sensors is { } snapshot ? Dashboard.From(snapshot, input.PumpSensorId) : null;

        Os(inv, input.Now, s, list);
        Bios(inv, input.Now, s, list);
        CpuIdle(dash, s, list);
        Load(input.Load, s, list);
        Memory(inv, s, list);
        Disks(inv, s, list);
        Volumes(input.Volumes, s, list);
        Graphics(inv, input.Sensors, dash, input.Now, s, list);
        Cooling(input, dash, s, list);
        Power(input.Sensors, s, list);
        Batteries(inv, s, list);
        Performance(input, s, list);
        Network(input, s, list);
        return [.. list.OrderBy(c => c.Area)];
    }

    // ---- System -------------------------------------------------------------------------------------------------

    private static void Os(HardwareInventory inv, DateTimeOffset now, Strings s, List<CheckResult> list)
    {
        if (inv.OperatingSystem is not { } os)
        {
            return;
        }

        var name = string.Join(" ", new[] { os.Name, os.DisplayVersion }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var finding = os.Build is null ? name : s.Format("mnt.os.finding", name, os.Build);
        var build = int.TryParse(os.Build?.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : 0;
        var end = build > 0 ? WindowsLifecycle.EndOfUpdates(build, os.Name) : null;
        var today = DateOnly.FromDateTime(now.Date);
        var c = s.Culture;
        CheckResult result = end switch
        {
            null => new(CheckArea.System, s["mnt.os"], finding, Verdict.Info),
            { } e when build < 22000 => new(CheckArea.System, s["mnt.os"], finding, Verdict.Problem, s.Format("mnt.os.win10", e.ToString("d", c))),
            { } e when today > e => new(CheckArea.System, s["mnt.os"], finding + " · " + s.Format("mnt.os.ended", e.ToString("d", c)), Verdict.Problem, s["mnt.os.update"]),
            { } e when today.AddDays(90) > e => new(CheckArea.System, s["mnt.os"], finding + " · " + s.Format("mnt.os.ending", e.ToString("d", c)), Verdict.Attention, s["mnt.os.update"]),
            { } e => new(CheckArea.System, s["mnt.os"], finding + " · " + s.Format("mnt.os.supported", e.ToString("d", c)), Verdict.Ok),
        };
        list.Add(result);
    }

    private static void Bios(HardwareInventory inv, DateTimeOffset now, Strings s, List<CheckResult> list)
    {
        if (inv.Bios is not { } bios || inv.System?.Product?.Contains("Virtual", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var c = s.Culture;
        var finding = string.Join(" · ", new[] { bios.Vendor, bios.Version, DateText.Display(bios.ReleaseDate, c) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!DateOnly.TryParseExact(bios.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            list.Add(new(CheckArea.System, s["mnt.bios"], finding, Verdict.Info));
            return;
        }

        var years = (now.Date - date.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25;
        list.Add(years > BiosOldYears
            ? new(CheckArea.System, s["mnt.bios"], finding + " · " + s.Format("mnt.bios.age", Math.Floor(years)), Verdict.Attention, s["mnt.bios.update"])
            : new(CheckArea.System, s["mnt.bios"], finding, Verdict.Ok));
    }

    // ---- Processor ---------------------------------------------------------------------------------------------

    private static void CpuIdle(DashboardReadout? dash, Strings s, List<CheckResult> list)
    {
        var item = s["mnt.cpuIdle"];
        if (dash?.Cpu.Temperature is not { } t)
        {
            list.Add(new(CheckArea.Processor, item, s["mnt.noReading"], Verdict.Info, s["mnt.noReading.advice"]));
            return;
        }

        var finding = s.Format("mnt.temp", t.ToString("0", s.Culture));
        list.Add(t switch
        {
            >= CpuIdleHot => new(CheckArea.Processor, item, finding, Verdict.Problem, s["mnt.cpu.clean"]),
            >= CpuIdleWarm => new(CheckArea.Processor, item, finding, Verdict.Attention, s["mnt.cpu.clean"]),
            _ => new(CheckArea.Processor, item, finding, Verdict.Ok),
        });
    }

    private static void Load(LoadTest? load, Strings s, List<CheckResult> list)
    {
        if (load is null)
        {
            return;
        }

        var c = s.Culture;
        var item = s.Format("mnt.load", load.Duration.TotalSeconds.ToString("0", c));
        var parts = new List<string>();
        if (load.MaxCpuTemperature is { } t)
        {
            parts.Add(s.Format("mnt.load.max", t.ToString("0", c)));
        }

        if (load.MaxCpuPower is { } p)
        {
            parts.Add($"{p.ToString("0", c)} W");
        }

        if (load.MinCpuClockMhz is { } mhz)
        {
            parts.Add(s.Format("mnt.load.clock", (mhz / 1000).ToString("0.00", c)));
        }

        parts.Add(load.Errors == 0 ? s["mnt.load.noErrors"] : s.Format("mnt.load.errors", load.Errors));
        var finding = string.Join(" · ", parts);
        list.Add(load switch
        {
            { Errors: > 0 } => new(CheckArea.Processor, item, finding, Verdict.Problem, s["mnt.load.unstable"]),
            { MaxCpuTemperature: >= CpuLoadHot } => new(CheckArea.Processor, item, finding, Verdict.Problem, s["mnt.cpu.clean"]),
            { MaxCpuTemperature: >= CpuLoadWarm } => new(CheckArea.Processor, item, finding, Verdict.Attention, s["mnt.cpu.clean"]),
            { MaxCpuTemperature: null } => new(CheckArea.Processor, item, finding, Verdict.Ok, s["mnt.noReading.advice"]),
            _ => new(CheckArea.Processor, item, finding, Verdict.Ok),
        });
    }

    // ---- Memory ------------------------------------------------------------------------------------------------

    private static void Memory(HardwareInventory inv, Strings s, List<CheckResult> list)
    {
        if (inv.Memory is not { } mem)
        {
            return;
        }

        var c = s.Culture;
        var total = mem.TotalInstalledBytes ?? mem.Modules.Sum(m => m.SizeBytes);
        if (total > 0)
        {
            var gb = total / (double)(1L << 30);
            var finding = s.Format("mnt.mem.finding", ReportModel.Bytes(total, c), mem.Modules.Count, mem.SlotCount?.ToString(c) ?? "?");
            list.Add(gb switch
            {
                < 3.9 => new(CheckArea.Memory, s["mnt.mem"], finding, Verdict.Problem, s["mnt.mem.small"]),
                < 7.9 => new(CheckArea.Memory, s["mnt.mem"], finding, Verdict.Attention, s["mnt.mem.small"]),
                _ => new(CheckArea.Memory, s["mnt.mem"], finding, Verdict.Ok),
            });
        }

        // One module in a board with more slots runs in single channel: about half the memory bandwidth.
        if (mem.Modules.Count == 1 && mem.SlotCount >= 2)
        {
            list.Add(new(CheckArea.Memory, s["mnt.mem.channel"], s["mnt.mem.single"], Verdict.Attention, s["mnt.mem.single.advice"]));
        }
        else if (mem.Modules.Count >= 2)
        {
            var parts = mem.Modules.Select(m => (m.PartNumber ?? m.Spd?.PartNumber ?? "").Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var sizes = mem.Modules.Select(m => m.SizeBytes).Distinct().Count();
            list.Add(parts > 1 || sizes > 1
                ? new(CheckArea.Memory, s["mnt.mem.channel"], s["mnt.mem.mixed"], Verdict.Attention, s["mnt.mem.mixed.advice"])
                : new(CheckArea.Memory, s["mnt.mem.channel"], s.Format("mnt.mem.matched", mem.Modules.Count), Verdict.Ok));
        }

        // Speed: the best XMP/EXPO profile the modules have against the speed they run at.
        var running = mem.Modules.Select(m => m.ConfiguredSpeedMts ?? m.SpeedMts).Where(x => x > 0).DefaultIfEmpty().Min();
        var rated = mem.Modules.Select(m => m.Spd is { } spd ? spd.XmpProfiles.Concat(spd.ExpoProfiles).Select(p => p.SpeedMts).DefaultIfEmpty().Max() : 0).DefaultIfEmpty().Min();
        if (running is { } r && r > 0 && rated > 0)
        {
            list.Add(rated > r + 100
                ? new(CheckArea.Memory, s["mnt.mem.speed"], s.Format("mnt.mem.speed.slow", r, rated), Verdict.Attention, s["mnt.mem.speed.advice"])
                : new(CheckArea.Memory, s["mnt.mem.speed"], s.Format("mnt.mem.speed.ok", r), Verdict.Ok));
        }
    }

    // ---- Storage -----------------------------------------------------------------------------------------------

    private static void Disks(HardwareInventory inv, Strings s, List<CheckResult> list)
    {
        var c = s.Culture;
        foreach (var disk in inv.Disks.Where(d => d.Removable != true))
        {
            var item = string.Join(" ", new[] { disk.Model ?? s["mnt.disk"], disk.SizeBytes is { } size ? $"({ReportModel.Bytes(size, c)})" : null }.OfType<string>());
            var isHdd = disk.MediaType?.Contains("HDD", StringComparison.OrdinalIgnoreCase) == true;
            if (disk.Health is not { } h)
            {
                list.Add(new(CheckArea.Storage, item, s["mnt.disk.noHealth"], Verdict.Info));
            }
            else
            {
                var verdict = Verdict.Ok;
                var notes = new List<string>();
                string? advice = null;
                void Raise(Verdict v, string? note, string adviceKey)
                {
                    if (note is not null)
                    {
                        notes.Add(note);
                    }

                    if (v > verdict)
                    {
                        verdict = v;
                        advice = s[adviceKey];
                    }
                }

                if (h.Assessment == DiskHealthStatus.Bad || h.CriticalWarning > 0)
                {
                    Raise(Verdict.Problem, s["mnt.disk.failing"], "mnt.disk.replace");
                }
                else if (h.Assessment == DiskHealthStatus.Caution)
                {
                    Raise(Verdict.Attention, s["mnt.disk.caution"], "mnt.disk.backup");
                }

                var bad = (h.ReallocatedSectors ?? 0) + (h.PendingSectors ?? 0);
                if (bad > 0)
                {
                    Raise(bad > 50 ? Verdict.Problem : Verdict.Attention, s.Format("mnt.disk.sectors", bad), bad > 50 ? "mnt.disk.replace" : "mnt.disk.backup");
                }

                if (h.MediaErrors > 0)
                {
                    Raise(Verdict.Attention, s.Format("mnt.disk.mediaErrors", h.MediaErrors), "mnt.disk.backup");
                }

                if (h.PercentageUsed is { } used)
                {
                    notes.Add(s.Format("mnt.disk.wear", used));
                    if (used >= WearCritical)
                    {
                        Raise(Verdict.Problem, null, "mnt.disk.replace");
                    }
                    else if (used >= WearHigh)
                    {
                        Raise(Verdict.Attention, null, "mnt.disk.plan");
                    }
                }

                if (h.TemperatureCelsius is { } temp)
                {
                    notes.Add(s.Format("mnt.temp", temp));
                    if (temp >= DiskHot)
                    {
                        Raise(Verdict.Problem, s["mnt.disk.hot"], "mnt.disk.cool");
                    }
                    else if (temp >= DiskWarm)
                    {
                        Raise(Verdict.Attention, s["mnt.disk.warm"], "mnt.disk.cool");
                    }
                }

                if (h.PowerOnHours is { } hours)
                {
                    notes.Add(s.Format("mnt.disk.hours", hours.ToString("N0", c)));
                }

                if (verdict == Verdict.Ok)
                {
                    notes.Insert(0, s["mnt.disk.healthy"]);
                }

                list.Add(new(CheckArea.Storage, item, string.Join(" · ", notes.Distinct()), verdict, advice));
            }

            if (isHdd)
            {
                list.Add(new(CheckArea.Storage, item, s["mnt.disk.hdd"], Verdict.Info, s["mnt.disk.hdd.advice"]));
            }
        }
    }

    private static void Volumes(IReadOnlyList<VolumeSpace> volumes, Strings s, List<CheckResult> list)
    {
        var c = s.Culture;
        foreach (var v in volumes.Where(v => v.TotalBytes > 0))
        {
            var item = s.Format(v.IsSystem ? "mnt.space.system" : "mnt.space", v.Name);
            var finding = s.Format("mnt.space.finding", ReportModel.Bytes(v.FreeBytes, c), ReportModel.Bytes(v.TotalBytes, c), v.FreePercent.ToString("0", c));
            list.Add(v.FreePercent switch
            {
                < FreeCritical => new(CheckArea.Storage, item, finding, Verdict.Problem, s["mnt.space.advice"]),
                < FreeLow => new(CheckArea.Storage, item, finding, Verdict.Attention, s["mnt.space.advice"]),
                _ => new(CheckArea.Storage, item, finding, Verdict.Ok),
            });
        }
    }

    // ---- Graphics ----------------------------------------------------------------------------------------------

    private static void Graphics(HardwareInventory inv, SensorSnapshot? snapshot, DashboardReadout? dash, DateTimeOffset now, Strings s, List<CheckResult> list)
    {
        var c = s.Culture;
        foreach (var gpu in inv.Gpus.Where(g => g.Name is not null && !g.Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)))
        {
            var name = gpu.Name!;
            if (gpu.Authenticity is { } a)
            {
                if (a.Findings.Count > 0)
                {
                    list.Add(new(CheckArea.Graphics, s.Format("mnt.gpu.auth", name), s.Format("mnt.gpu.fake", a.Findings.Count), Verdict.Problem, s["mnt.gpu.fake.advice"]));
                }
                else if (a.Checked)
                {
                    list.Add(new(CheckArea.Graphics, s.Format("mnt.gpu.auth", name), s["mnt.gpu.genuine"], Verdict.Ok));
                }
            }

            if (DateOnly.TryParseExact(gpu.DriverDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var driver))
            {
                var months = ((now.Year - driver.Year) * 12) + now.Month - driver.Month;
                var finding = s.Format("mnt.gpu.driver.finding", gpu.DriverVersion ?? "?", driver.ToString("d", c));
                list.Add(months > DriverOldMonths
                    ? new(CheckArea.Graphics, s.Format("mnt.gpu.driver", name), finding, Verdict.Attention, s["mnt.gpu.driver.advice"])
                    : new(CheckArea.Graphics, s.Format("mnt.gpu.driver", name), finding, Verdict.Ok));
            }

            // NVIDIA: a power limit set well below the card's default (by a tool or a previous owner) costs performance.
            if (gpu.PowerLimitW is { } limit && gpu.PowerLimitDefaultW is { } standard && limit < standard * 0.9)
            {
                list.Add(new(CheckArea.Graphics, s.Format("mnt.gpu.power", name),
                    s.Format("mnt.gpu.power.low", limit.ToString("0", c), standard.ToString("0", c)), Verdict.Attention, s["mnt.gpu.power.advice"]));
            }
        }

        // NVIDIA: the card holding its clock back at rest because of temperature, a hardware signal or power delivery.
        if (snapshot is not null)
        {
            var alarming = Gpu.Nvml.LimitSensors.Where(l => Gpu.Nvml.Alarming.HasFlag(l.Flag)).Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var card in snapshot.Readings.Where(r => r.HardwareKind == "GPU" && r.Id.StartsWith("/nvml/", StringComparison.Ordinal)).GroupBy(r => r.Hardware))
            {
                var active = card.Where(r => alarming.Contains(r.Name) && r.Value >= 50).Select(r => r.Name).ToList();
                list.Add(active.Count > 0
                    ? new(CheckArea.Graphics, s.Format("mnt.gpu.limits", card.Key), string.Join(" · ", active), Verdict.Attention, s["mnt.gpu.limits.advice"])
                    : new(CheckArea.Graphics, s.Format("mnt.gpu.limits", card.Key), s["mnt.gpu.limits.none"], Verdict.Ok));
            }
        }

        if (dash?.Gpu is { Temperature: { } t } g)
        {
            var finding = s.Format("mnt.temp", t.ToString("0", c)) + (g.HotSpot is { } hs ? " · " + s.Format("mnt.gpu.hotspot", hs.ToString("0", c)) : "");
            list.Add(t >= GpuIdleWarm
                ? new(CheckArea.Graphics, s.Format("mnt.gpu.temp", g.Name ?? "GPU"), finding, Verdict.Attention, s["mnt.gpu.clean"])
                : new(CheckArea.Graphics, s.Format("mnt.gpu.temp", g.Name ?? "GPU"), finding, Verdict.Ok));
        }
    }

    // ---- Cooling and power -------------------------------------------------------------------------------------

    private static void Cooling(MaintenanceInput input, DashboardReadout? dash, Strings s, List<CheckResult> list)
    {
        if (input.Sensors is not { } snapshot)
        {
            return;
        }

        var c = s.Culture;
        if (input.PumpSensorId is { } pumpId && snapshot.Readings.FirstOrDefault(r => r.Id == pumpId) is { } pump)
        {
            var rpm = pump.Value ?? 0;
            list.Add(rpm < input.PumpMinRpm
                ? new(CheckArea.Cooling, s["mnt.pump"], s.Format("mnt.rpm", rpm.ToString("N0", c)), Verdict.Problem, s["mnt.pump.advice"])
                : new(CheckArea.Cooling, s["mnt.pump"], s.Format("mnt.rpm", rpm.ToString("N0", c)), Verdict.Ok));
        }

        var fans = snapshot.Readings.Where(r => r.Kind == SensorKind.Fan && r.Value is not null && r.Id != input.PumpSensorId).ToList();
        if (fans.Count > 0)
        {
            var stoppedCpu = fans.Where(f => f.Value == 0 && f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)).Select(f => f.Name).ToList();
            var spinning = fans.Where(f => f.Value > 0).Select(f => $"{f.Name} {f.Value!.Value.ToString("N0", c)}").ToList();
            var finding = spinning.Count > 0 ? string.Join(" · ", spinning) + " RPM" : s["mnt.fans.none"];
            list.Add(stoppedCpu.Count > 0
                ? new(CheckArea.Cooling, s["mnt.fans"], s.Format("mnt.fans.stopped", string.Join(", ", stoppedCpu)) + " · " + finding, Verdict.Attention, s["mnt.fans.advice"])
                : new(CheckArea.Cooling, s["mnt.fans"], finding, Verdict.Ok));
        }
    }

    private static void Power(SensorSnapshot? snapshot, Strings s, List<CheckResult> list)
    {
        if (snapshot is null)
        {
            return;
        }

        var rails = PowerRails.Read(snapshot);
        var c = s.Culture;
        if (rails.Count == 0)
        {
            list.Add(new(CheckArea.Power, s["mnt.rails"], s["mnt.rails.none"], Verdict.Info));
            return;
        }

        var finding = string.Join(" · ", rails.Select(x => $"{x.Rail.Name} {x.Volts.ToString("0.00", c)} V"));
        var outside = rails.Where(x => !x.Rail.Within(x.Volts)).Select(x => x.Rail.Name).ToList();
        list.Add(outside.Count > 0
            ? new(CheckArea.Power, s["mnt.rails"], finding, Verdict.Problem, s.Format("mnt.rails.advice", string.Join(", ", outside)))
            : new(CheckArea.Power, s["mnt.rails"], finding + " · " + s["mnt.rails.ok"], Verdict.Ok));
    }

    private static void Batteries(HardwareInventory inv, Strings s, List<CheckResult> list)
    {
        var c = s.Culture;
        foreach (var b in inv.Batteries)
        {
            var item = b.IsUps ? s.Format("mnt.ups", b.Name ?? "UPS") : s.Format("mnt.battery", b.Name ?? "");
            if (b.DesignCapacity is not > 0 || b.FullChargeCapacity is not { } full || b.Relative)
            {
                list.Add(new(CheckArea.Battery, item, s["mnt.battery.noData"], Verdict.Info));
                continue;
            }

            var health = full * 100.0 / b.DesignCapacity.Value;
            var wear = 100 - health;
            var finding = s.Format("mnt.battery.finding", Math.Min(100, health).ToString("0", c)) + (b.CycleCount is > 0 ? " · " + s.Format("mnt.battery.cycles", b.CycleCount) : "");
            list.Add(wear switch
            {
                >= BatteryDead => new(CheckArea.Battery, item, finding, Verdict.Problem, s["mnt.battery.replace"]),
                >= BatteryWorn => new(CheckArea.Battery, item, finding, Verdict.Attention, s["mnt.battery.plan"]),
                _ => new(CheckArea.Battery, item, finding, Verdict.Ok),
            });
        }
    }

    // ---- Performance and network -------------------------------------------------------------------------------

    private static void Performance(MaintenanceInput input, Strings s, List<CheckResult> list)
    {
        if (input.BenchmarkResult is not { } result)
        {
            return;
        }

        var c = s.Culture;
        var finding = s.Format("mnt.bench.finding", CpuBenchmark.Points(result.MultiThread, c), CpuBenchmark.Points(result.SingleThread, c));
        var cmp = input.Benchmark;
        if (cmp?.MultiPercent is { } pct)
        {
            finding += " · " + s.Format("mnt.bench.percent", pct.ToString("0", c), cmp.SameModelRuns);
        }

        list.Add((cmp?.Verdict ?? ComparisonVerdict.NoReference) switch
        {
            ComparisonVerdict.AsExpected => new(CheckArea.Performance, s["mnt.bench"], finding, Verdict.Ok),
            ComparisonVerdict.SlightlyBelow => new(CheckArea.Performance, s["mnt.bench"], finding, Verdict.Attention, s["mnt.bench.below"]),
            ComparisonVerdict.Below => new(CheckArea.Performance, s["mnt.bench"], finding, Verdict.Problem, s["mnt.bench.below"]),
            _ => new(CheckArea.Performance, s["mnt.bench"], finding + " · " + s["mnt.bench.noRef"], Verdict.Info),
        });
    }

    private static void Network(MaintenanceInput input, Strings s, List<CheckResult> list)
    {
        if (input.Network is not { } n || input.Now - n.At > TimeSpan.FromDays(30))
        {
            return;
        }

        var c = s.Culture;
        var finding = s.Format("mnt.net.finding", n.DownloadMbps.ToString("N0", c), n.UploadMbps.ToString("N0", c), n.LatencyMs.ToString("0", c), n.At.LocalDateTime.ToString("d", c));
        var loss = n.LossPercent is > HWSuite.Core.Network.NetworkAdvice.LossPercent;
        var bloat = Math.Max(n.LatencyDownloadMs, n.LatencyUploadMs) - n.LatencyMs > HWSuite.Core.Network.NetworkAdvice.BufferbloatMs;
        list.Add(loss || bloat
            ? new(CheckArea.Network, s["mnt.net"], finding, Verdict.Attention, s[loss ? "mnt.net.loss" : "mnt.net.bloat"])
            : new(CheckArea.Network, s["mnt.net"], finding, Verdict.Ok));
    }
}
