using System.Diagnostics;
using System.Windows;

namespace ROROROblox.App.Modals;

internal partial class RobloxNotInstalledWindow : Window
{
    private const string DownloadUrl = "https://www.roblox.com/download";
    private const string BloxstrapUrl = "https://github.com/pizzaboxer/bloxstrap#configuration";

    public RobloxNotInstalledWindow()
    {
        InitializeComponent();
    }

    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(DownloadUrl);
        DialogResult = true;
        Close();
    }

    private void OnBloxstrapClick(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(BloxstrapUrl);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
        }
    }
}
