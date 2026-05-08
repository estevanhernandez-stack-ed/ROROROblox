# Default-Game Widget + Local Rename Overlay — Design Spec

**Version:** v1.3.x feature add (post-v1.2 FPS limiter)
**Date:** 2026-05-07
**Status:** Approved for implementation planning
**Branch (spec):** `docs/spec-default-game-widget-and-rename`
**Branch (implementation):** `feat/default-game-widget-and-rename` (cut 2026-05-08)
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

> ⚠️ **Build-time drift — banner-correct (pattern v from Vibe Thesis), 2026-05-08.** Read this before treating any individual section as ground truth. Four pre-build / build-time deviations, each surgical:
>
> 1. **§3 stack — `ui:FluentWindow` is not what shipped.** The spec called for `ui:FluentWindow` chrome on `RenameWindow`. The existing v1.1 modals (`WebView2NotInstalledWindow`, `RobloxNotInstalledWindow`, `DpapiCorruptWindow`) all use plain `Window` with `Background="{DynamicResource BgBrush}"` — not FluentWindow. `RenameWindow` matched the actual shipping pattern instead of the spec letter; chrome consistency is preserved (which was the underlying point of the §3 spec line).
>
> 2. **§5.3 placement — `RenameTarget` lives in Core, not App.** Spec said App project. Moved to Core because (a) it's pure data with zero UI dependencies, and (b) the Tests project doesn't reference App, so testability requires Core placement. `RenameResult` (popup outcome) also lives in Core for the same reason. Net cost: nothing — App still references RenameTarget normally; tests now reach it.
>
> 3. **§5.6 surfaces — 4 of 5 trigger surfaces wired in v1.3.0.0; Games settings sheet deferred.** The four shipping context-menu surfaces are: account row Border, per-row game ComboBox dropdown items, default-game widget dropdown row, Squad Launch sheet saved-server row. The fifth surface (Games settings sheet rows in `SettingsWindow.xaml`) is structurally similar to Squad Launch's code-behind plumbing (own constructor-injected store, no shared MainViewModel data context); deferred because the same set of saved games is renameable via the per-row ComboBox and widget dropdown surfaces, so no user flow is stranded. To wire later: `BuildGameRow` / `RenderListAsync` handlers in `SettingsWindow.xaml.cs` gain the same shape as Squad Launch's `OnRenameSavedServerAsync` / `OnResetSavedServerNameAsync`.
>
> 4. **§7 render surfaces — 8 actually changed, 2 vacuous, 2 deferred.** Of the 12 surfaces enumerated:
>    - **Changed (8):** widget readout, widget dropdown rows, per-row ComboBox display, ComboBox dropdown items, Squad Launch rows, account row primary label, account row MAIN-pill (auto via DisplayName-binding swap), follow-strip chips, compact-mode account row. (Plus the compact-mode Start CTA at MainWindow:1168, which §7 didn't enumerate but follows the same pattern.)
>    - **Vacuous (1):** "Tray menu Start [Account] label" — the tray context menu has no per-account "Start [X]" item in v1.3.0.0; surface anticipated a future tray feature that isn't wired. The Roblox window title (set by `RobloxWindowDecorator`) intentionally stays as `DisplayName` because `RunningRobloxScanner` matches windows by exact title pattern; switching the title to `RenderName` would break running-window attachment until the matcher is also updated (a coordinated change that's its own item).
>    - **Deferred (2):** Games settings sheet rows (paired with §5.6 surface deferral above) and Session History entries (the `row.AccountDisplayName` snapshot vs live-lookup distinction is defensible either way; spec said live-lookup, snapshot is what shipped).
>
> Source of truth for the 4 corrects: commits on `feat/default-game-widget-and-rename` between f454e33 and c9ee778, item 9's `process-notes.md > /build` block, and the per-item commit messages.

## 1. Overview

Two coupled features ship together because they answer the same complaint: "the game names are long, and the default game is buried in Settings."

1. **Default-game widget** — a quick-switch dropdown in the toolbar gap between the `Games` button and the `Launch multiple` CTA. Reads the existing `IFavoriteGameStore`. Click → dropdown of saved games → pick one → that's the new default.
2. **Local rename overlay** — right-click any saved game, saved private server, or account row → `Rename…` → small popup edits a per-record `LocalName: string?`. Roblox-side names stay untouched. Empty input on save normalizes to null (effective reset).

Strategic frame: this is **Mac-banner parity, Windows-tailored.** The Mac banner is full-width because Mac is single-URL (one knob). Windows already has the multi-game library + per-row picker, so the widget is properly secondary chrome — smaller, denser, lives in the toolbar — and the rename overlay is what makes the existing library bearable when game names run long.

The technical core is small:

1. Add `LocalName: string?` to three records: `FavoriteGame`, the saved-private-server record, `Account`. Forward/backward-compatible JSON.
2. UI reads `LocalName ?? Name` everywhere the original showed.
3. New widget binds to `IFavoriteGameStore`; one new ViewModel property + one new command + one new event on the store.
4. Rename popup is a single small `Window` reused across all three entity types.

## 2. Goals and non-goals

**Goals (v1.3.x):**
- Default-game widget in the header toolbar gap, sized between `Games` (small) and `Launch multiple` (medium).
- Click → dropdown of saved games; current default highlighted; `Manage games…` footer opens existing Games settings sheet.
- Empty-state UX when no games are saved (link to Games settings sheet).
- Right-click → `Rename…` / `Reset name` context menu on saved games (in widget dropdown, per-row ComboBox, Games settings sheet), saved private servers (in Squad Launch sheet), and account rows (main + compact).
- `LocalName ?? Name` rendering across every UI surface that shows the original.
- Forward/backward-compatible JSON storage on all three stores.
- Compact-mode account rows respect `LocalName` for `DisplayName` reads.

**Non-goals (v1.3.x):**
- **Mac parity.** Mac's `FavoriteGameStore` is single-URL; the widget + rename feature ports to Mac as a separate cycle once Windows ships. Tracked as forward-looking in §11.
- **Bulk rename / find-and-replace.** One item at a time.
- **Sync to Roblox.** Strictly local. The popup's mono-micro `ROBLOX NAME — <original>` line reminds the user of this.
- **A new "Saved private servers" settings sheet.** The Squad Launch sheet stays the surface for now (see §10 known UX concern).
- **Tray-menu account rename surface.** Tray's right-click menu is `Multi-Instance ON/OFF / Open / Quit` — no per-account list to attach a rename to. If a future tray-account-list ever lands, it reads `LocalName ?? DisplayName` automatically (free).
- **Auto-shorten heuristics.** No "smart truncate" of long Roblox names. Renames are explicit user choices; the UI truncates with ellipsis when needed.

## 3. Stack

No new dependencies. Uses what's already in the solution:

- `System.Text.Json` — already used by `FavoriteGameStore`, `PrivateServerStore`, `AccountStore`. Adding a nullable property is one line per record.
- `System.Windows.Controls.ContextMenu` — WPF native, no new package.
- WPF-UI by lepoco — for the popup window styling (`ui:FluentWindow` matches the existing modals).
- `INotifyPropertyChanged` (already in MVVM glue) — for widget reactivity.

## 4. Architecture

```
MainViewModel
  ├─ DefaultGameDisplay : string  (NEW; INPC; reads LocalName ?? Name)
  ├─ SetDefaultGameCommand : IRelayCommand<FavoriteGame>  (NEW)
  ├─ RenameItemCommand : IRelayCommand<RenameTarget>  (NEW)
  ├─ ResetItemNameCommand : IRelayCommand<RenameTarget>  (NEW)
  └─ subscribes IFavoriteGameStore.DefaultChanged  (NEW event)

DefaultGameWidget (XAML in MainWindow.xaml header row 2)
  ├─ binds to MainViewModel.DefaultGameDisplay
  ├─ ToggleButton opens dropdown popup
  └─ Popup ListBox bound to MainViewModel.AvailableGames
      └─ on click → SetDefaultGameCommand
      └─ "Manage games…" footer → existing OpenSettingsCommand

RenameWindow (NEW, App project)
  ├─ takes RenameTarget {Kind, Id, OriginalName, CurrentLocalName}
  ├─ Save → invokes IAccountStore / IFavoriteGameStore / IPrivateServerStore .UpdateLocalNameAsync(id, newName)
  └─ "Reset to original" link → same call with newName = null

Each store gets a new method:
  - IFavoriteGameStore.UpdateLocalNameAsync(long placeId, string? localName)
  - IPrivateServerStore.UpdateLocalNameAsync(Guid serverId, string? localName)
  - IAccountStore.UpdateLocalNameAsync(Guid accountId, string? localName)
```

**External boundaries (no additions):** the three existing JSON files on disk (`favorites.json`, `private-servers.json`, `accounts.dat`). Adding a property to each schema is the only on-disk change.

## 5. Components & interfaces

### 5.1 Data model additions (`ROROROblox.Core`)

```csharp
public sealed record FavoriteGame(
    long PlaceId,
    long UniverseId,
    string Name,
    string ThumbnailUrl,
    bool IsDefault,
    DateTimeOffset AddedAt,
    string? LocalName = null);  // NEW

public sealed record SavedPrivateServer(  // existing record in src/ROROROblox.Core/SavedPrivateServer.cs
    Guid Id,
    long PlaceId,
    string Code,
    PrivateServerCodeKind CodeKind,
    string Name,
    string PlaceName,
    string ThumbnailUrl,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastLaunchedAt,
    string? LocalName = null);  // NEW

public sealed record Account(
    Guid Id,
    string DisplayName,
    string AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLaunchedAt,
    string? LocalName = null);  // NEW
```

`LocalName = null` is the default for old records loaded from existing JSON files (System.Text.Json fills missing properties with the C# default for the type — `null` for nullable reference types). New records written back include the property; older RORORO versions reading those files ignore the unknown property by default. Roundtrip stable both directions.

### 5.2 Store interface additions

Each existing store gets one new method matching the shape of its existing single-property updates:

```csharp
public interface IFavoriteGameStore {
    // ... existing methods ...
    Task UpdateLocalNameAsync(long placeId, string? localName);
    event EventHandler? DefaultChanged;  // NEW — fires after SetDefaultAsync
}

public interface IPrivateServerStore {
    // ... existing methods ...
    Task UpdateLocalNameAsync(Guid serverId, string? localName);
}

public interface IAccountStore {
    // ... existing methods ...
    Task UpdateLocalNameAsync(Guid accountId, string? localName);
}
```

`DefaultChanged` event lets the widget react without a manual re-fetch. Existing `SetDefaultAsync` already mutates state; this just adds the broadcast.

### 5.3 RenameTarget (App project)

A small DTO so the rename popup is entity-agnostic:

```csharp
public sealed record RenameTarget(
    RenameTargetKind Kind,    // Game | PrivateServer | Account
    object Id,                // long for Game, Guid for the others
    string OriginalName,      // the Roblox-side value (Name or DisplayName)
    string? CurrentLocalName); // current override or null

public enum RenameTargetKind { Game, PrivateServer, Account }
```

`MainViewModel.RenameItemCommand` takes a `RenameTarget`, opens `RenameWindow`, on Save dispatches to the right store via a switch on `Kind`.

### 5.4 DefaultGameWidget (XAML, App project)

Lives inline in `MainWindow.xaml` Header Row 2, replacing the current `Width="*"` filler column:

```xml
<!-- Existing left-side toolbar buttons … -->
<ToggleButton x:Name="DefaultGameWidget"
              MinWidth="200" MaxWidth="340"
              Margin="8,0"
              Padding="10,6"
              Background="{DynamicResource RowBgBrush}"  <!-- or a new subtly-cyan-tinted brush; implementation detail -->
              BorderBrush="{DynamicResource CyanBrush}"
              BorderThickness="1"
              IsChecked="{Binding IsDefaultGameDropdownOpen, Mode=TwoWay}">
  <Grid>
    <!-- icon + DEFAULT label + DefaultGameDisplay + chevron -->
  </Grid>
  <ToggleButton.Style>
    <!-- empty-state trigger when AvailableGames.Count == 0 -->
  </ToggleButton.Style>
</ToggleButton>

<Popup IsOpen="{Binding ElementName=DefaultGameWidget, Path=IsChecked}"
       PlacementTarget="{Binding ElementName=DefaultGameWidget}"
       Placement="Bottom"
       StaysOpen="False">
  <Border ...>
    <ListBox ItemsSource="{Binding AvailableGames}"
             SelectedItem="{Binding CurrentDefaultGame, Mode=OneWay}">
      <!-- DataTemplate: dot + LocalName ?? Name + DEFAULT tag for current default -->
    </ListBox>
    <Button Content="Manage games…"
            Command="{Binding OpenSettingsCommand}" />
  </Border>
</Popup>
<!-- Existing right-side toolbar buttons … -->
```

Hidden in compact mode via the same `IsCompact`-bound trigger pattern the surrounding header already uses.

### 5.5 RenameWindow (XAML, App project)

`ui:FluentWindow`, ~360×180, non-resizable, owner-modal. Contents:

- Title bar: `Rename`
- Mono-micro line: `ROBLOX NAME — {OriginalName}`
- TextBox pre-filled with `CurrentLocalName ?? OriginalName`
- "Reset to original" hyperlink — visible only when `CurrentLocalName != null`
- Save / Cancel buttons. Enter = Save default action; Esc = Cancel.

On Save: trim input, treat empty/whitespace as null, call the right `UpdateLocalNameAsync` via `MainViewModel.RenameItemCommand`'s switch.

### 5.6 Right-click context menus (XAML, App project)

Five trigger surfaces, all pointing at the same `RenameItemCommand` with appropriately-shaped `RenameTarget`:

| Surface | Element | Right-click items |
|---|---|---|
| Default-game widget dropdown | ListBox row (per FavoriteGame) | Set as default · Rename… · Reset name · Remove |
| Per-row game ComboBox dropdown | ComboBoxItem (per FavoriteGame) | Rename… · Reset name |
| Games settings sheet list | ListBox row (per FavoriteGame) | Set as default · Rename… · Reset name · Remove |
| Squad Launch sheet | Saved-server row | Rename… · Reset name · Remove (existing button stays) |
| Account row (main + compact) | Border (whole row) | Rename… · Reset name · (existing actions remain on the row buttons) |

`Reset name` is enabled only when `LocalName != null`.

## 6. Data flows

### 6.1 Quick-switch the default game

```
User clicks DefaultGameWidget toggle
  → Popup opens, shows ListBox of saved games (LocalName ?? Name each row)
User clicks a non-default row
  → SetDefaultGameCommand fires with that FavoriteGame
  → IFavoriteGameStore.SetDefaultAsync(placeId)
    → mutates favorites.json (atomic write, existing pattern)
    → fires DefaultChanged
  → MainViewModel re-reads default, raises INPC on DefaultGameDisplay
  → widget readout updates; popup closes (StaysOpen=False)
```

### 6.2 Rename a saved game

```
User right-clicks a game (any of the 3 game surfaces)
  → ContextMenu opens with Rename… / Reset name / etc.
User clicks Rename…
  → RenameItemCommand fires with RenameTarget {Kind=Game, Id=placeId, ...}
  → opens RenameWindow modal
User edits text, clicks Save (or hits Enter)
  → RenameWindow.OnSave: trim input, normalize empty/whitespace to null
  → MainViewModel switch on Kind → IFavoriteGameStore.UpdateLocalNameAsync(placeId, newName)
    → mutates favorites.json (atomic write)
  → MainViewModel re-reads AvailableGames, raises INPC on DefaultGameDisplay
  → all surfaces showing this game re-render via LocalName ?? Name
```

Same flow for private servers (Kind=PrivateServer, Id=Guid) and accounts (Kind=Account, Id=Guid).

### 6.3 Reset a name

Identical to §6.2 except the popup's `Reset to original` hyperlink (or empty-input Save) calls `UpdateLocalNameAsync(id, null)` directly.

## 7. Where renames render (read-side surfaces)

| Surface | Renders | Field |
|---|---|---|
| Default-game widget readout | `MainViewModel.DefaultGameDisplay` | `LocalName ?? Name` |
| Default-game widget dropdown rows | per `FavoriteGame` | `LocalName ?? Name` |
| Per-row game ComboBox display | `AccountSummary.SelectedGame` | `LocalName ?? Name` |
| Per-row ComboBox dropdown items | per `FavoriteGame` | `LocalName ?? Name` |
| Games settings sheet rows | per `FavoriteGame` | `LocalName ?? Name` |
| Squad Launch sheet rows | per `SavedPrivateServer` | `LocalName ?? Name` |
| Account row primary label | `AccountSummary.DisplayName` | `LocalName ?? DisplayName` |
| Account row MAIN-pill row | (via `DisplayName` binding) | `LocalName ?? DisplayName` |
| Follow-strip chips (currently masked in v1.2 — see memory `project_rororo_follow_masked_v1.2`; renders when feature is restored) | per `AccountSummary` | `LocalName ?? DisplayName` |
| Compact-mode account row | per `AccountSummary` | `LocalName ?? DisplayName` |
| Tray menu `Start [Account]` label | `MainAccount.DisplayName` | `LocalName ?? DisplayName` |
| Session History entries | display-time read of original record | `LocalName ?? Name` |

The Roblox-fetched fields (`Name`, `DisplayName`) keep updating from `IRobloxApi` — the rename overlay never blocks those refreshes. This decoupling is intentional (§9 decision 3).

## 8. Edge cases & error handling

| Case | Behavior |
|---|---|
| Empty / whitespace input on Save | Trim → null. No error toast. Effective reset. |
| Very long input (>50 chars) | Allowed. UI truncates with ellipsis everywhere. |
| Two items renamed to the same string | Allowed. IDs disambiguate; this is local display only. |
| Roblox-side `DisplayName` refresh via `IRobloxApi.GetUserProfileAsync` | Updates `Account.DisplayName`. Never touches `LocalName`. |
| Roblox-side `Name` refresh on a game (e.g., `IRobloxApi.GetUniverseInfo` rewires the title) | Updates `FavoriteGame.Name`. Never touches `LocalName`. |
| Re-add a game with same `placeId` (existing replace semantics) | Existing `LocalName` preserved. The local nickname survives a re-add. |
| Re-add a private server with same `code` (existing replace semantics) | Existing `LocalName` preserved. Same as games. |
| `UpdateLocalNameAsync` called with a non-existent ID | Throws `KeyNotFoundException` — same shape `IFavoriteGameStore.SetDefaultAsync` already throws. ViewModel catches + surfaces a quiet status banner; rare in practice (only if the user concurrently removes the item from another surface). |
| File write fails (disk full, permissions) | Existing atomic-write contract on each store throws `IOException`. ViewModel catches + sets `StatusBanner = "Couldn't save name change. Disk error?"`. In-memory state stays correct for the session. |

## 9. Decisions log

1. **Add `LocalName` directly to existing records, not a separate "RenameOverrides" store.** Considered a single shared `RenameOverrideStore` keyed by `(EntityKind, Id)` for unification. Rejected: the existing per-store JSON files are already the natural home; one shared store doubles the read paths and orphans on entity removal. Per-record nullable property is the smaller move.

2. **`LocalName` lives on `Account` (Core), not `AccountSummary` (App ViewModel).** Considered putting the override only on the VM so it doesn't persist past restart. Rejected: users want their renames durable. Persisting on `Account` matches the existing pattern for `DisplayName` itself.

3. **Roblox-side refreshes never touch `LocalName`.** Considered "if the user hasn't renamed, prefer the latest Roblox name; if they have, keep their override." That's already the default behavior of `LocalName ?? Name` — no special logic needed. The decision worth recording: we don't try to detect "looks like the user just hasn't gotten around to renaming" — null means null, present means present.

4. **Right-click context menu over pencil-on-hover.** Pencil-on-hover is more immediately discoverable but adds visual noise to every list row everywhere and feels less Windows-native. Right-click is the universal "I want options for this thing" gesture and works uniformly across all five trigger surfaces. Trade documented; revisitable if user testing flags discoverability.

5. **Spec home is a fresh branch off main.** `docs/spec-default-game-widget-and-rename`. Keeps the bot-challenge fix branch (`fix/account-bot-challenge`) clean for its own PR; lets the spec land in main without being held hostage by either the bot-challenge fix or the implementation cycle.

6. **Quick-switch dropdown over readout-only.** Mac uses readout-only because Mac is single-URL and the banner *is* the whole UX surface. Windows already has multi-game library + per-row pickers, so the widget pulls its weight by adding a faster swap path than opening the Games settings sheet.

## 10. Known UX concern — deferred

**Saved private servers live inside the Squad Launch sheet, behind the magenta `Private server` toolbar button.** Surfaced 2026-05-07 by Este: "It is a bit hidden." The rename feature still works there (right-click → Rename), but if discoverability becomes a real complaint, a future cycle should consider:

- A "Saved private servers" section inside the existing Games settings sheet, OR
- A dedicated "Servers" toolbar button + sheet, OR
- A unified "Library" sheet covering both saved games and saved servers.

Out of scope for this branch. Captured here so a future /reflect cycle can pick it up rather than re-discovering the friction.

## 11. Out of scope (forward-looking)

- **Mac parity port.** Mac's `FavoriteGameStore` is currently single-URL. The full port (multi-game library + the widget + the rename overlay) is a separate cycle once Windows ships and proves the shape. Tracked in [rororo-mac CLAUDE.md] as the natural follow-on.
- **Combined Games + Servers settings sheet** (see §10).
- **Tray-menu account list.** If a future iteration adds per-account items in the tray, they read `LocalName ?? DisplayName` automatically — no schema change needed.
- **Bulk rename / find-and-replace.**
- **Sync overrides between Mac and Windows installations.** Both ports use the same on-disk JSON shape (the `LocalName` field is identical), so a future user-driven import/export feature inherits this for free.
- **Release notes.** v1.3.x release notes drafted later in the cycle. Voice template: [`docs/store/release-notes-1.2.0.0.md`](../../store/release-notes-1.2.0.0.md) — short headers, "killer use case" framing, plain-Windows-user language.

## 12. Testing

- **Unit tests (per store):** `LocalName` serialization roundtrip; loading legacy JSON without `LocalName` field deserializes to null; null-normalization at update time (empty / whitespace → null); `UpdateLocalNameAsync` on missing ID throws `KeyNotFoundException`.
- **Unit tests (`MainViewModel`):** `DefaultGameDisplay` updates on `IFavoriteGameStore.DefaultChanged`; `SetDefaultGameCommand` calls `SetDefaultAsync` then refreshes; `RenameItemCommand` dispatches to the right store based on `RenameTargetKind`.
- **No automated WPF visual tests.** Consistent with existing spec §8 manual-smoke-only convention. Manual checklist additions:
  - Rename a saved game; verify the new name shows in widget readout, widget dropdown, per-row ComboBox, ComboBox dropdown, and Games settings sheet.
  - Rename a saved private server; verify in Squad Launch sheet.
  - Rename an account; verify in account row, compact row, Follow chips, MAIN pill area, and tray "Start [Account]" label.
  - Reset name on each kind; verify revert to Roblox-side original.
  - Empty / whitespace rename; verify reset behavior (no error, just clears the override).
  - Right-click each surface confirms the context menu appears with the right items + correct enabled-state on `Reset name`.
  - Re-add a renamed game (same `placeId`); verify `LocalName` persists.
  - Re-fetch profile (touch a Roblox-side display name); verify `LocalName` survives.

## 13. References

- Canonical design spec: [`2026-05-03-rororoblox-design.md`](2026-05-03-rororoblox-design.md)
- Sibling v1.2 spec (FPS limiter): [`2026-05-07-per-account-fps-limiter-design.md`](2026-05-07-per-account-fps-limiter-design.md)
- Bot-challenge investigation that led into this brainstorm: [`../../investigations/2026-05-07-account-bot-challenge.md`](../../investigations/2026-05-07-account-bot-challenge.md) (lives on `fix/account-bot-challenge` branch)
- Mac sibling repo: https://github.com/estevanhernandez-stack-ed/rororo-mac — eventual port target.
- Brand tokens: `~/.claude/skills/626labs-design/colors_and_type.css`
- Existing toolbar layout (target placement): `src/ROROROblox.App/MainWindow.xaml` Header Row 2.
