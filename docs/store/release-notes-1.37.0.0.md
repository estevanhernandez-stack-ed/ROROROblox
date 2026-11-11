# RoRoRo v1.37.0.0 — release notes

> **Edit log.** A MINOR bump from 1.36.0.0 per the playbook's bump table: two user-visible features
> (the rating ask, three FPS caps) and two fixes on the way in. Everything here came out of the
> 2026-11-10 evidence sweep (`docs/cycles/2026-11-10-growth-and-quality/board.md`).
>
> 2026-11-10, first draft. **Known issues were re-verified, not carried blind.**
> `git log v1.36.0.0..HEAD` touched four areas: the launch path (a handler check before the
> shell-execute), the Roblox-needed dialog (one new sentence), Add account (the already-saved guard)
> and the main window's status strip (one new row). One carried issue sits in a touched area, "An
> unattended rejoin can stop at a Windows dialog": it still applies, because a launch that finds no
> handler ends at RoRoRo's own Roblox-needed dialog, which waits for someone to close it. Every other
> carried issue touches no code this release changed, so they carry as written. Four new known
> issues, each one a thing that was built and tested but not watched on a real machine.
>
> The rating ask is in the short list on purpose. It is the first time the app asks its user for
> anything.

---

# RoRoRo v1.37.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/RORORO-win-Setup.exe)

## Short list, for the GitHub release and the Discord post

```
• If this PC has no Roblox that RoRoRo can start, RoRoRo now says so and shows the download, instead of leaving you with a Windows message. This is what happens when Roblox came from the Microsoft Store.
• Adding an account you already saved no longer makes a second copy. RoRoRo refreshes its sign-in and tells you to add a different Roblox account.
• New FPS caps: 75, 100 and 180.
• After a few good days, a Store install may be asked once for a rating. Either answer ends it for good.
• Quit RoRoRo before updating from the Store: right-click the tray icon, then Quit.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### A launch no longer dead-ends when Roblox came from the Microsoft Store

The Microsoft Store edition of Roblox can't be started by other apps. If that was the only Roblox on
your PC, clicking **Launch As** opened a Windows message saying you need a new app to open the link,
and RoRoRo carried on as if the launch had started.

RoRoRo now checks before it launches. If there is no Roblox it can start, you get RoRoRo's own
**Roblox needed** dialog with a **Download Roblox** button, and when the Store edition is what you
have, the dialog says so: install the one from roblox.com, and both can stay installed. Your accounts
are untouched.

### Adding the same account twice

If you sign in to **Add account** with an account that is already saved, RoRoRo no longer adds a
second row for it. It refreshes that account's sign-in instead, which is handy when its session had
gone bad, and tells you that running two at once takes a different Roblox account.

Two rows for one account used to sign each other out, which looked like multi-instance not working.

### Three more FPS caps

75, 100 and 180 join the per-account FPS cap list, for monitors that run at those rates. Thanks to
the reviewer who asked.

### A rating, asked once

If you installed from the Microsoft Store, and a launch has worked on three separate days, RoRoRo
shows one line the next time it starts: a rating on the Microsoft Store helps other players find
it. **Rate RoRoRo** opens the Store's review page. **Not now** closes it. Either answer ends it for
good, and it never appears in the middle of a launch.

RoRoRo has thousands of installs and a couple of dozen ratings, so each one counts for a lot. The
direct download never asks.

## Known issues going into the next update

**The Store-edition fix hasn't been tried on a PC that only has the Store edition of Roblox.** The
check, the dialog and the launch path were each tested, and the check was run against real Windows
handlers, but nobody has walked the whole thing on such a PC. If the dialog shows when it shouldn't,
or doesn't when it should, an issue with the Diagnostics bundle helps a lot.

**Nobody has seen the rating line on a real Store install yet.** It was tested and looked at in
captures. If you leave RoRoRo running for weeks, you'll see it at your next start, not on the third
day.

**The new FPS caps haven't been watched on a live client.** They go through the same settings write
as 45, 90 and 165.

**You still can't type your own FPS cap.** The list is longer, not open-ended.

**None of the three health badges has been seen on a real account yet.** Session expired, Needs
verification and Limited by Roblox were each tested against Roblox's documented responses and on
screen, but nobody had an account in any of those states to try them on. If one shows up wrong for
you, an issue with the Diagnostics bundle helps a lot.

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

**The known-issues notice waits for the main window.** If you keep RoRoRo in the tray, you'll see it
the next time you open the window.

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed.

**Nobody has seen a large group in the alert window.** If a group of four or more looks wrong on your
desktop, that's worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other. Rare, harmless, and on the list.

## Compatibility

**No migration.** Your accounts, notes, history and settings carry over.

**No new network calls.** The check for a Roblox that RoRoRo can start reads Windows' own list of
which app opens which link. **Rate RoRoRo** hands a link to the Microsoft Store app; RoRoRo itself
contacts nothing.

**One new file,** `rating-ask.json`, on Store installs only: up to three dates and your answer.
It stays on your PC. The privacy policy lists it.

**Plugins:** the plugin contract did not change. Every plugin you have keeps working unchanged.

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
