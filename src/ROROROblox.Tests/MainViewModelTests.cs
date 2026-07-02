using ROROROblox.App.Notifications;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Tray;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Theming;
using ROROROblox.Core.Transport;

namespace ROROROblox.Tests;

/// <summary>
/// First MainViewModel-level test harness in the suite (Task 8, tray-residence gate). Every
/// constructor dependency is a hand-rolled fake implementing only the members MainViewModel's
/// constructor actually touches (event subscription + the fire-and-forget
/// <c>InitializeBloxstrapWarningAsync</c> read) — unused members throw
/// <see cref="NotImplementedException"/> to surface accidental use, mirroring the existing
/// <c>FakeAccountStore</c>/<c>FakeRobloxApi</c> convention in
/// <see cref="AccountUserIdBackfillServiceTests"/>. <see cref="IAccountStore"/> is the one real
/// concrete implementation (DPAPI-backed <c>AccountStore</c> over a throwaway temp file) rather
/// than a fake, since neither test needs seeded accounts and constructing the real store is no
/// more work than faking sixteen members — the temp file is cleaned up by each test's <c>finally</c>.
/// </summary>
public class MainViewModelTests
{
    private static (MainViewModel Vm, IAccountStore AccountStore, IRobloxProcessTracker ProcessTracker, string AccountStorePath) Build()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-test-{Guid.NewGuid():N}.dat");
        var accountStore = new AccountStore(path);
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();

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
            idleAlertPresenter: new IdleAlertPresenter(new FakeTrayService()));

        // MainViewModel never disposes the window decorator (App.xaml.cs's DI container owns
        // that lifetime in production); its ctor starts a real 1.5s reapply Timer that would
        // otherwise leak across every test in this class. Nothing in these tests calls
        // RefreshDecoratorForAccount, so disposing right after construction is safe.
        windowDecorator.Dispose();

        return (vm, accountStore, processTracker, path);
    }

    [Fact]
    public void SetContested_TogglesBannerText()
    {
        var (vm, _, _, path) = Build();
        try
        {
            Assert.Equal("", vm.ContestedBannerText);

            vm.SetContested(true);
            Assert.Equal(
                "Roblox has the multi-instance lock — it's probably running in your system tray.",
                vm.ContestedBannerText);

            vm.SetContested(false);
            Assert.Equal("", vm.ContestedBannerText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CloseRobloxForMeCommand_RaisesRequestEvent()
    {
        var (vm, _, _, path) = Build();
        try
        {
            var raised = false;
            vm.RequestCloseRobloxForMe += () => raised = true;
            vm.CloseRobloxForMeCommand.Execute(null);
            Assert.True(raised);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- fakes ----
    // Only members MainViewModel's constructor touches (event subscriptions + the
    // InitializeBloxstrapWarningAsync fire-and-forget read) are implemented for real; everything
    // else throws NotImplementedException to surface accidental use by a future test.

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
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null) => throw new NotImplementedException();
        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxCompatChecker : IRobloxCompatChecker
    {
        public Task<CompatCheckResult> CheckAsync() => throw new NotImplementedException();
        public Task<(string Name, MutexNameSource Source)> ResolveMutexNameAsync() => throw new NotImplementedException();
    }

    private sealed class FakeAppSettings : IAppSettings
    {
        // Read synchronously (via await) by MainViewModel's ctor fire-and-forget
        // InitializeBloxstrapWarningAsync — must return a benign completed Task, never throw.
        public Task<bool> GetBloxstrapWarningDismissedAsync() => Task.FromResult(true);

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
    }

    private sealed class FakeFavoriteGameStore : IFavoriteGameStore
    {
        // Subscribed unconditionally in the MainViewModel ctor — no-op accessors are enough
        // since neither test raises it.
        public event EventHandler? DefaultChanged { add { } remove { } }

        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => throw new NotImplementedException();
        public Task<FavoriteGame?> GetDefaultAsync() => throw new NotImplementedException();
        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl) => throw new NotImplementedException();
        public Task RemoveAsync(long placeId) => throw new NotImplementedException();
        public Task SetDefaultAsync(long placeId) => throw new NotImplementedException();
        public Task UpdateLocalNameAsync(long placeId, string? localName) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxProcessTracker : IRobloxProcessTracker
    {
        public IReadOnlyDictionary<Guid, TrackedProcess> Attached { get; } = new Dictionary<Guid, TrackedProcess>();

        public event EventHandler<RobloxProcessEventArgs>? ProcessAttached { add { } remove { } }
        public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed { add { } remove { } }
        public event EventHandler<RobloxProcessEventArgs>? ProcessExited { add { } remove { } }

        public Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default) => throw new NotImplementedException();
        public bool AttachExisting(Guid accountId, int pid) => throw new NotImplementedException();
        public bool IsTracking(Guid accountId) => throw new NotImplementedException();
        public bool RequestClose(Guid accountId) => throw new NotImplementedException();
        public bool Kill(Guid accountId) => throw new NotImplementedException();
    }

    private sealed class FakePresenceService : IPresenceService
    {
        public event EventHandler<AccountPresenceEventArgs>? AccountPresenceUpdated { add { } remove { } }
        public event EventHandler<Guid>? AccountSessionExpired { add { } remove { } }
        public event EventHandler<Guid>? AccountSessionLimited { add { } remove { } }

        public void Start() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public Task PollOnceAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task RequestImmediateRefreshAsync(Guid accountId) => throw new NotImplementedException();
    }

    private sealed class FakeDiagnosticsCollector : IDiagnosticsCollector
    {
        public Task<DiagnosticsSnapshot> CollectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakePrivateServerStore : IPrivateServerStore
    {
        public Task<IReadOnlyList<SavedPrivateServer>> ListAsync() => throw new NotImplementedException();
        public Task<SavedPrivateServer?> GetAsync(Guid id) => throw new NotImplementedException();
        public Task<SavedPrivateServer> AddAsync(long placeId, string code, PrivateServerCodeKind codeKind, string name, string placeName, string thumbnailUrl) => throw new NotImplementedException();
        public Task RemoveAsync(Guid id) => throw new NotImplementedException();
        public Task TouchLastLaunchedAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateLocalNameAsync(Guid serverId, string? localName) => throw new NotImplementedException();
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
        // Called synchronously by the ctor's InitializeBloxstrapWarningAsync fire-and-forget —
        // must not throw.
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
        // Real auto-property — MainViewModel both reads and writes WarnThreshold from its
        // (non-ctor) idle-settings init path.
        public TimeSpan WarnThreshold { get; set; }

        public event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed { add { } remove { } }

        public void OnAccountLaunched(Guid accountId) => throw new NotImplementedException();
        public void OnAccountExited(Guid accountId) => throw new NotImplementedException();
        public void Start() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public void Sample() => throw new NotImplementedException();
        public IReadOnlyList<AccountActivity> GetSnapshot() => throw new NotImplementedException();
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
    }
}
