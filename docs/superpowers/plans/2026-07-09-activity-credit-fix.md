# Activity Crediting Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A keep-alive plugin can credit an account's activity directly (`MarkAccountActive` RPC behind a new consent capability), so `GetAccountActivity` stops reporting plugin-kept-alive accounts as idle and ur-AFK stops re-firing.

**Architecture:** `ActivityMonitor.MarkActive(Guid)` (stamp + re-arm; same ConcurrentDictionary discipline as `Sample()` — no new locking). New proto unary `MarkAccountActive(MarkAccountActiveRequest) → Empty` + `host.commands.mark-account-active` capability (one const + one Catalog line + one interceptor-map line — the complete registration, per the extraction). App-level `IAccountActivityMarker` adapter (mirrors `ActivitySnapshotProvider`) injected into `PluginHostService`; handler parses the stringified-Guid account id, marks, returns Empty (untracked/bad id = domain no-op, never a gRPC error). PluginContract NuGet 0.4.0 → 0.5.0.

**Tech Stack:** .NET 10 / C#, gRPC (Grpc.AspNetCore, named pipes), xUnit (unit + real-pipe harness).

**Spec:** `docs/superpowers/specs/2026-07-09-activity-crediting-fix-design.md` (on this branch). **One deliberate simplification vs. spec §4:** no shared "stamp + re-evaluate" helper — `MarkActive` stamps `LastActivityAt` + clears `WarnLatched` directly; edge evaluation stays solely in `Sample()` (which already re-arms/uncrosses on its next tick). Stamping is equivalent and smaller; the spec's helper was insurance we don't need.

**Branch:** `feat/activity-credit-fix`. Coordinated follow-up (separate repo, NOT this plan): ur-AFK adopts the RPC and retires its client-side workaround.

## Global Constraints

- **Build/test with the explicit dotnet host** (`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" …`) against **`ROROROblox.slnx`** only.
- **The wall:** nothing detects or interacts with challenges/input content. This feature is a plugin *telling* the host it acted — the core stays observation-only. No hooks, no input reading.
- **Untracked account / malformed id ⇒ silent no-op** (domain-modeled, no RpcException beyond the interceptor's consent gate). A plugin marking a just-exited account must not fault the host.
- **Concurrency:** `_records` is a `ConcurrentDictionary` with benign in-place scalar writes; `MarkActive` uses the exact same discipline (no lock, no gate). Do NOT add locking.
- **Proto changes are additive-only** with the inline NuGet-version comment convention (`// Command surface (additive, NuGet 0.5.0): …`). csharp_namespace/package untouched.
- **Adding a `PluginHostService` ctor param ripples to every harness test constructing it positionally** — all constructions must be updated in the same task that changes the ctor (green commits).
- No user-profile paths; conventional commits; hand-rolled stubs matching the harness's existing doubles.

---

## File Structure

**Modified (Core):** `src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs`, `src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs` (add `MarkActive` to the interface).
**Modified (Contract):** `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`, `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` (0.4.0 → 0.5.0).
**Modified (App):** `src/ROROROblox.App/Plugins/PluginCapability.cs`, `Plugins/RpcMethodCapabilityMap.cs`, `Plugins/PluginHostService.cs`, `Plugins/IActivitySnapshotProvider.cs`-adjacent new file `Plugins/IAccountActivityMarker.cs` (interface + adapter), `App.xaml.cs` (DI arg).
**Tests:** `src/ROROROblox.Tests/` ActivityMonitor test file (extend); `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs` (extend) + a stub marker double + updates to every `new PluginHostService(` construction.

---

## Task 1: `ActivityMonitor.MarkActive` (Core)

**Files:**
- Modify: `src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs`, `src/ROROROblox.Core/Diagnostics/IActivityMonitor.cs`
- Test: the existing ActivityMonitor test file (locate: `ls src/ROROROblox.Tests | grep -i activity` — READ first, match its fake-probe/clock harness)

**Interfaces:**
- Produces: `void IActivityMonitor.MarkActive(Guid accountId)` — stamps now + re-arms. Consumed by Task 3's adapter.

- [ ] **Step 1: Write the failing tests** (adapt to the real harness — it has injectable `IClock`/probe fakes):

```csharp
    [Fact]
    public void MarkActive_TrackedAccount_StampsNow()
    {
        // launch account; advance fake clock; MarkActive(id);
        // GetSnapshot() shows SinceActivity == 0 for that account.
    }

    [Fact]
    public void MarkActive_UntrackedAccount_NoOps()
    {
        // MarkActive(Guid.NewGuid()) with no launched accounts -> no throw, snapshot unchanged.
    }

    [Fact]
    public void Mar