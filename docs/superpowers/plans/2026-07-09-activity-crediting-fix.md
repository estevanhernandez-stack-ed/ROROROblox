# GetAccountActivity Crediting Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a keep-alive plugin a truthful way to credit an account's activity: a new consent-gated `MarkAccountActive(accountId)` RPC → `ActivityMonitor.MarkActive` stamps that account directly, bypassing the foreground/global-tick heuristic that can't see plugin-directed input. Core stays observation-only; the plugin tells the host what it did.

**Architecture:** Additive proto RPC + new capability token + consent copy; `ActivityMonitor.MarkActive(Guid, DateTimeOffset)` (stamp + re-arm, reusing the existing `_records`/`ConcurrentDictionary` discipline — no new lock); a thin `IAccountActivityMarker` adapter (mirroring `ActivitySnapshotProvider`) wrapping the monitor; `PluginHostService.MarkAccountActive` handler parsing the stringified-Guid account id; one line in `RpcMethodCapabilityMap`; PluginContract NuGet bump.

**Tech Stack:** .NET 10 / C#, gRPC (Grpc.AspNetCore over named pipe), xUnit (real Kestrel+pipe integration harness + hand-rolled stubs — no Moq).

**Spec:** `docs/superpowers/specs/2026-07-09-activity-crediting-fix-design.md`. Branch: `feat/activity-credit-fix` (carries the queue-specs; rebases clean once #56 merges). **Coordinated follow-up (separate repo, NOT this plan):** ur-AFK adopts the RPC and drops its client-side workaround.

## Global Constraints

- **Build/test with the explicit dotnet host** (`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" …`) against **`ROROROblox.slnx`** only.
- **The wall holds:** nothing added may detect or interact with a challenge, hook input, or sense synthetic input. This is an *explicit plugin-declared* signal — the plugin calls the RPC; the core never infers. No `SetWindowsHookEx`, no per-window input inspection.
- **Additive contract only:** new RPC + new capability token + new proto messages. Do NOT alter existing RPCs, messages, or `contract_version` ("1.0" wire string is unchanged — the vocabulary grew, the wire protocol didn't). Existing plugins built against 0.4.0 keep working.
- **Capability-gated exactly like the input-synthesis class:** the RPC requires the new `host.commands.mark-account-active` capability; `CapabilityInterceptor` enforces it (returns `PermissionDenied` ungranted) with zero new interceptor code — only a `RpcMethodCapabilityMap` entry.
- **Account id is RoRoRo's internal Guid, stringified** (matches `ActivitySnapshotProvider`'s `.ToString()`); the handler `Guid.TryParse`s it and no-ops (not throws) on unparseable/unknown ids.
- No user-profile paths in committed files; conventional commits; hand-rolled test doubles matching the existing `ActivityMonitorTests` + `EndToEndContractTests` styles (read them before writing).

---

## File Structure

**Modified:**
- `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` — new RPC + request message.
- `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` — `<Version>` 0.4.0 → 0.5.0.
- `src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs` — `MarkActive`.
- `src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs` — `MarkActive` on the interface.
- `src/ROROROblox.App/Plugins/PluginCapability.cs` — new const + Catalog entry (consent copy).
- `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs` — new RPC→capability entry.
- `src/ROROROblox.App/Plugins/PluginHostService.cs` — handler + ctor dependency.
- `src/ROROROblox.App/App.xaml.cs` — pass the marker to `PluginHostService`.

**Created:**
- `src/ROROROblox.App/Plugins/IAccountActivityMarker.cs` + `AccountActivityMarker.cs` — thin adapter wrapping `IActivityMonitor.MarkActive` (mirrors `ActivitySnapshotProvider`).

**Tests:**
- `src/ROROROblox.Tests/ActivityMonitorTests.cs` — extend (`MarkActive`).
- `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs` — extend (granted round-trip + denied + header-path).

---

## Task 1: `ActivityMonitor.MarkActive` (Core)

**Files:**
- Modify: `src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs`, `src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs`
- Test: `src/ROROROblox.Tests/ActivityMonitorTests.cs` (extend — READ it first, match its fake-probe/clock harness)

**Interfaces:**
- Produces: `void IActivityMonitor.MarkActive(Guid accountId, DateTimeOffset nowUtc)` — stamps `LastActivityAt = nowUtc` for a tracked account and clears its `WarnLatched` (re-arm). No-op for an untracked id. Consumed by Task 3's adapter.

- [ ] **Step 1: Write the failing tests** (match the file's real harness — the extraction shows ctor takes `IForegroundWindowProbe, ISystemInputClock, IForegroundAccountResolver, IClock`; there's a fake clock already):

```csharp
    [Fact]
    public void MarkActive_StampsTrackedAccount()
    {
        var (mon, clock) = Build();               // use the file's existing builder/fakes
        var id = Guid.NewGuid();
        mon.OnAccountLaunched(id);
        clock.UtcNow = clock.UtcNow.AddMinutes(30); // age it well past threshold
        mon.MarkActive(id, clock.UtcNow);
        var snap = mon.GetSnapshot().Single(a => a.AccountId == id);
        Assert.True(snap.SinceActivity < TimeSpan.FromSeconds(1)); // freshly stamped
    }

    [Fact]
    public void MarkActive_ReArmsLatchedAccount()
    {
        var (mon, clock) = Build();
        var crossed = new List<IReadOnlyList<Guid>>();
        mon.WarnThresholdCrossed += (_, ids) => crossed.Add(ids);
        var id = Guid.NewGuid();
        mon.OnAccountLaunched(id);
        clock.UtcNow = clock.UtcNow.AddMinutes(20);
        mon.Sample();                              // latches (crossed once)
        Assert.Contains(crossed, list => list.Contains(id));
        mon.MarkActive(id, clock.UtcNow);          // re-arm: stamp + clear latch
        crossed.Clear();
        clock.UtcNow = clock.UtcNow.AddMinutes(20);
        mon.Sample();                              // must be able to cross AGAIN
        Assert.Contains(crossed, list => list.Contains(id));
    }

    [Fact]
    public void MarkActive_UntrackedId_NoOp()
    {
        var (mon, clock) = Build();
        mon.MarkActive(Guid.NewGuid(), clock.UtcNow); // must not throw
        Assert.Empty(mon.GetSnapshot());
    }
```

(If the file has no `Build()` helper, construct the monitor inline the way its existing tests do — the point is a real `ActivityMonitor` with fake probes + a mutable fake clock.)

- [ ] **Step 2: Run to verify they fail** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivityMonitorTests"` → FAIL (`MarkActive` undefined).

- [ ] **Step 3: Add to the interface** (`IActivityMonitor.cs`):

```csharp
    /// <summary>
    /// Directly credit an account as active as of <paramref name="nowUtc"/> — the path a
    /// keep-alive plugin uses after it synthesizes input into that account's window (the
    /// foreground/global-input heuristic in Sample() can't attribute plugin-directed input to
    /// the right window). Stamps LastActivityAt and re-arms the warn latch. No-op for an
    /// untracked account. The core never infers this — the plugin declares it via the
    /// consent-gated MarkAccountActive RPC.
    /// </summary>
    void MarkActive(Guid accountId, DateTimeOffset nowUtc);
```

- [ ] **Step 4: Implement in `ActivityMonitor.cs`** (reuse the exact in-place field-write discipline `Sample()` uses on `_records`; no new lock — `ConcurrentDictionary` + scalar field writes, same as `Sample()`'s stamp block):

```csharp
    public void MarkActive(Guid accountId, DateTimeOffset nowUtc)
    {
        if (_records.TryGetValue(accountId, out var rec))
        {
            rec.LastActivityAt = nowUtc;
            rec.WarnLatched = false; // re-arm — mirrors Sample()'s else-if re-arm branch
        }
        // Untracked id → no-op (account not launched / already exited).
    }
```

(Note: unlike the spec's suggestion to extract a shared stamp+re-eval helper, none is needed — `MarkActive` only stamps + re-arms; the coalesced edge *event* stays owned by `Sample()`'s periodic pass, which will observe the cleared latch on its next tick and re-cross later if the account goes idle again. Simpler, and it keeps the event firing on the timer thread as today. Confirm this matches the re-arm test's expectation.)

- [ ] **Step 5: Run to verify** — 3/3 new pass; whole `ActivityMonitorTests` green; full build 0 errors; full suite no regressions.

- [ ] **Step 6: Commit** — `feat(core): ActivityMonitor.MarkActive — direct activity credit for keep-alive plugins`

---

## Task 2: Proto RPC + capability token + consent copy + NuGet bump

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`, `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`, `src/ROROROblox.App/Plugins/PluginCapability.cs`
- No test (generated code + a const + a dictionary entry; exercised end-to-end in Task 4).

**Interfaces:**
- Produces: proto `MarkAccountActive(MarkAccountActiveRequest) returns (Empty)`; `PluginCapability.HostCommandsMarkAccountActive` const + Catalog entry. Consumed by Tasks 3, 4.

- [ ] **Step 1: Proto** — in the `RoRoRoHost` service block, after `GetAccountActivity` (keep the additive-comment convention):

```proto
  // Command surface (additive, NuGet 0.5.0): a keep-alive plugin credits an account as
  // active after it acts on that account's window (idle heuristic can't see plugin input).
  rpc MarkAccountActive(MarkAccountActiveRequest) returns (Empty);
```

And a request message near `AccountActivity`/`AccountActivityList`:

```proto
message MarkAccountActiveRequest {
  string account_id = 1;
}
```

(`Empty` already exists — reuse it.)

- [ ] **Step 2: NuGet bump** — `ROROROblox.PluginContract.csproj` `<Version>0.4.0</Version>` → `<Version>0.5.0</Version>`.

- [ ] **Step 3: Capability** — in `PluginCapability.cs`, add the const (in the `host.commands.*` group) + the Catalog entry (consent copy from the spec):

```csharp
    public const string HostCommandsMarkAccountActive = "host.commands.mark-account-active";
```

```csharp
        [HostCommandsMarkAccountActive] = "Let this plugin tell RoRoRo an account is still active (so idle warnings don't misfire). It cannot see what you type or do — only mark an account active.",
```

- [ ] **Step 4: Build** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` → 0 errors (proto regenerates `RoRoRoHostBase.MarkAccountActive` + `MarkAccountActiveRequest`; the abstract method is unimplemented on `PluginHostService` until Task 3 — but the base has a default `throw`-ing impl for unimplemented methods in gRPC, so the SOLUTION still builds; verify. If the generated base makes it abstract and the build breaks, this task's build check moves to after Task 3 — note it and proceed, keeping Task 2+3 as one green commit if needed).

- [ ] **Step 5: Commit** — `feat(contract): MarkAccountActive RPC + capability (NuGet 0.5.0)`

---

## Task 3: Host handler + marker adapter + interceptor entry + DI

**Files:**
- Create: `src/ROROROblox.App/Plugins/IAccountActivityMarker.cs`, `src/ROROROblox.App/Plugins/AccountActivityMarker.cs`
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs`, `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`, `src/ROROROblox.App/App.xaml.cs`
- No new unit test (integration-covered in Task 4).

**Interfaces:**
- Consumes: `IActivityMonitor.MarkActive` (Task 1), the proto types + capability (Task 2).
- Produces: `IAccountActivityMarker.Mark(string accountId)` (parses the Guid, calls the monitor with the clock's now); `PluginHostService.MarkAccountActive` override.

- [ ] **Step 1: The marker adapter** (mirrors `ActivitySnapshotProvider` — thin App-layer wrapper over the Core monitor, owns the string→Guid parse + the clock):

`IAccountActivityMarker.cs`:
```csharp
namespace ROROROblox.App.Plugins;

/// <summary>Host-side sink for a plugin's MarkAccountActive RPC. Parses the plugin-facing
/// stringified account id and credits the Core activity monitor. Mirrors
/// <see cref="IActivitySnapshotProvider"/>'s adapter role.</summary>
public interface IAccountActivityMarker
{
    /// <summary>Credit the account (RoRoRo's stringified Guid) as active now. No-op on an
    /// unparseable or untracked id.</summary>
    void Mark(string accountId);
}
```

`AccountActivityMarker.cs`:
```csharp
using System;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins;

public sealed class AccountActivityMarker : IAccountActivityMarker
{
    private readonly IActivityMonitor _monitor;
    private readonly IClock _clock;

    public AccountActivityMarker(IActivityMonitor monitor, IClock clock)
    {
        _monitor = monitor;
        _clock = clock;
    }

    public void Mark(string accountId)
    {
        if (Guid.TryParse(accountId, out var id))
        {
            _monitor.MarkActive(id, _clock.UtcNow);
        }
        // Unparseable id → no-op (defensive; the plugin should send our stringified Guid).
    }
}
```

- [ ] **Step 2: Host handler** in `PluginHostService.cs` — add an `IAccountActivityMarker` ctor param (alongside the existing `IActivitySnapshotProvider`; field + assignment) and the override (mirror `RequestLaunch`'s no-throw domain shape; `MarkActive` is fire-and-forget, returns `Empty`):

```csharp
    public override Task<Empty> MarkAccountActive(MarkAccountActiveRequest request, ServerCallContext context)
    {
        _activityMarker.Mark(request.AccountId);
        return Task.FromResult(new Empty());
    }
```

- [ ] **Step 3: Interceptor entry** — `RpcMethodCapabilityMap.cs`, in the `Map` dictionary (after `GetAccountActivity`):

```csharp
        ["MarkAccountActive"] = PluginCapability.HostCommandsMarkAccountActive,
```

- [ ] **Step 4: DI** — `App.xaml.cs`: register the marker + pass it to `PluginHostService`. The `IActivityMonitor` + `IClock` singletons already exist (extraction §10), so:

```csharp
        services.AddSingleton<ROROROblox.App.Plugins.IAccountActivityMarker>(sp =>
            new ROROROblox.App.Plugins.AccountActivityMarker(
                sp.GetRequiredService<IActivityMonitor>(),
                sp.GetRequiredService<IClock>()));
```

And add `sp.GetRequiredService<ROROROblox.App.Plugins.IAccountActivityMarker>()` as the new final arg to the `new PluginHostService(...)` lambda (extraction §10 shows the exact call — append after the `IActivitySnapshotProvider` arg, matching the new ctor param order).

- [ ] **Step 5: Build + suite** — `build ROROROblox.slnx` 0 errors; full `src/ROROROblox.Tests/` green (no unit-level change, but the ctor signature change must not break any `PluginHostService` construction in tests — grep `new PluginHostService(` in the test tree and update every site with a stub marker; the harness has a pattern for `StubActivityProvider` — add a trivial `StubActivityMarker : IAccountActivityMarker` with a no-op or recording `Mark`).

- [ ] **Step 6: Commit** — `feat(plugins): MarkAccountActive host handler + marker adapter + capability gating`

---

## Task 4: End-to-end integration tests

**Files:**
- Modify: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs` (+ a `StubActivityMarker` if not added in Task 3)
- Test: this task IS the tests.

**Interfaces:**
- Consumes: the full pipeline (proto, interceptor, host service, marker) from Tasks 1-3.

- [ ] **Step 1: Write the tests** — mirror `GetAccountActivity_ConsentedPlugin_ReturnsSnapshot` / `_DeniedWhenCapabilityNotGranted` / the production-accessor pair (extraction §8):

1. **Granted round-trip (the real proof):** wire a REAL `ActivityMonitor` (not a stub) through a real `AccountActivityMarker` into `PluginHostService`, and a real `ActivitySnapshotProvider` reading the same monitor. `OnAccountLaunched(id)`, advance a fake clock past threshold, then over the pipe: `client.MarkAccountActiveAsync(new MarkAccountActiveRequest { AccountId = id.ToString() })` → then `client.GetAccountActivityAsync(new Empty())` → assert that account's `SecondsSinceActivity` is small (freshly credited). This proves the whole path: RPC → interceptor → handler → marker → monitor → snapshot. Grant both `host.commands.mark-account-active` and `host.queries.account-activity`.
   - If sharing one real monitor+clock across both providers is awkward in the harness, the minimum viable version uses a recording `StubActivityMarker` and asserts the marker received the exact `accountId` string — but prefer the real-monitor round-trip; it's the honest end-to-end.
2. **Denied:** plugin grants only `host.events.account-launched`; `MarkAccountActiveAsync` → `Assert.ThrowsAsync<RpcException>` with `StatusCode.PermissionDenied`.
3. **Production-accessor header path:** `currentPluginAccessor: () => null`, `x-plugin-id` header set → resolves + passes; no header → `FailedPrecondition` (mirror `RequestLaunch_ProductionAccessor_*`).
4. **Unparseable/untracked id (no-throw):** granted plugin sends `AccountId = "not-a-guid"` → RPC returns `Empty` (no exception), and a subsequent `GetAccountActivity` is unaffected — proves the marker's defensive parse.

- [ ] **Step 2: Run** — `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.PluginTestHarness/` → all green (new + existing).

- [ ] **Step 3: Commit** — `test(plugins): MarkAccountActive end-to-end — granted round-trip, denied, header, bad-id`

---

## Final verification

- [ ] Whole solution: `build ROROROblox.slnx` 0 errors (warning count == baseline 27); `test ROROROblox.slnx` green.
- [ ] Local-path guard clean.
- [ ] **Wall audit:** `git diff <base>..HEAD | grep -iE "hook|SetWindowsHookEx|RegisterRawInput|GetAsyncKeyState|synthetic"` → zero (nothing added senses input; MarkActive is a declared stamp).
- [ ] Additive-contract check: `git diff <base>..HEAD -- "*.proto"` touches only the new RPC + new message; no existing line changed.
- [ ] Manual: n/a in-repo (the live proof is ur-AFK adopting the RPC — the coordinated follow-up).

---

## Self-review notes (author)

**Spec coverage:** §2.1 new capability+RPC → Task 2; §2.2 `ActivityMonitor.MarkActive` → Task 1; §2.3 foreground heuristic unchanged (Task 1 adds a path, edits nothing in `Sample()`); §4 marker adapter + interceptor + DI → Task 3; §5 edge cases (untracked no-op, race, unconsented denied, re-arm) → Tasks 1 (untracked, re-arm) + 4 (denied, bad-id); §6 tests → Tasks 1 + 4.

**Deviation from spec, flagged:** the spec suggested extracting a shared "stamp + re-evaluate" helper so `Sample()` and `MarkActive` share edge-evaluation. The plan does NOT — `MarkActive` only stamps + clears the latch, letting `Sample()`'s next periodic pass own the coalesced event (which keeps the event on the timer thread and avoids `MarkActive` firing `WarnThresholdCrossed` from a gRPC handler thread — arguably safer). If a reviewer wants the un-cross to emit an *immediate* event, that's the helper-extraction path; the plan's simpler choice is called out in Task 1 Step 4 for adjudication.

**Type consistency:** `MarkActive(Guid, DateTimeOffset)` (T1) ← `AccountActivityMarker.Mark(string)` parses to it (T3) ← `MarkAccountActiveRequest.account_id` string (T2) ← plugin sends the stringified Guid matching `ActivitySnapshotProvider`'s `.ToString()`. `HostCommandsMarkAccountActive` (T2) used in `RpcMethodCapabilityMap` (T3) + granted in tests (T4).

**Green-commit discipline:** T1 is self-contained (Core only). T2's generated base may or may not force the override immediately — Task 2 Step 4 flags folding T2+T3 into one commit if the build demands it. T3 updates every `new PluginHostService(` site (grep). T4 is tests-only.
