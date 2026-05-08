# ROROROblox v1.3.x — Technical Spec (pointer stub)

Spec-first Cart cycle. Canonical spec lives upstream:

→ [docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md)

Cycle history (each cycle's canonical spec is its own durable artifact):

- v1.1 core (multi-instance + accounts + distribution): [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md)
- v1.2 per-account FPS limiter: [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md)
- v1.3.x default-game widget + local rename overlay: this cycle

## Section index (for checklist references)

- §1 Overview
- §2 Goals and non-goals (v1.3.x scope + explicit cuts)
- §3 Stack (no new dependencies — `System.Text.Json`, WPF `ContextMenu`, WPF-UI `FluentWindow`, INPC; all already in solution)
- §4 Architecture (MainViewModel + DefaultGameWidget XAML + RenameWindow + per-store `UpdateLocalNameAsync` + new `IFavoriteGameStore.DefaultChanged` event)
- §5 Components & interfaces
  - §5.1 Data model additions — `LocalName: string?` on `FavoriteGame`, `SavedPrivateServer`, `Account`
  - §5.2 Store interface additions — `UpdateLocalNameAsync` on each + `DefaultChanged` event on `IFavoriteGameStore`
  - §5.3 `RenameTarget` DTO + `RenameTargetKind` enum (App project)
  - §5.4 `DefaultGameWidget` (inline XAML in `MainWindow.xaml` Header Row 2)
  - §5.5 `RenameWindow` (`ui:FluentWindow`, ~360×180, owner-modal, shared across all three entity kinds)
  - §5.6 Right-click context menus (5 trigger surfaces — widget dropdown, per-row game ComboBox, Games settings sheet, Squad Launch sheet, account row main + compact)
- §6 Data flows
  - §6.1 Quick-switch the default game (widget click → `SetDefaultAsync` → `DefaultChanged` → INPC)
  - §6.2 Rename a saved game (right-click → `RenameWindow` → `UpdateLocalNameAsync` → re-render)
  - §6.3 Reset a name (`Reset to original` link or empty Save → `UpdateLocalNameAsync(id, null)`)
- §7 Where renames render (read-side surfaces) — 12 surfaces enumerated, `LocalName ?? Name` (or `LocalName ?? DisplayName` for accounts)
- §8 Edge cases & error handling (empty input, long input, duplicate-string, Roblox-side refresh decoupling, re-add semantics, `KeyNotFoundException`, atomic-write `IOException`)
- §9 Decisions log (6 trade calls — `LocalName`-on-record vs separate store, `LocalName` on `Account` not `AccountSummary`, refresh decoupling, right-click vs pencil-on-hover, fresh-branch spec home, quick-switch vs readout-only)
- §10 Known UX concern — deferred (saved private servers behind Squad Launch button — discoverability)
- §11 Out of scope (forward-looking) — Mac parity port, combined Games + Servers sheet, tray-menu account list, bulk rename, sync, release notes
- §12 Testing (unit-test ACs per store + per ViewModel, manual smoke checklist additions for the rename surfaces)
- §13 References (canonical v1.1 spec, v1.2 sibling, bot-challenge investigation, Mac sibling repo, brand tokens, target XAML)

## What's deliberately not in this cycle

The v1.1 spec (multi-instance, AccountStore, RobloxLauncher, RobloxApi, MutexHolder, CookieCapture, distribution) is locked and load-bearing — none of those interfaces gain or lose surface area in v1.3.x. The only mutations to existing types are:

- 3× nullable property additions on existing records (`LocalName: string?`)
- 3× new method on existing store interfaces (`UpdateLocalNameAsync`)
- 1× new event on `IFavoriteGameStore` (`DefaultChanged`)
- 0× changes to `IRobloxApi`, `IRobloxLauncher`, `IMutexHolder`, `ICookieCapture`, `App`/`AppLifecycle`, MSIX/Velopack distribution shape

When build reality drifts from the canonical spec — banner-correct at the top of the canonical spec doc per pattern v from Vibe Thesis (per `CLAUDE.md` "Don't rewrite the canonical spec on drift" rule).
