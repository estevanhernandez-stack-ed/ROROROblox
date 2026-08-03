using Microsoft.Extensions.Logging;
using ROROROblox.Core;

namespace ROROROblox.App.Discord;

/// <summary>
/// Parses a raw <c>roblox-rororo://join/...</c> argument into a <see cref="LaunchTarget"/> and
/// raises <see cref="JoinRequested"/> — the single guarded implementation shared by both ways a
/// join can reach RoRoRo: a cold start (this process was launched with the URI as an argument)
/// and a relay (<c>SingleInstanceGuard.JoinUriReceived</c> forwarding one from a second
/// instance). Deliberately has no WPF dependency, so it can be constructed and driven directly
/// in a headless xUnit test.
/// <para>
/// <b>Exception boundary — this is the whole reason this type exists.</b> The relay path runs on
/// <c>SingleInstanceGuard</c>'s pipe-listener thread, inside a bare <c>Dispatcher.Invoke</c> that
/// only catches <see cref="OperationCanceledException"/> and <see cref="System.IO.IOException"/>.
/// An unguarded throw from a <see cref="JoinRequested"/> subscriber — Task 9's presence wiring,
/// or anything wired up later — would propagate past both of those catches and kill the listener
/// task for the rest of the process: every later launch attempt would then time out silently in
/// <c>SingleInstanceGuard.SignalExisting</c>, and a second instance would call
/// <c>Shutdown(0)</c> believing it had signalled the primary — wedging single-instance for good,
/// with no visible error. Single-instance is load-bearing for the whole product, so <see cref="Handle"/>
/// owns its own try/catch rather than trusting every current and future subscriber to be
/// well-behaved.
/// </para>
/// </summary>
internal sealed class InboundJoinRelay
{
    private readonly ILogger? _log;

    public InboundJoinRelay(ILogger? log)
    {
        _log = log;
    }

    /// <summary>Fires once a raw join URI decodes cleanly. Never fires for a garbage payload.</summary>
    public event EventHandler<LaunchTarget>? JoinRequested;

    /// <param name="rawUri">The raw, still URL-encoded <c>roblox-rororo://join/...</c> argument.</param>
    /// <param name="source">Log-only context ("cold start" or "relay") for the info/debug lines.</param>
    public void Handle(string rawUri, string source)
    {
        try
        {
            if (JoinUriParser.TryParse([rawUri], out var target))
            {
                _log?.LogInformation("Discord join received ({Source}).", source);
                JoinRequested?.Invoke(this, target);
            }
            else
            {
                _log?.LogDebug("Discord join URI failed to parse ({Source}); ignoring.", source);
            }
        }
        catch (Exception ex)
        {
            // This catch is the fix for the single-instance-pipe wedge described above. It also
            // covers whatever downstream subscribers do with the raised LaunchTarget (App
            // forwards this event onward), not just the parse — anything hung off JoinRequested,
            // however many hops away, funnels back through this one call site.
            _log?.LogWarning(ex, "Discord join handling threw ({Source}); the pipe listener stays alive regardless.", source);
        }
    }
}
