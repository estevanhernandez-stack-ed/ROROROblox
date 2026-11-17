using ROROROblox.App.AppLifecycle;
using System.ComponentModel;
using System.IO;
using System.Windows;

namespace ROROROblox.App.About;

internal partial class WelcomeWindow : Window
{
    /// <summary>
    /// Sentinel file written after the welcome modal is shown. Absent file = first run.
    /// Lives next to <c>accounts.dat</c> + <c>settings.json</c> in the per-user data directory.
    /// </summary>
    internal static string SentinelPath => ROROROblox.Core.AppDataRoot.Combine(".welcome-shown");

    private ExternalCloseWatcher? _externalClose;

    /// <summary>
    /// True when this window was closed by a request from another process (<c>taskkill</c> without
    /// <c>/F</c>, Task Manager's End task, <c>CloseMainWindow</c>), as <see cref="ExternalCloseWatcher"/>
    /// identifies it. The caller reads it after <c>ShowDialog</c> and ends the app instead of treating
    /// the close as the user's answer. taskkill posts to one unowned top-level window, never to an owned
    /// one (measured 2026-11-17), so this is only ever set on a dialog that has no owner.
    /// </summary>
    public bool ClosedFromOutside { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _externalClose ??= new ExternalCloseWatcher(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Asked on every attempt so the note is cleared by the close it belongs to.
        if (_externalClose?.TryConsume() ?? false)
        {
            ClosedFromOutside = true;
        }

        base.OnClosing(e);
    }

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
    internal static bool ShouldShowOnStartup(bool isFirstRun, int accountCount, bool accountsLoadFailed = false) =>
        isFirstRun && accountCount == 0 && !accountsLoadFailed;

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
    /// <para>
    /// <paramref name="owner"/> is for the second door About grew in F-039. About is itself shown
    /// modally over the main window, so a tour parented to the main window would be a modal opened
    /// behind the modal that asked for it. The caller that has a window passes it; the Tools menu,
    /// which has none, does not.
    /// </para>
    /// </summary>
    internal static void ShowTour(Window? requestedOwner = null)
    {
        var dialog = new WelcomeWindow();
        var owner = requestedOwner ?? Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        if (dialog.ClosedFromOutside)
        {
            // Unowned (tray, main window hidden), the tour is the one window an outside close can reach.
            (Application.Current?.MainWindow as MainWindow)?.ExternalCloseQuit?.Invoke();
        }
    }
}
