# ROROROblox v1.3.x — PRD (compressed)

Stories + acceptance criteria + prioritization. For full design rationale see [`docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md).

This is a Spec-first cycle (pattern mm) — the heavy thinking happened in the upstream design spec. This PRD compresses to stories that map cleanly to checklist items. Prior cycles' PRDs are preserved in their respective specs: [`v1.1`](superpowers/specs/2026-05-03-rororoblox-design.md) and [`v1.2`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md).

## Epic 1 — Default-game widget (v1.3.x must-ship)

### Story 1.1 — Widget readout in toolbar
**As a** Roblox player with several saved games **I want** the current default game visible at a glance in the toolbar **so that** I don't have to open Settings to remember which game `Launch multiple` is going to start.
- AC: `MainWindow` Header Row 2 shows a `ToggleButton` between the `Games` button and the `Launch multiple` CTA, sized `MinWidth=200 / MaxWidth=340`.
- AC: Readout displays `LocalName ?? Name` of the current default game (icon + `DEFAULT` micro-label + game name + chevron).
- AC: Widget reads from `MainViewModel.DefaultGameDisplay` (INPC-backed), which itself reads `IFavoriteGameStore`. No new external API calls.
- AC: When `IFavoriteGameStore.DefaultChanged` fires, the readout updates without manual refresh.

### Story 1.2 — Pick a different game from the dropdown
**As a** user **I want** to click the widget and pick a saved game from a dropdown **so that** I can swap the default in one click instead of opening the Games settings sheet.
- AC: Click on the widget toggles a `Popup` placed below it; current default is highlighted (`DEFAULT` tag); other rows show `LocalName ?? Name` only.
- AC: Click on a non-default row fires `SetDefaultGameCommand`, calls `IFavoriteGameStore.SetDefaultAsync(placeId)`, mutates `favorites.json` via the existing atomic-write contract, and fires `DefaultChanged`.
- AC: Popup closes automatically (`StaysOpen=False`) once selection is made.
- AC: Keyboard: Esc closes the popup with no change.

### Story 1.3 — Empty-state + Manage games entry
**As a** new user with zero saved games **I want** the widget to tell me I have no defaults and how to add one **so that** I'm not staring at a broken-looking control.
- AC: When `AvailableGames.Count == 0`, the widget shows a muted "No saved games yet" readout and routes its click to `OpenSettingsCommand` (Games settings sheet) instead of opening the popup.
- AC: When at least one game is saved, popup footer button `Manage games…` invokes `OpenSettingsCommand`.

### Story 1.4 — Hidden in compact mode
**As a** user running RORORO in compact mode **I want** the widget to disappear with the rest of the header chrome **so that** my row of accounts doesn't get squeezed.
- AC: The widget binds to the same `IsCompact` trigger pattern the surrounding header already uses; it collapses (not hides) so the toolbar reflows cleanly.

## Epic 2 — Local rename overlay (v1.3.x must-ship)

### Story 2.1 — Right-click → Rename… on every relevant surface
**As a** user **I want** a right-click `Rename…` option everywhere I see a saved game, saved private server, or account **so that** I can replace long Roblox-side names with something I recognize at a glance.
- AC: Right-click context menu appears on five trigger surfaces with the items below. `Reset name` is enabled only when `LocalName != null`.
  - Default-game widget dropdown row: `Set as default · Rename… · Reset name · Remove`
  - Per-row game ComboBox dropdown item: `Rename… · Reset name`
  - Games settings sheet row: `Set as default · Rename… · Reset name · Remove`
  - Squad Launch sheet (saved private server row): `Rename… · Reset name · Remove` (existing button stays)
  - Account row (main + compact): `Rename… · Reset name`
- AC: `Rename…` fires `RenameItemCommand` with a `RenameTarget {Kind, Id, OriginalName, CurrentLocalName}` shaped to the surface.
- AC: `Reset name` calls the right `UpdateLocalNameAsync(id, null)` directly (no popup).

### Story 2.2 — RenameWindow save / cancel / reset
**As a** user **I want** a small popup that shows the original Roblox name and lets me type a local one **so that** I always know what the underlying name is and can revert.
- AC: `RenameWindow` is a `ui:FluentWindow` (~360×180, non-resizable, owner-modal) that matches existing modal chrome — quality bar from cycle #1 applies (must not look like a programmatic placeholder).
- AC: Title `Rename`. Mono-micro line `ROBLOX NAME — {OriginalName}` is always visible. TextBox pre-filled with `CurrentLocalName ?? OriginalName`.
- AC: `Reset to original` hyperlink visible only when `CurrentLocalName != null`; clicking it calls `UpdateLocalNameAsync(id, null)` and closes the window.
- AC: Save trims input; empty/whitespace normalizes to `null` (effective reset, no error toast).
- AC: Enter = Save default; Esc = Cancel. Cancel makes no store call.
- AC: One window class is shared across all three entity kinds; `MainViewModel` switches on `RenameTarget.Kind` to dispatch to the right store.

### Story 2.3 — `LocalName ?? Name` rendering across all surfaces
**As a** user who renamed something **I want** my local name to show everywhere the original used to **so that** I'm not playing find-the-rename across surfaces.
- AC: All 12 surfaces from spec §7 render via `LocalName ?? Name` (or `LocalName ?? DisplayName` for accounts): default-game widget readout, widget dropdown rows, per-row game ComboBox display, ComboBox dropdown items, Games settings sheet rows, Squad Launch sheet rows, account row primary label, account MAIN-pill row, follow-strip chips (rendered when v1.2 follow-feature unmask lands — see memory `project_rororo_follow_masked_v1.2`), compact-mode account row, tray menu `Start [Account]` label, Session History entries.
- AC: After rename, `MainViewModel` re-reads the relevant collection and raises INPC; no manual surface-by-surface refresh required.
- AC: After reset, the same surfaces revert to the Roblox-side original.

### Story 2.4 — Forward/backward-compatible JSON storage
**As a** user **I want** my renames to survive across upgrades and downgrades **so that** I don't lose my work when I update RORORO or roll back a flaky release.
- AC: `FavoriteGame`, `SavedPrivateServer`, and `Account` each gain a `LocalName: string?` property defaulting to `null`.
- AC: Loading a legacy `favorites.json` / `private-servers.json` / `accounts.dat` written by an older RORORO version deserializes cleanly with `LocalName = null` (System.Text.Json default-fill behavior).
- AC: An older RORORO version reading a file written by v1.3.x ignores the unknown `LocalName` property and roundtrips it cleanly on next write — no silent strip.
- AC: Each store gains `UpdateLocalNameAsync(id, string? localName)` with the same atomic-write contract as existing single-property updates. Calling with a non-existent ID throws `KeyNotFoundException` (matches `SetDefaultAsync` shape).
- AC: `IFavoriteGameStore` exposes a new `event EventHandler? DefaultChanged` fired after `SetDefaultAsync` completes successfully.

### Story 2.5 — Roblox-side refresh never overwrites a local rename
**As a** user **I want** my rename to survive when Roblox-side name fetches happen in the background **so that** my local override doesn't get clobbered.
- AC: `IRobloxApi.GetUserProfileAsync` updates `Account.DisplayName` and never touches `LocalName`.
- AC: `IRobloxApi.GetUniverseInfo` (and equivalent game-name refreshes) update `FavoriteGame.Name` and never touch `LocalName`.
- AC: Re-adding a saved game with the same `placeId` (existing replace semantics) preserves the prior `LocalName`. Same for re-adding a saved private server with the same `code`.

### Story 2.6 — Edge-case error surfacing
**As a** user **I want** rename errors to be visible but never destructive **so that** I know what happened without losing in-memory state.
- AC: `KeyNotFoundException` on `UpdateLocalNameAsync` (concurrent removal from another surface) → `MainViewModel` catches and sets a quiet status banner; the popup closes; the in-memory list reflects reality.
- AC: `IOException` from atomic-write failure (disk full, permissions) → status banner reads `"Couldn't save name change. Disk error?"`; in-memory state stays correct for the session; user can retry.
- AC: Inputs >50 chars are allowed; UI truncates with ellipsis everywhere it renders.
- AC: Two items renamed to the same string is allowed — IDs disambiguate; this is local display only.

## Prioritization

- **P0 (must ship in v1.3.x):** all Epic 1 + Epic 2 stories.
- **P1 (deferred — see spec §10 + §11):** combined Games + Servers settings sheet (or dedicated `Servers` toolbar button); tray-menu per-account list (would auto-render `LocalName ?? DisplayName`); bulk rename / find-and-replace; auto-shorten heuristics on long Roblox names.
- **Forward-looking (separate cycle, not v1.3.x):** Mac parity port at [`rororo-mac`](https://github.com/estevanhernandez-stack-ed/rororo-mac); shared on-disk JSON shape means the schema ports for free.

## Explicit cuts

- Mac parity in this cycle (Mac is single-URL today; port is its own cycle once Windows ships).
- Bulk rename / find-and-replace.
- Sync overrides between Mac and Windows installations.
- A new "Saved private servers" settings sheet — Squad Launch sheet stays the surface; discoverability concern logged in spec §10 for a future /reflect cycle.
- Tray-menu per-account list (no surface to attach a rename to today).
- Auto-shorten heuristics on long Roblox names. Renames are explicit user choices; UI truncates with ellipsis.
- Pencil-on-hover affordance — right-click is the chosen gesture per spec §9 decision 4. Trade documented; revisitable if user testing flags discoverability.
- WPF visual snapshot tests (consistent with cycle #1 — too brittle for v1.x's small UI; manual smoke checklist in spec §12 covers regression).
