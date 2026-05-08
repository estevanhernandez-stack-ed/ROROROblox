# ROROROblox v1.3.x — Build Checklist

**Cycle type:** Spec-first cycle (pattern mm). Substantive design at [`docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md). [`spec.md`](spec.md), [`scope.md`](scope.md), and [`prd.md`](prd.md) are pointer-stubs to that canonical doc.

**Build mode:** autonomous-with-verification (checkpoints after items 2, 8)
**Comprehension checks:** off
**Git cadence:** commit after each item; no spike (no Roblox-side contract work — feature lives entirely on top of stable v1.1/v1.2 interfaces)
**Repo:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)
**Branch:** cut a fresh implementation branch off `main` when build session starts (per spec §9 decision 5 — separate from the spec branch)

---

- [ ] **1. Schema additions — `LocalName: string?` on three records + JSON compat tests**
  Spec ref: `spec.md > §5.1 Data model additions`, `§8 Edge cases (re-add semantics)`, `§12 Testing`
  What to build: Add `string? LocalName = null` as a trailing nullable parameter on three records in `ROROROblox.Core`: `FavoriteGame` (`src/ROROROblox.Core/FavoriteGame.cs`), `SavedPrivateServer` (`src/ROROROblox.Core/SavedPrivateServer.cs`), `Account` (`src/ROROROblox.Core/Account.cs`). Trailing position + nullable default = source-compat for callers that don't yet supply it. `System.Text.Json` deserializes missing fields to the C# default (`null`) automatically — no `[JsonPropertyName]` or custom converter needed. Verify the property appears in serialized JSON only when non-null is too risky (round-trip stability matters more than payload size); use the default behavior — `LocalName: null` does emit in the JSON, which is fine for forward-compat (older RORORO versions ignore unknown properties on read).
  Acceptance: Three new unit tests in `ROROROblox.Tests` per record (9 total): (a) loading a legacy JSON fixture without the `LocalName` field deserializes cleanly with `LocalName == null`; (b) writing a record with `LocalName = "Custom"` then re-reading produces `LocalName == "Custom"`; (c) writing a record with `LocalName = null` then re-reading produces `LocalName == null` (not missing-vs-null distinction surprise). All 61 existing tests still green (no regression in the v1.1/v1.2 store contracts).
  Verify: `dotnet test --filter LocalNameSchemaTests`. Manually inspect a sample `favorites.json` written by the test to confirm the JSON shape. Commit: `feat(core): add LocalName: string? to FavoriteGame, SavedPrivateServer, Account`.

- [ ] **2. Store interface additions — `UpdateLocalNameAsync` × 3 + `DefaultChanged` event + re-add preservation**
  Spec ref: `spec.md > §5.2 Store interface additions`, `§6.2 Rename data flow`, `§8 Edge cases (re-add semantics, KeyNotFoundException, IOException)`, `§9 Decisions log #2`
  What to build: Three additions to existing store interfaces + their implementations.
  - `IFavoriteGameStore`: add `Task UpdateLocalNameAsync(long placeId, string? localName)` and `event EventHandler? DefaultChanged`. Implementation mutates the in-memory list, writes via the existing atomic-write pattern (temp file + `File.Move(temp, dest, overwrite: true)`), throws `KeyNotFoundException` on missing `placeId` (matches `SetDefaultAsync` shape). `DefaultChanged` fires after `SetDefaultAsync` completes successfully — null-safe invocation pattern (`DefaultChanged?.Invoke(this, EventArgs.Empty)`).
  - `IPrivateServerStore`: add `Task UpdateLocalNameAsync(Guid serverId, string? localName)`. Same atomic-write contract; `KeyNotFoundException` on missing ID.
  - `IAccountStore`: add `Task UpdateLocalNameAsync(Guid accountId, string? localName)`. **DPAPI envelope must roundtrip the new property** — verify the encrypted blob on disk decrypts cleanly after the property is added (regression guard against the v1.1 envelope shape).
  - **Re-add preservation:** `IFavoriteGameStore.AddAsync` (and `IPrivateServerStore.AddAsync`) already replace on duplicate `placeId`/`code`. Modify the replace path so an existing `LocalName` is preserved across re-adds. Document in code with a single-line comment: `// LocalName survives re-add (spec §8)`. Apply the same guarantee to any equivalent Account replace path if one exists.
  Acceptance: Per-store unit tests for `UpdateLocalNameAsync` covering: happy path (set → read), null path (set → reset), `KeyNotFoundException` on missing ID, atomic-write fault injection (force `IOException` mid-write, confirm temp file cleanup + in-memory state intact), DPAPI roundtrip with `LocalName` populated. `IFavoriteGameStore.DefaultChanged` test fires once after `SetDefaultAsync` and zero times after a no-op `SetDefaultAsync` (re-setting current default). Re-add preservation test: add game `placeId=1` with `LocalName="Custom"` → re-add same `placeId` with new `Name`/`ThumbnailUrl` → assert `LocalName == "Custom"` on the resulting record. ~9-12 new tests total. All existing tests still green.
  Verify: `dotnet test --filter "UpdateLocalNameAsync|DefaultChanged|RealdPreservation"`. Manual: inspect `accounts.dat` after a roundtrip with `LocalName` populated; confirm DPAPI header is still `01 00 00 00 D0 8C 9D DF` and decrypt path works. Commit: `feat(core): UpdateLocalNameAsync + DefaultChanged event + re-add LocalName preservation`.

- [ ] **CHECKPOINT 1** (after item 2 — primitives complete; UI/ViewModel work starts next)
  Manual review: confirm items 1-2 work end-to-end via a temporary debug button in `MainWindow` (or a unit-test-driven sanity walk) that calls `IFavoriteGameStore.UpdateLocalNameAsync(<placeId>, "Custom")` against a real `favorites.json` on disk, then re-reads via `MainWindow`'s existing rendering. The render still shows `Name` (not `LocalName`) at this point because item 8 hasn't switched bindings yet — that's expected. The check is: does the on-disk JSON now contain `"localName": "Custom"`, and does a re-add of the same placeId preserve it? If anything is shaky, fix before proceeding to UI items — render-coverage work assumes the storage layer is bulletproof.

- [ ] **3. `RenameTarget` DTO + `RenameTargetKind` enum**
  Spec ref: `spec.md > §5.3 RenameTarget`
  What to build: Two types in `ROROROblox.App` (App project, NOT Core — entity-agnostic shape lives next to the popup that consumes it):
  ```csharp
  public sealed record RenameTarget(
      RenameTargetKind Kind,
      object Id,                  // long for Game, Guid for PrivateServer/Account
      string OriginalName,        // Roblox-side Name or DisplayName
      string? CurrentLocalName);

  public enum RenameTargetKind { Game, PrivateServer, Account }
  ```
  No interface; just the record + enum. Per spec §5.3, `MainViewModel.RenameItemCommand` will switch on `Kind` to dispatch to the right store.
  Acceptance: Type compiles. Two construction-shape tests confirm equality via the value-record contract (`new RenameTarget(Kind.Game, 1L, "X", null) == new RenameTarget(Kind.Game, 1L, "X", null)`). No persistence — pure DTO.
  Verify: `dotnet test --filter RenameTargetTests`. Commit: `feat(app): RenameTarget DTO + RenameTargetKind enum`.

- [ ] **4. `RenameWindow` — shared `ui:FluentWindow` modal**
  Spec ref: `spec.md > §5.5 RenameWindow`, `§6.2 Rename data flow`, `§6.3 Reset`, `§8 Edge cases (empty/whitespace input, long input)`
  What to build: New `RenameWindow.xaml` + code-behind in `ROROROblox.App` (`src/ROROROblox.App/Views/RenameWindow.xaml`). Shape: `ui:FluentWindow`, `Width=360 Height=180`, `ResizeMode=NoResize`, `WindowStartupLocation=CenterOwner`, owner-modal (caller passes `Owner` before `ShowDialog`). Contents from spec §5.5:
  - Title bar: `Rename`
  - Mono-micro line (`FontFamily=JetBrains Mono`, uppercase + 0.12em tracking per brand tokens at `~/.claude/skills/626labs-design/colors_and_type.css`): `ROBLOX NAME — {OriginalName}`
  - `TextBox` pre-filled with `CurrentLocalName ?? OriginalName`, focused on load, all-text-selected for fast retype
  - Hyperlink `Reset to original` — `Visibility = Visible` only when `CurrentLocalName != null` (binding via `BooleanToVisibilityConverter` on `HasLocalName`)
  - `Save` (cyan, default) and `Cancel` (text-style) buttons. `IsDefault=True` on Save (Enter triggers); `IsCancel=True` on Cancel (Esc triggers).
  - Code-behind: a single `static Task<RenameResult> ShowAsync(Window owner, RenameTarget target)` factory. Returns `RenameResult { ResultKind: Save | Cancel | Reset, NewName: string? }`. On Save: trim input, treat empty/whitespace as `null` (effective reset, same code path).
  **Quality bar (per builder profile + spec §3 + cycle #1 pattern x):** chrome MUST match the existing `ui:FluentWindow` modals (`WebView2NotInstalledWindow`, `RobloxNotInstalledWindow`, `DpapiCorruptWindow`) — same border treatment, same title bar, same brush palette, same typography stack. Programmatic placeholder chrome is disqualifying.
  Acceptance: Window opens centered on owner. Pre-fill + auto-select work. Enter saves; Esc cancels. `Reset to original` link only renders when `CurrentLocalName != null` and clicking it returns `ResultKind=Reset, NewName=null` (no `Save` round-trip required). Visual smoke with the cyan Save button + magenta accent (per brand tokens) — matches the cycle #1 modals side-by-side.
  Verify: `dotnet build` clean. Manual smoke: open the window from a test-only debug button on MainWindow with three different `RenameTarget` shapes (Game with no `CurrentLocalName`, PrivateServer with `CurrentLocalName="Squad #1"`, Account with `CurrentLocalName="Main Acct"`); confirm each renders correctly + the `Reset to original` link appears only for the latter two. **Compare side-by-side with the cycle #1 modals — chrome MUST be visually identical** (same FluentWindow title bar, same border, same button style). Commit: `feat(app): RenameWindow shared modal for rename overlay`.

- [ ] **5. `MainViewModel` plumbing — commands + `DefaultChanged` + RenameTarget dispatch + Roblox-API-refresh decoupling**
  Spec ref: `spec.md > §4 Architecture (MainViewModel section)`, `§5.4 DefaultGameWidget bindings`, `§6.1 + §6.2 + §6.3 Data flows`, `§7 Render surfaces (DefaultGameDisplay)`, `§8 Edge cases (refresh decoupling)`, `§9 Decisions log #3`
  What to build: Five additions to `MainViewModel` (`src/ROROROblox.App/ViewModels/MainViewModel.cs`):
  - `string DefaultGameDisplay` (INPC) — reads `_favoriteGameStore.Default?.LocalName ?? _favoriteGameStore.Default?.Name ?? "(no default)"`. Updates whenever `IFavoriteGameStore.DefaultChanged` or `Accounts`/`AvailableGames` mutates such that the default's display value changed.
  - `IRelayCommand<FavoriteGame> SetDefaultGameCommand` — invokes `_favoriteGameStore.SetDefaultAsync(game.PlaceId)`. Closes the dropdown popup via `IsDefaultGameDropdownOpen = false`.
  - `IRelayCommand<RenameTarget> RenameItemCommand` — opens `RenameWindow.ShowAsync(MainWindow, target)`. On `Save` or `Reset` result, switches on `target.Kind`:
    - `Game` → `_favoriteGameStore.UpdateLocalNameAsync((long)target.Id, result.NewName)`
    - `PrivateServer` → `_privateServerStore.UpdateLocalNameAsync((Guid)target.Id, result.NewName)`
    - `Account` → `_accountStore.UpdateLocalNameAsync((Guid)target.Id, result.NewName)`
    Then re-fetches the affected collection (`AvailableGames` / saved-server list / `Accounts`) and raises INPC. Catches `KeyNotFoundException` + `IOException` per spec §8 → sets `StatusBanner` to the documented copy.
  - `IRelayCommand<RenameTarget> ResetItemNameCommand` — same dispatch as RenameItemCommand but skips the popup and calls `UpdateLocalNameAsync(id, null)` directly. Used by the `Reset name` context menu item.
  - **Subscription:** in MainViewModel constructor, subscribe to `_favoriteGameStore.DefaultChanged += OnDefaultChanged` where `OnDefaultChanged` raises INPC on `DefaultGameDisplay`. Unsubscribe in `Dispose()`.
  - **Roblox-API-refresh decoupling:** the existing call sites that update `Account.DisplayName` (from `IRobloxApi.GetUserProfileAsync`) and `FavoriteGame.Name` (from `IRobloxApi.GetUniverseInfo` or equivalent) MUST construct the new record preserving the prior `LocalName`. Audit every call site that creates a refreshed record via `with` expression or constructor — explicitly thread `LocalName: existing.LocalName` through. **One-line code comment per call site:** `// LocalName survives Roblox-side refresh (spec §5.5 + §9 decision 3)`.
  Acceptance: Unit tests for `MainViewModel`: (a) `DefaultGameDisplay` updates on `DefaultChanged`; (b) `SetDefaultGameCommand` calls `SetDefaultAsync` then refreshes; (c) `RenameItemCommand` with `Kind=Game` dispatches to `IFavoriteGameStore.UpdateLocalNameAsync` and not the other stores (verify with strict mocks); same for `PrivateServer` and `Account`; (d) `RenameItemCommand` catches `KeyNotFoundException` and surfaces the status banner without rethrowing; (e) `RenameItemCommand` catches `IOException` and surfaces the documented status banner copy `"Couldn't save name change. Disk error?"`. **Roblox-refresh decoupling test:** mock `IRobloxApi.GetUserProfileAsync` to return a new display name; assert that the resulting `Account` record has the prior `LocalName` preserved. Same shape for the game-name refresh path. ~7-9 new tests.
  Verify: `dotnet test --filter "MainViewModel|RobloxRefreshDecoupling"`. Build clean. Commit: `feat(app): MainViewModel rename + default-game commands + refresh decoupling`.

- [ ] **6. `DefaultGameWidget` XAML — toolbar widget + popup + empty-state + compact-mode**
  Spec ref: `spec.md > §5.4 DefaultGameWidget`, `§6.1 Quick-switch data flow`, `prd.md > Story 1.1 / 1.2 / 1.3 / 1.4`
  What to build: Inline XAML in `MainWindow.xaml` Header Row 2, replacing the current `Width="*"` filler column between the `Games` button and the `Launch multiple` CTA. Shape per spec §5.4:
  ```xml
  <ToggleButton x:Name="DefaultGameWidget"
                MinWidth="200" MaxWidth="340"
                Margin="8,0" Padding="10,6"
                Background="{DynamicResource RowBgBrush}"
                BorderBrush="{DynamicResource CyanBrush}"
                BorderThickness="1"
                IsChecked="{Binding IsDefaultGameDropdownOpen, Mode=TwoWay}">
    <Grid>
      <!-- icon + DEFAULT micro-label + DefaultGameDisplay + chevron -->
    </Grid>
  </ToggleButton>
  <Popup IsOpen="{Binding ElementName=DefaultGameWidget, Path=IsChecked}"
         PlacementTarget="{Binding ElementName=DefaultGameWidget}"
         Placement="Bottom"
         StaysOpen="False">
    <Border Background="{DynamicResource SurfaceBgBrush}"
            BorderBrush="{DynamicResource CyanBrush}"
            BorderThickness="1"
            CornerRadius="4"
            Padding="4">
      <DockPanel>
        <Button DockPanel.Dock="Bottom"
                Content="Manage games…"
                Command="{Binding OpenSettingsCommand}" />
        <ListBox ItemsSource="{Binding AvailableGames}"
                 SelectedItem="{Binding CurrentDefaultGame, Mode=OneWay}">
          <!-- DataTemplate: dot + (LocalName ?? Name) + DEFAULT pill on current default -->
        </ListBox>
      </DockPanel>
    </Border>
  </Popup>
  ```
  Bind `AvailableGames.Count == 0` to a Style trigger that swaps the readout text to `"No saved games yet"` and hides the popup, routing the click to `OpenSettingsCommand` directly. Compact-mode hide: bind `Visibility` to `IsCompact` via the same `BooleanToVisibilityConverter` pattern the surrounding header already uses.
  Use brand tokens (cyan border accent, magenta `DEFAULT` pill) per `~/.claude/skills/626labs-design/colors_and_type.css`. Type face for `DEFAULT` micro-label is JetBrains Mono uppercase + 0.12em tracking.
  Acceptance: Widget renders in Header Row 2. Click toggles popup. Click on a non-default row fires `SetDefaultGameCommand` and closes the popup (`StaysOpen=False`). Esc closes the popup. `Manage games…` footer fires `OpenSettingsCommand`. Empty state: with zero saved games, widget shows `"No saved games yet"` and clicking opens Games settings sheet directly. Compact mode collapses the widget so the toolbar reflows.
  Verify: Manual smoke walking each path with three saved games + then with zero. Compact mode toggle. Side-by-side visual check against the existing `Games` button + `Launch multiple` CTA — sizing should sit between them. Commit: `feat(app): DefaultGameWidget in MainWindow Header Row 2`.

- [ ] **7. Right-click context menus on five trigger surfaces**
  Spec ref: `spec.md > §5.6 Right-click context menus`, `§9 Decisions log #4 (right-click vs pencil-on-hover)`
  What to build: Five `ContextMenu` definitions on the existing XAML elements per spec §5.6 table. Each `MenuItem` binds to `MainViewModel.RenameItemCommand` (or `ResetItemNameCommand` / `SetDefaultGameCommand` / existing `RemoveCommand`) with `CommandParameter` shaped to the surface's `RenameTarget`.
  - **Default-game widget dropdown ListBox row** (added in item 6): items `Set as default` / `Rename…` / `Reset name` / `Remove`. `RenameTarget {Kind=Game, Id=placeId, OriginalName=Name, CurrentLocalName=LocalName}`.
  - **Per-row game ComboBox dropdown item** (existing element in `MainWindow.xaml`): items `Rename…` / `Reset name`.
  - **Games settings sheet ListBox row** (existing element in the Games settings sheet): items `Set as default` / `Rename…` / `Reset name` / `Remove`.
  - **Squad Launch sheet saved-server row** (existing element in `SquadLaunchSheet.xaml`): items `Rename…` / `Reset name` / `Remove` (existing button stays). `RenameTarget {Kind=PrivateServer, Id=Guid, OriginalName=Name, CurrentLocalName=LocalName}`.
  - **Account row Border (main + compact)** (existing elements in `MainWindow.xaml`): items `Rename…` / `Reset name`. `RenameTarget {Kind=Account, Id=Guid, OriginalName=DisplayName, CurrentLocalName=LocalName}`. Other row buttons (Launch, Re-authenticate, Remove) remain as buttons, not context-menu items.
  `Reset name` `IsEnabled` binding: `LocalName != null` (use a `NullToBooleanConverter` or equivalent existing converter pattern). `RenameTarget` is built per click — use a small XAML-side `MultiBinding` or, more practically, a per-surface `OneTime` bindable wrapper exposed by the row's data context (e.g., `AccountSummary.RenameTarget`, `GameSummary.RenameTarget`) so the XAML stays clean.
  Acceptance: Right-click on each of the five surfaces opens the documented menu. `Rename…` opens `RenameWindow`. `Reset name` is greyed out when `LocalName == null`, enabled otherwise. `Set as default` (game surfaces) and `Remove` (where present) continue to work as before. Keyboard accessibility: Menu key (or Shift+F10) opens the same context menus.
  Verify: Manual smoke right-clicking each surface against a test data set with a mix of named and unnamed entries. Confirm `Reset name` enable/disable state changes immediately after a Save/Reset. Commit: `feat(app): right-click rename context menus on 5 surfaces`.

- [ ] **8. Render-surface coverage pass — `LocalName ?? Name`/`DisplayName` across 12 surfaces**
  Spec ref: `spec.md > §7 Where renames render (read-side surfaces)`, `prd.md > Story 2.3`
  What to build: Switch every read-side display from `Name`/`DisplayName` to `LocalName ?? Name`/`LocalName ?? DisplayName` across all 12 surfaces enumerated in spec §7. The mechanism is either:
  - **A new computed property** on the relevant ViewModel (`AccountSummary.RenderName`, `GameSummary.RenderName`, `SavedPrivateServerSummary.RenderName`) that returns `LocalName ?? Name`/`DisplayName`. Re-bind XAML to the new property. INPC raised on the computed property when either underlying field changes.
  - **OR a `FallbackConverter`** (XAML resource) consuming both bindings — slightly cleaner XAML, slightly noisier ViewModel. Either is fine; pick one and apply consistently across all 12 surfaces.
  The 12 surfaces (per spec §7):
  1. Default-game widget readout (`MainViewModel.DefaultGameDisplay` — already wired in item 5)
  2. Default-game widget dropdown rows (added in item 6 — verify binding goes through `RenderName`)
  3. Per-row game ComboBox display (`AccountSummary.SelectedGame` rendering)
  4. Per-row ComboBox dropdown items
  5. Games settings sheet rows
  6. Squad Launch sheet rows
  7. Account row primary label
  8. Account row MAIN-pill row (via `DisplayName` binding)
  9. Follow-strip chips (rendered when v1.2 follow-feature unmask lands — per memory `project_rororo_follow_masked_v1.2`; bindings should be in place even though the surface is currently `Visibility=Collapsed`)
  10. Compact-mode account row
  11. Tray menu `Start [Account]` label (`MainAccount.DisplayName` rendering — bind via `RenderName`)
  12. Session History entries (display-time read of original record)
  Acceptance: Every surface renders `LocalName` when set, the original `Name`/`DisplayName` when not. After a rename via `RenameWindow`, all surfaces re-render via INPC without manual refresh. After a reset, all surfaces revert. After a Roblox-side refresh (`IRobloxApi.GetUserProfileAsync` mock), `LocalName` is preserved (regression-guarded by item 5's tests, but eyes-on confirmation here).
  Verify: Manual smoke checklist per spec §12: rename a saved game; verify in widget readout, widget dropdown, per-row ComboBox, ComboBox dropdown, Games settings sheet. Rename a saved private server; verify in Squad Launch sheet. Rename an account; verify in account row, compact row, MAIN pill area, tray `Start [Account]` label (un-collapse the follow strip temporarily to verify the chip; then re-collapse). Reset name on each kind; verify revert. Empty/whitespace rename; verify reset behavior. Commit: `feat(app): LocalName ?? Name/DisplayName rendering across 12 surfaces`.

- [ ] **CHECKPOINT 2** (after item 8 — full UI coverage complete; docs + release notes start next)
  Manual review: walk every spec §12 manual smoke scenario. Confirm:
  - All 12 render surfaces respect `LocalName ?? …` (rename → see it everywhere; reset → see the original everywhere)
  - All 5 right-click surfaces present the right context menu items with correct enable/disable on `Reset name`
  - `RenameWindow` chrome matches the existing `ui:FluentWindow` modals side-by-side (no programmatic-placeholder vibe)
  - Default-game widget renders, click-to-pick works, empty state handles zero-games gracefully, compact mode collapses cleanly
  - Roblox-side refresh on a renamed account/game does NOT clobber the `LocalName` (force a `GetUserProfileAsync` via the existing refresh button if one exists; otherwise restart the app — refresh fires on startup)
  If anything is shaky, fix before docs. Item 9 is shipping-shaped; problems caught here are far cheaper than problems caught after release notes are drafted.

- [ ] **9. Documentation & Security Verification**
  Spec ref: `spec.md > §11 Out of scope > Release notes`, `§12 Testing`, `prd.md > Explicit cuts`, canonical spec §11 (Decisions log)
  What to build:
  **Documentation**
  - `docs/store/release-notes-1.3.0.0.md` — drafted in the voice template at [`docs/store/release-notes-1.2.0.0.md`](store/release-notes-1.2.0.0.md): short headers, "killer use case" framing, plain-Windows-user language. Lead with the rename overlay (the bigger feature for the Pet Sim 99 audience — long game/server names are a real friction). Default-game widget second.
  - `README.md` — "What's new in v1.3.x" subsection added near the top (one paragraph + a line or two each for the two features). Update the screenshot of the main window to show the new widget if the README has one.
  - `docs/scope.md`, `docs/spec.md`, `docs/prd.md`, `docs/checklist.md` — confirm each reflects what shipped vs what was originally proposed. **Banner-correct (per pattern v from Vibe Thesis) any sections that drifted; do NOT rewrite top-to-bottom.**
  - **Canonical design spec** at `docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md` — banner-correct any drift discovered during build (e.g., if the `RenameTarget.Id` ended up typed as `string` instead of `object` for some reason, name it at the top of the doc).
  - Mac sibling repo (`https://github.com/estevanhernandez-stack-ed/rororo-mac`) — DO NOT modify in this cycle. The forward-looking note in spec §11 captures the port intent.
  **Security verification (every release)**
  - **Secrets scan:** `git ls-files | xargs grep -lE "ROBLOSECURITY=|-----BEGIN|MIIB"` (zero hits expected). Confirm `.gitignore` still covers `*.pfx`, `webview2-data/`, `accounts.dat`, `last-update-check.txt`, `spike/`, `bin/`, `obj/`, `*.user`.
  - **Dependency audit:** `dotnet list package --vulnerable --include-transitive` on every project. Categorize findings as **actionable** (bumpable now) vs **documented-and-mitigated** (no patch available; mitigations in place; monitor) per pattern (ll) from the wbp-azure cycle. Append findings to `docs/security-audit-2026-05-04.md` (the cycle #1 audit doc) under a new `## v1.3.x re-scan (2026-05-XX)` section rather than spawning a new file.
  - **Local-path audit (per pattern kk from wbp-azure):** `git ls-files | xargs grep -lE "c:\\\\Users\\\\|C:/Users/"` should return only this docs file, `process-notes.md`, `PROVENANCE.txt`, and the cycle #1 security audit. Any new hit in source/XAML breaks CI on every machine that isn't the dev's — fix before push.
  - **Input validation re-audit:** confirm no new `Process.Start` call sites were introduced. Rename text flows through `IFavoriteGameStore.UpdateLocalNameAsync` / `IPrivateServerStore.UpdateLocalNameAsync` / `IAccountStore.UpdateLocalNameAsync` only — string parameter, no shell-out, no SQL.
  - **DPAPI envelope re-verify:** spot-check `accounts.dat` after a rename roundtrip on a real user account. Header bytes still `01 00 00 00 D0 8C 9D DF`; decrypt path still works after the schema change.
  - **MSIX unchanged:** v1.3.x adds zero new capabilities to `Package.appxmanifest`. No new declarations. Confirm the manifest diff vs the v1.2.0.0 ship is property changes only (version bump + maybe asset paths if the design skill added a refreshed splash).
  Acceptance: Release notes draft renders cleanly + reads with the cycle #1 voice. Secret scan + local-path audit both come back clean. Vulnerability scan output is appended to the cycle #1 audit doc with the new section dated. All spec §12 manual smoke scenarios pass on a clean Win11 VM. Banner-corrects on drifted spec sections (if any) clearly identify what was originally proposed vs what was actually built.
  Verify: Run the three grep commands above. Run `dotnet list package --vulnerable --include-transitive`. Walk the spec §12 manual smoke checklist on a clean Win11 VM end to end. Inspect `accounts.dat` header bytes. Compare `Package.appxmanifest` against v1.2.0.0's. Commit: `docs+security: v1.3.x release notes + audit + drift banners`.

---

## ✓/△ Embedded feedback

✓ **Sequencing:** Schema → stores → app DTO → window → ViewModel plumbing → widget XAML → context menus → render coverage. Each item depends only on items strictly before it. UI items (6, 7, 8) come last among build items so seams are real, not stubs. Item 4 (`RenameWindow`) is positioned BEFORE item 5 (ViewModel) deliberately — `RenameWindow.ShowAsync` is called from item 5's `RenameItemCommand`, so the window must exist first.

✓ **Granularity:** 9 items (cycle #1 ran 12; v1.3.x has smaller surface area — no auth/distribution/DPAPI bucket — so 9 lands inside the 8-12 target band). Each item is completable in one /build session. Two checkpoints (after items 2 and 8) catch problems before they cascade — primitives before consumers, full UI before docs.

✓ **Spec coverage:** Every numbered spec section (1-13) maps to at least one checklist item. Spec §5 components distribute across items 1, 2, 3, 4, 5, 6. Spec §6 data flows distribute into items 5, 6. Spec §7 render surfaces concentrate in item 8. Spec §8 edge cases distribute across items 1, 2, 5, 9. Spec §12 testing concentrates in items 1, 2, 5 (unit) + 4, 6, 7, 8 (manual). PRD stories 1.1-1.4 + 2.1-2.6 are all covered.

△ **Quality bar (chrome):** Item 4 (`RenameWindow`) MUST visually match the existing `ui:FluentWindow` modals side-by-side. The "won't ship a broken-looking tile even if the rest works" bar (pattern x from SnipSnap retro, reinforced by builder-profile.md cycle #2 carryover) applies to every modal, not just Store assets. Verify line in item 4 calls this out explicitly — if the Verify step turns up "looks placeholder-y," halt + fix before item 5 starts.

△ **Risk point (re-add preservation):** Item 2's re-add preservation logic touches existing `AddAsync` replace paths — the dominant regression risk in this cycle. The unit tests in item 2's Acceptance cover the happy path; the regression risk is that some other call site sets up the new record and forgets to thread `LocalName: existing.LocalName`. Audit `IFavoriteGameStore.AddAsync` + `IPrivateServerStore.AddAsync` implementations carefully; trace every place a record is constructed via `with` expression or new-up.

△ **Scope watch (item 8):** Render-surface coverage across 12 surfaces is the largest single item by file-count. If item 8 slips past 90 minutes flag for split into 8a (game/server surfaces — items 1-6 from §7) and 8b (account surfaces — items 7-12 from §7). The follow-strip chip surface (item 9 in spec §7) is currently `Visibility=Collapsed` — its binding still needs to be wired so the un-mask in a later cycle inherits the rename support for free; add a single-line code comment noting the masked state.

△ **What's deliberately not in this checklist:** No spike. No new dependencies. No Roblox-side contract changes. No new error buckets — the existing `KeyNotFoundException` + `IOException` patterns from v1.1's atomic-write contract carry the new methods. No MSIX manifest changes. If any of these turn up during build, halt and update the spec before proceeding (banner-correct, don't quietly add scope).
