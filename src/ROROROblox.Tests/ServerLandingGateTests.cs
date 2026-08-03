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
    public void ComposeRecycleMiss_NeverLanded_SaysSoRatherThanClaimingTheWrongServer()
    {
        var banner = ServerLandingReport.ComposeRecycleMiss("Alt3", ServerLandingOutcome.NeverLanded);

        Assert.Contains("Alt3", banner);
        Assert.DoesNotContain("different server", banner);
    }

    [Fact]
    public void ComposeSquadMiss_ReportsWhoDidNotMakeIt()
    {
        // "We are all together" is the entire point of Squad Launch, so a partial miss is the
        // headline, not a footnote.
        var banner = ServerLandingReport.ComposeSquadMiss(["Alt3", "Alt5"], totalVerified: 6);

        Assert.Contains("2 of 6", banner);
        Assert.Contains("Alt3", banner);
        Assert.Contains("Alt5", banner);
    }

    [Fact]
    public void ComposeSquadMiss_EveryoneLanded_SaysNothing()
    {
        Assert.Null(ServerLandingReport.ComposeSquadMiss([], totalVerified: 6));
    }
}
