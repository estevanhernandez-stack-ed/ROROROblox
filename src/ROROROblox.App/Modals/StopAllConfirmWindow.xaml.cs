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
}
