# RORORO — "Start anyway" escape hatch on the BLOCKED startup gate

---
**Date:** 2026-07-09
**Status:** Approved-shape (Este: "write the fix") — mini-spec, ready for implementation
**Author:** The Architect + Este
**Scope:** Restore a "Start anyway" path on the BLOCKED startup modal so a user can proceed when the singleton mutex is already held by a benign squatter (another RoRoRo instance, or a compatible tool) instead of being trapped with only mutex-reclaiming actions that cannot succeed.
**Origin:** Live-observed 2026-07-09 while smoking a dev build alongside the installed app. The dev build came up as a second RoRoRo instance; the installed app held `Local\ROBLOX_singletonEvent`; the acquire-first gate hard-blocked with only Close-Roblox-for-me / Retry — neither can free a mutex held by *RoRoRo*, so there was no way forward. Este: "the continue has worked previously and def works."
**Builds on:** the tray-residence gate ([`2026-07-02-rororo-tray-residence-gate-design.md`](2026-07-02-rororo-tray-residence-gate-design.md), the #32 cycle that introduced acquire-first + `StartupGateResult.Blocked`).
---

## 1. The regression

The #32 tray-residence redesign made startup **acquire-first**: `mutex.Acquire()` → `StartupGate.Evaluate(acquired)` → `Blocked` when acquisition fails. The BLOCKED modal ([`RobloxAlreadyRunningWindow`](../../../src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml)) offers **Close Roblox for me** and **Retry**, both of which re-`Acquire()` the mutex. That is correct when the holder is a tray-resident **Roblox** (kill it → acquire → proceed). It is a **trap** when the holder is another **RoRoRo** (or any benign squatter): Close-for-me kills Roblox clients but the mutex stays held by the other RoRoRo, Retry loops on "still locked," and there is no proceed path. The pre-#32 gate had a "continue anyway" that this removed.

## 2. Mutex semantics (why "Start anyway" is squatter-safe but not Roblox-safe)

RoRoRo enables Roblox multi-instance by **squatting** `Local\ROBLOX_singletonEvent` — while *something* holds that mutex continuously, each new Roblox launch fails to become the singleton owner and therefore does not activate-and-exit into a pre-existing Roblox window. The holder's *identity* does not matter to Roblox; only that the mutex stays held.

- **Holder is a benign squatter (another RoRoRo / compatible tool):** multi-instance is **already working** because the squat is in place. This RoRoRo does not need to *own* the mutex to launch accounts — it can proceed without it. **"Start anyway" is correct here.**
- **Holder is Roblox itself (tray-resident, holding its own singleton):** a new launch would see the mutex held **by another Roblox** and collapse into that instance. Starting anyway does **not** give multi-instance; the right action stays **Close Roblox for me**. **"Start anyway" does not help here** — but it does no *new* harm (the user simply won't get multi-instance until the tray Roblox is closed, which is already the status quo of the block).

We cannot perfectly distinguish the two holders from the mutex alone, so "Start anyway" is framed as the informed escape hatch — honest copy, and it drops the user straight into the existing contested-banner state so they can *see* that RoRoRo does not hold the lock.

## 3. Design

1. **Tri-state modal outcome.** `RobloxAlreadyRunningWindow` gains a `BlockedModalOutcome Outcome { get; }` with values `Recovered | StartAnyway | Quit` (default `Quit`). Close-for-me / Retry success → `Recovered`; the new **Start anyway** button → `StartAnyway`; Quit → `Quit`. The existing reacquire-in-place recovery is unchanged.
2. **Pure startup decision.** `BlockedStartupDecision.Resolve(outcome) → (bool Proceed, bool HoldsMutex)`: `Recovered → (true, true)`, `StartAnyway → (true, false)`, `Quit → (false, false)`. Unit-tested; this is the load-bearing invariant.
3. **`App.OnStartup` BLOCKED branch** switches on the resolved decision: `!Proceed →` `Shutdown(0)`; else proceed with `acquired = HoldsMutex`. On the `StartAnyway` path `acquired` stays **false** and we never own the mutex — the app runs "borrowed."
4. **Runtime messaging is free.** `MutexContestedWatcher` already polls `!IsHeld && IsHeldElsewhere()` every 5s and fires `ContestedChanged` → the contested banner. A borrowed start satisfies exactly that predicate, so within one tick the user sees the standard "RoRoRo doesn't hold the lock" banner with its Close-Roblox / Retry actions — no new runtime code.
5. **Tray honesty.** On a borrowed start, set the tray to `MultiInstanceState.Off` (true: we don't hold the lock) rather than `Error` (which means *mutex lost* and is misleading for a deliberate choice). The banner carries the nuance. A dedicated "borrowed/contested" tray state is a possible follow-up polish, not built here.

## 4. Non-goals

- No change to the Close-for-me / Retry recovery, the acquire-first gate, or `StartupGateResult`.
- No attempt to auto-detect holder identity (Roblox vs squatter) — the copy makes the tradeoff explicit instead.
- No new tray icon/state, no rewrite of the contested-banner copy.
- Normal single-install users never reach this modal; this is an escape hatch for the multi-holder case (dev-alongside-installed, or a user running a second tool).

## 5. Testing

- **Unit:** `BlockedStartupDecision.Resolve` — all three outcomes map to the correct `(Proceed, HoldsMutex)`; the default/unknown maps to `(false, false)` (fail-closed to Quit).
- **Manual smoke (gates merge):** with a second mutex holder present (run the installed app, then the dev build), the BLOCKED modal shows **Start anyway**; clicking it starts RoRoRo without the lock; within ~5s the contested banner appears; a Launch As still opens accounts (multi-instance works because the other holder squats the mutex); Quit still exits; Close-for-me / Retry still behave as before.

## 6. What ships

`BlockedModalOutcome` + the Start-anyway button/copy on `RobloxAlreadyRunningWindow`; `BlockedStartupDecision.Resolve` + unit tests; the `App.OnStartup` BLOCKED-branch switch + borrowed-start tray honesty. One small subagent-driven change on `fix/startup-start-anyway`.
