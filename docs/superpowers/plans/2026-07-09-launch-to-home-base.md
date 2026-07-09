# Launch-to-Home Base + Optional Default Game Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make "no default game" a real, honest state — launches with no target open **Roblox home** (signed in, joining nothing) instead of hard-failing. Games gain **Clear default** (symmetric with #54's private-server rows); the UI encourages a default rather than requiring one.

**Architecture:** New `LaunchTarget.Home` (`launchmode:app`, authenticated, **no** `placelauncherurl`) via a new `RobloxLauncher.BuildAppLaunchUri` + an `ExecuteLaunchAsync` branch; `ResolveDefaultAsync`'s terminal `return null` → `return Home` (favorites → legacy-settings-URL → Home). `FavoriteGameStore` gains `ClearDefaultAsync` + drops remove-promotion + fires `DefaultChanged` on default-removal (mirroring `PrivateServerStore` from #54). `MainViewModel.ReloadGamesAsync` drops the silent first-game fallback; the widget renders null-default as "Roblox home". Library game rows gain the Clear-default button (mirror the server row).

**Tech Stack:** .NET 10 / C#, WPF, xUnit (real stores over temp files, recording process-starter for URI assertions — no Moq).

**Spec:** `docs/superpowers/specs/2026-07-09-launch-to-home-base-design.md`. Branch: `feat/launch-to-home` (off main; independent of #55). **Contract caveat:** `launchmode:app` is a Roblox-protocol surface RoRoRo hasn't shipped — one live launch gates the merge (Este's smoke) + a decision-log entry (Task 5).

## Global Constraints

- **Build/test with the explicit dotnet host** (`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" …`) against **`ROROROblox.slnx`** only.
- **`Home` never carries a `placelauncherurl`** and always emits `launchmode:app` — it must NOT flow through `BuildLaunchUri`/`BuildPlaceLauncherUrl` (both `launchmode:play` + require a place URL). It gets its own URI path.
- **Never touch Roblox's own settings.** We inform; we don't reconfigure Roblox.
- **No first-run modal exists** — the "forced prompt" is `ExecuteLaunchAsync`'s hard-fail on no-default; this plan softens THAT (→ Home), touching no welcome/modal code.
- **Zero-default is legal, no auto-promotion** (games join servers in this: removing the default leaves zero default → Home).
- **Theme tokens only** in new XAML; the `InverseBoolToVisibilityStyle` **resource-order lesson** (define before consuming templates) already holds in `SettingsWindow.xaml` — don't disturb it.
- No user-profile paths in committed files; conventional commits; store tests over unique temp files matching `FavoriteGameStoreTests` / `RobloxLauncherTests` harness styles (READ them first).

---

## File Structure

**Modified (Core):** `LaunchTarget.cs` (Home record), `RobloxLauncher.cs` (BuildAppLaunchUri + ExecuteLaunchAsync branch + ResolveDefaultAsync tail), `FavoriteGameStore.cs` + `IFavoriteGameStore.cs` (ClearDefaultAsync, RemoveAsync no-promotion + event).
**Modified (App):** `ViewModels/MainViewModel.cs` (ReloadGamesAsync fallback drop, DefaultGameDisplay copy), `MainWindow.xaml` (widget tooltip for null-default), `Settings/SettingsWindow.xaml` (game-row Clear default + header copy), `Settings/SettingsWindow.xaml.cs` (OnClearDefaultClick).
**Tests:** `RobloxLauncherTests.cs`, `FavoriteGameStoreTests.cs`, `MainViewModelTests.cs` (extend each).
**Docs:** `docs/superpowers/smoke/2026-07-09-launch-to-home-smoke.md` (Task 5).

---

## Task 1: `LaunchTarget.Home` + the app-launch URI + resolution

**Files:**
- Modify: `src/ROROROblox.Core/LaunchTarget.cs`, `src/ROROROblox.Core/RobloxLauncher.cs`
- Test: `src/ROROROblox.Tests/RobloxLauncherTests.cs` (extend — READ its harness: `CreateLauncher`, `RecordingProcessStarter.LastUri`, `InMemoryAppSettings`, `StubRobloxApi`)

**Interfaces:**
- Produces: `LaunchTarget.Home` record; `RobloxLauncher.BuildAppLaunchUri(string ticket, long launchTime, string browserTrackerId)` (static, pure — `launchmode:app`, gameinfo, launchtime, browsertrackerid, locale, NO placelauncherurl); `ResolveDefaultAsync` returns `Home` for the terminal no-default case. Consumed by Tasks 3-5.

- [ ] **Step 1: Write the failing tests** (adapt to the real harness; `CreateLauncher` currently passes `favorites: null` — for the no-default→Home test, wire a fake favorites store whose `GetDefaultAsync()` returns null, or add a `CreateLauncher` overload; note in report):

```csharp
    [Fact]
    public void BuildAppLaunchUri_HasAppLaunchmode_NoPlaceLauncherUrl()
    {
        var uri = RobloxLauncher.BuildAppLaunchUri(
            ticket: "TKT-HOME", launchTime: 1714780000000, browserTrackerId: "1234567890123");

        Assert.Contains("launchmode:app", uri);
        Assert.Contains("gameinfo:TKT-HOME", uri);
        Assert.Contains("browsertrackerid:1234567890123", uri);
        Assert.DoesNotContain("placelauncherurl", uri);
        Assert.DoesNotContain("launchmode:play", uri);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_Home_BuildsAppLaunchUri()
    {
        var (launcher, _, processStarter) = CreateLauncher(ticket: "TKT", defaultPlaceUrl: null, startResult: 1);
        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Home());

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Contains(Uri.EscapeDataString("launchmode:app"), processStarter.LastUri);   // (or plain, per how LastUri is stored — match the file's other asserts)
        Assert.DoesNotContain(Uri.EscapeDataString("placelauncherurl"), processStarter.LastUri);
        Assert.DoesNotContain(Uri.EscapeDataString("RequestGame"), processStarter.LastUri);
    }
```

**Rewrite the existing `LaunchAsync_TypedApi_DefaultGame_WithoutAnyDefault_ReturnsFailed`** — post-change, no favorite default + no settings URL resolves to Home:

```csharp
    [Fact]
    public async Task LaunchAsync_TypedApi_DefaultGame_WithNoDefaultAnywhere_LaunchesHome()
    {
        var (launcher, _, processStarter) = CreateLauncher(ticket: "TKT", defaultPlaceUrl: null, startResult: 1);
        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.DefaultGame());

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Contains(Uri.EscapeDataString("launchmode:app"), processStarter.LastUri);
        Assert.DoesNotContain(Uri.EscapeDataString("placelauncherurl"), processStarter.LastUri);
    }
```

(Keep `..._DefaultGame_FallsBackToSettings` GREEN — a settings URL still resolves to a Place launch; the settings tier is retained as a mid-fallback.)

- [ ] **Step 2: Run to verify they fail** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxLauncherTests"` → the new/rewritten tests FAIL.

- [ ] **Step 3: Add `LaunchTarget.Home`** — in `LaunchTarget.cs`, mirror `DefaultGame`'s parameterless style + a doc bullet in the class-level `<list>` and a one-line summary:

```csharp
    /// <summary>Open Roblox at home, authenticated as the launching account, joining nothing —
    /// <c>launchmode:app</c>, no <c>placelauncherurl</c>. The "no default game" resolution target.</summary>
    public sealed record Home() : LaunchTarget;
```

- [ ] **Step 4: The app-launch URI builder** — in `RobloxLauncher.cs`, add a static method mirroring `BuildLaunchUri` but for `launchmode:app` (no placeUrl param, no placelauncherurl segment):

```csharp
    public static string BuildAppLaunchUri(string ticket, long launchTime, string browserTrackerId)
    {
        if (string.IsNullOrEmpty(ticket))
            throw new ArgumentException("Ticket must not be empty.", nameof(ticket));
        if (string.IsNullOrEmpty(browserTrackerId))
            throw new ArgumentException("Browser tracker id must not be empty.", nameof(browserTrackerId));

        var uri = new StringBuilder();
        uri.Append("roblox-player:1");
        uri.Append("+launchmode:app");                       // home, not a game join
        uri.Append("+gameinfo:").Append(ticket);             // still authenticated
        uri.Append("+launchtime:").Append(launchTime);
        uri.Append("+browsertrackerid:").Append(browserTrackerId);
        uri.Append("+robloxLocale:en_us+gameLocale:en_us");
        return uri.ToString();
    }
```

- [ ] **Step 5: Branch `ExecuteLaunchAsync`** for `Home` (skip `BuildPlaceLauncherUrl`; use the app URI). Replace the place-URL assembly (the `BuildPlaceLauncherUrl` + `BuildLaunchUri` pair) with:

```csharp
        var browserTrackerId = (stableBrowserTrackerId ?? _browserTrackerIdFactory()).ToString();
        var launchTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var uri = resolved is LaunchTarget.Home
            ? BuildAppLaunchUri(ticket.Ticket, launchTime, browserTrackerId)
            : BuildLaunchUri(ticket.Ticket, launchTime, browserTrackerId, BuildPlaceLauncherUrl(resolved, browserTrackerId));
```

Also add a `LaunchTarget.Home => throw new InvalidOperationException("Home has no placelauncherurl; build with BuildAppLaunchUri.")` arm to the `BuildPlaceLauncherUrl` switch (defensive — it must never be called for Home; mirrors the `DefaultGame` throw arm).

- [ ] **Step 6: `ResolveDefaultAsync` → Home** — change ONLY the terminal `return null;` (after the empty-settings-URL check) to:

```csharp
    // No favorite default and no legacy settings URL — open Roblox home (signed in) rather than
    // hard-failing. Encourages, doesn't require, a default game.
    return new LaunchTarget.Home();
```

Keep the favorites tier and the settings-URL tier exactly as-is (favorites default → Place; settings URL → FromUrl; else → Home). **Design note (for the reviewer):** the spec's §5 stronger read is "ignore settings entirely"; this plan retains the settings tier as a mid-fallback to avoid regressing any deep-legacy settings-URL user — same feature (true-no-default → Home), zero regression. `ExecuteLaunchAsync`'s `resolved is null` hard-fail branch is now dead (resolution never returns null) — leave the guard as defensive dead code OR remove it; if removed, note it. Prefer leaving it (belt-and-suspenders) with a comment.

- [ ] **Step 7: Run to verify** — new/rewritten launcher tests pass; `..._FallsBackToSettings` still green; full build 0 errors; full suite no regressions.

- [ ] **Step 8: Commit** — `feat(core): LaunchTarget.Home + launchmode:app URI; no-default resolves to Roblox home`

---

## Task 2: `FavoriteGameStore.ClearDefaultAsync` + no-promotion remove + event

**Files:**
- Modify: `src/ROROROblox.Core/IFavoriteGameStore.cs`, `src/ROROROblox.Core/FavoriteGameStore.cs`
- Test: `src/ROROROblox.Tests/FavoriteGameStoreTests.cs` (extend)

**Interfaces:**
- Produces: `Task IFavoriteGameStore.ClearDefaultAsync()` (mirror `PrivateServerStore.ClearDefaultAsync` — no-op short-circuit when zero-default, clear all, fire `DefaultChanged` outside the gate). `RemoveAsync` no longer promotes; fires `DefaultChanged` when the removed row was the default. Consumed by Task 4.

- [ ] **Step 1: Write the failing tests** (port `PrivateServerStore`'s `ClearDefault_...` + `Remove_DefaultServer_ZeroDefault_FiresEvent`; adapt `AddAsync(placeId, universeId, name, thumbnailUrl)` / `PlaceId`):

```csharp
    [Fact]
    public async Task ClearDefaultAsync_RemovesDefault_FiresOnce_NoOpWhenNone()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "A", "https://1"); // first add auto-defaults
        var fires = 0;
        store.DefaultChanged += (_, _) => fires++;
        await store.ClearDefaultAsync();                 // 111 is default -> clears (1)
        Assert.Equal(1, fires);
        Assert.Null(await store.GetDefaultAsync());
        await store.ClearDefaultAsync();                 // already zero -> no-op, no event
        Assert.Equal(1, fires);
        await store.SetDefaultAsync(111);                // set -> clear -> set round-trips
        Assert.NotNull(await store.GetDefaultAsync());
    }

    [Fact]
    public async Task RemoveAsync_OfDefault_LeavesZeroDefault_FiresEvent()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "A", "https://1"); // 111 default
        await store.AddAsync(222, 2, "B", "https://2"); // 111 stays default
        var fires = 0;
        store.DefaultChanged += (_, _) => fires++;
        await store.RemoveAsync(111);                    // removed default -> zero default + event (NO promotion)
        Assert.Equal(1, fires);
        Assert.Null(await store.GetDefaultAsync());
        await store.RemoveAsync(222);                    // 222 was never default -> no event
        Assert.Equal(1, fires);
    }
```

**Fix the now-wrong existing test:** `RemoveAsync_OfDefault_PromotesNext` asserted `GetDefaultAsync()` == 222 after removing 111 — that behavior is intentionally gone. Rename/rewrite it to the `..._LeavesZeroDefault_FiresEvent` above (or delete the old one if the new one supersedes it — don't leave a test asserting promotion).

- [ ] **Step 2: Run to verify** the new tests fail + the old promotion test fails (so it must be rewritten, not left).

- [ ] **Step 3: Interface** — add to `IFavoriteGameStore.cs` (mirror `IPrivateServerStore.ClearDefaultAsync`'s doc) + fix the stale class-doc ("removing the current default auto-promotes the next" → "removing the current default leaves no default; launches open Roblox home until you set one"):

```csharp
    /// <summary>
    /// Clear the default flag on every game, returning to the zero-default state. No-op (no write,
    /// no event) when nothing is default. Zero-default is legal: Launch As opens Roblox home.
    /// </summary>
    Task ClearDefaultAsync();
```

- [ ] **Step 4: Implement** in `FavoriteGameStore.cs`:
  - `ClearDefaultAsync` — copy `PrivateServerStore.ClearDefaultAsync` verbatim, swapping `Servers`→`Favorites`, `s.IsDefault`→`f.IsDefault` (gate → no-op-when-none → clear all `with { IsDefault = false }` → save → fire `DefaultChanged` outside the gate).
  - `RemoveAsync` — delete the `if (wasDefault && Count > 0) promote index 0` block; add a `changed`/`removedDefault` flag set when `wasDefault`, and fire `DefaultChanged?.Invoke(this, EventArgs.Empty)` **outside the gate** after save when it was the default (the method fires NO event today — this adds it, matching the server store).

- [ ] **Step 5: Run to verify** — all new + rewritten pass; whole `FavoriteGameStoreTests` green; full build + suite no regressions. **Watch:** `MainViewModel` subscribes to `_favorites.DefaultChanged` (reloads on it) — the new remove-fires-event means removing the default game now triggers a reload → widget flips to "Roblox home". That's correct; confirm no reentrancy issue (the event fires outside the gate, same as `SetDefaultAsync` already does).

- [ ] **Step 6: Commit** — `feat(core): FavoriteGameStore.ClearDefaultAsync + no-promotion remove (zero-default legal)`

---

## Task 3: `ReloadGamesAsync` no-fallback + widget "Roblox home"

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`, `src/ROROROblox.App/MainWindow.xaml`
- Test: `src/ROROROblox.Tests/MainViewModelTests.cs` (extend — its harness has a fake `IFavoriteGameStore`)

**Interfaces:**
- Consumes: `FavoriteGame.IsDefault`. Produces: `CurrentDefaultGame == null` is now a legitimate "no default → home" state; `DefaultGameDisplay` renders "Roblox home" for null.

- [ ] **Step 1: Write the failing tests:**

```csharp
    [Fact]
    public async Task ReloadGames_NoDefaultMarked_LeavesCurrentDefaultNull_NoFirstGameFallback()
    {
        var (vm, favorites, _, path) = Build();
        try
        {
            await favorites.AddAsync(111, 1, "A", "");     // first add auto-defaults...
            await favorites.ClearDefaultAsync();            // ...cleared -> games exist, none default
            await vm.ReloadGamesAsync();
            Assert.Null(vm.CurrentDefaultGame);             // NO silent first-game fallback
            Assert.Equal("Roblox home", vm.DefaultGameDisplay);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task DefaultGameDisplay_WithDefault_ShowsGameName()
    {
        var (vm, favorites, _, path) = Build();
        try
        {
            await favorites.AddAsync(111, 1, "Pet Sim 99", "");
            await vm.ReloadGamesAsync();
            Assert.Equal("Pet Sim 99", vm.DefaultGameDisplay);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

(Match the real `MainViewModelTests` harness `Build()` shape — adapt the fake-favorites access; the assertions are what matter.)

- [ ] **Step 2: Run to verify they fail** (`DefaultGameDisplay` currently returns "No saved games yet" for null; `ReloadGamesAsync`'s `??` fallback picks the first game).

- [ ] **Step 3: Drop the first-game fallback** in `ReloadGamesAsync` (~line 651) — remove the `?? AvailableGames.FirstOrDefault(g => !IsJoinByLinkSentinel(g) && !g.IsPrivateServer)` clause:

```csharp
        // Default is the game explicitly marked default — no silent first-game fallback.
        // Null is a real state: no default -> Launch As opens Roblox home.
        var defaultGame = AvailableGames.FirstOrDefault(g => g.IsDefault && !IsJoinByLinkSentinel(g) && !g.IsPrivateServer);
```

(The two consumers stay correct: `account.SelectedGame = FindMatchingEntry(...) ?? defaultGame` — a null `defaultGame` yields `SelectedGame = null`, which `ResolveLaunchTarget` turns into `DefaultGame()` → resolves to Home. `CurrentDefaultGame = defaultGame` — null is the new meaningful state.)

- [ ] **Step 4: `DefaultGameDisplay` → "Roblox home"** (~line 414) — the null-fallback becomes the home copy:

```csharp
    public string DefaultGameDisplay =>
        _currentDefaultGame?.LocalName ?? _currentDefaultGame?.Name ?? "Roblox home";
```

- [ ] **Step 5: Widget tooltip for the null-default state** (`MainWindow.xaml`, the `DefaultGameWidget` ToggleButton). The widget stays visible when games exist but none is default (only `WidgetGames.Count == 0` collapses it — unchanged). Add a tooltip that reflects the home state, e.g. bind the ToolTip to a new VM `DefaultGameTooltip` string (`_currentDefaultGame is null ? "Launches open Roblox at home. Set a default game in the Library to launch straight into it." : "The default game Launch As uses when no per-row pick is set. Click to change."`) with an `OnPropertyChanged(nameof(DefaultGameTooltip))` coupled in `CurrentDefaultGame`'s setter. (A VM string property is cleaner than a converter; match the file's existing property style.)

- [ ] **Step 6: Run to verify** — new tests pass; full build 0 errors; suite no regressions.

- [ ] **Step 7: Commit** — `feat(app): no-default game resolves to Roblox home; widget encourages a default`

---

## Task 4: Library game-row Clear default + header copy

UI task — thin XAML + code-behind over the Task-2 store (house convention: manual-smoke). Read `SettingsWindow.xaml`'s current game DataTemplate + the server-row Clear pattern first.

**Files:**
- Modify: `src/ROROROblox.App/Settings/SettingsWindow.xaml`, `src/ROROROblox.App/Settings/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `IFavoriteGameStore.ClearDefaultAsync` (Task 2). Mirrors the server-row Set/Clear pair (#54) exactly.

- [ ] **Step 1: Game-row Clear default button** — in the `FavoriteGame` DataTemplate's action `StackPanel`, AFTER the "Set default" button, add the Clear button (mirror the server row's — no `Tag`, `BoolToVisibilityConverter` on `IsDefault` so it shows only when default):

```xml
                        <Button Content="Clear default"
                                Click="OnClearDefaultClick"
                                Padding="10,6" Margin="0,0,8,0"
                                Background="{DynamicResource NavyBrush}"
                                Foreground="{DynamicResource MutedTextBrush}"
                                BorderBrush="{DynamicResource DividerBrush}"
                                BorderThickness="1"
                                FontSize="11"
                                ToolTip="Stop using this as the default. Launch As will open Roblox home until you set a default game."
                                Visibility="{Binding IsDefault, Converter={StaticResource BoolToVisibilityConverter}}" />
```

(Confirm `BoolToVisibilityConverter` resolves as a StaticResource in this window — the server row already uses it, so it does. Exactly one of Set default (style-trigger-hidden-on-default) / Clear default (shown-on-default) is visible per game row, same as the server row.)

- [ ] **Step 2: `OnClearDefaultClick`** (`SettingsWindow.xaml.cs`, next to `OnSetDefaultClick`; mirror `OnClearDefaultServerClick`):

```csharp
    private async void OnClearDefaultClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _favorites.ClearDefaultAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't clear the default: {ex.Message}";
        }
    }
```

- [ ] **Step 3: Header copy** — append the home note to the description TextBlock:

```
Saved games and private servers. The game marked DEFAULT is what Launch As uses — no default game? Launches open Roblox at home. The private server marked DEFAULT is pre-selected when you launch all accounts to a server. Rename any row to give it a custom name (Roblox-side names stay untouched).
```

- [ ] **Step 4: Verify** — full build 0 errors; suite no regressions. (Visual verification → Task 5 smoke.)

- [ ] **Step 5: Commit** — `feat(library): Clear default on game rows + home copy`

---

## Task 5: Smoke checklist + decision log + final verification

**Files:**
- Create: `docs/superpowers/smoke/2026-07-09-launch-to-home-smoke.md`

- [ ] **Step 1: Write the checklist:**

```markdown
# Smoke checklist — launch-to-home + optional default game

**Branch:** `feat/launch-to-home` · **Spec:** [`../specs/2026-07-09-launch-to-home-base-design.md`](../specs/2026-07-09-launch-to-home-base-design.md)

Store/resolution/URI logic is unit-tested; the live `launchmode:app` launch and the WPF surfaces are the human pass. **`launchmode:app` is a Roblox-contract surface RoRoRo hasn't shipped — the live launch is the load-bearing check.**

## Setup
- [ ] Quit the installed RoRoRo (single-instance); run the dev build; 1+ saved account, 1+ saved game.

## The load-bearing check — launchmode:app
- [ ] **1. Clear the default game** (Library → a game's **Clear default**) → the widget reads **"Roblox home"** (tooltip explains). Then **Launch As** an account → **the client opens signed in, at Roblox home, joining no game.** (This is the Roblox-contract check — if the app opens to home authenticated, `launchmode:app` works.)
- [ ] **2. Fresh state** (no games saved at all) → Launch As → same: Roblox home, signed in, no hard-fail, no prompt.

## Default game still works
- [ ] **3. Set a default game** → widget shows its name → Launch As lands straight in that game (unchanged).
- [ ] **4. Clear default toggling:** Set → Clear → the game row's Set/Clear buttons flip correctly (exactly one visible); the DEFAULT badge tracks.

## No-promotion + honesty
- [ ] **5. Remove the default game** with other games saved → default becomes **none** (no other game silently promoted); widget flips to "Roblox home".
- [ ] **6. Game default vs server default independent** (with #54): a default private server still pre-selects in Squad Launch; the game default/home change doesn't touch it.
- [ ] **7. Theming:** the Clear-default button + widget copy recolor under a custom theme.

## Result
- [ ] All pass (esp. #1 live launchmode:app) → merge-ready. Anything off → note the check + what you saw.
```

- [ ] **Step 2: Final verification battery** (report results):
  - `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors, warning count vs baseline.
  - `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test ROROROblox.slnx` → whole solution green.
  - `SCAN_ALL=1 bash .claude/hooks/pre-commit-local-path-guard.sh` → clean.
  - Hardcoded-hex grep on the branch diff (new XAML/C#) → 0.
  - Confirm `Home` never reaches `BuildPlaceLauncherUrl` (grep the branch for a `Home` arm throwing, and that `ExecuteLaunchAsync` branches before calling it).

- [ ] **Step 3: Commit** — `docs(launch): launch-to-home smoke checklist`

- [ ] **Step 4 (controller, not the subagent): decision-log entry** for the `launchmode:app` contract dependency (per the repo's Roblox-compat rule — this is a new upstream-contract surface; the entry is the tripwire if Roblox ever changes `launchmode:app` handling). Logged at cycle finish alongside the PR.

---

## Self-review notes (author)

**Spec coverage:** §2.1 Home target → T1. §2.2 no-default→Home → T1 (`ResolveDefaultAsync`) + T3 (VM null-default). §2.3 games Clear default + no-promotion → T2 (store) + T4 (UI). §2.4 encouragement copy → T3 (widget) + T4 (header). §2.5 contract caveat → T5 smoke + controller decision-log. §4 architecture → T1-T4. §5 edge cases: no-default→Home (T1), zero games→Home (T1 test), explicit selection unchanged (`ResolveLaunchTarget` untouched — T1 note), default removed→Home no-promotion (T2), legacy settings URL (retained mid-fallback — T1 Step 6 design note), Home-never-in-placelauncherurl (T1 throw arm + T5 grep).

**Deviation flagged:** T1 Step 6 retains the settings-URL fallback tier (favorites → settings → Home) rather than the spec §5 "ignore settings entirely" stronger read — chosen to avoid regressing deep-legacy settings-URL users; delivers the same feature. Reviewer/Este adjudicates.

**Type consistency:** `LaunchTarget.Home` (T1) ← resolution (T1), never in `BuildPlaceLauncherUrl` (T1 throw arm). `BuildAppLaunchUri(string,long,string)` (T1). `IFavoriteGameStore.ClearDefaultAsync()` (T2) → `OnClearDefaultClick` (T4). `DefaultGameDisplay` null→"Roblox home" (T3) consumed by the widget. `RemoveAsync` fires `DefaultChanged` (T2) → `MainViewModel` reload subscription (existing).

**Green-commit discipline:** T1 self-contained (Core launcher). T2 rewrites the promotion test in the same commit it changes the behavior. T4's XAML + handler land together (XAML references the handler). Every task builds green; the live launchmode:app proof is deferred to smoke by design (a Roblox-contract surface can't be unit-verified).
