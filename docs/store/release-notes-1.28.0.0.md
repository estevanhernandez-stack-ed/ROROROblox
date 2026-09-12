# RoRoRo v1.28.0.0 — release notes

> **Edit log.** Nothing dropped from v1.27's sections. The known-issues block is carried forward
> **verified rather than assumed**, per the playbook's standing rule: the two surfaces that wait for
> the next open are still excluded from the culture-change refresh, and `SettingsPage.xaml.cs`'s own
> comment says the exclusion is deliberate with stated reasons. So it is carried forward with the
> reason added, not just the symptom.
>
> **Not in the clan notes, on purpose:** the smoke harness and the eighteen manual rows walked
> against it. Real work, invisible to anyone using the app.

---

# RoRoRo v1.28.0.0

**Download:** [rororo-win-Setup.exe](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest)

## Short list, for the GitHub release and the Discord post

- Alerts can watch a number now, and tell you when it drops. A plugin supplies the number.
- Setting up phone alerts through ntfy is a camera photo instead of typing 33 characters.
- Settings stopped losing your edits — Enter saves, saved webhooks and keys have a Remove button.
- Streamer mode now hides a webhook or key you had revealed, instead of leaving it on screen.

## Longer form

### Alerts can watch a number

There is a **Metric alerts** switch in Settings → Alerts. Turn it on and RoRoRo will tell you when
a number crosses a line you set — as a desktop toast, in your own Discord channel, in the clan's,
or on your phone. It rides the same routing everything else does, which means it inherits the
five-minute cooldown per account. A bad night cannot turn into forty notifications.

**RoRoRo does not go and get the number.** It has no address for anything to fetch, on purpose —
there is a test in the build that fails if one ever appears. A plugin you install reports the
number, and you decide whether to allow that when you install it. Nothing in this release reports
one yet.

You set the thresholds in a small file of your own, so you decide what "too low" means rather than
arguing with a preset.

### Phone setup is a photo

Connecting ntfy used to mean typing a 33-character topic into a phone keyboard, with no error
correction and no way to tell you had got a character wrong except that nothing arrived. Point your
camera at the code instead.

The code hides itself while streamer mode is on. That topic is the credential — anyone who scans it
can read your alerts — and a code lifts off a paused frame far more easily than 33 characters of
text ever did.

### Settings stopped losing your edits

Three things on the Alerts page that were quietly annoying:

**Enter saves.** Every box on that page held your typing until you clicked somewhere else. On the
webhook fields you could at least see it — the field re-masks when it saves, and it just sat there.
Everywhere else it was silent, and "nothing happened" looks exactly like "the save failed."

**Saved webhooks and phone keys have a Remove button.** Clearing one used to be reveal, select all,
delete, then click away.

**Streamer mode protects what is already on screen.** Turning it on now hides a webhook or key you
had revealed. Clicking Show while it is on asks first. Before this, streamer mode masked your
account names and left a Discord webhook URL sitting in plain view — and a webhook URL is short
enough to read off a single paused frame.

The alerts status line also moved up, to sit under the rows it summarises and above the fields it
points at. Three of the things it can say are "paste it below" and "check the key below", and it
had been sitting underneath both of them.

### Compatibility

Nothing to do. No new permissions, no migration, and the package declares exactly what v1.27 did.

For anyone writing a plugin: there is one new call in the plugin interface, and a plugin must
declare it and be granted it at install like every other capability.

### Known issues going into the next update

Two spots still wait for the next open rather than switching in place when you change language: the
memory section in Settings, and the Discord presence status line. Both are deliberate rather than
missed — the memory section re-reads your settings to redraw, which risks stomping an edit you are
in the middle of, and the presence line re-narrates on its next update anyway. Every other surface
switches instantly.

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
