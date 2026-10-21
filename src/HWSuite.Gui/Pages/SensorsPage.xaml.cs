using System.Collections.ObjectModel;
using HWSuite.Core.Sensors;
using HWSuite.Gui.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace HWSuite.Gui.Pages;

/// <summary>Live sensors grouped by device, with a 5-minute chart of the selected sensor.</summary>
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
        HeaderName.Text = s["sensors.sensor"];
        HeaderKind.Text = s["app.columnKind"];
        HeaderValue.Text = s["sensors.value"];
        HeaderMin.Text = s["sensors.min"];
        HeaderMax.Text = s["sensors.max"];
        ChartTitle.Text = s["app.chartHint"];
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

    private void OnUpdated(object? sender, SensorSnapshot snapshot) => DispatcherQueue.TryEnqueue(() => Apply(snapshot));

    private void OnFailed(object? sender, Exception error) =>
        DispatcherQueue.TryEnqueue(() => StatusText.Text = App.Text.Format("app.sensorsFailed", error.Message));

    private void Apply(SensorSnapshot snapshot)
    {
        var s = App.Text;
        var c = s.Culture;
        var shown = 0;

        foreach (var r in snapshot.Readings)
        {
            if (r.Value is null && r.Min is null && r.Max is null)
            {
                continue;
            }

            if (!_items.TryGetValue(r.Id, out var item))
            {
                item = new SensorItem(r.Id, r.Name, r.Kind, s[$"sensorKind.{r.Kind}"]);
                _items[r.Id] = item;
                GroupFor(r).Add(item);
            }

            item.ValueText = SensorFormat.Format(r.Value, r.Kind, c);
            item.MinText = SensorFormat.Format(r.Min, r.Kind, c);
            item.MaxText = SensorFormat.Format(r.Max, r.Kind, c);
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
        DrawChart();
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
