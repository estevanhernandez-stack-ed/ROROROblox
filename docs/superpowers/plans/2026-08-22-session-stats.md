# Session Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A local stats surface — uptime, per-alt leaderboard, most-played game, peak concurrent alts, longest session, day streak — built from launch history RoRoRo already writes.

**Architecture:** A rollup store (`session-stats.json`) holding sums and records rather than rows, fed by a decorator over `ISessionHistoryStore` so there is exactly one call path into history. Peak concurrency is sampled live from the process tracker because it cannot be reconstructed later. Day buckets fold into months past a threshold so the file stops growing.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json`, xUnit. No new packages.

**Spec:** [`docs/superpowers/specs/2026-08-22-rororo-session-stats-design.md`](../specs/2026-08-22-rororo-session-stats-design.md)

## Global Constraints

- **No telemetry, of any kind, opt-in or otherwise.** `docs/PRIVACY.md` promises "No telemetry. No analytics. No third-party tracking." Nothing in this plan sends anything anywhere.
- **No new manifest capability.** `Package.appxmanifest` stays `runFullTrust` only.
- **Do not change `SessionHistoryStore.MaxRows`.** It stays 100. The rollup exists so the cap does not move.
- **Do not fix F-A (the raw `AccountDisplayName` at `MainViewModel.cs:2824`).** Its seam lands here; its fix is a separate cycle with its own register row.
- **No display name is ever a key.** Accounts key on `Guid accountId`, games on `long placeId`.
- **Match the existing store shape:** `SemaphoreSlim(1,1)` gate, camelCase `JsonSerializerOptions`, corrupt file falls back to empty, `DefaultPath()` static under `%LOCALAPPDATA%\ROROROblox\`.
- **Tests never read the developer's live `session-history.json`.** Temp files or committed fixtures only.
- Build: `dotnet build ROROROblox.slnx` · Test: `dotnet test src/ROROROblox.Tests/`
- Branch: `feat/session-stats`. Conventional commits, `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/ROROROblox.Core/SessionStats.cs` | Read model + `StatsEvent` records. Pure data, no I/O. |
| `src/ROROROblox.Core/ISessionStatsStore.cs` | `ApplyAsync` / `ReadAsync` / `ClearAsync`. |
| `src/ROROROblox.Core/SessionStatsStore.cs` | The file, the gate, the fold. |
| `src/ROROROblox.Core/StatsRecordingSessionHistoryStore.cs` | Decorator over `ISessionHistoryStore`. |
| `src/ROROROblox.Core/SessionStatsBackfill.cs` | One-time seed from existing history rows. |
| `src/ROROROblox.App/App.xaml.cs` | Registration swap (`:608`) + concurrency hook (near `:1238`). |
| `src/ROROROblox.App/History/SessionHistoryPage.xaml(.cs)` | Stats surface + roster dependency. |
| `src/ROROROblox.Tests/SessionStatsModelTests.cs` | Read-model shape. |
| `src/ROROROblox.Tests/SessionStatsStoreTests.cs` | Arithmetic, records, fold, corruption, clamping. |
| `src/ROROROblox.Tests/StatsRecordingSessionHistoryStoreTests.cs` | Pass-through and isolation. |
| `src/ROROROblox.Tests/SessionStatsBackfillTests.cs` | Fixture-driven seed + idempotence. |
| `src/ROROROblox.Tests/Fixtures/session-history-sample.json` | Anonymised 100-row fixture. |

---

## Task 1: The read model

**Files:**
- Create: `src/ROROROblox.Core/SessionStats.cs`
- Test: `src/ROROROblox.Tests/SessionStatsModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SessionStats`, `AccountStat`, `GameStat`, `DayStat`, `MonthStat`, `StreakRecord`, `LongestSession`, `StatsEvent` (with `LaunchRecorded` / `SessionEnded` / `ConcurrencyObserved` cases). Every later task uses these names.

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class SessionStatsModelTests
{
    [Fact]
    public void AnEmptyStatsReadsAsZeroesRatherThanNulls()
    {
        var s = SessionStats.Empty;

        Assert.Empty(s.Accounts);
        Assert.Empty(s.Games);
        Assert.Empty(s.Days);
        Assert.Empty(s.Months);
        Assert.Equal(0, s.PeakConcurrentAlts);
        Assert.Equal(TimeSpan.Zero, s.TotalUptime);
        Assert.Equal(0, s.SessionsMissingAnEnd);
        Assert.Null(s.Longest);
        Assert.Equal(0, s.Streak.CurrentDays);
        Assert.Equal(0, s.Streak.LongestDays);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsModelTests"`
Expected: FAIL — `SessionStats` does not exist.

- [ ] **Step 3: Write the model**

```csharp
namespace ROROROblox.Core;

/// <summary>
/// Rollup of launch history. Sums and records, never per-session rows — the raw window is
/// capped at 100 (SessionHistoryStore.MaxRows) and discards after roughly twenty days, so
/// anything phrased as "all time" has to be accumulated as it happens. See spec §0.
/// </summary>
public sealed record SessionStats(
    IReadOnlyDictionary<Guid, AccountStat> Accounts,
    IReadOnlyDictionary<long, GameStat> Games,
    IReadOnlyDictionary<string, DayStat> Days,      // key: local yyyy-MM-dd
    IReadOnlyDictionary<string, MonthStat> Months,  // key: local yyyy-MM
    int PeakConcurrentAlts,
    LongestSession? Longest,
    StreakRecord Streak,
    int SessionsMissingAnEnd,
    bool Backfilled)
{
    public static SessionStats Empty { get; } = new(
        new Dictionary<Guid, AccountStat>(),
        new Dictionary<long, GameStat>(),
        new Dictionary<string, DayStat>(),
        new Dictionary<string, MonthStat>(),
        PeakConcurrentAlts: 0,
        Longest: null,
        Streak: new StreakRecord(0, 0, null, null),
        SessionsMissingAnEnd: 0,
        Backfilled: false);

    /// <summary>Sum of every account's uptime. Excludes sessions that never recorded an end.</summary>
    public TimeSpan TotalUptime =>
        Accounts.Values.Aggregate(TimeSpan.Zero, (acc, a) => acc + a.Uptime);
}

public sealed record AccountStat(int Launches, TimeSpan Uptime, DateTimeOffset? LastSeenUtc);

/// <summary>Name is "last known" and purely for display — never a key. Spec §2.1.</summary>
public sealed record GameStat(string? LastKnownName, int Launches, TimeSpan Uptime);

public sealed record DayStat(int Launches, TimeSpan Uptime);

public sealed record MonthStat(int Launches, TimeSpan Uptime, int DaysPlayed);

/// <summary>
/// Maintained incrementally, never derived from <see cref="SessionStats.Days"/> — day buckets
/// fold into months past a threshold and a streak spanning that boundary would be unrecoverable.
/// Spec §2.2.
/// </summary>
public sealed record StreakRecord(
    int CurrentDays, int LongestDays, string? CurrentStartDay, string? LastPlayedDay);

public sealed record LongestSession(
    TimeSpan Duration, Guid AccountId, long? PlaceId, DateTimeOffset WhenUtc);

/// <summary>What the store is told. One case per thing that can move a number.</summary>
public abstract record StatsEvent
{
    public sealed record LaunchRecorded(
        Guid AccountId, long? PlaceId, string? GameName, DateTimeOffset AtUtc) : StatsEvent;

    public sealed record SessionEnded(
        Guid AccountId, long? PlaceId, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc) : StatsEvent;

    public sealed record ConcurrencyObserved(int Count) : StatsEvent;

    public sealed record SessionMissingEnd() : StatsEvent;
}
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/SessionStats.cs src/ROROROblox.Tests/SessionStatsModelTests.cs
git commit -m "feat(stats): the session-stats read model

Sums and records rather than rows. Keys are ids, never display names --
a rollup keyed by name splits a lifetime total in two on rename. Spec 2.1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: The store — arithmetic and records

**Files:**
- Create: `src/ROROROblox.Core/ISessionStatsStore.cs`, `src/ROROROblox.Core/SessionStatsStore.cs`
- Test: `src/ROROROblox.Tests/SessionStatsStoreTests.cs`

**Interfaces:**
- Consumes: everything from Task 1.
- Produces: `ISessionStatsStore` with `Task ApplyAsync(StatsEvent e)`, `Task<SessionStats> ReadAsync()`, `Task ClearAsync()`; `SessionStatsStore(string filePath)` and `SessionStatsStore()`; `SessionStatsStore.DefaultPath()`; `SessionStatsStore.RawDayLimit` (const int, 400).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class SessionStatsStoreTests : IDisposable
{
    private readonly string _tempPath;
    private static readonly Guid Alt = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
        var store = NewStore();
        // Clock moved backwards mid-session. Spec 5.
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
    public async Task ACorruptFileStartsFreshInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(_tempPath, "{ this is not json");

        var s = await NewStore().ReadAsync();

        Assert.Equal(0, s.PeakConcurrentAlts);
        Assert.Empty(s.Accounts);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsStoreTests"`
Expected: FAIL — `SessionStatsStore` does not exist.

- [ ] **Step 3: Write the interface**

```csharp
namespace ROROROblox.Core;

/// <summary>
/// Durable rollup of launch history. Survives SessionHistoryStore's 100-row prune, which is
/// why it exists at all — see spec 0.
/// </summary>
public interface ISessionStatsStore
{
    Task ApplyAsync(StatsEvent statsEvent);
    Task<SessionStats> ReadAsync();
    Task ClearAsync();
}
```

- [ ] **Step 4: Write the store**

Mirror `SessionHistoryStore` exactly: same `JsonSerializerOptions` (camelCase, indented, ignore-null), same `SemaphoreSlim(1,1)` gate, same corrupt-falls-back-to-empty `LoadAsync`, same `DefaultPath()` shape but returning `session-stats.json`. `ApplyAsync` loads, switches on the event, saves. Day keys are `AtUtc.ToLocalTime().ToString("yyyy-MM-dd")` — local, per spec §6. Leave the fold for Task 3; add `public const int RawDayLimit = 400;` now so Task 3 has the name.

Key detail for `SessionEnded`: compute `var d = EndedUtc - StartedUtc; if (d < TimeSpan.Zero) d = TimeSpan.Zero;` **before** touching any total.

Key detail for streak: on a `LaunchRecorded` whose local day differs from `Streak.LastPlayedDay`, extend when the new day is exactly one after, otherwise restart at 1; update `LongestDays` when `CurrentDays` exceeds it.

- [ ] **Step 5: Run them and watch them pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsStoreTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/ISessionStatsStore.cs src/ROROROblox.Core/SessionStatsStore.cs src/ROROROblox.Tests/SessionStatsStoreTests.cs
git commit -m "feat(stats): the rollup store

Same shape as SessionHistoryStore -- gated, atomic, corrupt falls back to
empty. Durations clamp at zero so a clock change cannot poison a lifetime
total, and sessions with no end are counted rather than guessed at.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: The fold

**Files:**
- Modify: `src/ROROROblox.Core/SessionStatsStore.cs`
- Test: `src/ROROROblox.Tests/SessionStatsStoreTests.cs`

**Interfaces:**
- Consumes: Task 2's store.
- Produces: no new public names. `RawDayLimit` becomes live.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task DaysBeyondTheLimitFoldIntoMonthsWithTotalsIntact()
    {
        var store = NewStore();
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // One launch a day for RawDayLimit + 50 days.
        for (var i = 0; i < SessionStatsStore.RawDayLimit + 50; i++)
        {
            var at = start.AddDays(i);
            await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", at));
            await store.ApplyAsync(new StatsEvent.SessionEnded(Alt, 1L, at, at.AddMinutes(10)));
        }

        var s = await store.ReadAsync();

        Assert.True(s.Days.Count <= SessionStatsStore.RawDayLimit,
            $"raw days should be capped at {SessionStatsStore.RawDayLimit}, found {s.Days.Count}");
        Assert.NotEmpty(s.Months);

        // The fold moves detail, never totals.
        var expected = TimeSpan.FromMinutes(10 * (SessionStatsStore.RawDayLimit + 50));
        Assert.Equal(expected, s.Accounts[Alt].Uptime);

        var folded = s.Days.Values.Aggregate(TimeSpan.Zero, (a, d) => a + d.Uptime)
                   + s.Months.Values.Aggregate(TimeSpan.Zero, (a, m) => a + m.Uptime);
        Assert.Equal(expected, folded);
    }

    [Fact]
    public async Task AStreakSurvivesTheFold()
    {
        // The test that proves streaks are records, not derivations. A streak established
        // before the fold boundary cannot be recomputed from monthly totals. Spec 2.2.
        var store = NewStore();
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < SessionStatsStore.RawDayLimit + 50; i++)
        {
            await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", start.AddDays(i)));
        }

        var s = await store.ReadAsync();

        Assert.True(s.Days.Count <= SessionStatsStore.RawDayLimit);
        Assert.Equal(SessionStatsStore.RawDayLimit + 50, s.Streak.LongestDays);
        Assert.Equal(SessionStatsStore.RawDayLimit + 50, s.Streak.CurrentDays);
    }

    [Fact]
    public async Task FoldingIsIdempotent()
    {
        var store = NewStore();
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < SessionStatsStore.RawDayLimit + 10; i++)
        {
            await store.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, 1L, "G", start.AddDays(i)));
        }

        var first = await store.ReadAsync();
        await store.ApplyAsync(new StatsEvent.ConcurrencyObserved(1)); // triggers another save
        var second = await store.ReadAsync();

        Assert.Equal(first.Months.Count, second.Months.Count);
        Assert.Equal(
            first.Months.Values.Aggregate(0, (a, m) => a + m.Launches),
            second.Months.Values.Aggregate(0, (a, m) => a + m.Launches));
    }
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsStoreTests"`
Expected: FAIL — day count exceeds the limit; `Months` is empty.

- [ ] **Step 3: Implement the fold**

In `SaveAsync`, before writing: if `Days.Count > RawDayLimit`, take the oldest keys beyond the newest `RawDayLimit`, and for each add its `Launches`/`Uptime` into the `yyyy-MM` month bucket (incrementing `DaysPlayed` by one per folded day), then remove the day. Touch nothing else — accounts, games, records, and the streak are untouched by folding, which is the whole point.

- [ ] **Step 4: Run them and watch them pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsStoreTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/SessionStatsStore.cs src/ROROROblox.Tests/SessionStatsStoreTests.cs
git commit -m "feat(stats): fold day buckets into months past the raw limit

Per-day is the only collection that grows forever. ApplyAsync is
read-modify-write on every session end, so unbounded days recreate the
objection this design raised against raising MaxRows -- slower clock,
same mistake. Streaks are records precisely so the fold is lossless.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: The decorator

**Files:**
- Create: `src/ROROROblox.Core/StatsRecordingSessionHistoryStore.cs`
- Test: `src/ROROROblox.Tests/StatsRecordingSessionHistoryStoreTests.cs`

**Interfaces:**
- Consumes: `ISessionHistoryStore`, `ISessionStatsStore`.
- Produces: `StatsRecordingSessionHistoryStore(ISessionHistoryStore inner, ISessionStatsStore stats, ILogger<StatsRecordingSessionHistoryStore>? log = null)` implementing `ISessionHistoryStore`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class StatsRecordingSessionHistoryStoreTests
{
    private sealed class RecordingInner : ISessionHistoryStore
    {
        public readonly List<LaunchSession> Added = new();
        public readonly List<Guid> Ended = new();
        public int ClearCalls;
        public Task<IReadOnlyList<LaunchSession>> ListAsync()
            => Task.FromResult<IReadOnlyList<LaunchSession>>(Added);
        public Task AddAsync(LaunchSession s) { Added.Add(s); return Task.CompletedTask; }
        public Task MarkEndedAsync(Guid id, DateTimeOffset at, string? hint = null)
        { Ended.Add(id); return Task.CompletedTask; }
        public Task ClearAsync() { ClearCalls++; return Task.CompletedTask; }
    }

    private sealed class ThrowingStats : ISessionStatsStore
    {
        public Task ApplyAsync(StatsEvent e) => throw new InvalidOperationException("disk on fire");
        public Task<SessionStats> ReadAsync() => Task.FromResult(SessionStats.Empty);
        public Task ClearAsync() => Task.CompletedTask;
    }

    private sealed class SpyStats : ISessionStatsStore
    {
        public readonly List<StatsEvent> Events = new();
        public Task ApplyAsync(StatsEvent e) { Events.Add(e); return Task.CompletedTask; }
        public Task<SessionStats> ReadAsync() => Task.FromResult(SessionStats.Empty);
        public Task ClearAsync() { Events.Clear(); return Task.CompletedTask; }
    }

    private static LaunchSession Sample(Guid? account = null) => new(
        Id: Guid.NewGuid(),
        AccountId: account ?? Guid.NewGuid(),
        AccountDisplayName: "Pokey",
        AccountAvatarUrl: null,
        GameName: "Pet Sim 99",
        PlaceId: 12345L,
        IsPrivateServer: false,
        LaunchedAtUtc: DateTimeOffset.UtcNow,
        EndedAtUtc: null,
        OutcomeHint: null);

    [Fact]
    public async Task EveryCallReachesTheInnerStore()
    {
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new SpyStats());
        var session = Sample();

        await sut.AddAsync(session);
        await sut.MarkEndedAsync(session.Id, DateTimeOffset.UtcNow);
        await sut.ClearAsync();
        await sut.ListAsync();

        Assert.Single(inner.Added);
        Assert.Single(inner.Ended);
        Assert.Equal(1, inner.ClearCalls);
    }

    [Fact]
    public async Task AddRecordsALaunch()
    {
        var stats = new SpyStats();
        var sut = new StatsRecordingSessionHistoryStore(new RecordingInner(), stats);

        await sut.AddAsync(Sample());

        Assert.Contains(stats.Events, e => e is StatsEvent.LaunchRecorded);
    }

    [Fact]
    public async Task AStatsFailureDoesNotPreventTheHistoryWrite()
    {
        // A swallowed inner call would be data loss wearing a feature's clothes. Spec 3.2.
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new ThrowingStats());

        await sut.AddAsync(Sample());

        Assert.Single(inner.Added);
    }

    [Fact]
    public async Task ClearingHistoryAlsoClearsStats()
    {
        var stats = new SpyStats();
        var sut = new StatsRecordingSessionHistoryStore(new RecordingInner(), stats);
        await sut.AddAsync(Sample());

        await sut.ClearAsync();

        Assert.Empty(stats.Events);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~StatsRecordingSessionHistoryStoreTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Write the decorator**

Inner call first, stats second, stats wrapped in `try/catch` that logs and swallows. `AddAsync` emits `LaunchRecorded`. `MarkEndedAsync` needs the start time, so look the row up via `inner.ListAsync()` before delegating; if the row is gone (pruned), emit `SessionMissingEnd` instead. `ClearAsync` clears both.

- [ ] **Step 4: Run them and watch them pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~StatsRecordingSessionHistoryStoreTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/StatsRecordingSessionHistoryStore.cs src/ROROROblox.Tests/StatsRecordingSessionHistoryStoreTests.cs
git commit -m "feat(stats): record through a decorator, not a second call site

F-121 existed because a fix landed at one call site while a second kept the
old form, with a comment claiming they matched. A decorator means there is
still exactly one call path into history, so stats cannot silently disagree
with what History shows. A stats failure never fails a launch record.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Backfill

**Files:**
- Create: `src/ROROROblox.Core/SessionStatsBackfill.cs`, `src/ROROROblox.Tests/Fixtures/session-history-sample.json`
- Test: `src/ROROROblox.Tests/SessionStatsBackfillTests.cs`

**Interfaces:**
- Consumes: `ISessionHistoryStore`, `ISessionStatsStore`, `SessionStats.Backfilled`.
- Produces: `static Task SessionStatsBackfill.RunOnceAsync(ISessionHistoryStore history, ISessionStatsStore stats)`.

- [ ] **Step 1: Build the fixture**

Create `src/ROROROblox.Tests/Fixtures/session-history-sample.json` with **exactly 100 sessions**, of which **93 carry an `endedAtUtc`**, across **8 account GUIDs** and **3 games**, shaped like spec §0. Use invented GUIDs and names — never real account ids. Total the 93 durations to a round, asserted figure. Mark the file `Content` with `CopyToOutputDirectory=PreserveNewest` in `ROROROblox.Tests.csproj`.

- [ ] **Step 2: Write the failing tests**

The fixture loader first — every test below uses it, and it must never touch a live file:

```csharp
using System.IO;
using System.Text.Json;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class SessionStatsBackfillTests : IDisposable
{
    private readonly string _tempPath;

    public SessionStatsBackfillTests()
        => _tempPath = Path.Combine(Path.GetTempPath(), $"rororoblox-backfill-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
    }

    /// <summary>In-memory history over the committed fixture. Never reads a live file.</summary>
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
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var rows = doc.RootElement.GetProperty("sessions").Deserialize<List<LaunchSession>>(opts)!;
        return new FixtureHistoryStore(rows);
    }

    [Fact]
    public async Task BackfillSeedsFromExistingRowsAndCountsTheEndlessOnes()
{
    var history = FixtureHistory();          // loads the 100-row fixture
    var stats = new SessionStatsStore(_tempPath);

    await SessionStatsBackfill.RunOnceAsync(history, stats);
    var s = await stats.ReadAsync();

    Assert.Equal(8, s.Accounts.Count);
    Assert.Equal(3, s.Games.Count);
    Assert.Equal(7, s.SessionsMissingAnEnd);
    Assert.True(s.Backfilled);
    Assert.True(s.TotalUptime > TimeSpan.Zero);
}

[Fact]
public async Task BackfillCannotRecoverPeakConcurrency()
{
    // Spec 4: concurrency is a property of a moment, not of any session.
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
    Assert.Equal(first.Accounts[first.Accounts.Keys.First()].Launches,
                 second.Accounts[first.Accounts.Keys.First()].Launches);
}
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsBackfillTests"`
Expected: FAIL — `SessionStatsBackfill` does not exist.

- [ ] **Step 4: Implement**

`RunOnceAsync` reads `stats.ReadAsync()`; if `Backfilled` is true it returns immediately — that flag is the idempotence guard. Otherwise enumerate `history.ListAsync()`, emitting `LaunchRecorded` per row and `SessionEnded` for rows with an end, `SessionMissingEnd` for rows without, then set `Backfilled`. Never emit `ConcurrencyObserved`.

- [ ] **Step 5: Run them and watch them pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsBackfillTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/SessionStatsBackfill.cs src/ROROROblox.Tests/SessionStatsBackfillTests.cs src/ROROROblox.Tests/Fixtures/session-history-sample.json src/ROROROblox.Tests/ROROROblox.Tests.csproj
git commit -m "feat(stats): seed the rollup from existing history once

A stats page reading zero on the day it ships is a bad first impression of
a feature whose whole appeal is an accumulated number. Best-effort: it
cannot recover peak concurrency, and the UI must not imply otherwise.

Fixture is anonymised and committed -- no test reads a developer's live
session-history.json, which differs per machine and is empty in CI.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: Wiring

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs:608` (registration), near `:1238` (concurrency hook)
- Test: `src/ROROROblox.Tests/SessionStatsWiringTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 4, 5.
- Produces: `ISessionStatsStore` resolvable from the container; `ISessionHistoryStore` resolves to the decorator.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void TheHistoryStoreResolvesToTheStatsRecordingDecorator()
{
    var services = new ServiceCollection();
    App.ConfigureServicesForTests(services);   // existing seam; follow TypedHttpClientRegistrationTests
    using var sp = services.BuildServiceProvider();

    var store = sp.GetRequiredService<ISessionHistoryStore>();

    Assert.IsType<StatsRecordingSessionHistoryStore>(store);
    Assert.NotNull(sp.GetRequiredService<ISessionStatsStore>());
}
```

If no `ConfigureServicesForTests` seam exists, mirror whatever `TypedHttpClientRegistrationTests.cs` already does to build a real container — do not invent a new seam.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsWiringTests"`
Expected: FAIL — resolves to `SessionHistoryStore`.

- [ ] **Step 3: Swap the registration**

Replace `App.xaml.cs:608` with a singleton `ISessionStatsStore` plus an `ISessionHistoryStore` that wraps `new SessionHistoryStore()` in the decorator.

- [ ] **Step 4: Add the concurrency hook**

Beside the existing `tracker.ProcessAttached += ...` near `:1238`, push `tracker.Attached.Count` into the stats store as `ConcurrencyObserved`. Fire-and-forget with a logged catch — this must never delay an attach.

- [ ] **Step 5: Call the backfill at startup**

After the container is built and before the window shows, fire `SessionStatsBackfill.RunOnceAsync(...)` fire-and-forget with a logged catch.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS — the pre-existing count plus this cycle's new tests, zero failures.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/SessionStatsWiringTests.cs
git commit -m "feat(stats): wire the rollup, the decorator, and the backfill

Peak concurrency is sampled at ProcessAttached because it is a property of
a moment, not of any session -- overlapping rows may already have been
pruned out from under us by the time anyone asks.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: The surface

**Files:**
- Modify: `src/ROROROblox.App/History/SessionHistoryPage.xaml`, `SessionHistoryPage.xaml.cs:57-63`, and its construction site at `App.xaml.cs:1124`
- Test: `src/ROROROblox.Tests/SessionStatsPresenterTests.cs`

**Interfaces:**
- Consumes: `ISessionStatsStore`, plus an account roster for name resolution.
- Produces: `SessionStatsPresenter.Build(SessionStats stats, IReadOnlyList<AccountSummary> roster)` returning display rows.

- [ ] **Step 1: Write the failing test**

Put the formatting logic in a **pure presenter** so it is testable without WPF — the page itself stays thin. The presenter is where §2.1 is honoured:

```csharp
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class SessionStatsPresenterTests
{
    /// <summary>
    /// An account whose Roblox display name and local rename differ — the exact case F-A gets
    /// wrong in the history writer. Copies the helper idiom in AccountSummaryTagTests.
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

    [Fact]
    public void AccountNamesComeFromTheRosterNotTheStore()
{
    // Spec 2.1: the store holds ids. A renamed account must show its CURRENT name,
    // which is the defect F-A has in the history writer.
    var id = Guid.NewGuid();
    var stats = SessionStats.Empty with
    {
        Accounts = new Dictionary<Guid, AccountStat>
        {
            [id] = new(Launches: 5, Uptime: TimeSpan.FromHours(2), LastSeenUtc: DateTimeOffset.UtcNow)
        }
    };
    var roster = new[] { SummaryWithLocalName(id, localName: "Grinder", displayName: "OldRobloxName") };

    var rows = SessionStatsPresenter.Build(stats, roster);

    Assert.Equal("Grinder", rows.Leaderboard.Single().Name);
}

[Fact]
public void AnAccountMissingFromTheRosterDegradesToAShortIdRatherThanBlank()
{
    var id = Guid.NewGuid();
    var stats = SessionStats.Empty with
    {
        Accounts = new Dictionary<Guid, AccountStat>
        {
            [id] = new(1, TimeSpan.FromMinutes(1), null)
        }
    };

    var rows = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

    Assert.False(string.IsNullOrWhiteSpace(rows.Leaderboard.Single().Name));
}

[Fact]
public void TheMissingEndCountIsSurfacedWhenNonZero()
{
    var stats = SessionStats.Empty with { SessionsMissingAnEnd = 3 };

    var rows = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

    Assert.Contains("3", rows.IntegrityNote);
}

[Fact]
public void NoIntegrityNoteWhenNothingIsMissing()
{
    var rows = SessionStatsPresenter.Build(SessionStats.Empty, Array.Empty<AccountSummary>());

    Assert.True(string.IsNullOrEmpty(rows.IntegrityNote));
}

[Fact]
public void MostPlayedRanksByUptimeNotLaunchCount()
{
    // Spec 6: a game opened and quit five times is not more played than one long session.
    var stats = SessionStats.Empty with
    {
        Games = new Dictionary<long, GameStat>
        {
            [1L] = new("Quick", Launches: 5, Uptime: TimeSpan.FromMinutes(5)),
            [2L] = new("Long",  Launches: 1, Uptime: TimeSpan.FromHours(3)),
        }
    };

    var rows = SessionStatsPresenter.Build(stats, Array.Empty<AccountSummary>());

    Assert.Equal("Long", rows.MostPlayedGame);
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsPresenterTests"`
Expected: FAIL — `SessionStatsPresenter` does not exist.

- [ ] **Step 3: Write the presenter**

Pure static class, no WPF types. Resolves names via `roster.FirstOrDefault(a => a.Id == id)?.RenderName`, falling back to the first eight characters of the id. Emits the six stats from §1 plus `IntegrityNote` (empty when `SessionsMissingAnEnd == 0`).

- [ ] **Step 4: Add the XAML section and the roster dependency**

Add a stats block above the existing history list. Add `ISessionStatsStore` and a roster accessor to the page's constructor, and update the construction site at `App.xaml.cs:1124`. Follow the existing card and type styles — do not introduce new brand tokens; see `~/.claude/skills/626labs-design/`. Label peak concurrency "since install" per §5.

- [ ] **Step 5: Run them and watch them pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~SessionStatsPresenterTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Run the whole suite and build**

Run: `dotnet build ROROROblox.slnx && dotnet test ROROROblox.slnx`
Expected: build clean, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/History/ src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/SessionStatsPresenterTests.cs
git commit -m "feat(stats): the stats surface on the history page

Formatting lives in a pure presenter so it is testable without WPF, and so
that name resolution -- ids to current names, via the roster -- is pinned by
a test rather than left to the view. Peak concurrency is labelled since
install, because backfill cannot reach it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Manual verification

**Files:** none — this is the gate before merge.

- [ ] **Step 1: Launch and confirm the backfill**

Run the app. Open Session History. Confirm the stats block reads non-zero on first launch (the backfill found existing rows) and that peak concurrency reads zero or "since install".

- [ ] **Step 2: Confirm peak concurrency samples live**

Launch two alts. Confirm peak concurrent rises to 2. Stop one. Confirm it does **not** fall.

- [ ] **Step 3: Confirm uptime accrues**

Note total uptime. Launch an alt, leave it a few minutes, stop it. Confirm uptime rose by roughly that much and the leaderboard row moved.

- [ ] **Step 4: Confirm the file**

Read `%LOCALAPPDATA%\ROROROblox\session-stats.json`. Confirm no display name appears as a key, and that account keys are GUIDs.

- [ ] **Step 5: Confirm history still works**

Confirm the history list still populates — the decorator has not broken the thing it wraps.

---

## Self-review notes

**Spec coverage.** §0 → Tasks 2, 3 (the rollup exists because of the cap). §1 → Tasks 2, 7. §2 → Tasks 1, 2. §2.1 → Task 1 model, Task 7 presenter test. §2.2 → Task 3. §3.1 → Task 2. §3.2 → Task 4. §3.3 → Task 6 step 4. §4 → Task 5. §5 → Task 2 (clamping, missing-end), Task 7 (integrity note, "since install"). §6 → Task 2 (local days), Task 7 (roster, uptime ranking). §7 → every task's tests. §8 → File Structure. §9 → Global Constraints.

**Known gap, deliberate.** Spec §7 lists a DST-transition streak test. It is not a separate task step because it belongs in Task 2's streak logic; add it there as a sixth test if the implementer's streak code branches on anything timezone-shaped. Flagged rather than silently dropped.
