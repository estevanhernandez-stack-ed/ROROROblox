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

    private IReadOnlyList<LibraryEntry> _entries = [];
    private LibraryView _view = LibraryView.Grid;
    private LibraryFilter _filters = LibraryFilter.None;
    private string _query = string.Empty;
    private string _statusText = string.Empty;

    /// <param name="resolveShareUrl">The <c>MainViewModel.ResolveShareUrlAsync</c> delegate: needs a non-expired cookie for the share-token form.</param>
    /// <param name="accounts">The roster, read off <c>MainViewModel.AccountsSnapshot</c> (never <c>Accounts</c>).</param>
    /// <param name="launch">Launches an account by id into a target (<c>MainViewModel.LaunchAccountByIdAsync</c>).</param>
    /// <param name="libraryChanged">Fired after a mutation, so the main window's reload runs.</param>
    public LibraryViewModel(
        LibraryCatalog catalog,
        IFavoriteGameStore favorites,
        IPrivateServerStore servers,
        IRobloxApi api,
        IAppSettings settings,
        Func<string, Task<LaunchTarget?>> resolveShareUrl,
        Func<IReadOnlyList<AccountSummary>> accounts,
        Func<Guid, LaunchTarget, Task> launch,
        Action? libraryChanged = null)
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
            Persist(() => _settings.SetLibraryViewAsync(value == LibraryView.List ? "list" : "grid"));
        }
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
            ApplyFilter();
            Persist(() => _settings.SetLibraryFiltersAsync(FormatFilters(value)));
        }
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
            ApplyFilter();
        }
    }

    /// <summary>Reads the persisted view and chips, then the catalog. Awaited by the page on every visit.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _view = ParseView(await _settings.GetLibraryViewAsync().ConfigureAwait(true));
        _filters = ParseFilters(await _settings.GetLibraryFiltersAsync().ConfigureAwait(true));
        OnChanged(nameof(View));
        OnChanged(nameof(Filters));
        await ReloadAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Re-reads the catalog and re-applies the chips and query. Does not notify the libraryChanged callback.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        _entries = await _catalog.ListAsync(ct).ConfigureAwait(true);
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
        if (Visible.Count == next.Count && Visible.SequenceEqual(next)) return;
        Visible.Clear();
        foreach (var e in next) Visible.Add(e);
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
        if (id is null) return false;
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
