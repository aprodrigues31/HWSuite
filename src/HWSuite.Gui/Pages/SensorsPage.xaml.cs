using System.Collections.ObjectModel;
using HWSuite.Core.Alerts;
using HWSuite.Core.Sensors;
using HWSuite.Gui.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace HWSuite.Gui.Pages;

/// <summary>Live sensors grouped by device, with a chart of the selected sensor's recent history.</summary>
public sealed partial class SensorsPage : Page
{
    private readonly ObservableCollection<SensorGroup> _groups = [];
    private readonly Dictionary<string, SensorItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SensorGroup> _groupsByTitle = new(StringComparer.Ordinal);
    private SensorItem? _selected;

    public SensorsPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["app.nav.sensors"];
        StatusText.Text = s["app.sensorsStarting"];
        ChartTitle.Text = s["app.chartHint"];
        AlertButtonText.Text = s["app.createAlert"];
        GroupedSensors.Source = _groups;
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

    private void Apply(SensorSnapshot snapshot)
    {
        var s = App.Text;
        var c = s.Culture;
        var shown = 0;
        IReadOnlyList<AlertRule> rules = App.Settings.Current.AlertsEnabled ? App.Settings.Rules : [];
        var normal = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        foreach (var r in snapshot.Readings)
        {
            if (r.Value is null && r.Min is null && r.Max is null)
            {
                continue;
            }

            if (!_items.TryGetValue(r.Id, out var item))
            {
                item = new SensorItem(r, s[$"sensorKind.{r.Kind}"]);
                _items[r.Id] = item;
                GroupFor(r).Add(item);
            }

            item.ValueText = SensorFormat.Format(r.Value, r.Kind, c);
            item.MinText = SensorFormat.Format(r.Min, r.Kind, c);
            item.MaxText = SensorFormat.Format(r.Max, r.Kind, c);
            item.Reading = r;
            item.ValueBrush = AlertEvaluator.IsBeyond(r, rules) ? critical : normal;
            shown++;
        }

        StatusText.Text = s.Format("app.lastUpdate", shown, snapshot.Timestamp.ToString("T", c));

        var access = App.Monitor.Access;
        var blocked = access.IsElevated && access.PawnIoInstalled && SensorDiagnostics.CpuLowLevelReadsBlocked(snapshot);
        if (blocked != HintBar.IsOpen)
        {
            HintBar.Message = s["sensors.lowLevelZero"];
            HintBar.IsOpen = blocked;
        }

        DrawChart();
    }

    private SensorGroup GroupFor(SensorReading r)
    {
        var title = (r.Parent is null ? r.Hardware : $"{r.Parent} › {r.Hardware}") + $"  ·  {r.HardwareKind}";
        if (!_groupsByTitle.TryGetValue(title, out var group))
        {
            group = new SensorGroup(title);
            _groupsByTitle[title] = group;
            _groups.Add(group);
        }

        return group;
    }

    private void SensorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = SensorList.SelectedItem as SensorItem;
        AlertButton.IsEnabled = _selected is not null;
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

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Line chart of the selected sensor's history, scaled to its own min/max.</summary>
    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        var s = App.Text;
        if (_selected is null)
        {
            ChartTitle.Text = s["app.chartHint"];
            return;
        }

        var values = App.Monitor.History.Get(_selected.Id);
        var finite = values.Where(double.IsFinite).ToList();
        ChartTitle.Text = s.Format("app.chartTitle", _selected.Name, _selected.ValueText,
            SensorFormat.Format(finite.Count > 0 ? finite.Min() : null, _selected.Kind, s.Culture),
            SensorFormat.Format(finite.Count > 0 ? finite.Max() : null, _selected.Kind, s.Culture));

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (finite.Count < 2 || width < 10 || height < 10)
        {
            return;
        }

        var low = finite.Min();
        var high = finite.Max();
        if (high - low < 1e-9)
        {
            low -= 1;
            high += 1;
        }

        var padding = (high - low) * 0.1;
        low -= padding;
        high += padding;

        var capacity = App.Monitor.History.Capacity;
        var step = width / (capacity - 1);
        var offset = capacity - values.Count; // newest sample sits at the right edge

        var grid = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        for (var i = 0; i <= 4; i++)
        {
            var y = height * i / 4;
            ChartCanvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
        }

        var line = new Polyline
        {
            Stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        };

        for (var i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                continue;
            }

            var x = (offset + i) * step;
            var y = height - ((values[i] - low) / (high - low) * height);
            line.Points.Add(new Point(x, y));
        }

        ChartCanvas.Children.Add(line);
    }
}
