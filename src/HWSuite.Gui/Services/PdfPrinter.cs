using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace HWSuite.Gui.Services;

/// <summary>Prints a saved HTML file to an A4 PDF beside it, through a (hidden) WebView2 on the page.</summary>
internal static class PdfPrinter
{
    /// <summary>Returns the PDF path; throws <see cref="InvalidOperationException"/> when WebView2 cannot render it.</summary>
    public static async Task<string> PrintAsync(WebView2 view, string htmlPath, bool landscape = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        await view.EnsureCoreWebView2Async();
        var core = view.CoreWebView2;
        var loaded = new TaskCompletionSource<bool>();
        void Done(CoreWebView2 _, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Done;
        try
        {
            core.Navigate(new Uri(htmlPath).AbsoluteUri);
            if (!await loaded.Task)
            {
                throw new InvalidOperationException(App.Text["rep.pdfFailed"]);
            }
        }
        finally
        {
            core.NavigationCompleted -= Done;
        }

        var settings = core.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = landscape ? 0.4 : 0;
        settings.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
        settings.PageWidth = 8.27;
        settings.PageHeight = 11.69;
        var pdf = Path.ChangeExtension(htmlPath, ".pdf");
        if (!await core.PrintToPdfAsync(pdf, settings))
        {
            throw new InvalidOperationException(App.Text["rep.pdfFailed"]);
        }

        return pdf;
    }

    public static void Open(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
