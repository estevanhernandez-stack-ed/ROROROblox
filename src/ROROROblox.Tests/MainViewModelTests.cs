using ROROROblox.App.Friends;
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
    internal static (MainViewModel Vm, IAccountStore AccountStore, IRobloxProcessTracker ProcessTracker, string AccountStorePath) Build(
        IRobloxLauncher? launcher = null,
        ICookieCapture? cookieCapture = null,
        Func<IAccountStore, IAccountStore>? wrapStore = null,
        IRobloxApi? api = null,
        IFavoriteGameStore? favorites = null,
        IRobloxInstanceStopper? instanceStopper = null,
        IMemoryWatchdog? memoryWatchdog = null,
        ITrayService? tray = null,
        IRobloxRunningProbe? runningProbe = null,
        IShellOpener? shellOpener = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-mvm-test-{Guid.NewGuid():N}.dat");
        var accountStore = new AccountStore(path);
        var vmStore = wrapStore?.Invoke(accountStore) ?? (IAccountStore)accountStore;
        var processTracker = new FakeRobloxProcessTracker();
        var windowDecorator = new RobloxWindowDecorator();
        var trayService = tray ?? new FakeTrayService();

        var vm = new MainViewModel(
            cookieCapture: cookieCapture ?? new FakeCookieCapture(),
            api: api ?? new FakeRobloxApi(),
            accountStore: vmStore,
            launcher: launcher ?? new FakeRobloxLauncher(),
            compatChecker: new FakeRobloxCompatChecker(),
            settings: new FakeAppSettings(),
            favorites: favorites ?? new FakeFavoriteGameStore(),
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
            memoryWatchdog: memoryWatchdog ?? new FakeMemoryWatchdog(),
            instanceStopper: instanceStopper ?? new FakeRobloxInstanceStopper(),
            runningProbe: runningProbe ?? new NoRobloxRunningProbe(),
            shellOpener: shellOpener ?? new NullShellOpener(),
            tray: trayService,
            idleAlertPresenter: new IdleAlertPresenter(trayService));

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
    public async Task ReloadGames_NoDefaultMarked_LeavesCurrentDefaultNull_NoFirstGameFallback()
    {
        // Task 3: zero-default is a legitimate state -- ReloadGamesAsync must not silently pick
        // AvailableGames.FirstOrDefault() as a stand-in default. Real FavoriteGameStore (not the
        // throwing FakeFavoriteGameStore) so AddAsync's real auto-default-on-first-add and
        // ClearDefaultAsync's real clear both exercise the actual store contract.
        var favoritesPath = Path.Combine(Path.GetTempPath(), $"rororo-mvm-favorites-test-{Guid.NewGuid():N}.json");
        var favorites = new FavoriteGameStore(favoritesPath);
        var (vm, _, _, path) = Build(favorites: favorites);
        try
        {
            await favorites.AddAsync(111, 1, "A", "");     // first add auto-defaults...
            await favorites.ClearDefaultAsync();            // ...cleared -> games exist, none default
            await vm.ReloadGamesAsync();
            Assert.Null(vm.CurrentDefaultGame);             // NO silent first-game fallback
            Assert.Equal("Roblox home", vm.DefaultGameDisplay);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(favoritesPath)) File.Delete(favoritesPath);
        }
    }

    [Fact]
    public async Task DefaultGameDisplay_WithDefault_ShowsGameName()
    {
        var favoritesPath = Path.Combine(Path.GetTempPath(), $"rororo-mvm-favorites-test-{Guid.NewGuid():N}.json");
        var favorites = new FavoriteGameStore(favoritesPath);
        var (vm, _, _, path) = Build(favorites: favorites);
        try
        {
            await favorites.AddAsync(111, 1, "Pet Sim 99", "");
            await vm.ReloadGamesAsync();
            Assert.Equal("Pet Sim 99", vm.DefaultGameDisplay);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(favoritesPath)) File.Delete(favoritesPath);
        }
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

    /// <summary>A display row carrying just the FPS cap — everything else is irrelevant here.</summary>
    private static AccountSummary RowWithCap(int? fpsCap) => new(new Account(
        Guid.NewGuid(),
        DisplayName: "acct",
        AvatarUrl: "",
        CreatedAt: DateTimeOffset.UtcNow,
        LastLaunchedAt: null,
        FpsCap: fpsCap));

    [Fact]
    public void FpsCapWarning_IsEmpty_WhenEveryAccountSharesOneCap()
    {
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(20));

            vm.RefreshFpsCapWarning();

            Assert.Equal(string.Empty, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_IsEmpty_ForASingleAccount()
    {
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));

            vm.RefreshFpsCapWarning();

            Assert.Equal(string.Empty, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_Appears_WhenTwoAccountsHaveDifferentCaps()
    {
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(9999));

            vm.RefreshFpsCapWarning();

            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_TreatsUnsetAsItsOwnValue()
    {
        // One account capped and one left alone is still a mismatch: the capped account's write
        // and the uncapped account's client contend over the same shared file.
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(null));

            vm.RefreshFpsCapWarning();

            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- FPS-cap warning dismissal (fix/settings-quiet-window) ----
    //
    // The dismissed state is a SIGNATURE of the distinct cap set, not a boolean -- these tests
    // pin that a dismissal is scoped to the exact configuration acknowledged, not "never show
    // this banner again." Each test's discriminating power is called out inline: what production
    // change would make it fail is stated, and two are proven red/green by a temporary mutation
    // (see the two "MUTATION PROOF" comments below, reverted immediately after).

    [Fact]
    public void ComputeFpsCapSignature_IsOrderIndependent_AndTreatsNullAsDistinctToken()
    {
        // Pure-function pin, independent of the VM: row order must not affect the signature, and
        // "no cap" must canonicalize to a stable "none" token rather than being dropped or
        // colliding with an actual numeric cap. Would fail if the implementation sorted by
        // insertion order instead of value, or silently omitted null caps from the signature.
        var a = MainViewModel.ComputeFpsCapSignature([20, null, 45]);
        var b = MainViewModel.ComputeFpsCapSignature([45, 20, null]);
        var c = MainViewModel.ComputeFpsCapSignature([null, 45, 20, 45, null]);   // dupes + reorder

        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Equal("none,20,45", a);
    }

    [Fact]
    public void FpsCapWarning_Dismiss_HidesTheCurrentlyVisibleBanner()
    {
        // Fails if DismissFpsCapWarningCommand doesn't actually record a signature that
        // RefreshFpsCapWarning compares against (e.g. a no-op command, or one that forgets to
        // call RefreshFpsCapWarning after recording).
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(9999));
            vm.RefreshFpsCapWarning();
            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);

            vm.DismissFpsCapWarningCommand.Execute(null);

            Assert.Equal(string.Empty, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_AfterDismiss_ChangeToAnUnseenCapSet_ShowsAgain()
    {
        // Fails if dismissal is implemented as a plain "ever dismissed" boolean/latch instead of
        // a signature comparison -- that shape would keep the banner hidden forever after the
        // first dismissal, even for a genuinely new mismatch the user never acknowledged.
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            var second = RowWithCap(9999);
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(second);
            vm.RefreshFpsCapWarning();
            vm.DismissFpsCapWarningCommand.Execute(null);
            Assert.Equal(string.Empty, vm.FpsCapWarningText);

            second.FpsCap = 45;   // {20, 9999} dismissed -> now {20, 45}, never acknowledged
            vm.RefreshFpsCapWarning();

            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_ReturnToAPreviouslyDismissedSet_StaysHidden()
    {
        // Deliberately routes through an INTERMEDIATE, different, visible mismatch before
        // returning to the originally-dismissed set. A version of this test that dismissed once
        // and immediately re-checked the same unchanged state would pass even if the whole
        // signature system were unwired (nothing would have re-shown the banner in between to
        // prove the comparison is live). Routing through a second, distinct mismatch first proves
        // the stored signature specifically survives an intervening "show" -- it fails if, e.g.,
        // RefreshFpsCapWarning's show-branch ever clears the stored dismissed signature as a
        // side effect (see MUTATION PROOF below, reverted after confirming red).
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            var second = RowWithCap(9999);
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(second);
            vm.RefreshFpsCapWarning();
            vm.DismissFpsCapWarningCommand.Execute(null);          // dismiss {20, 9999}
            Assert.Equal(string.Empty, vm.FpsCapWarningText);

            second.FpsCap = 45;                                     // -> {20, 45}, unseen, shows
            vm.RefreshFpsCapWarning();
            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);

            second.FpsCap = 9999;                                   // back to {20, 9999}, dismissed
            vm.RefreshFpsCapWarning();

            Assert.Equal(string.Empty, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_UnsetPlusCapped_IsStillAMismatch_EvenAfterDismissingADifferentSet()
    {
        // Pins the "unset is its own contending value" rule specifically through the dismissal
        // path -- fails if dismissal collapses null into "no cap" (e.g. by treating a null entry
        // as equal to whatever numeric cap is present) rather than the distinct "none" token.
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(45));
            vm.RefreshFpsCapWarning();
            vm.DismissFpsCapWarningCommand.Execute(null);           // dismiss {20, 45}
            Assert.Equal(string.Empty, vm.FpsCapWarningText);

            vm.Accounts.Clear();
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(null));                      // {20, none} -- never dismissed
            vm.RefreshFpsCapWarning();

            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
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

    [Fact]
    public async Task TryResolveMainFriendSource_MainCachedUserId_ReturnsMainSource()
    {
        var (vm, store, _, path) = Build();
        try
        {
            var mainAcc = await store.AddAsync("MainGuy", "", "maincookie"); // first add auto-promotes to main
            var mainRow = new AccountSummary(mainAcc) { RobloxUserId = 200 };  // cached → no api call
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "altcookie"));
            vm.Accounts.Add(mainRow);
            vm.Accounts.Add(alt);

            var source = await vm.TryResolveMainFriendSourceAsync(alt);

            Assert.NotNull(source);
            Assert.Equal(mainRow.Id, source!.AccountId);
            Assert.Equal(200, source.RobloxUserId);
            Assert.True(source.IsMain);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TryResolveMainFriendSource_NoMain_ReturnsNull()
    {
        var (vm, store, _, path) = Build();
        try
        {
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { RobloxUserId = 100 };
            alt.IsMain = false; // no account is main in the VM's view
            vm.Accounts.Add(alt);

            var source = await vm.TryResolveMainFriendSourceAsync(alt);

            Assert.Null(source);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TryResolveMainFriendSource_MainIsOpenedRow_ReturnsNull()
    {
        var (vm, store, _, path) = Build();
        try
        {
            var mainRow = new AccountSummary(await store.AddAsync("MainGuy", "", "c")) { RobloxUserId = 200 };
            vm.Accounts.Add(mainRow); // mainRow.IsMain is true (first add)

            var source = await vm.TryResolveMainFriendSourceAsync(mainRow); // opened on main's own row

            Assert.Null(source);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TryResolveMainFriendSource_MainUserIdUnresolved_ResolvesAndPersists()
    {
        var api = new StubProfileApi(_ => new UserProfile(200, "mainuser", "MainGuy"));
        var (vm, store, _, path) = Build(api: api);
        try
        {
            var mainRow = new AccountSummary(await store.AddAsync("MainGuy", "", "maincookie")); // RobloxUserId null
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "altcookie"));
            vm.Accounts.Add(mainRow);
            vm.Accounts.Add(alt);

            var source = await vm.TryResolveMainFriendSourceAsync(alt);

            Assert.NotNull(source);
            Assert.Equal(200, source!.RobloxUserId);
            Assert.Equal(200, mainRow.RobloxUserId);
            var persisted = (await store.ListAsync()).Single(a => a.Id == mainRow.Id);
            Assert.Equal(200, persisted.RobloxUserId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TryResolveMainFriendSource_MainResolveThrows_ReturnsNull()
    {
        var api = new StubProfileApi(_ => throw new CookieExpiredException());
        var (vm, store, _, path) = Build(api: api);
        try
        {
            var mainRow = new AccountSummary(await store.AddAsync("MainGuy", "", "maincookie")); // RobloxUserId null
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "altcookie"));
            vm.Accounts.Add(mainRow);
            vm.Accounts.Add(alt);

            var source = await vm.TryResolveMainFriendSourceAsync(alt);

            Assert.Null(source); // fallback to single-source
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- Task 4: trust-aware squad launch — Join-via-friend toggle ----

    [Fact]
    public async Task ToggleJoinViaFriendAsync_FlipsSummaryAndPersists()
    {
        var (vm, store, _, path) = Build();
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie"));
            Assert.False(row.JoinViaFriend);

            await vm.ToggleJoinViaFriendAsync(row);

            Assert.True(row.JoinViaFriend);
            var persisted = (await store.ListAsync()).Single(a => a.Id == row.Id);
            Assert.True(persisted.JoinViaFriend);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ToggleJoinViaFriendAsync_Twice_RoundTrips()
    {
        var (vm, store, _, path) = Build();
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie"));

            await vm.ToggleJoinViaFriendAsync(row);
            await vm.ToggleJoinViaFriendAsync(row);

            Assert.False(row.JoinViaFriend);
            var persisted = (await store.ListAsync()).Single(a => a.Id == row.Id);
            Assert.False(persisted.JoinViaFriend);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ToggleJoinViaFriendAsync_StoreThrows_RevertsAndSetsStatusBanner()
    {
        var (vm, store, _, path) = Build(wrapStore: inner => new JoinViaFriendThrowingStore(inner));
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie"));

            await vm.ToggleJoinViaFriendAsync(row);

            Assert.False(row.JoinViaFriend); // reverted
            Assert.Contains("Couldn't save join-via-friend", vm.StatusBanner);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task AccountSummary_ConstructedFromFlaggedAccount_CarriesJoinViaFriend()
    {
        var (_, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("Alt", "", "cookie");
            await store.SetJoinViaFriendAsync(added.Id, true);
            var flagged = (await store.ListAsync()).Single(a => a.Id == added.Id);

            var row = new AccountSummary(flagged);

            Assert.True(row.JoinViaFriend);
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
        public Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend) => inner.SetJoinViaFriendAsync(id, joinViaFriend);
        public Task SetCaptionColorAsync(Guid id, string? hex) => inner.SetCaptionColorAsync(id, hex);
        public Task SetFpsCapAsync(Guid id, int? fps) => inner.SetFpsCapAsync(id, fps);
        public Task UpdateLocalNameAsync(Guid accountId, string? localName) => inner.UpdateLocalNameAsync(accountId, localName);
        public Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId) => inner.UpdateStreamerIdentityAsync(accountId, fakeName, fakeAvatarId);
        public Task SetTagsAsync(Guid id, IReadOnlyList<string> tags) => inner.SetTagsAsync(id, tags);
        public Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids) => inner.ExportAccountsAsync(ids);
        public Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<AccountExportRecord> records) => inner.ImportMergeAsync(records);
    }

    /// <summary>
    /// Delegates every member to the real store except <see cref="SetJoinViaFriendAsync"/>, which
    /// throws — pins <see cref="MainViewModel.ToggleJoinViaFriendAsync"/>'s revert-on-persist-
    /// failure contract (Task 4, trust-aware squad launch).
    /// </summary>
    private sealed class JoinViaFriendThrowingStore(IAccountStore inner) : IAccountStore
    {
        public Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend)
            => throw new IOException("simulated persist failure");

        public Task UpdateRobloxUserIdAsync(Guid accountId, long userId) => inner.UpdateRobloxUserIdAsync(accountId, userId);
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
        public Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId) => inner.UpdateStreamerIdentityAsync(accountId, fakeName, fakeAvatarId);
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

    /// <summary>
    /// IRobloxApi double whose GetUserProfileAsync runs a supplied delegate (return a profile or
    /// throw). Every other member throws — the main-source resolution only calls GetUserProfileAsync.
    /// </summary>
    private sealed class StubProfileApi(Func<string, UserProfile> getProfile) : IRobloxApi
    {
        public Task<UserProfile> GetUserProfileAsync(string cookie) => Task.FromResult(getProfile(cookie));
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
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

    /// <summary>
    /// Records <see cref="ResetBaseline"/> calls and returns a settable snapshot — the shared
    /// <see cref="FakeMemoryWatchdog"/> below throws on both (deliberately, for tests that never
    /// touch memory-watchdog behavior), so Task 8's recycle tests need a capable double instead.
    /// </summary>
    private sealed class SpyMemoryWatchdog : IMemoryWatchdog
    {
        public readonly List<(Guid Id, int Pid)> Resets = new();
        public MemoryPressureSnapshot Snapshot = new(0, 0, 0, false, null, []);
        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }
        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed { add { } remove { } }
        public void OnAccountLaunched(Guid accountId, int pid) { }
        public void OnAccountExited(Guid accountId, int pid) { }
        public void ResetBaseline(Guid accountId, int pid) => Resets.Add((accountId, pid));
        public void Start() { }
        public void Stop() { }
        public void Sample() { }
        public MemoryPressureSnapshot GetSnapshot() => Snapshot;
    }

    [Fact]
    public async Task RecycleAccountCommand_StopsThenRelaunchesIntoTheAccountsLastLaunchTarget()
    {
        // MainViewModel-level companion to AccountRecyclerTests: that Core-level suite proves
        // AccountRecycler forwards whatever target it's GIVEN correctly, but not that MainViewModel
        // computes/passes the RIGHT target. A wiring bug (e.g. always resolving DefaultGame instead
        // of reading AccountSummary.LastLaunchTarget) would pass every AccountRecycler test and
        // still send the user back to square one -- only a MainViewModel-level assertion on target
        // IDENTITY closes that gap.
        var launcher = new RecordingSuccessLauncher();
        var stopper = new FakeRobloxInstanceStopper();
        var watchdog = new SpyMemoryWatchdog();
        var (vm, store, _, path) = Build(launcher, instanceStopper: stopper, memoryWatchdog: watchdog);
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);

            var originalTarget = new LaunchTarget.Place(PlaceId: 555666777);
            await vm.LaunchAccountForPluginAsync(row, originalTarget);
            Assert.Same(originalTarget, row.LastLaunchTarget);

            var ok = await vm.RecycleAccountAsync(row);

            Assert.True(ok);
            Assert.Equal(row.Id, Assert.Single(stopper.StoppedAccountIds));
            Assert.Equal(2, launcher.Launches.Count); // original launch, then the recycle relaunch
            Assert.Same(originalTarget, launcher.Launches[1]); // the SAME target -- not re-resolved
            Assert.Single(watchdog.Resets);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RecycleAccountCommand_FallsBackToResolvedTarget_WhenAccountNeverLaunchedThisSession()
    {
        var launcher = new RecordingSuccessLauncher();
        var (vm, store, _, path) = Build(launcher, memoryWatchdog: new SpyMemoryWatchdog());
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            Assert.Null(row.LastLaunchTarget);

            var ok = await vm.RecycleAccountAsync(row);

            Assert.True(ok);
            var target = Assert.Single(launcher.Launches);
            Assert.IsType<LaunchTarget.DefaultGame>(target); // no SelectedGame on the row -> default
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // === Server-instance targeting (v1.14) ===
    //
    // Presence has always known WHICH server an account is in; the pipeline dropped it at this
    // boundary. These tests hold the two ends: the row retains the pair, and Recycle spends it.

    private static AccountPresenceEventArgs InGameAt(Guid accountId, ServerInstance server) =>
        new(accountId, UserPresenceType.InGame, server.PlaceId, "Pet Simulator 99!",
            DateTimeOffset.UtcNow, server);

    [Fact]
    public async Task ApplyPresence_InGame_RetainsTheServerInstanceOnTheRow()
    {
        var (vm, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            var server = new ServerInstance(140403681187145, "fcbe3a36-d655-41da-ba8a-8280f5709568");

            vm.ApplyPresence(InGameAt(row.Id, server));

            // Discarding e.Server here again (the pre-v1.14 behaviour) makes this fail.
            Assert.Equal(server, row.CurrentServer);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplyPresence_LeavingTheGame_ClearsTheServerInstance()
    {
        // A job id outlives the session it names. Holding one after the account left would send
        // the next Recycle at a server it is not in — worse than not targeting at all.
        var (vm, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            vm.ApplyPresence(InGameAt(row.Id, new ServerInstance(140403681187145, "job-1")));
            Assert.NotNull(row.CurrentServer);

            vm.ApplyPresence(new AccountPresenceEventArgs(
                row.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));

            Assert.Null(row.CurrentServer);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RecycleAccountCommand_UpgradesAPlaceTargetToTheSERVERTheAccountIsIn()
    {
        // The feature, end to end at the ViewModel: Recycle used to relaunch into LastLaunchTarget
        // verbatim ("this game, any server") and matchmake the user away from their squad. Deleting
        // the upgrade call in RecycleAccountAsync makes this fail with a plain Place.
        var launcher = new RecordingSuccessLauncher();
        var (vm, store, _, path) = Build(launcher, memoryWatchdog: new SpyMemoryWatchdog());
        try
        {
            // Close the verification window so the fire-and-forget check resolves at once instead
            // of leaving a 90 s poll loop running past the end of this test.
            vm.ServerVerificationMaxWait = TimeSpan.Zero;
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            // Launched into the universe's ENTRY place; presence reports a different place inside
            // that universe (Pet Sim teleports) plus the job id. The pair must win, whole.
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(8737899170));
            vm.ApplyPresence(InGameAt(row.Id, new ServerInstance(140403681187145, "job-abc")));

            await vm.RecycleAccountAsync(row);

            var relaunch = Assert.IsType<LaunchTarget.GameJob>(launcher.Launches[1]);
            Assert.Equal(140403681187145, relaunch.PlaceId);
            Assert.Equal("job-abc", relaunch.JobId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RecycleAccountCommand_WithNoKnownServer_RelaunchesTheUnchangedPlace()
    {
        var launcher = new RecordingSuccessLauncher();
        var (vm, store, _, path) = Build(launcher, memoryWatchdog: new SpyMemoryWatchdog());
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            var original = new LaunchTarget.Place(8737899170);
            await vm.LaunchAccountForPluginAsync(row, original);

            await vm.RecycleAccountAsync(row);

            Assert.Same(original, launcher.Launches[1]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RecycleAccountCommand_NeverUpgradesAPrivateServerTarget()
    {
        // A private server's code already names one server, durably. Swapping it for a job id
        // would trade a permanent address for a perishable one and drop the credential.
        var launcher = new RecordingSuccessLauncher();
        var (vm, store, _, path) = Build(launcher, memoryWatchdog: new SpyMemoryWatchdog());
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            var vip = new LaunchTarget.PrivateServer(8737899170, "SHARE_TOKEN", PrivateServerCodeKind.LinkCode);
            await vm.LaunchAccountForPluginAsync(row, vip);
            vm.ApplyPresence(InGameAt(row.Id, new ServerInstance(140403681187145, "job-abc")));

            await vm.RecycleAccountAsync(row);

            Assert.Same(vip, launcher.Launches[1]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplyPresence_StampsWhenTheReadingArrived()
    {
        // Landing verification is only meaningful against readings taken AFTER the relaunch —
        // without this stamp the gate cannot tell a fresh confirmation from the pre-recycle one.
        var (vm, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            var at = DateTimeOffset.UtcNow;

            vm.ApplyPresence(new AccountPresenceEventArgs(
                row.Id, UserPresenceType.InGame, 140403681187145, "Pet Simulator 99!", at,
                new ServerInstance(140403681187145, "job-1")));

            Assert.Equal(at, row.PresenceUpdatedAtUtc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RecycleAccountCommand_ServerTargeted_VerifiesTheLandingAndBannersAMiss()
    {
        // A launch that "Started" proves a process started, nothing more. With the verification
        // window closed (zero wait) and no post-relaunch presence confirmation, the gate must call
        // it a miss and say so — deleting the verification kickoff leaves the banner untouched.
        var launcher = new RecordingSuccessLauncher();
        var (vm, store, _, path) = Build(launcher, memoryWatchdog: new SpyMemoryWatchdog());
        try
        {
            vm.ServerVerificationMaxWait = TimeSpan.Zero;
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(8737899170));
            vm.ApplyPresence(InGameAt(row.Id, new ServerInstance(140403681187145, "job-abc")));

            await vm.RecycleAccountAsync(row);
            var verification = vm.PendingServerVerification;
            Assert.NotNull(verification);
            await verification.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains("TestAlt", vm.StatusBanner);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RecycleAccountCommand_NotServerTargeted_SkipsVerificationEntirely()
    {
        // No job id means no claim to verify. Never nag about a plain "any server" relaunch —
        // landing elsewhere IS the contract there.
        var launcher = new RecordingSuccessLauncher();
        var (vm, store, _, path) = Build(launcher, memoryWatchdog: new SpyMemoryWatchdog());
        try
        {
            vm.ServerVerificationMaxWait = TimeSpan.Zero;
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(8737899170));

            await vm.RecycleAccountAsync(row);

            Assert.Null(vm.PendingServerVerification);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// Launches like <see cref="RecordingSuccessLauncher"/>, but runs a callback as the FIRST
    /// launch fires — standing in for presence reporting where that client landed while the rest of
    /// the batch is still queued. Squad-into-a-public-server is exactly that interleaving.
    /// </summary>
    private sealed class LandingLauncher(Action<int> onLaunch) : IRobloxLauncher
    {
        public List<LaunchTarget> Launches { get; } = [];

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Launches.Add(target);
            onLaunch(Launches.Count);
            return Task.FromResult<LaunchResult>(new LaunchResult.Started(9000 + Launches.Count, DateTimeOffset.UtcNow));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task SquadLaunch_PublicPlace_SendsTheRestOfTheSquadToTheServerTheFirstAccountGot()
    {
        // Before v1.14 this squad was structurally impossible — SelectedTarget was typed
        // PrivateServer, and a public place means "any server with room" for each account
        // independently. The batch must now pivot on where #1 actually landed.
        AccountSummary? first = null;
        var landed = new ServerInstance(140403681187145, "job-shared");
        var launcher = new LandingLauncher(n =>
        {
            if (n != 1 || first is null) return;
            // Presence lands for #1: in game, and here is which server. Timestamped after the
            // launch — a reading from before it would describe a client that no longer exists.
            first.PresenceState = UserPresenceType.InGame;
            first.CurrentServer = landed;
            first.PresenceUpdatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(30);
        });
        var (vm, store, _, path) = Build(launcher);
        try
        {
            vm.InterLaunchThrottle = TimeSpan.Zero;      // 5 s of real throttle buys nothing here
            vm.ServerVerificationPollInterval = TimeSpan.Zero;
            vm.ServerVerificationMaxWait = TimeSpan.Zero; // no background verification loop outliving the test
            foreach (var name in new[] { "Alt1", "Alt2", "Alt3" })
            {
                vm.Accounts.Add(new AccountSummary(await store.AddAsync(name, "", "cookie")));
            }
            first = vm.Accounts[0];

            await vm.SquadLaunchAsync(new LaunchTarget.Place(8737899170)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(3, launcher.Launches.Count);
            // #1 goes in blind — there is no server to name yet.
            Assert.IsType<LaunchTarget.Place>(launcher.Launches[0]);
            foreach (var follower in launcher.Launches.Skip(1))
            {
                var job = Assert.IsType<LaunchTarget.GameJob>(follower);
                Assert.Equal(landed.JobId, job.JobId);
                Assert.Equal(landed.PlaceId, job.PlaceId);
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SquadLaunch_PublicPlace_FirstAccountsServerNeverReadable_StillLaunchesEveryoneIntoTheGame()
    {
        // Spec floor: "No failure path may leave the account outside the game." A scattered squad
        // is worse than a together one and far better than no squad.
        var launcher = new LandingLauncher(_ => { });   // presence never reports a server
        var (vm, store, _, path) = Build(launcher);
        try
        {
            vm.InterLaunchThrottle = TimeSpan.Zero;
            vm.ServerVerificationPollInterval = TimeSpan.Zero;
            vm.ServerVerificationMaxWait = TimeSpan.Zero;
            vm.SquadServerResolveMaxWait = TimeSpan.Zero;  // close the window immediately
            foreach (var name in new[] { "Alt1", "Alt2" })
            {
                vm.Accounts.Add(new AccountSummary(await store.AddAsync(name, "", "cookie")));
            }

            var place = new LaunchTarget.Place(8737899170);
            await vm.SquadLaunchAsync(place).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2, launcher.Launches.Count);
            Assert.All(launcher.Launches, t => Assert.Same(place, t));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplyMemory_CalledTwiceWithUnchangedPressure_MemoryWarningStaysTrue()
    {
        // CRITICAL 1 (final-branch review, 2026-08-01): MemoryWarning must be CONDITION-derived
        // from the snapshot, not edge-derived from "did this call originate from a
        // PressureCrossed event." The prior shape painted MemoryWarning=true only when called
        // with warned:true (from PressureCrossed) and unconditionally wiped it to false on the
        // very next passive 30s-ticker call (warned:false) — even with the account still over
        // cap. The Recycle button is bound solely to MemoryWarning, so it would appear and then
        // erase itself within one 30s tick. This calls the SAME apply path twice — once
        // simulating the crossing, once simulating the passive refresh — with an unchanged
        // over-cap snapshot, and asserts the row is still warned after the second call.
        var (vm, store, _, path) = Build();
        try
        {
            var added = await store.AddAsync("TestAlt", "", "cookie");
            var row = new AccountSummary(added);
            vm.Accounts.Add(row);

            var overCapSnapshot = new MemoryPressureSnapshot(
                AvailableBytes: 1_000_000_000,
                AggregateGrowthBytesPerHour: 0,
                MinutesToCeiling: 0,
                HasProjection: false,
                TargetAccountId: row.Id,
                Accounts: [new AccountMemory(row.Id, 6L * 1024 * 1024 * 1024, 0, 0, OverCap: true, IsTarget: true, ReadOk: true)]);

            vm.ApplyMemory(overCapSnapshot); // simulates the PressureCrossed-triggered apply
            Assert.True(row.MemoryWarning);

            // Discriminator: an unchanged, still-over-cap snapshot must NOT clear the warning on
            // a second call, the way the old "warned: false" passive-refresh path did.
            vm.ApplyMemory(overCapSnapshot); // simulates the next 30s ticker's passive refresh
            Assert.True(row.MemoryWarning);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ApplyMemory_ProjectionIsMachineWide_CapIsScopedPerRow()
    {
        // Closes a coverage gap flagged in the final-branch re-review (2026-08-01, residual 3):
        // the C1 test above always uses HasProjection: false, so ApplyMemory's
        // `|| projectionWarned` term has zero coverage — deleting it leaves all 982 tests green
        // while silently killing the Recycle button for the projection axis, the feature's
        // headline "~N min to ceiling" warning. There was also no test proving a cap breach on
        // ONE row leaves a SECOND row unwarned (the per-row cap-scoping half of C1 was asserted
        // by reading the code, not by a test). One test, two cases, same two rows, closes both.
        var watchdog = new FakeMemoryWatchdogWithProjectionMinutes { ProjectionWarnMinutes = 120 };
        var (vm, store, _, path) = Build(memoryWatchdog: watchdog);
        try
        {
            var addedA = await store.AddAsync("RowA", "", "cookie");
            var addedB = await store.AddAsync("RowB", "", "cookie");
            var rowA = new AccountSummary(addedA);
            var rowB = new AccountSummary(addedB);
            vm.Accounts.Add(rowA);
            vm.Accounts.Add(rowB);

            // Case 1: projection crossed (machine-wide, 30 min < the 120-min threshold above);
            // only rowA is over cap. Discriminator for `|| projectionWarned`: BOTH rows must
            // warn — the machine is what runs out, not any one client — so rowB (never over
            // cap) warning true is the assertion that would go RED if projectionWarned were
            // dropped from the OR.
            var projectionSnapshot = new MemoryPressureSnapshot(
                AvailableBytes: 1_000_000_000,
                AggregateGrowthBytesPerHour: 500_000_000,
                MinutesToCeiling: 30,
                HasProjection: true,
                TargetAccountId: rowA.Id,
                Accounts:
                [
                    new AccountMemory(rowA.Id, 6L * 1024 * 1024 * 1024, 500_000_000, 30, OverCap: true, IsTarget: true, ReadOk: true),
                    new AccountMemory(rowB.Id, 1L * 1024 * 1024 * 1024, 0, 30, OverCap: false, IsTarget: false, ReadOk: true),
                ]);

            vm.ApplyMemory(projectionSnapshot);
            Assert.True(rowA.MemoryWarning);
            Assert.True(rowB.MemoryWarning); // discriminator: proves `|| projectionWarned` fires

            // Case 2: no projection at all, same OverCap pattern. Discriminator for
            // `account.OverCap ||`: only rowA (the over-cap one) may warn now — rowB going
            // false is the assertion that would go RED if the cap term were dropped from the OR
            // (leaving warned derived from projectionWarned alone, which is machine-wide and
            // would paint every row regardless of its own cap state).
            var capOnlySnapshot = new MemoryPressureSnapshot(
                AvailableBytes: 1_000_000_000,
                AggregateGrowthBytesPerHour: 0,
                MinutesToCeiling: 0,
                HasProjection: false,
                TargetAccountId: rowA.Id,
                Accounts:
                [
                    new AccountMemory(rowA.Id, 6L * 1024 * 1024 * 1024, 0, 0, OverCap: true, IsTarget: true, ReadOk: true),
                    new AccountMemory(rowB.Id, 1L * 1024 * 1024 * 1024, 0, 0, OverCap: false, IsTarget: false, ReadOk: true),
                ]);

            vm.ApplyMemory(capOnlySnapshot);
            Assert.True(rowA.MemoryWarning);
            Assert.False(rowB.MemoryWarning); // discriminator: proves cap scoping is per-row
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// The shared <see cref="FakeMemoryWatchdog"/> below throws on most members and hardcodes
    /// nothing settable pre-construction in a way this test needs — this double just needs a
    /// real, settable <see cref="ProjectionWarnMinutes"/> (the shared fake's auto-property
    /// defaults to 0, which would make every projection comparison in <c>ApplyMemory</c>
    /// trivially false) plus enough of the surface not to throw during
    /// <see cref="MainViewModel"/> construction (ctor only subscribes to
    /// <see cref="PressureCrossed"/>, which this never raises).
    /// </summary>
    private sealed class FakeMemoryWatchdogWithProjectionMinutes : IMemoryWatchdog
    {
        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }
        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed { add { } remove { } }
        public void OnAccountLaunched(Guid accountId, int pid) { }
        public void OnAccountExited(Guid accountId, int pid) { }
        public void ResetBaseline(Guid accountId, int pid) { }
        public void Start() { }
        public void Stop() { }
        public void Sample() { }
        public MemoryPressureSnapshot GetSnapshot() => new(0, 0, 0, false, null, []);
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
        public Task<bool?> GetEdgeRemediationAnswerAsync(string themeId) => Task.FromResult<bool?>(null);
        public Task SetEdgeRemediationAnswerAsync(string themeId, bool accepted) => Task.CompletedTask;
        public Task<bool> GetBloxstrapWarningDismissedAsync() => Task.FromResult(true);

        // Backing field (not throw-NotImplemented) so LoadAsync's read/dismiss-signature
        // round trip is exercisable by FPS-cap dismissal tests without a real AppSettings.
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
        // Careful mode off — the default a fresh install runs with, and what the squad tests want.
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
        // Subscribed unconditionally in the MainViewModel ctor — no-op accessors are enough
        // since neither test raises it.
        public event EventHandler? DefaultChanged { add { } remove { } }

        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => throw new NotImplementedException();
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

        // Fire-and-forget from LaunchAccountAsync's Started case (`_ = _processTracker.TrackLaunchAsync(...)`,
        // not awaited) -- a synchronous throw here would still surface (the call itself throws
        // before returning a Task), so this must complete cleanly for any test that exercises the
        // Started/success path (e.g. the Task 8 recycle tests below).
        public Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default) => Task.CompletedTask;
        public bool AttachExisting(Guid accountId, int pid) => throw new NotImplementedException();
        public bool IsTracking(Guid accountId) => throw new NotImplementedException();
        public bool RequestClose(Guid accountId) => throw new NotImplementedException();
        public bool Kill(Guid accountId) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxInstanceStopper : IRobloxInstanceStopper
    {
        public readonly List<Guid> StoppedAccountIds = new();
        public int StopAll() => 0;
        public bool StopAccount(Guid accountId) { StoppedAccountIds.Add(accountId); return true; }
        public int StopWindowless() => 0;
    }

    /// <summary>
    /// Always succeeds with an incrementing pid and records the exact <see cref="LaunchTarget"/>
    /// instance passed for each account — Task 8's recycle test needs to assert the SAME target
    /// reaches the launcher, not merely that a launch happened.
    /// </summary>
    private sealed class RecordingSuccessLauncher : IRobloxLauncher
    {
        private int _nextPid = 5000;
        public readonly List<LaunchTarget> Launches = new();

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Launches.Add(target);
            return Task.FromResult<LaunchResult>(new LaunchResult.Started(_nextPid++, DateTimeOffset.UtcNow));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
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
        public event EventHandler? DefaultChanged;

        // Task 3: ReloadGamesAsync always calls this (games + PS entries merge into one
        // dropdown); empty-but-real is enough since none of these tests save a PS entry.
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

    private sealed class NoRobloxRunningProbe : IRobloxRunningProbe
    {
        public IReadOnlyList<int> GetRunningPlayerPids() => Array.Empty<int>();
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => Array.Empty<RobloxProcessInfo>();
    }

    private sealed class NullShellOpener : IShellOpener
    {
        public void Open(string path) { }
    }
}
