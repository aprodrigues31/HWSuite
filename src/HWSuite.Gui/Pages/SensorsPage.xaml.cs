using HWSuite.Core.Alerts;
using HWSuite.Core.Sensors;
using HWSuite.Gui.Hud;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Live sensors: a summary strip (hottest CPU and GPU, total power, pump, how many need attention), filters by
/// kind and a search, then one panel per device with each channel's range bar, value, minimum and maximum.
/// Clicking a channel shows its recent history below and lets an alert be created for it.
/// </summary>
public sealed partial class SensorsPage : Page
{
    private static readonly SensorKind?[] FilterKinds = [null, SensorKind.Temperature, SensorKind.Voltage, SensorKind.Fan, SensorKind.Power, SensorKind.Clock, SensorKind.Load];
    private const double WarnTemperature = 80;

    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    private readonly List<ToggleButton> _filterButtons = [];
    private readonly Dictionary<string, (TextBlock Value, TextBlock Note)> _summary = [];
    private int _rowsA;
    private int _rowsB;
    private readonly HashSet<SensorCategory> _categories = [.. SensorCategories.Parse(App.Settings.Current.SensorCategories)];
    private readonly SortedDictionary<SensorCategory, ToggleButton> _categoryButtons = [];
    private ToggleButton? _allCategories;
    private SensorKind? _filter;
    private string _search = "";
    private Row? _selected;

    public SensorsPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["app.nav.sensors"].ToUpper(s.Culture);
        HeaderTag.Text = "// HWSUITE · " + s["rail.sensors"].ToUpper(s.Culture);
        StatusText.Text = s["app.sensorsStarting"];
        SearchBox.PlaceholderText = s["sensors.search"];
        ChartTitle.Text = s["app.chartHint"].ToUpper(s.Culture);
        AlertButtonText.Text = s["app.createAlert"];
        CategoryLabel.Text = s["sensors.components"].ToUpper(s.Culture);
        KindLabel.Text = s["sensors.kinds"].ToUpper(s.Culture);
        BuildSummary();
        BuildCategoryFilters();
        BuildFilters();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Monitor.Updated += OnUpdated;
        App.Monitor.Failed += OnFailed;
        if (App.Monitor.Latest is { } latest)
        {
            Apply(latest);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.Monitor.Updated -= OnUpdated;
        App.Monitor.Failed -= OnFailed;
        base.OnNavigatedFrom(e);
    }

    private void OnUpdated(object? sender, SensorSnapshot snapshot)
    {
        if (App.MainWindow is { IsShown: false })
        {
            return; // hidden in the tray: nothing to draw; the next update after showing catches up
        }

        DispatcherQueue.TryEnqueue(() => Apply(snapshot));
    }

    private void OnFailed(object? sender, Exception error) =>
        DispatcherQueue.TryEnqueue(() => StatusText.Text = App.Text.Format("app.sensorsFailed", error.Message));

    // ---- Summary and filters -----------------------------------------------------------------------------------

    private void BuildSummary()
    {
        var s = App.Text;
        string[] keys = ["cpu", "gpu", "power", "fan", "attention"];
        for (var i = 0; i < keys.Length; i++)
        {
            SummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(HudUi.TagText(s[$"sum.{keys[i]}"], s.Culture));
            var value = new TextBlock { Text = "—", Style = HudUi.Style("HudValueStyle"), FontSize = 24, Foreground = HudUi.Text };
            var note = new TextBlock { Style = HudUi.Style("HudMonoStyle"), FontSize = 12, Foreground = HudUi.Tag, TextTrimming = TextTrimming.CharacterEllipsis };
            stack.Children.Add(value);
            stack.Children.Add(note);
            var panel = new HudPanel { Content = stack, Padding = new Thickness(16, 12, 16, 12), Chamfer = 10 };
            Grid.SetColumn(panel, i);
            SummaryGrid.Children.Add(panel);
            _summary[keys[i]] = (value, note);
        }
    }

    /// <summary>"All" plus one button per component present (added as devices appear); several can be on.</summary>
    private void BuildCategoryFilters()
    {
        var s = App.Text;
        _allCategories = Chip(s["sensors.filter.all"]);
        _allCategories.Click += (_, _) =>
        {
            _categories.Clear();
            CategoryChanged();
        };
        CategoryFilters.Children.Add(_allCategories);
        RefreshCategoryChips();
    }

    private ToggleButton CategoryChip(SensorCategory category)
    {
        if (_categoryButtons.TryGetValue(category, out var existing))
        {
            return existing;
        }

        var button = Chip(App.Text[$"category.{category}"]);
        button.Click += (_, _) =>
        {
            if (!_categories.Remove(category))
            {
                _categories.Add(category);
            }

            CategoryChanged();
        };
        _categoryButtons[category] = button;

        // Keep the buttons in the fixed component order after "All".
        CategoryFilters.Children.Insert(1 + _categoryButtons.Keys.ToList().IndexOf(category), button);
        RefreshCategoryChips();
        return button;
    }

    private void CategoryChanged()
    {
        // Everything picked is the same as "All".
        if (_categoryButtons.Count > 0 && _categoryButtons.Keys.All(_categories.Contains))
        {
            _categories.Clear();
        }

        RefreshCategoryChips();
        var saved = SensorCategories.Format(_categories);
        App.Settings.Update(x => x with { SensorCategories = saved });
        ApplyFilter();
    }

    private void RefreshCategoryChips()
    {
        var c = App.Text.Culture;
        if (_allCategories is not null)
        {
            _allCategories.IsChecked = _categories.Count == 0;
        }

        foreach (var (category, button) in _categoryButtons)
        {
            var devices = _devices.Values.Count(d => d.Category == category);
            button.Content = (App.Text[$"category.{category}"] + (devices > 1 ? $" · {devices}" : "")).ToUpper(c);
            button.IsChecked = _categories.Contains(category);
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

    private void BuildFilters()
    {
        var s = App.Text;
        foreach (var kind in FilterKinds)
        {
            var label = kind is null ? s["sensors.filter.all"] : s[$"sensorKind.{kind}"];
            var button = new ToggleButton
            {
                Content = label.ToUpper(s.Culture),
                FontFamily = HudUi.Font("HudMonoFont"),
                FontSize = 12,
                CharacterSpacing = 120,
                Padding = new Thickness(14, 7, 14, 7),
                IsChecked = kind is null,
            };
            button.Click += (_, _) =>
            {
                _filter = kind;
                foreach (var other in _filterButtons)
                {
                    other.IsChecked = ReferenceEquals(other, button);
                }

                ApplyFilter();
            };
            _filterButtons.Add(button);
            Filters.Children.Add(button);
        }
    }

    /// <summary>Two columns of device panels when there is room for every column of a row; one column otherwise.</summary>
    private void DeviceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 1060;
        SecondColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DeviceGrid.ColumnSpacing = wide ? 16 : 0;
        Grid.SetColumn(ColumnB, wide ? 1 : 0);
        Grid.SetRow(ColumnB, wide ? 0 : 1);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private bool Shown(SensorCategory category) => _categories.Count == 0 || _categories.Contains(category);

    private bool Visible(Row row) =>
        Shown(row.Category)
        && (_filter is null || row.Reading.Kind == _filter)
        && (_search.Length == 0
            || row.Reading.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Reading.Hardware.Contains(_search, StringComparison.OrdinalIgnoreCase));

    private void ApplyFilter()
    {
        foreach (var row in _rows.Values)
        {
            row.Root.Visibility = Visible(row) ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var device in _devices.Values)
        {
            device.Panel.Visibility = Shown(device.Category) && device.Rows.Any(r => r.Root.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- Live values -------------------------------------------------------------------------------------------

    private void Apply(SensorSnapshot snapshot)
    {
        var s = App.Text;
        var c = s.Culture;
        IReadOnlyList<AlertRule> rules = App.Settings.Current.AlertsEnabled ? App.Settings.EffectiveRules : [];
        var shown = 0;
        var attention = new List<string>();
        var added = false;

        foreach (var r in snapshot.Readings)
        {
            if (r.Value is null && r.Min is null && r.Max is null)
            {
                continue;
            }

            if (!_rows.TryGetValue(r.Id, out var row))
            {
                row = AddRow(r);
                added = true;
            }

            row.Reading = r;
            var critical = AlertEvaluator.IsBeyond(r, rules);
            var warn = !critical && r.Kind == SensorKind.Temperature && r.Value >= WarnTemperature;
            var brush = critical ? HudUi.Crit : warn ? HudUi.Warn : null;
            row.Value.Text = SensorFormat.Format(r.Value, r.Kind, c);
            row.Value.Foreground = brush ?? HudUi.Text;
            row.Min.Text = SensorFormat.Format(r.Min, r.Kind, c);
            row.Max.Text = SensorFormat.Format(r.Max, r.Kind, c);
            row.Bar.Fill = brush;
            row.Bar.Value = SensorScale.Fraction(r);
            row.Dot.Fill = brush ?? HudUi.Accent;
            if (critical || warn)
            {
                attention.Add(r.Name);
            }

            shown++;
        }

        if (added)
        {
            ApplyFilter();
        }

        HeaderTag.Text = ("// HWSUITE · " + s.Format("sensors.channels", shown, _devices.Count)).ToUpper(c);
        StatusText.Text = s.Format("app.lastUpdate", shown, snapshot.Timestamp.ToString("T", c));
        ShowSummary(snapshot, attention);

        var access = App.Monitor.Access;
        var blocked = !App.IsDemo && access.IsElevated && access.PawnIoInstalled && SensorDiagnostics.CpuLowLevelReadsBlocked(snapshot);
        if (blocked != HintBar.IsOpen)
        {
            HintBar.Message = s["sensors.lowLevelZero"];
            HintBar.IsOpen = blocked;
        }

        DrawChart();
    }

    private void ShowSummary(SensorSnapshot snapshot, List<string> attention)
    {
        var s = App.Text;
        var c = s.Culture;
        var d = Dashboard.From(snapshot, App.Settings.Current.PumpSensorId);
        var cpuTemps = snapshot.Readings.Where(r => r.HardwareKind == "CPU" && r.Kind == SensorKind.Temperature).ToList();
        var gpuTemps = snapshot.Readings.Where(r => r.HardwareKind == "GPU" && r.Kind == SensorKind.Temperature).ToList();
        string Deg(double? v) => v is { } x ? $"{x.ToString("0.0", c)}°" : "—";

        Set("cpu", Deg(cpuTemps.Select(r => r.Max ?? r.Value).Max()), s.Format("sum.now", Deg(d.Cpu.Temperature)));
        Set("gpu", Deg(gpuTemps.Select(r => r.Max ?? r.Value).Max()), s.Format("sum.now", Deg(d.Gpu.HotSpot ?? d.Gpu.Temperature)));
        var total = (d.Cpu.PowerW ?? 0) + (d.Gpu.PowerW ?? 0);
        Set("power", total > 0 ? $"{total.ToString("0", c)} W" : "—",
            $"CPU {(d.Cpu.PowerW ?? 0).ToString("0", c)} · GPU {(d.Gpu.PowerW ?? 0).ToString("0", c)}");
        var fan = d.Fans.Count > 0 ? d.Fans[0] : null;
        Set("fan", fan is null ? "—" : $"{fan.Rpm.ToString("N0", c)}", fan is null ? "" : $"RPM · {fan.Name}");
        Set("attention", attention.Count.ToString(c), attention.Count == 0 ? s["sum.none"] : string.Join(" · ", attention.Take(3)),
            attention.Count > 0 ? HudUi.Warn : null);
    }

    private void Set(string key, string value, string note, Brush? color = null)
    {
        var (v, n) = _summary[key];
        v.Text = value;
        n.Text = note;
        v.Foreground = color ?? HudUi.Text;
    }

    // ---- Device panels -----------------------------------------------------------------------------------------

    private Row AddRow(SensorReading r)
    {
        var device = DeviceFor(r);
        var row = new Row(r) { Category = device.Category };
        var grid = Columns();
        grid.Padding = new Thickness(6, 6, 0, 6);
        grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        grid.BorderBrush = HudUi.Brush("HudAccentFaintBrush");
        grid.BorderThickness = new Thickness(0, 0, 0, 1);
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        name.Children.Add(row.Dot);
        name.Children.Add(new TextBlock { Text = r.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        Add(grid, name, 0);
        row.Bar.VerticalAlignment = VerticalAlignment.Center;
        Add(grid, row.Bar, 1);
        Add(grid, row.Value, 2);
        Add(grid, row.Min, 3);
        Add(grid, row.Max, 4);
        grid.Tapped += (_, _) => Select(row);
        row.Root = grid;
        device.Rows.Add(row);
        device.Body.Children.Add(grid);
        device.Count.Text = $"{device.Rows.Count} CH";
        if (ReferenceEquals(device.Column, ColumnA))
        {
            _rowsA++;
        }
        else
        {
            _rowsB++;
        }

        _rows[r.Id] = row;
        return row;
    }

    private Device DeviceFor(SensorReading r)
    {
        var key = (r.Parent is null ? r.Hardware : $"{r.Parent} › {r.Hardware}") + "|" + r.HardwareKind;
        if (_devices.TryGetValue(key, out var device))
        {
            return device;
        }

        var s = App.Text;
        var c = s.Culture;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = (r.Parent ?? r.Hardware).ToUpper(c),
            Style = HudUi.Style("HudNameStyle"),
            FontSize = 15,
            Margin = new Thickness(0, 0, 90, 0),
        });
        var count = new TextBlock { Style = HudUi.Style("HudTagStyle"), VerticalAlignment = VerticalAlignment.Center };
        var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 12, Foreground = HudUi.Tag, VerticalAlignment = VerticalAlignment.Center };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(count);
        right.Children.Add(chevron);
        header.Children.Add(right);
        header.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // clickable everywhere
        ToolTipService.SetToolTip(header, s["sensors.collapse"]);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock
        {
            Text = (r.Parent is null ? r.HardwareKind : $"{r.Hardware} · {r.HardwareKind}").ToUpper(c),
            Style = HudUi.Style("HudTagStyle"),
            Margin = new Thickness(0, -4, 0, 8),
        });
        var columns = Columns();
        columns.Padding = new Thickness(6, 4, 0, 6);
        columns.BorderBrush = HudUi.Brush("HudPanelStrokeBrush");
        columns.BorderThickness = new Thickness(0, 0, 0, 1);
        string[] titles = [s["sensors.colChannel"], s["sensors.colRange"], s["sensors.colNow"], s["sensors.min"], s["sensors.max"]];
        for (var i = 0; i < titles.Length; i++)
        {
            Add(columns, new TextBlock { Text = titles[i].ToUpper(c), Style = HudUi.Style("HudTagStyle"), FontSize = 11 }, i);
        }

        stack.Children.Add(columns);
        var body = new StackPanel();
        stack.Children.Add(body);

        // Each new device goes to the shorter column.
        var column = _rowsA <= _rowsB ? ColumnA : ColumnB;
        var panel = new HudPanel { Content = stack };
        column.Children.Add(panel);
        var category = SensorCategories.Of(r);
        device = new Device(panel, body, count, column, category);
        _devices[key] = device;

        // Clicking the title folds the panel to its header (and back).
        var folded = device;
        header.Tapped += (_, _) =>
        {
            folded.Collapsed = !folded.Collapsed;
            var shown = folded.Collapsed ? Visibility.Collapsed : Visibility.Visible;
            columns.Visibility = shown;
            body.Visibility = shown;
            chevron.Glyph = folded.Collapsed ? "\uE76C" : "\uE70D";
        };

        CategoryChip(category);
        RefreshCategoryChips();
        return device;
    }

    /// <summary>Channel | range | now | min | max, the same widths in every row of every panel.</summary>
    private static Grid Columns()
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        return grid;
    }

    private static void Add(Grid grid, FrameworkElement element, int column)
    {
        if (column >= 2 && element is TextBlock t)
        {
            t.HorizontalAlignment = HorizontalAlignment.Right;
        }

        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private void Select(Row row)
    {
        if (_selected is { } previous)
        {
            previous.Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        _selected = row;
        row.Root.Background = HudUi.Brush("HudSelectedBrush");
        AlertButton.IsEnabled = true;
        DrawChart();
    }

    /// <summary>
    /// Adds a rule for the selected sensor with a starting limit the user adjusts in Settings:
    /// fans alert below half their current speed, everything else above the current value plus a margin.
    /// </summary>
    private void AlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { Reading: var reading })
        {
            return;
        }

        var value = reading.Value is { } v && double.IsFinite(v) ? v : 0;
        var below = reading.Kind == SensorKind.Fan;
        var threshold = reading.Kind switch
        {
            SensorKind.Fan => Math.Round(value * 0.5),
            SensorKind.Temperature => Math.Ceiling(value + 10),
            _ => Math.Ceiling(Math.Abs(value) * 1.2 * 100) / 100,
        };

        var rule = new AlertRule
        {
            Label = $"{reading.Name} ({reading.Hardware})",
            Target = new AlertTarget { SensorId = reading.Id },
            Threshold = threshold,
            Direction = below ? AlertDirection.Below : AlertDirection.Above,
        };
        App.Settings.UpdateRules(rules => [.. rules.Where(r => r.Target.SensorId != reading.Id), rule]);

        var s = App.Text;
        AlertBar.Message = s.Format("app.alertCreated", rule.Label,
            s[below ? "settings.alertBelow" : "settings.alertAbove"],
            SensorFormat.Format(threshold, reading.Kind, s.Culture));

        AlertBar.IsOpen = true;
    }

    /// <summary>Line chart of the selected sensor's history.</summary>
    private void DrawChart()
    {
        var s = App.Text;
        if (_selected is null)
        {
            ChartTitle.Text = s["app.chartHint"].ToUpper(s.Culture);
            Chart.Visibility = Visibility.Collapsed; // the list gets the room until a channel is picked
            return;
        }

        var values = App.Monitor.History.Get(_selected.Reading.Id);
        var finite = values.Where(double.IsFinite).ToList();
        ChartTitle.Text = ("// " + s.Format("app.chartTitle", _selected.Reading.Name, _selected.Value.Text,
            SensorFormat.Format(finite.Count > 0 ? finite.Min() : null, _selected.Reading.Kind, s.Culture),
            SensorFormat.Format(finite.Count > 0 ? finite.Max() : null, _selected.Reading.Kind, s.Culture))).ToUpper(s.Culture);
        Chart.Visibility = Visibility.Visible;
        Chart.Capacity = App.Monitor.History.Capacity;
        Chart.Set([new ChartSeries(values, HudUi.Accent, Fill: true)]);
    }

    private sealed class Row(SensorReading reading)
    {
        public SensorReading Reading { get; set; } = reading;

        public SensorCategory Category { get; init; }

        public Grid Root { get; set; } = new();

        public Rectangle Dot { get; } = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };

        public SegmentBar Bar { get; } = new() { Height = 6 };

        public TextBlock Value { get; } = new() { FontFamily = HudUi.Font("HudMonoStrongFont"), FontSize = 13 };

        public TextBlock Min { get; } = new() { FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = HudUi.Tag };

        public TextBlock Max { get; } = new() { FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Foreground = HudUi.Tag };
    }

    private sealed record Device(HudPanel Panel, StackPanel Body, TextBlock Count, StackPanel Column, SensorCategory Category)
    {
        public List<Row> Rows { get; } = [];

        public bool Collapsed { get; set; }
    }
}
