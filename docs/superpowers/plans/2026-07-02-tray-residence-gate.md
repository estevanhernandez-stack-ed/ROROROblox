# Tray-Residence Gate + Runtime Lock Awareness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework RoRoRo's "Roblox already running" startup gate to be acquire-first (the mutex verdict decides, the process scan contextualizes), replace the restart-script modal with a Close-Roblox-for-me / Retry / Quit modal that never requires restarting RoRoRo, and add a contested-mutex watcher + main-window banner so the runtime case (Roblox grabbing the lock post-startup) is no longer silent.

**Architecture:** `MutexHolder` gains a non-acquiring `IsHeldElsewhere()` probe (Win32 `OpenMutex`). `StartupGate` becomes acquire-first: `Evaluate(bool mutexAcquired)` returns `StartupGateResult` (`Clean` / `Leftover(windowless, windowed)` / `Blocked`). Two thin modals render those states; a shared App-level `TryRecoverMultiInstance` composes the already-shipped `RobloxInstanceStopper.StopAll()` + `mutex.Acquire()`. A new `MutexContestedWatcher` (Core/Diagnostics, fake-testable, ActivityMonitor discipline) drives a main-window banner reusing the Part A `IdleSummaryText` strip pattern.

**Tech Stack:** .NET 10 / C#, WPF, CsWin32 (Win32 source generator — mutex P/Invoke), xUnit (hand-rolled fakes, no Moq).

## Global Constraints

Copied from the approved spec (`docs/superpowers/specs/2026-07-02-rororo-tray-residence-gate-design.md`) — every task implicitly includes these:

- **Build/test with the explicit dotnet host** (bare `dotnet` on PATH is SDK 10.0.202 and fails the 10.0.203 pin): PowerShell `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" …`; bash `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" …`.
- **Solution is `ROROROblox.slnx`** — never the stray `ROROROblox.sln`.
- **Acquire-first is the invariant:** the gate's verdict must be true at the moment RoRoRo proceeds — the mutex is acquired *before* the gate decides, so a "proceed" is never stale. Never re-introduce a check-then-acquire gap.
- **Never touch Roblox's own settings** (start-with-Windows etc.) — we inform, we don't reconfigure Roblox.
- **The restart script dies:** nothing in the new flow may tell the user to close-and-reopen RoRoRo.
- **Copy strings are exact** — the spec §5/§6 strings are reproduced verbatim in the tasks below; match them character-for-character (including `—` em-dash, `>` in "> 15m", the `^` glyph).
- **Test doubles:** hand-rolled fakes matching the existing `ROROROblox.Tests` style (see `StartupGateTests`' `FakeRobloxRunningProbe` + `ListLogger<T>`), never Moq.
- **No hardcoded absolute user-home paths** in any committed file — CI's full-tree local-path guard rejects a `%USERPROFILE%`-style leak. Use relative paths in source, `%LOCALAPPDATA%`/`$env:` forms in docs.
- **Mutex-name resolution chain is untouched** (RemoteConfig → LastKnownGood → Default).
- **Commits:** conventional (`feat`/`fix`/`test`/`docs`/`refactor`), one per task (or per RED→GREEN cycle).

---

## File Structure

**New:**
- `src/ROROROblox.Core/Diagnostics/StartupGateResult.cs` — the result record hierarchy.
- `src/ROROROblox.Core/Diagnostics/RobloxProcessInfo.cs` — `readonly record struct (int Pid, bool HasWindow)`.
- `src/ROROROblox.Core/Diagnostics/MutexContestedWatcher.cs` — the runtime watcher.
- `src/ROROROblox.App/ViewModels/LeftoverSummary.cs` — LEFTOVER copy formatter (mirrors `IdleSummary`).
- `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs` — the contested-banner + still-locked copy constants.
- `src/ROROROblox.App/Modals/LeftoverProcessesWindow.xaml` (+ `.xaml.cs`) — the LEFTOVER info modal.
- Tests: `MutexHolderIsHeldElsewhereTests.cs`, `RobloxRunningProbeSplitTests.cs`, `StartupGateEvaluateTests.cs`, `MutexContestedWatcherTests.cs`, `LeftoverSummaryTests.cs`, additions to `MainViewModelTests.cs`.

**Modified:**
- `src/ROROROblox.Core/IMutexHolder.cs` + `MutexHolder.cs` — add `IsHeldElsewhere()`.
- `src/ROROROblox.Core/NativeMethods.txt` — add `OpenMutex`.
- `src/ROROROblox.Core/Diagnostics/IRobloxRunningProbe.cs` + `RobloxRunningProbe.cs` — add `GetRunningPlayers()`.
- `src/ROROROblox.Core/Diagnostics/StartupGate.cs` — add `Evaluate(bool)` (+ keep `ShouldProceed` until Task 7 removes it).
- `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml` (+ `.xaml.cs`) — rework to BLOCKED variant.
- `src/ROROROblox.App/App.xaml.cs` — OnStartup reorder, `TryRecoverMultiInstance`, watcher wiring, DI reg.
- `src/ROROROblox.App/ViewModels/MainViewModel.cs` + `MainWindow.xaml` — contested banner.
- Existing tests touched by interface changes: `StartupGateTests.cs`, `RobloxInstanceStopperTests.cs` (fakes gain the new method).

---

## Task 1: MutexHolder.IsHeldElsewhere() — non-acquiring probe

**Files:**
- Modify: `src/ROROROblox.Core/NativeMethods.txt`, `src/ROROROblox.Core/IMutexHolder.cs`, `src/ROROROblox.Core/MutexHolder.cs`
- Test: `src/ROROROblox.Tests/MutexHolderIsHeldElsewhereTests.cs`

**Interfaces:**
- Produces: `bool IMutexHolder.IsHeldElsewhere()` — true iff the named mutex exists AND this holder does not hold it. Consumed by `MutexContestedWatcher` (Task 4) and the recovery/gate paths.

- [ ] **Step 1: Add the Win32 symbol.** Append to `src/ROROROblox.Core/NativeMethods.txt`:

```
OpenMutex
```

- [ ] **Step 2: Write the failing test**

```csharp
// src/ROROROblox.Tests/MutexHolderIsHeldElsewhereTests.cs
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class MutexHolderIsHeldElsewhereTests
{
    private static string UniqueName() => $@"Local\rororo-test-{System.Guid.NewGuid():N}";

    [Fact]
    public void IsHeldElsewhere_NobodyHolds_ReturnsFalse()
    {
        using var holder = new MutexHolder(UniqueName());
        Assert.False(holder.IsHeldElsewhere());
    }

    [Fact]
    public void IsHeldElsewhere_WeHoldIt_ReturnsFalse()
    {
        using var holder = new MutexHolder(UniqueName());
        Assert.True(holder.Acquire());
        Assert.False(holder.IsHeldElsewhere()); // held by us is NOT "elsewhere"
    }

    [Fact]
    public void IsHeldElsewhere_AnotherHolderHasIt_ReturnsTrue()
    {
        var name = UniqueName();
        using var owner = new MutexHolder(name);
        Assert.True(owner.Acquire());

        using var observer = new MutexHolder(name);
        Assert.True(observer.IsHeldElsewhere()); // owner holds it, observer sees it
    }

    [Fact]
    public void IsHeldElsewhere_AfterOwnerReleases_ReturnsFalse()
    {
        var name = UniqueName();
        using var owner = new MutexHolder(name);
        using var observer = new MutexHolder(name);
        Assert.True(owner.Acquire());
        Assert.True(observer.IsHeldElsewhere());

        owner.Release();
        Assert.False(observer.IsHeldElsewhere());
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MutexHolderIsHeldElsewhereTests"`
Expected: FAIL — `IsHeldElsewhere` not defined.

- [ ] **Step 4: Add to the interface** (`IMutexHolder.cs`):

```csharp
    /// <summary>
    /// Non-acquiring probe: true iff the named mutex currently exists AND this holder does not
    /// own it (i.e. someone else — the tray-resident Roblox — holds it). Returns false when we
    /// hold it or when nobody does. Does not acquire, wait, or mutate any handle.
    /// </summary>
    bool IsHeldElsewhere();
```

- [ ] **Step 5: Implement in `MutexHolder.cs`.** Add the method (uses CsWin32 `OpenMutex`; `SYNCHRONIZE = 0x00100000`):

```csharp
    private const uint SynchronizeAccess = 0x00100000; // SYNCHRONIZE

    public bool IsHeldElsewhere()
    {
        lock (_lock)
        {
            if (_handle is { IsInvalid: false })
            {
                return false; // we hold it — not "elsewhere"
            }
        }

        SafeFileHandle probe;
        try
        {
            unsafe
            {
                probe = PInvoke.OpenMutex(SynchronizeAccess, bInheritHandle: false, _mutexName);
            }
        }
        catch
        {
            return false; // probe failure → treat as not contested (fail-safe: no false banner)
        }

        try
        {
            return !probe.IsInvalid; // opened successfully → the mutex exists, held by someone else
        }
        finally
        {
            probe.Dispose();
        }
    }
```

**CsWin32 note:** confirm the generated `PInvoke.OpenMutex` signature after the first build — CsWin32 may emit the access-rights parameter as a `uint`, a `SYNCHRONIZATION_ACCESS_RIGHTS` enum, or a `MUTEX_ACCESS_RIGHTS` enum, and may or may not require `unsafe` for the `PCWSTR` name. Match the generated overload (cast `SynchronizeAccess` to the enum if needed, e.g. `(SYNCHRONIZATION_ACCESS_RIGHTS)SynchronizeAccess`, or pass the enum member directly if one exists). `SingleInstanceGuard.cs` and the existing `MutexHolder` CsWin32 calls show the repo's local call style.

- [ ] **Step 6: Run to verify it passes**

Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MutexHolderIsHeldElsewhereTests"`
Expected: 4/4 PASS. Then full build: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Core/NativeMethods.txt src/ROROROblox.Core/IMutexHolder.cs src/ROROROblox.Core/MutexHolder.cs src/ROROROblox.Tests/MutexHolderIsHeldElsewhereTests.cs
git commit -m "feat(core): MutexHolder.IsHeldElsewhere non-acquiring OpenMutex probe"
```

---

## Task 2: RobloxProcessInfo + windowed/windowless split on the probe

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/RobloxProcessInfo.cs`
- Modify: `src/ROROROblox.Core/Diagnostics/IRobloxRunningProbe.cs`, `RobloxRunningProbe.cs`
- Modify (fakes gain the method): `src/ROROROblox.Tests/StartupGateTests.cs`, `src/ROROROblox.Tests/RobloxInstanceStopperTests.cs`
- Test: `src/ROROROblox.Tests/RobloxRunningProbeSplitTests.cs`

**Interfaces:**
- Produces: `readonly record struct RobloxProcessInfo(int Pid, bool HasWindow)`; `IReadOnlyList<RobloxProcessInfo> IRobloxRunningProbe.GetRunningPlayers()`. `GetRunningPlayerPids()` stays (delegates to the new method), so `RobloxInstanceStopper` and the tray count are unaffected.

- [ ] **Step 1: Write the failing test** (proves the split + that pids delegates)

```csharp
// src/ROROROblox.Tests/RobloxRunningProbeSplitTests.cs
using System.Linq;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class RobloxRunningProbeSplitTests
{
    // A hand-rolled probe returning fixed data — proves GetRunningPlayerPids delegates to
    // GetRunningPlayers. The real RobloxRunningProbe's process enumeration is covered by
    // manual smoke (needs live processes), not this unit test.
    private sealed class FixedProbe : IRobloxRunningProbe
    {
        private readonly RobloxProcessInfo[] _players;
        public FixedProbe(params RobloxProcessInfo[] players) => _players = players;
        public System.Collections.Generic.IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => _players;
        public System.Collections.Generic.IReadOnlyList<int> GetRunningPlayerPids()
            => GetRunningPlayers().Select(p => p.Pid).ToArray();
    }

    [Fact]
    public void GetRunningPlayerPids_DelegatesToGetRunningPlayers()
    {
        var probe = new FixedProbe(
            new RobloxProcessInfo(101, HasWindow: true),
            new RobloxProcessInfo(202, HasWindow: false));

        Assert.Equal(new[] { 101, 202 }, probe.GetRunningPlayerPids());
    }

    [Fact]
    public void GetRunningPlayers_CarriesWindowedFlag()
    {
        var probe = new FixedProbe(
            new RobloxProcessInfo(101, HasWindow: true),
            new RobloxProcessInfo(202, HasWindow: false));

        var players = probe.GetRunningPlayers();
        Assert.Equal(1, players.Count(p => p.HasWindow));
        Assert.Equal(1, players.Count(p => !p.HasWindow));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxRunningProbeSplitTests"`
Expected: FAIL — `RobloxProcessInfo` / `GetRunningPlayers` not defined.

- [ ] **Step 3: Create the record + extend the interface**

`src/ROROROblox.Core/Diagnostics/RobloxProcessInfo.cs`:

```csharp
namespace ROROROblox.Core.Diagnostics;

/// <summary>A running RobloxPlayerBeta.exe process: its PID and whether it currently has a
/// top-level window (windowless = tray-resident client or orphan; windowed = a real game the
/// user may still be playing).</summary>
public readonly record struct RobloxProcessInfo(int Pid, bool HasWindow);
```

Add to `IRobloxRunningProbe.cs`:

```csharp
    /// <summary>
    /// Snapshot of every running RobloxPlayerBeta.exe with a windowed/windowless flag, so the
    /// startup gate can distinguish harmless orphans (windowless) from live game windows.
    /// </summary>
    IReadOnlyList<RobloxProcessInfo> GetRunningPlayers();
```

- [ ] **Step 4: Implement in `RobloxRunningProbe.cs`** (window flag via `MainWindowHandle`; `GetRunningPlayerPids` delegates):

```csharp
    public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
    {
        var processes = Process.GetProcessesByName(PlayerProcessName);
        try
        {
            return processes.Select(p =>
            {
                bool hasWindow;
                try { hasWindow = p.MainWindowHandle != IntPtr.Zero; }
                catch { hasWindow = false; } // exited mid-scan / access denied → treat as windowless
                return new RobloxProcessInfo(p.Id, hasWindow);
            }).ToArray();
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    public IReadOnlyList<int> GetRunningPlayerPids()
        => GetRunningPlayers().Select(p => p.Pid).ToArray();
```

- [ ] **Step 5: Update the two existing fakes** so the projects compile. In `StartupGateTests.cs`'s `FakeRobloxRunningProbe` and `RobloxInstanceStopperTests.cs`'s `FakeProbe`, add a `GetRunningPlayers()` implementation. Minimal shape (return windowless infos from the existing pid data so existing tests are unaffected):

```csharp
    // in FakeRobloxRunningProbe (StartupGateTests.cs):
    public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
    {
        if (NextThrow is not null) throw NextThrow;
        return NextResult.Select(pid => new RobloxProcessInfo(pid, HasWindow: false)).ToArray();
    }
    // keep GetRunningPlayerPids returning NextResult as before (or delegate to GetRunningPlayers)
```

```csharp
    // in FakeProbe (RobloxInstanceStopperTests.cs):
    public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
        => _throw is null ? _pids.Select(p => new RobloxProcessInfo(p, false)).ToArray() : throw _throw;
```

(Add `using ROROROblox.Core.Diagnostics;` / `using System.Linq;` where needed.)

- [ ] **Step 6: Run to verify** — split tests pass, and the pre-existing `StartupGateTests` + `RobloxInstanceStopperTests` still pass.

Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxRunningProbeSplitTests|FullyQualifiedName~StartupGateTests|FullyQualifiedName~RobloxInstanceStopperTests"`
Expected: all PASS. Full build 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/RobloxProcessInfo.cs src/ROROROblox.Core/Diagnostics/IRobloxRunningProbe.cs src/ROROROblox.Core/Diagnostics/RobloxRunningProbe.cs src/ROROROblox.Tests/RobloxRunningProbeSplitTests.cs src/ROROROblox.Tests/StartupGateTests.cs src/ROROROblox.Tests/RobloxInstanceStopperTests.cs
git commit -m "feat(core): windowed/windowless split on IRobloxRunningProbe.GetRunningPlayers"
```

---

## Task 3: StartupGateResult + acquire-first StartupGate.Evaluate

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/StartupGateResult.cs`
- Modify: `src/ROROROblox.Core/Diagnostics/StartupGate.cs` (ADD `Evaluate`; keep `ShouldProceed` for now — Task 7 removes it + the App call site together, so every commit builds)
- Test: `src/ROROROblox.Tests/StartupGateEvaluateTests.cs`

**Interfaces:**
- Consumes: `IRobloxRunningProbe.GetRunningPlayers()` (Task 2).
- Produces: `abstract record StartupGateResult { Clean; Leftover(int Windowless, int Windowed); Blocked }`; `StartupGateResult StartupGate.Evaluate(bool mutexAcquired)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/ROROROblox.Tests/StartupGateEvaluateTests.cs
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class StartupGateEvaluateTests
{
    private sealed class FakeProbe : IRobloxRunningProbe
    {
        public System.Collections.Generic.IReadOnlyList<RobloxProcessInfo> Players { get; set; }
            = System.Array.Empty<RobloxProcessInfo>();
        public System.Exception? Throw { get; set; }
        public System.Collections.Generic.IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
            => Throw is null ? Players : throw Throw;
        public System.Collections.Generic.IReadOnlyList<int> GetRunningPlayerPids()
            => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(GetRunningPlayers(), p => p.Pid));
    }

    [Fact]
    public void Evaluate_MutexNotAcquired_ReturnsBlocked()
    {
        var gate = new StartupGate(new FakeProbe());
        Assert.IsType<StartupGateResult.Blocked>(gate.Evaluate(mutexAcquired: false));
    }

    [Fact]
    public void Evaluate_AcquiredNoProcesses_ReturnsClean()
    {
        var gate = new StartupGate(new FakeProbe());
        Assert.IsType<StartupGateResult.Clean>(gate.Evaluate(mutexAcquired: true));
    }

    [Fact]
    public void Evaluate_AcquiredWithProcesses_ReturnsLeftoverWithSplit()
    {
        var probe = new FakeProbe
        {
            Players = new[]
            {
                new RobloxProcessInfo(1, HasWindow: false),
                new RobloxProcessInfo(2, HasWindow: false),
                new RobloxProcessInfo(3, HasWindow: true),
            },
        };
        var result = gate_Evaluate(probe, true);
        var leftover = Assert.IsType<StartupGateResult.Leftover>(result);
        Assert.Equal(2, leftover.Windowless);
        Assert.Equal(1, leftover.Windowed);
    }

    [Fact]
    public void Evaluate_AcquiredButProbeThrows_FailsOpenToClean()
    {
        var probe = new FakeProbe { Throw = new System.InvalidOperationException("scan mid-enum") };
        // acquired => we hold the lock => proceeding is safe even if we can't count leftovers
        Assert.IsType<StartupGateResult.Clean>(gate_Evaluate(probe, true));
    }

    private static StartupGateResult gate_Evaluate(IRobloxRunningProbe probe, bool acquired)
        => new StartupGate(probe).Evaluate(acquired);
}
```

- [ ] **Step 2: Run to verify it fails** → FAIL (`Evaluate` / `StartupGateResult` not defined).

- [ ] **Step 3: Create the result type**

`src/ROROROblox.Core/Diagnostics/StartupGateResult.cs`:

```csharp
namespace ROROROblox.Core.Diagnostics;

/// <summary>Acquire-first startup verdict. The mutex is acquired BEFORE this is computed, so the
/// answer is true at the moment RoRoRo proceeds. Mirrors the LaunchResult record-hierarchy pattern.</summary>
public abstract record StartupGateResult
{
    /// <summary>Mutex acquired, no leftover Roblox processes — proceed silently.</summary>
    public sealed record Clean : StartupGateResult;

    /// <summary>Mutex acquired, but leftover Roblox processes exist. Multi-instance is fine; this
    /// is informational. Windowless = safe-to-clean orphans; Windowed = live games the user may
    /// still be playing.</summary>
    public sealed record Leftover(int Windowless, int Windowed) : StartupGateResult;

    /// <summary>Mutex NOT acquired — someone else (the tray-resident Roblox) holds it. Block and
    /// offer recovery.</summary>
    public sealed record Blocked : StartupGateResult;
}
```

- [ ] **Step 4: Add `Evaluate` to `StartupGate.cs`** (leave `ShouldProceed` in place):

```csharp
    /// <summary>
    /// Acquire-first gate. Caller acquires the mutex first and passes the result. Not acquired →
    /// Blocked. Acquired + no leftover processes → Clean. Acquired + leftovers → Leftover(split).
    /// Fail-open to Clean if the process scan throws (we hold the lock, so proceeding is safe).
    /// </summary>
    public StartupGateResult Evaluate(bool mutexAcquired)
    {
        if (!mutexAcquired)
        {
            _log.LogInformation("StartupGate: mutex not acquired — Roblox holds the lock; blocking.");
            return new StartupGateResult.Blocked();
        }

        try
        {
            var players = _probe.GetRunningPlayers();
            if (players.Count == 0)
            {
                _log.LogInformation("StartupGate: mutex acquired, no leftover Roblox processes; clean start.");
                return new StartupGateResult.Clean();
            }

            var windowed = players.Count(p => p.HasWindow);
            var windowless = players.Count - windowed;
            _log.LogInformation(
                "StartupGate: mutex acquired with {Windowless} windowless + {Windowed} windowed leftover Roblox process(es); informational.",
                windowless, windowed);
            return new StartupGateResult.Leftover(windowless, windowed);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "StartupGate: process scan threw after mutex acquired; proceeding (we hold the lock).");
            return new StartupGateResult.Clean();
        }
    }
```

(Add `using System.Linq;` if not present.)

- [ ] **Step 5: Run to verify** → new tests PASS; existing `StartupGateTests` (still exercising `ShouldProceed`) also PASS. Full build 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/StartupGateResult.cs src/ROROROblox.Core/Diagnostics/StartupGate.cs src/ROROROblox.Tests/StartupGateEvaluateTests.cs
git commit -m "feat(core): acquire-first StartupGate.Evaluate returning StartupGateResult"
```

---

## Task 4: MutexContestedWatcher

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/MutexContestedWatcher.cs`
- Test: `src/ROROROblox.Tests/MutexContestedWatcherTests.cs`

**Interfaces:**
- Consumes: `IMutexHolder` (`IsHeld`, `IsHeldElsewhere` from Task 1).
- Produces: `MutexContestedWatcher(IMutexHolder)` with `event EventHandler<bool> ContestedChanged`, `void Poll()` (test seam), `void Start()`, `void Stop()`, `IDisposable`.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/ROROROblox.Tests/MutexContestedWatcherTests.cs
using System.Collections.Generic;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MutexContestedWatcherTests
{
    private sealed class FakeMutex : IMutexHolder
    {
        public bool Held;
        public bool HeldElsewhere;
        public string MutexName => @"Local\fake";
        public bool IsHeld => Held;
        public bool IsHeldElsewhere() => HeldElsewhere;
        public bool Acquire() { Held = true; return true; }
        public void Release() { Held = false; }
        public event System.EventHandler? MutexLost { add { } remove { } }
    }

    [Fact]
    public void Poll_ContestedWhenNotHeldAndHeldElsewhere_FiresTrueOnce()
    {
        var mutex = new FakeMutex { Held = false, HeldElsewhere = true };
        var watcher = new MutexContestedWatcher(mutex);
        var fires = new List<bool>();
        watcher.ContestedChanged += (_, c) => fires.Add(c);

        watcher.Poll();
        watcher.Poll(); // still contested → no repeat (edge-triggered)

        Assert.Equal(new[] { true }, fires);
    }

    [Fact]
    public void Poll_WhileWeHoldIt_NeverContested()
    {
        var mutex = new FakeMutex { Held = true, HeldElsewhere = true }; // held-elsewhere ignored while we hold
        var watcher = new MutexContestedWatcher(mutex);
        var fires = new List<bool>();
        watcher.ContestedChanged += (_, c) => fires.Add(c);

        watcher.Poll();

        Assert.Empty(fires);
    }

    [Fact]
    public void Poll_ContestedThenFreed_FiresTrueThenFalse()
    {
        var mutex = new FakeMutex { Held = false, HeldElsewhere = true };
        var watcher = new MutexContestedWatcher(mutex);
        var fires = new List<bool>();
        watcher.ContestedChanged += (_, c) => fires.Add(c);

        watcher.Poll();                    // true
        mutex.HeldElsewhere = false;
        watcher.Poll();                    // false (re-arm)
        mutex.HeldElsewhere = true;
        watcher.Poll();                    // true again

        Assert.Equal(new[] { true, false, true }, fires);
    }
}
```

- [ ] **Step 2: Run to verify it fails** → FAIL (type not defined).

- [ ] **Step 3: Implement**

```csharp
// src/ROROROblox.Core/Diagnostics/MutexContestedWatcher.cs
using System.Threading;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Runtime watcher for the multi-instance lock being held by someone else (the tray-resident
/// Roblox). Probes only while RoRoRo does NOT hold the mutex — once we hold it, nothing can take
/// it, so there is nothing to watch. Edge-triggered: ContestedChanged fires only on a transition.
/// Mirrors ActivityMonitor's discipline (injectable, Poll() test seam, Interlocked-guarded timer).
/// </summary>
public sealed class MutexContestedWatcher : IDisposable
{
    private const int IntervalMs = 5_000;

    private readonly IMutexHolder _mutex;
    private Timer? _timer;
    private bool _lastContested;
    private int _polling; // 0 idle, 1 running — skip overlap
    private bool _disposed;

    public event EventHandler<bool>? ContestedChanged;

    public MutexContestedWatcher(IMutexHolder mutex)
        => _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));

    public void Start()
    {
        if (_disposed) return;
        _timer ??= new Timer(_ => SafePoll(), null, IntervalMs, IntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>One probe tick. Contested = we don't hold the lock AND someone else does.</summary>
    public void Poll()
    {
        var contested = !_mutex.IsHeld && _mutex.IsHeldElsewhere();
        if (contested != _lastContested)
        {
            _lastContested = contested;
            ContestedChanged?.Invoke(this, contested);
        }
    }

    private void SafePoll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try { Poll(); }
        catch { /* never let a probe tick crash the timer thread */ }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
```

- [ ] **Step 4: Run to verify** → 3/3 PASS. Full build 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/MutexContestedWatcher.cs src/ROROROblox.Tests/MutexContestedWatcherTests.cs
git commit -m "feat(core): MutexContestedWatcher — edge-triggered runtime lock-contention probe"
```

---

## Task 5: Copy — LeftoverSummary formatter + MultiInstanceCopy constants

**Files:**
- Create: `src/ROROROblox.App/ViewModels/LeftoverSummary.cs`, `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs`
- Test: `src/ROROROblox.Tests/LeftoverSummaryTests.cs`

**Interfaces:**
- Produces: `static string LeftoverSummary.Format(int windowless, int windowed)`; `MultiInstanceCopy` constants (`ContestedBanner`, `StillLocked`) consumed by the modal (Task 6) and the banner (Task 8).

- [ ] **Step 1: Write the failing tests**

```csharp
// src/ROROROblox.Tests/LeftoverSummaryTests.cs
using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

public class LeftoverSummaryTests
{
    [Fact]
    public void Format_BothKinds_ReadsSplitThenReassurance()
    {
        Assert.Equal(
            "Found 3 leftover Roblox processes with no window, and 2 open Roblox windows from before. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 3, windowed: 2));
    }

    [Fact]
    public void Format_WindowlessOnly_OmitsWindowedClause()
    {
        Assert.Equal(
            "Found 3 leftover Roblox processes with no window. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 3, windowed: 0));
    }

    [Fact]
    public void Format_WindowedOnly_OmitsWindowlessClause()
    {
        Assert.Equal(
            "Found 2 open Roblox windows from before. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 0, windowed: 2));
    }

    [Fact]
    public void Format_Singulars()
    {
        Assert.Equal(
            "Found 1 leftover Roblox process with no window, and 1 open Roblox window from before. Multi-instance is fine — RoRoRo has the lock.",
            LeftoverSummary.Format(windowless: 1, windowed: 1));
    }
}
```

- [ ] **Step 2: Run to verify it fails** → FAIL.

- [ ] **Step 3: Implement**

`src/ROROROblox.App/ViewModels/LeftoverSummary.cs`:

```csharp
namespace ROROROblox.App.ViewModels;

/// <summary>Formats the LEFTOVER modal's split-aware body: windowless orphans (safe to clean) vs
/// open Roblox windows (live games). Always ends with the reassurance that multi-instance is fine
/// (RoRoRo already holds the lock in the Leftover case).</summary>
public static class LeftoverSummary
{
    public static string Format(int windowless, int windowed)
    {
        var clauses = new System.Collections.Generic.List<string>(2);
        if (windowless > 0)
            clauses.Add($"{windowless} leftover Roblox process{(windowless == 1 ? "" : "es")} with no window");
        if (windowed > 0)
            clauses.Add($"{windowed} open Roblox window{(windowed == 1 ? "" : "s")} from before");

        var found = string.Join(", and ", clauses);
        return $"Found {found}. Multi-instance is fine — RoRoRo has the lock.";
    }
}
```

`src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs`:

```csharp
namespace ROROROblox.App.ViewModels;

/// <summary>User-facing copy for the multi-instance lock states (spec §5/§6). Centralized so the
/// startup modal (Task 6) and the runtime banner (Task 8) share exact strings.</summary>
public static class MultiInstanceCopy
{
    /// <summary>Runtime banner shown when Roblox holds the lock post-startup.</summary>
    public const string ContestedBanner =
        "Roblox has the multi-instance lock — it's probably running in your system tray.";

    /// <summary>Tick shown in the BLOCKED modal after a Retry that still failed.</summary>
    public const string StillLocked = "Still locked — Roblox is still running.";
}
```

- [ ] **Step 4: Run to verify** → 4/4 PASS. Full build 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ViewModels/LeftoverSummary.cs src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs src/ROROROblox.Tests/LeftoverSummaryTests.cs
git commit -m "feat(app): leftover-summary formatter + multi-instance copy constants"
```

---

## Task 6: Rework the BLOCKED modal + add the LEFTOVER modal

UI task — thin windows over the Task 3/5 logic (the codebase pattern: modals are thin code-behind, logic lives in tested classes). No new unit test; verified by build + the manual smoke in Task 8's final checklist. Follows the 626 palette + existing modal chrome verbatim.

**Files:**
- Modify: `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml` (+ `.xaml.cs`)
- Create: `src/ROROROblox.App/Modals/LeftoverProcessesWindow.xaml` (+ `.xaml.cs`)

**Interfaces:**
- Produces:
  - `RobloxAlreadyRunningWindow(Func<bool> onCloseForMe, Func<bool> onRetry)` — BLOCKED modal. Each callback returns `true` if the mutex is now held (recovery succeeded). On success the window sets `DialogResult = true` and closes; on failure it shows the still-locked tick. `Quit` sets `DialogResult = false`.
  - `LeftoverProcessesWindow(int windowless, int windowed)` — info modal; `CleanUpRequested` bool (true if the user chose "Clean up + continue"). Always dismissable to continue.

- [ ] **Step 1: Rework `RobloxAlreadyRunningWindow.xaml`** — replace the restart-script steps + single button. New body/steps/buttons (copy verbatim from spec §5):

```xml
        <!-- Heading -->
        <TextBlock Grid.Row="0" Text="Roblox is already running"
                   FontSize="18" FontWeight="Bold" Foreground="#17D4FA" />

        <!-- Body: the tray-residence explanation -->
        <TextBlock Grid.Row="1"
                   Text="RoRoRo needs the multi-instance lock, but Roblox is holding it — it may be running with no window at all, hidden in your system tray."
                   TextWrapping="Wrap" FontSize="12" Foreground="#FFFFFF" Opacity="0.85"
                   Margin="0,12,0,0" />

        <!-- Steps: how to quit from the tray -->
        <StackPanel Grid.Row="2" Orientation="Vertical" Margin="0,12,0,0">
            <TextBlock FontSize="12" Margin="0,2,0,0">
                <Run Text="1." Foreground="#F22F89" FontWeight="Bold" />
                <Run Text=" Click the ^ near the clock (bottom-right)" Foreground="#FFFFFF" />
            </TextBlock>
            <TextBlock FontSize="12" Margin="0,2,0,0">
                <Run Text="2." Foreground="#F22F89" FontWeight="Bold" />
                <Run Text=" Right-click the Roblox icon" Foreground="#FFFFFF" />
            </TextBlock>
            <TextBlock FontSize="12" Margin="0,2,0,0">
                <Run Text="3." Foreground="#F22F89" FontWeight="Bold" />
                <Run Text=" Choose Quit Roblox — then hit Retry" Foreground="#FFFFFF" />
            </TextBlock>
        </StackPanel>

        <!-- Start-with-Windows note -->
        <TextBlock Grid.Row="3"
                   Text="Roblox can start itself with Windows. You can turn that off in Roblox's tray settings — but you don't have to; RoRoRo will catch it here either way."
                   TextWrapping="Wrap" FontSize="11" Foreground="#8A93A0" Margin="0,12,0,0" />

        <!-- Still-locked tick (hidden until a failed retry) -->
        <TextBlock Grid.Row="4" x:Name="StillLockedTick"
                   FontFamily="JetBrains Mono, Cascadia Mono, Consolas" FontSize="10"
                   Foreground="#F1B232" Margin="0,12,0,0" Visibility="Collapsed" />

        <!-- Three actions -->
        <StackPanel Grid.Row="5" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Quit RoRoRo" Padding="16,8" IsCancel="True"
                    Background="#22314A" Foreground="#FFFFFF" BorderThickness="0"
                    Margin="0,0,8,0" Click="OnQuitClick" />
            <Button Content="Retry" Padding="16,8"
                    Background="#22314A" Foreground="#FFFFFF" BorderThickness="0"
                    Margin="0,0,8,0" Click="OnRetryClick" />
            <Button Content="Close Roblox for me" Padding="16,8" IsDefault="True"
                    Background="#17D4FA" Foreground="#0F1F31" BorderThickness="0"
                    FontWeight="Bold" Click="OnCloseForMeClick" />
        </StackPanel>
```

Adjust the `Grid.RowDefinitions` to 6 rows (heading, body, steps, note, tick, buttons) — the existing file has 6 rows already (`Auto ×4, *, Auto`); repurpose row 3 for the note and row 4 (the `*` spacer) for the tick, or add a row. Match the existing row layout; keep `Height="360"` or grow to fit. Remove the old `MUTEX HOLD REQUIRES CLEAN START` line and the `Multi-instance launches won't work…` body.

- [ ] **Step 2: Rework `RobloxAlreadyRunningWindow.xaml.cs`** — callbacks + tick:

```csharp
using System;
using System.Windows;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>BLOCKED modal — shown when the mutex is held by someone else at startup. Offers
/// Close-Roblox-for-me and Retry (both re-acquire in place; success closes with DialogResult=true)
/// plus Quit RoRoRo (DialogResult=false). Never asks the user to restart RoRoRo.</summary>
internal partial class RobloxAlreadyRunningWindow : Window
{
    private readonly Func<bool> _onCloseForMe;
    private readonly Func<bool> _onRetry;

    public RobloxAlreadyRunningWindow(Func<bool> onCloseForMe, Func<bool> onRetry)
    {
        _onCloseForMe = onCloseForMe;
        _onRetry = onRetry;
        InitializeComponent();
    }

    private void OnCloseForMeClick(object sender, RoutedEventArgs e) => TryRecover(_onCloseForMe);
    private void OnRetryClick(object sender, RoutedEventArgs e) => TryRecover(_onRetry);

    private void TryRecover(Func<bool> recover)
    {
        if (recover())
        {
            DialogResult = true; // mutex now held — proceed with startup
            Close();
        }
        else
        {
            StillLockedTick.Text = MultiInstanceCopy.StillLocked;
            StillLockedTick.Visibility = Visibility.Visible;
        }
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Create `LeftoverProcessesWindow.xaml`** — model on `StopAllConfirmWindow.xaml` chrome (info variant, 2 buttons):

```xml
<Window x:Class="ROROROblox.App.Modals.LeftoverProcessesWindow"
        x:ClassModifier="internal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Leftover Roblox processes"
        Height="240" Width="520"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        Icon="/ROROROblox.App;component/Tray/Resources/tray-on.ico"
        Background="{DynamicResource BgBrush}">
    <Grid Margin="32">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Grid.Row="0" Text="Leftover Roblox processes"
                   FontSize="18" FontWeight="Bold" Foreground="#17D4FA" />
        <TextBlock Grid.Row="1" x:Name="BodyText" TextWrapping="Wrap"
                   FontSize="12" Foreground="#FFFFFF" Opacity="0.85" Margin="0,12,0,0" />
        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Clean up + continue" Padding="16,8"
                    Background="#22314A" Foreground="#FFFFFF" BorderThickness="0"
                    Margin="0,0,8,0" Click="OnCleanUpClick" />
            <Button Content="Continue" Padding="16,8" IsDefault="True" IsCancel="True"
                    Background="#17D4FA" Foreground="#0F1F31" BorderThickness="0"
                    FontWeight="Bold" Click="OnContinueClick" />
        </StackPanel>
    </Grid>
</Window>
```

`LeftoverProcessesWindow.xaml.cs`:

```csharp
using System.Windows;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Modals;

/// <summary>Informational (non-blocking) modal: mutex acquired but leftover Roblox processes
/// exist. Continue is the default; Clean up + continue sets CleanUpRequested so the caller runs
/// the stop-all teardown (with the unsaved-state confirm when windowed clients exist).</summary>
internal partial class LeftoverProcessesWindow : Window
{
    public bool CleanUpRequested { get; private set; }

    public LeftoverProcessesWindow(int windowless, int windowed)
    {
        InitializeComponent();
        BodyText.Text = LeftoverSummary.Format(windowless, windowed);
    }

    private void OnCleanUpClick(object sender, RoutedEventArgs e)
    {
        CleanUpRequested = true;
        DialogResult = true;
        Close();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        CleanUpRequested = false;
        DialogResult = true;
        Close();
    }
}
```

- [ ] **Step 4: Build** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors (XAML compiles; the reworked ctor now requires callers to pass callbacks — App is updated in Task 7, so the App project may not compile YET if it still calls `new RobloxAlreadyRunningWindow()`). **To keep this task's commit green, temporarily update the single App call site** in `App.xaml.cs` (the old gate block) to a compiling stub — e.g. comment out the old `new RobloxAlreadyRunningWindow()` block behind the still-present `ShouldProceed` path and pass no-op callbacks, OR do the minimal signature fix. If a clean green commit isn't achievable without the Task 7 reorder, fold Task 7 into this commit. Prefer: make the old block call `new RobloxAlreadyRunningWindow(() => false, () => false)` so it compiles and behaves as before (Quit-only in practice), then Task 7 replaces the whole block.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml.cs src/ROROROblox.App/Modals/LeftoverProcessesWindow.xaml src/ROROROblox.App/Modals/LeftoverProcessesWindow.xaml.cs src/ROROROblox.App/App.xaml.cs
git commit -m "feat(app): rework BLOCKED modal (close-for-me/retry/quit) + add LEFTOVER info modal"
```

---

## Task 7: App.OnStartup reorder + TryRecoverMultiInstance + DI

Composition/glue — no new unit test; verified by build + the full existing suite staying green + manual smoke. This is where acquire-first lands and `ShouldProceed` is removed.

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs`
- Modify: `src/ROROROblox.Core/Diagnostics/StartupGate.cs` (remove `ShouldProceed`)
- Modify: `src/ROROROblox.Tests/StartupGateTests.cs` (remove the now-obsolete `ShouldProceed` tests — `Evaluate` is covered by `StartupGateEvaluateTests`)

**Interfaces:**
- Consumes: `StartupGate.Evaluate` (Task 3), `IMutexHolder.Acquire` + `IsHeldElsewhere` (Task 1), `IRobloxInstanceStopper.StopAll`, both modals (Task 6), `StopAllConfirmWindow`.
- Produces: `bool TryRecoverMultiInstance(bool closeRobloxFirst)` (App method) — optionally StopAll (with the unsaved-state confirm when windowed clients exist), then `Acquire()`; returns whether the mutex is now held. Reused by the runtime banner (Task 8).

- [ ] **Step 1: Reorder `OnStartup`.** Replace the current gate block + the later `mutex.Acquire()` with acquire-first. The new sequence (after `ThemeService.ApplyAtStartup`):

```csharp
        // Resolve the singleton mutex NAME first (unchanged chain), THEN acquire, THEN gate on
        // the result. Acquire-first: the gate's verdict is guaranteed true at the moment we
        // proceed — no check-then-lose-the-lock race.
        var nameSource = MutexNameSource.Default;
        try
        {
            var compat = _services.GetRequiredService<IRobloxCompatChecker>();
            var resolved = await compat.ResolveMutexNameAsync();
            _services.GetRequiredService<ResolvedMutexName>().Value = resolved.Name;
            nameSource = resolved.Source;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mutex-name resolve threw; binding the hardcoded default.");
        }

        var mutex = _services.GetRequiredService<IMutexHolder>();
        var gate = _services.GetRequiredService<StartupGate>();

        var acquired = mutex.Acquire();
        var verdict = gate.Evaluate(acquired);

        if (verdict is StartupGateResult.Blocked)
        {
            // Roblox holds the lock — offer in-place recovery. If recovery succeeds the modal
            // returns true and we continue holding the mutex; otherwise the user quit.
            var modal = new Modals.RobloxAlreadyRunningWindow(
                onCloseForMe: () => TryRecoverMultiInstance(closeRobloxFirst: true),
                onRetry: () => TryRecoverMultiInstance(closeRobloxFirst: false));
            var recovered = modal.ShowDialog() == true;
            if (!recovered)
            {
                Shutdown(0);
                return;
            }
            acquired = true; // recovery acquired it
        }
        else if (verdict is StartupGateResult.Leftover leftover)
        {
            var info = new Modals.LeftoverProcessesWindow(leftover.Windowless, leftover.Windowed);
            info.ShowDialog();
            if (info.CleanUpRequested)
            {
                CleanUpLeftoverRoblox(leftover.Windowed > 0);
            }
            // mutex already held — proceed regardless
        }

        var tray = _services.GetRequiredService<ITrayService>();
        var mainWindow = _services.GetRequiredService<MainWindow>();

        WireTrayEvents(tray, mutex, mainWindow);
        WireMainViewModelEvents(mainWindow);
        WireMutexLost(mutex, tray);
        WireMainAvatarTrayPainter();
        WireRobloxWindowDecorator();
        WirePluginEventBus();
        WireActivityMonitor();
        WireContestedWatcher(mainWindow); // Task 8
        StartPluginHost();
        await InitializeIdleSettingsAsync();

        _log.LogInformation(
            "Startup mutex: name={Name}, source={Source}, acquired={Acquired}.",
            mutex.MutexName, nameSource, acquired);
        tray.UpdateStatus(acquired ? MultiInstanceState.On : MultiInstanceState.Error);

        tray.Show();
        _singleInstance.StartListening(mainWindow);
        mainWindow.Show();
```

(Note: the old flow resolved name → tray → mutex → wire → acquire. The reorder moves `Acquire` up. Keep all the `Wire*` calls; add `WireContestedWatcher` — defined in Task 8. If Task 8 isn't done yet, stub `WireContestedWatcher` as an empty method so this commit builds, and Task 8 fills it in.)

- [ ] **Step 2: Add `TryRecoverMultiInstance` + `CleanUpLeftoverRoblox`** to `App.xaml.cs`:

```csharp
    /// <summary>Shared multi-instance recovery: optionally close all Roblox first (with the
    /// unsaved-state confirm when windowed clients exist), then (re-)acquire the mutex. Returns
    /// whether RoRoRo now holds the lock. Used by the BLOCKED startup modal and the runtime
    /// banner. Marshals nothing — callers invoke on the UI thread.</summary>
    internal bool TryRecoverMultiInstance(bool closeRobloxFirst)
    {
        if (_services is null) return false;
        var mutex = _services.GetRequiredService<IMutexHolder>();
        if (mutex.IsHeld) return true;

        if (closeRobloxFirst)
        {
            var probe = _services.GetRequiredService<IRobloxRunningProbe>();
            var players = probe.GetRunningPlayers();
            var windowed = players.Count(p => p.HasWindow);
            if (windowed > 0)
            {
                // Live game windows among the processes — confirm before killing.
                var confirm = new Modals.StopAllConfirmWindow(players.Count);
                if (confirm.ShowDialog() != true) return mutex.IsHeld; // user cancelled
            }
            try { _services.GetRequiredService<IRobloxInstanceStopper>().StopAll(); }
            catch (Exception ex) { _log?.LogWarning(ex, "Close-for-me StopAll failed; retrying acquire anyway."); }
        }

        var acquired = mutex.Acquire();
        var tray = _services.GetRequiredService<ITrayService>();
        tray.UpdateStatus(acquired ? MultiInstanceState.On : MultiInstanceState.Error);
        TryRaiseMutexBusEvent(acquired ? "On" : "Error");
        return acquired;
    }

    /// <summary>LEFTOVER "Clean up + continue": stop leftover clients, with the unsaved-state
    /// confirm only when windowed clients exist. The mutex is already held here.</summary>
    private void CleanUpLeftoverRoblox(bool hasWindowedClients)
    {
        if (_services is null) return;
        try
        {
            if (hasWindowedClients)
            {
                var count = _services.GetRequiredService<IRobloxRunningProbe>().GetRunningPlayerPids().Count;
                var confirm = new Modals.StopAllConfirmWindow(count);
                if (confirm.ShowDialog() != true) return;
            }
            _services.GetRequiredService<IRobloxInstanceStopper>().StopAll();
        }
        catch (Exception ex) { _log?.LogWarning(ex, "Leftover clean-up failed."); }
    }
```

(Add `using System.Linq;` and `using ROROROblox.Core.Diagnostics;` to `App.xaml.cs` if not present.)

- [ ] **Step 3: Register the watcher in DI** (`ConfigureServices`, next to the other Diagnostics singletons):

```csharp
        services.AddSingleton<MutexContestedWatcher>(sp =>
            new MutexContestedWatcher(sp.GetRequiredService<IMutexHolder>()));
```

- [ ] **Step 4: Remove `ShouldProceed`** from `StartupGate.cs` and delete the obsolete `StartupGateTests` cases that call it (keep the file if it has other tests; otherwise remove it — `StartupGateEvaluateTests` is the coverage now). The `FakeRobloxRunningProbe` in `StartupGateTests.cs` may still be referenced by other tests; if `StartupGateTests.cs` becomes empty, delete it.

- [ ] **Step 5: Build + full suite + smoke**

Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors.
Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/` → green (no regressions).
Manual smoke (deferred to Task 8's checklist for the full script): confirm the app still launches clean when no Roblox is running.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/App.xaml.cs src/ROROROblox.Core/Diagnostics/StartupGate.cs src/ROROROblox.Tests/StartupGateTests.cs
git commit -m "feat(app): acquire-first OnStartup + TryRecoverMultiInstance shared recovery"
```

---

## Task 8: Runtime contested banner (MainViewModel + MainWindow) + watcher wiring + final verify

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`, `src/ROROROblox.App/MainWindow.xaml`, `src/ROROROblox.App/App.xaml.cs`
- Test: `src/ROROROblox.Tests/MainViewModelTests.cs` (add)

**Interfaces:**
- Consumes: `MutexContestedWatcher.ContestedChanged` (Task 4), `MultiInstanceCopy.ContestedBanner` (Task 5), `TryRecoverMultiInstance` (Task 7).
- Produces on `MainViewModel`: `string ContestedBannerText` (empty when not contested), `void SetContested(bool)`, `event Action? RequestCloseRobloxForMe`, `event Action? RequestRetryMutex`, and two `ICommand`s (`CloseRobloxForMeCommand`, `RetryMutexCommand`) that raise those events. App subscribes the events → `TryRecoverMultiInstance`.

- [ ] **Step 1: Write the failing test**

```csharp
// add to src/ROROROblox.Tests/MainViewModelTests.cs
    [Fact]
    public void SetContested_TogglesBannerText()
    {
        var (vm, _, _, path) = Build();  // reuse the existing MainViewModelTests harness
        try
        {
            Assert.Equal("", vm.ContestedBannerText);

            vm.SetContested(true);
            Assert.Equal(
                "Roblox has the multi-instance lock — it's probably running in your system tray.",
                vm.ContestedBannerText);

            vm.SetContested(false);
            Assert.Equal("", vm.ContestedBannerText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CloseRobloxForMeCommand_RaisesRequestEvent()
    {
        var (vm, _, _, path) = Build();
        try
        {
            var raised = false;
            vm.RequestCloseRobloxForMe += () => raised = true;
            vm.CloseRobloxForMeCommand.Execute(null);
            Assert.True(raised);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

(Match the real `MainViewModelTests` harness/`Build()` shape from the file; if the harness differs, adapt the construction — the two assertions are what matter.)

- [ ] **Step 2: Run to verify it fails** → FAIL.

- [ ] **Step 3: Implement on `MainViewModel.cs`** (mirror the `IdleSummaryText` property + the existing `RelayCommand` + event pattern, e.g. `RequestOpenPlugins`):

```csharp
    private string _contestedBannerText = string.Empty;

    /// <summary>Runtime banner text — non-empty only when Roblox holds the multi-instance lock
    /// and RoRoRo doesn't. Empty collapses the strip (mirrors StatusBanner/IdleSummaryText).</summary>
    public string ContestedBannerText
    {
        get => _contestedBannerText;
        private set => SetField(ref _contestedBannerText, value);
    }

    public void SetContested(bool contested)
        => ContestedBannerText = contested ? ViewModels.MultiInstanceCopy.ContestedBanner : string.Empty;

    public event Action? RequestCloseRobloxForMe;
    public event Action? RequestRetryMutex;

    public ICommand CloseRobloxForMeCommand => _closeRobloxForMeCommand ??=
        new RelayCommand(_ => RequestCloseRobloxForMe?.Invoke());
    private RelayCommand? _closeRobloxForMeCommand;

    public ICommand RetryMutexCommand => _retryMutexCommand ??=
        new RelayCommand(_ => RequestRetryMutex?.Invoke());
    private RelayCommand? _retryMutexCommand;
```

(Use the codebase's real `RelayCommand` shape — match how `LaunchAllCommand` etc. are declared. If the namespace of `MultiInstanceCopy` is `ROROROblox.App.ViewModels` (same as the VM), drop the qualifier.)

- [ ] **Step 4: Add the banner to `MainWindow.xaml`** — a third child in the shared status Border's `StackPanel` (avoids a grid-row renumber), with the two action buttons, visible when `ContestedBannerText` is non-empty:

```xml
                <!-- Runtime contested-lock banner (tray-residence): shown when Roblox holds the
                     multi-instance lock post-startup. Same recovery actions as the startup modal. -->
                <StackPanel Orientation="Vertical" Margin="0,4,0,0"
                            Visibility="{Binding ContestedBannerText, Converter={StaticResource StringToVisibilityConverter}}">
                    <TextBlock Text="{Binding ContestedBannerText}"
                               FontSize="11" TextWrapping="Wrap" Foreground="#F1B232" />
                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                        <Button Content="Close Roblox for me" Command="{Binding CloseRobloxForMeCommand}"
                                Padding="10,4" Margin="0,0,8,0"
                                Background="#17D4FA" Foreground="#0F1F31" BorderThickness="0" FontSize="11" />
                        <Button Content="Retry" Command="{Binding RetryMutexCommand}"
                                Padding="10,4"
                                Background="#22314A" Foreground="#FFFFFF" BorderThickness="0" FontSize="11" />
                    </StackPanel>
                </StackPanel>
```

Update the enclosing Border's collapse `MultiDataTrigger` to also require `ContestedBannerText` empty before the whole strip collapses (add a third `Condition Binding="{Binding ContestedBannerText}" Value=""`).

- [ ] **Step 5: Wire the watcher in `App.xaml.cs`** — implement `WireContestedWatcher` (the stub from Task 7):

```csharp
    private void WireContestedWatcher(MainWindow mainWindow)
    {
        if (_services is null) return;
        var watcher = _services.GetRequiredService<MutexContestedWatcher>();
        var vm = _services.GetRequiredService<MainViewModel>();

        watcher.ContestedChanged += (_, contested) =>
            Dispatcher.Invoke(() => vm.SetContested(contested));

        vm.RequestCloseRobloxForMe += () =>
        {
            if (TryRecoverMultiInstance(closeRobloxFirst: true)) vm.SetContested(false);
        };
        vm.RequestRetryMutex += () =>
        {
            if (TryRecoverMultiInstance(closeRobloxFirst: false)) vm.SetContested(false);
        };

        watcher.Start();
    }
```

Ensure `OnExit` disposes the watcher (`_services?.GetService<MutexContestedWatcher>()?.Dispose()` or add it to the existing teardown alongside the other disposables).

- [ ] **Step 6: Run tests + build + full manual smoke**

Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/` → all green.
Run: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors.
**Manual smoke (spec §8, for the user — needs a live desktop + real Roblox 0.727):**
1. Start Roblox, close its window so it tray-resides; launch RoRoRo → BLOCKED modal → quit Roblox from the tray → **Retry** → RoRoRo continues, no restart.
2. Same, but **Close Roblox for me** → auto-continues.
3. RoRoRo running, multi-instance toggled Off → start Roblox → banner appears ≤5s → **Close Roblox for me** → state On, banner clears.
4. Leave orphan + windowed clients from a prior session → relaunch → LEFTOVER modal with the correct split → both buttons.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/MainWindow.xaml src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/MainViewModelTests.cs
git commit -m "feat(app): runtime contested-lock banner + MutexContestedWatcher wiring"
```

---

## Final verification (after all tasks)

- [ ] **Whole solution build:** `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` — green.
- [ ] **Whole solution tests:** `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test ROROROblox.slnx` — green.
- [ ] **Local-path guard:** `SCAN_ALL=1 bash .claude/hooks/pre-commit-local-path-guard.sh` — clean.
- [ ] **Restart-script gone:** grep the diff for "Close RoRoRo" / "Re-open RoRoRo" — none remain in the modal.
- [ ] **Manual smoke** — the four scenarios above, on a clean Win11 box with Roblox 0.727.

---

## Self-review notes (author)

**Spec coverage:** §3 acquire-first → Tasks 1, 3, 7. §4 gate states + windowed/windowless split → Tasks 2, 3. §5 modals + copy → Tasks 5, 6, 7 (Close-for-me confirm gating in `TryRecoverMultiInstance`). §6 watcher + banner → Tasks 4, 5, 8. §7 error handling (fail-open, StopAll partial, watcher exception-safe) → Tasks 3, 4, 7. §8 testing → per-task tests + final smoke. §9 decisions honored. §10 deferred items not built.

**Type consistency:** `StartupGateResult` (Clean/Leftover(int,int)/Blocked) defined Task 3, consumed Task 7. `RobloxProcessInfo(int Pid, bool HasWindow)` defined Task 2, consumed Tasks 3, 7. `IsHeldElsewhere()` defined Task 1, consumed Task 4. `MutexContestedWatcher.ContestedChanged` (Task 4) consumed Task 8. `MultiInstanceCopy.ContestedBanner`/`StillLocked` (Task 5) consumed Tasks 6, 8. `TryRecoverMultiInstance(bool)` (Task 7) consumed Task 8. `LeftoverSummary.Format(int,int)` (Task 5) consumed Task 6.

**Green-commit discipline:** `ShouldProceed` survives (Tasks 1-6) until Task 7 removes it with the App reorder, so every commit builds. Task 6 flags the modal-ctor signature change and keeps the App call site compiling with a temporary `(() => false, () => false)` until Task 7. `WireContestedWatcher` is stubbed in Task 7 and filled in Task 8.

**Adaptation points flagged inline:** the CsWin32 `OpenMutex` overload shape (Task 1), the real `MainViewModelTests` harness/`Build()` (Task 8), the real `RelayCommand` declaration style (Task 8), and the exact modal row-definition layout (Task 6).
