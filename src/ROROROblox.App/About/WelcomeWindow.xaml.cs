using System.IO;
using System.Windows;

namespace ROROROblox.App.About;

internal partial class WelcomeWindow : Window
{
    /// <summary>
    /// Sentinel file written after the welcome modal is shown. Absent file = first run.
    /// Lives next to <c>accounts.dat</c> + <c>settings.json</c> in the per-user data directory.
    /// </summary>
    private static readonly string SentinelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        ".welcome-shown");

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// True when this is the user's first run (or the sentinel file was deleted). Probes for
    /// the sentinel under <c>%LOCALAPPDATA%\ROROROblox\.welcome-shown</c>.
    /// </summary>
    public static bool IsFirstRun()
    {
        try
        {
            return !File.Exists(SentinelPath);
        }
        catch
        {
            // If the LOCALAPPDATA probe itself fails, assume not-first-run rather than spamming
            // the modal on every launch.
            return false;
        }
    }

    /// <summary>Drop the sentinel so subsequent launches don't re-show the welcome modal.</summary>
    public static void MarkShown()
    {
        try
        {
            var dir = Path.GetDirectoryName(SentinelPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(SentinelPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Best-effort.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
