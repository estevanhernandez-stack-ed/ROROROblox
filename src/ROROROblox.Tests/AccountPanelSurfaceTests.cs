using System.IO;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 7, the parts of the panel surface that are not pixels: how an entry words itself, that a click
/// goes through the one launch path (and obeys what that path decides), that the note box saves, and that the
/// batch surfaces key off <c>HasMultipleAccounts</c>. The XAML itself is covered by the render and fence tests.
/// </summary>
public class AccountPanelSurfaceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static LibraryEntry Fav(string name, bool isDefault = false, string? local = null) =>
        new(LibraryEntryKind.Favourite, 111, null, name, local, null, isDefault, null, null, new LaunchTarget.Place(111));

    private static LibraryEntry Server(string name) =>
        new(LibraryEntryKind.PrivateServer, 111, Guid.NewGuid(), name, null, null, false, null, null,
            new LaunchTarget.PrivateServer(111, "FAKE-CODE", PrivateServerCodeKind.AccessCode));

    // ------------------------------------------------------------------ how an entry words itself

    [Fact]
    public void ARecentEntry_ReadsAgeThenTimeInRecentSessions()
    {
        var e = new PanelEntry(Fav("Pet Simulator 99"), Now.AddHours(-2), TimeSpan.FromHours(14), IsSaved: true);
        // Recent is the Library entry's kind; Favourite kind (saved) shows only the age. Recent shows both.
        Assert.Equal("2 hr ago", e.MetaText);

        var recent = new PanelEntry(e.Entry with { Kind = LibraryEntryKind.Recent }, Now.AddHours(-2), TimeSpan.FromHours(14), IsSaved: false);
        Assert.Equal("2 hr ago · 14 hr in recent sessions", recent.MetaText);
    }

    [Theory]
    [InlineData(30, "<1 min")]
    [InlineData(40 * 60, "40 min")]
    [InlineData(3 * 3600, "3 hr")]
    public void TimeInRecentSessions_UsesTheShortestHonestUnit(int seconds, string expected)
    {
        var recent = new PanelEntry(Fav("G") with { Kind = LibraryEntryKind.Recent }, Now.AddDays(-1), TimeSpan.FromSeconds(seconds), IsSaved: false);
        Assert.EndsWith(expected + " in recent sessions", recent.MetaText);
    }

    [Fact]
    public void ASavedEntryThisAccountNeverLaunched_SaysSo()
    {
        Assert.Equal("Not played by this account", new PanelEntry(Fav("Adopt Me!"), null, null, IsSaved: true).MetaText);
    }

    [Fact]
    public void APrivateServerLeadsWithItsGame_WhenTheGameIsKnown()
    {
        var e = new PanelEntry(Server("Clan base"), Now.AddHours(-5), null, IsSaved: true, PlaceName: "Pet Simulator 99");
        Assert.Equal("Pet Simulator 99 · 5 hr ago", e.MetaText);
        Assert.True(e.IsPrivate);
        Assert.Equal("5 hr ago", new PanelEntry(Server("Clan base"), Now.AddHours(-5), null, IsSaved: true).MetaText);
    }

    [Fact]
    public void TheAccessibleNameAndTooltip_CarryTheFullNameNotTheTrimmedOne()
    {
        const string name = "Pet Simulator 99 Hardcore Mega Edition (Beta Test Realm)";
        var e = new PanelEntry(Fav(name, local: null), Now.AddHours(-2), null, IsSaved: true);

        Assert.StartsWith("Launch " + name + ", ", e.AccessibleName);
        Assert.StartsWith(name + "\n", e.TooltipText);
        Assert.Equal("Save " + name + " to favourites", e.SaveAccessibleName);
    }

    [Fact]
    public void ALocalRename_WinsOverTheRobloxName()
        => Assert.Equal("My farm", new PanelEntry(Fav("Pet Simulator 99", local: "My farm"), null, null, IsSaved: true).DisplayName);

    [Fact]
    public void AnUnsavedPrivateRecent_IsDisabledPrivateAndExplainsWhy_AndOffersNoSave()
    {
        var synthesized = new LibraryEntry(LibraryEntryKind.Recent, 111, null, "Pet Simulator 99", null, null, false, Now, null, new LaunchTarget.Place(111));
        var e = new PanelEntry(synthesized, Now.AddHours(-3), null, IsSaved: false, CanLaunch: false);

        Assert.True(e.IsPrivate);
        Assert.False(e.ShowSave);
        Assert.Contains("cannot be relaunched", e.TooltipText);
    }

    [Fact]
    public void SaveShowsOnlyOnAnUnsavedLaunchableEntry()
    {
        Assert.True(new PanelEntry(Fav("G") with { Kind = LibraryEntryKind.Recent }, Now, null, IsSaved: false).ShowSave);
        Assert.False(new PanelEntry(Fav("G"), Now, null, IsSaved: true).ShowSave);
    }

    [Theory]
    [InlineData("Pet Simulator 99", "PS")]
    [InlineData("Adopt Me!", "AM")]
    [InlineData("Blox", "B")]
    [InlineData("!!!", "")]
    public void TheIconTileFallsBackToInitials(string name, string expected)
        => Assert.Equal(expected, new PanelEntry(Fav(name), null, null, IsSaved: true).Initials);

    [Fact]
    public async Task TheMinuteTick_RefreshesAnOpenPanelsAges_WithoutRebuildingOrReadingHistory()
    {
        var owner = new AccountSummary(new Account(Guid.NewGuid(), "Alt", "", Now, null));
        var historyReads = 0;
        var played = new LaunchSession(Guid.NewGuid(), owner.Id, "Alt", null, "Some Game", 111, false, Now.AddHours(-2), Now.AddHours(-1), null);
        var panel = new AccountPanelViewModel(
            owner,
            () => { historyReads++; return Task.FromResult<IReadOnlyList<LaunchSession>>([played]); },
            () => Task.FromResult<IReadOnlyList<LibraryEntry>>([]),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);
        owner.AttachPanel(panel);
        await panel.RefreshAsync();
        var entry = Assert.Single(panel.Recent);
        var readsBefore = historyReads;
        var metaBefore = entry.MetaText;
        var lastBefore = owner.LastPlayedText;
        var raised = new List<string?>();
        entry.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.StartsWith("2 hr ago", metaBefore);

        AccountSummary.ClockOverride.Value = () => DateTimeOffset.UtcNow.AddHours(3); // three hours pass
        try
        {
            owner.RefreshRelativeTimes();

            Assert.NotEqual(metaBefore, entry.MetaText);
            Assert.StartsWith("5 hr ago", entry.MetaText);
            Assert.NotEqual(lastBefore, owner.LastPlayedText);
            Assert.Contains(nameof(PanelEntry.MetaText), raised);
            Assert.Contains(nameof(PanelEntry.AccessibleName), raised);
        }
        finally { AccountSummary.ClockOverride.Value = null; }

        Assert.Same(entry, panel.Recent[0]);            // the same entry object: raised on, not rebuilt
        Assert.Equal(readsBefore, historyReads);        // and no history read
    }

    [Fact]
    public void TheExpanderGlyph_FollowsThePanel_AndRaisesWhenItFlips()
    {
        var owner = new AccountSummary(new Account(Guid.NewGuid(), "Alt", "", Now, null));
        var panel = new AccountPanelViewModel(
            owner,
            () => Task.FromResult<IReadOnlyList<LaunchSession>>([]),
            () => Task.FromResult<IReadOnlyList<LibraryEntry>>([]),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);
        owner.AttachPanel(panel);
        var raised = new List<string?>();
        owner.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("\u25B8", owner.PanelExpanderGlyph);
        panel.IsExpanded = true;
        Assert.Equal("\u25BE", owner.PanelExpanderGlyph);
        Assert.Contains(nameof(AccountSummary.PanelExpanderGlyph), raised);
        panel.IsExpanded = false;
        Assert.Equal("\u25B8", owner.PanelExpanderGlyph);
    }

    [Fact]
    public void AWithClone_DoesNotInheritTheOriginalsHandlers()
    {
        var a = new PanelEntry(Fav("G"), Now, null, IsSaved: false);
        var raised = 0;
        a.PropertyChanged += (_, _) => raised++;

        var clone = a with { IsSaved = true };
        clone.RaiseTimesChanged();

        Assert.Equal(0, raised);
    }

    // ------------------------------------------------------------------ the one launch path

    private static string ReadSource(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any()) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void ThePanelOwnsNoLaunchCode()
    {
        var source = ReadSource("src/ROROROblox.App/ViewModels/AccountPanelViewModel.cs");
        Assert.DoesNotContain("RobloxLauncher", source);
        // The only place a click goes: the delegate the main view model bound to LaunchAccountAsync(owner, target).
        Assert.Contains("_launch(entry.Entry.Target)", source);
        Assert.Contains("_launch(HomeTarget)", source);
    }

    private static async Task LoadRowsAsync(MainViewModel vm)
    {
        try { await vm.LoadAsync(); }
        catch (NotImplementedException) { } // LoadAsync ends at the presence fake's Start(); the rows are in by then
    }

    private sealed class ExpiredLauncher : IRobloxLauncher
    {
        public int Calls;
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Calls++;
            return Task.FromResult<LaunchResult>(new LaunchResult.CookieExpired());
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    private static PanelEntry EntryFor(LaunchTarget target) =>
        new(new LibraryEntry(LibraryEntryKind.Favourite, 8737899170, null, "Pet Simulator 99", null, null, false, null, null, target),
            null, null, IsSaved: true);

    [Fact]
    public async Task AnEntryClick_ReachesTheLauncherWithTheEntrysTarget_AsThatAccount()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await store.AddAsync("Alt", "", "FAKE-COOKIE-surface");
            await LoadRowsAsync(vm);
            var row = vm.Accounts.Single();
            var target = new LaunchTarget.PrivateServer(8737899170, "FAKE-CODE", PrivateServerCodeKind.AccessCode);

            await row.Panel!.LaunchAsync(EntryFor(target));

            Assert.Equal(target, Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task OpenRobloxHome_LaunchesTheHomeTarget_ThroughTheSamePath()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await store.AddAsync("Alt", "", "FAKE-COOKIE-surface");
            await LoadRowsAsync(vm);
            var row = vm.Accounts.Single();

            row.Panel!.OpenHomeCommand.Execute(null);
            await FlaggedLaunchTests.UntilSettledAsync(row);

            Assert.IsType<LaunchTarget.Home>(Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task AMarkedAccountsPanelClick_BecomesAFollow_NotTheEntrysPlace()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var main = await store.AddAsync("Main", "", "FAKE-COOKIE-main");
            var alt = await store.AddAsync("Alt", "", "FAKE-COOKIE-alt");
            await store.UpdateRobloxUserIdAsync(main.Id, 111);
            await store.UpdateRobloxUserIdAsync(alt.Id, 222);
            await store.SetJoinViaFriendAsync(alt.Id, true);
            await LoadRowsAsync(vm);
            var mainRow = vm.Accounts.Single(a => a.Id == main.Id);
            var altRow = vm.Accounts.Single(a => a.Id == alt.Id);
            vm.ApplyPresence(new AccountPresenceEventArgs(mainRow.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");

            await altRow.Panel!.LaunchAsync(EntryFor(new LaunchTarget.Place(8737899170)));

            // DecideFlaggedLaunch ran: the marked account follows the main instead of joining the place itself.
            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task AnExpiredAccountsPanelClick_SurfacesCookieExpired_LikeTheRowsOwnButton()
    {
        var launcher = new ExpiredLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await store.AddAsync("Alt", "", "FAKE-COOKIE-surface");
            await LoadRowsAsync(vm);
            var row = vm.Accounts.Single();
            Assert.False(row.SessionExpired);

            await row.Panel!.LaunchAsync(EntryFor(new LaunchTarget.Place(8737899170)));

            Assert.Equal(1, launcher.Calls);
            Assert.True(row.SessionExpired);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task ANonLaunchableEntry_NeverReachesTheLauncher()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await store.AddAsync("Alt", "", "FAKE-COOKIE-surface");
            await LoadRowsAsync(vm);
            var row = vm.Accounts.Single();
            var entry = EntryFor(new LaunchTarget.Place(8737899170)) with { CanLaunch = false, IsSaved = false };

            Assert.False(row.Panel!.LaunchCommand.CanExecute(entry));
            await row.Panel.LaunchAsync(entry);

            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    // ------------------------------------------------------------------ the note box

    [Fact]
    public async Task CommittingTheNoteBox_TrimsCapsAndPersists()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            await store.AddAsync("Alt", "", "FAKE-COOKIE-surface");
            await LoadRowsAsync(vm);
            var row = vm.Accounts.Single();

            row.NoteText = "   Clan bank on Fridays   ";
            await WaitAsync(async () => (await store.ListAsync()).Single().Note == "Clan bank on Fridays");
            Assert.Equal("Clan bank on Fridays", row.NoteText);                 // the box re-reads the trimmed text

            row.NoteText = new string('x', Account.MaxNoteLength + 50);
            await WaitAsync(async () => (await store.ListAsync()).Single().Note?.Length == Account.MaxNoteLength);

            row.NoteText = "  ";
            await WaitAsync(async () => (await store.ListAsync()).Single().Note is null);
            Assert.Equal(string.Empty, row.NoteText);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task AFactsRefresh_NeverRewritesWhatTheUserIsTypingInTheNoteBox()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            var added = await store.AddAsync("Alt", "", "FAKE-COOKIE-surface");
            await LoadRowsAsync(vm);
            var row = vm.Accounts.Single();
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.Facts = new AccountFacts(10, null, 1, DateTimeOffset.UtcNow, null);

            Assert.Contains(nameof(AccountSummary.RobuxText), raised);
            Assert.DoesNotContain(nameof(AccountSummary.NoteText), raised); // a re-read would clobber an uncommitted edit
            Assert.Equal(added.Id, row.Id);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    // ------------------------------------------------------------------ the one-account window

    [Fact]
    public async Task HasMultipleAccounts_FollowsTheRoster_AndRaisesWhenItFlips()
    {
        var (vm, store, _, path) = MainViewModelTests.Build();
        try
        {
            Assert.False(vm.HasMultipleAccounts);
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Accounts.Add(new AccountSummary(await store.AddAsync("A", "", "c1")));
            Assert.False(vm.HasMultipleAccounts);

            vm.Accounts.Add(new AccountSummary(await store.AddAsync("B", "", "c2")));
            Assert.True(vm.HasMultipleAccounts);
            Assert.Contains(nameof(MainViewModel.HasMultipleAccounts), raised);

            raised.Clear();
            vm.Accounts.RemoveAt(1);
            Assert.False(vm.HasMultipleAccounts);
            Assert.Contains(nameof(MainViewModel.HasMultipleAccounts), raised);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    private static async Task WaitAsync(Func<Task<bool>> done)
    {
        for (var i = 0; i < 300; i++)
        {
            if (await done()) return;
            await Task.Delay(10);
        }

        throw new TimeoutException("The condition did not become true.");
    }
}
