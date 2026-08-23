namespace ROROROblox.Core;

/// <summary>
/// Rollup of launch history. Sums and records, never per-session rows — the raw window is
/// capped at 100 (<see cref="SessionHistoryStore.MaxRows"/>) and discards after roughly twenty
/// days, so anything phrased as "all time" has to be accumulated as it happens rather than
/// derived later. See docs/superpowers/specs/2026-08-22-rororo-session-stats-design.md §0.
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

    /// <summary>
    /// Sum of every account's uptime. Excludes sessions that never recorded an end — those are
    /// counted separately in <see cref="SessionsMissingAnEnd"/> and surfaced, because silently
    /// dropping them undercounts and silently attributing them a duration invents data (§5).
    /// </summary>
    public TimeSpan TotalUptime =>
        Accounts.Values.Aggregate(TimeSpan.Zero, (acc, a) => acc + a.Uptime);
}

/// <summary>
/// Per-alt landing streak included (v1.23 addendum): the chain marks presence-confirmed landings,
/// never launches — an alt that launched into a privacy wall and sat at home did not log in
/// anywhere. Chains are independent per alt; that independence is the feature.
/// </summary>
public sealed record AccountStat(
    int Launches, TimeSpan Uptime, DateTimeOffset? LastSeenUtc,
    int StreakDays = 0, int LongestStreakDays = 0);

/// <summary>Name is "last known" and purely for display — never a key (§2.1).</summary>
public sealed record GameStat(string? LastKnownName, int Launches, TimeSpan Uptime);

public sealed record DayStat(int Launches, TimeSpan Uptime);

public sealed record MonthStat(int Launches, TimeSpan Uptime, int DaysPlayed);

/// <summary>
/// Maintained incrementally, never derived from <see cref="SessionStats.Days"/> — day buckets fold
/// into months past a threshold and a streak spanning that boundary would be unrecoverable (§2.2).
/// </summary>
public sealed record StreakRecord(
    int CurrentDays, int LongestDays, string? CurrentStartDay, string? LastPlayedDay);

public sealed record LongestSession(
    TimeSpan Duration, Guid AccountId, long? PlaceId, DateTimeOffset WhenUtc);

/// <summary>
/// Day bucket keys, and the only place "is this the next day" is decided.
///
/// <para>This exists as a seam because the alternative — subtracting two timestamps and comparing
/// to 24 hours — is wrong twice a year. A spring-forward local day is 23 hours and a fall-back one
/// is 25, so an elapsed-time check silently breaks a streak in March and silently extends one in
/// November. Comparing calendar dates cannot have that bug.</para>
/// </summary>
public static class DayKey
{
    /// <summary>
    /// Local calendar day. Local because "days in a row I played" is a human notion and a UTC
    /// boundary would break a streak at 7pm local (§6).
    /// </summary>
    public static string For(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd");

    public static string MonthOf(string dayKey) => dayKey[..7];

    /// <summary>
    /// Whether <paramref name="next"/> is the calendar day immediately after
    /// <paramref name="previous"/>. Operates on date strings, so its answer does not depend on the
    /// machine's time zone — which matters because CI runs UTC and developers do not.
    /// </summary>
    public static bool IsNextDay(string previous, string next)
        => DateOnly.TryParse(previous, out var p)
        && DateOnly.TryParse(next, out var n)
        && p.AddDays(1) == n;
}

/// <summary>What the store is told. One case per thing that can move a number.</summary>
public abstract record StatsEvent
{
    public sealed record LaunchRecorded(
        Guid AccountId, long? PlaceId, string? GameName, DateTimeOffset AtUtc) : StatsEvent;

    public sealed record SessionEnded(
        Guid AccountId, long? PlaceId, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc) : StatsEvent;

    public sealed record ConcurrencyObserved(int Count) : StatsEvent;

    public sealed record SessionMissingEnd() : StatsEvent;

    /// <summary>Latches the one-time seed so it never runs twice (§4).</summary>
    public sealed record BackfillCompleted() : StatsEvent;

    /// <summary>
    /// Presence confirmed this alt in a game. Fired from the presence poll, deduped per local
    /// day by the emitter — the heartbeat reports InGame every ~25s and a day is a day. Marks
    /// the alt's landing streak only; it does not invent a launch.
    /// </summary>
    public sealed record AccountLanded(Guid AccountId, DateTimeOffset AtUtc) : StatsEvent;
}
