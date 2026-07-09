using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the pure <see cref="AnchorGate"/> predicates — trust-aware squad launch (v1.9.0):
/// once the direct batch is dispatched, we wait for an anchor (a landed direct-batch account
/// with a known Roblox userId — FollowFriend needs a target id) before follow-dispatching the
/// flagged batch.
/// </summary>
public class AnchorGateTests
{
    private static AccountSummary NewSummary(bool inGame, long? robloxUserId)
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "Alt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: robloxUserId);
        var summary = new AccountSummary(account);
        if (inGame)
        {
            summary.PresenceState = UserPresenceType.InGame;
        }
        return summary;
    }

    // === CanAnchor: only inGame AND known userId passes ===

    [Theory]
    [InlineData(true, 12345L, true)]
    [InlineData(true, null, false)]
    [InlineData(false, 12345L, false)]
    [InlineData(false, null, false)]
    public void CanAnchor_TruthTable(bool inGame, long? robloxUserId, bool expected)
    {
        Assert.Equal(expected, AnchorGate.CanAnchor(inGame, robloxUserId));
    }

    // === PickAnchor: first capable, skipping landed-but-unknown-userId ===

    [Fact]
    public void PickAnchor_ReturnsFirstCapable()
    {
        var notLanded = NewSummary(inGame: false, robloxUserId: 1L);
        var landedNoId = NewSummary(inGame: true, robloxUserId: null);
        var capable1 = NewSummary(inGame: true, robloxUserId: 111L);
        var capable2 = NewSummary(inGame: true, robloxUserId: 222L);

        var anchor = AnchorGate.PickAnchor([notLanded, landedNoId, capable1, capable2]);

        Assert.Same(capable1, anchor);
    }

    [Fact]
    public void PickAnchor_SkipsLandedButUnknownUserId()
    {
        var landedNoId = NewSummary(inGame: true, robloxUserId: null);

        var anchor = AnchorGate.PickAnchor([landedNoId]);

        Assert.Null(anchor);
    }

    [Fact]
    public void PickAnchor_NoneCapable_ReturnsNull()
    {
        var notLanded = NewSummary(inGame: false, robloxUserId: 1L);
        var landedNoId = NewSummary(inGame: true, robloxUserId: null);

        var anchor = AnchorGate.PickAnchor([notLanded, landedNoId]);

        Assert.Null(anchor);
    }

    [Fact]
    public void PickAnchor_EmptyBatch_ReturnsNull()
    {
        var anchor = AnchorGate.PickAnchor([]);

        Assert.Null(anchor);
    }

    // === WaitComplete: done once landed (in game) ===

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WaitComplete_TruthTable(bool inGame, bool expected)
    {
        Assert.Equal(expected, AnchorGate.WaitComplete(inGame));
    }

    // === WaitExpired: past (or at) the deadline ===

    [Fact]
    public void WaitExpired_BeforeDeadline_False()
    {
        var deadline = new DateTime(2026, 1, 1, 0, 1, 30, DateTimeKind.Utc);
        var now = deadline.AddSeconds(-1);

        Assert.False(AnchorGate.WaitExpired(now, deadline));
    }

    [Fact]
    public void WaitExpired_AfterDeadline_True()
    {
        var deadline = new DateTime(2026, 1, 1, 0, 1, 30, DateTimeKind.Utc);
        var now = deadline.AddSeconds(1);

        Assert.True(AnchorGate.WaitExpired(now, deadline));
    }

    [Fact]
    public void WaitExpired_ExactDeadline_True()
    {
        // Boundary: exact match counts as expired (matches RobloxProcessTracker's `now >= deadline`
        // convention).
        var deadline = new DateTime(2026, 1, 1, 0, 1, 30, DateTimeKind.Utc);

        Assert.True(AnchorGate.WaitExpired(deadline, deadline));
    }

    [Fact]
    public void MaxWait_Is90Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), AnchorGate.MaxWait);
    }
}
