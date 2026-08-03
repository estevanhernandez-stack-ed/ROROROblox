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
/// </summary>
internal static class ServerLandingReport
{
    /// <summary>Recycle missed for one account. Recycle itself is the retry, so name it.</summary>
    public static string ComposeRecycleMiss(string accountName, ServerLandingOutcome outcome) =>
        outcome == ServerLandingOutcome.NeverLanded
            ? $"{accountName} didn't get back into the game. Recycle again to retry."
            : $"{accountName} came back in a different server — Roblox moved it. Recycle again to retry.";

    /// <summary>
    /// Squad Launch missed for some accounts. "We are all together" is the whole point of the
    /// feature, so a partial miss is the headline. Null when everyone made it — success is silent.
    /// </summary>
    public static string? ComposeSquadMiss(IReadOnlyList<string> missedNames, int totalVerified)
    {
        if (missedNames.Count == 0)
        {
            return null;
        }

        return $"{missedNames.Count} of {totalVerified} didn't make the squad's server: "
            + $"{string.Join(", ", missedNames)}. Recycle those rows to retry.";
    }
}
