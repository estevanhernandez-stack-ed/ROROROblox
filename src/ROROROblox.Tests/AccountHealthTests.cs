using System.Text.RegularExpressions;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 2 (spec 1.1, 1.2, 1.3): the health vocabulary. The ladder is first-match-wins; the
/// four non-Ok states differ by word and glyph with colour removed; NeedsVerification has exactly
/// one setter path; a 403 never marks an account for follow.
/// </summary>
public class AccountHealthTests
{
    private static AccountSummary Row(bool marked = false)
    {
        var account = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestAlt",
            AvatarUrl: "https://example.com/avatar.png",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            RobloxUserId: 12345L)
        { JoinViaFriend = marked };
        return new AccountSummary(account);
    }

    // ---- the ladder -------------------------------------------------------------------------

    [Fact]
    public void Health_IsOk_ForACleanRow() => Assert.Equal(AccountHealth.Ok, Row().Health);

    [Fact]
    public void Health_FirstMatchWins_InSpecOrder()
    {
        var s = Row(marked: true);
        Assert.Equal(AccountHealth.FollowMarked, s.Health);

        s.SessionLimited = true;
        Assert.Equal(AccountHealth.Limited, s.Health);

        s.SessionNeedsVerification = true;
        Assert.Equal(AccountHealth.NeedsVerification, s.Health);

        s.SessionExpired = true;
        Assert.Equal(AccountHealth.Expired, s.Health);

        s.SessionExpired = false;
        Assert.Equal(AccountHealth.NeedsVerification, s.Health);
        s.SessionNeedsVerification = false;
        Assert.Equal(AccountHealth.Limited, s.Health);
        s.SessionLimited = false;
        Assert.Equal(AccountHealth.FollowMarked, s.Health);
    }

    // ---- flatline: the four differ with colour removed --------------------------------------

    // AccountHealth is internal, so the theory carries its name (xUnit needs public signatures).
    public static TheoryData<string> NonOkStates() => new()
    {
        nameof(AccountHealth.Expired),
        nameof(AccountHealth.NeedsVerification),
        nameof(AccountHealth.Limited),
        nameof(AccountHealth.FollowMarked),
    };

    [Theory]
    [MemberData(nameof(NonOkStates))]
    public void EachNonOkState_HasAWordAndAGlyphAndAnAction(string state)
    {
        var health = Enum.Parse<AccountHealth>(state);
        Assert.False(string.IsNullOrWhiteSpace(AccountHealthInfo.Word(health)));
        Assert.False(string.IsNullOrWhiteSpace(AccountHealthInfo.Glyph(health)));
        Assert.False(string.IsNullOrWhiteSpace(AccountHealthInfo.Action(health)));
        Assert.Equal($"{AccountHealthInfo.Word(health)}, {AccountHealthInfo.Action(health)}",
            AccountHealthInfo.BadgeName(health));
    }

    [Fact]
    public void TheFourNonOkStates_HaveDistinctGlyphWordPairs()
    {
        var pairs = new[]
        {
            AccountHealth.Expired, AccountHealth.NeedsVerification, AccountHealth.Limited, AccountHealth.FollowMarked,
        }.Select(h => (Glyph: AccountHealthInfo.Glyph(h), Word: AccountHealthInfo.Word(h))).ToList();

        Assert.Equal(4, pairs.Distinct().Count());
        // Stronger than the pair: neither carrier repeats, so a glyph alone tells them apart and so
        // does a word alone. "They never share a glyph" (PRD).
        Assert.Equal(4, pairs.Select(p => p.Glyph).Distinct().Count());
        Assert.Equal(4, pairs.Select(p => p.Word).Distinct().Count());
    }

    // ---- the secondary line and the dot -----------------------------------------------------

    [Fact]
    public void MarkedAccount_InAGame_ReadsInGame_NotTheMark()
    {
        var s = Row(marked: true);
        s.PresenceState = UserPresenceType.InGame;
        s.CurrentGameName = "Pet Sim 99";
        s.InGameSinceUtc = DateTimeOffset.UtcNow.AddHours(-2);

        Assert.StartsWith("In Pet Sim 99 · ", s.SecondaryStatusText);
        Assert.DoesNotContain("Joins by following", s.SecondaryStatusText);
        Assert.Equal("green", s.StatusDot);
        Assert.True(s.ShowFollowMarkBadge); // the permanent name-line badge carries the mark
    }

    [Fact]
    public void MarkedAccount_AtRest_ReadsJoinsByFollowing_Ready()
    {
        var s = Row(marked: true);

        Assert.Equal("Joins by following · Ready", s.SecondaryStatusText);
    }

    [Fact]
    public void UnmarkedAccount_AtRest_StillReadsReady() => Assert.Equal("Ready", Row().SecondaryStatusText);

    [Fact]
    public void NeedsVerification_BeatsPresence_AndReadsItsAction()
    {
        var s = Row();
        s.PresenceState = UserPresenceType.InGame;
        s.CurrentGameName = "Pet Sim 99";
        s.SessionNeedsVerification = true;

        Assert.StartsWith("Needs verification", s.SecondaryStatusText);
        Assert.Equal("verify", s.StatusDot);
    }

    [Fact]
    public void Expired_StillBeatsNeedsVerification_OnTheLine()
    {
        var s = Row();
        s.SessionNeedsVerification = true;
        s.SessionExpired = true;

        Assert.Equal("Session expired", s.SecondaryStatusText);
        Assert.Equal("yellow", s.StatusDot);
    }

    [Fact]
    public void Badges_FollowTheirStates()
    {
        var s = Row(marked: true);
        Assert.False(s.ShowHealthBadge);  // marked alone has its own name-line badge
        Assert.True(s.ShowFollowMarkBadge);

        s.SessionLimited = true;
        Assert.True(s.ShowHealthBadge);
        Assert.True(s.ShowFollowMarkBadge); // coexist
        Assert.Equal("limited", s.HealthKind);
        Assert.Equal("Limited by Roblox, Re-authenticate", s.HealthBadgeName);

        s.SessionNeedsVerification = true;
        Assert.Equal("verify", s.HealthKind);
        Assert.Equal("Needs verification, Open Roblox for this account", s.HealthBadgeName);

        Assert.Equal("Joins by following, Toggle the mark", s.FollowMarkBadgeName);
    }

    // ---- the batch banner (spec 1.3) --------------------------------------------------------

    [Fact]
    public void LaunchAll_TwoMarkedAccountsAndAMain_SaysTwoWillFollowTheMain()
    {
        var main = Row();
        main.IsMain = true;
        var a = Row(marked: true);
        var b = Row(marked: true);
        var plan = SquadLaunchPlan.Build([main, a, b]);

        var result = LaunchEligibility.Compute([Cand(), Cand(), Cand()]);
        var banner = result.PartialBanner(dispatched: 3, verb: "Launch all finished", followMarked: plan.FollowMarkedCount);

        Assert.Equal(2, plan.FollowMarkedCount);
        Assert.Contains("2 will follow the main", banner);
    }

    [Fact]
    public void LaunchAll_NoMarkedAccounts_SaysNothingAboutFollowing()
    {
        var banner = LaunchEligibility.Compute([Cand()]).PartialBanner(1, "Launch all finished", followMarked: 0);

        Assert.DoesNotContain("follow", banner);
    }

    [Fact]
    public void TheMainIsNeverCountedAsFollowing_EvenWhenMarked()
    {
        var main = Row(marked: true);
        main.IsMain = true;
        var a = Row(marked: true);

        Assert.Equal(1, SquadLaunchPlan.Build([main, a]).FollowMarkedCount);
    }

    private static LaunchCandidate Cand() =>
        new(IsSelected: true, SessionExpired: false, SessionLimited: false, InGame: false, IsRunning: false, IsLaunching: false, Name: "x");

    // ---- one setter path for NeedsVerification; a 403 never marks for follow -----------------

    private sealed class ScriptedLauncher(Func<LaunchTarget, LaunchResult> next) : IRobloxLauncher
    {
        public List<LaunchTarget> Targets { get; } = [];

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Targets.Add(target);
            return Task.FromResult(next(target));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task Launch_NeedsVerification_SetsTheFlag_AndOnlyTheFlag()
    {
        var launcher = new ScriptedLauncher(_ => new LaunchResult.NeedsVerification("captcha"));
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie"));

            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(1));

            Assert.True(row.SessionNeedsVerification);
            Assert.Equal(AccountHealth.NeedsVerification, row.Health);
            Assert.False(row.SessionLimited);
            Assert.False(row.SessionExpired);
            Assert.False(row.JoinViaFriend);
            Assert.False((await store.ListAsync()).Single(a => a.Id == row.Id).JoinViaFriend);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Launch_Limited_403WithoutAChallenge_NeverSetsNeedsVerification_OrTheFollowMark()
    {
        var launcher = new ScriptedLauncher(_ => new LaunchResult.Limited());
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie"));

            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(1));

            Assert.True(row.SessionLimited);
            Assert.False(row.SessionNeedsVerification);
            Assert.False(row.JoinViaFriend);
            Assert.False((await store.ListAsync()).Single(a => a.Id == row.Id).JoinViaFriend);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task PresenceSessionLimited_FlipsLimitedOnly_NeverVerificationOrTheMark()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie"));
            vm.Accounts.Add(row);

            vm.ApplySessionLimited(row.Id);

            Assert.True(row.SessionLimited);
            Assert.False(row.SessionNeedsVerification);
            Assert.False(row.JoinViaFriend);
            Assert.Equal(AccountHealth.Limited, row.Health);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task OpenRobloxHomeCommand_LaunchesTheRowToTheHomeTarget()
    {
        var launcher = new ScriptedLauncher(_ => new LaunchResult.Failed(LaunchFailureKind.ProcessStartFailed, "test launch refused"));
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie"));

            vm.OpenRobloxHomeCommand.Execute(row);
            for (var i = 0; i < 200 && launcher.Targets.Count == 0; i++) await Task.Delay(25);

            Assert.IsType<LaunchTarget.Home>(Assert.Single(launcher.Targets));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void HealthAction_DispatchesByState()
    {
        Assert.Equal(MainViewModel.HealthAction.Reauthenticate, MainViewModel.HealthActionFor(AccountHealth.Expired));
        Assert.Equal(MainViewModel.HealthAction.Reauthenticate, MainViewModel.HealthActionFor(AccountHealth.Limited));
        Assert.Equal(MainViewModel.HealthAction.OpenRobloxHome, MainViewModel.HealthActionFor(AccountHealth.NeedsVerification));
        Assert.Equal(MainViewModel.HealthAction.None, MainViewModel.HealthActionFor(AccountHealth.Ok));
        Assert.Equal(MainViewModel.HealthAction.None, MainViewModel.HealthActionFor(AccountHealth.FollowMarked));
    }

    [Fact]
    public async Task HealthActionCommand_NeedsVerification_OpensHome_AndOkDoesNothing()
    {
        var launcher = new ScriptedLauncher(_ => new LaunchResult.Failed(LaunchFailureKind.ProcessStartFailed, "x"));
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie"));
            await vm.RunHealthActionAsync(row);
            Assert.Empty(launcher.Targets); // Ok: nothing

            row.JoinViaFriend = true;
            await vm.RunHealthActionAsync(row);
            Assert.Empty(launcher.Targets); // FollowMarked: nothing

            row.SessionNeedsVerification = true;
            await vm.RunHealthActionAsync(row);
            Assert.IsType<LaunchTarget.Home>(Assert.Single(launcher.Targets));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task APresencePollThatLands_ClearsNeedsVerification()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie")) { SessionNeedsVerification = true };
            vm.Accounts.Add(row);

            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.OnlineWebsite, null, null, DateTimeOffset.UtcNow));

            Assert.False(row.SessionNeedsVerification);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// NeedsVerification has exactly one setter path. A source scan, because the guarantee is about
    /// where the code is, not what one run does: the only non-false assignment in the App is the
    /// <c>LaunchResult.NeedsVerification</c> case in <c>LaunchAccountAsync</c>.
    /// </summary>
    [Fact]
    public void SessionNeedsVerification_IsSetTrueFromExactlyOnePlace()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var hits = new List<string>();
        var scanned = 0;
        foreach (var path in Directory.EnumerateFiles(appDir!, "*.cs", SearchOption.AllDirectories))
        {
            var rel = path.Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            scanned++;
            var text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, @"SessionNeedsVerification\s*=\s*(?!=)([^;\r\n]+);"))
            {
                if (m.Groups[1].Value.Trim() != "false") hits.Add($"{Path.GetFileName(path)}: {m.Value}");
            }
        }

        Assert.True(scanned >= 100, $"scanned only {scanned} files; the walk is broken");
        var single = Assert.Single(hits);
        Assert.StartsWith("MainViewModel.cs", single);
        Assert.Contains("= true", single);
    }

    [Fact]
    public void NoLaunchOutcomeCaseAssignsTheFollowMark()
    {
        // The 403 paths above never mark; this pins that the Limited and NeedsVerification cases in
        // LaunchAccountAsync contain no JoinViaFriend assignment.
        var appDir = XamlStyleScanner.AppSourceDirectory();
        var text = File.ReadAllText(Path.Combine(appDir!, "ViewModels", "MainViewModel.cs"));
        var start = text.IndexOf("case LaunchResult.Limited:", StringComparison.Ordinal);
        var end = text.IndexOf("case LaunchResult.Failed { Kind", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        Assert.DoesNotContain("JoinViaFriend", text[start..end]);
    }

    [Fact]
    public void TheRowNamesBothBadges_FromTheBoundAccessibleNames()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        var xaml = File.ReadAllText(Path.Combine(appDir!, "MainWindow.xaml"));

        Assert.Contains("AutomationProperties.Name=\"{Binding HealthBadgeName}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{Binding FollowMarkBadgeName}\"", xaml);
        Assert.Contains("DataContext.HealthActionCommand", xaml);
        Assert.DoesNotContain("<Setter Property=\"Command\"", xaml);
    }
}
