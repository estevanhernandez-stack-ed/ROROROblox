# RoRoRo v1.39.0.0 — release notes

> **Edit log.** A MINOR bump from 1.38.0.0. The playbook's table would allow a BUILD bump for a
> release of fixes; MINOR was chosen because two changes are ones a user can see and may rely on (a
> close request from another program now quits RoRoRo, and a second accounts file appears on disk),
> and the owner approved the number. All six fixes came out of the 2026-11-16 quality and stability
> sweep (`docs/cycles/2026-11-16-quality-and-stability/board.md`, items 1, 2, 3, 4, 5 and 7) and
> were built as one run (`docs/cart-runs/2026-11-16-v139-stability/`).
>
> 2026-11-16, first draft. **Known issues were re-verified, not carried blind.**
> `git diff v1.38.0.0..HEAD` touches ten shipped files: the account store (`AccountStore.cs`), the
> process tracker and its event args, the UI dispatcher wrapper, the main window's close handling
> with two new small classes, the main view model's exit and session-expired handlers, the memory
> watchdog, and five lines of wiring in `App.xaml.cs`. Four carried issues sit in touched areas.
> "The crash line hasn't been seen after a real crash" still applies, and one false source of that
> line is now closed, so the paragraph says both. "None of the three health badges has been seen on
> a real account" still applies; what an expired session does to the row changed, so the paragraph
> gains a sentence. "The idle-time fix hasn't been watched live" still applies: the activity
> monitor gained one guard and nothing else. "Nobody has watched Windows restart RoRoRo yet" still
> applies. Every other carried issue touches no code this release changed, so they carry as
> written. **One line was dropped from v1.38's text, not from this list:** v1.38 said ending RoRoRo
> from Task Manager "doesn't bring the line up"; that stays true, and the new paragraph on closing
> from outside covers it. Five new known issues, each a thing that was built and tested but not
> watched on a real machine, or a limit that outlives the fix.
>
> **Nothing in this release was run as a live build before the first draft.** A dev build has no
> data-folder override and uses the owner's real accounts file. The owner smoked one on 2026-11-16
> (`submission-packet-1.39.0.0.md`, "Live smoke"): the X, Alt+F4 and the taskbar's Close hid the
> window, Task Manager's End task ended RoRoRo cleanly, the backup appeared, and a quit with an
> account running left its client up. The Task Manager known issue was rewritten to match.
>
> There is no clan post file this release, as at v1.37 and v1.38. The short list below is still
> written to travel.

---

# RoRoRo v1.39.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/RORORO-win-Setup.exe)

## Short list, for the GitHub release and the Discord post

```
• Your saved accounts are safer. RoRoRo now keeps a backup of the accounts file and repairs itself from it if the file is ever damaged, by a power cut for example.
• Quitting RoRoRo, or shutting down your PC with Roblox open, no longer comes back as "RoRoRo closed unexpectedly".
• Launching an account that's already running no longer makes its row say it stopped.
• An account whose sign-in expires in the middle of a game no longer stays stuck on "In game".
• When another program asks RoRoRo to close, it closes. The X still sends it to the tray.
• The tray no longer warns about memory when no Roblox is running.
• Quit RoRoRo before updating from the Store: right-click the tray icon, then Quit.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### Your saved accounts have a backup

RoRoRo keeps your saved accounts in one encrypted file. Until now there was only that one copy,
and a save cut short at the wrong moment (a power cut, a hard crash) could have left it empty. An
empty file looked the same as "no accounts yet", so the next change would have made the loss
permanent. Nobody reported this happening. The code allowed it, and from v1.36 on the file was
being rewritten every few minutes, so the window was wide.

Three things changed:

- **Every save is finished properly** before the new file replaces the old one.
- **The previous good copy is kept** beside it as `accounts.dat.bak`, encrypted the same way.
- **If the accounts file is missing, empty or unreadable, RoRoRo loads the backup** and repairs the
  file from it straight away.

The file is also written far less often now: a refresh of Robux and friends that changed nothing
no longer rewrites it.

Removing an account removes it from the backup too.

### A normal quit no longer reads as a crash

v1.38 added a line that tells you when RoRoRo closed unexpectedly. One way to get that line was
not a crash at all: a Roblox window closing in the same moment RoRoRo was shutting down, which is
what happens when you shut down your PC with Roblox open. That is fixed. If you saw the line after
an ordinary shutdown, that was this.

### Rows that stay true

- **Launching an account that's already running** (from its panel, Play as, or Follow) used to
  confuse RoRoRo when the older window closed: the row said the account had stopped, and Stop did
  nothing, while the newer window was still up. The row now follows the newer window.
- **A sign-in that expires in the middle of a game** used to leave the row stuck on "In game"
  after the game was long gone. The row now shows the expired sign-in, and History records when
  the game actually closed.

### Closing RoRoRo from outside

The X on RoRoRo's window sends it to the tray. That's deliberate, and it hasn't changed, and
neither has Alt+F4 or Close on the taskbar.

What changed is a close request from another program. Task Manager's End task and `taskkill`
without `/F` used to hide the window and leave RoRoRo running. They now quit RoRoRo cleanly, the
same way Quit on the tray does.

### No memory warning with nothing running

The tray icon could show its memory warning while RoRoRo had no Roblox open at all, because your
PC was busy with something else. The warning is about Roblox clients, so it now needs at least one
running.

## Known issues going into the next update

**Nobody has seen the backup rescue a real damaged file.** Loading, repairing and scrubbing were
each tested against damaged files in a test folder. No real install has been through it.

**If you delete `accounts.dat` by itself to start over, your accounts come back** from the backup.
To reset RoRoRo, delete the whole folder, as the privacy policy describes.

**You still aren't told when an account with an expired sign-in drops out of its game.** The row
and History are now right, but no alert fires, and auto-rejoin can't bring it back until you sign
in again.

**"Updated … ago" on a row can read up to six hours just after you start RoRoRo,** even though the
numbers are fresher than that. It corrects itself within a few minutes.

**Closing from Task Manager has been seen once, on one PC.** End task on the open window quit
RoRoRo cleanly, with no crash line on the next start. If RoRoRo is in the tray, Task Manager lists
it as a background process and simply ends it, as before.

**The crash line hasn't been seen after a real crash.** Its rules were tested and it was looked at
in captures in English and French. One false cause of it is fixed in this release. It also waits
for the main window, like the known-issues notice: if you keep RoRoRo in the tray, you'll see
either one the next time you open the window.

**The launch messages added in 1.38 haven't been seen against live Roblox.** Nobody forced a real
rate limit or outage. If Roblox signals one in a way RoRoRo doesn't recognise, you get the general
"Couldn't sign this account in to Roblox" message with its detail.

**The Store-edition fix hasn't been tried on a PC that only has the Store edition of Roblox.** The
check, the dialog and the launch path were each tested, and the check was run against real Windows
handlers, but nobody has walked the whole thing on such a PC. If the dialog shows when it shouldn't,
or doesn't when it should, an issue with the Diagnostics bundle helps a lot.

**Nobody has seen the rating line on a real Store install yet.** It was tested and looked at in
captures. If you leave RoRoRo running for weeks, you'll see it at your next start, not on the third
day. A start that reports a crash doesn't also ask for a rating.

**The FPS caps added in 1.37 haven't been watched on a live client.** They go through the same
settings write as 45, 90 and 165.

**You still can't type your own FPS cap.** The list is longer, not open-ended.

**None of the three health badges has been seen on a real account yet.** Session expired, Needs
verification and Limited by Roblox were each tested against Roblox's documented responses and on
screen, but nobody had an account in any of those states to try them on. That includes this
release's change to what an expired sign-in does to a row that was in a game. If one shows up wrong
for you, an issue with the Diagnostics bundle helps a lot.

**The idle-time fix hasn't been watched live.** It was tested against a replay of the reported switch
between windows, not on a real one.

**Dates older than a week in the Library show in English format** ("Oct 24") whatever language you
use. Recent ones ("2 hours ago") are translated.

**The keyboard shortcut list on the About page is in English** in every language.

**A plugin's alert sound is not governed by yours.** Choosing Silent silences RoRoRo. A plugin that
makes its own noise carries on, because it's a separate program and nothing in RoRoRo's settings
reaches into it. Of the catalogued plugins, **Ur AFK is the only one that makes a sound**, the
Windows Exclamation when its own "sound on grab" is ticked. Its own switch is the way to stop it.
([#230](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/230))

**An account with Roblox presence privacy on will not get idle alerts.** Idle alerts need RoRoRo to
see that an account is in a game, and presence privacy hides exactly that. Settings explains it under
the idle controls.

**Plugin updates are offered on the direct-download build only.** If you installed RoRoRo from the
Microsoft Store, the Plugins window never lists available plugins or offers updates for the ones you
have, because a Store app isn't allowed to fetch a list of installable programs from a server. You
can still install and update a plugin by pasting its install link in that window.

**A verification page that appears mid-game is recovered late.** Roblox sometimes puts the "Verifying
you're not a bot" page up on an account that's already in a game. While that page is up, Roblox still
reports the account as in the game, so RoRoRo can't tell anything is wrong until Roblox idle-kicks it
behind the page, up to 20 minutes later. Then auto-rejoin brings it back as usual.

**A game you start outside RoRoRo can still land in a tray process** in the two minutes before that
process is closed. RoRoRo's own launches are covered.

**Nobody has watched Windows restart RoRoRo yet.** The registration is in place and the carry-on
after a restart is tested, but a real crash restart or a Store-update restart hasn't been observed.

**A restart while Roblox holds the multi-instance lock still asks.** The automatic restart skips the
leftover-games dialog, but not the one that appears when Roblox itself is holding the lock RoRoRo
needs.

**An unattended rejoin can stop at a Windows dialog.** If Roblox is mid-update when auto-rejoin
relaunches an account, the "Roblox isn't installed" message can be waiting for you when you get back,
and that account stays out until you close it.

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed.

**Nobody has seen a large group in the alert window.** If a group of four or more looks wrong on your
desktop, that's worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other. Rare, harmless, and on the list.

## Compatibility

**No migration.** Your accounts, notes, history and settings carry over. The backup appears the
first time RoRoRo saves the accounts file after you update.

**One new file:** `accounts.dat.bak`, next to `accounts.dat`, encrypted the same way. The privacy
policy lists it.

**No new network calls.**

**Plugins:** the plugin contract did not change. One behaviour did: when an account is launched
again while it is already running, a plugin is no longer told the account exited when the older
window closes. It still is when the account really stops.

**Scripts that close RoRoRo:** a polite close from another program (`taskkill` without `/F`) now
quits RoRoRo. It used to hide the window and leave RoRoRo running.

**Updating from the Store:** quit RoRoRo first (right-click the tray icon, then Quit). An update that
lands while RoRoRo is running can fail to install.

## Other channels

Also on the [Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). The Store build
updates itself through the Store.

A signed sideload MSIX ships with each release for anyone who prefers it. The dev certificate did not
rotate this release, so nobody needs to re-import anything.

**If you only ever installed from the Store,** your saved accounts and settings live inside the app's
own package folder, and uninstalling RoRoRo removes them. If you've ever run the direct download on
that PC, they live in `%LOCALAPPDATA%\ROROROblox` and survive an uninstall. The privacy policy
explains how to tell which one you have.

## Issues, ideas

[Open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues): bugs, requests,
anything that reads wrong. [Privacy policy](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/PRIVACY.md).
