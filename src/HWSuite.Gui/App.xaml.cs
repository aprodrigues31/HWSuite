using HWSuite.Core.Localization;
using HWSuite.Gui.Services;
using Microsoft.UI.Xaml;

namespace HWSuite.Gui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>UI text in the system language (Portuguese or English).</summary>
    public static Strings Text { get; } = Strings.For((string?)null);

    /// <summary>Shared sensor polling, used by every page that shows live values.</summary>
    public static SensorMonitor Monitor { get; } = new();

    public static MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Closed += (_, _) => Monitor.Dispose();
        MainWindow.Activate();
        Monitor.Start();
    }
}
