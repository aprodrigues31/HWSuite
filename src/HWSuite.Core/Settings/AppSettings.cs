using System.Text.Json;
using System.Text.Json.Serialization;
using HWSuite.Core.Alerts;

namespace HWSuite.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Desktop app preferences, stored as JSON in %LOCALAPPDATA%\HWSuite\settings.json.</summary>
public sealed record AppSettings
{
    public const int MinInterval = 1;
    public const int MaxInterval = 60;

    /// <summary>Seconds between sensor reads.</summary>
    public int IntervalSeconds { get; init; } = 1;

    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool CloseToTray { get; init; } = true;

    /// <summary>Start (minimized, as administrator) when the user signs in to Windows.</summary>
    public bool StartWithWindows { get; init; }

    public bool AlertsEnabled { get; init; } = true;

    /// <summary>Null until the user edits alerts; then the defaults are copied in and owned by the user.</summary>
    public IReadOnlyList<AlertRule>? Alerts { get; init; }

    /// <summary>Write every reading to a CSV file while the app runs.</summary>
    public bool LogEnabled { get; init; }

    /// <summary>Folder for CSV logs; null means Documents\HWSuite\logs.</summary>
    public string? LogFolder { get; init; }

    /// <summary>UI and report language: null follows Windows, otherwise "pt-BR" or "en".</summary>
    public string? Language { get; init; }

    /// <summary>Look for a newer release on GitHub when the app starts.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Alert when the +12 V, +5 V or +3.3 V rails leave the ATX tolerance (motherboard readings).</summary>
    public bool PsuAlertsEnabled { get; init; } = true;

    /// <summary>The fan sensor the liquid cooler's pump is plugged into (sensor id), or null.</summary>
    public string? PumpSensorId { get; init; }

    /// <summary>Alert when the pump turns slower than this.</summary>
    public int PumpMinRpm { get; init; } = DefaultPumpMinRpm;

    /// <summary>Accent color of the interface ("cyan", "violet", "green", "magenta", "amber"); null is cyan.</summary>
    public string? Accent { get; init; }

    /// <summary>Maintenance report header: the company that did the service.</summary>
    public string? CompanyName { get; init; }

    /// <summary>CNPJ / tax id, printed under the company name.</summary>
    public string? CompanyDocument { get; init; }

    /// <summary>Phone, e-mail, site.</summary>
    public string? CompanyContact { get; init; }

    public string? CompanyAddress { get; init; }

    /// <summary>Technician who signs the report.</summary>
    public string? TechnicianName { get; init; }

    /// <summary>Components shown on the Sensors page ("Cpu,Gpu"); null shows all of them.</summary>
    public string? SensorCategories { get; init; }

    /// <summary>Components shown on the Hardware page ("Cpu,Gpu"); null shows all of them.</summary>
    public string? InventoryCategories { get; init; }

    /// <summary>Sensors on the Charts page ("Hardware / Name" per line); null picks the CPU and GPU temperatures.</summary>
    public string? ChartSensors { get; init; }

    /// <summary>Charts page time window in minutes (live data).</summary>
    public int ChartMinutes { get; init; } = 5;

    /// <summary>The user agreed, once and for all, that M-Lab publishes their Internet tests (with the public IP).</summary>
    public bool MlabConsent { get; init; }

    public const int DefaultPumpMinRpm = 500;

    public AppSettings Normalized() => this with
    {
        IntervalSeconds = Math.Clamp(IntervalSeconds, MinInterval, MaxInterval),
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        PumpMinRpm = Math.Clamp(PumpMinRpm, 100, 5000),
        Language = Language is "pt-BR" or "en" ? Language : null,
        PumpSensorId = string.IsNullOrWhiteSpace(PumpSensorId) ? null : PumpSensorId,
        LogFolder = string.IsNullOrWhiteSpace(LogFolder) ? null : LogFolder,
    };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves <see cref="AppSettings"/>. A missing or damaged file means defaults, never a crash.</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HWSuite", "settings.json");

    public static AppSettings Parse(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings()).Normalized();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings.Normalized(), SettingsJsonContext.Default.AppSettings);
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path) ? Parse(File.ReadAllText(Path)) : new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes through a temporary file so a crash mid-save cannot leave half a file behind.</summary>
    public void Save(AppSettings settings)
    {
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = Path + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        File.Move(temp, Path, overwrite: true);
    }
}
