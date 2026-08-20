using System;
using System.Collections.Generic;
using System.Linq;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Decides whether an unattributed Roblox client is the successor of one that just exited (F-103).
/// <para>
/// Roblox restarts itself — after an update, or any self-restart. The replacement is a process
/// RoRoRo never launched, so <c>RobloxWindowDecorator</c> never decorates it and its title stays the
/// bare <c>Roblox</c> forever. The title is also the re-attach key
/// (<c>RunningRobloxScanner</c> parses <c>"Roblox - {name}"</c>), so a bare window is
/// unattributable by construction: it counts toward the running total and the memory watchdog while
/// no account row owns it, and the row's Stop cannot reach it.
/// </para>
/// <para>
/// A self-restart is a SUCCESSION — one client leaves, another arrives in its place moments later.
/// That is what this reads. Pure: given the recent exits, the current orphans and a clock, it
/// returns the attributions it is sure of.
/// </para>
/// </summary>
public static class ClientSuccession
{
    /// <summary>
    /// How long after an exit a new client may still be read as its replacement.
    /// <para>
    /// Roblox's own restart is quick, but an update writes files first. Too short and the real
    /// successor arrives after the window closed; too long and an unrelated client the user opened
    /// a minute later gets claimed by whoever happened to exit.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    /// <param name="AccountId">The account whose client left.</param>
    /// <param name="Pid">The pid that exited — kept so a caller can tell two exits apart.</param>
    public readonly record struct Exit(Guid AccountId, int Pid, DateTimeOffset At);

    /// <param name="Pid">An untagged, unattributed RobloxPlayerBeta.</param>
    /// <param name="StartedAt">When it started, so an orphan older than the exit is not a successor.</param>
    public readonly record struct Orphan(int Pid, DateTimeOffset StartedAt);

    /// <summary>
    /// Which orphan belongs to which account, for the cases that are not a guess.
    /// <para>
    /// ONLY UNAMBIGUOUS CASES ARE ATTRIBUTED. One recent exit and one orphan is a succession. Two of
    /// each is a coin toss, and a wrong attribution puts one alt's window under another account's
    /// name — worse than leaving it unlabelled, because a label gets believed. When in doubt this
    /// returns nothing and the caller falls back to whatever it does for unknowns.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(int Pid, Guid AccountId)> Attribute(
        IReadOnlyList<Exit> recentExits, IReadOnlyList<Orphan> orphans, DateTimeOffset now)
    {
        if (recentExits is null || orphans is null) return [];

        // An orphan that started BEFORE the exit cannot be its replacement, and an exit older than
        // the window is no longer a plausible predecessor.
        var live = recentExits.Where(e => now - e.At <= Window).ToList();
        if (live.Count == 0 || orphans.Count == 0) return [];

        var results = new List<(int, Guid)>();

        foreach (var orphan in orphans)
        {
            var candidates = live
                .Where(e => orphan.StartedAt >= e.At - TimeSpan.FromSeconds(2)) // small clock grace
                .ToList();

            // Zero candidates: nothing exited that this could be replacing.
            // More than one: genuinely ambiguous — decline rather than guess.
            if (candidates.Count != 1) continue;

            // And the reverse direction has to be unambiguous too. If two orphans both point at the
            // same exit, neither is safely attributable — one of them is somebody else's client.
            var contested = orphans.Count(o =>
                o.Pid != orphan.Pid
                && o.StartedAt >= candidates[0].At - TimeSpan.FromSeconds(2)
                && now - candidates[0].At <= Window);
            if (contested > 0) continue;

            results.Add((orphan.Pid, candidates[0].AccountId));
        }

        return results;
    }
}
