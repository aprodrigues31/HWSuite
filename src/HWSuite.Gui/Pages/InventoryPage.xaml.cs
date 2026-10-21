using System.Diagnostics;
using System.Text;
using HWSuite.Core.Collection;
using HWSuite.Core.Model;
using HWSuite.Core.Reporting;
using HWSuite.Hosting;
using Microsoft.UI.Text;
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
        PageTitle.Text = s["app.nav.inventory"];
        SaveButton.Content = s["app.saveReport"];
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
            card.Children.Add(new TextBlock { Text = section.Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });

            foreach (var group in section.Groups)
            {
                if (group.Title is not null)
                {
                    card.Children.Add(new TextBlock { Text = group.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
                }

                var rows = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
                rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                for (var i = 0; i < group.Rows.Count; i++)
                {
                    rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var label = new TextBlock { Text = group.Rows[i].Label, Foreground = secondary, TextWrapping = TextWrapping.Wrap };
                    var value = new TextBlock { Text = group.Rows[i].Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                    Grid.SetRow(label, i);
                    Grid.SetRow(value, i);
                    Grid.SetColumn(value, 1);
                    rows.Children.Add(label);
                    rows.Children.Add(value);
                }

                card.Children.Add(rows);
            }

            Sections.Children.Add(new Border
            {
                Child = card,
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            });
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
}
