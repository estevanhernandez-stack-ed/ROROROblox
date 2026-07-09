# RORORO — default private server + Library cleanup design

---
**Date:** 2026-07-07
**Status:** Approved (brainstorm complete) — ready for implementation plan
**Author:** The Architect + Este
**Scope:** Let a saved private server be marked the default (separate from the default game), pre-selected in the Squad Launch flow so a clan doesn't re-pick their server every time; and fix the Library window's dead-space layout + clipped header while we're in it.
**Origin:** Este noticed the Library's Saved private servers rows have Rename/Remove but no "Set default" — games have it, servers don't.
---

## 1. Problem & context

RoRoRo's **Library** window (`SettingsWindow`, title "RoRoRo -- Library"; opened by the toolbar **Games** button and the default-game widget's **Manage games…** link — both routes hit the same window) manages two collections:

- **Saved games** (`IFavoriteGameStore`, `favorites.json`) — each row has **Set default / Rename / Remove**. The default game (cyan DEFAULT badge) is what **Launch As / Launch All** use.
- **Saved private servers** (`IPrivateServerStore`, `private-servers.json`) — each row has only **Rename / Remove**. **No default concept exists.**

The only way to send every account to a private server is the **Squad Launch** modal (opened by the toolbar **Private server** button — a *different* window from the Library), which lists saved servers and makes the user pick one **every single time**. A clan that always plays on one private server has no set-and-forget.

The launch engine already supports a private-server default: `MainViewModel.ResolveLaunchTarget` honors a `FavoriteGame{IsPrivateServer, IsDefault}` as a `LaunchTarget.PrivateServer`, and `SavedPrivateServer` carries a stable `PrivateServerId` (Guid) used as its identity (a PS shares its place's `PlaceId`, so `PlaceId` alone collides — servers are keyed by `Id`). The gap is **persistence + the Set-default control + pre-selection**, not launch capability.

## 2. Decisions (from the brainstorm)

1. **Separate default, not unified.** A default *game* and a default *private server* are independent. Setting a default server does NOT touch the default game. Each powers its own launch path:
   - Default **game** → Launch As / Launch All (unchanged).
   - Default **server** → pre-selected in Squad Launch.
   - Rationale: no cross-store mutual-exclusion footgun, no shared-identity wrinkle, the game-default machinery is untouched. Simpler than a unified "one default target."
2. **Set-default lives in the Library**, as a **"Set default" button + DEFAULT badge** on the Saved private servers rows — a near-exact mirror of the games' existing control in the same file.
3. **Squad Launch pre-selects the default server** — it sorts to the top and is visually highlighted so it's the obvious one to launch; no auto-fire, no new button. If no default is set, today's behavior (manual pick) is unchanged.
4. **The default is unsettable.** The row that IS default shows a **Clear default** affordance (in place of the hidden Set-default button), returning to the zero-default state (manual pick). So a user can set → switch → or clear, and the default is never a one-way trap.
5. **Themed via the pack, not hardcoded.** Every new visual (DEFAULT badge, default-row border, Squad Launch highlight) uses existing `DynamicResource` theme tokens — no hardcoded hex — so custom themes restyle it. Verified against the `626labs:design` skill / theme resource dictionaries before implementation; if any genuinely new token is needed, it's registered in the theme pack (the same dictionaries the game DEFAULT badge already draws from), so it's not a theming blind spot.
6. **Bounded Library cleanup rides along:** fix the fixed-proportion dead-space layout, the clipped (non-wrapping) header, and refresh the header copy for both defaults. No add-flow redesign, no unified list, no tabs (those were explicitly deferred).

## 3. Non-goals

- No unified "single default target." Game and server defaults are independent.
- No auto-promotion when the default server is removed (unlike games, which promote index 0). Removing the default server leaves **zero** default — surprising a user by blasting all alts into some *other* server is worse than no default.
- No hardcoded colors in the new UI — everything through theme tokens (see §2.5).
- No redesign of the add flow (Search + Add-by-URL stay as they are), no unified games+servers list, no tabs.
- No change to the game-default behavior or storage.

## 4. Architecture & components

Three touch points: **Core store**, **Library UI (`SettingsWindow`)**, **Squad Launch pre-selection**.

### 4.1 Core — `SavedPrivateServer.IsDefault` + `IPrivateServerStore.SetDefaultAsync`

- **`SavedPrivateServer`** gains `bool IsDefault = false` as a new record field. Additive + defaulted false → old `private-servers.json` blobs without the field deserialize to `IsDefault = false` (back-compat, no migration).
- **`IPrivateServerStore`** gains, mirroring `IFavoriteGameStore`:
  - `Task SetDefaultAsync(Guid id)` — mark this server default, clear the flag on all others (single-store mutual exclusion, at most one `IsDefault == true` after). Throws `KeyNotFoundException` if `id` isn't present. No-op short-circuit if it's already the default (skip write AND skip the event, so subscribers treat every event as a real change).
  - `Task ClearDefaultAsync()` — clear the flag on every server, returning to the zero-default state (backs the **Clear default** button + `RemoveAsync` of the default). No-op short-circuit if nothing is currently default (no write, no event).
  - `event EventHandler? DefaultChanged` — fired **outside the gate** after `SetDefaultAsync` / `ClearDefaultAsync` (or a default-clearing `RemoveAsync`) mutates + persists, so subscribers can re-enter the store without deadlocking. Mirrors `IFavoriteGameStore.DefaultChanged`.
- **`PrivateServerStore`** implements `SetDefaultAsync` by mirroring `FavoriteGameStore.SetDefaultAsync` (SemaphoreSlim gate → `LoadAsync` → existence check → already-default short-circuit → `for` loop setting `IsDefault = (row.Id == id)` on every row → `SaveAsync` → fire `DefaultChanged` outside the gate).
- **`PrivateServerStore.RemoveAsync`** (exists): when the removed server was the default, the default simply becomes zero (no promotion). If it was the default, fire `DefaultChanged` after save so Squad Launch's pre-selection recomputes. (Removing a non-default server needs no event.)
- **Every record copy/construct site must carry `IsDefault`.** Audited (`PrivateServerStore.cs`): two mutators already use `with { … }` and preserve it automatically (`UpdateLocalNameAsync` :173, `TouchLastLaunchedAsync` :193 — no change needed). **Two sites full-reconstruct the record and MUST be updated:** `AddAsync` (:106) — on **replace** (same placeId+code) it must **preserve the existing row's `IsDefault`** (mirroring `FavoriteGameStore`'s IsDefault-preserve-on-re-add), and set `false` for a genuinely new row; the **legacy-migration** path (:245) sets `IsDefault: false`. Missing either silently drops a server's default on re-add or on legacy load.

### 4.2 Library UI — `SettingsWindow`

**Set-default control (the feature):** the `SavedPrivateServer` `DataTemplate` gains, mirroring the `FavoriteGame` template:
- A **DEFAULT badge** next to the name (cyan `#17d4fa` fill, navy text — same as games, for cross-section consistency), shown when `IsDefault`. It sits alongside the existing magenta **PRIVATE** badge (a row can show both PRIVATE + DEFAULT).
- A **"Set default"** button in the row's action group (left of Rename), `Visibility` collapsed when `IsDefault`. **In its place on the default row, a "Clear default" button** (`Visibility` visible only when `IsDefault`) → returns to zero-default. (This is the one place the server UI diverges from games, which have no un-set — servers allow zero default, so they get the Clear affordance.)
- The row `Border` gets a **cyan border** when `IsDefault` (mirrors the game default's `DataTrigger`).
- Code-behind `OnSetDefaultServerClick` → `await _servers.SetDefaultAsync((Guid)tag)`; `OnClearDefaultServerClick` → `await _servers.ClearDefaultAsync()`; both reload the servers list. `OnSetDefaultServerClick` mirrors `OnSetDefaultClick` for games.
- **Theming:** the DEFAULT badge fill (`CyanBrush`), navy text (`NavyBrush`), and the default-row border (`CyanBrush`) are all existing `DynamicResource` theme tokens — the same ones the game DEFAULT badge already uses — so custom themes restyle them for free. No hardcoded hex. Confirm against the `626labs:design` skill during implementation; no new token is expected (reuse the game-default set).

**Layout fix (the cleanup):** the current grid gives the games list a fixed `2*` height and the servers list a fixed `*`, so both always claim a 2:1 slice regardless of content — the dead gap. Replace with **one shared `ScrollViewer`** holding a `StackPanel` of: Saved games header → games `ItemsControl` (content-height) → Saved private servers header → servers `ItemsControl` (content-height). The fixed chrome (title, search, add-by-URL, status, Close) stays outside the scroll. Result: with one game + one server the sections sit directly under each other; the scroll region takes remaining height and only scrolls on overflow. Empty states render inline under their section header (not centered-overlaid in a fixed region).

**Header fixes (the cleanup):**
- Add `TextWrapping="Wrap"` to the description `TextBlock` (fixes the "…give it a custom" clip).
- Update the copy to cover both defaults, e.g.: *"Saved games and private servers. The game marked DEFAULT is what Launch As uses; the private server marked DEFAULT is pre-selected when you launch all to a server. Rename any row to give it a custom name (Roblox-side names stay untouched)."*

### 4.3 Squad Launch pre-selection — `SquadLaunchWindow`

- On load, after `ListAsync`, compute the ordered list: **the default server first** (if any), then the rest by the existing "most-recently-launched, then addedAt" sort. Extract the ordering into a **pure, unit-testable static** (`SquadLaunchOrdering.Order(servers)` → default-first-then-recency) so it's covered without WPF.
- The default server's row gets a highlighted treatment (cyan border + a small DEFAULT tag) so it's visually the obvious one to launch. Its **Launch all** button is unchanged — the user still clicks it; we've just surfaced it to the top and highlighted it (the "pre-select" is visual prominence, since each row launches independently — there is no select-then-confirm step in this modal).
- Subscribe to `IPrivateServerStore.DefaultChanged` isn't required here (the modal reads fresh on open); no live re-order needed mid-modal.

## 5. Data flow

1. **Set default:** Library → `OnSetDefaultServerClick` → `PrivateServerStore.SetDefaultAsync(id)` → persists `IsDefault` to `private-servers.json`, clears others → fires `DefaultChanged` → Library reloads the servers list → the row shows the DEFAULT badge + cyan border, its Set-default button hides.
2. **Launch:** user opens Squad Launch → `Order(servers)` puts the default first + highlighted → user clicks its **Launch all** → existing launch path (`LaunchTarget.PrivateServer`), unchanged.
3. **Remove default:** Library → Remove on the default server → `RemoveAsync` drops it, default becomes zero → `DefaultChanged` → next Squad Launch open has no highlighted default (manual pick), no regression.

## 6. Error handling & edge cases

- **`SetDefaultAsync` on a missing id** → `KeyNotFoundException` (mirrors games); the Library only calls it with ids from the rendered list, so this is a guard, not a user path.
- **Already-default** → no-op short-circuit (no write, no event).
- **Rename the default server** → default survives; `IsDefault` is on the record and rename only touches `LocalName`. (Re-verify: `UpdateLocalNameAsync` does a `with { LocalName = … }`, preserving `IsDefault`.)
- **Back-compat JSON** → old blobs lack `IsDefault` → deserialize to `false`; no default until the user sets one. Load must not throw on the missing field (default record value covers it).
- **Zero saved servers / no default** → today's Squad Launch behavior, unchanged.
- **Plugin contract** → `IsDefault` is an additive field on the `SavedPrivateServer` record. The plugin-facing surface (`PrivateServerStoreForPlugin` → the launch-invoker adapter) maps fields explicitly through the proto; adding a record field does not change the proto and does not break plugins. If exposing default-ness to plugins is ever wanted, that's a separate proto bump — out of scope here.
- **Concurrency** → `SetDefaultAsync` runs under the store's existing `SemaphoreSlim` gate, same as every other mutator; the `DefaultChanged` event fires outside the gate.

## 7. Testing

**Unit (Core, hand-rolled fakes / real store over a temp file, mirroring existing store tests):**
- `SetDefaultAsync` sets exactly one `IsDefault`; setting a second clears the first (mutual exclusion).
- Already-default → no-op (no event fired — assert via an event counter).
- `SetDefaultAsync` on an unknown id → `KeyNotFoundException`.
- `ClearDefaultAsync` from a set-default state → zero defaults, `DefaultChanged` fires; from an already-zero state → no-op (no event). Round-trip set → clear → set works.
- `RemoveAsync` of the default → default becomes zero, no promotion; `DefaultChanged` fires. `RemoveAsync` of a non-default → default unchanged, no `DefaultChanged`.
- Persistence round-trips `IsDefault` through JSON; loading a legacy blob without the field yields `IsDefault = false`.
- Rename (`UpdateLocalNameAsync`) preserves `IsDefault`.

**Unit (App):**
- `SquadLaunchOrdering.Order` — default-first, then recency; no default → pure recency; stable for ties.

**Manual smoke (WPF, house convention — the Library + Squad Launch windows are thin over the above):**
- Library: Set default on a server → DEFAULT badge + cyan border appear, Set-default button hides; set a different server → the badge moves. Remove the default → badge gone.
- Library layout: with one game + one server, no dead gap; header wraps (no clip); many rows scroll within the shared region.
- Squad Launch: with a default set, it opens with that server on top + highlighted; launch works. With no default, unchanged.

## 8. What ships

- `SavedPrivateServer.IsDefault` + `IPrivateServerStore.SetDefaultAsync` / `DefaultChanged` + `PrivateServerStore` impl + `RemoveAsync` default handling.
- `SettingsWindow` server-row Set-default button + DEFAULT badge + cyan border; the shared-scroll layout fix; header `TextWrapping` + copy.
- `SquadLaunchWindow` default-first ordering + highlight; `SquadLaunchOrdering` pure helper.
- Unit tests per §7; manual smoke checklist.

Small, self-contained, symmetric with the games default that already exists. Build as its own subagent-driven cycle like the tray gate.
