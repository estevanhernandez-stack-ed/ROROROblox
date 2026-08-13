# Ur Task action bridge — `ListMacros` / `repeat` / `StopMacro` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **Repo:** `%USERPROFILE%\Projects\rororo-ur-task` (NOT the RoRoRo repo). Run all commands from that repo root.

**Goal:** Extend the Ur Task action bridge (`626labs-ur-task` named pipe) with three additive things so the MCP connector can drive the full recovery loop: enumerate macros (`ListMacros`), run a macro on a loop (`repeat`), and stop a running playback (`StopMacro`).

**Architecture:** The bridge today deserializes each frame into exactly one type (`RunMacroRequest`) and replies with exactly one type (`RunMacroResponse`). Adding heterogeneous methods requires a **method-peeking envelope pre-parse** in the pipe server, then per-method typed dispatch. Two spec assumptions are corrected here from the code:
> - **`repeat` is NOT an existing flag.** The bridge path (`MacroRunInvoker → SequencePlayer.PlayAsync`) is single-pass; the forever-loop lives in a *different* runner (`AssignmentRunner`, hotkey-wired). `repeat` is implemented as a `do/while` wrap around the play delegate inside `MacroRunInvoker`, gated by a per-playback cancellation token.
> - **`StopMacro` has no playbackId registry today.** `PlaybackId` is generated and never stored; `SequencePlayer` is single-flight. This plan adds a `playbackId → CTS` registry in the invoker and an abort seam (`_abort` delegate → `SequencePlayer.Abort()`). With single-flight playback, `StopMacro` with or without an id resolves to "cancel the active playback(s) and abort the in-flight pass."

**Tech Stack:** .NET 10 / C# 14, System.Text.Json, named-pipe + 4-byte-length-prefixed JSON (`FrameCodec`), xUnit 2.9.

## Global Constraints

- **No solution file** — build/test target the csprojs directly. Standalone test run (the CI path):
  `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true -c Release`
- **Contract stays in the 1.x line** — `BridgeContract.IsSupportedVersion` accepts `"1."` prefixes. All new methods keep `contractVersion` `"1.0"`. Additive only: new records + one defaulted field, no breaking rename.
- **JSON is camelCase, `WhenWritingNull`** — via `BridgeContract.Json`. Every new record round-trips through it.
- **Frame cap is 64 KB** (`FrameCodec.MaxFrameBytes`). `ListMacros` responses must stay under it (fine for realistic macro counts).
- **Version bumps in lockstep** — `rororo-ur-task.csproj:10` AND `manifest.json:5` both carry the version; bump both 0.5.0 → 0.6.0, and add a `CHANGELOG.md` entry.
- **The connector never synthesizes input** — the bridge remains the only input path; these methods trigger/stop Ur Task's own runners, they do not add a new synthesis path.
- **Conventional commits** (`feat` / `test` / `build`).

---

## File Structure

- Modify `src/Ipc/BridgeContract.cs` — add `Repeat` field to `RunMacroRequest`; method-name constants; new records `RequestEnvelope`, `MacroSummary`, `ListMacrosResponse`, `StopMacroRequest`, `StopMacroResponse`.
- Modify `src/Ipc/IMacroRunInvoker.cs` — widen with `ListMacros()` + `StopMacro(...)`.
- Modify `src/Ipc/MacroRunInvoker.cs` — add abort delegate + playback registry; `ListMacros`; repeat `do/while`; `StopMacro`; busy-hardening.
- Modify `src/Ipc/MacroRunnerServer.cs` — envelope pre-parse + per-method dispatch/serialization.
- Modify `rororo-ur-task.csproj:10`, `manifest.json:5`, `CHANGELOG.md` — version 0.6.0.
- Tests (`tests/rororo-ur-task.Tests/Ipc/`): `BridgeContractTests.cs`, `MacroRunInvokerTests.cs`, `MacroRunnerServerTests.cs`.

---

## Task 1: Contract — `Repeat` field, method constants, new request/response records

**Files:**
- Modify: `src/Ipc/BridgeContract.cs`
- Test: `tests/rororo-ur-task.Tests/Ipc/BridgeContractTests.cs`

**Interfaces:**
- Produces: `RunMacroRequest.Repeat` (bool, defaulted false, appended last); `BridgeContract.MethodRunMacro` / `MethodListMacros` / `MethodStopMacro` string consts; `record RequestEnvelope(string? ContractVersion, string? Method)`; `record MacroSummary(string Id, string Name)`; `record ListMacrosResponse(bool Ok, IReadOnlyList<MacroSummary>? Macros, string? Reason, string? Detail)` with `Ok(...)`/`Refused(...)` factories; `record StopMacroRequest(string ContractVersion, string Method, string? PlaybackId, IReadOnlyList<string>? Targets, string? CallerPluginId)`; `record StopMacroResponse(bool Ok, int Stopped, string? Reason, string? Detail)` with factories. Consumed by Tasks 2-5 and Plan 3's bridge client.

- [ ] **Step 1: Write the failing round-trip tests.** In `BridgeContractTests.cs`, add:

```csharp
[Fact]
public void RunMacroRequest_Repeat_RoundTrips_AndDefaultsFalse()
{
    var withRepeat = new RunMacroRequest("1.0", "RunMacro", "m1", new[] { "123" }, null, "626labs.ur-mcp", Repeat: true);
    var json = JsonSerializer.Serialize(withRepeat, BridgeContract.Json);
    Assert.Contains("\"repeat\":true", json);
    Assert.Equal(true, JsonSerializer.Deserialize<RunMacroRequest>(json, BridgeContract.Json)!.Repeat);

    // Legacy payloads without "repeat" still deserialize, defaulting to false.
    var legacy = "{\"contractVersion\":\"1.0\",\"method\":\"RunMacro\",\"macroId\":\"m1\"}";
    Assert.False(JsonSerializer.Deserialize<RunMacroRequest>(legacy, BridgeContract.Json)!.Repeat);
}

[Fact]
public void Envelope_ExtractsMethod_FromAnyRequest()
{
    var json = "{\"contractVersion\":\"1.0\",\"method\":\"ListMacros\"}";
    var env = JsonSerializer.Deserialize<RequestEnvelope>(json, BridgeContract.Json)!;
    Assert.Equal("ListMacros", env.Method);
    Assert.Equal("1.0", env.ContractVersion);
}

[Fact]
public void ListMacrosResponse_RoundTrips_CamelCase()
{
    var resp = ListMacrosResponse.Ok(new[] { new MacroSummary("id-1", "Farm") });
    var json = JsonSerializer.Serialize(resp, BridgeContract.Json);
    Assert.Contains("\"macros\":[{\"id\":\"id-1\",\"name\":\"Farm\"}]", json);
    Assert.True(JsonSerializer.Deserialize<ListMacrosResponse>(json, BridgeContract.Json)!.Ok);
}

[Fact]
public void StopMacroRequest_RoundTrips()
{
    var req = new StopMacroRequest("1.0", "StopMacro", "pb-1", null, "626labs.ur-mcp");
    var json = JsonSerializer.Serialize(req, BridgeContract.Json);
    var back = JsonSerializer.Deserialize<StopMacroRequest>(json, BridgeContract.Json)!;
    Assert.Equal("pb-1", back.PlaybackId);
    Assert.Equal("StopMacro", back.Method);
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true --filter "RunMacroRequest_Repeat_RoundTrips_AndDefaultsFalse|Envelope_ExtractsMethod_FromAnyRequest|ListMacrosResponse_RoundTrips_CamelCase|StopMacroRequest_RoundTrips"`
Expected: FAIL — compile errors (`Repeat`, `RequestEnvelope`, `ListMacrosResponse`, `MacroSummary`, `StopMacroRequest` do not exist).

- [ ] **Step 3: Add the `Repeat` field.** In `RunMacroRequest`, append the parameter last (keeps positional call sites + JSON compatible):

```csharp
public sealed record RunMacroRequest(
    string ContractVersion,
    string Method,
    string MacroId,
    IReadOnlyList<string>? Targets,   // decimal user-ids, or ["foreground"]; null ⇒ foreground
    int? InterAltDelayMs,
    string? CallerPluginId,
    bool Repeat = false);             // loop macro end→start until StopMacro/abort
```

- [ ] **Step 4: Add method constants + envelope + new records.** In `BridgeContract`, replace the single `Method` const with a back-compatible set (keep `Method` as an alias so no existing reference breaks):

```csharp
    public const string Method = "RunMacro";          // back-compat alias
    public const string MethodRunMacro = "RunMacro";
    public const string MethodListMacros = "ListMacros";
    public const string MethodStopMacro = "StopMacro";
```

Add the envelope + records (top-level in the `Labs626.UrTask.Ipc` namespace, next to the other records):

```csharp
/// <summary>Minimal shape for peeking method + version before typed deserialization.</summary>
public sealed record RequestEnvelope(string? ContractVersion, string? Method);

public sealed record MacroSummary(string Id, string Name);

public sealed record ListMacrosResponse(
    bool Ok,
    IReadOnlyList<MacroSummary>? Macros,
    string? Reason,
    string? Detail)
{
    public static ListMacrosResponse Ok(IReadOnlyList<MacroSummary> macros) => new(true, macros, null, null);
    public static ListMacrosResponse Refused(string reason, string? detail = null) => new(false, null, reason, detail);
}

public sealed record StopMacroRequest(
    string ContractVersion,
    string Method,
    string? PlaybackId,                 // stop a specific playback; null ⇒ stop all active
    IReadOnlyList<string>? Targets,     // reserved for target-scoped stop; ignored while playback is single-flight
    string? CallerPluginId);

public sealed record StopMacroResponse(
    bool Ok,
    int Stopped,                        // how many playbacks were cancelled
    string? Reason,
    string? Detail)
{
    public static StopMacroResponse Done(int stopped) => new(true, stopped, null, null);
    public static StopMacroResponse Refused(string reason, string? detail = null) => new(false, 0, reason, detail);
}
```

> Naming note: `ListMacrosResponse.Ok(...)` (static factory) coexists with the `bool Ok` property — C# resolves the static-vs-instance by context, matching the existing `RunMacroResponse.Accepted`/`Refused` style. If the analyzer flags the name collision, rename the factory to `Success(...)` and update the test in Step 1 accordingly.

- [ ] **Step 5: Run to verify pass.**

Run: same filter as Step 2.
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/Ipc/BridgeContract.cs tests/rororo-ur-task.Tests/Ipc/BridgeContractTests.cs
git commit -m "feat(bridge): contract records for ListMacros, StopMacro, and RunMacro repeat"
```

---

## Task 2: `ListMacros` on the invoker

**Files:**
- Modify: `src/Ipc/IMacroRunInvoker.cs`
- Modify: `src/Ipc/MacroRunInvoker.cs`
- Test: `tests/rororo-ur-task.Tests/Ipc/MacroRunInvokerTests.cs`

**Interfaces:**
- Consumes: `MacroSummary` (Task 1), the invoker's existing `_loadMacros` delegate (`Func<IReadOnlyList<Macro>>`), `Macro.Id` (string), `Macro.Name` (string?).
- Produces: `IMacroRunInvoker.ListMacros() : IReadOnlyList<MacroSummary>` — id + name (null-name → `"(unnamed)"`), consumed by the server (Task 5) and Plan 3.

- [ ] **Step 1: Write the failing test.** In `MacroRunInvokerTests.cs`:

```csharp
[Fact]
public void ListMacros_ReturnsIdAndName_WithUnnamedFallback()
{
    var invoker = Build(
        macros: new[]
        {
            new Macro(1, "id-a", "Farm", /* remaining Macro params as the Build helper already supplies */),
            new Macro(1, "id-b", null,   /* ... */),
        },
        running: Array.Empty<AccountRegistry.AccountInfo>(),
        busy: false);

    var list = invoker.ListMacros();

    Assert.Equal(2, list.Count);
    Assert.Equal("Farm", Assert.Single(list, m => m.Id == "id-a").Name);
    Assert.Equal("(unnamed)", Assert.Single(list, m => m.Id == "id-b").Name);
}
```

> Use the existing test's exact `Macro(...)` construction (the `Build` helper and sibling tests already build `Macro` instances — copy their argument list verbatim so this compiles; only `Id` and `Name` matter here).

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true --filter ListMacros_ReturnsIdAndName_WithUnnamedFallback`
Expected: FAIL — `IMacroRunInvoker` has no `ListMacros`.

- [ ] **Step 3: Widen the interface.** In `IMacroRunInvoker.cs`, add:

```csharp
    IReadOnlyList<MacroSummary> ListMacros();
```

- [ ] **Step 4: Implement on `MacroRunInvoker`.** Add the method (reuses the existing `_loadMacros` delegate):

```csharp
    public IReadOnlyList<MacroSummary> ListMacros()
        => _loadMacros()
            .Select(m => new MacroSummary(m.Id, string.IsNullOrWhiteSpace(m.Name) ? "(unnamed)" : m.Name!))
            .ToList();
```

- [ ] **Step 5: Run to verify pass.**

Run: same filter as Step 2.
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/Ipc/IMacroRunInvoker.cs src/Ipc/MacroRunInvoker.cs tests/rororo-ur-task.Tests/Ipc/MacroRunInvokerTests.cs
git commit -m "feat(bridge): ListMacros — enumerate the macro library over the action bridge"
```

---

## Task 3: `repeat` — playback registry + `do/while` loop + busy-hardening

**Files:**
- Modify: `src/Ipc/MacroRunInvoker.cs` (registry field, ctor abort/registry seam, `RunAsync`, `ObservePlaybackAsync`)
- Test: `tests/rororo-ur-task.Tests/Ipc/MacroRunInvokerTests.cs`

**Interfaces:**
- Consumes: `RunMacroRequest.Repeat` (Task 1), the injected `_play` delegate (`Func<Macro, IReadOnlyList<AccountRegistry.AccountInfo>, int?, CancellationToken, Task>`).
- Produces: a `ConcurrentDictionary<string, CancellationTokenSource> _playbacks` keyed by `playbackId`; `internal int ActivePlaybackCount => _playbacks.Count` (test seam via `InternalsVisibleTo`); repeat behavior; RunAsync refuses while any playback is active (closes the between-pass single-flight gap).

- [ ] **Step 1: Write the failing repeat test.** In `MacroRunInvokerTests.cs` (the invoker exposes internals to the test project already):

```csharp
[Fact]
public async Task RunAsync_Repeat_LoopsUntilExternalCancel()
{
    int plays = 0;
    var cts = new CancellationTokenSource();
    var invoker = new MacroRunInvoker(
        loadMacros: () => new[] { new Macro(1, "m1", "Farm" /*, remaining params as Build supplies */) },
        snapshot: () => new[] { new AccountRegistry.AccountInfo(1001, 1, "alt-1", "acct-1") },
        resolveForegroundUserId: () => 1L,
        isBusy: () => false,
        abort: () => false,
        play: (macro, targets, delay, ct) =>
        {
            if (Interlocked.Increment(ref plays) >= 3) cts.Cancel(); // stop after 3 passes
            return Task.CompletedTask;
        });

    var resp = await invoker.RunAsync(
        new RunMacroRequest("1.0", "RunMacro", "m1", new[] { "1" }, null, "626labs.ur-mcp", Repeat: true),
        cts.Token);

    Assert.True(resp.Ok);                       // ack-on-accept
    Assert.True(SpinWait.SpinUntil(() => invoker.ActivePlaybackCount == 0, 2000));
    Assert.Equal(3, plays);                      // looped exactly until cancel, then stopped
}

[Fact]
public async Task RunAsync_RefusesWhileAPlaybackIsActive()
{
    var gate = new TaskCompletionSource();
    var invoker = new MacroRunInvoker(
        loadMacros: () => new[] { new Macro(1, "m1", "Farm" /* ... */) },
        snapshot: () => new[] { new AccountRegistry.AccountInfo(1001, 1, "alt-1", "acct-1") },
        resolveForegroundUserId: () => 1L,
        isBusy: () => false,
        abort: () => false,
        play: async (m, t, d, ct) => { await gate.Task; });

    var first = await invoker.RunAsync(
        new RunMacroRequest("1.0", "RunMacro", "m1", new[] { "1" }, null, "x", Repeat: true), CancellationToken.None);
    Assert.True(first.Ok);
    Assert.True(SpinWait.SpinUntil(() => invoker.ActivePlaybackCount == 1, 2000));

    var second = await invoker.RunAsync(
        new RunMacroRequest("1.0", "RunMacro", "m1", new[] { "1" }, null, "x"), CancellationToken.None);
    Assert.False(second.Ok);
    Assert.Equal("busy", second.Reason);

    gate.SetResult(); // let the first playback finish
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true --filter "RunAsync_Repeat_LoopsUntilExternalCancel|RunAsync_RefusesWhileAPlaybackIsActive"`
Expected: FAIL — the delegate ctor has no `abort` param; no `ActivePlaybackCount`; repeat not implemented.

- [ ] **Step 3: Add the registry field + abort delegate to the delegate ctor.** Add near the other fields:

```csharp
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _playbacks = new();
    private readonly Func<bool> _abort;

    internal int ActivePlaybackCount => _playbacks.Count;
```

Add `Func<bool> abort` to the delegate (test-facing) ctor parameter list and assign `_abort = abort ?? (() => false);`. In the production convenience ctor (`new MacroRunInvoker(MacroStore store, ... , SequencePlayer sequence)`), wire it: `abort: () => sequence.Abort()`.

- [ ] **Step 4: Register the playback + implement repeat in `ObservePlaybackAsync`.** In `RunAsync`, replace the fire-and-forget block so the playback is registered under its id with a linked CTS, and pass `request.Repeat`:

```csharp
    var playbackId = Guid.NewGuid().ToString("N");
    var playbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    _playbacks[playbackId] = playbackCts;
    _ = ObservePlaybackAsync(playbackId, macro, targets, request.InterAltDelayMs, request.Repeat, playbackCts);
    return Task.FromResult(RunMacroResponse.Accepted(playbackId));
```

Update `ObservePlaybackAsync` to loop and to deregister/dispose in `finally`:

```csharp
    private async Task ObservePlaybackAsync(
        string playbackId, Macro macro, IReadOnlyList<AccountRegistry.AccountInfo> targets,
        int? interAltDelayMs, bool repeat, CancellationTokenSource playbackCts)
    {
        try
        {
            do
            {
                await _play(macro, targets, interAltDelayMs, playbackCts.Token).ConfigureAwait(false);
            }
            while (repeat && !playbackCts.IsCancellationRequested);
        }
        catch (OperationCanceledException) { /* stopped */ }
        finally
        {
            _playbacks.TryRemove(playbackId, out _);
            playbackCts.Dispose();
        }
    }
```

Harden the busy guard at the top of `RunAsync` so an active playback (including a repeat between passes) refuses new runs:

```csharp
    if (_isBusy() || !_playbacks.IsEmpty)
        return Task.FromResult(RunMacroResponse.Refused("busy", "A sequence is already running."));
```

> Preserve the existing ordering (busy → unknown-macro → no-targets → accept) and the existing `ResolveTargets` call; only the registry/repeat wiring changes.

- [ ] **Step 5: Run to verify pass.**

Run: same filter as Step 2.
Expected: PASS (both).

- [ ] **Step 6: Commit.**

```bash
git add src/Ipc/MacroRunInvoker.cs tests/rororo-ur-task.Tests/Ipc/MacroRunInvokerTests.cs
git commit -m "feat(bridge): repeat — loop a macro until stopped, with an active-playback registry"
```

---

## Task 4: `StopMacro` — cancel by id or all, plus abort the in-flight pass

**Files:**
- Modify: `src/Ipc/IMacroRunInvoker.cs`
- Modify: `src/Ipc/MacroRunInvoker.cs`
- Test: `tests/rororo-ur-task.Tests/Ipc/MacroRunInvokerTests.cs`

**Interfaces:**
- Consumes: `StopMacroRequest` / `StopMacroResponse` (Task 1), `_playbacks` registry + `_abort` seam (Task 3).
- Produces: `IMacroRunInvoker.StopMacro(StopMacroRequest) : StopMacroResponse` — cancels the matching playback CTS (by id, or all when id is null), calls `_abort()` to stop the in-flight `SequencePlayer` pass, returns the count stopped.

- [ ] **Step 1: Write the failing test.** In `MacroRunInvokerTests.cs`:

```csharp
[Fact]
public async Task StopMacro_ByPlaybackId_CancelsThatPlayback_AndAborts()
{
    int aborts = 0;
    var gate = new TaskCompletionSource();
    var invoker = new MacroRunInvoker(
        loadMacros: () => new[] { new Macro(1, "m1", "Farm" /* ... */) },
        snapshot: () => new[] { new AccountRegistry.AccountInfo(1001, 1, "alt-1", "acct-1") },
        resolveForegroundUserId: () => 1L,
        isBusy: () => false,
        abort: () => { Interlocked.Increment(ref aborts); return true; },
        play: async (m, t, d, ct) => { await Task.Delay(Timeout.Infinite, ct); });

    var run = await invoker.RunAsync(
        new RunMacroRequest("1.0", "RunMacro", "m1", new[] { "1" }, null, "x", Repeat: true), CancellationToken.None);
    Assert.True(SpinWait.SpinUntil(() => invoker.ActivePlaybackCount == 1, 2000));

    var stop = invoker.StopMacro(new StopMacroRequest("1.0", "StopMacro", run.PlaybackId, null, "x"));

    Assert.True(stop.Ok);
    Assert.Equal(1, stop.Stopped);
    Assert.Equal(1, aborts);
    Assert.True(SpinWait.SpinUntil(() => invoker.ActivePlaybackCount == 0, 2000));
}

[Fact]
public void StopMacro_NoActivePlayback_ReturnsZero()
{
    var invoker = new MacroRunInvoker(
        loadMacros: Array.Empty<Macro>, snapshot: Array.Empty<AccountRegistry.AccountInfo>,
        resolveForegroundUserId: () => null, isBusy: () => false, abort: () => false,
        play: (m, t, d, ct) => Task.CompletedTask);

    var stop = invoker.StopMacro(new StopMacroRequest("1.0", "StopMacro", null, null, "x"));

    Assert.True(stop.Ok);
    Assert.Equal(0, stop.Stopped);
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true --filter "StopMacro_ByPlaybackId_CancelsThatPlayback_AndAborts|StopMacro_NoActivePlayback_ReturnsZero"`
Expected: FAIL — no `StopMacro` on the interface.

- [ ] **Step 3: Widen the interface.** In `IMacroRunInvoker.cs`:

```csharp
    StopMacroResponse StopMacro(StopMacroRequest request);
```

- [ ] **Step 4: Implement on `MacroRunInvoker`.**

```csharp
    public StopMacroResponse StopMacro(StopMacroRequest request)
    {
        int stopped = 0;

        if (!string.IsNullOrEmpty(request.PlaybackId))
        {
            if (_playbacks.TryGetValue(request.PlaybackId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                stopped = 1;
            }
        }
        else
        {
            foreach (var kvp in _playbacks)
            {
                try { kvp.Value.Cancel(); } catch (ObjectDisposedException) { }
                stopped++;
            }
        }

        // Abort the in-flight SequencePlayer pass so cancellation takes effect immediately,
        // not just at the next loop boundary. Single-flight today, so this stops the active pass.
        _abort();

        return StopMacroResponse.Done(stopped);
    }
```

> `_abort()` is idempotent-safe (`SequencePlayer.Abort()` returns false when nothing is running). Cancelling the CTS both breaks the repeat `do/while` and cancels the token passed into `_play`, so `Task.Delay(Timeout.Infinite, ct)` and real `SequencePlayer.PlayAsync` both unwind.

- [ ] **Step 5: Run to verify pass.**

Run: same filter as Step 2.
Expected: PASS (both).

- [ ] **Step 6: Commit.**

```bash
git add src/Ipc/IMacroRunInvoker.cs src/Ipc/MacroRunInvoker.cs tests/rororo-ur-task.Tests/Ipc/MacroRunInvokerTests.cs
git commit -m "feat(bridge): StopMacro — cancel a playback by id or all, aborting the in-flight pass"
```

---

## Task 5: Server dispatch — envelope pre-parse + `ListMacros` / `StopMacro` branches

**Files:**
- Modify: `src/Ipc/MacroRunnerServer.cs`
- Test: `tests/rororo-ur-task.Tests/Ipc/MacroRunnerServerTests.cs`

**Interfaces:**
- Consumes: `RequestEnvelope`, `RunMacroRequest`, `StopMacroRequest`, `ListMacrosResponse`, `StopMacroResponse`, `RunMacroResponse`, `BridgeContract.Method*` consts; `IMacroRunInvoker.{RunAsync, ListMacros, StopMacro}`; `FrameCodec.{ReadFrameAsync, WriteFrameAsync}`.
- Produces: a method-dispatched pipe server. Each branch serializes its own response type; the write path is unchanged (`byte[]` → `WriteFrameAsync`).

- [ ] **Step 1: Write the failing dispatch tests.** In `MacroRunnerServerTests.cs`, extend `FakeInvoker` to implement the widened interface (return canned `ListMacros` / `StopMacro` results), then:

```csharp
[Fact]
public async Task ListMacros_Dispatches_AndReturnsMacros()
{
    var invoker = new FakeInvoker { Macros = new[] { new MacroSummary("id-1", "Farm") } };
    var server = new MacroRunnerServer(invoker);

    var respJson = await RoundTripAsync(server, "{\"contractVersion\":\"1.0\",\"method\":\"ListMacros\"}");
    var resp = JsonSerializer.Deserialize<ListMacrosResponse>(respJson, BridgeContract.Json)!;

    Assert.True(resp.Ok);
    Assert.Equal("Farm", Assert.Single(resp.Macros!).Name);
}

[Fact]
public async Task StopMacro_Dispatches_AndReturnsOk()
{
    var invoker = new FakeInvoker { StopResult = StopMacroResponse.Done(1) };
    var server = new MacroRunnerServer(invoker);

    var respJson = await RoundTripAsync(server, "{\"contractVersion\":\"1.0\",\"method\":\"StopMacro\",\"playbackId\":\"pb-1\",\"callerPluginId\":\"x\"}");
    var resp = JsonSerializer.Deserialize<StopMacroResponse>(respJson, BridgeContract.Json)!;

    Assert.True(resp.Ok);
    Assert.Equal(1, resp.Stopped);
}

[Fact]
public async Task UnknownMethod_StillRefused()
{
    var server = new MacroRunnerServer(new FakeInvoker());
    var respJson = await RoundTripAsync(server, "{\"contractVersion\":\"1.0\",\"method\":\"Nope\",\"callerPluginId\":\"x\"}");
    var resp = JsonSerializer.Deserialize<RunMacroResponse>(respJson, BridgeContract.Json)!;
    Assert.False(resp.Ok);
    Assert.Equal("refused", resp.Reason);
}
```

> `RoundTripAsync` may currently return the deserialized `RunMacroResponse`; if so, add/adjust an overload that returns the raw JSON string (the harness already drives `HandleConnectionAsync` + `FrameCodec` over a real pipe pair — return the response frame as a UTF-8 string). Update `FakeInvoker` to also satisfy `ListMacros()` and `StopMacro(...)`.

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true --filter "ListMacros_Dispatches_AndReturnsMacros|StopMacro_Dispatches_AndReturnsOk|UnknownMethod_StillRefused"`
Expected: FAIL — server only knows `RunMacro`; `FakeInvoker` missing members.

- [ ] **Step 3: Rewrite `HandleConnectionAsync` to peek the method, then dispatch per-method.** Replace the body so it deserializes the envelope first and each branch produces its own response bytes:

```csharp
public async Task HandleConnectionAsync(Stream stream, CancellationToken ct)
{
    var frame = await FrameCodec.ReadFrameAsync(stream, ct).ConfigureAwait(false);
    if (frame is null) return; // peer connected then closed

    byte[] outBytes;
    try
    {
        var env = JsonSerializer.Deserialize<RequestEnvelope>(frame, BridgeContract.Json);
        outBytes = await DispatchAsync(env, frame, ct).ConfigureAwait(false);
    }
    catch (JsonException)
    {
        outBytes = JsonSerializer.SerializeToUtf8Bytes(
            RunMacroResponse.Refused("refused", "Malformed request JSON."), BridgeContract.Json);
    }

    await FrameCodec.WriteFrameAsync(stream, outBytes, ct).ConfigureAwait(false);
}

private async Task<byte[]> DispatchAsync(RequestEnvelope? env, byte[] frame, CancellationToken ct)
{
    if (env is null)
        return Bytes(RunMacroResponse.Refused("refused", "Empty request."));
    if (!BridgeContract.IsSupportedVersion(env.ContractVersion))
        return Bytes(RunMacroResponse.Refused("version-mismatch", $"Unsupported contractVersion '{env.ContractVersion}'."));

    switch (env.Method)
    {
        case BridgeContract.MethodRunMacro:
        {
            var req = JsonSerializer.Deserialize<RunMacroRequest>(frame, BridgeContract.Json);
            if (req is null) return Bytes(RunMacroResponse.Refused("refused", "Empty request."));
            if (string.IsNullOrWhiteSpace(req.CallerPluginId))
                return Bytes(RunMacroResponse.Refused("refused", "Missing callerPluginId."));
            return Bytes(await _invoker.RunAsync(req, ct).ConfigureAwait(false));
        }
        case BridgeContract.MethodListMacros:
            return Bytes(ListMacrosResponse.Ok(_invoker.ListMacros()));
        case BridgeContract.MethodStopMacro:
        {
            var req = JsonSerializer.Deserialize<StopMacroRequest>(frame, BridgeContract.Json);
            if (req is null) return Bytes(RunMacroResponse.Refused("refused", "Empty request."));
            if (string.IsNullOrWhiteSpace(req.CallerPluginId))
                return Bytes(StopMacroResponse.Refused("refused", "Missing callerPluginId."));
            return Bytes(_invoker.StopMacro(req));
        }
        default:
            return Bytes(RunMacroResponse.Refused("refused", $"Unknown method '{env.Method}'."));
    }
}

private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, BridgeContract.Json);
```

> Remove the old `ValidateAndDispatchAsync` (its logic is now in `DispatchAsync`). Keep the accept loop and pipe-name const untouched.

- [ ] **Step 4: Run to verify pass, then run the whole standalone suite to catch regressions in the old `RunMacro`/`WrongMethod` tests.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true`
Expected: all PASS. The prior `WrongMethod_RefusedWithoutDispatch` still passes (an unknown method still refuses); `RunMacro` dispatch unchanged in behavior.

- [ ] **Step 5: Commit.**

```bash
git add src/Ipc/MacroRunnerServer.cs tests/rororo-ur-task.Tests/Ipc/MacroRunnerServerTests.cs
git commit -m "feat(bridge): method-dispatched server — ListMacros + StopMacro alongside RunMacro"
```

---

## Task 6: Version bump to 0.6.0 + changelog + full green run

**Files:**
- Modify: `rororo-ur-task.csproj:10`, `manifest.json:5`, `CHANGELOG.md`

**Interfaces:**
- Produces: a shippable 0.6.0 with the bridge contract still `"1.0"` (wire) — the catalog's `latestVersion` for ur-task updates to 0.6.0 (Plan 3 / catalog upload, out of this plan's scope).

- [ ] **Step 1: Bump both version carriers.** `rororo-ur-task.csproj` line 10: `<Version>0.5.0</Version>` → `<Version>0.6.0</Version>`. `manifest.json` line 5: `"version": "0.5.0"` → `"version": "0.6.0"`. Leave `"contractVersion": "1.0"` unchanged (wire contract is back-compatible).

- [ ] **Step 2: Add a CHANGELOG entry.** Prepend to `CHANGELOG.md`:

```markdown
## 0.6.0

- Action bridge: `ListMacros` (enumerate the macro library), `repeat` on `RunMacro` (loop a macro until stopped), and `StopMacro` (cancel a playback by id or all). Enables AI-driven macro orchestration via the MCP connector. Wire contract stays 1.0 (additive).
```

- [ ] **Step 3: Full standalone suite green.**

Run: `dotnet test tests/rororo-ur-task.Tests/rororo-ur-task.Tests.csproj -p:StandaloneTestsOnly=true -c Release`
Expected: all PASS.

- [ ] **Step 4: Commit.**

```bash
git add rororo-ur-task.csproj manifest.json CHANGELOG.md
git commit -m "build(bridge): 0.6.0 — ListMacros/repeat/StopMacro action-bridge extensions"
```

---

## Self-Review

**Spec coverage** — §5.2 `ListMacros` → Task 2 + Task 5. §5.2 `repeat` → Task 3. §5.2 `StopMacro` → Task 4 + Task 5. Testing (§9: `ListMacros`/`repeat`/`StopMacro` unit-tested against library + runner fakes) → Tasks 2-5. Error surface (§8 "macro bridge busy → refused:busy verbatim") → preserved by the hardened busy guard (Task 3) and unchanged refusal reasons.

**Plan-time corrections (feed the spec banner):** (1) `repeat` is new behavior, implemented as a `do/while` in `MacroRunInvoker` — the spec's "the loop already exists internally, just expose it" refers to `AssignmentRunner`, which is not on the bridge path. (2) `StopMacro` has no playbackId registry today; this plan adds one plus an abort seam. With single-flight playback, id-scoped and all-scoped stop currently resolve to the same active playback set; `Targets` is reserved for when concurrent playbacks land.

**Placeholder scan** — the only intentional deferrals are the `Macro(...)` constructor argument lists in tests, explicitly flagged to copy from the existing `Build` helper (the full `Macro` record has fields beyond id/name that don't affect these tests). No TBD/TODO; every implementation step shows real code.

**Type consistency** — `MacroSummary(Id, Name)` used identically in Task 1 (contract), Task 2 (`ListMacros`), Task 5 (server). `StopMacroRequest`/`StopMacroResponse` fields match across Tasks 1/4/5. `RunMacroRequest.Repeat` name matches Tasks 1/3. Method constants `MethodRunMacro`/`MethodListMacros`/`MethodStopMacro` match Task 1 definitions and Task 5 switch. `IMacroRunInvoker` gains `ListMacros()` (Task 2) + `StopMacro(...)` (Task 4), both implemented on `MacroRunInvoker` and faked in `MacroRunnerServerTests` (Task 5).
