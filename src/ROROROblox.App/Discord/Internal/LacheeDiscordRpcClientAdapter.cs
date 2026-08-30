using DiscordRPC;
using DiscordRPC.Message;
using Microsoft.Extensions.Logging;
using ROROROblox.App.Discord;

namespace ROROROblox.App.Discord.Internal;

/// <summary>
/// Maps <see cref="IDiscordRpcClient"/> onto Lachee's client (NuGet id <c>DiscordRichPresence</c>,
/// namespace <c>DiscordRPC</c> — the plan brief's package id <c>Lachee.DiscordRichPresence</c> does not
/// exist on NuGet; this is the same library, correct id).
/// <para>
/// Two non-obvious requirements, both learned the hard way on the May branch: the client must
/// <c>Subscribe(EventType.Join)</c> or Discord never delivers the join command however correct
/// the presence looks, and the <c>roblox-rororo:</c> URI scheme must be registered BEFORE
/// Discord will accept a presence carrying secrets at all.
/// </para>
/// </summary>
internal sealed class LacheeDiscordRpcClientAdapter : IDiscordRpcClient
{
    private readonly string _applicationId;
    private readonly ILogger _log;
    private readonly bool _packagedInstall;
    private DiscordRpcClient? _client;

    public LacheeDiscordRpcClientAdapter(string applicationId, ILogger log, bool packagedInstall = false)
    {
        _applicationId = applicationId;
        _log = log;
        _packagedInstall = packagedInstall;
    }

    public bool IsInitialized => _client?.IsInitialized == true;

    public event EventHandler<string>? JoinRequested;
    public event EventHandler? ConnectionFailed;
    public event EventHandler? Ready;
    public event EventHandler<string>? Errored;

    public void Initialize()
    {
        if (_client is not null) return;

        if (string.IsNullOrWhiteSpace(_applicationId))
        {
            // Defense-in-depth: Task 6 owns the "skip the feature when unconfigured" decision
            // upstream, but `new DiscordRpcClient("")` throws ArgumentNullException synchronously.
            // Discord:ApplicationId shipped empty when this guard was written; since commit 8c75544
            // (2026-08-03) appsettings.json carries the public id, but a stripped or hand-edited
            // config can still blank it. One guard is too sharp an edge to rely on.
            _log.LogWarning("Discord presence unavailable: no application id configured.");
            return;
        }

        try
        {
            _client = new DiscordRpcClient(_applicationId);
            _client.OnReady += (_, _) => SafeInvoke(() => Ready?.Invoke(this, EventArgs.Empty));

            // OnConnectionFailed and OnClose both mean "no pipe" to the service above, but they are
            // NOT the same event down here: failed = Discord was never reachable, close = a pipe we
            // had went away (a Discord restart). The seam stays one event; the log tells them apart,
            // because "did our connection drop or was it never up?" is the first question worth
            // asking when presence goes quiet.
            _client.OnConnectionFailed += (_, _) =>
            {
                _log.LogDebug("Discord IPC connection attempt failed (Discord not reachable).");
                SafeInvoke(() => ConnectionFailed?.Invoke(this, EventArgs.Empty));
            };
            _client.OnClose += (_, e) =>
            {
                _log.LogInformation("Discord IPC pipe closed: {Reason}", e.Reason);
                SafeInvoke(() => ConnectionFailed?.Invoke(this, EventArgs.Empty));
            };
            _client.OnError += (_, e) => SafeInvoke(() => Errored?.Invoke(this, e.Message));
            _client.OnJoin += (_, e) => SafeInvoke(() => JoinRequested?.Invoke(this, e.Secret));

            _client.Initialize();
            _log.LogInformation("Discord IPC initialized for application {ApplicationId}.", _applicationId);

            // Lachee tracks its OWN internal "URI scheme registered" flag — separate from the
            // `roblox-rororo:` scheme JoinUriScheme.Register wrote earlier in OnStartup, which
            // Lachee knows nothing about. Subscribe() throws InvalidConfigurationException unless
            // this ran first. Wrapped and swallowed: a failure here still leaves presence (state,
            // details, party) working, just without the Join button.
            try
            {
                _client.RegisterUriScheme();
                _log.LogInformation("Discord URI scheme registered.");

                // Lachee's RegisterUriScheme writes the registry command WITHOUT the "%1"
                // argument placeholder, so Windows launches us with no argument when Discord
                // dispatches the discord-{applicationId} scheme. Fix the value it just wrote —
                // unless this install is packaged, where the manifest's uap10:Protocol owns the
                // scheme (already with Parameters="%1") and the value Lachee wrote only landed
                // in the package's virtual registry hive that Explorer never reads (spec
                // 2026-08-30-packaged-activation-design.md).
                if (_packagedInstall)
                {
                    _log.LogDebug("Packaged install: skipping Discord scheme command fixup; the manifest protocol owns the scheme.");
                }
                else
                {
                    var exePath = Environment.ProcessPath;
                    if (exePath is not null)
                    {
                        JoinUriScheme.FixupDiscordSchemeCommand(_applicationId, exePath);
                    }
                    else
                    {
                        _log.LogDebug("Environment.ProcessPath was null; skipped Discord URI scheme command fixup.");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Discord RegisterUriScheme failed; presence will connect without a Join button this session.");
            }

            // Without this the Join button renders and its click is never delivered. Subscribing
            // to JoinRequest (in addition to Join) covers both the in-client Join click and the
            // "Ask to Join" request path.
            _client.Subscribe(EventType.Join | EventType.JoinRequest);
            _log.LogInformation("Discord Join subscription active.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord IPC initialize failed; presence unavailable this session.");
            _client?.Dispose();
            _client = null;
        }
    }

    public void Deinitialize()
    {
        try
        {
            _client?.Deinitialize();
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord IPC deinitialize failed.");
        }
        finally
        {
            _client = null;
        }
    }

    public void SetPresence(DiscordPresencePayload payload)
    {
        if (_client is null) return;
        try
        {
            _client.SetPresence(ToRichPresence(payload));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord SetPresence failed; presence not updated this cycle.");
        }
    }

    /// <summary>
    /// Pure mapping from the seam DTO to Lachee's <see cref="RichPresence"/>. Touches no IPC, so
    /// it is unit-testable without a Discord pipe — see
    /// ROROROblox.Tests/Discord/DiscordPresencePayloadMappingTests.cs.
    /// </summary>
    internal static RichPresence ToRichPresence(DiscordPresencePayload payload) => new()
    {
        State = payload.State,
        Details = payload.Details,
        Timestamps = payload.StartedAtUtc is { } t ? new Timestamps(t.UtcDateTime) : null,
        Assets = new Assets
        {
            LargeImageKey = payload.LargeImageKey,
            LargeImageText = payload.LargeImageText,
        },
        Party = payload.Party is { } p ? new Party { ID = p.PartyId, Size = p.Size, Max = p.MaxSize } : null,
        Secrets = payload.Party is { } s ? new Secrets { JoinSecret = s.JoinSecret } : null,
    };

    public void ClearPresence()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord ClearPresence failed.");
        }
    }

    public void Dispose() => Deinitialize();

    /// <summary>
    /// Event forwarding fires on Lachee's background RPC thread (AutoEvents defaults to true).
    /// A throwing subscriber would otherwise surface on that thread, outside app control, and
    /// can take the process down. Isolate it instead.
    /// </summary>
    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord event subscriber threw; isolated to keep the IPC thread alive.");
        }
    }
}
