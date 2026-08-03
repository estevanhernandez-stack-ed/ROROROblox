using Microsoft.Extensions.Logging;
using ROROROblox.App.Discord.Internal;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Owns the Discord IPC connection and keeps presence in step with the roster.
/// <para>
/// Everything here is degrade-safe by contract: Discord not being installed or not running is the
/// normal case, not an error, and no Discord failure may affect a Roblox launch. Presence is a
/// passenger.
/// </para>
/// </summary>
internal sealed class DiscordPresenceService : IDisposable
{
    /// <summary>
    /// Discord will not render a Join button on a party it considers full — so <c>MaxSize</c> must
    /// always exceed the live party <c>Size</c>. This is comfortably above any realistic RoRoRo
    /// roster; it is not a Roblox server-capacity figure and must never be read as one.
    /// </summary>
    private const int PartyMaxSize = 100;

    private readonly IDiscordRpcClient _client;
    private readonly Func<RosterSnapshot> _roster;
    private readonly ILogger _log;
    private DiscordConfig _config = new();

    public DiscordPresenceService(IDiscordRpcClient client, Func<RosterSnapshot> rosterProvider, ILogger log)
    {
        _client = client;
        _roster = rosterProvider;
        _log = log;
        _client.JoinRequested += OnJoinRequested;
        // Ready/ConnectionFailed arrive on Lachee's background RPC thread, asynchronously and well
        // after ApplyAsync has already returned (Fix round 1, Finding 2) — SetStatus's StatusChanged
        // event is how a subscriber (PreferencesWindow) learns the real, eventual outcome instead of
        // reading a StatusLine snapshot that's already stale by the time it's read.
        _client.ConnectionFailed += (_, _) => SetStatus("Discord isn't running — presence starts when it does.");
        _client.Ready += (_, _) => SetStatus("Connected to Discord.");
        _client.Errored += (_, msg) => _log.LogDebug("Discord rejected a presence update: {Message}", msg);
    }

    /// <summary>Plain-language state for the Settings panel. Never a stack trace.</summary>
    public string StatusLine { get; private set; } = "Presence is off.";

    /// <summary>
    /// Fires whenever <see cref="StatusLine"/> changes — including from <c>Ready</c>/
    /// <c>ConnectionFailed</c>, which arrive on Lachee's background RPC thread. A subscriber must
    /// marshal to its own thread (e.g. a WPF <c>Dispatcher</c>) before touching UI from this event.
    /// </summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Live mirror of <c>DiscordConfig.JoinEnabled</c> for callers that gate a join dispatch outside
    /// this class — specifically the <c>roblox-rororo:</c> protocol-handler path
    /// (<see cref="ROROROblox.App.Discord.InboundJoinDispatcher"/>), which has no other seam back to
    /// the live config. <see cref="OnJoinRequested"/> already gates the in-client Join button
    /// against <c>_config.JoinEnabled</c> directly; this property exists for the OTHER inbound path,
    /// which does not go through this class at all.
    /// </summary>
    internal bool JoinEnabled => _config.JoinEnabled;

    /// <summary>Fires when a clan member clicks Join. The target is already decoded.</summary>
    public event EventHandler<LaunchTarget>? JoinRequested;

    private void SetStatus(string value)
    {
        if (StatusLine == value) return;
        StatusLine = value;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ApplyAsync(DiscordConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        try
        {
            if (!config.PresenceEnabled)
            {
                if (_client.IsInitialized) { _client.ClearPresence(); _client.Deinitialize(); }
                SetStatus("Presence is off.");
                return Task.CompletedTask;
            }

            // Honest transient (Fix round 1, Finding 2): without this, a caller that reads
            // StatusLine immediately after ApplyAsync returns sees whatever it was BEFORE this
            // call — "Presence is off." while presence is actually turning on — because the real
            // outcome (Ready/ConnectionFailed) hasn't arrived yet. Never contradictory, even in
            // the instant before the first Lachee callback lands.
            SetStatus("Connecting to Discord…");

            if (!_client.IsInitialized) { _client.Initialize(); }
            Refresh();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Discord presence apply failed; continuing without presence.");
            SetStatus("Discord isn't running — presence starts when it does.");
        }
        return Task.CompletedTask;
    }

    /// <summary>Recompute and push. Safe to call from any roster-changing event.</summary>
    public void Refresh()
    {
        try
        {
            // IsInitialized itself can throw on a disposed/faulted Lachee client — this guard used
            // to sit outside the try/catch, which meant a Discord-side fault could propagate out of
            // Refresh() and into a Roblox launch path (OnProcessAttached calls this). No Discord
            // failure may affect a Roblox launch, so the whole method body — including the guard —
            // is covered.
            if (!_config.PresenceEnabled || !_client.IsInitialized) return;

            var fields = PresencePayloadBuilder.Build(_roster());
            if (fields is null) { _client.ClearPresence(); return; }

            DiscordPresenceParty? party = null;
            if (_config.JoinEnabled && fields.JoinableServer is { } server)
            {
                // FIX 1 (final whole-branch review, 2026-08-03): encode a p| secret carrying the
                // real private-server code + kind when the joinable cluster's session was actually
                // launched into one — see RosterServer's remarks. Encoding a bare GameJob for a
                // private session (the pre-fix behavior) always published a Join button Roblox
                // would bounce server-side, defeating the denied-entry warning entirely.
                LaunchTarget launchTarget = server.PrivateServerCode is not null
                    ? new LaunchTarget.PrivateServer(
                        server.Server.PlaceId, server.PrivateServerCode, server.PrivateServerCodeKind!.Value)
                    : new LaunchTarget.GameJob(server.Server.PlaceId, server.Server.JobId);

                var secret = JoinSecretCodec.Encode(launchTarget);
                if (secret is not null)
                {
                    party = new DiscordPresenceParty(
                        $"rororo-{server.Server.JobId}", secret, fields.JoinableServerAccountCount, PartyMaxSize);
                }
            }

            _client.SetPresence(new DiscordPresencePayload(
                fields.State, fields.Details, fields.StartedAtUtc,
                LargeImageKey: "active_large", LargeImageText: "RoRoRo", Party: party));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Discord presence refresh failed; leaving the last state in place.");
        }
    }

    private void OnJoinRequested(object? sender, string secret)
    {
        if (!_config.JoinEnabled)
        {
            // The user turned Join off after a secret went out — a friend's stale cached Join
            // button or an in-flight click can still arrive here. Same "offer a Join the user did
            // not enable" failure Refresh() guards against outbound; guard it here inbound too.
            _log.LogDebug("Ignoring a Discord join request — Join is currently disabled.");
            return;
        }

        if (!JoinSecretCodec.TryDecode(secret, out var target))
        {
            _log.LogDebug("Ignoring an undecodable Discord join secret.");
            return;
        }
        JoinRequested?.Invoke(this, target);
    }

    public void Dispose() => _client.Dispose();
}
