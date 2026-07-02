using System;
using System.Windows;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>BLOCKED modal — shown when the mutex is held by someone else at startup. Offers
/// Close-Roblox-for-me and Retry (both re-acquire in place; success closes with DialogResult=true)
/// plus Quit RoRoRo (DialogResult=false). Never asks the user to restart RoRoRo.</summary>
internal partial class RobloxAlreadyRunningWindow : Window
{
    private readonly Func<bool> _onCloseForMe;
    private readonly Func<bool> _onRetry;

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
            DialogResult = true; // mutex now held — proceed with startup
            Close();
        }
        else
        {
            StillLockedTick.Text = MultiInstanceCopy.StillLocked;
            StillLockedTick.Visibility = Visibility.Visible;
        }
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
