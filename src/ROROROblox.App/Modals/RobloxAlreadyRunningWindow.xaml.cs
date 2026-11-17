using System.ComponentModel;
using System;
using System.Threading.Tasks;
using System.Windows;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>BLOCKED modal — shown when Roblox holds the singleton name at startup. Offers Retry and
/// Close-Roblox-for-me (both re-attempt the name; success sets <see cref="Outcome"/> to Recovered),
/// Start anyway (proceed without owning it), and Quit RoRoRo. Read <see cref="Outcome"/> after
/// ShowDialog; never asks the user to restart RoRoRo itself.
///
/// <para>Recovery is asynchronous because it polls: the singleton object only dies once its last
/// handle closes, so a freshly-quit Roblox needs a beat. The action buttons are disabled for the
/// duration — a second Retry press mid-poll would race the first.</para></summary>
internal partial class RobloxAlreadyRunningWindow : Window
{
    private readonly Func<Task<bool>> _onCloseForMe;
    private readonly Func<Task<bool>> _onRetry;

    /// <summary>The user's choice. Defaults to <see cref="BlockedModalOutcome.Quit"/> (fail closed)
    /// so a closed-without-choosing window is treated as Quit.</summary>
    public BlockedModalOutcome Outcome { get; private set; } = BlockedModalOutcome.Quit;

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

    public RobloxAlreadyRunningWindow(Func<Task<bool>> onCloseForMe, Func<Task<bool>> onRetry)
    {
        _onCloseForMe = onCloseForMe;
        _onRetry = onRetry;
        InitializeComponent();
    }

    private async void OnCloseForMeClick(object sender, RoutedEventArgs e) => await TryRecoverAsync(_onCloseForMe);
    private async void OnRetryClick(object sender, RoutedEventArgs e) => await TryRecoverAsync(_onRetry);

    private async Task TryRecoverAsync(Func<Task<bool>> recover)
    {
        ActionButtons.IsEnabled = false;
        try
        {
            if (await recover())
            {
                Outcome = BlockedModalOutcome.Recovered;
                DialogResult = true; // the name is ours (or a peer tool's) — proceed with startup
                Close();
                return;
            }

            StillLockedTick.Text = MultiInstanceCopy.StillLocked;
            StillLockedTick.Visibility = Visibility.Visible;
        }
        finally
        {
            // The window is already closing on the success path; re-enabling is harmless there and
            // necessary on the failure path so the user can try another action.
            ActionButtons.IsEnabled = true;
        }
    }

    private void OnStartAnywayClick(object sender, RoutedEventArgs e)
    {
        // Proceed without owning the name. Correct when a benign squatter (another RoRoRo / tool)
        // holds it — multi-instance already works. The runtime contested watcher banners the state.
        Outcome = BlockedModalOutcome.StartAnyway;
        DialogResult = true;
        Close();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Outcome = BlockedModalOutcome.Quit;
        DialogResult = false;
        Close();
    }
}
