# RoRoRo v1.35.0.0 — release notes

> **Edit log.** A MINOR bump from 1.34.0.0 per the playbook's bump table: the field-report cycle.
> A four-day agent-driven run on a Store build (Oct 21-25, 2026; triage
> `docs/investigations/2026-10-25-field-report-triage.md`) turned up a plugin-start blocker on Store
> installs, two crashes, a restart that waited an hour for a click, and launches that a leftover
> Roblox tray process quietly took over. All of it is fixed here, plus one plugin-facing feature
> (joining a named server).
>
> 2026-10-26, first draft. **Known issues were re-verified, not carried.** `git log v1.34.0.0..HEAD`
> touched one carried area: the leftover-process paragraph (#222). Idle Roblox tray processes are now
> ended before every launch, so they can no longer take one, and a Windows restart no longer stops at
> the leftover dialog; the paragraph is rewritten to what is still true. #230 re-checked open. The
> other carried paragraphs (presence privacy, Store plugin list, mid-game verification page, the
> Windows "isn't installed" dialog, the known-issues notice, the two language spots, the large group,
> the cooldown race) touch no code this release changed. Two new known issues: the BLOCKED dialog on a
> restart, and a browser-started launch meeting a leftover tray process.
>
> The display-glitch crash is described by its effect, not its mechanism (a DWM composition flip
> under WPF's window chrome); the mechanism is in `docs/decisions.md`. What flips composition on the
> field machine is not known, so the notes do not name a cause.

---

# RoRoRo v1.35.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Every launch runs in its own Roblox window again. A leftover Roblox process in the tray used to grab the next launch, so the account showed as closed and its window stayed unnamed.
• Plugins start on Microsoft Store installs again. They were stopping within a second of starting.
• Each plugin on the Plugins page says why it stopped, and Restart only appears when it can help.
• A flicker of the Windows display no longer closes RoRoRo. That was behind two crashes during the field test.
• RoRoRo now asks Windows to bring it back if it closes unexpectedly or the Store updates it. When it comes back with games open, they carry on without a dialog waiting for a click.
• Plugins can send an account straight into one specific server, and the plugin permission line says so.
• Logs are a fraction of the size. RoRoRo writes an account's status when it changes, not every 25 seconds.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### Every launch gets its own window again

Roblox keeps a small process running in the tray after you close a game. That process was quietly
accepting the next launch: RoRoRo started the game, the game opened inside the tray process instead,
and RoRoRo saw its own launch close four seconds later. The account showed as closed, its window
stayed plain "Roblox", plugins couldn't see it, and Stop couldn't reach it. Two launches close
together could even end up as one game.

Now RoRoRo closes any idle Roblox tray process right before it launches, so every launch runs in its
own window, gets its name and stays owned by its row. It only ever closes a tray process with no
window that RoRoRo isn't tracking; a game, or an account that's still loading, is never touched.

One side effect: while RoRoRo is launching accounts, Roblox's own tray icon goes away. Roblox puts it
back the next time a game closes.

And when a game does end up without an owner, RoRoRo no longer guesses whose it is when more than one
account closed recently. A window without a name is better than a window with the wrong one.

### Plugins start on Microsoft Store installs

On a Store install, every plugin stopped within a second of starting, before it could write a log of
its own. RoRoRo now starts plugins from their real location on disk, and they run normally. A plugin
that closes during startup also no longer turns a good install into a "failed" one.

### The Plugins page says why

Each plugin row now gives one line on why it stopped: couldn't start, crashed while starting, closed,
or crashed, with the code if there was one. **Restart** only appears when restarting can help; when a
plugin can't start at all, the page says to send the log instead of offering a button that would fail
again.

### A display flicker no longer closes RoRoRo

During the field test RoRoRo closed twice in one day, both times at the moment Windows briefly
switched off its desktop effects (that happens on a display or graphics driver reset, a remote
desktop connection, or waking from sleep). RoRoRo now notes it in the log and carries on.

### RoRoRo asks Windows to bring it back

RoRoRo now registers with Windows to be restarted if it closes unexpectedly, or when the Microsoft
Store closes it to install an update. After a crash, Windows only does this when Windows Error
Reporting is on. It is by default, but some PC clean-up tools turn it off.

When RoRoRo comes back with games still open, it keeps them running and picks them back up, instead
of stopping at the "Leftover Roblox processes" dialog and waiting for a click. On Oct 24 that dialog
held a restart for an hour.

It does not start RoRoRo after you restart your PC; that's still up to the run-on-login setting.

### Plugins can join a named server

A plugin can now send an account straight into one specific public server, for example the one with
the fewest players, instead of letting Roblox pick. A full server makes the account wait in line; it
never quietly lands it somewhere else. The permission line on the plugin consent screen now names
this alongside links and friends. It's the same permission, so nothing you've already granted
changes.

### Smaller logs

RoRoRo used to log every account's status every 25 seconds, which was almost all of a day's log. Now
it logs a status when it changes.

### For plugin authors

The plugin contract goes from 0.12.0 to 0.14.0, all additive.

- **0.13.0:** `GetAccounts` and the launch calls wait for RoRoRo to finish loading saved accounts
  instead of answering with an empty list, and refuse with `accounts-loading` if it takes too long.
  `HostInfo.accounts_loaded` says which. `StopAccounts` can wait until the clients have actually
  closed (`wait_for_exit`), so a relaunch right after it no longer meets "Account is already running".
- **0.14.0:** `HostInfo` says how the previous session ended (`last_exit_reason`: none, clean,
  session-ending, crash or unknown, with detail and time). `RequestLaunchTarget` takes a
  `game_instance` (place id plus server id from Roblox's public server list); a half pair or a
  malformed id is refused with `invalid-target`. The author guide has a recipe.

## Known issues going into the next update

**A plugin's alert sound is not governed by yours.** Choosing Silent silences RoRoRo. A plugin that
makes its own noise carries on, because it's a separate program and nothing in RoRoRo's settings
reaches into it. Of the catalogued plugins, **Ur AFK is the only one that makes a sound**, the
Windows Exclamation when its own "sound on grab" is ticked. Its own switch is the way to stop it.
([#230](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/230) asks whether RoRoRo
should expose your choice so a plugin can follow it.)

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

**Idle Roblox tray processes still sit there between launches.** RoRoRo now closes them right before
each launch, so they can no longer take one, but in a long session without launches they wait until
the next one.
([#222](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/222))

**A game you start outside RoRoRo can still land in a tray process.** RoRoRo's own launches are
covered. A game you start from the browser while a recently closed game's tray process is still
around can open inside it, and its window won't get a name.

**Nobody has watched Windows restart RoRoRo yet.** The registration is in place and the carry-on
after a restart is tested, but a real crash restart or a Store-update restart hasn't been observed. On
the test PC, Windows Error Reporting is switched off, so a crash there is never restarted.

**A restart while Roblox holds the multi-instance lock still asks.** The automatic restart skips the
leftover-games dialog, but not the one that appears when Roblox itself is holding the lock RoRoRo
needs. That one hasn't been seen in the field.

**An unattended rejoin can stop at a Windows dialog.** If Roblox is mid-update when auto-rejoin
relaunches an account, the "Roblox isn't installed" message can be waiting for you when you get back,
and that account stays out until you close it.

**The known-issues notice waits for the main window.** If you keep RoRoRo in the tray, you'll see it
the next time you open the window.

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed.

**Nobody has seen a large group in the alert window.** It has been watched with one account and with
two. If a group of four or more looks wrong on your desktop, that's worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other. Rare, harmless, and on the list.

## Compatibility

**No migration and no new settings.** RoRoRo now registers with Windows to be restarted after a crash
(when Windows Error Reporting is on) or a Store update. It does not start after a PC restart unless
run-on-login is on.

**Plugins:** the contract changes are additive. Every plugin you have keeps working unchanged. The
launch-to-a-server permission line has new wording; the permission itself is the same.

No new permissions and no new network calls.

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

[Open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues) — bugs, requests,
anything that reads wrong. [Privacy policy](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/PRIVACY.md).
