using HWSuite.Core.Benchmark;
using HWSuite.Core.Sensors;
using HWSuite.Providers.Windows;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>The graphics card half of the Benchmark page: Direct3D 11 compute benchmark and stress test.</summary>
public sealed partial class BenchmarkPage
{
    private static IReadOnlyList<GpuAdapter>? s_adapters;
    private CancellationTokenSource? _gpuRunning;
    private CancellationTokenSource? _gpuStress;
    private double? _gpuMaxTemperature;
    private readonly Dictionary<string, (double Now, double Min, double Max)> _gpuRails = [];

    private GpuAdapter? Adapter => s_adapters is { Count: > 0 } list ? list[0] : null;

    private void InitializeGpu()
    {
        var s = App.Text;
        GpuHeader.Text = s["app.bench.gpuSection"];
        GpuStartButton.Content = s["app.bench.start"];
        GpuStressButton.Content = s["app.stress.start"];
        GpuResultLabel.Text = s["app.gpubench.result"];
        GpuElapsedLabel.Text = s["app.stress.elapsed"];
        GpuRateLabel.Text = "GFLOPS";
        GpuErrorsLabel.Text = s["app.stress.errors"];
        GpuTempLabel.Text = s["app.gpustress.temperature"];
        GpuNoteText.Text = s["gpubench.note"];
        foreach (var seconds in Durations)
        {
            GpuDurationBox.Items.Add($"{seconds} s");
        }

        GpuDurationBox.SelectedIndex = 1;
    }

    private async Task LoadGpuAsync()
    {
        if (s_adapters is null)
        {
            try
            {
                s_adapters = await Task.Run(D3D11ComputeRunner.Adapters);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                s_adapters = [];
            }
        }

        GpuText.Text = Adapter is { } adapter ? $"{adapter.Name} · {GpuBenchmark.Version}" : App.Text["app.gpubench.none"];
        if (_gpuRunning is null && _gpuStress is null)
        {
            GpuStartButton.IsEnabled = GpuStressButton.IsEnabled = Adapter is not null; // a running test keeps its buttons as they are
        }
    }

    private async void GpuStartButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (_gpuRunning is not null)
        {
            await _gpuRunning.CancelAsync();
            return;
        }

        if (Adapter is not { } adapter)
        {
            return;
        }

        var seconds = Durations[Math.Max(0, GpuDurationBox.SelectedIndex)];
        using var cancel = new CancellationTokenSource();
        _gpuRunning = cancel;
        GpuStartButton.Content = s["app.bench.cancel"];
        GpuDurationBox.IsEnabled = GpuStressButton.IsEnabled = false;
        GpuResultValue.Text = "—";
        GpuStatusText.Text = "";
        var progress = new Progress<double>(p => GpuProgress.Value = p);

        try
        {
            var result = await Task.Run(() =>
            {
                using var runner = D3D11ComputeRunner.Create(adapter);
                return GpuBenchmark.Run(runner, TimeSpan.FromSeconds(seconds), progress, cancel.Token);
            });
            GpuResultValue.Text = s.Format("app.gpubench.value", GpuBenchmark.Gflops(result.Gflops, s.Culture));
            var path = GpuHistoryPath();
            GpuBenchmarkHistory.Append(path, result);
            GpuStatusText.Text = s["app.bench.savedHint"];
        }
        catch (OperationCanceledException)
        {
            GpuStatusText.Text = s["bench.cancelled"];
            GpuProgress.Value = 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or DllNotFoundException or System.Runtime.InteropServices.COMException)
        {
            GpuStatusText.Text = ex.Message;
        }
        finally
        {
            _gpuRunning = null;
            GpuStartButton.Content = s["app.bench.start"];
            GpuDurationBox.IsEnabled = GpuStressButton.IsEnabled = true;
        }
    }

    private async void GpuStressButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (_gpuStress is not null)
        {
            await _gpuStress.CancelAsync();
            return;
        }

        if (Adapter is not { } adapter)
        {
            return;
        }

        using var cancel = new CancellationTokenSource();
        _gpuStress = cancel;
        _gpuMaxTemperature = null;
        _gpuRails.Clear();
        GpuRailsText.Text = "";
        GpuStressButton.Content = s["app.stress.stop"];
        GpuStartButton.IsEnabled = GpuDurationBox.IsEnabled = false;
        GpuStressPanel.Visibility = Visibility.Visible;
        GpuStressStatusText.Text = s.Format("app.gpustress.running", adapter.Name);
        GpuErrorsValue.ClearValue(TextBlock.ForegroundProperty);
        GpuErrorsValue.Text = "0";
        GpuRateValue.Text = "—";
        GpuProgress.IsIndeterminate = true;
        GpuStatusText.Text = "";

        var ui = DispatcherQueue;
        KeepAwake.Start();
        try
        {
            var final = await Task.Run(() =>
            {
                using var runner = D3D11ComputeRunner.Create(adapter);
                return GpuBenchmark.Stress(runner, st => ui.TryEnqueue(() => ShowGpuStress(st)), cancel: cancel.Token);
            });
            ShowGpuStress(final);
            GpuStressStatusText.Text = s[final.Errors == 0 ? "gpustress.passed" : "gpustress.failed"]
                + (PowerRails.AllWithin(_gpuRails) ? "" : " " + s["app.psu.stressBad"]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or System.Runtime.InteropServices.COMException)
        {
            GpuStressStatusText.Text = ex.Message;
        }
        finally
        {
            KeepAwake.Stop();
            _gpuStress = null;
            GpuProgress.IsIndeterminate = false;
            GpuStressButton.Content = s["app.stress.start"];
            GpuStartButton.IsEnabled = GpuDurationBox.IsEnabled = true;
        }
    }

    private void ShowGpuStress(GpuStressStatus st)
    {
        var s = App.Text;
        GpuElapsedValue.Text = $"{(int)st.Elapsed.TotalHours}:{st.Elapsed.Minutes:00}:{st.Elapsed.Seconds:00}";
        GpuRateValue.Text = GpuBenchmark.Gflops(st.Gflops, s.Culture);
        GpuErrorsValue.Text = st.Errors.ToString("N0", s.Culture)
            + (st.FirstErrorAt is { } first ? " (" + s.Format("app.stress.firstError", $"{(int)first.TotalHours}:{first.Minutes:00}:{first.Seconds:00}") + ")" : "");
        if (st.Errors > 0)
        {
            GpuErrorsValue.Foreground = new SolidColorBrush(Colors.Red);
        }

        if (App.Monitor.Latest is { } snapshot && SensorSummary.GpuTemperature(snapshot) is { } temperature)
        {
            _gpuMaxTemperature = Math.Max(_gpuMaxTemperature ?? temperature, temperature);
            GpuTempValue.Text = string.Create(s.Culture, $"{temperature:0} °C ({_gpuMaxTemperature:0} °C)");
        }

        if (App.Monitor.Latest is { } latest)
        {
            PowerRails.Track(_gpuRails, latest);
            GpuRailsText.Text = _gpuRails.Count > 0 ? s["app.psu.stressRails"] + " " + PowerRails.Summary(_gpuRails, s) : "";
        }
    }

    private static string GpuHistoryPath() => BenchmarkFiles.Gpu;
}
