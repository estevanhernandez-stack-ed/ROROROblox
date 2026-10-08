# RoRoRo v1.33.0.0 — release notes

> **Edit log.** A MINOR bump: this is the alerts cycle, eleven built items and several user-visible
> behaviour changes, so 1.32.1.0 → 1.33.0.0 per the playbook's bump table.
>
> **Two of v1.32.1's known-issue paragraphs are GONE because this release fixes them**, which is the
> whole reason the cycle existed. The desktop balloon ignoring the routing grid and the five-minute
> quiet period: fixed, and confirmed live on 2026-10-07 — with Desktop unticked a two-account
> crossing logged "routed nowhere" and nothing appeared. The memory switch not saving under a screen
> reader: fixed, and confirmed by driving `TogglePattern.Toggle()` directly, where the UI state and
> `settings.json` now move together.
>
> **One paragraph is narrowed rather than dropped.** "Nobody has looked at a real toast for a big
> group" was about Windows rendering our balloon as a toast and clipping it. We draw the balloon
> ourselves now, so that specific risk is gone, but nobody has seen a group larger than two in it.
> The reworded version says only what is still unknown.
>
> **Six paragraphs carry forward, each verified rather than copied.** Issue #222 was re-read and is
> still OPEN. The language pair needed a real check because this release rewrites much of
> `SettingsPage.xaml.cs`: across the three commits that touch it, not one changed line mentions
> `OnUiCultureChanged`, so the memory section and the presence line still wait for the next open.
> The verification page, the unattended-rejoin dialog, the known-issues notice and the cooldown race
> are untouched by this work.
>
> **Two paragraphs are NEW, and both came out of the live smoke** rather than the code review: a
> plugin makes its own alert sound that your choice here does not govern, and an account with Roblox
> presence privacy on will never get an idle alert.
>
> **Left out, on purpose:**
>
> - **Colour emoji in game titles.** Evaluated and dropped. Both candidate libraries were a bad
>   trade for a Store binary, and the measurement said the cost of refusing is cosmetic: emoji
>   already render as monochrome line art, not missing-character boxes.
> - **The cycle's own paperwork** — scope, PRD, spec, checklist, smoke record. Real work, invisible
>   in the app.

---

# RoRoRo v1.33.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Alerts now go only where you send them. Untick Desktop for a kind and nothing pops up for it — before, memory warnings appeared on your desktop no matter what you picked.
• You decide how often alerts may repeat. One setting for everything, and an override per alert if one kind should be chattier or quieter than the rest. Set it to Every time and nothing is held back.
• The desktop alert is ours now. It is themed like the rest of the app, and when it names a single account you can click it to jump straight to that account's row.
• Pick what an alert sounds like — the RoRoRo chime, the Windows default, or silence — and press Hear it to find out before you commit.
• Idle alerts are for accounts actually in a game. An account parked at the Roblox home screen no longer gets one; it has nothing to be kicked out of.
• The three alert cards are one section now, one row per alert, with where it goes and how often side by side.
• The whole new section speaks German, Spanish, French, Polish, Brazilian Portuguese and Russian, not just English.
• Settings calls it Language and Appearance, because that is where the language picker lives.
• The About page signs itself, and the egg behind the version number is drawn in Este's own hand.
• Nothing else moved. Accounts, themes, plugins, history and every other setting carry over untouched.
```

## Longer form

### Alerts go where you send them

The routing grid has always let you choose Desktop, your Discord channel, the clan channel and your
phone per alert. Memory warnings ignored it. They raised a Windows balloon on your desktop whatever
you ticked, and whatever the quiet period said, because that one alert had its own private line to
the screen that never asked the router anything.

There were three different code paths that could put something in front of you. There is one now.
Every alert kind — dropped out, memory, recycled, uptime, auto-rejoin paused, metric, and idle —
goes through the same place, which means the grid, the per-account mute and the quiet period all
apply to all of them, the same way.

### How often is up to you

Alerts used to repeat at most once every five minutes per account, and that number was ours.

Now it is yours. **How often at most** in the Alerts section sets it for everything, and any single
alert can override it if that kind should be chattier or quieter than the rest. The choices run from
**Every time**, which holds nothing back, up to an hour.

The default is still five minutes, so an upgrade changes nobody's pace unless they want it changed.

### The alert you see is ours

The desktop alert is a window we draw rather than a Windows shell balloon. It follows your theme,
it is readable over a fullscreen game, and it carries the account, the number and what to do about
it on one line.

When an alert names a single account, clicking it brings RoRoRo forward with that account's row in
view. When it names several, it stays a notice.

### The sound is a choice, and you can hear it first

**Alert sound** offers the RoRoRo chime, the Windows default, or silence, and the choice takes
effect on the next alert with no restart. The chime is ours, synthesised rather than licensed: two
soft tones, about a third of a second, quiet.

Next to it is **Hear it**. Press it and the sound plays — the same sound an alert will make, through
the same code. Choosing between three sounds you cannot hear until one happens to fire is not
choosing.

Silence is honest silence: the notification is still drawn and the tray icon still colours.

### Idle means in-game idle

An idle alert used to fire for any account that went quiet. It now fires only for an account that is
actually in a game.

The reasoning is what the alert is for. Roblox kicks an idle player out of a game, so the alert is a
warning that something you are earning is about to stop. An account sitting at the Roblox home
screen has nothing to be kicked out of, and an alert about it is noise.

The row chip still shows idle time for every account, exactly as before. Only the alert changed.

### One alerts section

Three cards became one section: a row per alert, with where it goes and how often side by side, and
the tray badge rule stated once where you choose the destinations rather than three times in three
places.

### The new section speaks your language

Everything the alerts work added — thirty-two new pieces of text — is translated into German,
Spanish, French, Polish, Brazilian Portuguese and Russian, not left in English for anyone not
reading the app in English.

### The About page signs itself

The 626 Labs wordmark sits at the foot of the About page, and the easter egg behind the version
number is drawn in Este's own handwriting rather than typed. Neither needs a font installed.

## Known issues going into the next update

**A plugin's alert sound is not governed by yours.** Choosing Silent silences RoRoRo. A plugin that
makes its own noise — Ur Score does, on its own alerts — carries on, because it is a separate
program and nothing in RoRoRo's settings reaches into it. If you set Silent and still hear something,
that is where it is coming from.

**An account with Roblox presence privacy on will not get idle alerts.** Idle alerts now need RoRoRo
to see that an account is in a game, and Roblox's presence privacy hides exactly that. Such an
account gets no idle alert even while it is playing, and nothing on screen explains why. The
alternative would be guessing, and guessing wrong means alerting about an account you deliberately
parked.

**Plugin updates are offered on the direct-download build only.** If you installed RoRoRo from the
Microsoft Store, the Plugins window never lists available plugins or offers updates for the ones you
have — a Store app isn't allowed to fetch a list of installable programs from a server. You can still
install and update a plugin by pasting its install link in that window.

**A verification page that appears mid-game is recovered late.** Roblox sometimes puts the "Verifying
you're not a bot" page up on an account that's already in a game. While that page is up, Roblox
still reports the account as in the game, so RoRoRo can't tell anything is wrong until Roblox
idle-kicks it behind the page, up to 20 minutes later. Then auto-rejoin brings it back as usual.
Spotting the page sooner would mean RoRoRo reading your screen, which it deliberately doesn't do.

**Leftover Roblox processes pile up.** Each Roblox launch can leave a small windowless Roblox process
behind, and a long session collects them. Auto-rejoin launches more often, so it adds to the pile.
Restarting RoRoRo offers to clear them; a sweep while it runs is
[issue #222](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/222).

**An unattended rejoin can stop at a Windows dialog.** If Roblox is mid-update when auto-rejoin
relaunches an account, the "Roblox isn't installed" message can be waiting for you when you get back,
and that account stays out until you close it.

**The known-issues notice waits for the main window.** If you keep RoRoRo in the tray, you'll see it
the next time you open the window. That's deliberate: a notice beats a Windows toast for something
you can read at your own pace, and the page and its count are there either way.

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed. Re-checked against the
code for this release even though it rewrites much of that page — the culture-change handler is
untouched.

**Nobody has seen a large group in the new alert.** The drawn balloon has been watched with one
account and with two. Its text limits are measured and tested, but if a group of four or more looks
wrong on your desktop, that's worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other, because the cooldown is stamped after sending rather than before. Rare,
harmless, and on the list.

## Compatibility

**One migration, and it runs once.** If you had idle alerts muted before this release, they stay
muted: the old mute setting is read once and carried across as an empty destination list for idle
alerts. Tick a destination for **An account goes idle in a game** and they come back.

**Three new settings**, all defaulting to what the app did before: how often alerts may repeat (five
minutes), the per-alert overrides (none), and the alert sound (the chime). No new permissions, no new
network calls, nothing else to do.

## Other channels

Also on the [Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). The Store build
updates itself through the Store.

A signed sideload MSIX ships with each release for anyone who prefers it. The dev certificate did not
rotate this release, so nobody needs to re-import anything.

## Issues, ideas

[Open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues) — bugs, requests,
anything that reads wrong. [Privacy policy](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/PRIVACY.md).
