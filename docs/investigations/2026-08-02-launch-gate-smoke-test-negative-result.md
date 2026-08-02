# Launch gate smoke test — negative result, and a version-folder targeting bug

> **Revision note (same day).** The first version of this document blamed a Roblox client
> update shipped this morning. That was wrong — the evidence showed a version *already
> installed the day before* becoming the active launch target. Cause B is rewritten below;
> the withdrawn claim is preserved in the git history of this file, not silently deleted.
> Credit to Este for rejecting the conclusion on the grounds that no update had visibly
> fired. The lesson is the standing one: a clean before/after correlation in a log is not
> a cause, and "a version string changed" is not the same fact as "a new version shipped."

**Date:** 2026-08-02
**Build under test:** `fix/launch-gate-condition-based` @ `31af029`, Debug, `Core.dll` built 14:40:40 (verified to contain `WaitForNewClientAsync`, `NewClientWaitOutcome`, `SnapshotBeforePids`, `HoldForNewClientAsync`)
**Related:** PR #70, `docs/superpowers/specs/2026-08-01-launch-gate-condition-based-design.md`

## Verdict

**The condition-based launch gate did not fix the wrong-FPS-cap bug.** The 2026-08-01
repro still reproduces on the gate build.

Three findings. **Two of the three are ours**, and the one that is not cannot be dated
from the available evidence.

- **A — the settle grace is too short.** Ours. Confirmed by artifact, not just timing.
- **B — FFlag writes can land in a version folder Roblox is not launching from.** Ours,
  previously invisible, and it masked C for at least four days.
- **C — Roblox denies the local FPS FFlag when it does arrive.** Theirs. Duration unknown.

---

## Cause A — the settle grace is too short (ours)

The gate waits for a new `RobloxPlayerBeta` pid, then holds `SettleGrace` (1 s) before
releasing. That is anchored far better than the old fixed 250 ms hanging off
`Process.Start`, but it still fires **before the client has read its settings.**

Measured on this run:

| Event | este (pid 33608) | CElCPapa (pid 43324) |
|---|---|---|
| Process appears (`Get-Process` StartTime) | 15:41:19 | 15:41:20 |
| Client init t=0 (from its own log) | 15:41:21.000 | 15:41:23.000 |
| Config read (`DFIntTaskSchedulerTargetFps`, t+0.255 s) | 15:41:21.255 | 15:41:23.255 |
| **Process start → config read** | **~2.25 s** | **~3.25 s** |

The gate released este's launch at 15:41:20.166 — roughly 1.0 s after its pid appeared,
exactly as designed. CElCPapa's dispatch began at 15:41:20.545 and its settings write
landed somewhere in 15:41:20.5–21.8. **Este did not read its config until 15:41:21.255**,
so the overwrite arrived first.

The gap between "pid exists" and "client has read its settings" measured **2.25 s and
3.25 s** on a warm machine. `SettleGrace` is 1 s. The window the gate was built to close
is still open by 1–2 seconds.

Note the shape of the error: the spec correctly identified that `Process.Start` returning
is not the event we care about. We then replaced it with *pid appears + a fixed 1 s*,
which is a better proxy anchored to a real event — but still a fixed delay against an
unbounded, machine-dependent interval. Same class of mistake, one step further along.

`Get-Process` StartTime has ~1 s granularity, so treat 2.25 / 3.25 as ±1 s. The direction
is unambiguous: the read happens seconds after the pid, not within one.

## Cause A — direct artifact evidence

The race is not inferred from timing alone. The file itself proves it.

`version-18b8f177aa0f4699\ClientSettings\ClientAppSettings.json`, last written
**15:41:20** — CElCPapa's launch — contained exactly:

```json
{
  "DFIntTaskSchedulerTargetFps": 20
}
```

Este's client read that file at **15:41:21.255**, 1.1 s later. It read CElCPapa's value.
Este is configured "unlimited," which per `ClientAppSettingsWriter`'s contract should emit
`FFlagTaskSchedulerLimitTargetFpsTo2402` above 240 — that key is absent entirely. Este's
write was clobbered wholesale before its own client ever opened the file.

## Cause B — CORRECTED: no Roblox update fired; our writes were landing in a dead folder

**An earlier revision of this document claimed Roblox shipped a client update at ~10:28
today that locked `DFIntTaskSchedulerTargetFps` against local override. That claim is not
supported by the evidence and is withdrawn.**

What the evidence actually shows:

| Version folder | Product version | Installed | Has `ClientAppSettings.json`? |
|---|---|---|---|
| `version-145f189a6a974303` | 0.732.23.7321040 | 7/29 08:42 | **no** |
| `version-18b8f177aa0f4699` | 0.732.598.7321379 | **8/1 07:55** | **yes (ours)** |

`0.732.598` was installed **the day before**, not this morning. The 10:28 installer run
downloaded nothing — `File already exists for dc589dfc… Skipping download` — and was a
**relaunch/re-register**: `-relaunch version-18b8f177aa0f4699 -background -channel
zrbxtelemetry-rearch-flag-removal`, ending with
`Set the launcher for Roblox as: …\version-18b8f177aa0f4699\RobloxPlayerBeta.exe`.

So what changed at 10:28 was **which version folder Roblox launches from**, not the client
build and not Roblox's flag policy.

That reframes the denial completely. `ClientAppSettingsWriter.NewestActiveVersionFolder`
picks its target by newest `RobloxPlayerBeta.exe` write time, which resolved to
`0.732.598` from 8/1 onward. But until 10:28 Roblox was still *launching* `0.732.23` —
a folder with no `ClientAppSettings.json` at all.

**Before 10:28 our FFlag writes were landing in a folder the running client never read.**
No local config existed for the running version, so nothing was denied, so nothing was
logged. The denial did not start this morning; **our writes only started reaching a
running client this morning.** How long Roblox has denied this flag is not determinable
from these logs, and this document makes no claim about it.

The user's read was correct: this is our side.

### The real our-side bug: version-folder targeting can miss

`NewestActiveVersionFolder` resolves by file mtime. Roblox resolves by whatever its
installer last registered. Those diverged for roughly four days (8/1 07:55 → 8/2 10:28),
during which every FFlag write RoRoRo made was silently discarded into an inactive folder
with no error, no log line, and no user-visible symptom.

That is worth its own fix independent of the launch gate: read the registered launcher path
(the installer writes it, and `UpdateController` lines in the client log confirm it) rather
than guessing from mtime.

## Cause C — the FFlag is denied when it does arrive (theirs, timing unknown)

Both clients logged:

```
"DFIntTaskSchedulerTargetFps": 20
Warning [FLog::FlagFetchingStarterModule] Denied local configuration for: DFIntTaskSchedulerTargetFps
```

Roblox refuses the local `ClientAppSettings.json` override for this flag. It is the only
denial in the log — no other key RoRoRo writes is rejected.

Every client run on this machine today:

| Time | Client version | Local file present for that version? | Denial? |
|---|---|---|---|
| 08:26 | `0.732.23.7321040` | no | no |
| 10:14 | `0.732.23.7321040` | no | no |
| 10:20 | `0.732.23.7321040` | no | no |
| 10:25 | `0.732.23.7321040` | no | no |
| **10:28** | **installer re-registers 0.732.598 as the launch target** | — | — |
| 10:28 | `0.732.598.7321379` | yes | **denied** |
| 10:30 | `0.732.598.7321379` | yes | **denied** |
| 15:41 | `0.732.598.7321379` | yes | **denied** |
| 15:41 | `0.732.598.7321379` | yes | **denied** |
| 15:42 | `0.732.598.7321379` | yes | **denied** |

The correlation is real but the middle column is the explanation, not the version column.
Every "no denial" row is a run where **no local flag file existed for the version being
launched** — so there was nothing to accept or deny. The moment our writes and Roblox's
launch target aligned, the denial appeared on every single run.

**This document therefore makes no claim about when Roblox started denying this flag.**
It may be brand new; it may be months old. We could not have observed it before today
because our flag was never reaching a running client. Establishing the duration would
need a deliberate test — drop a hand-written `ClientAppSettings.json` into an older
version folder and launch it directly — which has not been run.

**What still works:** `GlobalBasicSettings_<N>.xml`'s `<int name="FramerateCap">` — the
lever the 2026-05 investigation already identified as the one that actually governs
default-config users. On-disk after this run: `FramerateCap=20`, CElCPapa's value, written
last. That is consistent with both clients showing 20.

**Not yet measured:** *when* the client reads `GlobalBasicSettings`. The 0.255 s figure
above is the `ClientAppSettings` read. The `GlobalBasicSettings` read time is unknown and
could be earlier or later. This matters, because it — not the FFlag read — is the interval
the settle grace actually needs to cover.

---

## What this means for PR #70

The branch is **not wrong**, and its safety properties all held: no hang, no stall, correct
anchoring, correct multi-launch sequencing, clean mutex acquisition, automatic stray
cleanup. The gate did exactly what it was specified to do. The specification was
insufficient.

Options, in rough order of preference:

1. **Measure the real read, then anchor to it.** Instrument when a client opens
   `GlobalBasicSettings_<N>.xml` (Sysinternals Process Monitor, or a handle poll). Once we
   know the true event, decide whether to anchor on it or on a milestone that provably
   follows it.
2. **Anchor on a later observable milestone.** The client's `MainWindowHandle` becoming
   non-zero is a real, already-probed event that is definitively after early init. More
   honest than any fixed number, at the cost of a longer hold.
3. **Raise `SettleGrace` on the measured data** (≥ 3–4 s). Cheapest, keeps the current
   shape, and is still a guess — it would need to hold on cold-start and slow machines,
   where the interval is longer than anything measured here.
4. **Stop sharing the file during the window.** Structurally correct, largest change.

Merging PR #70 as-is is defensible — it is a strict improvement on the shipped behaviour
and its follow-ups are filed. But it must not be described in release notes as fixing the
wrong-FPS-cap bug, because it does not.

Fix B first, and independently. It is a plain bug with a plain fix, it costs nothing to
correct, and while it stands, every future FFlag measurement on any machine is suspect —
including any attempt to date finding C.

## Open questions

- When does the client read `GlobalBasicSettings_<N>.xml` relative to process start? This
  is the number the settle grace actually has to cover, and it is still unmeasured.
- How long has `DFIntTaskSchedulerTargetFps` been denied? Not determinable from these logs.
- Does `GlobalBasicSettings`' `FramerateCap` remain honoured, or is it being tightened too?
  It is doing all the real work today, so this is the load-bearing question.
- How often does the mtime-vs-registered-launcher divergence happen in the wild? Four days
  on one machine is one sample.

## Log this to the dashboard

Two entries, and note that the first supersedes anything logged earlier today about a
Roblox client update — **no such update occurred.**

1. **Gotcha discovered (ours).** `ClientAppSettingsWriter` targets the version folder with
   the newest `RobloxPlayerBeta.exe` mtime; Roblox launches whatever its installer last
   registered. These diverged 8/1 07:55 → 8/2 10:28, and every FFlag write in that window
   was silently discarded into an inactive folder — no error, no log, no symptom. Fix by
   reading the registered launcher path instead of guessing from mtime.
2. **Roblox-side compatibility event (theirs, undated).** Client `0.732.598.7321379`
   denies local configuration of `DFIntTaskSchedulerTargetFps`
   (`FLog::FlagFetchingStarterModule`). The FFlag path for the FPS cap does not work on
   this version. `GlobalBasicSettings_<N>.xml`'s `FramerateCap` remains the working lever,
   consistent with the 2026-05 finding. **We cannot say when this started** — finding 1
   hid it from us until today.

---

# Addendum — measured 2026-08-02 16:12–16:22

Four measurement runs on the live app. These change the diagnosis and point at a fix.

## Finding D — the first new pid is frequently not the client

`WaitForNewClientAsync` takes the **first** new `RobloxPlayerBeta` pid the probe returns
and treats it as "our client arrived." Observed across runs, that pid often is not.

| Run | First new pid | Real client | Gap |
|---|---|---|---|
| 16:12 | 60464 — exited, never got a window | 5692 (2462 MB, window) | 0.023 s |
| 16:20 | 15932 — 26 MB, no window, survived | 50828 (2511 MB, window) | **5.92 s** |

In the 16:20 run the gate would have latched pid 15932 at +0 s, held its 1 s settle, and
released **roughly five seconds before the real client process even started.**

The 26 MB windowless signature also appears when a client is *closed* — pids 62912, 38584,
58388 and 15932 all match it. So the probe can hand the gate a pid produced by the user
closing a different window, entirely unrelated to the launch in flight.

No unit test could have caught this: every test probe in the suite returns exactly one
well-behaved pid that appears once and stays. The author of the measurement script (me)
made the identical mistake twice before noticing.

## Finding E — the settle grace is off by 2–3x, confirmed three times

Process start → client reads its config, from the client's own logs:

| Run | Interval |
|---|---|
| 15:41 este | 2.25 s |
| 15:41 CElCPapa | 3.25 s |
| 16:17 este | 1.98 s |

`SettleGrace` is 1 s.

## Finding F — the client rewrites the shared file for ~12 s after launch

This is the one that reframes the bug. Watching
`%LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml` across a single launch:

```
change #1  16:20:58.362  (+0 s)      <- ours, pre-Process.Start
change #2  16:21:00.052  (+1.69 s)   <- client
change #3  16:21:03.879  (+5.52 s)   <- client
change #4  16:21:06.944  (+8.58 s)   <- client
change #5  16:21:10.423  (+12.06 s)  <- client
(then silent for the remaining 48 s of the observation window)
```

**A starting client does not read this file once — it re-persists its own value
repeatedly for about twelve seconds.** So the race runs in both directions:

- our write for account B lands before A's client reads → A gets B's cap (the original bug), and
- our write for account B lands during A's writeback window → **A's client overwrites it,
  and B reads A's cap.**

It also explains the 15:41 symptom exactly. Este read 20 before it could keep 9999, then
faithfully wrote 20 back. Everything converged on 20.

**The writes stop.** Twelve seconds of contention, then quiet. That quiet is the fix.

## The fix this points to

Stop inferring the safe moment from pids and fixed delays. **Anchor on the file itself
going quiet.** After a launch, watch `GlobalBasicSettings_<N>.xml` and wait until it has
been unmodified for a short debounce (~2 s). That single condition covers the client's
read *and* its writeback storm, needs no pid heuristics, and self-tunes to machine speed —
a slow cold start simply stays noisy longer.

Pair it with the free correctness win: **the race only exists when consecutive launches
want different caps.** If every account shares a value, the file already holds it and
there is nothing to protect — launch at full speed. Most users set one cap, so most users
pay nothing, and the ~12 s per-launch cost lands only on the configuration that actually
needs it.

Sequence per launch, when caps differ: write cap → launch → wait for the file to go quiet
→ next launch.

### What still needs proving

The debounce design is sound on one run's data. Before building it:

- Reproduce the quiet window with **differing** caps, which is the case that matters and
  the case no run so far has exercised (every run used matching values, so no write could
  be distinguished from another by content).
- Confirm 12 s is typical rather than lucky — cold start, a busy machine, and a
  three-account sequence.
- Confirm a fully-started client stays quiet, so the debounce cannot be defeated by an
  older client writing during a later launch.

If the quiet window turns out not to exist under load, the honest answer is a single
global cap rather than per-account, and the feature gets retired rather than patched.
