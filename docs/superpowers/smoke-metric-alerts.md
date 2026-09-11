# Metric alerts — the manual smoke list

> Running list. Append as work lands; tick a row only after it has actually been done on a real
> machine. Spec: [2026-09-09-external-metric-alerts-design.md](specs/2026-09-09-external-metric-alerts-design.md).

Everything here is a thing the test suite **cannot** prove. A green suite plus an untouched list
below means the feature is unverified, not verified. Three of these need a live clan battle and
are Este's to run; the rest need only a running app.

Status key: `[ ]` not done · `[x]` done, with the date and what was seen · `[-]` not applicable yet
(the code it tests has not landed).

---

## Plan 1 — core (merged, PR #207, `9f39630`)

Plan 1 shipped no reachable surface: the coordinator exists, nothing feeds it, and the opt-in
setting has no reader. **There is nothing to smoke yet, and that is the honest status** — the rows
below become live when plan 2 lands the RPC.

- [-] **A breach reaches the desktop toast.** Blocked on plan 2: nothing can report a number yet.
- [-] **A breach reaches the phone.** Same. Note the phone leg was only ever believed once a real
      phone rang (phone-alerts spec §4) — the same discipline applies here.
- [-] **The observed value renders legibly.** A fractional metric must read `0.79`, not `0`. The
      unit tests pin the formatter; only a real toast proves the sentence reads well at toast width.

---

## Plan 2 — the plugin RPC (not yet written)

- [ ] **A consented plugin can report and it becomes an alert.** Install a plugin declaring
      `host.metrics.report`, grant it, report a number twice across a window, watch a toast.
- [ ] **An unconsented plugin is denied.** Revoke the capability, report again, confirm
      `PermissionDenied` at the plugin and no alert at the host. Absence is denial, so also confirm
      a plugin that never declared it is denied without having to revoke anything.
- [ ] **The opt-in setting actually gates it.** Turn metric alerts OFF, report a breaching number,
      confirm nothing fires. This is the row that matters most: through plan 1 the feature was off
      only because the destination list was empty, not because the toggle said so.
- [ ] **A clock-skewed reporter is visible, not silent.** Report an observation stamped in the
      future. Confirm the log says so. The failure this guards against is Rate rules going
      permanently quiet while Level and Event keep working, which looks like nothing at all.
- [ ] **The rules file is picked up, and its absence is inert.** With no rules file, confirm the
      app starts clean and never alerts. Add one, confirm it takes effect without a rebuild.
- [ ] **A malformed rules file does not take the app down.** Truncate it mid-object and restart.
- [ ] **Streamer mode masks the metric alert.** With streamer mode on, confirm the toast and the
      personal webhook carry the masked name and only the clan destination carries the real one.
- [ ] **The plugin pipe still binds with the new RPC present.** A missing capability-map entry
      disables plugins for the whole session and is logged only at Debug — it does not crash. So
      confirm plugins still work at all after this change, not just that metrics work.

---

## Plan 3 — the signed manifest (not yet written)

- [ ] **Manifest rotation without a rebuild.** Change a field path in the manifest, re-sign,
      confirm a running install picks it up. This is the entire justification for having a
      manifest; if it is never exercised it is speculative complexity (spec §5.5).
- [ ] **A bad signature falls back correctly.** Corrupt the signature and confirm resolution goes
      remote → last-known-good cache → off, and that "off" is visible somewhere rather than silent.
- [ ] **No vendor hostname ships.** Grep the built binary and the hosted manifest. Spec §1.6 makes
      this the test of whether the separation is real or a fig leaf.

---

## The live one (Este-gated, needs a real clan battle)

- [ ] **A real threshold crossed on purpose, and a phone that buzzes.** A real clan, a real battle,
      a member deliberately under the floor for the window. Same gate discipline as the phone-alerts
      smoke: the delivery legs were only believed once a real phone rang.
- [ ] **The 3-minute server cache does not read as a stall.** Confirm the delay between a real drop
      and the alert is the cache plus the window, and that it feels like a detector rather than a
      lag. This is a judgement call no test can make.
- [ ] **It catches the thing it was built for.** A macro that drifted out of its zone, or the wrong
      loadout — not a disconnect, which presence-as-truth already covers. If the only alerts it ever
      produces are for accounts that also dropped out, the feature is redundant and should be said
      to be.
