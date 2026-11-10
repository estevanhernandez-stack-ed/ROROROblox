using System.Diagnostics;
using System.Windows;

namespace ROROROblox.App.Modals;

internal partial class RobloxNotInstalledWindow : Window
{
    private const string DownloadUrl = "https://www.roblox.com/download";
    private const string BloxstrapUrl = "https://github.com/pizzaboxer/bloxstrap#configuration";

    /// <param name="storeEditionFound">
    /// True when the Microsoft Store edition of Roblox is on this PC. That edition cannot be
    /// launched by RoRoRo (it never registers <c>roblox-player:</c>), and the modal says so.
    /// </param>
    public RobloxNotInstalledWindow(bool storeEditionFound = false)
    {
        InitializeComponent();
        StoreEditionLine.Visibility = storeEditionFound ? Visibility.Visible : Visibility.Collapsed;

        // F-054. Informational — its ShowDialog result is ignored — so Esc simply dismisses.
        Controls.EscapeToDismiss.Wire(this);
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
