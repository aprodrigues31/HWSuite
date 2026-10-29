using System.Globalization;
using System.Text;
using HWSuite.Core.Maintenance;
using HWSuite.Gui.Hud;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Service history: every report saved in the reports folder (the records beside them), by client. Two services of
/// the same computer can be compared (what improved, what got worse), and a client's computers summarised in one page.
/// </summary>
public sealed partial class HistoryPage : Page
{
    private IReadOnlyList<(AttendanceRecord Record, string Path)> _all = [];
    private readonly List<(AttendanceRecord Record, string Path)> _picked = [];
    private string? _client; // null = all clients
    private string? _folder;

    public HistoryPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = "// HWSUITE · " + s["hist.tag"].ToUpper(c);
        PageTitle.Text = s["hist.title"].ToUpper(c);
        SearchBox.PlaceholderText = s["hist.search"];
        RefreshButton.Content = s["hist.refresh"];
        ClientsLabel.Text = ("// " + s["hist.clients"]).ToUpper(c);
        CompareButton.Content = s["hist.compare"];
        BatchButton.Content = s["hist.batch"].ToUpper(c);
        HintText.Text = s["hist.hint"];
        CompareTag.Text = ("// " + s["hist.compareTitle"]).ToUpper(c);
        EmptyText.Text = s["hist.empty"];
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var s = App.Text;
        _folder = App.Settings.ReportsFolder ?? Services.OutputLocations.SuggestedReportsFolder;
        FolderText.Text = s.Format("hist.folder", _folder);
        var folder = _folder;
        _all = await Task.Run(() => AttendanceRecord.Scan(folder));
        if (App.IsDemo && _all.Count == 0)
        {
            _all = DemoRecords(); // screenshots show a filled history
        }
        _picked.Clear();
        ComparePanel.Visibility = Visibility.Collapsed;
        BuildClients();
        BuildRows();
    }

    private static List<(AttendanceRecord, string)> DemoRecords()
    {
        var now = DateTimeOffset.Now;
        AttendanceRecord Visit(int daysAgo, string machine, string equipment, double idle, int wear, double free, int problems, int attention) => new()
        {
            At = now.AddDays(-daysAgo),
            Client = "Escritório Exemplo",
            Order = $"2026-{daysAgo:000}",
            Equipment = equipment,
            Machine = machine,
            Serial = machine + "-SN",
            Overall = problems > 0 ? Verdict.Problem : attention > 0 ? Verdict.Attention : Verdict.Ok,
            Ok = 14,
            Problem = problems,
            Attention = attention,
            CpuIdleTemperature = idle,
            Disks = [new DiskRecord("Samsung 980 500GB", "S1", wear, 40, 0, 9000, "Good")],
            Volumes = [new VolumeRecord("C:", free, 1, 100)],
            Findings = problems > 0 ? ["Espaço livre · C: (sistema): 6% livre"] : attention > 0 ? ["Driver de vídeo: versão de 2025"] : [],
        };
        return
        [
            (Visit(2, "RECEPCAO-01", "Recepção", 44, 14, 38, 0, 1), ""),
            (Visit(2, "FINANCEIRO-02", "Financeiro", 51, 31, 6, 1, 2), ""),
            (Visit(182, "RECEPCAO-01", "Recepção", 63, 11, 9, 1, 1), ""),
        ];
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => BuildRows();

    private static string ClientOf(AttendanceRecord r) => string.IsNullOrWhiteSpace(r.Client) ? App.Text["hist.noClient"] : r.Client.Trim();

    private void BuildClients()
    {
        var s = App.Text;
        var c = s.Culture;
        ClientChips.Children.Clear();
        var clients = _all.GroupBy(x => ClientOf(x.Record), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Count: g.Count(), Last: g.Max(x => x.Record.At)))
            .OrderByDescending(x => x.Last).ToList();
        if (_client is not null && clients.All(x => !string.Equals(x.Name, _client, StringComparison.OrdinalIgnoreCase)))
        {
            _client = null;
        }

        ToggleButton Chip(string label, bool on, Action click)
        {
            var chip = new ToggleButton { Content = label, IsChecked = on, FontFamily = HudUi.Font("HudMonoFont"), FontSize = 12, Padding = new Thickness(12, 6, 12, 6) };
            chip.Click += (_, _) => click();
            return chip;
        }

        ClientChips.Children.Add(Chip(s["sensors.filter.all"].ToUpper(c) + $" · {_all.Count}", _client is null, () =>
        {
            _client = null;
            BuildClients();
            BuildRows();
        }));
        foreach (var (name, count, _) in clients)
        {
            ClientChips.Children.Add(Chip($"{name.ToUpper(c)} · {count}", string.Equals(_client, name, StringComparison.OrdinalIgnoreCase), () =>
            {
                _client = name;
                _picked.Clear();
                BuildClients();
                BuildRows();
            }));
        }

        EmptyText.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListPanel.Visibility = _all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private IEnumerable<(AttendanceRecord Record, string Path)> Shown()
    {
        var search = SearchBox.Text.Trim();
        return _all.Where(x => (_client is null || string.Equals(ClientOf(x.Record), _client, StringComparison.OrdinalIgnoreCase))
            && (search.Length == 0 || string.Join(' ', x.Record.Machine, x.Record.Equipment, x.Record.Order, x.Record.Client, x.Record.Serial)
                .Contains(search, StringComparison.OrdinalIgnoreCase)));
    }

    private void BuildRows()
    {
        var s = App.Text;
        var c = s.Culture;
        Rows.Children.Clear();
        ListTag.Text = ("// " + (_client ?? s["hist.allClients"])).ToUpper(c);
        foreach (var (record, path) in Shown())
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var entry = (record, path);
            var pick = new CheckBox { IsChecked = _picked.Contains(entry), MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            pick.Click += (_, _) =>
            {
                if (pick.IsChecked == true)
                {
                    _picked.Add(entry);
                }
                else
                {
                    _picked.Remove(entry);
                }

                CompareButton.IsEnabled = CanCompare();
            };
            row.Children.Add(pick);

            var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            when.Children.Add(new TextBlock { Text = record.At.LocalDateTime.ToString("g", c), FontFamily = HudUi.Font("HudMonoFont"), FontSize = 13, Foreground = HudUi.Text });
            when.Children.Add(new TextBlock { Text = s[$"batch.kind.{record.Kind}"].ToUpper(c), Style = HudUi.Style("HudTagStyle") });
            Grid.SetColumn(when, 1);
            row.Children.Add(when);

            var what = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            what.Children.Add(new TextBlock { Text = record.Equipment ?? record.Machine, FontSize = 14, Foreground = HudUi.Text, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = string.Join(" · ", new[] { record.Machine, record.Client, record.Order is null ? null : s["mnt.report.order"] + " " + record.Order, record.Findings.FirstOrDefault() }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            what.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = HudUi.TextDim, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(what, 2);
            row.Children.Add(what);

            var badge = new Border
            {
                Background = VerdictBrush(record.Overall),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (record.Test is { } t ? s[t.Passed ? "batch.test.passed" : "batch.test.failed"] : s[$"mnt.verdict.{record.Overall}"]).ToUpper(c),
                    FontFamily = HudUi.Font("HudMonoStrongFont"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x03, 0x10, 0x16)),
                },
            };
            Grid.SetColumn(badge, 3);
            row.Children.Add(badge);

            var open = new Button { Content = s["hist.open"], VerticalAlignment = VerticalAlignment.Center };
            open.Click += (_, _) => OpenReport(record, path);
            Grid.SetColumn(open, 4);
            row.Children.Add(open);
            Rows.Children.Add(row);
        }

        CompareButton.IsEnabled = CanCompare();
    }

    private static Brush VerdictBrush(Verdict verdict) => verdict switch
    {
        Verdict.Ok => HudUi.Brush("HudGoodBrush"),
        Verdict.Attention => HudUi.Warn,
        Verdict.Problem => HudUi.Crit,
        _ => HudUi.Tag,
    };

    /// <summary>The PDF beside the record when there is one, else the HTML.</summary>
    private void OpenReport(AttendanceRecord record, string recordPath)
    {
        var folder = Path.GetDirectoryName(recordPath) ?? "";
        var html = record.ReportFile is { } name ? Path.Combine(folder, name) : null;
        var pdf = html is null ? null : Path.ChangeExtension(html, ".pdf");
        var file = pdf is not null && File.Exists(pdf) ? pdf : html is not null && File.Exists(html) ? html : null;
        if (file is null)
        {
            SavedText.Text = App.Text["hist.missing"];
            return;
        }

        Services.PdfPrinter.Open(file);
    }

    /// <summary>Two maintenance services of the same computer.</summary>
    private bool CanCompare() =>
        _picked.Count == 2 && _picked.All(p => p.Record.Kind == "maintenance") && _picked[0].Record.MachineKey == _picked[1].Record.MachineKey;

    private void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCompare())
        {
            return;
        }

        var s = App.Text;
        var c = s.Culture;
        var ordered = _picked.OrderBy(p => p.Record.At).ToList();
        var (before, after) = (ordered[0].Record, ordered[1].Record);
        CompareRows.Children.Clear();
        CompareTag.Text = ("// " + s.Format("hist.compareBetween", before.At.LocalDateTime.ToString("d", c), after.At.LocalDateTime.ToString("d", c))).ToUpper(c);
        CompareRows.Children.Add(CompareRow(s["hist.item"], s["hist.before"], s["hist.after"], 0, header: true));
        foreach (var row in AttendanceComparison.Compare(before, after, s))
        {
            CompareRows.Children.Add(CompareRow(row.Item, row.Before, row.After, row.Trend, header: false));
        }

        ComparePanel.Visibility = Visibility.Visible;
    }

    private static Grid CompareRow(string item, string before, string after, int trend, bool header)
    {
        var s = App.Text;
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 5, 0, 5), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        TextBlock Cell(string text, int column, Brush color)
        {
            var block = header
                ? new TextBlock { Text = text.ToUpper(s.Culture), Style = HudUi.Style("HudTagStyle") }
                : new TextBlock { Text = text, FontSize = 13, Foreground = color, FontFamily = column > 0 ? HudUi.Font("HudMonoFont") : HudUi.Font("HudBodyMediumFont") };
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
            return block;
        }

        Cell(item, 0, HudUi.Text);
        Cell(before, 1, HudUi.TextDim);
        Cell(after, 2, HudUi.Text);
        if (!header)
        {
            Cell(s[trend > 0 ? "hist.better" : trend < 0 ? "hist.worse" : "hist.same"], 3,
                trend > 0 ? HudUi.Brush("HudGoodBrush") : trend < 0 ? HudUi.Warn : HudUi.Tag);
        }

        return grid;
    }

    /// <summary>The client's computers in one page (latest service of each), saved beside their reports as PDF.</summary>
    private async void BatchButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        var records = Shown().Select(x => x.Record).ToList();
        if (records.Count == 0)
        {
            return;
        }

        string? html = null;
        try
        {
            BatchButton.IsEnabled = false;
            SavedText.Text = s["rep.pdfWorking"];
            (bool Proceed, string? Logo) choice = App.IsDemo ? (true, null) : await Services.OutputLocations.LogoAsync(XamlRoot);
            var (proceed, logo) = choice;
            if (!proceed)
            {
                SavedText.Text = "";
                return;
            }

            var company = MaintenanceSession.Company(App.Settings.Current) with { LogoDataUri = logo };
            var title = _client is null ? s["batch.title"] : s.Format("hist.batchTitle", _client);
            var folder = _client is not null && Shown().FirstOrDefault().Path is { } first ? Path.GetDirectoryName(first)! : _folder!;
            Directory.CreateDirectory(folder);
            html = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"hwsuite-resumo-{DateTime.Now:yyyy-MM-dd-HHmm}.html"));
            File.WriteAllText(html, BatchSummary.ToHtml(records, s, company, title), new UTF8Encoding(false));
            var pdf = await Services.PdfPrinter.PrintAsync(PdfView, html, landscape: true);
            SavedText.Text = s.Format("rep.saved", pdf);
            Services.PdfPrinter.Open(pdf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SavedText.Text = s.Format("rep.pdfFallback", ex.Message, html ?? "");
        }
        finally
        {
            BatchButton.IsEnabled = true;
        }
    }
}
