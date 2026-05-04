using System.Diagnostics;
using System.Windows;

namespace ROROROblox.App.Modals;

internal partial class WebView2NotInstalledWindow : Window
{
    private const string EvergreenInstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const string LearnMoreUrl = "https://learn.microsoft.com/en-us/microsoft-edge/webview2/";

    public WebView2NotInstalledWindow()
    {
        InitializeComponent();
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(EvergreenInstallerUrl);
        DialogResult = true;
        Close();
    }

    private void OnLearnMoreClick(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(LearnMoreUrl);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // If the OS can't open URLs at all, the user has bigger problems than our modal.
        }
    }
}
