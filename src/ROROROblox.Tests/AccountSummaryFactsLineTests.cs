using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The v1.36 facts line on <see cref="AccountSummary"/> (item 6, spec 2.1 / 2.4): what each badge
/// reads, when the line has something to say, and that facts for one account cannot reach another.
/// The streamer-mode rows live in <c>StreamerIdentityProviderTests</c>.
/// </summary>
public class AccountSummaryFactsLineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AccountSummary Row(Guid? id = null, AccountFacts? facts = null, string? note = null) =>
        new(new Account(
            Id: id ?? Guid.NewGuid(),
            DisplayName: "FakeAlt",
            AvatarUrl: "",
            CreatedAt: Now,
            LastLaunchedAt: null,
            Facts: facts,
            Note: note));

    private static AccountFacts Facts(long? robux = 1240, string? membership = null, int? friends = 3,
        DateTimeOffset? at = null, string? error = null) =>
        new(robux, membership, friends, at ?? Now.AddMinutes(-3), error);

    private static void GiveHistory(AccountSummary row, string game)
    {
        var panel = new AccountPanelViewModel(
            row,
            () => Task.FromResult<IReadOnlyList<LaunchSession>>([]),
            () => Task.FromResult<IReadOnlyList<LibraryEntry>>([]),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);
        row.AttachPanel(panel);
        panel.PrimeLastPlayed([new LaunchSession(Guid.NewGuid(), row.Id, "Alt", null, game, 1L, false, Now.AddHours(-2), Now.AddHours(-1), null)]);
    }

    [Fact]
    public void NullFacts_RenderTheDash_AndTheNamesReadUnknown()
    {
        var s = Row();

        Assert.Equal(AccountSummary.FactDash, s.RobuxText);
        Assert.Equal(AccountSummary.FactDash, s.MembershipText);
        Assert.Equal(AccountSummary.FactDash, s.FriendsOnlineText);
        Assert.Equal("Robux, unknown", s.RobuxAccessibleName);
        Assert.Equal("Membership, unknown", s.MembershipAccessibleName);
        Assert.Equal("Friends online, unknown", s.FriendsOnlineAccessibleName);
        Assert.Equal("Last played, unknown", s.LastPlayedAccessibleName);
        Assert.Equal(string.Empty, s.FactsAgeText);
    }

    [Fact]
    public void KnownFacts_RenderTheirValues_AndTheNamesCarryThem()
    {
        var s = Row(facts: Facts());

        Assert.Equal("R$ 1,240", s.RobuxText);
        Assert.Equal("3 friends online", s.FriendsOnlineText);
        Assert.Equal("Robux, R$ 1,240", s.RobuxAccessibleName);
        Assert.Equal("Friends online, 3 friends online", s.FriendsOnlineAccessibleName);
        Assert.Equal("Updated 3 min ago", s.FactsAgeText);
        Assert.False(s.FactsFailed);
    }

    [Fact]
    public void AFailedRefresh_KeepsThePreviousRobuxBesideTheFailedAgeHint()
    {
        var s = Row(facts: Facts(robux: 85, friends: 0, at: Now.AddHours(-2), error: "403"));

        Assert.Equal("R$ 85", s.RobuxText);
        Assert.Equal("0 friends online", s.FriendsOnlineText);
        Assert.Equal("Updated 2 hr ago · last try failed (403)", s.FactsAgeText);
        Assert.True(s.FactsFailed);
    }

    [Fact]
    public void AFailureWithNoSuccessEver_SaysSoInsteadOfInventingAnAge()
    {
        var s = Row(facts: new AccountFacts(null, null, null, null, "timeout"));

        Assert.Equal("Not updated yet · last try failed (timeout)", s.FactsAgeText);
        Assert.False(s.HasFactsLine); // the age hint alone never opens the line
    }

    [Fact]
    public void MembershipReadsPlus_ForAnyMembership_AndIsCollapsedOtherwise()
    {
        Assert.True(Row(facts: Facts(membership: "plus")).ShowMembershipBadge);
        Assert.Equal("Plus", Row(facts: Facts(membership: "plus")).MembershipText);
        Assert.True(Row(facts: Facts(membership: "premium")).ShowMembershipBadge);
        Assert.False(Row(facts: Facts(membership: "none")).ShowMembershipBadge);
        Assert.False(Row(facts: Facts(membership: null)).ShowMembershipBadge);
        Assert.False(Row().ShowMembershipBadge);
    }

    [Fact]
    public void TheLineIsCollapsed_WhenThereIsNothingToSay()
    {
        Assert.False(Row().HasFactsLine);
        Assert.False(Row(facts: new AccountFacts(null, null, null, null, null)).HasFactsLine);
        Assert.False(Row(note: "   ").HasFactsLine);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void AnyOneThing_OpensTheLine(bool robux, bool membership, bool friends, bool lastPlayed, bool note)
    {
        var s = Row(
            facts: new AccountFacts(robux ? 5 : null, membership ? "plus" : null, friends ? 2 : null, null, null),
            note: note ? "Farm alt" : null);
        if (lastPlayed) GiveHistory(s, "Pet Simulator 99");

        Assert.True(s.HasFactsLine);
    }

    [Fact]
    public void LastPlayedFromLocalHistory_AloneOpensTheLine_AndCarriesTheGame()
    {
        var s = Row();
        GiveHistory(s, "Pet Simulator 99");

        Assert.True(s.HasFactsLine);
        Assert.True(s.ShowLastPlayedBadge);
        Assert.StartsWith("Pet Simulator 99 · ", s.LastPlayedText);
        Assert.StartsWith("Last played, Pet Simulator 99", s.LastPlayedAccessibleName);
        Assert.False(s.ShowRobuxBadge);
    }

    [Fact]
    public void TheNoteBadge_ShowsTheFirstLineOnly()
    {
        var s = Row(note: "Farm alt, keep at 20 FPS\nsecond line stays in the panel");

        Assert.Equal("Farm alt, keep at 20 FPS", s.NoteFirstLine);
        Assert.Equal("Note, Farm alt, keep at 20 FPS", s.NoteAccessibleName);
        Assert.True(s.ShowNoteBadge);
    }

    [Fact]
    public void FactsApplied_ThroughApplyFacts_ReachOnlyTheirOwnRow()
    {
        // Build() disposes the window decorator and stops the periodic refresh itself.
        var (vm, _, _, _) = MainViewModelTests.Build();
        var a = Row();
        var b = Row();
        vm.Accounts.Add(a);
        vm.Accounts.Add(b);

        vm.ApplyFacts(a.Id, Facts(robux: 777));

        Assert.Equal("R$ 777", a.RobuxText);
        Assert.Null(b.Facts);
        Assert.Equal(AccountSummary.FactDash, b.RobuxText);
        Assert.False(b.HasFactsLine);
    }

    [Fact]
    public void ApplyingFacts_RaisesTheBadgeProperties_AndTheTickRaisesTheAgeHint()
    {
        var s = Row();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.Facts = Facts();
        Assert.Contains(nameof(AccountSummary.RobuxText), raised);
        Assert.Contains(nameof(AccountSummary.HasFactsLine), raised);
        Assert.Contains(nameof(AccountSummary.RobuxAccessibleName), raised);
        Assert.Contains(nameof(AccountSummary.FactsAgeText), raised);

        raised.Clear();
        s.RefreshRelativeTimes();
        Assert.Contains(nameof(AccountSummary.FactsAgeText), raised);
    }
}
