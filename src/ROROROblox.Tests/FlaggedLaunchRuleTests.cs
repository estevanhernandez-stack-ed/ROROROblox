using ROROROblox.Core;

namespace ROROROblox.Tests;

public class FlaggedLaunchRuleTests
{
    private const long MainId = 111;
    private static UserPresence InGame(long place, string? job = "job-1") =>
        new(MainId, UserPresenceType.InGame, place, job, null);

    [Fact]
    public void Unflagged_IsDirect()
    {
        var d = FlaggedLaunchRule.Decide(false, false, new LaunchTarget.Place(5), MainId, InGame(5));
        Assert.Equal(FlaggedLaunchOutcome.Direct, d.Outcome);
        Assert.IsType<LaunchTarget.Place>(d.Target);
    }

    [Fact]
    public void FlaggedMain_IsDirect_NeverFollowsItself()
    {
        var d = FlaggedLaunchRule.Decide(true, true, new LaunchTarget.DefaultGame(), MainId, InGame(5));
        Assert.Equal(FlaggedLaunchOutcome.Direct, d.Outcome);
    }

    [Fact]
    public void FlaggedWithExplicitFollow_IsDirect()
    {
        var follow = new LaunchTarget.FollowFriend(999);
        var d = FlaggedLaunchRule.Decide(true, false, follow, MainId, mainPresence: null);
        Assert.Equal(FlaggedLaunchOutcome.Direct, d.Outcome);
        Assert.Same(follow, d.Target);
    }

    [Theory]
    [InlineData(UserPresenceType.Offline)]
    [InlineData(UserPresenceType.OnlineWebsite)]
    [InlineData(UserPresenceType.InStudio)]
    public void FlaggedWithMainNotInGame_IsMainNotJoinable(UserPresenceType type)
    {
        var p = new UserPresence(MainId, type, null, null, null);
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), MainId, p);
        Assert.Equal(FlaggedLaunchOutcome.MainNotJoinable, d.Outcome);
    }

    [Fact]
    public void FlaggedWithMainInGameButPlaceHidden_IsMainNotJoinable()
    {
        var p = new UserPresence(MainId, UserPresenceType.InGame, null, null, null);
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), MainId, p);
        Assert.Equal(FlaggedLaunchOutcome.MainNotJoinable, d.Outcome);
    }

    [Fact]
    public void FlaggedWithNoMain_IsMainNotJoinable()
    {
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), null, null);
        Assert.Equal(FlaggedLaunchOutcome.MainNotJoinable, d.Outcome);
    }

    [Fact]
    public void FlaggedDefaultGame_FollowsTheMain()
    {
        var d = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.DefaultGame(), MainId, InGame(5));
        Assert.Equal(FlaggedLaunchOutcome.Follow, d.Outcome);
        Assert.Equal(new LaunchTarget.FollowFriend(MainId), d.Target);
    }

    [Fact]
    public void FlaggedGameJob_FollowsOnlyWhenTheMainIsInThatServer()
    {
        var same = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.GameJob(5, "job-1"), MainId, InGame(5, "job-1"));
        var other = FlaggedLaunchRule.Decide(true, false, new LaunchTarget.GameJob(5, "job-2"), MainId, InGame(5, "job-1"));
        Assert.Equal(FlaggedLaunchOutcome.Follow, same.Outcome);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer, other.Outcome);
    }

    [Fact]
    public void FlaggedPrivateServer_FollowsWhenTheMainWasLaunchedIntoThatSameServer()
    {
        var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        var mainLast = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        var d = FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(5), mainLast);
        Assert.Equal(FlaggedLaunchOutcome.Follow, d.Outcome);
        Assert.Equal(new LaunchTarget.FollowFriend(MainId), d.Target);
    }

    [Fact]
    public void FlaggedPrivateServer_SamePlaceDifferentCode_IsMainNotInThatServer()
    {
        // I5: the main is in the same place but a different (or public) server. Following it would
        // put the alt somewhere other than the private server it was pointed at.
        var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        var mainLast = new LaunchTarget.PrivateServer(5, "other", PrivateServerCodeKind.LinkCode);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer,
            FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(5), mainLast).Outcome);
    }

    [Fact]
    public void FlaggedPrivateServer_MainLastTargetNull_IsMainNotInThatServer()
    {
        var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer,
            FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(5), mainLastTarget: null).Outcome);
    }

    [Fact]
    public void FlaggedPrivateServer_MainInAPublicServerOfThatPlace_IsMainNotInThatServer()
    {
        var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer,
            FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(5), new LaunchTarget.Place(5)).Outcome);
    }

    [Fact]
    public void FlaggedPrivateServer_MainInADifferentPlace_IsMainNotInThatServer()
    {
        var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        var mainLast = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);
        Assert.Equal(FlaggedLaunchOutcome.MainNotInThatServer,
            FlaggedLaunchRule.Decide(true, false, ps, MainId, InGame(6), mainLast).Outcome);
    }
}
