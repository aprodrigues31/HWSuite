using HWSuite.Core;
using HWSuite.Gui.Pages;
using HWSuite.Gui.Services;
using HWSuite.Providers.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace HWSuite.Gui;

public sealed partial class MainWindow : Window
{
    private bool _installing;

    public MainWindow()
    {
        InitializeComponent();
        var s = App.Text;
        Title = $"HWSuite {BuildInfo.Current.Display}";
        AppWindow.Resize(new SizeInt32(1180, 820));

        NavSensors.Content = s["app.nav.sensors"];
        NavInventory.Content = s["app.nav.inventory"];
        NavAbout.Content = s["app.nav.about"];
        Nav.SelectedItem = NavSensors;

        UpdateAccessBar(App.Monitor.Access);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
        {
            "inventory" => typeof(InventoryPage),
            "about" => typeof(AboutPage),
            _ => typeof(SensorsPage),
        };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    /// <summary>Explains what limits the sensors and offers the one action that fixes it.</summary>
    private void UpdateAccessBar(AccessStatus access)
    {
        var s = App.Text;
        if (!access.IsElevated)
        {
            AccessBar.Title = s["app.notElevatedTitle"];
            AccessBar.Message = s["app.notElevated"];
            AccessButton.Content = s["app.reopenAdmin"];
            AccessButton.Visibility = Visibility.Visible;
            AccessBar.Severity = InfoBarSeverity.Warning;
            AccessBar.IsOpen = true;
        }
        else if (!access.PawnIoInstalled)
        {
            AccessBar.Title = s["app.noDriverTitle"];
            AccessBar.Message = s["app.noDriver"];
            AccessButton.Content = s["app.installDriver"];
            AccessButton.Visibility = Visibility.Visible;
            AccessBar.Severity = InfoBarSeverity.Warning;
            AccessBar.IsOpen = true;
        }
        else
        {
            AccessBar.IsOpen = false;
        }
    }

    private async void AccessButton_Click(object sender, RoutedEventArgs e)
    {
        var access = App.Monitor.Access;
        if (!access.IsElevated)
        {
            if (Elevation.RelaunchElevated())
            {
                Close(); // the elevated copy takes over
            }

            return;
        }

        if (access.PawnIoInstalled || _installing)
        {
            return;
        }

        _installing = true;
        AccessButton.Visibility = Visibility.Collapsed;
        AccessBar.Severity = InfoBarSeverity.Informational;
        AccessBar.Message = App.Text["app.installingDriver"];

        var s = App.Text;
        var result = await Task.Run(() => PawnIoInstaller.Install((step, detail) =>
            DispatcherQueue.TryEnqueue(() => AccessBar.Message = s.Format($"driver.step.{step}", detail ?? "—"))));

        _installing = false;
        AccessBar.Message = s.Format($"driver.outcome.{result.Outcome}", result.Detail ?? "");
        if (result.Succeeded)
        {
            AccessBar.Severity = InfoBarSeverity.Success;
            App.Monitor.Start(); // reopen the hardware so CPU and motherboard sensors appear
            AccessBar.Message = s["app.driverInstalled"];
            await Task.Delay(TimeSpan.FromSeconds(3));
            UpdateAccessBar(App.Monitor.Access);
        }
        else
        {
            AccessBar.Severity = InfoBarSeverity.Error;
            AccessButton.Visibility = Visibility.Visible;
        }
    }
}
