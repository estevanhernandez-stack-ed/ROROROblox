using ROROROblox.Core;

namespace ROROROblox.App.ViewModels;

/// <summary>What presence says about a launch that asked for one specific server.</summary>
internal enum ServerLandingOutcome
{
    /// <summary>No verdict yet — keep waiting.</summary>
    Pending,

    /// <summary>Presence confirms the account is in the server we asked for.</summary>
    Landed,

    /// <summary>In game, but a different server. Roblox matchmade it elsewhere.</summary>
    LandedElsewhere,

    /// <summary>Never reached in-game inside the window. Silence is not confirmation.</summary>
    NeverLanded,

    /// <summary>In game, but presence withheld the job id — we cannot tell. Do not claim a miss.</summary>
    Unverifiable,
}

/// <summary>
/// Pure verification logic for a <see cref="LaunchTarget.GameJob"/> launch (v1.14). A
/// <c>LaunchResult.Started</c> only proves a process started; whether it landed in the requested
/// server is a question only presence can answer, and only after the fact.
/// <para>
/// The trap: immediately after a recycle, the row still carries the presence reading from before
/// the client was stopped — which names exactly the server we just asked for. Comparing job ids
/// without checking WHEN the reading arrived reports success instantly and unconditionally. Every
/// verdict below is gated on <c>observedAtUtc &gt; launchedAtUtc</c> for that reason.
/// </para>
/// The ViewModel owns the polling loop and the (banner-only) surfacing — same split as
/// <see cref="AnchorGate"/> and <c>PreWarmGate</c>.
/// </summary>
internal static class ServerLandingGate
{
    /// <summary>
    /// Upper bound on the wait. Matches <see cref="AnchorGate.MaxWait"/>: it is the same physical
    /// event (a client going from launched to fully in-game), measured the same way.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How often the verification loop nudges presence for one account. Slow enough to stay well
    /// clear of Roblox's rate limiter across a full squad (≤9 extra calls per account per launch),
    /// fast enough to beat the 25 s background poll.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <inheritdoc cref="AnchorGate.WaitExpired"/>
    public static bool WaitExpired(DateTime utcNow, DateTime deadline) => utcNow >= deadline;

    /// <summary>
    /// Compare what we asked for against what presence reports.
    /// </summary>
    /// <param name="requested">The server the launch targeted.</param>
    /// <param name="observed">The account's current server per presence, if known.</param>
    /// <param name="inGame">Whether presence reports the account in a game at all.</param>
    /// <param name="observedAtUtc">When that presence reading was taken; null before any reading.</param>
    /// <param name="launchedAtUtc">When the launch fired. Readings at or before this are stale.</param>
    /// <param name="deadlineExpired">Whether <see cref="MaxWait"/> has elapsed.</param>
    public static ServerLandingOutcome Evaluate(
        ServerInstance requested,
        ServerInstance? observed,
        bool inGame,
        DateTimeOffset? observedAtUtc,
        DateTimeOffset launchedAtUtc,
        bool deadlineExpired)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var fresh = observedAtUtc is { } at && at > launchedAtUtc;

        if (fresh && inGame)
        {
            if (observed is null)
            {
                return ServerLandingOutcome.Unverifiable;
            }

            return string.Equals(observed.JobId, requested.JobId, StringComparison.OrdinalIgnoreCase)
                ? ServerLandingOutcome.Landed
                : ServerLandingOutcome.LandedElsewhere;
        }

        // Not in game yet (or nothing fresh to read): still loading, until it isn't.
        return deadlineExpired ? ServerLandingOutcome.NeverLanded : ServerLandingOutcome.Pending;
    }
}

/// <summary>
/// The words a missed landing gets. Banner only — no row affordance, no automatic retry (decision,
/// 2026-08-02): every retry is another client restart, and against a genuinely full server it is a
/// restart that cannot succeed. Tell the user plainly and let them choose.
/// <para>
/// <b>The two misses take opposite advice, and this is field-verified, not reasoned.</b> Squad
/// launch into a one-spot server, 2026-08-02: Roblox did not reject the seven that didn't fit — it
/// queued them ("server full, waiting in line 1 of 7") and let them in as spots opened. So
/// <see cref="ServerLandingOutcome.NeverLanded"/> usually means STANDING IN LINE, and recycling
/// forfeits that place. <see cref="ServerLandingOutcome.LandedElsewhere"/> means in a game and in
/// the wrong one, where a restart costs nothing. Recycle is the remedy for exactly one of these.
/// </para>
/// </summary>
internal static class ServerLandingReport
{
    /// <summary>Names shown before the rest collapse into a count. A banner is one line.</summary>
    private const int MaxNamesShown = 4;

    /// <summary>Recycle missed for one account.</summary>
    public static string ComposeRecycleMiss(string accountName, ServerLandingOutcome outcome) =>
        outcome == ServerLandingOutcome.NeverLanded
            ? $"{accountName} isn't in that server yet — check its Roblox window. A full server puts you in line; "
              + "waiting or picking another server beats recycling, which gives up the spot."
            : $"{accountName} came back in a different server — Roblox moved it. Recycle again to retry.";

    /// <summary>
    /// Squad Launch missed for some accounts. "We're all together" is the whole point of the
    /// feature, so a partial miss is the headline. Null when everyone made it — success is silent.
    /// The two groups get separate sentences because they get opposite advice.
    /// </summary>
    public static string? ComposeSquadMiss(
        IReadOnlyList<string> landedElsewhere, IReadOnlyList<string> notInYet, int totalVerified)
    {
        var parts = new List<string>();

        if (notInYet.Count > 0)
        {
            parts.Add($"{notInYet.Count} of {totalVerified} aren't in that server yet: {Names(notInYet)}. "
                + "Check their Roblox windows — a full server puts you in line, and recycling gives up the spot.");
        }

        if (landedElsewhere.Count > 0)
        {
            parts.Add($"{landedElsewhere.Count} of {totalVerified} landed in a different server: "
                + $"{Names(landedElsewhere)}. Recycle those rows to retry.");
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>
    /// First few names, then a count of the rest. The counts in the sentences around this stay
    /// exact — a trimmed list must never make a partial miss read as a smaller one.
    /// </summary>
    private static string Names(IReadOnlyList<string> names) =>
        names.Count <= MaxNamesShown
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(MaxNamesShown)) + $" +{names.Count - MaxNamesShown} more";
}
