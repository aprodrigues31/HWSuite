using HWSuite.Core.Sensors;

namespace HWSuite.Core.Alerts;

public enum AlertDirection
{
    Above,
    Below,
}

/// <summary>
/// Which sensors a rule watches: one exact sensor (<see cref="SensorId"/>) or every sensor that
/// matches the filters (for example every CPU temperature). With several matches the rule looks at
/// the worst one: the highest value for <see cref="AlertDirection.Above"/>, the lowest for Below.
/// </summary>
public sealed record AlertTarget
{
    public string? SensorId { get; init; }

    public string? HardwareKind { get; init; }

    public SensorKind? Kind { get; init; }

    public string? NameContains { get; init; }

    public bool Matches(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (SensorId is not null)
        {
            return string.Equals(reading.Id, SensorId, StringComparison.Ordinal);
        }

        return (HardwareKind is null || string.Equals(reading.HardwareKind, HardwareKind, StringComparison.OrdinalIgnoreCase))
            && (Kind is null || reading.Kind == Kind)
            && (NameContains is null || reading.Name.Contains(NameContains, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A threshold on one sensor or a group of sensors.</summary>
public sealed record AlertRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Name shown in settings and notifications, e.g. "CPU temperature".</summary>
    public string Label { get; init; } = "";

    public AlertTarget Target { get; init; } = new();

    public double Threshold { get; init; }

    public AlertDirection Direction { get; init; } = AlertDirection.Above;

    /// <summary>The value must come back this far past the threshold before the alert clears.</summary>
    public double Hysteresis { get; init; } = 3;

    /// <summary>Consecutive readings beyond the threshold needed to fire (filters short spikes).</summary>
    public int Samples { get; init; } = 3;

    public bool Enabled { get; init; } = true;

    /// <summary>Rules a fresh install starts with: temperatures that mean trouble on most hardware.</summary>
    public static IReadOnlyList<AlertRule> Defaults(Localization.Strings text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return
        [
            new AlertRule
            {
                Id = "default-cpu-temperature",
                Label = text["alert.default.cpu"],
                Target = new AlertTarget { HardwareKind = "CPU", Kind = SensorKind.Temperature },
                Threshold = 90,
            },
            new AlertRule
            {
                Id = "default-gpu-temperature",
                Label = text["alert.default.gpu"],
                Target = new AlertTarget { HardwareKind = "GPU", Kind = SensorKind.Temperature },
                Threshold = 95,
            },
            new AlertRule
            {
                Id = "default-storage-temperature",
                Label = text["alert.default.storage"],
                Target = new AlertTarget { HardwareKind = "Storage", Kind = SensorKind.Temperature },
                Threshold = 70,
            },
        ];
    }
}

public enum AlertChange
{
    Raised,
    Cleared,
}

/// <summary>A rule crossed its threshold (or came back). <see cref="Reading"/> is the sensor that decided it.</summary>
public sealed record AlertEvent(AlertRule Rule, AlertChange Change, SensorReading Reading, double Value);

/// <summary>
/// Turns snapshots into alert events. A rule fires after <see cref="AlertRule.Samples"/> readings in a
/// row beyond the threshold and clears only when the value returns past the threshold by
/// <see cref="AlertRule.Hysteresis"/>, so a value hovering at the limit does not spam notifications.
/// </summary>
public sealed class AlertEvaluator
{
    private readonly Dictionary<string, RuleState> _state = new(StringComparer.Ordinal);

    /// <summary>Rules currently raised, by rule id.</summary>
    public IReadOnlyCollection<string> Active => [.. _state.Where(p => p.Value.Raised).Select(p => p.Key)];

    public IReadOnlyList<AlertEvent> Evaluate(SensorSnapshot snapshot, IEnumerable<AlertRule> rules)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rules);

        var events = new List<AlertEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!rule.Enabled || !seen.Add(rule.Id))
            {
                continue;
            }

            var worst = Worst(snapshot, rule);
            if (!_state.TryGetValue(rule.Id, out var state))
            {
                state = new RuleState();
                _state[rule.Id] = state;
            }

            if (worst is not { } found)
            {
                state.Count = 0; // sensor gone: keep a raised alert until we can see it recover
                continue;
            }

            var (reading, value) = found;
            var beyond = rule.Direction == AlertDirection.Above ? value > rule.Threshold : value < rule.Threshold;
            if (!state.Raised)
            {
                state.Count = beyond ? state.Count + 1 : 0;
                if (state.Count >= Math.Max(1, rule.Samples))
                {
                    state.Raised = true;
                    events.Add(new AlertEvent(rule, AlertChange.Raised, reading, value));
                }
            }
            else
            {
                var margin = Math.Abs(rule.Hysteresis);
                var recovered = rule.Direction == AlertDirection.Above
                    ? value <= rule.Threshold - margin
                    : value >= rule.Threshold + margin;
                if (recovered)
                {
                    state.Raised = false;
                    state.Count = 0;
                    events.Add(new AlertEvent(rule, AlertChange.Cleared, reading, value));
                }
            }
        }

        // Rules removed or disabled since the last call stop being tracked.
        foreach (var id in _state.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _state.Remove(id);
        }

        return events;
    }

    /// <summary>True when the reading is beyond an enabled rule that targets it (for highlighting).</summary>
    public static bool IsBeyond(SensorReading reading, IEnumerable<AlertRule> rules)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(rules);
        return reading.Value is { } value && double.IsFinite(value) && rules.Any(rule =>
            rule.Enabled && rule.Target.Matches(reading)
            && (rule.Direction == AlertDirection.Above ? value > rule.Threshold : value < rule.Threshold));
    }

    private static (SensorReading Reading, double Value)? Worst(SensorSnapshot snapshot, AlertRule rule)
    {
        (SensorReading, double)? worst = null;
        foreach (var reading in snapshot.Readings)
        {
            if (reading.Value is not { } value || !double.IsFinite(value) || !rule.Target.Matches(reading))
            {
                continue;
            }

            if (worst is null
                || (rule.Direction == AlertDirection.Above ? value > worst.Value.Item2 : value < worst.Value.Item2))
            {
                worst = (reading, value);
            }
        }

        return worst;
    }

    private sealed class RuleState
    {
        public int Count { get; set; }

        public bool Raised { get; set; }
    }
}
