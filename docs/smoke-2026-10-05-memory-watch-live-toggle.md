# Smoke: the watch-memory toggle takes effect in this session

Branch `fix/memory-watch-live-toggle` (PR #225). The `App` wiring is not unit-testable — the race
lives in plain methods, the WPF handler does not — so this is what covers it. Ten minutes with
accounts running.

**Build under test:** `src\ROROROblox.App\bin\Release\net10.0-windows10.0.19041.0\ROROROblox.App.exe`,
stamped 1.32.0.0.

**Telling the builds apart.** Both builds write to the same `%LOCALAPPDATA%\ROROROblox` folder and the
same daily log, so every line's version stamp is the attribution: the Store install logs `v1.31.0`,
this build logs `v1.32.0`. Check `Get-Process ROROROblox.App | Select-Object Path` before trusting
any window.

## Before starting

1. Quit the Store build from the tray, so one instance owns the clients and the log is unambiguous.
2. Launch the branch build and launch two accounts from it.
3. **Mute the noise:** Settings → Alerts & memory → Alerts → "An account eats memory" — untick
   My channel, Clan channel and My phone. Leave Desktop ticked. This smoke deliberately forces
   warnings and there is no reason for the clan or a phone to receive them. Put them back at the end.

## The runs

### 1. The numbers are live (previously next-launch only)

With watching ON, set Memory → "Warn when one account passes (MB)" to a figure just under what a
client is actually using — the footer's live figure tells you, 500 is usually safe — and tab out of
the box to commit.

**Pass:** within about 30 seconds a desktop warning appears and the log carries a `v1.32.0 … memory
cap crossed` line. Before this change the new cap sat in the file until the next launch.

### 2. Off means off, now

Untick "Watch memory while accounts are running."

**Pass, three things:**

- No further memory warning ever arrives, however long the clients run. This is the bug Este hit.
- The memory chips and the tray's memory badge clear within a tick (30 s) rather than staying stuck
  on the last warning — that is `Stop()` retracting its snapshot.
- No new `v1.32.0 … MemoryWatchdog memory:` heartbeat appears. That summary only prints every 15
  minutes, so absence over a couple of minutes proves nothing; give it 20 if you want this one.

### 3. On means on, now

Re-tick the box. Raise the cap above current usage, tab out, then lower it under usage again so the
latch has a fresh crossing to find.

**Pass:** a warning fires again within ~30 s, with no restart anywhere in this run.

### 4. The toggle beats the tick

Tick and untick quickly, three or four times, ending unticked.

**Pass:** it ends off and stays off. A `Memory watchdog re-read dropped: the toggle moved while it
was in flight` line at Debug level is the generation counter doing its job, not a fault. The failure
this guards against is the opposite: ending unticked and a warning arriving 30 seconds later because
a stale read won.

## After

Blank the cap box (back to auto), set the toggle where you actually want it, and re-tick the three
memory destinations from the setup step.

## Result: runs 1-3 pass, 2026-10-05

Run unattended on Nebuchadnezzar, 15:17-15:29, against the branch build (v1.32.0, logged as
`v1.32.0` beside the Store install's `v1.31.0`). The MCP server had dropped out of the Claude Code
session, so `626labs.ur-mcp.exe` was driven directly over JSON-RPC — same server, same host pipe.

**Setup differed from the plan in one way that made it safer.** This machine had *zero*
`RobloxPlayerBeta` processes: the three accounts showing `InGame` are on Este's other computer, since
presence is remote truth while the watchdog counts local clients. So nothing was disturbed, and the
`0 client(s)` heartbeat flagged in the audit is explained — not a bug. `ItsjustesteAgain` was
launched fresh (offline, healthy cookie, not the captcha'd `ELeonDog`) and landed in game in 24 s at
2,386 MB private. The alert destinations were left alone rather than muted; memory warnings route to
Discord "Mine" only on this install, so one alert reached that channel.

| Run | What was done | Result |
|---|---|---|
| 1. Numbers are live | Hand-wrote `memoryCapMb: 1000` at 15:19:27 | **Pass.** Cap crossed at 15:19:57 — one sample tick, no restart. This used to need a relaunch. |
| 1b. Confound control | Re-armed (cap 20000), then cap 1000 again at 15:21:54 | **Pass.** Crossed at 15:21:57. Proves a low cap reliably crosses in ≤30 s while watching is on, so run 2's silence means the gate and not a stuck latch. |
| 2. Off means off | Re-armed, set `memoryWatchdogEnabled: false` at 15:23:37, then cap 1000 at 15:24:48 | **Pass.** Zero crossings in 150 s, against ≤30 s twice while on. Silent on all three axes for the whole 15:23:37-15:27:41 window. |
| 3. On means on | Set `memoryWatchdogEnabled: true` at 15:27:41 | **Pass.** Cap crossed at 15:28:27, projection at 15:28:57. No restart anywhere in the run. |
| 4. The UI toggle saves and nudges | Clicked the checkbox for real, both ways, 15:58-16:01 | **Pass.** Untick wrote `memoryWatchdogEnabled: false`; re-tick wrote `true`. Driven through UI Automation to open Settings and scroll the control into view, then a synthesized mouse click on the control itself. The fast-flip race is left to the unit test; two clicks 30 s apart cannot distinguish a nudge from the tick that follows it. |

The session log carries no `[ERR]`, no `Couldn't refresh the memory watchdog settings`, and no
`re-read dropped`. The only warnings are the four deliberate crossings and an unrelated orphaned-plugin
sweep. Settings were restored and diffed byte-for-key against the pre-run backup: identical.

**Not covered here:** the snapshot retraction's visible half. `Stop()` clearing the snapshot is unit
tested, but whether the chips and the tray badge visibly clear needs eyes on the window.

## Two things the run turned up

1. **The tray balloon ignores both the destinations and the cooldown.** `WireMemoryWarningTray`
   subscribes to `PressureCrossed` and calls `ShowMemoryWarning` with no destination check and no
   mute check, so a memory crossing always raises a Windows balloon — with the stock notification
   sound — whether or not "Desktop" is ticked for that row, and whether or not `AlertRouter`'s
   five-minute cooldown has elapsed. Measured: at 15:21:57 the dispatcher logged "routed nowhere"
   under the cooldown while the tray path fired anyway. On this install memory warnings route to
   Discord "Mine" alone, so every desktop balloon Este has seen for memory came from a path he has
   no switch for. That is the sound complaint and the cadence complaint in one line of wiring, and
   it belongs in the cycle.
2. **Nothing logs when the gate flips.** "Off" had to be inferred from the absence of crossings. One
   Information line when the gate opens or closes would make this smoke, and any future support
   question, a single grep.
3. **F-102 is alive again in the memory checkbox.** `MemoryWatchdogEnabledToggle` is wired to
   `Click`, and `TogglePattern.Toggle()` — the only pattern a CheckBox exposes, and the one every
   assistive technology, voice access and automation path uses — raises `Checked`/`Unchecked` and
   never `Click`. Measured: a UIA toggle flipped the box to Off and reported Off to automation while
   `settings.json` still read `true`, so nothing saved and the gate never moved. A real mouse click
   on the same control saved correctly. This is exactly the defect F-102 fixed for the streamer-mode
   toggle (which was moved to two-way binding for this reason, `SettingsPage.xaml.cs` ~line 130); the
   memory toggle never got the same treatment, and on the evidence of this page's markup neither did
   the other `Click`-wired checkboxes. A screen-reader user turning memory watching off is told it is
   off while it keeps running. **Fix belongs in the v1.33 cycle, with a fence test so the next
   `Click`-wired toggle cannot ship.**

Two smaller notes from driving the UI. The control's left padding does not hit-test — a click 11 px
left of the tick did nothing, which is a WPF template root with no `Background` and worth knowing
before anyone writes a UI test. And there is no visible way back to the accounts page once a nav page
is open: Escape walks Settings → Games and stops, and the nav list has no home entry. That may be an
affordance I failed to find rather than a missing one, but it is worth a look during consolidation.
