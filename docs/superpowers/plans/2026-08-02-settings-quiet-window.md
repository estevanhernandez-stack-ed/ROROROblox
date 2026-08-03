# Settings Quiet Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make per-account FPS caps actually survive close-together launches, by waiting for Roblox's shared settings file to go quiet and then verifying our write survived.

**Architecture:** Roblox keeps **one** settings file per install (`%LOCALAPPDATA%\Roblox\GlobalBasicSettings_<N>.xml`). A starting client re-persists its own `FramerateCap` to it repeatedly for ~9 seconds. So the competing writer is the *previous client*, not the next launch. Before launching, we wait for that file to be unmodified for a debounce, write our cap, confirm after a short window that it survived, and retry if it was clobbered. Correctness comes from the confirm-and-retry, not from the debounce constant being right.

**Tech Stack:** .NET 10, C# 14, xUnit, `Microsoft.Extensions.TimeProvider.Testing` (already referenced), Serilog via `Microsoft.Extensions.Logging`, WPF/WPF-UI for the banner.

**Spec:** `docs/superpowers/specs/2026-08-02-settings-quiet-window-design.md` — read it first.
**Evidence:** `docs/investigations/2026-08-02-launch-gate-smoke-test-negative-result.md`

## Global Constraints

- **Build `dotnet build ROROROblox.slnx`; test `dotnet test ROROROblox.slnx`.** `.slnx` is canonical. A stray `ROROROblox.sln` is gitignored, missing a project, and must never be built. A bare `dotnet build` errors MSB1011 while both exist.
- **Close any running `ROROROblox.App` before building** — it locks `ROROROblox.Core.dll` (MSB3027). Report BLOCKED rather than killing it.
- **A full `dotnet test` takes ~4 s plus build.** Iterate filtered and in the **foreground**: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~<Name>"`.
- **If a test appears to hang, check it rather than assuming slowness.** `Get-Process testhost | Select-Object CPU` — near-zero CPU over minutes is a parked thread, not compute.
- **No test may sleep in real time.** All delays route through the injected `TimeProvider`, always via the `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` overload. The two-argument overload ignores `FakeTimeProvider` and waits in real time.
- **No test may fail by hanging.** xUnit applies no default timeout. Every failure mode must be a red assertion. A `.WaitAsync(TimeSpan.FromSeconds(5))` ceiling that elapses only on failure is the established pattern in `RobloxLauncherTests.cs` — copy it.
- **Never log a cookie, and never log any account identifier beyond the existing account GUID.**
- No hardcoded Windows user-profile paths in committed source — the `local-path-guard` pre-commit hook rejects them and cannot tell an example from a violation.
- Conventional commits. Do not push; do not open a PR.
- Branch is `fix/settings-quiet-window`, already created off `main`.

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/ROROROblox.Core/GlobalBasicSettingsFile.cs` | **New.** One definition of "which settings file is the active one". Extracted from `GlobalBasicSettingsWriter` so the writer and the probe cannot disagree about the target. |
| `src/ROROROblox.Core/IGlobalBasicSettingsProbe.cs` | **New.** Read-side counterpart to `IGlobalBasicSettingsWriter`: current cap, and last-write timestamp. |
| `src/ROROROblox.Core/GlobalBasicSettingsProbe.cs` | **New.** Real implementation over the filesystem. |
| `src/ROROROblox.Core/FpsCapSettler.cs` | **New.** The whole mechanism: fast path, quiet wait, write, confirm, retry. Static and injectable-free so it is trivially unit-testable. `RobloxLauncher` is already large; this does not go in it. |
| `src/ROROROblox.Core/RobloxLauncher.cs` | **Modify.** Call the settler before launching; delete the pid-based gate. |
| `src/ROROROblox.App/App.xaml.cs` | **Modify.** Register the probe; pass it and a logger to `RobloxLauncher`. |
| `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs` | **Modify.** Add the differing-caps warning copy beside the existing banner strings. |
| `src/ROROROblox.App/ViewModels/MainViewModel.cs` | **Modify.** Compute and expose the warning. |
| `src/ROROROblox.App/MainWindow.xaml` | **Modify.** Render it in the existing shared banner `Border`. |

---

## Task 1: The settings-file probe

**Files:**
- Create: `src/ROROROblox.Core/GlobalBasicSettingsFile.cs`
- Create: `src/ROROROblox.Core/IGlobalBasicSettingsProbe.cs`
- Create: `src/ROROROblox.Core/GlobalBasicSettingsProbe.cs`
- Modify: `src/ROROROblox.Core/GlobalBasicSettingsWriter.cs` (use the extracted resolver)
- Test: `src/ROROROblox.Tests/GlobalBasicSettingsProbeTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `internal static class GlobalBasicSettingsFile` with `internal static FileInfo? Resolve(string robloxAppDataRoot)`
  - `public interface IGlobalBasicSettingsProbe { int? ReadFramerateCap(); DateTimeOffset? GetLastWriteTimeUtc(); }`
  - `public sealed class GlobalBasicSettingsProbe : IGlobalBasicSettingsProbe` with ctor `GlobalBasicSettingsProbe()` and `GlobalBasicSettingsProbe(string robloxAppDataRoot)`

**Context:** `GlobalBasicSettingsWriter` already resolves the active file — the highest-numbered `GlobalBasicSettings_<N>.xml`, excluding the `_Studio` variant. Read its existing private resolver before writing the extracted one and preserve its exact behaviour. Two components disagreeing about which file is active would be its own silent bug, which is precisely the class of defect that produced the `ClientAppSettingsWriter` folder-targeting bug logged in `docs/features.md`.

Both probe methods are **synchronous**. The file is small, and the established probe in this codebase (`IRobloxRunningProbe.GetRunningPlayerPids`) is sync too. Both must return `null` rather than throw on a missing, locked, or malformed file — an unknown reading is not the same as a known one, and callers depend on telling them apart.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/GlobalBasicSettingsProbeTests.cs`:

```csharp
using System;
using System.IO;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public sealed class GlobalBasicSettingsProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rororo-gbs-" + Guid.NewGuid().ToString("N"));

    public GlobalBasicSettingsProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string WriteSettings(string name, int? cap)
    {
        var body = cap is null
            ? "<Item class=\"UserGameSettings\"><Properties /></Item>"
            : $"<Item class=\"UserGameSettings\"><Properties><int name=\"FramerateCap\">{cap}</int></Properties></Item>";
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, $"<roblox>{body}</roblox>");
        return path;
    }

    [Fact]
    public void ReadFramerateCap_ReturnsTheValueFromTheHighestNumberedFile()
    {
        WriteSettings("GlobalBasicSettings_9.xml", 60);
        WriteSettings("GlobalBasicSettings_13.xml", 20);

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Equal(20, probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_IgnoresTheStudioVariant()
    {
        WriteSettings("GlobalBasicSettings_13.xml", 20);
        WriteSettings("GlobalBasicSettings_13_Studio.xml", 144);

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Equal(20, probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_ReturnsNullWhenThereIsNoFile()
    {
        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Null(probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_ReturnsNullWhenTheNodeIsAbsent()
    {
        WriteSettings("GlobalBasicSettings_13.xml", cap: null);

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Null(probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_ReturnsNullOnMalformedXmlRatherThanThrowing()
    {
        File.WriteAllText(Path.Combine(_root, "GlobalBasicSettings_13.xml"), "<roblox><not-closed>");

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Null(probe.ReadFramerateCap());
    }

    [Fact]
    public void GetLastWriteTimeUtc_TracksTheFileAndIsNullWhenAbsent()
    {
        var probe = new GlobalBasicSettingsProbe(_root);
        Assert.Null(probe.GetLastWriteTimeUtc());

        var path = WriteSettings("GlobalBasicSettings_13.xml", 20);
        var stamped = new DateTime(2026, 8, 2, 16, 21, 10, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamped);

        Assert.Equal(stamped, probe.GetLastWriteTimeUtc()!.Value.UtcDateTime);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~GlobalBasicSettingsProbeTests"`
Expected: FAIL to compile — `GlobalBasicSettingsProbe` does not exist.

- [ ] **Step 3: Extract the file resolver**

Read `GlobalBasicSettingsWriter`'s existing private resolution method first and move its logic verbatim. Create `src/ROROROblox.Core/GlobalBasicSettingsFile.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ROROROblox.Core;

/// <summary>
/// One definition of "which GlobalBasicSettings file is the active one", shared by
/// <see cref="GlobalBasicSettingsWriter"/> and <see cref="GlobalBasicSettingsProbe"/>.
/// <para>
/// Extracted deliberately. A writer and a reader that resolve the target independently can
/// silently disagree, which is exactly the shape of the ClientAppSettingsWriter defect logged in
/// docs/features.md — writes landing in a folder nothing reads, with no error and no symptom.
/// </para>
/// </summary>
internal static partial class GlobalBasicSettingsFile
{
    [GeneratedRegex(@"^GlobalBasicSettings_(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    /// <summary>
    /// The highest-numbered <c>GlobalBasicSettings_&lt;N&gt;.xml</c> under <paramref name="root"/>.
    /// The <c>_Studio</c> variant is excluded by the pattern — it belongs to Roblox Studio and
    /// writing to it would do nothing. Returns null when the directory or the file is absent.
    /// </summary>
    internal static FileInfo? Resolve(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        FileInfo? best = null;
        var bestN = -1;

        foreach (var path in Directory.EnumerateFiles(root, "GlobalBasicSettings_*.xml"))
        {
            var info = new FileInfo(path);
            var match = NamePattern().Match(info.Name);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var n))
            {
                continue;
            }

            if (n > bestN)
            {
                bestN = n;
                best = info;
            }
        }

        return best;
    }
}
```

Then change `GlobalBasicSettingsWriter` to call `GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot)` and delete its own private resolver. Do not change any of its other behaviour, its exception type, or its messages.

- [ ] **Step 4: Write the probe**

Create `src/ROROROblox.Core/IGlobalBasicSettingsProbe.cs`:

```csharp
namespace ROROROblox.Core;

/// <summary>
/// Read side of Roblox's shared user-settings file — the counterpart to
/// <see cref="IGlobalBasicSettingsWriter"/>.
/// <para>
/// Exists because a starting Roblox client re-persists its own FramerateCap to this file
/// repeatedly for ~9 seconds after launch (measured 2026-08-02). To set a per-account cap that
/// survives, we have to observe when the file stops changing and confirm our write held.
/// </para>
/// </summary>
public interface IGlobalBasicSettingsProbe
{
    /// <summary>
    /// The cap currently on disk, or <c>null</c> if the file is missing, locked, malformed, or has
    /// no FramerateCap node. Null means "unknown" — never treat it as a value.
    /// </summary>
    int? ReadFramerateCap();

    /// <summary>
    /// When the file was last written, or <c>null</c> if it is missing or unreadable. Callers use
    /// changes in this value to detect that a client is still writing.
    /// </summary>
    DateTimeOffset? GetLastWriteTimeUtc();
}
```

Create `src/ROROROblox.Core/GlobalBasicSettingsProbe.cs`:

```csharp
using System.Xml.Linq;

namespace ROROROblox.Core;

/// <inheritdoc cref="IGlobalBasicSettingsProbe" />
public sealed class GlobalBasicSettingsProbe : IGlobalBasicSettingsProbe
{
    private const string FramerateCapName = "FramerateCap";

    private readonly string _robloxAppDataRoot;

    public GlobalBasicSettingsProbe()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox"))
    {
    }

    public GlobalBasicSettingsProbe(string robloxAppDataRoot)
        => _robloxAppDataRoot = robloxAppDataRoot;

    public int? ReadFramerateCap()
    {
        var file = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot);
        if (file is null)
        {
            return null;
        }

        try
        {
            // Read the bytes ourselves rather than XDocument.Load(path): a client may hold the file
            // open mid-write, and we want a locked file to read as "unknown" (null), not as an
            // exception escaping into a launch path.
            var text = File.ReadAllText(file.FullName);
            var value = XDocument.Parse(text)
                .Descendants("int")
                .FirstOrDefault(e => (string?)e.Attribute("name") == FramerateCapName)
                ?.Value;

            return int.TryParse(value, out var cap) ? cap : null;
        }
        catch (Exception)
        {
            // Missing, locked, or malformed -> unknown. Callers must not confuse this with a value.
            return null;
        }
    }

    public DateTimeOffset? GetLastWriteTimeUtc()
    {
        var file = GlobalBasicSettingsFile.Resolve(_robloxAppDataRoot);
        if (file is null)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(file.FullName), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~GlobalBasicSettingsProbeTests"`
Expected: PASS, 6/6.

- [ ] **Step 6: Run the writer's existing tests — the resolver extraction must not have changed its behaviour**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~GlobalBasicSettings"`
Expected: PASS. If any pre-existing writer test fails, the extraction changed behaviour — fix the extraction, not the test.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Core/GlobalBasicSettingsFile.cs src/ROROROblox.Core/IGlobalBasicSettingsProbe.cs src/ROROROblox.Core/GlobalBasicSettingsProbe.cs src/ROROROblox.Core/GlobalBasicSettingsWriter.cs src/ROROROblox.Tests/GlobalBasicSettingsProbeTests.cs
git commit -m "feat(core): read side of the shared Roblox settings file"
```

---

## Task 2: The settle mechanism

**Files:**
- Create: `src/ROROROblox.Core/FpsCapSettler.cs`
- Test: `src/ROROROblox.Tests/FpsCapSettlerTests.cs`

**Interfaces:**
- Consumes: `IGlobalBasicSettingsProbe` (Task 1), and the existing `IGlobalBasicSettingsWriter.WriteFramerateCapAsync(int?, CancellationToken)`.
- Produces:
  - `public enum FpsCapSettleOutcome { AlreadySet, Settled, Exhausted, WriteFailed }`
  - `public static class FpsCapSettler` with `internal static readonly TimeSpan QuietDebounce/WriteConfirmWindow/QuietWaitTimeout/QuietPollInterval`, `internal const int MaxWriteAttempts`, and
    `public static Task<FpsCapSettleOutcome> SettleAsync(IGlobalBasicSettingsProbe probe, IGlobalBasicSettingsWriter writer, int desiredCap, TimeProvider timeProvider, ILogger logger, CancellationToken ct)`

**Context — read this before writing tests.** Task 1 of the *previous* cycle lost roughly two hours to a `FakeTimeProvider` hang. `Task.Delay(TimeSpan, TimeProvider, ct)` flips its own status synchronously when the clock passes its due time, but the awaiting continuation resumes **asynchronously**. So firing several `Advance()` calls back-to-back can arm a later timer against a clock that has already finished moving, and the wait never completes. The pump helper in the tests below exists for exactly that; do not remove it, and do not "simplify" the advances into a single jump.

- [ ] **Step 1: Write the failing tests**

Create `src/ROROROblox.Tests/FpsCapSettlerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public sealed class FpsCapSettlerTests
{
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(5);

    /// <summary>Scripted read side. Each ReadFramerateCap() pops the next scripted value.</summary>
    private sealed class FakeProbe : IGlobalBasicSettingsProbe
    {
        private readonly Queue<int?> _caps;
        public int ReadCalls { get; private set; }
        public DateTimeOffset? Mtime { get; set; } = DateTimeOffset.UnixEpoch;

        public FakeProbe(params int?[] caps) => _caps = new Queue<int?>(caps);

        public int? ReadFramerateCap()
        {
            ReadCalls++;
            return _caps.Count > 0 ? _caps.Dequeue() : null;
        }

        public DateTimeOffset? GetLastWriteTimeUtc() => Mtime;
    }

    private sealed class RecordingWriter : IGlobalBasicSettingsWriter
    {
        public List<int?> Writes { get; } = new();
        public Exception? Throw { get; set; }

        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            if (Throw is not null) { throw Throw; }
            Writes.Add(fps);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Advance the fake clock, yielding between steps so each awaiting continuation gets to arm
    /// its next timer before the clock moves again. Advancing in one jump can leave a later timer
    /// armed against a clock that has stopped moving — a permanent stall, not a slow test.
    /// </summary>
    private static async Task AdvanceAsync(FakeTimeProvider clock, TimeSpan total, TimeSpan step)
    {
        var elapsed = TimeSpan.Zero;
        while (elapsed < total)
        {
            clock.Advance(step);
            elapsed += step;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
        }
    }

    [Fact]
    public async Task FileAlreadyHoldsTheCap_WritesNothingAndReturnsImmediately()
    {
        var probe = new FakeProbe(20);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var outcome = await FpsCapSettler
            .SettleAsync(probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None)
            .WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.AlreadySet, outcome);
        Assert.Empty(writer.Writes);
        Assert.Equal(1, probe.ReadCalls);
        // No time passed: the fast path must not wait for quiet.
        Assert.Equal(DateTimeOffset.UnixEpoch.UtcDateTime, clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task QuietFileThenSurvivingWrite_Settles()
    {
        // read 1: current cap is 9999 (not ours) -> take the slow path
        // read 2: after the confirm window, our 20 is still there -> settled
        var probe = new FakeProbe(9999, 20);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            FpsCapSettler.QuietDebounce + FpsCapSettler.WriteConfirmWindow + TimeSpan.FromSeconds(1),
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(new int?[] { 20 }, writer.Writes);
    }

    [Fact]
    public async Task WriteClobbered_RetriesAndSettlesOnTheSecondAttempt()
    {
        // read 1: 9999 (not ours)
        // read 2: 9999 again -> our write was clobbered, retry
        // read 3: 20 -> survived
        var probe = new FakeProbe(9999, 9999, 20);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            (FpsCapSettler.QuietDebounce + FpsCapSettler.WriteConfirmWindow) * 3,
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Equal(2, writer.Writes.Count);
    }

    [Fact]
    public async Task NeverSurvives_ExhaustsAttemptsAndStillReturns()
    {
        // Always reads back someone else's value: every attempt is clobbered.
        var probe = new FakeProbe(9999, 9999, 9999, 9999, 9999, 9999, 9999, 9999);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            (FpsCapSettler.QuietDebounce + FpsCapSettler.WriteConfirmWindow) * (FpsCapSettler.MaxWriteAttempts + 2),
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        // Exhausting attempts must NOT abort the launch — the caller proceeds with whatever we wrote.
        Assert.Equal(FpsCapSettleOutcome.Exhausted, outcome);
        Assert.Equal(FpsCapSettler.MaxWriteAttempts, writer.Writes.Count);
    }

    [Fact]
    public async Task WriterThrows_DegradesToWriteFailedRatherThanEscaping()
    {
        var probe = new FakeProbe(9999);
        var writer = new RecordingWriter { Throw = new GlobalBasicSettingsWriteException("disk on fire") };
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        await AdvanceAsync(clock,
            FpsCapSettler.QuietDebounce + TimeSpan.FromSeconds(1),
            FpsCapSettler.QuietPollInterval);

        var outcome = await task.WaitAsync(TestBound);

        Assert.Equal(FpsCapSettleOutcome.WriteFailed, outcome);
    }

    [Fact]
    public async Task FileKeepsChanging_QuietWaitTimesOutButStillWritesAndReturns()
    {
        var probe = new FakeProbe(9999, 20);
        var writer = new RecordingWriter();
        var clock = new FakeTimeProvider();

        var task = FpsCapSettler.SettleAsync(
            probe, writer, desiredCap: 20, clock, NullLogger.Instance, CancellationToken.None);

        // Keep bumping the mtime so the file never goes quiet, past the timeout.
        var elapsed = TimeSpan.Zero;
        var budget = FpsCapSettler.QuietWaitTimeout + FpsCapSettler.WriteConfirmWindow + TimeSpan.FromSeconds(2);
        while (elapsed < budget)
        {
            probe.Mtime = probe.Mtime!.Value + TimeSpan.FromMilliseconds(50);
            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
        }

        var outcome = await task.WaitAsync(TestBound);

        // A contended file must not block the launch forever.
        Assert.Equal(FpsCapSettleOutcome.Settled, outcome);
        Assert.Single(writer.Writes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~FpsCapSettlerTests"`
Expected: FAIL to compile — `FpsCapSettler` and `FpsCapSettleOutcome` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/ROROROblox.Core/FpsCapSettler.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>How a per-account FPS cap ended up being applied (or not).</summary>
public enum FpsCapSettleOutcome
{
    /// <summary>The file already held this cap. Nothing written, nothing waited for.</summary>
    AlreadySet,

    /// <summary>Written, and confirmed still present after the confirm window.</summary>
    Settled,

    /// <summary>Every attempt was overwritten. Launching anyway, with a cap that may be wrong.</summary>
    Exhausted,

    /// <summary>The writer failed. Degraded, non-blocking — the launch proceeds.</summary>
    WriteFailed,
}

/// <summary>
/// Makes a per-account FPS cap survive close-together launches.
/// <para>
/// Roblox keeps ONE settings file per install and a starting client re-persists its own
/// FramerateCap to it repeatedly for ~9 seconds (measured 2026-08-02). So the party that
/// overwrites our value is the PREVIOUS CLIENT, not the next launch — which is why the earlier
/// pid-based launch gate could be correct in every detail and still not fix the bug. In the
/// decisive run our write survived 170 milliseconds.
/// </para>
/// <para>
/// Correctness here comes from <em>confirming</em> the write, not from <see cref="QuietDebounce"/>
/// being the right length. If the debounce is too short we notice the clobber and retry; the cost
/// is latency, not a wrong cap. Guessing exactly this class of constant is what produced the
/// previous design's 1-second settle grace.
/// </para>
/// </summary>
public static class FpsCapSettler
{
    /// <summary>
    /// How long the file must be unmodified before we call it quiet. Must exceed the largest gap
    /// observed BETWEEN a client's own writes (3.25 s on 2026-08-02) with margin.
    /// </summary>
    internal static readonly TimeSpan QuietDebounce = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long to wait before re-reading to confirm our write survived. The observed clobber
    /// arrived 170 ms after our write; 1 s covers that with headroom.
    /// </summary>
    internal static readonly TimeSpan WriteConfirmWindow = TimeSpan.FromSeconds(1);

    /// <summary>Ceiling on waiting for quiet. A contended file must never block a launch forever.</summary>
    internal static readonly TimeSpan QuietWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often to re-check the file's last-write time.</summary>
    internal static readonly TimeSpan QuietPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Bounds the worst case at roughly 3 x (QuietDebounce + WriteConfirmWindow).</summary>
    internal const int MaxWriteAttempts = 3;

    public static async Task<FpsCapSettleOutcome> SettleAsync(
        IGlobalBasicSettingsProbe probe,
        IGlobalBasicSettingsWriter writer,
        int desiredCap,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken ct)
    {
        // Fast path. The race only exists when consecutive launches want DIFFERENT caps; if the
        // file already says what we need, there is nothing to protect and nothing to wait for.
        // This is what keeps the feature shippable -- most users set one cap across every account
        // and must not pay a settle window per launch for a case they are not in.
        if (probe.ReadFramerateCap() == desiredCap)
        {
            logger.LogDebug("FPS cap {Cap} already on disk; no write, no wait.", desiredCap);
            return FpsCapSettleOutcome.AlreadySet;
        }

        for (var attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            var wentQuiet = await WaitForQuietAsync(probe, timeProvider, ct).ConfigureAwait(false);
            if (!wentQuiet)
            {
                logger.LogInformation(
                    "Settings file never went quiet within {Timeout}; writing FPS cap {Cap} anyway (attempt {Attempt}).",
                    QuietWaitTimeout, desiredCap, attempt);
            }

            try
            {
                await writer.WriteFramerateCapAsync(desiredCap, ct).ConfigureAwait(false);
            }
            catch (GlobalBasicSettingsWriteException ex)
            {
                // Same posture as the pre-existing call site: degraded, non-blocking. Roblox falls
                // back to whatever cap is already in the file.
                logger.LogWarning(ex, "Could not write FPS cap {Cap}; launching with the existing value.", desiredCap);
                return FpsCapSettleOutcome.WriteFailed;
            }

            await Task.Delay(WriteConfirmWindow, timeProvider, ct).ConfigureAwait(false);

            if (probe.ReadFramerateCap() == desiredCap)
            {
                return FpsCapSettleOutcome.Settled;
            }

            logger.LogWarning(
                "FPS cap {Cap} was overwritten within {Window} (attempt {Attempt} of {Max}) — a client is still settling.",
                desiredCap, WriteConfirmWindow, attempt, MaxWriteAttempts);
        }

        // Out of attempts. Launch anyway: a contended settings file must never abort a launch.
        // This is the ONLY path where the original wrong-cap bug can still reach a user, so it is
        // logged at Error to make it impossible to miss in a support bundle.
        logger.LogError(
            "Gave up applying FPS cap {Cap} after {Max} attempts; this client may run the wrong cap.",
            desiredCap, MaxWriteAttempts);
        return FpsCapSettleOutcome.Exhausted;
    }

    /// <summary>
    /// Block until the settings file has been unmodified for <see cref="QuietDebounce"/>.
    /// Returns false if <see cref="QuietWaitTimeout"/> elapses first — the caller proceeds anyway.
    /// </summary>
    private static async Task<bool> WaitForQuietAsync(
        IGlobalBasicSettingsProbe probe,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var deadline = timeProvider.GetUtcNow() + QuietWaitTimeout;
        var lastSeen = probe.GetLastWriteTimeUtc();
        var quietSince = timeProvider.GetUtcNow();

        while (timeProvider.GetUtcNow() < deadline)
        {
            if (timeProvider.GetUtcNow() - quietSince >= QuietDebounce)
            {
                return true;
            }

            await Task.Delay(QuietPollInterval, timeProvider, ct).ConfigureAwait(false);

            var now = probe.GetLastWriteTimeUtc();
            if (now != lastSeen)
            {
                lastSeen = now;
                quietSince = timeProvider.GetUtcNow();
            }
        }

        return false;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~FpsCapSettlerTests"`
Expected: PASS, 6/6, in milliseconds. **If any test takes seconds, a delay is on the real clock** — find the `Task.Delay` missing its `TimeProvider` argument.

- [ ] **Step 5: Prove the fast-path test discriminates**

Temporarily delete the `if (probe.ReadFramerateCap() == desiredCap)` early return. Re-run the filter. `FileAlreadyHoldsTheCap_WritesNothingAndReturnsImmediately` must go RED. Restore it and confirm GREEN. Record both in your report — a fast path that silently stops being a fast path is invisible otherwise.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/FpsCapSettler.cs src/ROROROblox.Tests/FpsCapSettlerTests.cs
git commit -m "feat(core): settle the FPS cap by confirming the write, not by timing it"
```

---

## Task 3: Swap the mechanism in RobloxLauncher

**Files:**
- Modify: `src/ROROROblox.Core/RobloxLauncher.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs` (DI registration + factory args)
- Modify: `src/ROROROblox.Tests/RobloxLauncherTests.cs`
- Delete tests: `src/ROROROblox.Tests/RobloxLauncherGateTests.cs`

**Interfaces:**
- Consumes: `FpsCapSettler.SettleAsync(...)` and `FpsCapSettleOutcome` (Task 2), `IGlobalBasicSettingsProbe` (Task 1).
- Produces: `RobloxLauncher` gains two trailing optional ctor parameters on **both** overloads — `IGlobalBasicSettingsProbe? settingsProbe = null` and `ILogger<RobloxLauncher>? logger = null`. Existing positional and named call sites stay valid.

**Context:** `RobloxLauncher` currently writes the cap **before** `ExecuteLaunchAsync` and then calls `HoldForNewClientAsync` **after** it. The new sequence does all the waiting **before** the launch, so the post-launch hold disappears entirely. Both `LaunchAsync` overloads have this same shape — the typed one around line 112 and the legacy one around line 260.

`RobloxLauncher` has no logger today. That absence is why the previous design's outcome was discarded and why the 2026-08-02 investigation had to be reconstructed from Roblox's own logs and file timestamps instead of ours. Add one, defaulting to `NullLogger` so existing test call sites are untouched.

- [ ] **Step 1: Write the failing tests**

Add to `src/ROROROblox.Tests/RobloxLauncherTests.cs`. Its fakes (`StubRobloxApi`, `InMemoryAppSettings`, `RecordingProcessStarter`, `CreateLauncher`) are private to that file, so tests needing a real launcher must live there.

```csharp
    /// <summary>Settings probe whose reported cap the test controls.</summary>
    private sealed class StubSettingsProbe : IGlobalBasicSettingsProbe
    {
        public int? Cap { get; set; }
        public int ReadCalls { get; private set; }
        public int? ReadFramerateCap() { ReadCalls++; return Cap; }
        public DateTimeOffset? GetLastWriteTimeUtc() => DateTimeOffset.UnixEpoch;
    }

    /// <summary>Records every FramerateCap write in order.</summary>
    private sealed class RecordingGlobalBasicWriter : IGlobalBasicSettingsWriter
    {
        public List<int?> Writes { get; } = new();
        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            Writes.Add(fps);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task LaunchAsync_WhenTheFileAlreadyHoldsTheCap_WritesNothingAndDoesNotWait()
    {
        var probe = new StubSettingsProbe { Cap = 20 };
        var gbs = new RecordingGlobalBasicWriter();
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            globalBasicSettings: gbs,
            settingsProbe: probe);

        var result = await launcher
            .LaunchAsync(TestCookie, new LaunchTarget.Place(42), fpsCap: 20)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Empty(gbs.Writes);          // fast path: nothing written
        Assert.Equal(1, probe.ReadCalls);  // and nothing waited for
    }

    [Fact]
    public async Task LaunchAsync_WhenTheCapDiffers_WritesItBeforeStartingTheProcess()
    {
        // Probe reports our value on the confirm read, so the settle succeeds on attempt 1.
        var probe = new StubSettingsProbe { Cap = 9999 };
        var gbs = new RecordingGlobalBasicWriter();
        var clock = new FakeTimeProvider();
        var starter = new OrderRecordingStarter(gbs);
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            globalBasicSettings: gbs,
            settingsProbe: probe,
            processStarter: starter,
            timeProvider: clock);

        var task = launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(42), fpsCap: 20);

        // Once the write lands, flip the probe so the confirm read sees our value.
        for (var i = 0; i < 400 && gbs.Writes.Count == 0; i++)
        {
            clock.Advance(FpsCapSettler.QuietPollInterval);
            await Task.Yield();
        }
        probe.Cap = 20;
        for (var i = 0; i < 200; i++)
        {
            clock.Advance(FpsCapSettler.QuietPollInterval);
            await Task.Yield();
        }

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(new int?[] { 20 }, gbs.Writes);
        // The whole point: the cap is on disk before the client exists.
        Assert.True(starter.WriteCountAtStart == 1,
            $"expected the cap written before Process.Start, saw {starter.WriteCountAtStart} writes at start");
    }

    /// <summary>Captures how many cap writes had happened at the moment Process.Start was called.</summary>
    private sealed class OrderRecordingStarter : IProcessStarter
    {
        private readonly RecordingGlobalBasicWriter _writer;
        public int WriteCountAtStart { get; private set; } = -1;
        public OrderRecordingStarter(RecordingGlobalBasicWriter writer) => _writer = writer;
        public int StartViaShell(string fileNameOrUri)
        {
            WriteCountAtStart = _writer.Writes.Count;
            return 1;
        }
    }
```

`CreateLauncher` gains `IGlobalBasicSettingsProbe? settingsProbe = null` and passes it through. Add the parameter with a `null` default so its existing call sites are untouched.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~RobloxLauncherTests"`
Expected: FAIL to compile — `CreateLauncher` has no `settingsProbe` parameter.

- [ ] **Step 3: Add the constructor parameters and field**

In `RobloxLauncher.cs`, add to **both** constructor overloads as the last parameters, and forward from the short overload to the full one:

```csharp
        IGlobalBasicSettingsProbe? settingsProbe = null,
        ILogger<RobloxLauncher>? logger = null)
```

Add fields beside the existing ones:

```csharp
    private readonly IGlobalBasicSettingsProbe? _settingsProbe;
    private readonly ILogger _log;
```

and in the full constructor body:

```csharp
        _settingsProbe = settingsProbe;
        _log = logger ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
```

- [ ] **Step 4: Replace the write block in both launch paths**

In **both** `LaunchAsync` overloads, replace the `if (_globalBasicSettings is not null) { ... WriteFramerateCapAsync ... }` block with a call to a new shared private helper. Leave the `_clientAppSettings` block exactly as it is — that writes a different file and is explicitly out of scope (see the spec's Non-goals).

Add the helper next to the other private helpers:

```csharp
    /// <summary>
    /// Apply this account's FPS cap so it survives a close-together launch, then return. All the
    /// waiting happens HERE, before Process.Start — not after it. The party that overwrites our
    /// value is the previous client (which re-persists its own cap for ~9s), so there is nothing
    /// useful to wait for once our own process has started.
    /// </summary>
    private async Task ApplyFpsCapAsync(int fpsCap)
    {
        if (_globalBasicSettings is null)
        {
            return;
        }

        if (_settingsProbe is null)
        {
            // No probe wired (test call sites). Preserve the old behaviour exactly: write and move on.
            try
            {
                await _globalBasicSettings.WriteFramerateCapAsync(fpsCap).ConfigureAwait(false);
            }
            catch (GlobalBasicSettingsWriteException)
            {
                // Non-blocking. Roblox falls back to whatever cap is currently in the file.
            }
            return;
        }

        await FpsCapSettler.SettleAsync(
            _settingsProbe, _globalBasicSettings, fpsCap, _timeProvider, _log, CancellationToken.None)
            .ConfigureAwait(false);
    }
```

and call it from both overloads inside the existing `if (fpsCap.HasValue)` block:

```csharp
                await ApplyFpsCapAsync(fpsCap.Value).ConfigureAwait(false);
```

- [ ] **Step 5: Delete the pid-based gate**

Remove from `RobloxLauncher.cs`: `WaitForNewClientAsync`, `NewClientWaitOutcome`, `HoldForNewClientAsync`, `SnapshotBeforePids`, `NewClientPollInterval`, `NewClientWaitTimeout`, `SettleGrace`, the `_runningProbe` field and its constructor parameters, and both `await HoldForNewClientAsync(result, beforePids)` call sites. `ExecuteLaunchAsync` / `ExecuteLegacyLaunchAsync` return to a plain `LaunchResult` instead of a tuple.

Keep `FFlagReadHold` only if something still uses it after this; if nothing does, delete it too.

Delete `src/ROROROblox.Tests/RobloxLauncherGateTests.cs` entirely — every test in it covers the removed mechanism.

- [ ] **Step 6: Update the DI registration**

In `App.xaml.cs`, register the probe beside the other settings services:

```csharp
        services.AddSingleton<IGlobalBasicSettingsProbe, GlobalBasicSettingsProbe>();
```

and in the `IRobloxLauncher` factory, drop `runningProbe:` and add:

```csharp
            settingsProbe: sp.GetRequiredService<IGlobalBasicSettingsProbe>(),
            logger: sp.GetRequiredService<ILogger<RobloxLauncher>>(),
```

The DI test added in PR #70 (`ProductionDiRegistration_...`) resolves `IRobloxLauncher` through the real `ConfigureServices`; update its `Replace(...)` list to swap `IGlobalBasicSettingsProbe` for a fake, and assert the launcher holds it. If `IRobloxRunningProbe` is still registered for other consumers (the memory watchdog and stray cleanup use it), **leave that registration alone** — only the launcher stops using it.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS. Report the suite duration — a jump toward 35 s means a test started sleeping in real time.

- [ ] **Step 8: Commit**

```bash
git add src/ROROROblox.Core/RobloxLauncher.cs src/ROROROblox.App/App.xaml.cs src/ROROROblox.Tests/RobloxLauncherTests.cs
git rm src/ROROROblox.Tests/RobloxLauncherGateTests.cs
git commit -m "fix(launcher): settle the FPS cap before launching, retire the pid gate"
```

---

## Task 4: Warn when caps differ

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs`
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`
- Modify: `src/ROROROblox.App/MainWindow.xaml`
- Test: `src/ROROROblox.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: nothing from Tasks 1-3 at runtime — this is display only.
- Produces: `MainViewModel.FpsCapWarningText` (string, empty when there is nothing to warn about).

**Context:** There is an established banner pattern to mirror exactly. `MainWindow.xaml` around line 1390 has one `Border` in `Grid.Row="3"` holding three independently-collapsing `TextBlock`s (`StatusBanner`, `IdleSummaryText`, `ContestedBannerText`), each bound through `StringToVisibilityConverter`, with a `MultiDataTrigger` collapsing the whole `Border` when all are empty. Add a fourth child and a fourth condition. Do not add a Grid row.

Copy lives in `MultiInstanceCopy` beside `ContestedBanner`. The voice is clan-facing: plain, second person, sentence case, no jargon, no apology, and it names the way out. **Say 15 seconds, not 10** — the measured settle is 9-12 s plus the debounce and confirm window, and a user told 10 who waits 14 thinks the app has hung.

- [ ] **Step 1: Write the failing tests**

Add to `src/ROROROblox.Tests/MainViewModelTests.cs`:

This file has **no** `CreateViewModel` or `MakeSummary` helper. It builds a view model with
`Build(launcher)` — see the existing usage around `MainViewModelTests.cs:121`, which destructures
`var (vm, store, _, path) = Build(launcher);` and wraps the body in `try` / `finally` with the
file's own temp-path cleanup. **Mirror that structure exactly**, including the `finally`; these
four tests only differ from the existing ones in what they put in `vm.Accounts`.

Add this row helper beside the file's other private helpers:

```csharp
    /// <summary>A display row carrying just the FPS cap — everything else is irrelevant here.</summary>
    private static AccountSummary RowWithCap(int? fpsCap) => new(new Account(
        Guid.NewGuid(),
        DisplayName: "acct",
        AvatarUrl: "",
        CreatedAt: DateTimeOffset.UtcNow,
        LastLaunchedAt: null,
        FpsCap: fpsCap));
```

Then the four tests. Each one wraps its body in the same `try` / `finally` cleanup the
surrounding tests use:

```csharp
    [Fact]
    public void FpsCapWarning_IsEmpty_WhenEveryAccountSharesOneCap()
    {
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(20));

            vm.RefreshFpsCapWarning();

            Assert.Equal(string.Empty, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_IsEmpty_ForASingleAccount()
    {
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));

            vm.RefreshFpsCapWarning();

            Assert.Equal(string.Empty, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_Appears_WhenTwoAccountsHaveDifferentCaps()
    {
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(9999));

            vm.RefreshFpsCapWarning();

            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FpsCapWarning_TreatsUnsetAsItsOwnValue()
    {
        // One account capped and one left alone is still a mismatch: the capped account's write
        // and the uncapped account's client contend over the same shared file.
        var (vm, _, _, path) = Build(new CapturingRobloxLauncher());
        try
        {
            vm.Accounts.Add(RowWithCap(20));
            vm.Accounts.Add(RowWithCap(null));

            vm.RefreshFpsCapWarning();

            Assert.Equal(MultiInstanceCopy.FpsCapMismatchBanner, vm.FpsCapWarningText);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~FpsCapWarning"`
Expected: FAIL to compile — `FpsCapWarningText`, `RefreshFpsCapWarning`, and `FpsCapMismatchBanner` do not exist.

- [ ] **Step 3: Add the copy**

In `src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs`, beside `ContestedBanner`:

```csharp
    /// <summary>
    /// Shown when the accounts on screen do not all share one FPS cap. Roblox keeps a single
    /// settings file per install, so a differing cap forces RoRoRo to wait for each client to
    /// finish loading before starting the next. Quotes 15 seconds deliberately: the measured
    /// settle is 9-12 s plus the confirm window, and a user told 10 who waits 14 assumes a hang.
    /// </summary>
    public const string FpsCapMismatchBanner =
        "Different FPS caps will slow your launches. Roblox keeps one shared settings file for "
        + "every client, so RoRoRo waits for each account to finish loading before starting the "
        + "next — about 15 seconds each. Set every account to the same cap to launch at full speed.";
```

- [ ] **Step 4: Add the view-model property**

In `MainViewModel.cs`, beside `ContestedBannerText`:

```csharp
    private string _fpsCapWarningText = string.Empty;

    /// <summary>
    /// Non-empty when the accounts on screen do not all share one FPS cap — see
    /// <see cref="MultiInstanceCopy.FpsCapMismatchBanner"/>. Display only; it does not gate
    /// launching. The user chose this trade, so do not make them re-confirm it.
    /// </summary>
    public string FpsCapWarningText
    {
        get => _fpsCapWarningText;
        private set => SetField(ref _fpsCapWarningText, value);
    }

    /// <summary>
    /// Recompute the mismatch warning. "Unset" counts as its own distinct value: a capped account
    /// and an uncapped one still contend over the same shared file.
    /// </summary>
    internal void RefreshFpsCapWarning()
    {
        var distinct = Accounts.Select(a => a.FpsCap).Distinct().Count();
        FpsCapWarningText = distinct > 1 ? MultiInstanceCopy.FpsCapMismatchBanner : string.Empty;
    }
```

`MainViewModel` is `internal sealed class MainViewModel : INotifyPropertyChanged` with a hand-rolled `SetField` helper — not CommunityToolkit `ObservableObject`. Use `SetField`, matching `ContestedBannerText` directly above.

Call `RefreshFpsCapWarning()` wherever the account list or an account's cap changes. At minimum: after accounts load, and in the handler that persists a cap change (search for `SetFpsCapAsync`).

- [ ] **Step 5: Add the banner to the XAML**

In `MainWindow.xaml`, add a fourth condition to the `MultiDataTrigger` that collapses the shared `Border`:

```xml
                                <Condition Binding="{Binding FpsCapWarningText}" Value="" />
```

and a fourth child inside the `StackPanel`, after the `ContestedBannerText` block:

```xml
                <!-- FPS-cap mismatch (2026-08-02). Roblox keeps one shared settings file per
                     install, so differing per-account caps force a settle window between
                     launches. Display only — it never blocks launching. -->
                <TextBlock Text="{Binding FpsCapWarningText}"
                           FontSize="11"
                           Margin="0,4,0,0"
                           TextWrapping="Wrap"
                           Foreground="{DynamicResource MutedTextBrush}"
                           Visibility="{Binding FpsCapWarningText, Converter={StaticResource StringToVisibilityConverter}}" />
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~FpsCapWarning"`
Expected: PASS, 4/4.

- [ ] **Step 7: Run the full suite and build the app**

Run: `dotnet test ROROROblox.slnx`
Expected: PASS. XAML errors surface only at build, so confirm `ROROROblox.App` builds too.

- [ ] **Step 8: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MultiInstanceCopy.cs src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/MainWindow.xaml src/ROROROblox.Tests/MainViewModelTests.cs
git commit -m "feat(ui): warn when per-account FPS caps differ"
```

---

## Post-implementation

- [ ] **Banner-correct the spec** if the build drifted from `docs/superpowers/specs/2026-08-02-settings-quiet-window-design.md`. Add a banner block naming what was proposed versus what was built; never rewrite it top to bottom.
- [ ] **Update `docs/features.md`** — remove the "Launch gate settle grace is too short" entry, which this replaces. Leave the `ClientAppSettingsWriter` registry entry: still real, still out of scope.
- [ ] **MANUAL VERIFICATION — required, and no automated test substitutes for it.** Use **three** accounts with **three different** caps, launched close together, each checked in-game. Two accounts is a single contention pair; three is where a debounce that is too short would actually show itself. Every automated check passed on the previous design while the bug sat untouched, and a smoke test on 2026-08-02 proved nothing because both accounts happened to share a cap. **If the values do not differ, the run proves nothing.**
- [ ] **Watch the log for `Gave up applying FPS cap`.** That is the one path where the original bug still reaches a user. If it appears in normal use, `QuietDebounce` or `MaxWriteAttempts` is too low.
