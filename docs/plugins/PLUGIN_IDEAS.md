# RoRoRo Plugin Ideas

> Status sheet for plugins 626 Labs intends to build or has scoped. The plugin system itself is documented in [`AUTHOR_GUIDE.md`](AUTHOR_GUIDE.md). This file is the **idea bench** — what's locked for v1.4, what's backlog, what each plugin demonstrates about the system.
>
> **Last updated:** 2026-05-10

## Status legend

- **v1.4 ship** — committed for the v1.4 release window. Ships as a separately-distributed sibling repo.
- **Backlog** — captured, not scheduled. Reach for one of these when v1.4.x needs a follow-up plugin.

---

## v1.4 ship

### 1. Clan Tracker

**Pitch.** Embeds the Pet Sim 99 clan's existing clan-tracker / leaderboard HTML page inside RoRoRo so clan members see standings + activity without alt-tabbing to a browser. First plugin most clan members will install, because the value is immediate and the install path teaches them how the rest of the plugin system works.

**Shape.** Plugin EXE hosts a WebView2 control that points at the hosted clan-tracker URL. Status-panel pane surface inside RoRoRo's main window renders the WebView (via a child-window embed pattern, since `host.ui.status-panel` contributes a pane RoRoRo renders inside).

**Capabilities declared.**

- `host.ui.status-panel` — the panel that hosts the WebView2.
- `host.events.account-launched` *(optional, v1)* — could highlight currently-playing accounts on the leaderboard if the hosted page has a "who's online" surface; otherwise drop and keep the plugin pure-observer of nothing.

**No `system.*` caps.** Plugin renders a webpage; no input synthesis, no global watching, no foreign-window focus.

**Open questions.**

- Hosted URL TBD — user has it; will surface at build time.
- Does the page need any auth flow (cookies, Roblox login, Discord OAuth)? If yes, WebView2 profile shape matters and we may need per-account profile isolation.
- Does the page want "currently launched accounts" piped in? If yes, plugin subscribes to `account-launched` and JS-bridges the data into the page.

**Why it's a strong first plugin.** Real value-add for the clan audience (no fake demo plugin). Demonstrates UI contribution + WebView2 hosting pattern other plugin authors can copy. Zero `system.*` caps = clean consent sheet.

---

### 2. TinyTask with per-window user-id awareness

**Pitch.** TinyTask-shaped record / playback for keyboard + mouse macros, but **aware of which RoRoRo-managed account window it's targeting**. Records bind to a Roblox user id (captured from the foreground window at record time); playback refuses to fire unless the foreground window matches that user id. Auto-stops when the bound account's window closes. This is the killer use case — clan members can record a farming sequence on one alt and trust it won't fire keys into the wrong window if focus shifts mid-playback.

**Shape.** Standalone EXE with its own minimal recorder UI (record / play / stop + hotkeys — F8 record/stop, F5 play, mirroring TinyTask muscle memory). Subscribes to RoRoRo's `account-launched` + `account-exited` streams to track which windows belong to which user ids. Pre-playback, checks current foreground window's user id against the recorded binding. Auto-stop on `account-exited` for the bound user id.

**Capabilities declared.**

- `system.synthesize-keyboard-input` — playback synthesizes keys.
- `system.synthesize-mouse-input` — playback synthesizes mouse moves + clicks.
- `system.watch-global-input` — recording captures keyboard + mouse globally.
- `host.events.account-launched` — track which windows / pids belong to which user ids.
- `host.events.account-exited` — auto-stop trigger.
- `host.ui.row-badge` — per-account "recording" / "playing" indicator on the main window's account rows.
- `host.ui.tray-menu` — informational entry ("TinyTask: idle / recording / playing"). Click handlers are stubbed in v1.4, so this is read-only; control happens via the plugin's own hotkeys + UI.

**Open questions.**

- Storage format for recorded macros. Plain JSON of `(timestamp, event-type, payload)` records is the obvious default. Replay engine reads + emits via `SendInput`.
- Window → user-id mapping. RoRoRo emits the pid in `account-launched` events; plugin watches foreground window's pid and matches. If Roblox process model changes (e.g., separate UI/render processes), this needs to handle parent/child relationships.
- Per-window playback (multi-alt simultaneous record/play) is v1.4.1+ work. v1.4 ships single-window-at-a-time playback to keep the surface small.
- Hotkey registration via `RegisterHotKey` is global, which means TinyTask's hotkeys steal `F8` / `F5` system-wide while the plugin runs. Configurable hotkey assignment is a quick add but adds settings UI scope.

**Why it ships with v1.4.** This is the canonical reference plugin the `AUTHOR_GUIDE.md` already names (`rororo-autokeys-plugin` placeholder). Shipping it on day one makes the plugin system a *real* platform on launch instead of a placeholder. Macro-shaped plugin demonstrates the `system.*` disclosure pattern the contract was designed around. The user-id-awareness twist is RoRoRo-native value generic TinyTask can't offer.

**The wall holds.** RoRoRo's Store binary remains macro-free. This plugin is a separately-distributed sideload — same architecture, same Store-policy posture as every other plugin. The fact that we (626 Labs) author it doesn't change its distribution shape.

---

## Backlog

### 3. Session Stats

**Pitch.** Status panel + row badges showing live per-account uptime, total launches this session, mutex state, last-launched-at timestamps. Pure observer — useful for clan members tracking farming time, useful for RoRoRo itself as a debugging surface during development.

**Capabilities declared.**

- `host.events.account-launched`
- `host.events.account-exited`
- `host.events.mutex-state-changed`
- `host.ui.status-panel`
- `host.ui.row-badge`
- `host.ui.tray-menu`

Demonstrates **all three** event streams + **three** UI surfaces. The richest-coverage demo plugin of the v1.4 contract. No `system.*` caps. Built as a backlog plugin once one of the other backlog candidates surfaces a v1.4.x need.

---

### 4. Discord Rich Presence

**Pitch.** Pushes "Playing Pet Sim 99 with N alts via RoRoRo" to the user's Discord Rich Presence whenever accounts launch. Clan-visible — every clan member sees who's farming right now, organically demos RoRoRo + the plugin system to non-RoRoRo Discord members.

**Capabilities declared.**

- `host.events.account-launched`
- `host.events.account-exited`

Needs `DiscordRichPresence` SDK (or `Discord.GameSDK`) as a NuGet dep. No UI contribution to RoRoRo's window — the presence shows up in Discord, not RoRoRo. Demonstrates external-integration plugin shape.

**Marketing angle.** The strongest viral surface in the backlog — clan members see RoRoRo named in their friends' Discord status without anyone selling it. Aligns with the "brand spreads for free" thesis.

---

### 5. Auto-Relaunch on Exit

**Pitch.** Watches `account-exited`; calls `RequestLaunch` against the same account id. Closes the farming-alt-crashed loop. Clan members lose less time when an alt crashes mid-session.

**Capabilities declared.**

- `host.events.account-exited`
- `host.commands.request-launch`
- `host.ui.row-badge` — show "auto-relaunch ON" per account.

**Risk.** A user who deliberately exits an alt gets their alt re-launched against intent. v1 mitigation: opt-in (default off), config via plugin's own UI since click handlers are stubbed. v1.5 (with bidirectional UI) gets a tray toggle per account.

**Why backlog over v1.4.** Solves a real clan pain point but overlaps conceptually with the TinyTask plugin (both touch the "I want to keep farming without supervision" surface). Better to ship TinyTask first, see whether crash-relaunch becomes the next ask.

---

## Cross-cutting notes

**Distribution.** Each plugin lives in its own sibling repo under `github.com/estevanhernandez-stack-ed/rororo-<name>-plugin`. Independent Velopack release pipelines. RoRoRo's installer never bundles plugin code (Store policy 10.2.2).

**Signing.** All 626 Labs-authored plugins sign with the 626 Labs LLC code-signing cert — **separate** from RoRoRo's Store cert. SmartScreen surfaces the publisher; cross-contamination is an immediate distribution-trust incident.

**Brand.** Each plugin's icon, install screenshots, and any in-plugin UI go through the `626labs-design` skill before ship. Programmatic placeholders disqualify per the SnipSnap retro rule.

**Test discipline.** Each plugin gets its own integration test against a real RoRoRo gRPC host (the v1.4 `ROROROblox.PluginTestHarness` pattern). The harness-blindspot follow-up (production-shape capability accessor) lands before the first plugin ships so we don't compound the bug.

---

**A 626 Labs product · *Imagine Something Else*.**
