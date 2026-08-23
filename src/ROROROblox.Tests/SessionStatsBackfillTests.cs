using System.IO;
using System.Text.Json;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Backfill seeds the rollup from whatever history rows survive, so the page does not read zero
/// on the day it ships (spec §4).
///
/// <para>Every test runs against a committed, anonymised fixture — never the developer's live
/// session-history.json, which differs per machine and is empty in CI. A test that reads a live
/// file passes for whoever wrote it and proves nothing anywhere else.</para>
/// </summary>
public class SessionStatsBackfillTests : IDisposable
{
    private readonly string _tempPath;

    public SessionStatsBackfillTests()
        => _tempPath = Path.Combine(Path.GetTempPath(), $"rororoblox-backfill-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
    }

    /// <summary>In-memory history over the committed fixture.</summary>
    private sealed class FixtureHistoryStore : ISessionHistoryStore
    {
        private readonly List<LaunchSession> _rows;
        public FixtureHistoryStore(List<LaunchSession> rows) => _rows = rows;
        public Task<IReadOnlyList<LaunchSession>> ListAsync()
            => Task.FromResult<IReadOnlyList<LaunchSession>>(_rows);
        public Task AddAsync(LaunchSession s) => Task.CompletedTask;
        public Task MarkEndedAsync(Guid id, DateTimeOffset at, string? hint = null) => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;
    }

    private static ISessionHistoryStore FixtureHistory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "session-history-sample.json");
        Assert.True(File.Exists(path), $"fixture missing at {path} — check CopyToOutputDirectory");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var rows = doc.RootElement.GetProperty("sessions").Deserialize<List<LaunchSession>>(opts)!;
        return new FixtureHistoryStore(rows);
    }

    [Fact]
    public async Task TheFixtureIsTheShapeTheseTestsAssume()
    {
        // Guards the guard: if someone regenerates the fixture differently, fail here with a
        // clear reason rather than in five confusing assertions below.
        var rows = await FixtureHistory().ListAsync();

        Assert.Equal(100, rows.Count);
        Assert.Equal(93, rows.Count(r => r.EndedAtUtc is not null));
        Assert.Equal(8, rows.Select(r => r.AccountId).Distinct().Count());
    }

    [Fact]
    public async Task BackfillSeedsFromExistingRowsAndCountsTheEndlessOnes()
    {
        var stats = new SessionStatsStore(_tempPath);

        await SessionStatsBackfill.RunOnceAsync(FixtureHistory(), stats);
        var s = await stats.ReadAsync();

        Assert.Equal(8, s.Accounts.Count);
        Assert.Equal(7, s.SessionsMissingAnEnd);
        Assert.True(s.Backfilled);
        Assert.Equal(TimeSpan.FromMinutes(2820), s.TotalUptime);
    }

    [Fact]
    public async Task OnlyGamesWithAPlaceIdBecomeKeyedRows()
    {
        // A launch to the Roblox home has no place id. It is a real launch and counts toward the
        // account, but there is no key to file it under — so it must not invent one.
        var stats = new SessionStatsStore(_tempPath);

        await SessionStatsBackfill.RunOnceAsync(FixtureHistory(), stats);
        var s = await stats.ReadAsync();

        Assert.Equal(2, s.Games.Count);
        Assert.Equal(100, s.Accounts.Values.Sum(a => a.Launches));
    }

    [Fact]
    public async Task BackfillCannotRecoverPeakConcurrency()
    {
        // Spec §4: concurrency is a property of a moment, not of any session. Overlapping rows
        // tell you two overlapped; the third and fourth alt may already have been pruned.
        var stats = new SessionStatsStore(_tempPath);

        await SessionStatsBackfill.RunOnceAsync(FixtureHistory(), stats);

        Assert.Equal(0, (await stats.ReadAsync()).PeakConcurrentAlts);
    }

    [Fact]
    public async Task RunningBackfillTwiceDoesNotDoubleAnyTotal()
    {
        var stats = new SessionStatsStore(_tempPath);
        await SessionStatsBackfill.RunOnceAsync(FixtureHistory(), stats);
        var first = await stats.ReadAsync();

        await SessionStatsBackfill.RunOnceAsync(FixtureHistory(), stats);
        var second = await stats.ReadAsync();

        Assert.Equal(first.TotalUptime, second.TotalUptime);
        Assert.Equal(first.SessionsMissingAnEnd, second.SessionsMissingAnEnd);
        Assert.Equal(first.Accounts.Values.Sum(a => a.Launches),
                     second.Accounts.Values.Sum(a => a.Launches));
    }

    [Fact]
    public async Task BackfillOnAnEmptyHistoryStillMarksItselfDone()
    {
        // Otherwise a user with no history re-runs the scan on every single startup forever.
        var stats = new SessionStatsStore(_tempPath);

        await SessionStatsBackfill.RunOnceAsync(new FixtureHistoryStore(new List<LaunchSession>()), stats);

        Assert.True((await stats.ReadAsync()).Backfilled);
    }
}
