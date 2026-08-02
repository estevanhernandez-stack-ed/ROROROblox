# Condition-Based Launch Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop losing per-account launch settings when accounts launch close together, by replacing a fixed post-launch delay with a wait for the launched client to actually appear.

**Architecture:** `RobloxLauncher` already serializes launches behind a `SemaphoreSlim` and then holds for a fixed 250 ms so the new client can read its settings before the next write. That hold is anchored to `Process.Start` returning from a `roblox-player:` protocol-handler invocation — which happens before `RobloxPlayerBeta` exists at all. This replaces the fixed delay with condition-based waiting: snapshot running Roblox PIDs before launching, then poll until a PID appears that was not in the snapshot, then wait a short settle grace. Bounded by a timeout that degrades to today's behaviour.

**Tech Stack:** .NET 10 / C# 14, xUnit, `TimeProvider` for test-controllable delays.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-08-01-launch-gate-condition-based-design.md`. Read it first. Banner-correct the spec on drift; never rewrite it top-to-bottom.
- **Build `dotnet build ROROROblox.slnx`; test `dotnet test ROROROblox.slnx`.** `.slnx` is canonical. A stray `ROROROblox.sln` is gitignored, missing the PluginTestHarness project, and must never be built. Bare `dotnet build` errors MSB1011 while both exist.
- **Close any running `ROROROblox.App` before building** — it locks `ROROROblox.Core.dll` (MSB3027). Report BLOCKED rather than killing it.
- **No test may sleep in real time.** All delays route through the injected `TimeProvider`. A race test that sleeps is flaky and worthless.
- **No behaviour change when the probe is absent.** Existing constructor call sites and tests that build `RobloxLauncher` without a probe must keep today's exact semantics.
- **Wait only on success.** `Failed`, `CookieExpired`, and `Limited` release the gate immediately.
- Pre-commit hooks `secret-scan` and `local-path-guard` must pass. No hardcoded user-profile paths in committed source — the `local-path-guard` hook rejects them and cannot tell a prohibition from a violation, so do not quote one even as an example.
- Conventional commits. Do not push; do not open a PR.

---

## File Structure

**Modify:**

| File | Change |
| --- | --- |
| `src/ROROROblox.Core/RobloxLauncher.cs` | Optional `IRobloxRunningProbe` ctor param; new constants; `WaitForNewClientAsync`; both launch sites call it instead of the fixed delay |
| `src/ROROROblox.App/App.xaml.cs` | Pass the already-registered `IRobloxRunningProbe` into the launcher registration |

**Create:**

| File | Responsibility |
| --- | --- |
| `src/ROROROblox.Tests/RobloxLauncherGateTests.cs` | Pure `WaitForNewClientAsync` tests. Self-contained — needs only a fake probe and a fake clock, no launcher harness. |

**Extend (do NOT duplicate its fakes):**

| File | Change |
| --- | --- |
| `src/ROROROblox.Tests/RobloxLauncherTests.cs` | Tests that need a real launcher. Its fakes (`StubRobloxApi`, `InMemoryAppSettings`, `RecordingProcessStarter`, `RecordingWriter`) and its `CreateLauncher(...)` helper are **private to that file**, so tests needing them must live there. Note the existing `LaunchAsync_TwoConcurrentCalls_AreSerialized` already asserts write ORDER — Task 3 extends that idea to assert the client appeared *between* the writes. |

**Two environment facts, verified — do not re-derive:**

- `ROROROblox.Core.csproj:14` already has `<InternalsVisibleTo Include="ROROROblox.Tests" />`, so `internal` constants and `internal static WaitForNewClientAsync` **are** visible to tests. No csproj change needed for visibility.
- `Microsoft.Extensions.TimeProvider.Testing` is **NOT** referenced by the test project (it has only xunit, Test.Sdk, coverlet, xunit.runner.visualstudio). `FakeTimeProvider` is therefore unavailable until Task 1 adds it.

**Existing signatures this plan builds on** (do not re-derive):

```csharp
// ROROROblox.Core.Diagnostics
public interface IRobloxRunningProbe
{
    IReadOnlyList<int> GetRunningPlayerPids();
    IReadOnlyList<RobloxProcessInfo> GetRunningPlayers();
}

// ROROROblox.Core
public abstract record LaunchResult
{
    public sealed record Started(int Pid, DateTimeOffset LaunchedAtUtc) : LaunchResult;
    public sealed record CookieExpired : LaunchResult;
    public sealed record Limited : LaunchResult;
    public sealed record Failed(string Message) : LaunchResult;
}

// RobloxLauncher's full ctor, as it exists today
public RobloxLauncher(
    IRobloxApi api, IAppSettings settings, IProcessStarter processStarter,
    TimeProvider timeProvider, Func<long> browserTrackerIdFactory,
    IFavoriteGameStore? favorites = null,
    IClientAppSettingsWriter? clientAppSettings = null,
    IGlobalBasicSettingsWriter? globalBasicSettings = null)
```

---

## Task 1: The gate itself

**Files:**
- Modify: `src/ROROROblox.Core/RobloxLauncher.cs` (constants near line 25-26; both launch paths at ~line 102 and ~line 235)
- Test: `src/ROROROblox.Tests/RobloxLauncherGateTests.cs` (create)

**Interfaces:**
- Consumes: `IRobloxRunningProbe.GetRunningPlayerPids()`; the existing `TimeProvider _timeProvider` field.
- Produces: `RobloxLauncher` gains a trailing optional ctor parameter `IRobloxRunningProbe? runningProbe = null` on **both** constructor overloads. No public method signatures change.

- [ ] **Step 0: Add the fake-clock package**

`FakeTimeProvider` is not currently available (verified — the test project references only xunit, Test.Sdk, coverlet, xunit.runner.visualstudio). Add to `src/ROROROblox.Tests/ROROROblox.Tests.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="9.0.0" />
```

Then `dotnet restore ROROROblox.slnx`. If that version does not resolve against the pinned SDK, take the newest that does and note the version in your report. **Do not hand-roll a fake `TimeProvider`** — implementing `CreateTimer` correctly is fiddly, and getting it subtly wrong produces tests that pass for the wrong reason.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/RobloxLauncherGateTests.cs`. These test `WaitForNewClientAsync` directly and need no launcher harness.

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The gate exists so a launched client finishes reading its per-account settings before the
/// NEXT launch overwrites the shared settings files. The old fixed 250ms hold was measured from
/// Process.Start returning on a roblox-player: URI — i.e. from Windows accepting the protocol
/// invocation, BEFORE RobloxPlayerBeta exists. Observed live 2026-08-01: two accounts launched
/// ~1s apart, and the first ran with the second's FPS cap.
/// </summary>
public class RobloxLauncherGateTests
{
    /// <summary>Returns a scripted sequence of pid snapshots, one per call.</summary>
    private sealed class ScriptedProbe : IRobloxRunningProbe
    {
        private readonly Queue<int[]> _snapshots;
        public int Calls { get; private set; }
        public ScriptedProbe(params int[][] snapshots) => _snapshots = new Queue<int[]>(snapshots);
        public IReadOnlyList<int> GetRunningPlayerPids()
        {
            Calls++;
            // Last snapshot repeats forever once exhausted.
            return _snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek();
        }
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
            => throw new NotSupportedException("gate only uses GetRunningPlayerPids");
    }

    [Fact]
    public async Task NewClientAppearing_ReleasesTheGateWithoutWaitingTheFullTimeout()
    {
        var clock = new FakeTimeProvider();
        // before: {100}. Then still {100}. Then {100, 555} — the new client.
        var probe = new ScriptedProbe(new[] { 100 }, new[] { 100 }, new[] { 100, 555 });

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int> { 100 }, clock, CancellationToken.None);

        // Advance two poll intervals; the third snapshot carries the new pid.
        clock.Advance(RobloxLauncher.NewClientPollInterval);
        clock.Advance(RobloxLauncher.NewClientPollInterval);
        clock.Advance(RobloxLauncher.SettleGrace);

        var outcome = await wait;
        Assert.Equal(NewClientWaitOutcome.Detected, outcome);
    }

    [Fact]
    public async Task NoNewClientEver_ReleasesAtTheTimeoutRatherThanHanging()
    {
        var clock = new FakeTimeProvider();
        var probe = new ScriptedProbe(new[] { 100 });   // never changes

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int> { 100 }, clock, CancellationToken.None);

        clock.Advance(RobloxLauncher.NewClientWaitTimeout + TimeSpan.FromSeconds(1));

        var outcome = await wait;
        Assert.Equal(NewClientWaitOutcome.TimedOut, outcome);
    }

    [Fact]
    public async Task PreExistingPids_AreNotMistakenForTheNewClient()
    {
        var clock = new FakeTimeProvider();
        // Three windowless orphans Roblox left behind, present before AND after. No new client.
        var orphans = new[] { 14392, 20432, 48276 };
        var probe = new ScriptedProbe(orphans);

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int>(orphans), clock, CancellationToken.None);

        clock.Advance(RobloxLauncher.NewClientWaitTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(NewClientWaitOutcome.TimedOut, await wait);
    }

    [Fact]
    public async Task ProbeThrowing_DoesNotEscape_AndDegradesToTimeout()
    {
        var clock = new FakeTimeProvider();
        var probe = new ThrowingProbe();

        var wait = RobloxLauncher.WaitForNewClientAsync(
            probe, before: new HashSet<int>(), clock, CancellationToken.None);

        clock.Advance(RobloxLauncher.NewClientWaitTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(NewClientWaitOutcome.TimedOut, await wait);
    }

    private sealed class ThrowingProbe : IRobloxRunningProbe
    {
        public IReadOnlyList<int> GetRunningPlayerPids() => throw new InvalidOperationException("probe blew up");
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxLauncherGateTests"`
Expected: FAIL to compile — `WaitForNewClientAsync`, `NewClientWaitOutcome`, and the three constants do not exist. That is the correct first RED for new API.

- [ ] **Step 3: Add the constants, the outcome enum, and the wait**

In `src/ROROROblox.Core/RobloxLauncher.cs`, beside the existing `FFlagReadHold` (line ~26):

```csharp
/// <summary>How often to re-check for the launched client. Cheap — a process-name enumeration.</summary>
internal static readonly TimeSpan NewClientPollInterval = TimeSpan.FromMilliseconds(250);

/// <summary>
/// Ceiling on waiting for a launched client to appear. Covers a cold start with a bootstrapper
/// update. On expiry we release anyway and degrade to the old fixed-delay behaviour — a launch
/// that never produces a client must never hang Squad Launch.
/// </summary>
internal static readonly TimeSpan NewClientWaitTimeout = TimeSpan.FromSeconds(30);

/// <summary>
/// Breathing room after the client process appears, before the next launch is allowed to
/// overwrite the shared settings files. Still an estimate — but anchored to THE CLIENT PROCESS
/// EXISTING rather than to Windows accepting a URI. That re-anchoring is the fix; the old 250ms
/// was measured against an unbounded gap (shell -> bootstrapper -> maybe an update -> client).
/// </summary>
internal static readonly TimeSpan SettleGrace = TimeSpan.FromSeconds(1);
```

Add the outcome type at namespace scope in the same file:

```csharp
/// <summary>Why <see cref="RobloxLauncher.WaitForNewClientAsync"/> returned.</summary>
public enum NewClientWaitOutcome
{
    /// <summary>A RobloxPlayerBeta pid appeared that was not in the pre-launch snapshot.</summary>
    Detected,

    /// <summary>No new pid within the ceiling. Gate released anyway — never hang the queue.</summary>
    TimedOut,
}
```

And the wait itself, as an internal static so it is testable without constructing a launcher:

```csharp
/// <summary>
/// Hold until a RobloxPlayerBeta pid appears that was not in <paramref name="before"/>, then
/// wait <see cref="SettleGrace"/>. Bounded by <see cref="NewClientWaitTimeout"/>.
/// Probe exceptions are swallowed — a probe glitch must degrade to the timeout, never abort a launch.
/// </summary>
internal static async Task<NewClientWaitOutcome> WaitForNewClientAsync(
    IRobloxRunningProbe probe,
    IReadOnlySet<int> before,
    TimeProvider timeProvider,
    CancellationToken ct)
{
    var deadline = timeProvider.GetUtcNow() + NewClientWaitTimeout;

    while (timeProvider.GetUtcNow() < deadline)
    {
        try
        {
            foreach (var pid in probe.GetRunningPlayerPids())
            {
                if (!before.Contains(pid))
                {
                    await Task.Delay(SettleGrace, timeProvider, ct).ConfigureAwait(false);
                    return NewClientWaitOutcome.Detected;
                }
            }
        }
        catch
        {
            // Probe glitch -> treat as "not yet". Never let it escape into the launch path.
        }

        await Task.Delay(NewClientPollInterval, timeProvider, ct).ConfigureAwait(false);
    }

    return NewClientWaitOutcome.TimedOut;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxLauncherGateTests"`
Expected: PASS, 4 tests. If a test hangs, the wait is not honouring the `FakeTimeProvider` — check that **every** delay uses the `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` overload and not the two-argument one.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/RobloxLauncher.cs src/ROROROblox.Tests/RobloxLauncherGateTests.cs
git commit -m "feat(launcher): condition-based wait for a launched client to appear"
```

---

## Task 2: Use the gate in both launch paths

**Files:**
- Modify: `src/ROROROblox.Core/RobloxLauncher.cs` — both constructors, the new field, and both launch sites (~line 102 and ~line 235)
- Test: `src/ROROROblox.Tests/RobloxLauncherGateTests.cs` (extend)

**Interfaces:**
- Consumes: `WaitForNewClientAsync`, the constants, and `NewClientWaitOutcome` from Task 1.
- Produces: both `RobloxLauncher` constructors gain a trailing `IRobloxRunningProbe? runningProbe = null`. Existing positional and named call sites remain valid.

- [ ] **Step 1: Write the failing tests**

These need a real launcher, so they go in **`src/ROROROblox.Tests/RobloxLauncherTests.cs`** — its fakes are private to that file. Add a counting probe alongside the existing private fakes:

```csharp
    /// <summary>Counts probe calls and never reports a new pid, so any wait runs to its ceiling.</summary>
    private sealed class CountingProbe : IRobloxRunningProbe
    {
        public int Calls { get; private set; }
        public IReadOnlyList<int> GetRunningPlayerPids() { Calls++; return Array.Empty<int>(); }
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => Array.Empty<RobloxProcessInfo>();
    }
```

Then the two tests:

```csharp
    [Fact]
    public async Task LaunchAsync_CookieExpired_NeverWaitsForAClient()
    {
        // Only a successful launch produces a client to wait for. If a non-Started result waited,
        // a user without Roblox installed would eat the full 30s ceiling on every click.
        var api = new StubRobloxApi(_ => throw new CookieExpiredException());
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var probe = new CountingProbe();
        var launcher = new RobloxLauncher(api, settings, processStarter, runningProbe: probe);

        var result = await launcher.LaunchAsync(TestCookie);

        Assert.IsType<LaunchResult.CookieExpired>(result);
        Assert.Equal(0, probe.Calls);   // never even snapshotted, let alone waited
    }

    [Fact]
    public async Task LaunchAsync_WithoutAProbe_StillCompletes_UnchangedBehaviour()
    {
        // The no-probe path must behave exactly as it did before this change, so every existing
        // call site and test that constructs a launcher without a probe keeps working.
        var api = new StubRobloxApi(_ => "ticket");
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 4242);
        var launcher = new RobloxLauncher(api, settings, processStarter);   // no probe

        var result = await launcher.LaunchAsync(TestCookie);

        var started = Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(4242, started.Pid);
    }
```

If `StubRobloxApi`'s constructor shape differs from `_ => "ticket"`, match whatever the file actually uses — read the neighbouring tests rather than assuming.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxLauncherGateTests"`
Expected: FAIL — the launcher has no probe parameter yet.

- [ ] **Step 3: Thread the probe through both constructors**

Add `IRobloxRunningProbe? runningProbe = null` as the **last** parameter of both overloads, store it in a `private readonly IRobloxRunningProbe? _runningProbe;`, and forward it from the short overload to the full one.

- [ ] **Step 4: Replace the fixed hold at both launch sites**

Both paths currently read:

```csharp
var result = await ExecuteLaunchAsync(cookie, target, browserTrackerId).ConfigureAwait(false);
await Task.Delay(FFlagReadHold).ConfigureAwait(false);
return result;
```

Replace with, at **both** sites (~line 102 and ~line 235):

```csharp
var result = await ExecuteLaunchAsync(cookie, target, browserTrackerId).ConfigureAwait(false);

// Only a successful launch produces a client to wait for. Failed / CookieExpired / Limited
// release immediately — a user without Roblox installed must not eat the ceiling every click.
if (result is LaunchResult.Started && _runningProbe is not null)
{
    await WaitForNewClientAsync(_runningProbe, beforePids!, _timeProvider, CancellationToken.None)
        .ConfigureAwait(false);
}
else if (result is LaunchResult.Started)
{
    await Task.Delay(FFlagReadHold, _timeProvider).ConfigureAwait(false);
}

return result;
```

And capture the snapshot **before** the settings writes, near the top of the same `try` block:

```csharp
// Snapshot BEFORE writing settings and launching. Windowless orphans Roblox leaves behind on
// exit are in here, so they can never be mistaken for the client we are about to start.
IReadOnlySet<int>? beforePids = _runningProbe is null
    ? null
    : new HashSet<int>(SafeGetPids(_runningProbe));
```

Add the helper beside `WaitForNewClientAsync`:

```csharp
private static IReadOnlyList<int> SafeGetPids(IRobloxRunningProbe probe)
{
    try { return probe.GetRunningPlayerPids(); }
    catch { return Array.Empty<int>(); }
}
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS. Existing `RobloxLauncherTests` must be untouched and green — they construct the launcher without a probe, which is the unchanged-behaviour path.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/RobloxLauncher.cs src/ROROROblox.Tests/RobloxLauncherGateTests.cs
git commit -m "fix(launcher): gate on the client appearing, not on Process.Start returning"
```

---

## Task 3: Wire the probe in DI, and the end-to-end test

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs` — the `IRobloxLauncher` registration
- Test: `src/ROROROblox.Tests/RobloxLauncherGateTests.cs` (extend)

**Interfaces:**
- Consumes: the probe-aware constructors from Task 2. `IRobloxRunningProbe` is already registered in DI (used by `StartupGate`) — reuse that registration, do not add a second.

- [ ] **Step 1: Write the failing end-to-end test**

This is the test that would have caught the shipped bug. Everything else is scaffolding around it.

Goes in `RobloxLauncherTests.cs`, beside the existing `LaunchAsync_TwoConcurrentCalls_AreSerialized` — which already asserts write *order* but never checks that the first client appeared *before* the second write. That gap is the bug.

Add a probe that records interleaving against the same list the writer records into:

```csharp
    /// <summary>
    /// Reports a new pid only after <paramref name="callsBeforeAppearing"/> polls, and appends a
    /// sentinel to the shared timeline when it does. Lets a test assert that the client appeared
    /// BETWEEN the two settings writes rather than after both.
    /// </summary>
    private sealed class AppearAfterProbe(List<int> timeline, int callsBeforeAppearing) : IRobloxRunningProbe
    {
        private int _calls;
        public IReadOnlyList<int> GetRunningPlayerPids()
        {
            _calls++;
            if (_calls < callsBeforeAppearing) return Array.Empty<int>();
            if (_calls == callsBeforeAppearing) timeline.Add(-1);   // -1 == "client appeared"
            return new[] { 999 };
        }
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => Array.Empty<RobloxProcessInfo>();
    }
```

```csharp
    [Fact]
    public async Task TwoSequentialLaunches_SecondWriteHappensOnlyAfterTheFirstClientAppears()
    {
        // The shipped bug (observed 2026-08-01): account A configured Unlimited (9999) launched
        // ~1s before account B configured 20. A ran at 20, because B's write landed before A's
        // client had read the file. The old hold was 250ms measured from Process.Start returning
        // on a protocol URI — before RobloxPlayerBeta even exists.
        var timeline = new List<int>();
        var writer = new RecordingWriter(timeline);
        var probe = new AppearAfterProbe(timeline, callsBeforeAppearing: 2);
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            clientAppSettings: writer,
            runningProbe: probe);

        await launcher.LaunchAsync("cookie-a", placeUrl: null, fpsCap: 9999);
        await launcher.LaunchAsync("cookie-b", placeUrl: null, fpsCap: 20);

        // Ordering is the assertion, not merely that both values were written:
        //   9999 written -> client appeared (-1) -> 20 written
        Assert.Equal(new[] { 9999, -1, 20 }, timeline);
    }
```

`CreateLauncher` will need a `runningProbe` parameter threaded through — add it with a `null` default so its existing call sites are untouched.

**Prove it discriminates:** temporarily revert Task 2's change at one launch site back to `Task.Delay(FFlagReadHold, _timeProvider)`, confirm RED (the timeline comes back `9999, 20, -1` — the second write beat the client), restore, confirm GREEN. Report that cycle. A race test that passes against the broken code is worthless.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "TwoSequentialLaunches"`
Expected: FAIL on the ordering assertion.

- [ ] **Step 3: Register the probe on the launcher**

In `src/ROROROblox.App/App.xaml.cs`, find the `IRobloxLauncher` registration and pass the already-registered `IRobloxRunningProbe` into it. Do not register a second instance — `StartupGate` already resolves one.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS.

- [ ] **Step 5: Build and confirm the app still starts**

Run: `dotnet build ROROROblox.slnx`
Expected: SUCCESS. Do not launch the app or any Roblox client.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/RobloxLauncherGateTests.cs
git commit -m "fix(launcher): wire the running probe so per-account settings survive squad launches"
```

---

## Post-implementation

- [ ] Banner-correct `docs/superpowers/specs/2026-08-01-launch-gate-condition-based-design.md` if the constants or shape drifted. Do not rewrite it.
- [ ] Note in the report whether `Microsoft.Extensions.TimeProvider.Testing` had to be added to the test project.
- [ ] **Manual verification is required and cannot be done by an agent:** launch two accounts with different FPS caps within a second of each other and confirm each runs at its own value. This is the exact reproduction from 2026-08-01. Flag it for Este; do not attempt it.
