using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the pure <see cref="SquadLaunchPlan"/> split — trust-aware squad launch (v1.9.0):
/// eligible accounts split into a direct batch (launched straight away) and a flagged/follow
/// batch (join-via-friend once an anchor lands). Order-preserving within each batch; direct
/// always precedes flagged in the conceptual sequence (Task 5 launches Direct first).
/// </summary>
public class SquadLaunchPlanTests
{
    private static AccountSummary NewSummary(string name, bool joinViaFriend = false)
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: name,
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: null);
        return new AccountSummary(account) { JoinViaFriend = joinViaFriend };
    }

    [Fact]
    public void Build_MixedFlags_SplitsPreservingOrderWithinEachBatch()
    {
        var a = NewSummary("A", joinViaFriend: false);
        var b = NewSummary("B", joinViaFriend: true);
        var c = NewSummary("C", joinViaFriend: false);
        var d = NewSummary("D", joinViaFriend: true);

        var plan = SquadLaunchPlan.Build([a, b, c, d]);

        Assert.Equal([a, c], plan.Direct);
        Assert.Equal([b, d], plan.Flagged);
    }

    [Fact]
    public void Build_AllFlagged_EmptyDirect()
    {
        var a = NewSummary("A", joinViaFriend: true);
        var b = NewSummary("B", joinViaFriend: true);

        var plan = SquadLaunchPlan.Build([a, b]);

        Assert.Empty(plan.Direct);
        Assert.Equal([a, b], plan.Flagged);
    }

    [Fact]
    public void Build_NoneFlagged_EmptyFlagged()
    {
        var a = NewSummary("A", joinViaFriend: false);
        var b = NewSummary("B", joinViaFriend: false);

        var plan = SquadLaunchPlan.Build([a, b]);

        Assert.Equal([a, b], plan.Direct);
        Assert.Empty(plan.Flagged);
    }

    [Fact]
    public void Build_EmptyInput_BothEmpty()
    {
        var plan = SquadLaunchPlan.Build([]);

        Assert.Empty(plan.Direct);
        Assert.Empty(plan.Flagged);
    }
}
