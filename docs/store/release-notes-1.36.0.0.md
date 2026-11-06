# RoRoRo v1.36.0.0 — release notes

> **Edit log.** A MINOR bump from 1.35.0.0 per the playbook's bump table: the account-library cycle
> (`docs/cycles/2026-11-01-account-library/`). The main window becomes an account manager, and Games
> becomes the Library.
>
> 2026-11-05, first draft. **Known issues were re-verified, not carried.** `git log v1.35.0.0..HEAD`
> touched one carried area: idle Roblox tray processes (#222), now closed after two minutes by their own
> sweep, so that paragraph is dropped and the browser-launch paragraph narrowed. #230 re-checked open
> (its host fix has not merged; the Silent hint in Settings says what Silent covers). Presence privacy,
> the Store plugin list, the mid-game verification page, the Windows "isn't installed" dialog, the
> known-issues notice, the two language spots, the large alert group and the cooldown race touch no code
> this release changed, so they carry. New known issues, each checked: the Library's older dates, the
> About shortcut list, and three health badges that have not been seen on a real account.
>
> The health badges are described by what you see, not by how RoRoRo detects them. The Needs
> verification and Limited states depend on Roblox's own responses, and neither has been observed live.

---

# RoRoRo v1.36.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Every account shows its Robux, Plus and friends online, with a note you write yourself.
• Open an account (the small triangle before its name) to see its recent games, favourites and private servers, and play any of them in one click.
• When an account needs you, a badge says why: Session expired, Needs verification or Limited by Roblox. One click starts the fix.
• Games is now the Library: every game you've played, favourited or saved, in a grid or a list. Search it, filter it, paste a game link, and pick who plays with Play as.
• The welcome tour starts with one account. The tips for several accounts come after.
• Idle time counts for the account you actually played on, even when you switch windows fast.
• Leftover background Roblox processes with no window close on their own after two minutes.
• Long menu names in other languages wrap instead of being cut off, and the About page reads cleanly in every language.
• Quit RoRoRo before updating from the Store: right-click the tray icon, then Quit.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### Every account at a glance

Each account now has a line under it with its Robux balance, a Plus badge if it has Roblox Plus,
how many of its friends are online, when it last played, and the first line of a note you write
yourself. RoRoRo reads these from Roblox with that account's own login, quietly in the background,
about every ten minutes per account, and keeps them in the same encrypted file as your accounts.
Streamer mode hides the Robux, Plus and friends numbers and the note.

### Open an account

Click the small triangle before an account's name. Its panel opens under it with three columns:
the games it played recently, your favourites, and your saved private servers. Click any of them to
play it as that account. The panel also has the account's note, and a button that opens Roblox at its
home screen as that account.

### Badges that tell you what's wrong

When an account can't launch, a badge on its line says why, and clicking it starts the fix:

- **Session expired**: Roblox signed it out. Click to sign in again.
- **Needs verification**: Roblox wants a check before it will let the account join. Click to open
  Roblox as that account and do it there.
- **Limited by Roblox**: Roblox has limited the account. Click to sign in again, or wait it out.

An account set to join by following gets a **Joins by following** badge too. On an expired or limited
account, **Re-authenticate** takes the place of **Launch As**, since it couldn't launch anyway.

### The Library

Games is now the Library, on the toolbar and in the tools window. It holds every game you've played,
favourited or saved as a private server, as tiles or as a list. Filter by Recent, Favourites and
Private servers, search by name, or paste any Roblox game or private server link into the same box.
**Play** uses your main account (or your only one); **Play as** lets you pick which account plays.

### A tour for one account

The welcome tour now starts with what one account gets: one place to launch Roblox, adding an
account, playing from its panel. The tips that only matter with several accounts come last, under
their own heading.

### Smaller fixes

- **Idle time.** RoRoRo now credits your activity to the window you actually used. Before, a quick
  switch to another account and back could credit the wrong one.
- **Leftover processes.** A background Roblox process that never opened a window, and that RoRoRo
  isn't using, is closed after two minutes. A game you're playing, or one still loading, is never
  touched.
- **Diagnostics** now shows how RoRoRo was installed (Microsoft Store, sideload, direct download or a
  developer build), and its version always matches the About page.
- **Other languages:** long names in the tools window's side menu wrap onto two lines instead of
  being cut off, and the About page's paragraphs no longer break mid-sentence.

## Known issues going into the next update

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

**No migration.** Your accounts, notes, history and settings carry over. The account facts fill in on
their own over the first few minutes.

**New network calls, all to Roblox, each with that account's own login:** the account's Robux balance
(`economy.roblox.com`), whether it has Plus (`premiumfeatures.roblox.com`), and how many friends are
online (`friends.roblox.com`). Nothing new leaves your PC for anywhere else. The privacy policy names
all three.

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
