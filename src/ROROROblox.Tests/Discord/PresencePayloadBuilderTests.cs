using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class PresencePayloadBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 21, 14, 0, TimeSpan.Zero);
    private static readonly ServerInstance ServerA = new(140403681187145, "job-a");
    private static readonly ServerInstance ServerB = new(140403681187145, "job-b");

    private static RosterAccount InGame(
        string name, ServerInstance? server, DateTimeOffset? since = null, LaunchTarget? lastLaunchTarget = null) =>
        new(Guid.NewGuid(), name, InGame: true, GameName: "Pet Simulator 99!",
            Server: RosterServer.TryFrom(server, lastLaunchTarget),
            InGameSinceUtc: since ?? T0);

    [Fact]
    public void Build_NothingRunning_ReturnsAnIdlePayload_NotNull()
    {
        // 2026-08-03, live smoke test: the RPC connection stays open whether or not anything is
        // running, so returning null (-> ClearPresence) still rendered a bare "Playing RoRoRo"
        // with no artwork and no text. Nothing running now means an honest idle payload, never a
        // null the caller has to interpret as "clear."
        var fields = PresencePayloadBuilder.Build(new RosterSnapshot([]));

        Assert.NotNull(fields);
        Assert.True(fields.IsIdle);
        Assert.Equal("No accounts yet", fields.Details);
        Assert.Null(fields.JoinableServer);           // not joinable
        Assert.Null(fields.StartedAtUtc);              // no elapsed run
        Assert.Equal(0, fields.JoinableServerAccountCount);
    }

    [Fact]
    public void Build_NothingRunning_SavedAccountsStandingBy_UsesTheRosterCount()
    {
        // The roster still knows the saved accounts even when none are live -- that count is real
        // information, so the idle line says it rather than inventing activity.
        var snapshot = new RosterSnapshot([
            new(Guid.NewGuid(), "CaptainNoodle", InGame: false, null, null, null),
            new(Guid.NewGuid(), "LadyPixel", InGame: false, null, null, null),
            new(Guid.NewGuid(), "DoctorDuck", InGame: false, null, null, null)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.True(fields.IsIdle);
        Assert.Equal("3 accounts standing by", fields.Details);
    }

    [Fact]
    public void Build_NothingRunning_OneSavedAccount_UsesSingularWording()
    {
        var snapshot = new RosterSnapshot([
            new(Guid.NewGuid(), "CaptainNoodle", InGame: false, null, null, null)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal("1 account standing by", fields.Details);
    }

    [Fact]
    public void Build_LiveAccounts_IsIdleIsFalse()
    {
        var fields = PresencePayloadBuilder.Build(new RosterSnapshot([InGame("CaptainNoodle", ServerA)]));

        Assert.False(fields.IsIdle);
    }

    [Fact]
    public void Build_AllAccountsInOneServer_SaysTheFleetIsTogether()
    {
        // The line only RoRoRo can say. Per-account presence would lose it entirely.
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA), InGame("LadyPixel", ServerA), InGame("DoctorDuck", ServerA)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.NotNull(fields);
        Assert.Equal("Pet Simulator 99!", fields.Details);
        Assert.Equal("3 accounts in one server", fields.State);
        Assert.Equal(ServerA, fields.JoinableServer!.Server);
        Assert.Null(fields.JoinableServer.PrivateServerCode);   // public roster still encodes g|
    }

    [Fact]
    public void Build_SplitAcrossServers_ReportsHowManyShareTheLargestServer()
    {
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA), InGame("LadyPixel", ServerA), InGame("DoctorDuck", ServerB)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal("3 accounts · 2 in this server", fields!.State);
        Assert.Equal(ServerA, fields.JoinableServer!.Server);   // the biggest cluster is the joinable one
    }

    [Fact]
    public void Build_SplitAcrossServers_JoinableServerAccountCountIsTheClusterSizeNotTheFleetSize()
    {
        // Two in server A, one in server B: the joinable count is the cluster (2), never the
        // whole live fleet (3) — a Discord party "2 of N" next to "3 accounts · 2 in this server"
        // must describe the server a Join click actually lands in, not the roster total.
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA), InGame("LadyPixel", ServerA), InGame("DoctorDuck", ServerB)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal(2, fields!.JoinableServerAccountCount);
    }

    [Fact]
    public void Build_SingleAccount_UsesSingularWording()
    {
        var fields = PresencePayloadBuilder.Build(new RosterSnapshot([InGame("CaptainNoodle", ServerA)]));

        Assert.Equal("1 account", fields!.State);
    }

    [Fact]
    public void Build_InGameButNoServerKnown_OffersNoJoin()
    {
        // Privacy or pre-first-poll: we know they are playing, not where. A Join button that
        // cannot work is worse than none.
        var fields = PresencePayloadBuilder.Build(new RosterSnapshot([InGame("CaptainNoodle", server: null)]));

        Assert.Equal("1 account", fields!.State);
        Assert.Null(fields.JoinableServer);
    }

    [Fact]
    public void Build_ElapsedTime_ComesFromTheOldestStillRunningAccount()
    {
        // The run's age, not the newest launch — otherwise the timer resets every time an alt
        // is recycled, which is precisely when it is most interesting.
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA, T0),
            InGame("LadyPixel", ServerA, T0.AddMinutes(45))]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal(T0, fields!.StartedAtUtc);
    }

    [Fact]
    public void Build_AccountsOutOfGame_AreNotCounted()
    {
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA),
            new RosterAccount(Guid.NewGuid(), "LadyPixel", InGame: false, null, null, null)]);

        Assert.Equal("1 account", PresencePayloadBuilder.Build(snapshot)!.State);
    }

    [Fact]
    public void Build_AccountsAcrossTwoGames_DetailsNamesTheGameTheJoinButtonPointsAt()
    {
        // Discord display must align: Details says game X, Join button lands in game X, not Y.
        // The roster-first account is in "Adopt Me!" but the biggestCluster is in "Pet Simulator 99!".
        var snapshot = new RosterSnapshot([
            new(Guid.NewGuid(), "RosterFirst", InGame: true, GameName: "Adopt Me!",
                Server: RosterServer.TryFrom(ServerB, null),
                InGameSinceUtc: T0),
            InGame("CaptainNoodle", ServerA),
            InGame("LadyPixel", ServerA)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        // Details should come from the cluster the Join button points at, not the roster-first account.
        Assert.Equal("Pet Simulator 99!", fields!.Details);
        Assert.Equal(ServerA, fields.JoinableServer!.Server);
    }

    [Fact]
    public void Build_MixedPrivateAndPublicRoster_JoinableServerResolvesFromTheWinningCluster()
    {
        // FIX 1 (final whole-branch review, 2026-08-03): two accounts together in a PRIVATE
        // server (the biggest cluster) and one alone in a public one. The winning cluster is
        // still decided by size alone -- but its private/public nature has to be read off the
        // SAME two accounts that make up that cluster, not the unrelated public row, and not by
        // blind .First() (LadyPixel's copy of the pairing never recorded a code; CaptainNoodle's
        // did -- both are the same physical server).
        var privateServer = new ServerInstance(8737899170, "job-private");
        var publicServer = new ServerInstance(140403681187145, "job-public");
        var privateTarget = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", privateServer, lastLaunchTarget: privateTarget),
            InGame("LadyPixel", privateServer),                 // same server, no recorded code
            InGame("DoctorDuck", publicServer)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal("job-private", fields!.JoinableServer!.Server.JobId);
        Assert.Equal("CODE", fields.JoinableServer.PrivateServerCode);
        Assert.Equal(PrivateServerCodeKind.LinkCode, fields.JoinableServer.PrivateServerCodeKind);
        Assert.Equal(2, fields.JoinableServerAccountCount);
    }
}
