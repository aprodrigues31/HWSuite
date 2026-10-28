using HWSuite.Core.Alerts;
using HWSuite.Core.Settings;

namespace HWSuite.Gui.Services;

/// <summary>The app's settings: loaded once at start, saved on every change, observed by the monitor and the window.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store = new(SettingsStore.DefaultPath);

    public SettingsService()
    {
        Current = _store.Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    /// <summary>The alert rules in effect: the user's list, or the defaults until the user edits them.</summary>
    public IReadOnlyList<AlertRule> Rules => Current.Alerts ?? AlertRule.Defaults(App.Text);

    /// <summary>The user's rules plus the ones derived from settings (power supply rails, pump), which are not edited as rules.</summary>
    public IReadOnlyList<AlertRule> EffectiveRules => [.. Rules, .. Core.Sensors.PowerRails.DerivedRules(Current, App.Text)];

    public static string DefaultLogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite", "logs");

    public string LogFolder => Current.LogFolder ?? DefaultLogFolder;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Current = change(Current).Normalized();
        try
        {
            _store.Save(Current);
        }
        catch (IOException)
        {
            // Settings still apply for this session; a read-only profile must not break the app.
        }
        catch (UnauthorizedAccessException)
        {
        }

        Changed?.Invoke(this, Current);
    }

    public void UpdateRules(Func<List<AlertRule>, IEnumerable<AlertRule>> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var rules = change([.. Rules]).ToList();
        Update(s => s with { Alerts = rules });
    }
}
