using System.Globalization;
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// Final-fix wave, commit B: the page's "Last checked" line used to always read as an ordinary
/// check, even for a fresh install behind a network that blocks GitHub -- "No known Roblox issues
/// right now. Last checked at 12:47 PM." looked exactly like a verified empty list. This is the one
/// pure function that decides what the line says and whether the page may claim an all-clear it has
/// actually verified. Every assertion here is built from <see cref="Loc.Get"/>/<see cref="Loc.Format"/>,
/// never a literal, so a wording change to the resx cannot silently desync these tests from it.
/// <para>
/// Every "today"/"yesterday" instant here is built from the LOCAL calendar day, not a hardcoded UTC
/// instant converted with <c>ToLocalTime()</c> — the latter can silently land on a different local
/// calendar day depending on the machine's time zone, which is exactly the kind of boundary this
/// feature exists to get right.
/// </para>
/// </summary>
public class KnownIssuesStatusLineTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>A DateTimeOffset for today at the given local hour, with the correct local offset already applied.</summary>
    private static DateTimeOffset LocalToday(double hour) =>
        new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today)).AddHours(hour);

    /// <summary>A DateTimeOffset for yesterday at the given local hour, with the correct local offset already applied.</summary>
    private static DateTimeOffset LocalYesterday(double hour) =>
        new DateTimeOffset(DateTime.Today.AddDays(-1), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddDays(-1))).AddHours(hour);

    [Fact]
    public void NoCheckHasFinishedYet_AndNothingIsVerified_SaysCheckingAndWithholdsAllClear()
    {
        var now = LocalToday(12);

        var line = KnownIssuesStatusLine.For(KnownIssuesSource.None, 0, null, null, now, Culture);

        Assert.Equal(Loc.Get("KnownIssuesPage_Checking"), line.Text);
        Assert.False(line.ShowAllClear);
    }

    /// <summary>A saved copy that is itself empty is a real, verified empty list.</summary>
    [Fact]
    public void NoCheckHasFinishedYet_ButASavedEmptyCopyLoaded_StillChecking_ButAllClearIsReal()
    {
        var now = LocalToday(12);

        var line = KnownIssuesStatusLine.For(KnownIssuesSource.SavedCopy, 0, null, null, now, Culture);

        Assert.Equal(Loc.Get("KnownIssuesPage_Checking"), line.Text);
        Assert.True(line.ShowAllClear);
    }

    [Fact]
    public void ALiveCheckSucceeded_SaysWhenAndAllowsAllClear()
    {
        var now = LocalToday(12);
        var checkedAt = LocalToday(10.5);

        var line = KnownIssuesStatusLine.For(
            KnownIssuesSource.Release, 0, checkedAt, KnownIssuesRefreshKind.Updated, now, Culture);

        Assert.Equal(Loc.Format("KnownIssuesPage_LastChecked", checkedAt.ToString("t", Culture)), line.Text);
        Assert.True(line.ShowAllClear);
    }

    /// <summary>The fresh-install-offline case: never had a verified list, and the one attempt failed.</summary>
    [Fact]
    public void TheLastAttemptFailed_AndNothingWasEverVerified_SaysNeverReached_AndWithholdsAllClear()
    {
        var now = LocalToday(12);
        var checkedAt = now;

        var line = KnownIssuesStatusLine.For(
            KnownIssuesSource.None, 0, checkedAt, KnownIssuesRefreshKind.NetworkFailed, now, Culture);

        Assert.Equal(Loc.Get("KnownIssuesPage_NeverReached"), line.Text);
        Assert.False(line.ShowAllClear);
    }

    [Fact]
    public void TheLastAttemptFailed_ButAnOlderVerifiedListExists_SaysCheckFailed_AndStillAllowsAllClear()
    {
        var now = LocalToday(12);
        var checkedAt = now;

        var line = KnownIssuesStatusLine.For(
            KnownIssuesSource.SavedCopy, 0, checkedAt, KnownIssuesRefreshKind.SignatureRejected, now, Culture);

        Assert.Equal(Loc.Format("KnownIssuesPage_CheckFailed", checkedAt.ToString("t", Culture)), line.Text);
        Assert.True(line.ShowAllClear);
    }

    [Fact]
    public void AStampFromToday_ShowsTimeOnly()
    {
        var now = LocalToday(12);
        var checkedAt = LocalToday(9.25);

        var line = KnownIssuesStatusLine.For(
            KnownIssuesSource.Release, 0, checkedAt, KnownIssuesRefreshKind.Updated, now, Culture);

        var expected = Loc.Format("KnownIssuesPage_LastChecked", checkedAt.ToString("t", Culture));
        Assert.Equal(expected, line.Text);
        Assert.DoesNotContain(checkedAt.ToString("d", Culture), line.Text);
    }

    /// <summary>A stamp from yesterday must carry a date -- otherwise it reads as today's after sleep.</summary>
    [Fact]
    public void AStampFromYesterday_IncludesTheDate()
    {
        var now = LocalToday(0.5);
        var checkedAt = LocalYesterday(23);

        var line = KnownIssuesStatusLine.For(
            KnownIssuesSource.Release, 0, checkedAt, KnownIssuesRefreshKind.Updated, now, Culture);

        var expected = Loc.Format("KnownIssuesPage_LastChecked", checkedAt.ToString("g", Culture));
        Assert.Equal(expected, line.Text);
    }
}
