using System;
using System.Windows;
using ROROROblox.App.AppLifecycle;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>BLOCKED modal — shown when the mutex is held by someone else at startup. Offers
/// Close-Roblox-for-me and Retry (both re-acquire in place; success sets <see cref="Outcome"/> to
/// Recovered), Start anyway (proceed without the mutex — a benign squatter holds it), and Quit
/// RoRoRo. Read <see cref="Outcome"/> after ShowDialog; never asks the user to restart RoRoRo.</summary>
internal partial class RobloxAlreadyRunningWindow : Window
{
    private readonly Func<bool> _onCloseForMe;
    private readonly Func<bool> _onRetry;

    /// <summary>The user's choice. Defaults to <see cref="BlockedModalOutcome.Quit"/> (fail closed)
    /// so a closed-without-choosing window is treated as Quit.</summary>
    public BlockedModalOutcome Outcome { get; private set; } = BlockedModalOutcome.Quit;

    public RobloxAlreadyRunningWindow(Func<bool> onCloseForMe, Func<bool> onRetry)
    {
        _onCloseForMe = onCloseForMe;
        _onRetry = onRetry;
        InitializeComponent();
    }

    private void OnCloseForMeClick(object sender, RoutedEventArgs e) => TryRecover(_onCloseForMe);
    private void OnRetryClick(object sender, RoutedEventArgs e) => TryRecover(_onRetry);

    private void TryRecover(Func<bool> recover)
    {
        if (recover())
        {
            Outcome = BlockedModalOutcome.Recovered;
            DialogResult = true; // mutex now held — proceed with startup
            Close();
        }
        else
        {
            StillLockedTick.Text = MultiInstanceCopy.StillLocked;
            StillLockedTick.Visibility = Visibility.Visible;
        }
    }

    private void OnStartAnywayClick(object sender, RoutedEventArgs e)
    {
        // Proceed without owning the mutex. Correct when a benign squatter (another RoRoRo / tool)
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
