using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// "Launch started" means a process started, not that it landed where we asked. Presence is the
/// only thing that knows. <see cref="ServerLandingGate"/> is the pure decision — the ViewModel owns
/// the await loop, same split as <see cref="AnchorGate"/> / <c>PreWarmGate</c>.
/// <para>
/// The trap this type exists to avoid: right after a recycle the row still holds the presence
/// reading from BEFORE the client was stopped — which names the very server we just asked for. A
/// naive comparison reports success instantly, every time, whether or not the feature works. Every
/// verdict here is gated on a reading that arrived after the launch.
/// </para>
/// </summary>
public class ServerLandingGateTests
{
    private static readonly ServerInstance Requested = new(140403681187145, "job-requested");
    private static readonly DateTimeOffset LaunchedAt = new(2026, 8, 2, 10, 30, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset Before = LaunchedAt.AddSeconds(-16);
    private static readonly DateTimeOffset After = LaunchedAt.AddSeconds(34);

    [Fact]
    public void Evaluate_ReadingOlderThanTheLaunch_IsPending_EvenWhenItNamesTheRequestedServer()
    {
        // The false-success case. Drop the freshness check and this test goes green while the
        // feature is broken — which is the one thing a verification test must never do.
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: Requested, inGame: true,
            observedAtUtc: Before, launchedAtUtc: LaunchedAt, deadlineExpired: false);

        Assert.Equal(ServerLandingOutcome.Pending, outcome);
    }

    [Fact]
    public void Evaluate_FreshReadingInTheRequestedServer_IsLanded()
    {
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: new ServerInstance(Requested.PlaceId, "job-requested"), inGame: true,
            observedAtUtc: After, launchedAtUtc: LaunchedAt, deadlineExpired: false);

        Assert.Equal(ServerLandingOutcome.Landed, outcome);
    }

    [Fact]
    public void Evaluate_FreshReadingInADifferentServer_IsLandedElsewhere()
    {
        // Roblox matchmade us away — full server, or a shape we do not understand. Either way the
        // user is not with their squad and deserves to hear it.
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: new ServerInstance(Requested.PlaceId, "job-somewhere-else"), inGame: true,
            observedAtUtc: After, launchedAtUtc: LaunchedAt, deadlineExpired: false);

        Assert.Equal(ServerLandingOutcome.LandedElsewhere, outcome);
    }

    [Fact]
    public void Evaluate_FreshReadingNotYetInGame_IsPendingUntilTheDeadline()
    {
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: null, inGame: false,
            observedAtUtc: After, launchedAtUtc: LaunchedAt, deadlineExpired: false);

        Assert.Equal(ServerLandingOutcome.Pending, outcome);
    }

    [Fact]
    public void Evaluate_NeverReachesInGameBeforeTheDeadline_IsNeverLanded()
    {
        // Spec: "Presence verification times out -> treat as a miss, not a success. Silence is not
        // confirmation."
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: null, inGame: false,
            observedAtUtc: After, launchedAtUtc: LaunchedAt, deadlineExpired: true);

        Assert.Equal(ServerLandingOutcome.NeverLanded, outcome);
    }

    [Fact]
    public void Evaluate_NoPresenceReadingAtAllBeforeTheDeadline_IsNeverLanded()
    {
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: null, inGame: false,
            observedAtUtc: null, launchedAtUtc: LaunchedAt, deadlineExpired: true);

        Assert.Equal(ServerLandingOutcome.NeverLanded, outcome);
    }

    [Fact]
    public void Evaluate_InGameButPresenceWithholdsTheJobId_IsUnverifiable()
    {
        // Privacy can hide the job id. We know it is playing and we do not know where — claiming a
        // miss would be a lie in the loud direction. Say nothing, log it.
        var outcome = ServerLandingGate.Evaluate(
            Requested, observed: null, inGame: true,
            observedAtUtc: After, launchedAtUtc: LaunchedAt, deadlineExpired: false);

        Assert.Equal(ServerLandingOutcome.Unverifiable, outcome);
    }

    // === The banner copy — the only surface a miss gets (decision, 2026-08-02) ===

    [Fact]
    public void ComposeRecycleMiss_NamesTheAccountAndTheRetry()
    {
        var banner = ServerLandingReport.ComposeRecycleMiss("Alt3", ServerLandingOutcome.LandedElsewhere);

        Assert.Contains("Alt3", banner);
        Assert.Contains("different server", banner);
        Assert.Contains("Recycle", banner);
    }

    [Fact]
    public void ComposeRecycleMiss_NeverLanded_NeverTellsThemToRecycleAgain()
    {
        // Field-verified 2026-08-02: a full server does NOT reject — Roblox queues you ("server
        // full, waiting in line 1 of 7") and lets you in as spots open. So "not in the game yet"
        // routinely means "standing in line," and recycling forfeits that place. Telling the user
        // to recycle here is advice that actively costs them the thing they are waiting for.
        var banner = ServerLandingReport.ComposeRecycleMiss("Alt3", ServerLandingOutcome.NeverLanded);

        Assert.Contains("Alt3", banner);
        Assert.DoesNotContain("different server", banner);
        // Not a word about recycling — the copy says what to check, never what to avoid. Naming
        // the wrong move is how a user ends up trying it.
        Assert.DoesNotContain("recycl", banner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line", banner);   // names the queue, so "still loading" isn't the only read
    }

    [Fact]
    public void ComposeRecycleMiss_LandedElsewhere_StillOffersRecycle()
    {
        // In a game, wrong server: nothing is queued, nothing is lost by restarting. Recycle IS
        // the retry here, so keep pointing at it.
        var banner = ServerLandingReport.ComposeRecycleMiss("Alt3", ServerLandingOutcome.LandedElsewhere);

        Assert.Contains("different server", banner);
        Assert.Contains("Recycle", banner);
    }

    [Fact]
    public void ComposeSquadMiss_AccountsStillQueuing_SendsThemToTheRobloxWindowNotToRecycle()
    {
        // The 2026-08-02 run: one spot free, eight accounts, seven left in Roblox's queue. The
        // banner was right about the count and wrong about the remedy.
        var banner = ServerLandingReport.ComposeSquadMiss(
            landedElsewhere: [], notInYet: ["Alt2", "Alt3"], totalVerified: 8);

        Assert.Contains("2 of 8", banner);
        Assert.Contains("Alt2", banner);
        Assert.Contains("line", banner);
        Assert.DoesNotContain("recycl", banner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeSquadMiss_AccountsInADifferentServer_OffersRecycle()
    {
        var banner = ServerLandingReport.ComposeSquadMiss(
            landedElsewhere: ["Alt3"], notInYet: [], totalVerified: 6);

        Assert.Contains("1 of 6", banner);
        Assert.Contains("Alt3", banner);
        Assert.Contains("Recycle", banner);
    }

    [Fact]
    public void ComposeSquadMiss_MixedOutcomes_KeepsTheTwoRemediesApart()
    {
        // Recycle helps one group and hurts the other — they cannot share a sentence.
        var banner = ServerLandingReport.ComposeSquadMiss(
            landedElsewhere: ["Alt3"], notInYet: ["Alt7"], totalVerified: 8);

        Assert.Contains("Alt3", banner);
        Assert.Contains("Alt7", banner);
        Assert.Contains("line", banner);
        Assert.Contains("Recycle", banner);
    }

    [Fact]
    public void ComposeSquadMiss_LongRosters_ShowFourNamesAndCountTheRest()
    {
        // A banner is one line. The COUNT stays exact — truncating the names must never make a
        // partial miss read as a smaller one.
        var banner = ServerLandingReport.ComposeSquadMiss(
            landedElsewhere: [], notInYet: ["A", "B", "C", "D", "E", "F", "G"], totalVerified: 8);

        Assert.Contains("7 of 8", banner);
        Assert.Contains("+3 more", banner);
        Assert.DoesNotContain("G", banner);
    }

    [Fact]
    public void ComposeSquadMiss_EveryoneLanded_SaysNothing()
    {
        Assert.Null(ServerLandingReport.ComposeSquadMiss([], [], totalVerified: 6));
    }
}
