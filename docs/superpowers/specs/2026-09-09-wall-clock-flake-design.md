# Killing the wall-clock flake family

> Design, 2026-09-09. Scope: `AppStorageDefenderTests` and `FpsCapSettlerTests` — the two classes
> CLAUDE.md names as "the wall-clock flake family", failing one arch on roughly half of all PRs.

## What was measured

Both PRs open on 2026-09-09 hit it. #204's arm64 job failed twice in a row on **different** members
of the family before passing on the third attempt:

| attempt | failed test |
|---|---|
| 1 | `AppStorageDefenderTests.NotifyConsumed_CompletesAfterGrace_NotBefore_NotAtCap` |
| 2 | `FpsCapSettlerTests.PostWriteQuietWait_CompetingWriteLandsInsideTheWindow_ForcesARetry` |
| 3 | — green |

x64 was green throughout, both times. The cost is not the reruns; it is that **you cannot tell a
real arm64 regression from noise without opening the logs**, which trains the team to wave red
through. That is the defect worth fixing.

## They are two different bugs, and only one is fixable here

Reading both, the shared name hides different mechanics.

### FpsCapSettler — already fixed as far as it can be

It already injects `TimeProvider`, already lives in a `DisableParallelization = true` collection,
and its pump already avoids `Task.Yield()` spin in favour of a semaphore (with a documented
measurement of why: a spin loop re-posts to the worker's LIFO **local** queue, starving the
**global** queue where `FakeTimeProvider` posts its timer callback).

Its residual failure is the pump exceeding `PumpObservationCeiling` (30s of real time) waiting for
a continuation the runner never scheduled. Its own assert message already says elapsed time cannot
separate a starved thread from a hang. **No change proposed.** Every cheap lever is already pulled,
and raising the ceiling only trades detection latency for a quieter dashboard.

### AppStorageDefender — genuinely unfixed

`AppStorageDefender` has **no clock injection at all**. It uses real `Task.Delay` for the
post-attach grace and the max cap, and real `Stopwatch.GetTimestamp()` for self-write suppression.
The failing test then races it:

```csharp
var grace = TimeSpan.FromMilliseconds(400);
...
defender.NotifyConsumed();
Assert.False(defender.Completion.IsCompleted, "grace not honored");   // <-- the race
```

If the thread is descheduled for more than 400 ms between those two lines — routine on a loaded
arm64 runner — the grace genuinely elapses and the assert fires on correct code. The test is
falsifiable by load, not by a defect.

## The change

**Inject `TimeProvider` into `AppStorageDefender`**, defaulted to `TimeProvider.System`, and
convert the two timing-sensitive tests to `FakeTimeProvider`. The pattern is already established in
this repo (`FpsCapSettler`, `AlertDispatcher`, `RobloxLauncher`, `PhoneAlertSender`), and
`Microsoft.Extensions.TimeProvider.Testing` is already a test dependency.

| site | before | after |
|---|---|---|
| post-attach grace | `Task.Delay(_postAttachGrace, ct)` | `Task.Delay(_postAttachGrace, _time, ct)` |
| max cap | `Task.Delay(_maxCap, ct)` | `Task.Delay(_maxCap, _time, ct)` |
| self-write stamp | `Stopwatch.GetTimestamp()` | `_time.GetTimestamp()` |
| suppression window | `static` off `Stopwatch.Frequency` | instance field off `_time.TimestampFrequency` |

With the clock faked, both tests lose `Stopwatch`, lose every real `Task.Delay`, and assert on
exact fake instants. Load cannot reach them.

### Deliberately NOT converted

The two `Task.Delay(50)` calls in the IO retry path stay real. They back off a genuine filesystem
contention, not domain timing; faking them would make a fake-clock test hang on an error path it is
not testing, in exchange for nothing. Boundary recorded here so the inconsistency reads as a choice.

### The three FSW tests stay real

`InitialStamp`, `Drift_Restamps` and `AfterCompletion_DriftIsNotRestamped` depend on a real
`FileSystemWatcher`. No clock abstraction makes an OS file event deterministic, and the file already
documents their generous budgets. They join a parallel-disabled collection instead — the same
treatment `FpsCapSettler` got — so at least they are not competing with ~2,000 other tests for pool
threads while they wait.

## Verification

- The two converted tests must pass with **zero real delay** — a full-suite run should get measurably
  shorter, since the pair currently spends ~1.5s sleeping.
- A fence test pins the collection attribute, mirroring `FpsCapSettlerTests.ThePumpKeepsItsQuietPool`,
  so the parallelization guard cannot be tidied away.
- Both arches green on CI without a rerun.

## What this does not claim

This removes one of the two family members. `FpsCapSettler` can still flake on a starved runner, and
if it does, the honest read stays what its own assert message says: re-run, and only a repeatable
failure is evidence of a hang. If it keeps costing cycles after this lands, the next lever is
`MaxParallelThreads` on the assembly, not another per-test patch.
