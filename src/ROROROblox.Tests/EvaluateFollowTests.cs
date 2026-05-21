using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Pure unit coverage for <see cref="MainViewModel.EvaluateFollow"/> — the land-at-home guard
/// for the follow paths (v1.6.0 item 8). A follow may only launch when the target is in a
/// joinable place (InGame AND a place id is visible). Every other presence shape — privacy-hidden
/// InGame (null place), online-but-not-in-game, in Studio, offline, or no presence at all — is
/// blocked with a plain user-facing message instead of a silent bounce to the Roblox home page.
///
/// Extracted from both follow paths (Friends modal + follow-an-alt) so the decision is the single
/// source of truth they share and can't drift apart.
/// </summary>
public class EvaluateFollowTests
{
    private const long FriendUserId = 12345;
    private const string Name = "Buildbot";

    private static UserPresence Presence(
        UserPresenceType type, long? placeId = null, string? jobId = null) =>
        new(FriendUserId, type, placeId, jobId, LastLocation: null);

    // === Launch: InGame with a visible joinable place ===

    [Fact]
    public void InGame_WithPlaceId_AllowsFollow()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.InGame, placeId: 920587237), Name);

        Assert.True(decision.CanFollow);
        Assert.Null(decision.BlockedMessage);
    }

    [Fact]
    public void InGame_WithPlaceIdAndJobId_AllowsFollow()
    {
        var decision = MainViewModel.EvaluateFollow(
            Presence(UserPresenceType.InGame, placeId: 920587237, jobId: "abc-123"), Name);

        Assert.True(decision.CanFollow);
        Assert.Null(decision.BlockedMessage);
    }

    // === Block: InGame but privacy-hides the place (null PlaceId) ===

    [Fact]
    public void InGame_WithNullPlaceId_BlocksWithMessage()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.InGame, placeId: null), Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
        Assert.Contains(Name, decision.BlockedMessage);
    }

    [Fact]
    public void InGame_WithZeroPlaceId_BlocksWithMessage()
    {
        // A 0 place id is "not a real place" — treat the same as null (privacy-hidden).
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.InGame, placeId: 0), Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
    }

    // === Block: online but not in a game ===

    [Fact]
    public void OnlineWebsite_BlocksWithMessage()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.OnlineWebsite), Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
        Assert.Contains(Name, decision.BlockedMessage);
    }

    [Fact]
    public void InStudio_BlocksWithMessage()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.InStudio), Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
    }

    // === Block: offline / invisible ===

    [Fact]
    public void Offline_BlocksWithMessage()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.Offline), Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
    }

    [Fact]
    public void Invisible_BlocksWithMessage()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.Invisible), Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
    }

    // === Block: no presence at all (privacy fully hidden / fetch returned nothing) ===

    [Fact]
    public void NullPresence_BlocksWithMessage()
    {
        var decision = MainViewModel.EvaluateFollow(null, Name);

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
        Assert.Contains(Name, decision.BlockedMessage);
    }

    // === Message quality: blocked messages name the friend and stay plain ===

    [Fact]
    public void BlockedMessage_NamesTheFriend_AndMentionsJoinable()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.OnlineWebsite), "K0ii");

        Assert.Contains("K0ii", decision.BlockedMessage);
        Assert.Contains("joinable", decision.BlockedMessage, StringComparison.OrdinalIgnoreCase);
    }

    // === Defensive: blank name still produces a usable message ===

    [Fact]
    public void BlankName_StillProducesMessage()
    {
        var decision = MainViewModel.EvaluateFollow(Presence(UserPresenceType.Offline), "");

        Assert.False(decision.CanFollow);
        Assert.False(string.IsNullOrWhiteSpace(decision.BlockedMessage));
    }
}
