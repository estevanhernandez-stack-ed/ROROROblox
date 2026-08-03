using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class PresencePayloadBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 21, 14, 0, TimeSpan.Zero);
    private static readonly ServerInstance ServerA = new(140403681187145, "job-a");
    private static readonly ServerInstance ServerB = new(140403681187145, "job-b");

    private static RosterAccount InGame(string name, ServerInstance? server, DateTimeOffset? since = null) =>
        new(Guid.NewGuid(), name, InGame: true, GameName: "Pet Simulator 99!", Server: server,
            InGameSinceUtc: since ?? T0);

    [Fact]
    public void Build_NothingRunning_ReturnsNull_SoPresenceIsCleared()
    {
        Assert.Null(PresencePayloadBuilder.Build(new RosterSnapshot([])));
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
        Assert.Equal(ServerA, fields.JoinableServer);
    }

    [Fact]
    public void Build_SplitAcrossServers_ReportsHowManyShareTheLargestServer()
    {
        var snapshot = new RosterSnapshot([
            InGame("CaptainNoodle", ServerA), InGame("LadyPixel", ServerA), InGame("DoctorDuck", ServerB)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        Assert.Equal("3 accounts · 2 in this server", fields!.State);
        Assert.Equal(ServerA, fields.JoinableServer);   // the biggest cluster is the joinable one
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
            new(Guid.NewGuid(), "RosterFirst", InGame: true, GameName: "Adopt Me!", Server: ServerB,
                InGameSinceUtc: T0),
            InGame("CaptainNoodle", ServerA),
            InGame("LadyPixel", ServerA)]);

        var fields = PresencePayloadBuilder.Build(snapshot);

        // Details should come from the cluster the Join button points at, not the roster-first account.
        Assert.Equal("Pet Simulator 99!", fields!.Details);
        Assert.Equal(ServerA, fields.JoinableServer);
    }
}
