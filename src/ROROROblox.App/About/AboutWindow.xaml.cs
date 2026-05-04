using System.Diagnostics;
using System.Windows;
using ROROROblox.App.Logging;

namespace ROROROblox.App.About;

internal partial class AboutWindow : Window
{
    private const string RepoUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox";
    private const string IssuesUrl = "https://github.com/estevanhernandez-stack-ed/ROROROblox/issues";

    public AboutWindow()
    {
        InitializeComponent();
        var version = typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = $"v{version}";
    }

    private void OnRepoClick(object sender, RoutedEventArgs e) => OpenUrl(RepoUrl);
    private void OnIssuesClick(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);

    private void OnLicenseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogging.LogDirectory,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch
        {
            // Best-effort.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best-effort.
        }
    }
}
