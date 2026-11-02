using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// One account the facts lane may refresh, as the composition root reads it from the live roster's
/// snapshot (never from the UI-owned collection): its id, its resolved Roblox user id if the
/// backfill has found it, the facts the row currently holds, and whether its session is expired.
/// </summary>
public sealed record FactsTarget(Guid Id, long? RobloxUserId, AccountFacts? Facts, bool SessionExpired);

/// <summary>Payload for <see cref="AccountFactsService.AccountFactsUpdated"/>.</summary>
public sealed class AccountFactsUpdatedEventArgs : EventArgs
{
    public AccountFactsUpdatedEventArgs(Guid accountId, AccountFacts facts)
    {
        AccountId = accountId;
        Facts = facts;
    }

    public Guid AccountId { get; }
    public AccountFacts Facts { get; }
}

/// <summary>
/// The account-facts lane: Robux, friends online and membership, one account at a time, from three
/// triggers and no others (panel expand, launch end, a slow timer). Spec 2026-11-01 §2.3.
/// <para>
/// THE STARTUP RULE, and why it is the easiest mistake in this cycle. v1.1.2.0 called
/// <c>users/authenticated</c> for every saved account at startup, right after the cookies came off
/// disk. "Hitting users.roblox.com/v1/users/authenticated for every saved account at startup ...
/// pattern-matches Roblox's anti-fraud heuristics. They flag the session for re-verification, our
/// 401 handler maps that to CookieExpiredException, the badge flips to 'expired', and the user gets
/// surprise 2FA prompts on the next Launch As even though the cookie is minutes old"
/// (<c>App.RunStartupChecksAsync</c>, the v1.1.2 note). A facts sweep at startup is the same
/// mistake with three endpoints instead of one. So: constructing this class does nothing, loading
/// eight accounts does nothing, <see cref="Start"/> only readies a worker that waits for a request,
/// and the timer is armed by <see cref="StartTimer"/> only after the user-id backfill has finished,
/// with its first tick a full <see cref="TimerTick"/> later. <see cref="RequestCount"/> exists so a
/// test can assert zero.
/// </para>
/// <para>
/// A 401 or 403 from any call is recorded in <see cref="AccountFacts.LastError"/> and nothing else.
/// This class has no reference to the presence service or the view model: presence stays the only
/// authority on an account's health, and a facts failure is one more reason to let it decide.
/// </para>
/// <para>
/// MEMBERSHIP IS OFF BY DEFAULT. The validate-membership endpoint is undocumented and marked for
/// deprecation (Roblox Plus replaced Premium on 2026-04-30), and its only measurement is checkpoint
/// C1's. Until then no request is made to it, <see cref="AccountFacts.Membership"/> stays null and no
/// membership <see cref="AccountFacts.LastError"/> is ever set (a dead endpoint would otherwise paint
/// "last try failed (404)" on every row whose Robux and friends refreshed fine). C1 flips it, by passing
/// <c>membershipEnabled: true</c> at the composition root, only on a measured answer.
/// </para>
/// <para>
/// Cookie hygiene: the plaintext cookie lives in a local for the span of one refresh, is passed to
/// <see cref="IAccountFactsApi"/>, and appears in no log template, exception message or event.
/// </para>
/// </summary>
public sealed class AccountFactsService : IDisposable
{
    /// <summary>An expand (or launch end) on facts younger than this enqueues nothing.</summary>
    public static readonly TimeSpan MinRefreshAge = TimeSpan.FromSeconds(60);

    /// <summary>The slow timer's period; each tick enqueues at most one account.</summary>
    public static readonly TimeSpan TimerTick = TimeSpan.FromSeconds(60);

    /// <summary>A tick only picks an account whose facts are older than this.</summary>
    public static readonly TimeSpan TimerRefreshAge = TimeSpan.FromMinutes(10);

    private const int QueueCapacity = 64;
    private const string MembershipPlus = "plus";
    private const string MembershipNone = "none";

    private readonly IAccountStore _store;
    private readonly IAccountFactsApi _api;
    private readonly Func<IReadOnlyList<FactsTarget>> _targets;
    private readonly ILogger<AccountFactsService> _log;
    private readonly TimeProvider _time;
    private readonly int _interAccountDelayMs;
    private readonly bool _membershipEnabled;
    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait, // TryWrite fails when full; Enqueue drops and un-marks.
    });
    private readonly object _gate = new();
    private readonly HashSet<Guid> _pending = []; // queued or in flight: the de-duplication set.
    // When we last TRIED each account this run, success or not. A failing account keeps its old
    // FetchedAtUtc, so without this the timer would pick the same dead account every minute and
    // starve the others.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastAttempt = new();
    private readonly CancellationTokenSource _cts = new();
    private DateTimeOffset? _lastRefreshEndedUtc;
    private Task? _worker;
    private Task? _timerLoop;
    private PeriodicTimer? _timer;
    private int _requestCount;
    private bool _disposed;

    /// <param name="store">Cookie reads and the facts cache.</param>
    /// <param name="api">The three lookups.</param>
    /// <param name="targets">Reads the live roster. Must be safe off the UI thread (a snapshot).</param>
    /// <param name="log">Optional.</param>
    /// <param name="time">Clock and timer source; tests pass a manual one.</param>
    /// <param name="interAccountDelayMs">Base wait between accounts; tests may pass 0.</param>
    /// <param name="membershipEnabled">
    /// Whether to call validate-membership at all. Default false: flipped only on checkpoint C1's measured answer.
    /// </param>
    public AccountFactsService(
        IAccountStore store,
        IAccountFactsApi api,
        Func<IReadOnlyList<FactsTarget>> targets,
        ILogger<AccountFactsService>? log = null,
        TimeProvider? time = null,
        int interAccountDelayMs = BackfillPacing.InterAccountDelayMs,
        bool membershipEnabled = false)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _log = log ?? NullLogger<AccountFactsService>.Instance;
        _time = time ?? TimeProvider.System;
        _interAccountDelayMs = interAccountDelayMs;
        _membershipEnabled = membershipEnabled;
    }

    /// <summary>True when validate-membership is part of a refresh (three calls instead of two).</summary>
    public bool MembershipEnabled => _membershipEnabled;

    /// <summary>
    /// Raised after each refresh has been written to the store (including a failed one, which
    /// carries the previous values and the reason). Raised on the worker thread; subscribers marshal.
    /// </summary>
    public event EventHandler<AccountFactsUpdatedEventArgs>? AccountFactsUpdated;

    /// <summary>How many HTTP lookups this instance has started. Zero until the first enqueued refresh runs.</summary>
    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <summary>True once <see cref="StartTimer"/> has armed the slow timer.</summary>
    public bool TimerArmed
    {
        get { lock (_gate) { return _timer is not null; } }
    }

    /// <summary>
    /// Ready the single worker. Idempotent. Makes no request: the worker waits on the queue, which
    /// only <see cref="Enqueue"/> and the timer fill.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _worker is not null)
            {
                return;
            }
            _worker = Task.Run(() => WorkerLoopAsync(_cts.Token));
        }
    }

    /// <summary>
    /// Arm the slow timer. Call exactly once, AFTER the user-id backfill has completed; the first
    /// tick is a full <see cref="TimerTick"/> from now. Idempotent.
    /// </summary>
    public void StartTimer()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }
            _timer = new PeriodicTimer(TimerTick, _time);
            var timer = _timer;
            _timerLoop = Task.Run(() => TimerLoopAsync(timer, _cts.Token));
        }
        Start();
    }

    /// <summary>
    /// The panel-expand and launch-end trigger. Returns true when the account was queued. Nothing
    /// is queued for an unknown or expired account, for one already queued or in flight, or when
    /// its facts (or its last attempt) are younger than <see cref="MinRefreshAge"/>.
    /// </summary>
    public bool Enqueue(Guid accountId)
    {
        var target = FindTarget(accountId);
        if (target is null || target.SessionExpired)
        {
            return false;
        }
        var age = AgeOf(target);
        if (age is { } a && a < MinRefreshAge)
        {
            return false;
        }
        return TryQueue(accountId);
    }

    /// <summary>
    /// One tick of the slow timer, public so a test can drive it without a clock: enqueue the single
    /// stalest account whose facts are older than <see cref="TimerRefreshAge"/>, skipping expired
    /// accounts. Never-fetched accounts are the stalest. Returns the account queued, or null.
    /// </summary>
    public Guid? RunTick()
    {
        IReadOnlyList<FactsTarget> targets;
        try
        {
            targets = _targets();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Facts tick: reading the roster threw; skipping this tick.");
            return null;
        }

        FactsTarget? pick = null;
        var pickAge = TimeSpan.MinValue;
        foreach (var t in targets)
        {
            if (t.SessionExpired)
            {
                continue;
            }
            // Never fetched sorts as infinitely old; strictly-greater keeps the first of equals.
            var age = AgeOf(t) ?? TimeSpan.MaxValue;
            if (age > TimerRefreshAge && age > pickAge)
            {
                pick = t;
                pickAge = age;
            }
        }

        return pick is not null && TryQueue(pick.Id) ? pick.Id : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        _cts.Cancel();
        _timer?.Dispose();
        _queue.Writer.TryComplete();
    }

    // ---- internals ----

    private FactsTarget? FindTarget(Guid accountId)
    {
        try
        {
            foreach (var t in _targets())
            {
                if (t.Id == accountId)
                {
                    return t;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Facts enqueue: reading the roster threw.");
        }
        return null;
    }

    /// <summary>How long since this account's facts last refreshed or were last tried; null when neither ever happened.</summary>
    private TimeSpan? AgeOf(FactsTarget target)
    {
        var last = target.Facts?.FetchedAtUtc;
        if (_lastAttempt.TryGetValue(target.Id, out var attempt) && (last is null || attempt > last))
        {
            last = attempt;
        }
        return last is null ? null : _time.GetUtcNow() - last.Value;
    }

    private bool TryQueue(Guid accountId)
    {
        lock (_gate)
        {
            if (_disposed || !_pending.Add(accountId))
            {
                return false;
            }
            if (!_queue.Writer.TryWrite(accountId))
            {
                _pending.Remove(accountId);
                return false;
            }
        }
        Start();
        return true;
    }

    private async Task TimerLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                RunTick();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "The account-facts timer loop stopped unexpectedly; facts refresh only on expand and launch end until restart.");
        }
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await WaitBetweenAccountsAsync(ct).ConfigureAwait(false);
                    if (await RefreshAsync(id, ct).ConfigureAwait(false))
                    {
                        _lastRefreshEndedUtc = _time.GetUtcNow();
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Facts refresh for {AccountId} threw; the lane continues.", id);
                }
                finally
                {
                    lock (_gate)
                    {
                        _pending.Remove(id);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    /// <summary>
    /// Two accounts' facts are never requested in the same second: the next refresh starts no
    /// sooner than a jittered <see cref="BackfillPacing"/> delay after the previous one ended. A
    /// refresh arriving long after the last one waits for nothing.
    /// </summary>
    private async Task WaitBetweenAccountsAsync(CancellationToken ct)
    {
        if (_interAccountDelayMs <= 0 || _lastRefreshEndedUtc is not { } ended)
        {
            return;
        }
        var wanted = TimeSpan.FromMilliseconds(Math.Max(0,
            _interAccountDelayMs + Random.Shared.Next(-BackfillPacing.JitterMs, BackfillPacing.JitterMs)));
        var remaining = wanted - (_time.GetUtcNow() - ended);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _time, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Returns true when at least one request was made (so the pacing clock should restart).</summary>
    private async Task<bool> RefreshAsync(Guid id, CancellationToken ct)
    {
        var target = FindTarget(id);
        if (target is null || target.SessionExpired)
        {
            return false; // removed or expired between the enqueue and now: no cookie to spend.
        }

        string cookie;
        try
        {
            cookie = await _store.RetrieveCookieAsync(id).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Facts: could not read the cookie for {AccountId}; skipping.", id);
            return false;
        }
        if (string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        var now = _time.GetUtcNow();
        _lastAttempt[id] = now;
        var previous = target.Facts;

        var outcome = await FetchAsync(cookie, target.RobloxUserId, previous, ct).ConfigureAwait(false);
        var facts = outcome.Facts is { } fetched
            ? fetched with { FetchedAtUtc = now }
            : (previous ?? new AccountFacts(null, null, null, null, null)) with { LastError = outcome.Error };

        try
        {
            await _store.SetFactsAsync(id, facts).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogDebug(ex, "Facts: persisting the refresh for {AccountId} failed; the row still updates.", id);
        }

        _log.LogDebug("Facts refreshed for {AccountId}: error={Error}", id, facts.LastError ?? "none");
        RaiseUpdated(id, facts);
        return true;
    }

    /// <summary>
    /// The calls, sequentially (two, or three when <see cref="MembershipEnabled"/>). Robux and friends-online are the core: any failure there, and a
    /// 401 or 403 from any of the three, abandons the refresh and keeps the previous values whole
    /// (a row never mixes a fresh Robux with a stale friends count under one age). Membership is the
    /// gated, deprecation-marked one: a non-auth failure there only blanks membership.
    /// </summary>
    private async Task<FetchOutcome> FetchAsync(string cookie, long? userId, AccountFacts? previous, CancellationToken ct)
    {
        long? robux;
        try
        {
            Interlocked.Increment(ref _requestCount);
            robux = await _api.GetRobuxAsync(cookie, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Describe(ex, ct) is { } code)
        {
            return FetchOutcome.Failed(code);
        }
        if (robux is null)
        {
            return FetchOutcome.Failed("parse");
        }

        // No resolved user id yet: friends-online and membership cannot be asked. Robux only.
        if (userId is not > 0)
        {
            return FetchOutcome.Ok(new AccountFacts(robux, _membershipEnabled ? previous?.Membership : null, previous?.FriendsOnline, null, null));
        }

        int? friends;
        try
        {
            Interlocked.Increment(ref _requestCount);
            friends = await _api.GetFriendsOnlineCountAsync(cookie, userId.Value, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Describe(ex, ct) is { } code)
        {
            return FetchOutcome.Failed(code);
        }
        if (friends is null)
        {
            return FetchOutcome.Failed("parse");
        }

        if (!_membershipEnabled)
        {
            return FetchOutcome.Ok(new AccountFacts(robux, null, friends, null, null));
        }

        MembershipProbe probe;
        try
        {
            Interlocked.Increment(ref _requestCount);
            probe = await _api.GetMembershipAsync(cookie, userId.Value, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Describe(ex, ct) is { } code)
        {
            return FetchOutcome.Failed(code);
        }

        if (probe.Status is 401 or 403)
        {
            return FetchOutcome.Failed(probe.Status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var (membership, membershipError) = MapMembership(probe);
        return FetchOutcome.Ok(new AccountFacts(robux, membership, friends, null, membershipError));
    }

    /// <summary>
    /// 200 with body <c>true</c> is "plus" (the badge word for any true: the endpoint cannot tell
    /// Plus from legacy Premium, and Premium closed to new members 2026-04-30); 200 with
    /// <c>false</c> is "none"; anything else is unknown (null) with the reason. A 403 is unknown,
    /// not "none" (handled by the caller before this point).
    /// </summary>
    private static (string? Membership, string? Error) MapMembership(MembershipProbe probe)
    {
        if (probe.Status == 200)
        {
            var body = probe.Body?.Trim();
            if (string.Equals(body, "true", StringComparison.OrdinalIgnoreCase))
            {
                return (MembershipPlus, null);
            }
            if (string.Equals(body, "false", StringComparison.OrdinalIgnoreCase))
            {
                return (MembershipNone, null);
            }
            return (null, "membership-body");
        }
        return (null, probe.Status.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Short non-secret reason for a failed lookup, or null when the exception is not one to swallow
    /// (our own cancellation). Never includes the exception message: a message is free text.
    /// </summary>
    private static string? Describe(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }
        return ex switch
        {
            AccountFactsRequestException http => http.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OperationCanceledException => "timeout",
            HttpRequestException => "network",
            _ => "error",
        };
    }

    private void RaiseUpdated(Guid id, AccountFacts facts)
    {
        try
        {
            AccountFactsUpdated?.Invoke(this, new AccountFactsUpdatedEventArgs(id, facts));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Facts: an AccountFactsUpdated subscriber threw for {AccountId}; continuing.", id);
        }
    }

    private readonly record struct FetchOutcome(AccountFacts? Facts, string? Error)
    {
        public static FetchOutcome Ok(AccountFacts facts) => new(facts, null);
        public static FetchOutcome Failed(string error) => new(null, error);
    }
}
