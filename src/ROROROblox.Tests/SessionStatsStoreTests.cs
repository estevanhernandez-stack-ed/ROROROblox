using System.IO;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The rollup store: arithmetic, records, streaks, and the honesty rules from spec §5.
/// Every test uses a temp file — none reads a developer's live session-stats.json.
/// </summary>
public class SessionStatsStoreTests : IDisposable
{
    private readonly string _tempPath;
    private static readonly Guid Alt = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public SessionStatsStoreTests()
        => _tempPath = Path.Combine(Path.GetTempPath(), $"rororoblox-stats-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
    }

    private SessionStatsStore NewStore() => new(_tempPath);

    private static DateTimeOffset Day(int d, int hour = 12)
        => new(2026, 3, d, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LaunchesAndUptimeAccumulatePerAccount()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 999L, "Pet Sim", Day(1)));
        await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 999L, Day(1), Day(1).AddMinutes(90)));

        var s = await store.ReadAsync();

        Assert.Equal(1, s.Accounts[Alt].Launches);
        Assert.Equal(TimeSpan.FromMinutes(90), s.Accounts[Alt].Uptime);
        Assert.Equal(TimeSpan.FromMinutes(90), s.TotalUptime);
        Assert.Equal(TimeSpan.FromMinutes(90), s.Games[999L].Uptime);
        Assert.Equal("Pet Sim", s.Games[999L].LastKnownName);
    }

    [Fact]
    public async Task TotalsSpanAccountsRatherThanTrackingOnlyTheLastOne()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 1L, Day(1), Day(1).AddMinutes(30)));
        await store.ApplyAsync(new StatsEvent.SessionEnded(Other, 1L, Day(1), Day(1).AddMinutes(45)));

        var s = await store.ReadAsync();

        Assert.Equal(2, s.Accounts.Count);
        Assert.Equal(TimeSpan.FromMinutes(75), s.TotalUptime);
    }

    [Fact]
    public async Task PeakConcurrencyOnlyRises()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.ConcurrencyObserved(3));
        await store.ApplyAsync(new StatsEvent.ConcurrencyObserved(7));
        await store.ApplyAsync(new StatsEvent.ConcurrencyObserved(2));

        Assert.Equal(7, (await store.ReadAsync()).PeakConcurrentAlts);
    }

    [Fact]
    public async Task ANegativeDurationContributesZeroRatherThanPoisoningTheTotal()
    {
        // Clock moved backwards mid-session. Spec §5 — a bad row must not poison a lifetime total.
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 1L, Day(2), Day(1)));

        var s = await store.ReadAsync();

        Assert.Equal(TimeSpan.Zero, s.TotalUptime);
        Assert.True(s.Accounts[Alt].Uptime >= TimeSpan.Zero);
    }

    [Fact]
    public async Task TheLongestSessionIsKeptAsARecord()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 1L, Day(1), Day(1).AddMinutes(30)));
        await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 2L, Day(2), Day(2).AddMinutes(200)));
        await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 3L, Day(3), Day(3).AddMinutes(10)));

        var longest = (await store.ReadAsync()).Longest;

        Assert.NotNull(longest);
        Assert.Equal(TimeSpan.FromMinutes(200), longest!.Duration);
        Assert.Equal(2L, longest.PlaceId);
    }

    [Fact]
    public async Task SessionsMissingAnEndAreCountedNotGuessed()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.SessionMissingEnd());
        await store.ApplyAsync(new StatsEvent.SessionMissingEnd());

        var s = await store.ReadAsync();

        Assert.Equal(2, s.SessionsMissingAnEnd);
        Assert.Equal(TimeSpan.Zero, s.TotalUptime);
    }

    [Fact]
    public async Task AStreakChainsAcrossADaylightSavingBoundary()
    {
        // Through the store this time, not just the helper: three launches on consecutive local
        // calendar days spanning the spring-forward transition. Deterministic in any time zone
        // because the assertion is about the chain, not about wall-clock spacing.
        var store = NewStore();
        foreach (var d in new[] { Day(7), Day(8), Day(9) })
        {
            await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", d));
        }

        var s = await store.ReadAsync();

        Assert.Equal(3, s.Streak.CurrentDays);
        Assert.Equal(3, s.Streak.LongestDays);
    }

    [Fact]
    public async Task AGapBreaksTheStreakButKeepsTheRecord()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(1)));
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(2)));
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(3)));
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(9)));  // gap

        var s = await store.ReadAsync();

        Assert.Equal(1, s.Streak.CurrentDays);
        Assert.Equal(3, s.Streak.LongestDays);
    }

    [Fact]
    public async Task TwoLaunchesOnOneDayDoNotAdvanceTheStreak()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(1, hour: 9)));
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(1, hour: 21)));

        var s = await store.ReadAsync();

        Assert.Equal(1, s.Streak.CurrentDays);
        Assert.Equal(2, s.Days[DayKey.For(Day(1))].Launches);
    }

    [Fact]
    public async Task ACorruptFileStartsFreshInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(_tempPath, "{ this is not json");

        var s = await NewStore().ReadAsync();

        Assert.Equal(0, s.PeakConcurrentAlts);
        Assert.Empty(s.Accounts);
    }

    [Fact]
    public async Task StatsSurviveAcrossStoreInstances()
    {
        await NewStore().ApplyAsync(new StatsEvent.ConcurrencyObserved(4));

        Assert.Equal(4, (await NewStore().ReadAsync()).PeakConcurrentAlts);
    }

    [Fact]
    public async Task ClearDropsEverything()
    {
        var store = NewStore();
        await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", Day(1)));
        await store.ApplyAsync(new StatsEvent.ConcurrencyObserved(5));

        await store.ClearAsync();
        var s = await store.ReadAsync();

        Assert.Empty(s.Accounts);
        Assert.Equal(0, s.PeakConcurrentAlts);
    }
}
