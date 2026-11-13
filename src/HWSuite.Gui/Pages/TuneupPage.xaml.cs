using HWSuite.Core.Maintenance;
using HWSuite.Core.Reporting;
using HWSuite.Gui.Hud;
using HWSuite.Providers.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Windows maintenance: disk cleanup, drive optimization (TRIM / defragmentation), Windows Update, Microsoft Store apps
/// and programs through winget, one after the other, with what each did. The one-line summaries go into the services
/// of the next maintenance report (<see cref="App.ServicesDone"/>).
/// </summary>
public sealed partial class TuneupPage : Page
{
    private readonly Dictionary<TuneupTasks, CheckBox> _tasks = [];
    private readonly Dictionary<TuneupTasks, HudPanel> _cards = [];
    private readonly Dictionary<TuneupArea, ToggleButton> _areaButtons = [];
    private ToggleButton? _allAreas;
    private IReadOnlyList<DiskBenchmarkDrive> _drives = [];
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Diagnostics.Stopwatch _taskClock = new();
    private readonly System.Diagnostics.Stopwatch _runClock = new();
    private List<double> _estimates = [];
    private List<TuneupTasks> _running = [];
    private int _current;
    private ToggleSwitch? _scheduleOn;
    private readonly Dictionary<DayOfWeek, CheckBox> _days = [];
    private TimePicker? _time;
    private TextBlock? _scheduleStatus;
    private CheckBox? _recycle;
    private CheckBox? _previous;
    private readonly StackPanel _cleanupDrives = new() { Spacing = 2 };
    private readonly StackPanel _optimizeDrives = new() { Spacing = 2 };
    private bool _drivesLoaded;
    private CancellationTokenSource? _cancel;

    public TuneupPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = "// HWSUITE · " + s["rep.tag"].ToUpper(c);
        PageTitle.Text = s["tune.title"].ToUpper(c);
        Subtitle.Text = s["tune.subtitle"];
        RunButton.Content = s["tune.run"].ToUpper(c);
        StopButton.Content = s["tune.stop"];
        AdminBar.Message = s["tune.needsAdmin"];

        Add(TuneupTasks.SystemHealth, "tune.health", "tune.health.hint", false);
        Add(TuneupTasks.Cleanup, "tune.cleanup", "tune.cleanup.hint", true, extra =>
        {
            _recycle = new CheckBox { Content = s["tune.recycle"] };
            _previous = new CheckBox { Content = s["tune.previous"] };
            extra.Children.Add(_recycle);
            extra.Children.Add(_previous);
            extra.Children.Add(DrivesHeader());
            extra.Children.Add(_cleanupDrives);
        });
        Add(TuneupTasks.Optimize, "tune.optimize", "tune.optimize.hint", true, extra =>
        {
            extra.Children.Add(DrivesHeader());
            extra.Children.Add(_optimizeDrives);
        });
        Add(TuneupTasks.WindowsUpdate, "tune.wu", "tune.wu.hint", true);
        Add(TuneupTasks.StoreApps, "tune.store", "tune.store.hint", true);
        Add(TuneupTasks.DesktopApps, "tune.apps", "tune.apps.hint", false);
        BuildAreas();
        BuildSchedule();
        HistoryTag.Text = ("// " + s["tune.history"]).ToUpper(c);
        _tick.Tick += (_, _) => ShowProgress();

        Loaded += (_, _) =>
        {
            RenderHistory();
            var admin = App.Monitor.Access.IsElevated;
            AdminBar.IsOpen = !admin && !App.IsDemo;
            RunButton.IsEnabled = admin || App.IsDemo;
            if (!_drivesLoaded)
            {
                _drivesLoaded = true;
                _ = LoadDrivesAsync();
            }
        };
    }

    private static TextBlock DrivesHeader() => new()
    {
        Text = App.Text["tune.drives"].ToUpper(App.Text.Culture),
        Style = HudUi.Style("HudTagStyle"),
        Margin = new Thickness(0, 10, 0, 2),
    };

    /// <summary>
    /// The fixed drives, each with a box in both lists: cleanup starts with the Windows drive ticked (Windows' own
    /// cleanup works there), optimization with every drive ticked.
    /// </summary>
    private async Task LoadDrivesAsync()
    {
        var s = App.Text;
        _cleanupDrives.Children.Add(new TextBlock { Text = s["tune.drives.loading"], Foreground = HudUi.TextDim, FontSize = 13 });
        _optimizeDrives.Children.Add(new TextBlock { Text = s["tune.drives.loading"], Foreground = HudUi.TextDim, FontSize = 13 });
        IReadOnlyList<DiskBenchmarkDrive> drives;
        try
        {
            drives = App.IsDemo
                ? [new("C:", null, "Samsung SSD 990 PRO 1TB", "NVMe", 1_000_202_273_280, 412_316_860_416, "SSD"),
                   new("D:", "Dados", "WDC WD20EZBX-00AYRA0", "SATA", 2_000_381_014_016, 1_207_959_552_000, "HDD")]
                : (await Task.Run(DiskBenchmarkTargets.Drives)).Where(d => !d.Removable).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            drives = [];
        }

        _drives = drives;
        var system = App.IsDemo ? "C:" : WindowsTuneup.SystemDrive;
        _cleanupDrives.Children.Clear();
        _optimizeDrives.Children.Clear();
        if (drives.Count == 0)
        {
            _cleanupDrives.Children.Add(new TextBlock { Text = s["tune.drives.none"], Foreground = HudUi.TextDim, FontSize = 13 });
            _optimizeDrives.Children.Add(new TextBlock { Text = s["tune.drives.none"], Foreground = HudUi.TextDim, FontSize = 13 });
        }

        foreach (var drive in drives)
        {
            var isSystem = string.Equals(drive.Letter, system, StringComparison.OrdinalIgnoreCase);
            _cleanupDrives.Children.Add(DriveBox(drive, isSystem, isSystem));
            _optimizeDrives.Children.Add(DriveBox(drive, isSystem, true));
        }

        Tasks.Reflow();
    }

    private static CheckBox DriveBox(DiskBenchmarkDrive drive, bool isSystem, bool on)
    {
        var s = App.Text;
        var title = isSystem ? s["tune.drives.system"] : drive.Label;
        var line = string.Join(" · ", new[] { title, drive.Model, drive.Media }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = drive.Letter + "  ", FontFamily = HudUi.Font("HudMonoFont") });
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = line });
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = s.Format("tune.drives.free", ReportModel.Bytes(drive.FreeBytes, s.Culture), ReportModel.Bytes(drive.TotalBytes, s.Culture)),
            FontSize = 12,
            Foreground = HudUi.TextDim,
        });
        return new CheckBox { Content = text, IsChecked = on, Tag = drive.Letter };
    }

    private static List<string> Chosen(StackPanel list) =>
        [.. list.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag)];

    private void Add(TuneupTasks task, string title, string hint, bool on, Action<StackPanel>? extra = null)
    {
        var s = App.Text;
        var box = new CheckBox { Content = s[title], IsChecked = on, FontFamily = HudUi.Font("HudBodyMediumFont"), FontSize = 15 };
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(box);
        content.Children.Add(new TextBlock { Text = s[hint], TextWrapping = TextWrapping.Wrap, Foreground = HudUi.TextDim, FontSize = 13, Margin = new Thickness(28, 0, 0, 0) });
        if (extra is not null)
        {
            var more = new StackPanel { Spacing = 2, Margin = new Thickness(28, 4, 0, 0) };
            extra(more);
            content.Children.Add(more);
        }

        var card = new HudPanel { Content = content };
        Tasks.Children.Add(card);
        _tasks[task] = box;
        _cards[task] = card;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        var chosen = Selected();
        if (chosen.Count == 0 || App.IsDemo)
        {
            return;
        }

        var cleanupDrives = Chosen(_cleanupDrives);
        var optimizeDrives = Chosen(_optimizeDrives);
        if (chosen.Contains(TuneupTasks.Cleanup) && cleanupDrives.Count == 0)
        {
            StatusText.Text = s["tune.drives.pickCleanup"];
            return;
        }

        if (chosen.Contains(TuneupTasks.Optimize) && optimizeDrives.Count == 0)
        {
            StatusText.Text = s["tune.drives.pickOptimize"];
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s["tune.confirm.title"],
            Content = new TextBlock { Text = s["tune.confirm.body"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = s["tune.confirm.ok"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        Progress.Visibility = Visibility.Visible;
        Results.Children.Clear();
        StartProgress(chosen, optimizeDrives);
        var started = DateTime.Now;
        var outcomes = new List<TuneupOutcome>();
        var options = new CleanupOptions(_recycle?.IsChecked == true, _previous?.IsChecked == true, cleanupDrives);
        void Log(string text) => DispatcherQueue.TryEnqueue(() => StatusText.Text = text);
        var reboot = false;

        // Nothing restarts this computer until the technician says so at the end.
        using var blocker = new Services.ShutdownBlocker(s["tune.blockReason"]);
        try
        {
            for (var i = 0; i < chosen.Count; i++)
            {
                var task = chosen[i];
                if (token.IsCancellationRequested)
                {
                    break;
                }

                _current = i;
                _taskClock.Restart();
                ShowProgress();

                var outcome = await Task.Run(() => task switch
                {
                    TuneupTasks.Cleanup => WindowsTuneup.Cleanup(options, s, Log, token),
                    TuneupTasks.Optimize => WindowsTuneup.Optimize(optimizeDrives, s, Log, token),
                    TuneupTasks.WindowsUpdate => WindowsTuneup.WindowsUpdate(s, Log, token),
                    TuneupTasks.StoreApps => WindowsTuneup.StoreApps(s, Log, token),
                    TuneupTasks.SystemHealth => WindowsTuneup.SystemHealth(s, Log, token),
                    _ => WindowsTuneup.DesktopApps(s, Log, token),
                }, CancellationToken.None);
                reboot |= outcome.RebootRequired;
                outcomes.Add(outcome);
                if (!token.IsCancellationRequested)
                {
                    var seconds = _taskClock.Elapsed.TotalSeconds;
                    _ = Task.Run(() => WindowsMaintenanceSchedule.LearnTime(task, seconds));
                }

                App.ServicesDone.Add(outcome.Summary);
                Results.Children.Add(ResultBlock(outcome));
            }

            StatusText.Text = s["tune.done"];
            _tick.Stop();
            Progress.Visibility = Visibility.Collapsed;
            ProgressText.Text = s.Format("tune.progressDone", MaintenancePlan.Duration(_runClock.Elapsed.TotalSeconds, s.Culture).TrimStart('~', ' '));
            var pending = reboot || blocker.RestartWasAsked || await Task.Run(WindowsTuneup.RestartPending);
            var run = new MaintenanceRun(started, DateTime.Now, false, pending, outcomes);
            await Task.Run(() => WindowsMaintenanceSchedule.Record(run));
            RenderHistory();
            if (pending)
            {
                await AskRestartAsync(blocker);
            }
        }
        finally
        {
            _tick.Stop();
            Progress.Visibility = Visibility.Collapsed;
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    /// <summary>Restart now or later: the technician decides (the blocker holds Windows until then).</summary>
    private async Task AskRestartAsync(Services.ShutdownBlocker blocker)
    {
        var s = App.Text;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s["tune.restart.title"],
            Content = new TextBlock { Text = s["tune.restart.body"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = s["tune.restart.now"],
            CloseButtonText = s["tune.restart.later"],
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            blocker.Dispose();
            StatusText.Text = WindowsTuneup.Restart(TimeSpan.FromSeconds(15), s["tune.restart.comment"])
                ? s["tune.restart.starting"]
                : s["tune.restart.failed"];
        }
        else
        {
            StatusText.Text = s["tune.done"] + " " + s["tune.restart.pending"];
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private static StackPanel ResultBlock(TuneupOutcome outcome)
    {
        var block = new StackPanel { Spacing = 2, Padding = new Thickness(0, 6, 0, 6), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        block.Children.Add(new TextBlock
        {
            Text = (outcome.Succeeded ? "✓ " : "! ") + outcome.Summary,
            FontFamily = HudUi.Font("HudBodyMediumFont"),
            FontSize = 14,
            Foreground = outcome.Succeeded ? HudUi.Brush("HudGoodBrush") : HudUi.Warn,
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var line in outcome.Details.Take(40))
        {
            block.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = HudUi.TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 0, 0, 0) });
        }

        return block;
    }

    // ---- Areas ---------------------------------------------------------------------------------------------------

    private void BuildAreas()
    {
        var s = App.Text;
        AreaHint.Text = s["tune.areaHint"];
        _allAreas = Chip(s["tune.area.All"]);
        _allAreas.IsChecked = true;
        _allAreas.Click += (_, _) =>
        {
            _allAreas.IsChecked = true;
            foreach (var b in _areaButtons.Values)
            {
                b.IsChecked = false;
            }

            ApplyAreas();
        };
        Areas.Children.Add(_allAreas);
        foreach (var area in Enum.GetValues<TuneupArea>())
        {
            var chip = Chip(s[$"tune.area.{area}"]);
            chip.Click += (_, _) =>
            {
                _allAreas!.IsChecked = !_areaButtons.Values.Any(b => b.IsChecked == true);
                ApplyAreas();
            };
            _areaButtons[area] = chip;
            Areas.Children.Add(chip);
        }
    }

    private static ToggleButton Chip(string label) => new()
    {
        Content = label.ToUpper(App.Text.Culture),
        FontFamily = HudUi.Font("HudMonoFont"),
        FontSize = 12,
        CharacterSpacing = 80,
        Padding = new Thickness(14, 7, 14, 7),
    };

    private bool Shown(TuneupTasks task) =>
        _allAreas?.IsChecked != false || (_areaButtons.TryGetValue(MaintenancePlan.Area(task), out var b) && b.IsChecked == true);

    private void ApplyAreas()
    {
        foreach (var (task, card) in _cards)
        {
            card.Visibility = Shown(task) ? Visibility.Visible : Visibility.Collapsed;
        }

        Tasks.Reflow();
    }

    /// <summary>Ticked tasks of the areas shown, in the order they run.</summary>
    private List<TuneupTasks> Selected() =>
        [.. MaintenancePlan.Order.Where(t => _tasks.TryGetValue(t, out var box) && box.IsChecked == true && Shown(t))];

    // ---- Progress and time left ------------------------------------------------------------------------------------

    private void StartProgress(List<TuneupTasks> chosen, List<string> optimizeDrives)
    {
        var learned = App.IsDemo || !OperatingSystem.IsWindows() ? new Dictionary<TuneupTasks, double>() : WindowsMaintenanceSchedule.Times();
        var picked = _drives.Where(d => optimizeDrives.Contains(d.Letter, StringComparer.OrdinalIgnoreCase)).ToList();
        var hdd = picked.Count(d => string.Equals(d.Media, "HDD", StringComparison.OrdinalIgnoreCase));
        _running = chosen;
        _estimates = [.. chosen.Select(t => t != TuneupTasks.Optimize && learned.TryGetValue(t, out var sec) ? sec : MaintenancePlan.DefaultSeconds(t, Math.Max(1, picked.Count - hdd), hdd))];
        _current = 0;
        _runClock.Restart();
        _taskClock.Restart();
        Progress.Value = 0;
        _tick.Start();
        ShowProgress();
    }

    private void ShowProgress()
    {
        if (_running.Count == 0)
        {
            return;
        }

        var s = App.Text;
        var elapsed = _taskClock.Elapsed.TotalSeconds;
        Progress.Value = MaintenancePlan.Fraction(_estimates, _current, elapsed);
        var task = _running[Math.Min(_current, _running.Count - 1)];
        var title = _tasks.TryGetValue(task, out var box) ? box.Content as string ?? task.ToString() : task.ToString();
        ProgressText.Text = s.Format("tune.progress", _current + 1, _running.Count, title, MaintenancePlan.Duration(MaintenancePlan.Remaining(_estimates, _current, elapsed), s.Culture));
    }

    // ---- Schedule --------------------------------------------------------------------------------------------------

    private void BuildSchedule()
    {
        var s = App.Text;
        var c = s.Culture;
        var cfg = App.Settings.Current;
        SchedulePanel.Children.Add(new TextBlock { Text = ("// " + s["tune.schedule"]).ToUpper(c), Style = HudUi.Style("HudTagStyle") });
        SchedulePanel.Children.Add(new TextBlock { Text = s["tune.schedule.hint"], TextWrapping = TextWrapping.Wrap, Foreground = HudUi.TextDim, FontSize = 13 });
        _scheduleOn = new ToggleSwitch { Header = s["tune.schedule.on"], IsOn = cfg.ScheduleEnabled };
        SchedulePanel.Children.Add(_scheduleOn);

        SchedulePanel.Children.Add(new TextBlock { Text = s["tune.schedule.days"].ToUpper(c), Style = HudUi.Style("HudTagStyle") });
        var days = new WrapPanel { Spacing = 6, LineSpacing = 4 };
        var chosen = cfg.ScheduleDays ?? [DayOfWeek.Sunday];
        var first = c.DateTimeFormat.FirstDayOfWeek;
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            var box = new CheckBox { Content = c.DateTimeFormat.GetAbbreviatedDayName(day), IsChecked = chosen.Contains(day), MinWidth = 70 };
            _days[day] = box;
            days.Children.Add(box);
        }

        SchedulePanel.Children.Add(days);
        var time = TimeOnly.TryParse(cfg.ScheduleTime, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(3, 0);
        _time = new TimePicker { Header = s["tune.schedule.time"], Time = time.ToTimeSpan(), ClockIdentifier = "24HourClock", MinuteIncrement = 5 };
        SchedulePanel.Children.Add(_time);
        var save = new Button { Content = s["tune.schedule.save"], Style = HudUi.Style("HudAccentButtonStyle") };
        save.Click += (_, _) => SaveSchedule();
        SchedulePanel.Children.Add(save);
        _scheduleStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = HudUi.Style("HudMonoStyle"), FontSize = 13 };
        SchedulePanel.Children.Add(_scheduleStatus);
        SchedulePanel.Children.Add(new TextBlock { Text = s["tune.schedule.note"], TextWrapping = TextWrapping.Wrap, Foreground = HudUi.Tag, FontSize = 12 });
        ShowSchedule();
    }

    private MaintenanceSchedule? CurrentSchedule()
    {
        var cfg = App.Settings.Current;
        if (!cfg.ScheduleEnabled || cfg.ScheduleDays is not { Count: > 0 } days)
        {
            return null;
        }

        var time = TimeOnly.TryParse(cfg.ScheduleTime, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(3, 0);
        return new MaintenanceSchedule(days, time, cfg.ScheduleTasks, cfg.ScheduleRecycleBin);
    }

    private void ShowSchedule(string? message = null)
    {
        var s = App.Text;
        var schedule = CurrentSchedule();
        var text = schedule is null
            ? s["tune.schedule.off"]
            : s.Format("tune.schedule.saved", schedule.Next(DateTime.Now)?.ToString("f", s.Culture) ?? "—") + "\n"
              + s.Format("tune.schedule.tasks", string.Join(", ", MaintenancePlan.Order.Where(x => schedule.Tasks.HasFlag(x)).Select(x => _tasks.TryGetValue(x, out var b) ? b.Content as string : x.ToString())));
        _scheduleStatus!.Text = message is null ? text : message + "\n" + text;
    }

    private void SaveSchedule()
    {
        var s = App.Text;
        var on = _scheduleOn!.IsOn;
        var days = _days.Where(d => d.Value.IsChecked == true).Select(d => d.Key).ToList();
        var tasks = Selected().Aggregate(TuneupTasks.None, (a, b) => a | b);
        var time = TimeOnly.FromTimeSpan(_time!.Time);
        if (on && days.Count == 0)
        {
            _scheduleStatus!.Text = s["tune.schedule.noDays"];
            return;
        }

        if (on && tasks == TuneupTasks.None)
        {
            _scheduleStatus!.Text = s["tune.schedule.noTasks"];
            return;
        }

        try
        {
            if (!App.IsDemo && OperatingSystem.IsWindows())
            {
                if (on)
                {
                    if (WindowsMaintenanceSchedule.CliPath() is null)
                    {
                        _scheduleStatus!.Text = s["tune.schedule.noCli"];
                        return;
                    }

                    WindowsMaintenanceSchedule.Save(new MaintenanceSchedule(days, time, tasks, _recycle?.IsChecked == true), s["tune.schedule"]);
                }
                else
                {
                    WindowsMaintenanceSchedule.Delete();
                }
            }

            App.Settings.Update(x => x with
            {
                ScheduleEnabled = on,
                ScheduleDays = days,
                ScheduleTime = time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ScheduleTasks = tasks,
                ScheduleRecycleBin = _recycle?.IsChecked == true,
            });
            ShowSchedule(on ? null : s["tune.schedule.removed"]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _scheduleStatus!.Text = s.Format("tune.schedule.failed", ex.Message);
        }
    }

    // ---- Results ---------------------------------------------------------------------------------------------------

    private void RenderHistory()
    {
        var s = App.Text;
        var c = s.Culture;
        var runs = App.IsDemo || !OperatingSystem.IsWindows() ? DemoHistory() : WindowsMaintenanceSchedule.History();
        HistoryRows.Children.Clear();
        if (runs.Count == 0)
        {
            HistoryRows.Children.Add(new TextBlock { Text = s["tune.history.none"], Foreground = HudUi.TextDim, FontSize = 13 });
            return;
        }

        foreach (var run in runs.Take(10))
        {
            var block = new StackPanel { Spacing = 3, Padding = new Thickness(0, 0, 0, 8), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
            var state = run.Succeeded ? s["tune.history.ok"] : s["tune.history.problems"];
            if (run.RebootPending)
            {
                state += " · " + s["tune.history.reboot"];
            }

            block.Children.Add(new TextBlock
            {
                Text = s.Format("tune.history.took", run.Start.ToString("g", c), run.Scheduled ? s["tune.history.scheduled"] : s["tune.history.manual"], MaintenancePlan.Duration((run.End - run.Start).TotalSeconds, c).TrimStart('~', ' ')) + " — " + state,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = run.Succeeded ? HudUi.Text : HudUi.Warn,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var o in run.Outcomes)
            {
                block.Children.Add(new TextBlock
                {
                    Text = (o.Succeeded ? "✓ " : "✗ ") + o.Summary,
                    FontSize = 13,
                    Foreground = o.Succeeded ? HudUi.TextDim : HudUi.Warn,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            HistoryRows.Children.Add(block);
        }
    }

    private static IReadOnlyList<MaintenanceRun> DemoHistory() =>
    [
        new(DateTime.Today.AddHours(3), DateTime.Today.AddHours(3).AddMinutes(38), true, true,
        [
            new(TuneupTasks.SystemHealth, true, "Saúde do Windows: problemas encontrados e corrigidos", []),
            new(TuneupTasks.Cleanup, true, "Limpeza de disco: 3,2 GB liberados", []),
            new(TuneupTasks.WindowsUpdate, true, "Windows Update: 2 atualizações instaladas", []),
        ]),
        new(DateTime.Today.AddDays(-3).AddHours(21), DateTime.Today.AddDays(-3).AddHours(21).AddMinutes(9), false, false,
        [
            new(TuneupTasks.Cleanup, true, "Limpeza de disco: 1,1 GB liberado", []),
            new(TuneupTasks.Optimize, false, "Otimização: 1 de 2 unidades", []),
        ]),
    ];
}
