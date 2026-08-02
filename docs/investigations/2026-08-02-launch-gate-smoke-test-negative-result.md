# Launch gate smoke test — negative result, and a Roblox-side FFlag lockout

**Date:** 2026-08-02
**Build under test:** `fix/launch-gate-condition-based` @ `31af029`, Debug, `Core.dll` built 14:40:40 (verified to contain `WaitForNewClientAsync`, `NewClientWaitOutcome`, `SnapshotBeforePids`, `HoldForNewClientAsync`)
**Related:** PR #70, `docs/superpowers/specs/2026-08-01-launch-gate-condition-based-design.md`

## Verdict

**The condition-based launch gate did not fix the wrong-FPS-cap bug.** The 2026-08-01
repro still reproduces on the gate build.

Two independent causes, only one of which is ours.

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

## Cause B — Roblox locked the FPS FFlag against local override (theirs)

Both clients logged:

```
"DFIntTaskSchedulerTargetFps": 20
Warning [FLog::FlagFetchingStarterModule] Denied local configuration for: DFIntTaskSchedulerTargetFps
```

Roblox is now **refusing** the local `ClientAppSettings.json` override for this flag.

Correlated against every client log on this machine today:

| Time | Client version | Denial? |
|---|---|---|
| 08:26 | `0.732.23.7321040` | no |
| 10:14 | `0.732.23.7321040` | no |
| 10:20 | `0.732.23.7321040` | no |
| 10:25 | `0.732.23.7321040` | no |
| **10:28** | **RobloxPlayerInstaller ran** | — |
| 10:28 | `0.732.598.7321379` | **DFIntTaskSchedulerTargetFps** |
| 10:30 | `0.732.598.7321379` | **DFIntTaskSchedulerTargetFps** |
| 15:41 | `0.732.598.7321379` | **DFIntTaskSchedulerTargetFps** |
| 15:41 | `0.732.598.7321379` | **DFIntTaskSchedulerTargetFps** |
| 15:42 | `0.732.598.7321379` | **DFIntTaskSchedulerTargetFps** |

Clean before/after boundary at the **10:28 client update, `0.732.23.7321040` →
`0.732.598.7321379`**. Every run on the old build accepted the flag; every run on the new
build denies it.

This is a Roblox-side contract change, shipped this morning, and it is independent of
anything in PR #70. It is exactly the class of event `roblox-compat.json` and the
decisions log exist to track.

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

## Open questions

- When does the client read `GlobalBasicSettings_<N>.xml` relative to process start?
- Is the FFlag denial permanent policy or a transient server-side flag-fetch state?
- Does the denial affect other flags RoRoRo writes, or only `DFIntTaskSchedulerTargetFps`?
- Does `GlobalBasicSettings` remain honoured on `0.732.598`, or is it also being tightened?

## Log this to the dashboard

Roblox-side compatibility event, per `CLAUDE.md`: client `0.732.23.7321040` →
`0.732.598.7321379` (2026-08-02 ~10:28 local) denies local configuration of
`DFIntTaskSchedulerTargetFps`. The FFlag path for the FPS cap is dead on this client
version; `GlobalBasicSettings` remains the working lever.
