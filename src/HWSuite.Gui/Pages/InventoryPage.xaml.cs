using System.Diagnostics;
using System.Text;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>Hardware inventory (the same report the CLI produces), with an HTML export.</summary>
public sealed partial class InventoryPage : Page
{
    private static HardwareInventory? s_cached;

    public InventoryPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["app.nav.inventory"].ToUpper(s.Culture);
        SaveButton.Content = s["app.saveReport"];
        SaveVbiosButton.Content = s["app.saveVbios"];
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (s_cached is null)
        {
            StatusText.Text = App.Text["app.loadingInventory"];
            s_cached = await Task.Run(() => new InventoryCollector(ProviderCatalog.Default()).Collect());
        }

        Render(s_cached);
        SaveButton.IsEnabled = true;
        SaveVbiosButton.Visibility = s_cached.Gpus.Any(g => g.VideoBios is not null) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Render(HardwareInventory inventory)
    {
        var s = App.Text;
        StatusText.Text = $"{inventory.MachineName} · {s["report.generated"]}: {inventory.CollectedAt.ToString("g", s.Culture)}";
        Sections.Children.Clear();

        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        foreach (var section in ReportModel.Build(inventory, s))
        {
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(new TextBlock { Text = ("// " + section.Title).ToUpper(s.Culture), Style = Hud.HudUi.Style("HudTagStyle"), Foreground = Hud.HudUi.Accent, Margin = new Thickness(0, 0, 0, 4) });

            foreach (var group in section.Groups)
            {
                if (group.Title is not null)
                {
                    card.Children.Add(new TextBlock { Text = group.Title.ToUpper(s.Culture), Style = Hud.HudUi.Style("HudNameStyle"), FontSize = 15, Margin = new Thickness(0, 8, 0, 2) });
                }

                var rows = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
                rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var i = 0; i < group.Rows.Count; i++)
                {
                    rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var label = new TextBlock { Text = group.Rows[i].Label, Foreground = secondary, TextWrapping = TextWrapping.Wrap };
                    var value = new TextBlock { Text = group.Rows[i].Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = Hud.HudUi.Font("HudMonoFont"), FontSize = 13 };
                    Grid.SetRow(label, i);
                    Grid.SetRow(value, i);
                    Grid.SetColumn(value, 1);
                    rows.Children.Add(label);
                    rows.Children.Add(value);
                }

                card.Children.Add(rows);
            }

            Sections.Children.Add(new Hud.HudPanel { Content = card });
        }
    }

    /// <summary>
    /// Writes the HTML report to Documents\HWSuite and opens it. (File pickers do not work in an
    /// elevated unpackaged app, so a fixed, predictable folder is used instead.)
    /// </summary>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (s_cached is null)
        {
            return;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"hwsuite-{s_cached.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        using (var file = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            new HtmlReportWriter().Write(s_cached, App.Text, file);
        }

        StatusText.Text = App.Text.Format("app.reportSaved", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Writes each available video BIOS to Documents\HWSuite and shows it in Explorer.</summary>
    private void SaveVbiosButton_Click(object sender, RoutedEventArgs e)
    {
        if (s_cached is null)
        {
            return;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HWSuite");
        Directory.CreateDirectory(folder);
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
