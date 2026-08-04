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
using ROROROblox.App.Discord;
using ROROROblox.App.History;
using ROROROblox.App.Friends;
using ROROROblox.App.JoinByLink;
using ROROROblox.App.Modals;
using ROROROblox.App.Settings;
using ROROROblox.App.SquadLaunch;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;

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
    private readonly IActivityMonitor _activityMonitor;
    private readonly IMemoryWatchdog _memoryWatchdog;
    private readonly IRobloxInstanceStopper _instanceStopper;
    private readonly AccountRecycler _accountRecycler;
    private readonly ITrayService _tray;
    private readonly Notifications.IdleAlertPresenter _idleAlertPresenter;
    private readonly Core.StreamerMode.IStreamerIdentityProvider? _streamerIdentity;
    private readonly DiscordConfigStore? _discordConfigStore;
    private readonly ILogger<MainViewModel> _log;

    /// <summary>
    /// Discord rich-presence service (Task 9 wires this during startup wiring, only when the user
    /// has Discord presence configured). Null in every install that hasn't opted in — which is the
    /// default and every existing test — so every call site below reaches it through <c>?.</c>.
    /// Roster-changing handlers call <see cref="DiscordPresenceService.Refresh"/> through this
    /// field rather than the service subscribing to anything itself: the service is PULL, not
    /// push, and this VM is the one place that knows every seam the roster actually changes at.
    /// </summary>
    internal DiscordPresenceService? DiscordPresence { get; set; }

    /// <summary>
    /// Test seam over the ctor-injected <see cref="_discordConfigStore"/>, so a fixture can supply
    /// one without threading another argument through every construction site. Production always
    /// takes the constructor path.
    /// </summary>
    internal DiscordConfigStore? DiscordConfigStoreOverride { get; set; }

    /// <summary>The store per-account mute writes through — ctor-injected, or a test override.</summary>
    private DiscordConfigStore? AlertConfigStore => _discordConfigStore ?? DiscordConfigStoreOverride;

    /// <summary>
    /// Builds the Preferences dialog. Set by the composition root, which is the only place that
    /// should know what that window needs — it now takes eleven services, and having this view
    /// model construct it meant every dependency added to Preferences also had to be added here,
    /// to a constructor that is already the largest in the app. The factory keeps that growth in
    /// the one place designed to absorb it.
    /// </summary>
    internal Func<Preferences.PreferencesWindow>? PreferencesWindowFactory { get; set; }

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

    /// <summary>The one row currently highlighted via <see cref="SetFocusedAccount"/> (Task 8), if any.</summary>
    private Guid? _focusedAccountId;

    private string _statusBanner = string.Empty;
    private string? _robloxCompatBanner;
    private bool _bloxstrapWarningVisible;
    private bool _isBusy;
    private bool _robloxUpdating;
    private int _liveProcessCount;
    private string _idleSummaryText = string.Empty;
    private int _idleWarnThresholdMinutes = 15;
    private bool _muteIdleAlerts;

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
        IActivityMonitor activityMonitor,
        IMemoryWatchdog memoryWatchdog,
        IRobloxInstanceStopper instanceStopper,
        ITrayService tray,
        Notifications.IdleAlertPresenter idleAlertPresenter,
        Core.StreamerMode.IStreamerIdentityProvider? streamerIdentity = null,
        DiscordConfigStore? discordConfigStore = null,
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
        _activityMonitor = activityMonitor;
        _memoryWatchdog = memoryWatchdog;
        _instanceStopper = instanceStopper;
        _tray = tray;
        _idleAlertPresenter = idleAlertPresenter;
        _streamerIdentity = streamerIdentity;
        _discordConfigStore = discordConfigStore;
        _log = log ?? NullLogger<MainViewModel>.Instance;

        // AccountRecycler (Task 8) is built here, not injected — its LaunchDelegate needs to call
        // back into THIS instance's own launch path (LaunchForRecycleAsync) so a recycle raises
        // AccountLaunched on the plugin bus exactly like any other launch, with no new wiring.
        // Capturing the method group is safe mid-constructor: the delegate isn't INVOKED until
        // later, well after construction finishes.
        _accountRecycler = new AccountRecycler(_instanceStopper, LaunchForRecycleAsync, _memoryWatchdog, _log);

        // Mirror must exist before any off-thread reader (presence loop, plugin host) can
        // resolve this VM — the ctor runs on the UI thread, so wiring it here is race-free.
        _accountsMirror = new ObservableCollectionMirror<AccountSummary>(Accounts);

        AddAccountCommand = new RelayCommand(AddAccountAsync, () => !IsBusy);
        LaunchAccountCommand = new RelayCommand(p => LaunchAccountAsync(p as AccountSummary));
        RemoveAccountCommand = new RelayCommand(p => RemoveAccountAsync(p as AccountSummary));
        // !IsBusy gate matches AddAccountCommand: the capture window is modeless, so without it
        // a second capture can start while one is open — each capture's user-data-dir sweep
        // would then delete files under the other's LIVE WebView2 profile.
        ReauthenticateCommand = new RelayCommand(p => ReauthenticateAsync(p as AccountSummary), _ => !IsBusy);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LaunchAllCommand = new RelayCommand(LaunchAllAsync, () => !IsBusy && Accounts.Any(a => a.IsSelected && !a.SessionExpired && !a.SessionLimited && !(a.InGame || a.IsRunning)));
        StopAccountCommand = new RelayCommand(p => StopAccount(p as AccountSummary));
        RecycleAccountCommand = new RelayCommand(p => _ = RecycleAccountAsync(p as AccountSummary));
        OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
        OpenAboutCommand = new RelayCommand(OpenAbout);
        OpenSquadLaunchCommand = new RelayCommand(OpenSquadLaunchAsync, () => !IsBusy && Accounts.Count > 0);
        OpenFriendFollowCommand = new RelayCommand(p => OpenFriendFollowAsync(p as AccountSummary));
        SetMainCommand = new RelayCommand(p => SetMainAsync(p as AccountSummary));
        ToggleCompactCommand = new RelayCommand(ToggleCompact);
        StartMainCommand = new RelayCommand(StartMainAsync, () => !IsBusy && Accounts.FirstOrDefault(a => a.IsMain) is { SessionExpired: false, SessionLimited: false, IsRunning: false, InGame: false });
        OpenHistoryCommand = new RelayCommand(OpenHistory);
        OpenPreferencesCommand = new RelayCommand(OpenPreferences);
        OpenPluginsCommand = new RelayCommand(_ => RequestOpenPlugins?.Invoke(this, EventArgs.Empty));
        DismissBloxstrapWarningCommand = new RelayCommand(_ => _ = DismissBloxstrapWarningAsync());
        DismissFpsCapWarningCommand = new RelayCommand(_ => _ = DismissFpsCapWarningAsync());

        // v1.3.x — default-game widget + rename overlay commands.
        SetDefaultGameCommand = new RelayCommand(p => _ = SetDefaultGameAsync(p as FavoriteGame));
        // RenameItemCommand / ResetItemNameCommand take the row's data context (FavoriteGame /
        // AccountSummary / SavedPrivateServer) as CommandParameter — saves writing 6 commands or
        // threading RenameTarget through XAML constructor binding gymnastics.
        RenameItemCommand = new RelayCommand(p => _ = RenameItemAsync(BuildRenameTarget(p)));
        ResetItemNameCommand = new RelayCommand(p => _ = ResetItemNameAsync(BuildRenameTarget(p)));
        RemoveGameCommand = new RelayCommand(p => _ = RemoveGameAsync(p as FavoriteGame));
        ToggleJoinViaFriendCommand = new RelayCommand(p => _ = ToggleJoinViaFriendAsync(p as AccountSummary));
        ToggleAlertsMutedCommand = new RelayCommand(p =>
        {
            if (p is AccountSummary row) { _ = SetAlertsMutedAsync(row, !row.AlertsMuted); }
        });

        // Streamer mode (v1.10) — main-window switch + reroll controls (Task 10). No-ops when
        // _streamerIdentity is null (VM-level test harness, which doesn't pass one).
        RerollAllCommand = new RelayCommand(RerollAllIdentitiesAsync);
        RerollAccountCommand = new RelayCommand(p => RerollAccountAsync(p));

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
        _presenceService.AccountSessionLimited += OnAccountSessionLimited;

        // v1.8 idle awareness — coalesced, edge-triggered toast when accounts newly cross the
        // warn threshold. The monitor itself (Task 5) already runs its own sample timer; this
        // VM only reacts to the crossing event + refreshes the passive row/banner display below.
        _activityMonitor.WarnThresholdCrossed += OnActivityWarnCrossed;

        // Memory watchdog (v1.11, Task 7) — a coalesced, edge-triggered crossing (cap or
        // projection, latched per account) paints the warned chip immediately instead of waiting
        // for the next 30s tick. Fires off the watchdog's own sample timer thread, so marshal to
        // the dispatcher like every other cross-thread event above. Wrapped in try/catch — this is
        // the FIRST subscriber in the chain (tray + plugin-bus subscribers follow in App.xaml.cs,
        // both already guarded), so an unhandled throw here (e.g. a TaskCanceledException from
        // Dispatcher.Invoke during shutdown) would abort the whole multicast delegate and starve
        // every subscriber behind it of the crossing.
        _memoryWatchdog.PressureCrossed += (_, snap) =>
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ApplyMemory(snap);

                    // Alerts hang off the CROSSING, never off ApplyMemory — the 30s ticker calls
                    // ApplyMemory too, and raising there would re-fire the same warning twice a
                    // minute for as long as pressure held. PressureCrossed is edge-triggered and
                    // latched per account, which is exactly the "this just became true" signal an
                    // alert wants.
                    RaiseAlerts(BuildMemoryAlerts(snap, DateTimeOffset.UtcNow));
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Memory-warning VM apply threw; the row chip may not have refreshed for this crossing.");
            }
        };

        // Streamer mode (v1.10, Task 10) — keep the main-window switch (and the tray checkmark,
        // via its own subscription) in sync when the mode flips from either surface. Mirrors
        // AccountSummary.OnIdentityChanged's un-marshaled OnPropertyChanged call: WPF's binding
        // engine auto-dispatches PropertyChanged notifications to the owning thread, so no manual
        // Dispatcher.Invoke is needed here (unlike TrayService's direct MenuItem property write).
        if (_streamerIdentity is not null)
        {
            _streamerIdentity.Changed += OnStreamerIdentityChanged;
        }

        _ = InitializeBloxstrapWarningAsync();

        // Tick once a minute to keep "5 min ago" / "Running for 12 min" current.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _ticker.Tick += (_, _) =>
        {
            foreach (var summary in Accounts)
            {
                summary.RefreshRelativeTimes();
            }

            // v1.8 idle awareness — project the latest ActivityMonitor snapshot onto the rows
            // (chip text + amber IdleWarn) and refresh the passive summary strip. Runs on the
            // same 30s cadence as the relative-time refresh above; no separate timer needed.
            ActivitySnapshotApplier.Apply(Accounts, _activityMonitor.GetSnapshot(),
                TimeSpan.FromMinutes(_idleWarnThresholdMinutes));
            IdleSummaryText = IdleSummary.Format(Accounts.Count(a => a.IdleWarn), _idleWarnThresholdMinutes);

            // Memory watchdog (Task 7) — repaint from the latest snapshot on the same 30s cadence
            // as the idle chips above. ApplyMemory (called by RefreshMemoryChips) recomputes
            // MemoryWarning from the snapshot's own condition every call — cap/projection state,
            // not "did PressureCrossed just fire" — so a row that's still over cap/projection
            // stays warned across this passive refresh instead of being wiped back to false
            // (final-branch review CRITICAL 1, 2026-08-01).
            RefreshMemoryChips();

            // Task 8 — PressureCrossed is edge-triggered (fires ON a crossing, stays silent while
            // it holds), so nothing else ever notices when pressure recedes: without this, the tray
            // badge would stay warned until restart even after the user recycles the fat client and
            // memory is fine again. Piggyback the clear-check on this same 30s cadence rather than
            // stand up a second timer. SetMemoryWarning(false) is idempotent when already off
            // (TrayService short-circuits on no state change), so it's safe to call unconditionally
            // every tick — no need to track "was it warned" here too.
            if (MemoryPressureEvaluator.IsClear(_memoryWatchdog.GetSnapshot(), _memoryWatchdog.ProjectionWarnMinutes))
            {
                _tray.SetMemoryWarning(false);
            }
        };
        _ticker.Start();
    }

    public ObservableCollection<AccountSummary> Accounts { get; } = [];

    private readonly ObservableCollectionMirror<AccountSummary> _accountsMirror;

    /// <summary>
    /// Lock-free point-in-time copy of <see cref="Accounts"/> for OFF-UI-THREAD readers —
    /// the presence poll loop, plugin gRPC adapters, and process-tracker event bridges.
    /// <see cref="Accounts"/> itself is UI-thread-owned; enumerating it from a threadpool
    /// thread races a concurrent Add/Remove into "Collection was modified" (the fault that
    /// silently killed the presence loop — 2026-06-12 review).
    /// </summary>
    public IReadOnlyList<AccountSummary> AccountsSnapshot => _accountsMirror.Snapshot;

    /// <summary>
    /// Project the live rows into the shape Discord presence consumes. Internal so the projection
    /// — especially the streamer-mode rule — is unit-testable without a Discord pipe.
    /// <para>
    /// Names come from <see cref="AccountSummary.RenderName"/>, never <c>DisplayName</c>: streamer
    /// mode has to hold on the way OUT of the app, or it is a promise that only covers the window
    /// the user is already looking at.
    /// </para>
    /// <para>
    /// Enumerates <see cref="AccountsSnapshot"/>, not <see cref="Accounts"/> — this method is
    /// called from <c>DiscordPresenceService.Refresh()</c>, which is not guaranteed to run on the
    /// UI thread (Task 9 wires <c>ApplyAsync</c> from a settings dialog and Lachee's IPC
    /// <c>Ready</c> callback runs off its own thread). Enumerating the UI-thread-owned
    /// <see cref="Accounts"/> from there risks the same "Collection was modified" fault the
    /// snapshot mirror exists to prevent.
    /// </para>
    /// </summary>
    /// <para>
    /// FIX 1 (final whole-branch review, 2026-08-03): <c>Server</c> is built via
    /// <see cref="RosterServer.TryFrom"/> from BOTH the account's current presence server AND its
    /// <see cref="AccountSummary.LastLaunchTarget"/> — the record of what the session was actually
    /// launched with (private-server place id, code, and kind included). Passing the raw
    /// <see cref="ServerInstance"/> alone (the pre-fix shape) always produced a public
    /// <c>g|</c> Discord secret, even for a private-server roster: a friend clicking Join landed on
    /// a public target Roblox then bounced server-side, silently defeating the denied-entry warning
    /// this feature exists to show.
    /// </para>
    /// <para>
    /// Corrected in the 2026-08-03 re-review's blocking finding: <see cref="RosterServer.TryFrom"/>
    /// no longer requires presence to agree with <c>LastLaunchTarget</c> about WHICH place the
    /// account is in (Pet Sim 99 teleports between places inside one universe, so that agreement
    /// never held for the audience this feature exists for). The private place id, code, and kind
    /// travel together from <c>LastLaunchTarget</c> itself; presence supplies liveness and
    /// clustering only. The stale-credential risk that place-matching used to (incompletely) guard
    /// against is instead closed by <see cref="ApplyPresence"/> clearing
    /// <see cref="AccountSummary.LastLaunchTarget"/> when the account fully leaves a game (Minor 1).
    /// </para>
    /// <para>
    /// <see cref="RosterSnapshot.IsStreamerModeActive"/> (2026-08-03) reads the SAME provider that
    /// already supplies <see cref="AccountSummary.RenderName"/> (<see cref="_streamerIdentity"/>)
    /// — not a second source of truth. <see cref="PresencePayloadBuilder"/> is pure and must stay
    /// that way, so the anonymizing decision travels in on the snapshot instead of the builder (or
    /// this service) reaching for a streamer-mode singleton itself.
    /// </para>
    internal RosterSnapshot BuildRosterSnapshot() => new(
        AccountsSnapshot.Select(a => new RosterAccount(
            a.Id,
            a.RenderName,
            a.InGame,
            a.CurrentGameName,
            RosterServer.TryFrom(a.CurrentServer, a.LastLaunchTarget),
            a.InGameSinceUtc)).ToList(),
        IsStreamerModeActive: _streamerIdentity?.IsActive ?? false);

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
    /// <summary>
    /// One-click remedy for the tray memory warning (Task 8) — stop the account's client and
    /// relaunch it into the SAME <see cref="LaunchTarget"/>, via <see cref="AccountRecycler"/>.
    /// </summary>
    public ICommand RecycleAccountCommand { get; }
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
    public ICommand DismissFpsCapWarningCommand { get; }

    public event EventHandler? RequestOpenPlugins;

    // v1.3.x default-game widget + rename overlay.
    public ICommand SetDefaultGameCommand { get; }
    public ICommand RenameItemCommand { get; }
    public ICommand ResetItemNameCommand { get; }
    public ICommand RemoveGameCommand { get; }

    /// <summary>
    /// Flips an account row's <see cref="AccountSummary.JoinViaFriend"/> preference and persists
    /// it — the account row's context-menu checkbox (trust-aware squad launch, v1.9.0). Parameter
    /// is the row's <see cref="AccountSummary"/>. See <see cref="ToggleJoinViaFriendAsync"/>.
    /// </summary>
    public ICommand ToggleJoinViaFriendCommand { get; }

    /// <summary>
    /// Flips an account row's <see cref="AccountSummary.AlertsMuted"/> preference and persists it
    /// through the Discord config — the account row's context-menu checkbox. Parameter is the
    /// row's <see cref="AccountSummary"/>. See <see cref="SetAlertsMutedAsync"/>.
    /// </summary>
    public ICommand ToggleAlertsMutedCommand { get; }

    /// <summary>
    /// True when streamer mode is active — bound two-way to the main-window <c>ui:ToggleSwitch</c>
    /// (Task 10). Get reads straight through to <see cref="Core.StreamerMode.IStreamerIdentityProvider.IsActive"/>;
    /// set fire-and-forgets <see cref="Core.StreamerMode.IStreamerIdentityProvider.SetActiveAsync"/> and relies on
    /// <see cref="OnStreamerIdentityChanged"/> to raise the change notification once the provider
    /// confirms the flip (keeps the switch and the tray checkbox as two views of one source of
    /// truth instead of an optimistic local flag that could drift). False (and a no-op set) when
    /// no provider was resolved — the VM-level test harness doesn't pass one.
    /// </summary>
    public bool StreamerModeOn
    {
        get => _streamerIdentity?.IsActive ?? false;
        set
        {
            if (_streamerIdentity is null) return;
            _ = _streamerIdentity.SetActiveAsync(value);
        }
    }

    /// <summary>Reroll every streamer-mode fake identity (accounts + lazily-met friends) at once — the "Reroll all identities" button. Task 10.</summary>
    public ICommand RerollAllCommand { get; }

    /// <summary>
    /// Reroll a single account's streamer-mode fake identity — the per-row context-menu "reroll"
    /// affordance. Parameter is the row's <see cref="AccountSummary.Id"/> (a <see cref="Guid"/>),
    /// not the whole row. Task 10.
    /// </summary>
    public ICommand RerollAccountCommand { get; }

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
    /// <summary>The currently-default <see cref="FavoriteGame"/>, or null when no game is
    /// marked default (games may still exist -- null is a legitimate "launches open Roblox
    /// home" state, not just the empty-library case). One-way bound on the widget popup
    /// ListBox to highlight the current default. v1.3.x.</summary>
    public FavoriteGame? CurrentDefaultGame
    {
        get => _currentDefaultGame;
        private set
        {
            if (SetField(ref _currentDefaultGame, value))
            {
                OnPropertyChanged(nameof(DefaultGameDisplay));
                OnPropertyChanged(nameof(DefaultGameTooltip));
            }
        }
    }

    /// <summary>
    /// What the widget shows in its toolbar readout. Reads <see cref="FavoriteGame.LocalName"/>
    /// when set, falling back to <see cref="FavoriteGame.Name"/>, then to "Roblox home" when no
    /// game is marked default -- a real state (Task 3), not just an empty-library placeholder.
    /// </summary>
    public string DefaultGameDisplay =>
        _currentDefaultGame?.LocalName ?? _currentDefaultGame?.Name ?? "Roblox home";

    /// <summary>
    /// Tooltip for the default-game widget ToggleButton. Coupled to <see cref="CurrentDefaultGame"/>
    /// so it flips in lockstep with <see cref="DefaultGameDisplay"/> -- explains the home-launch
    /// behavior when no default is set, nudging the user toward the Library instead of leaving
    /// the null state unexplained. v1.9 (Task 3).
    /// </summary>
    public string DefaultGameTooltip =>
        _currentDefaultGame is null
            ? "Launches open Roblox at home. Set a default game in the Library to launch straight into it."
            : "The default game Launch As uses when no per-row pick is set. Click to change.";

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
    /// Passive idle-summary strip text — e.g. "2 accounts idle &gt; 15m". Empty when nothing is
    /// past the warn threshold (the strip collapses on empty, mirroring StatusBanner). Refreshed
    /// on the same 30s ticker cadence that drives the row relative-time refresh. v1.8.
    /// </summary>
    public string IdleSummaryText
    {
        get => _idleSummaryText;
        private set => SetField(ref _idleSummaryText, value);
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

    private bool _alwaysShowRecycle;

    /// <summary>
    /// True when Recycle should ride every running row rather than appearing only under a latched
    /// memory warning (opt-in, Preferences). Bound by both row templates; see
    /// <see cref="IAppSettings.GetAlwaysShowRecycleAsync"/> for why the option exists.
    /// </summary>
    public bool AlwaysShowRecycle
    {
        get => _alwaysShowRecycle;
        set => SetField(ref _alwaysShowRecycle, value);
    }

    /// <summary>Loads accounts + games from disk. Called once at MainWindow load.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var accounts = await _accountStore.ListAsync();
            // Detach the streamer-identity subscription before discarding the old rows — the
            // provider is a long-lived app singleton, so a row left subscribed to its Changed
            // event would stay rooted forever (one leaked AccountSummary per reload).
            foreach (var stale in Accounts)
            {
                stale.DetachIdentityProvider();
            }
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

        // Read the dismissed FPS-cap signature here (not the ctor's fire-and-forget pattern used
        // by InitializeBloxstrapWarningAsync) so it is guaranteed to land before the very first
        // RefreshFpsCapWarning() call below -- LoadAsync is genuinely awaited by MainWindow before
        // first paint, so this can't race the way a fire-and-forget ctor read could.
        _dismissedFpsCapSignature = await _settings.GetDismissedFpsCapWarningSignatureAsync();
        RefreshFpsCapWarning();

        // Same reasoning as the read above — awaited before first paint, so the rows never flash
        // the wrong Recycle visibility. Preferences writes both the setting and this flag.
        AlwaysShowRecycle = await _settings.GetAlwaysShowRecycleAsync();

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

    private readonly SemaphoreSlim _reloadGamesGate = new(1, 1);

    /// <summary>
    /// Reload <see cref="AvailableGames"/> from the favorites store and re-sync each account's
    /// <see cref="AccountSummary.SelectedGame"/> -- preserve current selection if still present,
    /// else fall back to the favorites default. Called on initial load + after the Games dialog
    /// closes (since the user may have added / removed / set-default'd a game). Serialized: the
    /// DefaultChanged handler fires a fire-and-forget reload that must not race an explicit one --
    /// two concurrent AvailableGames/WidgetGames rebuilds corrupt the collection (NRE mid-rebuild),
    /// which bites off-thread in unit tests where there is no UI dispatcher to serialize them.
    /// </summary>
    public async Task ReloadGamesAsync()
    {
        await _reloadGamesGate.WaitAsync();
        try
        {
            await ReloadGamesCoreAsync();
        }
        finally
        {
            _reloadGamesGate.Release();
        }
    }

    private async Task ReloadGamesCoreAsync()
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

        // Default is the game explicitly marked default — no silent first-game fallback.
        // Null is a real state: no default -> Launch As opens Roblox home.
        var defaultGame = AvailableGames.FirstOrDefault(g => g.IsDefault && !IsJoinByLinkSentinel(g) && !g.IsPrivateServer);
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
        RefreshFpsCapWarning();
        _log.LogInformation("Added account {AccountId} ({Username}, userId {UserId}, isMain={IsMain})",
            account.Id, captured.Username, captured.UserId, account.IsMain);
        // Streamer-mode-aware: summary is already wired to the identity provider above, so
        // RenderName returns the fake per-account name while active — same masking as every
        // other visible surface (spec streamer-mode leak fix). The WebView login itself already
        // showed the real Roblox account, but this StatusBanner is a RORORO-owned surface and
        // must not re-print the real name once masking is live.
        StatusBanner = account.IsMain
            ? $"Added {summary.RenderName}. Marked as main — change it any time."
            : $"Added {summary.RenderName}.";
        OnPropertyChanged(nameof(MainAccount));
        OnPropertyChanged(nameof(CompactEmptyKind));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Plugin-host seam: launch a specific account into a resolved target.</summary>
    internal Task LaunchAccountForPluginAsync(AccountSummary summary, LaunchTarget target)
        => LaunchAccountAsync(summary, overrideTarget: target);

    /// <summary>Plugin-host seam: read-only access to the saved private-server store.</summary>
    internal IPrivateServerStore PrivateServerStoreForPlugin => _privateServerStore;

    /// <summary>
    /// A Discord join request landed — either the in-client Join button or the
    /// <c>roblox-rororo:</c> OS protocol handler; <paramref name="origin"/> says which.
    /// <paramref name="confirm"/> is injected so the decision is testable without showing a window.
    /// <para>
    /// <b>Fix round 2 — confirm is gated on ORIGIN, not just destination.</b> A private-server
    /// target always confirms, regardless of origin: Roblox checks permission server-side, so
    /// someone not on that server's list gets bounced, and saying so up front beats a mystery
    /// failure. Separately, ANY <see cref="JoinOrigin.UriHandler"/> join confirms even for a public
    /// server — the reason is origin, not destination risk. A <see cref="JoinOrigin.DiscordClient"/>
    /// join can only fire after the user turned Join on and a friend received a secret RoRoRo
    /// deliberately published; a <c>roblox-rororo:</c> URI can be triggered by any local process,
    /// <c>.url</c> file, or browser navigation, and nothing in it proves Discord sent it. The two
    /// prompts are deliberately different copy (see the two branches below) — never collapsed into
    /// one message, and never shown twice when both conditions hold (a private-server target from
    /// the URI handler still shows exactly one prompt: the private-server one, since it already
    /// carries the stronger "may be denied entry" warning).
    /// </para>
    /// <para>
    /// The row is picked BEFORE the confirm decision (a change from the pre-round-2 shape) because
    /// the URI-origin prompt has to name the account that's about to launch — see the confirm
    /// message below. It uses <see cref="AccountSummary.RenderName"/>, never <c>DisplayName</c>:
    /// streamer mode has to hold outbound, and a modal is outbound. One side effect: if there is
    /// nothing to launch with at all, this now returns the "nothing to join with" outcome without
    /// ever showing a confirm dialog, instead of showing one first and only then discovering there
    /// was nowhere to launch — a behavior improvement, not a regression any existing test depended on.
    /// </para>
    /// <para>
    /// <b>Must be called on the UI thread</b> — it reads the UI-bound <see cref="Accounts"/>
    /// collection, sets <see cref="StatusBanner"/>, and reaches <see cref="LaunchAccountAsync"/>,
    /// none of which tolerate a foreign thread. The two inbound paths differ on whether that's
    /// already true by the time they raise:
    /// <list type="bullet">
    ///   <item><c>App.JoinRequested</c> (the <c>roblox-rororo:</c> URI relay + cold start,
    ///   <see cref="JoinOrigin.UriHandler"/>) is safe as-is — <c>SingleInstanceGuard</c> raises its
    ///   relay inside <c>mainWindow.Dispatcher.Invoke</c>, and the cold-start path runs from
    ///   <c>OnStartup</c>, itself on the UI thread. No extra dispatch needed at the call site.</item>
    ///   <item><see cref="DiscordPresenceService.JoinRequested"/> (the in-client Join button,
    ///   <see cref="JoinOrigin.DiscordClient"/>) is NOT marshaled anywhere in its chain — Lachee's
    ///   <c>OnJoin</c> fires on its own background RPC thread, <c>LacheeDiscordRpcClientAdapter.SafeInvoke</c>
    ///   only try/catches, and <c>DiscordPresenceService.OnJoinRequested</c> forwards synchronously
    ///   on that same thread. Whoever subscribes this path MUST marshal onto the UI thread (e.g.
    ///   <c>Application.Current.Dispatcher.Invoke</c>) before calling this method — this is the
    ///   steady-state Join path (RoRoRo already running, Discord connected), so skipping the
    ///   dispatch here is not a rare-edge risk, it's the common crash.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal async Task<bool> HandleDiscordJoinAsync(LaunchTarget target, JoinOrigin origin, Func<string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Idle-first, then any non-expired account — but never a row mid-launch (IsLaunching),
        // so an inbound join can't double-launch a row that's already in flight. When nothing is
        // idle, the fallback CAN land on an already-running account: Roblox enforces one session
        // per account server-side, so this takes over (kicks) that account's live session rather
        // than silently failing. Accepted tradeoff — the plan chose "join always finds a seat"
        // over "join can be a no-op" — but it must not be a SILENT takeover, hence the distinct
        // banner below.
        var row = Accounts.FirstOrDefault(a => !a.SessionExpired && !a.IsRunning && !a.IsLaunching)
                  ?? Accounts.FirstOrDefault(a => !a.SessionExpired && !a.IsLaunching);
        if (row is null)
        {
            StatusBanner = "Nothing to join with — add an account first.";
            return false;
        }

        var isPrivateServer = target is LaunchTarget.PrivateServer;
        if (isPrivateServer || origin == JoinOrigin.UriHandler)
        {
            // FIX 8 (final whole-branch review, 2026-08-03): row.IsRunning is already known here —
            // the row is chosen above, before this decision. When it's true, the join is about to
            // kick that account's live session; the confirm prompt says so up front rather than the
            // user only learning it from the StatusBanner AFTER already agreeing to something else.
            // The banner below still fires post-confirm — this is additive, not a replacement.
            var takeoverClause = row.IsRunning
                ? $" This takes over {row.RenderName}'s running session."
                : string.Empty;
            var message = isPrivateServer
                ? $"This is a private server — you may be denied entry if you're not on its list.{takeoverClause} Try anyway?"
                : $"This join request came from outside RoRoRo and can't be verified — launching {row.RenderName} into this server.{takeoverClause} Continue anyway?";
            if (!confirm(message))
            {
                return false;
            }
        }

        var takingOverRunningAccount = row.IsRunning;
        if (takingOverRunningAccount)
        {
            StatusBanner = $"Joining via {row.RenderName} — this takes over that account's running session.";
        }

        await LaunchAccountAsync(row, overrideTarget: target).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Returns the launcher pid (<c>RobloxPlayerLauncher.exe</c>, from <see cref="LaunchResult.Started"/>)
    /// on success, 0 otherwise. Fire-and-forget from every OTHER caller's POV — the real player
    /// pid arrives later via <see cref="IRobloxProcessTracker.ProcessAttached"/> — but Task 8's
    /// <see cref="AccountRecycler"/> needs a definitive success/failure signal synchronously to
    /// decide whether it's safe to reset the memory-watchdog baseline, so this reports it directly
    /// rather than making the caller reverse-engineer success from mutated <see cref="AccountSummary"/>
    /// state.
    /// </summary>
    private async Task<int> LaunchAccountAsync(AccountSummary? summary, LaunchTarget? overrideTarget = null)
    {
        if (summary is null)
        {
            return 0;
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
                return 0;
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

            // Ensure a stable per-account browserTrackerId (v1.8.1 trust hygiene — followups
            // 2026-06-30 §6): a real client keeps one btid per account, so a fresh random one
            // per launch reads as a brand-new, unfamiliar client every time. Generate once,
            // persist, reuse for the account's lifetime. Soft-fail: a persist failure still
            // launches with the generated value (next launch just regenerates).
            if (summary.BrowserTrackerId is null)
            {
                var generated = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999);
                try
                {
                    await _accountStore.UpdateBrowserTrackerIdAsync(summary.Id, generated);
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "BrowserTrackerId persist failed for {AccountId}; using unpersisted value this launch.", summary.Id);
                }
                summary.BrowserTrackerId = generated;
            }

            var result = await _launcher.LaunchAsync(cookie, target, fpsCap: summary.FpsCap, browserTrackerId: summary.BrowserTrackerId);
            switch (result)
            {
                case LaunchResult.Started started:
                    await _accountStore.TouchLastLaunchedAsync(summary.Id);
                    summary.StampLaunched(DateTimeOffset.UtcNow);
                    summary.SessionExpired = false;
                    summary.StatusText = string.Empty;
                    summary.LastClosedAtUtc = null;
                    // Task 8: remembered so a later Recycle relaunches into the SAME target
                    // rather than re-resolving from the row's (possibly since-changed) picker.
                    summary.LastLaunchTarget = target;
                    _log.LogInformation("Launcher pid {Pid} for {AccountId}; tracking RobloxPlayerBeta", started.Pid, summary.Id);
                    await RecordSessionStartAsync(summary, target, started.LaunchedAtUtc);
                    // Fire-and-forget: tracker watches for the player process. UI updates flow back
                    // through ProcessAttached / ProcessAttachFailed events.
                    _ = _processTracker.TrackLaunchAsync(summary.Id, started.LaunchedAtUtc);
                    return started.Pid;
                case LaunchResult.CookieExpired:
                    _log.LogInformation("Cookie expired for account {AccountId}", summary.Id);
                    summary.SessionExpired = true;
                    summary.StatusText = string.Empty;
                    return 0;
                case LaunchResult.Limited:
                    _log.LogInformation("Account {AccountId} is rate-limited by Roblox (403)", summary.Id);
                    summary.SessionLimited = true;
                    summary.PresenceState = UserPresenceType.Offline;  // drop the stale "In game" dot
                    summary.CurrentGameName = null;
                    summary.InGameSinceUtc = null;
                    summary.StatusText = string.Empty;                 // copy comes from SecondaryStatusText
                    return 0;
                case LaunchResult.Failed failed when failed.Message.Contains("Roblox does not appear to be installed", StringComparison.OrdinalIgnoreCase):
                    _log.LogWarning("Roblox not installed at launch time for account {AccountId}", summary.Id);
                    summary.StatusText = "Roblox not installed.";
                    ShowRobloxNotInstalledModal();
                    return 0;
                case LaunchResult.Failed failed:
                    _log.LogWarning("Launch failed for account {AccountId}: {Message}", summary.Id, failed.Message);
                    summary.StatusText = failed.Message;
                    return 0;
                default:
                    return 0;
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
    /// <see cref="AccountRecycler.LaunchDelegate"/> implementation — runs the SAME launch path
    /// every other caller uses (<see cref="LaunchAccountAsync"/>, via the plugin-host seam), so a
    /// recycle raises <c>AccountLaunched</c> on the plugin bus exactly like a normal launch. Returns
    /// 0 (never throws) if the account id no longer resolves to a row — the account may have been
    /// removed between the warning firing and the user clicking Recycle.
    /// </summary>
    private Task<int> LaunchForRecycleAsync(Guid accountId, LaunchTarget target, CancellationToken ct)
    {
        // AccountsSnapshot: safe off the UI thread too, matching every other id->summary lookup
        // in this file's plugin-adjacent seams (MainViewModelLaunchInvokerAdapter et al.).
        var summary = AccountsSnapshot.FirstOrDefault(a => a.Id == accountId);
        return summary is null ? Task.FromResult(0) : LaunchAccountAsync(summary, overrideTarget: target);
    }

    /// <summary>
    /// One-click remedy for the tray memory warning (Task 8): stop the account's client and
    /// relaunch into the SAME <see cref="LaunchTarget"/> it was running, via <see cref="AccountRecycler"/>.
    /// Process exit is the only guaranteed reclaim of Roblox's leaked memory on Windows — this is
    /// the actual fix the warning points at, not a workaround. Falls back to
    /// <see cref="ResolveLaunchTarget"/>'s normal resolution when the account has no recorded
    /// <see cref="AccountSummary.LastLaunchTarget"/> yet (e.g. never finished a tracked launch
    /// this session). Internal (not private) so tests can await the sequence directly, mirroring
    /// <see cref="LaunchAccountForPluginAsync"/>.
    /// </summary>
    internal async Task<bool> RecycleAccountAsync(AccountSummary? summary)
    {
        if (summary is null) return false;

        var resolved = summary.LastLaunchTarget ?? ResolveLaunchTarget(summary.SelectedGame, overrideTarget: null);

        // v1.14: "same game" was never the promise the Recycle button makes — the user is mid-run
        // with a squad, and Roblox matchmakes a plain Place anywhere with room. If presence knows
        // which server this account is in, go back to THAT one. The rule (and the matched-pair
        // invariant that makes it correct) lives in ServerInstanceTargeting.
        var target = ServerInstanceTargeting.Upgrade(resolved, summary.CurrentServer);
        if (!ReferenceEquals(target, resolved))
        {
            _log.LogInformation(
                "Recycle: {From} -> {To} for account {AccountId} (presence server {PlaceId}/{JobId}).",
                resolved.GetType().Name, target.GetType().Name, summary.Id,
                summary.CurrentServer?.PlaceId, summary.CurrentServer?.JobId ?? "(none)");
        }

        // Spec log table: pre-recycle private bytes + the target being restored, never a cookie.
        // AccountMemory is a struct, so a plain FirstOrDefault() can't return null on a miss —
        // project through a nullable Select first so "no reading yet" stays distinguishable from
        // a genuine (impossible-in-practice) 0-byte reading.
        long? preRecycleBytes = _memoryWatchdog.GetSnapshot().Accounts
            .Where(a => a.AccountId == summary.Id && a.ReadOk)
            .Select(a => (long?)a.PrivateBytes)
            .FirstOrDefault();
        _log.LogInformation(
            "Recycle requested for account {AccountId} ({DisplayName}): pre-recycle private bytes={PreBytes}, target={Target}",
            summary.Id, summary.DisplayName, preRecycleBytes, target.GetType().Name);

        // Recycle stops the client before relaunching it. Without this the memory alert's own
        // "Recycle suggested" advice produces a dropped-out alert the moment it's followed.
        ExpectClose(summary.Id);

        var ok = await _accountRecycler.RecycleAsync(summary.Id, target).ConfigureAwait(true);
        if (!ok)
        {
            StatusBanner = $"Couldn't recycle {summary.RenderName} — relaunch failed.";
            return false;
        }

        // Started != landed. When we asked for one specific server, check with presence and say so
        // if Roblox put the account somewhere else. Fire-and-forget: the answer is up to 90 s away
        // and nothing downstream waits on it. The launch timestamp is taken HERE, after the
        // relaunch fired, so the row's pre-recycle reading can't be mistaken for a confirmation.
        if (target is LaunchTarget.GameJob job)
        {
            PendingServerVerification = VerifyRecycleLandingAsync(
                summary, new ServerInstance(job.PlaceId, job.JobId), DateTimeOffset.UtcNow);
        }

        return true;
    }

    /// <summary>
    /// Tunables for post-launch landing verification (v1.14). Defaults come from
    /// <see cref="ServerLandingGate"/>; tests shorten them so a verdict lands without real waiting.
    /// </summary>
    internal TimeSpan ServerVerificationPollInterval { get; set; } = ServerLandingGate.PollInterval;

    /// <inheritdoc cref="ServerVerificationPollInterval"/>
    internal TimeSpan ServerVerificationMaxWait { get; set; } = ServerLandingGate.MaxWait;

    /// <summary>
    /// How long a squad waits for its first account to report which server it landed in before
    /// giving up and sending the rest into the game instead. Same physical wait as
    /// <see cref="AnchorGate.MaxWait"/>.
    /// </summary>
    internal TimeSpan SquadServerResolveMaxWait { get; set; } = AnchorGate.MaxWait;

    /// <summary>
    /// The in-flight landing verification for the most recent server-targeted recycle, or null when
    /// the last relaunch made no server-specific claim to check.
    /// </summary>
    internal Task? PendingServerVerification { get; private set; }

    /// <summary>
    /// Watch presence until it can say whether <paramref name="summary"/> got into
    /// <paramref name="requested"/>, then narrate a miss. Success is silent — the user asked to go
    /// back to their server and they did.
    /// </summary>
    private async Task VerifyRecycleLandingAsync(
        AccountSummary summary, ServerInstance requested, DateTimeOffset launchedAtUtc)
    {
        var outcome = await AwaitServerLandingAsync(summary, requested, launchedAtUtc).ConfigureAwait(true);
        _log.LogInformation(
            "Recycle landing for {AccountId}: {Outcome} (requested {PlaceId}/{JobId}, observed {ObservedJobId}).",
            summary.Id, outcome, requested.PlaceId, requested.JobId, summary.CurrentServer?.JobId ?? "(none)");

        if (outcome is ServerLandingOutcome.LandedElsewhere or ServerLandingOutcome.NeverLanded)
        {
            StatusBanner = ServerLandingReport.ComposeRecycleMiss(summary.RenderName, outcome);
        }
    }

    /// <summary>
    /// Poll presence for one account until <see cref="ServerLandingGate"/> reaches a verdict or the
    /// window closes. Nudges the poller directly rather than waiting out the 25 s background tick;
    /// a refresh failure is not a verdict, so it degrades to that background tick instead of ending
    /// the wait. Bounded by <see cref="ServerVerificationMaxWait"/> — never hangs.
    /// </summary>
    private async Task<ServerLandingOutcome> AwaitServerLandingAsync(
        AccountSummary summary, ServerInstance requested, DateTimeOffset launchedAtUtc)
    {
        var deadline = DateTime.UtcNow + ServerVerificationMaxWait;
        while (true)
        {
            var outcome = ServerLandingGate.Evaluate(
                requested,
                summary.CurrentServer,
                summary.InGame,
                summary.PresenceUpdatedAtUtc,
                launchedAtUtc,
                ServerLandingGate.WaitExpired(DateTime.UtcNow, deadline));

            if (outcome != ServerLandingOutcome.Pending)
            {
                return outcome;
            }

            try
            {
                await _presenceService.RequestImmediateRefreshAsync(summary.Id).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Landing-verification presence refresh failed for {AccountId}; falling back to the background poll.", summary.Id);
            }

            await Task.Delay(ServerVerificationPollInterval).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Highlights one row — Task 8's tray memory-warning balloon click
    /// (<see cref="ITrayService.RequestFocusAccount"/>) wired from <c>App.xaml.cs</c>. Clears the
    /// previous highlight first so at most one row is ever flagged at a time. A no-op (previous
    /// highlight still clears) if the id no longer resolves to a saved row.
    /// </summary>
    internal void SetFocusedAccount(Guid accountId)
    {
        if (_focusedAccountId is { } previousId)
        {
            var previousRow = Accounts.FirstOrDefault(a => a.Id == previousId);
            if (previousRow is not null)
            {
                previousRow.IsFocused = false;
            }
        }

        _focusedAccountId = accountId;
        var row = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (row is not null)
        {
            row.IsFocused = true;
        }
    }

    /// <summary>
    /// Launch every non-expired, non-running account in sequence with the
    /// <see cref="InterLaunchThrottle"/> gap (5s as of v1.4.2.0). The gap gives
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
                launchingBanner: (summary, n, total) => $"Launching {summary.RenderName} ({n} of {total})...");
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
        a.IsSelected, a.SessionExpired, a.SessionLimited, a.InGame, a.IsRunning, a.IsLaunching, a.DisplayName);

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
            .Where(a => a.IsSelected && !a.SessionExpired && !a.SessionLimited
                        && !LaunchEligibility.IsBusy(ToLaunchCandidate(a)) && !a.IsLaunching)
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
    internal TimeSpan InterLaunchThrottle { get; set; } = TimeSpan.FromMilliseconds(5000);

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
    /// <param name="resolveTailTarget">
    /// v1.14 server-instance targeting: when set, #1 is dispatched on its own and this decides what
    /// the REST launch into, given where #1 actually ended up. That is the only way to put a squad
    /// in one public server — the server does not exist as an address until somebody is in it.
    /// Returning null keeps <paramref name="overrideTarget"/> for the tail.
    /// </param>
    private async Task DispatchBatchAsync(
        IReadOnlyList<AccountSummary> targets,
        LaunchTarget? overrideTarget,
        Func<AccountSummary, int, int, string> launchingBanner,
        bool waitForLanding = false,
        Func<AccountSummary, DateTimeOffset, Task<LaunchTarget?>>? resolveTailTarget = null)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var decision = await DecidePreWarmAsync().ConfigureAwait(true);

        // Single-account batches can't benefit from serializing the update (there is no "rest"),
        // so they always go down the normal path — the lone launch IS the pre-warm. A tail-target
        // resolver forces the same split for a different reason: the tail's address is unknown
        // until #1 lands.
        if ((decision == PreWarmDecision.PreWarmThenRelease || resolveTailTarget is not null) && targets.Count > 1)
        {
            // --- Pre-warm: launch #1, hold the rest until the update clears. ---
            var first = targets[0];
            StatusBanner = launchingBanner(first, 1, targets.Count);
            // Stamped BEFORE the launch so the tail resolver can tell a presence reading about the
            // client we just started from one left over from before it.
            var firstLaunchedAtUtc = DateTimeOffset.UtcNow;
            await LaunchAccountAsync(first, overrideTarget).ConfigureAwait(true);

            if (decision == PreWarmDecision.PreWarmThenRelease)
            {
                await WaitForPreWarmAsync(first).ConfigureAwait(true);
            }

            if (resolveTailTarget is not null)
            {
                // Waiting for #1 to be IN the game subsumes the pre-warm wait (installer gone +
                // attached is strictly earlier), so an update-pending batch is still serialized.
                overrideTarget = await resolveTailTarget(first, firstLaunchedAtUtc).ConfigureAwait(true) ?? overrideTarget;
            }

            if (waitForLanding)
            {
                // Careful mode: #1's install-pending pre-warm only waited for attach, not for it
                // to land in-game — without this wait #2 could release into the same mid-join
                // window careful mode exists to avoid.
                var firstLanded = await WaitForLandingAsync(first).ConfigureAwait(true);
                if (!firstLanded)
                {
                    StatusBanner = $"{first.RenderName} didn't land within {(int)AnchorGate.MaxWait.TotalSeconds}s — continuing.";
                }
            }

            // Release the REST through the existing throttled loop. #1 is already up.
            await ReleaseBatchAsync(targets, overrideTarget, launchingBanner, startIndex: 1, waitForLanding).ConfigureAwait(true);
            return;
        }

        // --- Normal path: strap-handled OR no update pending OR a single-account batch. ---
        await ReleaseBatchAsync(targets, overrideTarget, launchingBanner, startIndex: 0, waitForLanding).ConfigureAwait(true);
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
    /// Careful-mode / anchor wait: poll the summary's presence-fed InGame flag until it lands or
    /// the AnchorGate deadline passes. Presence is the existing 25s pipeline — no new Roblox
    /// calls. Timeout falls through (never strands the batch); the caller narrates the fallback.
    /// </summary>
    private async Task<bool> WaitForLandingAsync(AccountSummary summary)
    {
        var deadline = DateTime.UtcNow + AnchorGate.MaxWait;
        while (true)
        {
            if (AnchorGate.WaitComplete(summary.InGame))
            {
                return true;
            }
            if (AnchorGate.WaitExpired(DateTime.UtcNow, deadline))
            {
                _log.LogWarning("Landing wait for {Account} hit the {Cap}s cap; continuing.",
                    summary.DisplayName, (int)AnchorGate.MaxWait.TotalSeconds);
                return false;
            }
            await Task.Delay(PreWarmPollInterval).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The throttled launch loop, factored out of Launch-multiple / Private-server so the pre-warm
    /// path can release the tail of the batch through the SAME loop (with the SAME 5s throttle and
    /// "(n of total)" banner) after #1 is up. <paramref name="startIndex"/> skips the already-warmed
    /// first account; the throttle is applied between every dispatched client. <paramref
    /// name="waitForLanding"/> (careful mode, v1.9.0) serializes each join behind an
    /// <see cref="AnchorGate"/>-bounded wait for that account's presence-fed InGame flag before
    /// moving on — a trust-aware throttle beyond the fixed 5s inter-launch gap.
    /// </summary>
    private async Task ReleaseBatchAsync(
        IReadOnlyList<AccountSummary> targets,
        LaunchTarget? overrideTarget,
        Func<AccountSummary, int, int, string> launchingBanner,
        int startIndex,
        bool waitForLanding = false)
    {
        for (var idx = startIndex; idx < targets.Count; idx++)
        {
            var summary = targets[idx];
            StatusBanner = launchingBanner(summary, idx + 1, targets.Count);
            await LaunchAccountAsync(summary, overrideTarget).ConfigureAwait(true);
            if (waitForLanding)
            {
                // careful mode: serialize joins
                var landed = await WaitForLandingAsync(summary).ConfigureAwait(true);
                if (!landed)
                {
                    StatusBanner = $"{summary.RenderName} didn't land within {(int)AnchorGate.MaxWait.TotalSeconds}s — continuing.";
                }
            }
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

        var window = new SquadLaunchWindow(_privateServerStore, _api, _settings, url => ResolveShareUrlAsync(url), eligible, running, expired)
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
    /// Mass-launch every eligible account into the same private server, throttled
    /// <see cref="InterLaunchThrottle"/> (5s) apart so
    /// the process tracker can FIFO-claim each <c>RobloxPlayerBeta.exe</c> by start time. The
    /// override target trumps each row's per-account SelectedGame.
    /// <para>
    /// v1.9.0 trust-aware squad launch: <see cref="SquadLaunchPlan.Build"/> splits the eligible batch
    /// into <c>Direct</c> (dispatched straight into <paramref name="target"/>, unchanged from pre-v1.9)
    /// and <c>Flagged</c> (<see cref="AccountSummary.JoinViaFriend"/> accounts, which instead follow a
    /// landed direct-batch anchor via <see cref="LaunchTarget.FollowFriend"/> — spec §"Trust-aware squad
    /// launch"). Zero flagged accounts collapses to exactly the pre-v1.9 single dispatch. Careful mode
    /// (<see cref="IAppSettings.GetCarefulSquadLaunchAsync"/>) threads <c>waitForLanding</c> through
    /// every dispatch path so joins serialize on presence instead of firing on the fixed throttle alone.
    /// </para>
    /// <para>
    /// v1.14 server-instance targeting: <paramref name="target"/> may now be a PUBLIC place, which
    /// was structurally impossible before (this parameter was typed <c>PrivateServer</c>). A public
    /// server has no address until someone is standing in it, so #1 goes in as a plain
    /// <see cref="LaunchTarget.Place"/>, presence reports which server it got, and the rest are
    /// dispatched at THAT server via <see cref="LaunchTarget.GameJob"/>. If #1's server can't be
    /// read in time the rest still launch into the game — a scattered squad beats no squad.
    /// </para>
    /// </summary>
    internal async Task SquadLaunchAsync(LaunchTarget target)
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
            var careful = await _settings.GetCarefulSquadLaunchAsync();
            var plan = SquadLaunchPlan.Build(targets);
            _log.LogInformation(
                "Squad launch: target={Target}, {Count} eligible ({Direct} direct, {Flagged} join-via-friend), careful={Careful}, {Running} running, {Expired} expired, {Deselected} deselected",
                target.GetType().Name, targets.Count, plan.Direct.Count, plan.Flagged.Count, careful,
                result.Breakdown.Running, result.Breakdown.Expired, result.Breakdown.Deselected);
            if (targets.Count == 0)
            {
                StatusBanner = result.ZeroEligibleBanner;
                return;
            }

            // A public place needs #1 to land before the rest have an address to aim at. A private
            // server already IS one address, so it keeps the pre-v1.14 single-shot dispatch.
            ServerInstance? squadServer = null;
            Func<AccountSummary, DateTimeOffset, Task<LaunchTarget?>>? resolveTailTarget = target is LaunchTarget.Place
                ? async (first, launchedAtUtc) =>
                {
                    StatusBanner = $"Waiting for {first.RenderName} to land so the rest can join that server...";
                    squadServer = await WaitForServerInstanceAsync(first, launchedAtUtc).ConfigureAwait(true);
                    if (squadServer is null)
                    {
                        StatusBanner = $"Couldn't read {first.RenderName}'s server in time — the rest are joining the game, not that server.";
                        return null;
                    }
                    return ServerInstanceTargeting.Upgrade(target, squadServer);
                }
            : null;

            // Phase 1 — direct batch (byte-identical to today when nothing is flagged + careful off).
            if (plan.Direct.Count > 0)
            {
                await DispatchBatchAsync(
                    plan.Direct,
                    overrideTarget: target,
                    launchingBanner: (summary, n, total) => $"Joining server: {summary.RenderName} ({n} of {total})...",
                    waitForLanding: careful,
                    resolveTailTarget: resolveTailTarget);
            }

            if (plan.Flagged.Count > 0)
            {
                // Phase 2 — anchor: first direct-batch account that is InGame with a known userId.
                AccountSummary? anchor = null;
                if (plan.Direct.Count > 0)
                {
                    StatusBanner = "Waiting for a squad member to land (for join-via-friend accounts)...";
                    var deadline = DateTime.UtcNow + AnchorGate.MaxWait;
                    while (anchor is null && !AnchorGate.WaitExpired(DateTime.UtcNow, deadline))
                    {
                        anchor = AnchorGate.PickAnchor(plan.Direct);
                        if (anchor is null)
                        {
                            await Task.Delay(PreWarmPollInterval).ConfigureAwait(true);
                        }
                    }
                }

                if (anchor is { RobloxUserId: { } anchorUserId })
                {
                    // Phase 3 — flagged accounts follow the anchor into the same server.
                    _log.LogInformation("Join-via-friend: {Count} account(s) following anchor {Anchor} (userId {UserId}).",
                        plan.Flagged.Count, anchor.DisplayName, anchorUserId);
                    await ReleaseBatchAsync(
                        plan.Flagged,
                        overrideTarget: new LaunchTarget.FollowFriend(anchorUserId),
                        launchingBanner: (summary, n, total) => $"{summary.RenderName} joining via {anchor.RenderName} ({n} of {total})...",
                        startIndex: 0,
                        waitForLanding: careful);
                }
                else
                {
                    // Fallback — never strand: flagged accounts go direct with the standard throttle.
                    _log.LogWarning("Join-via-friend: no anchor landed within {Cap}s (direct batch: {Direct}); falling back to direct joins for {Count} flagged account(s).",
                        (int)AnchorGate.MaxWait.TotalSeconds, plan.Direct.Count, plan.Flagged.Count);
                    StatusBanner = plan.Direct.Count == 0
                        ? "No direct-join accounts to anchor on — flagged accounts joining directly."
                        : "No squad member landed in time — flagged accounts joining directly.";
                    if (plan.Direct.Count == 0)
                    {
                        // No Phase 1 ran, so no anchor was ever possible and the pre-warm gate
                        // never fired for this squad. Route the all-flagged fallback through
                        // DispatchBatchAsync so an install-pending update still serializes #1
                        // instead of firing every flagged client at once via ReleaseBatchAsync.
                        await DispatchBatchAsync(
                            plan.Flagged,
                            overrideTarget: target,
                            launchingBanner: (summary, n, total) => $"Joining server (direct fallback): {summary.RenderName} ({n} of {total})...",
                            waitForLanding: careful,
                            // Nothing landed before this batch, so #1 here defines the server the
                            // rest aim at — same first-lands-then-follow shape as the direct batch.
                            resolveTailTarget: resolveTailTarget);
                    }
                    else
                    {
                        // Anchor timed out, but Phase 1 already ran the pre-warm decision for the
                        // direct batch — no need to re-gate here. A squad server read during phase 1
                        // still applies: these accounts couldn't follow a friend, but they can still
                        // be sent at the server the direct batch is in.
                        await ReleaseBatchAsync(
                            plan.Flagged,
                            overrideTarget: ServerInstanceTargeting.Upgrade(target, squadServer),
                            launchingBanner: (summary, n, total) => $"Joining server (direct fallback): {summary.RenderName} ({n} of {total})...",
                            startIndex: 0,
                            waitForLanding: careful);
                    }
                }
            }

            StatusBanner = result.PartialBanner(targets.Count, "Squad launch finished");

            // Everyone was aimed at one specific server — check with presence who actually made it.
            // Fire-and-forget: the verdict is up to 90 s out and the batch is done either way.
            if (squadServer is not null)
            {
                var dispatched = plan.Direct.Concat(plan.Flagged).ToList();
                PendingServerVerification = VerifySquadLandingsAsync(dispatched, squadServer, DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Wait for one account to be IN a game and report WHICH server, so the rest of a squad can be
    /// aimed at it. Bounded by <see cref="AnchorGate.MaxWait"/> — the same physical wait the anchor
    /// gate measures, plus the job id. Null on timeout, on privacy withholding the job id, or if the
    /// account never lands; every caller falls back to launching into the game.
    /// </summary>
    private async Task<ServerInstance?> WaitForServerInstanceAsync(AccountSummary summary, DateTimeOffset launchedAtUtc)
    {
        var deadline = DateTime.UtcNow + SquadServerResolveMaxWait;
        while (true)
        {
            // Freshness matters here for the same reason it does in ServerLandingGate: only a
            // reading taken after the launch describes the client we just started.
            if (summary.PresenceUpdatedAtUtc is { } at && at > launchedAtUtc
                && summary.InGame && summary.CurrentServer is { } server)
            {
                _log.LogInformation("Squad server resolved from {Account}: place={PlaceId} job={JobId}.",
                    summary.DisplayName, server.PlaceId, server.JobId);
                return server;
            }

            if (ServerLandingGate.WaitExpired(DateTime.UtcNow, deadline))
            {
                _log.LogWarning(
                    "No server id from {Account} within {Cap}s (inGame={InGame}); the rest of the squad joins the game instead.",
                    summary.DisplayName, (int)AnchorGate.MaxWait.TotalSeconds, summary.InGame);
                return null;
            }

            try
            {
                await _presenceService.RequestImmediateRefreshAsync(summary.Id).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Squad server-resolve presence refresh failed for {AccountId}; falling back to the background poll.", summary.Id);
            }

            await Task.Delay(ServerVerificationPollInterval).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Check each squad member against the server they were aimed at and name the ones who missed.
    /// "We're all together" is the entire point of Squad Launch, so a partial miss is worth saying
    /// out loud; a clean sweep says nothing.
    /// </summary>
    private async Task VerifySquadLandingsAsync(
        IReadOnlyList<AccountSummary> dispatched, ServerInstance requested, DateTimeOffset launchedAtUtc)
    {
        var outcomes = await Task.WhenAll(dispatched.Select(async summary =>
            (summary, outcome: await AwaitServerLandingAsync(summary, requested, launchedAtUtc).ConfigureAwait(true))))
            .ConfigureAwait(true);

        // Split by outcome, not by "missed": one group should recycle and the other must not.
        // A queued account is one spot away from where it wants to be; recycling throws that away.
        var elsewhere = outcomes
            .Where(o => o.outcome is ServerLandingOutcome.LandedElsewhere)
            .Select(o => o.summary.RenderName)
            .ToList();
        var notInYet = outcomes
            .Where(o => o.outcome is ServerLandingOutcome.NeverLanded)
            .Select(o => o.summary.RenderName)
            .ToList();

        _log.LogInformation(
            "Squad landing check for server {JobId}: {Elsewhere} elsewhere, {NotInYet} not in yet, of {Total}.",
            requested.JobId, elsewhere.Count, notInYet.Count, dispatched.Count);

        if (ServerLandingReport.ComposeSquadMiss(elsewhere, notInYet, dispatched.Count) is { } banner)
        {
            StatusBanner = banner;
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

        var window = new JoinByLinkWindow(_api, url => ResolveShareUrlAsync(url), summary.RenderName)
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
    /// Resolve the MAIN account as a friends-list source for a picker opened on <paramref name="openedRow"/>.
    /// Returns null when there's no main, the main IS the opened row, or the main's RobloxUserId can't be
    /// resolved (missing/corrupt cookie, expired session, or profile-fetch failure) — every one of which
    /// collapses the picker to single-source. Resolves + persists the main's userId on demand (soft-fail),
    /// mirroring the opened-row resolution in <see cref="OpenFriendFollowAsync"/>.
    /// </summary>
    internal async Task<FriendSource?> TryResolveMainFriendSourceAsync(AccountSummary openedRow)
    {
        var main = MainAccount;
        if (main is null || main.Id == openedRow.Id)
        {
            return null;
        }

        long userId = main.RobloxUserId ?? 0;
        if (userId <= 0)
        {
            try
            {
                var cookie = await _accountStore.RetrieveCookieAsync(main.Id);
                var profile = await _api.GetUserProfileAsync(cookie);
                userId = profile.UserId;
                main.RobloxUserId = userId;
                try
                {
                    await _accountStore.UpdateRobloxUserIdAsync(main.Id, userId);
                }
                catch (Exception persistEx)
                {
                    _log.LogDebug(persistEx, "Couldn't persist main RobloxUserId {AccountId} (Friends modal).", main.Id);
                }
            }
            catch (Exception ex)
            {
                // Any failure (missing/corrupt cookie, expired session, fetch) collapses to single-source —
                // the user can still browse the opened account's own friends.
                _log.LogDebug(ex, "Couldn't resolve main's userId for Friends modal {AccountId}; single-source fallback.", main.Id);
                return null;
            }
        }

        return new FriendSource(main.Id, userId, main.DisplayName, IsMain: true);
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
                StatusBanner = $"{summary.RenderName}'s session expired — re-authenticate first.";
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Couldn't resolve userId for friends modal {AccountId}", summary.Id);
                StatusBanner = "Couldn't reach Roblox to load friends. Try again in a moment.";
                return;
            }
        }

        // Build the picker's friend sources: the opened row is always a source (and always the
        // launcher); the main is added as the default source when present and distinct, so main's
        // friends show first (alts usually have empty lists). The window retrieves each source's
        // cookie fresh per refresh — never holds plaintext for its lifetime.
        var rowSource = new FriendSource(summary.Id, userId, summary.DisplayName, summary.IsMain);
        var mainSource = await TryResolveMainFriendSourceAsync(summary);
        var (sources, defaultIndex) = FriendSourcePlan.Build(rowSource, mainSource);

        var window = new FriendFollowWindow(_api, _accountStore, sources, defaultIndex, summary.Id, _streamerIdentity)
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
        ExpectClose(summary.Id);
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
            DiscordPresence?.Refresh();
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
            DiscordPresence?.Refresh();
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
        => Application.Current?.Dispatcher.Invoke(() => ApplyPresence(e));

    /// <summary>
    /// UI-thread body of the presence handler (internal for tests — <see cref="OnAccountPresenceUpdated"/>
    /// marshals to it, same seam shape as <see cref="ApplySessionExpired"/>).
    /// </summary>
    internal void ApplyPresence(AccountPresenceEventArgs e)
    {
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            // A presence poll landing means Roblox is answering this cookie again — clear Limited.
            summary.SessionLimited = false;
            summary.PresenceUpdatedAtUtc = e.OccurredAtUtc;

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
                // WHICH server, not just which game (v1.14). Null when privacy or timing withheld
                // the job id — Recycle then behaves exactly as it did before this feature.
                summary.CurrentServer = e.Server;
            }
            else
            {
                // Capture combined active state BEFORE mutating presence so we can tell whether
                // this poll is the moment the row went fully inactive. The game name goes with it:
                // the dropped-out alert wants to say WHICH game the account fell out of, and the
                // lines below have already blanked it by the time that alert is built.
                var wasActive = summary.InGame || summary.IsRunning;
                var lastGameName = summary.CurrentGameName;

                summary.PresenceState = e.PresenceType;
                summary.CurrentGameName = null;
                summary.CurrentPlaceId = null;
                summary.CurrentServer = null;
                summary.InGameSinceUtc = null;
                // MINOR 1 (re-review, 2026-08-03): an account cannot join a genuinely different
                // server — public or private — without first fully leaving whatever it was in, so
                // presence reporting not-in-game is the deterministic point to drop a private-server
                // LastLaunchTarget. Without this, a stale private code from an earlier launch would
                // keep attaching itself to a later PUBLIC server of the same place (place matching
                // alone can't catch this, and the blocking-finding fix above deliberately stopped
                // requiring presence to agree on place at all). A within-session universe teleport
                // never passes through this branch — CurrentServer just gets a fresh (place, job)
                // while PresenceState stays InGame the whole time — so a genuinely continuous
                // private-server session's credential survives exactly as intended.
                summary.LastLaunchTarget = null;

                // Presence-confirmed close: the row was active, presence now says not-in-game,
                // and the process is also gone — both signals agree, so stamp the close. This is
                // the close-stamp the deferred OnProcessExited handed off to presence (the ghost
                // case resolving once the respawned client truly closes).
                if (wasActive && !summary.IsRunning)
                {
                    summary.LastClosedAtUtc = e.OccurredAtUtc;

                    // The dropped-out alert rides the SAME both-signals-agree rule, deliberately.
                    // The ghost case (process killed by the anti-multilaunch bootstrapper, client
                    // respawned under a new pid) is a false alarm we already know how to suppress —
                    // paging someone at 3am for it would burn the feature's credibility on the
                    // first night. RenderName, never DisplayName: streamer mode holds outbound.
                    //
                    // A close the user ASKED for is the other false alarm — see _expectedCloses.
                    if (WasCloseExpected(summary.Id, e.OccurredAtUtc))
                    {
                        _expectedCloses.Remove(summary.Id);
                        _log.LogDebug("Suppressed a dropped-out alert for {AccountId}: the close was user-initiated.", summary.Id);
                    }
                    else
                    {
                        // Both names travel: RenderName is what every destination shows by
                        // default, DisplayName is used only by the clan channel. See AlertTrigger.
                        RaiseAlerts([new AlertTrigger(
                            AlertKind.AccountDroppedOut, summary.Id, summary.RenderName,
                            summary.DisplayName, lastGameName, PrivateBytes: null, e.OccurredAtUtc)]);
                    }
                }
            }

            // Mirror the OnProcessAttached/Exited refresh shape so command-enablement (LaunchAll
            // CanExecute keys off InGame || IsRunning) and the compact view stay in sync.
            // LiveProcessCount is process-only, so it isn't touched here.
            OnPropertyChanged(nameof(CompactRows));
            OnPropertyChanged(nameof(HasCompactRows));
            RelayCommand.RaiseCanExecuteChanged();
            DiscordPresence?.Refresh();
        }
    }

    /// <summary>
    /// Presence poll returned 401 for one account (v1.5.0) — the cookie died between launches.
    /// Flip the row to the yellow "Session expired" badge. Marshalled to the dispatcher because
    /// the poller raises this off a threadpool thread. Spec §"Error handling" (401 from presence).
    ///
    /// Re-flag race guard (2026-07-03): the decision is made HERE, on the UI thread, where both
    /// this flip and reauth's tag-clear (<see cref="ReauthenticateAsync"/> sets SessionExpired =
    /// false as its last act) run — so whichever the dispatcher processes last wins consistently.
    /// If the account's live cookie generation is past the one this poll captured at start, a
    /// reauth replaced the cookie mid-poll and this 401 is stale — drop it rather than clobber a
    /// session the user just refreshed. reauth's UpdateCookieAsync bumps the generation before its
    /// tag-clear, so a suppressed flip can never resurrect the expired badge.
    /// </summary>
    private void OnAccountSessionExpired(object? sender, AccountSessionExpiredEventArgs e)
        => Application.Current?.Dispatcher.Invoke(() => ApplySessionExpired(e.AccountId, e.PolledCookieGeneration));

    /// <summary>
    /// UI-thread body of the 401 handler (internal for tests — <see cref="OnAccountSessionExpired"/>
    /// marshals to it). Runs the re-flag-race guard then flips the row. Must run on the UI thread so
    /// the generation read + flip are serialized against reauth's cookie-update + tag-clear.
    /// </summary>
    internal void ApplySessionExpired(Guid accountId, int polledCookieGeneration)
    {
        var summary = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (summary is null) return;

        var currentGeneration = _accountStore.GetCookieGeneration(accountId);
        if (currentGeneration != polledCookieGeneration)
        {
            _log.LogDebug(
                "Presence 401 for {AccountId} dropped — cookie was re-authed since the poll started (gen {Polled} -> {Current}).",
                accountId, polledCookieGeneration, currentGeneration);
            return;
        }

        summary.SessionExpired = true;
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void OnAccountSessionLimited(object? sender, Guid accountId)
        => Application.Current?.Dispatcher.Invoke(() => ApplySessionLimited(accountId));

    /// <summary>
    /// UI-thread body of the session-limited handler (internal for tests — <see cref="OnAccountSessionLimited"/>
    /// marshals to it, same seam shape as <see cref="ApplyPresence"/>/<see cref="ApplySessionExpired"/> —
    /// <c>Application.Current</c> is null off a real WPF host, so a test calling the raw event
    /// handler would silently no-op instead of exercising this body).
    /// </summary>
    internal void ApplySessionLimited(Guid accountId)
    {
        var summary = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (summary is null) return;
        summary.SessionLimited = true;
        summary.PresenceState = UserPresenceType.Offline;  // clear the frozen "In game"
        summary.CurrentGameName = null;
        summary.InGameSinceUtc = null;
        RelayCommand.RaiseCanExecuteChanged();
        DiscordPresence?.Refresh();
    }

    /// <summary>
    /// Coalesced, edge-triggered idle-warn crossing from <see cref="IActivityMonitor"/> (v1.8).
    /// Marshalled to the dispatcher because the monitor's sample timer raises this off its own
    /// timer thread, mirroring <see cref="OnAccountSessionLimited"/>. The presenter itself owns
    /// the muted check + message shape; this handler only forwards the coalesced count + the
    /// cached threshold/mute settings loaded by <see cref="InitializeIdleSettingsAsync"/>.
    /// </summary>
    private void OnActivityWarnCrossed(object? sender, IReadOnlyList<Guid> crossed)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _idleAlertPresenter.Notify(crossed.Count, _idleWarnThresholdMinutes, _muteIdleAlerts);
        });
    }

    /// <summary>
    /// Passive 30s repaint of every row's memory chip from the watchdog's last completed sample.
    /// Task 7.
    /// </summary>
    private void RefreshMemoryChips() => ApplyMemory(_memoryWatchdog.GetSnapshot());

    /// <summary>
    /// Projects a <see cref="MemoryPressureSnapshot"/> onto the visible rows via
    /// <see cref="MemoryChipFormatter.Format"/>. <c>MemoryWarning</c> (and therefore the chip's
    /// "▲" and the Recycle button's visibility, see <c>MainWindow.xaml</c>'s
    /// <c>MemoryWarning</c> DataTrigger) is CONDITION-derived from this snapshot every time this
    /// runs — never edge-derived from "did a crossing just fire." <see cref="IMemoryWatchdog.PressureCrossed"/>
    /// is edge-triggered (fires once per latch), but the passive 30s ticker
    /// (<see cref="RefreshMemoryChips"/>) calls this same method with <c>warned</c> nowhere in
    /// sight — if MemoryWarning depended on which caller invoked this, the ticker's very next tick
    /// would silently erase a warning (and the Recycle button with it) while the underlying
    /// pressure condition still holds. Fixed 2026-08-01 (final-branch review CRITICAL 1) — the
    /// prior "warned: true only from PressureCrossed, warned: false from the ticker" shape did
    /// exactly that.
    /// <para>
    /// <c>account.OverCap</c> scopes the cap axis to the account that is actually over cap
    /// (deliberately per-row, unlike the old unconditional <c>warned: true</c> which painted every
    /// row the moment ANY one account crossed). The projection axis is machine-wide by design —
    /// <see cref="MemoryPressureSnapshot.MinutesToCeiling"/> describes the whole machine's runway,
    /// not any one client's — so every readable row shares that half of the verdict.
    /// </para>
    /// A row with no matching account in the snapshot (not yet launched, or launched after the
    /// last sample) is left untouched. Task 7.
    /// </summary>
    /// <summary>
    /// Fires when something alert-worthy happened. The composition root wires this to
    /// <c>AlertDispatcher.DispatchAsync</c>.
    /// <para>
    /// An event rather than an injected dispatcher, deliberately: this constructor already takes
    /// twenty-odd dependencies, and every one added means touching every construction site in the
    /// test suite. Routing, muting, cooldown, and delivery all live in the dispatcher — the view
    /// model's whole job here is to notice, name the account, and say when.
    /// </para>
    /// </summary>
    internal event EventHandler<IReadOnlyList<AlertTrigger>>? AlertsRaised;

    /// <summary>
    /// Accounts the user just closed on purpose, and when. A dropped-out alert exists to report a
    /// client dying when nobody asked — a crash, a kick, a session dropping while the user is out.
    /// Clicking Stop and then being told the thing you clicked Stop on stopped is noise.
    /// <para>
    /// The sharpest case is self-inflicted: the memory alert's own text says "Recycle suggested,"
    /// and Recycle stops the client — so without this, following the advice in one alert
    /// immediately produces another.
    /// </para>
    /// </summary>
    private readonly Dictionary<Guid, DateTimeOffset> _expectedCloses = [];

    /// <summary>
    /// How long a deliberate close stays "expected." Long enough to cover the stop plus the
    /// presence poll that confirms it; short enough that a client dying for real a minute after
    /// you touched it still reports.
    /// </summary>
    internal static readonly TimeSpan ExpectedCloseWindow = TimeSpan.FromSeconds(60);

    /// <summary>Mark a close as user-initiated so it does not raise a dropped-out alert.</summary>
    internal void ExpectClose(Guid accountId) => _expectedCloses[accountId] = DateTimeOffset.UtcNow;

    /// <summary>Mark every running account as expected — app shutdown closes them all at once.</summary>
    internal void ExpectCloseForAll()
    {
        foreach (var row in AccountsSnapshot)
        {
            _expectedCloses[row.Id] = DateTimeOffset.UtcNow;
        }
    }

    private bool WasCloseExpected(Guid accountId, DateTimeOffset atUtc) =>
        _expectedCloses.TryGetValue(accountId, out var asked) && atUtc - asked <= ExpectedCloseWindow;

    /// <summary>
    /// Mute or unmute Discord alerts for one account, persisting through the config store.
    /// <para>
    /// Read-modify-write of the whole <see cref="DiscordConfig"/> record, deliberately explicit:
    /// getting this wrong silently wipes the user's webhook URL or presence toggle — settings they
    /// would then have to re-enter without ever being told why. Pinned by a test.
    /// </para>
    /// </summary>
    internal async Task SetAlertsMutedAsync(AccountSummary summary, bool muted)
    {
        ArgumentNullException.ThrowIfNull(summary);
        summary.AlertsMuted = muted;

        if (AlertConfigStore is not { } store) return;

        try
        {
            var config = await store.LoadAsync().ConfigureAwait(true);
            var ids = config.MutedAccountIds.ToHashSet();
            if (muted) { ids.Add(summary.Id); } else { ids.Remove(summary.Id); }
            await store.SaveAsync(config with { MutedAccountIds = [.. ids] }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Couldn't persist the alert mute for this account; it holds for this session only.");
        }
    }

    /// <summary>
    /// Guarded raise. An alert is a passenger — same contract as presence. A throwing subscriber
    /// must never propagate back into <see cref="ApplyPresence"/> or the watchdog's crossing
    /// handler, because both of those sit on paths that keep the roster honest.
    /// </summary>
    private void RaiseAlerts(IReadOnlyList<AlertTrigger> triggers)
    {
        if (triggers.Count == 0) return;

        try
        {
            AlertsRaised?.Invoke(this, triggers);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Alert subscriber threw; the alert was dropped.");
        }
    }

    /// <summary>
    /// Projects a memory crossing onto the accounts worth naming in an alert.
    /// <para>
    /// Accounts genuinely over their own cap are the answer when there are any. When the crossing
    /// is projection-only — the machine is heading for the ceiling but no single client is over
    /// cap — the watchdog still names the client worth recycling
    /// (<see cref="MemoryPressureSnapshot.TargetAccountId"/>), and the alert says which one rather
    /// than going quiet on a crossing that genuinely fired.
    /// </para>
    /// <para>
    /// <c>ReadOk == false</c> is excluded everywhere: it means UNKNOWN, not zero. Alerting on a
    /// reading we could not take is a wrong number stated confidently, which is the exact failure
    /// the watchdog exists to prevent.
    /// </para>
    /// </summary>
    internal IReadOnlyList<AlertTrigger> BuildMemoryAlerts(MemoryPressureSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var readable = snapshot.Accounts.Where(a => a.ReadOk).ToList();
        var named = readable.Where(a => a.OverCap).ToList();

        if (named.Count == 0 && snapshot.TargetAccountId is { } target)
        {
            named = readable.Where(a => a.AccountId == target).ToList();
        }

        return named
            .Select(a => new
            {
                Memory = a,
                Row = Accounts.FirstOrDefault(r => r.Id == a.AccountId),
            })
            .Where(x => x.Row is not null)
            .Select(x => new AlertTrigger(
                AlertKind.MemoryWarning, x.Memory.AccountId, x.Row!.RenderName,
                x.Row.DisplayName, x.Row.CurrentGameName, x.Memory.PrivateBytes, nowUtc))
            .ToList();
    }

    internal void ApplyMemory(MemoryPressureSnapshot snapshot)
    {
        // snapshot.Accounts is guaranteed non-null (even pre-first-sample) by
        // MemoryWatchdog.GetSnapshot()'s seeded field default — no null-guard needed here.
        var projectionWarned = snapshot.HasProjection
            && snapshot.MinutesToCeiling < _memoryWatchdog.ProjectionWarnMinutes;

        foreach (var account in snapshot.Accounts)
        {
            var row = Accounts.FirstOrDefault(r => r.Id == account.AccountId);
            if (row is null) continue;

            var warned = account.ReadOk && (account.OverCap || projectionWarned);

            row.MemoryText = MemoryChipFormatter.Format(account, warned, snapshot.HasProjection, snapshot.MinutesToCeiling);
            row.MemoryWarning = warned;
        }
    }

    /// <summary>
    /// Loads the cached idle-awareness settings (mute + warn-threshold minutes) and pushes the
    /// threshold into <see cref="IActivityMonitor.WarnThreshold"/>. Called once by the composition
    /// root after the VM is built, and again whenever the Preferences dialog saves a change so the
    /// monitor + toast copy pick up the new values without a restart. v1.8.
    /// </summary>
    public async Task InitializeIdleSettingsAsync(IAppSettings settings)
    {
        _idleWarnThresholdMinutes = await settings.GetIdleWarnThresholdMinutesAsync().ConfigureAwait(false);
        _muteIdleAlerts = await settings.GetMuteIdleAlertsAsync().ConfigureAwait(false);
        _activityMonitor.WarnThreshold = TimeSpan.FromMinutes(_idleWarnThresholdMinutes);
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
            $"Remove {summary.RenderName}?\nYou'll need to log in again to add it back.",
            "Remove Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var wasMain = summary.IsMain;
        // Captured BEFORE DetachIdentityProvider — RenderName falls back to the real name once
        // the row's identity subscription is torn down (no provider attached = no masking), so
        // reading it after Detach would silently re-leak the real name in the "Removed ..." banner
        // below even though this line looks identical to every other RenderName call site.
        var removedName = summary.RenderName;
        await _accountStore.RemoveAsync(summary.Id);
        // Unhook the streamer-identity subscription before dropping the row — see the matching
        // comment in LoadAsync for why this row would otherwise leak.
        summary.DetachIdentityProvider();
        Accounts.Remove(summary);
        // If the removed row was the only in-game account, no further presence/process event will
        // ever fire for it — without this, the last-pushed Discord payload would stay stale
        // indefinitely instead of dropping the account or clearing presence entirely.
        DiscordPresence?.Refresh();
        RefreshFpsCapWarning();

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
        StatusBanner = $"Removed {removedName}.";
    }

    // Internal for MainViewModelTests (same pattern as LaunchAccountForPluginAsync) — the
    // RelayCommand wrapper discards the Task, so tests drive the branches directly.
    internal async Task ReauthenticateAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var captured = await _cookieCapture.CaptureAsync();
            switch (captured)
            {
                case CookieCaptureResult.Cancelled:
                    // Pre-fix this returned silently and the user was left guessing why the tag
                    // stayed — the 2FA reauth bug's visible half (followups 2026-06-30 §1).
                    // Copy stays state-neutral: the Re-authenticate button also shows on
                    // SessionLimited rows, where "still expired" would be the wrong diagnosis.
                    StatusBanner = $"Re-authentication cancelled — {summary.RenderName}'s saved session is unchanged.";
                    return;
                case CookieCaptureResult.Failed failed when failed.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase):
                    ShowWebView2NotInstalledModal();
                    return;
                case CookieCaptureResult.Failed failed:
                    StatusBanner = $"Re-authentication didn't complete: {failed.Message}";
                    return;
            }

            var success = (CookieCaptureResult.Success)captured;

            // Identity guard: the capture window is a fresh profile — nothing stops the user
            // logging into a different account. Overwriting this row's cookie with another
            // account's session would silently corrupt the row, so refuse and say why.
            if (summary.RobloxUserId is long knownUserId && knownUserId != success.UserId)
            {
                StatusBanner = $"That login was a different account (@{success.Username}) — {summary.RenderName} is unchanged.";
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

            // Opportunistic RobloxUserId persist for pre-backfill rows (mirrors the other
            // opportunistic call sites) — soft-fail, the reauth itself already succeeded.
            if (summary.RobloxUserId is null)
            {
                try
                {
                    await _accountStore.UpdateRobloxUserIdAsync(summary.Id, success.UserId);
                    summary.RobloxUserId = success.UserId;
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "RobloxUserId persist failed during reauth for {AccountId}.", summary.Id);
                }
            }

            _log.LogInformation("Re-authenticated account {AccountId}", summary.Id);
            summary.SessionExpired = false;
            summary.StatusText = "Re-authenticated.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Flip an account row's join-via-friend preference (route batch launches through a friend
    /// follow instead of a direct join — trust-aware squad launch, v1.9.0) and persist it.
    /// Optimistic: the row flips immediately so the context-menu checkbox reflects the click
    /// without waiting on disk; on persist failure the flip is reverted and the failure surfaces
    /// via <see cref="StatusBanner"/> rather than leaving the UI silently out of sync with disk.
    /// </summary>
    internal async Task ToggleJoinViaFriendAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        var next = !summary.JoinViaFriend;
        summary.JoinViaFriend = next;
        try
        {
            await _accountStore.SetJoinViaFriendAsync(summary.Id, next);
        }
        catch (Exception ex)
        {
            summary.JoinViaFriend = !next; // revert on persist failure
            StatusBanner = $"Couldn't save join-via-friend: {ex.Message}";
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
    /// Also attaches the streamer-identity singleton (v1.10) so the row's
    /// <see cref="AccountSummary.RenderName"/>/<see cref="AccountSummary.AvatarDisplaySource"/>
    /// flip to fake values while the mode is active — no-op when the provider wasn't resolved
    /// (e.g. the VM-level test harness, which doesn't pass one).
    /// </summary>
    private void WireAccountSummary(AccountSummary summary)
    {
        summary.PropertyChanged += OnAccountSummaryPropertyChanged;
        summary.Tags.CollectionChanged += (_, _) => OnAccountTagsChanged(summary);
        if (_streamerIdentity is not null)
        {
            summary.AttachIdentityProvider(_streamerIdentity);
        }
    }

    /// <summary>
    /// The streamer-identity provider flipped active/inactive or reassigned identities — refresh
    /// <see cref="StreamerModeOn"/> so the main-window switch stays in sync whether the flip came
    /// from this window, the tray checkbox, or a plugin. Task 10.
    /// <para>
    /// Also refreshes Discord presence (2026-08-03): flipping streamer mode changes what
    /// <see cref="BuildRosterSnapshot"/> hands <see cref="PresencePayloadBuilder"/> — masked names,
    /// and now the anonymized roster count/party — so a push already sitting in Discord goes stale
    /// the instant the mode flips, not on the next unrelated roster event. Predicted by an earlier
    /// review note ("do this in the same commit that puts identity-derived data on the wire") —
    /// this is that commit. Safe to call even when <see cref="DiscordPresence"/> is null (presence
    /// never configured) via the null-conditional, same as every other roster-changing call site.
    /// </para>
    /// </summary>
    private void OnStreamerIdentityChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StreamerModeOn));
        DiscordPresence?.Refresh();
    }

    /// <summary>"Reroll all identities" button body — reassigns every streamer-mode fake identity at once. Task 10.</summary>
    private Task RerollAllIdentitiesAsync()
        => _streamerIdentity?.RerollAllAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Per-row context-menu "reroll" body. <paramref name="parameter"/> is the row's account id
    /// (a boxed <see cref="Guid"/>, per <see cref="RerollAccountCommand"/>'s CommandParameter
    /// binding) — NOT the <see cref="AccountSummary"/> itself, so a stale/removed row can't be
    /// rerolled through a dangling reference. Task 10.
    /// </summary>
    private Task RerollAccountAsync(object? parameter)
        => _streamerIdentity is not null && parameter is Guid accountId
            ? _streamerIdentity.RerollAsync(Core.StreamerMode.StreamerIdentityProvider.AccountKey(accountId))
            : Task.CompletedTask;

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
        RefreshFpsCapWarning();
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
            StatusBanner = $"{source.RenderName} has an expired session — re-authenticate first.";
            return;
        }
        if (target.RobloxUserId is not long targetUserId || targetUserId <= 0)
        {
            // RobloxUserId is cached lazily (validation pass + cookie capture). If it's never
            // landed, we don't have a userId to route to. Surface the gap rather than fail
            // silently inside the launcher.
            StatusBanner = $"Couldn't follow {target.RenderName} — Roblox userId not yet known. " +
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
        var decision = EvaluateFollow(targetPresence, target.RenderName);
        if (!decision.CanFollow)
        {
            StatusBanner = decision.BlockedMessage!; // non-null whenever CanFollow is false (see FollowDecision.Block)
            return;
        }
        StatusBanner = $"Following {target.RenderName} from {source.RenderName}...";
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
        var window = new SessionHistoryWindow(_sessionHistory, _favorites, _api, _streamerIdentity)
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
        // Fix round 1, Finding 4: no fallback here. _discordConfigStore is DI-supplied
        // unconditionally in production (App.ConfigureServices registers it regardless of whether
        // Discord:ApplicationId is configured — see that registration's remarks), so this is never
        // null at either real call site (this method, and App.OpenPreferencesFromTray). A
        // hand-rolled fallback that recomposed the same discord.dat path here was dead code that
        // could never run in production and left two places knowing where that file lives — a null
        // here means a test constructed this VM directly and is exercising OpenPreferencesCommand
        // without supplying one, which is a fixture bug to fix, not a case to paper over.
        if (PreferencesWindowFactory is null)
        {
            throw new InvalidOperationException(
                "OpenPreferences requires PreferencesWindowFactory. The composition root sets it " +
                "in production; a test exercising OpenPreferencesCommand must supply one.");
        }

        var window = PreferencesWindowFactory();
        window.Owner = Application.Current.MainWindow;
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
            : $"{summary.RenderName} is now your main.";
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
            foreach (var stale in Accounts)
            {
                stale.DetachIdentityProvider();
            }
            Accounts.Clear();
            RefreshFpsCapWarning();
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
        // RenameTarget.OriginalName is shown verbatim in RenameWindow's "ROBLOX NAME — ..." reference
        // line — a visible surface the review's explicit list didn't name, but the same masking rule
        // applies: RenderName (streamer-mode-aware) instead of the raw DisplayName.
        AccountSummary account =>
            new RenameTarget(RenameTargetKind.Account, account.Id, account.RenderName, account.LocalName),
        SavedPrivateServer server =>
            new RenameTarget(RenameTargetKind.PrivateServer, server.Id, server.Name, server.LocalName),
        _ => null,
    };

    private void OnFavoritesDefaultChanged(object? sender, EventArgs e)
    {
        // The store has already mutated + persisted; refresh our cached "current default" so the
        // widget readout flips, and re-sync each account's SelectedGame to keep row pickers in
        // lockstep. DefaultChanged fires on the store's lock thread — off the UI thread when
        // Clear/Set/RemoveAsync was awaited with ConfigureAwait(false) — but ReloadGamesAsync
        // mutates the UI-bound AvailableGames/WidgetGames collections. A cross-thread mutation
        // throws ("CollectionView does not support changes from a thread different from the
        // Dispatcher thread") and leaves the row pickers half-rendered (the dropdown-ghost bug),
        // so marshal onto the UI thread the same way every other store-event handler here does.
        // CheckAccess keeps the direct call for the UI-thread and no-dispatcher (unit-test) cases.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = ReloadGamesAsync();
        }
        else
        {
            dispatcher.Invoke(() => _ = ReloadGamesAsync());
        }
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

    private string _contestedBannerText = string.Empty;

    /// <summary>Runtime banner text — non-empty only when Roblox holds the multi-instance lock
    /// and RoRoRo doesn't. Empty collapses the strip (mirrors StatusBanner/IdleSummaryText).</summary>
    public string ContestedBannerText
    {
        get => _contestedBannerText;
        private set => SetField(ref _contestedBannerText, value);
    }

    public void SetContested(bool contested)
        => ContestedBannerText = contested ? MultiInstanceCopy.ContestedBanner : string.Empty;

    private string _fpsCapWarningText = string.Empty;

    /// <summary>
    /// Non-empty when the accounts on screen do not all share one FPS cap — see
    /// <see cref="MultiInstanceCopy.FpsCapMismatchBanner"/>. Display only; it does not gate
    /// launching. The user chose this trade, so do not make them re-confirm it -- UNLESS the set
    /// of distinct caps changes to something they haven't acknowledged yet; see
    /// <see cref="_dismissedFpsCapSignature"/>.
    /// </summary>
    public string FpsCapWarningText
    {
        get => _fpsCapWarningText;
        private set => SetField(ref _fpsCapWarningText, value);
    }

    /// <summary>
    /// The signature (see <see cref="ComputeFpsCapSignature"/>) of the distinct FPS-cap set that
    /// was in effect the last time the user dismissed <see cref="FpsCapWarningText"/>, or
    /// <c>null</c> if nothing has been dismissed yet. Loaded once in <see cref="LoadAsync"/> (not
    /// the ctor's fire-and-forget pattern -- see the comment there) and updated in
    /// <see cref="DismissFpsCapWarningAsync"/>. This is intentionally a single value, not a set of
    /// every signature ever dismissed: only the MOST RECENT acknowledgement counts, matching the
    /// spec's "one signature" persistence shape.
    /// </summary>
    private string? _dismissedFpsCapSignature;

    /// <summary>
    /// Pure, order-independent, dedup canonicalization of a set of per-account FPS caps into a
    /// stable string signature -- e.g. two accounts capped at 45 and one uncapped always produces
    /// <c>"none,45"</c> regardless of row order or how many rows share a value. <c>null</c> (no
    /// cap set) is represented as the literal <c>"none"</c> token, distinct from any numeric
    /// value, because an uncapped account still contends over the shared settings file with a
    /// capped one -- "unset" is its own contending value, not an absence of one. Extracted as a
    /// static pure function (mirrors <see cref="ResolveLaunchTarget"/> / <see cref="AccountMatchesFilter"/>)
    /// so the canonicalization rule is unit-testable without a live VM.
    /// </summary>
    internal static string ComputeFpsCapSignature(IEnumerable<int?> caps)
    {
        var distinct = caps.Distinct().ToList();
        var parts = new List<string>(distinct.Count);
        if (distinct.Contains(null))
        {
            parts.Add("none");
        }
        parts.AddRange(distinct
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .OrderBy(v => v)
            .Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return string.Join(",", parts);
    }

    /// <summary>
    /// Recompute the mismatch warning. "Unset" counts as its own distinct value: a capped account
    /// and an uncapped one still contend over the same shared file. The banner shows only when
    /// there IS a mismatch AND the current cap-set signature differs from the last one the user
    /// dismissed -- so acknowledging today's mismatch doesn't silence a genuinely different one
    /// that shows up later.
    /// </summary>
    internal void RefreshFpsCapWarning()
    {
        var caps = Accounts.Select(a => a.FpsCap).ToList();
        if (caps.Distinct().Count() <= 1)
        {
            FpsCapWarningText = string.Empty;
            return;
        }

        var signature = ComputeFpsCapSignature(caps);
        FpsCapWarningText = signature == _dismissedFpsCapSignature
            ? string.Empty
            : MultiInstanceCopy.FpsCapMismatchBanner;
    }

    /// <summary>
    /// Dismiss handler for <see cref="DismissFpsCapWarningCommand"/> -- mirrors
    /// <see cref="DismissBloxstrapWarningAsync"/>'s shape. Records the CURRENT cap-set signature
    /// (not a boolean) so a later change to a genuinely different set re-surfaces the banner, and
    /// a later return to this exact set stays quiet.
    /// </summary>
    private async Task DismissFpsCapWarningAsync()
    {
        var signature = ComputeFpsCapSignature(Accounts.Select(a => a.FpsCap));
        _dismissedFpsCapSignature = signature;
        RefreshFpsCapWarning();
        await _settings.SetDismissedFpsCapWarningSignatureAsync(signature);
    }

    public event Action? RequestCloseRobloxForMe;
    public event Action? RequestRetryMutex;

    public ICommand CloseRobloxForMeCommand => _closeRobloxForMeCommand ??=
        new RelayCommand(_ => RequestCloseRobloxForMe?.Invoke());
    private RelayCommand? _closeRobloxForMeCommand;

    public ICommand RetryMutexCommand => _retryMutexCommand ??=
        new RelayCommand(_ => RequestRetryMutex?.Invoke());
    private RelayCommand? _retryMutexCommand;

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
