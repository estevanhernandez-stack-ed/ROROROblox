# Core Activity-Awareness (Part A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give RORORO core input-accurate per-account idle awareness (foreground + `GetLastInputInfo`, no keystroke hook), surface it in the app's own UI (row chip + banner + mutable coalesced toast), and expose it to plugins through one consent-gated pull-query RPC.

**Architecture:** A dedicated `ActivityMonitor` (in `ROROROblox.Core/Diagnostics`, mirroring `PresenceService`) samples the foreground window + system last-input tick on a ~1s timer, stamps `last-activity-at` for the foreground account when input advanced, and raises a coalesced edge event when accounts cross a warn threshold. The App layer consumes it three ways: row chips via `AccountSummary` fields, a mutable tray toast via a small `IdleAlertPresenter`, and a consent-gated `GetAccountActivity` gRPC query for plugins. Core observes and reports; it never acts on a client.

**Tech Stack:** .NET 10 / C# 14, WPF, CsWin32 (Win32 source generator), Hardcodet.NotifyIcon.Wpf (tray), gRPC over named pipe (Grpc.Tools codegen), xUnit (hand-rolled fakes, no Moq).

## Global Constraints

Every task's requirements implicitly include these — copied verbatim from the spec and repo keystone:

- **Build/test with the explicit dotnet host** (bare `dotnet` on PATH is 10.0.202 and fails `global.json`'s 10.0.203 pin): `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe"`.
- **Solution is `ROROROblox.slnx`** — never the stray `ROROROblox.sln` (`dotnet build` bare errors MSB1011 while both exist).
- **Wall (core-only, absolute):** the monitor and all core code only *read* — `GetForegroundWindow`, `GetLastInputInfo`, process-tracker lookups. No `SendInput`, no window focus, no input synthesis, no client injection anywhere in this plan. Acting lives only in Part B (a separate plugin repo).
- **No keystroke hook.** Idle input is measured via `GetLastInputInfo` (timestamp only), never `WH_KEYBOARD_LL`. Core never sees keystroke content.
- **No secrets, no cookies in logs.** Nothing in this plan touches `.ROBLOSECURITY` or `IAccountStore` cookie data.
- **Plugin contract change is additive only.** Handshake `contract_version` stays `"1.0"`; the `ROROROblox.PluginContract` NuGet `<Version>` bumps `0.2.0 → 0.3.0`.
- **Notify copy is factual, not predictive** — "idle 18m" (fact), never "times out at 20:04" (guess).
- **Commits:** conventional (`feat` / `test` / `docs` / `refactor` / `build`). One commit per task (or per RED→GREEN cycle where a task has several).
- **Test doubles:** hand-rolled fakes matching the existing `ROROROblox.Tests` style (see `AccountSummaryTests`), not Moq.

---

## File Structure

**New files:**

- `src/ROROROblox.Core/Diagnostics/IForegroundAccountResolver.cs` — narrow pid→account reverse lookup (1 method), implemented by `RobloxProcessTracker`.
- `src/ROROROblox.Core/Diagnostics/IClock.cs` + `SystemClock.cs` — injectable now (reuse an existing clock abstraction if one already exists; grep first).
- `src/ROROROblox.Core/Diagnostics/IForegroundWindowProbe.cs` + `Win32ForegroundWindowProbe.cs` — foreground pid via CsWin32.
- `src/ROROROblox.Core/Diagnostics/ISystemInputClock.cs` + `Win32SystemInputClock.cs` — `GetLastInputInfo` tick via CsWin32.
- `src/ROROROblox.Core/Diagnostics/AccountActivity.cs` — `readonly record struct` snapshot item.
- `src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs` + `ActivityMonitor.cs` — the engine.
- `src/ROROROblox.App/Notifications/IdleAlertPresenter.cs` — toast/mute formatting.
- `src/ROROROblox.App/ViewModels/ActivitySnapshotApplier.cs` — apply snapshot to rows (pure static).
- `src/ROROROblox.App/ViewModels/IdleSummary.cs` — banner text formatter (pure static).
- `src/ROROROblox.App/Plugins/IActivitySnapshotProvider.cs` + `ActivitySnapshotProvider.cs` — adapter Core→plugin surface.
- Tests: `src/ROROROblox.Tests/ActivityMonitorTests.cs`, `ActivitySnapshotApplierTests.cs`, `IdleAlertPresenterTests.cs`, `IdleSummaryTests.cs`, `ActivitySnapshotProviderTests.cs`, plus additions to `AccountSummaryTests.cs`, `ConvertersTests.cs` (create if absent), `AppSettingsTests.cs` (if present).
- Integration: additions to `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`.

**Modified files:**

- `src/ROROROblox.Core/Diagnostics/IRobloxProcessTracker.cs` + `RobloxProcessTracker.cs` — implement `IForegroundAccountResolver`.
- `src/ROROROblox.Core/NativeMethods.txt` — add `GetForegroundWindow`, `GetWindowThreadProcessId`, `GetLastInputInfo`, `LASTINPUTINFO`.
- `src/ROROROblox.Core/AppSettings.cs` (+ `IAppSettings.cs`) — `MuteIdleAlerts`, `IdleWarnThresholdMinutes`.
- `src/ROROROblox.Core/ITrayService.cs` + `src/ROROROblox.App/Tray/TrayService.cs` — `ShowToast`.
- `src/ROROROblox.App/ViewModels/AccountSummary.cs` — idle fields (`SinceActivity`, `IdleWarn`, `IdleText`, `ShowIdleChip`).
- `src/ROROROblox.App/Converters.cs` — `IdleChipBrushConverter`.
- `src/ROROROblox.App/MainWindow.xaml` — idle chip in the row template + idle summary strip.
- `src/ROROROblox.App/ViewModels/MainViewModel.cs` — inject monitor, subscribe, refresh, banner.
- `src/ROROROblox.App/App.xaml.cs` — DI registration + lifecycle wiring + threshold/mute from settings.
- `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` + `.csproj` — new RPC/messages + version bump.
- `src/ROROROblox.App/Plugins/PluginCapability.cs`, `RpcMethodCapabilityMap.cs`, `PluginHostService.cs` — new capability + gated RPC.
- `docs/plugins/AUTHOR_GUIDE.md` — document the new capability.
- Preferences window XAML/VM (mirror the existing `LaunchMainOnStartup` control) — mute toggle + threshold input.

---

## Task 1: Reverse pid→account resolver on the process tracker

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/IForegroundAccountResolver.cs`
- Modify: `src/ROROROblox.Core/Diagnostics/RobloxProcessTracker.cs` (add interface + method over the existing `_claimedPidToAccount` field)
- Test: `src/ROROROblox.Tests/RobloxProcessTrackerResolverTests.cs`

**Interfaces:**
- Produces: `bool IForegroundAccountResolver.TryResolveAccountByPid(int pid, out Guid accountId)` — later tasks (`ActivityMonitor`) consume this narrow interface, not the full tracker.

- [ ] **Step 1: Write the failing test**

```csharp
// src/ROROROblox.Tests/RobloxProcessTrackerResolverTests.cs
using System;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class RobloxProcessTrackerResolverTests
{
    [Fact]
    public void TryResolveAccountByPid_UnknownPid_ReturnsFalse()
    {
        using var tracker = new RobloxProcessTracker(NullLogger<RobloxProcessTracker>.Instance);
        IForegroundAccountResolver resolver = tracker;

        var found = resolver.TryResolveAccountByPid(999999, out var accountId);

        Assert.False(found);
        Assert.Equal(Guid.Empty, accountId);
    }
}
```

(If `RobloxProcessTracker`'s constructor signature differs, match it — the logger dependency is shown in the extraction. Adjust the `new` call to the real ctor.)

- [ ] **Step 2: Run test to verify it fails**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxProcessTrackerResolverTests"`
Expected: FAIL — `IForegroundAccountResolver` does not exist / `RobloxProcessTracker` does not implement it.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/ROROROblox.Core/Diagnostics/IForegroundAccountResolver.cs
using System;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Narrow reverse lookup: which managed account owns a given OS process id.
/// Backed by <see cref="RobloxProcessTracker"/>'s claimed-pid map. Read-only.
/// </summary>
public interface IForegroundAccountResolver
{
    bool TryResolveAccountByPid(int pid, out Guid accountId);
}
```

In `RobloxProcessTracker.cs`, add the interface to the class declaration and the method (the field `_claimedPidToAccount` already exists):

```csharp
// class declaration: add IForegroundAccountResolver
internal sealed class RobloxProcessTracker : IRobloxProcessTracker, IForegroundAccountResolver, IDisposable

// method (place near the other public members):
public bool TryResolveAccountByPid(int pid, out Guid accountId)
    => _claimedPidToAccount.TryGetValue(pid, out accountId);
```

(If the class is `public`, keep it `public`. Match the existing accessibility.)

- [ ] **Step 4: Run test to verify it passes**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~RobloxProcessTrackerResolverTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/IForegroundAccountResolver.cs src/ROROROblox.Core/Diagnostics/RobloxProcessTracker.cs src/ROROROblox.Tests/RobloxProcessTrackerResolverTests.cs
git commit -m "feat(core): reverse pid->account resolver on RobloxProcessTracker"
```

---

## Task 2: ActivityMonitor — foreground+input stamping

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/IClock.cs`, `SystemClock.cs`, `IForegroundWindowProbe.cs`, `ISystemInputClock.cs`, `AccountActivity.cs`, `IActivityMonitor.cs`, `ActivityMonitor.cs`
- Test: `src/ROROROblox.Tests/ActivityMonitorTests.cs`

**Interfaces:**
- Consumes: `IForegroundAccountResolver` (Task 1).
- Produces:
  - `IForegroundWindowProbe { bool TryGetForegroundPid(out int pid); }`
  - `ISystemInputClock { uint LastInputTick { get; } }`
  - `IClock { DateTimeOffset UtcNow { get; } }`
  - `ActivityMonitor` with `void OnAccountLaunched(Guid)`, `void OnAccountExited(Guid)`, `void Sample()`, and (Task 4) `IReadOnlyList<AccountActivity> GetSnapshot()`, (Task 3) `event EventHandler<IReadOnlyList<Guid>> WarnThresholdCrossed`, `TimeSpan WarnThreshold`.

**Note (clock reuse):** before creating `IClock`, grep `src/ROROROblox.Core` for an existing clock abstraction (`IClock`, `ISystemClock`, `TimeProvider` usage). If one exists, use it and skip creating `IClock.cs`/`SystemClock.cs`; adjust the tests' fake accordingly.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/ROROROblox.Tests/ActivityMonitorTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class ActivityMonitorTests
{
    // ---- hand-rolled fakes ----
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeForeground : IForegroundWindowProbe
    {
        public int? Pid;
        public bool TryGetForegroundPid(out int pid)
        {
            pid = Pid ?? 0;
            return Pid is > 0;
        }
    }

    private sealed class FakeInput : ISystemInputClock
    {
        public uint Tick;
        public uint LastInputTick => Tick;
    }

    private sealed class FakeResolver : IForegroundAccountResolver
    {
        public readonly Dictionary<int, Guid> Map = new();
        public bool TryResolveAccountByPid(int pid, out Guid accountId)
            => Map.TryGetValue(pid, out accountId);
    }

    private static (ActivityMonitor m, FakeClock clock, FakeForeground fg, FakeInput input, FakeResolver res)
        Build()
    {
        var clock = new FakeClock();
        var fg = new FakeForeground();
        var input = new FakeInput();
        var res = new FakeResolver();
        var m = new ActivityMonitor(fg, input, res, clock);
        return (m, clock, fg, input, res);
    }

    [Fact]
    public void Sample_InputAdvancedWhileAccountForeground_StampsThatAccount()
    {
        var (m, clock, fg, input, res) = Build();
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);

        // move clock forward, put A foreground, advance input tick
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        fg.Pid = 1000;
        input.Tick = 42;
        m.Sample();

        var snap = m.GetSnapshot().Single(x => x.AccountId == a);
        Assert.Equal(clock.UtcNow, snap.LastActivityAt);
        Assert.Equal(TimeSpan.Zero, snap.SinceActivity);
    }

    [Fact]
    public void Sample_ForegroundNotTracked_StampsNobody()
    {
        var (m, clock, fg, input, res) = Build();
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);              // A launched, seeded at 12:00
        var seeded = m.GetSnapshot().Single(x => x.AccountId == a).LastActivityAt;

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        fg.Pid = 7777;                        // unknown pid (not in resolver)
        input.Tick = 99;
        m.Sample();

        var after = m.GetSnapshot().Single(x => x.AccountId == a).LastActivityAt;
        Assert.Equal(seeded, after);          // unchanged
    }

    [Fact]
    public void Sample_ForegroundButInputDidNotAdvance_AgesForegroundAccount()
    {
        var (m, clock, fg, input, res) = Build();
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);               // seeded at 12:00
        input.Tick = 10;
        m.Sample();                            // first real sample establishes baseline tick

        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        fg.Pid = 1000;                         // A is foreground...
        // input.Tick stays 10 -> no advance (AFK on the foreground window)
        m.Sample();

        var snap = m.GetSnapshot().Single(x => x.AccountId == a);
        Assert.True(snap.SinceActivity >= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void OnAccountExited_DropsRecord()
    {
        var (m, _, _, _, _) = Build();
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);
        Assert.Single(m.GetSnapshot());

        m.OnAccountExited(a);
        Assert.Empty(m.GetSnapshot());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivityMonitorTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/ROROROblox.Core/Diagnostics/IClock.cs
using System;
namespace ROROROblox.Core.Diagnostics;
public interface IClock { DateTimeOffset UtcNow { get; } }

// src/ROROROblox.Core/Diagnostics/SystemClock.cs
using System;
namespace ROROROblox.Core.Diagnostics;
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

// src/ROROROblox.Core/Diagnostics/IForegroundWindowProbe.cs
namespace ROROROblox.Core.Diagnostics;
public interface IForegroundWindowProbe { bool TryGetForegroundPid(out int pid); }

// src/ROROROblox.Core/Diagnostics/ISystemInputClock.cs
namespace ROROROblox.Core.Diagnostics;
public interface ISystemInputClock { uint LastInputTick { get; } }

// src/ROROROblox.Core/Diagnostics/AccountActivity.cs
using System;
namespace ROROROblox.Core.Diagnostics;
public readonly record struct AccountActivity(Guid AccountId, DateTimeOffset LastActivityAt, TimeSpan SinceActivity);
```

```csharp
// src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs
using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

public interface IActivityMonitor
{
    /// <summary>Warn line; default 15 minutes. Set from settings at composition.</summary>
    TimeSpan WarnThreshold { get; set; }

    /// <summary>Coalesced, edge-triggered: the accounts that newly crossed the warn line this sample.</summary>
    event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed;

    void OnAccountLaunched(Guid accountId);
    void OnAccountExited(Guid accountId);

    /// <summary>One sample tick: stamp the foreground account if input advanced, then evaluate thresholds.</summary>
    void Sample();

    IReadOnlyList<AccountActivity> GetSnapshot();
}
```

```csharp
// src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

public sealed class ActivityMonitor : IActivityMonitor
{
    private sealed class Record
    {
        public DateTimeOffset LastActivityAt;
        public bool WarnLatched;
    }

    private readonly IForegroundWindowProbe _foreground;
    private readonly ISystemInputClock _input;
    private readonly IForegroundAccountResolver _resolver;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, Record> _records = new();

    private uint _lastSeenInputTick;
    private bool _haveInputBaseline;

    public TimeSpan WarnThreshold { get; set; } = TimeSpan.FromMinutes(15);

    public event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed;

    public ActivityMonitor(
        IForegroundWindowProbe foreground,
        ISystemInputClock input,
        IForegroundAccountResolver resolver,
        IClock clock)
    {
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void OnAccountLaunched(Guid accountId)
        => _records[accountId] = new Record { LastActivityAt = _clock.UtcNow, WarnLatched = false };

    public void OnAccountExited(Guid accountId)
        => _records.TryRemove(accountId, out _);

    public void Sample()
    {
        var now = _clock.UtcNow;

        // 1. foreground + input stamping
        var currentTick = _input.LastInputTick;
        var advanced = _haveInputBaseline && currentTick != _lastSeenInputTick;
        _lastSeenInputTick = currentTick;
        _haveInputBaseline = true;

        if (advanced
            && _foreground.TryGetForegroundPid(out var pid)
            && _resolver.TryResolveAccountByPid(pid, out var accountId)
            && _records.TryGetValue(accountId, out var rec))
        {
            rec.LastActivityAt = now;
        }

        // 2. threshold edge evaluation (coalesced)
        List<Guid>? crossed = null;
        foreach (var kv in _records)
        {
            var since = now - kv.Value.LastActivityAt;
            if (since >= WarnThreshold)
            {
                if (!kv.Value.WarnLatched)
                {
                    kv.Value.WarnLatched = true;
                    (crossed ??= new List<Guid>()).Add(kv.Key);
                }
            }
            else if (kv.Value.WarnLatched)
            {
                kv.Value.WarnLatched = false; // re-arm
            }
        }

        if (crossed is { Count: > 0 })
        {
            WarnThresholdCrossed?.Invoke(this, crossed);
        }
    }

    public IReadOnlyList<AccountActivity> GetSnapshot()
    {
        var now = _clock.UtcNow;
        var list = new List<AccountActivity>(_records.Count);
        foreach (var kv in _records)
        {
            var since = now - kv.Value.LastActivityAt;
            if (since < TimeSpan.Zero) since = TimeSpan.Zero; // clock-skew guard
            list.Add(new AccountActivity(kv.Key, kv.Value.LastActivityAt, since));
        }
        return list;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivityMonitorTests"`
Expected: PASS (all four).

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/ src/ROROROblox.Tests/ActivityMonitorTests.cs
git commit -m "feat(core): ActivityMonitor foreground+input stamping + lifecycle"
```

---

## Task 3: ActivityMonitor — warn-threshold edge event

The `Sample()` implementation in Task 2 already raises `WarnThresholdCrossed`. This task locks that behavior with dedicated edge/coalesce/re-arm tests (Task 2 covered stamping only).

**Files:**
- Test: `src/ROROROblox.Tests/ActivityMonitorTests.cs` (add tests)

- [ ] **Step 1: Add the failing tests**

```csharp
    [Fact]
    public void Sample_CrossingThreshold_FiresOnceThenLatches()
    {
        var (m, clock, fg, input, res) = Build();
        m.WarnThreshold = TimeSpan.FromMinutes(15);
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);               // seeded at 12:00

        var fires = new List<IReadOnlyList<Guid>>();
        m.WarnThresholdCrossed += (_, batch) => fires.Add(batch);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();                            // crosses -> fires
        m.Sample();                            // still over, latched -> no fire

        Assert.Single(fires);
        Assert.Equal(a, Assert.Single(fires[0]));
    }

    [Fact]
    public void Sample_ReArmsAfterGoingActiveAgain()
    {
        var (m, clock, fg, input, res) = Build();
        m.WarnThreshold = TimeSpan.FromMinutes(15);
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);
        input.Tick = 5; m.Sample();            // baseline

        var fires = new List<IReadOnlyList<Guid>>();
        m.WarnThresholdCrossed += (_, batch) => fires.Add(batch);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();                            // fire #1 (latched)

        // becomes active again: foreground + input advance
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        fg.Pid = 1000; input.Tick = 6;
        m.Sample();                            // stamps -> since resets -> un-latches

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();                            // fire #2

        Assert.Equal(2, fires.Count);
    }

    [Fact]
    public void Sample_CoalescesMultipleCrossingsIntoOneBatch()
    {
        var (m, clock, fg, input, res) = Build();
        m.WarnThreshold = TimeSpan.FromMinutes(15);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        m.OnAccountLaunched(a);
        m.OnAccountLaunched(b);

        var fires = new List<IReadOnlyList<Guid>>();
        m.WarnThresholdCrossed += (_, batch) => fires.Add(batch);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();

        Assert.Single(fires);
        Assert.Equal(2, fires[0].Count);
    }
```

- [ ] **Step 2: Run to verify** — they should PASS immediately (Task 2 implemented the behavior). If any fails, fix `ActivityMonitor.Sample()` threshold logic until green.

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivityMonitorTests"`
Expected: PASS (all seven).

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.Tests/ActivityMonitorTests.cs
git commit -m "test(core): lock ActivityMonitor warn-edge coalesce + re-arm"
```

---

## Task 4: ActivityMonitor — GetSnapshot projection

`GetSnapshot()` exists from Task 2. Add tests asserting the projection contract (empty, SinceActivity computed vs clock, clamp).

**Files:**
- Test: `src/ROROROblox.Tests/ActivityMonitorTests.cs` (add tests)

- [ ] **Step 1: Add the tests**

```csharp
    [Fact]
    public void GetSnapshot_Empty_WhenNoAccounts()
    {
        var (m, _, _, _, _) = Build();
        Assert.Empty(m.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_ComputesSinceActivityAgainstClock()
    {
        var (m, clock, _, _, _) = Build();
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);               // stamped at 12:00
        clock.UtcNow = clock.UtcNow.AddMinutes(7);

        var snap = m.GetSnapshot().Single();
        Assert.Equal(a, snap.AccountId);
        Assert.Equal(TimeSpan.FromMinutes(7), snap.SinceActivity);
    }
```

- [ ] **Step 2: Run to verify** — PASS immediately.

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivityMonitorTests"`
Expected: PASS (all nine).

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.Tests/ActivityMonitorTests.cs
git commit -m "test(core): lock ActivityMonitor snapshot projection"
```

---

## Task 5: Win32 probes + timer + DI registration + lifecycle wiring

Wire the real monitor into the running app. This is composition/interop — no new unit test; verified by full build + all existing tests staying green + manual smoke.

**Files:**
- Modify: `src/ROROROblox.Core/NativeMethods.txt`
- Create: `src/ROROROblox.Core/Diagnostics/Win32ForegroundWindowProbe.cs`, `Win32SystemInputClock.cs`
- Modify: `src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs` + `ActivityMonitor.cs` (add `Start()`/`Stop()`/`IDisposable` timer)
- Modify: `src/ROROROblox.App/App.xaml.cs` (register + wire tracker events + start)

**Interfaces:**
- Consumes: `IActivityMonitor` (Task 2), `IForegroundAccountResolver` (Task 1), `IRobloxProcessTracker` events (`ProcessAttached`, `ProcessExited`, `Attached`).
- Produces: `ActivityMonitor.Start()` / `Stop()`; a running singleton wired to launch/exit.

- [ ] **Step 1: Add Win32 symbols**

Append to `src/ROROROblox.Core/NativeMethods.txt`:

```
GetForegroundWindow
GetWindowThreadProcessId
GetLastInputInfo
LASTINPUTINFO
```

- [ ] **Step 2: Implement the Win32 probes**

```csharp
// src/ROROROblox.Core/Diagnostics/Win32ForegroundWindowProbe.cs
using Windows.Win32;

namespace ROROROblox.Core.Diagnostics;

public sealed class Win32ForegroundWindowProbe : IForegroundWindowProbe
{
    public bool TryGetForegroundPid(out int pid)
    {
        pid = 0;
        var hwnd = PInvoke.GetForegroundWindow();
        if (hwnd.IsNull) return false;
        uint processId = 0;
        unsafe { PInvoke.GetWindowThreadProcessId(hwnd, &processId); }
        pid = (int)processId;
        return pid > 0;
    }
}
```

```csharp
// src/ROROROblox.Core/Diagnostics/Win32SystemInputClock.cs
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ROROROblox.Core.Diagnostics;

public sealed class Win32SystemInputClock : ISystemInputClock
{
    public uint LastInputTick
    {
        get
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return PInvoke.GetLastInputInfo(ref lii) ? lii.dwTime : 0u;
        }
    }
}
```

**Note:** CsWin32 generates `PInvoke.GetWindowThreadProcessId(HWND, uint*)` (pointer out-param) — the `unsafe`/`&` form above matches. If the generated overload differs (some CsWin32 versions emit `out uint`), use `PInvoke.GetWindowThreadProcessId(hwnd, out processId)` and drop `unsafe`. Confirm against the generated signature; `SingleInstanceGuard.cs` in App shows the local CsWin32 call style. If `unsafe` is needed, ensure `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` is set in `ROROROblox.Core.csproj` (add it if absent).

- [ ] **Step 3: Add timer lifecycle to ActivityMonitor**

Add to `IActivityMonitor`:

```csharp
    void Start();
    void Stop();
```

Add to `ActivityMonitor` (implement `IDisposable` too):

```csharp
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public void Start()
    {
        if (_disposed) return;
        _timer ??= new System.Threading.Timer(_ => SafeSample(), null,
            dueTime: TimeSpan.FromSeconds(2), period: TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeSample()
    {
        try { Sample(); } catch { /* never let a sample tick crash the timer thread */ }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
```

Update the class declaration to `: IActivityMonitor, IDisposable`.

- [ ] **Step 4: Register + wire in App.xaml.cs**

In the service registration block (near `IRobloxProcessTracker` / `IPresenceService`):

```csharp
services.AddSingleton<IForegroundWindowProbe, Win32ForegroundWindowProbe>();
services.AddSingleton<ISystemInputClock, Win32SystemInputClock>();
services.AddSingleton<IClock, SystemClock>(); // omit if reusing an existing clock registration
services.AddSingleton<IForegroundAccountResolver>(sp =>
    (IForegroundAccountResolver)sp.GetRequiredService<IRobloxProcessTracker>());
services.AddSingleton<IActivityMonitor>(sp => new ActivityMonitor(
    sp.GetRequiredService<IForegroundWindowProbe>(),
    sp.GetRequiredService<ISystemInputClock>(),
    sp.GetRequiredService<IForegroundAccountResolver>(),
    sp.GetRequiredService<IClock>()));
```

After the container is built and the tracker + monitor are resolved (where other post-build wiring happens, e.g. near where `IPresenceService` is started), wire lifecycle and start:

```csharp
var tracker = provider.GetRequiredService<IRobloxProcessTracker>();
var monitor = provider.GetRequiredService<IActivityMonitor>();

// seed already-attached accounts
foreach (var id in tracker.Attached.Keys)
    monitor.OnAccountLaunched(id);

tracker.ProcessAttached += (_, e) => monitor.OnAccountLaunched(e.AccountId);
tracker.ProcessExited  += (_, e) => monitor.OnAccountExited(e.AccountId);
monitor.Start();
```

(Match `RobloxProcessEventArgs`'s real account-id property name; the extraction shows `ProcessAttached`/`ProcessExited` of type `EventHandler<RobloxProcessEventArgs>`. Confirm the property is `AccountId`.)

- [ ] **Step 5: Build the solution + run all existing tests**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx`
Then: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/`
Expected: build succeeds; all tests green (no regressions).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/NativeMethods.txt src/ROROROblox.Core/Diagnostics/ src/ROROROblox.App/App.xaml.cs
git commit -m "feat(app): wire ActivityMonitor — Win32 probes, 1s timer, launch/exit lifecycle"
```

---

## Task 6: Settings — MuteIdleAlerts + IdleWarnThresholdMinutes

**Files:**
- Modify: `src/ROROROblox.Core/AppSettings.cs`, `src/ROROROblox.Core/IAppSettings.cs`
- Test: `src/ROROROblox.Tests/AppSettingsTests.cs` (add tests; create the file if none exists, mirroring the existing settings-test pattern)

**Interfaces:**
- Produces: `Task<bool> GetMuteIdleAlertsAsync()`, `Task SetMuteIdleAlertsAsync(bool)`, `Task<int> GetIdleWarnThresholdMinutesAsync()`, `Task SetIdleWarnThresholdMinutesAsync(int)`.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task MuteIdleAlerts_DefaultsFalse_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            Assert.False(await settings.GetMuteIdleAlertsAsync());

            await settings.SetMuteIdleAlertsAsync(true);
            Assert.True(await settings.GetMuteIdleAlertsAsync());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task IdleWarnThresholdMinutes_DefaultsFifteen_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            Assert.Equal(15, await settings.GetIdleWarnThresholdMinutesAsync());

            await settings.SetIdleWarnThresholdMinutesAsync(12);
            Assert.Equal(12, await settings.GetIdleWarnThresholdMinutesAsync());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

(Match `AppSettings`'s real constructor — the extraction shows a private `_filePath` and `DefaultPath()`; if the ctor takes a path, use it, else adapt to the real test-seam the existing settings tests use.)

- [ ] **Step 2: Run to verify it fails**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AppSettingsTests"`
Expected: FAIL — methods not defined.

- [ ] **Step 3: Implement**

Add to `SettingsBlob` (extend the existing record):

```csharp
private sealed record SettingsBlob(
    int Version,
    string? DefaultPlaceUrl,
    bool LaunchMainOnStartup = false,
    string? ActiveThemeId = null,
    bool BloxstrapWarningDismissed = false,
    bool MuteIdleAlerts = false,
    int IdleWarnThresholdMinutes = 15);
```

Add to `IAppSettings`:

```csharp
Task<bool> GetMuteIdleAlertsAsync();
Task SetMuteIdleAlertsAsync(bool muted);
Task<int> GetIdleWarnThresholdMinutesAsync();
Task SetIdleWarnThresholdMinutesAsync(int minutes);
```

Add to `AppSettings` (mirror the `LaunchMainOnStartup` gate pattern verbatim):

```csharp
public async Task<bool> GetMuteIdleAlertsAsync()
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try { return (await LoadAsync().ConfigureAwait(false)).MuteIdleAlerts; }
    finally { _gate.Release(); }
}

public async Task SetMuteIdleAlertsAsync(bool muted)
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        var s = await LoadAsync().ConfigureAwait(false);
        await SaveAsync(s with { MuteIdleAlerts = muted }).ConfigureAwait(false);
    }
    finally { _gate.Release(); }
}

public async Task<int> GetIdleWarnThresholdMinutesAsync()
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        var mins = (await LoadAsync().ConfigureAwait(false)).IdleWarnThresholdMinutes;
        return mins <= 0 ? 15 : mins;   // guard against a bad stored value
    }
    finally { _gate.Release(); }
}

public async Task SetIdleWarnThresholdMinutesAsync(int minutes)
{
    if (minutes <= 0) minutes = 15;
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        var s = await LoadAsync().ConfigureAwait(false);
        await SaveAsync(s with { IdleWarnThresholdMinutes = minutes }).ConfigureAwait(false);
    }
    finally { _gate.Release(); }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AppSettingsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/AppSettings.cs src/ROROROblox.Core/IAppSettings.cs src/ROROROblox.Tests/AppSettingsTests.cs
git commit -m "feat(core): idle-alert mute + warn-threshold settings"
```

---

## Task 7: AccountSummary idle fields + amber brush converter + XAML chip

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs`
- Modify: `src/ROROROblox.App/Converters.cs`
- Modify: `src/ROROROblox.App/MainWindow.xaml`
- Test: `src/ROROROblox.Tests/AccountSummaryTests.cs` (add), `src/ROROROblox.Tests/ConvertersTests.cs` (add/create)

**Interfaces:**
- Produces on `AccountSummary`: `TimeSpan? SinceActivity` (settable), `bool IdleWarn` (settable), `string IdleText` (computed), `bool ShowIdleChip` (computed). Consumed by `ActivitySnapshotApplier` (Task 8) and the row XAML.

- [ ] **Step 1: Write the failing tests**

```csharp
// add to AccountSummaryTests.cs
    [Fact]
    public void IdleText_FormatsMinutes()
    {
        var s = NewSummary();
        s.IsRunning = true;
        s.SinceActivity = TimeSpan.FromMinutes(18);
        Assert.Equal("idle 18m", s.IdleText);
    }

    [Fact]
    public void IdleText_FormatsSecondsUnderAMinute()
    {
        var s = NewSummary();
        s.IsRunning = true;
        s.SinceActivity = TimeSpan.FromSeconds(45);
        Assert.Equal("idle 45s", s.IdleText);
    }

    [Fact]
    public void IdleText_FormatsHoursAndMinutes()
    {
        var s = NewSummary();
        s.IsRunning = true;
        s.SinceActivity = TimeSpan.FromMinutes(64);
        Assert.Equal("idle 1h4m", s.IdleText);
    }

    [Fact]
    public void ShowIdleChip_OnlyWhenRunningAndOverOneMinute()
    {
        var s = NewSummary();
        s.SinceActivity = TimeSpan.FromMinutes(5);
        s.IsRunning = false;
        Assert.False(s.ShowIdleChip);          // not running

        s.IsRunning = true;
        s.SinceActivity = TimeSpan.FromSeconds(30);
        Assert.False(s.ShowIdleChip);          // under 1 min

        s.SinceActivity = TimeSpan.FromMinutes(5);
        Assert.True(s.ShowIdleChip);
    }

    [Fact]
    public void SettingSinceActivity_RaisesIdleTextAndChipChange()
    {
        var s = NewSummary();
        s.IsRunning = true;
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.SinceActivity = TimeSpan.FromMinutes(3);

        Assert.Contains(nameof(AccountSummary.IdleText), raised);
        Assert.Contains(nameof(AccountSummary.ShowIdleChip), raised);
    }
```

```csharp
// src/ROROROblox.Tests/ConvertersTests.cs  (create if absent)
using System.Globalization;
using System.Windows.Media;
using ROROROblox.App;
using Xunit;

namespace ROROROblox.Tests;

public class ConvertersTests
{
    [Fact]
    public void IdleChipBrush_WarnTrue_ReturnsAmber()
    {
        var conv = new IdleChipBrushConverter();
        var brush = (SolidColorBrush)conv.Convert(true, typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(0xF1, 0xB2, 0x32), brush.Color);
    }

    [Fact]
    public void IdleChipBrush_WarnFalse_ReturnsMuted()
    {
        var conv = new IdleChipBrushConverter();
        var brush = (SolidColorBrush)conv.Convert(false, typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(0x8A, 0x93, 0xA0), brush.Color);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AccountSummaryTests|FullyQualifiedName~ConvertersTests"`
Expected: FAIL — members/converter not defined.

- [ ] **Step 3: Implement the AccountSummary fields**

```csharp
// backing fields
private TimeSpan? _sinceActivity;
private bool _idleWarn;

public TimeSpan? SinceActivity
{
    get => _sinceActivity;
    set
    {
        if (SetField(ref _sinceActivity, value))
        {
            OnPropertyChanged(nameof(IdleText));
            OnPropertyChanged(nameof(ShowIdleChip));
        }
    }
}

public bool IdleWarn
{
    get => _idleWarn;
    set => SetField(ref _idleWarn, value);
}

public bool ShowIdleChip =>
    _isRunning && _sinceActivity is TimeSpan t && t >= TimeSpan.FromMinutes(1);

public string IdleText
{
    get
    {
        if (_sinceActivity is not TimeSpan t) return string.Empty;
        if (t < TimeSpan.FromMinutes(1)) return $"idle {(int)t.TotalSeconds}s";
        if (t < TimeSpan.FromHours(1)) return $"idle {(int)t.TotalMinutes}m";
        return $"idle {(int)t.TotalHours}h{t.Minutes}m";
    }
}
```

Also make `IsRunning`'s setter notify the chip (find the existing `IsRunning` setter and add the notifications, mirroring how other computed props are notified):

```csharp
// in the IsRunning setter, after SetField(...) succeeds, add:
OnPropertyChanged(nameof(ShowIdleChip));
```

- [ ] **Step 4: Implement the converter**

Add to `Converters.cs`:

```csharp
internal sealed class IdleChipBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Amber =
        new(System.Windows.Media.Color.FromRgb(0xF1, 0xB2, 0x32));
    private static readonly System.Windows.Media.SolidColorBrush Muted =
        new(System.Windows.Media.Color.FromRgb(0x8A, 0x93, 0xA0));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Amber : Muted;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

(If the converter test can't see `IdleChipBrushConverter` due to `internal`, ensure `ROROROblox.App` exposes internals to tests — check for an existing `[assembly: InternalsVisibleTo("ROROROblox.Tests")]`; the existing `StatusDotBrushConverter` tests, if any, confirm the seam. If none, the converter tests can be skipped in favor of a manual check, but prefer wiring `InternalsVisibleTo`.)

- [ ] **Step 5: Add the chip to the row XAML**

In `MainWindow.xaml`, after the `SecondaryStatusText` `TextBlock` in the row template, add:

```xaml
<Border Visibility="{Binding ShowIdleChip, Converter={StaticResource BoolToVisibilityConverter}}"
        Background="Transparent"
        Margin="8,0,0,0"
        VerticalAlignment="Center">
    <TextBlock Text="{Binding IdleText}"
               FontSize="11"
               Foreground="{Binding IdleWarn, Converter={StaticResource IdleChipBrushConverter}}" />
</Border>
```

Register the converter in the window/app resources next to `StatusDotBrushConverter`:

```xaml
<local:IdleChipBrushConverter x:Key="IdleChipBrushConverter" />
```

(Use the existing bool→visibility converter key already in the resource dictionary; if the project uses a different name than `BoolToVisibilityConverter`, use that. Grep the XAML for the existing visibility converter key.)

- [ ] **Step 6: Run tests + build**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AccountSummaryTests|FullyQualifiedName~ConvertersTests"`
Then: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx`
Expected: tests PASS; build succeeds (XAML compiles).

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/ViewModels/AccountSummary.cs src/ROROROblox.App/Converters.cs src/ROROROblox.App/MainWindow.xaml src/ROROROblox.Tests/AccountSummaryTests.cs src/ROROROblox.Tests/ConvertersTests.cs
git commit -m "feat(app): AccountSummary idle chip fields + amber brush + row XAML"
```

---

## Task 8: ActivitySnapshotApplier — apply snapshot to rows

**Files:**
- Create: `src/ROROROblox.App/ViewModels/ActivitySnapshotApplier.cs`
- Test: `src/ROROROblox.Tests/ActivitySnapshotApplierTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<AccountActivity>` (Task 2), `AccountSummary` idle setters (Task 7).
- Produces: `static void ActivitySnapshotApplier.Apply(IEnumerable<AccountSummary> rows, IReadOnlyList<AccountActivity> snapshot, TimeSpan warnThreshold)`.

- [ ] **Step 1: Write the failing test**

```csharp
// src/ROROROblox.Tests/ActivitySnapshotApplierTests.cs
using System;
using System.Collections.Generic;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class ActivitySnapshotApplierTests
{
    private static AccountSummary Row(Guid id)
    {
        var account = new Account(id, "Alt", "https://x/a.png", DateTimeOffset.UtcNow, null, 1L);
        return new AccountSummary(account) { IsRunning = true };
    }

    [Fact]
    public void Apply_MatchingId_SetsSinceAndWarn()
    {
        var id = Guid.NewGuid();
        var row = Row(id);
        var snap = new List<AccountActivity>
        {
            new(id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(20)),
        };

        ActivitySnapshotApplier.Apply(new[] { row }, snap, TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(20), row.SinceActivity);
        Assert.True(row.IdleWarn);
    }

    [Fact]
    public void Apply_BelowThreshold_WarnFalse()
    {
        var id = Guid.NewGuid();
        var row = Row(id);
        var snap = new List<AccountActivity>
        {
            new(id, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)),
        };

        ActivitySnapshotApplier.Apply(new[] { row }, snap, TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(5), row.SinceActivity);
        Assert.False(row.IdleWarn);
    }

    [Fact]
    public void Apply_UnknownRow_ClearsIdle()
    {
        var row = Row(Guid.NewGuid());
        row.SinceActivity = TimeSpan.FromMinutes(30);
        row.IdleWarn = true;

        ActivitySnapshotApplier.Apply(new[] { row }, new List<AccountActivity>(), TimeSpan.FromMinutes(15));

        Assert.Null(row.SinceActivity);
        Assert.False(row.IdleWarn);
    }
}
```

(Match the real `Account` record ctor — the extraction shows positional `Account(Id, DisplayName, AvatarUrl, CreatedAt, LastLaunchedAt, RobloxUserId)`. Adjust if the signature differs.)

- [ ] **Step 2: Run to verify it fails**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivitySnapshotApplierTests"`
Expected: FAIL — `ActivitySnapshotApplier` not defined.

- [ ] **Step 3: Implement**

```csharp
// src/ROROROblox.App/ViewModels/ActivitySnapshotApplier.cs
using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.ViewModels;

/// <summary>Projects an ActivityMonitor snapshot onto the visible account rows. Pure.</summary>
public static class ActivitySnapshotApplier
{
    public static void Apply(
        IEnumerable<AccountSummary> rows,
        IReadOnlyList<AccountActivity> snapshot,
        TimeSpan warnThreshold)
    {
        var byId = new Dictionary<Guid, AccountActivity>(snapshot.Count);
        foreach (var a in snapshot) byId[a.AccountId] = a;

        foreach (var row in rows)
        {
            if (byId.TryGetValue(row.Id, out var a))
            {
                row.SinceActivity = a.SinceActivity;
                row.IdleWarn = a.SinceActivity >= warnThreshold;
            }
            else
            {
                row.SinceActivity = null;
                row.IdleWarn = false;
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivitySnapshotApplierTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/ViewModels/ActivitySnapshotApplier.cs src/ROROROblox.Tests/ActivitySnapshotApplierTests.cs
git commit -m "feat(app): ActivitySnapshotApplier maps monitor snapshot to rows"
```

---

## Task 9: Tray toast + IdleAlertPresenter + IdleSummary

**Files:**
- Modify: `src/ROROROblox.Core/ITrayService.cs`, `src/ROROROblox.App/Tray/TrayService.cs`
- Create: `src/ROROROblox.App/Notifications/IdleAlertPresenter.cs`, `src/ROROROblox.App/ViewModels/IdleSummary.cs`
- Test: `src/ROROROblox.Tests/IdleAlertPresenterTests.cs`, `IdleSummaryTests.cs`

**Interfaces:**
- Produces: `void ITrayService.ShowToast(string title, string message)`; `IdleAlertPresenter.Notify(int crossedCount, int thresholdMinutes, bool muted)`; `static string IdleSummary.Format(int count, int thresholdMinutes)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/ROROROblox.Tests/IdleAlertPresenterTests.cs
using System.Collections.Generic;
using ROROROblox.App.Notifications;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public class IdleAlertPresenterTests
{
    private sealed class FakeTray : ITrayService
    {
        public readonly List<(string title, string message)> Toasts = new();
        public void ShowToast(string title, string message) => Toasts.Add((title, message));

        // remaining ITrayService members — no-op stubs
        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void SetCustomStateIcons(System.Drawing.Icon? on, System.Drawing.Icon? off, System.Drawing.Icon? error) { }
        public void Dispose() { }
        public event System.EventHandler? RequestOpenMainWindow { add { } remove { } }
        public event System.EventHandler? RequestToggleMutex { add { } remove { } }
        public event System.EventHandler? RequestStopAllInstances { add { } remove { } }
        public event System.EventHandler? RequestQuit { add { } remove { } }
        public event System.EventHandler? RequestOpenDiagnostics { add { } remove { } }
        public event System.EventHandler? RequestOpenLogs { add { } remove { } }
        public event System.EventHandler? RequestOpenPreferences { add { } remove { } }
        public event System.EventHandler? RequestActivateMain { add { } remove { } }
        public event System.EventHandler? RequestOpenHistory { add { } remove { } }
        public event System.EventHandler? RequestOpenPlugins { add { } remove { } }
    }

    [Fact]
    public void Notify_Unmuted_MultipleAccounts_ShowsOneCoalescedToast()
    {
        var tray = new FakeTray();
        new IdleAlertPresenter(tray).Notify(crossedCount: 3, thresholdMinutes: 15, muted: false);

        var toast = Assert.Single(tray.Toasts);
        Assert.Contains("3 accounts", toast.message);
        Assert.Contains("15m", toast.message);
    }

    [Fact]
    public void Notify_Muted_ShowsNothing()
    {
        var tray = new FakeTray();
        new IdleAlertPresenter(tray).Notify(crossedCount: 3, thresholdMinutes: 15, muted: true);
        Assert.Empty(tray.Toasts);
    }

    [Fact]
    public void Notify_ZeroCount_ShowsNothing()
    {
        var tray = new FakeTray();
        new IdleAlertPresenter(tray).Notify(crossedCount: 0, thresholdMinutes: 15, muted: false);
        Assert.Empty(tray.Toasts);
    }
}
```

```csharp
// src/ROROROblox.Tests/IdleSummaryTests.cs
using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests;

public class IdleSummaryTests
{
    [Fact]
    public void Format_Zero_ReturnsEmpty() => Assert.Equal("", IdleSummary.Format(0, 15));

    [Fact]
    public void Format_One_Singular() =>
        Assert.Equal("1 account idle > 15m", IdleSummary.Format(1, 15));

    [Fact]
    public void Format_Many_Plural() =>
        Assert.Equal("3 accounts idle > 15m", IdleSummary.Format(3, 15));
}
```

(The `FakeTray` must implement the full `ITrayService` — the members above are copied from `TrayService`; if the real interface differs, match it. Ensure `ITrayService` includes the new `ShowToast` from Step 3 before compiling the test.)

- [ ] **Step 2: Run to verify they fail**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~IdleAlertPresenterTests|FullyQualifiedName~IdleSummaryTests"`
Expected: FAIL — `ShowToast` / `IdleAlertPresenter` / `IdleSummary` not defined.

- [ ] **Step 3: Add ShowToast to the tray**

In `ITrayService.cs` add:

```csharp
/// <summary>Show a passive, non-blocking notification (tray balloon). Used for idle warnings.</summary>
void ShowToast(string title, string message);
```

In `TrayService.cs` implement it via the Hardcodet balloon:

```csharp
public void ShowToast(string title, string message)
{
    if (_disposed) return;
    _taskbarIcon.ShowBalloonTip(title, message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
}
```

- [ ] **Step 4: Implement the presenter + summary**

```csharp
// src/ROROROblox.App/Notifications/IdleAlertPresenter.cs
using ROROROblox.Core;

namespace ROROROblox.App.Notifications;

/// <summary>Turns a coalesced warn-threshold crossing into one mutable tray toast.</summary>
public sealed class IdleAlertPresenter
{
    private readonly ITrayService _tray;
    public IdleAlertPresenter(ITrayService tray) => _tray = tray;

    public void Notify(int crossedCount, int thresholdMinutes, bool muted)
    {
        if (crossedCount <= 0 || muted) return;
        var msg = crossedCount == 1
            ? $"1 account idle > {thresholdMinutes}m — it may reconnect soon."
            : $"{crossedCount} accounts idle > {thresholdMinutes}m — they may reconnect together.";
        _tray.ShowToast("ROROROblox", msg);
    }
}
```

```csharp
// src/ROROROblox.App/ViewModels/IdleSummary.cs
namespace ROROROblox.App.ViewModels;

/// <summary>Formats the passive idle-summary banner strip. Empty when none.</summary>
public static class IdleSummary
{
    public static string Format(int count, int thresholdMinutes)
    {
        if (count <= 0) return string.Empty;
        var noun = count == 1 ? "account" : "accounts";
        return $"{count} {noun} idle > {thresholdMinutes}m";
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~IdleAlertPresenterTests|FullyQualifiedName~IdleSummaryTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/ITrayService.cs src/ROROROblox.App/Tray/TrayService.cs src/ROROROblox.App/Notifications/IdleAlertPresenter.cs src/ROROROblox.App/ViewModels/IdleSummary.cs src/ROROROblox.Tests/IdleAlertPresenterTests.cs src/ROROROblox.Tests/IdleSummaryTests.cs
git commit -m "feat(app): tray toast + idle-alert presenter + summary formatter"
```

---

## Task 10: MainViewModel wiring + composition + Preferences controls

Integration/glue: subscribe the monitor, refresh rows + banner on the existing ticker, toast on the edge event, read settings into cached fields, and add the Preferences controls. No new unit test (the pieces are tested in Tasks 7–9); verified by full build + all tests green + manual smoke.

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs` (register presenter; set monitor.WarnThreshold + push settings on preferences save)
- Modify: the Preferences window XAML + its VM/code-behind (mirror the existing `LaunchMainOnStartup` control)

**Interfaces:**
- Consumes: `IActivityMonitor` (Task 2/5), `IdleAlertPresenter` (Task 9), `ActivitySnapshotApplier` (Task 8), `IdleSummary` (Task 9), `IAppSettings` idle getters (Task 6).
- Produces on `MainViewModel`: `string IdleSummaryText { get; }` bound to the summary strip.

- [ ] **Step 1: Add the monitor + presenter to MainViewModel**

Add ctor parameters (append to the existing long ctor, before the trailing optional `ILogger`):

```csharp
IActivityMonitor activityMonitor,
Notifications.IdleAlertPresenter idleAlertPresenter,
```

Add fields + the bound summary property + cached settings:

```csharp
private readonly IActivityMonitor _activityMonitor;
private readonly Notifications.IdleAlertPresenter _idleAlertPresenter;
private int _idleWarnThresholdMinutes = 15;
private bool _muteIdleAlerts;

private string _idleSummaryText = string.Empty;
public string IdleSummaryText
{
    get => _idleSummaryText;
    private set { if (SetField(ref _idleSummaryText, value)) { } }
}
```

Assign in the ctor body and subscribe:

```csharp
_activityMonitor = activityMonitor;
_idleAlertPresenter = idleAlertPresenter;
_activityMonitor.WarnThresholdCrossed += OnActivityWarnCrossed;
```

Add the handler (dispatcher-marshalled, mirroring `OnAccountSessionLimited`):

```csharp
private void OnActivityWarnCrossed(object? sender, IReadOnlyList<Guid> crossed)
{
    Application.Current?.Dispatcher.Invoke(() =>
    {
        _idleAlertPresenter.Notify(crossed.Count, _idleWarnThresholdMinutes, _muteIdleAlerts);
    });
}
```

In the existing 30s `_ticker.Tick` handler, after the relative-time refresh, add the row + banner refresh:

```csharp
ActivitySnapshotApplier.Apply(Accounts, _activityMonitor.GetSnapshot(),
    TimeSpan.FromMinutes(_idleWarnThresholdMinutes));
IdleSummaryText = IdleSummary.Format(Accounts.Count(a => a.IdleWarn), _idleWarnThresholdMinutes);
```

Add a small initializer the composition root calls after building the VM (so cached settings are loaded once):

```csharp
public async Task InitializeIdleSettingsAsync(IAppSettings settings)
{
    _idleWarnThresholdMinutes = await settings.GetIdleWarnThresholdMinutesAsync().ConfigureAwait(false);
    _muteIdleAlerts = await settings.GetMuteIdleAlertsAsync().ConfigureAwait(false);
    _activityMonitor.WarnThreshold = TimeSpan.FromMinutes(_idleWarnThresholdMinutes);
}
```

- [ ] **Step 2: Register the presenter + push settings in App.xaml.cs**

Add registration next to the other singletons:

```csharp
services.AddSingleton<Notifications.IdleAlertPresenter>();
```

After the VM is resolved and settings are available (near the Task 5 monitor wiring), call the initializer, and re-push on preferences save:

```csharp
await mainViewModel.InitializeIdleSettingsAsync(settings);

// wherever the Preferences dialog signals a save/close, re-read + push:
// (if preferences already trigger a settings re-read, hook there instead)
async void RefreshIdlePrefs()
{
    var mins = await settings.GetIdleWarnThresholdMinutesAsync();
    monitor.WarnThreshold = TimeSpan.FromMinutes(mins);
    await mainViewModel.InitializeIdleSettingsAsync(settings);
}
```

(Adapt to the app's actual preferences-save signal. If there is a `RequestOpenPreferences` → dialog → save flow, call `RefreshIdlePrefs()` after the dialog closes.)

- [ ] **Step 3: Add the idle summary strip to MainWindow.xaml**

Near the existing `StatusBanner` element, add a passive strip bound to `IdleSummaryText`, visible when non-empty (use the existing string-to-visibility converter, or a `Style` with a `DataTrigger` on empty string — mirror how `StatusBanner` shows/hides):

```xaml
<TextBlock Text="{Binding IdleSummaryText}"
           FontSize="11"
           Margin="0,2,0,0"
           Foreground="{DynamicResource MutedTextBrush}">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IdleSummaryText}" Value="">
                    <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

- [ ] **Step 4: Add the Preferences controls**

In the Preferences window, mirror the existing `LaunchMainOnStartup` checkbox for a "Mute idle alerts" checkbox bound to `MuteIdleAlerts`, and add a numeric input (or a preset combo: 10 / 12 / 15 / 18 minutes) bound to `IdleWarnThresholdMinutes`. Wire their save to `SetMuteIdleAlertsAsync` / `SetIdleWarnThresholdMinutesAsync` exactly as the existing preference persists `LaunchMainOnStartup`. (Read the Preferences window first; follow its established pattern — do not invent a new settings surface.)

- [ ] **Step 5: Build + full test run + manual smoke**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx`
Then: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/`
Expected: build succeeds; all tests green. Manual smoke (per spec §9): launch app, run an account, leave it idle, confirm the chip counts up, crosses to amber at threshold, one toast fires (and is silent when muted).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.App/MainWindow.xaml
# plus the Preferences window files touched
git commit -m "feat(app): wire idle awareness into MainViewModel + preferences (toast, chip, banner)"
```

---

## Task 11: Plugin contract — AccountActivity messages + GetAccountActivity RPC

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`
- Modify: `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` (`<Version>` bump)

**Interfaces:**
- Produces (generated): `RoRoRoHost.RoRoRoHostClient.GetAccountActivityAsync(Empty)`, messages `AccountActivity`, `AccountActivityList`, and the server base method `GetAccountActivity(Empty, ServerCallContext)`.

- [ ] **Step 1: Add the messages + RPC to the proto**

In the `service RoRoRoHost { ... }` block, add under the query surface (next to `GetCurrentServer`):

```protobuf
  // Query surface (additive, NuGet 0.3.0): per-account idle awareness.
  rpc GetAccountActivity(Empty) returns (AccountActivityList);
```

Add the messages (near `CurrentServer`):

```protobuf
message AccountActivity {
  string account_id = 1;
  int64  last_activity_unix_ms = 2;
  int64  seconds_since_activity = 3;
}

message AccountActivityList {
  repeated AccountActivity items = 1;
}
```

- [ ] **Step 2: Bump the contract version**

In `ROROROblox.PluginContract.csproj`:

```xml
<Version>0.3.0</Version>
```

- [ ] **Step 3: Build the contract project to run codegen**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build src/ROROROblox.PluginContract/`
Expected: PASS — Grpc.Tools regenerates the client/server stubs with `GetAccountActivity` and the new messages.

- [ ] **Step 4: Verify generated types resolve**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx`
Expected: PASS (the new generated types compile across the solution). This is the additive-contract proof; no unit test needed at the proto layer — Task 15 exercises it end to end.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.PluginContract/Protos/plugin_contract.proto src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj
git commit -m "feat(contract): GetAccountActivity RPC + AccountActivity messages (0.2.0->0.3.0)"
```

---

## Task 12: Capability + method map + author-guide doc

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginCapability.cs`
- Modify: `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`
- Modify: `docs/plugins/AUTHOR_GUIDE.md`
- Test: `src/ROROROblox.Tests/PluginCapabilityTests.cs` (add/create), `RpcMethodCapabilityMapTests.cs` (add/create)

**Interfaces:**
- Produces: `PluginCapability.HostQueriesAccountActivity = "host.queries.account-activity"` (in the catalog with a description); `RpcMethodCapabilityMap["GetAccountActivity"] == HostQueriesAccountActivity`.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/ROROROblox.Tests/PluginCapabilityTests.cs  (create if absent)
using ROROROblox.App.Plugins;
using Xunit;

namespace ROROROblox.Tests;

public class PluginCapabilityTests
{
    [Fact]
    public void AccountActivity_HasCapabilityConstAndDescription()
    {
        Assert.Equal("host.queries.account-activity", PluginCapability.HostQueriesAccountActivity);
        var desc = PluginCapability.Describe(PluginCapability.HostQueriesAccountActivity);
        Assert.False(string.IsNullOrWhiteSpace(desc));
        Assert.Contains("idle", desc, System.StringComparison.OrdinalIgnoreCase);
    }
}
```

```csharp
// src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs  (create if absent)
using ROROROblox.App.Plugins;
using Xunit;

namespace ROROROblox.Tests;

public class RpcMethodCapabilityMapTests
{
    [Fact]
    public void GetAccountActivity_RequiresActivityCapability()
    {
        Assert.Equal(PluginCapability.HostQueriesAccountActivity,
            RpcMethodCapabilityMap.RequiredCapabilityFor("GetAccountActivity"));
    }
}
```

(Match the real accessor names — the extraction shows a private `Catalog` dictionary and a private `Map`. If there's no public `Describe`/`RequiredCapabilityFor`, add thin public accessors, or assert against whatever public lookup already exists. Prefer adding minimal public read accessors if needed for the test.)

- [ ] **Step 2: Run to verify they fail**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginCapabilityTests|FullyQualifiedName~RpcMethodCapabilityMapTests"`
Expected: FAIL.

- [ ] **Step 3: Add the capability const + catalog entry**

In `PluginCapability.cs`:

```csharp
public const string HostQueriesAccountActivity = "host.queries.account-activity";
```

In the `Catalog` dictionary initializer, add:

```csharp
[HostQueriesAccountActivity] = "See how long each account has been idle — timestamps only, never what you type or do.",
```

If no public description accessor exists, add:

```csharp
public static string Describe(string capability)
    => Catalog.TryGetValue(capability, out var d) ? d : string.Empty;
```

- [ ] **Step 4: Add the map entry**

In `RpcMethodCapabilityMap.cs`, add to the `Map` dictionary:

```csharp
["GetAccountActivity"] = PluginCapability.HostQueriesAccountActivity,
```

If no public accessor exists, add:

```csharp
public static string? RequiredCapabilityFor(string method)
    => Map.TryGetValue(method, out var cap) ? cap : null;
```

- [ ] **Step 5: Document in AUTHOR_GUIDE.md**

Add a row/entry to the capability vocabulary section:

```markdown
- `host.queries.account-activity` — pull per-account idle time (`GetAccountActivity` → `AccountActivityList`). Timestamps only; never keystroke content. Consent-gated. Added in contract 0.3.0. Use it to drive keep-active policy without the host acting on the client.
```

- [ ] **Step 6: Run to verify they pass**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginCapabilityTests|FullyQualifiedName~RpcMethodCapabilityMapTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginCapability.cs src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs docs/plugins/AUTHOR_GUIDE.md src/ROROROblox.Tests/PluginCapabilityTests.cs src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs
git commit -m "feat(app): host.queries.account-activity capability + method-map + author guide"
```

---

## Task 13: Activity snapshot adapter for the plugin host

**Files:**
- Create: `src/ROROROblox.App/Plugins/IActivitySnapshotProvider.cs`, `ActivitySnapshotProvider.cs`
- Test: `src/ROROROblox.Tests/ActivitySnapshotProviderTests.cs`

**Interfaces:**
- Consumes: `IActivityMonitor.GetSnapshot()` (Task 2), `IClock` (Task 2).
- Produces: `IActivitySnapshotProvider { IReadOnlyList<AccountActivitySnapshot> Snapshot(); }` + `record AccountActivitySnapshot(string AccountId, long LastActivityUnixMs, long SecondsSinceActivity)`. Consumed by `PluginHostService` (Task 14).

- [ ] **Step 1: Write the failing test**

```csharp
// src/ROROROblox.Tests/ActivitySnapshotProviderTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class ActivitySnapshotProviderTests
{
    private sealed class StubMonitor : IActivityMonitor
    {
        public List<AccountActivity> Items = new();
        public TimeSpan WarnThreshold { get; set; } = TimeSpan.FromMinutes(15);
        public event EventHandler<IReadOnlyList<Guid>>? WarnThresholdCrossed { add { } remove { } }
        public void OnAccountLaunched(Guid accountId) { }
        public void OnAccountExited(Guid accountId) { }
        public void Sample() { }
        public void Start() { }
        public void Stop() { }
        public IReadOnlyList<AccountActivity> GetSnapshot() => Items;
    }

    [Fact]
    public void Snapshot_ProjectsGuidToStringAndTimesToUnixAndSeconds()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var monitor = new StubMonitor
        {
            Items = { new AccountActivity(id, at, TimeSpan.FromSeconds(300)) },
        };
        var provider = new ActivitySnapshotProvider(monitor);

        var item = provider.Snapshot().Single();
        Assert.Equal(id.ToString(), item.AccountId);
        Assert.Equal(at.ToUnixTimeMilliseconds(), item.LastActivityUnixMs);
        Assert.Equal(300, item.SecondsSinceActivity);
    }

    [Fact]
    public void Snapshot_ClampsNegativeSecondsToZero()
    {
        var monitor = new StubMonitor
        {
            Items = { new AccountActivity(Guid.NewGuid(), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(-5)) },
        };
        var provider = new ActivitySnapshotProvider(monitor);
        Assert.Equal(0, provider.Snapshot().Single().SecondsSinceActivity);
    }
}
```

(If `IActivityMonitor`'s member set differs by the time this task runs — e.g. `Start`/`Stop` were added in Task 5 — keep the stub in sync with the interface.)

- [ ] **Step 2: Run to verify it fails**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivitySnapshotProviderTests"`
Expected: FAIL — provider not defined.

- [ ] **Step 3: Implement**

```csharp
// src/ROROROblox.App/Plugins/IActivitySnapshotProvider.cs
using System.Collections.Generic;

namespace ROROROblox.App.Plugins;

public sealed record AccountActivitySnapshot(string AccountId, long LastActivityUnixMs, long SecondsSinceActivity);

/// <summary>Plugin-facing projection of the ActivityMonitor snapshot. Mirrors IRunningAccountsProvider.</summary>
public interface IActivitySnapshotProvider
{
    IReadOnlyList<AccountActivitySnapshot> Snapshot();
}
```

```csharp
// src/ROROROblox.App/Plugins/ActivitySnapshotProvider.cs
using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.Plugins;

public sealed class ActivitySnapshotProvider : IActivitySnapshotProvider
{
    private readonly IActivityMonitor _monitor;
    public ActivitySnapshotProvider(IActivityMonitor monitor) => _monitor = monitor;

    public IReadOnlyList<AccountActivitySnapshot> Snapshot()
    {
        var snap = _monitor.GetSnapshot();
        var list = new List<AccountActivitySnapshot>(snap.Count);
        foreach (var a in snap)
        {
            var seconds = (long)a.SinceActivity.TotalSeconds;
            if (seconds < 0) seconds = 0;
            list.Add(new AccountActivitySnapshot(
                a.AccountId.ToString(),
                a.LastActivityAt.ToUnixTimeMilliseconds(),
                seconds));
        }
        return list;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ActivitySnapshotProviderTests"`
Expected: PASS.

- [ ] **Step 5: Register + commit**

Add to `App.xaml.cs` registrations:

```csharp
services.AddSingleton<IActivitySnapshotProvider, ActivitySnapshotProvider>();
```

```bash
git add src/ROROROblox.App/Plugins/IActivitySnapshotProvider.cs src/ROROROblox.App/Plugins/ActivitySnapshotProvider.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/ActivitySnapshotProviderTests.cs
git commit -m "feat(app): IActivitySnapshotProvider adapter over ActivityMonitor"
```

---

## Task 14: PluginHostService.GetAccountActivity + ctor wiring

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs` (add ctor dep + RPC override)
- Modify: every `new PluginHostService(...)` site (composition root + `PluginTestHarness` fakes)

**Interfaces:**
- Consumes: `IActivitySnapshotProvider` (Task 13), generated `AccountActivityList`/`AccountActivity`/`Empty` (Task 11).
- Produces: `override Task<AccountActivityList> GetAccountActivity(Empty request, ServerCallContext context)`.

- [ ] **Step 1: Add the constructor dependency**

Add `IActivitySnapshotProvider activityProvider` to the `PluginHostService` ctor (append after `runningAccounts` to keep related reads together), a field, and the null-check assignment:

```csharp
private readonly IActivitySnapshotProvider _activityProvider;
// ... in ctor params:
IActivitySnapshotProvider activityProvider,
// ... in ctor body:
_activityProvider = activityProvider ?? throw new ArgumentNullException(nameof(activityProvider));
```

- [ ] **Step 2: Implement the RPC (mirror GetRunningAccounts)**

```csharp
public override Task<AccountActivityList> GetAccountActivity(Empty request, ServerCallContext context)
{
    var list = new AccountActivityList();
    foreach (var a in _activityProvider.Snapshot())
    {
        list.Items.Add(new AccountActivity
        {
            AccountId = a.AccountId,
            LastActivityUnixMs = a.LastActivityUnixMs,
            SecondsSinceActivity = a.SecondsSinceActivity,
        });
    }
    return Task.FromResult(list);
}
```

- [ ] **Step 3: Update all construction sites**

Find them: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` will now fail at each `new PluginHostService(...)`. Update:
- The composition root (App.xaml.cs / wherever the host service is built): pass `provider.GetRequiredService<IActivitySnapshotProvider>()`.
- Any other production construction site.

(The `PluginTestHarness` sites are updated in Task 15 with a stub provider.)

- [ ] **Step 4: Build**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build src/ROROROblox.App/`
Expected: PASS (production construction sites updated). The harness project may still fail to build until Task 15 — that's expected and fixed there.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginHostService.cs src/ROROROblox.App/App.xaml.cs
git commit -m "feat(app): implement GetAccountActivity host RPC + wire activity provider"
```

---

## Task 15: Integration test — consented vs denied GetAccountActivity

**Files:**
- Modify: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs` (add two tests + a stub provider; update the `new PluginHostService(...)` calls with the new arg)

**Interfaces:**
- Consumes: the real named-pipe host + `RoRoRoHost.RoRoRoHostClient.GetAccountActivityAsync` (Task 11), `IActivitySnapshotProvider` (Task 13), the new host ctor (Task 14).

- [ ] **Step 1: Add a stub provider + the two tests**

```csharp
// in EndToEndContractTests.cs

private sealed class StubActivityProvider : ROROROblox.App.Plugins.IActivitySnapshotProvider
{
    private readonly System.Collections.Generic.IReadOnlyList<ROROROblox.App.Plugins.AccountActivitySnapshot> _items;
    public StubActivityProvider(params ROROROblox.App.Plugins.AccountActivitySnapshot[] items) => _items = items;
    public System.Collections.Generic.IReadOnlyList<ROROROblox.App.Plugins.AccountActivitySnapshot> Snapshot() => _items;
}

[Fact]
public async Task GetAccountActivity_ConsentedPlugin_ReturnsSnapshot()
{
    var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
    var accountId = Guid.NewGuid().ToString();

    var registry = new SingleInstalledPluginLookup(new InstalledPlugin
    {
        Manifest = new PluginManifest
        {
            SchemaVersion = 1, Id = "626labs.test", Name = "Test", Version = "1.0",
            ContractVersion = "1.0", Publisher = "626", Description = "x",
            Capabilities = new[] { "host.queries.account-activity" },
        },
        InstallDir = Path.GetTempPath(),
        Consent = new ConsentRecord
        {
            PluginId = "626labs.test",
            GrantedCapabilities = new[] { "host.queries.account-activity" },
            AutostartEnabled = false,
        },
    });

    var hostService = new PluginHostService(
        registry, "1.4.0", "1.0",
        new FixedHostState("On"),
        new EmptyAccounts(),
        new InProcessPluginEventBus(),
        new NoOpLauncher(),
        new PluginUITranslator(new NullUIHost()),
        new StubActivityProvider(
            new ROROROblox.App.Plugins.AccountActivitySnapshot(accountId, 1_700_000_000_000, 300)));

    var interceptor = new CapabilityInterceptor(
        currentPluginAccessor: () => "626labs.test",
        consentLookup: id => new[] { "host.queries.account-activity" });

    var startup = new PluginHostStartupService(
        hostService, interceptor, NullLogger<PluginHostStartupService>.Instance, pipeName);
    await startup.StartAsync(CancellationToken.None);
    try
    {
        using var channel = ConnectChannel(pipeName);
        var client = new RoRoRoHost.RoRoRoHostClient(channel);

        var resp = await client.GetAccountActivityAsync(new Empty());

        var item = Assert.Single(resp.Items);
        Assert.Equal(accountId, item.AccountId);
        Assert.Equal(300, item.SecondsSinceActivity);
    }
    finally
    {
        await startup.StopAsync(CancellationToken.None);
        await startup.DisposeAsync();
    }
}

[Fact]
public async Task GetAccountActivity_DeniedWhenCapabilityNotGranted()
{
    var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";

    var registry = new SingleInstalledPluginLookup(new InstalledPlugin
    {
        Manifest = new PluginManifest
        {
            SchemaVersion = 1, Id = "626labs.test", Name = "Test", Version = "1.0",
            ContractVersion = "1.0", Publisher = "626", Description = "x",
            Capabilities = new[] { "host.events.account-launched" }, // NOT account-activity
        },
        InstallDir = Path.GetTempPath(),
        Consent = new ConsentRecord
        {
            PluginId = "626labs.test",
            GrantedCapabilities = new[] { "host.events.account-launched" },
            AutostartEnabled = false,
        },
    });

    var hostService = new PluginHostService(
        registry, "1.4.0", "1.0",
        new FixedHostState("On"),
        new EmptyAccounts(),
        new InProcessPluginEventBus(),
        new NoOpLauncher(),
        new PluginUITranslator(new NullUIHost()),
        new StubActivityProvider());

    var interceptor = new CapabilityInterceptor(
        currentPluginAccessor: () => "626labs.test",
        consentLookup: id => new[] { "host.events.account-launched" });

    var startup = new PluginHostStartupService(
        hostService, interceptor, NullLogger<PluginHostStartupService>.Instance, pipeName);
    await startup.StartAsync(CancellationToken.None);
    try
    {
        using var channel = ConnectChannel(pipeName);
        var client = new RoRoRoHost.RoRoRoHostClient(channel);

        var ex = await Assert.ThrowsAsync<RpcException>(
            async () => await client.GetAccountActivityAsync(new Empty()));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }
    finally
    {
        await startup.StopAsync(CancellationToken.None);
        await startup.DisposeAsync();
    }
}
```

- [ ] **Step 2: Update any other PluginHostService construction in the harness**

Every existing `new PluginHostService(...)` in the harness needs the new `StubActivityProvider()` arg appended (matching the Task 14 ctor order). Build to find them:

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build src/ROROROblox.PluginTestHarness/`
Fix each construction site, then rebuild until it compiles.

- [ ] **Step 3: Run the integration tests**

Run: `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test src/ROROROblox.PluginTestHarness/`
Expected: PASS (both new tests + all existing).

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs
git commit -m "test(harness): GetAccountActivity consented + PermissionDenied integration"
```

---

## Final verification (after all tasks)

- [ ] **Whole solution build:** `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" build ROROROblox.slnx` — green.
- [ ] **Whole solution tests:** `& "%USERPROFILE%\AppData\Local\Microsoft\dotnet\dotnet.exe" test ROROROblox.slnx` — unit + integration green.
- [ ] **Local-path audit** (per keystone, before any push): grep the diff for hardcoded absolute user-home path references (`%USERPROFILE%`-style) in committable code — none.
- [ ] **Wall audit:** grep the new code for `SendInput` / `SetForegroundWindow` / focus / input synthesis — none in core (read-only observation only).
- [ ] **Manual smoke** (spec §9): run 2 accounts, idle one, watch the chip count up + go amber at threshold, confirm one coalesced toast (silent when muted), confirm the summary strip shows the count.

---

## Self-review notes (author)

**Spec coverage:** §3 signal → Tasks 2, 5. §4 monitor + reverse resolver → Tasks 1, 2, 5. §5 data flow (launch/exit seed, 1s sample, snapshot pull) → Tasks 2, 5, 8, 10. §6 notify (row chip, banner, toast, threshold, mute) → Tasks 6, 7, 9, 10. §7 plugin contract (proto, capability, map, host, versioning, author guide) → Tasks 11–15. §8 error handling (non-tracked foreground, mid-exit, tick-wrap-as-no-advance, empty snapshot, clamp ≥0, PermissionDenied) → Tasks 2, 8, 13, 15. §9 testing → every task's test block + final smoke. §10 scope boundary → nothing here acts on a client; verified in final wall audit.

**Type consistency:** `AccountActivity` (Core record struct) vs `AccountActivity` (proto message) never share a file — the App `AccountActivitySnapshot` sits between them (Task 13 maps Core→App; Task 14 maps App→proto). `IActivityMonitor` member set is defined in Task 2 and extended in Task 5 (`Start`/`Stop`); stubs in Tasks 13 sync to the full interface. `WarnThreshold` is the single threshold source, set from `IdleWarnThresholdMinutes` (Task 6) in Task 10.

**Known adaptation points flagged inline** (real signatures the implementer confirms against the codebase): `RobloxProcessTracker` ctor, `RobloxProcessEventArgs.AccountId`, CsWin32 `GetWindowThreadProcessId` overload shape, `AppSettings` test seam, the Preferences window pattern, `InternalsVisibleTo` for converter tests, and `PluginCapability`/`RpcMethodCapabilityMap` public accessors.
