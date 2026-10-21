using HWSuite.Core;
using Microsoft.UI.Xaml.Controls;

namespace HWSuite.Gui.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        PageTitle.Text = App.Text["app.nav.about"];
        AboutText.Text = App.Text.Format("app.about", BuildInfo.Current.Display);
    }
}
