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
