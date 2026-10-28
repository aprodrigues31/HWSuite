using HWSuite.Core.Alerts;
using HWSuite.Core.Localization;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;

namespace HWSuite.Tests;

public class PowerRailsTests
{
    private static readonly Strings Pt = Strings.For("pt-BR");

    private static SensorReading Volt(string name, double value, string kind = "Super I/O") =>
        new($"/lpc/nct6798d/voltage/{name}", "Nuvoton NCT6798D", kind, name, SensorKind.Voltage, value, value, value) { Parent = "ASUS TUF GAMING B550M-PLUS" };

    private static SensorReading Fan(string name, double rpm, int index) =>
        new($"/lpc/nct6798d/fan/{index}", "Nuvoton NCT6798D", "Super I/O", name, SensorKind.Fan, rpm, rpm, rpm);

    private static SensorSnapshot Snapshot(params SensorReading[] readings) => new(DateTimeOffset.Now, readings);

    [Fact]
    public void Atx_ranges()
    {
        var rails = PowerRails.All.ToDictionary(r => r.Name);
        Assert.Equal((11.4, 12.6), (rails["+12V"].Low, rails["+12V"].High));
        Assert.Equal((4.75, 5.25), (rails["+5V"].Low, rails["+5V"].High));
        Assert.Equal((3.135, 3.465), (rails["+3.3V"].Low, rails["+3.3V"].High));
    }

    [Fact]
    public void Rails_are_found_by_board_profile_name_only()
    {
        var snapshot = Snapshot(Volt("+12V", 12.096), Volt("+5V", 5.04), Volt("+3.3V", 3.296), Volt("+3.3V Standby", 3.39), Volt("Vcore", 1.1), Volt("+12V", 12.1, kind: "GPU"));
        var read = PowerRails.Read(snapshot).ToDictionary(x => x.Rail.Name, x => x.Volts);
        Assert.Equal(3, read.Count);
        Assert.Equal(12.096, read["+12V"]);
        Assert.Equal(3.296, read["+3.3V"]);
    }

    [Fact]
    public void Derived_rules_fire_when_12v_sags_under_load()
    {
        var rules = PowerRails.DerivedRules(new AppSettings(), Pt);
        Assert.Equal(6, rules.Count);
        Assert.Equal("+12V abaixo de 11,40 V (faixa ATX)", rules.Single(r => r.Id == "psu-12v-low").Label);

        var evaluator = new AlertEvaluator();
        IReadOnlyList<AlertEvent> events = [];
        foreach (var volts in new[] { 12.1, 11.3, 11.3, 11.3 })
        {
            events = evaluator.Evaluate(Snapshot(Volt("+12V", volts), Volt("+5V", 5.0), Volt("+3.3V", 3.3)), rules);
        }

        var raised = Assert.Single(events);
        Assert.Equal("psu-12v-low", raised.Rule.Id);
        Assert.Equal(AlertChange.Raised, raised.Change);
    }

    [Fact]
    public void Psu_alerts_can_be_turned_off_and_the_pump_rule_follows_the_setting()
    {
        Assert.Empty(PowerRails.DerivedRules(new AppSettings { PsuAlertsEnabled = false }, Pt));
        var pump = Assert.Single(PowerRails.DerivedRules(new AppSettings { PsuAlertsEnabled = false, PumpSensorId = "/lpc/nct6798d/fan/6", PumpMinRpm = 600 }, Pt));
        Assert.Equal(AlertDirection.Below, pump.Direction);
        Assert.Equal(600, pump.Threshold);
        Assert.Equal("/lpc/nct6798d/fan/6", pump.Target.SensorId);
        Assert.Equal("Bomba do water cooler abaixo de 600 RPM", pump.Label);
    }

    [Fact]
    public void A_stalled_pump_raises_the_alert()
    {
        var settings = new AppSettings { PsuAlertsEnabled = false, PumpSensorId = "/lpc/nct6798d/fan/6" };
        var rules = PowerRails.DerivedRules(settings, Pt);
        var evaluator = new AlertEvaluator();
        Assert.Empty(evaluator.Evaluate(Snapshot(Fan("CPU_OPT", 992, 6)), rules));
        evaluator.Evaluate(Snapshot(Fan("CPU_OPT", 0, 6)), rules);
        Assert.Equal(AlertChange.Raised, Assert.Single(evaluator.Evaluate(Snapshot(Fan("CPU_OPT", 0, 6)), rules)).Change);
    }

    [Fact]
    public void The_pump_header_is_renamed()
    {
        var named = PowerRails.NamePump(Snapshot(Fan("CPU_FAN", 1200, 1), Fan("CPU_OPT", 992, 6)), "/lpc/nct6798d/fan/6", Pt);
        Assert.Equal(["CPU_FAN", "Bomba (water cooler) · CPU_OPT"], named.Readings.Select(r => r.Name));
        var untouched = Snapshot(Fan("CPU_FAN", 1200, 1));
        Assert.Same(untouched, PowerRails.NamePump(untouched, null, Pt));
    }

    [Fact]
    public void Stress_tracking_keeps_min_and_max()
    {
        var rails = new Dictionary<string, (double Now, double Min, double Max)>();
        PowerRails.Track(rails, Snapshot(Volt("+12V", 12.10)));
        PowerRails.Track(rails, Snapshot(Volt("+12V", 11.98)));
        PowerRails.Track(rails, Snapshot(Volt("+12V", 12.05)));
        Assert.Equal((12.05, 11.98, 12.10), rails["+12V"]);
        Assert.True(PowerRails.AllWithin(rails));
        Assert.Equal("+12V 12,050 V (11,980–12,100 V) ✓", PowerRails.Summary(rails, Pt));

        PowerRails.Track(rails, Snapshot(Volt("+12V", 11.2)));
        Assert.False(PowerRails.AllWithin(rails));
        Assert.Contains("⚠", PowerRails.Summary(rails, Pt), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_keep_pump_and_psu_choices()
    {
        var json = SettingsStore.Serialize(new AppSettings { PumpSensorId = "/lpc/nct6798d/fan/6", PumpMinRpm = 20, PsuAlertsEnabled = false });
        var back = SettingsStore.Parse(json);
        Assert.Equal("/lpc/nct6798d/fan/6", back.PumpSensorId);
        Assert.Equal(100, back.PumpMinRpm); // clamped
        Assert.False(back.PsuAlertsEnabled);
    }
}
