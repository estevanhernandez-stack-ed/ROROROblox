# ROROROblox — Scope (pointer stub)

Spec-first Cart cycle (pattern mm) — substantive scope decisions live in the upstream design spec authored before /onboard:

→ [docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md)

Cycle history: v1.1 scope (multi-instance + saved-account quick-launch) lives in [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md). v1.2 (per-account FPS limiter via `GlobalBasicSettingsWriter`) shipped 2026-05-07; spec at [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md).

## In scope (v1.3.x)

- Default-game quick-switch widget in `MainWindow` Header Row 2 (between `Games` and `Launch multiple`). Reads existing `IFavoriteGameStore`. Click → dropdown → pick → new default. Empty-state link to Games settings sheet. Hidden in compact mode.
- Per-record `LocalName: string?` rename overlay across `FavoriteGame`, `SavedPrivateServer`, and `Account`. Right-click → `Rename…` / `Reset name` from five trigger surfaces (widget dropdown, per-row game ComboBox, Games settings sheet, Squad Launch sheet, account row main + compact).
- `LocalName ?? Name` (or `LocalName ?? DisplayName` for accounts) rendering across every UI surface that shows the original — 12 read-side surfaces enumerated in spec §7.
- Forward/backward-compatible JSON storage on all three on-disk stores (`favorites.json`, `private-servers.json`, `accounts.dat`). Old records load with `LocalName = null`; older RORORO versions ignore the unknown property.
- Single shared `RenameWindow` (`ui:FluentWindow`, ~360×180, owner-modal) reused across all three entity kinds via a `RenameTarget` DTO.

## Out of scope (deferred or never)

- **Mac parity port.** Mac is single-URL today; the widget + rename feature ports to Mac as a separate cycle once Windows ships. The on-disk JSON shape is identical so the port inherits the schema for free.
- Bulk rename / find-and-replace. One item at a time.
- Sync overrides between Mac and Windows installations (no cookie / setting sync at all per v1 design).
- A new "Saved private servers" settings sheet — Squad Launch stays the surface; discoverability concern logged in spec §10 for a future cycle.
- Tray-menu per-account list (no surface to attach a rename to today; if added later it reads `LocalName ?? DisplayName` for free).
- Auto-shorten heuristics on long Roblox names. Renames are explicit user choices; UI truncates with ellipsis.
- Pencil-on-hover affordance — right-click is the chosen gesture (spec §9 decision 4).

## Distribution audience

Carried unchanged from v1.1 — Pet Sim 99 clan first (non-technical Windows users running multi-alt for farming), Microsoft Store second. The rename feature exists specifically because long Roblox game names crowd this audience's UI; the default-game widget exists because they swap defaults more often than Settings-sheet round-trips support comfortably.
