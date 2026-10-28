using System.Globalization;
using HWSuite.Core.Alerts;
using HWSuite.Core.Settings;

namespace HWSuite.Core.Sensors;

/// <summary>A power supply rail as the motherboard measures it, with the ATX range (±5 % of nominal).</summary>
public sealed record PowerRail(string Name, double Nominal, double Low, double High)
{

    public bool Within(double volts) => volts >= Low && volts <= High;
}

/// <summary>
/// Power supply health from the motherboard's own voltage readings (most PSUs have no data link): the
/// +12 V, +5 V and +3.3 V rails against the ATX ±5 % tolerance, and the derived alert rules. Also the
/// liquid-cooler pump: a fan header the user marks as the pump, renamed and watched for a stall.
/// </summary>
public static class PowerRails
{
    public static IReadOnlyList<PowerRail> All { get; } =
    [
        new("+12V", 12, 11.40, 12.60),
        new("+5V", 5, 4.75, 5.25),
        new("+3.3V", 3.3, 3.135, 3.465),
    ];

    /// <summary>The rail a reading measures: a Super I/O voltage named "+12V", "+5V" or "+3.3V" (board profile names).</summary>
    public static PowerRail? RailOf(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Kind == SensorKind.Voltage && reading.HardwareKind == "Super I/O"
            ? All.FirstOrDefault(r => string.Equals(reading.Name, r.Name, StringComparison.Ordinal))
            : null;
    }

    /// <summary>Current value of each rail found in the snapshot.</summary>
    public static IReadOnlyList<(PowerRail Rail, double Volts)> Read(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. snapshot.Readings
            .Select(r => (Rail: RailOf(r), r.Value))
            .Where(x => x.Rail is not null && x.Value is > 0)
            .GroupBy(x => x.Rail!.Name)
            .Select(g => (g.First().Rail!, g.First().Value!.Value))];
    }

    /// <summary>
    /// Rules derived from the settings, kept apart from the rules the user edits: two per rail (below and
    /// above the ATX range) and the pump stall rule.
    /// </summary>
    public static IReadOnlyList<AlertRule> DerivedRules(AppSettings settings, Localization.Strings text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        var c = text.Culture;
        var rules = new List<AlertRule>();
        if (settings.PsuAlertsEnabled)
        {
            foreach (var rail in All)
            {
                var target = new AlertTarget { HardwareKind = "Super I/O", Kind = SensorKind.Voltage, NameContains = rail.Name };
                var id = rail.Name.TrimStart('+').Replace(".", "", StringComparison.Ordinal).ToLowerInvariant();
                rules.Add(new AlertRule
                {
                    Id = $"psu-{id}-low",
                    Label = text.Format("alert.psu.low", rail.Name, rail.Low.ToString("0.00#", c)),
                    Target = target,
                    Threshold = rail.Low,
                    Direction = AlertDirection.Below,
                    Hysteresis = 0.03,
                    Samples = 3,
                });
                rules.Add(new AlertRule
                {
                    Id = $"psu-{id}-high",
                    Label = text.Format("alert.psu.high", rail.Name, rail.High.ToString("0.00#", c)),
                    Target = target,
                    Threshold = rail.High,
                    Direction = AlertDirection.Above,
                    Hysteresis = 0.03,
                    Samples = 3,
                });
            }
        }

        if (settings.PumpSensorId is { } pump)
        {
            rules.Add(new AlertRule
            {
                Id = "pump-stall",
                Label = text.Format("alert.pump", settings.PumpMinRpm.ToString("N0", c)),
                Target = new AlertTarget { SensorId = pump },
                Threshold = settings.PumpMinRpm,
                Direction = AlertDirection.Below,
                Hysteresis = 100,
                Samples = 2,
            });
        }

        return rules;
    }

    /// <summary>The snapshot with the pump's fan header shown as "Pump (liquid cooler) · CPU_OPT".</summary>
    public static SensorSnapshot NamePump(SensorSnapshot snapshot, string? pumpSensorId, Localization.Strings text)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(text);
        if (pumpSensorId is null || !snapshot.Readings.Any(r => r.Id == pumpSensorId))
        {
            return snapshot;
        }

        return snapshot with
        {
            Readings = [.. snapshot.Readings.Select(r => r.Id == pumpSensorId ? r with { Name = text.Format("pump.name", r.Name) } : r)],
        };
    }

    /// <summary>Keeps the lowest and highest value of each rail across snapshots (for a stress test).</summary>
    public static void Track(Dictionary<string, (double Now, double Min, double Max)> rails, SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(rails);
        foreach (var (rail, volts) in Read(snapshot))
        {
            rails[rail.Name] = rails.TryGetValue(rail.Name, out var seen)
                ? (volts, Math.Min(seen.Min, volts), Math.Max(seen.Max, volts))
                : (volts, volts, volts);
        }
    }

    /// <summary>True when every tracked rail stayed in the ATX range the whole time.</summary>
    public static bool AllWithin(IReadOnlyDictionary<string, (double Now, double Min, double Max)> rails)
    {
        ArgumentNullException.ThrowIfNull(rails);
        return All.Where(r => rails.ContainsKey(r.Name)).All(r => r.Within(rails[r.Name].Min) && r.Within(rails[r.Name].Max));
    }

    /// <summary>"+12V 12.096 V (11.988–12.120 V) ✓" per rail, for the stress test panels.</summary>
    public static string Summary(IReadOnlyDictionary<string, (double Now, double Min, double Max)> rails, Localization.Strings text)
    {
        ArgumentNullException.ThrowIfNull(rails);
        ArgumentNullException.ThrowIfNull(text);
        var c = text.Culture;
        var parts = All.Where(r => rails.ContainsKey(r.Name)).Select(r =>
        {
            var (now, min, max) = rails[r.Name];
            var ok = r.Within(min) && r.Within(max);
            return text.Format(ok ? "psu.railOk" : "psu.railBad", r.Name, now.ToString("0.000", c), min.ToString("0.000", c), max.ToString("0.000", c));
        });
        return string.Join(" · ", parts);
    }
}
