# Metric alerts — the manual smoke list

> Running list. Append as work lands; tick a row only after it has actually been done on a real
> machine. Spec: [2026-09-09-external-metric-alerts-design.md](specs/2026-09-09-external-metric-alerts-design.md).

Everything here is a thing the test suite **cannot** prove. A green suite plus an untouched list
below means the feature is unverified, not verified.

Status key: `[ ]` runnable now, not done · `[x]` done, with the date and what was seen ·
`[-]` not runnable yet, with the reason.

A row whose title is followed by `` `[harness]` `` is driven by `tools/MetricSmoke` — one command
replays it and asserts it (`dotnet run --project tools/MetricSmoke`). The marker is machine-read:
`ScenarioTableTests` fails if the harness's scenario table names a row that is not marked here, if a
marked row has no scenario, or if the total number of rows on this list changes. So marking a row is
a claim the tests hold you to, and 14 rows carry it today — 16 scenarios, because two of those rows
carry two cases each (granted-then-revoked versus never-declared; live pickup versus absence). A
marker is not a tick: the harness has to be RUN, and the row still gets its `[x]` and its date from
whoever ran it.

**2026-09-11: the harness has now been run, and every row it drives has been proven able to go red.**
Sixteen scenarios green against v1.27.0 on a real machine, and then each one deliberately broken and
watched to fail — including the `App.xaml.cs` line the whole feature hangs from, whose deletion leaves
all 2239 unit tests green. The record, with what was broken and what was seen, is in
[smoke-harness-verification.md](smoke-harness-verification.md). No box below is ticked from that run:
several rows keep a half the harness cannot see (whether the shell actually drew the toast, above all),
and that half is what a tick would be claiming.

Two of the sixteen do not get an unqualified pass even in that record, and this list does not smooth
that away: **the plugin-pipe row** has only ever been reached by a different breakage than the one it is
named for, and **the clock-skew row** was reddened by changing the scenario's own input rather than by
breaking the product. Both caveats are repeated at the row itself, below, rather than left to this
paragraph alone.

---

## Setup — what you need before any row below

**The sixteen `[harness]` rows: one command.** From the repo root, with RoRoRo **not** already
running:

```text
dotnet run --project tools/MetricSmoke
```

If RoRoRo is up, the tool refuses to start rather than risk it — `discord.dat` is read once, at
startup, so a running app would keep posting to your real webhooks no matter how cleanly this tool
rewrites the file underneath it (the finding that shaped the runner; see
[smoke-harness-verification.md](smoke-harness-verification.md)). Quit it from the tray first.

What it does to your profile (`%LOCALAPPDATA%\ROROROblox`), and how it gives it back:

- Backs up `discord.dat`, `notify.dat`, `metric-rules.json`, the one settings key it flips
  (`metricAlertsEnabled`, and `streamerMode` for the masked-naming row), and its own grant in
  `consent.dat` — five files, into `%LOCALAPPDATA%\ROROROblox\smoke-backup`, which never leaves the
  machine and is `.gitignore`d.
- Swaps in a scratch config: both webhook URLs point at a local catcher it starts itself, the phone
  is overwritten with an unconfigured record, a canned rules file lands, the opt-in and streamer mode
  go on, and its own plugin id is granted `host.metrics.report` (plus `host.queries.accounts`, for
  the masked-naming row) — then reads every one of those writes back before reporting a single
  metric, and aborts with nothing sent if any of them disagree.
- Prints `START RORORO NOW` and waits up to three minutes for the app to answer on the plugin pipe —
  start it in that window. It has to start *after* the swap to pick any of it up.
- Runs the sixteen scenarios (a few minutes), then restores everything it backed up, in a `finally`
  that also fires on the first Ctrl-C (a second one kills it outright, for whoever wants out now).

**If a run is interrupted** — a second Ctrl-C, a crash, the machine losing power — the next
`dotnet run --project tools/MetricSmoke` finds the marker the dead run left and restores from it
before touching anything else. To put the profile back without starting a new run:

```text
dotnet run --project tools/MetricSmoke -- --recover
```

It is safe to run even when nothing needs recovering — it says so and exits. A
`smoke-run-in-progress.json` sitting in `smoke-backup\` is the sign a restore did not finish; the
tool's own output names which file(s) it could not put back, and what to re-enter by hand
(webhook URLs, phone credentials) if the backup itself is gone too.

**The rows that stay manual** — the five below marked as such, plus *Settings still says "No alerts
yet"* — need the app running normally, no harness, with:

1. **Metric alerts** ticked on in Settings → Alerts. The app writes `metricAlertsEnabled` into
   `settings.json` itself; the toggle nudges the gate the moment the write succeeds, not on the next
   poll.
2. A real rules file at `%LOCALAPPDATA%\ROROROblox\metric-rules.json` — absent means no rules, which
   means nothing can ever alert:

   ```json
   [
     { "metricId": "smoke.points", "kind": "Rate",  "threshold": 100, "windowMinutes": 10 },
     { "metricId": "smoke.share",  "kind": "Level", "threshold": 0.8,  "alertWhenBelow": true },
     { "metricId": "smoke.place",  "kind": "Event" }
   ]
   ```

   `kind` is `Rate`, `Level` or `Event`. `Rate` breaches when the change per minute over the window
   falls BELOW the threshold. `Level` compares the latest value against the threshold in the
   direction `alertWhenBelow` sets. `Event` fires when the value simply changes. `windowMinutes` is
   unused by `Level` and `Event`.
3. A plugin that actually reports something — nothing ships that does, which is the whole point of
   the separation. `docs/plugins/AUTHOR_GUIDE.md` has the recipe; the cheapest thing that works is a
   throwaway console app against `ROROROblox.PluginContract` calling `ReportMetric` twice with a gap
   between them, granted `host.metrics.report` at the consent sheet.
4. For the phone and Discord rows specifically: real credentials or webhooks saved in Settings →
   Alerts. The point of those two rows is that a real destination receives it — the one thing the
   harness's own local catcher exists to avoid needing.

Toggling from Settings is live immediately — no restart, no 30-second wait. The periodic 30-second
re-read still exists underneath (it is what catches a hand-edited `settings.json`, still a supported
way in), and it skips the read entirely while no rules file is present, so a hand edit made before
any rules file exists waits for the tick after the file appears, not 30 seconds from the edit.

---

## Runnable today

All four delivery legs work: desktop toast, personal Discord channel, clan Discord channel, and
phone, resolved through the same `AlertRouter`/`AlertDispatcher` every other alert kind already
uses. The routing control lives in Settings → Alerts, alongside the **Metric alerts** opt-in toggle.

- [ ] **A breach reaches the desktop toast.** `[harness]` Report twice across the window, under the floor.
      The simplest leg to test first — no webhook, no phone credentials, no routing tick required —
      and the row everything else rests on. The harness reads the `Alert → Local` line the dispatcher
      writes *before* it sends, which is not the same claim as the shell having drawn the toast — that
      one stays a by-eye check, once, because scraping a transient shell notification is worse than
      having no check at all (harness spec §2).
- [ ] **A consented plugin can report at all.** `[harness]` Grant `host.metrics.report` at the consent sheet and
      confirm the report is accepted rather than refused.
- [ ] **An unconsented plugin is denied.** `[harness]` Two cases, and they are different paths through the
      consent record: a capability that was granted and then revoked, and one the plugin never
      declared at all. Absence is denial, so both must refuse. Confirm `PermissionDenied` at the
      plugin AND no alert at the host — a gate that denied the caller while still recording the
      report would look identical from the plugin's side.
- [ ] **The opt-in setting actually gates it.** `[harness]` Turn it off, report a breaching number, confirm
      nothing fires. Then turn it on and confirm the next report is believed immediately — the
      Settings toggle nudges the gate the moment it saves, without waiting on the 30-second poll.
      This is the row that matters most: through plan 1 the feature was off only because the
      destination list was empty, not because the toggle said so.
- [ ] **The observed value renders legibly.** `[harness]` Use the `Level` rule on a 0.0–1.0 ratio and report
      `0.79`. It must read `0.79`, not `0`. The unit tests pin the formatter; only a real toast
      proves the sentence reads well at toast width.
- [ ] **An unrecognised `subject_id` still reaches the user.** `[harness]` Report a breaching value against an
      account id RoRoRo has no record of. The alert must still fire, keyed globally, rather than
      vanishing because a plugin guessed an id wrong.
- [ ] **A clock-skewed reporter is visible, not silent.** `[harness]` Report an observation stamped hours in the
      future. Confirm the log says so by name. The failure this guards against is Rate rules going
      permanently quiet while Level and Event keep working, which from outside looks like nothing
      happening at all. **Caveat carried from the verification record:** this row has only ever been
      proven red by changing the *scenario's* stamp — the input side — not by breaking the product's
      skew detection. `FutureTolerance` and the drop-log line have not themselves been made to
      misbehave; what is proven is that the row notices a missing drop line, not that it notices a
      broken skew check specifically.
- [ ] **A resetting cumulative counter does not fire a false rate breach.** `[harness]` Report a rising value,
      then a lower one, as the author guide's worked example describes. No alert off the apparent
      drop, and normal reporting again once two fresh samples land after the reset.
- [ ] **Repeated breaches do not become repeated toasts.** `[harness]` Report under the floor several times in
      a row inside the five-minute cooldown. Exactly one toast. This is the guarantee the whole
      design rests on — thresholding lives in the host precisely so a bad night cannot become forty
      notifications — and nothing else on this list checks it end to end.
- [ ] **The rules file is picked up live, and its absence is inert.** `[harness]` With no rules file, the app
      starts clean and never alerts. Add one while running and confirm it takes effect without a
      restart.
- [ ] **A malformed rules file does not take the app down.** `[harness]` Truncate it mid-object. The app must
      keep running and the plugin host must keep working; you lose your rules, not your session.
      Check the log names the file — a user with a typo has nothing else to go on.
- [ ] **Streamer mode masks the metric alert.** `[harness]` With streamer mode on, the toast carries the masked
      name, the personal Discord channel carries the masked name, and the clan Discord channel
      carries the real one — the same policy every other alert kind's channel routing already uses.
- [ ] **The plugin pipe still binds with the new RPC present.** `[harness]` A missing capability-map entry
      disables plugins for the whole session and is logged only at Debug, so it does not crash and
      does not show. Confirm plugins still work AT ALL, not just that metrics work. **Caveat carried
      from the verification record:** this row has never actually gone red for that defect — deleting
      the capability-map entry makes the whole run abort before this row runs at all, because the
      runner's own wait-for-app gate uses the same probe the row asserts on. It has been proven able to
      fail, but only by killing RoRoRo the instant the pipe answers, before the row's own check
      completes.
- [ ] **Settings still says "No alerts yet" while the Metric alerts toggle is off.** No harness covers
      this yet. Deliberate, and it will look like a bug if you do not expect it: the status line
      excludes `MetricBreach` from the count until the toggle is on, even though
      `MetricBreachDestinations` defaults to the desktop toast underneath. Turn the toggle on and
      confirm the sentence starts counting it. This is a status-LINE assertion, not a log line — the
      harness reads the app's log file and never its UI, so covering this row needs a UIA path nobody
      has built yet, not a limit of what a log can say.
- [ ] **A breach reaches the phone. No harness will ever cover this.** Tick **My phone** on the
      metric-alerts routing row in Settings → Alerts, with phone credentials saved, report a
      breaching value, and confirm the push notification arrives. The phone leg was only ever
      believed once a real phone rang (phone-alerts spec §4); the same discipline applies here. A
      local listener can prove RoRoRo POSTed; it cannot prove a phone buzzed.
- [ ] **A breach reaches a Discord channel, personal and clan.** No harness will ever fully cover
      this, with the clan one carrying the real account name and the personal one the masked one.
      Tick **My channel** and **Clan channel** on the metric-alerts routing row with a webhook saved
      for each, report a breaching value on each, and confirm both arrive with the right name policy.
      The harness's own `streamer-mode-masks` scenario proves the routing and the masking policy
      against a local catcher it starts itself; it cannot prove a payload lands and renders in real
      Discord, so a real webhook stays a by-hand check.
- [ ] **An unconfigured destination falls back to the desktop toast.** `[harness]` Tick **My phone** on the
      metric-alerts routing row with no phone credentials saved, report a breaching value, and
      confirm it lands as a toast rather than vanishing.
- [ ] **A real threshold crossed on purpose, and a phone that buzzes (Este-gated).** No harness will
      ever cover this. Not a repeat of the phone row above — that one is a synthetic report anyone
      can make from a throwaway plugin; this one needs a real clan, a real battle, and a member
      deliberately under the floor for the window, which is the only way to confirm the whole chain
      fires end to end against a live signal instead of a manufactured one. No harness manufactures a
      real battle, so this stays a live event, not a rerunnable check. No longer blocked on routing —
      a hand-written rules file and the phone checkbox are enough now that a breach can reach a
      phone. Needs Este to run a real clan battle; nobody else can tick this box.

---

## No vendor name ships — mostly automated, one thing left to eyeball

The signed 626-hosted manifest this section used to wait on is dropped (2026-09-11 — see
`docs/decisions.md` and the banner at the top of the spec). There is no manifest to rotate and no
signature to corrupt, so those two rows are gone rather than parked.

- [ ] **No vendor hostname or company name ships in source.** `NoVendorNameFenceTests` now proves
      this on every run instead of relying on review: it scans every
      `.cs`/`.proto`/`.resx`/`.xaml`/`.csproj`/`.json`
      file under `Core`, `App` and `PluginContract` for the vendor's own name and the acronym its
      community trackers use for it ("biggames" / "big games" / "bgsi"). It deliberately does
      **not** forbid the *game's* name — that has been pervasive, pre-existing and shipped in Core
      and App since before metric alerts existed (account-tag examples, the memory-headroom
      advisor's tuning comments, the Discord roster and session-history accessibility work), and
      this app's own audience is a clan that plays it. If you write about this fence elsewhere, say
      what it actually covers — the company and its API, not the game — or you have overclaimed in
      the same breath as correcting one.
      What the fence cannot see is a vendor endpoint that never spells the vendor's name. The
      design spec's own host is `ps99.biggamesapi.io`, and "biggames" is the only half of that the
      fence matches — a URL written to the same API against a bare `ps99.*` subdomain, a numeric
      host, or a rebranded hostname that has dropped the company name would pass clean. So would
      any of it arriving somewhere the scan does not reach at all: the Store listing copy,
      screenshots and reviewer letters, or the compiled binary's embedded resources.
      By eye before a release, then: grep the three shipping projects for `http` and read every
      hit. There should be no host in `Core`, `App` or `PluginContract` that belongs to a game's
      API rather than to Roblox, GitHub or 626 Labs — whatever it calls itself. The fence proves
      the name is absent; only a human proves the *call* is.

---

## The live one (Este-gated, needs a real clan battle)

- [-] **The 3-minute server cache does not read as a stall. No harness will ever cover this.**
      Confirm the delay between a real drop and the alert is the cache plus the window, and that it
      feels like a detector rather than a lag. A judgement call no test can make — "feels like" has
      no assertion.
- [-] **It catches the thing it was built for. No harness will ever cover this.** A macro that
      drifted out of its zone, or the wrong loadout — not a disconnect, which presence-as-truth
      already covers. If the only alerts it ever produces are for accounts that also dropped out, the
      feature is redundant and should be said to be — and only a human watching a real battle, not a
      synthetic report, can tell the two apart.
