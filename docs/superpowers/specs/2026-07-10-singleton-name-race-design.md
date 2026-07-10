# The singleton is a name race, not a lock

**Date:** 2026-07-10
**Status:** Built
**Supersedes the mutex model in:** [2026-07-02-rororo-tray-residence-gate-design.md](2026-07-02-rororo-tray-residence-gate-design.md) §"Retry becomes trivially correct"

## What we believed

That `Local\ROBLOX_singletonEvent` is a mutex; that Roblox *holds* it; that `Acquire()` fails with
`ERROR_ALREADY_EXISTS` when someone else has it; and that Retry is therefore "trivially correct —
just call `Acquire()` again."

Every clause of that is wrong, and the errors compound.

## What is actually true

Measured 2026-07-10 against a tray-resident Roblox client (Windows-startup, no game running, no
RoRoRo process):

```text
OpenMutex (Local\ROBLOX_singletonEvent)  → NULL, err 6   (ERROR_INVALID_HANDLE)
OpenEvent (Local\ROBLOX_singletonEvent)  → handle        (it is an Event)
CreateMutex(name, bInitialOwner: true)   → NULL, err 6   (ERROR_INVALID_HANDLE)
```

**Roblox creates an Event. RoRoRo creates a Mutex.** Same name, different kernel object types.
Windows lets exactly one object own a name, so whoever creates it first wins, and the loser's
create call fails *because the name is taken by an object of another type*.

That is the whole trick. RoRoRo does not "hold a lock Roblox wants" — it **squats the name with an
incompatible object type**, so Roblox's own `CreateEvent` fails and Roblox never installs its
single-instance enforcement. Multi-instance is a side effect of Roblox losing a name race.

Three consequences follow.

### 1. `Acquire()` never hit the branch its comment described

`MutexHolder.Acquire()` checked `ERROR_ALREADY_EXISTS` and commented *"Another process already holds
the named mutex."* The Roblox case cannot reach that branch: it fails earlier, at
`handle.IsInvalid`, with `ERROR_INVALID_HANDLE`. Both paths returned a bare `false`, so nothing
noticed.

The two failure codes mean **opposite things**:

| `CreateMutex` result | Who holds the name | Multi-instance? | Correct response |
| --- | --- | --- | --- |
| `ERROR_INVALID_HANDLE` (6) | **Roblox**, as an Event | Genuinely off | Block; offer recovery |
| `ERROR_ALREADY_EXISTS` (183) | **Another RoRoRo or compatible tool**, as a Mutex | **Still works** | Proceed; do not block |

If a peer tool squats the name, Roblox has *already lost* its singleton — multi-instance is fine and
there is nothing to recover from. RoRoRo nonetheless threw the BLOCKED modal at the user.

### 2. `IsHeldElsewhere()` was blind to Roblox

The probe opened the name with `OpenMutex`, which cannot open an Event. Against the only case that
matters in production it returned `false`, so `MutexContestedWatcher` never raised the contested
banner when Roblox took the singleton mid-session.

Every existing unit test held the name with another `MutexHolder` — a *Mutex* — which is the
compatible-tool case. The tests passed for years while the production case was broken. A bug can hide
indefinitely behind a test that only exercises the easy branch.

### 3. Roblox-with-Windows breaks the happy path

The tray client creates the Event **at process start**, not at game launch (observed: process up at
06:25:59, Event present, no game). Since Roblox now starts with Windows by default, RoRoRo can never
win the name on a cold boot. **Every launch hits the BLOCKED modal** until the user fully exits
Roblox.

And the object dies only when its **last handle closes**. A just-quit `RobloxPlayerBeta` takes a beat
to tear down, so a single instantaneous `Acquire()` right after quitting Roblox loses — which is the
mechanical cause of the long-standing "hit Retry twice" behavior.

## What was built

**A typed outcome.** `MutexAcquireOutcome { Acquired, HeldByRoblox, HeldByCompatibleTool, Failed }`,
returned by the new `IMutexHolder.TryAcquire()`. `Acquire()` remains as
`TryAcquire() == Acquired` so the boolean contract is preserved.

**A gate that routes by outcome.** `StartupGate.Evaluate(MutexAcquireOutcome)` maps `HeldByRoblox` →
`Blocked` (as before), `Failed` → `Blocked` (conservative), and `HeldByCompatibleTool` → the new
`StartupGateResult.SharedLock`, which **skips the modal entirely** and proceeds exactly as
"Start anyway" does. The contested watcher still banners that we don't own the handle.

**A probe that sees both types.** `IsHeldElsewhere()` now checks `OpenMutex || OpenEvent`. The
contested banner works against real Roblox for the first time.

**A Retry that polls.** `IMutexHolder.TryAcquireWithRetryAsync(window, interval)` re-attempts until it
wins or the window expires — 3 s at 150 ms in the app. It returns *early* on
`HeldByCompatibleTool`, because a peer tool holding the name is a stable state, not a race, and
waiting it out accomplishes nothing. Recovery went async so the modal stays responsive while it
polls; the action buttons disable for the duration so a second Retry press can't race the first.

## Verification

Live, against the real tray-resident Roblox:

```text
StartupGate: singleton name held by Roblox (as an event); multi-instance unavailable. Blocking.
```

Retry, with Roblox still holding the name, returned after **3.16 s** (window: 3 s) — previously it
returned instantly — and the modal remained responsive throughout.

Unit tests recreate both cases honestly by holding the name with a real `EventWaitHandle` (Roblox) or
a real `MutexHolder` (peer tool):

- `TryAcquire` → `HeldByRoblox` when an Event owns the name; `HeldByCompatibleTool` when a Mutex does.
- `IsHeldElsewhere` → true for an Event. **Reverting the probe to `OpenMutex`-only turns this test
  red**, which is precisely what no test did before.
- `TryAcquireWithRetryAsync` wins once the blocker releases mid-poll, gives up when it doesn't, and
  returns immediately for a compatible tool.
- `StartupGate` returns `SharedLock` (not `Blocked`) for the compatible-tool outcome.

868 unit + 16 harness tests pass.

## Not addressed

- **The name is still hardcoded.** `MutexHolder.DefaultMutexName` is a constant and the remote-config
  swap remains unbuilt — see the banner atop the canonical spec. If Roblox renames the object, this
  still needs a binary release. Nothing here changes that.
- **Roblox's side is inferred.** That Roblox skips single-instance enforcement *because* its
  `CreateEvent` fails is the only explanation consistent with the product working, but it has not been
  instrumented. The type mismatch and the error codes are measured; Roblox's internal branch is not.
- **The BLOCKED modal still fires on every cold boot** for users with Roblox-on-startup. That is
  correct behavior given the name race, but it is a UX cost worth a separate look — "mutex-only
  background mode," where RoRoRo takes the name at login before Roblox can, is the obvious answer and
  is already noted as a v2 idea in the competitive doc.
