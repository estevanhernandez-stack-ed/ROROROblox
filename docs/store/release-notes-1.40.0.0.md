# RoRoRo v1.40.0.0 — release notes

> **SHIPPED 2026-11-18, both channels.** Tag `v1.40.0.0` on `28c8f9a`; the body below the `---` is
> the GitHub release's notes, published as Latest at 17:27 CST after the Store went live. Drafted
> 2026-11-17 on `release/1.40.0.0` from `git diff v1.39.0.0..HEAD` at `f1d0eeb`; this banner said
> "PREPARED, NOT SHIPPED" until then.
>
> **Edit log.** A MINOR bump from 1.39.0.0: the release adds something a user can see and click (the
> update button) on top of the stability fixes, and two more things a user may rely on (an outside
> close during startup, two new file names a damaged install can show). **The number is assumed from
> the branch name; the owner has not confirmed it in this draft.**
>
> What is in it, from `git log v1.39.0.0..HEAD` (32 commits): the exit fix (PR #293), the
> dev-build test folder (PR #294, not user-visible), the eight items of the second stability run
> (`docs/cart-runs/2026-11-17-stability-part-two/`) and the update button
> (`docs/cart-runs/2026-11-17-update-button/`). The diff is 127 files, +8,633/-272, of which 57
> files, +2,450/-236 are shipped source and resources under `src/` outside the test project.
>
> **Known issues were re-verified, not carried blind.** Method: v1.39's list read against the
> shipped-file list of the diff (`git diff --name-only v1.39.0.0..HEAD -- src ':!src/ROROROblox.Tests'`)
> and, where a file was touched, against its hunks.
>
> - **Dropped, fixed in this release:** "You still aren't told when an account with an expired sign-in
>   drops out" (`f72ba48`); "Dates older than a week show in English format" (`34d1986`, in
>   `AccountSummary.RelativeAgo`, the Squad Launch window and History); "The keyboard shortcut list on
>   the About page is in English" (`34d1986`).
> - **Rewritten:** "Closing from Task Manager has been seen once, on one PC". v1.39's smoke saw End
>   task quit RoRoRo cleanly; this cycle's live runs used `taskkill` without `/F` only. The paragraph
>   says both and claims neither more broadly. "The crash line hasn't been seen after a real crash"
>   keeps its text and gains the exit-time note below. "Nobody has seen the backup rescue a real
>   damaged file" gains the vault-shape rule. "None of the three health badges has been seen on a real
>   account" is unchanged in text; the expiry alert is covered by its own paragraph.
> - **Carried as written, untouched by the diff:** "Updated … ago" can read up to six hours; the
>   launch messages added in 1.38; the Store-edition Roblox check; the rating line; the FPS caps
>   added in 1.37; no typed FPS cap; the idle-time fix; the plugin sound note; presence privacy and
>   idle alerts; plugin updates on the direct download only; a verification page mid-game; a game
>   started outside RoRoRo landing in a tray process; Windows restarting RoRoRo; a restart while Roblox
>   holds the lock; an unattended rejoin stopping at a Windows dialog; the two spots that wait for the
>   next open on a language change; a large group in the alert window; two groups alerting one account.
>   (The memory section in Settings and the Discord presence line sit in files this diff did not
>   touch.) "If you delete `accounts.dat` by itself, your accounts come back from the backup" is
>   unchanged: the load order in `AccountStore.cs` kept its missing, empty or unreadable rule.
> - **New, from the two run reports' "left open" and "not exercised" lists:** the update check on a
>   Store install, the direct-download button, a lock on the saved-accounts file at startup, a
>   wrong-kind row in a file, the compact-mode button, a running direct-download build, and the
>   launch-failure sentence's wording.
>
> There is no clan post file this release, as at v1.37 to v1.39. The short list below is written to
> travel.

---

# RoRoRo v1.40.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/RORORO-win-Setup.exe)

## Short list, for the GitHub release and the Discord post

```
• New Update available button in the main window. On a Store install, RoRoRo asks the Microsoft Store (through Windows) whether an update is waiting, and one click opens RoRoRo's Store page. It tells you about the next update, not this one. On the direct download it reads Quit to update once an update is downloaded.
• A damaged saved-games, private-servers, history or stats file no longer stops RoRoRo from starting. What can be read is kept. A damaged accounts file is restored from its backup and is never read as empty.
• Start Fresh, on the "saved accounts can't be unlocked" screen, checks again first and only sets the file aside. It never deletes it, and it says so if it can't move it.
• RoRoRo quits faster, and another program asking it to close now works while a startup screen is up too.
• An alt whose sign-in expired now raises an alert when it drops out.
• A launch that fails shows on its row instead of closing RoRoRo or leaving the row on "Launching...".
• The About page's shortcut list, and dates across RoRoRo, follow your language.
• Quit RoRoRo before updating from the Store: right-click the tray icon, then Quit.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### An Update available button

When an update is waiting, a button appears at the left of the buttons in the main window's header.

- **On a Microsoft Store install,** it reads **Update available**. RoRoRo asks the Microsoft Store,
  through Windows, whether a newer version is waiting: once after RoRoRo has started, then every six
  hours while it runs. One click opens RoRoRo's page in the Microsoft Store, and you install from
  there. RoRoRo downloads and installs nothing itself, and it sends nothing of its own in that
  question: Windows makes the request for the installed app, the way the Store app would. If the
  check fails for any reason, there is no button and nothing else changes.
- **On the direct download,** it reads **Quit to update** once the daily update check has downloaded
  a new version. Quit RoRoRo and open it again to finish updating. The click quits RoRoRo for you.

It starts with this version. If you are reading this because you just updated, the button couldn't
have told you about this update. It will tell you about the next one, and that matters because a
running RoRoRo can hold a Store update back: before this, nothing in the app said an update existed.

The button is hidden in compact mode, because the whole header is. It returns when you expand the
window.

### Quitting is quicker, and a close from outside works during startup

Every quit used to wait out a two-second cleanup that never finished. It no longer does. On one PC,
with a development build, a quit that took 2.5 seconds took about half a second (0.41 and 0.58
seconds in two runs).

v1.39 made a close request from another program quit RoRoRo instead of hiding it. That held once
RoRoRo was up. It now holds while a startup screen is showing too: the "leftover Roblox" dialog, the
first-run tour, and the others. `taskkill` without `/F` with the tour up quit RoRoRo in about a third
of a second in live runs. The X, Alt+F4 and the taskbar's Close still send RoRoRo to the tray.

One change you may notice on a first run: the main window now appears first and the tour opens over
it, where before the tour opened while the window was still being set up.

### Damaged files no longer stop RoRoRo from starting

A saved-games, private-servers, history, stats, plugin-consent or icon-cache file whose contents are
the wrong shape (an empty `{}`, a missing list) could crash RoRoRo on every start until someone found
and deleted the file. Now RoRoRo reads around it: good entries are kept, a bad entry costs that
entry, and a file with nothing usable reads as empty. A plugin whose consent entry is dropped asks
again.

The saved-accounts file is handled more carefully, because it is the one you can't recreate. A
damaged one, including one that opens but holds nothing it should, goes to its backup. If there is no
usable backup, RoRoRo shows the "saved accounts can't be unlocked" screen. It never reads a damaged
file as an empty account list, which the next save would have made permanent. An account row with a
missing name or avatar is repaired; one with no sign-in reads as signed out and asks you to sign in
again.

If RoRoRo can't read your saved accounts this time for a reason it didn't plan for, the list stays
empty for that session, the file is left alone, and the window says so: "Your saved accounts couldn't
be read this time. The file wasn't changed. Restart RoRoRo to try again."

### Start Fresh checks before it sets anything aside

On the "saved accounts can't be unlocked" screen, **Start Fresh** used to set the accounts file aside
and carry on, and on the next start the screen could come straight back. It now:

- **Checks again first.** That screen can also appear when a scanner or a sync tool is holding a
  healthy file for a moment. If the accounts read fine on the second look, nothing is set aside and
  you land on your accounts.
- **Only sets aside what still can't be read,** and never deletes it. Two file names can now appear
  in `%LOCALAPPDATA%\ROROROblox`: `accounts.dat.corrupt-<time>` and `accounts.dat.bak.corrupt-<time>`.
  They hold the same encrypted data as `accounts.dat` and its backup, they appear only after you
  press Start Fresh on a file that could not be read, and RoRoRo never reads or deletes them. Delete
  them yourself when you no longer want them. The privacy policy lists both.
- **Says so when it can't move a file.** The screen stays open and names the file, and nothing is
  deleted.

### An alert when an alt with an expired sign-in drops out

When an account's sign-in has expired (or Roblox has limited it), the row says so, and RoRoRo
stops polling it. Before, if its client then closed, nobody was told. Now the dropped-out alert fires
when its client exits, with the same wording, routing and repeat limits as any other drop. It stays
quiet when you closed the client yourself, when you launched the account again, and when Windows is
shutting down or signing you out.

### A failed launch shows on its row

A launch that failed before the Roblox client started used to close RoRoRo when it came from the
follow chip or a pasted link, and left the row on "Launching..." when it came from the Launch button.
The row now shows the failed-launch message with the kind of error. It shows the kind, not the full
text, because the full text can carry a folder path and a row can be on screen while you stream.

### The About page and dates follow your language

The About page's keyboard-shortcut list is translated, and dates and times on account rows, in
History and in the Squad Launch window follow the language you chose rather than your Windows
regional format. A French RoRoRo on an English-region PC read "Nov 3"; it now reads the French form.
In the six translated languages History's times show on a 24-hour clock. English is unchanged.

### Smaller

- A plugin that asks RoRoRo to stop a client that has already exited gets "nothing to stop" back
  instead of an error.
- An account list left empty by a read failure says why (see above).

## Known issues going into the next update

**The Store update check has not been run on a Store-installed build.** It can't be before a release
that contains it: the sideload package can't be installed beside the Store one. The button, the click
that opens the Store, and the direct-download "Quit to update" were each tested, and opening the
Store link was tried by hand, but nobody has seen Windows answer "an update is waiting" to RoRoRo. If
the call doesn't work from the packaged app, no button shows and nothing else breaks. The first Store
release after this one is the real test.

**Whether a downloaded direct-download update installs when RoRoRo quits or the next time it
starts isn't measured.** That is why the button says to quit RoRoRo and open it again.

**A direct-download RoRoRo you leave running learns about an update only the next time it starts.**
Its check runs once after startup, at most once a day.

**The update button isn't shown in compact mode.** The whole header is hidden there.

**An alt with an expired sign-in raising the dropped-out alert hasn't been seen on a real account.**
It was tested, with an expired session and a client exiting. After a cancelled Windows shutdown, a
real drop in the next minute stays quiet.

**A failed-launch row hasn't been seen on a real PC.** It was tested against a store that throws. Its
sentence says RoRoRo couldn't sign the account in, which reads loosely for a disk error.

**A lock on the saved-accounts file at startup still shows the "saved accounts can't be unlocked"
screen.** Its text blames a Windows restore. Start Fresh no longer sets a healthy file aside, but the
wording is wrong for a lock, and a retry needs wording in seven languages.

**A wrong-kind row in a file still makes the whole file unreadable.** A missing or null entry costs
that entry now. An entry of the wrong JSON type (text where a number belongs) makes the file
unreadable, and that list starts empty.

**Nobody has seen the backup rescue a real damaged file.** Loading, repairing and scrubbing were
each tested against damaged files in a test folder, and so was the new rule that a vault that opens
but holds nothing it should goes to the backup. In live runs on a test folder, a file of eight junk
bytes was set aside by Start Fresh with its bytes intact, and four wrong-shape files were read around
at startup. No real install has been through any of it.

**If you delete `accounts.dat` by itself to start over, your accounts come back** from the backup.
To reset RoRoRo, delete the whole folder, as the privacy policy describes.

**"Updated … ago" on a row can read up to six hours just after you start RoRoRo,** even though the
numbers are fresher than that. It corrects itself within a few minutes.

**A close from another program has been timed with `taskkill` only in this release.** The last
release's smoke saw Task Manager's End task quit RoRoRo cleanly, once, on one PC. The "already
running" startup dialog, which is one of the screens covered, was tested and not seen live. If
RoRoRo is in the tray, Task Manager lists it as a background process and simply ends it, as before.
A tool that posts a close to one of RoRoRo's dialogs (AutoHotkey's `WinClose`, for one) now quits
RoRoRo, as it already did for the main window.

**The crash line hasn't been seen after a real crash.** Its rules were tested and it was looked at in
captures in English and French. A false cause of it was fixed in v1.39, and the slow exit that
sometimes preceded it is fixed here. It also waits for the main window, like the known-issues
notice: if you keep RoRoRo in the tray, you'll see either one the next time you open the window.

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
screen, but nobody had an account in any of those states to try them on. If one shows up wrong for
you, an issue with the Diagnostics bundle helps a lot.

**The idle-time fix hasn't been watched live.** It was tested against a replay of the reported switch
between windows, not on a real one.

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

**No migration.** Your accounts, notes, history and settings carry over.

**One new network call, on Store installs only.** RoRoRo asks the Microsoft Store, through Windows,
whether an update is waiting: once after startup and every six hours while it runs. The request is
made by Windows for the installed app and RoRoRo sends nothing of its own. Clicking the button opens
RoRoRo's page in the Microsoft Store. Direct-download installs never make this request. The privacy
policy lists it.

**Two new file names, only when you press Start Fresh on a file that can't be read:**
`accounts.dat.corrupt-<time>` and `accounts.dat.bak.corrupt-<time>`, next to `accounts.dat`,
encrypted the same way. RoRoRo never deletes them. The privacy policy lists both.

**Plugins:** the plugin contract did not change. Two behaviours did: a stop request for a client that
has already exited now comes back as "nothing to stop" rather than an error, and a plugin-consent
entry that is damaged is dropped, so that plugin asks for consent again.

**Scripts that close RoRoRo:** a polite close from another program (`taskkill` without `/F`) now
quits RoRoRo while a startup screen is up, as it already did once RoRoRo was running.

**Updating from the Store:** quit RoRoRo first (right-click the tray icon, then Quit). An update that
lands while RoRoRo is running can fail to install, and a running RoRoRo can hold an update back.

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
