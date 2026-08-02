# RoRoRo memory watchdog + warning system — design

> **Banner correction (2026-08-01, task 10):** this doc's Scope section says "plugin contract
> 0.5.0 exposing memory pressure to plugins." By the time task 10 landed, two unrelated
> additive bumps had already shipped on `ROROROblox.PluginContract` between this spec's
> writing and implementation — `host.commands.mark-account-active` at 0.5.0 and
> `host.commands.stop-accounts` at 0.6.0 — so the memory-pressure capability
> (`host.events.memory-pressure`, rpc `SubscribeMemoryPressure`) shipped at **0.7.0**, not
> 0.5.0. The 0.5.0 number was correct when this doc was written; the version sequence just
> moved on. No other part of the design changed.
>
> **Two more deviations in the "Plugin contract 0.5.0" section below (task 10, directed in the
> implementation dispatch, not discovered during it):**
> 1. The section's proto sketch (`rpc OnMemoryPressure(AccountMemorySnapshot) returns
>    (Empty)`) was a plugin-side push callback, like `Plugin.OnUIInteraction`. Shipped
>    instead as `rpc SubscribeMemoryPressure(SubscriptionRequest) returns (stream
>    AccountMemorySnapshot)` on the `RoRoRoHost` service — the same server-streaming shape as
>    `SubscribeAccountLaunched` / `SubscribeAccountExited` / `SubscribeMutexStateChanged`.
>    Consistency with the three existing subscriptions won over the sketch.
>
>    *(Amended after review: the dispatch originally specified `Empty` as the request type.
>    The review pointed out that the other three subscriptions all take `SubscriptionRequest`,
>    and that the rpc was brand new in the same commit — so nobody had built against it and the
>    swap was wire-identical and free right then, whereas adding a filter later would have been
>    a breaking signature change. Changed before the version was published.)*
> 2. `IPluginEventBus.MemoryPressure` carries `ROROROblox.Core.Diagnostics.AccountMemory`
>    (already proto-free), not a proto `AccountMemorySnapshot` or a new bespoke bus record.
>    `RunningAccountSnapshot` exists specifically to decouple the bus from the proto
>    runtime; `AccountMemory` already has no proto dependency, so wrapping it again would
>    just be a second name for the same shape. `PluginHostService.SubscribeMemoryPressure`
>    maps it onto the wire message the same way the other three subscriptions do.
>
> The proto message shape itself (`account_id` / `private_bytes` / `growth_mb_per_hr` /
> `mins_to_ceiling` / `over_cap` / `is_target`) shipped exactly as sketched, plus one
> additive field the sketch didn't have: `bool read_ok = 7` — see the proto's own comment
> for why a stale reading needed an explicit flag rather than silently reusing
> `private_bytes`.

**Date:** 2026-08-01
**Status:** Approved design, not yet planned or implemented.
**Driver:** [`docs/investigations/2026-08-01-long-session-window-death.md`](../../investigations/2026-08-01-long-session-window-death.md)
**Dashboard decision:** `n5NxsdLo7CGf8u4izC3K` (project RORORO)

## Why this exists

RobloxPlayerBeta leaks roughly **307 MB/hour/client** while idle. Field telemetry from a clan user:
one account went 1.7 GB → 6 GB in 14 hours *sitting still in an event*. Linear against wall-clock
time, not workload-driven.

Multi-instance multiplies it. Time to RAM exhaustion is
`t = (usable_GB / N − baseline) / 0.307`, which puts 3 clients on a 32 GB box at ~23 hours and
6 clients on 64 GB at ~26 hours — dead center in the 20-30 hour window users reported as
"my account windows shut down on their own."

**The leak is Roblox's and we cannot fix it.** Process exit is the only guaranteed reclaim on
Windows. So RoRoRo's job is to make the failure *visible and survivable*: measure the growth,
project the ceiling, warn before the wall, and make recovery one click.

## Scope

**In:** per-client memory sampling, dual-trigger warning (absolute cap + headroom projection),
warning surfaced on the account row + tray icon + tray balloon, one-click per-account Recycle,
plugin contract 0.5.0 exposing memory pressure to plugins.

**Out** — recorded so it is not re-litigated later:

| Excluded | Why |
| --- | --- |
| Automatic recycle with no user action | Will eventually recycle someone mid-event and cost them progress. Revisit as opt-in once the warn path has earned trust. |
| Working-set trimming (`EmptyWorkingSet`, `SetProcessWorkingSetSize(h,-1,-1)`) | **Rejected on the merits.** Evicts pages to the pagefile without reducing commit charge — it does not move the quantity that causes the crash. It forces hard faults back in (stutter storm on an already-pressured box), and it would corrupt this very watchdog's readings if we sampled working set. See "Metric" below. |
| Pagefile modification | Requires elevation, invasive, and only converts a crash into thrashing. Reporting pagefile state in System Health is fine; changing it for the user is not. |
| Graphics/texture baseline reduction | Real and worth doing — pushes the whole curve right for every client at once. Needs its own spec written on **measured** numbers, and this watchdog is the instrument that makes measuring possible. Sequenced after. |
| Historical charts / trend graphs | YAGNI. The projection is the useful number; a chart is decoration. |

## Metric: private bytes, not working set

**Sample `Process.PrivateMemorySize64`. Never `WorkingSet64`.**

Two independent reasons, either sufficient:

1. **Windows trims working sets of minimized windows automatically and aggressively.** In a
   multi-instance rig most alts sit minimized for the entire session. `WorkingSet64` would collapse
   on minimize and the projection would go blind on exactly the accounts most at risk.
2. Working set is reducible by anything (Windows, us, a third-party "optimizer") without the leaked
   memory going anywhere. Private commit tracks the actual allocation.

Note for interpretation: the field telemetry above was almost certainly a Task Manager working-set
reading, so 307 MB/hr may **understate** true commit growth. Direction and order of magnitude hold;
treat the rate as a conservative floor.

## Architecture

New components in `ROROROblox.Core/Diagnostics/`. `MemoryWatchdog` deliberately mirrors
`ActivityMonitor` — same ctor-injection shape, same `Interlocked`-guarded timer, same public
`Sample()` test seam, same latch/re-arm edge semantics. A reader who understands one understands
the other.

| Component | Responsibility | Depends on |
| --- | --- | --- |
| `IProcessMemoryProbe` | Private bytes for a pid. Prod impl reads `Process.PrivateMemorySize64`. | — |
| `ISystemMemoryProbe` | Machine-wide **available and total** physical RAM. Prod impl calls `GlobalMemoryStatusEx` (add to `NativeMethods.txt` for CsWin32). Total is needed to derive defaults — see Settings. | — |
| `MemoryWatchdog` | Sampling, growth estimation, dual-trigger evaluation, latching, target selection. | both probes, `IClock` |
| `AccountMemory` | Per-account record: private bytes, growth rate, projected minutes, over-cap flag. | — |
| `MemoryPressureSnapshot` | Aggregate: available bytes, total growth, projected minutes, target account. | — |
| `IMemoryWatchdog` | Interface for DI + `MainViewModel` consumption. | — |

`MemoryWatchdog` holds a `ConcurrentDictionary<Guid, Record>` where `Record` carries
`BaselineBytes`, `BaselineAt`, `LastBytes`, `LastReadOk`, `CapLatched`, `ProjectionLatched`.

**Sampling interval: 30 seconds.** The leak is ~5 MB/minute; 30s already oversamples, and it matches
`MainViewModel`'s existing ticker cadence.

## Growth estimation

`growthBytesPerHour = (currentBytes − baselineBytes) / elapsedHours`

A rolling or least-squares fit buys nothing — the observed leak is linear. Two guards:

- **Minimum observation window: 10 minutes.** Below it, emit no projection for that client. A
  30-second slope yields a confident, wrong "18 minutes to ceiling."
- **Baseline ratchet.** If `currentBytes < baselineBytes` (teleport, game change, Roblox freeing a
  level), reset `BaselineBytes = currentBytes`, `BaselineAt = now`, and restart the observation
  window. Without this a single post-teleport drop poisons the slope for the rest of the session.

## Triggers

Both are evaluated every tick; whichever crosses first fires.

**1. Absolute cap.** Any single client with `privateBytes > MemoryCapMb × 1024 × 1024`.
Catches an abnormally fast individual client without waiting for collective pressure.

**2. Headroom projection.** All arithmetic in **bytes and bytes-per-hour**; the `*Mb` settings are
converted at the boundary, never mixed into the formula raw.

```text
reserveBytes        = MemoryReserveMb × 1024 × 1024
availableForClients = max(0, availPhysBytes − reserveBytes)
aggregateGrowth     = Σ growthBytesPerHour over clients with a valid reading
                      and a met observation window            // bytes/hour
minutesToCeiling    = (availableForClients / aggregateGrowth) × 60
fire when minutesToCeiling < ProjectionWarnMinutes
```

`availableForClients` clamps at zero so an already-exhausted machine yields `minutesToCeiling == 0`
(fire immediately) rather than a negative projection.

Aggregate, not per-client: the machine dies from the sum. The reserve means we fire before the box
is actually on the floor rather than at the moment of death.

**Target selection.** When either trigger fires, the **client with the highest private bytes** is
named as the recycle target. The projection describes the machine; the user needs to know which
account to act on.

**Latching.** Each trigger latches per-account on crossing and re-arms when the condition clears,
exactly as `ActivityMonitor` does with `WarnLatched`. One warning per crossing, not one per tick.

## Failure modes

| Case | Required behavior |
| --- | --- |
| `aggregateGrowth <= 0` | Emit no projection. Never divide by zero. A flat or shrinking set cannot exhaust anything. The cap trigger still applies. |
| Client observed < 10 min | No projection contribution from that client; it is excluded from `aggregateGrowth` until the window is met. |
| Client shrank since baseline | Ratchet baseline down, restart window (see above). |
| Pid unreadable — access denied, exited mid-tick | **Exclude from the aggregate. Never substitute zero.** A zero understates total growth and *delays* the warning, which is the dangerous direction. Mark `LastReadOk = false`, log at Debug, keep the record for the next tick. |
| Negative elapsed (clock skew) | Clamp to zero, mirroring `ActivityMonitor.GetSnapshot`. |
| Account exits | Drop the record. |
| Recycle completes | New pid → fresh baseline, observation window restarts, both latches cleared. |
| `availPhys` read fails | Skip projection evaluation this tick; cap trigger still runs. Do not guess a value. |

The unreadable-pid row is the same class of defect as the open `RobloxRunningProbe` `hasWindow` bug
(see investigation doc): an unknown value must never be allowed to impersonate a benign one. Both
should be written with that discipline.

## User-facing surfaces

**Account row chip.** Inline, always present once a valid reading exists: `6.2 GB`. On warning it
gains state and the projection: `▲ 6.2 GB · ~90 min`.

**Tray icon.** A distinct warning state. **Do not fold this into
`ITrayService.UpdateStatus(MultiInstanceState)`** — that enum answers "is multi-instance working,"
an unrelated axis. Overloading it would let a memory warning erase the ON/ERROR state the user needs
during an actual mutex problem. Add a separate method and a separate icon overlay.

**Tray balloon.** Fires once per latched crossing. These sessions run 20+ hours unattended; a
warning that only lives in the app window is a warning nobody sees. Clicking it focuses RoRoRo with
the target account's row selected.

**Recycle.** A per-account action on the warned row: stop that account's process, relaunch through
`IRobloxLauncher` to the **same `LaunchTarget`** (game or saved private server), reset baseline.
Relaunch raises `AccountLaunched` on the plugin bus as normal, so UrTask's existing spawn→spot macro
path picks it up with no new wiring — that is the automated-reconnect loop, assembled from parts
that already exist.

## Settings

**Defaults are derived from total physical RAM, not shipped as absolutes.** We do not know our
users' hardware — the clan is non-technical Windows users on machines ranging from 16 GB to 64 GB,
and no fixed number is correct across that range. A 2 GB reserve is 12.5% of a 16 GB box and 3% of a
64 GB one; an 8 GB per-client cap is unreachable on a 16 GB machine (the box dies long first) and
unremarkable on 64 GB. A fixed default would be wrong for most people, silently.

Added to `AppSettings` / `IAppSettings`. Each is user-overridable; the derivation is only the
default when unset.

| Key | Default | Meaning |
| --- | --- | --- |
| `MemoryWatchdogEnabled` | `true` | Master switch. Off = no sampling, no timer. |
| `MemoryReserveMb` | `clamp(8% of totalPhys, 1024, 4096)` | Headroom withheld from `availPhys` so we warn before the floor. 16 GB → ~1.3 GB, 32 GB → ~2.6 GB, 64 GB → 4 GB (clamped). |
| `MemoryCapMb` | `max(35% of totalPhys, 4096)` | Per-client runaway cap — "no single client should own a third of the machine." `0` disables the cap trigger. |
| `ProjectionWarnMinutes` | `120` | Fire when projected time-to-ceiling drops below this. The only genuinely rig-independent default: two hours of notice is two hours of notice on any machine. |

Derivation runs once at startup against `ISystemMemoryProbe.TotalPhysicalBytes` and is recomputed if
the setting is reset to unset. It is not re-derived per tick — a user override must stick.

## Rig visibility

`DiagnosticsSnapshot` currently reports OS, .NET, Roblox version, WebView2, account count, and live
process count — **but not RAM.** Add:

- `TotalPhysicalMemoryBytes`
- `AvailablePhysicalMemoryBytes` (at capture time)
- Per-account private bytes and growth rate for live clients

This matters beyond cosmetics. Users already paste System Health for support, so the next report of
"my windows closed on their own" arrives with the rig and the memory curve **already attached**,
instead of costing a day of investigation to establish what a single number would have told us.
Self-reporting also beats asking: non-technical users misstate their own specs routinely.

## Plugin contract 0.5.0

Current contract is 0.4.0. This is a minor bump — additive only, no breaking changes to existing
messages or rpcs.

```proto
message AccountMemorySnapshot {
  string account_id       = 1;
  uint64 private_bytes    = 2;
  double growth_mb_per_hr = 3;
  uint32 mins_to_ceiling  = 4;   // 0 = no valid projection
  bool   over_cap         = 5;
  bool   is_target        = 6;   // fattest client at fire time
}

rpc OnMemoryPressure(AccountMemorySnapshot) returns (Empty);
```

- `IPluginEventBus` gains `event Action<AccountMemorySnapshot>? MemoryPressure` and
  `RaiseMemoryPressure(...)`, alongside the existing `AccountLaunched` / `AccountExited` /
  `MutexStateChanged`.
- **A new `PluginCapability` gates it, enforced through `ConsentStore` like every other capability.**
  Per-process memory telemetry is not cookie-grade, but it is still system data about the user's
  machine, and the consent model does not get a silent exception carved into it.
- `docs/plugins/AUTHOR_GUIDE.md` gains a recipe: subscribe to pressure → recycle → macro back.

## Logging

**The point of this section: a user's log file should contain the memory curve.** The reason the
2026-08-01 investigation cost a morning is that we had no curve — we had a symptom and had to derive
the mechanism from arithmetic and a Discord message. Every future report of this shape should arrive
with the evidence already in the file.

| Event | Level | Cadence | Content |
| --- | --- | --- | --- |
| Periodic summary | Information | **every 15 min** | One line, all live clients: private bytes, growth MB/hr, projected minutes, available RAM. ~96 lines/day, ~20 KB. Reconstructs the full curve retroactively. |
| Cap crossed | Warning | once per latched crossing | Account, private bytes, the cap it crossed. |
| Projection crossed | Warning | once per latched crossing | Projected minutes, aggregate growth, available RAM, target account. |
| Recycle invoked | Information | per action | Account, pre-recycle private bytes, the `LaunchTarget` being restored, whether it was user-clicked or (future) automatic. |
| Recycle completed / failed | Information / Warning | per action | New pid on success; the failure reason on failure. |
| Unreadable pid | Debug | per occurrence | Pid + account, so an access-denied pattern is visible rather than silent. |

**Do not log every 30s tick.** `AppLogging`'s own comment records that HttpClientFactory writing
every 10s consumed ~90% of a 15 MB day; a per-tick memory line per client would repeat that mistake.
The 15-minute summary carries the same information at 1/30th the volume.

## Log versioning — required fix, not optional

The app version currently reaches the log on **exactly one line**: the startup banner at
`App.xaml.cs:58` (which reads `typeof(App).Assembly` and is correct — the historical "stuck at an
early version" bug was System Health reading the unversioned Core assembly, fixed in `4a19cd5`).

That is not sufficient here. The sink rolls daily and at 25 MB, and a 20-30 hour session spans a day
boundary by definition — so the file that covers the failure is a **rolled file containing no
startup banner and therefore no version at all.** Support receives an unattributable log.

Two changes to `AppLogging.Configure()`:

1. Take the app version as a parameter, and `.Enrich.WithProperty("Version", version)`.
2. **Add `{Version}` to the `outputTemplate`.** Enrichment alone is invisible to the file sink —
   note that the existing `.Enrich.WithProperty("App", "ROROROblox")` is not in the template and
   consequently never appears in the log today. Every line must carry the version so any fragment of
   any rolled file is self-attributing.

This also disambiguates the two-binaries case: a Store install and a dev build can both be present
on one machine, and a versionless log makes it guesswork which one produced it.

**Related retention caveat, worth verifying during implementation:** `retainedFileCountLimit: 14`
counts **files, not days**. With `rollOnFileSizeLimit: true`, a heavy day can consume several files,
so the real retention window may be well under 14 days — exactly when we are asking users for logs
after a multi-day session. Confirm the effective window and raise the count if it is short.

## Testing

xUnit, every probe injected. **No test touches a real process or reads real system memory.**

- Growth math over a known elapsed span.
- Sub-10-minute window suppresses the projection.
- Cap trigger: fires, latches, re-arms on clearing.
- Projection trigger: fires, latches, re-arms on clearing.
- `aggregateGrowth == 0` and `< 0` produce no projection and no divide-by-zero.
- Target selection picks the highest private-bytes client.
- **Unreadable pid is excluded from the aggregate — asserted explicitly as "not treated as zero."**
- Baseline ratchets on shrink and restarts the window.
- Account exit drops the record.
- Recycle resets baseline and clears both latches.
- `availPhys` read failure skips projection but still evaluates the cap.
- **Derived defaults:** reserve clamps at both ends (16 GB → 1024 floor not hit, 8 GB → floor hit,
  128 GB → 4096 ceiling hit); cap floors at 4096 on small machines; **an explicit user override is
  never overwritten by re-derivation.**
- **Logging cadence:** the 15-minute summary emits on schedule and not per 30s tick; threshold
  warnings emit once per latched crossing and not repeatedly while the condition holds.
- **Log versioning:** `AppLogging.Configure(version)` puts the version in the rendered output, not
  merely in enrichment — assert against rendered text, since the existing `App` property proves
  enrichment alone never reaches the file.

## Open items for the implementation plan

- Tray warning icon artwork goes through the `626labs-design` skill, per the repo's no-programmatic-
  placeholders rule. Needed sizes match the existing `tray-on` / `tray-off` / `tray-error` set.
- Win32 surface is **`GlobalMemoryStatusEx`**, field **`ullAvailPhys`**, added to
  `NativeMethods.txt`. `GetPerformanceInfo` was considered and rejected: it reports commit limit and
  page-size detail this design does not use, for a wider P/Invoke surface. If a future revision needs
  commit-limit awareness rather than physical headroom, that is the time to revisit — not now.
