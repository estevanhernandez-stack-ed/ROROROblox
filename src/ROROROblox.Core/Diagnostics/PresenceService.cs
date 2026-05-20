using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Default <see cref="IPresenceService"/>. Each tick (or each direct <see cref="PollOnceAsync"/>
/// call) snapshots the current <see cref="PresenceTarget"/> set, queries each account's own
/// presence with its own cookie, resolves the game name (cached by place id), and raises
/// <see cref="AccountPresenceUpdated"/>. No WPF dependencies — lives in Core beside
/// <see cref="RobloxProcessTracker"/> and is unit-testable with a stub <see cref="IRobloxApi"/>.
/// Spec §1.
/// </summary>
/// <remarks>
/// Item 1 (v1.5.0) scope: poll loop + presence→event mapping + game-name cache. Resilience
/// (401 expired-signal, 429 backoff, hold-last-on-failure) and the fast-confirm immediate re-poll
/// are item 2; the loop is structured so they slot in around <see cref="PollTargetAsync"/> without
/// reshaping it.
/// </remarks>
public sealed class PresenceService : IPresenceService, IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(25);

    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly Func<IReadOnlyList<PresenceTarget>> _snapshotProvider;
    private readonly ILogger<PresenceService> _log;
    private readonly TimeSpan _pollInterval;

    // Resolved game names keyed by place id. Checked before any metadata fetch so repeated polls
    // for an account sitting in the same game don't re-hit GetGameMetadataByPlaceIdAsync.
    private readonly ConcurrentDictionary<long, string> _gameNameCache = new();

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _disposed;

    public PresenceService(
        IRobloxApi api,
        IAccountStore accountStore,
        Func<IReadOnlyList<PresenceTarget>> snapshotProvider)
        : this(api, accountStore, snapshotProvider, NullLogger<PresenceService>.Instance) { }

    public PresenceService(
        IRobloxApi api,
        IAccountStore accountStore,
        Func<IReadOnlyList<PresenceTarget>> snapshotProvider,
        ILogger<PresenceService> log,
        TimeSpan? pollInterval = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public event EventHandler<AccountPresenceEventArgs>? AccountPresenceUpdated;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loopTask is not null) return; // already running

        _timer = new PeriodicTimer(_pollInterval);
        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_timer, _loopCts.Token);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() / dispose — expected.
        }
    }

    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        var targets = _snapshotProvider();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            await PollTargetAsync(target, ct).ConfigureAwait(false);
        }
    }

    private async Task PollTargetAsync(PresenceTarget target, CancellationToken ct)
    {
        // Cookie is used only in-memory for the HTTPS call and is NEVER logged (CLAUDE.md DPAPI
        // rule). Log only the account id + resolved presence type.
        var cookie = await _accountStore.RetrieveCookieAsync(target.AccountId).ConfigureAwait(false);
        var presences = await _api.GetPresenceAsync(cookie, [target.RobloxUserId]).ConfigureAwait(false);

        var presence = MatchPresence(presences, target.RobloxUserId);
        var presenceType = presence?.PresenceType ?? UserPresenceType.Offline;
        long? placeId = presence?.PlaceId;

        string? gameName = null;
        if (presenceType == UserPresenceType.InGame && placeId is { } pid)
        {
            gameName = await ResolveGameNameAsync(pid, ct).ConfigureAwait(false);
        }

        _log.LogDebug("Presence for account {AccountId}: {PresenceType}", target.AccountId, presenceType);

        AccountPresenceUpdated?.Invoke(this, new AccountPresenceEventArgs(
            target.AccountId,
            presenceType,
            placeId,
            gameName,
            DateTimeOffset.UtcNow));
    }

    private static UserPresence? MatchPresence(IReadOnlyList<UserPresence> presences, long userId)
    {
        if (presences.Count == 0) return null;
        foreach (var p in presences)
        {
            if (p.UserId == userId) return p;
        }
        // Single-user self-query: if the id doesn't echo back exactly, the lone entry is still ours.
        return presences[0];
    }

    private async Task<string?> ResolveGameNameAsync(long placeId, CancellationToken ct)
    {
        if (_gameNameCache.TryGetValue(placeId, out var cached))
        {
            return cached;
        }

        var meta = await _api.GetGameMetadataByPlaceIdAsync(placeId).ConfigureAwait(false);
        var name = meta?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            _gameNameCache[placeId] = name;
            return name;
        }

        // Unresolved (place not found / network blip). Don't cache — retry next poll. The
        // ViewModel falls back to "In a game" when the name hasn't resolved yet (spec §2).
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
