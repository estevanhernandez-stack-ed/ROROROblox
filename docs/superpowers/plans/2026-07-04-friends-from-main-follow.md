# Friends-from-main follow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the per-account Friends picker browse the user's **main** account's friends (default) or the opened account's own, while the account whose row was opened is always the one that launches (follows the picked friend in).

**Architecture:** Extend the existing `FriendFollowWindow` (which already separates "whose friends to list" from "who launches" — it only returns a `FollowFriend` target, the caller launches) with an optional second source identity + a one-button source switch. A pure decision (`FriendSourcePlan.Build`) and an async main-resolution seam (`MainViewModel.TryResolveMainFriendSourceAsync`) carry the logic and are unit-tested; the WPF window is manual-smoke per house convention. `OpenFriendFollowAsync` wires them together; the launch identity (the opened row) is unchanged.

**Tech Stack:** .NET 10 / C# 14, WPF, xUnit. Design spec: [`docs/superpowers/specs/2026-07-04-friends-from-main-follow-design.md`](../specs/2026-07-04-friends-from-main-follow-design.md).

## Global Constraints

- **Branch:** `feat/friends-from-main-follow` (already created off `main`).
- **Solution:** build/test `ROROROblox.slnx` — never the stray `ROROROblox.sln`.
- **Pinned dotnet host:** bare `dotnet` fails `global.json` (pins 10.0.203). Use the explicit host for ALL builds/tests: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe"` (bash) — installed user-local.
- **Commits:** conventional commits (`feat` / `test` / `refactor`).
- **Secret hygiene:** `.ROBLOSECURITY` cookies are never logged and never retained on a window past the API call — fetch fresh into a local. Preserve the existing per-refresh cookie pattern.
- **Copy voice:** builder-to-builder, second person, sentence case. No "empower / leverage / seamlessly / unlock." No emoji. Action-first — nothing may read as an automatic join; the Follow button click is the only launch trigger.
- **Types are `internal`;** `ROROROblox.App.csproj` already has `<InternalsVisibleTo Include="ROROROblox.Tests" />`, so tests can see them.
- **No end-to-end against real roblox.com.**

---

### Task 1: `FriendSource` record + `FriendSourcePlan.Build` (pure decision)

**Files:**
- Create: `src/ROROROblox.App/Friends/FriendSource.cs`
- Test: `src/ROROROblox.Tests/FriendSourcePlanTests.cs`

**Interfaces:**
- Produces: `internal sealed record FriendSource(Guid AccountId, long RobloxUserId, string DisplayName, bool IsMain)` and `internal static (IReadOnlyList<FriendSource> Sources, int DefaultIndex) FriendSourcePlan.Build(FriendSource openedRow, FriendSource? main)`. Both in namespace `ROROROblox.App.Friends`.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/FriendSourcePlanTests.cs`:

```csharp
using ROROROblox.App.Friends;

namespace ROROROblox.Tests;

/// <summary>
/// The pure decision for which friends-list sources the picker offers and which is the default.
/// Main (when present and a DIFFERENT account than the opened row) is index 0 = shown by default;
/// the opened row is always a source. Main == row, or no main, collapses to single-source.
/// </summary>
public class FriendSourcePlanTests
{
    [Fact]
    public void Build_MainPresentAndDistinct_MainIsDefaultThenRow()
    {
        var row = new FriendSource(Guid.NewGuid(), 100, "Alt", IsMain: false);
        var main = new FriendSource(Guid.NewGuid(), 200, "MainGuy", IsMain: true);

        var (sources, defaultIndex) = FriendSourcePlan.Build(row, main);

        Assert.Equal(2, sources.Count);
        Assert.Same(main, sources[0]);   // index 0 = main = shown by default
        Assert.Same(row, sources[1]);
        Assert.Equal(0, defaultIndex);
    }

    [Fact]
    public void Build_NoMain_SingleSourceIsTheRow()
    {
        var row = new FriendSource(Guid.NewGuid(), 100, "Alt", IsMain: false);

        var (sources, defaultIndex) = FriendSourcePlan.Build(row, main: null);

        Assert.Same(row, Assert.Single(sources));
        Assert.Equal(0, defaultIndex);
    }

    [Fact]
    public void Build_MainIsTheOpenedRow_SingleSource()
    {
        var id = Guid.NewGuid();
        var row = new FriendSource(id, 100, "MainGuy", IsMain: true);
        var main = new FriendSource(id, 100, "MainGuy", IsMain: true);

        var (sources, defaultIndex) = FriendSourcePlan.Build(row, main);

        Assert.Same(row, Assert.Single(sources));
        Assert.Equal(0, defaultIndex);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter FriendSourcePlanTests`
Expected: FAIL to compile — `FriendSource` / `FriendSourcePlan` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/ROROROblox.App/Friends/FriendSource.cs`:

```csharp
namespace ROROROblox.App.Friends;

/// <summary>
/// One account the Friends picker can list friends FROM: its saved-account id, resolved Roblox
/// user id, display name, and whether it is the user's designated main. The picker's LAUNCH
/// identity (the row it was opened on) is tracked separately by the caller — a source is only
/// "whose friends you're browsing."
/// </summary>
internal sealed record FriendSource(Guid AccountId, long RobloxUserId, string DisplayName, bool IsMain);

/// <summary>
/// Pure decision for which friends-list sources the picker offers and which is shown by default.
/// </summary>
internal static class FriendSourcePlan
{
    /// <summary>
    /// Build the ordered source list for a picker opened on <paramref name="openedRow"/>. When
    /// <paramref name="main"/> is present and a DIFFERENT account than the opened row, main is placed
    /// first (index 0 = the default source) so main's friends show by default, with the opened row as
    /// the toggle alternate. When main is null or IS the opened row, the picker is single-source.
    /// </summary>
    public static (IReadOnlyList<FriendSource> Sources, int DefaultIndex) Build(
        FriendSource openedRow, FriendSource? main)
    {
        if (main is null || main.AccountId == openedRow.AccountId)
        {
            return ([openedRow], 0);
        }
        return ([main, openedRow], 0);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter FriendSourcePlanTests`
Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Friends/FriendSource.cs src/ROROROblox.Tests/FriendSourcePlanTests.cs
git commit -m "feat(friends): FriendSource + FriendSourcePlan.Build source-decision seam"
```

---

### Task 2: `MainViewModel.TryResolveMainFriendSourceAsync` (async main resolution)

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (add method near `OpenFriendFollowAsync`, ~line 1454)
- Test: `src/ROROROblox.Tests/MainViewModelTests.cs` (add tests + a stub api + extend `Build`)

**Interfaces:**
- Consumes: `FriendSource` (Task 1); `MainAccount` (existing `AccountSummary?` property); `_accountStore`, `_api`, `_log` (existing fields).
- Produces: `internal async Task<FriendSource?> TryResolveMainFriendSourceAsync(AccountSummary openedRow)` — returns null when there's no main, the main IS the opened row, or the main's userId can't be resolved.

- [ ] **Step 1: Extend the test harness — add an injectable api + a profile stub**

In `src/ROROROblox.Tests/MainViewModelTests.cs`, change the `Build` signature to accept an optional `IRobloxApi`. Find:

```csharp
    private static (MainViewModel Vm, IAccountStore AccountStore, IRobloxProcessTracker ProcessTracker, string AccountStorePath) Build(
        IRobloxLauncher? launcher = null,
        ICookieCapture? cookieCapture = null,
        Func<IAccountStore, IAccountStore>? wrapStore = null)
    {
```

Replace with:

```csharp
    private static (MainViewModel Vm, IAccountStore AccountStore, IRobloxProcessTracker ProcessTracker, string AccountStorePath) Build(
        IRobloxLauncher? launcher = null,
        ICookieCapture? cookieCapture = null,
        Func<IAccountStore, IAccountStore>? wrapStore = null,
        IRobloxApi? api = null)
    {
```

Then find the constructor call line `api: new FakeRobloxApi(),` and replace with:

```csharp
            api: api ?? new FakeRobloxApi(),
```

Add this stub next to the other fakes (after `FakeRobloxApi`):

```csharp
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
```

- [ ] **Step 2: Write the failing tests**

Add to `src/ROROROblox.Tests/MainViewModelTests.cs` (inside the `MainViewModelTests` class, near the other `ApplySessionExpired`/reauth tests). `ROROROblox.App.Friends` and `ROROROblox.Core` are already in scope via the existing usings; add `using ROROROblox.App.Friends;` at the top of the file if the compiler flags `FriendSource`.

```csharp
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
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter TryResolveMainFriendSource`
Expected: FAIL to compile — `TryResolveMainFriendSourceAsync` does not exist.

- [ ] **Step 4: Write the implementation**

In `src/ROROROblox.App/ViewModels/MainViewModel.cs`, add this method immediately **above** `OpenFriendFollowAsync` (~line 1449). `ROROROblox.App.Friends` is already in scope (the file constructs `FriendFollowWindow`):

```csharp
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test src/ROROROblox.Tests/ --filter TryResolveMainFriendSource`
Expected: PASS — 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.Tests/MainViewModelTests.cs
git commit -m "feat(friends): TryResolveMainFriendSourceAsync — resolve main as a picker source"
```

---

### Task 3: Extend `FriendFollowWindow` with the source switch (WPF — manual smoke)

**Files:**
- Modify: `src/ROROROblox.App/Friends/FriendFollowWindow.xaml`
- Modify: `src/ROROROblox.App/Friends/FriendFollowWindow.xaml.cs`

**Interfaces:**
- Consumes: `FriendSource` (Task 1).
- Produces: new constructor `FriendFollowWindow(IRobloxApi api, IAccountStore accountStore, IReadOnlyList<FriendSource> sources, int defaultSourceIndex, Guid launcherAccountId)`. `SelectedTarget` / `SelectedPresence` / `SelectedFriendName` outputs are unchanged.

No unit tests — WPF window, manual smoke per house convention (matches `FriendFollowWindow`'s existing posture). The build compiling + the whole suite staying green is the automated gate; behavior is verified by the smoke step.

- [ ] **Step 1: Add the source-switch button + launcher hint to the XAML**

In `src/ROROROblox.App/Friends/FriendFollowWindow.xaml`, inside the `<StackPanel Grid.Row="0">`, immediately **after** the descriptive subtitle `<TextBlock ... Margin="0,4,0,0" TextWrapping="Wrap" />` (the block ending at the `permit."` text), add:

```xml
            <Button x:Name="SourceSwitchButton"
                    HorizontalAlignment="Left"
                    Padding="12,5"
                    Margin="0,12,0,0"
                    Background="{DynamicResource NavyBrush}"
                    Foreground="{DynamicResource CyanBrush}"
                    BorderBrush="{DynamicResource DividerBrush}"
                    BorderThickness="1"
                    FontSize="11"
                    Visibility="Collapsed"
                    Click="OnSourceSwitchClick" />
            <TextBlock x:Name="LauncherHint"
                       FontSize="11"
                       Foreground="{DynamicResource CyanBrush}"
                       Margin="0,8,0,0"
                       TextWrapping="Wrap"
                       Visibility="Collapsed" />
```

The existing subtitle already states the follow works "for public AND private servers if your friend's privacy + server allowlist permit" — that is the friends-only caveat; no separate caveat line is added (DRY).

- [ ] **Step 2: Rewrite the code-behind fields + constructor**

In `src/ROROROblox.App/Friends/FriendFollowWindow.xaml.cs`, replace the fields block and constructor. Find:

```csharp
    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly Guid _accountId;
    private readonly long _accountUserId;
```

Replace with:

```csharp
    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly IReadOnlyList<FriendSource> _sources;
    private readonly Guid _launcherAccountId;
    private int _currentSourceIndex;
```

Find the constructor:

```csharp
    public FriendFollowWindow(IRobloxApi api, IAccountStore accountStore, Guid accountId, long accountUserId, string accountDisplayName)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        }
        if (accountUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountUserId));
        }

        _api = api ?? throw new ArgumentNullException(nameof(api));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _accountId = accountId;
        _accountUserId = accountUserId;
        InitializeComponent();
        Title = $"ROROROblox -- Friends -- {accountDisplayName}";
        AccountTitle.Text = accountDisplayName;
        Loaded += async (_, _) => await RefreshAsync();
    }
```

Replace with:

```csharp
    public FriendFollowWindow(
        IRobloxApi api,
        IAccountStore accountStore,
        IReadOnlyList<FriendSource> sources,
        int defaultSourceIndex,
        Guid launcherAccountId)
    {
        if (sources is null || sources.Count == 0)
        {
            throw new ArgumentException("At least one friend source is required.", nameof(sources));
        }
        if (defaultSourceIndex < 0 || defaultSourceIndex >= sources.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultSourceIndex));
        }

        _api = api ?? throw new ArgumentNullException(nameof(api));
        _accountStore = accountStore ?? throw new ArgumentNullException(nameof(accountStore));
        _sources = sources;
        _currentSourceIndex = defaultSourceIndex;
        _launcherAccountId = launcherAccountId;

        InitializeComponent();

        if (_sources.Count > 1)
        {
            SourceSwitchButton.Visibility = Visibility.Visible;
        }
        UpdateSourceChrome();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private FriendSource CurrentSource => _sources[_currentSourceIndex];

    /// <summary>Refresh title, source-switch label, and the launcher hint for the current source.</summary>
    private void UpdateSourceChrome()
    {
        var current = CurrentSource;
        Title = $"ROROROblox -- Friends -- {current.DisplayName}";
        AccountTitle.Text = current.DisplayName;

        if (_sources.Count > 1)
        {
            var other = _sources[(_currentSourceIndex + 1) % _sources.Count];
            SourceSwitchButton.Content = $"View {other.DisplayName}'s friends";
        }

        // When you're browsing a list that isn't the launching account's own, name the launcher so
        // it's clear which account the Follow button will actually start. Action-first — the button
        // is the only trigger; nothing auto-joins.
        if (current.AccountId != _launcherAccountId)
        {
            var launcherName = _sources.First(s => s.AccountId == _launcherAccountId).DisplayName;
            LauncherHint.Text = $"Follow one to launch {launcherName} into their server.";
            LauncherHint.Visibility = Visibility.Visible;
        }
        else
        {
            LauncherHint.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnSourceSwitchClick(object sender, RoutedEventArgs e)
    {
        _currentSourceIndex = (_currentSourceIndex + 1) % _sources.Count;
        UpdateSourceChrome();
        await RefreshAsync();
    }
```

- [ ] **Step 3: Point `RefreshAsync` at the current source**

In the same file, in `RefreshAsync`, find:

```csharp
            var cookie = await _accountStore.RetrieveCookieAsync(_accountId);

            var friends = await _api.GetFriendsAsync(cookie, _accountUserId);
```

Replace with:

```csharp
            var source = CurrentSource;
            var cookie = await _accountStore.RetrieveCookieAsync(source.AccountId);

            var friends = await _api.GetFriendsAsync(cookie, source.RobloxUserId);
```

Then, in the same method, find the expired-session message:

```csharp
        catch (CookieExpiredException)
        {
            StatusText.Text = "Session expired — close this and re-authenticate the account first.";
        }
```

Replace with (names the current source, and points at the switch when there's an alternate):

```csharp
        catch (CookieExpiredException)
        {
            StatusText.Text = _sources.Count > 1
                ? $"{CurrentSource.DisplayName}'s session expired — re-authenticate it, or switch to the other account's friends."
                : $"{CurrentSource.DisplayName}'s session expired — close this and re-authenticate the account first.";
        }
```

- [ ] **Step 4: Build the whole solution**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build ROROROblox.slnx`
Expected: Build succeeded. (`OpenFriendFollowAsync` still calls the OLD constructor at this point — it won't compile until Task 4. So expect ONE error: the old `new FriendFollowWindow(_api, _accountStore, summary.Id, userId, summary.DisplayName)` call no longer matches. That is expected; Task 4 fixes it. If any OTHER error appears, fix it before moving on.)

> Note: because Task 3 and Task 4 straddle a constructor signature change, they are committed together after Task 4's build passes. Do NOT commit at the end of Task 3 — the tree won't compile. Proceed directly to Task 4.

---

### Task 4: Wire `OpenFriendFollowAsync` to the new source model (manual smoke)

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (~lines 1509-1514, the window-construction block)

**Interfaces:**
- Consumes: `FriendSource` + `FriendSourcePlan.Build` (Task 1), `TryResolveMainFriendSourceAsync` (Task 2), the new `FriendFollowWindow` constructor (Task 3).

- [ ] **Step 1: Replace the window-construction block**

In `src/ROROROblox.App/ViewModels/MainViewModel.cs`, inside `OpenFriendFollowAsync`, find:

```csharp
        // Pass the store + account id, not the plaintext cookie — the window retrieves the cookie
        // fresh per refresh into a short-lived local instead of holding it for its whole lifetime.
        var window = new FriendFollowWindow(_api, _accountStore, summary.Id, userId, summary.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };
```

Replace with:

```csharp
        // Build the picker's friend sources: the opened row is always a source (and always the
        // launcher); the main is added as the default source when present and distinct, so main's
        // friends show first (alts usually have empty lists). The window retrieves each source's
        // cookie fresh per refresh — never holds plaintext for its lifetime.
        var rowSource = new FriendSource(summary.Id, userId, summary.DisplayName, summary.IsMain);
        var mainSource = await TryResolveMainFriendSourceAsync(summary);
        var (sources, defaultIndex) = FriendSourcePlan.Build(rowSource, mainSource);

        var window = new FriendFollowWindow(_api, _accountStore, sources, defaultIndex, summary.Id)
        {
            Owner = Application.Current.MainWindow,
        };
```

The block below it (the `ShowDialog` → `EvaluateFollow` → `LaunchAccountAsync(summary, overrideTarget: target)`) is unchanged — the launch identity stays the opened row.

- [ ] **Step 2: Build + run the whole suite**

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" test ROROROblox.slnx`
Expected: Build succeeded; all tests PASS (the prior green count + 3 from Task 1 + 5 from Task 2 = prior + 8), 1 integration skipped.

- [ ] **Step 3: Manual smoke on a real multi-account setup**

Quit the installed RoRoRo from the tray first (single-instance guard). Launch the Release build:

Run: `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build ROROROblox.slnx -c Release` then start `src/ROROROblox.App/bin/Release/net10.0-windows/ROROROblox.App.exe`

Verify:
1. Open the Friends picker on an **alt** row → it shows the **main's** friends by default; the header names the main; the hint reads "Follow one to launch [Alt] into their server."
2. Click **"View [Alt]'s friends"** → list switches to the alt's own friends; hint hides (browsing the launcher's own list).
3. Switch back → main's friends; **Follow** an in-game joinable friend → the **alt** (not the main) launches into that friend's server.
4. Open the picker on the **main's own** row → no switch button (single source), behaves exactly as before.
5. With **no main set** (unset the main first) → picker opens on the row's own friends, no switch button.

- [ ] **Step 4: Local-path audit + commit (Tasks 3 + 4 together)**

Run the repo's committable local-path guard over the diff:

Run: `git diff main | grep -inE 'c:\\\\+users|/c/users' | grep -viE 'estevan|localappdata' || echo CLEAN`
Expected: `CLEAN`.

```bash
git add src/ROROROblox.App/Friends/FriendFollowWindow.xaml src/ROROROblox.App/Friends/FriendFollowWindow.xaml.cs src/ROROROblox.App/ViewModels/MainViewModel.cs
git commit -m "feat(friends): source switch in the picker — browse main's friends, launch the opened alt"
```

---

## Self-Review

**1. Spec coverage** (each §):
- §1 reframe / drop flavor-detection + Limited anchor → no Limited code touched; feature is a general picker extension. ✓
- §2 mental model (row = launcher, toggle = list source, default main) → `FriendSourcePlan.Build` (main index 0 default) + `_launcherAccountId` separate from source. ✓ (Task 1, 3, 4)
- §3.1 window: optional second identity + toggle + action-first header/hint + cookie hygiene → Task 3. ✓
- §3.2 `OpenFriendFollowAsync` resolves main, launch line unchanged → Task 4. ✓
- §3.3 testable source-resolution seam → `FriendSourcePlan.Build` (Task 1) + `TryResolveMainFriendSourceAsync` (Task 2), both unit-tested. ✓
- §4 edge cases: no main / main == row (Task 1 + Task 2 tests), main expired-or-unresolved → null fallback (Task 2 tests), friends-only caveat (existing subtitle, Task 3 note). ✓
- §5 testing: window manual smoke (Task 3/4), source-resolution unit tests (Task 1/2), EvaluateFollow unchanged. ✓
- §6 out-of-scope: no flavor detection, no Limited button, no friendship detection, no `FollowAltAsync` change, no persisted source preference. ✓ (nothing in the plan adds these)

**2. Placeholder scan:** none — every code step shows complete code; every command shows expected output.

**3. Type consistency:** `FriendSource(Guid AccountId, long RobloxUserId, string DisplayName, bool IsMain)` used identically in Tasks 1/2/3/4. `FriendSourcePlan.Build(openedRow, main) → (IReadOnlyList<FriendSource>, int)` consumed in Task 4 as `(sources, defaultIndex)`. `TryResolveMainFriendSourceAsync(AccountSummary) → Task<FriendSource?>` defined Task 2, consumed Task 4. New window constructor `(IRobloxApi, IAccountStore, IReadOnlyList<FriendSource>, int, Guid)` defined Task 3, consumed Task 4. `UserProfile(long UserId, string Username, string DisplayName)` matches the real record. Consistent.
