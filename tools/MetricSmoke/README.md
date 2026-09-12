# MetricSmoke

Drives 16 of the 21 rows on [`docs/superpowers/smoke-metric-alerts.md`](../../docs/superpowers/smoke-metric-alerts.md)
against a live RoRoRo, over the real plugin pipe, using your real `%LOCALAPPDATA%\ROROROblox` profile.
There is no scratch profile to run this against — the app has no data-root seam — so this tool backs
your profile up before it touches anything and puts it back when it is done. Read the whole of this
file before you run it once; it is short.

## What it proves, and what it does not

It reports metrics as a consented plugin would, watches the app's own log and a local webhook catcher
it starts itself, and asserts on what it sees: the plugin pipe answers, consent is enforced both ways,
a breach reaches the desktop toast line, the personal and clan Discord bodies, and (when it can be
routed safely) the phone leg's fallback; rules are picked up live and survive a malformed file; a
clock-skewed report is dropped and said so; a resetting counter does not fire a false breach; repeated
breaches cost one toast, not several; streamer mode masks the right channel; the opt-in gate actually
gates. Every one of those sixteen scenarios has been deliberately broken and watched turn red — the
record is [`docs/superpowers/smoke-harness-verification.md`](../../docs/superpowers/smoke-harness-verification.md).

It does **not** prove:

- **That Windows actually painted the toast.** It reads the `Alert → Local` line the dispatcher writes
  *before* it sends. Scraping the shell for a transient notification would make the harness flaky about
  something it is not asserting; glance at the toast once by eye instead.
- **That a real phone rang, or a real Discord webhook rendered anything.** The phone is deliberately
  left unconfigured so the fallback row can run without paging you, and the two Discord channels the
  streamer-mode row checks are a local catcher this tool starts, not real Discord. A local listener can
  prove RoRoRo POSTed; it cannot prove a phone buzzed or a channel rendered a message.
- **Anything about a real clan, a real battle, or whether the three-minute cache delay feels like a
  detector rather than a lag.** Those need a human watching the real thing, on purpose.
- **That the plugin-pipe row's OWN defect (a missing capability-map entry) turns it red.** That defect
  aborts the whole run before the row executes, because the run's own liveness probe is the same one
  the row asserts on. The row has been proven able to fail, just not from that breakage — see the
  verification record for how it was reached instead.
- **The clock-skew row, from the product side.** It has only been reddened by changing the scenario's
  own input (a report stamped in the past), not by breaking the app's skew detection.
- **Anything at all in CI.** This needs a real Windows profile and a human to launch the app in the
  three-minute window it opens. It has never been run there and nothing here claims otherwise.

Five rows on the smoke list, plus the status-line row that needs a UIA path nobody has built yet, stay
manual for these reasons. See the smoke list itself for exactly which and why.

## The one command

From the repo root, with RoRoRo **not** already running:

```text
dotnet run --project tools/MetricSmoke
```

If RoRoRo is already up, this refuses to start. `DiscordConfigService` reads `discord.dat` once, at
startup, and keeps that in memory — so a session already running would keep posting to your *real*
webhooks no matter how cleanly this tool rewrites the file underneath it, and a test breach could land
in your real clan channel. Quit RoRoRo from the tray first.

The run then:

1. Backs up `discord.dat`, `notify.dat`, `metric-rules.json`, the one settings key it flips
   (`metricAlertsEnabled`, plus `streamerMode` for the masked-naming row), and its own grant in
   `consent.dat` — five files, into `%LOCALAPPDATA%\ROROROblox\smoke-backup`. That folder never leaves
   the machine (`.gitignore`d) and is deleted automatically once the restore completes.
2. Swaps in a scratch config: both webhook URLs point at a catcher this tool starts on localhost, the
   phone is overwritten with an unconfigured record, a canned rules file lands, the opt-in and streamer
   mode go on, and the harness's own plugin id is granted `host.metrics.report` (plus
   `host.queries.accounts`, for the masked-naming row).
3. Reads every one of those writes back before reporting a single metric, and aborts — with nothing
   sent — if any of them disagree with what it wrote.
4. Prints `START RORORO NOW` and waits up to three minutes for the app to answer on the plugin pipe.
   Start it in that window. It has to start *after* the swap, or it will not pick any of it up.
5. Runs the sixteen scenarios (a few minutes) against the running app.
6. Restores every file it backed up, in a `finally` that also runs on the first Ctrl-C. A second Ctrl-C
   kills the process outright, for whoever wants out immediately.

Exit codes: `0` every scenario passed, `1` a scenario failed (or setup refused before anything was
sent), `2` the profile is still not fully restored — read the last few lines, they say which file.

An optional `--subject <account-guid>` picks which saved account the masked-naming row reports
against; without it, the tool asks the host for one and skips that single row if it cannot get an
answer within a couple of the app's own settings-refresh ticks.

## If a run is interrupted

A crashed run, a second Ctrl-C, or the machine losing power leaves a `smoke-run-in-progress.json`
marker behind in `smoke-backup\`, next to whatever backups it had already taken. Two ways back:

- **Run the tool again.** `dotnet run --project tools/MetricSmoke` finds the marker and restores from
  it before touching anything else, then (if that succeeded) proceeds with a fresh run.
- **Just recover, without starting a new run:**

  ```text
  dotnet run --project tools/MetricSmoke -- --recover
  ```

  Safe to run even when there is nothing to recover — it says so and exits `0`. If it cannot fully
  restore (a backup file is gone, or something else has a file open), it says exactly which file, and
  what to re-enter by hand if the backup itself is unrecoverable: the two webhook URLs, or the phone
  credentials, out of wherever you keep them.

Two runs will never race each other over the same profile: a named OS semaphore keyed to the backup
folder makes a second concurrent run (or a `--recover` against a still-live run) refuse instead of
fighting over the same files.

## Why your real profile, and not a scratch one

The app has no seam for pointing its data root somewhere else — that is the better long-term fix, and
it is written up and deliberately deferred in the harness design spec's §2 (rejected alternatives)
because building it is a bigger, riskier change than the harness it would enable, in code paths whose
failure mode is losing a user's saved accounts. Until that exists, this tool's whole job is running
against the real thing without leaving it worse off — hence the backup-and-restore rather than a sandbox.

## Related reading

- [`docs/superpowers/smoke-metric-alerts.md`](../../docs/superpowers/smoke-metric-alerts.md) — the full
  21-row list, with which rows this tool drives and which stay manual.
- [`docs/superpowers/smoke-harness-verification.md`](../../docs/superpowers/smoke-harness-verification.md) —
  the record of every row being deliberately broken and watched fail, including the two that need a
  caveat.
- [`docs/superpowers/specs/2026-09-11-metric-smoke-harness-design.md`](../../docs/superpowers/specs/2026-09-11-metric-smoke-harness-design.md) —
  the design this tool implements.
- [`docs/plugins/AUTHOR_GUIDE.md`](../../docs/plugins/AUTHOR_GUIDE.md) — how a real plugin reports a
  metric, for the manual rows this tool does not drive.
