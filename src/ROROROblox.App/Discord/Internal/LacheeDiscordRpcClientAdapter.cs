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

    public void Initialize()
    {
        _inner.Initialize();
        // Lachee throws BadPresenceException on every SetPresence with Secrets unless the URI
        // scheme is registered. The registration writes a discord-<appId>:// handler to the
        // Windows registry pointing at our current process — Discord uses that scheme to wake
        // our app when a clanmate clicks Join.
        try
        {
            _inner.RegisterUriScheme();
        }
        catch
        {
            // Best-effort. Without registration, presence-with-secrets calls will throw
            // BadPresenceException; the Join button won't render to friends.
        }

        // Lachee's RegisterUriScheme writes the registry command WITHOUT the "%1" argument
        // placeholder. Without it, Windows launches our exe with no command-line argument when
        // Discord dispatches the URI scheme. Surfaced 2026-05-07 via registry inspection.
        FixupUriSchemeRegistryEntry();

        // CRITICAL: subscribe to Discord IPC join events. The URI scheme dispatch is just a
        // wake-up — the actual join secret arrives via the IPC pipe as an ACTIVITY_JOIN
        // command. Without this subscription, Discord doesn't deliver the event at all,
        // OnJoin never fires, and we sit on a useless wake-only dispatch (URI shape:
        // "discord-{appId}:///" with empty path + empty query, observed in her log).
        try
        {
            _inner.Subscribe(EventType.Join | EventType.JoinRequest);
        }
        catch
        {
            // Best-effort. Without subscription, Layer 2 inbound is silent on the joiner's
            // side; the launcher's outbound party Join button still renders correctly to
            // friends, the click just goes nowhere on this user's machine.
        }
    }

    private void FixupUriSchemeRegistryEntry()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            var commandKeyPath = $@"Software\Classes\discord-{_inner.ApplicationID}\shell\open\command";
            using var commandKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(commandKeyPath);
            if (commandKey is null) return;
            // The "%1" placeholder is what Windows substitutes with the URI Discord dispatches.
            // Quoting both halves preserves paths with spaces + URIs with query params.
            commandKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
        }
        catch
        {
            // Registry write can fail (permissions, locked-down user). Best-effort — without
            // this, URI scheme dispatch loses the join secret but the rest of the app works.
        }
    }

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
