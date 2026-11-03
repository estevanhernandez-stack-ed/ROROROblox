using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// One entry in an account's panel: a <see cref="LibraryEntry"/> decorated with THIS account's
/// last-played and total (the entry itself is the shared Library's). Data only; the view words it.
/// </summary>
/// <param name="Entry">The shared Library entry, or a synthesized Recent one for a place nobody saved.</param>
/// <param name="LastPlayedUtc">When this account last launched it. Null if it never did.</param>
/// <param name="TotalPlayed">Sum of this account's session durations over the history window ("in recent sessions"). Null if none ended.</param>
/// <param name="IsSaved">True when the place or server is in the Library (a favourite or a saved private server).</param>
/// <param name="CanLaunch">False for a Recent private-server group with no saved server behind it: the history row has no access code, and launching its place would join the PUBLIC game.</param>
/// <param name="PlaceName">For a saved private server, the game it lives in as the icon cache knows it (no fetch), or null. Shown beside the age.</param>
public sealed record PanelEntry(
    LibraryEntry Entry, DateTimeOffset? LastPlayedUtc, TimeSpan? TotalPlayed, bool IsSaved, bool CanLaunch = true, string? PlaceName = null)
    : INotifyPropertyChanged
{
    /// <summary>
    /// Copy constructor written out so a <c>with</c> clone does NOT inherit the original's
    /// <see cref="PropertyChanged"/> handlers (the synthesized one would copy the event's backing field, and
    /// the clone would then notify views bound to the entry it replaced).
    /// </summary>
    private PanelEntry(PanelEntry original)
    {
        Entry = original.Entry;
        LastPlayedUtc = original.LastPlayedUtc;
        TotalPlayed = original.TotalPlayed;
        IsSaved = original.IsSaved;
        CanLaunch = original.CanLaunch;
        PlaceName = original.PlaceName;
    }

    /// <summary>Raised only by <see cref="RaiseTimesChanged"/>: the data is immutable, but its wording drifts with the clock.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The age in <see cref="MetaText"/> (and the tooltip and accessible name built from it) is now stale: say so, without touching the data.</summary>
    public void RaiseTimesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MetaText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TooltipText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
    }

    /// <summary>The name the entry shows: the Library's rename when there is one.</summary>
    public string DisplayName => Entry.RenderName;

    /// <summary>Up to two letters of the name, drawn on the icon tile until (or unless) the real icon arrives.</summary>
    public string Initials
    {
        get
        {
            var letters = DisplayName.Split([' ', '-', '_', ':', '!', '.'], StringSplitOptions.RemoveEmptyEntries)
                                     .Select(w => w.FirstOrDefault(char.IsLetterOrDigit))
                                     .Where(c => c != default)
                                     .Take(2)
                                     .ToArray();
            return letters.Length == 0 ? string.Empty : new string(letters).ToUpper(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>True for a saved private server and for a Recent group that was a private server (saved or not).</summary>
    public bool IsPrivate => Entry.Kind == LibraryEntryKind.PrivateServer || Entry.PrivateServerId is not null || !CanLaunch;

    /// <summary>True for the Library's default entry (the one the Launch button uses).</summary>
    public bool IsDefault => Entry.IsDefault;

    /// <summary>The inline Save affordance: an unsaved Recent entry that can be launched (so it has a public place to save).</summary>
    public bool ShowSave => !IsSaved && CanLaunch;

    /// <summary>
    /// The second line: how long ago THIS account last played it, then (Recent only) the time it spent
    /// there "in recent sessions" (the window is 100 history rows, not all time); a saved entry this
    /// account never launched says so. A private server leads with its game when the icon cache knows it.
    /// </summary>
    public string MetaText
    {
        get
        {
            var parts = new List<string>(3);
            if (Entry.Kind == LibraryEntryKind.PrivateServer && !string.IsNullOrWhiteSpace(PlaceName)) parts.Add(PlaceName!);
            parts.Add(LastPlayedUtc is { } at ? ROROROblox.App.ViewModels.AccountSummary.RelativeAgo(at) : Loc.Get("Shell_Panel_Meta_NotPlayed"));
            if (Entry.Kind == LibraryEntryKind.Recent && TotalPlayed is { } total && total > TimeSpan.Zero)
                parts.Add(Loc.Format("Shell_Panel_Meta_InRecent", FormatDuration(total)));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Tooltip: the full name over the full meta line, for the day the column trims either with an ellipsis; a disabled entry adds why.</summary>
    public string TooltipText => DisplayName + "\n" + MetaText + (CanLaunch ? string.Empty : "\n" + DisabledHint);

    /// <summary>Accessible name of the entry's launch button: the full name and meta, never the trimmed text.</summary>
    public string AccessibleName => Loc.Format("Shell_Panel_Entry_Name", DisplayName, MetaText);

    /// <summary>Accessible name of the inline Save button.</summary>
    public string SaveAccessibleName => Loc.Format("Shell_Panel_Save_Name", DisplayName);

    /// <summary>Why a disabled entry is disabled, or empty when it is not.</summary>
    public string DisabledHint => CanLaunch ? string.Empty : Loc.Get("Shell_Panel_Entry_PrivateUnsaved");

    private static string FormatDuration(TimeSpan d)
        => d < TimeSpan.FromMinutes(1) ? Loc.Get("Shell_Panel_Duration_LessThanMinute")
         : d < TimeSpan.FromHours(1) ? Loc.Format("Shell_Panel_Duration_Minutes", (int)d.TotalMinutes)
         : Loc.Format("Shell_Panel_Duration_Hours", (int)d.TotalHours);
}

/// <summary>This account's most recent session with a place: the name to show and when it started.</summary>
public sealed record PanelLastPlayed(string? Name, DateTimeOffset AtUtc);

/// <summary>
/// The per-account panel under a row (spec 2026-11-01 §3): Recent, Favourites and Private servers
/// for ONE account, joined from the session history and the shared Library. No store of its own,
/// no launch code of its own: <c>launch</c> is the main view model's <c>LaunchAccountAsync(owner, target)</c>.
/// <para>
/// <b>Deviation from §3.1's "exactly four delegates".</b> The four are the first four parameters. Three
/// optional dependencies follow because the item's acceptance needs them: an <see cref="IGameIconCache"/>
/// (an unsaved Recent entry needs an icon and a universe id to be saved), an
/// <see cref="IFavoriteGameStore"/> (Save calls <c>AddAsync</c>), and an <see cref="IUiDispatcher"/>
/// (the cache raises <see cref="IGameIconCache.IconResolved"/> on the facts worker's thread, so the
/// panel marshals before touching its collections). All three default to null, in which case the
/// panel simply has no icons, no Save and runs inline.
/// </para>
/// <para>
/// <b>Nothing at startup.</b> Constructing a panel reads and fetches nothing. A collapsed panel never
/// refreshes and never asks for an icon. <see cref="ResolveExpanded"/> (load) may read local files
/// once for a panel that resolves open, but requests no icon and no facts; icons are asked for by
/// <see cref="Expand"/>, a session ending, or an explicit <see cref="RequestVisibleIcons"/>.
/// </para>
/// </summary>
public sealed class AccountPanelViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// Recent shows this many distinct games or servers. Spec §3.2 said eight (a default to confirm on the
    /// first interactive run); the C2 walk (2026-11-02) cut it to three: one open panel is then never taller
    /// than three entries, and one account fits the declared 900x600 with it open (measured: three entries fit,
    /// four scroll).
    /// </summary>
    public const int RecentCap = 3;

    /// <summary>Favourites shows this many entries; the group header carries the total and a "See all" link past it (C2 ruling P1).</summary>
    public const int FavouritesCap = 3;

    /// <summary>Private servers shows this many entries; the group header carries the total and a "See all" link past it (C2 ruling P1).</summary>
    public const int PrivateServersCap = 3;

    private readonly AccountSummary _owner;
    private readonly Func<Task<IReadOnlyList<LaunchSession>>> _history;
    private readonly Func<Task<IReadOnlyList<LibraryEntry>>> _library;
    private readonly Func<LaunchTarget, Task> _launch;
    private readonly Func<Task> _requestFacts;
    private readonly IGameIconCache? _icons;
    private readonly IFavoriteGameStore? _favorites;
    private readonly IUiDispatcher? _ui;
    private readonly Action? _openLibrary;
    private readonly HashSet<long> _savedHere = new();
    private bool _isExpanded;
    private bool _iconsAttached;
    private int _refreshSeq;
    private PanelLastPlayed? _lastPlayed;
    private int _favouritesTotal;
    private int _privateServersTotal;

    public AccountPanelViewModel(
        AccountSummary owner,
        Func<Task<IReadOnlyList<LaunchSession>>> history,
        Func<Task<IReadOnlyList<LibraryEntry>>> library,
        Func<LaunchTarget, Task> launch,
        Func<Task> requestFacts,
        IGameIconCache? icons = null,
        IFavoriteGameStore? favorites = null,
        IUiDispatcher? ui = null,
        Action? openLibrary = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _requestFacts = requestFacts ?? throw new ArgumentNullException(nameof(requestFacts));
        _icons = icons;
        _favorites = favorites;
        _ui = ui;
        _openLibrary = openLibrary;
        LaunchCommand = new RelayCommand(p => p is PanelEntry e ? LaunchAsync(e) : Task.CompletedTask, p => p is PanelEntry { CanLaunch: true });
        OpenHomeCommand = new RelayCommand(() => _launch(HomeTarget));
        SaveCommand = new RelayCommand(p => p is PanelEntry e ? SaveAsync(e) : Task.CompletedTask, p => p is PanelEntry { IsSaved: false, CanLaunch: true });
        OpenLibraryCommand = new RelayCommand(() => _openLibrary?.Invoke());
    }

    public ObservableCollection<PanelEntry> Recent { get; } = new();
    public ObservableCollection<PanelEntry> Favourites { get; } = new();
    public ObservableCollection<PanelEntry> PrivateServers { get; } = new();

    /// <summary>The one-account "play within two clicks of Add account" affordance: Open Roblox home.</summary>
    public LaunchTarget HomeTarget { get; } = new LaunchTarget.Home();

    public ICommand LaunchCommand { get; }
    public ICommand OpenHomeCommand { get; }
    public ICommand SaveCommand { get; }

    /// <summary>The "See all" link in the Favourites and Private servers headers: opens the Library page (the header's Library button does the same).</summary>
    public ICommand OpenLibraryCommand { get; }

    /// <summary>How many favourites the Library holds, however many of them the column shows (<see cref="FavouritesCap"/>).</summary>
    public int FavouritesTotal
    {
        get => _favouritesTotal;
        private set
        {
            if (_favouritesTotal == value) return;
            _favouritesTotal = value;
            OnPropertyChanged(nameof(FavouritesTotal));
            OnPropertyChanged(nameof(FavouritesCountText));
            OnPropertyChanged(nameof(ShowFavouritesCount));
            OnPropertyChanged(nameof(ShowFavouritesSeeAll));
        }
    }

    /// <summary>How many saved private servers the Library holds, however many of them the column shows (<see cref="PrivateServersCap"/>).</summary>
    public int PrivateServersTotal
    {
        get => _privateServersTotal;
        private set
        {
            if (_privateServersTotal == value) return;
            _privateServersTotal = value;
            OnPropertyChanged(nameof(PrivateServersTotal));
            OnPropertyChanged(nameof(PrivateServersCountText));
            OnPropertyChanged(nameof(ShowPrivateServersCount));
            OnPropertyChanged(nameof(ShowPrivateServersSeeAll));
        }
    }

    /// <summary>The count after the Favourites header word: "· 5". Empty when there are none (the empty state already reads).</summary>
    public string FavouritesCountText => CountText(FavouritesTotal);

    /// <summary>The count after the Private servers header word: "· 1". Empty when there are none.</summary>
    public string PrivateServersCountText => CountText(PrivateServersTotal);

    public bool ShowFavouritesCount => FavouritesTotal > 0;

    public bool ShowPrivateServersCount => PrivateServersTotal > 0;

    /// <summary>The "See all" link shows only when the column hides entries AND there is a Library page to open.</summary>
    public bool ShowFavouritesSeeAll => FavouritesTotal > FavouritesCap && _openLibrary is not null;

    /// <summary>The "See all" link shows only when the column hides entries AND there is a Library page to open.</summary>
    public bool ShowPrivateServersSeeAll => PrivateServersTotal > PrivateServersCap && _openLibrary is not null;

    private static string CountText(int total) =>
        total > 0 ? "· " + total.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>This account's most recent session with a place, or null. The row's <c>LastPlayedText</c> reads this same object.</summary>
    public PanelLastPlayed? LastPlayed
    {
        get => _lastPlayed;
        private set
        {
            if (Equals(_lastPlayed, value)) return;
            _lastPlayed = value;
            OnPropertyChanged(nameof(LastPlayed));
        }
    }

    /// <summary>Raised only when the USER opens or closes the panel (and by Add account on an empty roster). The main view model persists it.</summary>
    public event EventHandler<bool>? UserToggled;

    /// <summary>
    /// The persisted, user-chosen state. Setting it is the user's toggle: it raises
    /// <see cref="UserToggled"/>; opening also refreshes the panel and asks for facts when the
    /// cached ones are older than <see cref="AccountFactsService.MinRefreshAge"/>.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            UserToggled?.Invoke(this, value);
            if (value)
            {
                AttachIcons();
                _ = RefreshAsync();
                if (FactsAreStale()) _ = RequestFactsSafeAsync();
            }
            else
            {
                DetachIcons();
            }
        }
    }

    /// <summary>Open the panel (the user's click). A no-op when it is already open.</summary>
    public void Expand() => IsExpanded = true;

    /// <summary>
    /// Load-time resolution: the persisted value if there is one, else <paramref name="defaultExpanded"/>
    /// (main, or the first row when there is no main). Writes nothing, requests no facts and no icon.
    /// A panel that resolves open reads local history and the Library once.
    /// </summary>
    public void ResolveExpanded(bool defaultExpanded)
    {
        var resolved = _owner.PersistedPanelExpanded ?? defaultExpanded;
        if (_isExpanded == resolved) return;
        _isExpanded = resolved;
        OnPropertyChanged(nameof(IsExpanded));
        if (resolved)
        {
            AttachIcons();
            _ = RefreshCoreAsync(requestIcons: false);
        }
    }

    /// <summary>Re-query history and the Library. Called on expand and after a session ends. Never throws.</summary>
    public Task RefreshAsync() => RefreshCoreAsync(requestIcons: true);

    /// <summary>
    /// Ask the icon cache for the unsaved Recent places on screen that have no cached icon. Does
    /// nothing while the panel is collapsed.
    /// </summary>
    public void RequestVisibleIcons()
    {
        if (!_isExpanded || _icons is null) return;
        foreach (var e in Recent)
        {
            if (!e.IsSaved && e.Entry.IconUrl is null) _icons.Request(e.Entry.PlaceId);
        }
    }

    /// <summary>Every click on an entry lands here: the main view model's launch path, with its guards.</summary>
    public Task LaunchAsync(PanelEntry entry) => entry.CanLaunch ? _launch(entry.Entry.Target) : Task.CompletedTask;

    /// <summary>
    /// Set <see cref="LastPlayed"/> from an already-read history list (the load-time prime: one disk
    /// read for every row, no collections built, no facts, no icons). Never overwrites a value a
    /// fuller refresh already set.
    /// </summary>
    public void PrimeLastPlayed(IReadOnlyList<LaunchSession> history)
    {
        if (_lastPlayed is not null) return;
        var row = history.Where(r => r.AccountId == _owner.Id && (r.PlaceId is not null || r.IsPrivateServer))
                         .OrderByDescending(r => r.LaunchedAtUtc).FirstOrDefault();
        if (row is null) return;
        Marshal(() => LastPlayed ??= new PanelLastPlayed(string.IsNullOrWhiteSpace(row.GameName) ? null : row.GameName, row.LaunchedAtUtc));
    }

    /// <summary>
    /// Save an unsaved Recent place to the Library. Uses the cache's universe id; when the cache has
    /// no row for the place it resolves it first (on the paced worker's rules, never IRobloxApi
    /// directly). If it cannot be resolved nothing is saved and the entry stays unsaved.
    /// </summary>
    public async Task SaveAsync(PanelEntry entry)
    {
        if (entry.IsSaved || !entry.CanLaunch || _favorites is null || _icons is null) return;
        var placeId = entry.Entry.PlaceId;
        try
        {
            var info = _icons.TryGet(placeId);
            if (info is null)
            {
                await _icons.ResolveAsync(placeId).ConfigureAwait(true);
                info = _icons.TryGet(placeId);
            }
            if (info is null) return;
            var name = string.IsNullOrWhiteSpace(info.Name) ? entry.Entry.Name : info.Name;
            await _favorites.AddAsync(placeId, info.UniverseId, name, info.IconUrl).ConfigureAwait(true);
            _savedHere.Add(placeId);
            Marshal(() =>
            {
                var i = Recent.IndexOf(entry);
                if (i >= 0) Recent[i] = entry with { IsSaved = true };
            });
            _ = RefreshAsync();
        }
        catch (Exception)
        {
            // A failed save leaves the entry unsaved; the user can press Save again.
        }
    }

    /// <summary>
    /// The row's 30 s tick: re-raise the entries' time-derived text so an open panel never shows a frozen age.
    /// Raises on the existing entries only; no rebuild, no history read, no network. A closed panel has
    /// nothing bound, so this is cheap there.
    /// </summary>
    public void RefreshRelativeTimes()
    {
        foreach (var e in Recent) e.RaiseTimesChanged();
        foreach (var e in Favourites) e.RaiseTimesChanged();
        foreach (var e in PrivateServers) e.RaiseTimesChanged();
    }

    /// <summary>Drop the icon subscription. Called when the row leaves the roster (the cache is a long-lived singleton).</summary>
    public void Detach() => DetachIcons();

    // ------------------------------------------------------------------ query

    private async Task RefreshCoreAsync(bool requestIcons)
    {
        var seq = Interlocked.Increment(ref _refreshSeq);
        try
        {
            var history = await _history().ConfigureAwait(true);
            var library = await _library().ConfigureAwait(true);
            if (seq != Volatile.Read(ref _refreshSeq)) return; // a newer refresh is in flight and will win
            Apply(Query(history, library));
            if (requestIcons) RequestVisibleIcons();
        }
        catch (Exception)
        {
            // Fire-and-forget callers: a store that cannot be read leaves the last good panel standing.
        }
    }

    private sealed record Model(
        List<PanelEntry> Recent, List<PanelEntry> Favourites, List<PanelEntry> Servers, PanelLastPlayed? LastPlayed,
        int FavouritesTotal, int ServersTotal);

    private Model Query(IReadOnlyList<LaunchSession> history, IReadOnlyList<LibraryEntry> library)
    {
        var mine = history.Where(r => r.AccountId == _owner.Id)
                          .OrderByDescending(r => r.LaunchedAtUtc)
                          .ToList();
        var favs = library.Where(e => e.Kind == LibraryEntryKind.Favourite).ToList();
        var servers = library.Where(e => e.Kind == LibraryEntryKind.PrivateServer).ToList();

        LibraryEntry? SavedFor(LaunchSession r)
        {
            if (r.PrivateServerId is { } sid) return servers.FirstOrDefault(s => s.PrivateServerId == sid);
            if (r.IsPrivateServer) return null; // a legacy private row must not match the public favourite of the same place
            return r.PlaceId is { } p ? favs.FirstOrDefault(f => f.PlaceId == p) : null;
        }

        // Recent: rows with a place, grouped by PrivateServerId ?? PlaceId, latest first, capped.
        var recent = new List<PanelEntry>();
        var groups = mine.Where(r => r.PlaceId is not null)
                         .GroupBy(r => r.PrivateServerId is { } sid ? $"s:{sid:N}" : $"p:{r.PlaceId}")
                         .OrderByDescending(g => g.Max(r => r.LaunchedAtUtc))
                         .Take(RecentCap);
        foreach (var g in groups)
        {
            var latest = g.First();
            var placeId = latest.PlaceId!.Value;
            var saved = SavedFor(latest);
            var entry = saved ?? Synthesize(latest, placeId);
            var unsavedPrivate = saved is null && latest.IsPrivateServer; // Favourites cannot hold a private server
            recent.Add(new PanelEntry(entry, latest.LaunchedAtUtc, TotalOf(g),
                IsSaved: saved is not null || (!unsavedPrivate && _savedHere.Contains(placeId)), CanLaunch: !unsavedPrivate,
                PlaceName: saved?.Kind == LibraryEntryKind.PrivateServer ? GameNameOf(g, saved) : null));
        }

        // Favourites and private servers keep the Library's own order (newest played by anyone first) and show
        // the first three; the totals travel with the model so the headers can say how many there are.
        var favourites = favs.Take(FavouritesCap).Select(f =>
        {
            var rows = mine.Where(r => r.PlaceId == f.PlaceId && r.PrivateServerId is null).ToList();
            return new PanelEntry(f, rows.Count > 0 ? rows[0].LaunchedAtUtc : null, TotalOf(rows), IsSaved: true);
        }).ToList();

        var serverEntries = servers.Take(PrivateServersCap).Select(s =>
        {
            var rows = mine.Where(r => r.PrivateServerId == s.PrivateServerId).ToList();
            return new PanelEntry(s, rows.Count > 0 ? rows[0].LaunchedAtUtc : null, TotalOf(rows), IsSaved: true,
                PlaceName: GameNameOf(rows, s) ?? _icons?.TryGet(s.PlaceId)?.Name);
        }).ToList();

        // Last played (§2.1): the newest row with a place or a private server; the Library's rename wins.
        PanelLastPlayed? last = null;
        var lastRow = mine.FirstOrDefault(r => r.PlaceId is not null || r.IsPrivateServer);
        if (lastRow is not null)
        {
            var name = SavedFor(lastRow)?.RenderName;
            if (string.IsNullOrWhiteSpace(name)) name = lastRow.GameName;
            last = new PanelLastPlayed(string.IsNullOrWhiteSpace(name) ? null : name, lastRow.LaunchedAtUtc);
        }

        return new Model(recent, favourites, serverEntries, last, favs.Count, servers.Count);
    }

    /// <summary>The game a private server lives in, from this account's own history rows for it; null when the row only carries the server's own name.</summary>
    private static string? GameNameOf(IEnumerable<LaunchSession> rows, LibraryEntry server)
        => rows.Select(r => r.GameName)
               .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && !string.Equals(n, server.RenderName, StringComparison.OrdinalIgnoreCase));

    private LibraryEntry Synthesize(LaunchSession latest, long placeId)
    {
        var cached = _icons?.TryGet(placeId);
        var name = !string.IsNullOrWhiteSpace(latest.GameName)
            ? latest.GameName!
            : cached?.Name ?? placeId.ToString(CultureInfo.InvariantCulture);
        return new LibraryEntry(
            LibraryEntryKind.Recent, placeId, latest.PrivateServerId, name, LocalName: null,
            cached?.IconUrl, IsDefault: false, latest.LaunchedAtUtc, TotalPlayed: null, new LaunchTarget.Place(placeId));
    }

    private static TimeSpan? TotalOf(IEnumerable<LaunchSession> rows)
    {
        TimeSpan? total = null;
        foreach (var r in rows)
        {
            if (r.Duration is { } d) total = (total ?? TimeSpan.Zero) + d;
        }
        return total;
    }

    private void Apply(Model m)
    {
        Replace(Recent, m.Recent);
        Replace(Favourites, m.Favourites);
        Replace(PrivateServers, m.Servers);
        FavouritesTotal = m.FavouritesTotal;
        PrivateServersTotal = m.ServersTotal;
        LastPlayed = m.LastPlayed;
    }

    private static void Replace(ObservableCollection<PanelEntry> target, List<PanelEntry> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    // ------------------------------------------------------------------ icons

    private void AttachIcons()
    {
        if (_icons is null || _iconsAttached) return;
        _iconsAttached = true;
        _icons.IconResolved += OnIconResolved;
    }

    private void DetachIcons()
    {
        if (_icons is null || !_iconsAttached) return;
        _iconsAttached = false;
        _icons.IconResolved -= OnIconResolved;
    }

    private void OnIconResolved(object? sender, GameIconResolvedEventArgs e)
        => Marshal(() =>
        {
            for (var i = 0; i < Recent.Count; i++)
            {
                var p = Recent[i];
                if (p.IsSaved || p.Entry.PlaceId != e.PlaceId || p.Entry.IconUrl is not null) continue;
                Recent[i] = p with { Entry = p.Entry with { IconUrl = e.Info.IconUrl } };
            }
        });

    private void Marshal(Action action)
    {
        if (_ui is null) action(); else _ui.Invoke(action);
    }

    // ------------------------------------------------------------------ facts

    private bool FactsAreStale()
        => _owner.Facts?.FetchedAtUtc is not { } at || DateTimeOffset.UtcNow - at >= AccountFactsService.MinRefreshAge;

    private async Task RequestFactsSafeAsync()
    {
        try { await _requestFacts().ConfigureAwait(true); }
        catch (Exception) { /* a facts request never breaks the panel */ }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
