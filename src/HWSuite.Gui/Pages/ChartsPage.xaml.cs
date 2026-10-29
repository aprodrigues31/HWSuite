using System.Diagnostics;
using HWSuite.Core.Sensors;
using HWSuite.Gui.Hud;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Charts: pick sensors (up to <see cref="MaxSensors"/>) and see them over time, one chart per unit, live (the last
/// hour kept by the monitor) or from a recorded CSV log; recording is switched on and off here too.
/// </summary>
public sealed partial class ChartsPage : Page
{
    private const int MaxSensors = 8;
    private static readonly int[] WindowMinutes = [1, 5, 15, 60, 0]; // minutes; 0 = everything

    /// <summary>Line colors, readable on the current skin's background.</summary>
    private static readonly Color[] Palette = [.. HudSkins.Current.ChartColors.Select(HudSkins.Parse)];

    /// <summary>Most sensor chips shown at once; the filters narrow the rest down.</summary>
    private const int MaxChips = 120;

    private static readonly SensorKind[] KindOrder =
    [
        SensorKind.Temperature, SensorKind.Voltage, SensorKind.Fan, SensorKind.Power, SensorKind.Clock, SensorKind.Load,
        SensorKind.Current, SensorKind.Control, SensorKind.Level, SensorKind.Data, SensorKind.Throughput,
        SensorKind.Frequency, SensorKind.Flow, SensorKind.Factor, SensorKind.Energy, SensorKind.TimeSpan, SensorKind.Timing,
        SensorKind.Noise, SensorKind.Humidity, SensorKind.Conductivity, SensorKind.Other,
    ];

    private readonly List<string> _selected = [];
    private readonly HashSet<SensorCategory> _categories = [];
    private SensorKind? _kind;
    private string _chipsLayout = "";
    private readonly Dictionary<string, (TimeChart Chart, StackPanel Legend)> _charts = new(StringComparer.Ordinal);
    private IReadOnlyList<TimelineSensor> _sensors = [];
    private TimelineData? _file; // the log being viewed; null = live
    private string _chartsLayout = "";
    private bool _loading = true;
    private DateTime _lastRefresh;

    public ChartsPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = "// HWSUITE · " + s["charts.tag"].ToUpper(c);
        PageTitle.Text = s["charts.title"].ToUpper(c);
        PickerTag.Text = ("// " + s["charts.sensors"]).ToUpper(c);
        CategoryLabel.Text = s["sensors.components"].ToUpper(c);
        KindLabel.Text = s["sensors.kinds"].ToUpper(c);
        LogFolderButton.Content = s["charts.logFolder"];
        SearchBox.PlaceholderText = s["charts.search"];
        ClearButton.Content = s["charts.clear"];
        FolderButton.Content = s["results.openFolder"];
        RecordButton.Content = s["charts.record"];
        EmptyText.Text = s["charts.empty"];
        foreach (var minutes in WindowMinutes)
        {
            WindowBox.Items.Add(minutes == 0 ? s["charts.window.all"] : s.Format("charts.window", minutes));
        }

        var settings = App.Settings.Current;
        WindowBox.SelectedIndex = Math.Max(0, Array.IndexOf(WindowMinutes, settings.ChartMinutes));
        _selected.AddRange((settings.ChartSensors ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxSensors));
        FillSources();
        _loading = false;

        Loaded += (_, _) =>
        {
            App.Monitor.Updated += Monitor_Updated;
            ShowRecording();
            Refresh(rebuildList: true);
        };
        Unloaded += (_, _) => App.Monitor.Updated -= Monitor_Updated;
    }

    private static string LogFolder => App.Settings.LogFolder;

    // ---- Sources -------------------------------------------------------------------------------------------------

    /// <summary>"Live" plus the CSV logs in the log folder, newest first.</summary>
    private void FillSources()
    {
        var s = App.Text;
        var keep = SourceBox.SelectedItem as ComboBoxItem;
        _loading = true;
        SourceBox.Items.Clear();
        SourceBox.Items.Add(new ComboBoxItem { Content = s["charts.live"], Tag = null });
        try
        {
            if (Directory.Exists(LogFolder))
            {
                foreach (var file in new DirectoryInfo(LogFolder).GetFiles("*.csv").OrderByDescending(f => f.LastWriteTime).Take(60))
                {
                    var size = Core.Reporting.ReportModel.Bytes(file.Length, s.Culture);
                    SourceBox.Items.Add(new ComboBoxItem { Content = $"{file.Name}  ·  {size}", Tag = file.FullName });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an unreadable folder just shows no logs
        }

        var again = SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => keep is not null && Equals(i.Tag, keep.Tag));
        SourceBox.SelectedItem = again ?? SourceBox.Items[0];
        _loading = false;
    }

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _file = null;
        if ((SourceBox.SelectedItem as ComboBoxItem)?.Tag is string path)
        {
            try
            {
                _file = SensorLogReader.Read(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                RecordText.Text = App.Text.Format("log.cannotRead", System.IO.Path.GetFileName(path), ex.Message);
            }
        }

        Refresh(rebuildList: true);
    }

    private void WindowBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || WindowBox.SelectedIndex < 0)
        {
            return;
        }

        var minutes = WindowMinutes[WindowBox.SelectedIndex];
        if (!App.IsDemo)
        {
            App.Settings.Update(x => x with { ChartMinutes = minutes });
        }

        Refresh(rebuildList: false);
    }

    private TimeSpan? Window => WindowBox.SelectedIndex is >= 0 and var i && WindowMinutes[i] > 0 ? TimeSpan.FromMinutes(WindowMinutes[i]) : null;

    // ---- Recording -----------------------------------------------------------------------------------------------

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        var on = RecordButton.IsChecked == true;
        App.Settings.Update(x => x with { LogEnabled = on });
        ShowRecording();
        FillSources();
    }

    private void ShowRecording()
    {
        var s = App.Text;
        var on = App.Settings.Current.LogEnabled;
        RecordButton.IsChecked = on;
        RecordButton.Content = on ? s["charts.recording"] : s["charts.record"];
        RecordText.Text = on && App.Monitor.LogPath is { } path ? s.Format("charts.recordingTo", path) : s.Format("charts.recordHint", LogFolder);
    }

    /// <summary>Changes where logs are recorded (the same setting as in Settings › Sensor log).</summary>
    private void LogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Services.ShellDialogs.PickFolder(App.Text["charts.logFolder"], LogFolder) is { } folder)
        {
            var stored = Core.Settings.DataLocation.StorePath(folder);
            App.Settings.Update(x => x with { LogFolder = stored });
            ShowRecording();
            FillSources();
        }
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogFolder}\"") { UseShellExecute = true })?.Dispose();
    }

    // ---- Sensor picker -------------------------------------------------------------------------------------------

    private void Monitor_Updated(object? sender, SensorSnapshot e)
    {
        if (_file is not null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (DateTime.UtcNow - _lastRefresh >= TimeSpan.FromMilliseconds(900))
            {
                Refresh(rebuildList: _chipsLayout != string.Join('|', App.Monitor.Timeline.Sensors().Count, string.Join(',', _selected)));
            }
        });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => BuildList();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _selected.Clear();
        SelectionChanged();
    }

    private static SensorKind ChipKind(SensorKind kind) => kind == SensorKind.SmallData ? SensorKind.Data : kind;

    private bool Shown(TimelineSensor sensor) => _categories.Count == 0 || _categories.Contains(sensor.Category);

    private static ToggleButton Chip(string label, bool on) => new()
    {
        Content = label,
        IsChecked = on,
        FontFamily = HudUi.Font("HudMonoFont"),
        FontSize = 12,
        CharacterSpacing = 60,
        Padding = new Thickness(12, 6, 12, 6),
    };

    /// <summary>Component and reading-type chips (only those the sensors have), then the sensor chips that match.</summary>
    private void BuildList()
    {
        var s = App.Text;
        var c = s.Culture;

        // Components present.
        var categories = _sensors.Select(x => x.Category).Distinct().Order().ToList();
        _categories.IntersectWith(categories);
        CategoryFilters.Children.Clear();
        var all = Chip(s["sensors.filter.all"].ToUpper(c), _categories.Count == 0);
        all.Click += (_, _) =>
        {
            _categories.Clear();
            BuildList();
        };
        CategoryFilters.Children.Add(all);
        foreach (var category in categories)
        {
            var chip = Chip(s[$"category.{category}"].ToUpper(c), _categories.Contains(category));
            chip.Click += (_, _) =>
            {
                if (!_categories.Remove(category))
                {
                    _categories.Add(category);
                }

                BuildList();
            };
            CategoryFilters.Children.Add(chip);
        }

        // Reading types of the chosen components.
        var kinds = _sensors.Where(Shown).Select(x => ChipKind(x.Kind)).ToHashSet();
        if (_kind is { } k && !kinds.Contains(k))
        {
            _kind = null;
        }

        KindFilters.Children.Clear();
        var allKinds = Chip(s["sensors.filter.all"].ToUpper(c), _kind is null);
        allKinds.Click += (_, _) =>
        {
            _kind = null;
            BuildList();
        };
        KindFilters.Children.Add(allKinds);
        foreach (var kind in KindOrder.Where(kinds.Contains))
        {
            var chip = Chip(s[$"sensorKind.{kind}"].ToUpper(c), _kind == kind);
            chip.Click += (_, _) =>
            {
                _kind = kind;
                BuildList();
            };
            KindFilters.Children.Add(chip);
        }

        // Sensor chips: the selected ones first, with their line color.
        var search = SearchBox.Text.Trim();
        var matching = _sensors
            .Where(x => Shown(x) && (_kind is null || ChipKind(x.Kind) == _kind)
                && (search.Length == 0 || x.Key.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => _selected.Contains(x.Key) ? _selected.IndexOf(x.Key) : int.MaxValue)
            .ToList();
        SensorChips.Children.Clear();
        var full = _selected.Count >= MaxSensors;
        foreach (var sensor in matching.Take(MaxChips))
        {
            var on = _selected.Contains(sensor.Key);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (on)
            {
                content.Children.Add(new Rectangle { Width = 10, Height = 10, RadiusX = 5, RadiusY = 5, Fill = new SolidColorBrush(ColorOf(sensor.Key)), VerticalAlignment = VerticalAlignment.Center });
            }

            content.Children.Add(new TextBlock { Text = sensor.Name, FontSize = 13 });
            content.Children.Add(new TextBlock { Text = sensor.Unit, FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }); // chip's own text color: readable on and off
            var chip = new ToggleButton { Content = content, IsChecked = on, IsEnabled = on || !full, Padding = new Thickness(10, 5, 10, 5) };
            ToolTipService.SetToolTip(chip, sensor.Key);
            chip.Click += (_, _) =>
            {
                if (!_selected.Remove(sensor.Key) && _selected.Count < MaxSensors)
                {
                    _selected.Add(sensor.Key);
                }

                SelectionChanged();
            };
            SensorChips.Children.Add(chip);
        }

        MoreText.Text = s.Format("charts.more", matching.Count - MaxChips);
        MoreText.Visibility = matching.Count > MaxChips ? Visibility.Visible : Visibility.Collapsed;
        SelectedText.Text = s.Format("charts.selected", _selected.Count, MaxSensors);
        _chipsLayout = string.Join('|', _sensors.Count, string.Join(',', _selected));
    }

    private void SelectionChanged()
    {
        if (!App.IsDemo)
        {
            App.Settings.Update(x => x with { ChartSensors = _selected.Count == 0 ? "" : string.Join('\n', _selected) });
        }

        BuildList();
        Refresh(rebuildList: false);
    }

    /// <summary>First visit: the processor and graphics card temperatures and the first fans.</summary>
    private void PickDefaults()
    {
        if (App.Settings.Current.ChartSensors is not null || _selected.Count > 0 || App.Monitor.Latest is not { } latest)
        {
            return;
        }

        var d = Dashboard.From(latest);
        var ids = new[] { d.Cpu.TemperatureId, d.Gpu.TemperatureId }.Concat(d.Fans.Take(2).Select(f => f.Id)).OfType<string>().ToHashSet();
        _selected.AddRange(latest.Readings.Where(r => ids.Contains(r.Id)).Select(TimelineSensor.KeyOf).Distinct());
    }

    // ---- Charts --------------------------------------------------------------------------------------------------

    private void Refresh(bool rebuildList)
    {
        _lastRefresh = DateTime.UtcNow;
        if (_file is null)
        {
            _sensors = App.Monitor.Timeline.Sensors();
            PickDefaults();
        }
        else
        {
            _sensors = [.. _file.Series.Where(x => SeriesStats.Of(x.Values) is not null).Select(x => x.Sensor)];
        }

        if (rebuildList)
        {
            BuildList();
        }

        var data = (_file ?? App.Monitor.Timeline.Snapshot(_selected)).Select(_selected).Last(Window);
        var groups = data.Series.GroupBy(x => (x.Sensor.Kind, x.Sensor.Unit)).ToList();
        var layout = string.Join('|', groups.Select(g => $"{g.Key.Kind}:{string.Join(',', g.Select(x => x.Sensor.Key))}"));
        if (layout != _chartsLayout)
        {
            BuildCharts(groups.Select(g => (g.Key.Kind, g.Key.Unit)).ToList());
            _chartsLayout = layout;
        }

        EmptyText.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var c = App.Text.Culture;
        foreach (var group in groups)
        {
            var (chart, legend) = _charts[$"{group.Key.Kind}:{group.Key.Unit}"];
            var lines = group.Select(x => new TimeSeries(Short(x.Sensor), x.Values, new SolidColorBrush(ColorOf(x.Sensor.Key)), x.Sensor.Kind)).ToList();
            chart.Set(data.Times, lines);
            legend.Children.Clear();
            foreach (var x in group)
            {
                legend.Children.Add(LegendRow(x, c));
            }
        }
    }

    private void BuildCharts(IReadOnlyList<(SensorKind Kind, string Unit)> groups)
    {
        var s = App.Text;
        ChartsHost.Children.Clear();
        _charts.Clear();
        foreach (var (kind, unit) in groups)
        {
            var chart = new TimeChart { Height = 240, FromZero = kind is SensorKind.Fan or SensorKind.Load or SensorKind.Power or SensorKind.Control };
            var legend = new StackPanel { Spacing = 0 };
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(chart);
            content.Children.Add(legend);
            var title = s[$"sensorKind.{kind}"] + (unit.Length > 0 ? $" · {unit}" : "");
            ChartsHost.Children.Add(HudUi.Panel(title, content, s.Culture));
            _charts[$"{kind}:{unit}"] = (chart, legend);
        }

        ChartsHost.Reflow();
    }

    private Grid LegendRow(TimelineSeries series, System.Globalization.CultureInfo c)
    {
        var s = App.Text;
        var stats = SeriesStats.Of(series.Values);
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 5, 0, 5), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 1, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var _ in Enumerable.Range(0, 4))
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        }

        row.Children.Add(new Rectangle { Width = 12, Height = 3, Fill = new SolidColorBrush(ColorOf(series.Sensor.Key)), VerticalAlignment = VerticalAlignment.Center });
        // Sensor name first (what tells the lines apart), the device under it.
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = series.Sensor.Name, FontSize = 13, Foreground = HudUi.Text, TextTrimming = TextTrimming.CharacterEllipsis });
        name.Children.Add(new TextBlock { Text = series.Sensor.Hardware.Split('›').Last().Trim(), FontSize = 11, Foreground = HudUi.Tag, TextTrimming = TextTrimming.CharacterEllipsis });
        ToolTipService.SetToolTip(name, series.Sensor.Key);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        string[] labels = [s["log.min"], s["log.avg"], s["log.max"], s["charts.now"]];
        double?[] values = [stats?.Min, stats?.Average, stats?.Max, stats?.Last];
        for (var i = 0; i < 4; i++)
        {
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            cell.Children.Add(new TextBlock { Text = labels[i].ToUpper(c), FontSize = 10, Foreground = HudUi.Tag, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = HudUi.Font("HudMonoFont") });
            cell.Children.Add(new TextBlock { Text = SensorFormat.Format(values[i], series.Sensor.Kind, c), FontSize = 13, Foreground = i == 2 ? HudUi.Text : HudUi.TextDim, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = HudUi.Font("HudMonoStrongFont") });
            Grid.SetColumn(cell, 2 + i);
            row.Children.Add(cell);
        }

        return row;
    }

    /// <summary>"Device · sensor", the device shortened to its last part (the chip, not the whole board name).</summary>
    private static string Short(TimelineSensor sensor)
    {
        var device = sensor.Hardware.Split('›').Last().Trim();
        return device.Length == 0 ? sensor.Name : $"{device} · {sensor.Name}";
    }

    private Color ColorOf(string key)
    {
        var index = _selected.IndexOf(key);
        return Palette[(index < 0 ? 0 : index) % Palette.Length];
    }
}
