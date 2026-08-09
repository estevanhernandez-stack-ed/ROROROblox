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

    /// <summary>
    /// Whether the automatic first-run tour should appear. Pure, so the rule is testable without
    /// touching %LOCALAPPDATA% or constructing a Window.
    /// <para>
    /// The caller must mark the sentinel only when this returns true. Marking unconditionally is the
    /// bug this replaces: an upgrading user with accounts burned the sentinel and never saw the tour.
    /// </para>
    /// </summary>
    internal static bool ShouldShowOnStartup(bool isFirstRun, int accountCount) =>
        isFirstRun && accountCount == 0;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Show the tour on demand (Tools menu). Deliberately does NOT touch the sentinel: opening the
    /// tour manually says nothing about whether the automatic first-run path has run.
    /// <para>
    /// Owner resolution checks visibility, not just loaded state: the main window's X-close handler
    /// cancels close and calls <c>Hide()</c> as the minimize-to-tray path, which leaves
    /// <c>IsLoaded</c> true even though nothing is on screen. The tray can fire this tour with the
    /// main window hidden that way, and an invisible owner would otherwise still receive activation
    /// back on dismissal.
    /// </para>
    /// </summary>
    internal static void ShowTour()
    {
        var dialog = new WelcomeWindow();
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
    }
}
