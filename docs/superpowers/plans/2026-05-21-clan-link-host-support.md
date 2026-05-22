# clan-link Host Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the RoRoRo plugin contract so a plugin can (a) launch one of the user's accounts into a *targeted* server (a private-server link, a public place, or by following a friend) and (b) read the private-server link the user most recently launched into, so a Discord clan plugin can drive squad-up joins.

**Architecture:** Additive contract bump (NuGet 0.1.0 → 0.2.0, handshake `contractVersion` stays `"1.0"`). Two new gRPC methods reuse the host's already-built launch machinery — `MainViewModel.ResolveShareUrlAsync` + `LaunchAccountAsync(summary, overrideTarget)` + `IPrivateServerStore`. No new behavior in the launch pipeline; only a new entry point gated by two new capabilities. Existing plugins (`hello-plugin`, `rororo-ur-task`) are untouched.

**Tech Stack:** C# / .NET 10, gRPC (Grpc.Tools codegen from `plugin_contract.proto`, `GrpcServices="Both"`), xUnit. Branch: `feat/clan-link-host-support` (off `v1.7.0-install-deferral`).

**Build/test commands:**
- Build contract (regen bindings): `dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`
- Run a single test: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~<TestName>"`
- Run all plugin tests: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~Plugin|FullyQualifiedName~Capability|FullyQualifiedName~LaunchInvoker"`

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` | Wire contract: new RPCs + messages | Modify |
| `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj` | NuGet version 0.2.0 | Modify |
| `src/ROROROblox.Core/SavedPrivateServer.cs` | `ToShareUrl()` helper | Modify |
| `src/ROROROblox.App/Plugins/PluginCapability.cs` | Two new capability constants + catalog | Modify |
| `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs` | Map the two new RPCs to caps | Modify |
| `src/ROROROblox.App/Plugins/IPluginLaunchInvoker.cs` | Extend seam: target launch + current-server query + DTO | Modify |
| `src/ROROROblox.App/ViewModels/MainViewModel.cs` | Expose `LaunchAccountAsync` + store to the adapter (internal) | Modify |
| `src/ROROROblox.App/Plugins/Adapters/MainViewModelLaunchInvokerAdapter.cs` | Implement the new invoker methods | Modify |
| `src/ROROROblox.App/Plugins/PluginHostService.cs` | Two new RPC overrides | Modify |
| `src/ROROROblox.Tests/SavedPrivateServerShareUrlTests.cs` | ToShareUrl tests | Create |
| `src/ROROROblox.Tests/PluginCapabilityTests.cs` | Cap tests | Modify |
| `src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs` | Map tests | Create |
| `src/ROROROblox.Tests/MainViewModelLaunchInvokerAdapterTests.cs` | Adapter behavior | Create |
| `src/ROROROblox.Tests/PluginHostServiceTests.cs` | RPC override tests | Modify |
| `docs/plugins/AUTHOR_GUIDE.md` | Document the two new capabilities + recipes | Modify |

---

## Task 1: Proto — new RPCs + messages

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`

- [ ] **Step 1: Add the two RPCs to the `RoRoRoHost` service.** After the existing `rpc RequestLaunch(LaunchRequest) returns (LaunchResult);` line, add:

```proto
  // Command surface (contract 1.0, additive in NuGet 0.2.0)
  rpc RequestLaunchTarget(LaunchTargetRequest) returns (LaunchResult);
  // Query surface (additive)
  rpc GetCurrentServer(Empty) returns (CurrentServer);
```

- [ ] **Step 2: Add the messages.** After the existing `LaunchResult` message, add:

```proto
message LaunchTargetRequest {
  string account_id = 1;
  oneof target {
    // A Roblox server link in ANY form the host understands (private-server share
    // URL, PlaceLauncher accessCode URL, roblox.com/share token, public game URL,
    // or a bare place id). The host resolves it via its existing share-URL resolver.
    string share_url = 2;
    // Follow a friend into whatever server they're in (Roblox permission-checks).
    int64 follow_user_id = 3;
  }
}

message CurrentServer {
  bool present = 1;                    // false when no private server has been launched
  string share_url = 2;               // ready-to-post shareable link
  string place_name = 3;
  int64  place_id = 4;
  int64  last_launched_at_unix_ms = 5;
}
```

- [ ] **Step 3: Build the contract project to regenerate bindings.**

Run: `dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`
Expected: build succeeds; generated `RoRoRoHostBase` now has virtual `RequestLaunchTarget` + `GetCurrentServer`, and `LaunchTargetRequest` / `CurrentServer` types exist.

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.PluginContract/Protos/plugin_contract.proto
git commit -m "feat(contract): add RequestLaunchTarget + GetCurrentServer RPCs"
```

---

## Task 2: New capabilities

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginCapability.cs`
- Modify: `src/ROROROblox.Tests/PluginCapabilityTests.cs`

- [ ] **Step 1: Write failing tests.** Append to `PluginCapabilityTests`:

```csharp
    [Fact]
    public void IsKnown_ReturnsTrue_ForNewClanCapabilities()
    {
        Assert.True(PluginCapability.IsKnown("host.commands.launch-target"));
        Assert.True(PluginCapability.IsKnown("host.queries.current-server"));
    }

    [Fact]
    public void Display_LaunchTarget_DisclosesTheRisk()
    {
        var explanation = PluginCapability.Display("host.commands.launch-target");
        Assert.Contains("server", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown capability", explanation, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Run, verify fail.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~PluginCapabilityTests"`
Expected: FAIL (`IsKnown` returns false for the new strings).

- [ ] **Step 3: Add the constants + catalog entries.** In `PluginCapability.cs`, after `HostCommandsRequestLaunch`, add:

```csharp
    public const string HostCommandsLaunchTarget = "host.commands.launch-target";
    public const string HostQueriesCurrentServer = "host.queries.current-server";
```

And add to the `Catalog` dictionary (the consent-sheet disclosure — be honest, this is a powerful grant):

```csharp
        [HostCommandsLaunchTarget] = "Allow the plugin to launch one of your accounts into a Roblox server from a link or friend it provides.",
        [HostQueriesCurrentServer] = "Allow the plugin to read the private-server link you most recently launched, so it can share it.",
```

- [ ] **Step 4: Run, verify pass.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~PluginCapabilityTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginCapability.cs src/ROROROblox.Tests/PluginCapabilityTests.cs
git commit -m "feat(plugins): add launch-target + current-server capabilities"
```

---

## Task 3: Capability map

**Files:**
- Modify: `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`
- Create: `src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs`

- [ ] **Step 1: Write failing test.** Create `RpcMethodCapabilityMapTests.cs`:

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class RpcMethodCapabilityMapTests
{
    [Fact]
    public void RequestLaunchTarget_RequiresLaunchTargetCapability()
        => Assert.Equal(PluginCapability.HostCommandsLaunchTarget, RpcMethodCapabilityMap.Required("RequestLaunchTarget"));

    [Fact]
    public void GetCurrentServer_RequiresCurrentServerCapability()
        => Assert.Equal(PluginCapability.HostQueriesCurrentServer, RpcMethodCapabilityMap.Required("GetCurrentServer"));

    [Fact]
    public void RequestLaunchTarget_IsKnown()
        => Assert.True(RpcMethodCapabilityMap.IsKnown("RequestLaunchTarget"));

    [Fact]
    public void ExtractMethodName_PullsTrailingComponent()
        => Assert.Equal("RequestLaunchTarget", RpcMethodCapabilityMap.ExtractMethodName("/rororo.plugin.v1.RoRoRoHost/RequestLaunchTarget"));
}
```

- [ ] **Step 2: Run, verify fail.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~RpcMethodCapabilityMapTests"`
Expected: FAIL (`Required` returns null for unknown methods).

- [ ] **Step 3: Add map entries.** In `RpcMethodCapabilityMap.cs`, inside the `Map` dictionary after the `["RequestLaunch"]` line:

```csharp
        ["RequestLaunchTarget"] = PluginCapability.HostCommandsLaunchTarget,
        ["GetCurrentServer"] = PluginCapability.HostQueriesCurrentServer,
```

- [ ] **Step 4: Run, verify pass.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~RpcMethodCapabilityMapTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs src/ROROROblox.Tests/RpcMethodCapabilityMapTests.cs
git commit -m "feat(plugins): gate RequestLaunchTarget + GetCurrentServer"
```

---

## Task 4: `SavedPrivateServer.ToShareUrl()`

A shareable URL that round-trips back through `LaunchTarget.FromUrl`. LinkCode → `?privateServerLinkCode=`; AccessCode → a PlaceLauncher form carrying `accessCode=` + `placeId=` (FromUrl recognizes both).

**Files:**
- Modify: `src/ROROROblox.Core/SavedPrivateServer.cs`
- Create: `src/ROROROblox.Tests/SavedPrivateServerShareUrlTests.cs`

- [ ] **Step 1: Write failing tests.** Create `SavedPrivateServerShareUrlTests.cs`:

```csharp
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class SavedPrivateServerShareUrlTests
{
    private static SavedPrivateServer Make(string code, PrivateServerCodeKind kind) => new(
        Guid.NewGuid(), 920587237, code, kind, "name", "Place", "", DateTimeOffset.UtcNow, null);

    [Fact]
    public void LinkCode_BuildsPrivateServerLinkCodeUrl()
    {
        var url = Make("ABC-LINK", PrivateServerCodeKind.LinkCode).ToShareUrl();
        Assert.Contains("placeId=920587237".Replace("placeId=", ""), url); // place id present
        Assert.Contains("920587237", url);
        Assert.Contains("privateServerLinkCode=ABC-LINK", url);
        // Round-trips back to a PrivateServer LinkCode target.
        var parsed = LaunchTarget.FromUrl(url);
        var ps = Assert.IsType<LaunchTarget.PrivateServer>(parsed);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
        Assert.Equal("ABC-LINK", ps.Code);
    }

    [Fact]
    public void AccessCode_RoundTripsToAccessCodeTarget()
    {
        var url = Make("XYZ-ACCESS", PrivateServerCodeKind.AccessCode).ToShareUrl();
        var parsed = LaunchTarget.FromUrl(url);
        var ps = Assert.IsType<LaunchTarget.PrivateServer>(parsed);
        Assert.Equal(PrivateServerCodeKind.AccessCode, ps.Kind);
        Assert.Equal("XYZ-ACCESS", ps.Code);
    }
}
```

- [ ] **Step 2: Run, verify fail.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~SavedPrivateServerShareUrlTests"`
Expected: FAIL (`ToShareUrl` not defined).

- [ ] **Step 3: Implement.** Add to `SavedPrivateServer` (after `RenameName`/`RenderName`):

```csharp
    /// <summary>
    /// A shareable Roblox URL for this server that round-trips through
    /// <see cref="LaunchTarget.FromUrl"/>. LinkCode → the website share form;
    /// AccessCode → a PlaceLauncher form (FromUrl recognizes accessCode=).
    /// </summary>
    public string ToShareUrl() => CodeKind switch
    {
        PrivateServerCodeKind.LinkCode =>
            $"https://www.roblox.com/games/{PlaceId}?privateServerLinkCode={Uri.EscapeDataString(Code)}",
        PrivateServerCodeKind.AccessCode =>
            $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={PlaceId}&accessCode={Uri.EscapeDataString(Code)}",
        _ => $"https://www.roblox.com/games/{PlaceId}",
    };
```

- [ ] **Step 4: Run, verify pass.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~SavedPrivateServerShareUrlTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.Core/SavedPrivateServer.cs src/ROROROblox.Tests/SavedPrivateServerShareUrlTests.cs
git commit -m "feat(core): SavedPrivateServer.ToShareUrl round-trip helper"
```

---

## Task 5: Extend `IPluginLaunchInvoker`

**Files:**
- Modify: `src/ROROROblox.App/Plugins/IPluginLaunchInvoker.cs`

- [ ] **Step 1: Add the DTO + two methods.** Append to the interface and add the record below it (same file):

```csharp
    /// <summary>
    /// Launch <paramref name="accountId"/> into a target. Exactly one of
    /// <paramref name="shareUrl"/> / <paramref name="followUserId"/> is set by the caller;
    /// shareUrl is resolved by the host's share-URL resolver, followUserId becomes a
    /// follow-friend launch. Same return contract as <see cref="RequestLaunchAsync"/>.
    /// </summary>
    Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(
        string accountId, string? shareUrl, long? followUserId);

    /// <summary>Most-recently-launched saved private server, or null if none.</summary>
    Task<CurrentServerInfo?> GetCurrentServerAsync();
}

/// <summary>Host-internal DTO for the GetCurrentServer RPC (mapped to proto in the service).</summary>
public sealed record CurrentServerInfo(
    string ShareUrl, string PlaceName, long PlaceId, long LastLaunchedAtUnixMs);
```

> Note: the closing `}` shown above is the interface's closing brace — insert the two methods *before* it, then the record *after* it.

- [ ] **Step 2: Build to surface the contract break.**
Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj`
Expected: FAIL — `MainViewModelLaunchInvokerAdapter` and `FakeLaunchInvoker` no longer satisfy the interface. (Fixed in Tasks 7 + 8.)

- [ ] **Step 3: Commit (interface only; build red is expected until Task 7).**

```bash
git add src/ROROROblox.App/Plugins/IPluginLaunchInvoker.cs
git commit -m "feat(plugins): extend IPluginLaunchInvoker for target launch + current-server"
```

---

## Task 6: Expose `MainViewModel` seams to the adapter

The adapter lives in the same assembly (`ROROROblox.App`), so `internal` is enough. `ResolveShareUrlAsync` is already `public` (`MainViewModel.cs:671`). We need `LaunchAccountAsync` and the store reachable.

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add internal accessors.** Near the other internal/public members, add:

```csharp
    /// <summary>Plugin-host seam: launch a specific account into a resolved target.</summary>
    internal Task LaunchAccountForPluginAsync(AccountSummary summary, LaunchTarget target)
        => LaunchAccountAsync(summary, overrideTarget: target);

    /// <summary>Plugin-host seam: read-only access to the saved private-server store.</summary>
    internal IPrivateServerStore PrivateServerStoreForPlugin => _privateServerStore;
```

(Do NOT change the visibility of `LaunchAccountAsync` itself — wrap it, keeping the private method private.)

- [ ] **Step 2: Build.**
Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj`
Expected: still FAIL only on the adapter not implementing the new interface members (Task 7). No new errors from this file.

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs
git commit -m "feat(plugins): expose MainViewModel launch + store seams to the host adapter"
```

---

## Task 7: Implement the adapter

**Files:**
- Modify: `src/ROROROblox.App/Plugins/Adapters/MainViewModelLaunchInvokerAdapter.cs`
- Create: `src/ROROROblox.Tests/MainViewModelLaunchInvokerAdapterTests.cs`

> The adapter holds `MainViewModel _vm`. The new methods reuse the existing eligibility checks. Because `MainViewModel` is hard to construct in a unit test, these tests target the *resolution + dispatch decision* by extracting the pure logic. Implement the adapter to delegate the testable decisions to small static helpers so the tests below compile against them.

- [ ] **Step 1: Write failing tests.** Create `MainViewModelLaunchInvokerAdapterTests.cs`:

```csharp
using ROROROblox.App.Plugins.Adapters;

namespace ROROROblox.Tests;

public class MainViewModelLaunchInvokerAdapterTests
{
    [Fact]
    public void ValidateLaunchTargetArgs_RejectsMissingAccountId()
    {
        var (ok, reason) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs("", "url", null);
        Assert.False(ok);
        Assert.Contains("account", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_RejectsNonGuidAccountId()
    {
        var (ok, reason) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs("not-a-guid", "url", null);
        Assert.False(ok);
        Assert.Contains("GUID", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_RejectsNoTarget()
    {
        var (ok, reason) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs(Guid.NewGuid().ToString(), null, null);
        Assert.False(ok);
        Assert.Contains("target", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_AcceptsShareUrl()
    {
        var (ok, _) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs(Guid.NewGuid().ToString(), "https://x", null);
        Assert.True(ok);
    }

    [Fact]
    public void ValidateLaunchTargetArgs_AcceptsFollowUserId()
    {
        var (ok, _) = MainViewModelLaunchInvokerAdapter.ValidateLaunchTargetArgs(Guid.NewGuid().ToString(), null, 12345L);
        Assert.True(ok);
    }
}
```

- [ ] **Step 2: Run, verify fail.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~MainViewModelLaunchInvokerAdapterTests"`
Expected: FAIL (`ValidateLaunchTargetArgs` not defined).

- [ ] **Step 3: Implement.** Add the static validator + the two interface methods to `MainViewModelLaunchInvokerAdapter`. Reuse `LaunchTarget.FollowFriend` and `_vm.ResolveShareUrlAsync` / `_vm.LaunchAccountForPluginAsync` / `_vm.PrivateServerStoreForPlugin`:

```csharp
    internal static (bool ok, string? reason) ValidateLaunchTargetArgs(string accountId, string? shareUrl, long? followUserId)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return (false, "accountId is required.");
        if (!Guid.TryParse(accountId, out _)) return (false, $"accountId '{accountId}' is not a valid GUID.");
        if (string.IsNullOrWhiteSpace(shareUrl) && followUserId is null) return (false, "A launch target (share_url or follow_user_id) is required.");
        return (true, null);
    }

    public async Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(
        string accountId, string? shareUrl, long? followUserId)
    {
        var (argsOk, argsReason) = ValidateLaunchTargetArgs(accountId, shareUrl, followUserId);
        if (!argsOk) return (false, argsReason, 0);

        var id = Guid.Parse(accountId);
        var summary = _vm.Accounts.FirstOrDefault(a => a.Id == id);
        if (summary is null) return (false, $"No saved account with id {id}.", 0);
        if (summary.SessionExpired) return (false, "Account session is expired; re-add the account first.", 0);
        if (summary.IsLaunching) return (false, "Account is already launching.", 0);
        if (summary.IsRunning) return (false, "Account is already running.", 0);

        LaunchTarget? target;
        if (followUserId is { } uid)
        {
            target = new LaunchTarget.FollowFriend(uid);
        }
        else
        {
            target = await _vm.ResolveShareUrlAsync(shareUrl!).ConfigureAwait(false);
            if (target is null) return (false, "Couldn't read that as a Roblox server link.", 0);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            await _vm.LaunchAccountForPluginAsync(summary, target).ConfigureAwait(false);
        else
            await dispatcher.InvokeAsync(() => _vm.LaunchAccountForPluginAsync(summary, target)).Task.Unwrap().ConfigureAwait(false);

        return (true, null, 0); // PID arrives via SubscribeAccountLaunched
    }

    public async Task<CurrentServerInfo?> GetCurrentServerAsync()
    {
        var servers = await _vm.PrivateServerStoreForPlugin.ListAsync().ConfigureAwait(false);
        var newest = servers.Where(s => s.LastLaunchedAt is not null)
                            .OrderByDescending(s => s.LastLaunchedAt)
                            .FirstOrDefault();
        if (newest is null) return null;
        return new CurrentServerInfo(
            newest.ToShareUrl(),
            string.IsNullOrEmpty(newest.PlaceName) ? newest.RenderName : newest.PlaceName,
            newest.PlaceId,
            (newest.LastLaunchedAt!.Value).ToUnixTimeMilliseconds());
    }
```

Add `using ROROROblox.Core;` if not present (for `LaunchTarget`, `CurrentServerInfo` lives in `ROROROblox.App.Plugins`).

- [ ] **Step 4: Run, verify pass + app builds.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~MainViewModelLaunchInvokerAdapterTests"`
Expected: PASS. (App build still red until the test-file `FakeLaunchInvoker` is updated in Task 8; that's fine.)

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/Adapters/MainViewModelLaunchInvokerAdapter.cs src/ROROROblox.Tests/MainViewModelLaunchInvokerAdapterTests.cs
git commit -m "feat(plugins): adapter implements target launch + current-server"
```

---

## Task 8: PluginHostService RPC overrides

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs`
- Modify: `src/ROROROblox.Tests/PluginHostServiceTests.cs`

- [ ] **Step 1: Update the test `FakeLaunchInvoker` + add RPC tests.** In `PluginHostServiceTests.cs`, replace the `FakeLaunchInvoker` class with one that implements the new members, and add two tests:

```csharp
    private sealed class FakeLaunchInvoker : IPluginLaunchInvoker
    {
        public List<string> Invocations { get; } = new();
        public List<(string acct, string? url, long? follow)> TargetInvocations { get; } = new();
        public CurrentServerInfo? Current { get; set; }

        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
        {
            Invocations.Add(accountId);
            return Task.FromResult<(bool, string?, int)>((true, null, 12345));
        }

        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(string accountId, string? shareUrl, long? followUserId)
        {
            TargetInvocations.Add((accountId, shareUrl, followUserId));
            return Task.FromResult<(bool, string?, int)>((true, null, 6789));
        }

        public Task<CurrentServerInfo?> GetCurrentServerAsync() => Task.FromResult(Current);
    }
```

Add the two tests:

```csharp
    [Fact]
    public async Task RequestLaunchTarget_DispatchesShareUrl_ToLauncher()
    {
        var fake = new FakeLaunchInvoker();
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator());

        var acct = Guid.NewGuid().ToString();
        var result = await service.RequestLaunchTarget(new LaunchTargetRequest
        {
            AccountId = acct,
            ShareUrl = "https://www.roblox.com/games/1?privateServerLinkCode=ABC",
        }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        Assert.Equal(6789, result.ProcessId);
        var inv = Assert.Single(fake.TargetInvocations);
        Assert.Equal(acct, inv.acct);
        Assert.Equal("https://www.roblox.com/games/1?privateServerLinkCode=ABC", inv.url);
        Assert.Null(inv.follow);
    }

    [Fact]
    public async Task GetCurrentServer_ReturnsPresentFalse_WhenNone()
    {
        var fake = new FakeLaunchInvoker { Current = null };
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator());

        var result = await service.GetCurrentServer(new Empty(), FakeServerCallContext.Create());
        Assert.False(result.Present);
    }

    [Fact]
    public async Task GetCurrentServer_MapsInfo_WhenPresent()
    {
        var fake = new FakeLaunchInvoker { Current = new CurrentServerInfo("https://x", "Pet Sim", 99, 1700000000000) };
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator());

        var result = await service.GetCurrentServer(new Empty(), FakeServerCallContext.Create());
        Assert.True(result.Present);
        Assert.Equal("https://x", result.ShareUrl);
        Assert.Equal("Pet Sim", result.PlaceName);
        Assert.Equal(99, result.PlaceId);
        Assert.Equal(1700000000000, result.LastLaunchedAtUnixMs);
    }
```

- [ ] **Step 2: Run, verify fail.**
Run: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~PluginHostServiceTests"`
Expected: FAIL (overrides not defined).

- [ ] **Step 3: Add the overrides.** In `PluginHostService.cs`, after `RequestLaunch`:

```csharp
    public override async Task<LaunchResult> RequestLaunchTarget(LaunchTargetRequest request, ServerCallContext context)
    {
        string? shareUrl = request.TargetCase == LaunchTargetRequest.TargetOneofCase.ShareUrl ? request.ShareUrl : null;
        long? followUserId = request.TargetCase == LaunchTargetRequest.TargetOneofCase.FollowUserId ? request.FollowUserId : null;
        var (ok, reason, pid) = await _launcher.RequestLaunchTargetAsync(request.AccountId, shareUrl, followUserId).ConfigureAwait(false);
        return new LaunchResult { Ok = ok, FailureReason = reason ?? string.Empty, ProcessId = pid };
    }

    public override async Task<CurrentServer> GetCurrentServer(Empty request, ServerCallContext context)
    {
        var info = await _launcher.GetCurrentServerAsync().ConfigureAwait(false);
        if (info is null) return new CurrentServer { Present = false };
        return new CurrentServer
        {
            Present = true,
            ShareUrl = info.ShareUrl,
            PlaceName = info.PlaceName,
            PlaceId = info.PlaceId,
            LastLaunchedAtUnixMs = info.LastLaunchedAtUnixMs,
        };
    }
```

- [ ] **Step 4: Run all plugin tests + build app.**
Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj` then `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj --filter "FullyQualifiedName~Plugin|FullyQualifiedName~Capability|FullyQualifiedName~LaunchInvoker"`
Expected: build PASS, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginHostService.cs src/ROROROblox.Tests/PluginHostServiceTests.cs
git commit -m "feat(plugins): RequestLaunchTarget + GetCurrentServer RPC handlers"
```

---

## Task 9: NuGet version bump

**Files:**
- Modify: `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`

- [ ] **Step 1: Bump version.** Change `<Version>0.1.0</Version>` to `<Version>0.2.0</Version>`.

- [ ] **Step 2: Build the contract.**
Run: `dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Release`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj
git commit -m "chore(contract): bump to 0.2.0 (additive: launch-target + current-server)"
```

---

## Task 10: Docs — AUTHOR_GUIDE capability tables + recipes

**Files:**
- Modify: `docs/plugins/AUTHOR_GUIDE.md`

- [ ] **Step 1: Add the two capabilities** to the `host.*` table (after `host.commands.request-launch`):

```markdown
| `host.commands.launch-target` | `RequestLaunchTarget(accountId, share_url | follow_user_id)` — launch an account into a specific server (private link, public place, or follow-friend) |
| `host.queries.current-server` | `GetCurrentServer()` — read the user's most-recently-launched private-server share link |
```

- [ ] **Step 2: Add a recipe** under "Recipes" for the squad-up round-trip (share link via `GetCurrentServer`, join via `RequestLaunchTarget`). Note: `contractVersion` stays `"1.0"`; these need NuGet `0.2.0`.

- [ ] **Step 3: Commit**

```bash
git add docs/plugins/AUTHOR_GUIDE.md
git commit -m "docs(author-guide): document launch-target + current-server capabilities"
```

---

## Self-review notes (carried from the spec)

- **contractVersion stays `"1.0"`** — additive proto + capability-gated; do NOT bump the handshake string (would reject `hello-plugin` / `rororo-ur-task`). Verified against `PluginHostService.cs:61` exact-match handshake.
- **Consent honesty** — `host.commands.launch-target` lets a plugin drive an account into an externally-supplied server; the catalog copy in Task 2 must make that unmistakable on the consent sheet.
- **No new DI** — the existing `IPluginLaunchInvoker` registration in `App.xaml.cs` covers the new methods; the `PluginHostService` ctor is unchanged.
- **Out of scope (later release / plugin plan):** squad-all launch, live per-alt presence ("who's online and where"), the clan-link plugin itself, the 626-hub roster/identity layer.
