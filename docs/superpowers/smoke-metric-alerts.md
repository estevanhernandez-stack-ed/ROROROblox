# Metric alerts — the manual smoke list

> Running list. Append as work lands; tick a row only after it has actually been done on a real
> machine. Spec: [2026-09-09-external-metric-alerts-design.md](specs/2026-09-09-external-metric-alerts-design.md).

Everything here is a thing the test suite **cannot** prove. A green suite plus an untouched list
below means the feature is unverified, not verified.

Status key: `[ ]` runnable now, not done · `[x]` done, with the date and what was seen ·
`[-]` not runnable yet, with the reason.

---

## Setup — what you need before any row below

A smoke list nobody can execute is the same as no list, so this is the part that makes the rest
runnable. Three things have to be true before a single toast can appear.

**1. Turn the feature on.** There is no control for it yet (that is the routing work below), so set
it in the settings file directly while the app is CLOSED — the app rewrites settings.json on exit,
so editing it while running loses the change:

```
%LOCALAPPDATA%\ROROROblox\settings.json   →   "metricAlertsEnabled": true
```

**2. Write a rules file.** Absent means no rules, which means nothing can ever alert. Same folder:

```
%LOCALAPPDATA%\ROROROblox\metric-rules.json
```

```json
[
  { "metricId": "smoke.points", "kind": "Rate",  "threshold": 100, "windowMinutes": 10 },
  { "metricId": "smoke.share",  "kind": "Level", "threshold": 0.8,  "alertWhenBelow": true },
  { "metricId": "smoke.place",  "kind": "Event" }
]
```

`kind` is `Rate`, `Level` or `Event`. `Rate` breaches when the change per minute over the window
falls BELOW the threshold. `Level` compares the latest value against the threshold in the direction
`alertWhenBelow` sets. `Event` fires when the value simply changes. `windowMinutes` is unused by
`Level` and `Event`.

**3. Get something to report.** Nothing ships that reports a metric — that is the whole point of the
separation, and it is why `docs/plugins/AUTHOR_GUIDE.md` carries the recipe. You need a plugin that
declares `host.metrics.report`, which you then grant at the consent sheet. The cheapest thing that
works is a throwaway console app against `ROROROblox.PluginContract` calling `ReportMetric` twice
with a gap between them; two samples is the minimum for a Rate rule to compute anything at all.

**The gate is re-read every 30 seconds while a rules file exists**, so flipping the setting does not
need a restart — but flipping it does nothing at all while no rules file is present, because the
refresh skips the read entirely in that case.

---

## Runnable today

The desktop toast is the only delivery leg that works. Nothing can point a breach at Discord or a
phone until the routing control lands, so every row needing a channel or a phone sits in the next
section marked not-runnable rather than pending.

- [ ] **A breach reaches the desktop toast.** Report twice across the window, under the floor.
      The one delivery leg that is reachable today, and the row everything else rests on.
- [ ] **A consented plugin can report at all.** Grant `host.metrics.report` at the consent sheet and
      confirm the report is accepted rather than refused.
- [ ] **An unconsented plugin is denied.** Two cases, and they are different paths through the
      consent record: a capability that was granted and then revoked, and one the plugin never
      declared at all. Absence is denial, so both must refuse. Confirm `PermissionDenied` at the
      plugin AND no alert at the host — a gate that denied the caller while still recording the
      report would look identical from the plugin's side.
- [ ] **The opt-in setting actually gates it.** Turn it off, report a breaching number, confirm
      nothing fires. Then turn it on and confirm the next report is believed within 30 seconds
      without a restart. This is the row that matters most: through plan 1 the feature was off only
      because the destination list was empty, not because the toggle said so.
- [ ] **The observed value renders legibly.** Use the `Level` rule on a 0.0–1.0 ratio and report
      `0.79`. It must read `0.79`, not `0`. The unit tests pin the formatter; only a real toast
      proves the sentence reads well at toast width.
- [ ] **An unrecognised `subject_id` still reaches the user.** Report a breaching value against an
      account id RoRoRo has no record of. The alert must still fire, keyed globally, rather than
      vanishing because a plugin guessed an id wrong.
- [ ] **A clock-skewed reporter is visible, not silent.** Report an observation stamped hours in the
      future. Confirm the log says so by name. The failure this guards against is Rate rules going
      permanently quiet while Level and Event keep working, which from outside looks like nothing
      happening at all.
- [ ] **A resetting cumulative counter does not fire a false rate breach.** Report a rising value,
      then a lower one, as the author guide's worked example describes. No alert off the apparent
      drop, and normal reporting again once two fresh samples land after the reset.
- [ ] **Repeated breaches do not become repeated toasts.** Report under the floor several times in
      a row inside the five-minute cooldown. Exactly one toast. This is the guarantee the whole
      design rests on — thresholding lives in the host precisely so a bad night cannot become forty
      notifications — and nothing else on this list checks it end to end.
- [ ] **The rules file is picked up live, and its absence is inert.** With no rules file, the app
      starts clean and never alerts. Add one while running and confirm it takes effect without a
      restart.
- [ ] **A malformed rules file does not take the app down.** Truncate it mid-object. The app must
      keep running and the plugin host must keep working; you lose your rules, not your session.
      Check the log names the file — a user with a typo has nothing else to go on.
- [ ] **Streamer mode masks the metric alert.** With streamer mode on, the toast carries the masked
      name. The channel half of this row — personal masked, clan real — waits on routing.
- [ ] **The plugin pipe still binds with the new RPC present.** A missing capability-map entry
      disables plugins for the whole session and is logged only at Debug, so it does not crash and
      does not show. Confirm plugins still work AT ALL, not just that metrics work.
- [ ] **Settings still says "No alerts yet" with only metric alerts configured.** Deliberate, and it
      will look like a bug if you do not expect it: the status line excludes this kind because its
      destination is currently fixed and unchangeable. It must start counting once routing lands.

---

## Not runnable yet — waiting on the routing control

`DiscordConfig.MetricBreachDestinations` defaults to the desktop toast and nothing in the app can
change it. The Settings page reads routing checkboxes for the four older alert kinds only.

- [-] **A breach reaches the phone.** No surface writes a phone destination for this kind. The phone
      leg was only ever believed once a real phone rang (phone-alerts spec §4); the same discipline
      applies here, so this row cannot be waved through on the strength of the toast working.
- [-] **A breach reaches a Discord channel**, personal and clan, with the clan one carrying the real
      account name and the personal one the masked name.
- [-] **An unconfigured destination falls back to the desktop toast.** Point a breach at the phone
      with no phone credentials saved and confirm it lands as a toast rather than vanishing.

---

## Not runnable yet — waiting on the signed manifest

- [-] **Manifest rotation without a rebuild.** Change a field path in the manifest, re-sign, confirm
      a running install picks it up. This is the entire justification for having a manifest; if it is
      never exercised it is speculative complexity (spec §5.5).
- [-] **A bad signature falls back correctly.** Corrupt the signature and confirm resolution goes
      remote, then last-known-good cache, then off — and that "off" is visible somewhere rather than
      silent.
- [-] **No vendor hostname ships.** Grep the built binary and the hosted manifest. Spec §1.6 makes
      this the test of whether the separation is real or a fig leaf.

---

## The live one (Este-gated, needs a real clan battle)

- [-] **A real threshold crossed on purpose, and a phone that buzzes.** A real clan, a real battle, a
      member deliberately under the floor for the window. Blocked on routing, not on the manifest —
      a hand-written rules file is enough once a breach can reach a phone.
- [-] **The 3-minute server cache does not read as a stall.** Confirm the delay between a real drop
      and the alert is the cache plus the window, and that it feels like a detector rather than a
      lag. A judgement call no test can make.
- [-] **It catches the thing it was built for.** A macro that drifted out of its zone, or the wrong
      loadout — not a disconnect, which presence-as-truth already covers. If the only alerts it ever
      produces are for accounts that also dropped out, the feature is redundant and should be said
      to be.
