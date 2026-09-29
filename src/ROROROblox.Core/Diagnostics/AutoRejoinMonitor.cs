using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// One presence/process reading for an account auto-rejoin might act on, offered to a single
/// <see cref="AutoRejoinMonitor.Tick"/> call.
/// </summary>
/// <param name="PresenceKnown">False when presence can't be read for this account at all (no
/// Roblox user id, a rate-limited or expired session). <paramref name="InGame"/> is meaningless
/// then: the monitor holds (no out-of-game time accrues, nothing is rejoined) rather than reading
/// "no information" as "out of game".</param>
public sealed record AutoRejoinCandidate(
    Guid AccountId,
    bool Enabled,
    bool IsMain,
    bool IsRunning,
    bool InGame,
    ServerInstance? CurrentServer,
    bool StopInProgress,
    bool PresenceKnown = true);

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
/// <para>
/// <b>Every <see cref="Tick"/> call must pass every candidate row</b> the caller currently knows
/// about, not a filtered subset. An id absent from one call's <c>accounts</c> list is read as "this
/// account row was removed" — its state, including <see cref="IsPaused"/> and the rejoin budget, is
/// forgotten before that call returns. Passing a filtered list (e.g. "just the running ones") would
/// silently reset a paused or budget-tracked account the next time it reappears.
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
        /// <summary>First tick of the current presence-unknown stretch; null while presence is known.</summary>
        public DateTimeOffset? UnknownSince;

        /// <summary>Forget the watch clock, so the next reading starts a fresh one.</summary>
        public void ResetWatch()
        {
            WatchSince = null;
            EverInGameSinceLaunch = false;
            LastInGameAt = null;
            UnknownSince = null;
        }
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
    /// state, returning the actions (if any) the caller should carry out.
    /// <para>
    /// <b>Contract:</b> <paramref name="accounts"/> must be the caller's full, current row list on
    /// every call, not a filtered subset. An id absent here is treated as "that row is gone" — its
    /// state is dropped before this call returns, including <see cref="IsPaused"/> and the rejoin
    /// budget, so a removed account does not leak state forever. If the caller later reintroduces
    /// the same id (re-added account, or a filter that was only temporary), it starts over as if it
    /// were brand new: not paused, empty budget.
    /// </para>
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
            // in flight. The watch clock is forgotten on every path but in-flight, so turning the
            // toggle back on (or Resume) starts a fresh clock rather than reusing a stale
            // LastInGameAt from before the account was switched off.
            if (!candidate.Enabled || candidate.IsMain || state.Paused || state.InFlight)
            {
                if (!state.InFlight)
                    state.ResetWatch();
                continue;
            }

            // Rule 2: the client itself is gone, or a stop we initiated is mid-flight. A closed
            // client is never relaunched, and a stop-in-progress isn't a drop to react to.
            if (!candidate.IsRunning || candidate.StopInProgress)
            {
                state.ResetWatch();
                continue;
            }

            // Rule 2b: presence can't be read for this account (no user id, rate-limited or
            // expired session). "No information" is not "out of game": hold. The clock doesn't
            // advance while presence is dark; when it comes back, the dark stretch is skipped.
            if (!candidate.PresenceKnown)
            {
                state.UnknownSince ??= now;
                continue;
            }
            if (state.UnknownSince is { } darkFrom)
            {
                var dark = now - darkFrom;
                if (state.LastInGameAt is { } lastIn) state.LastInGameAt = lastIn + dark;
                if (state.WatchSince is { } since) state.WatchSince = since + dark;
                state.UnknownSince = null;
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
    /// drop triggered the relaunch. Also forgets the last server: the new client may have been
    /// sent somewhere else entirely, and a failed join must not retarget the old client's server.
    /// </summary>
    public void NotifyLaunched(Guid accountId, DateTimeOffset now)
    {
        var state = GetOrCreate(accountId);
        state.InFlight = false;
        state.WatchSince = now;
        state.EverInGameSinceLaunch = false;
        state.LastInGameAt = null;
        state.UnknownSince = null;
        state.LastServer = null;
    }

    /// <summary>
    /// The view model couldn't act on a <see cref="AutoRejoinAction.Rejoin"/> it was just handed (a
    /// modal was already open, a squad launch was mid-flight, whatever). Refunds the budget slot
    /// that decision spent — removes the newest <c>RejoinTimes</c> entry, the one this rejoin just
    /// added — and clears in-flight so the next <see cref="Tick"/> can try again.
    /// <para>
    /// A no-op when there's no outstanding rejoin to skip: an unknown <paramref name="accountId"/>,
    /// or one that isn't currently <c>InFlight</c>. Without that guard, a duplicate call (or one
    /// for an account that never had a rejoin in flight) would refund a slot nothing spent, letting
    /// more than <see cref="BudgetPerWindow"/> rejoins through inside <see cref="BudgetWindow"/>.
    /// </para>
    /// </summary>
    public void NotifyRejoinSkipped(Guid accountId)
    {
        if (!_states.TryGetValue(accountId, out var state) || !state.InFlight)
            return;

        state.InFlight = false;
        if (state.RejoinTimes.Count > 0)
            state.RejoinTimes.RemoveAt(state.RejoinTimes.Count - 1);
    }

    /// <summary>
    /// The view model acted on a <see cref="AutoRejoinAction.Rejoin"/> (the client was stopped) but
    /// the relaunch didn't land, or was abandoned. Clears in-flight so later ticks can act again,
    /// and does NOT refund: the stop happened, so the cycle counts against
    /// <see cref="BudgetPerWindow"/>. Contrast <see cref="NotifyRejoinSkipped"/>, which is for a
    /// rejoin that stopped nothing. A no-op when there's no outstanding rejoin.
    /// </summary>
    public void ClearInFlight(Guid accountId)
    {
        if (_states.TryGetValue(accountId, out var state))
            state.InFlight = false;
    }

    /// <summary>
    /// Pause auto-rejoin for this account from outside the budget rule (the view model gave up
    /// relaunching a client it had stopped). Same state a fourth drop inside the hour produces:
    /// skipped by every <see cref="Tick"/> until <see cref="Resume"/>. Also clears in-flight.
    /// </summary>
    public void Pause(Guid accountId)
    {
        var state = GetOrCreate(accountId);
        state.Paused = true;
        state.InFlight = false;
    }

    /// <summary>
    /// The user turned auto-rejoin back on for a paused account. Clears the pause, the budget
    /// history and the watch clock — a resume is a fresh start, not a partially refilled one.
    /// </summary>
    public void Resume(Guid accountId)
    {
        var state = GetOrCreate(accountId);
        state.Paused = false;
        state.RejoinTimes.Clear();
        state.ResetWatch();
    }

    /// <summary>True if this account is currently paused (fourth drop inside the hour).</summary>
    public bool IsPaused(Guid accountId) => _states.TryGetValue(accountId, out var state) && state.Paused;
}
