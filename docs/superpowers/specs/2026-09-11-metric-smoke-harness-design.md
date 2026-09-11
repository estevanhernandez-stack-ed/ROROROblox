# RoRoRo — automating the metric-alert smoke list

> Design, 2026-09-11. Status: **draft for review, not approved.**
> Origin: Este — "we built this app for automated smoke tests. Let's see how much of it we can
> automate." The metric-alert smoke list has 21 rows and none has ever been run.

## §0 What was measured before designing

Every fact below was read out of the code, not assumed. Four of them are the reason this is a
tractable piece of work rather than a large one.

1. **The capability gate needs no plugin install.** `CapabilityInterceptor.EnforceCapability`
   checks exactly three things: the method appears in `RpcMethodCapabilityMap`, an `x-plugin-id`
   header is present, and the consent lookup for that id contains the required capability. It never
   consults the installed-plugin registry. **A driver therefore needs a pipe connection, a header,
   and a consent record — no manifest, no `/plugins/` install, no consent sheet.** This is the single
   fact that collapses most of the expected difficulty.

2. **Consent is programmatically writable.** `ConsentStore(string filePath)` exposes
   `GrantAsync(pluginId, capabilities)` and `RevokeAsync(pluginId)`. The app reads
   `%LOCALAPPDATA%\ROROROblox\consent.dat` (`App.xaml.cs:1139`). A harness can grant itself a
   capability and revoke it afterwards, so consent is **self-cleaning** and needs no backup.

3. **The log is a precise pass/fail surface.** Serilog, daily-rolling into
   `%LOCALAPPDATA%\ROROROblox\logs\`, with `ROROROblox.*` at Debug so Information lines land. The
   three lines that matter already exist and were written for humans debugging, which makes them
   good assertions:
   - delivered: `Alert → {Destination}: {Title} ({Count} account(s)).`
   - suppressed: `Alert raised for {Count} account(s) but routed nowhere — check the destination,
     the per-account mute, and the {Cooldown}-minute cooldown.`
   - clock skew: `Dropped a metric report for {MetricId} stamped {Skew} in the future …`

4. **The log carries the alert's Title but not its Body.** So the log alone cannot prove the observed
   value renders as `0.79` rather than `0`, nor that the personal channel gets the masked name while
   the clan channel gets the real one. That is what forces a **local HTTP listener standing in as a
   Discord webhook** into this design; it is not gold-plating.

5. **There is no data-root override, and adding one is bigger than this harness.** `LocalApplicationData`
   is reached directly in 30 places across Core and App; `AppSettings.DefaultPath()` is one of them.
   So the harness cannot redirect the app at a scratch profile. It must operate on the real one, which
   is the central constraint this design answers (§1.4).

6. **No production change is needed.** Everything above is already reachable. `AppLogging.Configure`
   does take a log-directory override, but only a test uses it and production passes none — the
   harness reads the real log from a recorded offset instead, which needs nothing from the app.

7. **UIA against this app is proven.** `scripts/capture-ui.ps1` drives it by AutomationId through a
   full language sweep, including reading localized control text. One smoke row needs that and no more.

## §1 The decision

**A non-shipping driver that talks to the real pipe, a local webhook receiver, and a runner that sets
up state, replays scenarios, and asserts — covering 16 of 21 rows, with the other 5 staying manual on
purpose.**

1. **`tools/MetricSmoke/`, not a test project.** `tools/CompatSigner` is the established home for a
   non-shipping executable in this repo. It must never be referenced by the app and never reach the
   Store build.

2. **The driver is a plugin only in the sense the interceptor cares about.** It opens the named pipe
   `rororo-plugin-host`, sends `x-plugin-id: rororo.smoke`, and calls `ReportMetric`. It is not
   installed, has no manifest, and appears in no UI. §0.1 is why that works.

3. **The webhook receiver is a local `HttpListener`** that the runner points `discord.dat`'s personal
   and clan webhook URLs at. It captures the exact payload the app POSTs, which is the only way to
   assert on the alert body — the observed value's formatting, and masked-versus-real naming.

4. **State is touched per-file, with the lightest strategy each file allows.** This is the part that
   decides whether the harness is one anybody runs twice, because it operates on Este's real profile:

   | File | What the harness needs | Strategy |
   | --- | --- | --- |
   | `consent.dat` | grant `host.metrics.report` to `rororo.smoke` | **Self-cleaning.** `RevokeAsync` afterwards. No backup — the harness only ever adds and removes its own id. |
   | `settings.json` | `metricAlertsEnabled` on and off | **Single key.** Read the original value, restore it at the end. Never rewrite the file wholesale. |
   | `metric-rules.json` | scenario rules | **Harness-owned.** Back up only if one already exists, then restore or delete. |
   | `discord.dat` | destinations plus two webhook URLs | **Full backup and restore.** The only file needing it, and it holds real webhook URLs, so the backup never leaves the machine and is never committed. |

5. **A crash must not leave the profile broken.** The runner writes a marker beside its backups
   before touching anything and removes it on clean exit. A run that finds an orphaned marker
   restores from those backups before doing anything else, and says so. Without this, one Ctrl-C
   leaves Este with a config pointing at a dead localhost webhook.

6. **Local one-command runner. CI is not promised.** It drives a real WPF app with a tray and toasts.
   Whether a GitHub runner handles that is unknown, and claiming CI before proving it is how a smoke
   harness becomes shelfware. Prove it stable locally first.

## §2 Rejected alternatives, with reasons

- **Add a data-root seam to production so the harness gets a scratch profile.** The genuinely better
  long-term answer: one `AppPaths.DataRoot` honouring an override, replacing 30 direct
  `LocalApplicationData` calls. Rejected for now because it is a larger, riskier change than the
  thing it enables, in code paths whose failure mode is losing a user's accounts. Worth doing on its
  own merits later; then this harness gets isolation and CI almost free.
- **Run the app as a second Windows user or in a VM.** True isolation, and too heavy to run per
  release. The point is a command Este actually types.
- **Assert by screen-scraping the toast.** A Windows toast is rendered by the shell, is transient,
  and is exactly the kind of thing that makes a harness flaky. The dispatcher's log line proves the
  alert was routed and sent; that the shell then drew it is what the one remaining manual toast check
  is for.
- **Extend `ur-mcp` with a ReportMetric tool instead of writing a driver.** That plugin lives in
  another repo and ships to users. Putting a smoke-test surface in a shipped plugin to save writing
  one here is backwards.
- **Mock the phone.** Covered in §3. A mock that "proves" the phone leg is worse than an untested one,
  because it converts an honest gap into a false green.

## §3 What deliberately stays manual

Five rows, and each for a reason that no harness changes.

1. **A breach reaching a real phone**, and 2. **the Este-gated version during a real battle.** The
   phone-alerts spec's discipline is that the delivery legs were only ever believed once a real phone
   rang. A local listener can prove the app POSTed; it cannot prove a phone buzzed.
3. **Whether the three-minute cache delay reads as a detector rather than a lag.** A judgement call.
4. **Whether it catches what it was built for** rather than only flagging accounts that already
   dropped out. If every alert it ever produces coincides with a drop-out, the feature is redundant —
   and only a human watching a real battle can say so.
5. **The Windows toast actually appearing.** §2 explains why scraping it is worse than checking it
   once by eye.

Everything else — 16 rows — is automatable, and one of those 16 is already covered by a unit fence.

## §4 Open questions for review

1. **Does the app need restarting between scenarios?** The rules file is re-read per report and the
   settings gate refreshes on a 30-second tick, so most scenarios should need no restart. Two might:
   the malformed-rules row, and anything depending on startup wiring. Resolve by trying it, not by
   designing for the worst case.
2. **Is `tools/` in `ROROROblox.slnx`?** `CompatSigner` is invoked by path in a workflow, which does
   not settle it. If adding the driver to the solution would put it in the Store build, it stays out
   and is invoked by path.
3. **How does the runner know the app finished starting?** The pipe's own ungated `GetHostInfo` is the
   obvious liveness probe and needs no extra surface. Confirm it is reachable before the gate modals.
4. **Should the harness assert the absence of a log line, and for how long?** "No alert fired" is a
   negative, and a negative with too short a window is a false green. A fixed wait keyed to the
   cooldown is the likely answer.

## §5 Test plan — how the tester gets tested

A smoke harness that passes when the feature is broken is worse than no harness, so:

1. **Every assertion must be proven to fail.** For each automated row, break the thing it checks —
   toggle the gate, delete the subscription line, revoke consent — and confirm that row goes red.
   A row never seen red is not a check. This is the same discipline the no-vendor fence was held to.
2. **The restore path is tested by killing the runner mid-run**, then running it again and confirming
   it detects the orphaned marker and restores. §1.5 is worthless untested.
3. **The driver's denial cases must fail closed.** Revoke consent and confirm `PermissionDenied`
   rather than a silent no-op, so a harness that has quietly lost its grant cannot report green.
4. **Unit-testable parts get unit tests**: the log parsing, the payload assertions, the backup and
   restore logic. The orchestration itself is proven by §5.1 rather than by unit tests.
