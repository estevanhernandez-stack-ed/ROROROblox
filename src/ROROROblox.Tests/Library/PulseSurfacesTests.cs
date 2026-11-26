using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Games;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.Library;
using ROROROblox.Tests.Rendering;

namespace ROROROblox.Tests.Library;

/// <summary>
/// v1.41 item 12 without a window: the account panel's rules for the pulse, and the one reading the Library and the
/// panel share. A real <see cref="GamePulseService"/> on a fake clock, with a counting answer, so "one reading" and
/// "asks nothing before the gate" are request counts, not assumptions.
/// </summary>
[Collection("MutatesUiCulture")]
public class PulseSurfacesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class Api : IGamePulseApi
    {
        public List<long[]> Requests { get; } = [];

        /// <summary>The games Roblox still returns; an asked id not in this set is "gone".</summary>
        public HashSet<long> Known { get; } = [];

        public Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
        {
            var ids = universeIds.ToArray();
            Requests.Add(ids);
            var found = ids.Where(Known.Contains).ToDictionary(id => id, _ => new GamePulseReading(81_827, Start.AddDays(-2)));
            return Task.FromResult(new GamePulseRead(FeedOutcome.Ok, new GamePulseAnswer(found)));
        }
    }

    private sealed class Rig : IDisposable
    {
        public FakeTimeProvider Clock { get; } = new(Start);
        public Api Api { get; } = new();
        public GamePulseService Service { get; }

        public Rig(bool allow = true)
        {
            Service = new GamePulseService(() => Api, Clock);
            if (allow) Service.AllowReadsFrom(Start);
        }

        public void Settle() => Clock.Advance(GamePulseService.Debounce);
        public void Dispose() => Service.Dispose();
    }

    private static LibraryEntry Game(long place, string name, long? universe, bool isDefault = false) =>
        new(LibraryEntryKind.Favourite, place, null, name, null, null, isDefault, null, null, new LaunchTarget.Place(place), universe);

    private static LibraryEntry Server(long place, string name, long? universe) =>
        new(LibraryEntryKind.PrivateServer, place, Guid.NewGuid(), name, null, null, false, null, null,
            new LaunchTarget.PrivateServer(place, "FAKE-CODE", PrivateServerCodeKind.AccessCode), universe);

    private static AccountPanelViewModel Panel(IReadOnlyList<LibraryEntry> library, IGamePulseService? pulse, bool visible = true)
    {
        var owner = new AccountSummary(new Account(Guid.NewGuid(), "NovaFake", "", Start, null, IsMain: true));
        var panel = new AccountPanelViewModel(
            owner,
            () => Task.FromResult<IReadOnlyList<LaunchSession>>([]),
            () => Task.FromResult(library),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);
        owner.AttachPanel(panel);
        if (pulse is not null)
        {
            panel.SetPulse(pulse);
            panel.SetMainWindowVisible(visible);
        }

        return panel;
    }

    [Fact]
    public void ACollapsedPanel_DeclaresNothing_AndAnOpenOneOnAVisibleWindowDeclaresItsEntries()
    {
        using var rig = new Rig();
        var library = new[] { Game(1, "Alpha Fake", 10), Server(1, "Clan Base", 10), Game(2, "Beta Fake", 20) };
        rig.Api.Known.UnionWith([10, 20]);
        var panel = Panel(library, rig.Service);

        panel.RefreshAsync().GetAwaiter().GetResult();
        rig.Settle();
        Assert.Empty(rig.Api.Requests);   // collapsed: the panel built its entries and said nothing

        panel.Expand();
        rig.Settle();
        Assert.Single(rig.Api.Requests);
        Assert.Equal([10L, 20L], rig.Api.Requests[0].OrderBy(i => i).ToArray());
    }

    [Fact]
    public void AHiddenOrMinimisedWindow_EmptiesThePanelsInterest_AndRestoreDeclaresItAgain()
    {
        using var rig = new Rig();
        rig.Api.Known.UnionWith([10]);
        var panel = Panel([Game(1, "Alpha Fake", 10)], rig.Service);
        panel.Expand();
        rig.Settle();
        Assert.Single(rig.Api.Requests);

        panel.SetMainWindowVisible(false);
        rig.Clock.Advance(TimeSpan.FromMinutes(6));   // past the 5 minute re-read age; nobody is looking
        Assert.Single(rig.Api.Requests);

        panel.SetMainWindowVisible(true);
        rig.Settle();
        Assert.Equal(2, rig.Api.Requests.Count);      // declared again, and the reading was old enough to ask for
    }

    [Fact]
    public void APanelOpenAtStartup_AsksNothingBeforeTheGate()
    {
        using var rig = new Rig(allow: false);
        rig.Service.AllowReadsFrom(Start.AddSeconds(30));
        rig.Api.Known.UnionWith([10]);
        var panel = Panel([Game(1, "Alpha Fake", 10)], rig.Service);

        panel.ResolveExpanded(defaultExpanded: true);   // the load path: open, requests no icon and no fact
        rig.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(rig.Api.Requests);

        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(rig.Api.Requests);
    }

    [Fact]
    public void AReading_IsReplacedInPlace_AndTheCollectionKeepsItsShape()
    {
        using var rig = new Rig();
        rig.Api.Known.UnionWith([10]);
        var panel = Panel([Game(1, "Alpha Fake", 10), Game(2, "Beta Fake", 20)], rig.Service);
        panel.Expand();
        var before = panel.Favourites.ToList();
        Assert.All(before, e => Assert.Null(e.Pulse));
        Assert.Equal(string.Empty, before[0].PulseCountText);
        Assert.Equal(string.Empty, before[0].PulseUpdatedText);

        rig.Settle();
        var after = panel.Favourites.ToList();

        Assert.Equal(2, after.Count);
        Assert.Equal(81_827, after[0].Pulse!.Value.Playing);
        Assert.Equal("81,827 playing", after[0].PulseCountText);
        Assert.Equal("updated 2 days ago", after[0].PulseUpdatedText);
        // The meta line carries the count only (Este, 2026-11-26); the date is the tooltip's and the accessible name's.
        Assert.EndsWith("81,827 playing", after[0].MetaText);
        Assert.DoesNotContain("updated", after[0].MetaText);
        Assert.Equal(["Alpha Fake", after[0].MetaText, "updated 2 days ago"], after[0].TooltipText.Split("\n"));
        Assert.Contains("Alpha Fake", after[0].AccessibleName);
        Assert.Contains("81,827 playing", after[0].AccessibleName);
        Assert.EndsWith("updated 2 days ago", after[0].AccessibleName);
        Assert.Same(before[1], after[1]);   // Beta Fake is a game Roblox no longer returns: untouched, no line
        Assert.Null(after[1].Pulse);
        Assert.Equal(string.Empty, after[1].PulseCountText);
        Assert.Equal(string.Empty, after[1].PulseUpdatedText);
        Assert.Equal(["Beta Fake", after[1].MetaText], after[1].TooltipText.Split("\n"));
    }

    [Fact]
    public void ACopyOfAnEntry_CarriesItsPulse()
    {
        var entry = new PanelEntry(Game(1, "Alpha Fake", 10), null, null, IsSaved: true, Pulse: new GamePulseView(5, Start));
        var copy = entry with { IsSaved = false };
        Assert.Equal(entry.Pulse, copy.Pulse);
    }

    [Fact]
    public void APrivateServer_ShowsTheDate_AndNoCount()
    {
        using var rig = new Rig();
        rig.Api.Known.UnionWith([10]);
        var panel = Panel([Server(1, "Clan Base", 10)], rig.Service);
        panel.Expand();
        rig.Settle();

        var entry = Assert.Single(panel.PrivateServers);
        Assert.Equal(string.Empty, entry.PulseCountText);
        Assert.Equal("updated 2 days ago", entry.PulseUpdatedText);
        // No pulse on the meta line at all; the date is in the tooltip and the accessible name.
        Assert.DoesNotContain("playing", entry.MetaText);
        Assert.DoesNotContain("updated", entry.MetaText);
        Assert.EndsWith("updated 2 days ago", entry.TooltipText);
        Assert.EndsWith("updated 2 days ago", entry.AccessibleName);
    }

    [Fact]
    public void WhenTheSwitchTurnsOff_TheLineGoesAtOnce_AndEntriesKeepTheirPlace()
    {
        using var rig = new Rig();
        rig.Api.Known.UnionWith([10]);
        var panel = Panel([Game(1, "Alpha Fake", 10)], rig.Service);
        panel.Expand();
        rig.Settle();
        Assert.NotEqual(string.Empty, panel.Favourites[0].PulseCountText);
        Assert.NotEqual(string.Empty, panel.Favourites[0].PulseUpdatedText);

        rig.Service.SetEnabled(false);

        Assert.Single(panel.Favourites);
        Assert.Equal(string.Empty, panel.Favourites[0].PulseCountText);
        Assert.Equal(string.Empty, panel.Favourites[0].PulseUpdatedText);
    }

    [Fact]
    public void ADetachedPanel_DeclaresNothingAndStopsListening()
    {
        using var rig = new Rig();
        rig.Api.Known.UnionWith([10]);
        var panel = Panel([Game(1, "Alpha Fake", 10)], rig.Service);
        panel.Expand();
        rig.Settle();
        var asked = rig.Api.Requests.Count;

        panel.Detach();
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(asked, rig.Api.Requests.Count);
    }

    [Fact]
    public void TheLibraryAndAnOpenPanel_ReadOneNumber_FromOneRequest()
    {
        using var rig = new Rig();
        rig.Api.Known.UnionWith([20010]);

        var favourites = new LibraryPageFixtures.MemFavorites();
        favourites.Items.Add(new FavoriteGame(2001, 20010, "Petal Simulator", "", true, Start));
        var servers = new LibraryPageFixtures.MemServers();
        var catalog = new LibraryCatalog(favourites, servers, new LibraryPageFixtures.MemStats([]), new LibraryPageFixtures.MemHistory(), new LibraryPageFixtures.NoIcons());
        var library = new LibraryViewModel(
            catalog, favourites, servers, new LibraryPageFixtures.NullApi(), new MainViewModelTests.FakeAppSettings(),
            _ => Task.FromResult<LaunchTarget?>(null), () => [], (_, _) => Task.CompletedTask, pulse: rig.Service);
        library.LoadAsync().GetAwaiter().GetResult();
        var item = Assert.Single(library.Items);
        library.NoteOnScreen(item);

        var panel = Panel(catalog.ListAsync().GetAwaiter().GetResult(), rig.Service);
        panel.Expand();
        rig.Settle();

        Assert.Single(rig.Api.Requests);   // two surfaces, one game, one question
        var entry = Assert.Single(panel.Favourites);
        Assert.Equal(item.PulseCountText, entry.PulseCountText);
        Assert.Equal(item.PulseUpdatedText, entry.PulseUpdatedText);
        Assert.Equal("81,827 playing · updated 2 days ago", item.PulseText);
    }

    [Fact]
    public void AGameRobloxNoLongerReturns_ShowsNoLine_AndCanStillBeRemoved()
    {
        using var rig = new Rig();   // Known is empty: every asked game is gone
        var favourites = new LibraryPageFixtures.MemFavorites();
        favourites.Items.Add(new FavoriteGame(2001, 20010, "Petal Simulator", "", false, Start));
        var servers = new LibraryPageFixtures.MemServers();
        var catalog = new LibraryCatalog(favourites, servers, new LibraryPageFixtures.MemStats([]), new LibraryPageFixtures.MemHistory(), new LibraryPageFixtures.NoIcons());
        var library = new LibraryViewModel(
            catalog, favourites, servers, new LibraryPageFixtures.NullApi(), new MainViewModelTests.FakeAppSettings(),
            _ => Task.FromResult<LaunchTarget?>(null), () => [], (_, _) => Task.CompletedTask, pulse: rig.Service);
        library.LoadAsync().GetAwaiter().GetResult();
        var item = Assert.Single(library.Items);
        library.NoteOnScreen(item);
        rig.Settle();

        Assert.Single(rig.Api.Requests);
        Assert.False(item.HasPulse);
        Assert.Equal(string.Empty, item.PulseText);
        Assert.Equal("Petal Simulator", item.EntryName);

        library.RequestRemove(item);
        Assert.Same(item, library.PendingRemoval);
        library.ConfirmRemoveAsync().GetAwaiter().GetResult();
        Assert.Empty(favourites.Items);
    }
}
