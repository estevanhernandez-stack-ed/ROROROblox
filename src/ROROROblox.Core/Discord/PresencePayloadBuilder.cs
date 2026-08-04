namespace ROROROblox.Core.Discord;

/// <summary>
/// Roster snapshot → Discord presence fields. Pure: no clock, no IPC, no I/O, so "what does
/// presence say when three of eight are in one server and five are elsewhere?" is a unit test
/// rather than something to check by eye in Discord.
/// </summary>
public static class PresencePayloadBuilder
{
    public static PresenceFields Build(RosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var live = snapshot.Accounts.Where(a => a.InGame).ToList();
        if (live.Count == 0)
        {
            return BuildIdle(snapshot);
        }

        // The biggest cluster of accounts sharing one server is what a friend would want to join.
        var biggestCluster = live
            .Where(a => a.Server is not null)
            .GroupBy(a => a.Server!.Server.JobId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        var togetherCount = biggestCluster?.Count() ?? 0;

        // Streamer mode hides the roster SIZE, not the roster's LOCATION — "3 of 8" (or "8
        // accounts in one server") tells a viewer exactly how big the fleet is; "in a server" /
        // "in a game" says where without a single digit. Every non-streamer branch below collapses
        // into those two: something live is known to share a server (a joinable cluster exists),
        // or nothing live has a known server yet.
        var state = snapshot.IsStreamerModeActive
            ? (biggestCluster is not null ? "In a server" : "In a game")
            : live.Count == 1
                ? "1 account"
                : togetherCount == live.Count
                    ? $"{live.Count} accounts in one server"
                    : togetherCount > 1
                        ? $"{live.Count} accounts · {togetherCount} in this server"
                        : $"{live.Count} accounts";

        // Details should reflect the game the Join button points at (biggestCluster), not the roster-first account.
        var details = biggestCluster?.Select(a => a.GameName)
                          .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                      ?? live.Select(a => a.GameName)
                          .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        // The cluster's private/public nature is decided from the SAME accounts the cluster is
        // built from, not an unrelated row: every member shares the same (place, job) pair by
        // construction (grouped by JobId), but only the member(s) whose LastLaunchTarget was
        // actually a PrivateServer carry the code — others in the same physical server may have
        // arrived via Follow, a plain GameJob, or a since-cleared LastLaunchTarget (MainViewModel
        // clears it once an account fully leaves a game — see RosterServer's remarks). Preferring a
        // member that carries the code (when one exists) keeps a private session correctly
        // identified even when .First() would otherwise land on a member that never recorded one.
        var joinableRepresentative = biggestCluster?.FirstOrDefault(a => a.Server?.PrivateServerCode is not null)
                                      ?? biggestCluster?.First();

        // The party MAXIMUM is the honest ceiling: the user's total saved-account count (every
        // account the roster snapshot knows about, live or not) — not an arbitrary constant. "3 of
        // 8" reads as three of my eight accounts; that reading only holds if 8 is actually true.
        //
        // FULL-ROSTER EDGE — UNVERIFIED, 2026-08-03: when every saved account is in the same
        // server, size == max. The prior code's comment asserted Discord will not render a Join
        // button on a party it considers "full" at size == max. That assertion is REASONING, not
        // MEASUREMENT — nobody has watched it happen. Este is verifying it live. Do NOT pre-emptively
        // fudge this (e.g. max = size + 1) to dodge an unconfirmed edge case. If the live test shows
        // Join actually disappearing at size == max, the fix is one line here: change
        // `savedAccountCount` below to `savedAccountCount + 1` for the max — and log a decision
        // entry recording that Discord's full-party behavior was confirmed, since every other
        // caller of this builder will want to know.
        var savedAccountCount = snapshot.Accounts.Count;

        int partyCount;
        int partyMax;
        if (snapshot.IsStreamerModeActive)
        {
            // Discord requires SOME party size to render a Join button at all — these two numbers
            // are a Discord UI requirement, not a fact about the user. 1 of 2 says nothing about
            // the real fleet (not the count, not how many share a server) while still satisfying
            // Discord's rendering rule. The Join secret itself is unaffected — streamer mode hides
            // the count, never the function.
            partyCount = 1;
            partyMax = 2;
        }
        else
        {
            partyCount = togetherCount;
            partyMax = savedAccountCount;
        }

        return new PresenceFields(
            Details: details,
            State: state,
            StartedAtUtc: live.Where(a => a.InGameSinceUtc is not null).Min(a => a.InGameSinceUtc),
            JoinableServer: joinableRepresentative?.Server,
            JoinableServerAccountCount: partyCount,
            JoinableServerAccountMax: partyMax);
    }

    /// <summary>
    /// Nothing running is not "nothing to say." The RPC connection stays open regardless (see
    /// <see cref="PresenceFields"/>'s remarks), so the choice is between a blank-looking entry and
    /// a deliberate one. The only thing the roster actually knows in this state is how many saved
    /// accounts are standing by — that is real information, so it is what the idle entry says.
    /// Never a game name, never elapsed time, never a Join target: none of those are true right now.
    /// </summary>
    private static PresenceFields BuildIdle(RosterSnapshot snapshot)
    {
        var saved = snapshot.Accounts.Count;

        // Streamer mode: "No accounts yet" carries no digit (zero saved accounts isn't a fleet
        // size to hide), but any positive count is — "8 accounts standing by" is exactly as much
        // of a headcount leak idle as "8 accounts in one server" is live. The active variant says
        // the same true thing without the number.
        var details = snapshot.IsStreamerModeActive
            ? (saved == 0 ? "No accounts yet" : "Accounts standing by")
            : saved switch
            {
                0 => "No accounts yet",
                1 => "1 account standing by",
                _ => $"{saved} accounts standing by",
            };

        // Discord already renders the application name as the card's header, so naming the product
        // again on the second line spends the only other visible string saying "RoRoRo" twice. The
        // tagline is the thing that isn't already on screen.
        return new PresenceFields(
            Details: details,
            State: "Imagine Something Else.",
            StartedAtUtc: null,
            JoinableServer: null,
            JoinableServerAccountCount: 0,
            JoinableServerAccountMax: 0,
            IsIdle: true);
    }
}
