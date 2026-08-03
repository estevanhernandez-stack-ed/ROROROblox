using System.Windows;

namespace ROROROblox.App.Modals;

/// <summary>
/// Private-server join warning (Task 8, v1.9 Discord presence). Shown before a Discord Join
/// dispatches into a <c>LaunchTarget.PrivateServer</c> — Roblox checks server-side permission,
/// so a clan member not on that server's list gets bounced after the client launches, and this
/// says so up front. <see cref="MainViewModel"/>'s <c>HandleDiscordJoinAsync</c> takes the
/// decision as an injected <c>Func&lt;string, bool&gt;</c> so it's testable without a window —
/// <see cref="Confirm"/> is the one production implementation of that delegate, wired by whichever
/// inbound-join subscriber shows it (in-client Join button, or the <c>roblox-rororo:</c> URI
/// relay). Not a destructive confirm — nothing is lost by trying — so "Try anyway" is the
/// IsDefault button, unlike <see cref="StopAllConfirmWindow"/>'s cancel-defaults-to-safe pattern.
/// </summary>
internal partial class JoinRequestWindow : Window
{
    private bool _tryAnyway;

    private JoinRequestWindow(string message)
    {
        InitializeComponent();
        BodyText.Text = message;
    }

    /// <summary>
    /// Shows the warning modally and returns whether the user chose to proceed. Takes an
    /// <paramref name="owner"/> param, so it isn't itself a <c>Func&lt;string, bool&gt;</c> —
    /// wrap it in a lambda closing over the owner window to match what
    /// <c>HandleDiscordJoinAsync</c> expects: <c>msg =&gt; JoinRequestWindow.Confirm(owner, msg)</c>.
    /// Must run on the UI thread — both inbound-join paths already marshal onto it before a
    /// subscriber sees the event (see <c>App.JoinRequested</c>'s remarks), so this does not
    /// re-dispatch itself.
    /// </summary>
    public static bool Confirm(Window? owner, string message)
    {
        var window = new JoinRequestWindow(message);
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        window.ShowDialog();
        return window._tryAnyway;
    }

    private void OnTryAnywayClick(object sender, RoutedEventArgs e)
    {
        _tryAnyway = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _tryAnyway = false;
        DialogResult = false;
        Close();
    }
}
