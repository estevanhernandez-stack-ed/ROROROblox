using DiscordRPC;
using DiscordRPC.Message;
using Microsoft.Extensions.Logging;

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
    private DiscordRpcClient? _client;

    public LacheeDiscordRpcClientAdapter(string applicationId, ILogger log)
    {
        _applicationId = applicationId;
        _log = log;
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
            // upstream, but `new DiscordRpcClient("")` throws ArgumentNullException synchronously,
            // and Discord:ApplicationId ships empty today. One guard is too sharp an edge to rely on.
            _log.LogWarning("Discord presence unavailable: no application id configured.");
            return;
        }

        try
        {
            _client = new DiscordRpcClient(_applicationId);
            _client.OnReady += (_, _) => SafeInvoke(() => Ready?.Invoke(this, EventArgs.Empty));
            _client.OnConnectionFailed += (_, _) => SafeInvoke(() => ConnectionFailed?.Invoke(this, EventArgs.Empty));
            _client.OnClose += (_, _) => SafeInvoke(() => ConnectionFailed?.Invoke(this, EventArgs.Empty));
            _client.OnError += (_, e) => SafeInvoke(() => Errored?.Invoke(this, e.Message));
            _client.OnJoin += (_, e) => SafeInvoke(() => JoinRequested?.Invoke(this, e.Secret));

            _client.Initialize();
            // Without this the Join button renders and its click is never delivered.
            _client.Subscribe(EventType.Join);
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
