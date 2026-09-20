# RoRoRo v1.30.0.0 — release notes

> **Edit log.** Nothing dropped from v1.29's sections. The known-issues block is carried forward
> **verified rather than assumed**, per the playbook's standing rule. The localization pair:
> `git log v1.29.0.0..main` touched no file under `Localization/`, no `SettingsPage.xaml.cs`, and no
> `DiscordPresenceService.cs`, so both spots still wait for the next open. The cooldown-race note:
> `AlertDispatcher.cs` line 53 still names it as known and deliberately unfixed. The toast note is
> unchanged for the same reason it was true in v1.29 — no automated test can see a Windows toast.
>
> **The crossing change is the headline and it is a BEHAVIOUR change, not a fix with no cost.**
> Someone whose alert was buzzing every few minutes will find it goes quiet. That is the point, and
> the notes say so plainly rather than burying it as "improved alert handling". The restart caveat
> gets its own sentence in the long form, because it is the one case where the new behaviour is
> less chatty than someone might want.
>
> **Not in the clan notes, on purpose:** the smoke tool's baseline injection, the batcher's group
> key gaining the alert kind, and the jump from 2,311 to 2,329 unit tests. Real work, invisible to
> anyone using the app.
>
> **Ur Score is named here for the first time.** It is in `docs/store/plugins-catalog.json` now, so
> a reader can actually install it from inside RoRoRo — which is what made naming it honest rather
> than a promise with someone else's release date attached.

---

# RoRoRo v1.30.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Alerts fire once, when something changes. A rule that stayed true used to alert again every few minutes, for as long as it stayed true — a stat sitting under its floor for three hours could buzz your phone thirty times. It now alerts once, when the number crosses the line, and goes quiet until it crosses back.
• You can ask to be told when it comes right again. Tick "Also tell me when it comes right again" on a rule and you get a second alert when the number comes back over its line: "K0i2 clan points is back above 9,000,000,000", or "is climbing again" for a rate rule. Off unless you tick it.
• Plugins install on a slow connection. Installing a plugin gave up after 100 seconds, so anyone whose connection could not pull the whole file in that time could never install one, however many times they tried. It now has ten minutes, and if it still runs out it tells you what to try instead.
• An alert about your clan says so. An alert for a number that belongs to no single account used to start with a dash and name nobody. It now leads with the rule's own name.
• Nothing else moved. Accounts, themes, plugins, history, and settings all carry over untouched. RoRoRo still gathers no number itself — a plugin you install reports it.
```

## Longer form

### Alerts fire once, when something changes

A metric alert used to ask "is this true?" every time a number arrived. If your clan's points had
stopped climbing, that was true at 1:00, still true at 1:06, still true at 1:12 — and each one was a
fresh alert. The only thing holding it back was a five-minute gap between sends, so a stat sitting
on the wrong side of its line for three hours could buzz a phone thirty times about one problem.

It asks a different question now: **did this just become true?** An alert fires on the crossing, and
then stays quiet. Cross back over the line and cross it again, and you get a second alert, because
that is genuinely a second thing happening.

You will notice this most if you had a rule that was talking a lot. It will go quiet. That is not it
breaking — it is the rule finally saying one thing once.

Two details worth knowing:

- **A rule needs two readings before it can say anything.** One number on its own has nothing to
  compare against, so a brand-new rule waits for its second reading. With most reporters that is a
  few minutes.
- **After RoRoRo restarts, a problem that was already happening is not re-announced.** The history
  a rule compares against lives in memory, so a restart starts it fresh. It will tell you the next
  time the number genuinely crosses. The alternative was every rule that was still unhappy
  announcing itself again on every launch, which is worse.

### You can ask to be told when it comes right again

Once an alert only fires on the crossing, silence afterwards means "still bad" — and on a lock
screen that looks exactly like "nothing is wrong."

So a rule can now ask for the other half. Tick **"Also tell me when it comes right again"** when you
set the rule up, and the number coming back over its line sends a second alert:

```
K0i2 clan points is back above 9,000,000,000
• now 9,200,000,000
```

A rate rule says *is climbing again* instead. It goes to the same places the original alert goes —
there is no second setting to find — and it is **off unless you tick it**, so every rule you already
have keeps saying exactly what it said before.

### Plugins install on a slow connection

Installing a plugin from a link gave up after 100 seconds. That was not a limit on how long it would
wait for a reply — it was a limit on the whole download, so a plugin of any size on a connection
that could not finish inside a minute and a half failed every single time, with a message about a
timeout that gave you nothing to do about it.

It has ten minutes now, and it reads the file as it arrives instead of holding it all at once. If it
still runs out, it says what to try: check the connection, or download the plugin's files yourself
and unzip them into the plugins folder — which works, because RoRoRo asks for consent the first time
you launch a plugin it finds there.

Reported as [issue #136](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/136).

### An alert about your clan says so

Some numbers do not belong to any one account — where your clan is placed, how far ahead the clan
above you is. An alert about one of those used to open with a dash and name nobody at all:

```
 — Clan points went above 9,000,000,000
•  — now 9,040,000,000
```

The name of the account was simply missing, because there was no account. It now leads with the
rule's own name, so the alert reads as a sentence about the thing it is actually about.

For anyone writing a plugin: the plugin interface did not move — no new call, no new capability, no
field change. A plugin built against v1.28 or v1.29 runs against this one untouched. A rule can
carry a new optional `tellMeWhenItRecovers` field; leave it out and nothing changes.

### Known issues going into the next update

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed — the memory section
re-reads your settings to redraw, which risks stomping an edit you are in the middle of, and the
presence line re-narrates on its next update anyway. Every other surface switches instantly.
Re-checked against the code for this release, not copied forward.

**Nobody has looked at a real toast for a big group yet.** The desktop balloon's limits were measured
from the library that draws it, and the tests hold the app to them, but Windows renders that balloon
as a toast and may clip further than those numbers — and no automated test can see a toast. If a
four-account-or-more group looks wrong on your desktop, that is worth an issue.

**Two groups for the same stat but different rules can both alert one account** if they finish within
milliseconds of each other, because the cooldown is stamped after sending rather than before. Rare,
harmless, and on the list.

### Other channels

- **Microsoft Store:** [RoRoRo on the Store](https://apps.microsoft.com/detail/9NMJCS390KWB) — this
  is the version going up for certification. The Store updates you automatically once it clears.
- **Sideload MSIX:** attached to this release with `dev-cert.cer` — import the cert to Local
  Machine → Trusted People first, same as before. No cert rotation this release.

### Issues, ideas

Something broken, something missing, or a translation that reads wrong in your language:
[github.com/estevanhernandez-stack-ed/ROROROblox/issues](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues).
Privacy policy:
[estevanhernandez-stack-ed.github.io/ROROROblox/privacy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/).
