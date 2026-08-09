using System.Windows;

namespace ROROROblox.App.Modals;

/// <summary>
/// Branded confirm for the tray "Stop all Roblox instances" teardown. Destructive — force-closes
/// every running client — so it is confirm-gated, magenta primary per the brand stakes pairing.
/// Only shown when at least one client is running; <c>DialogResult == true</c> means proceed.
/// </summary>
internal partial class StopAllConfirmWindow : Window
{
    public StopAllConfirmWindow(int runningCount)
    {
        InitializeComponent();
        BodyText.Text = runningCount == 1
            ? "1 Roblox client is running. This closes it immediately."
            : $"{runningCount} Roblox clients are running. This closes them all immediately.";
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Show the confirm and report whether the user accepted. Owner resolution matches
    /// <c>LaunchHeadroomWindow.ShouldProceed</c>: an unloaded or absent main window means centre on
    /// screen rather than parent to something that cannot host a dialog. Visibility is checked
    /// too, not just loaded state: the main window's X-close handler cancels close and calls
    /// <c>Hide()</c> as the minimize-to-tray path, which leaves <c>IsLoaded</c> true even though
    /// nothing is on screen. The tray can fire this Stop-all confirm with the main window hidden
    /// that way, and an invisible owner would otherwise still receive activation back on dismissal.
    /// </summary>
    internal static bool Confirm(int runningCount)
    {
        var dialog = new StopAllConfirmWindow(runningCount);
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }
}
