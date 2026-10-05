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

## Result

Not yet run. Fill in below with the date, the build, and what each run did.
