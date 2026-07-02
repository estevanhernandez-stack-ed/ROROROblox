# RORORO — tray-residence gate + runtime lock awareness design

---
**Date:** 2026-07-02
**Status:** Approved (brainstorm complete) — ready for implementation plan
**Author:** The Architect + Este
**Scope:** Rework the "Roblox already running" startup gate for Roblox 0.727's tray-residence, and add runtime (post-startup) lock awareness. Ships in the same release as the Limited fix (PR #30) and activity awareness (PR #31).
**Supersedes:** the detection/UX portions of [`2026-05-08-roblox-already-running-detect-design.md`](2026-05-08-roblox-already-running-detect-design.md) — that spec gets a banner-correct pointing here; its non-goals ("no retry, no runtime detection") no longer hold.
**Related:** [`2026-06-30-rororo-limited-followups.md`](2026-06-30-rororo-limited-followups.md) §2 (the 0.727 facts), `docs/reviews/2026-06-12-raw-findings.txt` line ~332 (compose stop-all + retry instead of dead-ending)
---

> ## ⚠️ Banner-correct (2026-07-02, post-build)
>
> One §5 drift from the shipped build: the BLOCKED modal's mono-micro tag `MULTI-INSTANCE NEEDS THE LOCK` was **dropped** — its row was repurposed for the amber still-locked tick (`Still locked — Roblox is still running.`), which carries actual state instead of a static label. Deliberate plan-level trade (Task 6), caught at the whole-branch review. Everything else in §5/§6 shipped char-exact.

## 1. Problem & context

Roblox 0.727 (installed 2026-06-27) **stays resident in the system tray** when its window closes — a windowless `RobloxPlayerBeta.exe` that keeps holding the singleton mutex (`Local\ROBLOX_singletonEvent`, name from remote config). It can also **start itself with Windows**. Some users can disable that; the product has to help the ones who won't.

Today's gate handles this badly in three ways:

1. **Wrong signal.** `StartupGate` blocks on a bare process scan — *any* `RobloxPlayerBeta.exe` trips it, including dead ~180MB windowless orphans (13 processes observed for 6 accounts) that hold nothing. The thing that actually blocks multi-instance is **the mutex being held by someone else**, and the gate never consults it.
2. **Wrong advice.** The modal's only path is a three-step script — close Roblox, close RoRoRo, re-open RoRoRo — plus a lone `Quit RoRoRo` button. In reality, quitting Roblox from the tray is sufficient and RoRoRo could simply retry in place. Nothing about the flow requires restarting RoRoRo.
3. **Runtime blindness.** The gate is one-shot at startup. If Roblox grabs the lock later (multi-instance toggled off, or a `MutexLost`), the only trace is a silent tray "Error" state; the main window surfaces no multi-instance state at all.

## 2. Goals & non-goals

**Goals:**

1. Block startup **only when actually blocked** — the mutex held by someone else — and explain exactly what's happening ("Roblox is running, possibly windowless in your system tray") with a working fix path that never requires restarting RoRoRo.
2. Give leftover-process situations an **informational** treatment (safe to continue + optional cleanup), not a hard block — while being honest that some "leftovers" are live game windows the user may still be playing.
3. One-click recovery: **Close Roblox for me** (existing `RobloxInstanceStopper.StopAll()`) followed by an automatic re-acquire, plus a manual **Retry** for users who quit from the tray themselves.
4. Close the runtime blind spot: detect Roblox holding the lock post-startup and surface it in the **main window** (which currently shows nothing) with the same recovery actions.
5. All decision logic unit-testable behind fakes; Win32 additions stay thin.

**Non-goals (this cycle):**

- No mid-session orphan-cleanup row in the main window (startup cleanup only; a runtime cleanup surface is a later iteration).
- No toast for the contested state — the banner is a passive surface; the startup gate is the loud moment.
- Never touch Roblox's own start-with-Windows setting — we inform, we don't reconfigure Roblox.
- No "mutex-only background mode" (the competitive doc's v2 idea) — out of scope.
- No change to the mutex-name resolution chain (RemoteConfig → LastKnownGood → Default stays as is).

## 3. Approach — acquire-first: the acquire IS the probe

Chosen over (a) layering a non-acquiring probe onto the existing gate order — which leaves a TOCTOU hole where the probe says "free," the tray Roblox grabs the lock a beat later, and the real `Acquire()` fails after the gate said go — and (b) a copy/buttons-only fix that keeps blocking on harmless orphans.

Reorder startup so the mutex is acquired **before** the gate decides anything. The gate's answer is then guaranteed true at the moment RoRoRo proceeds: if `Acquire()` succeeded we hold the lock and nothing can take it; if it failed, someone genuinely holds it right now. Retry becomes trivially correct — just call `Acquire()` again. `MutexHolder.Acquire()` already fail-fasts (`CreateMutex` + `ERROR_ALREADY_EXISTS` → `false`, no waiting), so the primitive exists.

## 4. Startup flow & gate states

New `App.OnStartup` order: theme → resolve mutex name (unchanged) → **`mutex.Acquire()`** → process scan for context → gate decision:

| Mutex | Processes | State | Behavior |
|---|---|---|---|
| Acquired | none | **CLEAN** | Proceed silently — today's happy path |
| Acquired | ≥1 | **LEFTOVER** (info) | Info modal; **never blocks**; Continue is the default |
| Not acquired | any | **BLOCKED** | Helper modal — someone holds the lock |

`StartupGate` is reworked to return a `StartupGateResult` — `Clean`, `Leftover(int windowless, int windowed)`, or `Blocked` — computed from injected `IMutexHolder` + `IRobloxRunningProbe` (+ a windowed/windowless split via `MainWindowTitle`, the `RunningRobloxScanner` technique). Fully fake-testable. Any probe/scan exception keeps today's **fail-open** behavior (proceed, log).

**The LEFTOVER nuance (important):** processes found while the mutex was free are not necessarily junk. If RoRoRo previously quit while games were running, those clients keep playing *without* holding the lock — live, windowed, possibly mid-game. The modal therefore reports the split honestly (windowless orphans = safe to clean; open windows = "you can keep playing these — cleaning up closes them"), and **Clean up** routes through the existing `StopAllConfirmWindow` ("UNSAVED GAME STATE WILL BE LOST") only when windowed clients exist. Windowless-only → no scary confirm.

## 5. The modals

**BLOCKED** (reworked `RobloxAlreadyRunningWindow`):

- Heading: `Roblox is already running`
- Body: `RoRoRo needs the multi-instance lock, but Roblox is holding it — it may be running with no window at all, hidden in your system tray.`
- Steps (magenta numerals, replacing the restart script):
  `1. Click the ^ near the clock (bottom-right)`
  `2. Right-click the Roblox icon`
  `3. Choose Quit Roblox — then hit Retry`
- Note: `Roblox can start itself with Windows. You can turn that off in Roblox's tray settings — but you don't have to; RoRoRo will catch it here either way.`
- Mono-micro: `MULTI-INSTANCE NEEDS THE LOCK`
- Buttons: **Close Roblox for me** (primary) · **Retry** · **Quit RoRoRo**
- `Close Roblox for me` → `StopAll()` (with the unsaved-state confirm only when windowed clients exist) → auto-`Acquire()` → success closes the modal and startup continues; failure falls through to the still-locked tick.
- `Retry` → `Acquire()`; success → continue; failure → modal stays, shows `Still locked — Roblox is still running.`
- `Quit RoRoRo` → today's escape hatch, unchanged (`Shutdown(0)`).

**LEFTOVER** (new, same window shell, info variant):

- Heading: `Leftover Roblox processes`
- Body (split-aware), e.g.: `Found 3 leftover Roblox processes with no window, and 2 open Roblox windows from before. Multi-instance is fine — RoRoRo has the lock.` (Singular/plural and zero-count phrasing handled per side.)
- Buttons: **Clean up + continue** · **Continue** (default)

## 6. Runtime awareness — watcher + banner

**Probe primitive:** `IMutexHolder` gains `bool IsHeldElsewhere()` — a non-acquiring Win32 `OpenMutex` check ("the named mutex exists and it isn't ours"). Thin, beside `Acquire`/`IsHeld`.

**`MutexContestedWatcher`** (new, `Core/Diagnostics`, the ActivityMonitor discipline — injectable probe + timer, fake-testable): every ~5s, **only while RoRoRo doesn't hold the lock** (Off or Error), probe `IsHeldElsewhere()` and raise an edge-triggered `ContestedChanged(bool)`. While we hold the mutex there is nothing to watch — a later-starting Roblox cannot take it from us. Probe exceptions are treated as not-contested, logged, and never kill the timer.

**Main-window banner** (the window currently surfaces no multi-instance state): shown **only** when multi-instance is not On **and** the lock is contested:

> `Roblox has the multi-instance lock — it's probably running in your system tray.` [Close Roblox for me] [Retry]

On success the state flips to On through the existing path (tray icon/tooltip update as today) and the banner clears. Deliberately no banner for Off-and-uncontested (a user choice the tray already shows).

**Shared recovery:** both surfaces call one App-level `TryRecoverMultiInstance(bool closeRobloxFirst)` — optional `StopAll()` → `Acquire()` → update `MultiInstanceState` → report success/still-locked. One implementation, two surfaces, no drift.

## 7. Error handling

- Gate probe/scan exception → fail-open (proceed), log — unchanged posture.
- `StopAll()` partial failure (a pid refuses to die) → re-probe anyway; still locked → still-locked tick, modal/banner stays.
- Watcher probe exception → not-contested, log, keep ticking.
- Retry is `Acquire()` — cheap, idempotent, no rate-limit needed.
- Probe/exit races (tray Roblox quits just as Retry lands, or starts just after a tick) self-heal on the next Retry/tick; the acquire-first design means a success is never stale.
- Mutex-name resolution chain untouched.

## 8. Testing

- **StartupGate decision table** with fakes: acquired+0 → Clean; acquired+N → Leftover with correct windowless/windowed split; not-acquired → Blocked; probe exception → fail-open Clean (logged).
- **`TryRecoverMultiInstance`**: closeFirst true/false; stopper invoked (or not); acquire retried; state updated; still-locked reported.
- **`MutexContestedWatcher`**: probes only while not held; edge-triggered (no repeat events while state unchanged); re-arms after a flip back; exception → not-contested and timer survives.
- **Modal/banner ViewModel logic**: BLOCKED vs LEFTOVER variant selection; still-locked tick after failed retry; banner visible only when (not On ∧ contested); actions call the shared recovery; clears on success.
- **`IsHeldElsewhere`** stays thin Win32 → manual smoke, not unit tests.
- **Manual smoke script:** (1) start Roblox, close its window so it tray-resides, launch RoRoRo → BLOCKED modal → quit Roblox from the tray → Retry → RoRoRo continues without restart. (2) Same, but Close-for-me → auto-continues. (3) RoRoRo running with multi-instance Off → start Roblox → banner appears ≤5s → Close-for-me → state On, banner clears. (4) Leave orphan/windowed clients from a previous session → relaunch RoRoRo → LEFTOVER modal with the correct split → both buttons.

## 9. Decisions & rationale

| Decision | Choice | Why |
|---|---|---|
| Gate signal | Both, explained — mutex verdict decides, process scan contextualizes | Only the mutex actually blocks; orphans stop causing false blocks, but users still learn what's lying around |
| Probe strategy | Acquire-first (acquire IS the probe) | No TOCTOU between check and take; Retry is just `Acquire()` again; reuses the shipped fail-fast |
| Fix-it button | Close-for-me + auto-retry (primary), manual Retry kept | Fastest path for non-technical users; Este's own manual tray-quit flow stays first-class |
| Restart advice | Killed | Nothing in the new flow ever requires restarting RoRoRo |
| Runtime scope | Contested watcher + main-window banner | Closes the silent-tray-Error hole; banner passive, contested-only |
| Leftover handling | Info modal with windowless/windowed split | Some "leftovers" are live games; honesty + the existing unsaved-state confirm gate the destructive path |
| Old spec | Banner-correct 2026-05-08 detect spec | Its non-goals (no retry, no runtime) are superseded; repo rule is banner, not rewrite |

## 10. Deferred / follow-ups

- Mid-session orphan-cleanup surface in the main window (runtime equivalent of the LEFTOVER modal).
- Toast for the contested state, if the passive banner proves too quiet in practice.
- The "mutex-only background mode" v2 idea (competitive doc) — separate conversation.
- After this ships: cut the release (Limited fix + activity awareness + this) and run the MS Store submission.

## References

- Old gate spec (to banner-correct): [`2026-05-08-roblox-already-running-detect-design.md`](2026-05-08-roblox-already-running-detect-design.md)
- 0.727 facts: [`2026-06-30-rororo-limited-followups.md`](2026-06-30-rororo-limited-followups.md) §2
- Existing machinery: `src/ROROROblox.Core/MutexHolder.cs`, `src/ROROROblox.Core/Diagnostics/StartupGate.cs`, `RobloxRunningProbe.cs`, `RobloxInstanceStopper.cs`, `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml`, `src/ROROROblox.App/Tray/RunningRobloxScanner.cs`
