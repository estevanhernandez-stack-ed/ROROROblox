# ROROROblox — save-pasted-links Build Checklist

**Cycle:** v1.3.x save-pasted-links (follow-on to default-game-widget + rename, which shipped 2026-05-08 via PR #3)
**Cycle type:** Spec-first cycle (pattern mm). Substantive design at [`docs/superpowers/specs/2026-05-08-save-pasted-links-design.md`](superpowers/specs/2026-05-08-save-pasted-links-design.md). [`spec.md`](spec.md) is a pointer-stub to that canonical doc.
**Build mode:** autonomous-with-verification (one checkpoint after item 2 — primitives + wiring complete; verify before doc/security pass)
**Comprehension checks:** off
**Git cadence:** commit after each item
**Branch:** `feat/save-pasted-links` (cut from `main` 2026-05-08 after spec PR #2 merged — already current)
**Repo:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)

**Effort estimates:** wall-clock guesses for autonomous mode. Total cycle ≈ 90–120 minutes including checkpoint.

---

- [ ] **1. JoinByLinkWindow — `Save to my library` checkbox + `SaveToLibrary` property**
  Spec ref: `spec.md > §4.1 JoinByLinkWindow.xaml`, `§4.2 JoinByLinkWindow.xaml.cs`
  Effort: ~15 min
  Dependencies: none (independent UI primitive — no Core changes, no MainViewModel reads yet)
  What to build: Add a `<CheckBox x:Name="SaveCheckBox">` row to `src/ROROROblox.App/JoinByLink/JoinByLinkWindow.xaml`, immediately above the action-button `StackPanel`. Default unchecked. Brand-token styling consistent with the rest of the dialog (`Foreground="{DynamicResource WhiteBrush}"`). Add an `Auto` row to `Grid.RowDefinitions` and shift the existing `StatusText` (currently `Grid.Row="3"`) to `Grid.Row="4"` and the action-button `StackPanel` (currently `Grid.Row="4"`) to `Grid.Row="5"`. In code-behind, expose `public bool SaveToLibrary => SaveCheckBox.IsChecked == true;` alongside the existing `SelectedTarget` property. No other code-behind changes — checkbox is independent of URL parsing and target resolution.
  ```xml
  <CheckBox Grid.Row="3" x:Name="SaveCheckBox"
            Content="Save to my library"
            Foreground="{DynamicResource WhiteBrush}"
            Margin="0,12,0,0"
            ToolTip="Add this game or private server to your saved library so it shows up next time without retyping." />
  ```
  Acceptance: Dialog renders with the new checkbox row visible above the action buttons, vertically aligned with the existing rhythm (12px top margin matches the spacing between `PreviewBorder` and `StatusText`). `SaveCheckBox` is unchecked on every open (no two-way binding, no persistence). Resizing the window doesn't crash (the `RowDefinitions` shift is well-formed). `SaveToLibrary` returns `false` when unchecked, `true` when checked.
  Verify: `dotnet build` clean. Manual smoke — open the Join-by-link dialog from any per-account row's `(Paste a link...)` entry, confirm the checkbox renders with the existing dialog's brand-token styling, toggling it doesn't affect the existing parse/preview logic. Side-by-side compare with the existing dialog (pre-checkbox) to confirm no regressions in the StackPanel / status text rhythm. Commit: `feat(app): JoinByLinkWindow — Save to my library checkbox`.

- [ ] **2. MainViewModel.SavePastedTargetAsync — TDD wire-up + 7-case test suite**
  Spec ref: `spec.md > §4.3 MainViewModel`, `§5 Save semantics + naming`, `§6 Re-add idempotence`, `§7 Error handling`, `§8.1 Unit tests`
  Effort: ~60–90 min
  Dependencies: item 1 (needs `SaveToLibrary` property exposed on the dialog)
  What to build: Test-driven implementation — write the 7 unit tests first, watch them fail, implement to green.
  - **New test file:** `src/ROROROblox.Tests/JoinByLinkSaveTests.cs`. Targets the `MainViewModel` save-on-paste branch via the same in-memory store pattern existing tests use (see `RenameDispatchTests.cs:43-46`). 7 cases:
    1. `Place + SaveToLibrary=true` → `_favoriteGameStore.AddAsync` called once with `(meta.PlaceId, meta.UniverseId, meta.Name, meta.IconUrl)` matching the metadata fetch result. `_privateServerStore.AddAsync` not called. `ReloadGamesAsync` called.
    2. `PrivateServer + SaveToLibrary=true` → `_privateServerStore.AddAsync` called once with `(placeId, code, kind, placeName, placeName, thumbnail)` — name and placeName both default to the metadata-fetched place name. `_favoriteGameStore.AddAsync` not called.
    3. `Place + SaveToLibrary=false` → neither store's `AddAsync` called.
    4. `PrivateServer + SaveToLibrary=false` → neither store's `AddAsync` called.
    5. `_favoriteGameStore.AddAsync` throws → launch dispatch still occurs, warning logged. Same case for `_privateServerStore.AddAsync` (test both stores).
    6. `_api.GetGameMetadataByPlaceIdAsync` returns `null` → save skipped (no `AddAsync` called), warning logged, launch dispatch still occurs.
    7. `_api.GetGameMetadataByPlaceIdAsync` throws despite the null-returning contract → caught, warning logged, save skipped, launch dispatch still occurs (defensive).
  - **Implementation:** Modify `src/ROROROblox.App/ViewModels/MainViewModel.cs`. In the existing `OpenJoinByLinkAsync` flow, after `dialog.ShowDialog()` returns `true` and `dialog.SelectedTarget` is non-null, but **before** dispatching the launch, call `SavePastedTargetAsync(dialog.SelectedTarget)` inside a `try { } catch (Exception ex) { _log.LogWarning(...) }`. Wrap is mandatory — save failure must NEVER block the launch (mirrors `MainViewModel.cs:613` avatar-fetch soft-fail).
  - **New private method:** `SavePastedTargetAsync(LaunchTarget target)` switches on `Place` vs `PrivateServer`. For each branch: fetch metadata via `_api.GetGameMetadataByPlaceIdAsync(placeId)`; null-check + early return + warning log; `AddAsync` to the appropriate store; for `Place`, call `ReloadGamesAsync()` so `AvailableGames` and `WidgetGames` pick up the new entry immediately. For `PrivateServer`, no main-window reload needed (Library sheet lists from `IPrivateServerStore.ListAsync()` directly on next open). `default:` arm exists but is a no-op — only `Place` and `PrivateServer` are save-eligible from this surface.
  - **Soft-fail discipline:** Every awaited call inside `SavePastedTargetAsync` must not bubble. Either the inner `try { } catch` in `SavePastedTargetAsync` itself (preferred — keeps the outer catch a single safety net) or the outer try in `OpenJoinByLinkAsync` (acceptable). Both null-return AND exception paths must result in: launch dispatch continues, exactly one `_log.LogWarning` entry written, no UI surface.
  Acceptance: All 7 new tests pass. Existing tests (61+ pre-cycle, plus cycle-2 additions) still green — `dotnet test` clean. Manual fault-injection: temporarily throw inside `_api.GetGameMetadataByPlaceIdAsync` (or air-gap the network), paste a valid public URL with checkbox checked, click Launch — launch is **attempted** (will fail downstream because no network, but the save-fail itself does not surface). Log file shows one warning per save attempt, not multiple.
  Verify: `dotnet test --filter JoinByLinkSaveTests`. `dotnet test` (full suite). Manual smoke per spec §8.2 step 8 (airplane-mode fault injection). Commit: `feat(app): MainViewModel — wire save-on-paste for Place + PrivateServer + 7-case test suite`.

- [ ] **CHECKPOINT 1** (after item 2 — both save paths working in isolation; manual smoke confirms before doc/security pass)
  Manual review on a clean working tree (stash any uncommitted CookieCapture mods first):
  - Open RORORO with at least one signed-in account.
  - Click any per-account row's Game dropdown → `(Paste a link...)`.
  - **Path A — public game:** paste `https://www.roblox.com/games/920587237` (or any public place URL). Check `Save to my library`. Click Launch. Verify Roblox launches AND the pasted game appears in the per-account Game dropdown + default-game widget after dialog close.
  - **Path B — private server:** paste a private server share URL. Check `Save to my library`. Click Launch. Verify launch AND the pasted server appears in the Library sheet's saved-private-servers section.
  - **Path C — opt-out works:** repeat A with checkbox UNCHECKED. Game launches but is **not** in any list.
  - **Path D — re-paste preserves rename:** rename a saved game via the existing context menu, then re-paste the same URL with checkbox checked. Custom name preserved (cycle-2 re-add guarantee).
  - If any path is shaky, fix before proceeding to item 3 — Documentation & Security Verification assumes the wire-up is bulletproof.

- [ ] **3. Documentation & Security Verification**
  Spec ref: Cart-required final + `spec.md > §11 Decisions to log on completion`
  Effort: ~15–20 min
  Dependencies: item 2 + checkpoint 1
  What to build (verification + documentation, no production code):
  - **README touch-up:** if any user-visible flow text mentions Join-by-link, add a one-line note about the optional save. If the README doesn't surface this flow at all, no update needed (don't invent a new section). Same call for any user-facing release notes — only update if save-from-paste deserves a line in the next release-notes cut.
  - **Spec banner-correct (only on drift):** if the implementation diverged from the canonical spec at `docs/superpowers/specs/2026-05-08-save-pasted-links-design.md`, add a top-of-doc warning block per pattern v from Vibe Thesis. Name what was originally proposed vs what was actually built. **Do NOT rewrite the spec top-to-bottom — that destroys /reflect-time framing.** If the implementation matched the spec, skip this entirely.
  - **Secrets scan:** `git diff main...HEAD` reviewed for `.ROBLOSECURITY` literals, PFX/cert bytes, or any cookie-shaped string. The existing pre-commit hook ran clean on item 1's commit (visible in commit log: `[secret-scan] clean`); confirm it still runs clean on item 2's commit. Manual eyeball at the diff to belt-and-suspenders the hook.
  - **Local-path audit:** `git diff main...HEAD` reviewed for `c:\Users\` references in committable code (per pattern kk from wbp-azure). Pre-commit hook covers this; manual confirm anyway.
  - **Dependency audit:** `git diff main...HEAD -- '*.csproj' 'Directory.Packages.props' '*.sln'` should show **zero changes** — this cycle adds zero new dependencies (spec §3 explicit). Any non-zero diff is a red flag; investigate before proceeding.
  - **Deployment-security check:** DPAPI envelope shape unchanged — the cycle didn't touch `accounts.dat` or any DPAPI-protected file. Confirm by inspecting `IAccountStore` calls in the diff (should be zero) and verifying no test-file fixtures contain `01 00 00 00 D0 8C 9D DF` byte changes.
  - **Decisions log to 626 Labs Dashboard** via `mcp__626Labs__manage_decisions log`, three entries per spec §11:
    1. Architectural choice — "Save-on-paste lives in MainViewModel.SavePastedTargetAsync, not in JoinByLinkWindow itself. Reason: dialog stays a dumb intent-collector; persistence side-effects belong to the VM that already owns store references and reload coordination." Tag with bound RORORO project ID.
    2. UX choice — "Save default OFF, no cross-session persistence. Reason: avoids 'I forgot I checked it' library pollution, matches user-stated default."
    3. Asymmetry — "Games window saves always; JoinByLink saves opt-in. Two surfaces, two purposes."
  - **Final test suite pass:** `dotnet test` — full suite, all green. Tests added in item 2 + every existing test from cycles 1-2.
  Acceptance: `dotnet test` 100% green. `git diff main...HEAD --stat` shows expected files only (`JoinByLinkWindow.xaml`, `JoinByLinkWindow.xaml.cs`, `MainViewModel.cs`, `JoinByLinkSaveTests.cs`, optional README touch). Pre-commit hooks (`secret-scan`, `local-path-guard`) ran clean across all cycle commits. Three decisions logged to the dashboard. No new dependencies in any `.csproj`. README updated only if user-visible copy genuinely required a new line.
  Verify: `dotnet test`. `git diff --stat main...HEAD`. `git log main..HEAD --oneline` reviewed for shape (item 1 + item 2 commits visible). Browse the dashboard to confirm three new decisions tagged with the RORORO project ID. Commit (only if README/release-notes changed): `docs: README touch-up for save-on-paste`.

---

## Risk callouts logged for `/build`

- **Item 2 is the bulk of the cycle.** TDD discipline matters here — write the 7 tests first, watch them fail with the right kind of failure (`AddAsync was never called` vs `wrong args`), then implement. Skipping the watch-them-fail step is a known TDD anti-pattern that hides false greens.
- **Soft-fail discipline is non-negotiable.** Every `await` inside `SavePastedTargetAsync` must not bubble to the outer launch flow. The "save fail blocks launch" regression class is exactly the kind of ergonomic-debt-disguised-as-correctness pattern the spec deliberately rules out (§7). The fault-injection tests (cases 5, 6, 7) are the regression guard — don't skip them.
- **Working tree has uncommitted CookieCapture mods.** Not from this cycle. Stash with a clear name (`git stash push -m "wip-cookie-capture-pre-save-pasted-links"`) before starting item 1 so they don't ride along into the cycle's commits. Pop after the cycle completes.
- **Reload coordination is the subtle bit.** `ReloadGamesAsync()` mutates `AvailableGames` + `WidgetGames` collections. If the launch dispatch reads from those collections (e.g., for the "currently selected game" lookup), order matters. Spec §4.3 says save runs **before** launch dispatch — verify the launch path doesn't read mutated state mid-dispatch. If it does, swap order: launch first, save after dialog has fully closed.
- **The `IRobloxApi.GetGameMetadataByPlaceIdAsync` re-fetch is the optimization opportunity flagged in spec §4.3.** If `OpenJoinByLinkAsync` (or its callees) already resolves metadata as part of the launch path, threading it through to `SavePastedTargetAsync` avoids a redundant network round-trip. This is a /build-time judgment — if the launch path keeps the metadata in a local variable, plumb it; otherwise the fresh fetch is fine (the call is idempotent + cookie-free).

## Spec banner-correct triggers

If any of these happen during build, banner-correct the canonical spec at `docs/superpowers/specs/2026-05-08-save-pasted-links-design.md` per pattern v from Vibe Thesis:

- The grid row-shift in item 1 turns out to be more invasive than the spec describes (e.g., other XAML elements depend on row indices that need adjustment).
- `SavePastedTargetAsync` ends up living somewhere other than `MainViewModel` (e.g., extracted to a service for testability).
- A test case from spec §8.1 turns out to be redundant or impossible to express (e.g., the test framework can't fault-inject `null` returns from a mocked async method without ceremony).
- Any of the soft-fail patterns end up needing an outer `try { } catch` in `OpenJoinByLinkAsync` instead of (or in addition to) the inner one in `SavePastedTargetAsync`.

If none of these happen — implementation matched spec — skip the banner-correct entirely. Banner-corrects are for genuine drift, not ceremony.
