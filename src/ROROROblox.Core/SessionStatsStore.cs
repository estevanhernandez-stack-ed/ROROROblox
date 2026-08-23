using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// JSON-file-backed rollup. Same shape pattern as <see cref="SessionHistoryStore"/> /
/// <see cref="FavoriteGameStore"/> — semaphore-gated, corrupt file falls back to empty.
///
/// <para>Unlike the history store this one is never capped by row count, because it holds sums
/// rather than rows. The one collection that would grow forever is the per-day bucket, and that
/// folds into months past <see cref="RawDayLimit"/> (§2.2) — otherwise this file would recreate
/// the exact objection the design raised against simply raising the history cap: a growing
/// read-modify-write on every session end.</para>
/// </summary>
public sealed class SessionStatsStore : ISessionStatsStore, IDisposable
{
    /// <summary>
    /// How many raw day buckets survive before folding into months. Four hundred covers thirteen
    /// months, which is what "this year" and any year-over-year comparison need.
    /// </summary>
    public const int RawDayLimit = 400;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private bool _disposed;

    public SessionStatsStore() : this(DefaultPath()) { }

    public SessionStatsStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must not be empty.", nameof(filePath));
        }
        _filePath = filePath;
    }

    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ROROROblox", "session-stats.json");
    }

    public async Task<SessionStats> ReadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return (await LoadAsync().ConfigureAwait(false)).ToStats();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyAsync(StatsEvent statsEvent)
    {
        ArgumentNullException.ThrowIfNull(statsEvent);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            Apply(blob, statsEvent);
            Fold(blob);
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(new Blob()).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // =====================================================================
    // Folding one event in.
    // =====================================================================

    private static void Apply(Blob b, StatsEvent e)
    {
        switch (e)
        {
            case StatsEvent.LaunchRecorded l:
            {
                var acc = b.Accounts.TryGetValue(l.AccountId, out var a) ? a : new AccountRow();
                acc.Launches++;
                acc.LastSeenUtc = l.AtUtc;
                b.Accounts[l.AccountId] = acc;

                if (l.PlaceId is { } place)
                {
                    var g = b.Games.TryGetValue(place, out var gv) ? gv : new GameRow();
                    g.Launches++;
                    if (!string.IsNullOrWhiteSpace(l.GameName)) g.LastKnownName = l.GameName;
                    b.Games[place] = g;
                }

                var key = DayKey.For(l.AtUtc);
                var day = b.Days.TryGetValue(key, out var dv) ? dv : new DayRow();
                day.Launches++;
                b.Days[key] = day;

                AdvanceStreak(b, key);
                break;
            }

            case StatsEvent.SessionEnded s:
            {
                // Clamp BEFORE touching any total. A clock change mid-session must not be able to
                // subtract from a lifetime number (§5).
                var d = s.EndedUtc - s.StartedUtc;
                if (d < TimeSpan.Zero) d = TimeSpan.Zero;

                var acc = b.Accounts.TryGetValue(s.AccountId, out var a) ? a : new AccountRow();
                acc.UptimeTicks += d.Ticks;
                acc.LastSeenUtc = s.EndedUtc;
                b.Accounts[s.AccountId] = acc;

                if (s.PlaceId is { } place)
                {
                    var g = b.Games.TryGetValue(place, out var gv) ? gv : new GameRow();
                    g.UptimeTicks += d.Ticks;
                    b.Games[place] = g;
                }

                var key = DayKey.For(s.StartedUtc);
                var day = b.Days.TryGetValue(key, out var dv) ? dv : new DayRow();
                day.UptimeTicks += d.Ticks;
                b.Days[key] = day;

                if (d.Ticks > b.LongestTicks)
                {
                    b.LongestTicks = d.Ticks;
                    b.LongestAccountId = s.AccountId;
                    b.LongestPlaceId = s.PlaceId;
                    b.LongestWhenUtc = s.StartedUtc;
                }
                break;
            }

            case StatsEvent.ConcurrencyObserved c:
                // Only ever upward. A detach must not lower the record.
                if (c.Count > b.PeakConcurrentAlts) b.PeakConcurrentAlts = c.Count;
                break;

            case StatsEvent.SessionMissingEnd:
                b.SessionsMissingAnEnd++;
                break;
        }
    }

    /// <summary>
    /// Streaks are maintained here and never derived from <see cref="Blob.Days"/>, because days
    /// fold into months and a streak spanning that boundary would be unrecoverable (§2.2).
    /// Consecutiveness goes through <see cref="DayKey.IsNextDay"/> — never elapsed hours.
    /// </summary>
    private static void AdvanceStreak(Blob b, string dayKey)
    {
        if (b.LastPlayedDay == dayKey) return;   // same day again — not a new day

        if (b.LastPlayedDay is not null && DayKey.IsNextDay(b.LastPlayedDay, dayKey))
        {
            b.CurrentStreakDays++;
        }
        else
        {
            b.CurrentStreakDays = 1;
            b.CurrentStreakStartDay = dayKey;
        }

        b.LastPlayedDay = dayKey;
        if (b.CurrentStreakDays > b.LongestStreakDays) b.LongestStreakDays = b.CurrentStreakDays;
    }

    /// <summary>
    /// Collapse the oldest day buckets into months once past <see cref="RawDayLimit"/>. Moves
    /// detail, never totals — accounts, games, records, and the streak are deliberately untouched.
    /// </summary>
    private static void Fold(Blob b)
    {
        if (b.Days.Count <= RawDayLimit) return;

        // Ordinal sort is safe because day keys are zero-padded ISO (pinned by a model test).
        var oldest = b.Days.Keys.OrderBy(k => k, StringComparer.Ordinal)
                                .Take(b.Days.Count - RawDayLimit)
                                .ToList();

        foreach (var key in oldest)
        {
            var day = b.Days[key];
            var monthKey = DayKey.MonthOf(key);
            var m = b.Months.TryGetValue(monthKey, out var mv) ? mv : new MonthRow();
            m.Launches += day.Launches;
            m.UptimeTicks += day.UptimeTicks;
            m.DaysPlayed++;
            b.Months[monthKey] = m;
            b.Days.Remove(key);
        }
    }

    // =====================================================================
    // Persistence. Corrupt file falls back to empty, same as the history store.
    // =====================================================================

    private async Task<Blob> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath)) return new Blob();
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return new Blob();
            return JsonSerializer.Deserialize<Blob>(json, JsonOptions) ?? new Blob();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Stats are the least important thing this app does — a bad file must never block a
            // launch, so start fresh rather than throwing (§5).
            return new Blob();
        }
    }

    private async Task SaveAsync(Blob blob)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(blob, JsonOptions)).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    // =====================================================================
    // On-disk shape. Mutable and tick-based so folding is cheap; the public
    // read model (SessionStats) is the immutable projection of this.
    // =====================================================================

    private sealed class Blob
    {
        public int Version { get; set; } = 1;
        public Dictionary<Guid, AccountRow> Accounts { get; set; } = new();
        public Dictionary<long, GameRow> Games { get; set; } = new();
        public Dictionary<string, DayRow> Days { get; set; } = new();
        public Dictionary<string, MonthRow> Months { get; set; } = new();
        public int PeakConcurrentAlts { get; set; }
        public long LongestTicks { get; set; }
        public Guid? LongestAccountId { get; set; }
        public long? LongestPlaceId { get; set; }
        public DateTimeOffset? LongestWhenUtc { get; set; }
        public int CurrentStreakDays { get; set; }
        public int LongestStreakDays { get; set; }
        public string? CurrentStreakStartDay { get; set; }
        public string? LastPlayedDay { get; set; }
        public int SessionsMissingAnEnd { get; set; }
        public bool Backfilled { get; set; }

        public SessionStats ToStats() => new(
            Accounts.ToDictionary(
                kv => kv.Key,
                kv => new AccountStat(kv.Value.Launches, TimeSpan.FromTicks(kv.Value.UptimeTicks), kv.Value.LastSeenUtc)),
            Games.ToDictionary(
                kv => kv.Key,
                kv => new GameStat(kv.Value.LastKnownName, kv.Value.Launches, TimeSpan.FromTicks(kv.Value.UptimeTicks))),
            Days.ToDictionary(
                kv => kv.Key,
                kv => new DayStat(kv.Value.Launches, TimeSpan.FromTicks(kv.Value.UptimeTicks))),
            Months.ToDictionary(
                kv => kv.Key,
                kv => new MonthStat(kv.Value.Launches, TimeSpan.FromTicks(kv.Value.UptimeTicks), kv.Value.DaysPlayed)),
            PeakConcurrentAlts,
            LongestTicks > 0 && LongestAccountId is { } lid
                ? new LongestSession(TimeSpan.FromTicks(LongestTicks), lid, LongestPlaceId, LongestWhenUtc ?? default)
                : null,
            new StreakRecord(CurrentStreakDays, LongestStreakDays, CurrentStreakStartDay, LastPlayedDay),
            SessionsMissingAnEnd,
            Backfilled);
    }

    private sealed class AccountRow
    {
        public int Launches { get; set; }
        public long UptimeTicks { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
    }

    private sealed class GameRow
    {
        public string? LastKnownName { get; set; }
        public int Launches { get; set; }
        public long UptimeTicks { get; set; }
    }

    private sealed class DayRow
    {
        public int Launches { get; set; }
        public long UptimeTicks { get; set; }
    }

    private sealed class MonthRow
    {
        public int Launches { get; set; }
        public long UptimeTicks { get; set; }
        public int DaysPlayed { get; set; }
    }
}
