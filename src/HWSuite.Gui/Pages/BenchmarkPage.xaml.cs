using HWSuite.Core.Benchmark;
using HWSuite.Core.Sensors;
using HWSuite.Hosting;
using HWSuite.Providers.Windows;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Processor benchmark (one thread, then all threads) with the saved runs for comparison, and the stress
/// test (all threads until stopped, every result checked). The page stays cached, so a running test keeps
/// going and keeps its display while the user looks at the Sensors page.
/// </summary>
public sealed partial class BenchmarkPage : Page
{
    private static readonly int[] Durations = [5, 10, 30];
    private static string? s_processor;
    private CancellationTokenSource? _running;
    private CancellationTokenSource? _stress;
    private double? _maxTemperature;
    private readonly Dictionary<string, (double Now, double Min, double Max)> _rails = [];

    public BenchmarkPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        var s = App.Text;
        StressButton.Content = s["app.stress.start"];
        ElapsedLabel.Text = s["app.stress.elapsed"];
        RateLabel.Text = s["app.stress.rate"];
        ErrorsLabel.Text = s["app.stress.errors"];
        TempLabel.Text = s["app.stress.temperature"];
        StressExplain.Text = s["app.stress.explain"];
        PageTitle.Text = s["app.nav.bench"];
        StartButton.Content = s["app.bench.start"];
        SingleLabel.Text = s["app.bench.single"];
        MultiLabel.Text = s.Format("app.bench.multi", Environment.ProcessorCount);
        NoteText.Text = s["bench.note"];
        HistoryTitle.Text = s["app.bench.history"];
        foreach (var seconds in Durations)
        {
            DurationBox.Items.Add($"{seconds} s");
        }

        DurationBox.SelectedIndex = 1;
        CpuHeader.Text = s["app.bench.cpuSection"];
        InitializeComparison();
        InitializeGpu();
        Loaded += async (_, _) =>
        {
            s_processor ??= await Task.Run(ProviderCatalog.ProcessorName);
            ProcessorText.Text = $"{s_processor} · {CpuBenchmark.Version}";
            ShowHistory();
            ShowComparison(_lastResult);
            await LoadGpuAsync();
        };
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (_running is not null)
        {
            await _running.CancelAsync();
            return;
        }

        var seconds = Durations[Math.Max(0, DurationBox.SelectedIndex)];
        using var cancel = new CancellationTokenSource();
        _running = cancel;
        StartButton.Content = s["app.bench.cancel"];
        DurationBox.IsEnabled = false;
        StressButton.IsEnabled = false;
        SingleValue.Text = MultiValue.Text = "—";
        ScalingText.Text = "";

        // Created on the UI thread, so reports arrive on the UI thread.
        var progress = new Progress<(double Fraction, string Phase)>(p =>
        {
            Progress.Value = p.Fraction;
            StatusText.Text = p.Phase == "done" ? "" : s[$"bench.phase.{p.Phase}"] + "...";
        });

        try
        {
            var result = await Task.Run(() => CpuBenchmark.Run(TimeSpan.FromSeconds(seconds), progress, cancel: cancel.Token));
            SingleValue.Text = CpuBenchmark.Points(result.SingleThread, s.Culture);
            MultiValue.Text = CpuBenchmark.Points(result.MultiThread, s.Culture);
            ScalingText.Text = s.Format("app.bench.scaling", result.Scaling.ToString("0.0", s.Culture));
            var path = HistoryPath();
            _lastResult = result;
            ShowComparison(result);
            BenchmarkHistory.Append(path, result, s_processor);
            StatusText.Text = s.Format("bench.saved", path);
            ShowHistory();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = s["bench.cancelled"];
            Progress.Value = 0;
        }
        catch (IOException ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _running = null;
            StartButton.Content = s["app.bench.start"];
            DurationBox.IsEnabled = true;
            StressButton.IsEnabled = true;
        }
    }

    private async void StressButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (_stress is not null)
        {
            await _stress.CancelAsync();
            return;
        }

        using var cancel = new CancellationTokenSource();
        _stress = cancel;
        _maxTemperature = null;
        _rails.Clear();
        RailsText.Text = "";
        StressButton.Content = s["app.stress.stop"];
        StartButton.IsEnabled = DurationBox.IsEnabled = false;
        StressPanel.Visibility = Visibility.Visible;
        StressStatusText.Text = s.Format("app.stress.running", Environment.ProcessorCount);
        ErrorsValue.ClearValue(TextBlock.ForegroundProperty);
        ErrorsValue.Text = "0";
        RateValue.Text = "—";
        Progress.IsIndeterminate = true;
        StatusText.Text = "";

        var ui = DispatcherQueue;
        KeepAwake.Start(); // UI thread: the same thread clears it below
        try
        {
            var final = await Task.Run(() => CpuStressTest.Run(st => ui.TryEnqueue(() => ShowStress(st)), cancel: cancel.Token));
            ShowStress(final);
            StressStatusText.Text = s[final.Errors == 0 ? "stress.passed" : "stress.failed"]
                + (PowerRails.AllWithin(_rails) ? "" : " " + s["app.psu.stressBad"]);
        }
        finally
        {
            KeepAwake.Stop();
            _stress = null;
            Progress.IsIndeterminate = false;
            StressButton.Content = s["app.stress.start"];
            StartButton.IsEnabled = DurationBox.IsEnabled = true;
        }
    }

    private void ShowStress(StressStatus st)
    {
        var s = App.Text;
        ElapsedValue.Text = $"{(int)st.Elapsed.TotalHours}:{st.Elapsed.Minutes:00}:{st.Elapsed.Seconds:00}";
        RateValue.Text = CpuBenchmark.Points(st.PointsPerSecond, s.Culture);
        ErrorsValue.Text = st.Errors.ToString("N0", s.Culture)
            + (st.FirstErrorAt is { } first ? " (" + s.Format("app.stress.firstError", $"{(int)first.TotalHours}:{first.Minutes:00}:{first.Seconds:00}") + ")" : "");
        if (st.Errors > 0)
        {
            ErrorsValue.Foreground = new SolidColorBrush(Colors.Red);
        }

        if (App.Monitor.Latest is { } snapshot && SensorSummary.CpuTemperature(snapshot) is { } temperature)
        {
            _maxTemperature = Math.Max(_maxTemperature ?? temperature, temperature);
            TempValue.Text = string.Create(s.Culture, $"{temperature:0} °C ({_maxTemperature:0} °C)");
        }

        if (App.Monitor.Latest is { } latest)
        {
            PowerRails.Track(_rails, latest);
            RailsText.Text = _rails.Count > 0 ? s["app.psu.stressRails"] + " " + PowerRails.Summary(_rails, s) : "";
        }
    }

    private void ShowHistory()
    {
        var s = App.Text;
        HistoryList.Children.Clear();
        IReadOnlyList<BenchmarkEntry> entries;
        try
        {
            entries = BenchmarkHistory.Read(HistoryPath());
        }
        catch (IOException)
        {
            entries = [];
        }

        foreach (var entry in entries.Take(10))
        {
            HistoryList.Children.Add(new TextBlock
            {
                Text = $"{entry.At.LocalDateTime.ToString("g", s.Culture)} · {s["app.bench.single"]}: {CpuBenchmark.Points(entry.SingleThread, s.Culture)}"
                    + $" · {s.Format("app.bench.multi", entry.Threads)}: {CpuBenchmark.Points(entry.MultiThread, s.Culture)} · {entry.Processor} · {entry.Version}",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
        }

        HistoryTitle.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string HistoryPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite", "benchmark.csv");
}
