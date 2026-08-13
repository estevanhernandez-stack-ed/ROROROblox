# RoRoRo host — `GetAccounts` RPC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one additive gRPC RPC — `GetAccounts` — to the RoRoRo plugin host so a consented plugin can enumerate ALL saved accounts (running or not) with `is_main` + `roblox_user_id`, and ship it as `ROROROblox.PluginContract` NuGet 0.5.0.

**Architecture:** Mirror the existing `GetAccountActivity` RPC pattern — additive `.proto` RPC + messages, a dedicated snapshot provider abstraction, a capability gate, and integration + unit tests. Unlike `GetAccountActivity`, there is **no** existing all-saved-with-`is_main` provider across the plugin-host boundary, so this plan adds a new `ISavedAccountsProvider` + `SavedAccountSnapshot` record, a new adapter over `MainViewModel.AccountsSnapshot`, DI wiring, and a 10th constructor parameter on `PluginHostService` (which ripples to every `new PluginHostService(...)` call site in both test projects — those are updated in Task 2 to keep the build green).

**Tech Stack:** .NET 10 / C# 14, Grpc.AspNetCore 2.68, Grpc.Tools codegen (`GrpcServices="Both"`), xUnit, named-pipe gRPC.

## Global Constraints

- **Canonical solution is `ROROROblox.slnx`** — never build the legacy stray `ROROROblox.sln`. Bare `dotnet build` errors MSB1011 while both exist; always pass `ROROROblox.slnx`.
- **Contract is additive + wire-compatible** — `.proto` package stays `rororo.plugin.v1`; only append RPCs/messages, never renumber or remove fields. NuGet `<Version>` bumps 0.4.0 → 0.5.0.
- **`SavedAccount` is place-free** — a saved-but-not-running account is not in a game; live game identity comes from the already-enriched `GetRunningAccounts` (contract 0.4.0). Do not add `place_id`/`place_name` to `SavedAccount`.
- **Conventional commits** (`feat` / `test` / `docs` / `build`).
- **No plaintext-cookie exposure** — this RPC touches account metadata only (id, uid, display name, is_main). Never surface `.ROBLOSECURITY` or any secret in the DTO, logs, or errors.
- **`GetAccounts` is capability-gated** — `host.queries.accounts` (mirrors `host.queries.account-activity`), enforced per-RPC by the existing data-driven `CapabilityInterceptor`.

---

## File Structure

**Contract (`src/ROROROblox.PluginContract/`):**
- Modify `Protos/plugin_contract.proto` — append `GetAccounts` RPC + `SavedAccountsList` / `SavedAccount` messages.
- Modify `ROROROblox.PluginContract.csproj` — `<Version>` 0.4.0 → 0.5.0.

**Host app (`src/ROROROblox.App/Plugins/`):**
- Create `ISavedAccountsProvider.cs` — the new boundary provider interface + `SavedAccountSnapshot` record (with `IsMain`).
- Create `Adapters/MainViewModelSavedAccountsAdapter.cs` — concrete adapter over `MainViewModel.AccountsSnapshot`, no running filter, maps `IsMain`.
- Modify `PluginHostService.cs` — add `_savedAccounts` field + 10th ctor param + `GetAccounts` override.
- Modify `PluginCapability.cs` — add `HostQueriesAccounts` const + `Catalog` description entry.
- Modify `RpcMethodCapabilityMap.cs` — add `["GetAccounts"] = HostQueriesAccounts`.
- Modify `App.xaml.cs` — register `ISavedAccountsProvider` in DI + pass to `PluginHostService`.

**Unit tests (`src/ROROROblox.Tests/`):**
- Modify `PluginHostServiceTests.cs` — add `FakeSavedAccountsProvider` + `NoSavedAccounts()` helper, update all `new PluginHostService(...)` call sites, add `GetAccounts_ReturnsSavedListFromProvider`.
- Modify `RpcMethodCapabilityMapTests.cs` — add `GetAccounts_RequiresAccountsCapability`.
- Modify `PluginCapabilityTests.cs` — add `Accounts_HasCapabilityConstAndDescription`.

**Integration tests (`src/ROROROblox.PluginTestHarness/`):**
- Modify `EndToEndContractTests.cs` — add `EmptySavedAccounts` stub, update all `new PluginHostService(...)` call sites, add `GetAccounts_ConsentedPlugin_ReturnsSnapshot` + `GetAccounts_DeniedWhenCapabilityNotGranted`.
- Create `StubSavedAccountsProvider.cs` — seedable stub (clone of `StubActivityProvider.cs`).

**Docs (`docs/plugins/`):**
- Modify `AUTHOR_GUIDE.md` — add `host.queries.accounts` capability-table row.

---

## Task 1: Contract — append `GetAccounts` RPC + messages, bump NuGet to 0.5.0

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`
- Modify: `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj:9`

**Interfaces:**
- Produces: generated `RoRoRoHost.RoRoRoHostBase.GetAccounts(Empty, ServerCallContext)` (override target), `RoRoRoHost.RoRoRoHostClient.GetAccountsAsync(Empty)`, and message types `SavedAccountsList { RepeatedField<SavedAccount> Accounts }`, `SavedAccount { string AccountId; long RobloxUserId; string DisplayName; bool IsMain }`. Every later task and Plan 3 consume these exact names.

- [ ] **Step 1: Add the RPC to the `RoRoRoHost` service.** In `plugin_contract.proto`, inside `service RoRoRoHost`, immediately after the `GetAccountActivity` line (currently line 35), add:

```proto
  // Query surface (additive, NuGet 0.5.0): all saved accounts + is_main,
  // for name/main resolution. GetRunningAccounts lists running only.
  rpc GetAccounts(Empty) returns (SavedAccountsList);
```

- [ ] **Step 2: Add the messages.** In the messages section, immediately after the `AccountActivityList` message (currently ends line 161), add:

```proto
message SavedAccountsList {
  repeated SavedAccount accounts = 1;
}

message SavedAccount {
  string account_id = 1;       // RoRoRo internal Guid as string
  int64  roblox_user_id = 2;   // 0 when not yet resolved
  string display_name = 3;     // nickname override if set, else display name
  bool   is_main = 4;
}
```

- [ ] **Step 3: Bump the NuGet version.** In `ROROROblox.PluginContract.csproj`, change line 9:

```xml
    <Version>0.4.0</Version>
```

to:

```xml
    <Version>0.5.0</Version>
```

- [ ] **Step 4: Build the contract project to run codegen and verify the generated symbols exist.**

Run: `dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`
Expected: `Build succeeded`. Grpc.Tools regenerates `RoRoRoHostBase.GetAccounts`, `RoRoRoHostClient.GetAccountsAsync`, and the two message types automatically from the `.proto` (this project uses `GrpcServices="Both"`).

- [ ] **Step 5: Commit.**

```bash
git add src/ROROROblox.PluginContract/Protos/plugin_contract.proto src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj
git commit -m "feat(contract): 0.5.0 — GetAccounts RPC (all saved accounts + is_main)"
```

---

## Task 2: `PluginHostService.GetAccounts` + `ISavedAccountsProvider` + ctor param + all call sites

**Files:**
- Create: `src/ROROROblox.App/Plugins/ISavedAccountsProvider.cs`
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs` (fields ~16-26, ctor ~28-48, add override after `GetAccountActivity` ~124)
- Test: `src/ROROROblox.Tests/PluginHostServiceTests.cs`
- Build-fix only (no assertions yet): every `new PluginHostService(...)` call site in `src/ROROROblox.Tests/PluginHostServiceTests.cs` and `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`

**Interfaces:**
- Consumes: `SavedAccountsList` / `SavedAccount` from Task 1.
- Produces: `ISavedAccountsProvider { IReadOnlyList<SavedAccountSnapshot> Snapshot(); }` and `record SavedAccountSnapshot(string AccountId, long RobloxUserId, string DisplayName, bool IsMain)` — consumed by Task 4 (adapter) and the tests. `PluginHostService` ctor gains a 10th positional param `ISavedAccountsProvider savedAccounts` appended last.

- [ ] **Step 1: Create the provider abstraction + DTO.** Create `src/ROROROblox.App/Plugins/ISavedAccountsProvider.cs`:

```csharp
namespace ROROROblox.App.Plugins;

/// <summary>
/// Supplies a point-in-time snapshot of ALL saved accounts (running or not),
/// including which one is the main. Mirrors <see cref="IRunningAccountsProvider"/>
/// but does not filter to running and carries <c>IsMain</c>. Backs the
/// GetAccounts RPC (name/main resolution for the MCP connector).
/// </summary>
public interface ISavedAccountsProvider
{
    /// <summary>Point-in-time snapshot. Callers should treat the result as immutable.</summary>
    IReadOnlyList<SavedAccountSnapshot> Snapshot();
}

public sealed record SavedAccountSnapshot(
    string AccountId,
    long RobloxUserId,
    string DisplayName,
    bool IsMain);
```

- [ ] **Step 2: Write the failing unit test.** In `src/ROROROblox.Tests/PluginHostServiceTests.cs`, add a `FakeSavedAccountsProvider` (mirror `FakeRunningAccountsProvider` at ~line 345) and a `NoSavedAccounts()` helper (mirror `NoAccounts()` at ~line 34), then the test:

```csharp
    private static ISavedAccountsProvider NoSavedAccounts() =>
        new FakeSavedAccountsProvider(Array.Empty<SavedAccountSnapshot>());

    private sealed class FakeSavedAccountsProvider : ISavedAccountsProvider
    {
        private readonly List<SavedAccountSnapshot> _snapshots;
        public FakeSavedAccountsProvider(IEnumerable<SavedAccountSnapshot> snapshots) { _snapshots = snapshots.ToList(); }
        public IReadOnlyList<SavedAccountSnapshot> Snapshot() => _snapshots;
    }

    [Fact]
    public async Task GetAccounts_ReturnsSavedListFromProvider()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var saved = new FakeSavedAccountsProvider(new[]
        {
            new SavedAccountSnapshot("00000000-0000-0000-0000-000000000001", 12345, "Pokey", IsMain: true),
            new SavedAccountSnapshot("00000000-0000-0000-0000-000000000002", 0, "Spud", IsMain: false),
        });
        var service = new PluginHostService(
            registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(),
            new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), saved);

        var list = await service.GetAccounts(new Empty(), FakeServerCallContext.Create());

        Assert.Equal(2, list.Accounts.Count);
        var main = Assert.Single(list.Accounts, a => a.IsMain);
        Assert.Equal("Pokey", main.DisplayName);
        Assert.Equal(12345L, main.RobloxUserId);
        Assert.Equal("00000000-0000-0000-0000-000000000001", main.AccountId);
        var spud = Assert.Single(list.Accounts, a => a.DisplayName == "Spud");
        Assert.Equal(0L, spud.RobloxUserId);
        Assert.False(spud.IsMain);
    }
```

- [ ] **Step 3: Run the test to verify it fails to compile.**

Run: `dotnet test src/ROROROblox.Tests/ --filter GetAccounts_ReturnsSavedListFromProvider`
Expected: FAIL — compile error (`PluginHostService` has no 10th param / no `GetAccounts` method).

- [ ] **Step 4: Add the field, ctor param, and override to `PluginHostService`.** Add the field after `_activityProvider` (~line 25):

```csharp
    private readonly ISavedAccountsProvider _savedAccounts;
```

Append the ctor param after `activityProvider` (~line 43) and its null-check assignment:

```csharp
        IActivitySnapshotProvider activityProvider,
        ISavedAccountsProvider savedAccounts)
```
```csharp
        _savedAccounts = savedAccounts ?? throw new ArgumentNullException(nameof(savedAccounts));
```

Add the override after `GetAccountActivity` (~line 124):

```csharp
    public override Task<SavedAccountsList> GetAccounts(Empty request, ServerCallContext context)
    {
        var list = new SavedAccountsList();
        foreach (var a in _savedAccounts.Snapshot())
        {
            list.Accounts.Add(new SavedAccount
            {
                AccountId = a.AccountId,
                RobloxUserId = a.RobloxUserId,
                DisplayName = a.DisplayName,
                IsMain = a.IsMain,
            });
        }
        return Task.FromResult(list);
    }
```

- [ ] **Step 5: Fix every `new PluginHostService(...)` call site to keep the solution building.** Append `, NoSavedAccounts()` to each call in `PluginHostServiceTests.cs` (~12 sites). In `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`, first add a stub near the other harness stubs (~line 558, next to `EmptyAccounts`):

```csharp
    private sealed class EmptySavedAccounts : ISavedAccountsProvider
    {
        public IReadOnlyList<SavedAccountSnapshot> Snapshot() => Array.Empty<SavedAccountSnapshot>();
    }
```

then append `, new EmptySavedAccounts()` to each `new PluginHostService(...)` call in that file (~6 sites). These are build-green fixes only; Task 5 replaces the relevant one with a seeded stub.

- [ ] **Step 6: Run the unit test to verify it passes and the solution builds.**

Run: `dotnet test src/ROROROblox.Tests/ --filter GetAccounts_ReturnsSavedListFromProvider`
Expected: PASS. Then `dotnet build ROROROblox.slnx` → `Build succeeded` (confirms all harness call sites compile).

- [ ] **Step 7: Commit.**

```bash
git add src/ROROROblox.App/Plugins/ISavedAccountsProvider.cs src/ROROROblox.App/Plugins/PluginHostService.cs src/ROROROblox.Tests/PluginHostServiceTests.cs src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs
git commit -m "feat(plugins): GetAccounts host impl + ISavedAccountsProvider (all saved + is_main)"
```

---

## Task 3: Capability wiring — `host.queries.accounts` const, catalog, RPC map

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginCapability.cs` (const block ~line 19, `Catalog` ~line 39)
- Modify: `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs` (dict ~line 23)
- Test: `src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs`, `src/ROROROblox.Tests/PluginCapabilityTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `PluginCapability.HostQueriesAccounts == "host.queries.accounts"`, a `Catalog` entry making it known + grantable, and `RpcMethodCapabilityMap.Required("GetAccounts") == "host.queries.accounts"` (the `CapabilityInterceptor` is fully data-driven off this map — no interceptor edit needed).

- [ ] **Step 1: Write the failing capability-map test.** In `RpcMethodCapabilityMapTests.cs`, mirror `GetAccountActivity_RequiresActivityCapability` (~line 24):

```csharp
    [Fact]
    public void GetAccounts_RequiresAccountsCapability()
    {
        Assert.Equal(PluginCapability.HostQueriesAccounts, RpcMethodCapabilityMap.Required("GetAccounts"));
    }
```

- [ ] **Step 2: Write the failing capability-const test.** In `PluginCapabilityTests.cs`, mirror `AccountActivity_HasCapabilityConstAndDescription` (~line 67):

```csharp
    [Fact]
    public void Accounts_HasCapabilityConstAndDescription()
    {
        Assert.Equal("host.queries.accounts", PluginCapability.HostQueriesAccounts);
        Assert.True(PluginCapability.IsKnown(PluginCapability.HostQueriesAccounts));
        Assert.False(string.IsNullOrWhiteSpace(PluginCapability.Display(PluginCapability.HostQueriesAccounts)));
        Assert.True(PluginCapability.IsHostEnforced(PluginCapability.HostQueriesAccounts));
    }
```

- [ ] **Step 3: Run both tests to verify they fail.**

Run: `dotnet test src/ROROROblox.Tests/ --filter "GetAccounts_RequiresAccountsCapability|Accounts_HasCapabilityConstAndDescription"`
Expected: FAIL — `PluginCapability.HostQueriesAccounts` does not exist (compile error).

- [ ] **Step 4: Add the capability const + catalog entry.** In `PluginCapability.cs`, after the `HostQueriesAccountActivity` const (~line 19):

```csharp
    public const string HostQueriesAccounts = "host.queries.accounts";
```

In the `Catalog` dictionary, after the `HostQueriesAccountActivity` entry (~line 39):

```csharp
        [HostQueriesAccounts] = "See your saved accounts — names and which one is your main. Never reads cookies or passwords.",
```

- [ ] **Step 5: Add the RPC → capability map entry.** In `RpcMethodCapabilityMap.cs`, after the `GetAccountActivity` entry (~line 23):

```csharp
        ["GetAccounts"] = PluginCapability.HostQueriesAccounts,
```

- [ ] **Step 6: Run both tests to verify they pass.**

Run: `dotnet test src/ROROROblox.Tests/ --filter "GetAccounts_RequiresAccountsCapability|Accounts_HasCapabilityConstAndDescription"`
Expected: PASS.

- [ ] **Step 7: Commit.**

```bash
git add src/ROROROblox.App/Plugins/PluginCapability.cs src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs src/ROROROblox.Tests/PluginCapabilityTests.cs
git commit -m "feat(plugins): host.queries.accounts capability gate for GetAccounts"
```

---

## Task 4: Concrete adapter + DI registration

**Files:**
- Create: `src/ROROROblox.App/Plugins/Adapters/MainViewModelSavedAccountsAdapter.cs`
- Modify: `src/ROROROblox.App/App.xaml.cs` (DI registration + `PluginHostService` construction site)

**Interfaces:**
- Consumes: `ISavedAccountsProvider` / `SavedAccountSnapshot` (Task 2), `MainViewModel.AccountsSnapshot` → `IReadOnlyList<AccountSummary>` with `.Id` (Guid), `.RobloxUserId` (long?), `.RenderName` (string), `.IsMain` (bool).
- Produces: a DI-registered `ISavedAccountsProvider` the host resolves at startup. This adapter is thin VM glue (no unit test, matching the existing untested `MainViewModelRunningAccountsAdapter`); it is exercised by Task 5's integration test.

- [ ] **Step 1: Create the adapter.** Clone `MainViewModelRunningAccountsAdapter.cs`, drop the `if (!a.IsRunning) continue;` filter, map `IsMain`. Create `src/ROROROblox.App/Plugins/Adapters/MainViewModelSavedAccountsAdapter.cs`:

```csharp
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Bridges the app's account list (all saved accounts, running or not) to the
/// plugin host's <see cref="ISavedAccountsProvider"/>. Reads the lock-free
/// <see cref="MainViewModel.AccountsSnapshot"/> mirror — no filter — and carries
/// <c>IsMain</c> so the connector can resolve "the main" and launch not-running alts.
/// </summary>
internal sealed class MainViewModelSavedAccountsAdapter : ISavedAccountsProvider
{
    private readonly MainViewModel _vm;

    public MainViewModelSavedAccountsAdapter(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public IReadOnlyList<SavedAccountSnapshot> Snapshot()
    {
        var accounts = _vm.AccountsSnapshot;
        var saved = new List<SavedAccountSnapshot>(accounts.Count);
        foreach (var a in accounts)
        {
            saved.Add(new SavedAccountSnapshot(
                AccountId: a.Id.ToString(),
                RobloxUserId: a.RobloxUserId ?? 0,
                DisplayName: a.RenderName,
                IsMain: a.IsMain));
        }
        return saved;
    }
}
```

- [ ] **Step 2: Register in DI and pass to `PluginHostService`.** In `App.xaml.cs`, find the existing `IRunningAccountsProvider` / `MainViewModelRunningAccountsAdapter` registration (the dossier notes the adapter-wiring pattern near line 537) and add the parallel registration:

```csharp
services.AddSingleton<ISavedAccountsProvider>(sp =>
    new MainViewModelSavedAccountsAdapter(sp.GetRequiredService<MainViewModel>()));
```

Then, at the `new PluginHostService(...)` construction site in `App.xaml.cs` (the production wiring — distinct from the test call sites), append the resolved provider as the 10th argument, matching how `IActivitySnapshotProvider` is passed:

```csharp
    sp.GetRequiredService<IActivitySnapshotProvider>(),
    sp.GetRequiredService<ISavedAccountsProvider>())
```

> If `PluginHostService` is constructed via DI (all params resolved), adding the `AddSingleton` registration alone suffices and no explicit argument append is needed. Verify which pattern `App.xaml.cs` uses and apply the matching one.

- [ ] **Step 3: Build to verify wiring compiles and resolves.**

Run: `dotnet build ROROROblox.slnx`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit.**

```bash
git add src/ROROROblox.App/Plugins/Adapters/MainViewModelSavedAccountsAdapter.cs src/ROROROblox.App/App.xaml.cs
git commit -m "feat(plugins): wire MainViewModelSavedAccountsAdapter into the host DI"
```

---

## Task 5: Integration tests over the real named-pipe gRPC

**Files:**
- Create: `src/ROROROblox.PluginTestHarness/StubSavedAccountsProvider.cs`
- Modify: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`

**Interfaces:**
- Consumes: `ISavedAccountsProvider` / `SavedAccountSnapshot`; harness helpers `ConnectChannel`, `SingleInstalledPluginLookup`, `FixedHostState`, `EmptyAccounts`, `NoOpLauncher`, `PluginUITranslator`, `NullUIHost`, `StubActivityProvider`, `PluginHostStartupService`, `CapabilityInterceptor`.
- Produces: proof the RPC works end-to-end over the pipe, consented and denied.

- [ ] **Step 1: Create the seedable stub.** Clone `StubActivityProvider.cs`. Create `src/ROROROblox.PluginTestHarness/StubSavedAccountsProvider.cs`:

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.PluginTestHarness;

internal sealed class StubSavedAccountsProvider : ISavedAccountsProvider
{
    private readonly IReadOnlyList<SavedAccountSnapshot> _snapshots;
    public StubSavedAccountsProvider(params SavedAccountSnapshot[] snapshots) => _snapshots = snapshots;
    public IReadOnlyList<SavedAccountSnapshot> Snapshot() => _snapshots;
}
```

- [ ] **Step 2: Write the failing consented integration test.** In `EndToEndContractTests.cs`, mirror `GetAccountActivity_ConsentedPlugin_ReturnsSnapshot`. Grant `host.queries.accounts`, seed a `StubSavedAccountsProvider`, pass it as the 10th arg (replacing `new EmptySavedAccounts()` for this test):

```csharp
    [Fact]
    public async Task GetAccounts_ConsentedPlugin_ReturnsSnapshot()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var mainId = Guid.NewGuid().ToString();

        var registry = new SingleInstalledPluginLookup(new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1, Id = "626labs.test", Name = "Test", Version = "1.0",
                ContractVersion = "1.0", Publisher = "626", Description = "x",
                Capabilities = new[] { "host.queries.accounts" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.queries.accounts" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0", new FixedHostState("On"), new EmptyAccounts(),
            new InProcessPluginEventBus(), new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()), new StubActivityProvider(),
            new StubSavedAccountsProvider(
                new SavedAccountSnapshot(mainId, 12345, "Pokey", IsMain: true),
                new SavedAccountSnapshot(Guid.NewGuid().ToString(), 0, "Spud", IsMain: false)));

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { "host.queries.accounts" });

        var startup = new PluginHostStartupService(
            hostService, interceptor, NullLogger<PluginHostStartupService>.Instance, pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var resp = await client.GetAccountsAsync(new Empty());

            Assert.Equal(2, resp.Accounts.Count);
            var main = Assert.Single(resp.Accounts, a => a.IsMain);
            Assert.Equal(mainId, main.AccountId);
            Assert.Equal("Pokey", main.DisplayName);
            Assert.Equal(12345L, main.RobloxUserId);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }
```

> `new StubActivityProvider()` here assumes a param-less construction yields an empty snapshot; if `StubActivityProvider` requires args, pass an empty snapshot the same way the existing tests do.

- [ ] **Step 3: Write the failing denied integration test.** Mirror `GetAccountActivity_DeniedWhenCapabilityNotGranted` — declare/grant only `host.events.account-launched`, assert `PERMISSION_DENIED`:

```csharp
    [Fact]
    public async Task GetAccounts_DeniedWhenCapabilityNotGranted()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";

        var registry = new SingleInstalledPluginLookup(new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1, Id = "626labs.test", Name = "Test", Version = "1.0",
                ContractVersion = "1.0", Publisher = "626", Description = "x",
                Capabilities = new[] { "host.events.account-launched" },
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
            registry, "1.4.0", "1.0", new FixedHostState("On"), new EmptyAccounts(),
            new InProcessPluginEventBus(), new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()), new StubActivityProvider(),
            new StubSavedAccountsProvider(new SavedAccountSnapshot(Guid.NewGuid().ToString(), 1, "X", IsMain: true)));

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

            var ex = await Assert.ThrowsAsync<RpcException>(() => client.GetAccountsAsync(new Empty()).ResponseAsync);
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }
```

- [ ] **Step 4: Run to verify fail, then (after Steps already implemented the RPC in Task 2) pass.**

Run: `dotnet test src/ROROROblox.PluginTestHarness/ --filter "GetAccounts_ConsentedPlugin_ReturnsSnapshot|GetAccounts_DeniedWhenCapabilityNotGranted"`
Expected: PASS (both). If the consented test returns `PermissionDenied`, the Task 3 map entry is missing; if the denied test returns data, the interceptor isn't gating — re-check `RpcMethodCapabilityMap`.

- [ ] **Step 5: Commit.**

```bash
git add src/ROROROblox.PluginTestHarness/StubSavedAccountsProvider.cs src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs
git commit -m "test(plugins): GetAccounts integration — consented returns, denied 403 over named-pipe gRPC"
```

---

## Task 6: Author-guide doc row + full green run

**Files:**
- Modify: `docs/plugins/AUTHOR_GUIDE.md` (capability table ~line 105)

- [ ] **Step 1: Add the capability-table row.** After the `host.queries.account-activity` row (~line 105), add:

```markdown
| `host.queries.accounts` | Read all saved accounts — id, Roblox user id, display name, and which is main. Names/cookies-free. |
```

(Match the existing table's exact column shape.)

- [ ] **Step 2: Run the full solution test suite.**

Run: `dotnet test ROROROblox.slnx`
Expected: all tests PASS (unit + harness), including the four new tests. No regressions in the existing ~18 call sites.

- [ ] **Step 3: Commit.**

```bash
git add docs/plugins/AUTHOR_GUIDE.md
git commit -m "docs(plugins): document host.queries.accounts capability"
```

---

## Self-Review

**Spec coverage** — §5.1 (`GetAccounts` RPC + `SavedAccount` shape) → Tasks 1-5. New `host.queries.accounts` capability, consent-gated → Task 3 + Task 5 (denied test). Integration test over real named-pipe gRPC (§9) → Task 5. Unit test off account store (§9) → Task 2. All covered.

**Plan-time correction (feeds the spec banner):** the spec's §5.1 line "Host impl reads the account store (all saved accounts, `is_main` already tracked)" understates the work — the *data* is tracked on `AccountSummary`, but there is **no all-saved provider across the plugin-host boundary**, so this plan adds `ISavedAccountsProvider` + adapter + DI + a 10th ctor param (rippling to ~18 test call sites, all updated in Task 2). Scope unchanged; cost corrected.

**Placeholder scan** — no TBD/TODO; every code step shows real code; every run step shows the command + expected result.

**Type consistency** — `SavedAccount` (proto) fields `AccountId`/`RobloxUserId`/`DisplayName`/`IsMain` match `SavedAccountSnapshot` record fields and the adapter's mapping and every test's assertions. Method name is `GetAccounts` everywhere (proto RPC, override, `RpcMethodCapabilityMap` key, `GetAccountsAsync` client call). Capability string `host.queries.accounts` matches across const, catalog, map, tests, manifest, and doc row.
