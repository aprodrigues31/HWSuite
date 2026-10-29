using System.Xml.Linq;
using HWSuite.Core.Alerts;
using HWSuite.Core.Localization;
using HWSuite.Core.Sensors;
using HWSuite.Core.Settings;
using HWSuite.Providers.Windows;

namespace HWSuite.Tests;

public class AlertEvaluatorTests
{
    private static SensorReading Temp(string id, string hardwareKind, string name, double? value) =>
        new(id, hardwareKind + " device", hardwareKind, name, SensorKind.Temperature, value, null, null);

    private static SensorSnapshot Snap(params SensorReading[] readings) => new(DateTimeOffset.UnixEpoch, readings);

    private static readonly AlertRule CpuRule = new()
    {
        Id = "cpu",
        Label = "CPU",
        Target = new AlertTarget { HardwareKind = "CPU", Kind = SensorKind.Temperature },
        Threshold = 90,
        Hysteresis = 5,
        Samples = 3,
    };

    [Fact]
    public void Fires_only_after_consecutive_samples_beyond_the_threshold()
    {
        var evaluator = new AlertEvaluator();
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 95)), [CpuRule]));
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 80)), [CpuRule])); // spike reset
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 95)), [CpuRule]));
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 96)), [CpuRule]));
        var raised = Assert.Single(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 97)), [CpuRule]));
        Assert.Equal(AlertChange.Raised, raised.Change);
        Assert.Equal(97, raised.Value);
        Assert.Contains("cpu", evaluator.Active);
    }

    [Fact]
    public void Clears_only_past_the_hysteresis_and_does_not_repeat_while_raised()
    {
        var evaluator = new AlertEvaluator();
        var rule = CpuRule with { Samples = 1 };
        Assert.Single(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 91)), [rule]));
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 99)), [rule]));
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 88)), [rule])); // below 90 but within 5
        var cleared = Assert.Single(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 85)), [rule]));
        Assert.Equal(AlertChange.Cleared, cleared.Change);
        Assert.Empty(evaluator.Active);
    }

    [Fact]
    public void Group_rule_uses_the_hottest_matching_sensor()
    {
        var evaluator = new AlertEvaluator();
        var rule = CpuRule with { Samples = 1 };
        var events = evaluator.Evaluate(Snap(
            Temp("a", "CPU", "Core #1", 70),
            Temp("b", "CPU", "Core (Tctl/Tdie)", 93),
            Temp("c", "GPU", "GPU Core", 99)), [rule]);
        var e = Assert.Single(events);
        Assert.Equal("b", e.Reading.Id);
    }

    [Fact]
    public void Below_rule_and_exact_sensor_target()
    {
        var evaluator = new AlertEvaluator();
        var rule = new AlertRule
        {
            Id = "fan",
            Target = new AlertTarget { SensorId = "/lpc/fan/0" },
            Threshold = 300,
            Direction = AlertDirection.Below,
            Samples = 1,
        };
        var fan = new SensorReading("/lpc/fan/0", "Nuvoton", "Super I/O", "Fan #1", SensorKind.Fan, 120, null, null);
        var other = fan with { Id = "/lpc/fan/1", Value = 0 };
        var e = Assert.Single(evaluator.Evaluate(Snap(fan, other), [rule]));
        Assert.Equal(AlertChange.Raised, e.Change);
        Assert.Equal("/lpc/fan/0", e.Reading.Id);
    }

    [Fact]
    public void Disabled_rules_and_missing_values_never_fire()
    {
        var evaluator = new AlertEvaluator();
        var disabled = CpuRule with { Samples = 1, Enabled = false };
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", 120)), [disabled]));
        var rule = CpuRule with { Samples = 1 };
        Assert.Empty(evaluator.Evaluate(Snap(Temp("t", "CPU", "Core", null), Temp("u", "CPU", "Core", double.NaN)), [rule]));
    }

    [Fact]
    public void IsBeyond_marks_readings_over_a_matching_rule()
    {
        Assert.True(AlertEvaluator.IsBeyond(Temp("t", "CPU", "Core", 95), [CpuRule]));
        Assert.False(AlertEvaluator.IsBeyond(Temp("t", "CPU", "Core", 60), [CpuRule]));
        Assert.False(AlertEvaluator.IsBeyond(Temp("t", "GPU", "Core", 95), [CpuRule]));
    }

    [Fact]
    public void Default_rules_are_localized_and_target_cpu_gpu_and_storage()
    {
        var rules = AlertRule.Defaults(Strings.For("pt-BR"));
        Assert.Equal(["CPU", "GPU", "Storage"], rules.Select(r => r.Target.HardwareKind));
        Assert.Equal("Temperatura do processador", rules[0].Label);
        Assert.All(rules, r => Assert.Equal(SensorKind.Temperature, r.Target.Kind));
    }
}

public class SettingsAndSummaryTests
{
    [Fact]
    public void Settings_round_trip_through_json()
    {
        var settings = new AppSettings
        {
            IntervalSeconds = 2,
            Theme = AppTheme.Dark,
            CloseToTray = false,
            LogEnabled = true,
            LogFolder = @"D:\logs",
            Alerts = [new AlertRule { Id = "x", Label = "Fan", Target = new AlertTarget { SensorId = "/fan/0" }, Threshold = 300, Direction = AlertDirection.Below }],
        };

        var json = SettingsStore.Serialize(settings);
        Assert.Contains("\"theme\": \"Dark\"", json, StringComparison.Ordinal);
        Assert.Contains("\"direction\": \"Below\"", json, StringComparison.Ordinal);

        var back = SettingsStore.Parse(json);
        Assert.Equal(2, back.IntervalSeconds);
        Assert.Equal(AppTheme.Dark, back.Theme);
        Assert.False(back.CloseToTray);
        Assert.Equal(@"D:\logs", back.LogFolder);
        var rule = Assert.Single(back.Alerts!);
        Assert.Equal("/fan/0", rule.Target.SensorId);
        Assert.Equal(AlertDirection.Below, rule.Direction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("null")]
    public void Damaged_settings_fall_back_to_defaults(string json)
    {
        var settings = SettingsStore.Parse(json);
        Assert.Equal(1, settings.IntervalSeconds);
        Assert.True(settings.CloseToTray);
        Assert.Null(settings.Alerts);
    }

    [Fact]
    public void Settings_are_normalized_and_saved_atomically()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hwsuite-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            Assert.Equal(new AppSettings(), store.Load()); // missing file
            store.Save(new AppSettings { IntervalSeconds = 500, LogFolder = "  " });
            var loaded = store.Load();
            Assert.Equal(AppSettings.MaxInterval, loaded.IntervalSeconds);
            Assert.Null(loaded.LogFolder);
            Assert.False(File.Exists(store.Path + ".tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Tooltip_prefers_the_package_temperatures()
    {
        var snapshot = new SensorSnapshot(DateTimeOffset.UnixEpoch,
        [
            new("c1", "Ryzen", "CPU", "CCD1 (Tdie)", SensorKind.Temperature, 61, null, null),
            new("c2", "Ryzen", "CPU", "Core (Tctl/Tdie)", SensorKind.Temperature, 55.4, null, null),
            new("g1", "RX 7800 XT", "GPU", "GPU Hot Spot", SensorKind.Temperature, 70, null, null),
            new("g2", "RX 7800 XT", "GPU", "GPU Core", SensorKind.Temperature, 48, null, null),
        ]);
        var text = Strings.For("pt-BR");
        Assert.Equal("HWSuite · CPU 55,4 °C · GPU 48,0 °C", SensorSummary.Tooltip(snapshot, text));
        Assert.Equal("HWSuite", SensorSummary.Tooltip(null, text));
    }

    [Fact]
    public void Summary_falls_back_to_the_hottest_sensor()
    {
        var snapshot = new SensorSnapshot(DateTimeOffset.UnixEpoch,
        [
            new("c1", "Xeon", "CPU", "Core #1", SensorKind.Temperature, 50, null, null),
            new("c2", "Xeon", "CPU", "Core #2", SensorKind.Temperature, 57, null, null),
            new("c3", "Xeon", "CPU", "Core #3", SensorKind.Temperature, 0, null, null),
        ]);
        Assert.Equal(57, SensorSummary.CpuTemperature(snapshot));
        Assert.Null(SensorSummary.GpuTemperature(snapshot));
    }

    [Fact]
    public void Startup_task_xml_is_valid_and_runs_elevated_at_logon()
    {
        var xml = StartupTask.BuildTaskXml(@"C:\Apps\HWSuite & Co\HWSuite.Gui.exe", @"PC\ana");
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"C:\Apps\HWSuite & Co\HWSuite.Gui.exe", doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal(StartupTask.MinimizedArgument, doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("InteractiveToken", doc.Descendants(ns + "LogonType").Single().Value);
        Assert.All(doc.Descendants(ns + "UserId"), u => Assert.Equal(@"PC\ana", u.Value));
    }

    [Fact]
    public void Startup_task_registers_and_unregisters_on_an_elevated_windows_runner()
    {
        if (!OperatingSystem.IsWindows() || !AccessStatus.Detect().IsElevated)
        {
            return; // needs Task Scheduler and administrator rights (the CI runner has both)
        }

        var name = "HWSuite-Test-" + Guid.NewGuid().ToString("N")[..8];
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        try
        {
            Assert.Null(StartupTask.Register(exe, name));
            Assert.True(StartupTask.IsRegistered(name));
            Assert.Null(StartupTask.Unregister(name));
            Assert.False(StartupTask.IsRegistered(name));
        }
        finally
        {
            StartupTask.Unregister(name);
        }
    }

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("en", "en")]
    [InlineData("fr", null)]
    [InlineData(null, null)]
    public void Language_setting_keeps_only_supported_languages(string? saved, string? expected) =>
        Assert.Equal(expected, HWSuite.Core.Settings.SettingsStore.Parse(HWSuite.Core.Settings.SettingsStore.Serialize(new HWSuite.Core.Settings.AppSettings { Language = saved })).Language);
}
