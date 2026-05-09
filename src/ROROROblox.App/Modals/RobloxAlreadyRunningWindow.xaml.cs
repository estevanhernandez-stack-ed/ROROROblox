using System.Windows;

namespace ROROROblox.App.Modals;

/// <summary>
/// Cycle 4 hard-block modal. Shown by <c>App.OnStartup</c> when <c>StartupGate.ShouldProceed</c>
/// returns false — i.e., a foreign <c>RobloxPlayerBeta.exe</c> is running at app launch. The
/// caller owns shutdown sequencing; this window's only job is to display the explanation and
/// collect the dismissal.
/// </summary>
internal partial class RobloxAlreadyRunningWindow : Window
{
    public RobloxAlreadyRunningWindow()
    {
        InitializeComponent();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
