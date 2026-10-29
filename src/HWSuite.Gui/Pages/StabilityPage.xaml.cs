using System.Text;
using HWSuite.Core;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Collection;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Model;
using HWSuite.Core.Sensors;
using HWSuite.Core.Stability;
using HWSuite.Gui.Hud;
using HWSuite.Hosting;
using HWSuite.Providers.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Stability test (processor, graphics card and RAM together) or RAM diagnostic, with live values, a verdict and the
/// certificate for the client as PDF or HTML in the reports folder (plus its history record).
/// </summary>
public sealed partial class StabilityPage : Page
{
    private static readonly int[] Minutes = [5, 10, 15, 30, 60];
    private readonly Dictionary<string, TextBlock> _tiles = [];
    private CancellationTokenSource? _cancel;
    private StabilityResult? _result;
    private HardwareInventory? _inventory;
    private ServiceOrder? _order;

    public StabilityPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = "// HWSUITE · " + s["stab.tag"].ToUpper(c);
        PageTitle.Text = s["stab.title"].ToUpper(c);
        Subtitle.Text = s["stab.subtitle"];
        SetupTag.Text = ("// " + s["stab.setup"]).ToUpper(c);
        OrderTag.Text = ("// " + s["rep.order"]).ToUpper(c);
        LiveTag.Text = ("// " + s["stab.live"]).ToUpper(c);
        FullMode.Text = s["stab.mode.full"];
        MemoryMode.Text = s["stab.mode.memory"];
        CpuCheck.Content = s["mnt.area.Processor"];
        GpuCheck.Content = s["mnt.area.Graphics"];
        MemoryCheck.Content = s["mnt.area.Memory"];
        DurationBox.Header = s["stab.duration.header"];
        foreach (var m in Minutes)
        {
            DurationBox.Items.Add(s.Format("charts.window", m));
        }

        DurationBox.SelectedIndex = 2;
        ClientBox.Header = s["mnt.report.client"];
        EquipmentBox.Header = s["rep.equipment"];
        OrderBox.Header = s["rep.orderNumber"];
        NotesBox.Header = s["mnt.report.notes"];
        StartButton.Content = s["stab.start"].ToUpper(c);
        PdfButton.Content = s["rep.pdf"].ToUpper(c);
        HtmlButton.Content = s["rep.html"];
        ShowMode();

        foreach (var (key, label) in new[] { ("errors", "stab.tile.errors"), ("cpu", "stab.tile.cpu"), ("gpu", "stab.tile.gpu"), ("power", "stab.tile.power"), ("ram", "stab.tile.ram") })
        {
            var tile = new StackPanel { Spacing = 2 };
            tile.Children.Add(HudUi.TagText(s[label], c));
            var value = new TextBlock { Text = "—", FontFamily = HudUi.Font("HudMonoStrongFont"), FontSize = 22, Foreground = HudUi.Text };
            tile.Children.Add(value);
            _tiles[key] = value;
            Tiles.Children.Add(tile);
        }

        Unloaded += (_, _) => _cancel?.Cancel();
    }

    private bool MemoryOnly => ReferenceEquals(ModeBar.SelectedItem, MemoryMode);

    private void ModeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowMode();

    private void ShowMode()
    {
        if (ModeText is null)
        {
            return; // during InitializeComponent
        }

        var s = App.Text;
        ModeText.Text = s[MemoryOnly ? "stab.mode.memory.text" : "stab.mode.full.text"];
        PartsPanel.Visibility = MemoryOnly ? Visibility.Collapsed : Visibility.Visible;
        DurationBox.SelectedIndex = MemoryOnly ? 1 : 2;
        TopPanels.Reflow();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancel is not null)
        {
            _cancel.Cancel(); // the button reads "Stop" while running
            return;
        }

        var s = App.Text;
        var c = s.Culture;
        var minutes = Minutes[Math.Max(0, DurationBox.SelectedIndex)];
        var options = MemoryOnly
            ? new StabilityOptions(TimeSpan.FromMinutes(minutes), Cpu: false, Gpu: false, Memory: true) { MemoryShare = 1.0 }
            : new StabilityOptions(TimeSpan.FromMinutes(minutes), CpuCheck.IsChecked == true, GpuCheck.IsChecked == true, MemoryCheck.IsChecked == true);
        if (!options.Cpu && !options.Gpu && !options.Memory)
        {
            StatusText.Text = s["stab.nothing"];
            return;
        }

        _cancel = new CancellationTokenSource();
        StartButton.Content = s["stab.stop"].ToUpper(c);
        ResultPanel.Visibility = Visibility.Collapsed;
        LivePanel.Visibility = Visibility.Visible;
        StatusText.Text = s["rep.step.inventory"];
        _order = new ServiceOrder(Text(ClientBox), null, Text(EquipmentBox), Text(OrderBox), null, Text(NotesBox));
        KeepAwake.Start();
        try
        {
            _inventory ??= await Task.Run(() => new InventoryCollector(ProviderCatalog.Default()).Collect());
            StatusText.Text = s[MemoryOnly ? "stab.running.memory" : "stab.running"];
            Func<IGpuComputeRunner>? gpu = null;
            if (options.Gpu && D3D11ComputeRunner.Adapters() is { Count: > 0 } adapters)
            {
                gpu = () => D3D11ComputeRunner.Create(adapters[0]);
            }

            var times = new List<DateTimeOffset>();
            List<double> cpuTemps = [], gpuTemps = [];
            var progress = new Progress<StabilityProgress>(p =>
            {
                Progress.Value = Math.Min(1, p.Elapsed / p.Duration);
                TimeText.Text = $"{SvgClock(p.Elapsed)} / {SvgClock(p.Duration)}";
                var errors = p.CpuErrors + p.GpuErrors + p.MemoryErrors;
                _tiles["errors"].Text = errors.ToString("N0", c);
                _tiles["errors"].Foreground = errors > 0 ? HudUi.Crit : HudUi.Text;
                _tiles["cpu"].Text = p.CpuTemperature is { } ct ? $"{ct:0} °C" : "—";
                _tiles["gpu"].Text = p.GpuTemperature is { } gt ? $"{gt:0} °C" : "—";
                _tiles["power"].Text = p.CpuPower is null && p.GpuPower is null ? "—" : $"{(p.CpuPower ?? 0) + (p.GpuPower ?? 0):0} W";
                _tiles["ram"].Text = p.Memory is { } m ? $"{m.BytesVerified / (double)(1L << 30):N0} GB" : "—";
                times.Add(DateTimeOffset.Now);
                cpuTemps.Add(p.CpuTemperature ?? double.NaN);
                gpuTemps.Add(p.GpuTemperature ?? double.NaN);
                LiveChart.Set([.. times],
                [
                    new TimeSeries(s["mnt.area.Processor"], [.. cpuTemps], HudUi.Accent, SensorKind.Temperature),
                    new TimeSeries(s["mnt.area.Graphics"], [.. gpuTemps], HudUi.Warn, SensorKind.Temperature),
                ]);
            });
            var token = _cancel.Token;
            _result = await Task.Run(() => StabilityTest.Run(options, () => App.Monitor.Latest, gpu, progress, token));
            ShowResult(_result);
            StatusText.Text = "";
        }
        catch (Exception ex) when (ex is OutOfMemoryException or InvalidOperationException or IOException)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            KeepAwake.Stop();
            _cancel?.Dispose();
            _cancel = null;
            StartButton.Content = s["stab.start"].ToUpper(c);
        }
    }

    private static string SvgClock(TimeSpan t) => Core.Reporting.SvgChart.Elapsed(t);

    private static string? Text(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private void ShowResult(StabilityResult result)
    {
        var s = App.Text;
        var c = s.Culture;
        VerdictBanner.Background = result.Passed ? HudUi.Brush("HudGoodBrush") : HudUi.Crit;
        VerdictTitle.Text = s[result.Passed ? "stab.passed" : "stab.failed"].ToUpper(c);
        VerdictText.Text = result.Stop switch
        {
            StabilityStop.CpuTemperature => s.Format("stab.stop.cpu", result.Options.CpuTemperatureLimit.ToString("0", c)),
            StabilityStop.GpuTemperature => s.Format("stab.stop.gpu", result.Options.GpuTemperatureLimit.ToString("0", c)),
            StabilityStop.Cancelled => s["stab.stop.cancelled"],
            _ => s[result.Passed ? "stab.passed.text" : "stab.failed.text"],
        } + " " + s.Format("stab.summary", result.CpuErrors, result.GpuErrors, result.MemoryErrors,
            result.MaxCpuTemperature?.ToString("0", c) ?? "—", result.MaxGpuTemperature?.ToString("0", c) ?? "—");
        ResultPanel.Visibility = Visibility.Visible;
        SavedText.Text = "";
    }

    // ---- Saving: same folder, logo and per-client rules as the maintenance report ---------------------------------

    private async Task<string?> SaveHtmlAsync()
    {
        if (_result is null || _inventory is null || _order is null)
        {
            return null;
        }

        string? folder;
        string? logo = null;
        if (App.IsDemo)
        {
            folder = Services.OutputLocations.SuggestedReportsFolder;
        }
        else
        {
            folder = await Services.OutputLocations.ReportsFolderAsync(XamlRoot);
            if (folder is null)
            {
                return null;
            }

            var (proceed, uri) = await Services.OutputLocations.LogoAsync(XamlRoot);
            if (!proceed)
            {
                return null;
            }

            logo = uri;
        }

        var company = Core.Maintenance.MaintenanceSession.Company(App.Settings.Current) with { LogoDataUri = logo };
        var fake = new MaintenanceReport(company, _order, [], _inventory, _result.StartedAt, "");
        folder = MaintenanceReportWriter.Folder(fake, folder, App.Settings.Current.ReportsPerClient);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, StabilityReportWriter.FileName(_result, _order, _inventory.MachineName ?? "pc", "html"));
        File.WriteAllText(path, StabilityReportWriter.ToHtml(_result, _inventory, _order, company, App.Text), new UTF8Encoding(false));
        AttendanceRecord.From(_result, _inventory, _order, company, BuildInfo.Current.Display, Path.GetFileName(path)).SaveBeside(path);
        return path;
    }

    private async void HtmlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await SaveHtmlAsync() is { } path)
            {
                SavedText.Text = App.Text.Format("rep.saved", path);
                Services.PdfPrinter.Open(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SavedText.Text = ex.Message;
        }
    }

    private async void PdfButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        string? html = null;
        try
        {
            html = await SaveHtmlAsync();
            if (html is null)
            {
                return;
            }

            PdfButton.IsEnabled = false;
            SavedText.Text = s["rep.pdfWorking"];
            var pdf = await Services.PdfPrinter.PrintAsync(PdfView, html);
            SavedText.Text = s.Format("rep.saved", pdf);
            Services.PdfPrinter.Open(pdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SavedText.Text = s.Format("rep.pdfFallback", ex.Message, html ?? "");
        }
        finally
        {
            PdfButton.IsEnabled = true;
        }
    }
}
