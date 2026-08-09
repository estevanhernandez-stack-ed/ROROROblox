using ROROROblox.App.About;

namespace ROROROblox.Tests;

/// <summary>
/// The first-run tour is the one surface documenting six unlabelled account-row affordances.
/// <para>
/// Before F-001, MainWindow.OnLoaded called MarkShown() BEFORE checking the account count, so an
/// upgrading user with accounts burned the sentinel and could never see the tour — not on that
/// launch, and not on any later one. The middle case below is that bug.
/// </para>
/// </summary>
public class WelcomeSentinelTests
{
    [Fact]
    public void FirstRunWithNoAccounts_ShowsTheTour()
    {
        Assert.True(WelcomeWindow.ShouldShowOnStartup(isFirstRun: true, accountCount: 0));
    }

    [Fact]
    public void FirstRunWithAccounts_DoesNotShowAndMustNotBurnTheSentinel()
    {
        // The assertion is only half the guard: the caller must not call MarkShown() when this is
        // false. MainWindow.OnLoaded marks INSIDE this branch for exactly that reason.
        Assert.False(WelcomeWindow.ShouldShowOnStartup(isFirstRun: true, accountCount: 4));
    }

    [Fact]
    public void NotFirstRun_NeverShows()
    {
        Assert.False(WelcomeWindow.ShouldShowOnStartup(isFirstRun: false, accountCount: 0));
        Assert.False(WelcomeWindow.ShouldShowOnStartup(isFirstRun: false, accountCount: 9));
    }
}
