# Memory Watchdog + Warning System Implementation Plan

> ## ⚠️ EXECUTED 2026-08-01 — this plan was WRONG in four places
>
> All ten tasks shipped on `feat/memory-watchdog`. Every implementation was correct on the first
> pass. **Every defect the reviews caught was in this document, not in the code written from it.**
> Corrected here rather than rewritten in place, so the mistakes stay legible.
>
> 1. **Task 6's Step 3 logging snippet defeats Task 6's own stated purpose.** It logs `accounts.Count`
>    and the *summed* growth rate and never iterates `accounts` — so with three clients the log reads
>    "3 clients, 920 MB/hr total" and you cannot tell which one is ballooning. The whole justification
>    for the task was "a user's log should contain the curve"; the snippet emits a scalar. It passed
>    both cadence tests. **Shipped code carries a per-client payload inside the same single 15-minute
>    call**, with unreadable accounts tagged `(stale)` so a stale last-known-good is never mistaken for
>    a fresh reading.
> 2. **Task 8's step list never wired anything.** Its own Interfaces block declared it consumes
>    `IMemoryWatchdog.PressureCrossed`; steps 1-6 never subscribed to it, and no later task closed the
>    loop. The tray warning surface, the balloon, `RequestFocusAccount`, and `RecycleAccountCommand`
>    all shipped **built but unreachable, with every test green** — a client could leak to the cap and
>    the user would never see a warning or a way to fix it. Shipped code wires all of it, marshals the
>    tray writes to the UI thread (`PressureCrossed` is raised from a `Timer` callback), and clears the
>    warning via `MemoryPressureEvaluator.IsClear` — the plan never specified that the edge-triggered
>    warning needs a way to turn *off*.
> 3. **Task 10's contract version was stale on arrival.** This plan says bump 0.4.0 → 0.5.0; the
>    contract was already at **0.6.0** by execution time, so following it literally would have been a
>    rollback. Shipped as **0.7.0**. The proto sketch below also shows `SubscribeMemoryPressure(Empty)`
>    — shipped as `SubscriptionRequest`, matching the three pre-existing subscription rpcs. Free to fix
>    then, a breaking major bump to fix later.
> 4. **Several supplied tests could not fail.** Task 3's ratchet and clamp tests, Task 4's target and
>    coalescing coverage, Task 5's persistence gap, and Task 9's watchdog test were each satisfied
>    regardless of whether the behaviour they named existed. All replaced with discriminating versions,
>    each proven by deliberately breaking the production code and confirming RED.
>
> **The transferable lesson:** a reviewer that only checks "does the code match the brief" would have
> passed every one of these. Ask instead whether the output satisfies the *purpose*, and for every
> test name the production change that would make it fail.
>
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect Roblox client memory growth per account, project time to machine RAM exhaustion, warn the user before the wall, and offer one-click recycle back into the same game.

**Architecture:** A `MemoryWatchdog` in `ROROROblox.Core/Diagnostics/` that deliberately mirrors the existing `ActivityMonitor` — constructor-injected probes, an `Interlocked`-guarded `System.Threading.Timer`, a public `Sample()` test seam, and latch/re-arm threshold edges. It samples `PrivateMemorySize64` per tracked account every 30s, estimates a linear growth rate, and fires two independent triggers (per-client absolute cap, machine-wide headroom projection). The App layer subscribes and paints an account-row chip, a tray warning state, and a tray balloon; a Recycle command stops and relaunches one account to the same `LaunchTarget`. Plugin contract 0.5.0 exposes pressure events to plugins behind a consent-gated capability.

**Tech Stack:** .NET 10 / C# 14, WPF, xUnit, CsWin32 (`GlobalMemoryStatusEx`), Serilog, gRPC + protobuf (plugin contract).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-08-01-memory-watchdog-design.md`. Read it before starting. When build reality diverges, **banner-correct the spec — never rewrite it top-to-bottom.**
- **Metric is `Process.PrivateMemorySize64`. Never `WorkingSet64`.** Windows trims working sets of minimized windows, and most alts are minimized for the whole session.
- **All memory arithmetic in bytes and bytes-per-hour.** `*Mb` settings convert at the boundary (`× 1024 × 1024`), never mixed raw into a formula.
- **An unreadable value must never impersonate a benign one.** An unreadable pid is excluded from aggregates, never counted as zero. Zero understates growth and delays the warning — the dangerous direction. This is the same class of defect as the `RobloxRunningProbe` `hasWindow` bug fixed in PR #68; write these with the same discipline.
- **Build with `dotnet build ROROROblox.slnx`.** `.slnx` is canonical. A stray `ROROROblox.sln` regenerated by Qodo IDE is gitignored, missing the PluginTestHarness project, and must never be built. Bare `dotnet build` errors MSB1011 while both exist.
- **Tests:** `dotnet test ROROROblox.slnx`. **No test may touch a real process or read real system memory.** Every probe is injected.
- **Close any running `ROROROblox.App` dev build before building** — it locks `ROROROblox.Core.dll` and `ROROROblox.PluginContract.dll` and the build fails with MSB3027.
- **Conventional commits** (`feat` / `fix` / `docs` / `refactor` / `test` / `chore` / `build` / `ci`).
- **Never log a `.ROBLOSECURITY` value or cookie.** Pre-commit hooks (`secret-scan`, `local-path-guard`) enforce this and must stay green.
- **No programmatic icon placeholders.** Tray artwork goes through the `626labs-design` skill (Task 8).
- **TDD is mandatory.** Every task: failing test → watch it fail for the right reason → minimal code → watch it pass → commit.

---

## File Structure

**Create:**

| File | Responsibility |
| --- | --- |
| `src/ROROROblox.Core/Diagnostics/IProcessMemoryProbe.cs` | Private bytes for a pid; test seam |
| `src/ROROROblox.Core/Diagnostics/ProcessMemoryProbe.cs` | Prod impl over `Process.PrivateMemorySize64` |
| `src/ROROROblox.Core/Diagnostics/ISystemMemoryProbe.cs` | Machine total + available physical RAM |
| `src/ROROROblox.Core/Diagnostics/SystemMemoryProbe.cs` | Prod impl over `GlobalMemoryStatusEx` |
| `src/ROROROblox.Core/Diagnostics/AccountMemory.cs` | Per-account reading record |
| `src/ROROROblox.Core/Diagnostics/MemoryPressureSnapshot.cs` | Aggregate machine-level record |
| `src/ROROROblox.Core/Diagnostics/IMemoryWatchdog.cs` | DI + ViewModel-facing interface |
| `src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs` | Sampling, growth, triggers, latching |
| `src/ROROROblox.Core/Diagnostics/MemoryDefaults.cs` | Pure derivation of settings from total RAM |
| `src/ROROROblox.Tests/MemoryWatchdogGrowthTests.cs` | Growth math, ratchet, window, unreadable pid |
| `src/ROROROblox.Tests/MemoryWatchdogTriggerTests.cs` | Cap, projection, latching, target selection |
| `src/ROROROblox.Tests/MemoryDefaultsTests.cs` | Derivation clamps and override-stickiness |
| `src/ROROROblox.Tests/MemoryWatchdogLoggingTests.cs` | Summary cadence, one-warning-per-crossing |
| `src/ROROROblox.Tests/AppLoggingVersionTests.cs` | Version reaches rendered output |

**Modify:**

| File | Change |
| --- | --- |
| `src/ROROROblox.App/Logging/AppLogging.cs` | `Configure(version)`, `{Version}` in template |
| `src/ROROROblox.App/App.xaml.cs` | Pass version to logging; register + wire watchdog |
| `src/ROROROblox.Core/NativeMethods.txt` | Add `GlobalMemoryStatusEx` |
| `src/ROROROblox.Core/AppSettings.cs`, `IAppSettings.cs` | Four new settings |
| `src/ROROROblox.Core/ITrayService.cs`, `src/ROROROblox.App/Tray/TrayService.cs` | Warning surface (separate from `MultiInstanceState`) |
| `src/ROROROblox.App/ViewModels/AccountSummary.cs` | Memory chip properties |
| `src/ROROROblox.App/ViewModels/MainViewModel.cs` | Subscribe, paint, Recycle command |
| `src/ROROROblox.App/MainWindow.xaml` | Row chip binding |
| `src/ROROROblox.Core/Diagnostics/DiagnosticsCollector.cs` + snapshot record | RAM + per-account memory |
| `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` | 0.5.0 additions |
| `src/ROROROblox.App/Plugins/IPluginEventBus.cs`, `InProcessPluginEventBus.cs`, `PluginCapability.cs`, `PluginHostService.cs` | Pressure event + capability |
| `docs/plugins/AUTHOR_GUIDE.md` | Pressure → recycle → macro recipe |

---

## Task 1: Log versioning

**Independently shippable. Merge this first — it has value with or without the watchdog, and every later task's logs benefit.**

**Files:**
- Modify: `src/ROROROblox.App/Logging/AppLogging.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs:49` and `:58`
- Test: `src/ROROROblox.Tests/AppLoggingVersionTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `AppLogging.Configure(string version)` — replaces the current no-arg `Configure()`. Returns `ILoggerFactory` as before.

**Why:** the version currently reaches the log on exactly one line — the startup banner. The sink rolls daily *and* at 25 MB, and a 20-30 hour session spans a day boundary by definition, so the file covering a failure is a rolled file with no version in it. Note `.Enrich.WithProperty("App", "ROROROblox")` is already enriched but absent from `outputTemplate` and therefore **never appears in the log** — that is the proof that enrichment alone is invisible.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using ROROROblox.App.Logging;
using Xunit;

namespace ROROROblox.Tests;

public class AppLoggingVersionTests
{
    // Asserts against RENDERED text, not enrichment. The existing "App" property is enriched but
    // missing from outputTemplate and never reaches the file — proof that asserting on enrichment
    // would pass while the log stayed unattributable.
    [Fact]
    public void Configure_WritesVersionIntoEveryRenderedLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rororo-logtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = AppLogging.Configure("9.9.9", dir);
            factory.CreateLogger("T").LogInformation("marker-line");
            AppLogging.Shutdown();

            var text = string.Join("\n", Directory.GetFiles(dir, "*.log").Select(File.ReadAllText));
            Assert.Contains("marker-line", text);
            Assert.Contains("9.9.9", text);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AppLoggingVersionTests"`
Expected: FAIL to compile — `Configure` takes no arguments. Add the parameters (Step 3) and re-run; it must then fail with the version assertion, **not** the `marker-line` one. If `marker-line` fails, the sink path is wrong — fix that before proceeding.

- [ ] **Step 3: Add the parameters and the template token**

In `AppLogging.cs`, make the log directory overridable for tests and thread the version through:

```csharp
private static string _logDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ROROROblox", "logs");

public static string LogDirectory => _logDirectory;
public static string LogFilePath => Path.Combine(_logDirectory, "rororoblox-.log");

public static ILoggerFactory Configure(string version, string? logDirectoryOverride = null)
{
    if (!string.IsNullOrWhiteSpace(logDirectoryOverride))
    {
        _logDirectory = logDirectoryOverride;
    }
    Directory.CreateDirectory(_logDirectory);

    var serilogLogger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.WithProperty("App", "ROROROblox")
        .Enrich.WithProperty("Version", version)
        .WriteTo.File(
            path: LogFilePath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 25 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            // {Version} MUST be in the template. Enrichment alone never reaches the file sink.
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] v{Version} {SourceContext} {Message:lj}{NewLine}{Exception}",
            restrictedToMinimumLevel: LogEventLevel.Debug)
        .CreateLogger();

    Log.Logger = serilogLogger;
    _factory = new SerilogLoggerFactory(serilogLogger, dispose: true);
    return _factory;
}
```

`retainedFileCountLimit` moves 14 → 30. It counts **files, not days**; with `rollOnFileSizeLimit` a heavy day burns several, so 14 could mean under 5 days of history — precisely when we are asking users for logs after a multi-day session.

- [ ] **Step 4: Update the call site**

`App.xaml.cs` — hoist the version above `Configure` (it is currently computed at line 58, after logging is configured at line 49):

```csharp
var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
_loggerFactory = AppLogging.Configure(version);
_log = _loggerFactory.CreateLogger<App>();
WireGlobalExceptionHandlers();
```

Then delete the now-duplicate `var version = ...` at old line 58, keeping the `LogInformation` banner as-is.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS. Baseline before this plan is 924 unit + 16 integration.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.App/Logging/AppLogging.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/AppLoggingVersionTests.cs
git commit -m "fix(logging): stamp the app version on every log line, not just the startup banner"
```

---

## Task 2: Memory probes

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/IProcessMemoryProbe.cs`, `ProcessMemoryProbe.cs`, `ISystemMemoryProbe.cs`, `SystemMemoryProbe.cs`
- Modify: `src/ROROROblox.Core/NativeMethods.txt`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `bool IProcessMemoryProbe.TryReadPrivateBytes(int pid, out long privateBytes)` — `false` on any failure. **Callers must never treat `false` as zero.**
  - `bool ISystemMemoryProbe.TryRead(out long totalPhysicalBytes, out long availablePhysicalBytes)` — `false` on failure.

- [ ] **Step 1: Write the interfaces**

`IProcessMemoryProbe.cs`:

```csharp
namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Private committed bytes for one process. Returns false rather than throwing or guessing —
/// a pid we cannot read is UNKNOWN, and callers must exclude it from aggregates rather than
/// substitute zero. Zero understates growth and delays the warning.
/// </summary>
public interface IProcessMemoryProbe
{
    bool TryReadPrivateBytes(int pid, out long privateBytes);
}
```

`ISystemMemoryProbe.cs`:

```csharp
namespace ROROROblox.Core.Diagnostics;

/// <summary>Machine-wide physical memory. Total drives derived settings defaults; available drives the projection.</summary>
public interface ISystemMemoryProbe
{
    bool TryRead(out long totalPhysicalBytes, out long availablePhysicalBytes);
}
```

- [ ] **Step 2: Add the Win32 import**

Append to `src/ROROROblox.Core/NativeMethods.txt`:

```text
GlobalMemoryStatusEx
MEMORYSTATUSEX
```

`GetPerformanceInfo` was considered and rejected in the spec — it exposes commit-limit and page-size detail this design does not use, for a wider P/Invoke surface.

- [ ] **Step 3: Write the production implementations**

`ProcessMemoryProbe.cs`:

```csharp
using System.Diagnostics;

namespace ROROROblox.Core.Diagnostics;

public sealed class ProcessMemoryProbe : IProcessMemoryProbe
{
    public bool TryReadPrivateBytes(int pid, out long privateBytes)
    {
        privateBytes = 0;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Refresh();
            privateBytes = p.PrivateMemorySize64;
            return true;
        }
        catch
        {
            // Exited, access denied, or gone mid-read. UNKNOWN — never a zero reading.
            return false;
        }
    }
}
```

`SystemMemoryProbe.cs`:

```csharp
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace ROROROblox.Core.Diagnostics;

public sealed class SystemMemoryProbe : ISystemMemoryProbe
{
    public bool TryRead(out long totalPhysicalBytes, out long availablePhysicalBytes)
    {
        totalPhysicalBytes = 0;
        availablePhysicalBytes = 0;
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!PInvoke.GlobalMemoryStatusEx(ref status))
            {
                return false;
            }
            totalPhysicalBytes = (long)status.ullTotalPhys;
            availablePhysicalBytes = (long)status.ullAvailPhys;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Build to verify the CsWin32 generation resolves**

Run: `dotnet build ROROROblox.slnx`
Expected: SUCCESS. If `MEMORYSTATUSEX` does not resolve, check the generated namespace CsWin32 chose and correct the `using` — do **not** hand-write the P/Invoke.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/IProcessMemoryProbe.cs src/ROROROblox.Core/Diagnostics/ProcessMemoryProbe.cs src/ROROROblox.Core/Diagnostics/ISystemMemoryProbe.cs src/ROROROblox.Core/Diagnostics/SystemMemoryProbe.cs src/ROROROblox.Core/NativeMethods.txt
git commit -m "feat(diagnostics): process + system memory probes for the watchdog"
```

---

## Task 3: Watchdog records and growth estimation

**Files:**
- Create: `AccountMemory.cs`, `MemoryPressureSnapshot.cs`, `IMemoryWatchdog.cs`, `MemoryWatchdog.cs` (all in `src/ROROROblox.Core/Diagnostics/`)
- Test: `src/ROROROblox.Tests/MemoryWatchdogGrowthTests.cs`

**Interfaces:**
- Consumes: `IProcessMemoryProbe`, `ISystemMemoryProbe` (Task 2); `IClock` (`DateTimeOffset UtcNow`).
- Produces:
  - `readonly record struct AccountMemory(Guid AccountId, long PrivateBytes, double GrowthBytesPerHour, int MinutesToCeiling, bool OverCap, bool IsTarget, bool ReadOk)`
  - `readonly record struct MemoryPressureSnapshot(long AvailableBytes, double AggregateGrowthBytesPerHour, int MinutesToCeiling, bool HasProjection, Guid? TargetAccountId, IReadOnlyList<AccountMemory> Accounts)`
  - `MemoryWatchdog.OnAccountLaunched(Guid, int pid)`, `.OnAccountExited(Guid)`, `.Sample()`, `.GetSnapshot()`, `.ResetBaseline(Guid, int pid)`
  - `MemoryWatchdog.MinimumObservation` — `TimeSpan.FromMinutes(10)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryWatchdogGrowthTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public readonly Dictionary<int, long?> Readings = new(); // null = unreadable
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            if (!Readings.TryGetValue(pid, out var v) || v is null) return false;
            privateBytes = v.Value;
            return true;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public long Total = 32L * 1024 * 1024 * 1024;
        public long Available = 20L * 1024 * 1024 * 1024;
        public bool Ok = true;
        public bool TryRead(out long total, out long available)
        {
            total = Total; available = Available;
            return Ok;
        }
    }

    private const long Gb = 1024L * 1024 * 1024;

    private static (MemoryWatchdog wd, FakeClock clock, FakeProcessMemory proc, FakeSystemMemory sys) Build()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock);
        return (wd, clock, proc, sys);
    }

    [Fact]
    public void Growth_IsBytesPerHourOverElapsed()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(2));
        proc.Readings[10] = 3 * Gb;           // +1 GB over 2 hours
        wd.Sample();

        var acct = Assert.Single(wd.GetSnapshot().Accounts);
        Assert.Equal(0.5 * Gb, acct.GrowthBytesPerHour, precision: 0);
    }

    [Fact]
    public void ObservationWindowUnmet_YieldsNoProjection()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromMinutes(5)); // under the 10-minute minimum
        proc.Readings[10] = 4 * Gb;
        wd.Sample();

        Assert.Equal(0, wd.GetSnapshot().MinutesToCeiling);
    }

    [Fact]
    public void ClientShrank_RatchetsBaselineAndRestartsWindow()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        proc.Readings[10] = 2 * Gb;  // teleport freed memory
        wd.Sample();

        // Baseline ratcheted to 2 GB and the window restarted, so no slope is claimed yet.
        var acct = Assert.Single(wd.GetSnapshot().Accounts);
        Assert.Equal(0, acct.GrowthBytesPerHour, precision: 0);
    }

    [Fact]
    public void UnreadablePid_IsExcludedFromAggregate_NotTreatedAsZero()
    {
        var (wd, clock, proc, _) = Build();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        proc.Readings[20] = 2 * Gb;
        wd.OnAccountLaunched(readable, pid: 10);
        wd.OnAccountLaunched(unreadable, pid: 20);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        proc.Readings[10] = 3 * Gb;   // +1 GB/hr
        proc.Readings[20] = null;     // now unreadable
        wd.Sample();

        var snap = wd.GetSnapshot();
        // Aggregate is the readable client's 1 GB/hr ONLY. A zero substituted for the
        // unreadable one would still be 1 GB/hr, so assert the flag too.
        Assert.Equal(1.0 * Gb, snap.AggregateGrowthBytesPerHour, precision: 0);
        Assert.False(Assert.Single(snap.Accounts, a => a.AccountId == unreadable).ReadOk);
    }

    [Fact]
    public void AccountExited_DropsTheRecord()
    {
        var (wd, _, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        wd.OnAccountExited(id);
        wd.Sample();

        Assert.Empty(wd.GetSnapshot().Accounts);
    }

    [Fact]
    public void NegativeElapsed_ClampsInsteadOfProducingNegativeGrowth()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(-1)); // clock skew
        proc.Readings[10] = 3 * Gb;
        wd.Sample();

        Assert.True(Assert.Single(wd.GetSnapshot().Accounts).GrowthBytesPerHour >= 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MemoryWatchdogGrowthTests"`
Expected: FAIL to compile — `MemoryWatchdog` does not exist. That is the correct first RED for a new type.

- [ ] **Step 3: Write the records and the interface**

`AccountMemory.cs`:

```csharp
using System;

namespace ROROROblox.Core.Diagnostics;

/// <summary>One account's memory reading. <paramref name="ReadOk"/> false means UNKNOWN — the
/// caller must exclude it from aggregates, never treat it as a zero reading.</summary>
public readonly record struct AccountMemory(
    Guid AccountId,
    long PrivateBytes,
    double GrowthBytesPerHour,
    int MinutesToCeiling,
    bool OverCap,
    bool IsTarget,
    bool ReadOk);
```

`MemoryPressureSnapshot.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ROROROblox.Core.Diagnostics;

/// <summary>Machine-level view. <c>MinutesToCeiling == 0</c> means "no valid projection"
/// OR "already exhausted" — both are cases where the caller should not display a countdown
/// derived from arithmetic it cannot trust. Use <see cref="HasProjection"/> to distinguish.</summary>
public readonly record struct MemoryPressureSnapshot(
    long AvailableBytes,
    double AggregateGrowthBytesPerHour,
    int MinutesToCeiling,
    bool HasProjection,
    Guid? TargetAccountId,
    IReadOnlyList<AccountMemory> Accounts);
```

`IMemoryWatchdog.cs`:

```csharp
using System;

namespace ROROROblox.Core.Diagnostics;

public interface IMemoryWatchdog
{
    long CapBytes { get; set; }
    long ReserveBytes { get; set; }
    int ProjectionWarnMinutes { get; set; }

    /// <summary>Coalesced, edge-triggered — the accounts that newly crossed a trigger this sample.</summary>
    event EventHandler<MemoryPressureSnapshot>? PressureCrossed;

    void OnAccountLaunched(Guid accountId, int pid);
    void OnAccountExited(Guid accountId);
    void ResetBaseline(Guid accountId, int pid);
    void Start();
    void Stop();
    void Sample();
    MemoryPressureSnapshot GetSnapshot();
}
```

- [ ] **Step 4: Write the watchdog — sampling and growth only**

Triggers land in Task 4; this step must make the Task 3 tests pass and nothing more.

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Samples private bytes per tracked account, estimates a linear growth rate, and projects time
/// to machine RAM exhaustion. Mirrors <see cref="ActivityMonitor"/>'s shape deliberately:
/// injected probes, Interlocked-guarded timer, public Sample() seam, latch/re-arm edges.
/// </summary>
public sealed class MemoryWatchdog : IMemoryWatchdog, IDisposable
{
    /// <summary>Below this, no slope is claimed. A 30s sample yields a confident, wrong projection.</summary>
    public static readonly TimeSpan MinimumObservation = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);

    private sealed class Record
    {
        public int Pid;
        public long BaselineBytes;
        public DateTimeOffset BaselineAt;
        public long LastBytes;
        public bool LastReadOk;
        public bool CapLatched;
        public bool ProjectionLatched;
    }

    private readonly IProcessMemoryProbe _process;
    private readonly ISystemMemoryProbe _system;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, Record> _records = new();

    private Timer? _timer;
    private int _sampling;
    private bool _disposed;
    private MemoryPressureSnapshot _last;

    public long CapBytes { get; set; }
    public long ReserveBytes { get; set; }
    public int ProjectionWarnMinutes { get; set; } = 120;

    public event EventHandler<MemoryPressureSnapshot>? PressureCrossed;

    public MemoryWatchdog(IProcessMemoryProbe process, ISystemMemoryProbe system, IClock clock)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void OnAccountLaunched(Guid accountId, int pid) => ResetBaseline(accountId, pid);

    public void OnAccountExited(Guid accountId) => _records.TryRemove(accountId, out _);

    public void ResetBaseline(Guid accountId, int pid)
        => _records[accountId] = new Record
        {
            Pid = pid,
            BaselineBytes = 0,
            BaselineAt = _clock.UtcNow,
            LastBytes = 0,
            LastReadOk = false,
            CapLatched = false,
            ProjectionLatched = false,
        };

    public void Start()
    {
        if (_disposed) return;
        _timer ??= new Timer(_ => SafeSample(), null, SampleInterval, SampleInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SafeSample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try { Sample(); }
        catch { /* never let a sample tick crash the timer thread */ }
        finally { Interlocked.Exchange(ref _sampling, 0); }
    }

    public void Sample()
    {
        var now = _clock.UtcNow;
        var accounts = new List<AccountMemory>(_records.Count);
        double aggregateGrowth = 0;

        foreach (var kv in _records)
        {
            var rec = kv.Value;

            if (!_process.TryReadPrivateBytes(rec.Pid, out var bytes))
            {
                // UNKNOWN. Keep the record for the next tick; contribute NOTHING to the aggregate.
                rec.LastReadOk = false;
                accounts.Add(new AccountMemory(kv.Key, rec.LastBytes, 0, 0, false, false, ReadOk: false));
                continue;
            }

            rec.LastReadOk = true;
            rec.LastBytes = bytes;

            // First successful reading seeds the baseline.
            if (rec.BaselineBytes == 0)
            {
                rec.BaselineBytes = bytes;
                rec.BaselineAt = now;
            }
            else if (bytes < rec.BaselineBytes)
            {
                // Ratchet: a teleport freed memory. Without this, one drop poisons the slope forever.
                rec.BaselineBytes = bytes;
                rec.BaselineAt = now;
            }

            var elapsed = now - rec.BaselineAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero; // clock skew

            double growth = 0;
            if (elapsed >= MinimumObservation)
            {
                growth = (bytes - rec.BaselineBytes) / elapsed.TotalHours;
                if (growth < 0) growth = 0;
                aggregateGrowth += growth;
            }

            accounts.Add(new AccountMemory(kv.Key, bytes, growth, 0, false, false, ReadOk: true));
        }

        var systemOk = _system.TryRead(out _, out var available);
        var hasProjection = systemOk && aggregateGrowth > 0;
        var minutes = 0;
        if (hasProjection)
        {
            var availableForClients = Math.Max(0, available - ReserveBytes);
            minutes = (int)(availableForClients / aggregateGrowth * 60);
        }

        _last = new MemoryPressureSnapshot(
            AvailableBytes: systemOk ? available : 0,
            AggregateGrowthBytesPerHour: aggregateGrowth,
            MinutesToCeiling: minutes,
            HasProjection: hasProjection,
            TargetAccountId: null,
            Accounts: accounts);
    }

    public MemoryPressureSnapshot GetSnapshot() => _last;

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MemoryWatchdogGrowthTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/AccountMemory.cs src/ROROROblox.Core/Diagnostics/MemoryPressureSnapshot.cs src/ROROROblox.Core/Diagnostics/IMemoryWatchdog.cs src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs src/ROROROblox.Tests/MemoryWatchdogGrowthTests.cs
git commit -m "feat(diagnostics): MemoryWatchdog sampling + linear growth estimation"
```

---

## Task 4: Triggers, latching, target selection

**Files:**
- Modify: `src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs`
- Test: `src/ROROROblox.Tests/MemoryWatchdogTriggerTests.cs` (create)

**Interfaces:**
- Consumes: everything from Task 3.
- Produces: `PressureCrossed` now fires; `AccountMemory.OverCap` / `.IsTarget` and `MemoryPressureSnapshot.TargetAccountId` are populated.

- [ ] **Step 1: Write the failing tests**

Reuse the fakes from Task 3 by copying them into this file (they are private nested classes; duplication here is deliberate so each test file stands alone).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryWatchdogTriggerTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public readonly Dictionary<int, long?> Readings = new();
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            if (!Readings.TryGetValue(pid, out var v) || v is null) return false;
            privateBytes = v.Value;
            return true;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public long Total = 32L * Gb;
        public long Available = 20L * Gb;
        public bool Ok = true;
        public bool TryRead(out long total, out long available)
        {
            total = Total; available = Available;
            return Ok;
        }
    }

    [Fact]
    public void CapCrossed_FiresOnceAndLatches()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(0, fires);

        proc.Readings[10] = 5 * Gb; // over cap
        wd.Sample();
        Assert.Equal(1, fires);

        wd.Sample();                // still over — must NOT re-fire
        Assert.Equal(1, fires);
    }

    [Fact]
    public void CapCleared_ReArmsSoNextCrossingFiresAgain()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(1, fires);

        proc.Readings[10] = 1 * Gb; // recycled — clears and re-arms
        wd.Sample();
        proc.Readings[10] = 5 * Gb;
        wd.Sample();
        Assert.Equal(2, fires);
    }

    [Fact]
    public void ProjectionCrossed_FiresWhenHeadroomRunsShort()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory { Available = 2 * Gb };
        var wd = new MemoryWatchdog(proc, sys, clock)
        {
            CapBytes = 0,                 // cap disabled — isolate the projection trigger
            ReserveBytes = 1 * Gb,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        proc.Readings[10] = 4 * Gb;   // 2 GB/hr; 1 GB usable headroom => 30 min
        wd.Sample();

        Assert.Equal(1, fires);
        Assert.True(wd.GetSnapshot().MinutesToCeiling < 120);
    }

    [Fact]
    public void ZeroAggregateGrowth_ProducesNoProjectionAndNoDivideByZero()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory { Available = 1 * Gb }, clock)
        {
            CapBytes = 0,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        wd.Sample();                  // flat — no growth

        Assert.False(wd.GetSnapshot().HasProjection);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Target_IsTheFattestClient()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 3 * Gb };

        var small = Guid.NewGuid();
        var fat = Guid.NewGuid();
        proc.Readings[10] = 1 * Gb;
        proc.Readings[20] = 6 * Gb;
        wd.OnAccountLaunched(small, 10);
        wd.OnAccountLaunched(fat, 20);
        wd.Sample();

        Assert.Equal(fat, wd.GetSnapshot().TargetAccountId);
        Assert.True(wd.GetSnapshot().Accounts.Single(a => a.AccountId == fat).IsTarget);
    }

    [Fact]
    public void SystemReadFails_SkipsProjectionButStillEvaluatesCap()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory { Ok = false };
        var wd = new MemoryWatchdog(proc, sys, clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        Assert.False(wd.GetSnapshot().HasProjection);
        Assert.Equal(1, fires); // cap still fired
    }

    [Fact]
    public void ResetBaseline_ClearsBothLatches()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(1, fires);

        wd.ResetBaseline(id, pid: 11); // recycled: new pid, fresh baseline, latches cleared
        proc.Readings[11] = 5 * Gb;
        wd.Sample();
        Assert.Equal(2, fires);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MemoryWatchdogTriggerTests"`
Expected: FAIL — `PressureCrossed` never fires and `TargetAccountId` is always null.

- [ ] **Step 3: Implement triggers, latching, and target selection**

Replace the tail of `Sample()` (from `var systemOk = ...` onward) with:

```csharp
        var systemOk = _system.TryRead(out _, out var available);
        var hasProjection = systemOk && aggregateGrowth > 0;
        var minutes = 0;
        if (hasProjection)
        {
            var availableForClients = Math.Max(0, available - ReserveBytes);
            minutes = (int)(availableForClients / aggregateGrowth * 60);
        }

        // Target = fattest client with a valid reading. The projection describes the machine;
        // the user needs to know which account to act on.
        Guid? target = accounts
            .Where(a => a.ReadOk)
            .OrderByDescending(a => a.PrivateBytes)
            .Select(a => (Guid?)a.AccountId)
            .FirstOrDefault();

        // Edge-triggered evaluation. Latch per account so one crossing = one warning.
        var crossed = false;
        for (var i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            if (!_records.TryGetValue(a.AccountId, out var rec)) continue;

            var overCap = CapBytes > 0 && a.ReadOk && a.PrivateBytes > CapBytes;
            if (overCap && !rec.CapLatched) { rec.CapLatched = true; crossed = true; }
            else if (!overCap) { rec.CapLatched = false; }

            var overProjection = hasProjection && minutes < ProjectionWarnMinutes;
            if (overProjection && !rec.ProjectionLatched) { rec.ProjectionLatched = true; crossed = true; }
            else if (!overProjection) { rec.ProjectionLatched = false; }

            accounts[i] = a with { OverCap = overCap, IsTarget = target == a.AccountId, MinutesToCeiling = minutes };
        }

        _last = new MemoryPressureSnapshot(
            AvailableBytes: systemOk ? available : 0,
            AggregateGrowthBytesPerHour: aggregateGrowth,
            MinutesToCeiling: minutes,
            HasProjection: hasProjection,
            TargetAccountId: target,
            Accounts: accounts);

        if (crossed)
        {
            PressureCrossed?.Invoke(this, _last);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS — both new test classes plus the existing suite.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs src/ROROROblox.Tests/MemoryWatchdogTriggerTests.cs
git commit -m "feat(diagnostics): dual-trigger memory pressure with latched edges + target selection"
```

---

## Task 5: Derived settings defaults

**Files:**
- Create: `src/ROROROblox.Core/Diagnostics/MemoryDefaults.cs`
- Modify: `src/ROROROblox.Core/AppSettings.cs`, `src/ROROROblox.Core/IAppSettings.cs`
- Test: `src/ROROROblox.Tests/MemoryDefaultsTests.cs` (create)

**Interfaces:**
- Consumes: `ISystemMemoryProbe.TryRead` (Task 2).
- Produces: `MemoryDefaults.ReserveMb(long totalPhysicalBytes)`, `MemoryDefaults.CapMb(long totalPhysicalBytes)`.
- New settings on `IAppSettings`: `bool MemoryWatchdogEnabled`, `int MemoryReserveMb`, `int MemoryCapMb`, `int ProjectionWarnMinutes`. Follow the exact property + persistence pattern already used by the neighbouring settings in `AppSettings.cs`.

**Why derived:** we do not know users' hardware. A 2 GB reserve is 12.5% of a 16 GB box and 3% of a 64 GB one; an 8 GB cap is unreachable on 16 GB and unremarkable on 64 GB. A fixed default is silently wrong for most people.

- [ ] **Step 1: Write the failing tests**

```csharp
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryDefaultsTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(8, 1024)]    // 8% of 8 GB = 655 MB -> floor
    [InlineData(16, 1310)]   // 8% of 16 GB
    [InlineData(32, 2621)]   // 8% of 32 GB
    [InlineData(64, 4096)]   // 8% of 64 GB = 5242 -> ceiling
    [InlineData(128, 4096)]  // ceiling holds
    public void ReserveMb_ClampsBetween1024And4096(int totalGb, int expectedMb)
        => Assert.Equal(expectedMb, MemoryDefaults.ReserveMb(totalGb * Gb));

    [Theory]
    [InlineData(8, 4096)]    // 35% of 8 GB = 2867 -> floor
    [InlineData(16, 5734)]   // 35% of 16 GB
    [InlineData(32, 11468)]  // 35% of 32 GB
    [InlineData(64, 22937)]  // 35% of 64 GB
    public void CapMb_FloorsAt4096(int totalGb, int expectedMb)
        => Assert.Equal(expectedMb, MemoryDefaults.CapMb(totalGb * Gb));

    [Fact]
    public void UnreadableTotal_FallsBackToConservativeDefaults()
    {
        // Zero total means the probe failed. Do not derive from a value we do not have.
        Assert.Equal(1024, MemoryDefaults.ReserveMb(0));
        Assert.Equal(4096, MemoryDefaults.CapMb(0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MemoryDefaultsTests"`
Expected: FAIL to compile — `MemoryDefaults` does not exist.

- [ ] **Step 3: Write the derivation**

```csharp
using System;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Settings defaults derived from installed RAM. We do not know our users' hardware and a fixed
/// number is wrong across the 16-64 GB range the clan actually runs — silently, which is worse
/// than being wrong loudly. A zero total means the probe failed: fall back rather than derive
/// from a value we do not have.
/// </summary>
public static class MemoryDefaults
{
    private const long Mb = 1024L * 1024;
    private const int ReserveFloorMb = 1024;
    private const int ReserveCeilingMb = 4096;
    private const int CapFloorMb = 4096;

    /// <summary>8% of installed RAM, clamped to [1 GB, 4 GB].</summary>
    public static int ReserveMb(long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0) return ReserveFloorMb;
        var eightPercent = (int)(totalPhysicalBytes * 0.08 / Mb);
        return Math.Clamp(eightPercent, ReserveFloorMb, ReserveCeilingMb);
    }

    /// <summary>35% of installed RAM — "no single client owns a third of the machine" — floored at 4 GB.</summary>
    public static int CapMb(long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0) return CapFloorMb;
        var thirtyFivePercent = (int)(totalPhysicalBytes * 0.35 / Mb);
        return Math.Max(thirtyFivePercent, CapFloorMb);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MemoryDefaultsTests"`
Expected: PASS. If an `InlineData` expectation is off by one from integer truncation, correct the **expectation** to the truncated value — do not add rounding to the production code.

- [ ] **Step 5: Add the four settings**

In `IAppSettings.cs` and `AppSettings.cs`, add `MemoryWatchdogEnabled` (default `true`), `MemoryReserveMb`, `MemoryCapMb`, `ProjectionWarnMinutes` (default `120`), following the existing property and persistence pattern in that file exactly. Store `0` for "unset" on the two derived ints so the composition root can tell "user chose zero" from "never set" — `MemoryCapMb == 0` legitimately means *disable the cap*, so use a nullable (`int?`) for these two rather than a sentinel.

- [ ] **Step 6: Run the full suite and commit**

```bash
dotnet test ROROROblox.slnx
git add src/ROROROblox.Core/Diagnostics/MemoryDefaults.cs src/ROROROblox.Core/AppSettings.cs src/ROROROblox.Core/IAppSettings.cs src/ROROROblox.Tests/MemoryDefaultsTests.cs
git commit -m "feat(settings): derive memory watchdog defaults from installed RAM"
```

---

## Task 6: Watchdog logging

**Files:**
- Modify: `src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs`
- Test: `src/ROROROblox.Tests/MemoryWatchdogLoggingTests.cs` (create)

**Interfaces:**
- Consumes: Task 4's `Sample()`.
- Produces: `MemoryWatchdog` constructor gains an optional `ILogger<MemoryWatchdog>? log = null` final parameter (defaulting to `NullLogger`), matching how `ActivityMonitor` and `DiagnosticsCollector` take loggers. Existing call sites are unaffected.

**Why:** the 2026-08-01 investigation cost a morning because we had a symptom and no curve. A user's log should contain the curve.

- [ ] **Step 1: Write the failing tests**

Use a capturing `ILogger` so the assertions are on real emitted records, not on a mock's call log.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryWatchdogLoggingTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, ex)));
    }

    // Fakes duplicated per-file so each test file stands alone — see Task 3.
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public readonly Dictionary<int, long?> Readings = new();
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            if (!Readings.TryGetValue(pid, out var v) || v is null) return false;
            privateBytes = v.Value;
            return true;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public bool TryRead(out long total, out long available)
        {
            total = 32L * Gb; available = 20L * Gb;
            return true;
        }
    }

    [Fact]
    public void Summary_EmitsEvery15Minutes_NotEveryTick()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock, log) { CapBytes = 0 };

        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(Guid.NewGuid(), 10);

        // 30 ticks at 30s = 15 minutes of wall clock.
        for (var i = 0; i < 30; i++)
        {
            wd.Sample();
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        var summaries = log.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("memory"));
        Assert.InRange(summaries, 1, 2); // once or twice, never 30 times
    }

    [Fact]
    public void CapCrossing_LogsWarningOncePerCrossing()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var log = new CapturingLogger<MemoryWatchdog>();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock, log) { CapBytes = 4 * Gb };

        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(Guid.NewGuid(), 10);
        wd.Sample();
        wd.Sample();
        wd.Sample();

        Assert.Single(log.Entries.Where(e => e.Level == LogLevel.Warning));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~MemoryWatchdogLoggingTests"`
Expected: FAIL to compile — the 4-argument constructor does not exist.

- [ ] **Step 3: Add the logger and the cadence**

Add to the class:

```csharp
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(15);
    private readonly ILogger<MemoryWatchdog> _log;
    private DateTimeOffset _lastSummaryAt = DateTimeOffset.MinValue;
```

Constructor gains `ILogger<MemoryWatchdog>? log = null` and assigns `_log = log ?? NullLogger<MemoryWatchdog>.Instance;` (add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Logging.Abstractions;`).

At the end of `Sample()`, after `_last` is assigned:

```csharp
        // Per-tick logging is banned: AppLogging's own comment records HttpClientFactory at 10s
        // consuming ~90% of a 15 MB day. The 15-minute summary carries the same information at
        // 1/30th the volume, and is what puts the memory CURVE in a user's log file.
        if (now - _lastSummaryAt >= SummaryInterval)
        {
            _lastSummaryAt = now;
            _log.LogInformation(
                "memory: {Count} client(s), aggregate {GrowthMbPerHr:F0} MB/hr, available {AvailableMb} MB, projection {Minutes} min (valid={Valid})",
                accounts.Count, aggregateGrowth / (1024 * 1024), (systemOk ? available : 0) / (1024 * 1024), minutes, hasProjection);
        }
```

Inside the latch block, when a latch newly sets:

```csharp
            if (overCap && !rec.CapLatched)
            {
                rec.CapLatched = true;
                crossed = true;
                _log.LogWarning("memory cap crossed: account {AccountId} at {Mb} MB (cap {CapMb} MB)",
                    a.AccountId, a.PrivateBytes / (1024 * 1024), CapBytes / (1024 * 1024));
            }
```

and the equivalent `LogWarning` for the projection latch, including `minutes`, `aggregateGrowth`, `available`, and `target`.

In the unreadable-pid branch:

```csharp
                _log.LogDebug("memory: pid {Pid} for account {AccountId} unreadable this tick; excluded from aggregate", rec.Pid, kv.Key);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs src/ROROROblox.Tests/MemoryWatchdogLoggingTests.cs
git commit -m "feat(diagnostics): 15-minute memory summary + latched warning logs"
```

---

## Task 7: DI wiring, row chip, MainViewModel

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs` (DI registration ~line 457 area; a new `WireMemoryWatchdog()` alongside `WireActivityMonitor()` ~line 189)
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs`
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`
- Modify: `src/ROROROblox.App/MainWindow.xaml`

**Interfaces:**
- Consumes: `IMemoryWatchdog` (Task 4), settings (Task 5).
- Produces: `AccountSummary.MemoryText` (`string?`), `AccountSummary.MemoryWarning` (`bool`) for binding.

- [ ] **Step 1: Register the services**

In the DI setup beside `services.AddSingleton<IGlobalBasicSettingsWriter, GlobalBasicSettingsWriter>();`:

```csharp
services.AddSingleton<IProcessMemoryProbe, ProcessMemoryProbe>();
services.AddSingleton<ISystemMemoryProbe, SystemMemoryProbe>();
services.AddSingleton<IMemoryWatchdog, MemoryWatchdog>();
```

- [ ] **Step 2: Wire startup, resolving derived defaults once**

Add `WireMemoryWatchdog();` next to `WireActivityMonitor();`, and implement it mirroring `WireActivityMonitor`'s guarded shape — a failure here must never block startup:

```csharp
private void WireMemoryWatchdog()
{
    if (_services is null) return;
    try
    {
        var watchdog = _services.GetRequiredService<IMemoryWatchdog>();
        var settings = _services.GetRequiredService<IAppSettings>();
        if (!settings.MemoryWatchdogEnabled) return;

        _services.GetRequiredService<ISystemMemoryProbe>().TryRead(out var total, out _);

        // Derive ONCE at startup. A user override must stick — never re-derive over it.
        watchdog.ReserveBytes = (settings.MemoryReserveMb ?? MemoryDefaults.ReserveMb(total)) * 1024L * 1024L;
        watchdog.CapBytes = (settings.MemoryCapMb ?? MemoryDefaults.CapMb(total)) * 1024L * 1024L;
        watchdog.ProjectionWarnMinutes = settings.ProjectionWarnMinutes;

        var tracker = _services.GetRequiredService<IRobloxProcessTracker>();
        tracker.ProcessExited += (_, e) => watchdog.OnAccountExited(e.AccountId);

        watchdog.Start();
    }
    catch (Exception ex)
    {
        _log?.LogWarning(ex, "Memory watchdog wiring failed; continuing without it.");
    }
}
```

Also call `watchdog.OnAccountLaunched(accountId, pid)` at the same place the decorator's `Track(pid, summary)` is called (`App.xaml.cs:823` area), so launch/exit bookkeeping stays in one place.

- [ ] **Step 3: Add the chip properties to AccountSummary**

Follow the existing `INotifyPropertyChanged` pattern used by neighbouring properties in that file:

```csharp
private string? _memoryText;
public string? MemoryText
{
    get => _memoryText;
    set { if (_memoryText != value) { _memoryText = value; Raise(); } }
}

private bool _memoryWarning;
public bool MemoryWarning
{
    get => _memoryWarning;
    set { if (_memoryWarning != value) { _memoryWarning = value; Raise(); } }
}
```

- [ ] **Step 4: Paint from the ViewModel**

In `MainViewModel`'s constructor, subscribe; in the existing 30s `_ticker` handler, call `RefreshMemoryChips()`:

```csharp
_memoryWatchdog.PressureCrossed += (_, snap) =>
    Application.Current.Dispatcher.Invoke(() => ApplyMemory(snap, warned: true));

private void RefreshMemoryChips() => ApplyMemory(_memoryWatchdog.GetSnapshot(), warned: false);

private void ApplyMemory(MemoryPressureSnapshot snap, bool warned)
{
    foreach (var a in snap.Accounts)
    {
        var row = Accounts.FirstOrDefault(r => r.Id == a.AccountId);
        if (row is null) continue;

        if (!a.ReadOk)
        {
            row.MemoryText = null;   // UNKNOWN renders nothing, never "0 GB"
            continue;
        }

        var gb = a.PrivateBytes / 1024d / 1024d / 1024d;
        // `warned` already means a trigger latched this sample — do not re-derive the
        // threshold here. The watchdog owns that decision; the ViewModel only renders it.
        var warn = warned;

        // Only append a countdown when the projection is real. Never render a number
        // derived from arithmetic we could not complete.
        row.MemoryText = warn && snap.HasProjection
            ? $"▲ {gb:F1} GB · ~{snap.MinutesToCeiling} min"
            : warn ? $"▲ {gb:F1} GB" : $"{gb:F1} GB";
        row.MemoryWarning = warn;
    }
}
```

- [ ] **Step 5: Bind in MainWindow.xaml**

Add the chip to the account row template next to the existing status elements, styled per `~/.claude/skills/626labs-design` tokens. Bind `Text` to `MemoryText` and drive the warning colour from `MemoryWarning`.

- [ ] **Step 6: Build, run, verify manually**

Run: `dotnet build ROROROblox.slnx` then launch the app with at least one account and confirm a chip appears within 30s and updates.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/App.xaml.cs src/ROROROblox.App/ViewModels/AccountSummary.cs src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/MainWindow.xaml
git commit -m "feat(ui): per-account memory chip driven by the watchdog"
```

---

## Task 8: Tray warning surface + Recycle

**Files:**
- Modify: `src/ROROROblox.Core/ITrayService.cs`, `src/ROROROblox.App/Tray/TrayService.cs`
- Create: `src/ROROROblox.App/Tray/Resources/tray-warn.ico`, `tray-warn-titlebar.png`
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IMemoryWatchdog.PressureCrossed`, `IRobloxLauncher`, `IRobloxProcessTracker`, `LaunchTarget`.
- Produces: `ITrayService.ShowMemoryWarning(string title, string message)`, `ITrayService.SetMemoryWarning(bool active)`, `event EventHandler<Guid> RequestFocusAccount`; `MainViewModel.RecycleAccountCommand`.

**Critical:** do **not** route this through `UpdateStatus(MultiInstanceState)`. That enum answers "is multi-instance working." Overloading it would let a memory warning erase the ON/ERROR state the user needs during an actual mutex problem.

- [ ] **Step 1: Produce the icon artwork**

Invoke the `626labs-design` skill. Sizes and formats must match the existing `tray-on` / `tray-off` / `tray-error` set. **Programmatic placeholders are disqualifying** per the repo rules — this ships to the Store.

- [ ] **Step 2: Extend ITrayService**

```csharp
/// <summary>
/// Memory-pressure warning overlay. Deliberately SEPARATE from <see cref="UpdateStatus"/> —
/// MultiInstanceState answers "is multi-instance working", an unrelated axis. Folding memory
/// pressure into it would erase the ON/ERROR state during a real mutex problem.
/// </summary>
void SetMemoryWarning(bool active);

/// <summary>Balloon for a newly-crossed memory threshold. Fires once per latched crossing.</summary>
void ShowMemoryWarning(string title, string message);

/// <summary>Fired when the user clicks a memory-warning balloon — carries the target account.</summary>
event EventHandler<Guid> RequestFocusAccount;
```

Implement in `TrayService` using the existing `Hardcodet.NotifyIcon.Wpf` surface already used for the icon and menu.

- [ ] **Step 3: Write the failing test for the recycle sequence**

Put the sequence in its own `AccountRecycler` in Core rather than inline in `MainViewModel`. The codebase already extracts collaborators this way (`RobloxInstanceStopper`, `ActivitySnapshotApplier`), and it makes the ordering testable without standing up the whole ViewModel harness. `MainViewModel.RecycleAccountCommand` becomes a thin call into it.

Create `src/ROROROblox.Tests/AccountRecyclerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class AccountRecyclerTests
{
    private sealed class FakeStopper : IRobloxInstanceStopper
    {
        public readonly List<Guid> Stopped = new();
        public void StopAll() { }
        public bool StopAccount(Guid accountId) { Stopped.Add(accountId); return true; }
    }

    private sealed class RecordingLauncher
    {
        public readonly List<(Guid Id, LaunchTarget Target)> Launches = new();
        public int NextPid = 4242;
        public Task<int> LaunchAsync(Guid id, LaunchTarget target, CancellationToken ct = default)
        {
            Launches.Add((id, target));
            return Task.FromResult(NextPid);
        }
    }

    private sealed class SpyWatchdog : IMemoryWatchdog
    {
        public readonly List<(Guid Id, int Pid)> Resets = new();
        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }
        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed { add { } remove { } }
        public void OnAccountLaunched(Guid accountId, int pid) { }
        public void OnAccountExited(Guid accountId) { }
        public void ResetBaseline(Guid accountId, int pid) => Resets.Add((accountId, pid));
        public void Start() { }
        public void Stop() { }
        public void Sample() { }
        public MemoryPressureSnapshot GetSnapshot() => default;
    }

    [Fact]
    public async Task Recycle_StopsThenRelaunchesToTheSameLaunchTarget()
    {
        var id = Guid.NewGuid();
        var target = new LaunchTarget.Place(PlaceId: 8737899170);
        var stopper = new FakeStopper();
        var launcher = new RecordingLauncher();
        var watchdog = new SpyWatchdog();
        var recycler = new AccountRecycler(stopper, launcher.LaunchAsync, watchdog);

        var ok = await recycler.RecycleAsync(id, target);

        Assert.True(ok);
        Assert.Equal(id, Assert.Single(stopper.Stopped));
        var launch = Assert.Single(launcher.Launches);
        Assert.Equal(id, launch.Id);
        Assert.Same(target, launch.Target); // the SAME target — you land where you were
    }

    [Fact]
    public async Task Recycle_ResetsTheWatchdogBaselineToTheNewPid()
    {
        var id = Guid.NewGuid();
        var watchdog = new SpyWatchdog();
        var launcher = new RecordingLauncher { NextPid = 777 };
        var recycler = new AccountRecycler(new FakeStopper(), launcher.LaunchAsync, watchdog);

        await recycler.RecycleAsync(id, new LaunchTarget.Home());

        Assert.Equal((id, 777), Assert.Single(watchdog.Resets));
    }

    [Fact]
    public async Task Recycle_RelaunchFails_ReportsFailureAndDoesNotResetBaseline()
    {
        var id = Guid.NewGuid();
        var watchdog = new SpyWatchdog();
        var recycler = new AccountRecycler(
            new FakeStopper(),
            (_, _, _) => Task.FromResult(0), // 0 = launch produced no process
            watchdog);

        var ok = await recycler.RecycleAsync(id, new LaunchTarget.Home());

        Assert.False(ok);
        Assert.Empty(watchdog.Resets); // a stale baseline is worse than none
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~AccountRecyclerTests"`
Expected: FAIL to compile — `AccountRecycler` does not exist. If `IRobloxInstanceStopper` has no `StopAccount(Guid)`, add it alongside the existing `StopAll()` and implement it in `RobloxInstanceStopper` using the same teardown-budget logic already there.

- [ ] **Step 5: Implement AccountRecycler**

Create `src/ROROROblox.Core/Diagnostics/AccountRecycler.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Stop one account's client and relaunch it into the SAME target. Process exit is the only
/// guaranteed reclaim of Roblox's leaked memory on Windows, so this is the actual remedy the
/// watchdog's warning points at. Extracted from the ViewModel so the ordering is testable.
/// </summary>
public sealed class AccountRecycler
{
    public delegate Task<int> LaunchDelegate(Guid accountId, LaunchTarget target, CancellationToken ct);

    private readonly IRobloxInstanceStopper _stopper;
    private readonly LaunchDelegate _launch;
    private readonly IMemoryWatchdog _watchdog;
    private readonly ILogger _log;

    public AccountRecycler(
        IRobloxInstanceStopper stopper,
        LaunchDelegate launch,
        IMemoryWatchdog watchdog,
        ILogger? log = null)
    {
        _stopper = stopper ?? throw new ArgumentNullException(nameof(stopper));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
        _log = log ?? NullLogger.Instance;
    }

    public async Task<bool> RecycleAsync(Guid accountId, LaunchTarget target, CancellationToken ct = default)
    {
        _log.LogInformation("Recycling account {AccountId} into {Target} (user-initiated).", accountId, target);
        _stopper.StopAccount(accountId);

        var pid = await _launch(accountId, target, ct).ConfigureAwait(false);
        if (pid <= 0)
        {
            _log.LogWarning("Recycle of account {AccountId} failed: relaunch produced no process.", accountId);
            // Deliberately do NOT reset the baseline — a baseline pointing at a dead pid would
            // silently blind the watchdog for this account.
            return false;
        }

        _watchdog.ResetBaseline(accountId, pid);
        _log.LogInformation("Recycled account {AccountId}; new pid {Pid}.", accountId, pid);
        return true;
    }
}
```

Then make `MainViewModel.RecycleAccountCommand` a thin call into `RecycleAsync`, passing the account's current `LaunchTarget`. Relaunch raises `AccountLaunched` on the plugin bus through the existing launch path — that is what lets UrTask's spawn→spot macro pick up with no new wiring.

- [ ] **Step 6: Run to verify it passes, then commit**

```bash
dotnet test ROROROblox.slnx
git add src/ROROROblox.Core/ITrayService.cs src/ROROROblox.App/Tray/ src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.Tests/MainViewModelTests.cs
git commit -m "feat(tray): memory warning surface + one-click account recycle"
```

---

## Task 9: System Health rig data

**Files:**
- Modify: `src/ROROROblox.Core/Diagnostics/DiagnosticsCollector.cs` and the `DiagnosticsSnapshot` record (find it with `grep -rn "record DiagnosticsSnapshot" src/`)
- Modify: the diagnostics window that renders the snapshot

**Interfaces:**
- Consumes: `ISystemMemoryProbe` (Task 2), `IMemoryWatchdog.GetSnapshot()` (Task 4).
- Produces: `DiagnosticsSnapshot` gains `TotalPhysicalMemoryBytes`, `AvailablePhysicalMemoryBytes`, `IReadOnlyList<AccountMemory> AccountMemory`.

**Why:** users already paste System Health for support. Adding RAM means the next "my windows closed on their own" report arrives with the rig and the curve attached, instead of costing a day to establish what one number would have shown. It also beats asking — non-technical users misstate their specs routinely.

- [ ] **Step 1: Write the failing test**

Add to the existing diagnostics test file (find it with `grep -rln "DiagnosticsCollector" src/ROROROblox.Tests/`), reusing that file's existing fakes for `IAccountStore`, `IRobloxProcessTracker`, and `IMutexHolder`:

```csharp
private sealed class FakeSystemMemoryProbe : ISystemMemoryProbe
{
    public bool Ok = true;
    public long Total = 34_359_738_368;  // 32 GB
    public long Available = 21_474_836_480;  // 20 GB
    public bool TryRead(out long total, out long available)
    {
        total = Ok ? Total : 0;
        available = Ok ? Available : 0;
        return Ok;
    }
}

[Fact]
public async Task Collect_ReportsInstalledAndAvailableRam()
{
    var collector = BuildCollector(new FakeSystemMemoryProbe());

    var snap = await collector.CollectAsync();

    Assert.Equal(34_359_738_368, snap.TotalPhysicalMemoryBytes);
    Assert.Equal(21_474_836_480, snap.AvailablePhysicalMemoryBytes);
}

[Fact]
public async Task Collect_ProbeFails_ReportsZeroRatherThanAGuess()
{
    var collector = BuildCollector(new FakeSystemMemoryProbe { Ok = false });

    var snap = await collector.CollectAsync();

    // The class contract is "a missing piece becomes 'not detected' rather than throwing."
    // Zero is honest here; a fabricated plausible number in a support bundle is not.
    Assert.Equal(0, snap.TotalPhysicalMemoryBytes);
    Assert.Equal(0, snap.AvailablePhysicalMemoryBytes);
}
```

Add a `BuildCollector(ISystemMemoryProbe)` helper to that file if one does not already exist, wiring the collector's other constructor arguments from the fakes already present.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~DiagnosticsCollector"`
Expected: FAIL to compile — `TotalPhysicalMemoryBytes` does not exist on the snapshot.

- [ ] **Step 3: Extend the record and collector**

`DiagnosticsCollector` takes `ISystemMemoryProbe` and `IMemoryWatchdog` as new constructor parameters. Keep the existing best-effort discipline: every probe failure becomes a zero/empty field, never an exception — the class docstring promises "a clean snapshot is always produceable even on a busted machine."

- [ ] **Step 4: Render the new fields, run, commit**

```bash
dotnet test ROROROblox.slnx
git add src/ROROROblox.Core/Diagnostics/ src/ROROROblox.App/
git commit -m "feat(diagnostics): report RAM + per-account memory in System health"
```

---

## Task 10: Plugin contract 0.5.0

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`, `ROROROblox.PluginContract.csproj` (version 0.4.0 → 0.5.0)
- Modify: `src/ROROROblox.App/Plugins/IPluginEventBus.cs`, `InProcessPluginEventBus.cs`, `PluginCapability.cs`, `PluginHostService.cs`
- Modify: `docs/plugins/AUTHOR_GUIDE.md`

**Interfaces:**
- Consumes: `MemoryPressureSnapshot` / `AccountMemory` (Task 4).
- Produces: proto `AccountMemorySnapshot`; `IPluginEventBus.MemoryPressure` + `RaiseMemoryPressure`; `PluginCapability.HostEventsMemoryPressure = "host.events.memory-pressure"`.

**Additive only — no breaking changes to existing messages or rpcs.**

- [ ] **Step 1: Add the proto message and rpc**

```proto
message AccountMemorySnapshot {
  string account_id       = 1;
  uint64 private_bytes    = 2;
  double growth_mb_per_hr = 3;
  uint32 mins_to_ceiling  = 4;   // 0 = no valid projection
  bool   over_cap         = 5;
  bool   is_target        = 6;   // fattest client at fire time
}

rpc SubscribeMemoryPressure(Empty) returns (stream AccountMemorySnapshot);
```

Use the **server-streaming** form to match the existing `SubscribeAccountLaunched` / `SubscribeAccountExited` / `SubscribeMutexStateChanged` pattern rather than the spec's shorthand `OnMemoryPressure` — consistency with the established contract wins.

- [ ] **Step 2: Add the capability**

In `PluginCapability.cs`:

```csharp
public const string HostEventsMemoryPressure = "host.events.memory-pressure";
```

and to the `Catalog` dictionary, in the plain-language voice the other entries use:

```csharp
[HostEventsMemoryPressure] = "Notify the plugin when an account's memory use gets high enough to risk the machine running out of RAM.",
```

The consent model gets no silent exception — this is enforced through `ConsentStore` and the `CapabilityInterceptor` exactly like every other `host.*` capability.

- [ ] **Step 3: Extend the bus**

Add `event Action<AccountMemorySnapshot>? MemoryPressure;` to `IPluginEventBus` and `RaiseMemoryPressure(...)` to `InProcessPluginEventBus`, mirroring the existing members. Wire `IMemoryWatchdog.PressureCrossed` to raise it from `App.xaml.cs`'s `WirePluginEventBus`.

- [ ] **Step 4: Add the integration test**

In `src/ROROROblox.PluginTestHarness/`, follow the existing end-to-end contract tests: a real Kestrel + named-pipe subscriber receives a pressure message after the bus raises one. **Note the known harness blindspot** — `EndToEndContractTests` hardcodes the capability accessor while production uses `x-plugin-id` headers; assert the capability gate through the production header path here rather than inheriting that shortcut.

- [ ] **Step 5: Bump the version and document**

Set `<Version>0.5.0</Version>` in `ROROROblox.PluginContract.csproj`. Add the recipe to `docs/plugins/AUTHOR_GUIDE.md`: subscribe to pressure → call the existing stop/launch commands → macro back to the event spot.

- [ ] **Step 6: Run and commit**

```bash
dotnet test ROROROblox.slnx
git add src/ROROROblox.PluginContract/ src/ROROROblox.App/Plugins/ src/ROROROblox.PluginTestHarness/ docs/plugins/AUTHOR_GUIDE.md
git commit -m "feat(contract): 0.5.0 — memory pressure events for plugins"
```

---

## Post-implementation

- [ ] Banner-correct `docs/superpowers/specs/2026-08-01-memory-watchdog-design.md` wherever build reality diverged. **Do not rewrite it top-to-bottom** — name what was proposed vs what was built.
- [ ] Log a decision via `mcp__626Labs__manage_decisions log` against project `PBWgg5mimZyAzAG3niAp`, referencing the driver decision `n5NxsdLo7CGf8u4izC3K`.
- [ ] Confirm the effective log retention window after Task 1's change to 30 files, on a machine that has actually rolled on size.
- [ ] Sequence the graphics/texture baseline-reduction spec — the watchdog is now the instrument that makes those numbers measurable.
