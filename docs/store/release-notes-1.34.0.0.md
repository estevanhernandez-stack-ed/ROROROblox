# RoRoRo v1.34.0.0 — release notes

> **Edit log.** A MINOR bump from 1.33.0.0 per the playbook's bump table: one new feature surface
> (theme sharing), a user-visible change to how metric alerts name accounts, idle-threshold
> behaviour, and clarifying copy. Not tagged at the time of writing.
>
> 2026-10-23, first completion: the draft had covered only the Clarified copy. Added theme sharing,
> metric-alert scoping, the package-version fix and the plugin-facing HostInfo change. Corrected
> the Silent line, which named Ur Score as a plugin that plays sounds; Ur Score has no sound code,
> and the in-app hint now names no plugin so it cannot go stale. Same error v1.33's notes made.
>
> 2026-10-23, second pass, into the playbook's Phase 2 shape: download line, fenced short list,
> known issues, compatibility, other channels. **Known issues were re-verified, not carried.**
> `git log v1.33.0.0..HEAD` touched none of their areas except one: the presence-privacy paragraph
> said "nothing on screen explains why", and this release adds exactly that explanation, so it was
> rewritten. Issues #222 and #230 re-checked open. The privacy note under Other channels is new: it
> reflects the 2026-10-11 measurement that a Store-only install keeps its data in the package folder.
>
> The version-display line is scoped to About and the log because the Cart run moved the App-side
> request headers and left Core-side reads alone; "every request" would be untrue.

---

# RoRoRo v1.34.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Share a theme you made, or import one a friend sent you. Two new buttons in the theme builder, and an imported theme applies straight away.
• Metric alerts name only the accounts running on this PC. Saved alts that aren't open no longer turn up in them.
• When none of your running accounts moved a number, the alert says so, instead of naming someone who wasn't there.
• The idle threshold always matches what the picker shows, and a hand edit to settings.json lands within about 30 seconds.
• About shows the version you actually have installed, on the Microsoft Store and sideload builds too.
• Settings explains three things that used to be guesswork: why Invisible accounts never get idle alerts, what Silent covers, and what counts as activity.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### Share a theme, and import one

The theme builder has **Share theme** and **Import theme** beside Open themes folder.

**Share theme** saves your active custom theme as a file you can send to anyone. It's available when
the theme you're using is one you made; the built-in themes are already on everyone's machine.

**Import theme** reads one back and applies it straight away. A file that would overwrite a built-in
theme is refused, and so is a theme you already have, so importing the same file twice doesn't leave
you with two copies.

### Metric alerts name only the accounts running here

A metric alert used to name every saved account the reporter covered, including alts that weren't
open. If you had six saved and two running, an alert could name all six.

Now it names only the ones running on this PC, like "3 accounts running here." The alert still fires
on the number itself, so nothing that would have alerted before goes quiet; it just stops naming
accounts that had nothing to do with it. If you run alts on two PCs, each PC names its own.

### When it wasn't yours, the alert says so

A clan-wide number can cross because other players moved it, or because an alt on another PC did.
That alert used to name your saved accounts anyway. Now it reads:

> None of your running accounts moved this. It came from other players, or from an alt on another PC.

### The idle threshold keeps its word

The picker offers 10, 12, 15 and 18 minutes. A stored value outside that set — a hand edit, say —
used to run silently at a number the picker couldn't show. Now it reads as the nearest offered value,
ties going to the lower one. Reading never writes back, so your file keeps what you typed until you
pick something.

And a hand edit no longer waits for a restart. RoRoRo re-reads the threshold every 30 seconds.

### About tells the truth about your version

The Microsoft Store and sideload builds now report their real installed package version in About and
in the log. Direct-download builds already did.

### Three things Settings now says out loud

- **Invisible accounts never get idle alerts.** With Roblox presence privacy on, RoRoRo can't see
  the account in a game, and idle alerts only fire for accounts it sees in one. The idle controls say
  so now.
- **Silent covers RoRoRo's own alerts only.** Plugins that play sounds have their own sound settings.
  The alert-sound picker says so beside "Silent still draws the notification."
- **Idle counts per account, from the client in the foreground.** Working in another program counts
  as idle for every alt. The threshold picker says so.

### For plugin authors

`GetHostInfo` gains three fields: `install_flavour` (store, sideload or dev), `process_path` and
`informational_version`. Fields 1 to 3 are unchanged, and the plugin contract goes from 0.11.0 to
0.12.0. A plugin built against the older contract keeps working and simply doesn't see the new
fields. RoRoRo Ur MCP 0.4.1 reads them, so an agent can tell which RoRoRo binary it's driving.

## Known issues going into the next update

**A plugin's alert sound is not governed by yours.** Choosing Silent silences RoRoRo. A plugin that
makes its own noise carries on, because it's a separate program and nothing in RoRoRo's settings
reaches into it. Of the catalogued plugins, **Ur AFK is the only one that makes a sound**, the
Windows Exclamation when its own "sound on grab" is ticked. Its own switch is the way to stop it.
Settings now says this beside the sound picker.
([#230](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/230) asks whether RoRoRo
should expose your choice so a plugin can follow it.)

**An account with Roblox presence privacy on will not get idle alerts.** Idle alerts need RoRoRo to
see that an account is in a game, and presence privacy hides exactly that. The alternative would be
guessing, and guessing wrong means alerting about an account you deliberately parked. What changed
in this release is that Settings now explains it, under the idle controls.

**Plugin updates are offered on the direct-download build only.** If you installed RoRoRo from the
Microsoft Store, the Plugins window never lists available plugins or offers updates for the ones you
have, because a Store app isn't allowed to fetch a list of installable programs from a server. You
can still install and update a plugin by pasting its install link in that window.

**A verification page that appears mid-game is recovered late.** Roblox sometimes puts the "Verifying
you're not a bot" page up on an account that's already in a game. While that page is up, Roblox still
reports the account as in the game, so RoRoRo can't tell anything is wrong until Roblox idle-kicks it
behind the page, up to 20 minutes later. Then auto-rejoin brings it back as usual. Spotting the page
sooner would mean RoRoRo reading your screen, which it deliberately doesn't do.

**Leftover Roblox processes pile up.** Each Roblox launch can leave a small windowless Roblox process
behind, and a long session collects them. Auto-rejoin launches more often, so it adds to the pile.
Restarting RoRoRo offers to clear them; a sweep while it runs is
[issue #222](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/222).

**An unattended rejoin can stop at a Windows dialog.** If Roblox is mid-update when auto-rejoin
relaunches an account, the "Roblox isn't installed" message can be waiting for you when you get back,
and that account stays out until you close it.

**The known-issues notice waits for the main window.** If you keep RoRoRo in the tray, you'll see it
the next time you open the window. That's deliberate: a notice beats a Windows toast for something
you can read at your own pace.

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed.

**Nobody has seen a large group in the alert window.** It has been watched with one account and with
two. Its text limits are measured and tested, but if a group of four or more looks wrong on your
desktop, that's worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other, because the cooldown is stamped after sending rather than before. Rare,
harmless, and on the list.

## Compatibility

**No migration and no new settings.** One stored value can now read differently: an idle threshold
outside 10, 12, 15 or 18 reads as the nearest of those, and the file itself is left alone until you
pick a value.

**Plugins:** the contract change is additive. Every plugin you have keeps working unchanged.

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
