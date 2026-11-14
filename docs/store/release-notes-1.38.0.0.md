# RoRoRo v1.38.0.0 — release notes

> **Edit log.** A MINOR bump from 1.37.0.0 per the playbook's bump table: one user-visible feature
> (the crash notice) and two user-visible fixes (launch errors, the first-run row). All three came
> out of the 2026-11-10 evidence sweep (`docs/cycles/2026-11-10-growth-and-quality/board.md`, items
> 4 and 5) and the new-user walk in other languages
> (`docs/investigations/2026-11-10-new-user-walk-other-languages.md`).
>
> 2026-11-14, first draft. **Known issues were re-verified, not carried blind.**
> `git log v1.37.0.0..HEAD` touched four areas: what a failed auth-ticket request is called
> (`RobloxLauncher.ClassifyTicketFailure`, `RobloxApi`), the main window's status strip (one new
> row), the account row's layout (the game picker gives way) and the empty-state hint's wording. Two
> carried issues sit in touched areas. "The known-issues notice waits for the main window" still
> applies, and the crash notice lives in the same strip, so it waits the same way; the two are now
> one paragraph. "An unattended rejoin can stop at a Windows dialog" still applies: the change
> renames failures, it does not close dialogs. Every other carried issue touches no code this
> release changed, so they carry as written. Nothing was dropped. Two new known issues, each a thing
> that was built and tested but not watched on a real machine. The three v1.37 "not watched live"
> paragraphs stay: nothing since has watched them.
>
> There is no clan post file this release, on the owner's word (2026-11-14). The short list below is
> still written to travel.

---

# RoRoRo v1.38.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/RORORO-win-Setup.exe)

## Short list, for the GitHub release and the Discord post

```
• A launch that fails now says why, in your language: no internet, Roblox slow to answer, Roblox asking RoRoRo to slow down, or Roblox having trouble. Each one says what to do next.
• If RoRoRo closed unexpectedly last time, the next start says so and points you to Diagnostics to save a support bundle. Nothing is sent unless you send it.
• On a new account's row, Remove is no longer cut off in French, German, Russian, Portuguese or Spanish.
• Quit RoRoRo before updating from the Store: right-click the tray icon, then Quit.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### A failed launch says what went wrong

When a launch failed for a reason other than an expired sign-in, the account's row used to show a
developer's error in English, whatever language you use: "Failed to obtain auth ticket: A task was
canceled."

It now says which thing happened, in your language, and what to do:

- **Couldn't reach Roblox.** Check your internet connection, then try again.
- **Roblox took too long to answer.** Try again in a moment.
- **Roblox is asking RoRoRo to slow down.** Wait a minute, then try again.
- **Roblox is having trouble right now.** Try again in a few minutes.

Four older messages were reworded the same way. "Launch never connected" is now "Roblox didn't
start", with the two things worth checking. A failure RoRoRo doesn't recognise still shows its
detail, because that's the one a bug report needs.

### After a crash, RoRoRo says so

If RoRoRo closed unexpectedly, the next start opens with one line saying so, and an **Open
Diagnostics** button. From there, **Save support bundle** makes a zip you can send to
support@626labs.dev. **Dismiss** closes the line.

Nothing is sent on its own. RoRoRo has no telemetry, so a bundle you choose to send is the only way
a crash reaches us. The bundle holds no passwords or cookies.

Only a crash RoRoRo recorded counts. Ending it from Task Manager, a Store update or a power cut
doesn't bring the line up.

### A new account's row fits in every language

With one account and no saved game, the **Remove** button was cut off at the default window size
in French, German, Russian, Portuguese and Spanish. The game picker now gives up its room before
any button does, and the hint inside it was shortened in those five languages so it shows whole.

## Known issues going into the next update

**The crash line hasn't been seen after a real crash.** Its rules were tested and it was looked at
in captures in English and French. It also waits for the main window, like the known-issues
notice: if you keep RoRoRo in the tray, you'll see either one the next time you open the window.

**The new launch messages haven't been seen against live Roblox.** Nobody forced a real rate limit
or outage. If Roblox signals one in a way RoRoRo doesn't recognise, you get the general "Couldn't
sign this account in to Roblox" message with its detail.

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

**No migration.** Your accounts, notes, history and settings carry over.

**No new network calls.** The launch messages name failures of a request RoRoRo already made. The
crash line reads a file RoRoRo has written since v1.35 and sends nothing.

**No new files.** The privacy policy is unchanged.

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
