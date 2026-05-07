using DiscordRPC;
using DiscordRPC.Message;

namespace ROROROblox.App.Discord.Internal;

/// <summary>
/// Production adapter wrapping <see cref="DiscordRpcClient"/> from Lachee.DiscordRichPresence.
/// Translates our <see cref="DiscordPresencePayload"/> to the library's RichPresence type.
///
/// Constructed with <c>autoEvents: true</c> so the library owns its own pump thread —
/// callers don't have to call Invoke() on a timer. Saves us a moving part.
/// </summary>
internal sealed class LacheeDiscordRpcClientAdapter : IDiscordRpcClient
{
    private readonly DiscordRpcClient _inner;
    private bool _disposed;

    public LacheeDiscordRpcClientAdapter(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("Discord ApplicationId must not be empty.", nameof(applicationId));
        }
        _inner = new DiscordRpcClient(applicationId, autoEvents: true);
        _inner.OnJoin += OnJoinHandler;
        _inner.OnConnectionFailed += OnConnectionFailedHandler;
        _inner.OnReady += OnReadyHandler;
        _inner.OnError += OnErrorHandler;
        _inner.OnPresenceUpdate += OnPresenceUpdateHandler;
    }

    public bool IsInitialized => _inner.IsInitialized;

    public void Initialize() => _inner.Initialize();
    public void Deinitialize() => _inner.Deinitialize();
    public void ClearPresence() => _inner.ClearPresence();

    public void SetPresence(DiscordPresencePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var presence = new RichPresence
        {
            State = payload.State,
            Details = payload.Details,
        };

        if (!string.IsNullOrEmpty(payload.LargeImageKey) || !string.IsNullOrEmpty(payload.SmallImageKey))
        {
            presence.Assets = new Assets
            {
                LargeImageKey = payload.LargeImageKey,
                LargeImageText = payload.LargeImageText,
                SmallImageKey = payload.SmallImageKey,
                SmallImageText = payload.SmallImageText,
            };
        }

        if (payload.Party is not null)
        {
            presence.Party = new Party
            {
                ID = payload.Party.PartyId,
                Max = payload.Party.MaxSize,
                Privacy = Party.PrivacySetting.Public,
            };
            presence.Secrets = new Secrets
            {
                JoinSecret = payload.Party.JoinSecret,
            };
        }

        _inner.SetPresence(presence);
    }

    public event EventHandler<string>? JoinRequested;
    public event EventHandler? ConnectionFailed;
    public event EventHandler? Ready;
    public event EventHandler<string>? Errored;
    public event EventHandler? PresenceUpdated;

    private void OnJoinHandler(object sender, JoinMessage args) =>
        JoinRequested?.Invoke(this, args.Secret);

    private void OnConnectionFailedHandler(object sender, ConnectionFailedMessage args) =>
        ConnectionFailed?.Invoke(this, EventArgs.Empty);

    private void OnReadyHandler(object sender, ReadyMessage args) =>
        Ready?.Invoke(this, EventArgs.Empty);

    private void OnErrorHandler(object sender, ErrorMessage args) =>
        Errored?.Invoke(this, $"{args.Code}: {args.Message}");

    private void OnPresenceUpdateHandler(object sender, PresenceMessage args) =>
        PresenceUpdated?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _inner.OnJoin -= OnJoinHandler;
            _inner.OnConnectionFailed -= OnConnectionFailedHandler;
            _inner.OnReady -= OnReadyHandler;
            _inner.OnError -= OnErrorHandler;
            _inner.OnPresenceUpdate -= OnPresenceUpdateHandler;
            _inner.Dispose();
        }
        catch
        {
            // Dispose must not throw.
        }
    }
}
