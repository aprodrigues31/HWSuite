using System.Diagnostics;
using System.Text;
using HWSuite.Core;
using HWSuite.Core.Benchmark;
using HWSuite.Core.Collection;
using HWSuite.Core.Maintenance;
using HWSuite.Core.Network;
using HWSuite.Gui.Hud;
using HWSuite.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Maintenance report: the service order (client, equipment, what was done) and the company profile on the
/// left; on the right, the diagnosis with an optional load test and benchmark, its verdicts on screen, and the
/// report saved as PDF (rendered by WebView2) or HTML in Documents\HWSuite\Reports.
/// </summary>
public sealed partial class ReportPage : Page
{
    private static readonly TimeSpan LoadDuration = TimeSpan.FromSeconds(60);
    private MaintenanceReport? _report;
    private bool _running;

    public ReportPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = "// HWSUITE · " + s["rep.tag"].ToUpper(c);
        PageTitle.Text = s["mnt.report.title"].ToUpper(c);
        Subtitle.Text = s["rep.subtitle"];
        OrderTag.Text = ("// " + s["rep.order"]).ToUpper(c);
        CompanyTag.Text = ("// " + s["rep.company"]).ToUpper(c);
        RunTag.Text = ("// " + s["rep.run"]).ToUpper(c);
        ClientBox.Header = s["mnt.report.client"];
        ClientContactBox.Header = s["rep.clientContact"];
        EquipmentBox.Header = s["rep.equipment"];
        OrderBox.Header = s["rep.orderNumber"];
        ServicesBox.Header = s["mnt.report.services"];
        ServicesBox.PlaceholderText = s["rep.servicesHint"];
        NotesBox.Header = s["mnt.report.notes"];
        CompanyBox.Header = s["rep.companyName"];
        DocumentBox.Header = s["rep.document"];
        TechnicianBox.Header = s["mnt.report.technician"];
        ContactBox.Header = s["rep.contact"];
        AddressBox.Header = s["rep.address"];
        LogoButton.Content = s["rep.logoFolder"];
        LoadCheck.Content = s.Format("rep.load", LoadDuration.TotalSeconds);
        BenchCheck.Content = s["rep.bench"];
        InventoryCheck.Content = s["rep.inventory"];
        RunButton.Content = s["rep.runButton"].ToUpper(c);
        PdfButton.Content = s["rep.pdf"].ToUpper(c);
        HtmlButton.Content = s["rep.html"];
        FolderButton.Content = s["results.openFolder"];

        var settings = App.Settings.Current;
        CompanyBox.Text = settings.CompanyName ?? "";
        DocumentBox.Text = settings.CompanyDocument ?? "";
        TechnicianBox.Text = settings.TechnicianName ?? "";
        ContactBox.Text = settings.CompanyContact ?? "";
        AddressBox.Text = settings.CompanyAddress ?? "";
        ShowLogo();

        if (App.IsDemo)
        {
            // Screenshots: a filled-in order and a diagnosis made right away (no load test).
            ClientBox.Text = s["rep.demo.client"];
            EquipmentBox.Text = s["rep.demo.equipment"];
            OrderBox.Text = "2026-1029-01";
            ServicesBox.Text = s["rep.demo.services"];
            CompanyBox.Text = string.IsNullOrEmpty(CompanyBox.Text) ? s["rep.demo.company"] : CompanyBox.Text;
            TechnicianBox.Text = string.IsNullOrEmpty(TechnicianBox.Text) ? s["rep.demo.technician"] : TechnicianBox.Text;
            LoadCheck.IsChecked = false;
            Loaded += async (_, _) =>
            {
                if (_report is null && !_running)
                {
                    await RunAsync();
                }
            };
        }
    }

    private void Layout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Side by side when wide, one column (form, then diagnosis) when narrow.
        var wide = e.NewSize.Width >= 940;
        RightColumn.Width = wide ? new GridLength(1.35, GridUnitType.Star) : new GridLength(0);
        Layout.ColumnSpacing = wide ? 16 : 0;
        Grid.SetColumn(ResultColumn, wide ? 1 : 0);
        Grid.SetRow(ResultColumn, wide ? 0 : 1);
    }

    private void ShowLogo()
    {
        var s = App.Text;
        LogoText.Text = MaintenanceSession.LogoPath() is { } path
            ? s.Format("rep.logoFound", Path.GetFileName(path))
            : s.Format("rep.logoMissing", BenchmarkFiles.Folder);
    }

    private void Company_LostFocus(object sender, RoutedEventArgs e)
    {
        static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (App.IsDemo)
        {
            return; // demo values are not saved
        }

        App.Settings.Update(x => x with
        {
            CompanyName = Clean(CompanyBox.Text),
            CompanyDocument = Clean(DocumentBox.Text),
            TechnicianName = Clean(TechnicianBox.Text),
            CompanyContact = Clean(ContactBox.Text),
            CompanyAddress = Clean(AddressBox.Text),
        });
    }

    private void LogoButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BenchmarkFiles.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BenchmarkFiles.Folder}\"") { UseShellExecute = true })?.Dispose();
        ShowLogo();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    /// <summary>Collects everything (inventory, sensors, drives, optional load test and benchmark), then runs the checks.</summary>
    private async Task RunAsync()
    {
        if (_running)
        {
            return;
        }

        var s = App.Text;
        _running = true;
        RunButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        SavedText.Text = "";
        try
        {
            StatusText.Text = s["rep.step.inventory"];
            var inventory = await Task.Run(() => new InventoryCollector(ProviderCatalog.Default()).Collect());
            var volumes = await Task.Run(MaintenanceSession.Volumes);
            var settings = App.Settings.Current;
            var input = new MaintenanceInput(inventory, DateTimeOffset.Now)
            {
                Sensors = App.Monitor.Latest,
                Volumes = volumes,
                PumpSensorId = settings.PumpSensorId,
                PumpMinRpm = settings.PumpMinRpm,
                Network = NetworkHistory.Read(BenchmarkFiles.Network) is [var lastNet, ..] ? lastNet : null,
            };

            if (LoadCheck.IsChecked == true)
            {
                StatusText.Text = s.Format("rep.step.load", LoadDuration.TotalSeconds);
                Progress.IsIndeterminate = false;
                var progress = new Progress<double>(p => Progress.Value = p);
                var load = await Task.Run(() => MaintenanceSession.RunLoad(LoadDuration, () => App.Monitor.Latest, progress));
                input = input with { Load = load, Sensors = App.Monitor.Latest };
                Progress.IsIndeterminate = true;
            }

            if (BenchCheck.IsChecked == true)
            {
                StatusText.Text = s["rep.step.bench"];
                var result = await Task.Run(() => CpuBenchmark.Run(TimeSpan.FromSeconds(10)));
                var processor = inventory.Processors.FirstOrDefault()?.Name;
                var comparison = BenchmarkComparison.Build(result, processor, BenchmarkComparison.Builtin(), BenchmarkComparison.Local(BenchmarkComparison.LocalFolder));
                input = input with { BenchmarkResult = result, Benchmark = comparison };
            }

            var checks = MaintenanceChecks.Run(input, s);
            var order = new ServiceOrder(Text(ClientBox), Text(ClientContactBox), Text(EquipmentBox), Text(OrderBox), Text(ServicesBox), Text(NotesBox));
            var company = new CompanyProfile(Text(CompanyBox), Text(DocumentBox), Text(ContactBox), Text(AddressBox), Text(TechnicianBox), MaintenanceSession.LogoDataUri());
            _report = new MaintenanceReport(company, order, checks, inventory, input.Now, BuildInfo.Current.Display, InventoryCheck.IsChecked == true) { Load = input.Load };
            ShowResult(_report);
            StatusText.Text = s.Format("rep.done", checks.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _running = false;
            RunButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private static string? Text(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private void ShowResult(MaintenanceReport report)
    {
        var s = App.Text;
        var c = s.Culture;
        var overall = report.Overall;
        VerdictBanner.Background = VerdictBrush(overall);
        VerdictTitle.Text = s[$"mnt.overall.{overall}"].ToUpper(c);
        VerdictText.Text = s[$"mnt.overall.{overall}.text"];
        VerdictCounts.Text = $"{report.Count(Verdict.Ok)} OK · {report.Count(Verdict.Attention)} ! · {report.Count(Verdict.Problem)} ✕";

        CheckList.Children.Clear();
        foreach (var area in report.Checks.GroupBy(x => x.Area))
        {
            var rows = new StackPanel { Spacing = 0 };
            foreach (var check in area)
            {
                rows.Children.Add(CheckRow(check));
            }

            CheckList.Children.Add(HudUi.Panel(s[$"mnt.area.{area.Key}"], rows, c));
        }

        ResultPanel.Visibility = Visibility.Visible;
    }

    private static Grid CheckRow(CheckResult check)
    {
        var s = App.Text;
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Background = VerdictBrush(check.Verdict),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = s[$"mnt.verdict.{check.Verdict}"].ToUpper(s.Culture),
                FontFamily = HudUi.Font("HudMonoStrongFont"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x03, 0x10, 0x16)),
            },
        };
        grid.Children.Add(badge);
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = check.Item, FontFamily = HudUi.Font("HudBodyMediumFont"), FontSize = 14, Foreground = HudUi.Text, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = check.Finding, FontSize = 13, Foreground = HudUi.TextDim, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(check.Advice))
        {
            text.Children.Add(new TextBlock { Text = "→ " + check.Advice, FontSize = 13, Foreground = check.Verdict >= Verdict.Attention ? HudUi.Warn : HudUi.Tag, TextWrapping = TextWrapping.Wrap });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static Brush VerdictBrush(Verdict verdict) => verdict switch
    {
        Verdict.Ok => HudUi.Brush("HudGoodBrush"),
        Verdict.Attention => HudUi.Warn,
        Verdict.Problem => HudUi.Crit,
        _ => HudUi.Tag,
    };

    // ---- Saving ------------------------------------------------------------------------------------------------

    private string? SaveHtml()
    {
        if (_report is null)
        {
            return null;
        }

        Directory.CreateDirectory(MaintenanceSession.ReportsFolder);
        var path = Path.Combine(MaintenanceSession.ReportsFolder, MaintenanceReportWriter.FileName(_report, "html"));
        File.WriteAllText(path, MaintenanceReportWriter.ToHtml(_report, App.Text), new UTF8Encoding(false));
        return path;
    }

    private void HtmlButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SaveHtml() is { } path)
            {
                SavedText.Text = App.Text.Format("rep.saved", path);
                Open(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SavedText.Text = ex.Message;
        }
    }

    /// <summary>Writes the HTML, has WebView2 load it and print it to PDF next to it, then opens the PDF.</summary>
    private async void PdfButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        string? html = null;
        try
        {
            html = SaveHtml();
            if (html is null)
            {
                return;
            }

            PdfButton.IsEnabled = false;
            SavedText.Text = s["rep.pdfWorking"];
            await PdfView.EnsureCoreWebView2Async();
            var core = PdfView.CoreWebView2;
            var loaded = new TaskCompletionSource<bool>();
            void Done(Microsoft.Web.WebView2.Core.CoreWebView2 _, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
            core.NavigationCompleted += Done;
            core.Navigate(new Uri(html).AbsoluteUri);
            var ok = await loaded.Task;
            core.NavigationCompleted -= Done;
            if (!ok)
            {
                throw new InvalidOperationException(s["rep.pdfFailed"]);
            }

            var settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0; // the page sets its own A4 margins
            settings.PageWidth = 8.27;
            settings.PageHeight = 11.69;
            var pdf = Path.ChangeExtension(html, ".pdf");
            if (!await core.PrintToPdfAsync(pdf, settings))
            {
                throw new InvalidOperationException(s["rep.pdfFailed"]);
            }

            SavedText.Text = s.Format("rep.saved", pdf);
            Open(pdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Without WebView2 the HTML is still there: printing it from the browser gives the same PDF.
            SavedText.Text = s.Format("rep.pdfFallback", ex.Message, html ?? "");
        }
        finally
        {
            PdfButton.IsEnabled = true;
        }
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(MaintenanceSession.ReportsFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{MaintenanceSession.ReportsFolder}\"") { UseShellExecute = true })?.Dispose();
    }

    private static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
