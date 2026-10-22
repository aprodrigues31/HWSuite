using System.Globalization;
using System.Text.RegularExpressions;

namespace HWSuite.Core.Sensors;

/// <summary>
/// What one Super I/O input means on a given motherboard: its real name, the resistor divider the
/// board uses in front of it (<see cref="Scale"/>), or that nothing is wired to it (<see cref="Hide"/>).
/// <see cref="Index"/> is the chip input number, the last part of the sensor id (".../voltage/4").
/// </summary>
public sealed record BoardSensorRule(SensorKind Kind, int Index, string? Name = null, double Scale = 1, double Offset = 0, bool Hide = false);

/// <summary>
/// Labels for one motherboard model. Super I/O chips only report numbered inputs; which input is the
/// CPU fan header or the +12 V rail, and with which divider, is a board design decision, so it has to
/// come from the board's documentation or from readings checked against the BIOS.
/// </summary>
public sealed record BoardProfile(string Name, Regex Board, string Chip, IReadOnlyList<BoardSensorRule> Rules)
{
    public bool Matches(string? board, string chipHardwareName) =>
        board is not null && Board.IsMatch(board) && chipHardwareName.Contains(Chip, StringComparison.OrdinalIgnoreCase);

    public BoardSensorRule? RuleFor(SensorKind kind, int index) =>
        Rules.FirstOrDefault(r => r.Kind == kind && r.Index == index);
}

public static partial class BoardProfiles
{
    // Fan headers are named as printed on the board, so they can be matched by eye.
    public static IReadOnlyList<BoardProfile> All { get; } =
    [
        new BoardProfile(
            "ASUS TUF GAMING B550M-PLUS",
            TufB550mPlus(),
            "NCT6798D",
            [
                // Dividers confirmed against the CPU's own telemetry (SVI2 Core 1.138 V vs 0.576 V x 2)
                // and the nominal rails (+5 V: 1.008 V x 5; +12 V: 0.992 V x 12).
                new(SensorKind.Voltage, 0, "CPU Core (Vcore)", Scale: 2),
                new(SensorKind.Voltage, 1, "+5V", Scale: 5),
                new(SensorKind.Voltage, 4, "+12V", Scale: 12),
                new(SensorKind.Voltage, 2, "AVCC"),
                new(SensorKind.Voltage, 3, "+3.3V"),
                new(SensorKind.Voltage, 7, "+3.3V Standby"),
                new(SensorKind.Voltage, 8, "CMOS Battery"),
            ]),
    ];

    /// <summary>The profile for this board and chip, if HWSuite has one.</summary>
    public static BoardProfile? Find(string? board, string chipHardwareName) =>
        All.FirstOrDefault(p => p.Matches(board, chipHardwareName));

    /// <summary>Chip input number from an id such as "/lpc/nct6798d/0/voltage/4"; null for other ids.</summary>
    public static int? InputIndex(string sensorId)
    {
        ArgumentNullException.ThrowIfNull(sensorId);
        var slash = sensorId.LastIndexOf('/');
        return slash >= 0 && int.TryParse(sensorId.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }

    /// <summary>Applies the matching rule: renamed, rescaled (value, min and max) or dropped (null).</summary>
    public static SensorReading? Apply(BoardProfile profile, SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(reading);
        if (InputIndex(reading.Id) is not { } index || profile.RuleFor(reading.Kind, index) is not { } rule)
        {
            return reading;
        }

        if (rule.Hide)
        {
            return null;
        }

        double? Fix(double? v) => v is { } x && double.IsFinite(x) ? (x * rule.Scale) + rule.Offset : v;
        return reading with
        {
            Name = rule.Name ?? reading.Name,
            Value = Fix(reading.Value),
            Min = Fix(reading.Min),
            Max = Fix(reading.Max),
        };
    }

    // ASUS reports the Wi-Fi model as "TUF GAMING B550M-PLUS (WI-FI)"; the sensor layout is the same.
    [GeneratedRegex(@"\bTUF GAMING B550M-PLUS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TufB550mPlus();
}

/// <summary>
/// Puts board-specific names and dividers on Super I/O readings. The board is the reading's
/// <see cref="SensorReading.Parent"/> (the motherboard name); boards without a profile pass through.
/// </summary>
public sealed class BoardProfileSource(ISensorSource inner) : ISensorSource
{
    private readonly Dictionary<(string? Board, string Chip), BoardProfile?> _cache = [];

    public string Name => inner.Name;

    public IReadOnlyList<Model.Diagnostic> Diagnostics => inner.Diagnostics;

    /// <summary>Profiles used so far, for "names from profile X" notes.</summary>
    public IReadOnlyCollection<string> ProfilesInUse => [.. _cache.Values.OfType<BoardProfile>().Select(p => p.Name).Distinct()];

    public SensorSnapshot Read()
    {
        var snapshot = inner.Read();
        var readings = new List<SensorReading>(snapshot.Readings.Count);
        foreach (var reading in snapshot.Readings)
        {
            var profile = ProfileFor(reading);
            if (profile is null)
            {
                readings.Add(reading);
            }
            else if (BoardProfiles.Apply(profile, reading) is { } applied)
            {
                readings.Add(applied);
            }
        }

        return snapshot with { Readings = readings };
    }

    public void Dispose() => inner.Dispose();

    private BoardProfile? ProfileFor(SensorReading reading)
    {
        if (!string.Equals(reading.HardwareKind, "Super I/O", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = (reading.Parent, reading.Hardware);
        if (!_cache.TryGetValue(key, out var profile))
        {
            profile = BoardProfiles.Find(reading.Parent, reading.Hardware);
            _cache[key] = profile;
        }

        return profile;
    }
}
