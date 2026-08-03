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
    /// <para>
    /// <b>Must run on the UI thread — this constructs and shows a WPF <see cref="Window"/>, so it
    /// throws if called off it.</b> It does NOT re-dispatch itself, so the caller is on the hook.
    /// The two inbound-join paths are NOT equally safe here: <c>App.JoinRequested</c> (the
    /// <c>roblox-rororo:</c> URI relay + cold start) already arrives on the UI thread — see that
    /// event's remarks — so a subscriber wiring THIS delegate for that path needs no extra
    /// dispatch. <c>DiscordPresenceService.JoinRequested</c> (the in-client Join button) does NOT
    /// arrive on the UI thread — it fires on Lachee's background RPC thread with nothing in its
    /// chain that marshals — so a subscriber wiring this delegate for THAT path must wrap the call
    /// in <c>Application.Current.Dispatcher.Invoke</c> (or post via <c>InvokeAsync</c>) before
    /// reaching <c>HandleDiscordJoinAsync</c>, which is what ultimately calls back into this
    /// method for a private-server target.
    /// </para>
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
