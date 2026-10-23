namespace HWSuite.Core.Gpu;

/// <summary>One row of the AMD Overdrive 8 table (ADLOD8SingleInitSetting): the tuning range and the card's default.</summary>
public sealed record Od8Setting(int Id, int Min, int Max, int Default);

/// <summary>
/// Interprets the AMD Overdrive 8 table the driver reports for a card (ADL2_Overdrive8_Init_SettingX2_Get).
/// The defaults are the card's own values, factory overclock included, which a reference database cannot know.
/// Ids follow ADLOD8SettingId in adl_defines.h (AMD display-library SDK).
/// </summary>
public static class Overdrive8
{
    public const int GfxClockMax = 0;
    public const int GfxClockMin = 1;
    public const int MemoryClockMax = 8;
    public const int PowerPercentage = 9;
    public const int OperatingTempMax = 13;
    public const int MemoryClockMin = 34;

    /// <summary>Default maximum GPU clock and memory clock, in MHz, when the values are plausible.</summary>
    public static (int? GpuMhz, int? MemoryMhz) DefaultClocks(IEnumerable<Od8Setting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var byId = settings.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        int? Clock(int id) => byId.TryGetValue(id, out var s) && s.Default is >= 300 and <= 6000 ? s.Default : null;
        return (Clock(GfxClockMax), Clock(MemoryClockMax));
    }

    /// <summary>Short name of a setting id, for the raw dump.</summary>
    public static string Name(int id) => id switch
    {
        GfxClockMax => "GFXCLK_FMAX",
        GfxClockMin => "GFXCLK_FMIN",
        2 => "GFXCLK_FREQ1",
        3 => "GFXCLK_VOLTAGE1",
        4 => "GFXCLK_FREQ2",
        5 => "GFXCLK_VOLTAGE2",
        6 => "GFXCLK_FREQ3",
        7 => "GFXCLK_VOLTAGE3",
        MemoryClockMax => "UCLK_FMAX",
        PowerPercentage => "POWER_PERCENTAGE",
        10 => "FAN_MIN_SPEED",
        11 => "FAN_ACOUSTIC_LIMIT",
        12 => "FAN_TARGET_TEMP",
        OperatingTempMax => "OPERATING_TEMP_MAX",
        33 => "GFXCLK_CURVE_VFT_FMIN",
        MemoryClockMin => "UCLK_FMIN",
        37 => "OD_VOLTAGE",
        47 => "TDC_PERCENTAGE",
        _ => $"ID_{id}",
    };
}
