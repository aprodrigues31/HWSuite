using HWSuite.Core.Benchmark;
using HWSuite.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HWSuite.Gui.Pages;

/// <summary>Processor benchmark (one thread, then all threads), with the saved runs for comparison.</summary>
public sealed partial class BenchmarkPage : Page
{
    private static readonly int[] Durations = [5, 10, 30];
    private static string? s_processor;
    private CancellationTokenSource? _running;

    public BenchmarkPage()
    {
        InitializeComponent();
        var s = App.Text;
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
        Loaded += async (_, _) =>
        {
            s_processor ??= await Task.Run(ProviderCatalog.ProcessorName);
            ProcessorText.Text = $"{s_processor} · {CpuBenchmark.Version}";
            ShowHistory();
        };
        Unloaded += (_, _) => _running?.Cancel();
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
