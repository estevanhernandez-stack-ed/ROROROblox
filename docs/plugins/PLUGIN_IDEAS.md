# RoRoRo Plugin Ideas

> Status sheet for plugins 626 Labs intends to build or has scoped. The plugin system itself is documented in [`AUTHOR_GUIDE.md`](AUTHOR_GUIDE.md). This file is the **idea bench** — what's locked for v1.4, what's backlog, what each plugin demonstrates about the system.
>
> **Last updated:** 2026-05-10

## Plugin-status criterion

A plugin earns the title by **actually integrating with RoRoRo's runtime knowledge** — currently-launched accounts, pid → user-id mappings, mutex state, capability-disclosed system actions. Things that *don't* integrate (e.g., a clan-tracker webpage embed with no awareness of RoRoRo's state) belong as either standalone Windows apps OR in-app features in RoRoRo's main window — *not* as plugins. The plugin shape carries install / consent / gRPC complexity that should be earned, not assigned by convenience.

## Status legend

- **v1.4 ship** — committed for the v1.4 release window. Ships as a separately-distributed sibling repo.
- **Backlog** — captured, not scheduled. Reach for one of these when v1.4.x needs a follow-up plugin.
- **Not a plugin** — was once on this list, didn't earn the criterion above. Pointer to where the work actually lives.

---

## v1.4 ship

### 1. RoRoRo Ur Task

**Pitch.** TinyTask-shaped record / playback for keyboard + mouse macros, but **aware of which RoRoRo-managed account window it's targeting**. Name plays "RoRoRo + Your Task" off "TinyTask" — ties the plugin to the parent product on first read. Records bind to a Roblox user id (captured from the foreground window at record time); playback refuses to fire unless the foreground window matches that user id. Auto-stops when the bound account's window closes. This is the killer use case — clan members can record a farming sequence on one alt and trust it won't fire keys into the wrong window if focus shifts mid-playback.

**Shape.** Standalone EXE with its own minimal recorder UI (record / play / stop + hotkeys — F8 record/stop, F5 play, mirroring TinyTask muscle memory). Subscribes to RoRoRo's `account-launched` + `account-exited` streams to track which windows belong to which user ids. Pre-playback, checks current foreground window's user id against the recorded binding. Auto-stop on `account-exited` for the bound user id.

**Capabilities declared.**

- `system.synthesize-keyboard-input` — playback synthesizes keys.
- `system.synthesize-mouse-input` — playback synthesizes mouse moves + clicks.
- `system.watch-global-input` — recording captures keyboard + mouse globally.
- `host.events.account-launched` — track which windows / pids belong to which user ids.
- `host.events.account-exited` — auto-stop trigger.
- `host.ui.row-badge` *(declared, host-side rendering stubbed in v1.4)* — call accepted, handle returned, but `WpfPluginUIHost` is a stub that doesn't paint pixels until host-UI rendering lands (v1.4.1+). Declare it now so the contract is forward-compatible; the user-visible recording indicator lives in the plugin's own window for v1.4.
- `host.ui.tray-menu` *(declared, host-side rendering stubbed in v1.4)* — same shape as row-badge. Plugin owns its own tray icon for v1.4 control surface.

**v1.4 reality check on host-side UI.** `WpfPluginUIHost` is explicitly a stub in v1.4 (logs the call, returns a handle, paints nothing). Real host-side rendering of plugin-contributed tray items / row badges / status panels lands in v1.4.1+. For v1.4, plugins own their entire user-visible UI surface — their own window, their own tray icon, their own hotkeys. Capability declarations are still made for contract forward-compatibility.

**Open questions.**

- Storage format for recorded macros. Plain JSON of `(timestamp, event-type, payload)` records is the obvious default. Replay engine reads + emits via `SendInput`.
- Window → user-id mapping. RoRoRo emits the pid in `account-launched` events; plugin watches foreground window's pid and matches. If Roblox process model changes (e.g., separate UI/render processes), this needs to handle parent/child relationships.
- Per-window playback (multi-alt simultaneous record/play) is v1.4.1+ work. v1.4 ships single-window-at-a-time playback to keep the surface small.
- Hotkey registration via `RegisterHotKey` is global, which means TinyTask's hotkeys steal `F8` / `F5` system-wide while the plugin runs. Configurable hotkey assignment is a quick add but adds settings UI scope.

**Why it ships with v1.4.** This is the canonical reference plugin the `AUTHOR_GUIDE.md` already names (`rororo-autokeys-plugin` placeholder — superseded by "RoRoRo Ur Task"). Shipping it on day one makes the plugin system a *real* platform on launch instead of a placeholder. Macro-shaped plugin demonstrates the `system.*` disclosure pattern the contract was designed around. The user-id-awareness twist is RoRoRo-native value generic TinyTask can't offer.

**The wall holds.** RoRoRo's Store binary remains macro-free. This plugin is a separately-distributed sideload — same architecture, same Store-policy posture as every other plugin. The fact that we (626 Labs) author it doesn't change its distribution shape.

**Sibling repo.** `github.com/estevanhernandez-stack-ed/rororo-ur-task` (TBD on exact slug — alternatives: `rororo-ur-task-plugin`, `rrr-ur-task`).

---

## Backlog

### 2. Session Stats

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

### 3. Discord Rich Presence

**Pitch.** Pushes "Playing Pet Sim 99 with N alts via RoRoRo" to the user's Discord Rich Presence whenever accounts launch. Clan-visible — every clan member sees who's farming right now, organically demos RoRoRo + the plugin system to non-RoRoRo Discord members.

**Capabilities declared.**

- `host.events.account-launched`
- `host.events.account-exited`

Needs `DiscordRichPresence` SDK (or `Discord.GameSDK`) as a NuGet dep. No UI contribution to RoRoRo's window — the presence shows up in Discord, not RoRoRo. Demonstrates external-integration plugin shape.

**Marketing angle.** The strongest viral surface in the backlog — clan members see RoRoRo named in their friends' Discord status without anyone selling it. Aligns with the "brand spreads for free" thesis.

---

### 4. Auto-Relaunch on Exit

**Pitch.** Watches `account-exited`; calls `RequestLaunch` against the same account id. Closes the farming-alt-crashed loop. Clan members lose less time when an alt crashes mid-session.

**Capabilities declared.**

- `host.events.account-exited`
- `host.commands.request-launch`
- `host.ui.row-badge` — show "auto-relaunch ON" per account.

**Risk.** A user who deliberately exits an alt gets their alt re-launched against intent. v1 mitigation: opt-in (default off), config via plugin's own UI since click handlers are stubbed. v1.5 (with bidirectional UI) gets a tray toggle per account.

**Why backlog over v1.4.** Solves a real clan pain point but overlaps conceptually with RoRoRo Ur Task (both touch the "I want to keep farming without supervision" surface). Better to ship Ur Task first, see whether crash-relaunch becomes the next ask.

---

## Not a plugin

### Clan Tracker

**Originally listed here.** Pitched as a plugin that would embed the Pet Sim 99 clan-tracker HTML page (clan placement, player status within clan) inside RoRoRo via WebView2.

**Why it's not a plugin.** Didn't earn the plugin-status criterion. The pitch had no awareness of RoRoRo's runtime state — it was just a webpage viewer wrapped in plugin clothing. Plugin shape carries gRPC + install/consent overhead that's only worth paying when the work *actually* integrates.

**Where it lives instead.** Future in-app feature of RoRoRo itself. Clan placement + player status read straight from Este's existing tracker (in `~/Projects/`) into RoRoRo's main window. No plugin, no WebView2 embed inside a status panel, no gRPC plumbing — just a native panel populated from the tracker's data shape.

**Status.** Not v1.4 work. Captured here so the next /onboard or scope conversation surfaces it as a candidate in-app feature rather than re-resurrecting the plugin pitch.

---

## Cross-cutting notes

**Distribution.** Each plugin lives in its own sibling repo under `github.com/estevanhernandez-stack-ed/rororo-<name>-plugin`. Independent Velopack release pipelines. RoRoRo's installer never bundles plugin code (Store policy 10.2.2).

**Signing.** All 626 Labs-authored plugins sign with the 626 Labs LLC code-signing cert — **separate** from RoRoRo's Store cert. SmartScreen surfaces the publisher; cross-contamination is an immediate distribution-trust incident.

**Brand.** Each plugin's icon, install screenshots, and any in-plugin UI go through the `626labs-design` skill before ship. Programmatic placeholders disqualify per the SnipSnap retro rule.

**Test discipline.** Each plugin gets its own integration test against a real RoRoRo gRPC host (the v1.4 `ROROROblox.PluginTestHarness` pattern). The harness-blindspot follow-up (production-shape capability accessor) landed at commit 88b6364 — production-shape tests now guard against the bug #3 class of regression.

---

**A 626 Labs product · *Imagine Something Else*.**
