using HWSuite.Core.Benchmark;
using HWSuite.Core.Reporting;
using HWSuite.Providers.Windows;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HWSuite.Gui.Pages;

/// <summary>The disk part of the Benchmark page: CrystalDiskMark-style tests on a chosen drive.</summary>
public sealed partial class BenchmarkPage
{
    private static readonly int[] DiskSizesGb = [1, 4];
    private IReadOnlyList<DiskBenchmarkDrive> _drives = [];
    private CancellationTokenSource? _diskRunning;

    private void InitializeDisk()
    {
        var s = App.Text;
        DiskHeader.Text = s["app.bench.diskSection"];
        DriveBox.Header = s["app.disk.drive"];
        DiskSizeBox.Header = s["app.disk.size"];
        DiskStartButton.Content = s["app.bench.start"];
        DiskNoteText.Text = s["app.disk.note"];
        foreach (var gb in DiskSizesGb)
        {
            DiskSizeBox.Items.Add($"{gb} GB");
        }

        DiskSizeBox.SelectedIndex = 0;
        ShowDiskTable(null);
    }

    private async Task LoadDisksAsync()
    {
        if (_diskRunning is not null)
        {
            return; // keep the list (and the selection) while a test runs
        }

        var s = App.Text;
        var previous = DriveBox.SelectedIndex >= 0 && DriveBox.SelectedIndex < _drives.Count ? _drives[DriveBox.SelectedIndex].Letter : null;
        _drives = await Task.Run(DiskBenchmarkTargets.Drives);
        DriveBox.Items.Clear();
        foreach (var d in _drives)
        {
            var name = string.Join(" ", new[] { d.Model, d.Bus is null ? null : $"({d.Bus})" }.OfType<string>());
            DriveBox.Items.Add(s.Format("app.disk.driveItem", d.Letter, name.Length > 0 ? name : d.Label ?? "", ReportModel.Bytes(d.FreeBytes, s.Culture), ReportModel.Bytes(d.TotalBytes, s.Culture)));
        }

        DriveBox.SelectedIndex = _drives.Count == 0 ? -1 : Math.Max(0, _drives.ToList().FindIndex(d => d.Letter == previous));
        DiskStartButton.IsEnabled = _drives.Count > 0;
        DiskStatusText.Text = _drives.Count == 0 ? s["app.disk.none"] : "";
    }

    private async void DiskStartButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        if (_diskRunning is not null)
        {
            await _diskRunning.CancelAsync();
            return;
        }

        if (DriveBox.SelectedIndex < 0 || DriveBox.SelectedIndex >= _drives.Count)
        {
            return;
        }

        var drive = _drives[DriveBox.SelectedIndex];
        var bytes = (long)DiskSizesGb[Math.Max(0, DiskSizeBox.SelectedIndex)] << 30;
        if (drive.FreeBytes < bytes + (1L << 30))
        {
            DiskStatusText.Text = s.Format("app.disk.noSpace", drive.Letter, ReportModel.Bytes(bytes, s.Culture));
            return;
        }

        using var cancel = new CancellationTokenSource();
        _diskRunning = cancel;
        DiskStartButton.Content = s["app.bench.cancel"];
        DriveBox.IsEnabled = DiskSizeBox.IsEnabled = false;
        ShowDiskTable(null);
        var progress = new Progress<(double Fraction, string Phase)>(p =>
        {
            DiskProgress.Value = p.Fraction;
            DiskStatusText.Text = p.Phase switch
            {
                "done" => "",
                "prepare" => s["app.disk.phase.prepare"],
                _ => s.Format("app.disk.phase", p.Phase[..p.Phase.LastIndexOf(' ')], s[p.Phase.EndsWith("read", StringComparison.Ordinal) ? "app.disk.read.short" : "app.disk.write.short"]),
            };
        });

        try
        {
            var result = await Task.Run(() =>
            {
                var path = Path.Combine(DiskBenchmarkTargets.TestFolder(drive.Letter), "hwsuite-disk-test.tmp");
                return DiskBenchmark.Run(path, bytes, TimeSpan.FromSeconds(5), DiskBenchmarkTargets.Open, drive.Letter, drive.Model, progress: progress, cancel: cancel.Token);
            });
            ShowDiskTable(result);
            DiskBenchmarkHistory.Append(BenchmarkFiles.Disk, result);
            DiskStatusText.Text = s["app.bench.savedHint"];
        }
        catch (OperationCanceledException)
        {
            DiskStatusText.Text = s["bench.cancelled"];
            DiskProgress.Value = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiskStatusText.Text = ex.Message;
            DiskProgress.Value = 0;
        }
        finally
        {
            _diskRunning = null;
            DiskStartButton.Content = s["app.bench.start"];
            DriveBox.IsEnabled = DiskSizeBox.IsEnabled = true;
        }
    }

    /// <summary>Test | Read MB/s | Write MB/s | IOPS (random tests); dashes before a run.</summary>
    private void ShowDiskTable(DiskBenchmarkResult? result)
    {
        var s = App.Text;
        var c = s.Culture;
        var grid = DiskGrid;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        foreach (var width in new[] { GridLength.Auto, new GridLength(130), new GridLength(130), GridLength.Auto })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        Row(0, s["app.disk.test"], s["app.disk.read"], s["app.disk.write"], s["app.disk.iops"], header: true);
        var index = 1;
        foreach (var test in DiskTest.Standard)
        {
            var r = result?.Tests.FirstOrDefault(x => x.Name == test.Name);
            Row(index++, test.Name,
                r is null ? "—" : r.ReadMBps.ToString("N1", c),
                r is null ? "—" : r.WriteMBps.ToString("N1", c),
                r is null || !test.Random ? "" : $"{r.ReadIops.ToString("N0", c)} / {r.WriteIops.ToString("N0", c)}",
                header: false);
        }

        void Row(int row, string name, string read, string write, string iops, bool header)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = new[] { name, read, write, iops };
            for (var col = 0; col < cells.Length; col++)
            {
                var text = new TextBlock
                {
                    Text = cells[col],
                    FontWeight = header || col == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    HorizontalAlignment = col is 1 or 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
                if (header)
                {
                    text.Foreground = secondary;
                }
                else if (col is 1 or 2)
                {
                    text.Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"];
                }

                Grid.SetRow(text, row);
                Grid.SetColumn(text, col);
                grid.Children.Add(text);
            }
        }
    }
}
