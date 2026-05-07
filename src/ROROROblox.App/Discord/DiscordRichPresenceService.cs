using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ROROROblox.App.Discord.Internal;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Layer 1 surface: per-user Discord rich presence. Owns one <see cref="IDiscordRpcClient"/>
/// at a time. Connection lifecycle:
///   - <see cref="StartAsync"/> guards on <see cref="IDiscordConfig.RichPresenceEnabled"/> +
///     non-empty Discord:ApplicationId. If either is false, the IPC client is never created.
///   - On <c>ConnectionFailed</c> from the client, schedule a reconnect at 5s exponential
///     backoff up to 60s (5/10/20/40/60). Reset to 5s on the next <c>Ready</c>.
///   - On <see cref="IDiscordConfig.Changed"/> with RichPresenceEnabled flipped, tear down or
///     spin up accordingly.
/// </summary>
public sealed class DiscordRichPresenceService : IDiscordPresence, IDisposable
{
    // Match Discord developer portal asset slot names. Item 10 uploads the actual PNGs.
    internal const string IdleLargeKey = "idle_large";
    internal const string ActiveLargeKey = "active_large";
    internal const string IdleSmallKey = "idle_small";
    internal const string ActiveSmallKey = "active_small";
    internal const int PartyMaxSize = 6;

    private static readonly int[] BackoffSecondsLadder = [5, 10, 20, 40, 60];

    private readonly IDiscordConfig _config;
    private readonly ILogger<DiscordRichPresenceService> _log;
    private readonly string? _applicationId;
    private readonly Func<string, IDiscordRpcClient> _clientFactory;
    private readonly object _gate = new();

    private IDiscordRpcClient? _client;
    private Timer? _reconnectTimer;
    private int _reconnectStep;
    private bool _started;
    private bool _disposed;

    public DiscordRichPresenceService(
        IDiscordConfig config,
        ILogger<DiscordRichPresenceService> log,
        IConfiguration appConfig)
        : this(config, log, appConfig, defaultClientFactory: null)
    {
    }

    /// <summary>Test-only constructor: inject a fake <see cref="IDiscordRpcClient"/> factory.</summary>
    internal DiscordRichPresenceService(
        IDiscordConfig config,
        ILogger<DiscordRichPresenceService> log,
        IConfiguration appConfig,
        Func<string, IDiscordRpcClient>? defaultClientFactory)
    {
        _config = config;
        _log = log;
        _applicationId = appConfig?["Discord:ApplicationId"];
        _clientFactory = defaultClientFactory ?? (id => new LacheeDiscordRpcClientAdapter(id));
        _config.Changed += OnConfigChanged;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_applicationId))
        {
            _log.LogDebug("DiscordRichPresenceService.StartAsync — Discord:ApplicationId unset; staying off.");
            return Task.CompletedTask;
        }
        if (!_config.RichPresenceEnabled)
        {
            _log.LogDebug("DiscordRichPresenceService.StartAsync — RichPresenceEnabled is false; staying off.");
            return Task.CompletedTask;
        }

        ConnectClient();
        return Task.CompletedTask;
    }

    public Task UpdateStateAsync(RichPresenceState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        var client = _client;
        if (client is null) return Task.CompletedTask;

        var payload = BuildPayload(state, party: null);
        TrySetPresence(client, payload);
        return Task.CompletedTask;
    }

    public Task SetPartyAsync(string serverShareUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serverShareUrl)) return Task.CompletedTask;
        var client = _client;
        if (client is null) return Task.CompletedTask;

        var party = new DiscordPresenceParty(
            PartyId: HashPartyId(serverShareUrl),
            JoinSecret: EncodeJoinSecret(serverShareUrl),
            MaxSize: PartyMaxSize);

        // Default to InPrivateServer when we set a party — the count is unknown at this seam,
        // DiscordPresenceLifecycle (item 7) will follow up with a precise UpdateStateAsync.
        var state = new RichPresenceState(PresenceMode.InPrivateServer, ActiveAccountCount: 1, CurrentActivity: "In a private server");
        var payload = BuildPayload(state, party);
        TrySetPresence(client, payload);
        return Task.CompletedTask;
    }

    public Task ClearPartyAsync(CancellationToken ct)
    {
        var client = _client;
        if (client is null) return Task.CompletedTask;

        // Re-emit current state without a party. Without finer-grained state knowledge here,
        // emit Idle/0 — DiscordPresenceLifecycle (item 7) immediately follows up with the real
        // count when its AccountStarted/Stopped handler fires.
        var payload = BuildPayload(new RichPresenceState(PresenceMode.Idle, 0, null), party: null);
        TrySetPresence(client, payload);
        return Task.CompletedTask;
    }

    public event EventHandler<JoinRequestedEventArgs>? JoinRequested;

    public ValueTask DisposeAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sync IDisposable shim so the DI container's sync ServiceProvider.Dispose() can shut us
    /// down without throwing InvalidOperationException ("type only implements IAsyncDisposable").
    /// Teardown is sync-safe — cancels timers, unsubscribes events, calls client.Dispose().
    /// </summary>
    public void Dispose() => DisposeCore();

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        _config.Changed -= OnConfigChanged;
        TeardownClient();
    }

    // ---- Internals ----

    internal static DiscordPresencePayload BuildPayload(RichPresenceState state, DiscordPresenceParty? party)
    {
        var (largeKey, largeText, smallKey, smallText) = state.Mode switch
        {
            PresenceMode.Idle              => (IdleLargeKey, "ROROROblox", IdleSmallKey, "626 Labs"),
            PresenceMode.AccountsActive    => (ActiveLargeKey, "ROROROblox", ActiveSmallKey, "626 Labs"),
            PresenceMode.InPrivateServer   => (ActiveLargeKey, "ROROROblox", ActiveSmallKey, "626 Labs"),
            _                              => (IdleLargeKey, "ROROROblox", IdleSmallKey, "626 Labs"),
        };

        string? details = state.Mode switch
        {
            PresenceMode.Idle => "Idle",
            PresenceMode.AccountsActive => $"{state.ActiveAccountCount} account{(state.ActiveAccountCount == 1 ? "" : "s")} active",
            PresenceMode.InPrivateServer => "In a private server",
            _ => null,
        };

        return new DiscordPresencePayload(
            State: state.CurrentActivity,
            Details: details,
            LargeImageKey: largeKey,
            LargeImageText: largeText,
            SmallImageKey: smallKey,
            SmallImageText: smallText,
            Party: party);
    }

    /// <summary>SHA256-truncated party id. Discord caps party ids; 16 hex chars stays well under.</summary>
    internal static string HashPartyId(string serverShareUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(serverShareUrl));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>Encode the share URL as a Discord join secret. Round-trippable via <see cref="DecodeJoinSecret"/>.</summary>
    internal static string EncodeJoinSecret(string serverShareUrl) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(serverShareUrl));

    internal static string DecodeJoinSecret(string joinSecret) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(joinSecret));

    private void ConnectClient()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_client is not null) return; // already connected/connecting

            try
            {
                var client = _clientFactory(_applicationId!);
                client.JoinRequested += OnRpcJoinRequested;
                client.ConnectionFailed += OnRpcConnectionFailed;
                client.Ready += OnRpcReady;
                client.Errored += OnRpcErrored;
                client.PresenceUpdated += OnRpcPresenceUpdated;
                client.Initialize();
                _client = client;
                _started = true;
                _log.LogInformation("Discord RPC client initialized for ApplicationId {AppId}.", Redact(_applicationId!));

                // Push initial Idle presence immediately so Discord has something to display
                // before any account is launched. Without this the user sees nothing on their
                // profile card — Discord shows the LAST-set presence, and an unset presence is
                // just absence. Lachee buffers SetPresence calls until the pipe is ready, so
                // calling synchronously here is safe; the actual IPC payload flushes on Ready.
                var idle = new RichPresenceState(PresenceMode.Idle, 0, null);
                TrySetPresence(client, BuildPayload(idle, party: null));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Discord RPC client initialize failed; scheduling reconnect.");
                ScheduleReconnect();
            }
        }
    }

    private void TeardownClient()
    {
        IDiscordRpcClient? client;
        lock (_gate)
        {
            client = _client;
            _client = null;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            _reconnectStep = 0;
            _started = false;
        }
        if (client is null) return;
        try
        {
            client.JoinRequested -= OnRpcJoinRequested;
            client.ConnectionFailed -= OnRpcConnectionFailed;
            client.Ready -= OnRpcReady;
            client.Errored -= OnRpcErrored;
            client.PresenceUpdated -= OnRpcPresenceUpdated;
            try { client.ClearPresence(); } catch { /* best effort */ }
            try { client.Deinitialize(); } catch { /* best effort */ }
            client.Dispose();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Discord RPC client teardown threw; ignoring.");
        }
    }

    private void TrySetPresence(IDiscordRpcClient client, DiscordPresencePayload payload)
    {
        try
        {
            client.SetPresence(payload);
            _log.LogInformation(
                "Discord SetPresence queued: details={Details} largeKey={LargeKey} party={HasParty}",
                payload.Details, payload.LargeImageKey, payload.Party is not null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Discord RPC SetPresence threw; presence stale until next update.");
        }
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_config.RichPresenceEnabled)
        {
            // Flipped ON (or stayed ON with other changes). Spin up if we don't have a client yet.
            if (_client is null && _started == false)
            {
                _ = StartAsync(CancellationToken.None);
            }
        }
        else
        {
            // Flipped OFF. Tear down without scheduling a reconnect.
            _log.LogInformation("Discord rich-presence flipped OFF via settings; disconnecting.");
            TeardownClient();
        }
    }

    private void OnRpcJoinRequested(object? sender, string joinSecret)
    {
        try
        {
            var url = DecodeJoinSecret(joinSecret);
            JoinRequested?.Invoke(this, new JoinRequestedEventArgs(url));
        }
        catch (FormatException ex)
        {
            _log.LogWarning(ex, "Couldn't decode JoinSecret as Base64; ignoring join request.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "JoinRequested handler threw; ignoring.");
        }
    }

    private void OnRpcConnectionFailed(object? sender, EventArgs e)
    {
        _log.LogDebug("Discord RPC connection failed; scheduling reconnect.");
        ScheduleReconnect();
    }

    private void OnRpcReady(object? sender, EventArgs e)
    {
        _log.LogInformation("Discord RPC connection ready; reconnect backoff reset.");
        lock (_gate)
        {
            _reconnectStep = 0;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }
        // Belt-and-suspenders: re-push Idle on Ready so the presence is established even if the
        // pre-Ready SetPresence in ConnectClient raced or got lost. Lachee's queue should handle
        // pre-Ready calls but this guarantees Discord has a payload to display by the time the
        // user looks at their profile card.
        var client = _client;
        if (client is not null)
        {
            var idle = new RichPresenceState(PresenceMode.Idle, 0, null);
            TrySetPresence(client, BuildPayload(idle, party: null));
        }
    }

    private void OnRpcErrored(object? sender, string message)
    {
        // Discord-side rejection. Most likely cause: malformed payload, missing required field,
        // or asset-key referenced that isn't uploaded to the dev portal. Log loud so we see it.
        _log.LogWarning("Discord RPC error from server: {Message}", message);
    }

    private void OnRpcPresenceUpdated(object? sender, EventArgs e)
    {
        // Proof Discord acknowledged the presence — if we never see this line in the log,
        // SetPresence is being silently dropped on the Discord side.
        _log.LogInformation("Discord acknowledged presence update.");
    }

    private void ScheduleReconnect()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _reconnectTimer?.Dispose();
            var idx = Math.Min(_reconnectStep, BackoffSecondsLadder.Length - 1);
            var delay = TimeSpan.FromSeconds(BackoffSecondsLadder[idx]);
            _reconnectStep = Math.Min(_reconnectStep + 1, BackoffSecondsLadder.Length - 1);
            _reconnectTimer = new Timer(_ => ReconnectTick(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReconnectTick()
    {
        if (_disposed) return;
        if (!_config.RichPresenceEnabled) return;
        // Tear down any half-state, then attempt a fresh init.
        TeardownClient();
        ConnectClient();
    }

    private static string Redact(string applicationId)
    {
        if (applicationId.Length <= 6) return "***";
        return string.Concat(applicationId.AsSpan(0, 4), "…", applicationId.AsSpan(applicationId.Length - 2));
    }

    /// <summary>Test-only: surface the reconnect ladder so DiscordRichPresenceServiceTests can assert.</summary>
    internal static IReadOnlyList<int> ReconnectBackoffSeconds => BackoffSecondsLadder;
}
