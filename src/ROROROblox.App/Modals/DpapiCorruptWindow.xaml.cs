using System.Windows;
using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.App.Modals;

internal partial class DpapiCorruptWindow : Window
{
    private readonly Func<Task<StartFreshResult>> _startFresh;
    private bool _working;

    /// <param name="startFresh">The account store's start-fresh: sets the vault files aside and reports what it could not.</param>
    public DpapiCorruptWindow(Func<Task<StartFreshResult>> startFresh)
    {
        _startFresh = startFresh ?? throw new ArgumentNullException(nameof(startFresh));
        InitializeComponent();
    }

    private async void OnStartFreshClick(object sender, RoutedEventArgs e)
    {
        if (_working)
        {
            return;
        }
        _working = true;
        StartFreshButton.IsEnabled = false;
        try
        {
            var outcome = await StartFreshAttempt.RunAsync(_startFresh);
            if (outcome.Succeeded)
            {
                DialogResult = true;
                Close();
                return;
            }
            // Stay open and say so. Closing as if it had worked would leave the vault in place and bring
            // this dialog back on every start.
            FailureText.Text = outcome.FailureMessage;
            FailureText.Visibility = Visibility.Visible;
        }
        finally
        {
            _working = false;
            StartFreshButton.IsEnabled = true;
        }
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>What one Start fresh press came to: closed as done, or open with a reason.</summary>
internal sealed record StartFreshOutcome(bool Succeeded, string FailureMessage);

/// <summary>
/// The decision behind the Start fresh button, apart from the window so a test can drive it. Total: a
/// store that throws is a failure to report, not a crash out of an async void click handler.
/// </summary>
internal static class StartFreshAttempt
{
    public static async Task<StartFreshOutcome> RunAsync(Func<Task<StartFreshResult>> startFresh)
    {
        StartFreshResult result;
        try
        {
            result = await startFresh();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new StartFreshOutcome(false, Loc.Format("DpapiCorruptWindow_CouldNotSetAside", "accounts.dat"));
        }
        return result.Succeeded
            ? new StartFreshOutcome(true, string.Empty)
            : new StartFreshOutcome(false, Loc.Format("DpapiCorruptWindow_CouldNotSetAside", string.Join(", ", result.Failed)));
    }
}
