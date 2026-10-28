using System.Diagnostics;
using HWSuite.Core;
using HWSuite.Core.Benchmark;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HWSuite.Gui.Pages;

/// <summary>
/// The processor comparison: the run among other processors measured with the same test (shipped
/// references and the CSVs in Documents\HWSuite\referencias), a verdict against the same model, and the
/// buttons to send the result to the project and to open the references folder.
/// </summary>
public sealed partial class BenchmarkPage
{
    private const double BarWidth = 260;
    private BenchmarkResult? _lastResult;

    private void InitializeComparison()
    {
        var s = App.Text;
        ComparisonTitle.Text = s["app.compare.title"];
        SubmitButton.Content = s["app.compare.submit"];
        ReferencesButton.Content = s["app.compare.folder"];
        ComparisonNote.Text = s["app.compare.note"];
    }

    private void ShowComparison(BenchmarkResult? result)
    {
        if (result is null)
        {
            return;
        }

        var s = App.Text;
        var c = s.Culture;
        var comparison = BenchmarkComparison.Build(result, s_processor, BenchmarkComparison.Builtin(), BenchmarkComparison.Local(BenchmarkComparison.LocalFolder));
        ComparisonPanel.Visibility = Visibility.Visible;
        SubmitButton.IsEnabled = true;
        VerdictText.Text = comparison.Verdict switch
        {
            ComparisonVerdict.NoReference => s["app.compare.noReference"],
            _ => s.Format($"app.compare.{comparison.Verdict}",
                comparison.SinglePercent!.Value.ToString("0", c), comparison.MultiPercent!.Value.ToString("0", c), comparison.SameModelRuns),
        };

        var grid = ComparisonGrid;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        foreach (var width in new[] { GridLength.Auto, new GridLength(BarWidth), GridLength.Auto, GridLength.Auto })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var neutral = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"];
        AddRow(0, s["app.compare.processor"], null, s["bench.phase.multi"], s["bench.phase.single"], header: true);
        var max = comparison.Rows.Max(r => r.MultiThread);
        var row = 1;
        foreach (var r in comparison.Rows)
        {
            var current = r.Source == ReferenceSource.Current;
            var name = current ? s.Format("app.compare.you", r.Processor) : r.Processor + (r.Runs > 1 ? $" ({r.Runs})" : "");
            AddRow(row++, name, max > 0 ? r.MultiThread / max : 0, CpuBenchmark.Points(r.MultiThread, c), CpuBenchmark.Points(r.SingleThread, c), header: false, current, current ? accent : neutral);
        }

        void AddRow(int index, string name, double? fraction, string multi, string single, bool header, bool bold = false, Brush? bar = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var weight = bold || header ? FontWeights.SemiBold : FontWeights.Normal;
            var foreground = header ? secondary : null;
            Add(new TextBlock { Text = name, FontWeight = weight, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 }, 0);
            if (fraction is { } f)
            {
                Add(new Rectangle { Width = Math.Max(2, f * BarWidth), Height = 12, Fill = bar, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }, 1);
            }

            Add(new TextBlock { Text = multi, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Right }, 2);
            Add(new TextBlock { Text = single, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Right }, 3);

            void Add(FrameworkElement element, int column)
            {
                if (foreground is not null && element is TextBlock text)
                {
                    text.Foreground = foreground;
                }

                Grid.SetRow(element, index);
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }
        }
    }

    /// <summary>Opens a pre-filled GitHub issue in the browser; the user reviews it and submits it there.</summary>
    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null)
        {
            return;
        }

        var link = BenchmarkComparison.SubmissionLink(_lastResult, s_processor, memory: null, BuildInfo.Current.Display);
        Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        StatusText.Text = App.Text["app.compare.submitted"];
    }

    private void ReferencesButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = BenchmarkComparison.LocalFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
    }
}
