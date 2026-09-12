# MetricSmoke — every row proven red before it was trusted green

> What this file is: the record of `tools/MetricSmoke` being run against a live RoRoRo for the first
> time, and of each of its sixteen rows being deliberately broken to prove the row can actually go
> red. Spec §5.1: a harness that passes when the feature is broken is worse than no harness, because
> it converts an honest gap into a false assurance. **A row never seen red is not a check.**
>
> Run on 2026-09-11 against a Release build of v1.27.0 on the author's own machine and profile
> (`%LOCALAPPDATA%\ROROROblox`), driven by `tools/MetricSmoke` with `ProfileGuard` protecting the five
> profile files it writes. No Roblox client was launched and no account needed to be signed in. The
> profile was verified byte-identical afterwards — see [Restore](#restore).

## What the first honest run did

**The harness as Task 5 shipped it: 8 passed, 7 failed, 1 skipped.** Not one of those eight verdicts
was about the feature. Both defects were in the harness, and both are the kind only a live run finds.

### Defect 1 — the delivered-alert recogniser never matched a real log line

`LogTail.DeliveredPattern` was written from the call site:

```csharp
log.LogInformation("Alert → {Destination}: {Title} ({Count} account(s)).", alert.Destination, ...);
```

What the file sink actually writes is

```text
Alert → "Local":  — smoke.toast (1 account(s)).
```

`AppLogging.Configure` renders the message as `{Message:lj}`. The `l` writes **string** properties
literally; the `j` sends every other property value through Serilog's `JsonValueFormatter`. The title
is a string, so it appears bare — and `AlertDestination` is an **enum**, so it arrives JSON-quoted.
The pattern's `(?<destination>\S+)` captured `"Local"` with the quotes, every scenario compared that
against `nameof(AlertDestination.Local)`, and the comparison never matched.

So every row that filters by destination failed while the alert it asserted on had in fact been
delivered. The app's own log for that run, tallied afterwards, is unambiguous:

```text
1 Alert → "Clan":  — smoke.toast      1 Alert → "Local":  — smoke.toast      1 Alert → "Mine":  — smoke.toast
1 Alert → "Clan":  — smoke.value      1 Alert → "Local":  — smoke.value      1 Alert → "Mine":  — smoke.value
… and the same three lines each for smoke.subject, smoke.repeat, smoke.live, smoke.malformed, smoke.fallback
```

Seven rows red, twenty-one deliveries in the log, zero dispatch failures. The rows that passed were
exactly the ones asserting an **absence** (which needs no destination filter) — so the shipped
harness was wrong in the one direction that matters least on a working build and most on a broken
one: it could report a feature as broken, and a reader who then "fixed" the harness by loosening the
filter would have lost the check.

**Fixed** in `tools/MetricSmoke/LogTail.cs` (optional quotes, outside the capture) with the reason
written at the pattern, and pinned by two new tests in `LogTailTests` — one built from a line copied
verbatim out of a real run's log, one from the bare form another sink would write. The existing
fixtures had the outputTemplate's *prefix* right and its *value rendering* wrong, which is why 97
green harness tests did not catch this; the fixtures are corrected and the class comment now says
what got it wrong.

### Defect 2 — the masked-naming row skipped on a profile with accounts in it

`ResolveNamedSubjectAsync` asked the host for its saved accounts once, immediately after the pipe
answered, and took an empty answer as "this profile has no accounts". But the pipe binds early and
fire-and-forget, **before the vault is read**. Measured in the first run: the pipe answered 2 s after
launch, and the accounts landed in `MainViewModel.AccountsSnapshot` 28 s after that. So the row
skipped — on every unattended run, on a profile with eight accounts saved.

A row that always skips is not a covered row. **Fixed** by asking again until the host has accounts or
`SmokeTimings.SettingsPickup` (two of the app's own routine ticks) is spent; an empty answer after that
really is a profile with none, and the row still skips on it. Untested by construction — the helper
needs a live host — so the live run below is its test.

**The case is closed; the class is not, and this is an open limit rather than a fixed one.** A vault that
takes longer than the pickup window — a bigger account list, a slower disk, a cold DPAPI — skips the row
again, and **a skip does not change the exit code**: `Program.RunAsync` returns non-zero for failures,
never-ran rows and aborts, and a skipped row is none of those. So that run exits **0** with only a line
on stdout saying an uncovered row is not a covered row, which a human reads and no automation does. That
behaviour came from task 5 and is not changed here; stating it is the point, because a record that
credits a stdout sentence with the force of an exit code is the same species of false assurance this
whole harness exists to prevent.

### The clean run, after those two fixes

**16 passed, 0 failed, 0 skipped.** Every row ran; nothing skipped. Verdict lines, verbatim:

| Row | What it reported |
| --- | --- |
| pipe-binds | GetHostInfo answered on rororo-plugin-host |
| consented-report-accepted | ReportMetric accepted for rororo.smoke |
| denied-after-revoke | PermissionDenied for rororo.smoke.revoked, and no alert was recorded |
| denied-never-declared | PermissionDenied for rororo.smoke.undeclared, and no alert was recorded |
| breach-reaches-toast | exactly one 'Alert → Local' for a rate under the floor |
| value-renders-legibly | the body reads 'at 0.79' — the fraction survived |
| unrecognised-subject | an alert for an account id the app has never seen still landed |
| clock-skew-visible | the drop is named in the log and raised no alert |
| counter-reset-no-false-breach | no alert off the apparent drop, watched for 30s |
| repeated-breaches-one-toast | three breaches inside the cooldown, one toast |
| rules-picked-up-live | a rule written mid-session fired on the next report |
| rules-absence-inert | accepted and silent with no rules file, watched for 30s |
| malformed-rules-survivable | survived the truncated file, and a valid one worked again after it |
| streamer-mode-masks | personal body masked, clan body not, and the two differ |
| unconfigured-destination-falls-back | Phone was routed, unconfigured, and the alert landed on the desktop instead |
| opt-in-gates-it | nothing raised and nothing routed for 30s with the opt-in off |

That is the baseline every breakage below is measured against.

## The one that mattered: deleting the subscription line

One line in `App.WireAlertsAsync` is the entire link between the metric sink and the alert system:

```csharp
metricSink.AlertsRaised += (_, triggers) => _ = dispatcher.DispatchAsync(triggers);
```

Commented out, rebuilt, and the **whole unit suite still passed — 2239 tests, 0 failed**. Nothing in
this repository's test suite touches that line. The feature is completely dead and every gate is green.

The harness, run against that build: **8 passed, 8 failed.** Every row that asserts a delivery went
red; every row that asserts an absence stayed green, correctly, because with nothing delivered the
absences are all still true.

| Row | Verdict with the line gone |
| --- | --- |
| breach-reaches-toast | FAIL — no 'Alert → Local' for smoke.toast in 30s |
| value-renders-legibly | FAIL — no 'Alert → Mine' for smoke.value within 30s, so there is no body to read |
| unrecognised-subject | FAIL — no alert for smoke.subject within 30s |
| repeated-breaches-one-toast | FAIL — the first breach never reached the desktop, so there is no cooldown to test |
| rules-picked-up-live | FAIL — the file is not being re-read |
| malformed-rules-survivable | FAIL — a valid file written after the malformed one never took effect |
| streamer-mode-masks | FAIL — no 'Alert → Clan' for smoke.streamer, so there are no two bodies to compare |
| unconfigured-destination-falls-back | FAIL — nothing reached the desktop for smoke.fallback |
| the other eight | PASS — pipe, consent, both denials, skew, reset, rules-absence, opt-in |

**That is the justification for the whole harness in one row of evidence.** A green 2239-test suite and
a silently dead feature, caught in seven minutes by a tool nobody had to remember to look at.

## Every other row, and what made it go red

Grouped by build, because each group is one rebuild and one seven-minute run. Every breakage is listed
with what the harness printed, and each run's *other* rows stayed green — the attribution below is
what the runs actually showed, not what they were expected to show.

### Build A — the capability map, the gate, the cooldown, the value format

| Row | What was broken | What the harness said |
| --- | --- | --- |
| denied-after-revoke | `RpcMethodCapabilityMap["ReportMetric"]` → `null` (ungated) | FAIL — `rororo.smoke.revoked holds no grant yet ReportMetric was ACCEPTED. Absence must be denial.` |
| denied-never-declared | same | FAIL — same sentence for `rororo.smoke.undeclared` |
| opt-in-gates-it | the sink's gate func `() => MetricAlertsEnabled` → `() => true` | FAIL — `3 alert(s) fired for smoke.gate with the opt-in off.` |
| repeated-breaches-one-toast | `AlertRouter.Route`'s cooldown `.Where(…)` removed | FAIL — `three breaches inside the 5-minute cooldown produced 3 'Alert → Local' line(s), not one.` |
| value-renders-legibly | `WebhookPayload`'s `{v:0.##}` → `{(long)v}` — the exact bug that shipped until 2026-09-09 | FAIL — `the body reads 'at 0', which is not 0.79.` |

11 passed, 5 failed. No collateral: the other eleven rows were unmoved.

### Build B — stale rules, a past stamp, an unreachable floor, no masking

| Row | What was broken | What the harness said |
| --- | --- | --- |
| breach-reaches-toast | `SmokeRules.RateFloor` 100 → 0, so a rate of 1/min is no longer under the floor | FAIL — `no 'Alert → Local' for smoke.toast in 30s` |
| clock-skew-visible | the scenario stamps its report 3 h in the **past** instead of the future | FAIL — `nothing in the log names smoke.skew as dropped for a future stamp within 30s.` |
| rules-absence-inert | `LocalFileMetricRuleSource` serves the last-known rules when the file is **absent** | FAIL — `3 alert(s) fired for smoke.absent with no rules file present.` |
| malformed-rules-survivable | same source serves the last-known rules when the parse **fails** | FAIL — `an alert fired for smoke.malformed while the rules file was unparseable.` |
| streamer-mode-masks | `AlertDispatcher`'s `useRealNames:` → `true` for every destination | FAIL — `the personal channel's body does not carry the masked name the host reports for this account.` |

11 passed, 5 failed. Worth noting that the floor breakage is *also* caught statically —
`ScenarioTableTests.TheValuesTheScenariosCallBreachingDoBreach` runs the production evaluator over the
scenarios' own numbers — so that row has two independent guards, and the live one is the weaker of the
two.

### Build C — a rules file never re-read, a phone that lies, a reset that is not one

| Row | What was broken | What the harness said |
| --- | --- | --- |
| counter-reset-no-false-breach | `MetricHistory.RatePerMinute`'s decrease guard deleted, so the rate is computed across the reset | FAIL — `4 alert(s) fired for smoke.reset: a counter reset read as a rate collapse.` |
| rules-picked-up-live | the rule cache returns its first non-empty parse forever, ignoring the content hash | FAIL — `smoke.live was added to the rules file and the next report did not alert within 30s — the file is not being re-read.` |
| unconfigured-destination-falls-back | `AlertDispatcher`'s `phoneReady` drops `&& phone.IsConfigured`, so the router believes an empty `notify.dat` is a configured phone | FAIL — `1 alert(s) went to Phone, so the destination was treated as configured and this row proves nothing about the fallback.` |

13 passed, 3 failed. This build is also where the fan-out count shows through: with Phone wrongly
routed, the reset row counted **four** deliveries rather than three, because `DeliveredFor` with no
destination filter sees every leg.

### Build D — the consent grant withheld

The harness's own setup granted `host.queries.accounts` but **not** `host.metrics.report`, which is the
"revoke the grant" lever applied to the one id every reporting row uses.

| Row | What the harness said |
| --- | --- |
| consented-report-accepted | FAIL — `ReportMetric was refused with PermissionDenied. The consent grant for rororo.smoke is not in force.` |

3 passed, 13 failed. The cascade is expected and correct — with no grant nothing can be reported, so
every row that reports fails, each naming the `PermissionDenied` it got. The two **denial** rows stayed
green, which is the right answer: absence is still denial.

### Build E — the capability-map entry deleted (and what it could NOT prove)

`RpcMethodCapabilityMap["ReportMetric"]` removed entirely, which is the defect the pipe-binds row
exists for. The app started, logged

```text
[DBG] PluginHostStartupService.StartAsync threw; plugins disabled this session.
System.InvalidOperationException: RpcMethodCapabilityMap is missing entries for: ReportMetric.
```

0.8 s after launch, and ran on with plugins silently off and nothing user-visible — exactly as the
map's own comment says.

**The harness refused rather than reporting:** `RoRoRo never answered on rororo-plugin-host. Nothing was
reported.` Exit code 1, profile restored, 15 rows never run and said to be uncovered.

**But the pipe-binds row itself never produced a verdict**, because the runner's wait-for-app gate uses
the same probe the row asserts on. This breakage cannot turn that row red — it stops the run before row
1 exists. That is a loud, correct outcome and it is not the same thing as the row being a check, so it
is recorded as what it is. (Also worth knowing: this breakage is caught statically too, by
`RpcMethodCapabilityMapTests` and the harness project's `CapabilityMap_CoversEveryHostMethod`, both of
which fail red on it.)

### Build F — the host killed the instant the pipe answered

So the pipe-binds row was reached a second way, with no code change at all: the app was killed the
moment the runner printed `RoRoRo is answering on the plugin pipe`, inside the window where the naming
lookup runs and before the first scenario.

| Row | What the harness said |
| --- | --- |
| pipe-binds | FAIL — `GetHostInfo did not answer on rororo-plugin-host. Plugins are off for this session — check the log at Debug for a faulted plugin-host bind.` |

0 passed, 15 failed, 1 skipped. **So the row is a check** — its assertion can and does fail — it is
just not the capability-map breakage that shows it.

### Build G — unknown subjects dropped

`MetricReportSinkAdapter.Report` returns early when `resolveNames` hands back an empty display name,
i.e. the exact defect the unrecognised-subject row exists to catch: an alert vanishing because a plugin
named an account the app has no record of.

| Row | What the harness said |
| --- | --- |
| unrecognised-subject | FAIL — `no alert for smoke.subject within 30s — an unrecognised subject id appears to have been dropped rather than keyed globally.` |

9 passed, 7 failed. **And the signature of this run is what makes it interesting:** the streamer row —
the only row that reports against an account the host *can* name — stayed **green** while every other
delivery row went red. That pattern distinguishes "unknown subjects are being dropped" from "the
dispatcher is not wired at all", which reddens the streamer row too. The harness does not just say
something broke; the shape of which rows fall tells you which thing.

## The scoreboard

Sixteen rows, sixteen proven able to fail. Each row lists the breakage that exercises **its own**
assertion; where a row also fell to a broader breakage, that is noted but not what it is credited to.

| # | Row (scenario) | Proven red by | Verdict |
| --- | --- | --- | --- |
| 1 | pipe-binds | the host killed after the pipe answered (build F) | **check** — the capability-map breakage aborts the run instead, see build E |
| 2 | consented-report-accepted | the metrics grant withheld (build D) | **check** |
| 3 | denied-after-revoke | `ReportMetric` ungated in the capability map (build A) | **check** |
| 4 | denied-never-declared | same (build A) | **check** |
| 5 | breach-reaches-toast | the rate floor made unreachable (build B); also the subscription cut | **check** |
| 6 | value-renders-legibly | the value cast back to `long` (build A); also the subscription cut | **check** |
| 7 | unrecognised-subject | unknown subjects dropped in the sink (build G); also the subscription cut | **check** |
| 8 | clock-skew-visible | the report stamped in the past instead of the future (build B) | **check, with an asterisk** — proven from the INPUT side (the scenario's own stamp), not by changing the product; the skew check itself was never made to misbehave |
| 9 | counter-reset-no-false-breach | the rate's decrease guard deleted (build C) | **check** |
| 10 | repeated-breaches-one-toast | the router's cooldown filter deleted (build A); also the subscription cut | **check** |
| 11 | rules-picked-up-live | the rule cache never re-reading (build C); also the subscription cut | **check** |
| 12 | rules-absence-inert | stale rules served for an absent file (build B) | **check** |
| 13 | malformed-rules-survivable | stale rules served for an unparseable file (build B); also the subscription cut | **check** |
| 14 | streamer-mode-masks | `useRealNames: true` for every destination (build B); also the subscription cut | **check** |
| 15 | unconfigured-destination-falls-back | an unconfigured phone treated as configured (build C); also the subscription cut | **check** |
| 16 | opt-in-gates-it | the sink's gate hardwired to `true` (build A) | **check** |

**No row had to be marked "not yet a check."** Two carry an asterisk, and the table states both rather
than leaving them to the narrative:

- **Row 1 (pipe-binds)** has been seen to fail, but not from the breakage it was written against — the
  runner's wait-for-app gate uses the same probe the row asserts on, so the capability-map deletion
  aborts the run instead of reddening the row.
- **Row 8 (clock-skew-visible)** was reddened from the input side: the scenario stamped its report in the
  past, so the row correctly reported the absence of a drop line it had given the sink no reason to
  write. Twelve of the sixteen fell to a change in the product; this one did not, and `FutureTolerance`
  and the drop line itself have not been made to misbehave. What is proven is that the row notices when
  the line is missing — which is the assertion — not that it notices a broken skew check specifically.

## What the runs said about the feature, not the harness

- **Zero swallowed dispatch failures across eleven runs.** `TrayService.ShowToast`'s UI-thread
  marshalling (added in task 5's fix round) holds: not one `Alert dispatch failed` line appeared, in any
  run, including the ones where the toast fired on every row.
- **The fan-out is exactly three legs on this profile**, every time: `Mine`, `Clan`, `Local`, with
  `Phone` deduping into `Local` because `notify.dat` was swapped for an unconfigured record. No run ever
  produced a duplicate desktop toast from that dedupe.
- **The cooldown works as documented under real timing.** Three breaching reports a second apart
  produced exactly one delivery per destination, repeatedly, with no flake across runs.
- **A rules file is genuinely re-read live** — a rule written mid-session fired on the next report,
  every time — and a truncated one costs the rules and not the session, with a valid file working again
  immediately after.
- **The value survives as `0.79`** in the webhook body, and the `long?` regression reproduces as `at 0`
  the moment the format is reverted. That row is worth keeping for that reason alone.

## What the harness still does not prove

Recorded so the green run is not read as more than it is:

- **Whether the toast was actually drawn.** The desktop row asserts the `Alert → Local` line, which the
  dispatcher writes *before* the send; the shell actually painting the notification is still the manual
  check the design kept (§2). The swallowed-failure recogniser is what stands between that line and a
  silent failure behind it, and it was quiet in every run.
- **The fallback's counterfactual.** `Local` is routed too, so "the alert lands on the desktop instead
  of vanishing" is not what gets observed — what gets observed is the router taking the fallback arm
  (no `Alert → Phone`). Proving the vanishing needs a destination set with no `Local` in it, and
  therefore another app restart.
- **"Normal reporting again after the reset"**, the second sentence of the counter-reset row: the rate
  stays unmeasurable until the decrease leaves the ten-minute window, so asserting it costs ten minutes.
- **Six rows of the list are not harness-driven at all** — the five §3 names and *Settings still says
  "No alerts yet"*, which needs the UIA path.
- **A skipped row still exits zero.** See the note under Defect 2: the only signal is a stdout sentence.
  Any automation that ever runs this — a nightly, a pre-release gate — would read a run with an uncovered
  row as a pass. The fix is a line in `Program.RunAsync`'s return; it is not made here because no
  behaviour was changed in this task.
- **A `Cancelled` status reads as a consent problem.** With the host gone, the
  consented-report-accepted row reported "the consent grant for rororo.smoke is not in force" for what
  was actually a dead pipe. Harmless in context (row 1 had already said the host was gone) and worth
  narrowing to `PermissionDenied` next time that file is open.

## Restore

Every deliberate breakage was reverted and the tree rebuilt: `dotnet build ROROROblox.slnx -c Release`
→ 0 errors, `dotnet test ROROROblox.slnx -c Release` → **2239 unit + 27 harness passed, 1 harness
skipped by design**, and a final clean harness run against the reverted build to confirm nothing was
left broken.

The profile was checked against a SHA-256 snapshot taken before the first run:

- `discord.dat`, `notify.dat`, `accounts.dat` — **byte-identical** after every run.
- `metric-rules.json` — **absent again**, as it was before (the guard deletes a file it created).
- `consent.dat` — same size, and the three `rororo.smoke*` ids revoked by name in every run's restore
  log. The bytes differ because a revoke is a read-modify-write through DPAPI and re-encrypting the
  same content never reproduces the same envelope.
- `settings.json` — every key at its original value, `streamerMode` back to its own, and one
  difference: `metricAlertsEnabled` is now *present* and `false` where it was previously absent (and
  therefore false by default). That is the documented limit of the single-key restore — `AppSettings`
  writes the key to turn it on, and the restore puts the value back rather than the file.
