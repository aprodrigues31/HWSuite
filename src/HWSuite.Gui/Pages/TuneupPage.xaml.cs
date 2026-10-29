using HWSuite.Core.Maintenance;
using HWSuite.Gui.Hud;
using HWSuite.Providers.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HWSuite.Gui.Pages;

/// <summary>
/// Windows maintenance: disk cleanup, drive optimization (TRIM / defragmentation), Windows Update, Microsoft Store apps
/// and programs through winget, one after the other, with what each did. The one-line summaries go into the services
/// of the next maintenance report (<see cref="App.ServicesDone"/>).
/// </summary>
public sealed partial class TuneupPage : Page
{
    private readonly Dictionary<TuneupTasks, CheckBox> _tasks = [];
    private CheckBox? _recycle;
    private CheckBox? _previous;
    private CancellationTokenSource? _cancel;

    public TuneupPage()
    {
        InitializeComponent();
        var s = App.Text;
        var c = s.Culture;
        HeaderTag.Text = "// HWSUITE · " + s["rep.tag"].ToUpper(c);
        PageTitle.Text = s["tune.title"].ToUpper(c);
        Subtitle.Text = s["tune.subtitle"];
        RunButton.Content = s["tune.run"].ToUpper(c);
        StopButton.Content = s["tune.stop"];
        AdminBar.Message = s["tune.needsAdmin"];

        Add(TuneupTasks.Cleanup, "tune.cleanup", "tune.cleanup.hint", true, extra =>
        {
            _recycle = new CheckBox { Content = s["tune.recycle"] };
            _previous = new CheckBox { Content = s["tune.previous"] };
            extra.Children.Add(_recycle);
            extra.Children.Add(_previous);
        });
        Add(TuneupTasks.Optimize, "tune.optimize", "tune.optimize.hint", true);
        Add(TuneupTasks.WindowsUpdate, "tune.wu", "tune.wu.hint", true);
        Add(TuneupTasks.StoreApps, "tune.store", "tune.store.hint", true);
        Add(TuneupTasks.DesktopApps, "tune.apps", "tune.apps.hint", false);

        Loaded += (_, _) =>
        {
            var admin = App.Monitor.Access.IsElevated;
            AdminBar.IsOpen = !admin && !App.IsDemo;
            RunButton.IsEnabled = admin || App.IsDemo;
        };
    }

    private void Add(TuneupTasks task, string title, string hint, bool on, Action<StackPanel>? extra = null)
    {
        var s = App.Text;
        var box = new CheckBox { Content = s[title], IsChecked = on, FontFamily = HudUi.Font("HudBodyMediumFont"), FontSize = 15 };
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(box);
        content.Children.Add(new TextBlock { Text = s[hint], TextWrapping = TextWrapping.Wrap, Foreground = HudUi.TextDim, FontSize = 13, Margin = new Thickness(28, 0, 0, 0) });
        if (extra is not null)
        {
            var more = new StackPanel { Spacing = 2, Margin = new Thickness(28, 4, 0, 0) };
            extra(more);
            content.Children.Add(more);
        }

        Tasks.Children.Add(new HudPanel { Content = content });
        _tasks[task] = box;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        var chosen = _tasks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToList();
        if (chosen.Count == 0 || App.IsDemo)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s["tune.confirm.title"],
            Content = new TextBlock { Text = s["tune.confirm.body"], TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = s["tune.confirm.ok"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        Progress.Visibility = Visibility.Visible;
        Results.Children.Clear();
        var options = new CleanupOptions(_recycle?.IsChecked == true, _previous?.IsChecked == true);
        void Log(string text) => DispatcherQueue.TryEnqueue(() => StatusText.Text = text);
        var reboot = false;
        try
        {
            foreach (var task in chosen)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var outcome = await Task.Run(() => task switch
                {
                    TuneupTasks.Cleanup => WindowsTuneup.Cleanup(options, s, Log, token),
                    TuneupTasks.Optimize => WindowsTuneup.Optimize(s, Log, token),
                    TuneupTasks.WindowsUpdate => WindowsTuneup.WindowsUpdate(s, Log, token),
                    TuneupTasks.StoreApps => WindowsTuneup.StoreApps(s, Log, token),
                    _ => WindowsTuneup.DesktopApps(s, Log, token),
                }, CancellationToken.None);
                reboot |= outcome.RebootRequired;
                App.ServicesDone.Add(outcome.Summary);
                Results.Children.Add(ResultBlock(outcome));
            }

            StatusText.Text = s["tune.done"] + (reboot ? " " + s["tune.rebootNow"] : "");
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private static StackPanel ResultBlock(TuneupOutcome outcome)
    {
        var block = new StackPanel { Spacing = 2, Padding = new Thickness(0, 6, 0, 6), BorderBrush = HudUi.Brush("HudAccentFaintBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        block.Children.Add(new TextBlock
        {
            Text = (outcome.Succeeded ? "✓ " : "! ") + outcome.Summary,
            FontFamily = HudUi.Font("HudBodyMediumFont"),
            FontSize = 14,
            Foreground = outcome.Succeeded ? HudUi.Brush("HudGoodBrush") : HudUi.Warn,
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var line in outcome.Details.Take(40))
        {
            block.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = HudUi.TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 0, 0, 0) });
        }

        return block;
    }
}
