using ROROROblox.App.History;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The presenter is pure so the formatting rules are testable without WPF, and so name
/// resolution — ids to CURRENT names, via the roster — is pinned by a test rather than left to
/// the view. Spec §2.1: the store holds ids; a name baked in at write time goes stale on rename,
/// which is F-A's defect in the history writer.
/// </summary>
public class SessionStatsPresenterTests
{
    private static readonly Guid Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// An account whose Roblox display name and local rename differ — the exact case F-A gets
    /// wrong. Copies the helper idiom in AccountSummaryTagTests.
    /// </summary>
    private static AccountSummary SummaryWithLocalName(Guid id, string localName, string displayName)
    {
        var account = new Account(
            Id: id,
            DisplayName: displayName,
            AvatarUrl: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            Tags: null);
        return new AccountSummary(account) { LocalName = localName };
    }

    private static SessionStats WithAccount(Guid id, int launches = 1, TimeSpan? uptime = null)
        => SessionStats.Empty with
        {
            Accounts = new Dictionary<Guid, AccountStat>
            {
                [id] = new(launches, uptime ?? TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow),
            },
        };

    [Fact]
    public void AccountNamesComeFromTheRosterNotTheStore()
    {
        var stats = WithAccount(Id, launches: 5, uptime: TimeSpan.FromHours(2));
        var roster = new[] { SummaryWithLocalName(Id, localName: "Grinder", displayName: "OldRobloxName") };

        var view = SessionStatsPresenter.Build(stats, roster);

        Assert.Equal("Grinder", view.Leaderboard.Single().Name);
    }

    [Fact]
    public void AnAccountMissingFromTheRosterDegradesToAShortIdRatherThanBlank()
    {
        // Deleted account, stats survive — the row must still say SOMETHING attributable.
        var view = SessionStatsPresenter.Build(WithAccount(Id), Array.Empty<AccountSummary>());

        var name = view.Leaderboard.Single().Name;
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.StartsWith("3333", name);
    }

    [Fact]
    public void TheLeaderboardRanksByUptime()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var stats = SessionStats.Empty with
        {
            Accounts = new Dictionary<Guid, AccountStat>
            {
                [a] = new(Launches: 50, Uptime: TimeSpan.FromMinutes(10), LastSeenUtc: null),
                [b] = new(Launches: 1, Uptime: TimeSpan.FromHours(9), LastSeenUtc: null),
            },
        };

        var view = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

        Assert.Equal(TimeSpan.FromHours(9),
            view.Leaderboard.First().Uptime);
    }

    [Fact]
    public void MostPlayedRanksByUptimeNotLaunchCount()
    {
        // Spec §6: a game opened and quit five times is not more played than one long session.
        var stats = SessionStats.Empty with
        {
            Games = new Dictionary<long, GameStat>
            {
                [1L] = new("Quick", Launches: 5, Uptime: TimeSpan.FromMinutes(5)),
                [2L] = new("Long", Launches: 1, Uptime: TimeSpan.FromHours(3)),
            },
        };

        var view = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

        Assert.Equal("Long", view.MostPlayedGame);
    }

    [Fact]
    public void AGameWithNoKnownNameFallsBackToItsPlaceId()
    {
        var stats = SessionStats.Empty with
        {
            Games = new Dictionary<long, GameStat>
            {
                [8737899170L] = new(null, Launches: 3, Uptime: TimeSpan.FromHours(1)),
            },
        };

        var view = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

        Assert.Contains("8737899170", view.MostPlayedGame);
    }

    [Fact]
    public void TheMissingEndCountIsSurfacedWhenNonZero()
    {
        var stats = SessionStats.Empty with { SessionsMissingAnEnd = 3 };

        var view = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

        Assert.Contains("3", view.IntegrityNote);
    }

    [Fact]
    public void NoIntegrityNoteWhenNothingIsMissing()
    {
        var view = SessionStatsPresenter.Build(SessionStats.Empty, Array.Empty<AccountSummary>());

        Assert.True(string.IsNullOrEmpty(view.IntegrityNote));
    }

    [Theory]
    [InlineData(0, 0, 0, "0m")]        // zero reads as a number, not a blank
    [InlineData(0, 0, 42, "42m")]      // under an hour: minutes only
    [InlineData(0, 3, 5, "3h 5m")]     // hours carry minutes
    [InlineData(2, 1, 0, "49h 0m")]    // days fold INTO hours — "2d 1h" hides the grind;
                                       // 49 hours across alts is the brag, so show hours
    public void UptimeFormatsForBragging(int days, int hours, int minutes, string expected)
        => Assert.Equal(expected,
            SessionStatsPresenter.FormatUptime(new TimeSpan(days, hours, minutes, 0)));

    [Fact]
    public void AnEmptyStatsProducesAViewThatSaysSoRatherThanZerosPretendingToBeData()
    {
        var view = SessionStatsPresenter.Build(SessionStats.Empty, Array.Empty<AccountSummary>());

        Assert.False(view.HasAnything);
        Assert.Empty(view.Leaderboard);
    }
}
