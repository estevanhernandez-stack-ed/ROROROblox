# Memory headroom — scope

**Findings:** F-082 (the warning that cannot fire), F-080 (running total on screen),
F-081 (why a client exited)

**One sentence:** RoRoRo watches memory and says nothing to the one user who needs it —
the person running ten accounts on sixteen gigabytes.

---

## The problem, in numbers we measured

Per-client memory on Roblox 733, this machine, 2026-08-07: **median 2650 MB, peak 3280 MB**.

| installed | usable after Windows | clients that fit | per-client cap today | can it fire? |
|---|---|---|---|---|
| 8 GB | 4.6 GB | 1 | 4096 MB | maybe |
| **16 GB** | **12.9 GB** | **4** | 5734 MB | **no** |
| 32 GB | 28.6 GB | 11 | 11468 MB | **no** |
| 48 GB | 44.6 GB | 17 | 17203 MB | **no** |
| 64 GB | 60.6 GB | 23 | 22937 MB | **no** |

**Ten accounts need ~26.5 GB.** On 16 GB that is oversubscribed by 13.6 GB.

The watchdog has exactly two triggers and neither fires there:

1. **Per-client cap** — 35% of installed RAM, floored at 4 GB (`MemoryDefaults.cs:27`).
   A Roblox client peaks near 3300 MB, so at 16 GB and above it never crosses.
2. **Projection** — needs `aggregateGrowth > 0` (`MemoryWatchdog.cs:193`). Once clients
   plateau, `hasProjection` is false and it cannot fire.

There is **no absolute headroom check**. A user at 100% RAM with steady clients gets nothing.

### We did this in v1.12

`852bc66` replaced a cap that was firing with one that cannot. The v1.11.1 logs from
2026-08-01 show the old fixed cap crossing repeatedly at **2640 MB**; the same machine now
derives **16886 MB**.

The commit's reasoning was right — a fixed default *is* wrong across 16–64 GB. The error is
the axis: **a per-client cap must not scale with installed RAM.** A Roblox client uses what
it uses; having 64 GB does not make one client's 3 GB more acceptable. Risk is aggregate.

---

## What ships

### 1. An absolute headroom trigger (F-082, the core)

Warn when **available memory falls below the reserve**, independent of growth. This is the
trigger that catches a plateaued, oversubscribed machine — the exact case that is silent now.

Reuses the existing latch + deadband machinery (`CapReArmFactor` and friends), because the
old fixed cap's real failure was noise: the Aug 1 logs show one account crossing four times
in eight minutes before the deadband work landed. **A trigger that cries wolf gets muted, and
a muted warning is worse than none.**

### 2. Re-base the per-client cap on a footprint, not a fraction

The cap's honest job is anomaly detection — *this one client is abnormally large* — not
system-pressure proxy. So it becomes an absolute figure derived from what a Roblox client
actually costs (measured: 2650 median / 3280 peak), not a percentage of the user's RAM.

**Invariant:** `MemoryCapMb` / `MemoryReserveMb` are user-settable and a non-null stored value
is a deliberate override that must never be silently re-derived
(`IAppSettings.GetMemoryCapMbAsync` says so explicitly). Only the *derived default* changes.

### 3. Aggregate becomes the primary signal

Report and reason about `sum(client private bytes)` against usable memory. Per-client stays
as the anomaly axis. This is the reframe the whole finding rests on.

### 4. The running total on screen (F-080)

Footer becomes `6 Roblox clients running · 16.2 GB` — `MainViewModel.cs:765-770`, between the
count and Compact, per Este's request. It is also the natural place for the warning state to
show, since it is already the line the user glances at.

Getting this number today required a PowerShell probe against the process list. The watchdog
has sampled it every 15 seconds all along.

### 5. A pre-launch check — the piece that actually prevents the problem

Before launching client N+1, compare expected footprint against `available - reserve`. If it
does not fit, say so **before** the launch rather than narrating the aftermath.

**Warn, do not block.** It is their machine and their call, and a hard block on a wrong
estimate is worse than a soft warning on a right one. (Contrast the mutex modal, which *does*
hard-block — there the failure is deterministic and recoverable only one way. This is a
forecast, and forecasts should not veto.)

### 6. Why a client exited (F-081)

`RobloxProcessTracker.cs:385` logs pid + account. Add exit code, last private-bytes reading,
and session duration — plus a distinct line when several clients exit inside a few seconds.

This is not bookkeeping. On 2026-08-07 the gap produced a **wrong** conclusion, not just a
slow one: three exits were read as Roblox's updater killing clients when Este had closed them
himself, and nothing in our logs could tell the two apart.

---

## Testing

Table-driven across the real matrix — RAM tiers {8, 16, 32, 48, 64} GB × client counts
{1, 4, 6, 8, 10} — using the measured 2650/3280 footprint. **This encodes today's arithmetic
as tests**, so the 16 GB × 10 case fails loudly forever if anyone re-derives the cap from a
percentage again.

Specifically:
- 16 GB × 10 clients **must** warn. It is the case that reported this and the case that is
  silent today.
- 48 GB × 8 clients (this machine, 2026-08-07) **must not** warn. Real, measured, healthy.
- A user-set `MemoryCapMb` survives every default change.
- The headroom trigger latches once and re-arms only past the deadband — asserted against the
  four-crossings-in-eight-minutes shape from the Aug 1 logs.

Seven memory test files already exist (`MemoryDefaultsTests`, `MemoryPressureEvaluatorTests`,
`MemoryWatchdogTriggerTests`, …). New triggers join them; no new harness.

---

## Explicitly not in scope

- **The drop-outs themselves.** Still unexplained. This wave makes them *visible and
  predictable*, not fixed. Nothing here claims to stop a client dying.
- **Captcha on reconnect.** Real, reproduced 2026-08-07, chronic in this repo
  (AppStorageDefender, v1.4.2.0). Este's call to leave it — chased before without resolution.
- **Auto-recycling under pressure.** The watchdog warns; it has never acted on its own, and
  this wave does not change that. Closing somebody's client for them needs its own decision.

---

## Risks

| risk | mitigation |
|---|---|
| False alarms mute the feature | Reuse the existing latch + deadband; test against the historical four-crossings shape |
| Estimating a client's footprint wrong | Derive from measured data, and warn rather than block so a bad estimate costs a dismissed banner |
| Changing defaults surprises existing users | Only the *derived* default moves; explicit user values are untouched, and that is a test |
| One machine, one game | 2650/3280 comes from 47 GB running Pet Sim. Treat as a starting constant, revisit if clan numbers differ |
