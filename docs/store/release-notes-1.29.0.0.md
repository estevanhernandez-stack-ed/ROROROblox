# RoRoRo v1.29.0.0 — release notes

> **Edit log.** Nothing dropped from v1.28's sections. The known-issues block is carried forward
> **verified rather than assumed**, per the playbook's standing rule: `git log v1.28.0.0..v1.29.0.0`
> touched no localization or Settings-page code, and the exclusion comment in
> `SettingsPage.xaml.cs` (line 443) still names the memory section and the Discord status line as
> deliberate. Carried forward with the reason, not just the symptom.
>
> **The five-second grouping delay is in here on purpose.** It is kept out of the Store what's-new
> block — a Store reader cannot act on it and it makes them worry about something they will never
> notice — but the clan reads these notes to know what changed, and "your alert now waits up to five
> seconds" is a real cost. It gets a sentence in the short list and a paragraph below it.
>
> **Not in the clan notes, on purpose:** the 22-row smoke harness, the scenario count, and the jump
> from 2,258 to 2,311 unit tests. Real work, invisible to anyone using the app.
>
> **No plugin is named.** A reporter exists, but it is not in `docs/store/plugins-catalog.json`, so
> nobody can install one from inside RoRoRo yet. Naming it here would be a promise with someone
> else's release date attached.

---

# RoRoRo v1.29.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

```
• Metric alerts say what fired. An alert used to read "ps99.diamonds at 2974993". It now reads "Diamonds went above 0" over "now 2,974,993" — the name you gave the rule, what you asked it to watch for, and numbers with commas in them. A rule with no name still falls back to the metric id, the way it read before.
• One read is one alert. A plugin reporting a number for eight accounts at once used to send eight alerts to every destination. That is now one: "8 accounts — Diamonds went above 0", a line per account. It costs up to five seconds, because the group waits that long for the rest of the read.
• Two different stats on one account both get through. Metric alerts shared one five-minute cooldown per account, so whichever crossed its line first silenced the other. Each stat has its own now. Every other kind of alert keeps the cooldown it had.
• Nothing inside an alert can ping a channel. Discord posts go out with mentions switched off, so a rule name, a metric id or an account name that happens to contain @everyone stays text. Every alert kind, not just this one.
• A long alert still arrives. A list of accounts too long for Discord, your phone or the desktop toast is trimmed to fit and ends with "and 12 more", instead of failing to post or getting cut off mid-name.
• Nothing else moved. Accounts, themes, plugins, history, and settings all carry over untouched. RoRoRo still gathers no number itself — a plugin you install reports it, and nothing in this release reports one.
```

## Longer form

### Metric alerts say what fired

The first real run of metric alerts, on 2026-09-15, sent 24 notifications for one plugin read, and
every one of them read like this:

```
CElCPapa — ps99.diamonds
• CElCPapa — ps99.diamonds at 2974993
```

That is the metric id and a bare number. It does not say which rule fired, what you asked it to
watch for, or whether 2974993 is good news. The alert never carried the rule that fired, so there
was nothing to say it with.

It carries it now. The same breach posts `CElCPapa — Diamonds went above 0` over
`• CElCPapa — now 2,974,993`, and the four kinds of rule each get their own sentence: a Rate rule
says **stopped climbing** and gives you the measured rate, the window it measured over and the
floor you set; a Level rule says **fell below** or **went above** its threshold with the current
value; an Event rule says **changed**. Numbers of a thousand and up get separators.

**The name comes from you.** A rule row takes an optional `label` in `metric-rules.json` — "Diamonds",
"Points", whatever you call the thing — and that is what the alert prints. A rule without one falls
back to the metric id exactly as it did in 1.28, so an old rules file keeps working and just reads
the way it always did.

One thing worth knowing if you play in another language: the alert sentence is English everywhere.
One payload feeds the desktop toast, both webhooks and your phone, and it composes in English. The
app's own interface stays in your language.

### One read is one alert

A plugin reports a number one account at a time, and alerts only ever grouped what arrived in a
single call. So eight accounts in one read became eight alerts, to every destination you had turned
on. Twenty-four notifications for one thing happening.

A metric breach now waits five seconds for the rest of its read, and everything that breached the
same rule on the same stat in that time goes out as one alert: `8 accounts — Diamonds went above 0`,
one line per account. Mute, your destinations and the desktop fallback all work exactly as before —
this happens before the router, not instead of it.

**The cost, stated plainly:** every metric alert now lands up to five seconds later than it used to,
and a plugin that spreads one read over more than five seconds will send you two alerts for it. Five
seconds against a five-minute cooldown is a trade worth making, but it is a real five seconds.

**Two different stats on one account now both reach you.** Alerts are silenced for five minutes per
account per kind, which meant a Points stall and a Diamonds drop on one account in the same read
were one kind, and whichever sent first swallowed the other. A metric alert's five minutes is now
per stat. Two rules on the *same* stat still share it — that is the same news twice, not two things.
Drops, memory warnings, recycle completions and the all-good mark are untouched.

### Alerts can't ping a channel, and a long one still arrives

Two bits of hardening that apply to **every** alert kind, not just metric ones.

**Mentions are off.** Every Discord post now goes out with mentions disabled, so nothing inside an
alert can notify the channel it lands in. Rule labels are hand-edited, metric ids come from plugins
and account names come from you, and the clan destination is a shared channel — an `@everyone`
anywhere in any of those now stays text.

**A long list is trimmed, and says so.** An alert with more accounts than its destination can carry
used to fail to post, or get cut off mid-name on the desktop with no sign anything was missing. Each
destination now gets as many lines as it actually holds and ends with `and 12 more`. The desktop
balloon holds far less than a webhook does, so a big group names three or four accounts there and
tells you how many it did not name; Discord and your phone get the long version.

### Compatibility

Nothing to do. No new permissions, no migration, no settings change, and the package declares
exactly what v1.28 did.

An old `metric-rules.json` keeps working unchanged. Add `"label": "Diamonds"` to a rule row when you
want the friendly name in the alert.

For anyone writing a plugin: the plugin interface did not move — no new call, no new capability, no
field change. A plugin built against v1.28 runs against this one untouched.

### Known issues going into the next update

**Two spots still wait for the next open when you change language:** the memory section in Settings
and the Discord presence status line. Both are deliberate rather than missed — the memory section
re-reads your settings to redraw, which risks stomping an edit you are in the middle of, and the
presence line re-narrates on its next update anyway. Every other surface switches instantly. Same as
v1.28; re-checked against the code rather than copied forward.

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
