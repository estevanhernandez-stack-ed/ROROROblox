# Smoke: the v1.33 alerts cycle

Branch `cycle/v1.33-alerts` (PR #229). **Pass conditions are written before the run** and the results
are filled in as it happens — a smoke doc written afterwards is a changelog.

Nine of the eleven built items are covered by fences and unit tests. This covers what a test cannot
reach: a Windows notification actually painting, a sound actually playing, a theme actually looking
right, and two drawn marks nobody has laid eyes on.

**Build under test:** `src\ROROROblox.App\bin\Release\net10.0-windows10.0.19041.0\ROROROblox.App.exe`.

**Telling the builds apart.** The Store install and this build write to the same
`%LOCALAPPDATA%\ROROROblox` folder and the same daily log, so the version stamp on each line is the
attribution. Run `Get-Process ROROROblox.App | Select-Object Path` before trusting any window — on
2026-10-07 the Store build 1.32.0.0 was the one running.

## Before starting

1. **Quit the Store build from the tray**, so one instance owns the clients and the log is
   unambiguous. Closing only Roblox is not enough — both processes must die for `MutexHolder` to
   re-acquire cleanly.
2. Launch the branch build and launch two accounts from it. One of them should end up **in a game**,
   not parked at the Roblox home screen: several runs below depend on it, and an account that cannot
   join directly needs Follow instead.
3. **Mute the noise.** Settings → Alerts → for "An account eats memory", untick My channel, Clan
   channel and My phone; leave Desktop ticked. This smoke deliberately forces warnings and the clan
   does not need the test traffic. **Put them back at the end** and note here that you did.
4. Note the starting theme, so step 9 can restore it. A previous session left the theme on flatline
   after claiming a byte-identical restore.

## The runs

### 1. Desktop is a destination like any other — the bug that started the cycle

Settings → Alerts → "An account eats memory" → untick **Desktop**. Force a crossing (set the per-account
warn figure just under what a client is actually using; the footer's live figure tells you).

**Pass:** **no** notification appears, and the log shows the dispatcher routed it nowhere. The tray
icon still colours. Before this cycle the balloon went out regardless — measured on 2026-10-05, where
the dispatcher logged "routed nowhere" while the popup appeared anyway.

**Confound control:** re-tick Desktop and force another crossing. A notification must appear. If
nothing appears either way, the crossing is not firing and run 1 proved nothing.

**Result:**

### 2. It is our balloon, and it is themed

With Desktop ticked, force a crossing and look at what appears.

**Pass:** a themed window in the app's own palette — not the Windows shell balloon. Readable, nothing
clipped, title and body both present.

**Result:**

### 3. A single-account alert clicks through

Force a crossing that names exactly one account, then click the notification.

**Pass:** the main window comes forward with that account's row in view. Item 4 restored this after
item 3 broke it invisibly.

**Result:**

### 4. The sound is a choice

Settings → Alerts → Alert sound. Try each of the three, forcing a crossing after each.

**Pass:** **RoRoRo chime** plays our own short sound. **Windows default** plays the system asterisk.
**Silent** plays nothing — and still draws the notification and still colours the tray. No restart
needed between changes.

**Result:**

### 5. The quiet period holds, and Every time means every time

Set "How often at most" to 5 minutes. Force two crossings about a minute apart.

**Pass:** the second produces nothing. Then set it to **Every time** and repeat: both arrive.

**Note on a false pass:** the first attempt at this during the build was worthless because the
re-arm cycle was shorter than the quiet period, so every crossing legitimately fired. Keep the
period comfortably longer than the gap between your two crossings.

**Result:**

### 6. Idle means in-game idle — Este's ruling

Set the idle warn threshold low (1 minute). Leave one account **in a game** and one **parked at the
Roblox home screen**, then stop touching the machine.

**Pass:** the in-game account raises an idle alert; the parked one **never does**. The row chip
appears on both regardless, which is the documented behaviour and what the hint now says.

**Known consequence to confirm rather than be surprised by:** an account whose Roblox presence
privacy hides it also raises nothing, because we cannot see that it is in a game. Nothing on screen
explains this.

**Result:**

### 7. The fullscreen question — this one goes in the release notes either way

Put a Roblox client **fullscreen** and force a crossing.

**Pass is either answer, as long as it is recorded:** the themed balloon paints over the fullscreen
client, **or** it does not. If it does not, it goes in the release notes as a known issue rather than
being quietly hoped away. Try borderless windowed too if the client offers it, since the answer may
differ.

**Result:**

### 8. The About page signs itself — nobody has seen these

Open About.

**Pass:** the **626Labs LLC** wordmark sits at the foot of the page, always visible, crisp, clear of
the text above it. Then click the version number six or seven times.

**Pass:** **Koii 4 eva** fades in, in Este's hand, filled with the cyan-to-magenta gradient and
carrying the magenta glow.

**The two judgement calls most likely to want nudging** — say so rather than accepting them: the
wordmark's clear space (currently 32px above) and the egg's height (currently 26px).

**Result:**

### 9. All four themes, and the German and Polish Alerts page

Walk the theme picker through **Brand, Midnight, Magenta Heat and Flatline**. On each, look at the
Alerts section and the About page.

**Pass:** the balloon, the wordmark and the egg are all legible on every ground. The wordmark takes
the theme's own text ink; the egg's gradient takes the theme's own cyan and magenta — so under
Flatline the gradient will be achromatic while its glow stays brand magenta, which is deliberate.

Then switch the UI language to **German** and **Polish** and return to Alerts.

**Pass:** nothing is cut off. Measured beforehand: every language maxes at **2 lines** in the 180px
kind column and nothing reaches 3, so this is not about overflow. What needs eyes is the **rhythm** —
within one language some rows are one line and some are two, which is less even than English's
uniform single line. If it looks wrong, `docs/cycles/2026-10-05-alerts/6b-wrap-measurement.txt` names
exactly which strings to shorten.

**Restore the starting theme and language, and confirm here that you did.**

**Result:**

### 10. A toggle tells the truth to everything that asks

With accounts running, untick **"Watch memory while accounts are running"** using the keyboard
(Tab to it, press Space) rather than the mouse.

**Pass:** warnings stop, and `settings.json` shows `memoryWatchdogEnabled: false`. Item 7's conversion
means the keyboard and automation paths now save; before it, only a mouse click did.

**Result:**

## Not covered here, and why

- **The plugin launch reason.** `RORORO_LAUNCH_REASON` is covered by tests at the only seam that can
  observe it; a real child cannot be asked what its environment was. It needs Ur Score to read it,
  which waits on this reaching main.
- **Phone and Discord legs.** Deliberately muted for this run. They were proven live on 2026-09-05
  and this cycle did not change the senders — only what decides whether to call them, which runs 1
  and 5 cover.
- **Colour emoji.** Dropped (item 9), with the measurement recorded there.

## Afterwards

- Put the memory alert's Discord and phone destinations back.
- Confirm the theme and UI language are as they started.
- Fill in every **Result** above, including the ones that passed, and especially run 7's answer.
