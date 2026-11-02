using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
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
public sealed record PanelEntry(LibraryEntry Entry, DateTimeOffset? LastPlayedUtc, TimeSpan? TotalPlayed, bool IsSaved, bool CanLaunch = true);

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
    /// <summary>Recent shows this many distinct games or servers (spec §3.2, default).</summary>
    public const int RecentCap = 8;

    private readonly AccountSummary _owner;
    private readonly Func<Task<IReadOnlyList<LaunchSession>>> _history;
    private readonly Func<Task<IReadOnlyList<LibraryEntry>>> _library;
    private readonly Func<LaunchTarget, Task> _launch;
    private readonly Func<Task> _requestFacts;
    private readonly IGameIconCache? _icons;
    private readonly IFavoriteGameStore? _favorites;
    private readonly IUiDispatcher? _ui;
    private readonly HashSet<long> _savedHere = new();
    private bool _isExpanded;
    private bool _iconsAttached;
    private int _refreshSeq;
    private PanelLastPlayed? _lastPlayed;

    public AccountPanelViewModel(
        AccountSummary owner,
        Func<Task<IReadOnlyList<LaunchSession>>> history,
        Func<Task<IReadOnlyList<LibraryEntry>>> library,
        Func<LaunchTarget, Task> launch,
        Func<Task> requestFacts,
        IGameIconCache? icons = null,
        IFavoriteGameStore? favorites = null,
        IUiDispatcher? ui = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _requestFacts = requestFacts ?? throw new ArgumentNullException(nameof(requestFacts));
        _icons = icons;
        _favorites = favorites;
        _ui = ui;
        LaunchCommand = new RelayCommand(p => p is PanelEntry e ? LaunchAsync(e) : Task.CompletedTask, p => p is PanelEntry { CanLaunch: true });
        OpenHomeCommand = new RelayCommand(() => _launch(HomeTarget));
        SaveCommand = new RelayCommand(p => p is PanelEntry e ? SaveAsync(e) : Task.CompletedTask, p => p is PanelEntry { IsSaved: false, CanLaunch: true });
    }

    public ObservableCollection<PanelEntry> Recent { get; } = new();
    public ObservableCollection<PanelEntry> Favourites { get; } = new();
    public ObservableCollection<PanelEntry> PrivateServers { get; } = new();

    /// <summary>The one-account "play within two clicks of Add account" affordance: Open Roblox home.</summary>
    public LaunchTarget HomeTarget { get; } = new LaunchTarget.Home();

    public ICommand LaunchCommand { get; }
    public ICommand OpenHomeCommand { get; }
    public ICommand SaveCommand { get; }

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
        List<PanelEntry> Recent, List<PanelEntry> Favourites, List<PanelEntry> Servers, PanelLastPlayed? LastPlayed);

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
                IsSaved: saved is not null || (!unsavedPrivate && _savedHere.Contains(placeId)), CanLaunch: !unsavedPrivate));
        }

        var favourites = favs.Select(f =>
        {
            var rows = mine.Where(r => r.PlaceId == f.PlaceId && r.PrivateServerId is null).ToList();
            return new PanelEntry(f, rows.Count > 0 ? rows[0].LaunchedAtUtc : null, TotalOf(rows), IsSaved: true);
        }).ToList();

        var serverEntries = servers.Select(s =>
        {
            var rows = mine.Where(r => r.PrivateServerId == s.PrivateServerId).ToList();
            return new PanelEntry(s, rows.Count > 0 ? rows[0].LaunchedAtUtc : null, TotalOf(rows), IsSaved: true);
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

        return new Model(recent, favourites, serverEntries, last);
    }

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
