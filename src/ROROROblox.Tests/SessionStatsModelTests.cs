using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The read model, and the seam that keeps daylight saving from silently editing a streak.
/// See docs/superpowers/specs/2026-08-22-rororo-session-stats-design.md §2.2 and §6.
/// </summary>
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

    [Theory]
    // Spring forward 2026-03-08 (a 23-hour local day) and fall back 2026-11-01 (25 hours).
    // Both are consecutive calendar days and must chain a streak. An elapsed-hours check gets
    // both of these wrong, in opposite directions.
    [InlineData("2026-03-07", "2026-03-08", true)]
    [InlineData("2026-11-01", "2026-11-02", true)]
    [InlineData("2026-12-31", "2027-01-01", true)]   // year boundary
    [InlineData("2026-02-28", "2026-03-01", true)]   // non-leap month boundary
    [InlineData("2024-02-28", "2024-02-29", true)]   // leap day exists in a leap year
    [InlineData("2026-03-07", "2026-03-09", false)]  // a real gap
    [InlineData("2026-03-08", "2026-03-08", false)]  // same day is not a new day
    [InlineData("2026-03-09", "2026-03-08", false)]  // backwards is not forwards
    public void NextDayIsACalendarQuestionNotAnElapsedTimeOne(string a, string b, bool expected)
        => Assert.Equal(expected, DayKey.IsNextDay(a, b));

    [Fact]
    public void AnUnparseableDayKeyIsFalseRatherThanAThrow()
        => Assert.False(DayKey.IsNextDay("not-a-date", "2026-03-08"));

    [Fact]
    public void MonthOfTakesTheYearAndMonthOfADayKey()
        => Assert.Equal("2026-03", DayKey.MonthOf("2026-03-08"));

    [Fact]
    public void DayKeysAreSortableAsStrings()
    {
        // The fold relies on ordering day keys lexically to find the oldest. That only works
        // because the format is zero-padded ISO — worth pinning, since a "M/d/yyyy" format
        // would sort 2026-10-01 before 2026-2-01 and fold the wrong days.
        var keys = new[] { "2026-10-01", "2026-02-01", "2026-01-15" };
        Array.Sort(keys, StringComparer.Ordinal);

        Assert.Equal(new[] { "2026-01-15", "2026-02-01", "2026-10-01" }, keys);
    }
}
