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

        _client = new DiscordRpcClient(_applicationId);
        _client.OnReady += (_, _) => Ready?.Invoke(this, EventArgs.Empty);
        _client.OnConnectionFailed += (_, _) => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        _client.OnClose += (_, _) => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        _client.OnError += (_, e) => Errored?.Invoke(this, e.Message);
        _client.OnJoin += (_, e) => JoinRequested?.Invoke(this, e.Secret);

        _client.Initialize();
        // Without this the Join button renders and its click is never delivered.
        _client.Subscribe(EventType.Join);
    }

    public void Deinitialize()
    {
        _client?.Deinitialize();
        _client?.Dispose();
        _client = null;
    }

    public void SetPresence(DiscordPresencePayload payload)
    {
        if (_client is null) return;
        _client.SetPresence(new RichPresence
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
        });
    }

    public void ClearPresence() => _client?.ClearPresence();

    public void Dispose() => Deinitialize();
}
