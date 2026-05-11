using System.Collections.ObjectModel;
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
    private readonly IDiagnosticsCollector _diagnostics;
    private readonly IPrivateServerStore _privateServerStore;
    private readonly ISessionHistoryStore _sessionHistory;
    private readonly Startup.IStartupRegistration _startupRegistration;
    private readonly Core.Theming.IThemeStore _themeStore;
    private readonly Theming.ThemeService _themeService;
    private readonly Tray.RobloxWindowDecorator _windowDecorator;
    private readonly IBloxstrapDetector _bloxstrapDetector;
    private readonly ILogger<MainViewModel> _log;

    /// <summary>
    /// In-flight session-history rows keyed by account id. Populated when LaunchAccountAsync
    /// succeeds; consumed by OnProcessExited / OnProcessAttachFailed to stamp end / outcome.
    /// In-memory only — restart loses pending end-stamps, but the launched-at row is already
    /// persisted via <see cref="ISessionHistoryStore.AddAsync"/>.
    /// </summary>
    private readonly Dictionary<Guid, Guid> _pendingSessionByAccountId = new();
    private readonly DispatcherTimer _ticker;

    private string _statusBanner = string.Empty;
    private string? _robloxCompatBanner;
    private bool _bloxstrapWarningVisible;
    private bool _isBusy;
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
        IDiagnosticsCollector diagnostics,
        IPrivateServerStore privateServerStore,
        ISessionHistoryStore sessionHistory,
        Startup.IStartupRegistration startupRegistration,
        Core.Theming.IThemeStore themeStore,
        Theming.ThemeService themeService,
        Tray.RobloxWindowDecorator windowDecorator,
        IBloxstrapDetector bloxstrapDetector,
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
        _diagnostics = diagnostics;
        _privateServerStore = privateServerStore;
        _sessionHistory = sessionHistory;
        _startupRegistration = startupRegistration;
        _themeStore = themeStore;
        _themeService = themeService;
        _windowDecorator = windowDecorator;
        _bloxstrapDetector = bloxstrapDetector;
        _log = log ?? NullLogger<MainViewModel>.Instance;

        AddAccountCommand = new RelayCommand(AddAccountAsync, () => !IsBusy);
        LaunchAccountCommand = new RelayCommand(p => LaunchAccountAsync(p as AccountSummary));
        RemoveAccountCommand = new RelayCommand(p => RemoveAccountAsync(p as AccountSummary));
        ReauthenticateCommand = new RelayCommand(p => ReauthenticateAsync(p as AccountSummary));
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LaunchAllCommand = new RelayCommand(LaunchAllAsync, () => !IsBusy && Accounts.Any(a => a.IsSelected && !a.SessionExpired && !a.IsRunning));
        StopAccountCommand = new RelayCommand(p => StopAccount(p as AccountSummary));
        OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
        OpenAboutCommand = new RelayCommand(OpenAbout);
        OpenSquadLaunchCommand = new RelayCommand(OpenSquadLaunchAsync, () => !IsBusy && Accounts.Count > 0);
        OpenFriendFollowCommand = new RelayCommand(p => OpenFriendFollowAsync(p as AccountSummary));
        SetMainCommand = new RelayCommand(p => SetMainAsync(p as AccountSummary));
        ToggleCompactCommand = new RelayCommand(ToggleCompact);
        StartMainCommand = new RelayCommand(StartMainAsync, () => !IsBusy && Accounts.FirstOrDefault(a => a.IsMain) is { SessionExpired: false, IsRunning: false });
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
                summary.PropertyChanged += OnAccountSummaryPropertyChanged;
                Accounts.Add(summary);
            }
        }
        catch (AccountStoreCorruptException)
        {
            ShowDpapiCorruptModal();
        }

        await ReloadGamesAsync();
        OnPropertyChanged(nameof(MainAccount));
        OnPropertyChanged(nameof(CompactEmptyKind));
        OnPropertyChanged(nameof(CompactRows));
        OnPropertyChanged(nameof(HasCompactRows));
        RelayCommand.RaiseCanExecuteChanged();
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
        AvailableGames.Clear();
        WidgetGames.Clear();
        foreach (var game in games)
        {
            AvailableGames.Add(game);
            WidgetGames.Add(game); // widget dropdown excludes the sentinel entirely
        }
        // Sentinel entry users click to open the Join-by-link modal. Lives at the bottom of the
        // dropdown so accidental clicks are unlikely. NOT added to WidgetGames — the widget is for
        // setting the default game, not for one-off launches.
        AvailableGames.Add(JoinByLinkSentinel);

        var defaultGame = AvailableGames.FirstOrDefault(g => g.IsDefault && !IsJoinByLinkSentinel(g))
                          ?? AvailableGames.FirstOrDefault(g => !IsJoinByLinkSentinel(g));
        foreach (var account in Accounts)
        {
            var stillThere = AvailableGames.FirstOrDefault(g =>
                !IsJoinByLinkSentinel(g) && g.PlaceId == account.SelectedGame?.PlaceId);
            account.SelectedGame = stillThere ?? defaultGame;
        }

        // Keep the widget readout in lockstep with what the store thinks. INPC fires for
        // DefaultGameDisplay are coupled via CurrentDefaultGame's setter.
        CurrentDefaultGame = defaultGame;
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
        summary.PropertyChanged += OnAccountSummaryPropertyChanged;
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

            // Target precedence:
            //   1. Explicit override (Squad Launch / Friend Follow / Join-by-Link).
            //   2. Per-row SelectedGame from the ComboBox.
            //   3. LaunchTarget.DefaultGame -> launcher resolves favorites + settings tier.
            LaunchTarget target;
            if (overrideTarget is not null)
            {
                target = overrideTarget;
            }
            else if (summary.SelectedGame is { PlaceId: > 0 } sg)
            {
                target = new LaunchTarget.Place(sg.PlaceId);
            }
            else
            {
                target = new LaunchTarget.DefaultGame();
            }

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
            var targets = Accounts
                .Where(a => a.IsSelected && !a.SessionExpired && !a.IsRunning && !a.IsLaunching)
                .ToList();
            var deselectedCount = Accounts.Count(a => !a.IsSelected);
            _log.LogInformation("LaunchMultiple: {Count} eligible, {Skipped} deselected", targets.Count, deselectedCount);

            if (targets.Count == 0)
            {
                StatusBanner = deselectedCount > 0
                    ? "Nothing to launch — every selected account is running or expired. Toggle the dot next to a status to include more."
                    : "Nothing to launch — every account is already running or expired.";
                return;
            }

            StatusBanner = $"Launching {targets.Count} selected account{(targets.Count == 1 ? "" : "s")}...";
            var i = 0;
            foreach (var summary in targets)
            {
                StatusBanner = $"Launching {summary.DisplayName} ({++i} of {targets.Count})...";
                await LaunchAccountAsync(summary);
                if (i < targets.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500));
                }
            }
            var tail = deselectedCount > 0 ? $" ({deselectedCount} deselected, skipped.)" : string.Empty;
            StatusBanner = $"Launch multiple finished. {targets.Count} client{(targets.Count == 1 ? "" : "s")} dispatched.{tail}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Open the Squad Launch modal. After the modal closes, if the user picked a target,
    /// dispatch every eligible account into it via <see cref="SquadLaunchAsync"/>.
    /// </summary>
    private async Task OpenSquadLaunchAsync()
    {
        // Eligibility for the Private server modal counts SELECTED accounts only. Deselected
        // rows are surfaced in the modal's status line so the user knows why the count is low.
        var eligible = Accounts.Count(a => a.IsSelected && !a.SessionExpired && !a.IsRunning && !a.IsLaunching);
        var running = Accounts.Count(a => a.IsSelected && a.IsRunning);
        var expired = Accounts.Count(a => a.IsSelected && a.SessionExpired);

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
            var targets = Accounts
                .Where(a => a.IsSelected && !a.SessionExpired && !a.IsRunning && !a.IsLaunching)
                .ToList();
            var deselectedCount = Accounts.Count(a => !a.IsSelected);
            _log.LogInformation("PrivateServer: placeId={PlaceId}, {Count} eligible, {Skipped} deselected",
                target.PlaceId, targets.Count, deselectedCount);
            if (targets.Count == 0)
            {
                StatusBanner = deselectedCount > 0
                    ? "Nothing to launch — every selected account is running or expired."
                    : "Nothing to launch — every account is already running or expired.";
                return;
            }

            var i = 0;
            foreach (var summary in targets)
            {
                StatusBanner = $"Joining private server: {summary.DisplayName} ({++i} of {targets.Count})...";
                await LaunchAccountAsync(summary, overrideTarget: target);
                if (i < targets.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500));
                }
            }
            var tail = deselectedCount > 0 ? $" ({deselectedCount} deselected, skipped.)" : string.Empty;
            StatusBanner = $"Private server launch finished. {targets.Count} client{(targets.Count == 1 ? "" : "s")} joined.{tail}";
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

        var window = new FriendFollowWindow(_api, cookie, userId, summary.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };
        if (window.ShowDialog() == true && window.SelectedTarget is { } target)
        {
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
                    gameName = summary.SelectedGame?.Name ?? $"Place {ps.PlaceId} (private server)";
                    break;
                case LaunchTarget.Place p:
                    placeId = p.PlaceId;
                    gameName = summary.SelectedGame?.Name ?? $"Place {p.PlaceId}";
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
            summary.IsRunning = false;
            summary.RunningPid = null;
            summary.RunningSinceUtc = null;
            summary.LastClosedAtUtc = e.OccurredAtUtc;
            LiveProcessCount = _processTracker.Attached.Count;
            OnPropertyChanged(nameof(CompactRows));
            OnPropertyChanged(nameof(HasCompactRows));
            RelayCommand.RaiseCanExecuteChanged();
        });
        // Fire-and-forget the history end-stamp; persistence isn't on the UI critical path.
        _ = RecordSessionEndAsync(e.AccountId, e.OccurredAtUtc, outcomeHint: null);
    }

    private void OnProcessAttachFailed(object? sender, RobloxProcessEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            // The launcher fired but no player process appeared. Most common: Roblox version drift,
            // place removed, antivirus quarantine. Surface the hint in the row.
            summary.StatusText = "Launch never connected. Check Roblox is current + antivirus isn't blocking.";
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
    /// via <see cref="LaunchTarget.FollowFriend"/>. Roblox does the privacy + game-state check
    /// server-side; if target isn't currently in a place, the launcher silently lands at home.
    /// We surface a status banner so the user knows whether the chip click did anything.
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
        if (!target.IsRunning)
        {
            // Roblox returns "no game" when the target isn't in a place. We still fire the
            // launch (Roblox might surface its own friendly error), but warn the user.
            StatusBanner = $"{target.DisplayName} isn't currently in a game — Roblox may bounce {source.DisplayName} to the home page.";
        }
        else
        {
            StatusBanner = $"Following {target.DisplayName} from {source.DisplayName}...";
        }
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
        var window = new Preferences.PreferencesWindow(_settings, _startupRegistration, _themeStore, _themeService)
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
    /// PrivateServer renames: nothing to refresh at MainViewModel level — Squad Launch sheet
    /// re-lists from the store on its next open.
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
                // No MainViewModel-side surface for saved private servers. Squad Launch sheet
                // re-lists on open.
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
}
