using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.App.Games;

/// <summary>How the Library lays its entries out. Persisted by name through <see cref="IAppSettings.GetLibraryViewAsync"/>.</summary>
internal enum LibraryView
{
    Grid,
    List,
}

/// <summary>The kind chips. Multi-select; <see cref="None"/> (nothing selected) shows everything.</summary>
[Flags]
internal enum LibraryFilter
{
    None = 0,
    Recent = 1,
    Favourites = 2,
    PrivateServers = 4,
}

/// <summary>What <see cref="LibraryViewModel.SubmitAsync"/> did with the box.</summary>
internal enum LibrarySubmitOutcome
{
    /// <summary>The box was empty.</summary>
    Empty,

    /// <summary>Plain text matched something already in the Library; nothing was asked of Roblox.</summary>
    LocalMatch,

    /// <summary>A game was added to the favourites.</summary>
    AddedGame,

    /// <summary>A private server was added to the saved servers.</summary>
    AddedServer,

    /// <summary>No local match: <see cref="LibraryViewModel.FromRoblox"/> holds the search results.</summary>
    Searched,

    /// <summary>Link-shaped but not a Roblox link, or a link that did not resolve, or a search with no results. Nothing was added or launched.</summary>
    NotUnderstood,

    /// <summary>The add or search threw; the status line carries the reason.</summary>
    Failed,
}

/// <summary>
/// The Library page's brain (spec 2026-11-01 4.2): everything that decides something, so it can be
/// tested without WPF. The page keeps the rename window, the confirm dialogs and focus.
/// <para>
/// Three rules this class owns. Typing filters <see cref="Visible"/> locally and instantly, with no
/// call to Roblox. Enter on a paste goes through <see cref="LibraryPaste"/>, which gates the existing
/// parsers on a Roblox host. Play with no account named resolves the main, then the only account, then
/// the first row.
/// </para>
/// </summary>
internal sealed class LibraryViewModel : INotifyPropertyChanged
{
    private readonly LibraryCatalog _catalog;
    private readonly IFavoriteGameStore _favorites;
    private readonly IPrivateServerStore _servers;
    private readonly IRobloxApi _api;
    private readonly IAppSettings _settings;
    private readonly Func<string, Task<LaunchTarget?>> _resolveShareUrl;
    private readonly Func<IReadOnlyList<AccountSummary>> _accounts;
    private readonly Func<Guid, LaunchTarget, Task> _launch;
    private readonly Action? _libraryChanged;
    private readonly IGameIconCache? _icons;
    private readonly IUiDispatcher? _ui;
    private readonly Dictionary<(LibraryEntryKind Kind, long PlaceId, Guid? ServerId), LibraryItem> _itemCache = new();
    private bool _iconsAttached;
    private LibraryItem? _pendingRemoval;

    private IReadOnlyList<LibraryEntry> _entries = [];
    private LibraryView _view = LibraryView.Grid;
    private LibraryFilter _filters = LibraryFilter.None;
    private string _query = string.Empty;
    private string _statusText = string.Empty;

    /// <param name="resolveShareUrl">The <c>MainViewModel.ResolveShareUrlAsync</c> delegate: needs a non-expired cookie for the share-token form.</param>
    /// <param name="accounts">The roster, read off <c>MainViewModel.AccountsSnapshot</c> (never <c>Accounts</c>).</param>
    /// <param name="launch">Launches an account by id into a target (<c>MainViewModel.LaunchAccountByIdAsync</c>).</param>
    /// <param name="libraryChanged">Fired after a mutation, so the main window's reload runs.</param>
    /// <param name="icons">The icon cache. A tile that comes on screen asks it for its own place and nothing else; null means no icons are ever requested.</param>
    /// <param name="ui">Marshals the cache's worker-thread <c>IconResolved</c> onto the UI thread; null runs inline (tests).</param>
    public LibraryViewModel(
        LibraryCatalog catalog,
        IFavoriteGameStore favorites,
        IPrivateServerStore servers,
        IRobloxApi api,
        IAppSettings settings,
        Func<string, Task<LaunchTarget?>> resolveShareUrl,
        Func<IReadOnlyList<AccountSummary>> accounts,
        Func<Guid, LaunchTarget, Task> launch,
        Action? libraryChanged = null,
        IGameIconCache? icons = null,
        IUiDispatcher? ui = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resolveShareUrl = resolveShareUrl ?? throw new ArgumentNullException(nameof(resolveShareUrl));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _libraryChanged = libraryChanged;
        _icons = icons;
        _ui = ui;
        FromRoblox.CollectionChanged += (_, _) =>
        {
            FromRobloxItems.Clear();
            foreach (var r in FromRoblox) FromRobloxItems.Add(new FromRobloxItem(r));
            RaiseShape();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Entries after the chips and the query.</summary>
    public ObservableCollection<LibraryEntry> Visible { get; } = [];

    /// <summary>The "From Roblox" group, filled by Enter when nothing local matched.</summary>
    public ObservableCollection<GameSearchResult> FromRoblox { get; } = [];

    /// <summary>Every entry the catalog returned, unfiltered.</summary>
    public IReadOnlyList<LibraryEntry> Entries => _entries;

    /// <summary>The last status line (existing <c>Shell_Games_*</c> strings).</summary>
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>
    /// What the grid and the list bind: <see cref="Visible"/> as worded <see cref="LibraryItem"/>s, one stable
    /// instance per saved entry so a keystroke moves tiles instead of rebuilding them.
    /// </summary>
    public ObservableCollection<LibraryItem> Items { get; } = [];

    /// <summary><see cref="FromRoblox"/> as worded rows (the creator and player lines come from resx keys).</summary>
    public ObservableCollection<FromRobloxItem> FromRobloxItems { get; } = [];

    /// <summary>The Library has nothing in it at all and nothing from Roblox is showing: the invitation shows.</summary>
    public bool ShowEmptyInvitation => _entries.Count == 0 && FromRoblox.Count == 0;

    /// <summary>The Library has entries but the chips or the box hide every one, and no Roblox results are showing.</summary>
    public bool ShowNoMatch => _entries.Count > 0 && Visible.Count == 0 && FromRoblox.Count == 0;

    public bool HasFromRoblox => FromRoblox.Count > 0;

    private void RaiseShape()
    {
        OnChanged(nameof(ShowEmptyInvitation));
        OnChanged(nameof(ShowNoMatch));
        OnChanged(nameof(HasFromRoblox));
    }

    /// <summary>Puts a line in the status area. For the page's catch blocks, so a failure from a click handler is shown, not swallowed.</summary>
    public void Report(string text) => StatusText = text;

    /// <summary>The outcome of the last <see cref="SubmitAsync"/>.</summary>
    public LibrarySubmitOutcome LastOutcome { get; private set; } = LibrarySubmitOutcome.Empty;

    /// <summary>Grid or list. Setting it persists it.</summary>
    public LibraryView View
    {
        get => _view;
        set
        {
            if (_view == value) return;
            _view = value;
            OnChanged();
            OnChanged(nameof(IsGridView));
            OnChanged(nameof(IsListView));
            Persist(() => _settings.SetLibraryViewAsync(value == LibraryView.List ? "list" : "grid"));
        }
    }

    /// <summary>The Grid radio. Setting it true selects the grid; setting it false is the radio group un-checking it and changes nothing.</summary>
    public bool IsGridView
    {
        get => _view == LibraryView.Grid;
        set { if (value) View = LibraryView.Grid; }
    }

    /// <summary>The List radio, the mirror of <see cref="IsGridView"/>.</summary>
    public bool IsListView
    {
        get => _view == LibraryView.List;
        set { if (value) View = LibraryView.List; }
    }

    /// <summary>The selected kind chips. Setting it re-filters and persists it.</summary>
    public LibraryFilter Filters
    {
        get => _filters;
        set
        {
            if (_filters == value) return;
            _filters = value;
            OnChanged();
            RaiseChips();
            ApplyFilter();
            Persist(() => _settings.SetLibraryFiltersAsync(FormatFilters(value)));
        }
    }

    /// <summary>The Recent chip. The check glyph on the chip binds to this, never to a style trigger on the button.</summary>
    public bool IsRecentOn
    {
        get => _filters.HasFlag(LibraryFilter.Recent);
        set => SetFilter(LibraryFilter.Recent, value);
    }

    public bool IsFavouritesOn
    {
        get => _filters.HasFlag(LibraryFilter.Favourites);
        set => SetFilter(LibraryFilter.Favourites, value);
    }

    public bool IsPrivateServersOn
    {
        get => _filters.HasFlag(LibraryFilter.PrivateServers);
        set => SetFilter(LibraryFilter.PrivateServers, value);
    }

    private void SetFilter(LibraryFilter flag, bool on)
        => Filters = on ? _filters | flag : _filters & ~flag;

    private void RaiseChips()
    {
        OnChanged(nameof(IsRecentOn));
        OnChanged(nameof(IsFavouritesOn));
        OnChanged(nameof(IsPrivateServersOn));
    }

    /// <summary>Instant local filter on <see cref="LibraryEntry.Name"/> and <see cref="LibraryEntry.LocalName"/>.</summary>
    public string Query
    {
        get => _query;
        set
        {
            value ??= string.Empty;
            if (_query == value) return;
            _query = value;
            OnChanged();
            // Roblox results answer the text that was submitted; once the text changes they are stale.
            if (FromRoblox.Count > 0) FromRoblox.Clear();
            ApplyFilter();
        }
    }

    /// <summary>Reads the persisted view and chips, then the catalog. Awaited by the page on every visit.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _view = ParseView(await _settings.GetLibraryViewAsync().ConfigureAwait(true));
        _filters = ParseFilters(await _settings.GetLibraryFiltersAsync().ConfigureAwait(true));
        OnChanged(nameof(View));
        OnChanged(nameof(IsGridView));
        OnChanged(nameof(IsListView));
        OnChanged(nameof(Filters));
        RaiseChips();
        AttachIcons();
        await ReloadAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Re-reads the catalog and re-applies the chips and query. Does not notify the libraryChanged callback.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        _entries = await _catalog.ListAsync(ct).ConfigureAwait(true);
        var live = _entries.Select(LibraryItem.KeyOf).ToHashSet();
        foreach (var gone in _itemCache.Keys.Where(k => !live.Contains(k)).ToList()) _itemCache.Remove(gone);
        ApplyFilter();
    }

    public static LibraryView ParseView(string? stored)
        => string.Equals(stored?.Trim(), "list", StringComparison.OrdinalIgnoreCase) ? LibraryView.List : LibraryView.Grid;

    public static LibraryFilter ParseFilters(string? stored)
    {
        var result = LibraryFilter.None;
        foreach (var token in (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token is not null
                && !long.TryParse(token, out _)
                && Enum.TryParse<LibraryFilter>(token, ignoreCase: true, out var one)
                && one != LibraryFilter.None
                && Enum.IsDefined(one))
            {
                result |= one;
            }
        }

        return result;
    }

    public static string FormatFilters(LibraryFilter filters)
        => string.Join(',', new[] { LibraryFilter.Recent, LibraryFilter.Favourites, LibraryFilter.PrivateServers }
            .Where(f => filters.HasFlag(f)).Select(f => f.ToString()));

    private static bool Matches(LibraryEntry e, LibraryFilter filters)
    {
        if (filters == LibraryFilter.None) return true;
        return e.Kind switch
        {
            LibraryEntryKind.Recent => filters.HasFlag(LibraryFilter.Recent),
            LibraryEntryKind.Favourite => filters.HasFlag(LibraryFilter.Favourites),
            LibraryEntryKind.PrivateServer => filters.HasFlag(LibraryFilter.PrivateServers),
            _ => true,
        };
    }

    private void ApplyFilter()
    {
        var q = _query.Trim();
        var next = new List<LibraryEntry>(_entries.Count);
        foreach (var e in _entries)
        {
            if (!Matches(e, _filters)) continue;
            if (q.Length > 0
                && !e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !(e.LocalName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            next.Add(e);
        }

        // Touch the collection only where it differs, so a keystroke that changes nothing repaints nothing.
        if (Visible.Count == next.Count && Visible.SequenceEqual(next))
        {
            RaiseShape();
            return;
        }

        Visible.Clear();
        foreach (var e in next) Visible.Add(e);
        SyncItems();
        RaiseShape();
    }

    /// <summary>
    /// Makes <see cref="Items"/> match <see cref="Visible"/> with the fewest moves: items that stay keep their
    /// instance (and their realised tile); only the ones that enter or leave are added or removed.
    /// </summary>
    private void SyncItems()
    {
        var next = new List<LibraryItem>(Visible.Count);
        foreach (var e in Visible)
        {
            var key = LibraryItem.KeyOf(e);
            if (_itemCache.TryGetValue(key, out var item)) item.Update(e);
            else _itemCache[key] = item = new LibraryItem(e);
            next.Add(item);
        }

        var keep = next.ToHashSet();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Items[i])) Items.RemoveAt(i);
        }

        for (var i = 0; i < next.Count; i++)
        {
            if (i < Items.Count && ReferenceEquals(Items[i], next[i])) continue;
            var at = Items.IndexOf(next[i]);
            if (at >= 0) Items.Move(at, i);
            else Items.Insert(i, next[i]);
        }
    }

    // ---- icons: a tile that comes on screen asks for its own place, and nothing else is fetched ----

    private void AttachIcons()
    {
        if (_icons is null || _iconsAttached) return;
        _iconsAttached = true;
        _icons.IconResolved += OnIconResolved;
    }

    /// <summary>Stops listening to the icon cache. The page calls it when it leaves the screen.</summary>
    public void Detach()
    {
        if (_icons is null || !_iconsAttached) return;
        _iconsAttached = false;
        _icons.IconResolved -= OnIconResolved;
    }

    /// <summary>A tile was realised: if it has no icon, ask the cache for that one place. Does nothing for a tile that is not on screen, because only a realised tile calls it.</summary>
    public void NoteOnScreen(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_icons is null || !string.IsNullOrWhiteSpace(item.IconUrl)) return;
        _icons.Request(item.PlaceId);
    }

    private void OnIconResolved(object? sender, GameIconResolvedEventArgs e)
    {
        void Apply()
        {
            foreach (var item in _itemCache.Values)
            {
                if (item.PlaceId == e.PlaceId && string.IsNullOrWhiteSpace(item.IconUrl)) item.IconUrl = e.Info.IconUrl;
            }
        }

        if (_ui is null) Apply(); else _ui.Invoke(Apply);
    }

    /// <summary>
    /// Enter in the one box. A paste is decided by <see cref="LibraryPaste"/>; plain text that matches
    /// something local does nothing remote; plain text that matches nothing searches Roblox into
    /// <see cref="FromRoblox"/>.
    /// </summary>
    public async Task<LibrarySubmitOutcome> SubmitAsync()
    {
        var text = _query.Trim();
        if (text.Length == 0)
        {
            StatusText = Loc.Get("Shell_Games_TypeToSearch");
            return LastOutcome = LibrarySubmitOutcome.Empty;
        }

        try
        {
            var verdict = LibraryPaste.Classify(text);
            switch (verdict.Kind)
            {
                case LibraryPasteKind.Rejected:
                    StatusText = Loc.Get("Shell_Games_NoPlaceId");
                    return LastOutcome = LibrarySubmitOutcome.NotUnderstood;

                case LibraryPasteKind.Place:
                    return LastOutcome = await AddGameAsync(verdict.PlaceId).ConfigureAwait(true);

                case LibraryPasteKind.PrivateServer:
                    return LastOutcome = await AddServerAsync(text).ConfigureAwait(true);
            }

            if (Visible.Count > 0)
            {
                StatusText = string.Empty;
                return LastOutcome = LibrarySubmitOutcome.LocalMatch;
            }

            StatusText = Loc.Format("Shell_Games_Searching", text);
            var results = await _api.SearchGamesAsync(text).ConfigureAwait(true);
            FromRoblox.Clear();
            foreach (var r in results) FromRoblox.Add(r);
            if (results.Count == 0)
            {
                StatusText = Loc.Format("Shell_Games_NoResults", text);
                return LastOutcome = LibrarySubmitOutcome.NotUnderstood;
            }

            StatusText = Loc.Plural("Shell_Games_FoundMatches", results.Count);
            return LastOutcome = LibrarySubmitOutcome.Searched;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = Loc.Format("Shell_Games_CouldntAdd", ex.Message);
            return LastOutcome = LibrarySubmitOutcome.Failed;
        }
    }

    private async Task<LibrarySubmitOutcome> AddGameAsync(long placeId)
    {
        StatusText = Loc.Get("Shell_Games_LookingUp");
        var meta = await _api.GetGameMetadataByPlaceIdAsync(placeId).ConfigureAwait(true);
        if (meta is null)
        {
            StatusText = Loc.Format("Shell_Games_NoMetadata", placeId);
            return LibrarySubmitOutcome.NotUnderstood;
        }

        await _favorites.AddAsync(meta.PlaceId, meta.UniverseId, meta.Name, meta.IconUrl).ConfigureAwait(true);
        await AfterAddAsync(meta.Name).ConfigureAwait(true);
        return LibrarySubmitOutcome.AddedGame;
    }

    private async Task<LibrarySubmitOutcome> AddServerAsync(string text)
    {
        StatusText = Loc.Get("Shell_Games_LookingUp");
        var target = await _resolveShareUrl(text).ConfigureAwait(true);
        if (target is not LaunchTarget.PrivateServer ps)
        {
            StatusText = Loc.Get("Shell_Games_NoPlaceId");
            return LibrarySubmitOutcome.NotUnderstood;
        }

        var meta = await _api.GetGameMetadataByPlaceIdAsync(ps.PlaceId).ConfigureAwait(true);
        if (meta is null)
        {
            StatusText = Loc.Format("Shell_Games_NoMetadata", ps.PlaceId);
            return LibrarySubmitOutcome.NotUnderstood;
        }

        // Name defaults to the place name, as JoinByLinkSave and the Squad window do; rename later from the Library.
        await _servers.AddAsync(ps.PlaceId, ps.Code, ps.Kind, meta.Name, meta.Name, meta.IconUrl).ConfigureAwait(true);
        await AfterAddAsync(meta.Name).ConfigureAwait(true);
        return LibrarySubmitOutcome.AddedServer;
    }

    /// <summary>Adds a "From Roblox" search result, the existing Add affordance.</summary>
    public async Task AddSearchResultAsync(GameSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            await _favorites.AddAsync(result.PlaceId, result.UniverseId, result.Name, result.IconUrl).ConfigureAwait(true);
            await AfterAddAsync(result.Name).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = Loc.Format("Shell_Games_CouldntAdd", ex.Message);
        }
    }

    private async Task AfterAddAsync(string name)
    {
        Query = string.Empty;
        await ReloadAsync().ConfigureAwait(true);
        StatusText = Loc.Format("Shell_Games_Added", name);
        _libraryChanged?.Invoke();
    }

    /// <summary>
    /// Plays an entry as <paramref name="accountId"/>, or with null as the main account, else the only
    /// account, else the first row. False when there is no account to launch.
    /// </summary>
    public async Task<bool> PlayAsync(LibraryEntry entry, Guid? accountId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = accountId ?? ResolveDefaultAccount();
        if (id is null)
        {
            StatusText = Loc.Get("Library_NoAccount");
            return false;
        }

        await _launch(id.Value, entry.Target).ConfigureAwait(true);
        return true;
    }

    /// <summary>Main, then the only account, then the first row. Null on an empty roster.</summary>
    internal Guid? ResolveDefaultAccount()
    {
        var accounts = _accounts();
        if (accounts.Count == 0) return null;
        var main = accounts.FirstOrDefault(a => a.IsMain);
        if (main is not null) return main.Id;
        return accounts[0].Id;
    }

    // ---- the five management passthroughs: thin, then reload ----

    public Task SetDefaultAsync(LibraryEntry e) => ThenReloadAsync(() => e.Kind == LibraryEntryKind.PrivateServer && e.PrivateServerId is { } id
        ? _servers.SetDefaultAsync(id)
        : _favorites.SetDefaultAsync(e.PlaceId));

    public Task ClearDefaultAsync(LibraryEntry e) => ThenReloadAsync(() => e.Kind == LibraryEntryKind.PrivateServer
        ? _servers.ClearDefaultAsync()
        : _favorites.ClearDefaultAsync());

    /// <summary>Sets the local name; null or blank resets it.</summary>
    public Task RenameAsync(LibraryEntry e, string? newName) => ThenReloadAsync(() => e.Kind == LibraryEntryKind.PrivateServer && e.PrivateServerId is { } id
        ? _servers.UpdateLocalNameAsync(id, newName)
        : _favorites.UpdateLocalNameAsync(e.PlaceId, newName));

    public Task ResetNameAsync(LibraryEntry e) => RenameAsync(e, null);

    public Task RemoveAsync(LibraryEntry e) => ThenReloadAsync(() => e.Kind == LibraryEntryKind.PrivateServer && e.PrivateServerId is { } id
        ? _servers.RemoveAsync(id)
        : _favorites.RemoveAsync(e.PlaceId));

    // ---- the themed remove confirm: an in-page bar, not a window and not a stock dialog ----

    /// <summary>The entry the user asked to remove and has not yet confirmed. Null when no confirm is open.</summary>
    public LibraryItem? PendingRemoval
    {
        get => _pendingRemoval;
        private set
        {
            if (ReferenceEquals(_pendingRemoval, value)) return;
            _pendingRemoval = value;
            OnChanged();
            OnChanged(nameof(IsRemoveConfirmOpen));
            OnChanged(nameof(RemoveConfirmText));
        }
    }

    public bool IsRemoveConfirmOpen => _pendingRemoval is not null;

    /// <summary>"Remove {name} from your saved games?" (or servers). Empty while no confirm is open.</summary>
    public string RemoveConfirmText => _pendingRemoval is null
        ? string.Empty
        : Loc.Format(_pendingRemoval.IsPrivate ? "Shell_Games_RemoveServerConfirm" : "Shell_Games_RemoveGameConfirm", _pendingRemoval.DisplayName);

    /// <summary>Opens the confirm for a saved entry. A Recent place is not saved, so there is nothing to remove and nothing opens.</summary>
    public void RequestRemove(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsSaved) PendingRemoval = item;
    }

    public void CancelRemove() => PendingRemoval = null;

    /// <summary>Removes the pending entry. The confirm closes first, so a double click cannot remove twice.</summary>
    public async Task ConfirmRemoveAsync()
    {
        if (_pendingRemoval is not { } item) return;
        PendingRemoval = null;
        try
        {
            await RemoveAsync(item.Entry).ConfigureAwait(true);
            StatusText = Loc.Get("Shell_Games_Removed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = Loc.Format("Shell_Games_CouldntRemove", ex.Message);
        }
    }

    private async Task ThenReloadAsync(Func<Task> mutate)
    {
        await mutate().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
        _libraryChanged?.Invoke();
    }

    private Task _persisting = Task.CompletedTask;

    /// <summary>Completes when every view or chip write queued so far has landed (or failed quietly). For tests and shutdown.</summary>
    internal Task PersistenceSettled => _persisting;

    // Writes run in order, so two quick toggles land as the user left them.
    private void Persist(Func<Task> write)
        => _persisting = RunAfterAsync(_persisting, write);

    private static async Task RunAfterAsync(Task previous, Func<Task> write)
    {
        await previous.ConfigureAwait(false);
        try { await write().ConfigureAwait(false); }
        catch (Exception) { /* a view preference is not worth surfacing a failed write */ }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
