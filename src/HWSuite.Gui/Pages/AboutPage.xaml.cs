using HWSuite.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HWSuite.Gui.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        PageTitle.Text = App.Text["app.nav.about"].ToUpper(App.Text.Culture);
        AboutText.Text = App.Text.Format("app.about", BuildInfo.Current.Display);
        CheckButton.Content = App.Text["app.update.check"];
        VersionTag.Text = $"// {BuildInfo.Current.Display} · MPL-2.0".ToUpper(App.Text.Culture);
        FontsText.Text = App.Text["about.fonts"];
    }

    /// <summary>Checks GitHub now; a newer release also opens the update bar with its download button.</summary>
    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Text;
        CheckButton.IsEnabled = false;
        Checking.IsActive = true;
        UpdateText.Text = s["app.update.checking"];
        try
        {
            var result = await App.FindUpdateAsync();
            UpdateText.Text = result switch
            {
                null => s["app.update.failed"],
                { Newer: true } r => s.Format("app.update.title", r.Release.Version),
                _ => s.Format("app.update.latest", BuildInfo.Current.Version),
            };
        }
        finally
        {
            Checking.IsActive = false;
            CheckButton.IsEnabled = true;
        }
    }
}
