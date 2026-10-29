using System.Diagnostics;
using System.Text;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Gui.Hud;
using HWSuite.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HWSuite.Gui.Pages;

/// <summary>Hardware inventory (the same report the CLI produces), with an HTML export.</summary>
public sealed partial class InventoryPage : Page
{
    private static HardwareInventory? s_cached;

    private readonly HashSet<InventoryCategory> _categories = [];
    private readonly Dictionary<InventoryCategory, ToggleButton> _chips = [];
    private readonly List<(InventoryCategory Category, HudPanel Panel, List<(FrameworkElement Element, string Text)> Rows)> _blocks = [];
    private ToggleButton? _all;

    public InventoryPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["app.nav.inventory"].ToUpper(s.Culture);
        SaveButton.Content = s["app.saveReport"];
        SaveVbiosButton.Content = s["app.saveVbios"];
        SearchBox.PlaceholderText = s["inv.search"];
        CategoryLabel.Text = ("// " + s["inv.components"]).ToUpper(s.Culture);
        EmptyText.Text = s["inv.none"];
        foreach (var name in (App.Settings.Current.InventoryCategories ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<InventoryCategory>(name, out var category))
            {
                _categories.Add(category);
            }
        }

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (s_cached is null)
        {
            StatusText.Text = App.Text["app.loadingInventory"];
            s_cached = await Task.Run(() => new InventoryCollector(ProviderCatalog.Default()).Collect());
        }

        if (_blocks.Count == 0)
        {
            Render(s_cached);
        }

        SaveButton.IsEnabled = true;
        SaveVbiosButton.Visibility = s_cached.Gpus.Any(g => g.VideoBios is not null) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Render(HardwareInventory inventory)
    {
        var s = App.Text;
        var c = s.Culture;
        StatusText.Text = $"{inventory.MachineName} · {s["report.generated"]}: {inventory.CollectedAt.ToString("g", c)}";
        Sections.Children.Clear();
        _blocks.Clear();

        foreach (var section in ReportModel.Build(inventory, s))
        {
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(new TextBlock { Text = ("// " + section.Title).ToUpper(c), Style = HudUi.Style("HudTagStyle"), Foreground = HudUi.Accent, Margin = new Thickness(0, 0, 0, 4) });
            var rows = new List<(FrameworkElement, string)>();
            foreach (var group in section.Groups)
            {
                if (group.Title is not null)
                {
                    card.Children.Add(new TextBlock { Text = group.Title.ToUpper(c), Style = HudUi.Style("HudNameStyle"), FontSize = 15, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap });
                }

                foreach (var row in group.Rows)
                {
                    // One small grid per row, so the search can hide rows one by one.
                    var line = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 2, 0, 2) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MaxWidth = 280 });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                    var label = new TextBlock { Text = row.Label, Foreground = HudUi.TextDim, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                    var value = new TextBlock { Text = row.Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = HudUi.Font("HudMonoFont"), FontSize = 13, Foreground = HudUi.Text };
                    Grid.SetColumn(value, 1);
                    line.Children.Add(label);
                    line.Children.Add(value);
                    card.Children.Add(line);
                    rows.Add((line, $"{group.Title} {row.Label} {row.Value}"));
                }
            }

            var panel = new HudPanel { Content = card };
            Sections.Children.Add(panel);
            _blocks.Add((section.Category, panel, rows));
        }

        BuildChips();
        ApplyFilter();
    }

    // ---- Filters -------------------------------------------------------------------------------------------------

    private void BuildChips()
    {
        var s = App.Text;
        CategoryFilters.Children.Clear();
        _chips.Clear();
        _all = Chip(s["sensors.filter.all"]);
        _all.Click += (_, _) =>
        {
            _categories.Clear();
            CategoriesChanged();
        };
        CategoryFilters.Children.Add(_all);
        foreach (var category in _blocks.Select(b => b.Category).Distinct().Order())
        {
            var chip = Chip(s[$"inv.cat.{category}"]);
            chip.Click += (_, _) =>
            {
                if (!_categories.Remove(category))
                {
                    _categories.Add(category);
                }

                CategoriesChanged();
            };
            _chips[category] = chip;
            CategoryFilters.Children.Add(chip);
        }

        RefreshChips();
    }

    private static ToggleButton Chip(string label) => new()
    {
        Content = label.ToUpper(App.Text.Culture),
        FontFamily = HudUi.Font("HudMonoFont"),
        FontSize = 12,
        CharacterSpacing = 80,
        Padding = new Thickness(14, 7, 14, 7),
    };

    private void RefreshChips()
    {
        if (_all is not null)
        {
            _all.IsChecked = _categories.Count == 0;
        }

        foreach (var (category, chip) in _chips)
        {
            chip.IsChecked = _categories.Contains(category);
        }
    }

    private void CategoriesChanged()
    {
        if (!App.IsDemo)
        {
            var text = _categories.Count == 0 ? null : string.Join(',', _categories.Order());
            App.Settings.Update(x => x with { InventoryCategories = text });
        }

        RefreshChips();
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>Shows the chosen components; the search keeps only matching rows (and blocks with any).</summary>
    private void ApplyFilter()
    {
        var search = SearchBox.Text.Trim();
        var shown = 0;
        foreach (var (category, panel, rows) in _blocks)
        {
            var any = false;
            foreach (var (element, text) in rows)
            {
                var match = search.Length == 0 || text.Contains(search, StringComparison.OrdinalIgnoreCase);
                element.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                any |= match;
            }

            var visible = (_categories.Count == 0 || _categories.Contains(category)) && any;
            panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            shown += visible ? 1 : 0;
        }

        EmptyText.Visibility = shown == 0 && _blocks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Sections.Reflow();
    }

    /// <summary>Writes the HTML report to the reports folder (chosen in Settings, or asked the first time) and opens it.</summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (s_cached is null || await Services.OutputLocations.ReportsFolderAsync(XamlRoot) is not { } folder)
        {
            return;
        }

        var path = Path.Combine(folder, $"hwsuite-{s_cached.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        using (var file = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            new HtmlReportWriter().Write(s_cached, App.Text, file);
        }

        StatusText.Text = App.Text.Format("app.reportSaved", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Writes each available video BIOS to the reports folder and shows it in Explorer.</summary>
    private async void SaveVbiosButton_Click(object sender, RoutedEventArgs e)
    {
        if (s_cached is null || await Services.OutputLocations.ReportsFolderAsync(XamlRoot) is not { } folder)
        {
            return;
        }

        string? last = null;
        foreach (var gpu in s_cached.Gpus.Where(g => g.VideoBios is not null))
        {
            last = Path.Combine(folder, Core.Gpu.VideoBios.FileName(gpu.Reference?.Model ?? gpu.Name, gpu.VideoBios!));
            File.WriteAllBytes(last, gpu.VideoBios!.Image);
        }

        if (last is not null)
        {
            StatusText.Text = App.Text.Format("app.vbiosSaved", last);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{last}\"") { UseShellExecute = true })?.Dispose();
        }
    }
}
