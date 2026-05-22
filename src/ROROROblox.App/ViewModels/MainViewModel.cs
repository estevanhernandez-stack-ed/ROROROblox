using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.About;
using ROROROblox.App.Diagnostics;
using ROROROblox.App.History;
using ROROROblox.App.Friends;
using ROROROblox.App.JoinByLink;
using ROROROblox.App.Modals;
using ROROROblox.App.Settings;
using ROROROblox.App.SquadLaunch;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// Orchestrates the main-window flows: Add Account, Launch As, Remove, Re-authenticate.
/// Coordinates <see cref="ICookieCapture"/> + <see cref="IRobloxApi"/> + <see cref="IAccountStore"/>
/// + <see cref="IRobloxLauncher"/>; surfaces error modals for the four spec §7 buckets.
/// </summary>
internal sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ICookieCapture _cookieCapture;
    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly IRobloxLauncher _launcher;
    private readonly IRobloxCompatChecker _compatChecker;
    private readonly IAppSettings _settings;
    private readonly IFavoriteGameStore _favorites;
    private readonly IRobloxProcessTracker _processTracker;
    private readonly IPresenceService _presenceService;
    private readonly IDiagnosticsCollector _diagnostics;
    private readonly IPrivateServerStore _privateServerStore;
    private readonly ISessionHistoryStore _sessionHistory;
    private readonly Startup.IStartupRegistration _startupRegistration;
    private readonly Core.Theming.IThemeStore _themeStore;
    private readonly Theming.ThemeService _themeService;
    private readonly Tray.RobloxWindowDecorator _windowDecorator;
    private readonly IBloxstrapDetector _bloxstrapDetector;
    private readonly IRobloxUpdateProbe _updateProbe;
    private readonly Core.Transport.IAccountTransport _accountTransport;
    private readonly ILogger<MainViewModel> _log;

    /// <summary>
    /// In-flight session-history rows keyed by account id. Populated when LaunchAccountAsync
    /// succeeds; consumed by OnProcessExited / OnProcessAttachFailed to stamp end / outcome.
    /// In-memory only — restart loses pending end-stamps, but the launched-at row is already
    /// persisted via <see cref="ISessionHistoryStore.AddAsync"/>.
    /// </summary>
    private readonly Dictionary<Guid, Guid> _pendingSessionByAccountId = new();

    /// <summary>
    /// Live appStorage identity defenders keyed by account id (v1.6.0 item 9). Tracked
    /// per-account (not fire-and-forget) so <see cref="OnProcessAttached"/> can find the
    /// right defender and call <see cref="AppStorageDefender.NotifyConsumed"/> once the
    /// client is up. Entries are removed when the defender disposes (cap fallback or
    /// post-attach grace). The defender's own <c>_active</c> takeover still cancels a prior
    /// launch's defender when a newer launch dispatches.
    /// </summary>
    private readonly Dictionary<Guid, AppStorageDefender> _defendersByAccountId = new();
    private readonly object _defendersLock = new();
    private readonly DispatcherTimer _ticker;

    private string _statusBanner = string.Empty;
    private string? _robloxCompatBanner;
    private bool _bloxstrapWarningVisible;
    private bool _isBusy;
    private bool _robloxUpdating;
    private int _liveProcessCount;

    public MainViewModel(
        ICookieCapture cookieCapture,
        IRobloxApi api,
        IAccountStore accountStore,
        IRobloxLauncher launcher,
        IRobloxCompatChecker compatChecker,
        IAppSettings settings,
        IFavoriteGameStore favorites,
        IRobloxProcessTracker processTracker,
        IPresenceService presenceService,
        IDiagnosticsCollector diagnostics,
        IPrivateServerStore privateServerStore,
        ISessionHistoryStore sessionHistory,
        Startup.IStartupRegistration startupRegistration,
        Core.Theming.IThemeStore themeStore,
        Theming.ThemeService themeService,
        Tray.RobloxWindowDecorator windowDecorator,
        IBloxstrapDetector bloxstrapDetector,
        IRobloxUpdateProbe updateProbe,
        Core.Transport.IAccountTransport accountTransport,
        ILogger<MainViewModel>? log = null)
    {
        _cookieCapture = cookieCapture;
        _api = api;
        _accountStore = accountStore;
        _launcher = launcher;
        _compatChecker = compatChecker;
        _settings = settings;
        _favorites = favorites;
        _processTracker = processTracker;
        _presenceService = presenceService;
        _diagnostics = diagnostics;
        _privateServerStore = privateServerStore;
        _sessionHistory = sessionHistory;
        _startupRegistration = startupRegistration;
        _themeStore = themeStore;
        _themeService = themeService;
        _windowDecorator = windowDecorator;
        _bloxstrapDetector = bloxstrapDetector;
        _updateProbe = updateProbe;
        _accountTransport = accountTransport;
        _log = log ?? NullLogger<MainViewModel>.Instance;

        AddAccountCommand = new RelayCommand(AddAccountAsync, () => !IsBusy);
        LaunchAccountCommand = new RelayCommand(p => LaunchAccountAsync(p as AccountSummary));
        RemoveAccountCommand = new RelayCommand(p => RemoveAccountAsync(p as AccountSummary));
        ReauthenticateCommand = new RelayCommand(p => ReauthenticateAsync(p as AccountSummary));
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LaunchAllCommand = new RelayCommand(LaunchAllAsync, () => !IsBusy && Accounts.Any(a => a.IsSelected && !a.SessionExpired && !(a.InGame || a.IsRunning)));
        StopAccountCommand = new RelayCommand(p => StopAccount(p as AccountSummary));
        OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
        OpenAboutCommand = new RelayCommand(OpenAbout);
        OpenSquadLaunchCommand = new RelayCommand(OpenSquadLaunchAsync, () => !IsBusy && Accounts.Count > 0);
        OpenFriendFollowCommand = new RelayCommand(p => OpenFriendFollowAsync(p as AccountSummary));
        SetMainCommand = new RelayCommand(p => SetMainAsync(p as AccountSummary));
        ToggleCompactCommand = new RelayCommand(ToggleCompact);
        StartMainCommand = new RelayCommand(StartMainAsync, () => !IsBusy && Accounts.FirstOrDefault(a => a.IsMain) is { SessionExpired: false, IsRunning: false, InGame: false });
        OpenHistoryCommand = new RelayCommand(OpenHistory);
        OpenPreferencesCommand = new RelayCommand(OpenPreferences);
        OpenPluginsCommand = new RelayCommand(_ => RequestOpenPlugins?.Invoke(this, EventArgs.Empty));
        DismissBloxstrapWarningCommand = new RelayCommand(_ => _ = DismissBloxstrapWarningAsync());

        // v1.3.x — default-game widget + rename overlay commands.
        SetDefaultGameCommand = new RelayCommand(p => _ = SetDefaultGameAsync(p as FavoriteGame));
        // RenameItemCommand / ResetItemNameCommand take the row's data context (FavoriteGame /
        // AccountSummary / SavedPrivateServer) as CommandParameter — saves writing 6 commands or
        // threading RenameTarget through XAML constructor binding gymnastics.
        RenameItemCommand = new RelayCommand(p => _ = RenameItemAsync(BuildRenameTarget(p)));
        ResetItemNameCommand = new RelayCommand(p => _ = ResetItemNameAsync(BuildRenameTarget(p)));
        RemoveGameCommand = new RelayCommand(p => _ = RemoveGameAsync(p as FavoriteGame));

        // Subscribe to favorites' default-changed event so the widget readout updates without a
        // manual re-fetch. Fires after SetDefaultAsync mutates + persists, on real change only.
        _favorites.DefaultChanged += OnFavoritesDefaultChanged;

        _processTracker.ProcessAttached += OnProcessAttached;
        _processTracker.ProcessExited += OnProcessExited;
        _processTracker.ProcessAttachFailed += OnProcessAttachFailed;

        // v1.5.0 presence — server-truth running state for display (the ghost fix). Events may
        // arrive on threadpool threads (the poller runs up to 4 concurrent), so the handlers
        // marshal to the dispatcher just like the process-tracker handlers do.
        _presenceService.AccountPresenceUpdated += OnAccountPresenceUpdated;
        _presenceService.AccountSessionExpired += OnAccountSessionExpired;

        _ = InitializeBloxstrapWarningAsync();

        // Tick once a minute to keep "5 min ago" / "Running for 12 min" current.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _ticker.Tick += (_, _) =>
        {
            foreach (var summary in Accounts)
            {
                summary.RefreshRelativeTimes();
            }
        };
        _ticker.Start();
    }

    public ObservableCollection<AccountSummary> Accounts { get; } = [];

    /// <summary>
    /// Sentinel entry the per-row ComboBox treats as "open the Join-by-link modal."
    /// PlaceId == 0 is the marker; <see cref="IsJoinByLinkSentinel"/> is the typed predicate.
    /// MainWindow's ComboBox SelectionChanged handler intercepts this and reverts the row's
    /// SelectedGame after firing <see cref="OpenJoinByLinkAsync"/>.
    /// </summary>
    public static FavoriteGame JoinByLinkSentinel { get; } = new FavoriteGame(
        PlaceId: 0,
        UniverseId: 0,
        Name: "(Paste a link...)",
        ThumbnailUrl: string.Empty,
        IsDefault: false,
        AddedAt: DateTimeOffset.MinValue);

    /// <summary>True if <paramref name="game"/> is the Join-by-link sentinel, NOT a real saved game.</summary>
    public static bool IsJoinByLinkSentinel(FavoriteGame? game) => game is { PlaceId: 0 };

    /// <summary>
    /// Pure mapping from a row's picker selection to the concrete <see cref="LaunchTarget"/> the
    /// launcher dispatches. Extracted from <c>LaunchAccountAsync</c> so the precedence is
    /// unit-testable without standing up the VM. v1.6.0.
    /// </summary>
    /// <remarks>
    /// Precedence:
    /// <list type="number">
    ///   <item>Explicit override (Squad Launch / Friend Follow / Join-by-Link) trumps everything.</item>
    ///   <item>A PS-carrying <see cref="FavoriteGame"/> entry -> <see cref="LaunchTarget.PrivateServer"/>
    ///   (checked BEFORE the plain Place case — a PS entry has PlaceId &gt; 0 too).</item>
    ///   <item>A plain saved game (PlaceId &gt; 0, no PS code) -> <see cref="LaunchTarget.Place"/>.</item>
    ///   <item>Null / the JoinByLink sentinel (PlaceId == 0) -> <see cref="LaunchTarget.DefaultGame"/>,
    ///   which the launcher resolves from favorites + settings.</item>
    /// </list>
    /// </remarks>
    public static LaunchTarget ResolveLaunchTarget(FavoriteGame? selected, LaunchTarget? overrideTarget)
    {
        if (overrideTarget is not null)
        {
            return overrideTarget;
        }

        if (selected is { PlaceId: > 0, IsPrivateServer: true } ps)
        {
            return new LaunchTarget.PrivateServer(
                ps.PlaceId,
                ps.PrivateServerCode!,
                ps.PrivateServerCodeKind ?? PrivateServerCodeKind.LinkCode);
        }

        if (selected is { PlaceId: > 0 } sg)
        {
            return new LaunchTarget.Place(sg.PlaceId);
        }

        return new LaunchTarget.DefaultGame();
    }

    /// <summary>
    /// Pure match predicate for the v1.6.0 tag filter (item 7b). An account is shown when the
    /// (outer-trimmed) filter is a case-insensitive substring of ANY of its <paramref name="tags"/>
    /// OR of its <paramref name="renderName"/>. An empty/whitespace filter matches everything.
    /// Extracted from the per-row <c>IsFilteredOut</c> wiring so the rules are unit-testable
    /// without standing up the VM or WPF.
    /// </summary>
    /// <param name="tags">The account's tags. Null is treated as no tags.</param>
    /// <param name="renderName">The account's display label (LocalName ?? DisplayName).</param>
    /// <param name="filter">Raw filter-box text. Only the outer whitespace is trimmed.</param>
    public static bool AccountMatchesFilter(IEnumerable<string> tags, string renderName, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }
        var needle = filter.Trim();
        if (!string.IsNullOrEmpty(renderName) &&
            renderName.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (tags is null)
        {
            return false;
        }
        foreach (var tag in tags)
        {
            if (!string.IsNullOrEmpty(tag) &&
                tag.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Land-at-home guard for the follow paths (v1.6.0 item 8). A follow may only fire when the
    /// target is in a <em>joinable</em> place — <see cref="UserPresenceType.InGame"/> AND a place id
    /// is actually visible to us. A friend can be InGame yet expose a null/zero
    /// <see cref="UserPresence.PlaceId"/> because their join/visibility privacy hides the server; in
    /// that case <c>RequestFollowUser</c> gets server-rejected and the launcher silently bounces to
    /// the Roblox home page. Every non-joinable shape (privacy-hidden InGame, online-not-in-game,
    /// in Studio, offline, invisible, or no presence at all) is treated uniformly: do NOT launch,
    /// surface a plain message instead.
    /// <para>
    /// Pure so both follow surfaces (the Friends modal and the follow-an-alt path) share the exact
    /// same decision and can't drift apart.
    /// </para>
    /// </summary>
    /// <param name="presence">The target's presence snapshot, or null when none could be read.</param>
    /// <param name="targetName">The target's display name, for the user-facing message.</param>
    public static FollowDecision EvaluateFollow(UserPresence? presence, string targetName)
    {
        var name = string.IsNullOrWhiteSpace(targetName) ? "that friend" : targetName;

        // Joinable == InGame AND a real place id we can actually see. PlaceId is populated only when
        // InGame AND the target's privacy lets the requesting cookie's owner see the server; a
        // null/zero place id means "InGame, but no joinable place visible to us."
        if (presence is { PresenceType: UserPresenceType.InGame, PlaceId: > 0 })
        {
            return FollowDecision.Allow();
        }

        return FollowDecision.Block(
            $"Can't follow {name} — they're not in a joinable game right now (or their join privacy is off).");
    }

    /// <summary>
    /// Games available for the per-account picker on each row. Synced from the favorites store
    /// at LoadAsync time and again every time the Games dialog closes. Always ends with the
    /// <see cref="JoinByLinkSentinel"/> "(Paste a link...)" entry.
    /// </summary>
    public ObservableCollection<FavoriteGame> AvailableGames { get; } = [];

    public ICommand AddAccountCommand { get; }
    public ICommand LaunchAccountCommand { get; }
    public ICommand RemoveAccountCommand { get; }
    public ICommand ReauthenticateCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand LaunchAllCommand { get; }
    public ICommand StopAccountCommand { get; }
    public ICommand OpenDiagnosticsCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand OpenSquadLaunchCommand { get; }
    public ICommand OpenFriendFollowCommand { get; }
    public ICommand SetMainCommand { get; }
    public ICommand ToggleCompactCommand { get; }
    public ICommand StartMainCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand OpenPreferencesCommand { get; }
    public ICommand OpenPluginsCommand { get; }
    public ICommand DismissBloxstrapWarningCommand { get; }

    public event EventHandler? RequestOpenPlugins;

    // v1.3.x default-game widget + rename overlay.
    public ICommand SetDefaultGameCommand { get; }
    public ICommand RenameItemCommand { get; }
    public ICommand ResetItemNameCommand { get; }
    public ICommand RemoveGameCommand { get; }

    /// <summary>
    /// Saved games for the default-game widget dropdown. Same content as
    /// <see cref="AvailableGames"/> minus the JoinByLink sentinel. Updated alongside
    /// AvailableGames in <see cref="ReloadGamesAsync"/>. Empty when no games are saved —
    /// the widget XAML's empty-state trigger reads this length. v1.3.x.
    /// </summary>
    public ObservableCollection<FavoriteGame> WidgetGames { get; } = [];

    private bool _isDefaultGameDropdownOpen;
    /// <summary>Two-way bound to the widget's ToggleButton/Popup. v1.3.x.</summary>
    public bool IsDefaultGameDropdownOpen
    {
        get => _isDefaultGameDropdownOpen;
        set => SetField(ref _isDefaultGameDropdownOpen, value);
    }

    private FavoriteGame? _currentDefaultGame;
    /// <summary>The currently-default <see cref="FavoriteGame"/>, or null when no games saved.
    /// One-way bound on the widget popup ListBox to highlight the current default. v1.3.x.</summary>
    public FavoriteGame? CurrentDefaultGame
    {
        get => _currentDefaultGame;
        private set
        {
            if (SetField(ref _currentDefaultGame, value))
            {
                OnPropertyChanged(nameof(DefaultGameDisplay));
            }
        }
    }

    /// <summary>
    /// What the widget shows in its toolbar readout. Reads <see cref="FavoriteGame.LocalName"/>
    /// when set, falling back to <see cref="FavoriteGame.Name"/>, then to a muted placeholder
    /// when no games are saved. v1.3.x.
    /// </summary>
    public string DefaultGameDisplay =>
        _currentDefaultGame?.LocalName ?? _currentDefaultGame?.Name ?? "No saved games yet";

    private bool _isCompact;
    /// <summary>True when the main window is in compact (collapsed) mode. Drives the bottom-bar
    /// button label, the column visibility on the row template, and the empty-state surface.</summary>
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (SetField(ref _isCompact, value))
            {
                OnPropertyChanged(nameof(CompactToggleLabel));
                OnPropertyChanged(nameof(CompactRows));
                OnPropertyChanged(nameof(HasCompactRows));
                OnPropertyChanged(nameof(MainAccount));
                OnPropertyChanged(nameof(CompactEmptyKind));
            }
        }
    }

    public string CompactToggleLabel => _isCompact ? "Expand" : "Compact";

    private string _accountFilter = string.Empty;
    /// <summary>
    /// Tag/name filter text bound to the filter box above the account list (v1.6.0, item 7b).
    /// On change, every account's <see cref="AccountSummary.IsFilteredOut"/> is recomputed via
    /// <see cref="AccountMatchesFilter"/> — the row container's Visibility binds to that flag, so
    /// the underlying <see cref="Accounts"/> collection and its order are NEVER touched (this is
    /// what keeps drag-to-reorder index math intact, vs a CollectionViewSource filter). While a
    /// filter is active (<see cref="IsFilterActive"/>) the drag handlers no-op, so filtering and
    /// reordering can't fight each other.
    /// </summary>
    public string AccountFilter
    {
        get => _accountFilter;
        set
        {
            if (SetField(ref _accountFilter, value))
            {
                OnPropertyChanged(nameof(IsFilterActive));
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// True when a non-empty filter is in effect. The drag-reorder handlers read this to disable
    /// reordering while filtered (clearing the filter restores it). v1.6.0.
    /// </summary>
    public bool IsFilterActive => !string.IsNullOrWhiteSpace(_accountFilter);

    /// <summary>
    /// Recompute <see cref="AccountSummary.IsFilteredOut"/> for every account against the current
    /// <see cref="AccountFilter"/>. Empty/whitespace filter clears the flag on all rows. Called on
    /// every filter change and after the account list (re)loads. v1.6.0.
    /// </summary>
    private void ApplyFilter()
    {
        foreach (var summary in Accounts)
        {
            summary.IsFilteredOut = !AccountMatchesFilter(summary.Tags, summary.RenderName, _accountFilter);
        }
    }

    /// <summary>Account designated as the user's main, if any. Used by the compact-mode CTA + tray hooks.</summary>
    public AccountSummary? MainAccount => Accounts.FirstOrDefault(a => a.IsMain);

    /// <summary>Subset of accounts shown in compact mode — only ones currently running or launching.</summary>
    public IEnumerable<AccountSummary> CompactRows =>
        Accounts.Where(a => a.IsRunning || a.IsLaunching);

    public bool HasCompactRows => CompactRows.Any();

    /// <summary>
    /// Empty-state for compact mode. Three discrete states keep the empty area from looking broken:
    ///   <c>StartMain</c> — main is set, idle: show "Start [Username]" CTA.
    ///   <c>NoMainPicked</c> — accounts exist but none is main: show "Pick a main →" hint.
    ///   <c>NoAccounts</c> — no accounts saved at all: show "+ Add your first account" CTA.
    /// </summary>
    public CompactEmptyState CompactEmptyKind
    {
        get
        {
            if (Accounts.Count == 0) return CompactEmptyState.NoAccounts;
            return MainAccount is null ? CompactEmptyState.NoMainPicked : CompactEmptyState.StartMain;
        }
    }

    /// <summary>How many tracked Roblox client processes are currently alive.</summary>
    public int LiveProcessCount
    {
        get => _liveProcessCount;
        private set
        {
            if (SetField(ref _liveProcessCount, value))
            {
                OnPropertyChanged(nameof(LiveProcessSummary));
            }
        }
    }

    /// <summary>Footer text — e.g. "3 Roblox clients running" / "No clients running".</summary>
    public string LiveProcessSummary => _liveProcessCount switch
    {
        0 => "No Roblox clients running",
        1 => "1 Roblox client running",
        _ => $"{_liveProcessCount} Roblox clients running",
    };

    public string StatusBanner
    {
        get => _statusBanner;
        set => SetField(ref _statusBanner, value);
    }

    /// <summary>
    /// Yellow drift banner — populated when the installed Roblox version is outside the remote
    /// known-good range fetched from <c>roblox-compat.json</c>. Null when no drift / fetch failed.
    /// Spec §7.1.
    /// </summary>
    public string? RobloxCompatBanner
    {
        get => _robloxCompatBanner;
        set => SetField(ref _robloxCompatBanner, value);
    }

    /// <summary>
    /// True when Bloxstrap is the registered <c>roblox-player</c> handler AND the user has
    /// not yet dismissed the warning. The MainWindow XAML binds a yellow banner to this.
    /// Resolves to false silently when registry access is denied — no scary error to the user.
    /// </summary>
    public bool BloxstrapWarningVisible
    {
        get => _bloxstrapWarningVisible;
        private set => SetField(ref _bloxstrapWarningVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    /// <summary>
    /// True while a batch is holding for a pending Roblox update to land on the first client
    /// (v1.7.0 install-deferral pre-warm). The seam item 5 binds the "Roblox is updating — hold on"
    /// UX to; this item only sets/clears the flag (and a plain status line) around the pre-warm
    /// wait. False on the no-update / strap paths — those never enter the wait. Spec
    /// §"Components > 4. Updating-UX".
    /// </summary>
    public bool RobloxUpdating
    {
        get => _robloxUpdating;
        private set => SetField(ref _robloxUpdating, value);
    }

    /// <summary>Loads accounts + games from disk. Called once at MainWindow load.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var accounts = await _accountStore.ListAsync();
            Accounts.Clear();
            // Manual SortOrder wins when set; among rows that share a SortOrder (typical: every
            // account at 0 because the user has never reordered), fall back to most-recently-
            // launched first so freshly-touched accounts surface naturally.
            var ordered = accounts
                .OrderBy(a => a.SortOrder)
                .ThenByDescending(a => a.LastLaunchedAt ?? a.CreatedAt);
            foreach (var account in ordered)
            {
                var summary = new AccountSummary(account);
                WireAccountSummary(summary);
                Accounts.Add(summary);
            }
        }
        catch (AccountStoreCorruptException)
        {
            ShowDpapiCorruptModal();
        }

        // Re-apply any active filter against the freshly-loaded rows so a reload (e.g. after the
        // Games dialog closes) doesn't surface filtered-out rows. No-op when the filter is empty.
        ApplyFilter();

        await ReloadGamesAsync();
        OnPropertyChanged(nameof(MainAccount));
        OnPropertyChanged(nameof(CompactEmptyKind));
        OnPropertyChanged(nameof(CompactRows));
        OnPropertyChanged(nameof(HasCompactRows));
        RelayCommand.RaiseCanExecuteChanged();

        // v1.5.0 — start the presence poll loop now that Accounts is populated, so the first
        // tick has targets. Start() is idempotent (no-op if already running), so the repeated
        // LoadAsync calls on Games-dialog close don't spin up a second loop. Accounts that get
        // their RobloxUserId backfilled after this point enter the poll snapshot automatically —
        // the snapshot provider re-reads Accounts on every tick.
        _presenceService.Start();
    }

    /// <summary>
    /// Reload <see cref="AvailableGames"/> from the favorites store and re-sync each account's
    /// <see cref="AccountSummary.SelectedGame"/> -- preserve current selection if still present,
    /// else fall back to the favorites default. Called on initial load + after the Games dialog
    /// closes (since the user may have added / removed / set-default'd a game).
    /// </summary>
    public async Task ReloadGamesAsync()
    {
        var games = await _favorites.ListAsync();
        var privateServers = await _privateServerStore.ListAsync();
        AvailableGames.Clear();
        WidgetGames.Clear();
        foreach (var game in games)
        {
            AvailableGames.Add(game);
            WidgetGames.Add(game); // widget dropdown excludes the sentinel entirely
        }
        // Saved private servers join the per-account dropdown as FavoriteGame-shaped entries
        // carrying the PS code/kind + stable PS Id (v1.6.0). They render with the server's
        // RenderName so renames show, plus a "(private server)" suffix via DropdownLabel. NOT
        // added to WidgetGames — the default-game widget is for games, not one-off PS launches
        // (same exclusion the JoinByLink sentinel gets).
        foreach (var server in privateServers)
        {
            AvailableGames.Add(ToFavoriteEntry(server));
        }
        // Sentinel entry users click to open the Join-by-link modal. Lives at the bottom of the
        // dropdown so accidental clicks are unlikely. NOT added to WidgetGames — the widget is for
        // setting the default game, not for one-off launches.
        AvailableGames.Add(JoinByLinkSentinel);

        // Default-game candidates exclude PS entries + the sentinel — the default is a game.
        var defaultGame = AvailableGames.FirstOrDefault(g => g.IsDefault && !IsJoinByLinkSentinel(g) && !g.IsPrivateServer)
                          ?? AvailableGames.FirstOrDefault(g => !IsJoinByLinkSentinel(g) && !g.IsPrivateServer);
        foreach (var account in Accounts)
        {
            account.SelectedGame = FindMatchingEntry(account.SelectedGame) ?? defaultGame;
        }

        // Keep the widget readout in lockstep with what the store thinks. INPC fires for
        // DefaultGameDisplay are coupled via CurrentDefaultGame's setter.
        CurrentDefaultGame = defaultGame;
    }

    /// <summary>
    /// Project a <see cref="SavedPrivateServer"/> into the <see cref="FavoriteGame"/> shape the
    /// per-account dropdown consumes. Carries the PS code/kind (for launch) + the stable PS Id
    /// (for re-sync matching and in-dropdown rename routing). v1.6.0.
    /// </summary>
    private static FavoriteGame ToFavoriteEntry(SavedPrivateServer server) => new(
        PlaceId: server.PlaceId,
        UniverseId: 0,
        Name: server.Name,
        ThumbnailUrl: server.ThumbnailUrl,
        IsDefault: false,
        AddedAt: server.AddedAt,
        LocalName: server.LocalName,
        PrivateServerCode: server.Code,
        PrivateServerCodeKind: server.CodeKind,
        PrivateServerId: server.Id);

    /// <summary>
    /// Re-sync a row's prior selection to the freshly-rebuilt <see cref="AvailableGames"/> list.
    /// PS entries match by stable PS Id (a PS can share a placeId with a favorite game OR with
    /// another PS, so placeId alone collides); game entries match by placeId. The sentinel never
    /// re-syncs. Returns null when the prior selection is gone, so the caller falls back to the
    /// default game. v1.6.0.
    /// </summary>
    private FavoriteGame? FindMatchingEntry(FavoriteGame? prior)
    {
        if (prior is null || IsJoinByLinkSentinel(prior))
        {
            return null;
        }

        if (prior.IsPrivateServer)
        {
            return AvailableGames.FirstOrDefault(g => g.IsPrivateServer && g.PrivateServerId == prior.PrivateServerId);
        }

        return AvailableGames.FirstOrDefault(g =>
            !IsJoinByLinkSentinel(g) && !g.IsPrivateServer && g.PlaceId == prior.PlaceId);
    }

    /// <summary>
    /// Translate any of Roblox's three private-server URL forms — share URL with privateServerLinkCode,
    /// already-resolved launcher URL with accessCode, or the newer <c>roblox.com/share?code=X&amp;type=Server</c>
    /// share token — into a concrete <see cref="LaunchTarget"/>. The first two are pure-string parses
    /// via <see cref="LaunchTarget.FromUrl"/>; the share-token form requires an authenticated API call
    /// against Roblox's resolve-link endpoint, so this method needs an account cookie. We pick any
    /// non-expired account for the resolution call — Roblox's API doesn't care which user asks; it
    /// just needs a valid session. The resulting linkCode goes through normal launch as
    /// <see cref="PrivateServerCodeKind.LinkCode"/>. Returns null if every form fails or no account
    /// is available to resolve a share token.
    /// </summary>
    public async Task<LaunchTarget?> ResolveShareUrlAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        // First: the cheap sync paths (existing share-link form + already-resolved launcher form).
        var direct = LaunchTarget.FromUrl(url);
        if (direct is not null)
        {
            return direct;
        }

        // Second: the newer roblox.com/share?code=X&type=Y form. Needs a Roblox API call to
        // resolve the opaque code into a real (placeId, linkCode) pair.
        if (!LaunchTarget.TryParseShareLink(url, out var code, out var linkType))
        {
            return null;
        }
        if (!string.Equals(linkType, "Server", StringComparison.OrdinalIgnoreCase))
        {
            // Non-server share tokens (Game / Profile / etc.) aren't useful for launching as a
            // private server — bail rather than silently launch into something else.
            return null;
        }

        var resolverAccount = Accounts.FirstOrDefault(a => !a.SessionExpired);
        if (resolverAccount is null)
        {
            _log.LogInformation("No non-expired account available to resolve share token; skipping.");
            return null;
        }

        string cookie;
        try
        {
            cookie = await _accountStore.RetrieveCookieAsync(resolverAccount.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "RetrieveCookieAsync failed during share-link resolution.");
            return null;
        }

        try
        {
            var resolution = await _api.ResolveShareLinkAsync(cookie, code, "Server").ConfigureAwait(true);
            if (resolution is null || resolution.PlaceId <= 0 || string.IsNullOrEmpty(resolution.LinkCode))
            {
                _log.LogInformation("Roblox share-link resolve returned no usable server data for code {Code}.", code);
                return null;
            }
            return new LaunchTarget.PrivateServer(resolution.PlaceId, resolution.LinkCode, PrivateServerCodeKind.LinkCode);
        }
        catch (CookieExpiredException)
        {
            // The resolver's cookie expired between our last validation and now. Mark it.
            resolverAccount.SessionExpired = true;
            return null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "ResolveShareLinkAsync threw for code {Code}.", code);
            return null;
        }
    }

    /// <summary>
    /// Fires the remote compat fetch + version-drift check. Best-effort; failures leave the
    /// banner null. Called by App.OnStartup after the main window is loaded.
    /// </summary>
    public async Task LoadCompatBannerAsync()
    {
        try
        {
            var result = await _compatChecker.CheckAsync();
            RobloxCompatBanner = result.HasDrift ? result.Banner : null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Compat banner check failed; leaving null.");
            RobloxCompatBanner = null;
        }
    }

    /// <summary>
    /// Background pass that validates every saved cookie against Roblox's authenticated-user
    /// endpoint. Marks expired sessions yellow proactively so the user doesn't discover them
    /// only when Launch As fails. Runs sequentially with a 350 ms gap between requests so we
    /// don't hammer auth on startup. Skips accounts already running (their cookie just worked).
    /// </summary>
    public async Task ValidateSessionsAsync(CancellationToken ct = default)
    {
        var snapshot = Accounts.ToList();
        if (snapshot.Count == 0)
        {
            return;
        }
        _log.LogInformation("Validating {Count} stored sessions in background.", snapshot.Count);

        foreach (var summary in snapshot)
        {
            if (ct.IsCancellationRequested) return;
            if (summary.IsRunning) continue;

            string cookie;
            try
            {
                cookie = await _accountStore.RetrieveCookieAsync(summary.Id).ConfigureAwait(true);
            }
            catch (AccountStoreCorruptException)
            {
                // Don't show the modal here — first launch attempt will surface it cleanly.
                _log.LogWarning("AccountStore corrupt during session validation; aborting pass.");
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "RetrieveCookieAsync failed for {AccountId}; skipping.", summary.Id);
                continue;
            }

            try
            {
                var profile = await _api.GetUserProfileAsync(cookie).ConfigureAwait(true);
                summary.SessionExpired = false;
                summary.RobloxUserId = profile.UserId; // cache for the Friends modal
                // Cycle 5: persist so a restart doesn't lose the resolved userId.
                // Soft-fail — persist failure must not bubble to the validation flow.
                try
                {
                    await _accountStore.UpdateRobloxUserIdAsync(summary.Id, profile.UserId).ConfigureAwait(true);
                }
                catch (Exception persistEx)
                {
                    _log.LogDebug(persistEx, "Couldn't persist RobloxUserId for {AccountId} (validation pass); will retry on next resolution.", summary.Id);
                }
            }
            catch (CookieExpiredException)
            {
                _log.LogInformation("Session for {AccountId} ({Name}) is expired.", summary.Id, summary.DisplayName);
                summary.SessionExpired = true;
            }
            catch (Exception ex)
            {
                // Network failure / 5xx — leave the session badge alone. Don't false-alarm yellow
                // on a flaky DNS lookup.
                _log.LogDebug(ex, "Validation transient failure for {AccountId}; leaving state.", summary.Id);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), ct).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        var expired = snapshot.Count(s => s.SessionExpired);
        if (expired > 0)
        {
            StatusBanner = expired == 1
                ? "1 saved session has expired. Click Re-authenticate to refresh it."
                : $"{expired} saved sessions have expired. Click Re-authenticate to refresh.";
        }
    }

    private async Task AddAccountAsync()
    {
        IsBusy = true;
        try
        {
            var captured = await _cookieCapture.CaptureAsync();
            switch (captured)
            {
                case CookieCaptureResult.Success success:
                    await CompleteAddAsync(success);
                    break;
                case CookieCaptureResult.Cancelled:
                    return;
                case CookieCaptureResult.Failed failed when failed.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase):
                    ShowWebView2NotInstalledModal();
                    break;
                case CookieCaptureResult.Failed failed:
                    StatusBanner = failed.Message;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompleteAddAsync(CookieCaptureResult.Success captured)
    {
        string avatarUrl = string.Empty;
        try
        {
            avatarUrl = await _api.GetAvatarHeadshotUrlAsync(captured.UserId);
        }
        catch (Exception ex)
        {
            // Avatar fetch is best-effort — the row still works without an image.
            _log.LogDebug(ex, "Avatar fetch failed for new account {UserId}.", captured.UserId);
        }

        var account = await _accountStore.AddAsync(captured.Username, avatarUrl, captured.Cookie);
        var summary = new AccountSummary(account) { RobloxUserId = captured.UserId };
        // Cycle 5: persist the userId from cookie capture so the next session has it without
        // any API call. AddAsync doesn't take a userId parameter; this is a follow-up write.
        // Soft-fail — persist failure must not bubble to the add flow's success banner.
        try
        {
            await _accountStore.UpdateRobloxUserIdAsync(account.Id, captured.UserId).ConfigureAwait(true);
        }
        catch (Exception persistEx)
        {
            _log.LogDebug(persistEx, "Couldn't persist RobloxUserId for newly-added {AccountId}; will retry on next resolution.", account.Id);
        }
        WireAccountSummary(summary);
        Accounts.Insert(0, summary);
        _log.LogInformation("Added account {AccountId} ({Username}, userId {UserId}, isMain={IsMain})",
            account.Id, captured.Username, captured.UserId, account.IsMain);
        StatusBanner = account.IsMain
            ? $"Added {captured.Username}. Marked as main — change it any time."
            : $"Added {captured.Username}.";
        OnPropertyChanged(nameof(MainAccount));
        OnPropertyChanged(nameof(CompactEmptyKind));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private async Task LaunchAccountAsync(AccountSummary? summary, LaunchTarget? overrideTarget = null)
    {
        if (summary is null)
        {
            return;
        }

        summary.IsLaunching = true;
        summary.StatusText = "Launching...";
        OnPropertyChanged(nameof(CompactRows));
        OnPropertyChanged(nameof(HasCompactRows));
        _log.LogInformation("Launching account {AccountId} ({DisplayName}) target={Target}",
            summary.Id, summary.DisplayName, overrideTarget?.GetType().Name ?? "from-row");
        try
        {
            string cookie;
            try
            {
                cookie = await _accountStore.RetrieveCookieAsync(summary.Id);
            }
            catch (AccountStoreCorruptException)
            {
                ShowDpapiCorruptModal();
                return;
            }

            _log.LogInformation("Launch dispatch: id={Id} name={Name} robloxUserId={RobloxUserId} cookieFp={CookieFp}",
                summary.Id, summary.DisplayName, summary.RobloxUserId, CookieFp(cookie));

            // Stamp identity into appStorage.json + defend it until the launched client
            // CONSUMES it (OnProcessAttached → NotifyConsumed → ~10s grace) rather than a
            // fixed window. The ~120s max cap is the install-delay upper bound: a Roblox
            // install box popping mid-launch can postpone the real RPB's first read of
            // appStorage.json well past the old 12s window, expiring the defense before
            // the identity is consumed → wrong account + captcha (v1.6.0 item 9).
            if (summary.RobloxUserId is { } userId)
            {
                var defender = new AppStorageDefender(
                    summary.DisplayName, summary.DisplayName, userId,
                    _log,
                    maxCap: TimeSpan.FromSeconds(120),
                    postAttachGrace: TimeSpan.FromSeconds(10));
                var accountId = summary.Id;
                lock (_defendersLock)
                {
                    _defendersByAccountId[accountId] = defender;
                }
                _ = defender.Completion.ContinueWith(
                    _ =>
                    {
                        lock (_defendersLock)
                        {
                            // Only remove if it's still the same defender — a newer launch
                            // for the same account may have replaced it (and the older one's
                            // Completion fires when its takeover cancels it).
                            if (_defendersByAccountId.TryGetValue(accountId, out var current)
                                && ReferenceEquals(current, defender))
                            {
                                _defendersByAccountId.Remove(accountId);
                            }
                        }
                        return defender.DisposeAsync().AsTask();
                    },
                    TaskScheduler.Default);
            }
            else
            {
                _log.LogWarning(
                    "Skipping appStorage defender for {Account} — RobloxUserId is null",
                    summary.DisplayName);
            }

            LaunchTarget target = ResolveLaunchTarget(summary.SelectedGame, overrideTarget);

            var result = await _launcher.LaunchAsync(cookie, target, fpsCap: summary.FpsCap);
            switch (result)
            {
                case LaunchResult.Started started:
                    await _accountStore.TouchLastLaunchedAsync(summary.Id);
                    summary.StampLaunched(DateTimeOffset.UtcNow);
                    summary.SessionExpired = false;
                    summary.StatusText = string.Empty;
                    summary.LastClosedAtUtc = null;
                    _log.LogInformation("Launcher pid {Pid} for {AccountId}; tracking RobloxPlayerBeta", started.Pid, summary.Id);
                    await RecordSessionStartAsync(summary, target, started.LaunchedAtUtc);
                    // Fire-and-forget: tracker watches for the player process. UI updates flow back
                    // through ProcessAttached / ProcessAttachFailed events.
                    _ = _processTracker.TrackLaunchAsync(summary.Id, started.LaunchedAtUtc);
                    break;
                case LaunchResult.CookieExpired:
                    _log.LogInformation("Cookie expired for account {AccountId}", summary.Id);
                    summary.SessionExpired = true;
                    summary.StatusText = string.Empty;
                    break;
                case LaunchResult.Failed failed when failed.Message.Contains("Roblox does not appear to be installed", StringComparison.OrdinalIgnoreCase):
                    _log.LogWarning("Roblox not installed at launch time for account {AccountId}", summary.Id);
                    summary.StatusText = "Roblox not installed.";
                    ShowRobloxNotInstalledModal();
                    break;
                case LaunchResult.Failed failed:
                    _log.LogWarning("Launch failed for account {AccountId}: {Message}", summary.Id, failed.Message);
                    summary.StatusText = failed.Message;
                    break;
            }
        }
        finally
        {
            summary.IsLaunching = false;
            OnPropertyChanged(nameof(CompactRows));
            OnPropertyChanged(nameof(HasCompactRows));
        }
    }

    /// <summary>
    /// Launch every non-expired, non-running account in sequence with a 1.5s gap. The gap gives
    /// the tracker time to claim each <c>RobloxPlayerBeta.exe</c> by start time before the next
    /// launch fires (otherwise FIFO matching gets murky).
    /// </summary>
    private async Task LaunchAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // Pre-snapshot presence refresh (closes the 67ms race): a just-closed client resolves
            // to not-in-game before we read eligibility, so it's correctly counted as launchable
            // rather than "already running." AccountPresenceUpdated is marshaled to this (UI) thread
            // and we await on it, so after this returns each AccountSummary's state is fresh and we
            // compute eligibility below. A presence failure must never block a launch — log + proceed
            // with current state. (v1.5.0 spec §"Components > 3".)
            await RefreshPresenceBeforeLaunchAsync();

            var summaries = Accounts.ToList();
            var result = LaunchEligibility.Compute(summaries.Select(ToLaunchCandidate));
            var targets = MatchEligible(summaries, result.Eligible);
            _log.LogInformation("LaunchMultiple: {Count} eligible, {Running} running, {Expired} expired, {Deselected} deselected",
                targets.Count, result.Breakdown.Running, result.Breakdown.Expired, result.Breakdown.Deselected);
            foreach (var t in targets)
            {
                _log.LogInformation("LaunchMultiple target: id={Id} name={Name} robloxUserId={RobloxUserId}",
                    t.Id, t.DisplayName, t.RobloxUserId);
            }

            if (targets.Count == 0)
            {
                StatusBanner = result.ZeroEligibleBanner;
                return;
            }

            StatusBanner = $"Launching {targets.Count} selected account{(targets.Count == 1 ? "" : "s")}...";
            await DispatchBatchAsync(
                targets,
                overrideTarget: null,
                launchingBanner: (summary, n, total) => $"Launching {summary.DisplayName} ({n} of {total})...");
            StatusBanner = result.PartialBanner(targets.Count, "Launch multiple finished");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Project an <see cref="AccountSummary"/> into the pure <see cref="LaunchCandidate"/> the
    /// eligibility computation consumes. The v1.5.0 augment rule lives in
    /// <see cref="LaunchEligibility"/>, not here — this is a flat field map only.
    /// </summary>
    private static LaunchCandidate ToLaunchCandidate(AccountSummary a) => new(
        a.IsSelected, a.SessionExpired, a.InGame, a.IsRunning, a.IsLaunching, a.DisplayName);

    /// <summary>
    /// Re-resolve the <see cref="AccountSummary"/> rows for the eligible candidates the helper
    /// returned. The helper works on value snapshots; we match back by index against the same
    /// ordered list it was computed from so we launch the live summaries (not stale copies).
    /// </summary>
    private static List<AccountSummary> MatchEligible(
        IReadOnlyList<AccountSummary> ordered,
        IReadOnlyList<LaunchCandidate> eligible)
    {
        // Recompute the eligibility predicate against the live rows in the same order — cheaper and
        // less error-prone than threading identity through the value structs, and the predicate is
        // the single source of truth in LaunchEligibility.IsBusy.
        return ordered
            .Where(a => a.IsSelected && !a.SessionExpired && !LaunchEligibility.IsBusy(ToLaunchCandidate(a)) && !a.IsLaunching)
            .ToList();
    }

    /// <summary>
    /// One-shot presence refresh run before a batch launch computes eligibility. Wrapped so a
    /// presence failure (network/5xx/timeout) never blocks the launch — we log and proceed with the
    /// current (held-last) state. v1.5.0 spec §"Components > 3".
    /// </summary>
    private async Task RefreshPresenceBeforeLaunchAsync()
    {
        try
        {
            await _presenceService.PollOnceAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Pre-launch presence refresh failed; proceeding with last-known state.");
        }
    }

    /// <summary>
    /// Inter-launch throttle between batch clients — gives the tracker time to FIFO-claim each
    /// <c>RobloxPlayerBeta.exe</c> by start time AND widens the appStorage contested window
    /// (v1.4.2.0). Shared by Launch-multiple + Private-server batches.
    /// </summary>
    private static readonly TimeSpan InterLaunchThrottle = TimeSpan.FromMilliseconds(5000);

    /// <summary>
    /// Poll cadence for the v1.7.0 pre-warm wait — checks the installer-gone + first-attached
    /// signals roughly twice a second, bounded by <see cref="PreWarmGate.MaxWait"/>.
    /// </summary>
    private static readonly TimeSpan PreWarmPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Dispatch a batch of eligible accounts through the throttled launch loop, with the v1.7.0
    /// install-deferral pre-warm gate wrapped AROUND it (spec §"Components > 2/3" + "Data flow").
    /// <para>
    /// Gate (pure <see cref="PreWarmGate.Decide"/>): a strap is the handler → it self-updates, so
    /// launch the whole batch at normal speed; else no update pending → launch the whole batch at
    /// normal speed (the common path, unchanged); else (update pending) → launch the FIRST account,
    /// wait until the installer is gone AND #1 attached (bounded by <see cref="PreWarmGate.MaxWait"/>),
    /// then release the rest through the same loop. The update lands once, up front, on #1; the rest
    /// find a matching version and never trigger the installer.
    /// </para>
    /// Eligibility / skip-reason banners are computed by the callers BEFORE this — pre-warm wraps the
    /// batch, it doesn't replace eligibility.
    /// </summary>
    private async Task DispatchBatchAsync(
        IReadOnlyList<AccountSummary> targets,
        LaunchTarget.PrivateServer? overrideTarget,
        Func<AccountSummary, int, int, string> launchingBanner)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var decision = await DecidePreWarmAsync().ConfigureAwait(true);

        // Single-account batches can't benefit from serializing the update (there is no "rest"),
        // so they always go down the normal path — the lone launch IS the pre-warm.
        if (decision == PreWarmDecision.PreWarmThenRelease && targets.Count > 1)
        {
            // --- Pre-warm: launch #1, hold the rest until the update clears. ---
            var first = targets[0];
            StatusBanner = launchingBanner(first, 1, targets.Count);
            await LaunchAccountAsync(first, overrideTarget).ConfigureAwait(true);

            await WaitForPreWarmAsync(first).ConfigureAwait(true);

            // Release the REST through the existing throttled loop. #1 is already up.
            await ReleaseBatchAsync(targets, overrideTarget, launchingBanner, startIndex: 1).ConfigureAwait(true);
            return;
        }

        // --- Normal path: strap-handled OR no update pending OR a single-account batch. ---
        await ReleaseBatchAsync(targets, overrideTarget, launchingBanner, startIndex: 0).ConfigureAwait(true);
    }

    /// <summary>
    /// Run the pure <see cref="PreWarmGate.Decide"/> gate against the two live probes. Both probe
    /// reads are degrade-safe by contract (a strap-detect or CDN failure returns the "don't block"
    /// answer), and we additionally swallow here so a probe surprise never stalls a batch — on any
    /// throw we fall back to <see cref="PreWarmDecision.LaunchAllNow"/> (today's behavior).
    /// </summary>
    private async Task<PreWarmDecision> DecidePreWarmAsync()
    {
        try
        {
            // Strap-handling short-circuits the (more expensive) network update check.
            var strapHandling = _bloxstrapDetector.IsStrapHandlingLaunches();
            if (strapHandling)
            {
                return PreWarmGate.Decide(strapHandling: true, updatePending: false);
            }

            var updatePending = await _updateProbe.IsUpdatePendingAsync().ConfigureAwait(true);
            return PreWarmGate.Decide(strapHandling: false, updatePending);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Pre-warm decision probe threw; defaulting to launch-all-now.");
            return PreWarmDecision.LaunchAllNow;
        }
    }

    /// <summary>
    /// Block (cooperatively, polling on the UI thread) until the v1.7.0 pre-warm wait completes:
    /// <c>RobloxPlayerInstaller.exe</c> gone AND the first account attached (its
    /// <c>summary.IsRunning</c> flipped true via <see cref="OnProcessAttached"/>). Bounded by
    /// <see cref="PreWarmGate.MaxWait"/> — on the cap we proceed best-effort and release the rest
    /// anyway (never hang the batch forever). Sets/clears <see cref="RobloxUpdating"/> as the seam
    /// item 5 binds the "Roblox is updating — hold on" UX to.
    /// </summary>
    private async Task WaitForPreWarmAsync(AccountSummary first)
    {
        var deadline = DateTime.UtcNow + PreWarmGate.MaxWait;
        // RobloxUpdating drives the branded "Roblox is updating" banner (MainWindow.xaml, item 5) —
        // it owns the user-facing message now, so we no longer set the plain StatusBanner line here
        // (that would double the same words). The status row returns to the launch-progress banner
        // once ReleaseBatchAsync releases the rest of the batch.
        RobloxUpdating = true;
        _log.LogInformation("Pre-warm: holding the batch on {Account} until the Roblox update clears.", first.DisplayName);
        try
        {
            while (true)
            {
                // installerRunning is degrade-safe-to-false; firstAttached is the UI-thread summary
                // flag set by OnProcessAttached. Both feed the pure wait-complete predicate.
                var installerRunning = _updateProbe.IsInstallerRunning();
                if (PreWarmGate.PreWarmWaitComplete(installerRunning, first.IsRunning))
                {
                    _log.LogInformation("Pre-warm complete: installer gone + {Account} attached. Releasing the rest.", first.DisplayName);
                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    _log.LogWarning(
                        "Pre-warm wait hit the {Cap}s cap (installerRunning={Installer}, firstAttached={Attached}); releasing the rest best-effort.",
                        (int)PreWarmGate.MaxWait.TotalSeconds, installerRunning, first.IsRunning);
                    return;
                }

                await Task.Delay(PreWarmPollInterval).ConfigureAwait(true);
            }
        }
        finally
        {
            RobloxUpdating = false;
        }
    }

    /// <summary>
    /// The throttled launch loop, factored out of Launch-multiple / Private-server so the pre-warm
    /// path can release the tail of the batch through the SAME loop (with the SAME 5s throttle and
    /// "(n of total)" banner) after #1 is up. <paramref name="startIndex"/> skips the already-warmed
    /// first account; the throttle is applied between every dispatched client.
    /// </summary>
    private async Task ReleaseBatchAsync(
        IReadOnlyList<AccountSummary> targets,
        LaunchTarget.PrivateServer? overrideTarget,
        Func<AccountSummary, int, int, string> launchingBanner,
        int startIndex)
    {
        for (var idx = startIndex; idx < targets.Count; idx++)
        {
            var summary = targets[idx];
            StatusBanner = launchingBanner(summary, idx + 1, targets.Count);
            await LaunchAccountAsync(summary, overrideTarget).ConfigureAwait(true);
            if (idx < targets.Count - 1)
            {
                await Task.Delay(InterLaunchThrottle).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Open the Squad Launch modal. After the modal closes, if the user picked a target,
    /// dispatch every eligible account into it via <see cref="SquadLaunchAsync"/>.
    /// </summary>
    private async Task OpenSquadLaunchAsync()
    {
        // Eligibility for the Private server modal counts SELECTED accounts only. Deselected
        // rows are surfaced in the modal's status line so the user knows why the count is low. The
        // "running" count uses the v1.5.0 augment rule (InGame || IsRunning) so an in-game alt with
        // a lost pid is correctly surfaced as skipped, matching SquadLaunchAsync's eligibility.
        var breakdown = LaunchEligibility.Compute(Accounts.Select(ToLaunchCandidate));
        var eligible = breakdown.Eligible.Count;
        var running = breakdown.Breakdown.Running;
        var expired = breakdown.Breakdown.Expired;

        var window = new SquadLaunchWindow(_privateServerStore, _api, url => ResolveShareUrlAsync(url), eligible, running, expired)
        {
            Owner = Application.Current.MainWindow,
        };
        var dialogResult = window.ShowDialog();
        if (dialogResult == true && window.SelectedTarget is { } target)
        {
            await SquadLaunchAsync(target);
        }
    }

    /// <summary>
    /// Mass-launch every eligible account into the same private server, throttled 1.5 s apart so
    /// the process tracker can FIFO-claim each <c>RobloxPlayerBeta.exe</c> by start time. The
    /// override target trumps each row's per-account SelectedGame.
    /// </summary>
    private async Task SquadLaunchAsync(LaunchTarget.PrivateServer target)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // Same pre-snapshot presence refresh as LaunchAllAsync — closes the just-closed-client
            // race before computing eligibility. Failures never block the launch.
            await RefreshPresenceBeforeLaunchAsync();

            var summaries = Accounts.ToList();
            var result = LaunchEligibility.Compute(summaries.Select(ToLaunchCandidate));
            var targets = MatchEligible(summaries, result.Eligible);
            _log.LogInformation("PrivateServer: placeId={PlaceId}, {Count} eligible, {Running} running, {Expired} expired, {Deselected} deselected",
                target.PlaceId, targets.Count, result.Breakdown.Running, result.Breakdown.Expired, result.Breakdown.Deselected);
            if (targets.Count == 0)
            {
                StatusBanner = result.ZeroEligibleBanner;
                return;
            }

            await DispatchBatchAsync(
                targets,
                overrideTarget: target,
                launchingBanner: (summary, n, total) => $"Joining private server: {summary.DisplayName} ({n} of {total})...");
            StatusBanner = result.PartialBanner(targets.Count, "Private server launch finished");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Open the per-row "Join by link" paste modal. Triggered when the user picks the
    /// <see cref="JoinByLinkSentinel"/> entry in their game dropdown. The modal parses the
    /// pasted URL via <see cref="LaunchTarget.FromUrl"/> and, if valid, fires a one-shot launch
    /// for this account into that target. Doesn't persist anywhere — it's the "play once into
    /// what someone DM'd me" path.
    /// </summary>
    public async Task OpenJoinByLinkAsync(AccountSummary? summary)
    {
        if (summary is null) return;

        var window = new JoinByLinkWindow(_api, url => ResolveShareUrlAsync(url), summary.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };
        if (window.ShowDialog() == true && window.SelectedTarget is { } target)
        {
            var saveToLibrary = window.SaveToLibrary;
            await JoinByLinkSave.ApplyAsync(_api, _favorites, _privateServerStore, target, saveToLibrary, _log);
            if (saveToLibrary && target is LaunchTarget.Place)
            {
                // ApplyAsync already swallowed any save failure; reload is best-effort.
                // PrivateServer saves don't need this — the Library sheet lists from the
                // store directly on next open.
                await ReloadGamesAsync();
            }
            await LaunchAccountAsync(summary, overrideTarget: target);
        }
    }

    /// <summary>
    /// Open the Friends modal for one account. Resolves the Roblox userId on first open
    /// (cached on <see cref="AccountSummary"/> for subsequent opens). After the modal closes,
    /// if the user picked a friend to follow, fire the launch with that target.
    /// </summary>
    private async Task OpenFriendFollowAsync(AccountSummary? summary)
    {
        if (summary is null) return;

        string cookie;
        try
        {
            cookie = await _accountStore.RetrieveCookieAsync(summary.Id);
        }
        catch (AccountStoreCorruptException)
        {
            ShowDpapiCorruptModal();
            return;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "RetrieveCookieAsync failed for friends modal {AccountId}", summary.Id);
            StatusBanner = "Couldn't read this account's saved session.";
            return;
        }

        // Resolve userId if we don't already have it cached.
        long userId = summary.RobloxUserId ?? 0;
        if (userId <= 0)
        {
            try
            {
                var profile = await _api.GetUserProfileAsync(cookie);
                userId = profile.UserId;
                summary.RobloxUserId = userId;
                // Cycle 5: persist so the next session can skip this resolve.
                // Soft-fail — persist failure must not bubble to the friends-modal flow.
                try
                {
                    await _accountStore.UpdateRobloxUserIdAsync(summary.Id, userId);
                }
                catch (Exception persistEx)
                {
                    _log.LogDebug(persistEx, "Couldn't persist RobloxUserId for {AccountId} (Friends modal); will retry on next resolution.", summary.Id);
                }
            }
            catch (CookieExpiredException)
            {
                summary.SessionExpired = true;
                StatusBanner = $"{summary.DisplayName}'s session expired — re-authenticate first.";
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Couldn't resolve userId for friends modal {AccountId}", summary.Id);
                StatusBanner = "Couldn't reach Roblox to load friends. Try again in a moment.";
                return;
            }
        }

        // Pass the store + account id, not the plaintext cookie — the window retrieves the cookie
        // fresh per refresh into a short-lived local instead of holding it for its whole lifetime.
        var window = new FriendFollowWindow(_api, _accountStore, summary.Id, userId, summary.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };
        if (window.ShowDialog() == true && window.SelectedTarget is { } target)
        {
            // Re-run the same land-at-home guard FollowAltAsync uses, against the friend's presence
            // snapshot carried out of the modal. The modal already gates the button on this, but we
            // re-check here so the launch decision is owned by one shared rule (EvaluateFollow) and
            // a privacy-hidden / stale-presence target gets a clear message instead of a silent
            // bounce to the Roblox home page.
            var decision = EvaluateFollow(window.SelectedPresence, window.SelectedFriendName ?? "that friend");
            if (!decision.CanFollow)
            {
                StatusBanner = decision.BlockedMessage!; // non-null whenever CanFollow is false (see FollowDecision.Block)
                return;
            }
            await LaunchAccountAsync(summary, overrideTarget: target);
        }
    }

    /// <summary>
    /// Send the tracked Roblox window for this account a graceful close (CloseMainWindow).
    /// Falls back to Kill if a second click arrives while still tracking.
    /// </summary>
    private void StopAccount(AccountSummary? summary)
    {
        if (summary is null || !_processTracker.IsTracking(summary.Id))
        {
            return;
        }
        _log.LogInformation("StopAccount {AccountId} (pid {Pid})", summary.Id, summary.RunningPid);
        if (!_processTracker.RequestClose(summary.Id))
        {
            // Window unresponsive — escalate.
            _processTracker.Kill(summary.Id);
        }
    }

    /// <summary>
    /// Persist a new in-flight session row at launch time. Failures are non-fatal — history is
    /// comfort, not load-bearing — so any throw here just logs at debug.
    /// </summary>
    private async Task RecordSessionStartAsync(AccountSummary summary, LaunchTarget target, DateTimeOffset launchedAtUtc)
    {
        try
        {
            // Resolve a human-readable game name if we can. PrivateServer + Place know their
            // PlaceId; DefaultGame doesn't (the launcher resolves it internally), so fall back
            // to the row's SelectedGame name. FollowFriend has no place at all — null game name.
            string? gameName = null;
            long? placeId = null;
            var isPrivate = false;
            switch (target)
            {
                case LaunchTarget.PrivateServer ps:
                    placeId = ps.PlaceId;
                    isPrivate = true;
                    // Prefer the row's PS entry name (RenderName picks up the rename); fall back
                    // to a generic label if the row selection isn't a PS entry (override path).
                    gameName = summary.SelectedGame?.IsPrivateServer == true
                        ? summary.SelectedGame.RenderName
                        : summary.SelectedGame?.RenderName ?? $"Place {ps.PlaceId} (private server)";
                    break;
                case LaunchTarget.Place p:
                    placeId = p.PlaceId;
                    gameName = summary.SelectedGame?.RenderName ?? $"Place {p.PlaceId}";
                    break;
                case LaunchTarget.DefaultGame:
                    gameName = summary.SelectedGame?.Name;
                    placeId = summary.SelectedGame?.PlaceId;
                    break;
                case LaunchTarget.FollowFriend:
                    gameName = "Following a friend";
                    break;
            }

            var session = new LaunchSession(
                Id: Guid.NewGuid(),
                AccountId: summary.Id,
                AccountDisplayName: summary.DisplayName,
                AccountAvatarUrl: summary.AvatarUrl,
                GameName: gameName,
                PlaceId: placeId,
                IsPrivateServer: isPrivate,
                LaunchedAtUtc: launchedAtUtc,
                EndedAtUtc: null,
                OutcomeHint: null);

            _pendingSessionByAccountId[summary.Id] = session.Id;
            await _sessionHistory.AddAsync(session);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Recording session start threw for account {AccountId}; continuing.", summary.Id);
        }
    }

    private async Task RecordSessionEndAsync(Guid accountId, DateTimeOffset endedAtUtc, string? outcomeHint)
    {
        if (!_pendingSessionByAccountId.TryGetValue(accountId, out var sessionId))
        {
            return;
        }
        _pendingSessionByAccountId.Remove(accountId);
        try
        {
            await _sessionHistory.MarkEndedAsync(sessionId, endedAtUtc, outcomeHint);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Stamping session end threw for account {AccountId}; continuing.", accountId);
        }
    }

    private void OnProcessAttached(object? sender, RobloxProcessEventArgs e)
    {
        // The launched client is up and can now read the stamped identity for captcha
        // branding. Tell its defender to wind down after the post-attach grace (v1.6.0
        // item 9). Normal path: attach in ~1-2s → defends ~12s total (attach + grace),
        // same protective behavior as the old fixed window, just measured from attach.
        AppStorageDefender? defender;
        lock (_defendersLock)
        {
            _defendersByAccountId.TryGetValue(e.AccountId, out defender);
        }
        defender?.NotifyConsumed();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            summary.IsRunning = true;
            summary.RunningPid = e.Pid;
            summary.RunningSinceUtc = e.OccurredAtUtc;
            summary.StatusText = string.Empty;
            LiveProcessCount = _processTracker.Attached.Count;
            OnPropertyChanged(nameof(CompactRows));
            OnPropertyChanged(nameof(HasCompactRows));
            RelayCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnProcessExited(object? sender, RobloxProcessEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;

            // The pid is genuinely gone — always clear process state.
            summary.IsRunning = false;
            summary.RunningPid = null;
            summary.RunningSinceUtc = null;

            // v1.5.0 anti-ghost rule: do NOT unconditionally stamp LastClosedAtUtc. A row is
            // "Closed" only when BOTH presence and process tracking agree it's gone. The Roblox
            // anti-multilaunch bootstrapper kills the pid we attached to and respawns the real
            // client under a new pid we never claimed — if we stamped "Closed" here while
            // presence still reports in-game, the live client reads "Closed" (the ghost).
            if (summary.InGame)
            {
                // Process gone but presence still in-game (the ghost case). Don't stamp Closed.
                // Fire a fast-confirm re-poll: if the window is truly gone the next presence
                // event will stamp the close via OnAccountPresenceUpdated; if it's still up the
                // row keeps showing "In <game>".
                _ = _presenceService.RequestImmediateRefreshAsync(e.AccountId);
            }
            else if (summary.RobloxUserId is > 0)
            {
                // Presence-capable account, currently not in-game — both signals agree it's
                // closed, so stamp it now. Still fast-confirm to keep presence current.
                summary.LastClosedAtUtc = e.OccurredAtUtc;
                _ = _presenceService.RequestImmediateRefreshAsync(e.AccountId);
            }
            else
            {
                // No RobloxUserId — presence can never run for this account, so process tracking
                // is the only signal. Keep the pre-v1.5.0 behavior: stamp the close immediately.
                summary.LastClosedAtUtc = e.OccurredAtUtc;
            }

            LiveProcessCount = _processTracker.Attached.Count;
            OnPropertyChanged(nameof(CompactRows));
            OnPropertyChanged(nameof(HasCompactRows));
            RelayCommand.RaiseCanExecuteChanged();
        });
        // Fire-and-forget the history end-stamp; persistence isn't on the UI critical path.
        _ = RecordSessionEndAsync(e.AccountId, e.OccurredAtUtc, outcomeHint: null);
    }

    /// <summary>
    /// Presence poll landed for one account (v1.5.0). Authoritative for <em>display</em>:
    /// in-game state + game name. Events arrive on threadpool threads (the poller runs up to 4
    /// concurrent), so marshal to the dispatcher before touching the UI-bound summary. Spec
    /// §"Components > 2" + "Data flow."
    /// </summary>
    private void OnAccountPresenceUpdated(object? sender, AccountPresenceEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;

            if (e.PresenceType == UserPresenceType.InGame)
            {
                // Stamp the in-game-since time on the transition into a game OR on a game switch
                // (place id changed), so the "· {age}" tail resets when they hop games.
                if (!summary.InGame || e.PlaceId != summary.CurrentPlaceId)
                {
                    summary.InGameSinceUtc = e.OccurredAtUtc;
                }
                summary.CurrentPlaceId = e.PlaceId;
                summary.CurrentGameName = e.GameName;
                summary.PresenceState = e.PresenceType;
            }
            else
            {
                // Capture combined active state BEFORE mutating presence so we can tell whether
                // this poll is the moment the row went fully inactive.
                var wasActive = summary.InGame || summary.IsRunning;

                summary.PresenceState = e.PresenceType;
                summary.CurrentGameName = null;
                summary.CurrentPlaceId = null;
                summary.InGameSinceUtc = null;

                // Presence-confirmed close: the row was active, presence now says not-in-game,
                // and the process is also gone — both signals agree, so stamp the close. This is
                // the close-stamp the deferred OnProcessExited handed off to presence (the ghost
                // case resolving once the respawned client truly closes).
                if (wasActive && !summary.IsRunning)
                {
                    summary.LastClosedAtUtc = e.OccurredAtUtc;
                }
            }

            // Mirror the OnProcessAttached/Exited refresh shape so command-enablement (LaunchAll
            // CanExecute keys off InGame || IsRunning) and the compact view stay in sync.
            // LiveProcessCount is process-only, so it isn't touched here.
            OnPropertyChanged(nameof(CompactRows));
            OnPropertyChanged(nameof(HasCompactRows));
            RelayCommand.RaiseCanExecuteChanged();
        });
    }

    /// <summary>
    /// Presence poll returned 401 for one account (v1.5.0) — the cookie died between launches.
    /// Flip the row to the yellow "Session expired" badge. Marshalled to the dispatcher because
    /// the poller raises this off a threadpool thread. Spec §"Error handling" (401 from presence).
    /// </summary>
    private void OnAccountSessionExpired(object? sender, Guid accountId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == accountId);
            if (summary is null) return;
            summary.SessionExpired = true;
            RelayCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnProcessAttachFailed(object? sender, RobloxProcessEventArgs e)
    {
        // IMPORTANT (v1.6.0 item 9): do NOT dispose the appStorage defender here. During a
        // long Roblox install the RPB spawns AFTER the 30s tracker attach timeout, so this
        // fires while the install is still in progress — disposing now would re-expose the
        // wrong-account bug (defense expires before the real client reads the identity). Let
        // the ~120s max cap bound the defender instead. The defender stays in
        // _defendersByAccountId until its Completion ContinueWith removes it at the cap.
        _log.LogInformation(
            "Process attach failed for account {AccountId}; leaving appStorage defender to its max cap (install may still be in progress).",
            e.AccountId);

        // v1.7.0 item 5: if a Roblox installer is running, the client hasn't attached because Roblox
        // is mid-update — not a real failure. Branch the row copy on that signal so the slow-install
        // case this cycle targets reads as an intended hold, not a scary AV/never-connected error.
        // IsInstallerRunning() is synchronous and never throws — call it directly.
        var installerRunning = _updateProbe.IsInstallerRunning();
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            // The launcher fired but no player process appeared. Most common: Roblox version drift,
            // place removed, antivirus quarantine — UNLESS an install is in progress, in which case
            // the install is the reason. PreWarmGate.AttachFailedMessage owns the branch (tested).
            summary.StatusText = PreWarmGate.AttachFailedMessage(installerRunning);
        });
        // Stamp the session row with an outcome hint instead of an end timestamp — the launch
        // never actually ran. Useful when scrolling history later: "this one never connected."
        _ = RecordSessionEndAsync(e.AccountId, e.OccurredAtUtc, outcomeHint: "Never connected");
    }

    private async Task RemoveAccountAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Remove {summary.DisplayName}?\nYou'll need to log in again to add it back.",
            "Remove Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var wasMain = summary.IsMain;
        await _accountStore.RemoveAsync(summary.Id);
        Accounts.Remove(summary);

        // Store auto-promotes a new main when the previous one was just removed; mirror that
        // promotion onto the in-memory AccountSummary list so the MAIN pill flips immediately.
        if (wasMain && Accounts.Count > 0)
        {
            var promoted = await _accountStore.ListAsync();
            var promotedId = promoted.FirstOrDefault(a => a.IsMain)?.Id;
            foreach (var a in Accounts)
            {
                a.IsMain = promotedId.HasValue && a.Id == promotedId.Value;
            }
            OnPropertyChanged(nameof(MainAccount));
        }

        OnPropertyChanged(nameof(CompactEmptyKind));
        OnPropertyChanged(nameof(CompactRows));
        OnPropertyChanged(nameof(HasCompactRows));
        RelayCommand.RaiseCanExecuteChanged();
        StatusBanner = $"Removed {summary.DisplayName}.";
    }

    private async Task ReauthenticateAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var captured = await _cookieCapture.CaptureAsync();
            if (captured is not CookieCaptureResult.Success success)
            {
                if (captured is CookieCaptureResult.Failed failed
                    && failed.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
                {
                    ShowWebView2NotInstalledModal();
                }
                return;
            }

            // Refresh avatar URL while we're at it (display name might have changed too — but we
            // keep the original DisplayName so the row identity is stable).
            try
            {
                _ = await _api.GetAvatarHeadshotUrlAsync(success.UserId);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Avatar refresh failed during reauth for {AccountId}.", summary.Id);
            }

            await _accountStore.UpdateCookieAsync(summary.Id, success.Cookie);
            _log.LogInformation("Re-authenticated account {AccountId}", summary.Id);
            summary.SessionExpired = false;
            summary.StatusText = "Re-authenticated.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_favorites, _privateServerStore, _api) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        // Refresh in case the user added / removed / set-default'd a game.
        _ = ReloadGamesAsync();
    }

    private void OpenDiagnostics()
    {
        var window = new DiagnosticsWindow(_diagnostics) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private void OpenAbout()
    {
        var window = new AboutWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    /// <summary>
    /// Attach every reactive-persist subscription a row needs. Called at both row-creation sites
    /// (initial load + Add Account) so a freshly-added account persists tag edits just like a loaded
    /// one. Tags are seeded in the AccountSummary constructor BEFORE this subscribe, so wiring
    /// CollectionChanged here never fires a redundant persist for the rows loaded from disk.
    /// </summary>
    private void WireAccountSummary(AccountSummary summary)
    {
        summary.PropertyChanged += OnAccountSummaryPropertyChanged;
        summary.Tags.CollectionChanged += (_, _) => OnAccountTagsChanged(summary);
    }

    /// <summary>
    /// A row's tag collection changed (add/remove) — persist the whole normalized list. Mirrors the
    /// <see cref="PersistIsSelectedAsync"/> soft-failure shape: fire-and-forget, a write failure
    /// doesn't block the chip showing/hiding; the next edit reconverges.
    /// </summary>
    private void OnAccountTagsChanged(AccountSummary summary)
    {
        // Re-evaluate this row against the active filter — a tag added/removed while a filter is
        // applied should immediately reflect in the row's visibility (v1.6.0, item 7b). No-op
        // visually when no filter is set (the predicate returns "matches" for an empty filter).
        if (IsFilterActive)
        {
            summary.IsFilteredOut = !AccountMatchesFilter(summary.Tags, summary.RenderName, _accountFilter);
        }
        _ = PersistTagsAsync(summary.Id, summary.Tags.ToList());
    }

    private async Task PersistTagsAsync(Guid accountId, IReadOnlyList<string> tags)
    {
        try
        {
            await _accountStore.SetTagsAsync(accountId, tags);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Persisting {Count} tags for {AccountId} failed.", tags.Count, accountId);
        }
    }

    /// <summary>
    /// Persist account-level toggles whenever they flip. Today: <see cref="AccountSummary.IsSelected"/>
    /// (the per-row dot for batch launches). Fire-and-forget — a write failure doesn't block the
    /// UI flip; the next click reconverges. Other AccountSummary properties (running state,
    /// status text, etc.) are intentionally session-only.
    /// </summary>
    private void OnAccountSummaryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AccountSummary summary) return;
        if (e.PropertyName == nameof(AccountSummary.IsSelected))
        {
            _ = PersistIsSelectedAsync(summary.Id, summary.IsSelected);
        }
        else if (e.PropertyName == nameof(AccountSummary.CaptionColorHex))
        {
            _ = PersistCaptionColorAsync(summary.Id, summary.CaptionColorHex);
        }
    }

    private async Task PersistIsSelectedAsync(Guid accountId, bool isSelected)
    {
        try
        {
            await _accountStore.SetSelectedAsync(accountId, isSelected);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Persisting IsSelected={Selected} for {AccountId} failed.", isSelected, accountId);
        }
    }

    private async Task PersistCaptionColorAsync(Guid accountId, string? hex)
    {
        try
        {
            await _accountStore.SetCaptionColorAsync(accountId, hex);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Persisting caption color {Hex} for {AccountId} failed.", hex, accountId);
        }
    }

    /// <summary>
    /// Persist a per-account FPS cap. Called by the row template's ComboBox SelectionChanged
    /// handler. Catches and swallows store exceptions — a failed FPS write should never
    /// prevent the row from re-rendering with the new selection.
    /// </summary>
    public async Task OnFpsCapChangedAsync(AccountSummary row, int? newValue)
    {
        if (row is null) return;
        row.FpsCap = newValue;
        try
        {
            await _accountStore.SetFpsCapAsync(row.Id, newValue);
        }
        catch (Exception ex)
        {
            // Swallow — the in-memory row already reflects the new value, and a future restart
            // will surface store problems via the standard load path.
            _log.LogWarning(ex, "Failed to persist FPS cap for {Id}", row.Id);
        }
    }

    private async Task DismissBloxstrapWarningAsync()
    {
        BloxstrapWarningVisible = false;
        await _settings.SetBloxstrapWarningDismissedAsync(true);
    }

    private async Task InitializeBloxstrapWarningAsync()
    {
        var dismissed = await _settings.GetBloxstrapWarningDismissedAsync();
        BloxstrapWarningVisible = !dismissed && _bloxstrapDetector.IsBloxstrapHandler();
    }

    /// <summary>
    /// Launch <paramref name="source"/> into <paramref name="target"/>'s current Roblox server
    /// via <see cref="LaunchTarget.FollowFriend"/>. Guarded by the shared
    /// <see cref="EvaluateFollow"/> rule (same as the Friends-modal path): when the target isn't in
    /// a joinable game we block with a clear message instead of firing a launch that silently lands
    /// at the Roblox home page. Only a real joinable place launches.
    /// </summary>
    public async Task FollowAltAsync(AccountSummary? source, AccountSummary? target)
    {
        if (source is null || target is null) return;
        if (ReferenceEquals(source, target)) return;
        if (source.SessionExpired)
        {
            StatusBanner = $"{source.DisplayName} has an expired session — re-authenticate first.";
            return;
        }
        if (target.RobloxUserId is not long targetUserId || targetUserId <= 0)
        {
            // RobloxUserId is cached lazily (validation pass + cookie capture). If it's never
            // landed, we don't have a userId to route to. Surface the gap rather than fail
            // silently inside the launcher.
            StatusBanner = $"Couldn't follow {target.DisplayName} — Roblox userId not yet known. " +
                           "Try Re-authenticating that account, or wait a moment after login.";
            return;
        }
        // Share the SAME land-at-home guard as the Friends-modal path so the two follow surfaces
        // can't drift. The saved-account row carries the v1.5.0 presence (PresenceState +
        // CurrentPlaceId); project it into a UserPresence and let EvaluateFollow be the single rule.
        // A target not in a joinable game is blocked here instead of firing a launch that bounces
        // source to the Roblox home page.
        var targetPresence = new UserPresence(
            targetUserId, target.PresenceState, target.CurrentPlaceId, GameJobId: null, LastLocation: null);
        var decision = EvaluateFollow(targetPresence, target.DisplayName);
        if (!decision.CanFollow)
        {
            StatusBanner = decision.BlockedMessage!; // non-null whenever CanFollow is false (see FollowDecision.Block)
            return;
        }
        StatusBanner = $"Following {target.DisplayName} from {source.DisplayName}...";
        var follow = new LaunchTarget.FollowFriend(targetUserId);
        await LaunchAccountAsync(source, overrideTarget: follow);
    }

    /// <summary>
    /// Push the current AccountSummary's caption color to any running Roblox window for that
    /// account RIGHT NOW (instead of waiting up to 1.5s for the decorator's poll). Called by
    /// the row's color picker after Apply / Reset so the visual feedback is instant.
    /// </summary>
    public void RefreshDecoratorForAccount(Guid accountId)
    {
        try
        {
            _windowDecorator.RefreshAccount(accountId);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Decorator refresh for {AccountId} threw; will land on next tick.", accountId);
        }
    }

    /// <summary>
    /// Move <paramref name="source"/> to the position currently held by <paramref name="target"/>,
    /// shifting <paramref name="target"/> + everything below down one slot. Used by the row's
    /// drag handler. Persists the new order via <see cref="IAccountStore.UpdateSortOrderAsync"/>;
    /// silently no-ops if either argument is null or the same row.
    /// </summary>
    public async Task MoveAccountAsync(AccountSummary? source, AccountSummary? target)
    {
        if (source is null || target is null || ReferenceEquals(source, target))
        {
            return;
        }
        var srcIdx = Accounts.IndexOf(source);
        var dstIdx = Accounts.IndexOf(target);
        if (srcIdx < 0 || dstIdx < 0 || srcIdx == dstIdx)
        {
            return;
        }
        Accounts.Move(srcIdx, dstIdx);

        try
        {
            await _accountStore.UpdateSortOrderAsync(Accounts.Select(a => a.Id).ToList());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Persisting reordered accounts failed; in-memory order kept.");
        }
    }

    private void OpenHistory()
    {
        var window = new SessionHistoryWindow(_sessionHistory, _favorites, _api)
        {
            Owner = Application.Current.MainWindow,
        };
        window.ShowDialog();
        // The user may have bookmarked games from history; refresh the per-row dropdowns so
        // they appear without a restart.
        _ = ReloadGamesAsync();
    }

    private void OpenPreferences()
    {
        var window = new Preferences.PreferencesWindow(
            _settings, _startupRegistration, _themeStore, _themeService,
            _accountStore, _accountTransport, this)
        {
            Owner = Application.Current.MainWindow,
        };
        window.ShowDialog();
    }

    /// <summary>
    /// Set the given account as the user's main. Persists via <see cref="IAccountStore.SetMainAsync"/>;
    /// flips the in-memory IsMain flag on every account in lockstep so the row's MAIN pill updates
    /// without a re-list. Click the current main again to unset (toggle behavior).
    /// </summary>
    private async Task SetMainAsync(AccountSummary? summary)
    {
        if (summary is null) return;
        var newMainId = summary.IsMain ? Guid.Empty : summary.Id;
        try
        {
            await _accountStore.SetMainAsync(newMainId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SetMain failed for {AccountId}", summary.Id);
            StatusBanner = "Couldn't set main account — see log for details.";
            return;
        }

        foreach (var a in Accounts)
        {
            a.IsMain = a.Id == newMainId;
        }
        OnPropertyChanged(nameof(MainAccount));
        OnPropertyChanged(nameof(CompactEmptyKind));
        StatusBanner = newMainId == Guid.Empty
            ? "Main account cleared."
            : $"{summary.DisplayName} is now your main.";
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void ToggleCompact() => IsCompact = !IsCompact;

    /// <summary>
    /// Compact-mode CTA: launch the main account into its current per-row game pick. Falls back
    /// to the launcher's default-place resolution if the row hasn't picked a game yet. Mirrors
    /// LaunchAccountAsync so the same tracker / cookie-expired / not-installed paths apply.
    /// </summary>
    private async Task StartMainAsync()
    {
        var main = MainAccount;
        if (main is null) return;
        await LaunchAccountAsync(main);
    }

    private static void ShowWebView2NotInstalledModal()
    {
        var window = new WebView2NotInstalledWindow();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    private static void ShowRobloxNotInstalledModal()
    {
        var window = new RobloxNotInstalledWindow();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    private void ShowDpapiCorruptModal()
    {
        var window = new DpapiCorruptWindow();
        window.Owner = Application.Current.MainWindow;
        var startFresh = window.ShowDialog() == true;
        if (startFresh)
        {
            // The store's load already failed; renaming + creating-empty is the recovery path.
            // For v1 we let the next AddAsync naturally overwrite the corrupt file via the
            // atomic-write path. The accounts list stays empty.
            Accounts.Clear();
            StatusBanner = "Started fresh. Add accounts to begin.";
        }
        else
        {
            // User chose Quit — let the app exit so they can restore from a backup.
            Application.Current.Shutdown(0);
        }
    }

    // ---------- v1.3.x — default-game widget + rename overlay handlers ----------

    /// <summary>
    /// Build a <see cref="RenameTarget"/> from a row's data context. Pattern-matches on the
    /// known entity types so XAML can pass <c>CommandParameter="{Binding}"</c> directly.
    /// Returns null on unrecognized types (no-op at command boundary).
    /// </summary>
    private static RenameTarget? BuildRenameTarget(object? source) => source switch
    {
        // PS-carrying dropdown entries (v1.6.0) route to the PrivateServer rename path by stable
        // PS Id — checked BEFORE the plain game case since a PS entry has PlaceId > 0 too.
        FavoriteGame { IsPrivateServer: true, PrivateServerId: { } psId } psEntry =>
            new RenameTarget(RenameTargetKind.PrivateServer, psId, psEntry.Name, psEntry.LocalName),
        FavoriteGame game when game.PlaceId > 0 =>
            new RenameTarget(RenameTargetKind.Game, game.PlaceId, game.Name, game.LocalName),
        AccountSummary account =>
            new RenameTarget(RenameTargetKind.Account, account.Id, account.DisplayName, account.LocalName),
        SavedPrivateServer server =>
            new RenameTarget(RenameTargetKind.PrivateServer, server.Id, server.Name, server.LocalName),
        _ => null,
    };

    private void OnFavoritesDefaultChanged(object? sender, EventArgs e)
    {
        // The store has already mutated + persisted; just refresh our cached view of "what's
        // the current default" so the widget readout flips. ReloadGamesAsync also re-syncs
        // each account's SelectedGame to the new default — keeps row pickers in lockstep.
        _ = ReloadGamesAsync();
    }

    private async Task RemoveGameAsync(FavoriteGame? game)
    {
        if (game is null || IsJoinByLinkSentinel(game))
        {
            return;
        }

        try
        {
            await _favorites.RemoveAsync(game.PlaceId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RemoveAsync failed for placeId {PlaceId}.", game.PlaceId);
            StatusBanner = "Couldn't remove that game. Disk error?";
            return;
        }

        await ReloadGamesAsync();
    }

    private async Task SetDefaultGameAsync(FavoriteGame? game)
    {
        if (game is null || IsJoinByLinkSentinel(game))
        {
            return;
        }

        // Close the popup first — gives instant visual feedback even if the SetDefaultAsync
        // call takes a tick. The DefaultChanged event will trigger ReloadGamesAsync which
        // refreshes CurrentDefaultGame + DefaultGameDisplay anyway.
        IsDefaultGameDropdownOpen = false;

        try
        {
            await _favorites.SetDefaultAsync(game.PlaceId);
        }
        catch (KeyNotFoundException ex)
        {
            // Race: game removed from another surface between the user opening the popup and
            // clicking. Surface a quiet status banner; in-memory list will reconcile on next reload.
            _log.LogDebug(ex, "SetDefaultAsync: game {PlaceId} no longer exists.", game.PlaceId);
            StatusBanner = "That game isn't saved any more.";
            await ReloadGamesAsync();
        }
    }

    private async Task RenameItemAsync(RenameTarget? target)
    {
        if (target is null)
        {
            return;
        }

        var owner = Application.Current.MainWindow;
        if (owner is null)
        {
            _log.LogWarning("RenameItemAsync invoked with no MainWindow available.");
            return;
        }

        var result = await Modals.RenameWindow.ShowAsync(owner, target);
        if (result.Kind == RenameResultKind.Cancel)
        {
            return;
        }

        try
        {
            await RenameDispatch.ApplyAsync(_favorites, _privateServerStore, _accountStore, target, result.NewName);
        }
        catch (KeyNotFoundException ex)
        {
            // Race: the entity was removed from another surface between context-menu open and Save.
            _log.LogDebug(ex, "Rename target {Kind} {Id} no longer exists.", target.Kind, target.Id);
            StatusBanner = $"That {target.Kind.ToString().ToLowerInvariant()} isn't saved any more.";
        }
        catch (System.IO.IOException ex)
        {
            _log.LogWarning(ex, "Atomic write failed during rename of {Kind} {Id}.", target.Kind, target.Id);
            StatusBanner = "Couldn't save name change. Disk error?";
            return;
        }

        await OnRenameAppliedAsync(target, result.NewName);
    }

    private async Task ResetItemNameAsync(RenameTarget? target)
    {
        if (target is null)
        {
            return;
        }

        try
        {
            await RenameDispatch.ApplyAsync(_favorites, _privateServerStore, _accountStore, target, newLocalName: null);
        }
        catch (KeyNotFoundException ex)
        {
            _log.LogDebug(ex, "Reset target {Kind} {Id} no longer exists.", target.Kind, target.Id);
            StatusBanner = $"That {target.Kind.ToString().ToLowerInvariant()} isn't saved any more.";
            return;
        }
        catch (System.IO.IOException ex)
        {
            _log.LogWarning(ex, "Atomic write failed during reset of {Kind} {Id}.", target.Kind, target.Id);
            StatusBanner = "Couldn't save name change. Disk error?";
            return;
        }

        await OnRenameAppliedAsync(target, null);
    }

    /// <summary>
    /// After a successful rename or reset, refresh whatever surfaces could now be stale.
    /// Account renames: update the matching <see cref="AccountSummary"/>'s LocalName so the
    /// row's RenderName flips immediately. Game renames: full ReloadGamesAsync so AvailableGames
    /// + WidgetGames + per-row SelectedGame all see the new instance with new LocalName.
    /// PrivateServer renames: full ReloadGamesAsync (v1.6.0) — saved PS entries now live in the
    /// per-account dropdown, so the rebuilt list picks up the new RenderName; Squad Launch sheet
    /// also re-lists from the store on its next open.
    /// </summary>
    private async Task OnRenameAppliedAsync(RenameTarget target, string? newLocalName)
    {
        switch (target.Kind)
        {
            case RenameTargetKind.Game:
                await ReloadGamesAsync();
                break;
            case RenameTargetKind.Account:
                var accountId = (Guid)target.Id;
                var summary = Accounts.FirstOrDefault(a => a.Id == accountId);
                if (summary is not null)
                {
                    summary.LocalName = newLocalName;
                }
                break;
            case RenameTargetKind.PrivateServer:
                // Saved private servers now appear in the per-account dropdown (v1.6.0), so a
                // rename has to rebuild AvailableGames for the new RenderName to show. Squad Launch
                // sheet still re-lists from the store on its own open.
                await ReloadGamesAsync();
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private static string CookieFp(string? cookie)
    {
        if (string.IsNullOrEmpty(cookie)) return "<empty>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(cookie);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 4);
    }
}

/// <summary>
/// Result of <see cref="MainViewModel.EvaluateFollow"/>: whether a follow may launch, and the
/// plain user-facing message to surface when it may not. <see cref="BlockedMessage"/> is non-null
/// exactly when <see cref="CanFollow"/> is false.
/// </summary>
public sealed record FollowDecision(bool CanFollow, string? BlockedMessage)
{
    public static FollowDecision Allow() => new(true, null);

    public static FollowDecision Block(string message) => new(false, message);
}
