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
    private static (MainViewModel Vm, IAccountStore AccountStore, IRobloxProcessTracker ProcessTracker, string AccountStorePath) Build(
        IRobloxLauncher? launcher = null,
        ICookieCapture? cookieCapture = null,
        Func<IAccountStore, IAccountStore>? wrapStore = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-test-{Guid.NewGuid():N}.dat");
        var accountStore = new AccountStore(path);
        var vmStore = wrapStore?.Invoke(accountStore) ?? (IAccountStore)accountStore;
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();

        var vm = new MainViewModel(
            cookieCapture: cookieCapture ?? new FakeCookieCapture(),
            api: new FakeRobloxApi(),
            accountStore: vmStore,
            launcher: launcher ?? new FakeRobloxLauncher(),
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

    [Fact]
    public async Task Launch_GeneratesAndPersistsStableBrowserTrackerId_ThenReuses()
    {
        // v1.8.1 trust hygiene: first launch of an account with no persisted btid generates a
        // 13-digit value, persists it, and passes it to the launcher; the second launch reuses
        // the exact same value instead of rolling a new one.
        var launcher = new CapturingRobloxLauncher();
        var (vm, store, _, path) = Build(launcher);
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);

            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.FollowFriend(1));
            var first = Assert.Single(launcher.BrowserTrackerIds);
            Assert.NotNull(first);
            Assert.InRange(first!.Value, 1_000_000_000_000, 9_999_999_999_999);
            Assert.Equal(first, row.BrowserTrackerId);
            var persisted = (await store.ListAsync()).Single(a => a.Id == row.Id);
            Assert.Equal(first, persisted.BrowserTrackerId);

            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.FollowFriend(1));
            Assert.Equal(2, launcher.BrowserTrackerIds.Count);
            Assert.Equal(first, launcher.BrowserTrackerIds[1]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplySessionExpired_CookieReauthedSincePoll_DropsTheFlip()
    {
        // The re-flag race (2026-07-03): a presence poll started before a reauth, its stale 401
        // arrives after the reauth cleared the tag. The poll captured cookie generation 0; the
        // reauth bumped it to 1 (UpdateCookieAsync). ApplySessionExpired must drop the flip so
        // the just-refreshed row does NOT snap back to "Session expired."
        var (vm, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added) { SessionExpired = false };
            vm.Accounts.Add(row);
            await store.UpdateCookieAsync(added.Id, "fresh-cookie-from-reauth"); // bumps generation 0 -> 1

            vm.ApplySessionExpired(added.Id, polledCookieGeneration: 0);

            Assert.False(row.SessionExpired); // stale 401 dropped
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplySessionExpired_GenerationUnchanged_FlipsToExpired()
    {
        // Genuine expiry (no reauth since the poll started): generations match, so the flip lands.
        var (vm, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added) { SessionExpired = false };
            vm.Accounts.Add(row);

            vm.ApplySessionExpired(added.Id, polledCookieGeneration: store.GetCookieGeneration(added.Id));

            Assert.True(row.SessionExpired);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// Seed one account into the store (optionally with a persisted RobloxUserId) and hand back
    /// a detached expired-tagged row for it. Detached because <c>LoadAsync</c> drags in
    /// <c>ReloadGamesAsync</c> (throwing fakes); <c>ReauthenticateAsync</c> only touches the row
    /// it's given plus the store, so a detached row exercises the real branch under test.
    /// </summary>
    private static async Task<AccountSummary> SeedExpiredAccountAsync(
        IAccountStore store, long? robloxUserId, string cookie = "original-cookie")
    {
        var added = await store.AddAsync("TestAlt", "", cookie);
        if (robloxUserId is long id)
        {
            await store.UpdateRobloxUserIdAsync(added.Id, id);
        }
        return new AccountSummary(added with { RobloxUserId = robloxUserId }) { SessionExpired = true };
    }

    [Fact]
    public async Task ReauthenticateAsync_CancelledCapture_KeepsTagAndSurfacesBanner()
    {
        var (vm, store, _, path) = Build(cookieCapture: new StubCookieCapture(new CookieCaptureResult.Cancelled()));
        try
        {
            var row = await SeedExpiredAccountAsync(store, 111);

            await vm.ReauthenticateAsync(row);

            Assert.True(row.SessionExpired);
            Assert.Equal("Re-authentication cancelled — TestAlt's saved session is unchanged.", vm.StatusBanner);
            Assert.Equal("original-cookie", await store.RetrieveCookieAsync(row.Id));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReauthenticateAsync_FailedCapture_KeepsTagAndSurfacesBanner()
    {
        var (vm, store, _, path) = Build(cookieCapture: new StubCookieCapture(
            new CookieCaptureResult.Failed("Login was unsuccessful.")));
        try
        {
            var row = await SeedExpiredAccountAsync(store, 111);

            await vm.ReauthenticateAsync(row);

            Assert.True(row.SessionExpired);
            Assert.Equal("Re-authentication didn't complete: Login was unsuccessful.", vm.StatusBanner);
            Assert.Equal("original-cookie", await store.RetrieveCookieAsync(row.Id));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReauthenticateAsync_DifferentAccountCookie_RefusesOverwrite()
    {
        var (vm, store, _, path) = Build(cookieCapture: new StubCookieCapture(
            new CookieCaptureResult.Success("intruder-cookie", 999, "SomeOtherUser")));
        try
        {
            var row = await SeedExpiredAccountAsync(store, 111);

            await vm.ReauthenticateAsync(row);

            Assert.True(row.SessionExpired);
            Assert.Equal(
                "That login was a different account (@SomeOtherUser) — TestAlt is unchanged.",
                vm.StatusBanner);
            Assert.Equal("original-cookie", await store.RetrieveCookieAsync(row.Id));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReauthenticateAsync_MatchingAccount_ClearsTagAndUpdatesCookie()
    {
        var (vm, store, _, path) = Build(cookieCapture: new StubCookieCapture(
            new CookieCaptureResult.Success("fresh-cookie", 111, "TestAlt")));
        try
        {
            var row = await SeedExpiredAccountAsync(store, 111);

            await vm.ReauthenticateAsync(row);

            Assert.False(row.SessionExpired);
            Assert.Equal("Re-authenticated.", row.StatusText);
            Assert.Equal("fresh-cookie", await store.RetrieveCookieAsync(row.Id));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReauthenticateAsync_BackfillPersistFails_ReauthStillSucceeds()
    {
        // The RobloxUserId backfill is opportunistic — a failed persist must not fail the
        // reauth itself (tag clears, cookie updates, row userId stays null for the next try).
        var (vm, store, _, path) = Build(
            cookieCapture: new StubCookieCapture(new CookieCaptureResult.Success("fresh-cookie", 222, "TestAlt")),
            wrapStore: real => new UserIdPersistThrowingStore(real));
        try
        {
            var row = await SeedExpiredAccountAsync(store, robloxUserId: null);

            await vm.ReauthenticateAsync(row);

            Assert.False(row.SessionExpired);
            Assert.Equal("fresh-cookie", await store.RetrieveCookieAsync(row.Id));
            Assert.Null(row.RobloxUserId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ReauthenticateAsync_UnknownRowUserId_AcceptsAndBackfills()
    {
        var (vm, store, _, path) = Build(cookieCapture: new StubCookieCapture(
            new CookieCaptureResult.Success("fresh-cookie", 222, "TestAlt")));
        try
        {
            var row = await SeedExpiredAccountAsync(store, robloxUserId: null);

            await vm.ReauthenticateAsync(row);

            Assert.False(row.SessionExpired);
            Assert.Equal(222, row.RobloxUserId);
            Assert.Equal("fresh-cookie", await store.RetrieveCookieAsync(row.Id));
            var persisted = (await store.ListAsync()).Single(a => a.Id == row.Id);
            Assert.Equal(222, persisted.RobloxUserId);
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

    /// <summary>Capture double returning a canned result — drives the ReauthenticateAsync branches.</summary>
    private sealed class StubCookieCapture(CookieCaptureResult result) : ICookieCapture
    {
        public Task<CookieCaptureResult> CaptureAsync() => Task.FromResult(result);
    }

    /// <summary>
    /// Delegates every member to the real store except <see cref="UpdateRobloxUserIdAsync"/>,
    /// which throws — pins ReauthenticateAsync's soft-fail contract for the opportunistic
    /// backfill persist.
    /// </summary>
    private sealed class UserIdPersistThrowingStore(IAccountStore inner) : IAccountStore
    {
        public Task UpdateRobloxUserIdAsync(Guid accountId, long userId)
            => throw new IOException("simulated persist failure");

        public Task UpdateBrowserTrackerIdAsync(Guid accountId, long browserTrackerId) => inner.UpdateBrowserTrackerIdAsync(accountId, browserTrackerId);
        public int GetCookieGeneration(Guid id) => inner.GetCookieGeneration(id);
        public Task<IReadOnlyList<Account>> ListAsync() => inner.ListAsync();
        public Task<Account> AddAsync(string displayName, string avatarUrl, string cookie) => inner.AddAsync(displayName, avatarUrl, cookie);
        public Task RemoveAsync(Guid id) => inner.RemoveAsync(id);
        public Task<string> RetrieveCookieAsync(Guid id) => inner.RetrieveCookieAsync(id);
        public Task UpdateCookieAsync(Guid id, string newCookie) => inner.UpdateCookieAsync(id, newCookie);
        public Task TouchLastLaunchedAsync(Guid id) => inner.TouchLastLaunchedAsync(id);
        public Task SetMainAsync(Guid id) => inner.SetMainAsync(id);
        public Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder) => inner.UpdateSortOrderAsync(idsInOrder);
        public Task SetSelectedAsync(Guid id, bool isSelected) => inner.SetSelectedAsync(id, isSelected);
        public Task SetCaptionColorAsync(Guid id, string? hex) => inner.SetCaptionColorAsync(id, hex);
        public Task SetFpsCapAsync(Guid id, int? fps) => inner.SetFpsCapAsync(id, fps);
        public Task UpdateLocalNameAsync(Guid accountId, string? localName) => inner.UpdateLocalNameAsync(accountId, localName);
        public Task SetTagsAsync(Guid id, IReadOnlyList<string> tags) => inner.SetTagsAsync(id, tags);
        public Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids) => inner.ExportAccountsAsync(ids);
        public Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<AccountExportRecord> records) => inner.ImportMergeAsync(records);
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

    /// <summary>
    /// Records the btid passed to each launch and returns a generic Failed — the failed branch
    /// only sets row StatusText, so the test stays clear of the Started path's tracker /
    /// session-history dependencies (throwing fakes).
    /// </summary>
    private sealed class CapturingRobloxLauncher : IRobloxLauncher
    {
        public List<long?> BrowserTrackerIds { get; } = [];

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            BrowserTrackerIds.Add(browserTrackerId);
            return Task.FromResult<LaunchResult>(new LaunchResult.Failed("test launch refused"));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
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
        public event EventHandler<AccountSessionExpiredEventArgs>? AccountSessionExpired { add { } remove { } }
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
