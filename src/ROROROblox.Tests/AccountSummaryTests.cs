using System.ComponentModel;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Status reconciliation tests for <see cref="AccountSummary"/> — the v1.5.0 "augment, don't
/// replace" rule: a row is ACTIVE if <c>InGame OR IsRunning</c>, and shows "Closed" only when
/// BOTH are false. The headline is the ghost fix (case 1): presence says InGame while the local
/// pid was lost — the row must show the game, never "Closed".
/// Spec: docs/superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md §"Components > 2".
/// </summary>
public class AccountSummaryTests
{
    private static AccountSummary NewSummary()
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestAlt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: 12345L);
        return new AccountSummary(account);
    }

    // === 1. Headline: the ghost fix ===

    [Fact]
    public void Ghost_PresenceInGame_PidLost_ShowsGame_NotClosed_Green()
    {
        var s = NewSummary();
        s.IsRunning = false;                 // local pid was lost (the ghost)
        s.CurrentGameName = "Pet Sim 99";
        s.PresenceState = UserPresenceType.InGame;

        Assert.StartsWith("In Pet Sim 99", s.SecondaryStatusText);
        Assert.DoesNotContain("Closed", s.SecondaryStatusText);
        Assert.Equal("green", s.StatusDot);
    }

    // === 2. InGame wins over a stale LastClosedAtUtc ===

    [Fact]
    public void InGame_WithStaleClose_ShowsGame_Green()
    {
        var s = NewSummary();
        s.IsRunning = false;
        s.LastClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5); // stale close stamp
        s.CurrentGameName = "Adopt Me";
        s.PresenceState = UserPresenceType.InGame;

        Assert.StartsWith("In Adopt Me", s.SecondaryStatusText);
        Assert.DoesNotContain("Closed", s.SecondaryStatusText);
        Assert.Equal("green", s.StatusDot);
    }

    // === 3. Running, not in game, fresh launch → Connecting ===

    [Fact]
    public void Running_NotInGame_FreshLaunch_ShowsConnecting_Green()
    {
        var s = NewSummary();
        s.IsRunning = true;
        s.RunningSinceUtc = DateTimeOffset.UtcNow; // within the last 60s

        Assert.Equal("Connecting…", s.SecondaryStatusText);
        Assert.Equal("green", s.StatusDot);
    }

    // === 4. Running, not in game, older launch → Running ===

    [Fact]
    public void Running_NotInGame_OlderLaunch_ShowsRunning_Green()
    {
        var s = NewSummary();
        s.IsRunning = true;
        s.RunningSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-2); // > 60s ago

        Assert.Equal("Running", s.SecondaryStatusText);
        Assert.Equal("green", s.StatusDot);
    }

    // === 5. Not in game, not running, recently closed → Closed, grey ===

    [Fact]
    public void NotInGame_NotRunning_RecentlyClosed_ShowsClosed_Grey()
    {
        var s = NewSummary();
        s.IsRunning = false;
        s.LastClosedAtUtc = DateTimeOffset.UtcNow;

        Assert.StartsWith("Closed", s.SecondaryStatusText);
        Assert.Equal("grey", s.StatusDot);
    }

    // === 6. SessionExpired wins over InGame; dot is yellow ===

    [Fact]
    public void SessionExpired_BeatsInGame_ShowsExpired_Yellow()
    {
        var s = NewSummary();
        s.PresenceState = UserPresenceType.InGame; // even though in-game...
        s.CurrentGameName = "Pet Sim 99";
        s.SessionExpired = true;                   // ...expiry wins

        Assert.Equal("Session expired", s.SecondaryStatusText);
        Assert.Equal("yellow", s.StatusDot);
    }

    // === 7. Ready / Last launched fallbacks ===

    [Fact]
    public void AllFalse_WithLastLaunched_ShowsLastLaunched()
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestAlt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: DateTimeOffset.UtcNow.AddHours(-3),
            RobloxUserId: 12345L);
        var s = new AccountSummary(account);

        Assert.StartsWith("Last launched", s.SecondaryStatusText);
        Assert.Equal("grey", s.StatusDot);
    }

    [Fact]
    public void AllFalse_NothingSet_ShowsReady()
    {
        var s = NewSummary();

        Assert.Equal("Ready", s.SecondaryStatusText);
        Assert.Equal("grey", s.StatusDot);
    }

    // === 8. PropertyChanged wiring ===

    [Fact]
    public void SettingPresenceStateToInGame_RaisesSecondaryStatusTextChange()
    {
        var s = NewSummary();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.PresenceState = UserPresenceType.InGame;

        Assert.Contains(nameof(AccountSummary.SecondaryStatusText), raised);
        Assert.Contains(nameof(AccountSummary.StatusDot), raised);
        Assert.Contains(nameof(AccountSummary.InGame), raised);
    }

    [Fact]
    public void SettingCurrentGameName_RaisesSecondaryStatusTextChange()
    {
        var s = NewSummary();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.CurrentGameName = "Pet Sim 99";

        Assert.Contains(nameof(AccountSummary.SecondaryStatusText), raised);
    }

    // === In-game text fallbacks (precedence rule 2 details) ===

    [Fact]
    public void InGame_WithSinceTime_AppendsRelativeAge()
    {
        var s = NewSummary();
        s.CurrentGameName = "Pet Sim 99";
        s.InGameSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        s.PresenceState = UserPresenceType.InGame;

        // "In Pet Sim 99 · 5 min" shape — middle dot separator + a non-empty age.
        Assert.StartsWith("In Pet Sim 99 · ", s.SecondaryStatusText);
        Assert.Contains("min", s.SecondaryStatusText);
    }

    [Fact]
    public void InGame_NoSinceTime_ShowsGameNameOnly()
    {
        var s = NewSummary();
        s.CurrentGameName = "Pet Sim 99";
        s.PresenceState = UserPresenceType.InGame; // InGameSinceUtc stays null

        Assert.Equal("In Pet Sim 99", s.SecondaryStatusText);
    }

    [Fact]
    public void InGame_NoGameName_ShowsInAGame()
    {
        var s = NewSummary();
        s.PresenceState = UserPresenceType.InGame; // CurrentGameName stays null

        Assert.Equal("In a game", s.SecondaryStatusText);
    }

    // === Precedence: launch error (StatusText) only surfaces when not active ===

    [Fact]
    public void StatusText_LaunchError_SurfacesWhenIdle()
    {
        var s = NewSummary();
        s.StatusText = "Launch failed: ticket rejected";

        Assert.Equal("Launch failed: ticket rejected", s.SecondaryStatusText);
    }

    [Fact]
    public void InGame_DefaultPresence_IsOffline_NotInGame()
    {
        var s = NewSummary();

        Assert.Equal(UserPresenceType.Offline, s.PresenceState);
        Assert.False(s.InGame);
    }
}
