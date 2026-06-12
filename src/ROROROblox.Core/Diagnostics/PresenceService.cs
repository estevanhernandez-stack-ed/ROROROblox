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
/// Item 1 (v1.5.0) scope: poll loop + presence→event mapping + game-name cache.
/// <para>
/// Item 2 (v1.5.0) added the resilience layer around <see cref="PollTargetAsync"/>:
/// 401/<see cref="CookieExpiredException"/> → <see cref="AccountSessionExpired"/> (no presence
/// event); empty-list (failed call) → hold last-known (raise nothing, log Debug); a concurrency
/// cap (<see cref="MaxConcurrentPolls"/> via <see cref="SemaphoreSlim"/>) + jitter so N accounts
/// don't fire simultaneously; and the <see cref="RequestImmediateRefreshAsync"/> fast-confirm hook.
/// </para>
/// <para>
/// <b>429 limitation (intentional, item 2):</b> the spec calls for backing off "for the remainder
/// of the cycle" on HTTP 429. But <see cref="IRobloxApi.GetPresenceAsync"/> swallows 429 (and every
/// other non-401 failure) into an EMPTY list — the service cannot distinguish a 429 from a network
/// blip without changing <c>RobloxApi</c>, which is out of scope for this item. The empty-list →
/// hold-last path covers 429 functionally: we raise nothing and retry on the next 25 s tick, and
/// since each account is polled exactly once per cycle there is no within-cycle hammering to stop.
/// The real within-cycle politeness is the concurrency cap + jitter above. If true 429 backoff
/// becomes load-bearing, a future spike (item 7) can surface the status code from
/// <c>RobloxApi.GetPresenceAsync</c> — matching the spec's "measure before tuning the cadence"
/// stance. Do NOT change <c>RobloxApi</c> for this without that measurement.
/// </para>
/// </remarks>
public sealed class PresenceService : IPresenceService, IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(25);

    // Max random pre-call delay so N accounts don't fire their POSTs at the same instant. Small
    // relative to the 25 s cadence — politeness, not throttling. Tests pass TimeSpan.Zero.
    private static readonly TimeSpan DefaultMaxJitter = TimeSpan.FromMilliseconds(750);

    // Spec §1 "Concurrency / rate limits": never N simultaneous presence POSTs.
    private const int MaxConcurrentPolls = 4;

    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly Func<IReadOnlyList<PresenceTarget>> _snapshotProvider;
    private readonly ILogger<PresenceService> _log;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _maxJitter;

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
        TimeSpan? pollInterval = null,
        TimeSpan? maxJitter = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _maxJitter = maxJitter ?? DefaultMaxJitter;
    }

    public event EventHandler<AccountPresenceEventArgs>? AccountPresenceUpdated;

    public event EventHandler<Guid>? AccountSessionExpired;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // "Already running" must mean RUNNING. A completed task here would mean the loop died
        // (it shouldn't — RunLoopAsync contains per-tick failures — but if it ever does, the
        // next Start() call revives it instead of no-opping forever, which is exactly how the
        // pre-fix loop death became session-permanent).
        if (_loopTask is { IsCompleted: false }) return;

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
                try
                {
                    await PollOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // Stop() — handled by the outer catch.
                }
                catch (Exception ex)
                {
                    // One bad tick must never kill the loop. Before this catch existed, a
                    // snapshot-provider fault or store throw silently faulted _loopTask and
                    // presence — the v1.5 ghost-closed fix — was dead for the session with
                    // no signal and (because of Start()'s already-running guard) no revival.
                    _log.LogWarning(ex, "Presence poll tick failed; holding last-known state and continuing.");
                }
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
        if (targets.Count == 0) return;

        // Spec §1: stagger calls with a small concurrency cap + jitter so N accounts never fire
        // their POSTs simultaneously. The semaphore caps in-flight presence calls at
        // MaxConcurrentPolls; the per-call jitter spreads them inside the cap.
        using var gate = new SemaphoreSlim(MaxConcurrentPolls, MaxConcurrentPolls);
        var polls = new List<Task>(targets.Count);
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            polls.Add(PollWithGateAsync(target, gate, ct));
        }

        await Task.WhenAll(polls).ConfigureAwait(false);
    }

    public async Task RequestImmediateRefreshAsync(Guid accountId)
    {
        // Fast-confirm hook (spec §1): poll just this one account out-of-band. Look it up in the
        // current snapshot — if it's gone (expired / no userId), no-op. No new throttle needed; a
        // single out-of-band call is below the per-cycle politeness budget.
        var target = FindTarget(accountId);
        if (target is null) return;

        await PollTargetAsync(target, CancellationToken.None).ConfigureAwait(false);
    }

    private PresenceTarget? FindTarget(Guid accountId)
    {
        foreach (var t in _snapshotProvider())
        {
            if (t.AccountId == accountId) return t;
        }
        return null;
    }

    private async Task PollWithGateAsync(PresenceTarget target, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyJitterAsync(ct).ConfigureAwait(false);
            await PollTargetAsync(target, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // PollTargetAsync handles the EXPECTED failures (401 → session-expired event,
            // empty list → hold last-known). This catches the unexpected ones — the store
            // throwing KeyNotFound for an account removed between snapshot and poll, an
            // accounts.dat read hiccup — so one broken target degrades to hold-last-known
            // instead of faulting the whole tick through Task.WhenAll.
            _log.LogWarning(ex,
                "Presence poll for account {AccountId} failed; holding last-known state.", target.AccountId);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task ApplyJitterAsync(CancellationToken ct)
    {
        if (_maxJitter <= TimeSpan.Zero) return Task.CompletedTask;
        var delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * _maxJitter.TotalMilliseconds);
        return Task.Delay(delay, ct);
    }

    private async Task PollTargetAsync(PresenceTarget target, CancellationToken ct)
    {
        // Cookie is used only in-memory for the HTTPS call and is NEVER logged (CLAUDE.md DPAPI
        // rule). Log only the account id + resolved presence type.
        IReadOnlyList<UserPresence> presences;
        try
        {
            var cookie = await _accountStore.RetrieveCookieAsync(target.AccountId).ConfigureAwait(false);
            presences = await _api.GetPresenceAsync(cookie, [target.RobloxUserId]).ConfigureAwait(false);
        }
        catch (CookieExpiredException)
        {
            // 401 — the cookie died between launches. Flip the row to "Session expired" and do NOT
            // raise a presence event for this account (spec §1 + "Error handling": 401 from presence).
            _log.LogDebug("Presence for account {AccountId}: cookie expired (401)", target.AccountId);
            AccountSessionExpired?.Invoke(this, target.AccountId);
            return;
        }

        // GetPresenceAsync returns an EMPTY list when the CALL FAILED — 429, network, malformed
        // (RobloxApi.GetPresenceAsync swallows non-401 failures into []). A genuinely-offline
        // account still gets a populated entry (PresenceType == Offline). So empty == failure →
        // HOLD last-known state: log at Debug and raise nothing. This is also how true 429s degrade
        // gracefully (see the 429 limitation note in this file's header comment / item-2 report).
        if (presences.Count == 0)
        {
            _log.LogDebug(
                "Presence poll for account {AccountId} returned no data (call failed — holding last-known state)",
                target.AccountId);
            return;
        }

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
