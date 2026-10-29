using System.Diagnostics;
using HWSuite.Core.Benchmark;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Saved benchmark results as tables, one tab per kind of test: newest first, numbers right-aligned, a bar
/// comparing each run with the best one, the best run highlighted. Clearing (by kind) lives here too.
/// </summary>
public sealed partial class BenchmarkResultsPage : Page
{
    private const double BarWidth = 120;
    private string _tab = "cpu";

    public BenchmarkResultsPage()
    {
        InitializeComponent();
        var s = App.Text;
        PageTitle.Text = s["results.title"];
        Subtitle.Text = s["results.subtitle"];
        CpuTab.Text = s["app.clear.cpu"];
        GpuTab.Text = s["app.clear.gpu"];
        DiskTab.Text = s["app.clear.disk"];
        ClearButton.Content = s["app.clear.button"];
        FolderButton.Content = s["results.openFolder"];
        Loaded += (_, _) => Show();
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _tab = sender.SelectedItem?.Tag as string ?? "cpu";
        Show();
    }

    private void Show()
    {
        var s = App.Text;
        ResultTable table;
        try
        {
            table = _tab switch
            {
                "gpu" => ResultTables.Gpu(GpuBenchmarkHistory.Read(BenchmarkFiles.Gpu), s),
                "disk" => ResultTables.Disk(DiskBenchmarkHistory.Read(BenchmarkFiles.Disk), s),
                _ => ResultTables.Cpu(BenchmarkHistory.Read(BenchmarkFiles.Cpu), s),
            };
        }
        catch (IOException ex)
        {
            Table.Children.Clear();
            Table.Children.Add(new TextBlock { Text = ex.Message, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap });
            return;
        }

        CountText.Text = s.Format("results.count", table.Rows.Count);
        Render(table);
    }

    private void Render(ResultTable table)
    {
        var s = App.Text;
        Table.Children.Clear();
        if (table.Rows.Count == 0)
        {
            Table.Children.Add(new TextBlock
            {
                Text = s["results.empty"],
                Margin = new Thickness(16, 24, 16, 24),
                Foreground = Brush("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        Table.Children.Add(Line(table, table.Columns.Select(c => c.Header).ToList(), header: true, bar: null, best: false, odd: false));
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            Table.Children.Add(Line(table, row.Cells, header: false, row.Bar, row.IsBest, odd: i % 2 == 1));
        }
    }

    /// <summary>One table line: fixed column widths so every line aligns; the bar column draws the bar before the value.</summary>
    private Border Line(ResultTable table, IReadOnlyList<string> cells, bool header, double? bar, bool best, bool odd)
    {
        var s = App.Text;
        var grid = new Grid { ColumnSpacing = 20 };
        for (var col = 0; col < table.Columns.Count; col++)
        {
            // Date and name fixed; number columns sized by their header (about 7 px per character, at most two lines).
            var headerWidth = Math.Clamp(table.Columns[col].Header.Length * 7.0, 90, 200);
            var width = col switch
            {
                0 => 140,
                1 => 280,
                _ when col == table.BarColumn => Math.Max(250, headerWidth),
                _ => headerWidth,
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        for (var col = 0; col < cells.Count && col < table.Columns.Count; col++)
        {
            var numeric = table.Columns[col].Numeric;
            var text = new TextBlock
            {
                Text = cells[col],
                TextWrapping = header ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = header || (best && col == table.BarColumn) ? FontWeights.SemiBold : FontWeights.Normal,
            };

            // Only headers get a color; a null Foreground would make the text transparent, not the default color.
            if (header)
            {
                text.Foreground = Brush("TextFillColorSecondaryBrush");
            }
            if (!header && col == 1)
            {
                ToolTipService.SetToolTip(text, cells[col]); // full name when trimmed
            }

            FrameworkElement cell = text;
            if (!header && col == table.BarColumn && bar is { } fraction)
            {
                // Bar + value, the value right-aligned so numbers of all rows line up.
                var panel = new Grid { ColumnSpacing = 8 };
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth) });
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
                track.Children.Add(new Rectangle { Fill = Brush("ControlFillColorSecondaryBrush"), RadiusX = 4, RadiusY = 4 });
                track.Children.Add(new Rectangle
                {
                    Width = Math.Max(4, fraction * BarWidth),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Fill = Brush(best ? "AccentFillColorDefaultBrush" : "ControlStrongFillColorDefaultBrush"),
                    RadiusX = 4,
                    RadiusY = 4,
                });
                Grid.SetColumn(text, 1);
                panel.Children.Add(track);
                panel.Children.Add(text);
                cell = panel;
            }
            else if (!header && best && col == 0)
            {
                // "★ best" next to the date of the best run.
                var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                stack.Children.Add(text);
                stack.Children.Add(new Border
                {
                    Background = Brush("AccentFillColorDefaultBrush"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 0, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = s["results.best"], FontSize = 11, Foreground = Brush("TextOnAccentFillColorPrimaryBrush") },
                });
                cell = stack;
            }

            Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }

        var line = new Border
        {
            Child = grid,
            Padding = new Thickness(12, header ? 10 : 8, 12, header ? 10 : 8),
            CornerRadius = new CornerRadius(4),
        };
        if (odd)
        {
            line.Background = Brush("SubtleFillColorSecondaryBrush");
        }

        if (header)
        {
            line.BorderThickness = new Thickness(0, 0, 0, 1);
            line.BorderBrush = Brush("DividerStrokeColorDefaultBrush");
        }

        return line;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BenchmarkFiles.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BenchmarkFiles.Folder}\"") { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Asks which results to delete (nothing is checked at first) and deletes only those.</summary>
    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        var cpu = new CheckBox { Content = s["app.clear.cpu"] };
        var gpu = new CheckBox { Content = s["app.clear.gpu"] };
        var disk = new CheckBox { Content = s["app.clear.disk"] };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = s["app.clear.message"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(cpu);
        panel.Children.Add(gpu);
        panel.Children.Add(disk);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s["app.clear.title"],
            Content = panel,
            PrimaryButtonText = s["app.clear.confirm"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
        };

        void Update(object? o, RoutedEventArgs a) => dialog.IsPrimaryButtonEnabled = cpu.IsChecked == true || gpu.IsChecked == true || disk.IsChecked == true;
        foreach (var box in new[] { cpu, gpu, disk })
        {
            box.Checked += Update;
            box.Unchecked += Update;
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var kinds = (cpu.IsChecked == true ? BenchmarkKinds.Cpu : BenchmarkKinds.None)
            | (gpu.IsChecked == true ? BenchmarkKinds.Gpu : BenchmarkKinds.None)
            | (disk.IsChecked == true ? BenchmarkKinds.Disk : BenchmarkKinds.None);
        try
        {
            BenchmarkFiles.Clear(kinds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Subtitle.Text = ex.Message;
        }

        Show();
    }
}
