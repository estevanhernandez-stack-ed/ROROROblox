using Microsoft.Extensions.Logging;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.Discord;

/// <summary>
/// Gates and dispatches an inbound Discord join into <see cref="MainViewModel.HandleDiscordJoinAsync"/>.
/// Extracted out of <c>App.OnStartup</c>'s Discord wiring (Fix round 1, Finding 1) specifically so
/// the <c>JoinEnabled</c> gate is unit-testable without a live WPF <c>Application</c> — nothing in
/// this class touches WPF.
/// <para>
/// <b>Why this exists at all.</b> <see cref="DiscordPresenceService.OnJoinRequested"/> already gates
/// the in-client Join button against the live <c>DiscordConfig.JoinEnabled</c> — see that method's
/// remarks. The <c>roblox-rororo:</c> OS protocol-handler path had NO such gate anywhere in its
/// chain: <c>JoinUriScheme.Register</c> runs unconditionally on every install,
/// <c>InboundJoinRelay.Handle</c> does not read config, and <c>HandleDiscordJoinAsync</c>'s only
/// gate is the private-server confirm modal. Without this class, once a real Discord application id
/// ships, anything able to trigger the URI (a stale Discord card, a hand-crafted link, a malicious
/// actor who learns the scheme) launches a saved account into an arbitrary server — and can take
/// over an already-running session — whether or not the user ever turned Join on.
/// </para>
/// <para>
/// Both inbound-join call sites in <c>App.xaml.cs</c> route through this one class, so the gate
/// applies uniformly: <c>App.JoinRequested</c> (the URI relay/cold-start path, already UI-thread
/// marshalled — see that event's remarks) calls <see cref="HandleAsync"/> directly; the in-client
/// Join button (<see cref="DiscordPresenceService.JoinRequested"/>, Lachee's background RPC thread)
/// is still hopped onto the UI thread by the caller BEFORE reaching <see cref="HandleAsync"/> — this
/// class does no thread marshalling of its own.
/// </para>
/// </summary>
internal sealed class InboundJoinDispatcher
{
    private readonly Func<bool> _joinEnabled;
    private readonly MainViewModel _viewModel;
    private readonly Func<string, bool> _confirm;
    private readonly ILogger? _log;

    public InboundJoinDispatcher(
        Func<bool> joinEnabled,
        MainViewModel viewModel,
        Func<string, bool> confirm,
        ILogger? log = null)
    {
        _joinEnabled = joinEnabled ?? throw new ArgumentNullException(nameof(joinEnabled));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
        _log = log;
    }

    /// <summary>
    /// Never throws — every exception (including from <see cref="MainViewModel.HandleDiscordJoinAsync"/>
    /// itself) is caught and logged, matching every other inbound-join call site's swallow-and-log
    /// contract (<see cref="ROROROblox.App.Discord.InboundJoinRelay"/>'s remarks explain why: an
    /// unguarded throw here can wedge the single-instance pipe listener for the rest of the process).
    /// </summary>
    public async Task HandleAsync(LaunchTarget target)
    {
        if (!_joinEnabled())
        {
            // Same reasoning as DiscordPresenceService.OnJoinRequested's own gate: Join may have
            // been turned off after this request was already in flight (a stale cached Join button,
            // a cold-start argument queued before the user unchecked the setting). Log and drop it
            // rather than launching something the user did not opt into.
            _log?.LogDebug("Ignoring an inbound Discord join — Join is currently disabled.");
            return;
        }

        try
        {
            await _viewModel.HandleDiscordJoinAsync(target, _confirm).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Inbound Discord join handling threw; ignoring.");
        }
    }
}
