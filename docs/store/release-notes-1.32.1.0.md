# RoRoRo v1.32.1.0 — release notes

> **Edit log.** A BUILD bump, not a MINOR one: this release is one bug fix, so the version moves
> 1.32.0.0 → 1.32.1.0 per the playbook's bump table.
>
> **All seven of v1.32's known-issue paragraphs are carried forward, verified rather than assumed.**
> Issue #222 (leftover Roblox processes) was re-read and is still OPEN. The localization pair needed
> a real check, because this release edits `SettingsPage.xaml.cs`: the diff is a field, a constructor
> parameter, an assignment and one nudge call, and `OnUiCultureChanged` is untouched, so the memory
> section still waits for the next open when you change language. The mid-game verification page, the
> unattended-rejoin dialog, the known-issues notice, the unmeasured toast and the cooldown race are
> all unchanged by a fix that only moves when a setting is read.
>
> **Three known-issue paragraphs are NEW, and two of them are things this release's own work turned
> up** while driving the live app on 2026-10-05: the desktop balloon ignores where you told memory
> warnings to go and ignores the five-minute cooldown, and the memory checkbox does not save when a
> screen reader toggles it. Both are real, both are unfixed here, and the next release is the alerts
> cycle that fixes them. The third names something long-standing that only became visible this week:
> plugin updates are offered on the direct-download build only.
>
> **Left out, on purpose:**
>
> - **The Ur Score 0.7.0 catalog entry.** `plugins-catalog.json` is served from the latest release
>   rather than the binary, and was updated the night 0.7.0 shipped. Tagging this changes nothing for
>   anyone, and Ur Score's own notes carry what's in it.
> - **Video narration copy and the alerts-cycle scope document.** Real work, invisible in the app.

---

# RoRoRo v1.32.1.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Turning off "Watch memory while accounts are running" now takes effect immediately. Before, unticking it only took hold the next time you opened RoRoRo, so memory warnings kept arriving after you had switched them off.
• Turning it back on is immediate too. No restart either way.
• The memory numbers are live as well. Change how much RAM to keep free, when to warn about one account, or how far ahead to warn, and the very next check uses the new figure.
• Nothing else moved. Accounts, themes, plugins, alerts, history, and settings all carry over untouched.
```

## Longer form

### "Watch memory" now means this session, not the next one

Untick **Watch memory while accounts are running** in Settings → Alerts & memory, and RoRoRo stops
watching right then. Tick it again and it starts again. Neither needs a restart.

Before this release that switch was only read once, when RoRoRo started. Unticking it saved your
choice and changed nothing else, so the checks kept running and the warnings kept coming until the
next time you opened the app — with nothing on screen to tell you a restart was what it wanted. If
you ever turned memory watching off and kept getting memory warnings, that was this, and it was not
you misreading the switch.

The three numbers underneath it are live now too. **Memory to keep free**, **warn when one account
passes**, and **warn this far ahead** used to sit in the file until the next launch. Change one, and
the next check uses it.

One more thing you'll notice: when you turn watching off, the memory chips and the tray's memory
warning clear instead of freezing on whatever they last said. A stopped watcher now holds no
opinion, rather than leaving its last one on screen with nothing left to update it.

### Known issues going into the next update

**A desktop balloon for a memory warning ignores where you told it to go.** The routing grid in
Settings lets you pick Desktop, your channel, the clan channel and your phone per alert — but memory
warnings raise a Windows balloon on the desktop whatever you ticked, and whatever the five-minute
quiet period says. If memory warnings pop up when you expected them only in Discord, that's why. The
next release is a pass over alerts that fixes it.

**The memory switch doesn't save when a screen reader flips it.** Toggling **Watch memory while
accounts are running** through a screen reader, voice access, or any automation tool changes what the
box looks like and reports, but does not save your choice — the switch keeps running. Clicking it
with a mouse or pressing space on it saves correctly. Being fixed in the next release, with a test so
it can't come back.

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

### Compatibility

None. No new permissions, no migration, no settings change. Your `settings.json` is read exactly as
before; this release only changes how often it is read.

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
