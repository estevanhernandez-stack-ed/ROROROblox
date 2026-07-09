# RORORO — launch-to-home base + optional default game design

---
**Date:** 2026-07-09
**Status:** Approved-shape (from the #54 review conversation) — mini-spec, ready for implementation plan
**Author:** The Architect + Este
**Scope:** Make "no default game" a real, honest state: launches with no target open Roblox **home** (signed in as the right account) instead of silently falling back to the first saved game. Games gain **Clear default** (symmetric with private servers, #54); the UI *encourages* setting a default rather than requiring one.
**Origin:** Este, reviewing #54's asymmetry: "Can the base be launching to Roblox home, but it is encouraged to set a default game?"
**Builds on:** [`2026-07-07-default-private-server-design.md`](2026-07-07-default-private-server-design.md) (PR #54 — Clear default on servers, zero-default-legal model)
---

## 1. Problem

The default game is secretly a *requirement*. `ResolveLaunchTarget`/`ReloadGamesAsync` fall back `IsDefault → first saved game`, so a user with games but no marked default still launches into the first game — and the widget *displays* that first game as if it were chosen. Clear default on game rows would therefore be a lie (hides the badge, changes nothing), which is why #54 shipped it for servers only. First-run makes it worse: Launch As demands a default game URL before it will do anything.

## 2. Design decisions

1. **`LaunchTarget.Home`** — a new launch shape: open the Roblox app at home, authenticated as the launching account, joining nothing.
2. **Zero-default-game resolves to Home.** The silent first-game fallback dies. Precedence with a row's explicit selection is unchanged; only the *no-selection, no-default* path changes: `DefaultGame` resolution with no `IsDefault` favorite → `Home` (not first-favorite, not a prompt).
3. **Games gain Clear default** — same control pair as #54 gave servers (`Set default` / `Clear default`, style-trigger visibility), same store semantics (`ClearDefaultAsync`, no-op short-circuit, `DefaultChanged` outside the gate). **Auto-promotion on remove dies too:** removing the default game now leaves zero default (→ Home), mirroring the server store — removing a game should not silently re-target every launch at whatever was first in the list.
4. **Encouragement, not requirement:**
   - Widget with no default reads **"Roblox home"** with tooltip copy: *"Launches open Roblox at home. Set a default game in the Library to launch straight into it."*
   - First-run: the forced default-game-URL prompt is removed. Launch As just works (→ Home). The existing "Add games with the Games button" affordances remain the nudge.
   - Library header copy updates: "…No default game? Launches open Roblox at home."
5. **Contract caveat (spec §7.1-class):** `launchmode:app` is a Roblox-protocol surface we haven't shipped. One manual live-launch verification gates the merge; the decision log records the dependency (if Roblox changes launchmode handling, this is the entry that finds it).

## 3. Non-goals

- No change to Place / PrivateServer / FollowFriend launches, the auth-ticket flow, or Squad Launch.
- No removal of the settings `DefaultPlaceUrl` plumbing this cycle (it becomes vestigial for resolution; rip-out is a later cleanup).
- No per-account "home vs default" preference — one global behavior.
- No Store-listing/reviewer-letter changes (no new endpoints, no new disclosure — same protocol handler, one more launchmode value).

## 4. Architecture

**Core — `LaunchTarget.cs`:** add `public sealed record Home() : LaunchTarget;` with doc: home = `launchmode:app`, no placelauncherurl.

**Core — `RobloxLauncher.cs`:**
- URI builder: `Home` emits `roblox-player:1+launchmode:app+gameinfo:<ticket>+…` — same fields as today's URI **minus** `placelauncherurl` (which is required for `launchmode:play` per the spike finding, not for `app`). The `BuildPlaceLauncherUrl` switch throws on `Home` (never called for it) the same way it throws on unresolved `DefaultGame`.
- `DefaultGame` resolution (~line 115): favorites default → that game; **no default → `Home`** (replaces first-favorite/settings fallback).

**Core — `FavoriteGameStore.cs`:**
- Add `ClearDefaultAsync()` — copy `PrivateServerStore`'s (gate → no-op short-circuit → clear all → save → `DefaultChanged` outside gate).
- `RemoveAsync`: delete the promote-index-0 branch; removing the default fires `DefaultChanged` and leaves zero default (mirrors server store).

**App — `MainViewModel.ReloadGamesAsync`:** `defaultGame = FirstOrDefault(IsDefault…)` **without** the `?? FirstOrDefault(…)` fallback. `CurrentDefaultGame = null` is now meaningful; rows with no selection and no default get `SelectedGame = null` → resolver returns `DefaultGame` → launcher resolves `Home`. Widget binding renders "Roblox home" for null (converter or fallback binding), tooltip per §2.4.

**App — `SettingsWindow.xaml`:** game rows gain the `Clear default` button (verbatim #54 server-row pattern — style-trigger visibility, theme tokens, defined-before-use resource order per the 2026-07-09 lesson). Header copy per §2.4.

**App — first-run prompt:** locate the "prompt for default game URL on first Launch As" path and remove/soften it (launch proceeds to Home; no modal). The prompt's add-a-game affordance stays reachable via Library.

## 5. Edge cases

- **Games exist, none default:** Home. (The old behavior — first game — is gone; this is the point.)
- **Zero games saved:** Home (today: forced prompt). Strictly better first-run.
- **Row has an explicit selection:** unchanged — selection beats default beats Home.
- **Default game removed:** zero default → Home; `DefaultChanged` fires; widget flips to "Roblox home." No surprise re-target.
- **JoinByLink sentinel / PS entries:** unchanged (they resolve before the DefaultGame path).
- **Legacy `DefaultPlaceUrl` in settings:** ignored by resolution (vestigial); no migration needed.
- **launchmode:app fails on some Roblox build:** the launch error surfaces through the existing launch-failure path (toast/log); the compat decision-log entry is the tripwire. Fallback if Roblox ever kills `launchmode:app`: revert resolution to require-a-default (one commit).

## 6. Testing

- **Unit (launcher):** `Home` URI contains `launchmode:app`, contains `gameinfo`, does NOT contain `placelauncherurl` or `RequestGame`; `DefaultGame` with a default resolves to that place URI; `DefaultGame` with no default resolves to the `Home` URI shape.
- **Unit (store):** `ClearDefaultAsync` mirrors the server-store tests (clear/no-op/event/round-trip); `RemoveAsync` of default → zero default + event, non-default → no event, **no promotion** (assert no other row gained `IsDefault`).
- **Unit (VM):** `ReloadGamesAsync` with no default yields `CurrentDefaultGame = null` (no first-game fallback).
- **Manual (gates merge):** live `launchmode:app` launch — account lands on Roblox home signed in as the right user (the §2.5 contract check); Clear default on a game → widget reads "Roblox home" → Launch As opens home; set default → launches straight in again; first-run path (fresh settings) launches without the old prompt.

## 7. What ships

`LaunchTarget.Home` + launcher URI branch + resolution change; `FavoriteGameStore.ClearDefaultAsync` + no-promotion remove; VM null-default handling + widget copy; Library game-row Clear default + header copy; first-run prompt removal; tests per §6; decision-log entry for the launchmode:app contract dependency.

Sequenced behind PR #54 (branches from it or from main-after-#54-merges — it reuses #54's store pattern and Library row shape). Own subagent-driven cycle: ~5 tasks.
