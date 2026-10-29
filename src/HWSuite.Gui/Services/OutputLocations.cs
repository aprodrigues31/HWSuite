using HWSuite.Core.Benchmark;
using HWSuite.Core.Maintenance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HWSuite.Gui.Services;

/// <summary>
/// The user decides where files go. The reports folder and the company logo come from Settings; when they were never
/// chosen, the first report asks (with a "remember this choice" box) instead of writing somewhere on its own.
/// </summary>
internal static class OutputLocations
{
    /// <summary>Suggested folder in the dialog: Documents\HWSuite\Reports, or HWSuite\Reports next to a portable copy.</summary>
    public static string SuggestedReportsFolder => MaintenanceSession.ReportsFolder;

    /// <summary>The folder to save reports in, asking the first time; null when the user cancels.</summary>
    public static async Task<string?> ReportsFolderAsync(XamlRoot root)
    {
        if (App.Settings.ReportsFolder is { Length: > 0 } saved && Directory.Exists(saved))
        {
            return saved;
        }

        var s = App.Text;
        var chosen = App.Settings.ReportsFolder is { Length: > 0 } missing ? missing : SuggestedReportsFolder;
        var path = new TextBlock { Text = chosen, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = Hud.HudUi.Font("HudMonoFont"), FontSize = 13 };
        var browse = new Button { Content = s["files.browse"] };
        browse.Click += (_, _) =>
        {
            if (ShellDialogs.PickFolder(s["files.reports.pick"], path.Text) is { } folder)
            {
                path.Text = folder;
            }
        };
        var remember = new CheckBox { Content = s["files.remember"], IsChecked = true };
        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = s["files.reports.body"], TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(path);
        panel.Children.Add(browse);
        panel.Children.Add(remember);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = s["files.reports.title"],
            Content = panel,
            PrimaryButtonText = s["files.save"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(path.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (remember.IsChecked == true)
        {
            var folder = Core.Settings.DataLocation.StorePath(path.Text);
            App.Settings.Update(x => x with { ReportsFolder = folder });
        }

        return path.Text;
    }

    /// <summary>
    /// The logo for a report as a data URI: the saved choice, or asked the first time ("no logo" is a valid answer).
    /// Returns (false, null) when the user cancels the report.
    /// </summary>
    public static async Task<(bool Proceed, string? LogoDataUri)> LogoAsync(XamlRoot root)
    {
        var saved = App.Settings.LogoFile;
        if (saved is "")
        {
            return (true, null); // remembered: reports without a logo
        }

        if (saved is not null && MaintenanceSession.LogoFromFile(saved) is { } uri)
        {
            return (true, uri);
        }

        var s = App.Text;
        string? file = saved is not null && File.Exists(saved) ? saved : MaintenanceSession.LogoPath();
        var name = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = Hud.HudUi.Font("HudMonoFont"), FontSize = 13 };
        var image = new Image { MaxHeight = 80, MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        void Show()
        {
            name.Text = file ?? s["files.logo.none"];
            image.Source = file is not null && !file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(file))
                : null;
        }

        Show();
        var browse = new Button { Content = s["files.browse"] };
        browse.Click += (_, _) =>
        {
            if (ShellDialogs.PickFile(s["files.logo.pick"], file ?? BenchmarkFiles.Folder, (s["files.logo.images"], "*.png;*.jpg;*.jpeg;*.svg")) is { } picked)
            {
                file = picked;
                Show();
            }
        };
        var remember = new CheckBox { Content = s["files.remember"], IsChecked = true };
        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock { Text = s["files.logo.body"], TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(image);
        panel.Children.Add(name);
        panel.Children.Add(browse);
        panel.Children.Add(remember);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = s["files.logo.title"],
            Content = panel,
            PrimaryButtonText = s["files.logo.use"],
            SecondaryButtonText = s["files.logo.without"],
            CloseButtonText = s["app.clear.cancel"],
            DefaultButton = ContentDialogButton.Primary,
        };

        var answer = await dialog.ShowAsync();
        if (answer == ContentDialogResult.None)
        {
            return (false, null);
        }

        var use = answer == ContentDialogResult.Primary && file is not null ? file : null;
        if (remember.IsChecked == true)
        {
            var keep = use is null ? "" : Core.Settings.DataLocation.StorePath(use);
            App.Settings.Update(x => x with { LogoFile = keep });
        }

        return (true, use is null ? null : MaintenanceSession.LogoFromFile(use));
    }
}
