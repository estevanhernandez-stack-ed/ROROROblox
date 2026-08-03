using System.Windows;

namespace ROROROblox.App.Modals;

/// <summary>
/// Confirm-before-join modal (Task 8, v1.9 Discord presence). Shown by
/// <c>MainViewModel.HandleDiscordJoinAsync</c> before a Discord join dispatches, for either of two
/// DIFFERENT reasons that carry DIFFERENT copy in <c>BodyText</c> (see that method's remarks for
/// the full decision table, added Fix round 2):
/// <list type="bullet">
///   <item>Target is a <c>LaunchTarget.PrivateServer</c> — Roblox checks permission server-side, so
///   a clan member not on that server's list gets bounced after the client launches, and this
///   says so up front. Applies regardless of where the join request came from.</item>
///   <item>The join arrived via the <c>roblox-rororo:</c> URI handler (<c>JoinOrigin.UriHandler</c>)
///   — even for a public server. Nothing about the URI proves Discord sent it (any local process,
///   <c>.url</c> file, or browser navigation can trigger it), so this confirms on origin, not
///   destination risk, and names the account about to launch instead of warning about entry.</item>
/// </list>
/// When BOTH apply (a private-server target reached via the URI handler), exactly one prompt shows
/// — the private-server one, since <c>HandleDiscordJoinAsync</c> checks that condition first and it
/// already carries the stronger warning.
/// <para>
/// <c>HandleDiscordJoinAsync</c> takes the decision as an injected <c>Func&lt;string, bool&gt;</c>
/// so it's testable without a window — <see cref="Confirm"/> is the one production implementation
/// of that delegate, wired by whichever inbound-join subscriber shows it (in-client Join button, or
/// the <c>roblox-rororo:</c> URI relay). Not a destructive confirm — nothing is lost by trying — so
/// "Try anyway" is the IsDefault button, unlike <see cref="StopAllConfirmWindow"/>'s
/// cancel-defaults-to-safe pattern.
/// </para>
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
    /// Shows the confirm modal and returns whether the user chose to proceed. Takes an
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
    /// method — for either reason above, not just a private-server target.
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
