# RoRoRo v1.32.0.0 — release notes

> **Edit log.** Nothing dropped from v1.31's sections. The four known-issue paragraphs are carried
> forward **verified rather than assumed**, per the playbook's standing rule. The known-issues notice
> still lives in the main window only (no change to `KnownIssues/`). The localization pair:
> `OnUiCultureChanged` in `SettingsPage.xaml.cs` was read, not skipped, because this release edits
> that file. It gains the auto-rejoin status-line watcher and still leaves the memory section and the
> Discord presence line to their next open. The toast note is unchanged, since no test can see a
> Windows toast. The cooldown race: `AlertDispatcher.cs` line 53 still names it as known and
> deliberately unfixed. Three paragraphs are NEW: a captcha during play recovers late (measured, and
> the spec's accepted cost); leftover Roblox processes pile up (issue #222, which auto-rejoin adds
> to); and a launch dialog can wait for you after an unattended rejoin.
>
> **The headline is the overnight problem, told plainly.** On 2026-09-29 three alts were found
> idle-kicked with their windows still open, sitting on the Disconnected dialog for hours, while a
> keep-alive reported them fine. Auto-rejoin exists because of that morning. The notes say what
> happens and what it never does (touch the verification page), and name nobody.
>
> **A behaviour change for anyone who already ticked Join via friend.** Before 1.32 only Squad Launch
> read the flag, so the Launch button joined a flagged account directly. Now every launch follows your
> main. Anyone who ticked it on an account that no longer needs it will meet the new dialog, so the
> notes say so and point at "It's fixed, join directly".
>
> **Left out, on purpose:**
>
> - **The plugin reason code** (`LaunchResult.reason_code`). It's a contract detail for plugin
>   authors, and the contract package's own release notes carry it. Users only see its effect: a
>   plugin can say why a launch was refused.
> - **Ur Task 0.9.1 in the plugin catalog.** `plugins-catalog.json` is served from the latest
>   release, not the binary, and was updated on the current release the day 0.9.1 shipped. Tagging
>   1.32 changes nothing for anyone. Ur Task's own notes carry it.
> - **Test, CI and translation-pipeline work.** Real, and invisible to anyone using the app.

---

# RoRoRo v1.32.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Alts that drop out come back on their own. Turn on "Rejoin if it drops out" on an account (right-click its row). If its Roblox window is open but it has been out of the game for 3 minutes, RoRoRo closes it and joins it again. Never offered for your main.
• That covers idle kicks, failed joins, and the "Verifying you're not a bot" page. RoRoRo never touches that page. It closes the window and joins again.
• It knows when to stop. At most 3 rejoins an hour per account. A 4th, or 3 relaunches in a row that fail, turns it off for that account and tells you. Your stop, Stop all, or a plugin's stop always wins.
• Join via friend now works everywhere. An account with it ticked follows your main on every launch, not just Squad Launch. If your main isn't in a game, RoRoRo asks: follow someone else, "It's fixed, join directly" (which unticks it), or Cancel.
• Launch multiple and Squad Launch ask once for the whole batch instead of joining a ticked account directly after 90 seconds.
• Nothing else moved. Accounts, themes, plugins, alerts, history, and settings all carry over untouched.
```

## Longer form

### Alts that drop out come back on their own

Leave a few alts running overnight and some will be gone by morning. Roblox kicks an account that
has been idle 20 minutes, and the window stays open on the Disconnected dialog. It looks fine from
across the room, so it can sit there for hours.

Right-click an account and turn on **Rejoin if it drops out**. From then on, RoRoRo watches whether
that account is actually in a game, using what Roblox itself reports, and not what the window looks
like. If the Roblox window is open but the account has been out of the game for **3 minutes**,
RoRoRo closes that window and joins again: into the same server when it knows which one, or back
through your main when the account is set to Join via friend. It gives a fresh launch 5 minutes to
get in before it counts as a failed join.

It doesn't matter why the account is out. An idle kick, a join that failed, or Roblox's **"Verifying
you're not a bot"** page all look the same from here. **RoRoRo never clicks, types into or answers
that page.** It closes the window, and the account joins again.

It's never offered for your main. Your main is the one you're playing.

### It knows when to stop

- **At most 3 rejoins an hour per account.** A 4th, or 3 relaunches in a row that don't start,
  turns the option off for that account and tells you. The desktop toast is on by default; Discord
  and phone are there if you want them (Settings › Alerts). Turn it back on when you're ready. A
  restart won't quietly turn it back on for you.
- **You always win.** Stop an account yourself, press Stop all, or let a plugin stop it, and it
  stays stopped. A Launch multiple or Squad Launch in progress is left alone until it finishes.
- **No news isn't bad news.** If RoRoRo can't tell whether an account is in a game (Roblox is
  rate-limiting it, or its session expired), it waits instead of guessing.
- **A Join via friend account waits for your main.** If your main isn't in a game, RoRoRo doesn't
  close the window at all. It waits, and joins the moment your main is back.

### Join via friend now works everywhere

**Join via friend** is for an account that runs into Roblox's verification page when it joins a
server directly, but gets in fine by following your main. Until now only Squad Launch read it. The
Launch button, Join by link, Launch multiple and plugins all joined directly anyway.

Now every launch follows your main when your main is in a game. When it isn't, RoRoRo asks
instead of joining directly:

- **Follow another account**: pick one of your accounts that's in a game you can join.
- **It's fixed, join directly**: unticks Join via friend for that account and joins as usual.
- **Cancel** (the default): nothing launches.

**If you ticked Join via friend on an account that doesn't need it any more**, you'll meet that
question the first time your main isn't in a game. "It's fixed, join directly" takes care of it
for good.

**Launch multiple and Squad Launch ask once for the whole batch.** They start everyone else first,
wait for your main to land, then send the ticked accounts after it. Squad Launch used to give up
after 90 seconds and join them directly. Now it asks instead.

A private-server link follows your main only if your main is in that same server. If it's
somewhere else, RoRoRo asks.

### Known issues going into the next update

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
and the Discord presence status line. Both are deliberate rather than missed. The memory section
re-reads your settings to redraw, which risks overwriting an edit you're in the middle of, and the
presence line re-narrates on its next update anyway. Every other surface switches instantly.
Re-checked against the code for this release, not copied forward.

**Nobody has looked at a real toast for a big group yet.** The desktop balloon's limits were measured
from the library that draws it, and the tests hold the app to them, but Windows renders that balloon
as a toast and may clip further than those numbers, and no automated test can see a toast. If a
four-account-or-more group looks wrong on your desktop, that's worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other, because the cooldown is stamped after sending rather than before. Rare,
harmless, and on the list.

### Other channels

- **Microsoft Store:** [RoRoRo on the Store](https://apps.microsoft.com/detail/9NMJCS390KWB). This is
  the version going up for certification. The Store updates you automatically once it clears.
- **Sideload MSIX:** attached to this release with `dev-cert.cer`. Import the cert to Local
  Machine → Trusted People first, same as before. No cert rotation this release.

### Issues, ideas

Something broken, something missing, or a translation that reads wrong in your language:
[github.com/estevanhernandez-stack-ed/ROROROblox/issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues).
Privacy policy:
[estevanhernandez-stack-ed.github.io/ROROROblox/privacy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/).
