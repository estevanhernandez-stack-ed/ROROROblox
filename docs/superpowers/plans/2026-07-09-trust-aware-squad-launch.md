# Trust-Aware Squad Launch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Squad launches stop losing accounts to Roblox's join-challenge: challenge-prone accounts (per-account `JoinViaFriend` flag) enter the private server by following an already-landed squad member (the anchor); an optional careful mode serializes all joins so no two accounts are ever mid-join simultaneously.

**Architecture:** `Account.JoinViaFriend` (additive-defaulted, threaded through all seven construct/transport sites) + `IAccountStore.SetJoinViaFriendAsync` (SetSelectedAsync shape). Pure `SquadLaunchPlan` (direct/flagged split) + `AnchorGate` (anchor eligibility + wait predicate, PreWarmGate shape). `SquadLaunchAsync` becomes three-phase: direct batch → anchor-wait → flagged accounts dispatch `LaunchTarget.FollowFriend(anchor)`; careful mode injects a wait-for-InGame between every dispatch. Toggle lives in the account row's context menu; careful mode is a Squad Launch checkbox persisted via `IAppSettings`.

**Tech Stack:** .NET 10 / C#, WPF, xUnit (real stores over temp files, hand-rolled fakes — no Moq).

**Spec:** `docs/superpowers/specs/2026-07-09-trust-aware-squad-launch-design.md` (approved). Branch: `feat/trust-aware-squad-launch` (spec committed 2876929). Compat tripwire: decision `bsvxc1ZvSP9MHMQbnHXU`.

## Global Constraints

- **Build/test with the explicit dotnet host** (`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" …`) against **`ROROROblox.slnx`** only.
- **`JoinViaFriend` must be carried by EVERY construct/copy site** — `Account`, `StoredAccount`, `ListAsync` projection, `AddAsync` (both records), `ImportMergeAsync`, `ExportAccountsAsync`, `AccountExportRecord`. The `with {}` mutators need no change. Missing one silently drops the flag (the #54 lesson).
- **Never strand an account:** every fallback path (no anchor, anchor timeout, all-flagged) still dispatches every eligible account, direct, with the standard throttle, and says so in the banner.
- **No captcha automation of any kind** — nothing in this cycle observes, waits on, or interacts with a challenge. We route joins; we never touch the gate.
- **Zero flagged accounts + careful mode off ⇒ byte-identical behavior to today** (Phase 2/3 skip on empty; no new waits).
- **UI thread discipline:** orchestration waits use the `WaitForPreWarmAsync` shape — `ConfigureAwait(true)`, poll `AccountSummary` state (fed by the existing presence pipeline; NO new Roblox calls), deadline via `DateTime.UtcNow`.
- **Theme tokens only** in new XAML; resource define-before-use (the 2026-07-09 lesson).
- No user-profile paths in committed files; conventional commits; hand-rolled fakes; store tests over unique temp files matching the existing harnesses (read `AccountStoreTests`-family files before writing tests).

---

## File Structure

**Modified (Core):** `Account.cs`, `AccountStore.cs`, `IAccountStore.cs`, `Transport/AccountExportRecord.cs`.
**Created (App):** `ViewModels/SquadLaunchPlan.cs` (pure split), `ViewModels/AnchorGate.cs` (pure predicates).
**Modified (App):** `ViewModels/MainViewModel.cs` (summary flag + toggle command + 3-phase orchestration + widened batch loop), `ViewModels/AccountSummary.cs` (JoinViaFriend property), `MainWindow.xaml` (context-menu item), `SquadLaunch/SquadLaunchWindow.xaml(.cs)` (careful-mode checkbox + IAppSettings), `Core/IAppSettings.cs` + `Core/AppSettings.cs` (CarefulSquadLaunch setting).
**Tests:** account-store tests file (extend), `SquadLaunchPlanTests.cs` (new), `AnchorGateTests.cs` (new), settings tests (extend if an AppSettings test file exists — check).
**Docs:** `docs/superpowers/smoke/2026-07-09-trust-aware-squad-launch-smoke.md` (Task 6).

---

## Task 1: `Account.JoinViaFriend` — field + all seven sites + store mutator

**Files:**
- Modify: `src/ROROROblox.Core/Account.cs`, `src/ROROROblox.Core/AccountStore.cs`, `src/ROROROblox.Core/IAccountStore.cs`, `src/ROROROblox.Core/Transport/AccountExportRecord.cs`
- Test: the existing account-store test file(s) (locate via `ls src/ROROROblox.Tests | grep -i accountstore` — READ first, match harness)

**Interfaces:**
- Produces: `Account.JoinViaFriend` (bool, default false); `Task IAccountStore.SetJoinViaFriendAsync(Guid id, bool joinViaFriend)`. Consumed by Tasks 2, 4, 5.

- [ ] **Step 1: Write the failing tests** (adapt to the real harness; names/shape illustrative):

```csharp
    [Fact]
    public async Task JoinViaFriend_DefaultsFalse_OnAdd()
    {
        // AddAsync a fresh account -> Account.JoinViaFriend == false.
    }

    [Fact]
    public async Task SetJoinViaFriend_RoundTripsThroughDpapiStore()
    {
        // Add -> SetJoinViaFriendAsync(id, true) -> reopen store from the same file -> ListAsync -> flag true.
    }

    [Fact]
    public async Task SetJoinViaFriend_NoOpWrite_WhenUnchanged()
    {
        // Set true twice; assert file LastWriteTime unchanged on the second call (no-op write avoidance),
        // matching SetSelectedAsync's discipline. (If the harness lacks a clean way to observe writes,
        // assert via a second store instance that state is still true and move on — the body mirrors
        // SetSelectedAsync verbatim, which carries the same guarantee.)
    }

    [Fact]
    public async Task SetJoinViaFriend_UnknownId_NoOps()
    {
        // Mirrors SetSelectedAsync's silent-no-op convention for chatty UI toggles (no throw).
    }

    [Fact]
    public async Task ExportImport_CarriesJoinViaFriend()
    {
        // Set flag true -> ExportAccountsAsync -> ImportMergeAsync into a fresh store -> flag survives.
    }

    [Fact]
    public async Task Load_LegacyBlobWithoutJoinViaFriend_DefaultsFalse()
    {
        // The DPAPI store can't be hand-authored as raw JSON; instead: create an account with the
        // CURRENT store, then assert the tolerant-load guarantee the same way the existing
        // IsMain/SortOrder legacy tests do (System.Text.Json fills missing fields with defaults —
        // AccountStore.cs:684-686 documents this). Match whatever pattern the existing legacy tests use.
    }
```

- [ ] **Step 2: Run to verify they fail** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AccountStore"` → new tests FAIL (member not defined).

- [ ] **Step 3: Add the field, trailing-defaulted, to all three records:**

`Account.cs` — append after `BrowserTrackerId`:

```csharp
    long? BrowserTrackerId = null,
    bool JoinViaFriend = false);
```

`AccountStore.cs` `StoredAccount` — same append. `Transport/AccountExportRecord.cs` — same append.

- [ ] **Step 4: Thread the flag through the four projection/construction sites** in `AccountStore.cs` (the `with {}` mutators need nothing):

1. `ListAsync` (~line 58): add `a.JoinViaFriend` to the `new Account(...)` projection (last positional/named arg).
2. `AddAsync` (~89-100): `new StoredAccount(..., JoinViaFriend: false)` and the returned `new Account(..., JoinViaFriend: false)` (a fresh account is never flagged).
3. `ExportAccountsAsync` (~527-538): `new AccountExportRecord(..., JoinViaFriend: a.JoinViaFriend)`.
4. `ImportMergeAsync` (~579-593): `new StoredAccount(..., JoinViaFriend: r.JoinViaFriend)`.

- [ ] **Step 5: Add the mutator.** `IAccountStore.cs`:

```csharp
    /// <summary>
    /// Per-account "route this account into squads via friend-follow" preference (the account is
    /// challenge-prone on direct joins). Silent no-op on unknown id + no-op-write avoidance,
    /// matching <see cref="SetSelectedAsync"/>'s chatty-toggle convention.
    /// </summary>
    Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend);
```

`AccountStore.cs` — mirror `SetSelectedAsync` (lines 163-185) verbatim, swapping the field:

```csharp
    public async Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var blob = await LoadAsync().ConfigureAwait(false);
            var idx = blob.Accounts.FindIndex(a => a.Id == id);
            if (idx < 0)
            {
                return;
            }
            if (blob.Accounts[idx].JoinViaFriend == joinViaFriend)
            {
                return; // no-op write avoidance — saves a DPAPI roundtrip on chatty toggles.
            }
            blob.Accounts[idx] = blob.Accounts[idx] with { JoinViaFriend = joinViaFriend };
            await SaveAsync(blob).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
```

- [ ] **Step 6: Update any fake `IAccountStore` implementations** in the test tree (grep `IAccountStore` for fakes — `MainViewModelTests`' fake set will need the new member; a no-op `Task.CompletedTask` body is correct there).

- [ ] **Step 7: Run to verify** — new tests PASS; whole account-store class green; full build 0 errors; full suite no regressions.

- [ ] **Step 8: Commit** — `feat(core): Account.JoinViaFriend — all construct/transport sites + SetJoinViaFriendAsync`

---

## Task 2: Pure planning — `SquadLaunchPlan` + `AnchorGate`

**Files:**
- Create: `src/ROROROblox.App/ViewModels/SquadLaunchPlan.cs`, `src/ROROROblox.App/ViewModels/AnchorGate.cs`
- Test: `src/ROROROblox.Tests/SquadLaunchPlanTests.cs`, `src/ROROROblox.Tests/AnchorGateTests.cs` (new files; PreWarmGateTests is the style model — plain Facts/Theories, no async, no mocks)

**Interfaces:**
- Produces (consumed by Task 5):

```csharp
internal sealed record SquadPlan(
    IReadOnlyList<AccountSummary> Direct,
    IReadOnlyList<AccountSummary> Flagged);

internal static class SquadLaunchPlan
{
    /// <summary>Split eligible accounts: non-flagged keep their order (direct batch);
    /// flagged accounts come after (follow batch). Pure.</summary>
    public static SquadPlan Build(IReadOnlyList<AccountSummary> eligible);
}

internal static class AnchorGate
{
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);

    /// <summary>An account can anchor a friend-follow when it is in the game AND its Roblox
    /// userId is known (FollowFriend needs a target id).</summary>
    public static bool CanAnchor(bool inGame, long? robloxUserId);

    /// <summary>Pick the first anchor-capable account, or null.</summary>
    public static AccountSummary? PickAnchor(IReadOnlyList<AccountSummary> directBatch);

    /// <summary>Careful-mode / anchor wait tick: done when landed; expired when past deadline.</summary>
    public static bool WaitComplete(bool inGame);
    public static bool WaitExpired(DateTime utcNow, DateTime deadline);
}
```

(`Build` takes `AccountSummary` — it reads only `JoinViaFriend`; Task 4 adds that property to the summary. To keep THIS task compiling before Task 4, `Build`/`PickAnchor` are written against `AccountSummary` but this task's tests construct real `AccountSummary` instances — read `AccountSummaryTests` for the construction pattern. If `AccountSummary` construction needs heavy scaffolding, the acceptable alternative is generic overloads taking `Func<T,bool> isFlagged` selectors — implementer's call, note it in the report. The plan's default: add the trivial `JoinViaFriend` auto-property to `AccountSummary` IN THIS TASK (one line, no store wiring — Task 4 wires it), keeping signatures concrete.)

- [ ] **Step 1: Write the failing tests.** `SquadLaunchPlanTests`: split preserves order within each batch; flagged-last overall; all-flagged → empty Direct; none-flagged → empty Flagged; empty in → both empty. `AnchorGateTests`: `CanAnchor` truth table (`[Theory]`: inGame×userId — only true/known passes); `PickAnchor` returns the FIRST capable (skips landed-but-unknown-userId accounts) / null when none; `WaitComplete`/`WaitExpired` trivial cases + a deadline boundary case.

- [ ] **Step 2: FAIL** (types not defined) → **Step 3: implement** per the interfaces above (Build = two `Where` passes preserving input order; PickAnchor = `FirstOrDefault(s => CanAnchor(s.InGame, s.RobloxUserId))`).

- [ ] **Step 4: PASS + full build/suite green.**

- [ ] **Step 5: Commit** — `feat(app): SquadLaunchPlan split + AnchorGate predicates — pure, tested`

---

## Task 3: Careful-mode setting (Core) + Squad Launch checkbox (UI)

**Files:**
- Modify: `src/ROROROblox.Core/IAppSettings.cs`, `src/ROROROblox.Core/AppSettings.cs`, `src/ROROROblox.App/SquadLaunch/SquadLaunchWindow.xaml`, `.xaml.cs`, and the `SquadLaunchWindow` construction site in `MainViewModel.OpenSquadLaunchAsync` (~1365).
- Test: extend the AppSettings test file if one exists (check `ls src/ROROROblox.Tests | grep -i settings`); otherwise the setting rides the same tolerant-default guarantee the store tests already pin.

**Interfaces:**
- Produces: `Task<bool> IAppSettings.GetCarefulSquadLaunchAsync()` / `Task SetCarefulSquadLaunchAsync(bool)` (consumed by Task 5); a checkbox in Squad Launch that reads the setting on load and writes on toggle.

- [ ] **Step 1: Setting plumbing** — mirror `MuteIdleAlerts` exactly: interface pair; `AppSettings` Get/Set bodies (`AppSettings.cs:164-180` shape); `SettingsBlob` gains `bool CarefulSquadLaunch = false` (trailing default → tolerant load; the three fallback-blob constructions need NO edit, same as MuteIdleAlerts). Test: set→get round-trip through a reopened instance if a settings test file exists.

- [ ] **Step 2: Inject `IAppSettings` into `SquadLaunchWindow`** (new ctor param after `IRobloxApi`); update the single construction site in `OpenSquadLaunchAsync` to pass `_settings` (confirm the field name MainViewModel uses for IAppSettings — grep `IAppSettings` in MainViewModel.cs).

- [ ] **Step 3: The checkbox.** New grid row between eligibility text (row 3) and status/close (old row 4 → becomes 5); rows renumber accordingly:

```xml
        <CheckBox Grid.Row="4" x:Name="CarefulModeToggle"
                  Margin="0,10,0,0"
                  Foreground="{DynamicResource WhiteBrush}"
                  FontSize="12"
                  Click="OnCarefulModeToggle"
                  ToolTip="Waits for each account to fully land in the game before launching the next. Slower, but no two accounts are ever joining at the same time — which is what triggers Roblox's verification prompts.">
            <TextBlock Text="Careful mode — land accounts one at a time" TextWrapping="Wrap" />
        </CheckBox>
```

Code-behind mirrors `OnMuteIdleAlertsToggle` (`PreferencesWindow.xaml.cs:184-202`): read on `OnLoaded` (`CarefulModeToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();`), write on click with the try/catch + suppress-reload pattern.

- [ ] **Step 4: Build 0 errors, suite green, commit** — `feat(squad): careful-mode setting + Squad Launch checkbox`

---

## Task 4: Summary flag + context-menu toggle

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs` (if not already carrying the property from Task 2's note), `src/ROROROblox.App/ViewModels/MainViewModel.cs`, `src/ROROROblox.App/MainWindow.xaml`
- Test: extend `MainViewModelTests.cs` (its harness + fake `IAccountStore` exist; the fake gained `SetJoinViaFriendAsync` in Task 1).

**Interfaces:**
- Consumes: `SetJoinViaFriendAsync` (Task 1). Produces: `AccountSummary.JoinViaFriend` (SetField property, raises INPC), `MainViewModel.ToggleJoinViaFriendCommand` (RelayCommand, parameter = `AccountSummary`).

- [ ] **Step 1: Failing tests:** command flips the summary property and calls the store (fake records the call: id + value); toggling twice round-trips; summaries built from accounts carry the persisted flag (find where `AccountSummary` is constructed from `Account` — the same place `RobloxUserId`/`Tags` flow — and assert a flagged account yields a flagged summary).

- [ ] **Step 2:** FAIL → **Step 3: implement.** `AccountSummary.JoinViaFriend` as a `SetField` property; summary-construction threading; the command (mirror an existing AccountSummary-parameterized command's declaration style):

```csharp
    public ICommand ToggleJoinViaFriendCommand => _toggleJoinViaFriendCommand ??= new RelayCommand(async p =>
    {
        if (p is not AccountSummary summary) return;
        summary.JoinViaFriend = !summary.JoinViaFriend;
        try { await _accountStore.SetJoinViaFriendAsync(summary.Id, summary.JoinViaFriend); }
        catch (Exception ex)
        {
            summary.JoinViaFriend = !summary.JoinViaFriend; // revert on persist failure
            StatusBanner = $"Couldn't save join-via-friend: {ex.Message}";
        }
    });
    private RelayCommand? _toggleJoinViaFriendCommand;
```

(Match the file's real RelayCommand + async conventions — read neighboring commands first; if house RelayCommand isn't async-friendly in this shape, use the pattern the existing async commands use.)

- [ ] **Step 4: Context menu item** in `MainWindow.xaml` (~line 186, after "Reset name"):

```xml
                        <MenuItem Header="Join via friend (challenge-prone account)"
                                  IsCheckable="True"
                                  IsChecked="{Binding JoinViaFriend, Mode=OneWay}"
                                  Command="{Binding PlacementTarget.DataContext.ToggleJoinViaFriendCommand,
                                                    RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                                  CommandParameter="{Binding}"
                                  ToolTip="Squad launches route this account into the server by following a squad member who's already in — Roblox's verification prompts hit direct joins, not follows. Requires friendship with your squad." />
```

(`IsChecked` OneWay + Command doing the flip avoids double-toggle; verify click behavior — if IsCheckable+Command double-fires in this WPF version, drop IsCheckable and prefix a ✓ via header binding instead; note which in the report.)

- [ ] **Step 5: Build + suite green, commit** — `feat(app): per-account Join-via-friend toggle (context menu + persisted)`

---

## Task 5: Three-phase orchestration + careful mode in the batch loop

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` only.
- Test: none new (orchestration is thin over Tasks 1-4's tested logic + the existing launch machinery; the wait predicates are AnchorGate-tested). Verified by build + suite + Task 6 smoke.

**Interfaces:**
- Consumes: `SquadLaunchPlan.Build`, `AnchorGate.*` (Task 2), `GetCarefulSquadLaunchAsync` (Task 3), `AccountSummary.JoinViaFriend` (Task 4), `LaunchTarget.FollowFriend` (exists).

- [ ] **Step 1: Widen `ReleaseBatchAsync`'s override param** from `LaunchTarget.PrivateServer?` to `LaunchTarget?` (callers unchanged — the narrower type flows in fine) **and add an optional per-dispatch wait**:

```csharp
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
                await WaitForLandingAsync(summary).ConfigureAwait(true); // careful mode: serialize joins
            }
            if (idx < targets.Count - 1)
            {
                await Task.Delay(InterLaunchThrottle).ConfigureAwait(true);
            }
        }
    }
```

- [ ] **Step 2: `WaitForLandingAsync`** — the `WaitForPreWarmAsync` shape (poll + deadline, UI thread, banner-narrated):

```csharp
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
```

- [ ] **Step 3: Rework `SquadLaunchAsync`** into the three phases (full replacement of the dispatch section; eligibility + logging + zero-eligible banner stay):

```csharp
    private async Task SquadLaunchAsync(LaunchTarget.PrivateServer target)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await RefreshPresenceBeforeLaunchAsync();

            var summaries = Accounts.ToList();
            var result = LaunchEligibility.Compute(summaries.Select(ToLaunchCandidate));
            var targets = MatchEligible(summaries, result.Eligible);
            var careful = await _settings.GetCarefulSquadLaunchAsync();
            var plan = SquadLaunchPlan.Build(targets);
            _log.LogInformation(
                "PrivateServer: placeId={PlaceId}, {Count} eligible ({Direct} direct, {Flagged} join-via-friend), careful={Careful}, {Running} running, {Expired} expired, {Deselected} deselected",
                target.PlaceId, targets.Count, plan.Direct.Count, plan.Flagged.Count, careful,
                result.Breakdown.Running, result.Breakdown.Expired, result.Breakdown.Deselected);
            if (targets.Count == 0)
            {
                StatusBanner = result.ZeroEligibleBanner;
                return;
            }

            // Phase 1 — direct batch (byte-identical to today when nothing is flagged + careful off).
            if (plan.Direct.Count > 0)
            {
                await DispatchBatchAsync(
                    plan.Direct,
                    overrideTarget: target,
                    launchingBanner: (summary, n, total) => $"Joining private server: {summary.DisplayName} ({n} of {total})...",
                    waitForLanding: careful);
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
                        launchingBanner: (summary, n, total) => $"{summary.DisplayName} joining via {anchor.DisplayName} ({n} of {total})...",
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
                    await ReleaseBatchAsync(
                        plan.Flagged,
                        overrideTarget: target,
                        launchingBanner: (summary, n, total) => $"Joining private server (direct fallback): {summary.DisplayName} ({n} of {total})...",
                        startIndex: 0,
                        waitForLanding: careful);
                }
            }

            StatusBanner = result.PartialBanner(targets.Count, "Private server launch finished");
        }
        finally
        {
            IsBusy = false;
        }
    }
```

**Adaptation notes:** `DispatchBatchAsync` (the pre-warm wrapper) gains the pass-through `waitForLanding` parameter down to its `ReleaseBatchAsync` calls — read its body (~1224-1328) and thread it. Confirm MainViewModel's `IAppSettings` field name. `Accounts`/eligibility code is verbatim-existing — keep it identical.

- [ ] **Step 4: Verify** — full build 0 errors; whole solution test green (777+ baseline: Tasks 1-4 added tests). Grep sanity: `waitForLanding: careful` present on all three dispatch paths; the zero-flagged path reduces to exactly one `DispatchBatchAsync` call.

- [ ] **Step 5: Commit** — `feat(squad): three-phase trust-aware squad launch — direct, anchor, follow (+ careful mode)`

---

## Task 6: Smoke checklist + final verification

**Files:**
- Create: `docs/superpowers/smoke/2026-07-09-trust-aware-squad-launch-smoke.md`

- [ ] **Step 1: Write the checklist:**

```markdown
# Smoke checklist — trust-aware squad launch

**Branch:** `feat/trust-aware-squad-launch` · **Spec:** [`../specs/2026-07-09-trust-aware-squad-launch-design.md`](../specs/2026-07-09-trust-aware-squad-launch-design.md)

Store/plan/gate logic is unit-tested; the orchestration + UI below is the human pass. Needs live
Roblox + the clan private server + at least one challenge-prone account.

## Setup
- [ ] Quit installed RoRoRo (single-instance); run the dev build; 3+ eligible accounts, PS saved.

## Toggle + persistence
- [ ] **1.** Right-click a row → "Join via friend" → check appears; restart the app → still checked.
- [ ] **2.** Export accounts → import on the same box → flag survives.

## Squad launch — the real test
- [ ] **3. Zero flagged, careful off:** squad launch behaves EXACTLY as before (5s cadence, same banners).
- [ ] **4. Flagged path:** flag a challenge-prone account → squad launch → direct accounts go first,
      banner shows "Waiting for a squad member to land…", then "[flagged] joining via [anchor]…" —
      and the flagged account lands IN THE SAME private server, no CAPTCHA.
- [ ] **5. Fallback:** flag ALL accounts → squad launch → banner explains no anchor; everyone joins
      direct with the normal throttle. Nobody is skipped.
- [ ] **6. Careful mode:** enable the checkbox → cadence visibly serializes (each account lands
      before the next dispatches); toggle persists across app restarts.
- [ ] **7. Banner truthfulness:** every phase narrated; timeout fallback says so.

## The success metric (spec §7)
- [ ] The challenge-prone accounts land first-try, zero CAPTCHAs, across several days of dailies.

## Result
- [ ] All pass → merge-ready. Anything off → check + observation → fix pass before merge.
```

- [ ] **Step 2: Final verification** — whole solution build + test green; `SCAN_ALL=1` guard clean; grep new XAML/C# for hardcoded hex → 0; grep the diff for anything touching challenge/captcha handling → only comments/banners (the wall).

- [ ] **Step 3: Commit** — `docs(squad): trust-aware squad launch smoke checklist`

---

## Self-review notes (author)

**Spec coverage:** §2.1 flag → T1, T4. §2.2 phases → T2 (plan/gate), T5 (orchestration). §2.3 anchor+userId + not-friends-lands-home accepted → T2 `CanAnchor`, spec'd tooltip in T4. §2.4 careful mode → T3 (setting+UI), T5 (waits). §2.5/§1 contract tripwire → decision logged pre-plan. §2.6 banners → T5 strings. §5 edge cases: all-flagged → T5 fallback + T2 test; anchor-timeout → T5 fallback; zero-flagged byte-identical → T5 structure + smoke 3; SessionLimited exclusion → untouched eligibility. §6 tests → T1/T2/T3/T4 + smoke.

**Type consistency:** `SetJoinViaFriendAsync(Guid,bool)` T1→T4. `SquadPlan(Direct,Flagged)` + `AnchorGate.MaxWait/CanAnchor/PickAnchor/WaitComplete/WaitExpired` T2→T5. `GetCarefulSquadLaunchAsync` T3→T5. `AccountSummary.JoinViaFriend` T2(property)/T4(wiring)→T5. `ReleaseBatchAsync(..., LaunchTarget?, ..., bool waitForLanding)` widened in T5; its existing callers compile via implicit widening + default param.

**Sequencing:** T2's `AccountSummary.JoinViaFriend` one-liner keeps the pure code concrete before T4 wires persistence (noted in both tasks — T4 checks whether T2 already added it). Every commit builds.

**Adaptation points flagged:** account-store test harness names (T1), AccountSummary construction site (T4), MainViewModel's IAppSettings field name + `DispatchBatchAsync` threading (T5), RelayCommand async convention (T4), IsCheckable+Command double-fire check (T4).
