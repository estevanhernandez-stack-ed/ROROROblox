using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The matched-pair invariant, encoded. <c>placeId</c> and <c>jobId</c> address ONE server
/// together; pairing a launch target's place with presence's job id produced "This experience has
/// ended, or the server became unavailable unexpectedly due to a system error" during the
/// 2026-08-02 spike, because games teleport between places inside a universe (Pet Sim's entry
/// place is 8737899170; presence reported the account was actually in 140403681187145).
/// <para>
/// <see cref="ServerInstance"/> exists so both halves travel as one value and the upgrade decision
/// has no way to mix sources. These tests are the reason that type is not just two parameters.
/// </para>
/// </summary>
public class ServerInstanceTargetingTests
{
    private static readonly ServerInstance Presence =
        new(140403681187145, "fcbe3a36-d655-41da-ba8a-8280f5709568");

    private const long EntryPlaceId = 8737899170;

    // === ServerInstance — a pair is whole or it does not exist ===

    [Fact]
    public void TryFrom_BothHalvesPresent_ReturnsThePair()
    {
        var server = ServerInstance.TryFrom(140403681187145, "job-1");

        Assert.NotNull(server);
        Assert.Equal(140403681187145, server.PlaceId);
        Assert.Equal("job-1", server.JobId);
    }

    [Fact]
    public void TryFrom_JobIdWithoutPlaceId_ReturnsNull()
    {
        // Spec failure table: "jobId known but no placeId -> never construct a half-pair."
        Assert.Null(ServerInstance.TryFrom(null, "job-1"));
    }

    [Fact]
    public void TryFrom_PlaceIdWithoutJobId_ReturnsNull()
    {
        // The common case: presence has the account in-game but privacy or timing withheld the
        // job id. A place alone is exactly what LaunchTarget.Place already means.
        Assert.Null(ServerInstance.TryFrom(140403681187145, null));
        Assert.Null(ServerInstance.TryFrom(140403681187145, "   "));
    }

    [Fact]
    public void TryFrom_NonPositivePlaceId_ReturnsNull()
    {
        Assert.Null(ServerInstance.TryFrom(0, "job-1"));
        Assert.Null(ServerInstance.TryFrom(-5, "job-1"));
    }

    // === Upgrade — which targets become server-specific, and which never do ===

    [Fact]
    public void Upgrade_PlaceWithAPresencePair_TakesBOTHHalvesFromPresence()
    {
        // THE test. The upgraded target must carry presence's place (140403681187145), never the
        // place we originally launched into (8737899170). Reverting the upgrade to reuse the
        // launch target's PlaceId makes this fail — and that shape is a live Roblox error, not a
        // silent mismatch.
        var upgraded = ServerInstanceTargeting.Upgrade(new LaunchTarget.Place(EntryPlaceId), Presence);

        var job = Assert.IsType<LaunchTarget.GameJob>(upgraded);
        Assert.Equal(Presence.PlaceId, job.PlaceId);
        Assert.NotEqual(EntryPlaceId, job.PlaceId);
        Assert.Equal(Presence.JobId, job.JobId);
    }

    [Fact]
    public void Upgrade_PlaceWithNoPresencePair_StaysAPlace()
    {
        // Offline, privacy-hidden, or pre-first-poll: launch exactly as today. Never strand.
        var upgraded = ServerInstanceTargeting.Upgrade(new LaunchTarget.Place(EntryPlaceId), server: null);

        var place = Assert.IsType<LaunchTarget.Place>(upgraded);
        Assert.Equal(EntryPlaceId, place.PlaceId);
    }

    [Fact]
    public void Upgrade_PrivateServer_IsNeverUpgraded()
    {
        // A private server's code already identifies exactly one server. Upgrading it would swap a
        // durable address for a perishable one — and drop the access credential entirely.
        var target = new LaunchTarget.PrivateServer(EntryPlaceId, "CODE", PrivateServerCodeKind.LinkCode);

        var upgraded = ServerInstanceTargeting.Upgrade(target, Presence);

        Assert.Same(target, upgraded);
    }

    [Fact]
    public void Upgrade_FollowFriend_IsNeverUpgraded()
    {
        // FollowFriend resolves server-side to wherever the friend IS — strictly fresher than any
        // job id we hold.
        var target = new LaunchTarget.FollowFriend(98765);

        Assert.Same(target, ServerInstanceTargeting.Upgrade(target, Presence));
    }

    [Fact]
    public void Upgrade_HomeAndDefaultGame_AreNeverUpgraded()
    {
        var home = new LaunchTarget.Home();
        var def = new LaunchTarget.DefaultGame();

        Assert.Same(home, ServerInstanceTargeting.Upgrade(home, Presence));
        Assert.Same(def, ServerInstanceTargeting.Upgrade(def, Presence));
    }

    [Fact]
    public void Upgrade_ExistingGameJob_TakesTheFRESHPresencePair()
    {
        // Second recycle in a session: LastLaunchTarget is already a GameJob, but the account may
        // have been matchmade elsewhere since. Reusing the remembered job id would send it back to
        // a server it is no longer in. Newest presence wins.
        var stale = new LaunchTarget.GameJob(Presence.PlaceId, "stale-job-id");

        var upgraded = ServerInstanceTargeting.Upgrade(stale, Presence);

        var job = Assert.IsType<LaunchTarget.GameJob>(upgraded);
        Assert.Equal(Presence.JobId, job.JobId);
    }

    [Fact]
    public void Upgrade_ExistingGameJob_WithNoPresencePair_DegradesToPlace()
    {
        // No live pair means the remembered job id is unverifiable and possibly dead — and a dead
        // job id is a hard Roblox error that leaves the account at the home screen. "Back in the
        // game, wrong server" is the acceptable floor; stranded is not.
        var stale = new LaunchTarget.GameJob(Presence.PlaceId, "stale-job-id");

        var upgraded = ServerInstanceTargeting.Upgrade(stale, server: null);

        var place = Assert.IsType<LaunchTarget.Place>(upgraded);
        Assert.Equal(Presence.PlaceId, place.PlaceId);
    }
}
