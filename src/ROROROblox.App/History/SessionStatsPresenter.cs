using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.History;

/// <summary>
/// Turns the rollup into display rows. Pure — no WPF types — so the formatting rules and,
/// more importantly, the name resolution are pinned by tests rather than left to the view.
///
/// <para>NAMES. The store holds account ids only (spec §2.1); this is where they become names,
/// via the roster's <see cref="AccountSummary.RenderName"/> — which is both rename-aware (the
/// LocalName half F-A's history writer gets wrong) and streamer-mode-aware (the identity
/// provider substitutes fakes when active, so this page never leaks the real roster on stream).
/// An account no longer in the roster degrades to a short id rather than a blank: stats survive
/// account deletion, and an unattributable row reads as a bug.</para>
/// </summary>
internal static class SessionStatsPresenter
{
    internal sealed record LeaderboardRow(string Name, int Launches, TimeSpan Uptime, int StreakDays)
    {
        /// <summary>
        /// Empty at zero, deliberately: per-alt streaks start accruing at install (history rows
        /// record where a launch was AIMED, not whether it landed, so backfill cannot seed a
        /// truthful chain), and eight rows of "0d streak" on day one reads as broken rather
        /// than new.
        /// </summary>
        public string StreakSuffix => StreakDays > 0 ? $" · {StreakDays}d streak" : string.Empty;
    }

    internal sealed record StatsView(
        string TotalUptime,
        int PeakConcurrentAlts,
        string MostPlayedGame,
        string LongestSession,
        int StreakDays,
        int LongestStreakDays,
        IReadOnlyList<LeaderboardRow> Leaderboard,
        string IntegrityNote,
        bool HasAnything);

    internal static StatsView Build(SessionStats stats, IReadOnlyList<AccountSummary> roster)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(roster);

        var leaderboard = stats.Accounts
            .OrderByDescending(kv => kv.Value.Uptime)
            .ThenByDescending(kv => kv.Value.Launches)
            .Select(kv => new LeaderboardRow(
                ResolveName(kv.Key, roster), kv.Value.Launches, kv.Value.Uptime, kv.Value.StreakDays))
            .ToList();

        // Uptime, not launch count: a game opened and quit five times is not more played than
        // one three-hour session (spec §6).
        var mostPlayed = stats.Games
            .OrderByDescending(kv => kv.Value.Uptime)
            .Select(kv => kv.Value.LastKnownName ?? $"place {kv.Key}")
            .FirstOrDefault() ?? "—";

        return new StatsView(
            TotalUptime: FormatUptime(stats.TotalUptime),
            PeakConcurrentAlts: stats.PeakConcurrentAlts,
            MostPlayedGame: mostPlayed,
            LongestSession: stats.Longest is { } l ? FormatUptime(l.Duration) : "—",
            StreakDays: stats.Streak.CurrentDays,
            LongestStreakDays: stats.Streak.LongestDays,
            Leaderboard: leaderboard,
            // Counted out loud rather than silently dropped or silently guessed (spec §5).
            IntegrityNote: stats.SessionsMissingAnEnd > 0
                ? $"{stats.SessionsMissingAnEnd} session(s) didn't record an end and aren't counted in uptime."
                : string.Empty,
            HasAnything: stats.Accounts.Count > 0 || stats.PeakConcurrentAlts > 0);
    }

    private static string ResolveName(Guid id, IReadOnlyList<AccountSummary> roster)
        => roster.FirstOrDefault(a => a.Id == id)?.RenderName
           ?? id.ToString("N")[..8];

    /// <summary>
    /// Hours-first, deliberately: "49h 0m" rather than "2d 1h". Total uptime across eight alts is
    /// the bragging number, and days-folding hides the grind it exists to show.
    /// </summary>
    internal static string FormatUptime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var hours = (int)t.TotalHours;
        return hours == 0 ? $"{t.Minutes}m" : $"{hours}h {t.Minutes}m";
    }
}
