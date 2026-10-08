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

**Result 2026-10-07: PASS.** Desktop unticked for memory; a 2-account crossing at 18:46:43 logged "routed nowhere". Cooldown ruled out rather than assumed — account `9958ba45` had never been sent an alert that day, so it had no quiet period to be inside. Re-ticked, and 18:31:22 routed to Local. Before this cycle the balloon went out regardless.

### 2. It is our balloon, and it is themed

With Desktop ticked, force a crossing and look at what appears.

**Pass:** a themed window in the app's own palette — not the Windows shell balloon. Readable, nothing
clipped, title and body both present.

**Result 2026-10-07: PASS.** Themed window, not the shell balloon. Seen single-account ("PapasbbBri — memory warning") and grouped ("2 accounts — memory warning").

### 3. A single-account alert clicks through

Force a crossing that names exactly one account, then click the notification.

**Pass:** the main window comes forward with that account's row in view. Item 4 restored this after
item 3 broke it invisibly.

**Result 2026-10-07: PASS.** Driven by a synthesised click, because the balloon handles `MouseLeftButtonUp` on its own surface rather than exposing an invokable pattern, and it lives only 8 seconds (`BalloonMilliseconds`) — an agent round-trip costs more than the whole window, so trigger, detect and click had to happen inside one script. Foreground went from `…Visual Studio Code` to `RoRoRo`. The script's own verdict line said "weak" and was WRONG: it tested `$before -notmatch 'RoRoRo'`, and the VS Code title contains "ROROROblox", which matches case-insensitively.

### 4. The sound is a choice

Settings → Alerts → Alert sound. Try each of the three, forcing a crossing after each.

**Pass:** **RoRoRo chime** plays our own short sound. **Windows default** plays the system asterisk.
**Silent** plays nothing — and still draws the notification and still colours the tray. No restart
needed between changes.

**There is no way to hear a sound from Settings.** `OnAlertSoundChanged` only saves; the string
"What a desktop alert sounds like" is the picker's accessible name, not a play button. So this run
cannot be done from the Settings page at all — an alert has to actually fire. That is a gap in item
5 worth closing: a user choosing between three sounds is choosing blind.

**A second noise-maker exists, and this setting does not govern it.** Ur Score plays its own sound
from its own process on its own metric alerts. Confirmed 2026-10-07: with RoRoRo set to
`WindowsDefault`, a metric alert still produced the chime (Ur Score's), while a memory alert
produced the asterisk (RoRoRo's). Set RoRoRo to Silent and the plugin will still make noise.
Product-level, not a v1.33 regression, but it will read as a bug to whoever hits it.

**Result 2026-10-07: FAIL, with two findings and one correction to the diagnosis.**

Windows default works — the asterisk played on a memory alert at 18:31:22. What failed is that this run cannot be performed from Settings at all, and that a second process makes noise RoRoRo does not govern (both recorded above).

**Two wrong diagnoses, recorded because the reasoning is the lesson.** First: the chime cannot play, because `SoundPlayer.Play()` is async and `PlayChime` disposes the player and the stream immediately. The mechanism is real and the conclusion was wrong — the chime plays. Second: the setting never reaches the player. Also wrong. The truth was that Ur Score had made the chime heard at 18:22 from its own process while RoRoRo was set to WindowsDefault. Both errors were inference from "this could fail" to "this is the failure", settled only by a test with an unambiguous answer.

### 5. The quiet period holds, and Every time means every time

Set "How often at most" to 5 minutes. Force two crossings about a minute apart.

**Pass:** the second produces nothing. Then set it to **Every time** and repeat: both arrive.

**Note on a false pass:** the first attempt at this during the build was worthless because the
re-arm cycle was shorter than the quiet period, so every crossing legitimately fired. Keep the
period comfortably longer than the gap between your two crossings.

**Result 2026-10-07: suppression half PASS, "Every time" half NOT RUN.** Measured by accident and better for it: a forced crossing at 18:36:22.270 was held against a send at 18:31:22.273 — **4:59.997**, suppressed by three milliseconds. That is the `>=` boundary behaving exactly as item 2 specified. A second account in the same event was held at 3:59.993.

### 6. Idle means in-game idle — Este's ruling

Set the idle warn threshold low (1 minute). Leave one account **in a game** and one **parked at the
Roblox home screen**, then stop touching the machine.

**Pass:** the in-game account raises an idle alert; the parked one **never does**. The row chip
appears on both regardless, which is the documented behaviour and what the hint now says.

**Known consequence to confirm rather than be surprised by:** an account whose Roblox presence
privacy hides it also raises nothing, because we cannot see that it is in a game. Nothing on screen
explains this.

**Result 2026-10-07: NOT RUN — and the two failed attempts are the useful part.**

This is the only run that checks the ruling Este made today, and it is the hardest to stage.

**The threshold cannot be set from a file.** `InitializeIdleSettingsAsync` runs at startup and from the picker handler, and nowhere else — there is no periodic re-read, unlike the memory numbers. A `settings.json` edit never reaches `ActivityMonitor.WarnThreshold`. It has to be the picker.

**And the value has to be one the picker offers.** The store accepts any value above zero; the picker's lowest option is 10 minutes. A written-in `1` was silently rewritten to 15 when the Settings page next painted. Two separate reasons the first attempt was dead, and only the second was visible.

**The parked account must be LAUNCHED, not merely saved.** `ActivityMonitor` keeps a record per account from `OnAccountLaunched` to `OnAccountExited`; an account with no running client never has a crossing raised for it, so "it didn't alert" would be true for the boring reason instead of the interesting one — a vacuous pass of exactly the kind this cycle kept producing.

**The setup that would actually prove it:** one account in a game; one launched and running, parked at the Roblox home screen; the picker at 10 minutes; ten minutes of no input from anyone. Pass is the in-game one alerting and the parked one staying silent.

### 7. The fullscreen question — this one goes in the release notes either way

Put a Roblox client **fullscreen** and force a crossing.

**Pass is either answer, as long as it is recorded:** the themed balloon paints over the fullscreen
client, **or** it does not. If it does not, it goes in the release notes as a known issue rather than
being quietly hoped away. Try borderless windowed too if the client offers it, since the answer may
differ.

**Result 2026-10-07: PASS. The balloon paints over a fullscreen client.** Captured: a Pet Sim 99 client filling the whole 3440×1440 primary, with the balloon bottom-right reading "PapasbbBri — memory warning / PapasbbBri — 3.2 GB · Recycle suggested". Legible against saturated white, pink and yellow, which is close to a worst case. **Nothing for the release notes.**

**Two false starts worth keeping.** The first attempt measured a client at `782,260 816x638` — windowed, because the clients had been relaunched since being fullscreened. The second measured `-8,-8 3456x1408`: maximized, not fullscreen, the bottom ~40px taskbar strip uncovered. A geometry guard refused both rather than returning a pass on the easy case. The third attempt then fired no alert at all, because the reset cap was hardcoded at 3200 and the fullscreen client had grown to 3232 — the latch never reset. Caps are computed from live usage now.

### 8. The About page signs itself — nobody has seen these

Open About.

**Pass:** the **626Labs LLC** wordmark sits at the foot of the page, always visible, crisp, clear of
the text above it. Then click the version number six or seven times.

**Pass:** **Koii 4 eva** fades in, in Este's hand, filled with the cyan-to-magenta gradient and
carrying the magenta glow.

**The two judgement calls most likely to want nudging** — say so rather than accepting them: the
wordmark's clear space (currently 32px above) and the egg's height (currently 26px).

**Result 2026-10-07: PASS, with one content fix.** Both marks render: the 626Labs LLC wordmark at the foot, and the egg revealing the hand-written mark with the duo gradient and the magenta glow. Este: the clan is **K0ii**, with a zero — misspelt for a long time. Regenerated (`96a6b1d`); the accessible name moved with it, and the generator notes the character is a zero so nobody corrects it back.

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

**Result 2026-10-07: PASS.** Este's request from the run: the Settings nav item **Appearance should read "Language and Appearance"** — language is important and nobody hunting for it thinks to look under Appearance. Not actioned; it is a nav rename plus seven catalogues, and new scope on a closed branch.

### 10. A toggle tells the truth to everything that asks

**Corrected 2026-10-07, mid-run: the original instruction here was a test that could not fail.**
It said to use the keyboard rather than the mouse. F-102's own finding records that "a mouse click
or a Space keypress on a focused box both raise `Click` and work correctly" — both paths always
worked. The path that was broken is **UI Automation**, which is what a screen reader uses and what
no human can produce by hand.

Driven instead through `TogglePattern.Toggle()` by a script, reading `settings.json` before and
after and restoring the toggle at the end.

**Pass:** the UIA state and `settings.json` move together. Before item 7 the box would report its
new state while the file never changed.

**Result 2026-10-07: PASS.** `BEFORE On / True` → `AFTER Off / False` → `RESTORED On / True`.

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
