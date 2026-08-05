using ROROROblox.App.Notifications;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Tray;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;
using ROROROblox.Core.StreamerMode;
using ROROROblox.Core.Theming;
using ROROROblox.Core.Transport;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Trimmed copy of <c>MainViewModelTests.Build()</c> (see that file's header for why the fakes are
/// hand-rolled, throw-on-unused-member doubles rather than a mocking library) — purpose-built for
/// Discord roster-projection tests, which need one wired, in-game <see cref="AccountSummary"/> row
/// whose streamer identity is attached so <c>RenderName</c> genuinely differs from
/// <c>DisplayName</c>. Deliberately does NOT share <c>MainViewModelTests</c>'s private nested fakes
/// — those are private to that class — so every fake here is its own copy.
/// </summary>
internal static class DiscordTestHarness
{
    /// <summary>
    /// Builds a <see cref="MainViewModel"/> with one account loaded, wired to a streamer-identity
    /// provider that is always active and always renders <paramref name="maskedName"/>, then drives
    /// it in-game via <see cref="MainViewModel.ApplyPresence"/> so the returned row has
    /// <c>InGame == true</c>, a game name, and an in-game-since timestamp. Callers mutate the
    /// returned row further (e.g. <c>CurrentServer</c>, <c>PresenceState</c>) to shape individual
    /// test scenarios.
    /// </summary>
    public static (MainViewModel Vm, AccountSummary Row) VmWithOneInGameAccount(string realName, string maskedName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-discord-test-{Guid.NewGuid():N}.dat");
        var accountStore = new AccountStore(path);
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();
        var trayService = new FakeTrayService();
        var streamerIdentity = new FakeStreamerIdentityProvider(maskedName);

        var vm = new MainViewModel(
            cookieCapture: new FakeCookieCapture(),
            api: new FakeRobloxApi(),
            accountStore: accountStore,
            launcher: new FakeRobloxLauncher(),
            compatChecker: new FakeRobloxCompatChecker(),
            settings: new FakeAppSettings(),
            favorites: new FakeFavoriteGameStore(),
            processTracker: processTracker,
            presenceService: new FakePresenceService(),
            diagnostics: new FakeDiagnosticsCollector(),
            privateServerStore: new FakePrivateServerStore(),
            sessionHistory: new FakeSessionHistoryStore(),
            startupRegistration: new FakeStartupRegistration(),
            themeStore: new FakeThemeStore(),
            themeService: new ThemeService(new FakeThemeStore(), new FakeAppSettings()),
            windowDecorator: windowDecorator,
            bloxstrapDetector: new FakeBloxstrapDetector(),
            updateProbe: new FakeRobloxUpdateProbe(),
            accountTransport: new FakeAccountTransport(),
            activityMonitor: new FakeActivityMonitor(),
            memoryWatchdog: new FakeMemoryWatchdog(),
            instanceStopper: new FakeRobloxInstanceStopper(),
            tray: trayService,
            idleAlertPresenter: new IdleAlertPresenter(trayService),
            streamerIdentity: streamerIdentity);

        // Same leak-avoidance reasoning as MainViewModelTests.Build(): nothing here calls
        // RefreshDecoratorForAccount, so the ctor's 1.5s reapply Timer would otherwise outlive the
        // test.
        windowDecorator.Dispose();

        var added = accountStore.AddAsync(realName, "", "cookie")
            .GetAwaiter().GetResult();

        // LoadAsync wires every loaded row through WireAccountSummary, which attaches the
        // streamer-identity provider passed above — that's what makes RenderName diverge from
        // DisplayName below.
        vm.LoadAsync().GetAwaiter().GetResult();
        var row = vm.Accounts.Single(a => a.Id == added.Id);

        // Drive the row in-game through the same seam production code uses (ApplyPresence), rather
        // than poking AccountSummary fields directly, so this harness exercises the real path
        // BuildRosterSnapshot's callers rely on.
        vm.ApplyPresence(new AccountPresenceEventArgs(
            row.Id,
            UserPresenceType.InGame,
            placeId: 8737899170,
            gameName: "Pet Simulator 99!",
            occurredAtUtc: DateTimeOffset.UtcNow,
            server: null));

        return (vm, row);
    }

    /// <summary>
    /// Task 8: one idle account (never launched, session not expired, no tracked process) —
    /// exactly the row <see cref="MainViewModel.HandleDiscordJoinAsync"/>'s
    /// <c>Accounts.FirstOrDefault</c> picks. Uses <see cref="RecordingLauncher"/> (not the
    /// throw-on-call <see cref="FakeRobloxLauncher"/> used by <see cref="VmWithOneInGameAccount"/>)
    /// so tests can assert on what actually reached the launcher, including "nothing" when the
    /// user declines the private-server warning.
    /// </summary>
    public static (MainViewModel Vm, RecordingLauncher Launcher) VmWithOneIdleAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-discord-test-{Guid.NewGuid():N}.dat");
        var accountStore = new AccountStore(path);
        var launcher = new RecordingLauncher();
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();
        var trayService = new FakeTrayService();

        var vm = BuildVm(accountStore, launcher, processTracker, windowDecorator, trayService);

        windowDecorator.Dispose();

        accountStore.AddAsync("IdleAccount", "", "cookie").GetAwaiter().GetResult();
        vm.LoadAsync().GetAwaiter().GetResult();

        return (vm, launcher);
    }

    /// <summary>
    /// Like <see cref="VmWithOneInGameAccount"/>, but hands back the streamer-identity provider so
    /// a test can flip streamer mode the way the tray and plugins do — through the provider, not
    /// through the view model's setter.
    /// <para>
    /// Returned rather than stashed in a static: xUnit runs test classes in parallel, so a static
    /// "last built provider" is overwritten by whichever collection happens to build a view model
    /// next, and the test flips someone else's provider. That is a self-inflicted flake, and this
    /// harness had one for exactly one commit.
    /// </para>
    /// </summary>
    public static (MainViewModel Vm, AccountSummary Row, IStreamerIdentityProvider Streamer) VmWithStreamerProvider(
        string realName, string maskedName)
    {
        var (vm, row) = VmWithOneInGameAccount(realName, maskedName);
        return (vm, row, vm.StreamerIdentityForTests!);
    }

    /// <summary>
    /// One account plus a real <see cref="DiscordConfigStore"/> over a temp file, wired into the
    /// view model — for the per-account mute, where the point of the test is that the preference
    /// actually round-trips through DPAPI storage rather than living in memory.
    /// </summary>
    public static (MainViewModel Vm, AccountSummary Row, DiscordConfigStore ConfigStore) VmWithConfigStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rororo-discord-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var accountStore = new AccountStore(Path.Combine(dir, "accounts.dat"));
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();
        var trayService = new FakeTrayService();

        var vm = BuildVm(accountStore, new FakeRobloxLauncher(), processTracker, windowDecorator, trayService);

        windowDecorator.Dispose();

        var configStore = new DiscordConfigStore(Path.Combine(dir, "discord.dat"));
        vm.DiscordConfigStoreOverride = configStore;

        accountStore.AddAsync("MutableAccount", "", "cookie").GetAwaiter().GetResult();
        vm.LoadAsync().GetAwaiter().GetResult();

        return (vm, vm.Accounts.Single(), configStore);
    }

    /// <summary>
    /// Task 8: an empty roster — <see cref="MainViewModel.HandleDiscordJoinAsync"/> should treat
    /// "nothing to launch with" as an empty state (return false, set a banner) rather than throw.
    /// </summary>
    public static (MainViewModel Vm, RecordingLauncher Launcher) VmWithNoAccounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-discord-test-{Guid.NewGuid():N}.dat");
        var accountStore = new AccountStore(path);
        var launcher = new RecordingLauncher();
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();
        var trayService = new FakeTrayService();

        var vm = BuildVm(accountStore, launcher, processTracker, windowDecorator, trayService);

        windowDecorator.Dispose();

        vm.LoadAsync().GetAwaiter().GetResult();

        return (vm, launcher);
    }

    /// <summary>Shared ctor wiring for the Task 8 factories above — same fake set as <see cref="VmWithOneInGameAccount"/>, minus the streamer-identity provider (neither Task 8 test needs a masked name).</summary>
    private static MainViewModel BuildVm(
        AccountStore accountStore,
        IRobloxLauncher launcher,
        FakeRobloxProcessTracker processTracker,
        RobloxWindowDecorator windowDecorator,
        FakeTrayService trayService)
        => new(
            cookieCapture: new FakeCookieCapture(),
            api: new FakeRobloxApi(),
            accountStore: accountStore,
            launcher: launcher,
            compatChecker: new FakeRobloxCompatChecker(),
            settings: new FakeAppSettings(),
            favorites: new FakeFavoriteGameStore(),
            processTracker: processTracker,
            presenceService: new FakePresenceService(),
            diagnostics: new FakeDiagnosticsCollector(),
            privateServerStore: new FakePrivateServerStore(),
            sessionHistory: new FakeSessionHistoryStore(),
            startupRegistration: new FakeStartupRegistration(),
            themeStore: new FakeThemeStore(),
            themeService: new ThemeService(new FakeThemeStore(), new FakeAppSettings()),
            windowDecorator: windowDecorator,
            bloxstrapDetector: new FakeBloxstrapDetector(),
            updateProbe: new FakeRobloxUpdateProbe(),
            accountTransport: new FakeAccountTransport(),
            activityMonitor: new FakeActivityMonitor(),
            memoryWatchdog: new FakeMemoryWatchdog(),
            instanceStopper: new FakeRobloxInstanceStopper(),
            tray: trayService,
            idleAlertPresenter: new IdleAlertPresenter(trayService),
            streamerIdentity: new FakeStreamerIdentityProvider(string.Empty));

    /// <summary>
    /// Always succeeds with an incrementing pid and records the exact <see cref="LaunchTarget"/>
    /// instance passed for each launch — Task 8's dispatch tests need to assert on what reached
    /// the launcher (or that nothing did, when the user declines the private-server warning).
    /// Mirrors <c>MainViewModelTests.RecordingSuccessLauncher</c>, kept as its own copy per this
    /// file's header note on not sharing private fakes across test files.
    /// </summary>
    internal sealed class RecordingLauncher : IRobloxLauncher
    {
        private int _nextPid = 6000;
        public List<LaunchTarget> Launches { get; } = [];

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Launches.Add(target);
            return Task.FromResult<LaunchResult>(new LaunchResult.Started(_nextPid++, DateTimeOffset.UtcNow));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Starts active (matches this class's original hardcoded-<see langword="true"/> behavior, so
    /// every existing caller that never touches active-state keeps seeing the masked name
    /// immediately) but is genuinely stateful from here: <see cref="SetActiveAsync"/> flips
    /// <see cref="IsActive"/> and raises <see cref="Changed"/> like the real
    /// <c>StreamerIdentityProvider</c> does, and <see cref="ForAccount"/>/<see cref="ForFriend"/>
    /// consult that live state instead of always masking. Needed once a test has to prove
    /// something reacts to a streamer-mode TOGGLE (2026-08-03) — the prior always-on, no-op-set
    /// fake could only ever represent one frozen state.
    /// </summary>
    private sealed class FakeStreamerIdentityProvider(string maskedName) : IStreamerIdentityProvider
    {
        public bool IsActive { get; private set; } = true;
        public event EventHandler? Changed;

        public Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities)
            => Task.CompletedTask;

        public Task SetActiveAsync(bool active)
        {
            if (IsActive == active) return Task.CompletedTask;
            IsActive = active;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl)
            => IsActive ? new(maskedName, realAvatarUrl) : new(realName, realAvatarUrl);
        public DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl)
            => IsActive ? new(maskedName, realAvatarUrl) : new(realName, realAvatarUrl);
        public Task RerollAsync(string identityKey) => Task.CompletedTask;
        public Task RerollAllAsync() => Task.CompletedTask;
    }

    private sealed class FakeCookieCapture : ICookieCapture
    {
        public Task<CookieCaptureResult> CaptureAsync() => throw new NotImplementedException();
    }

    private sealed class FakeRobloxApi : IRobloxApi
    {
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotImplementedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxLauncher : IRobloxLauncher
    {
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null) => throw new NotImplementedException();
        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxCompatChecker : IRobloxCompatChecker
    {
        public Task<CompatCheckResult> CheckAsync() => throw new NotImplementedException();
        public Task<(string Name, MutexNameSource Source)> ResolveMutexNameAsync() => throw new NotImplementedException();
    }

    private sealed class FakeAppSettings : IAppSettings
    {
        public Task<bool?> GetEdgeRemediationAnswerAsync(string themeId) => Task.FromResult<bool?>(null);
        public Task SetEdgeRemediationAnswerAsync(string themeId, bool accepted) => Task.CompletedTask;
        public Task<bool> GetBloxstrapWarningDismissedAsync() => Task.FromResult(true);
        public string? DismissedFpsCapWarningSignature { get; set; }
        public Task<string?> GetDismissedFpsCapWarningSignatureAsync() => Task.FromResult(DismissedFpsCapWarningSignature);
        public Task SetDismissedFpsCapWarningSignatureAsync(string? signature)
        {
            DismissedFpsCapWarningSignature = signature;
            return Task.CompletedTask;
        }

        public Task<string?> GetDefaultPlaceUrlAsync() => throw new NotImplementedException();
        public Task SetDefaultPlaceUrlAsync(string url) => throw new NotImplementedException();
        public Task<bool> GetLaunchMainOnStartupAsync() => throw new NotImplementedException();
        public Task SetLaunchMainOnStartupAsync(bool enabled) => throw new NotImplementedException();
        public Task<string?> GetActiveThemeIdAsync() => throw new NotImplementedException();
        public Task SetActiveThemeIdAsync(string themeId) => throw new NotImplementedException();
        public Task SetBloxstrapWarningDismissedAsync(bool value) => throw new NotImplementedException();
        public Task<bool> GetMuteIdleAlertsAsync() => throw new NotImplementedException();
        public Task SetMuteIdleAlertsAsync(bool muted) => throw new NotImplementedException();
        public Task<int> GetIdleWarnThresholdMinutesAsync() => throw new NotImplementedException();
        public Task SetIdleWarnThresholdMinutesAsync(int minutes) => throw new NotImplementedException();
        public Task<bool> GetCarefulSquadLaunchAsync() => Task.FromResult(false);
        public Task<bool> GetAlwaysShowRecycleAsync() => Task.FromResult(false);
        public Task SetAlwaysShowRecycleAsync(bool always) => throw new NotImplementedException();
        public Task SetCarefulSquadLaunchAsync(bool careful) => throw new NotImplementedException();
        public Task<bool> GetStreamerModeAsync() => throw new NotImplementedException();
        public Task SetStreamerModeAsync(bool enabled) => throw new NotImplementedException();
        public Task<bool> GetMemoryWatchdogEnabledAsync() => throw new NotImplementedException();
        public Task SetMemoryWatchdogEnabledAsync(bool enabled) => throw new NotImplementedException();
        public Task<int?> GetMemoryReserveMbAsync() => throw new NotImplementedException();
        public Task SetMemoryReserveMbAsync(int? reserveMb) => throw new NotImplementedException();
        public Task<int?> GetMemoryCapMbAsync() => throw new NotImplementedException();
        public Task SetMemoryCapMbAsync(int? capMb) => throw new NotImplementedException();
        public Task<int> GetProjectionWarnMinutesAsync() => throw new NotImplementedException();
        public Task SetProjectionWarnMinutesAsync(int minutes) => throw new NotImplementedException();
    }

    private sealed class FakeFavoriteGameStore : IFavoriteGameStore
    {
        public event EventHandler? DefaultChanged { add { } remove { } }

        // LoadAsync unconditionally awaits ReloadGamesAsync -> _favorites.ListAsync(), unlike
        // MainViewModelTests' fixtures (which never call LoadAsync at all, wiring rows straight
        // into vm.Accounts instead). This harness DOES call LoadAsync — it's the only way to reach
        // the private WireAccountSummary that attaches the streamer-identity provider — so this
        // must return a real (empty) result rather than throw.
        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => Task.FromResult<IReadOnlyList<FavoriteGame>>([]);
        public Task<FavoriteGame?> GetDefaultAsync() => throw new NotImplementedException();
        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl) => throw new NotImplementedException();
        public Task RemoveAsync(long placeId) => throw new NotImplementedException();
        public Task SetDefaultAsync(long placeId) => throw new NotImplementedException();
        public Task ClearDefaultAsync() => Task.CompletedTask;
        public Task UpdateLocalNameAsync(long placeId, string? localName) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxProcessTracker : IRobloxProcessTracker
    {
        public IReadOnlyDictionary<Guid, TrackedProcess> Attached { get; } = new Dictionary<Guid, TrackedProcess>();

        public event EventHandler<RobloxProcessEventArgs>? ProcessAttached { add { } remove { } }
        public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed { add { } remove { } }
        public event EventHandler<RobloxProcessEventArgs>? ProcessExited { add { } remove { } }

        public Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default) => Task.CompletedTask;
        public bool AttachExisting(Guid accountId, int pid) => throw new NotImplementedException();
        public bool IsTracking(Guid accountId) => throw new NotImplementedException();
        public bool RequestClose(Guid accountId) => throw new NotImplementedException();
        public bool Kill(Guid accountId) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxInstanceStopper : IRobloxInstanceStopper
    {
        public int StopAll() => 0;
        public bool StopAccount(Guid accountId) => true;
        public int StopWindowless() => 0;
    }

    private sealed class FakePresenceService : IPresenceService
    {
        public event EventHandler<AccountPresenceEventArgs>? AccountPresenceUpdated { add { } remove { } }
        public event EventHandler<AccountSessionExpiredEventArgs>? AccountSessionExpired { add { } remove { } }
        public event EventHandler<Guid>? AccountSessionLimited { add { } remove { } }

        // LoadAsync calls Start() unconditionally once Accounts is populated (real behavior, not
        // a code path MainViewModelTests exercises since it never calls LoadAsync). No-op here —
        // this harness drives presence itself via ApplyPresence, never through a real poll loop.
        public void Start() { }
        public void Stop() => throw new NotImplementedException();
        public Task PollOnceAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task RequestImmediateRefreshAsync(Guid accountId) => Task.CompletedTask;
    }

    private sealed class FakeDiagnosticsCollector : IDiagnosticsCollector
    {
        public Task<DiagnosticsSnapshot> CollectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePrivateServerStore : IPrivateServerStore
    {
        public event EventHandler? DefaultChanged;

        public Task<IReadOnlyList<SavedPrivateServer>> ListAsync() => Task.FromResult<IReadOnlyList<SavedPrivateServer>>([]);
        public Task<SavedPrivateServer?> GetAsync(Guid id) => throw new NotImplementedException();
        public Task<SavedPrivateServer> AddAsync(long placeId, string code, PrivateServerCodeKind codeKind, string name, string placeName, string thumbnailUrl) => throw new NotImplementedException();
        public Task RemoveAsync(Guid id) => throw new NotImplementedException();
        public Task TouchLastLaunchedAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateLocalNameAsync(Guid serverId, string? localName) => throw new NotImplementedException();
        public Task SetDefaultAsync(Guid id) => throw new NotImplementedException();
        public Task ClearDefaultAsync() => throw new NotImplementedException();
    }

    private sealed class FakeSessionHistoryStore : ISessionHistoryStore
    {
        public Task<IReadOnlyList<LaunchSession>> ListAsync() => throw new NotImplementedException();
        public Task AddAsync(LaunchSession session) => throw new NotImplementedException();
        public Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null) => throw new NotImplementedException();
        public Task ClearAsync() => throw new NotImplementedException();
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled() => throw new NotImplementedException();
        public void Enable() => throw new NotImplementedException();
        public void Disable() => throw new NotImplementedException();
    }

    private sealed class FakeThemeStore : IThemeStore
    {
        public string UserThemesFolder => throw new NotImplementedException();
        public Task<IReadOnlyList<Theme>> ListAsync() => throw new NotImplementedException();
        public Task<Theme?> GetByIdAsync(string id) => throw new NotImplementedException();
        public Task<Theme> SaveUserThemeAsync(string rawJson) => throw new NotImplementedException();
    }

    private sealed class FakeBloxstrapDetector : IBloxstrapDetector
    {
        public bool IsBloxstrapHandler() => false;
        public bool IsStrapHandlingLaunches() => throw new NotImplementedException();
    }

    private sealed class FakeRobloxUpdateProbe : IRobloxUpdateProbe
    {
        public bool IsInstallerRunning() => throw new NotImplementedException();
        public Task<bool> IsUpdatePendingAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeAccountTransport : IAccountTransport
    {
        public byte[] Export(IReadOnlyList<AccountExportRecord> records, string passphrase) => throw new NotImplementedException();
        public IReadOnlyList<AccountExportRecord> Import(byte[] bundle, string passphrase) => throw new NotImplementedException();
    }

    private sealed class FakeActivityMonitor : IActivityMonitor
    {
        public TimeSpan WarnThreshold { get; set; }

        public event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed { add { } remove { } }

        public void OnAccountLaunched(Guid accountId) => throw new NotImplementedException();
        public void OnAccountExited(Guid accountId) => throw new NotImplementedException();
        public void Start() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public void Sample() => throw new NotImplementedException();
        public void MarkActive(Guid accountId, DateTimeOffset nowUtc) => throw new NotImplementedException();
        public IReadOnlyList<AccountActivity> GetSnapshot() => throw new NotImplementedException();
    }

    private sealed class FakeMemoryWatchdog : IMemoryWatchdog
    {
        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }

        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed { add { } remove { } }

        public void OnAccountLaunched(Guid accountId, int pid) => throw new NotImplementedException();
        public void OnAccountExited(Guid accountId, int pid) => throw new NotImplementedException();
        public void ResetBaseline(Guid accountId, int pid) => throw new NotImplementedException();
        public void Start() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public void Sample() => throw new NotImplementedException();
        public MemoryPressureSnapshot GetSnapshot() => throw new NotImplementedException();
    }

    private sealed class FakeTrayService : ITrayService
    {
        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void ShowToast(string title, string message) { }
        public void Dispose() { }

        public event EventHandler? RequestOpenMainWindow { add { } remove { } }
        public event EventHandler? RequestToggleMutex { add { } remove { } }
        public event EventHandler? RequestStopAllInstances { add { } remove { } }
        public event EventHandler? RequestQuit { add { } remove { } }
        public event EventHandler? RequestOpenDiagnostics { add { } remove { } }
        public event EventHandler? RequestOpenLogs { add { } remove { } }
        public event EventHandler? RequestOpenPreferences { add { } remove { } }
        public event EventHandler? RequestOpenHistory { add { } remove { } }
        public event EventHandler? RequestOpenPlugins { add { } remove { } }
        public event EventHandler? RequestActivateMain { add { } remove { } }
        public void SetMemoryWarning(bool active) { }
        public void ShowMemoryWarning(string title, string message, Guid accountId) { }
        public event EventHandler<Guid>? RequestFocusAccount { add { } remove { } }
    }
}
