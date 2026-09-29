using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// One presence/process reading for an account auto-rejoin might act on, offered to a single
/// <see cref="AutoRejoinMonitor.Tick"/> call.
/// </summary>
public sealed record AutoRejoinCandidate(
    Guid AccountId,
    bool Enabled,
    bool IsMain,
    bool IsRunning,
    bool InGame,
    ServerInstance? CurrentServer,
    bool StopInProgress);

/// <summary>
/// What <see cref="AutoRejoinMonitor.Tick"/> wants done for one account. This type carries the
/// decision only — task 7's view model is the thing that actually stops or relaunches a client.
/// </summary>
public abstract record AutoRejoinAction(Guid AccountId)
{
    /// <summary>
    /// Relaunch the account into <paramref name="LastServer"/> (or its default target, when null —
    /// no server has ever been seen). <paramref name="FailedJoin"/> is true when the account never
    /// made it in-game since its most recent launch: the 5-minute first-join grace expired instead
    /// of the normal 3-minute out-of-game drop, which the caller may want to log or surface
    /// differently than an ordinary idle-kick recovery.
    /// </summary>
    public sealed record Rejoin(Guid AccountId, ServerInstance? LastServer, bool FailedJoin) : AutoRejoinAction(AccountId);

    /// <summary>
    /// The account hit <see cref="AutoRejoinMonitor.BudgetPerWindow"/> rejoins inside
    /// <see cref="AutoRejoinMonitor.BudgetWindow"/>. Auto-rejoin is now paused for it until
    /// <see cref="AutoRejoinMonitor.Resume"/> is called; the caller should alert rather than retry.
    /// </summary>
    public sealed record Pause(Guid AccountId) : AutoRejoinAction(AccountId);
}

/// <summary>
/// Decides whether an opted-in alt whose Roblox client is still running, but hasn't been seen in a
/// game long enough to look like an idle kick, a failed join, or a captcha page, should be
/// relaunched — or whether it has burned its hourly rejoin budget and should pause instead.
/// <para>
/// Pure: every decision is a function of the <see cref="AutoRejoinCandidate"/> list and the
/// <c>now</c> passed to <see cref="Tick"/>, plus this instance's own per-account state. There is no
/// <see cref="DateTimeOffset.UtcNow"/> anywhere in this class and no I/O — task 7 owns the 30-second
/// UI timer and passes time in, so the rules here are exercised in tests without a clock.
/// </para>
/// <para>
/// <b>Single-threaded by contract, not by locking.</b> The view model calls every member —
/// <see cref="Tick"/>, <see cref="NotifyLaunched"/>, <see cref="NotifyRejoinSkipped"/>,
/// <see cref="Resume"/>, <see cref="IsPaused"/> — from the UI thread only. Nothing here takes a
/// lock; calling it from more than one thread is a bug in the caller, not something this class
/// guards against.
/// </para>
/// </summary>
public sealed class AutoRejoinMonitor
{
    /// <summary>
    /// How long the client can sit outside a game, once it has been in one since its last launch,
    /// before that counts as a drop worth rejoining. Measured on the live rig (spec): RoRoRo's
    /// presence poll saw an idle kick 18 s after it happened, against a 25 s poll interval — 3
    /// minutes leaves comfortable margin against poll lag without sitting out a real, if slow, load
    /// screen.
    /// </summary>
    public static readonly TimeSpan OutOfGameLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a freshly launched client gets before its first join counts as a drop. Longer than
    /// <see cref="OutOfGameLimit"/> because a cold client legitimately takes longer to reach its
    /// first game than a warm one takes to recover from a kick.
    /// </summary>
    public static readonly TimeSpan FirstJoinGrace = TimeSpan.FromMinutes(5);

    /// <summary>The rolling window the per-account rejoin budget is measured against.</summary>
    public static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Rejoins allowed per account inside <see cref="BudgetWindow"/> before auto-rejoin pauses
    /// itself for that account. An account that needs a fourth rejoin inside an hour isn't having a
    /// bad server, it's stuck — repeatedly relaunching it just burns the machine.
    /// </summary>
    public const int BudgetPerWindow = 3;

    private sealed class State
    {
        public DateTimeOffset? LastInGameAt;
        public DateTimeOffset? WatchSince;
        public bool EverInGameSinceLaunch;
        public ServerInstance? LastServer;
        public readonly List<DateTimeOffset> RejoinTimes = [];
        public bool InFlight;
        public bool Paused;
    }

    private readonly Dictionary<Guid, State> _states = [];

    private State GetOrCreate(Guid accountId)
    {
        if (!_states.TryGetValue(accountId, out var state))
        {
            state = new State();
            _states[accountId] = state;
        }
        return state;
    }

    /// <summary>
    /// Evaluate every candidate against <paramref name="now"/> and this monitor's own per-account
    /// state, returning the actions (if any) the caller should carry out. Accounts absent from
    /// <paramref name="accounts"/> have their state dropped before this call returns — a removed
    /// row (account deleted, list rebuilt without it) does not leak state forever.
    /// </summary>
    public IReadOnlyList<AutoRejoinAction> Tick(DateTimeOffset now, IReadOnlyList<AutoRejoinCandidate> accounts)
    {
        List<AutoRejoinAction>? actions = null;
        var seen = new HashSet<Guid>(accounts.Count);

        foreach (var candidate in accounts)
        {
            seen.Add(candidate.AccountId);
            var state = GetOrCreate(candidate.AccountId);

            // Rule 1: opted out, the main, already paused, or a rejoin we started is still
            // in flight. Forgetting WatchSince only on the not-enabled path means flipping the
            // toggle back on later starts a fresh clock rather than reusing a stale one.
            if (!candidate.Enabled || candidate.IsMain || state.Paused || state.InFlight)
            {
                if (!candidate.Enabled)
                    state.WatchSince = null;
                continue;
            }

            // Rule 2: the client itself is gone, or a stop we initiated is mid-flight. A closed
            // client is never relaunched, and a stop-in-progress isn't a drop to react to.
            if (!candidate.IsRunning || candidate.StopInProgress)
            {
                state.WatchSince = null;
                state.EverInGameSinceLaunch = false;
                state.LastInGameAt = null;
                continue;
            }

            // Rule 3: in a game right now — record it and move on.
            if (candidate.InGame)
            {
                state.LastInGameAt = now;
                state.EverInGameSinceLaunch = true;
                state.LastServer = candidate.CurrentServer ?? state.LastServer;
                continue;
            }

            // Rule 4: running, not in a game. Start (or keep) the watch clock and check whether
            // it's been long enough to act — the first-join grace before ever joining once,
            // the shorter out-of-game limit after that.
            state.WatchSince ??= now;
            var due = state.EverInGameSinceLaunch
                ? now - state.LastInGameAt!.Value >= OutOfGameLimit
                : now - state.WatchSince!.Value >= FirstJoinGrace;
            if (!due)
                continue;

            // Rule 5: due. Age the budget window out, then either spend a slot on a rejoin or
            // pause if the account is already tapped out for the hour.
            var cutoff = now - BudgetWindow;
            state.RejoinTimes.RemoveAll(t => t < cutoff);

            if (state.RejoinTimes.Count >= BudgetPerWindow)
            {
                state.Paused = true;
                (actions ??= []).Add(new AutoRejoinAction.Pause(candidate.AccountId));
            }
            else
            {
                state.RejoinTimes.Add(now);
                state.InFlight = true;
                (actions ??= []).Add(new AutoRejoinAction.Rejoin(
                    candidate.AccountId, state.LastServer, FailedJoin: !state.EverInGameSinceLaunch));
            }
        }

        if (_states.Count > seen.Count)
        {
            List<Guid>? stale = null;
            foreach (var id in _states.Keys)
            {
                if (!seen.Contains(id))
                    (stale ??= []).Add(id);
            }
            if (stale is not null)
            {
                foreach (var id in stale)
                    _states.Remove(id);
            }
        }

        return (IReadOnlyList<AutoRejoinAction>?)actions ?? [];
    }

    /// <summary>
    /// A launch of ours started for this account. Clears in-flight and resets the watch clock so
    /// the fresh client gets its own <see cref="FirstJoinGrace"/> rather than inheriting whatever
    /// drop triggered the relaunch.
    /// </summary>
    public void NotifyLaunched(Guid accountId, DateTimeOffset now)
    {
        var state = GetOrCreate(accountId);
        state.InFlight = false;
        state.WatchSince = now;
        state.EverInGameSinceLaunch = false;
    }

    /// <summary>
    /// The view model couldn't act on a <see cref="AutoRejoinAction.Rejoin"/> it was just handed (a
    /// modal was already open, a squad launch was mid-flight, whatever). Refunds the budget slot
    /// that decision spent — removes the newest <c>RejoinTimes</c> entry, the one this rejoin just
    /// added — and clears in-flight so the next <see cref="Tick"/> can try again.
    /// </summary>
    public void NotifyRejoinSkipped(Guid accountId)
    {
        var state = GetOrCreate(accountId);
        state.InFlight = false;
        if (state.RejoinTimes.Count > 0)
            state.RejoinTimes.RemoveAt(state.RejoinTimes.Count - 1);
    }

    /// <summary>
    /// The user turned auto-rejoin back on for a paused account. Clears the pause and the budget
    /// history — a resume is a fresh start, not a partially refilled one.
    /// </summary>
    public void Resume(Guid accountId)
    {
        var state = GetOrCreate(accountId);
        state.Paused = false;
        state.RejoinTimes.Clear();
    }

    /// <summary>True if this account is currently paused (fourth drop inside the hour).</summary>
    public bool IsPaused(Guid accountId) => _states.TryGetValue(accountId, out var state) && state.Paused;
}
