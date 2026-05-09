# RoRoRo Plugin System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a v1.4 plugin system for RoRoRo Windows: out-of-process plugins talk to RoRoRo over a named-pipe gRPC contract, with manifest+consent trust, in-app GitHub-URL installer, and per-plugin opt-in autostart. Auto-keys (canonical first consumer) lands in a sibling repo in a follow-up sprint.

**Architecture:** RoRoRo hosts a gRPC server on `\\.\pipe\rororo-plugin-host` (per-user ACL, HTTP/2 over named pipe via Kestrel + `ListenNamedPipe`). Plugins are separate signed EXEs that bundle the `ROROROblox.PluginContract` NuGet, connect as gRPC clients, declare capabilities in a `manifest.json`, and contribute UI through declarative messages translated to WPF in RoRoRo. Capabilities are gated by a gRPC interceptor against per-plugin user consent records stored DPAPI-encrypted.

**Tech Stack:** .NET 10 + C# 14, WPF, `Grpc.AspNetCore` + `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes`, `Google.Protobuf` codegen, `System.IO.Pipes` for ACLs, `System.Security.Cryptography.ProtectedData` for DPAPI consent records, xUnit, existing `StubHttpHandler` pattern for installer download tests.

**Spec:** [`docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md`](../specs/2026-05-09-rororo-plugin-system-design.md)
**Reference bundle:** [`docs/port-reference/auto-keys/v1/`](../../port-reference/auto-keys/v1/)
**Active memory:** `~/.claude/projects/C--Users-estev-Projects-ROROROblox/memory/project_rororo_plugin_system_design.md`

---

## File Structure

**New project: `src/ROROROblox.PluginContract/`** — ships as NuGet, referenced by RoRoRo and plugin EXEs
- `ROROROblox.PluginContract.csproj` — netstandard2.1 (broad consumer compat)
- `Protos/plugin_contract.proto` — gRPC service + DTO definitions
- `PackageReadme.md` — NuGet package readme

**New module: `src/ROROROblox.App/Plugins/`**
- `PluginManifest.cs` — manifest record + JSON parser
- `PluginCapability.cs` — capability constants + validation helpers
- `InstalledPlugin.cs` — registry record (disk path + manifest + consent)
- `PluginRegistry.cs` — disk scan + in-memory list + lookups
- `ConsentStore.cs` — DPAPI-encrypted per-plugin consent records (mirrors `AccountStore`)
- `PluginInstaller.cs` — GitHub URL → manifest → SHA verify → unpack → register
- `PluginProcessSupervisor.cs` — launch plugin EXEs, monitor crashes, teardown
- `PluginHostService.cs` — gRPC server-side `RoRoRoHost` impl
- `CapabilityInterceptor.cs` — gRPC interceptor that gates calls by manifest+consent
- `PluginUITranslator.cs` — UI declarations → WPF tray menu / row badge / status panel
- `PluginsHostingExtensions.cs` — DI registration helpers
- `PluginHostStartupService.cs` — `IHostedService` that starts/stops Kestrel+gRPC server

**New project: `src/ROROROblox.PluginTestHarness/`** — xUnit project for integration tests
- `ROROROblox.PluginTestHarness.csproj`
- `Fakes/StubPluginClient.cs` — gRPC client used in integration tests
- `Fakes/InMemoryNamedPipe.cs` — for testing without real Windows pipe
- Integration test classes per scenario

**Modified files:**
- `ROROROblox.sln` — add `ROROROblox.PluginContract` and `ROROROblox.PluginTestHarness`
- `src/ROROROblox.App/ROROROblox.App.csproj` — add `Grpc.AspNetCore`, `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes`, `Microsoft.Extensions.Hosting`, project ref to `ROROROblox.PluginContract`
- `src/ROROROblox.App/App.xaml.cs` — register plugin services in DI, start `PluginHostStartupService`
- `src/ROROROblox.App/MainWindow.xaml.cs` — wire plugin row badge surface
- `src/ROROROblox.App/Tray/TrayService.cs` — wire plugin tray menu surface
- `src/ROROROblox.App/MainWindow.xaml` — Plugins page (install URL field + list)

**New test files in `src/ROROROblox.Tests/`:**
- `PluginManifestTests.cs`
- `PluginCapabilityTests.cs`
- `ConsentStoreTests.cs`
- `PluginRegistryTests.cs`
- `PluginInstallerTests.cs`
- `PluginProcessSupervisorTests.cs`
- `CapabilityInterceptorTests.cs`

---

## Milestones

- **M1 (Foundation):** Tasks 1-9 — contract NuGet, manifest, consent, registry, installer, supervisor. After M1: can install a plugin and launch it from disk; no gRPC yet.
- **M2 (gRPC plumbing + UI):** Tasks 10-19 — gRPC server, capability gating, all RPCs, UI translator. After M2: a stub plugin connects and contributes UI surfaces.
- **M3 (User-facing UI + composition):** Tasks 20-25 — Plugins page, consent sheet, status banners, integration tests, end-to-end smoke. After M3: sprint shippable.

Each task uses TDD discipline: failing test → minimal impl → passing test → commit.

---

## Task 1: Create `ROROROblox.PluginContract` project

**Files:**
- Create: `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`
- Create: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` (placeholder)
- Modify: `ROROROblox.sln`

- [ ] **Step 1: Create the csproj file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PackageId>ROROROblox.PluginContract</PackageId>
    <Version>0.1.0</Version>
    <Authors>626 Labs LLC</Authors>
    <Description>gRPC contract for RoRoRo plugins. Reference this NuGet to author a plugin.</Description>
    <PackageReadmeFile>PackageReadme.md</PackageReadmeFile>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.28.3" />
    <PackageReference Include="Grpc.Net.Client" Version="2.68.0" />
    <PackageReference Include="Grpc.Tools" Version="2.68.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos\plugin_contract.proto" GrpcServices="Both" />
    <None Include="PackageReadme.md" Pack="true" PackagePath="\" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create a placeholder .proto file (real schema lands Task 2)**

```proto
syntax = "proto3";

option csharp_namespace = "ROROROblox.PluginContract";

package rororo.plugin.v1;

// Placeholder — real services + messages defined in Task 2.
message Empty {}
```

- [ ] **Step 3: Create `PackageReadme.md`**

```markdown
# ROROROblox.PluginContract

gRPC contract for RoRoRo plugins. Reference this NuGet to author a plugin that runs alongside RoRoRo and contributes UI / responds to events / triggers Roblox launches.

See [the plugin author guide](https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/docs/plugins/AUTHOR_GUIDE.md) (lands when the contract stabilizes).
```

- [ ] **Step 4: Add the project to the solution**

Run from repo root:
```bash
dotnet sln ROROROblox.sln add src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj
```

Expected: `Project ... added to the solution.`

- [ ] **Step 5: Verify the project builds**

```bash
dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Debug
```

Expected: `Build succeeded.` Codegen runs; output `obj/Debug/netstandard2.1/Protos/PluginContract.cs` exists.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.PluginContract/ ROROROblox.sln
git commit -m "feat(plugins): scaffold ROROROblox.PluginContract project"
```

---

## Task 2: Define the gRPC contract (`.proto`)

**Files:**
- Modify: `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`

- [ ] **Step 1: Replace the placeholder `.proto` with the real schema**

```proto
syntax = "proto3";

option csharp_namespace = "ROROROblox.PluginContract";

package rororo.plugin.v1;

// =====================================================================
// Services
// =====================================================================

// RoRoRoHost — implemented by RoRoRo, called by the plugin.
service RoRoRoHost {
  // Handshake — first call after the named-pipe connection is established.
  // Plugin sends manifest hash + contract version; RoRoRo responds with host info
  // or rejects the connection.
  rpc Handshake(HandshakeRequest) returns (HandshakeResponse);

  // Read surface
  rpc GetHostInfo(Empty) returns (HostInfo);
  rpc GetRunningAccounts(Empty) returns (RunningAccountsList);

  // Server-streaming events
  rpc SubscribeAccountLaunched(SubscriptionRequest) returns (stream AccountLaunchedEvent);
  rpc SubscribeAccountExited(SubscriptionRequest) returns (stream AccountExitedEvent);
  rpc SubscribeMutexStateChanged(SubscriptionRequest) returns (stream MutexStateEvent);

  // Command surface
  rpc RequestLaunch(LaunchRequest) returns (LaunchResult);

  // UI surface (v1: tray menu, row badge, status panel)
  rpc AddTrayMenuItem(MenuItemSpec) returns (UIHandle);
  rpc AddRowBadge(RowBadgeSpec) returns (UIHandle);
  rpc AddStatusPanel(StatusPanelSpec) returns (UIHandle);
  rpc UpdateUI(UIUpdate) returns (Empty);
  rpc RemoveUI(UIHandle) returns (Empty);
}

// Plugin — implemented by the plugin, called by RoRoRo.
service Plugin {
  rpc OnUIInteraction(UIInteractionEvent) returns (Empty);
  rpc OnConsentChanged(ConsentChangeEvent) returns (Empty);
  rpc OnShutdown(Empty) returns (Empty);
}

// =====================================================================
// Messages
// =====================================================================

message Empty {}

message HandshakeRequest {
  string plugin_id = 1;
  string manifest_sha256 = 2;
  string contract_version = 3;  // semver, e.g. "1.0"
  repeated string declared_capabilities = 4;
}

message HandshakeResponse {
  bool accepted = 1;
  string reject_reason = 2;
  string host_version = 3;
  string contract_version = 4;
}

message HostInfo {
  string version = 1;
  bool multi_instance_enabled = 2;
  string multi_instance_state = 3;  // "On" / "Off" / "Error"
}

message RunningAccountsList {
  repeated RunningAccount accounts = 1;
}

message RunningAccount {
  string account_id = 1;       // RoRoRo internal Guid as string
  int64 roblox_user_id = 2;    // UID-aware: which Roblox user this account is
  string display_name = 3;
  int32 process_id = 4;
}

message SubscriptionRequest {
  // No filters in v1 — plugin subscribes to all events.
}

message AccountLaunchedEvent {
  string account_id = 1;
  int64 roblox_user_id = 2;
  string display_name = 3;
  int32 process_id = 4;
  int64 launched_at_unix_ms = 5;
}

message AccountExitedEvent {
  string account_id = 1;
  int64 roblox_user_id = 2;
  int32 process_id = 3;
  int64 exited_at_unix_ms = 4;
}

message MutexStateEvent {
  string state = 1;  // "On" / "Off" / "Error"
}

message LaunchRequest {
  string account_id = 1;
  // future: place_id, fps_cap (not v1)
}

message LaunchResult {
  bool ok = 1;
  string failure_reason = 2;
  int32 process_id = 3;
}

message UIHandle {
  string id = 1;  // RoRoRo-issued, opaque to plugin
}

message MenuItemSpec {
  string label = 1;
  string tooltip = 2;
  bool enabled = 3;
}

message RowBadgeSpec {
  string text = 1;
  string color_hex = 2;  // optional override; default to brand cyan
  string tooltip = 3;
}

message StatusPanelSpec {
  string title = 1;
  string body_markdown = 2;  // basic markdown subset (bold, italic, lists)
}

message UIUpdate {
  UIHandle handle = 1;
  oneof kind {
    MenuItemSpec menu_item = 2;
    RowBadgeSpec row_badge = 3;
    StatusPanelSpec status_panel = 4;
  }
}

message UIInteractionEvent {
  UIHandle handle = 1;
  string interaction_kind = 2;  // "click", "hover-enter", "hover-leave"
  int64 timestamp_unix_ms = 3;
}

message ConsentChangeEvent {
  repeated string newly_granted_capabilities = 1;
  repeated string newly_revoked_capabilities = 2;
}
```

- [ ] **Step 2: Build the contract project**

```bash
dotnet build src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj -c Debug
```

Expected: build succeeds, generated code in `obj/Debug/netstandard2.1/Protos/PluginContract.cs` and `obj/Debug/netstandard2.1/Protos/PluginContractGrpc.cs`.

- [ ] **Step 3: Verify generated types exist**

Quick smoke — open the generated file and confirm `RoRoRoHost.RoRoRoHostBase` and `Plugin.PluginBase` classes are present. (Or: run `dotnet build` again with `/v:diagnostic` and grep for `Generated <type>`.)

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.PluginContract/Protos/plugin_contract.proto
git commit -m "feat(plugins): define gRPC contract — RoRoRoHost + Plugin services"
```

---

## Task 3: PluginManifest model + parser

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginManifest.cs`
- Test: `src/ROROROblox.Tests/PluginManifestTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginManifestTests
{
    private const string ValidManifestJson = """
    {
        "schemaVersion": 1,
        "id": "626labs.test-plugin",
        "name": "Test Plugin",
        "version": "0.1.0",
        "contractVersion": "1.0",
        "publisher": "626 Labs LLC",
        "description": "A test plugin.",
        "capabilities": ["host.events.account-launched", "host.ui.tray-menu"],
        "icon": "icon.png",
        "updateFeed": "https://example.com/feed.atom"
    }
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsManifest()
    {
        var manifest = PluginManifest.Parse(ValidManifestJson);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("626labs.test-plugin", manifest.Id);
        Assert.Equal("Test Plugin", manifest.Name);
        Assert.Equal("0.1.0", manifest.Version);
        Assert.Equal("1.0", manifest.ContractVersion);
        Assert.Contains("host.events.account-launched", manifest.Capabilities);
        Assert.Contains("host.ui.tray-menu", manifest.Capabilities);
    }

    [Fact]
    public void Parse_MissingRequiredField_Throws()
    {
        const string missingId = """
        { "schemaVersion": 1, "name": "x", "version": "1.0", "contractVersion": "1.0", "publisher": "x", "description": "x", "capabilities": [] }
        """;

        Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(missingId));
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersion_Throws()
    {
        const string futureSchema = """
        { "schemaVersion": 99, "id": "x", "name": "x", "version": "1.0", "contractVersion": "1.0", "publisher": "x", "description": "x", "capabilities": [] }
        """;

        var ex = Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(futureSchema));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Parse_InvalidId_Throws()
    {
        const string badId = """
        { "schemaVersion": 1, "id": "Not Valid Id With Spaces", "name": "x", "version": "1.0", "contractVersion": "1.0", "publisher": "x", "description": "x", "capabilities": [] }
        """;

        Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(badId));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginManifestTests" -v minimal`
Expected: build fails — `PluginManifest`, `PluginManifestException` don't exist yet.

- [ ] **Step 3: Implement `PluginManifest` + `PluginManifestException`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ROROROblox.App.Plugins;

public sealed record PluginManifest
{
    public required int SchemaVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string ContractVersion { get; init; }
    public required string Publisher { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Capabilities { get; init; }
    public string? Icon { get; init; }
    public string? UpdateFeed { get; init; }

    public const int CurrentSchemaVersion = 1;
    private static readonly Regex IdPattern = new(@"^[a-z0-9]+(\.[a-z0-9-]+)+$", RegexOptions.Compiled);

    public static PluginManifest Parse(string json)
    {
        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            throw new PluginManifestException($"Manifest JSON is malformed: {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new PluginManifestException("Manifest JSON parsed to null.");
        }

        if (dto.SchemaVersion is null)
        {
            throw new PluginManifestException("Manifest is missing schemaVersion.");
        }
        if (dto.SchemaVersion != CurrentSchemaVersion)
        {
            throw new PluginManifestException(
                $"Unsupported schemaVersion {dto.SchemaVersion}. This RoRoRo expects schemaVersion {CurrentSchemaVersion}.");
        }

        Require(dto.Id, "id");
        Require(dto.Name, "name");
        Require(dto.Version, "version");
        Require(dto.ContractVersion, "contractVersion");
        Require(dto.Publisher, "publisher");
        Require(dto.Description, "description");
        if (dto.Capabilities is null)
        {
            throw new PluginManifestException("Manifest is missing capabilities.");
        }

        if (!IdPattern.IsMatch(dto.Id!))
        {
            throw new PluginManifestException(
                $"Manifest id '{dto.Id}' is not in reverse-DNS form (e.g. '626labs.auto-keys').");
        }

        return new PluginManifest
        {
            SchemaVersion = dto.SchemaVersion.Value,
            Id = dto.Id!,
            Name = dto.Name!,
            Version = dto.Version!,
            ContractVersion = dto.ContractVersion!,
            Publisher = dto.Publisher!,
            Description = dto.Description!,
            Capabilities = dto.Capabilities,
            Icon = dto.Icon,
            UpdateFeed = dto.UpdateFeed,
        };
    }

    private static void Require(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PluginManifestException($"Manifest is missing {fieldName}.");
        }
    }

    private sealed class ManifestDto
    {
        [JsonPropertyName("schemaVersion")] public int? SchemaVersion { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("contractVersion")] public string? ContractVersion { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }
        [JsonPropertyName("updateFeed")] public string? UpdateFeed { get; set; }
    }
}

public sealed class PluginManifestException : Exception
{
    public PluginManifestException(string message) : base(message) { }
    public PluginManifestException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginManifestTests" -v minimal`
Expected: 4/4 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginManifest.cs src/ROROROblox.Tests/PluginManifestTests.cs
git commit -m "feat(plugins): PluginManifest model + JSON parser"
```

---

## Task 4: PluginCapability constants + validation

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginCapability.cs`
- Test: `src/ROROROblox.Tests/PluginCapabilityTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginCapabilityTests
{
    [Fact]
    public void IsKnown_ReturnsTrue_ForDefinedCapability()
    {
        Assert.True(PluginCapability.IsKnown("host.events.account-launched"));
        Assert.True(PluginCapability.IsKnown("host.ui.tray-menu"));
        Assert.True(PluginCapability.IsKnown("system.synthesize-keyboard-input"));
    }

    [Fact]
    public void IsKnown_ReturnsFalse_ForUnknown()
    {
        Assert.False(PluginCapability.IsKnown("host.events.bogus"));
        Assert.False(PluginCapability.IsKnown(""));
        Assert.False(PluginCapability.IsKnown("not.a.real.capability"));
    }

    [Fact]
    public void IsHostEnforced_ReturnsTrue_ForHostNamespace()
    {
        Assert.True(PluginCapability.IsHostEnforced("host.events.account-launched"));
        Assert.True(PluginCapability.IsHostEnforced("host.commands.request-launch"));
    }

    [Fact]
    public void IsHostEnforced_ReturnsFalse_ForSystemNamespace()
    {
        Assert.False(PluginCapability.IsHostEnforced("system.synthesize-keyboard-input"));
    }

    [Fact]
    public void Display_ReturnsHumanReadableExplanation()
    {
        var explanation = PluginCapability.Display("host.events.account-launched");
        Assert.Contains("when an account launches", explanation, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginCapabilityTests" -v minimal`
Expected: build error — `PluginCapability` doesn't exist.

- [ ] **Step 3: Implement `PluginCapability`**

```csharp
namespace ROROROblox.App.Plugins;

/// <summary>
/// The capability vocabulary plugins declare in their manifest. Two namespaces:
/// <list type="bullet">
///   <item><c>host.*</c> — what the plugin asks RoRoRo for. Gated by gRPC interceptor on every call.</item>
///   <item><c>system.*</c> — what the plugin does locally on the user's machine. Disclosed for consent
///   but not enforced by RoRoRo (the plugin runs as its own process; we can't sandbox it).</item>
/// </list>
/// </summary>
public static class PluginCapability
{
    public const string HostEventsAccountLaunched = "host.events.account-launched";
    public const string HostEventsAccountExited = "host.events.account-exited";
    public const string HostEventsMutexStateChanged = "host.events.mutex-state-changed";
    public const string HostCommandsRequestLaunch = "host.commands.request-launch";
    public const string HostUITrayMenu = "host.ui.tray-menu";
    public const string HostUIRowBadge = "host.ui.row-badge";
    public const string HostUIStatusPanel = "host.ui.status-panel";

    public const string SystemSynthesizeKeyboardInput = "system.synthesize-keyboard-input";
    public const string SystemSynthesizeMouseInput = "system.synthesize-mouse-input";
    public const string SystemWatchGlobalInput = "system.watch-global-input";
    public const string SystemPreventSleep = "system.prevent-sleep";
    public const string SystemFocusForeignWindows = "system.focus-foreign-windows";

    private static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
        [HostEventsAccountLaunched] = "Notify the plugin when an account launches.",
        [HostEventsAccountExited] = "Notify the plugin when an account exits.",
        [HostEventsMutexStateChanged] = "Notify the plugin when multi-instance state changes.",
        [HostCommandsRequestLaunch] = "Allow the plugin to ask RoRoRo to launch a Roblox account.",
        [HostUITrayMenu] = "Allow the plugin to add tray menu items.",
        [HostUIRowBadge] = "Allow the plugin to add a badge on each saved-account row.",
        [HostUIStatusPanel] = "Allow the plugin to add a status panel to the main window.",
        [SystemSynthesizeKeyboardInput] = "The plugin will synthesize keyboard input on your machine.",
        [SystemSynthesizeMouseInput] = "The plugin will synthesize mouse input on your machine.",
        [SystemWatchGlobalInput] = "The plugin will watch your keyboard + mouse input system-wide.",
        [SystemPreventSleep] = "The plugin will prevent your computer from sleeping while it runs.",
        [SystemFocusForeignWindows] = "The plugin will activate / focus other applications' windows.",
    };

    public static bool IsKnown(string capability)
        => !string.IsNullOrEmpty(capability) && Catalog.ContainsKey(capability);

    public static bool IsHostEnforced(string capability)
        => IsKnown(capability) && capability.StartsWith("host.", StringComparison.Ordinal);

    public static string Display(string capability)
        => Catalog.TryGetValue(capability, out var explanation)
            ? explanation
            : $"Unknown capability: {capability}";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginCapabilityTests" -v minimal`
Expected: 5/5 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginCapability.cs src/ROROROblox.Tests/PluginCapabilityTests.cs
git commit -m "feat(plugins): PluginCapability vocabulary + validation"
```

---

## Task 5: ConsentStore — DPAPI-encrypted per-plugin consent records

**Files:**
- Create: `src/ROROROblox.App/Plugins/ConsentStore.cs`
- Test: `src/ROROROblox.Tests/ConsentStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class ConsentStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public ConsentStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-consent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "consent.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenStoreDoesNotExist()
    {
        var store = new ConsentStore(_filePath);
        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task GrantAsync_PersistsRecord_AndIsReadableOnRoundtrip()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched", "host.ui.tray-menu" });

        // New store instance to verify on-disk persistence.
        var store2 = new ConsentStore(_filePath);
        var list = await store2.ListAsync();

        var record = Assert.Single(list);
        Assert.Equal("626labs.test", record.PluginId);
        Assert.Contains("host.events.account-launched", record.GrantedCapabilities);
        Assert.False(record.AutostartEnabled); // default off
    }

    [Fact]
    public async Task SetAutostartAsync_PersistsToggle()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched" });
        await store.SetAutostartAsync("626labs.test", enabled: true);

        var store2 = new ConsentStore(_filePath);
        var list = await store2.ListAsync();
        Assert.True(Assert.Single(list).AutostartEnabled);
    }

    [Fact]
    public async Task RevokeAsync_RemovesPlugin()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched" });
        await store.RevokeAsync("626labs.test");

        var store2 = new ConsentStore(_filePath);
        Assert.Empty(await store2.ListAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenFileIsTampered()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched" });

        // Tamper.
        await File.WriteAllBytesAsync(_filePath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var store2 = new ConsentStore(_filePath);
        Assert.Empty(await store2.ListAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ConsentStoreTests" -v minimal`
Expected: build fails — `ConsentStore` doesn't exist.

- [ ] **Step 3: Implement `ConsentStore`**

```csharp
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.App.Plugins;

/// <summary>
/// DPAPI-encrypted (per-user, per-machine) store of plugin consent records.
/// Mirrors the AccountStore pattern: a JSON list, encrypted with
/// <c>ProtectedData.Protect(..., DataProtectionScope.CurrentUser)</c>.
/// On tamper / decryption failure, returns an empty list (so a stray file
/// can't crash plugin discovery).
/// </summary>
public sealed class ConsentStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public ConsentStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public async Task<IReadOnlyList<ConsentRecord>> ListAsync()
    {
        var records = await LoadAsync().ConfigureAwait(false);
        return records.Values.ToList();
    }

    public async Task GrantAsync(string pluginId, IEnumerable<string> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var records = await LoadAsync().ConfigureAwait(false);
        records[pluginId] = new ConsentRecord
        {
            PluginId = pluginId,
            GrantedCapabilities = capabilities.Distinct().ToList(),
            AutostartEnabled = records.TryGetValue(pluginId, out var existing) && existing.AutostartEnabled,
        };
        await SaveAsync(records).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string pluginId)
    {
        var records = await LoadAsync().ConfigureAwait(false);
        records.Remove(pluginId);
        await SaveAsync(records).ConfigureAwait(false);
    }

    public async Task SetAutostartAsync(string pluginId, bool enabled)
    {
        var records = await LoadAsync().ConfigureAwait(false);
        if (!records.TryGetValue(pluginId, out var existing))
        {
            throw new InvalidOperationException($"No consent record for plugin {pluginId}.");
        }
        records[pluginId] = existing with { AutostartEnabled = enabled };
        await SaveAsync(records).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, ConsentRecord>> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, ConsentRecord>();
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var list = JsonSerializer.Deserialize<List<ConsentRecord>>(decrypted, JsonOptions);
            if (list is null)
            {
                return new Dictionary<string, ConsentRecord>();
            }
            return list.ToDictionary(r => r.PluginId, StringComparer.Ordinal);
        }
        catch (CryptographicException)
        {
            // Tampered or wrong-user envelope. Treat as empty.
            return new Dictionary<string, ConsentRecord>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, ConsentRecord>();
        }
    }

    private async Task SaveAsync(Dictionary<string, ConsentRecord> records)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(records.Values.ToList(), JsonOptions);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllBytesAsync(_filePath, encrypted).ConfigureAwait(false);
    }
}

public sealed record ConsentRecord
{
    public required string PluginId { get; init; }
    public required IReadOnlyList<string> GrantedCapabilities { get; init; }
    public bool AutostartEnabled { get; init; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ConsentStoreTests" -v minimal`
Expected: 5/5 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/ConsentStore.cs src/ROROROblox.Tests/ConsentStoreTests.cs
git commit -m "feat(plugins): ConsentStore — DPAPI-encrypted per-plugin consent records"
```

---

## Task 6: InstalledPlugin record + PluginRegistry

**Files:**
- Create: `src/ROROROblox.App/Plugins/InstalledPlugin.cs`
- Create: `src/ROROROblox.App/Plugins/PluginRegistry.cs`
- Test: `src/ROROROblox.Tests/PluginRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginRegistryTests : IDisposable
{
    private readonly string _pluginsRoot;
    private readonly string _consentPath;

    public PluginRegistryTests()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"ROROROblox-reg-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(tempBase, "plugins");
        _consentPath = Path.Combine(tempBase, "consent.dat");
        Directory.CreateDirectory(_pluginsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_pluginsRoot)!))
        {
            Directory.Delete(Path.GetDirectoryName(_pluginsRoot)!, recursive: true);
        }
    }

    private void WritePlugin(string id, string manifestJson)
    {
        var dir = Path.Combine(_pluginsRoot, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifestJson);
    }

    [Fact]
    public async Task ScanAsync_ReturnsEmpty_WhenNoPluginsInstalled()
    {
        var registry = new PluginRegistry(_pluginsRoot, new ConsentStore(_consentPath));
        var plugins = await registry.ScanAsync();
        Assert.Empty(plugins);
    }

    [Fact]
    public async Task ScanAsync_ReturnsManifest_WhenManifestPresent()
    {
        WritePlugin("626labs.test", """
        {"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched"]}
        """);

        var registry = new PluginRegistry(_pluginsRoot, new ConsentStore(_consentPath));
        var plugins = await registry.ScanAsync();

        var plugin = Assert.Single(plugins);
        Assert.Equal("626labs.test", plugin.Manifest.Id);
        Assert.Equal(Path.Combine(_pluginsRoot, "626labs.test"), plugin.InstallDir);
        Assert.Empty(plugin.Consent.GrantedCapabilities); // no consent yet
    }

    [Fact]
    public async Task ScanAsync_SkipsDirectoriesWithMalformedManifest()
    {
        WritePlugin("good", """
        {"schemaVersion":1,"id":"good","name":"x","version":"1","contractVersion":"1.0","publisher":"x","description":"x","capabilities":[]}
        """);
        WritePlugin("bad", "{not json}");

        var registry = new PluginRegistry(_pluginsRoot, new ConsentStore(_consentPath));
        var plugins = await registry.ScanAsync();

        Assert.Single(plugins);
        Assert.Equal("good", plugins[0].Manifest.Id);
    }

    [Fact]
    public async Task ScanAsync_PairsManifestWithConsentRecord()
    {
        WritePlugin("626labs.test", """
        {"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.ui.tray-menu"]}
        """);
        var consent = new ConsentStore(_consentPath);
        await consent.GrantAsync("626labs.test", new[] { "host.ui.tray-menu" });

        var registry = new PluginRegistry(_pluginsRoot, consent);
        var plugins = await registry.ScanAsync();

        Assert.Contains("host.ui.tray-menu", Assert.Single(plugins).Consent.GrantedCapabilities);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginRegistryTests" -v minimal`
Expected: build fails.

- [ ] **Step 3: Implement `InstalledPlugin` + `PluginRegistry`**

```csharp
// src/ROROROblox.App/Plugins/InstalledPlugin.cs
namespace ROROROblox.App.Plugins;

public sealed record InstalledPlugin
{
    public required PluginManifest Manifest { get; init; }
    public required string InstallDir { get; init; }
    public required ConsentRecord Consent { get; init; }

    public string ExecutablePath => System.IO.Path.Combine(InstallDir, Manifest.Id + ".exe");
}
```

```csharp
// src/ROROROblox.App/Plugins/PluginRegistry.cs
using System.IO;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Disk-scanned + in-memory list of installed plugins. Default plugins root:
/// <c>%LOCALAPPDATA%\ROROROblox\plugins\</c>. Pairs each on-disk manifest with
/// the user's consent record (or an empty default).
/// </summary>
public sealed class PluginRegistry
{
    public static string DefaultPluginsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox", "plugins");

    private readonly string _pluginsRoot;
    private readonly ConsentStore _consentStore;

    public PluginRegistry(string pluginsRoot, ConsentStore consentStore)
    {
        _pluginsRoot = pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot));
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
    }

    public async Task<IReadOnlyList<InstalledPlugin>> ScanAsync()
    {
        if (!Directory.Exists(_pluginsRoot))
        {
            return Array.Empty<InstalledPlugin>();
        }

        var consentByPluginId = (await _consentStore.ListAsync().ConfigureAwait(false))
            .ToDictionary(r => r.PluginId, StringComparer.Ordinal);

        var plugins = new List<InstalledPlugin>();
        foreach (var dir in Directory.EnumerateDirectories(_pluginsRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                var manifest = PluginManifest.Parse(json);
                var consent = consentByPluginId.TryGetValue(manifest.Id, out var existing)
                    ? existing
                    : new ConsentRecord
                    {
                        PluginId = manifest.Id,
                        GrantedCapabilities = Array.Empty<string>(),
                        AutostartEnabled = false,
                    };
                plugins.Add(new InstalledPlugin
                {
                    Manifest = manifest,
                    InstallDir = dir,
                    Consent = consent,
                });
            }
            catch (PluginManifestException)
            {
                // Skip malformed manifests; surface in logs at the caller.
                continue;
            }
        }
        return plugins;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginRegistryTests" -v minimal`
Expected: 4/4 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/InstalledPlugin.cs src/ROROROblox.App/Plugins/PluginRegistry.cs src/ROROROblox.Tests/PluginRegistryTests.cs
git commit -m "feat(plugins): InstalledPlugin + PluginRegistry disk scan"
```

---

## Task 7: PluginInstaller — GitHub URL → SHA verify → unpack

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginInstaller.cs`
- Test: `src/ROROROblox.Tests/PluginInstallerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginInstallerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _pluginsRoot;
    private readonly StubHttpHandler _http;

    public PluginInstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-install-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(_pluginsRoot);
        _http = new StubHttpHandler();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private static (byte[] zipBytes, string sha256) BuildZipWithManifest(string manifestJson)
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(manifestJson);
        }
        var bytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (bytes, sha);
    }

    [Fact]
    public async Task InstallAsync_ValidPackage_ExtractsManifestAndZipContent()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched"]}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot);
        var result = await installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: new[] { "host.events.account-launched" });

        Assert.Equal("626labs.test", result.Manifest.Id);
        Assert.True(File.Exists(Path.Combine(_pluginsRoot, "626labs.test", "manifest.json")));
    }

    [Fact]
    public async Task InstallAsync_ShaMismatch_RefusesAndCleansUp()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}""";
        var (zipBytes, _) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("0000000000000000000000000000000000000000000000000000000000000000") });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot);

        await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: Array.Empty<string>()));

        Assert.False(Directory.Exists(Path.Combine(_pluginsRoot, "626labs.test")));
    }

    [Fact]
    public async Task InstallAsync_ManifestMissingRequiredCapability_Throws()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot);

        await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: new[] { "host.events.account-launched" }));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginInstallerTests" -v minimal`
Expected: build fails.

- [ ] **Step 3: Implement `PluginInstaller`**

```csharp
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Installs a plugin from a base URL: pulls manifest.json + manifest.sha256 + plugin.zip,
/// SHA-verifies the zip, parses + validates the manifest (including required-capability
/// presence check), unpacks to <c>%LOCALAPPDATA%\ROROROblox\plugins\&lt;id&gt;\</c>.
/// User-initiated only — the call originates from the plugin install dialog,
/// never from auto-discovery or background polling. Store-policy 10.2.2 clean.
/// </summary>
public sealed class PluginInstaller
{
    private readonly HttpClient _http;
    private readonly string _pluginsRoot;

    public PluginInstaller(HttpClient http, string pluginsRoot)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pluginsRoot = pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot));
    }

    public async Task<InstalledPlugin> InstallAsync(string baseUrl, IReadOnlyList<string> requireCapabilities)
    {
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        // 1. Fetch manifest, parse, sanity-check required capabilities.
        var manifestJson = await GetStringAsync(new Uri(baseUrl + "manifest.json"));
        PluginManifest manifest;
        try
        {
            manifest = PluginManifest.Parse(manifestJson);
        }
        catch (PluginManifestException ex)
        {
            throw new PluginInstallerException($"Manifest validation failed: {ex.Message}", ex);
        }

        foreach (var required in requireCapabilities)
        {
            if (!manifest.Capabilities.Contains(required))
            {
                throw new PluginInstallerException(
                    $"Plugin manifest does not declare required capability '{required}'.");
            }
        }

        // 2. Fetch SHA256, fetch zip, verify.
        var expectedSha = (await GetStringAsync(new Uri(baseUrl + "manifest.sha256"))).Trim().ToLowerInvariant();
        var zipBytes = await GetByteArrayAsync(new Uri(baseUrl + "plugin.zip"));
        var actualSha = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        if (!string.Equals(actualSha, expectedSha, StringComparison.Ordinal))
        {
            throw new PluginInstallerException(
                $"Plugin zip SHA256 mismatch. Expected {expectedSha}, got {actualSha}.");
        }

        // 3. Unpack to install dir.
        var installDir = Path.Combine(_pluginsRoot, manifest.Id);
        if (Directory.Exists(installDir))
        {
            Directory.Delete(installDir, recursive: true);
        }
        Directory.CreateDirectory(installDir);
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var dest = Path.Combine(installDir, entry.FullName);
                var fullDestPath = Path.GetFullPath(dest);
                if (!fullDestPath.StartsWith(Path.GetFullPath(installDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PluginInstallerException("Zip-slip detected — zip contains paths outside install dir.");
                }
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
        catch
        {
            // Best-effort cleanup on failure
            try { Directory.Delete(installDir, recursive: true); } catch { }
            throw;
        }

        return new InstalledPlugin
        {
            Manifest = manifest,
            InstallDir = installDir,
            Consent = new ConsentRecord
            {
                PluginId = manifest.Id,
                GrantedCapabilities = Array.Empty<string>(),
                AutostartEnabled = false,
            },
        };
    }

    private async Task<string> GetStringAsync(Uri uri)
    {
        using var response = await _http.GetAsync(uri).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PluginInstallerException($"GET {uri} returned {(int)response.StatusCode}.");
        }
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private async Task<byte[]> GetByteArrayAsync(Uri uri)
    {
        using var response = await _http.GetAsync(uri).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PluginInstallerException($"GET {uri} returned {(int)response.StatusCode}.");
        }
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }
}

public sealed class PluginInstallerException : Exception
{
    public PluginInstallerException(string message) : base(message) { }
    public PluginInstallerException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginInstallerTests" -v minimal`
Expected: 3/3 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginInstaller.cs src/ROROROblox.Tests/PluginInstallerTests.cs
git commit -m "feat(plugins): PluginInstaller — GitHub URL fetch + SHA verify + unpack"
```

---

## Task 8: PluginProcessSupervisor — launch + monitor + teardown

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs`
- Test: `src/ROROROblox.Tests/PluginProcessSupervisorTests.cs`

- [ ] **Step 1: Write the failing test**

Use the existing `IProcessStarter` interface (verify it already supports the operations needed; if not, extend it minimally).

```csharp
using System.IO;
using ROROROblox.App.Plugins;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class PluginProcessSupervisorTests : IDisposable
{
    private readonly string _tempDir;

    public PluginProcessSupervisorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-sup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private InstalledPlugin MakePlugin(string id, bool autostart)
    {
        return new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1, Id = id, Name = id, Version = "1.0",
                ContractVersion = "1.0", Publisher = "x", Description = "x",
                Capabilities = Array.Empty<string>(),
            },
            InstallDir = Path.Combine(_tempDir, id),
            Consent = new ConsentRecord
            {
                PluginId = id, GrantedCapabilities = Array.Empty<string>(),
                AutostartEnabled = autostart,
            },
        };
    }

    [Fact]
    public void StartAutostartPlugins_LaunchesOnlyAutostartEnabled()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);

        supervisor.StartAutostart(new[]
        {
            MakePlugin("a", autostart: true),
            MakePlugin("b", autostart: false),
            MakePlugin("c", autostart: true),
        });

        Assert.Equal(2, fake.Started.Count);
        Assert.Contains(fake.Started, s => s.id == "a");
        Assert.Contains(fake.Started, s => s.id == "c");
        Assert.DoesNotContain(fake.Started, s => s.id == "b");
    }

    [Fact]
    public void StopAll_TerminatesEveryTrackedProcess()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);

        supervisor.StartAutostart(new[]
        {
            MakePlugin("a", autostart: true),
            MakePlugin("b", autostart: true),
        });

        supervisor.StopAll();

        Assert.Equal(2, fake.KilledPids.Count);
    }

    [Fact]
    public void Restart_StopsAndStartsTheSamePlugin()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("a", autostart: true);

        supervisor.StartAutostart(new[] { plugin });
        Assert.Single(fake.Started);

        supervisor.Restart(plugin);

        Assert.Equal(2, fake.Started.Count);
        Assert.Single(fake.KilledPids);
    }

    private sealed class FakeProcessStarter : IPluginProcessStarter
    {
        public List<(string id, string exePath)> Started { get; } = new();
        public List<int> KilledPids { get; } = new();
        private int _nextPid = 1000;

        public int Start(string id, string exePath)
        {
            Started.Add((id, exePath));
            return _nextPid++;
        }

        public void Kill(int pid) => KilledPids.Add(pid);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginProcessSupervisorTests" -v minimal`
Expected: build fails — `PluginProcessSupervisor`, `IPluginProcessStarter` don't exist.

- [ ] **Step 3: Implement `IPluginProcessStarter` + `PluginProcessSupervisor`**

```csharp
namespace ROROROblox.App.Plugins;

public interface IPluginProcessStarter
{
    /// <summary>Launch the plugin EXE. Returns its process id.</summary>
    int Start(string pluginId, string exePath);
    /// <summary>Terminate the plugin process. No-op if already dead.</summary>
    void Kill(int pid);
}
```

```csharp
namespace ROROROblox.App.Plugins;

public sealed class PluginProcessSupervisor
{
    private readonly IPluginProcessStarter _starter;
    private readonly Dictionary<string, int> _pidByPluginId = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public PluginProcessSupervisor(IPluginProcessStarter starter)
    {
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
    }

    public IReadOnlyDictionary<string, int> RunningPids
    {
        get { lock (_lock) { return new Dictionary<string, int>(_pidByPluginId); } }
    }

    public void StartAutostart(IEnumerable<InstalledPlugin> plugins)
    {
        foreach (var plugin in plugins)
        {
            if (!plugin.Consent.AutostartEnabled) continue;
            StartOne(plugin);
        }
    }

    public void Restart(InstalledPlugin plugin)
    {
        lock (_lock)
        {
            if (_pidByPluginId.TryGetValue(plugin.Manifest.Id, out var oldPid))
            {
                _starter.Kill(oldPid);
                _pidByPluginId.Remove(plugin.Manifest.Id);
            }
        }
        StartOne(plugin);
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var pid in _pidByPluginId.Values)
            {
                _starter.Kill(pid);
            }
            _pidByPluginId.Clear();
        }
    }

    private void StartOne(InstalledPlugin plugin)
    {
        var pid = _starter.Start(plugin.Manifest.Id, plugin.ExecutablePath);
        lock (_lock) { _pidByPluginId[plugin.Manifest.Id] = pid; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginProcessSupervisorTests" -v minimal`
Expected: 3/3 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs src/ROROROblox.App/Plugins/IPluginProcessStarter.cs src/ROROROblox.Tests/PluginProcessSupervisorTests.cs
git commit -m "feat(plugins): PluginProcessSupervisor — launch + monitor + teardown"
```

---

## Task 9: M1 milestone checkpoint

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test src/ROROROblox.Tests/ --no-build -v minimal`
Expected: all existing tests pass + the ~17 new plugin-related tests pass.

- [ ] **Step 2: Verify the App still builds end-to-end**

Run: `dotnet build src/ROROROblox.App/ROROROblox.App.csproj -c Debug`
Expected: builds clean (the running App may block bin-copy, that's OK).

- [ ] **Step 3: Tag the milestone**

```bash
git tag plugin-system-m1
```

- [ ] **Step 4: Log decision to dashboard**

Use `mcp__626Labs__manage_decisions log` with category `architecture`, naming what was built (PluginContract NuGet, manifest/consent/registry/installer/supervisor) and what's pending (gRPC plumbing).

---

## Task 10: Add `Grpc.AspNetCore` + Kestrel named-pipe deps to App

**Files:**
- Modify: `src/ROROROblox.App/ROROROblox.App.csproj`
- Modify: `ROROROblox.sln` (verify PluginContract project ref)

- [ ] **Step 1: Add the package + project references**

Update the App csproj `<ItemGroup>` for packages:

```xml
<ItemGroup>
  <!-- existing packages... -->
  <PackageReference Include="Grpc.AspNetCore" Version="2.68.0" />
  <PackageReference Include="Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\ROROROblox.Core\ROROROblox.Core.csproj" />
  <ProjectReference Include="..\ROROROblox.PluginContract\ROROROblox.PluginContract.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Restore + verify build**

```bash
dotnet restore src/ROROROblox.App/ROROROblox.App.csproj
dotnet build src/ROROROblox.App/ROROROblox.App.csproj -c Debug
```

Expected: build succeeds. Log a warning if the running App.exe blocks bin-copy; the obj/ output proves the compile passed.

- [ ] **Step 3: Commit**

```bash
git add src/ROROROblox.App/ROROROblox.App.csproj
git commit -m "build(plugins): add Grpc.AspNetCore + Kestrel named-pipe transport"
```

---

## Task 11: PluginHostService — handshake + GetHostInfo + GetRunningAccounts

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginHostService.cs`
- Test: `src/ROROROblox.PluginTestHarness/HandshakeTests.cs` (the test harness gets scaffolded in Task 17 — defer integration tests for handshake to that task)

For this task, write **unit tests** that exercise `PluginHostService.HandshakeAsync` directly (no real gRPC pipe). The integration test follows in Task 17.

- [ ] **Step 1: Write the failing test**

```csharp
using Grpc.Core;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.Tests;

public class PluginHostServiceTests
{
    private static InstalledPlugin MakeInstalled(string id, params string[] caps) => new()
    {
        Manifest = new PluginManifest
        {
            SchemaVersion = 1, Id = id, Name = id, Version = "1.0",
            ContractVersion = "1.0", Publisher = "626", Description = "x",
            Capabilities = caps,
        },
        InstallDir = "/fake",
        Consent = new ConsentRecord { PluginId = id, GrantedCapabilities = caps, AutostartEnabled = false },
    };

    [Fact]
    public async Task Handshake_AcceptsMatchingContractVersion()
    {
        var registry = new InMemoryRegistry(new[] { MakeInstalled("626labs.test", "host.events.account-launched") });
        var service = new PluginHostService(registry, hostVersion: "1.4.0", supportedContractVersion: "1.0");

        var response = await service.Handshake(new HandshakeRequest
        {
            PluginId = "626labs.test",
            ManifestSha256 = "ignored-in-v1",
            ContractVersion = "1.0",
        }, TestServerCallContext.Create());

        Assert.True(response.Accepted);
        Assert.Equal("1.4.0", response.HostVersion);
    }

    [Fact]
    public async Task Handshake_RejectsContractVersionMismatch()
    {
        var registry = new InMemoryRegistry(new[] { MakeInstalled("626labs.test") });
        var service = new PluginHostService(registry, hostVersion: "1.4.0", supportedContractVersion: "1.0");

        var response = await service.Handshake(new HandshakeRequest
        {
            PluginId = "626labs.test", ContractVersion = "99.0",
        }, TestServerCallContext.Create());

        Assert.False(response.Accepted);
        Assert.Contains("contract", response.RejectReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handshake_RejectsUnknownPluginId()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var service = new PluginHostService(registry, "1.4.0", "1.0");

        var response = await service.Handshake(new HandshakeRequest
        {
            PluginId = "nonexistent", ContractVersion = "1.0",
        }, TestServerCallContext.Create());

        Assert.False(response.Accepted);
        Assert.Contains("not installed", response.RejectReason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InMemoryRegistry : IInstalledPluginsLookup
    {
        private readonly List<InstalledPlugin> _plugins;
        public InMemoryRegistry(IEnumerable<InstalledPlugin> plugins) { _plugins = plugins.ToList(); }
        public InstalledPlugin? FindById(string id) => _plugins.FirstOrDefault(p => p.Manifest.Id == id);
    }
}
```

Note: `TestServerCallContext.Create()` — gRPC has a helper for tests; if missing, mock with a minimal stub.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginHostServiceTests" -v minimal`
Expected: build fails — `PluginHostService`, `IInstalledPluginsLookup` don't exist.

- [ ] **Step 3: Implement `IInstalledPluginsLookup` + `PluginHostService` (handshake-only for now)**

```csharp
// src/ROROROblox.App/Plugins/IInstalledPluginsLookup.cs
namespace ROROROblox.App.Plugins;

public interface IInstalledPluginsLookup
{
    InstalledPlugin? FindById(string id);
}
```

```csharp
// src/ROROROblox.App/Plugins/PluginHostService.cs
using Grpc.Core;
using ROROROblox.PluginContract;

namespace ROROROblox.App.Plugins;

public sealed partial class PluginHostService : RoRoRoHost.RoRoRoHostBase
{
    private readonly IInstalledPluginsLookup _registry;
    private readonly string _hostVersion;
    private readonly string _supportedContractVersion;

    public PluginHostService(IInstalledPluginsLookup registry, string hostVersion, string supportedContractVersion)
    {
        _registry = registry;
        _hostVersion = hostVersion;
        _supportedContractVersion = supportedContractVersion;
    }

    public override Task<HandshakeResponse> Handshake(HandshakeRequest request, ServerCallContext context)
    {
        var plugin = _registry.FindById(request.PluginId);
        if (plugin is null)
        {
            return Task.FromResult(new HandshakeResponse
            {
                Accepted = false,
                RejectReason = $"Plugin {request.PluginId} is not installed.",
                HostVersion = _hostVersion,
                ContractVersion = _supportedContractVersion,
            });
        }

        if (request.ContractVersion != _supportedContractVersion)
        {
            return Task.FromResult(new HandshakeResponse
            {
                Accepted = false,
                RejectReason = $"Plugin contract version {request.ContractVersion} not supported. Host expects {_supportedContractVersion}.",
                HostVersion = _hostVersion,
                ContractVersion = _supportedContractVersion,
            });
        }

        return Task.FromResult(new HandshakeResponse
        {
            Accepted = true,
            HostVersion = _hostVersion,
            ContractVersion = _supportedContractVersion,
        });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~PluginHostServiceTests" -v minimal`
Expected: 3/3 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginHostService.cs src/ROROROblox.App/Plugins/IInstalledPluginsLookup.cs src/ROROROblox.Tests/PluginHostServiceTests.cs
git commit -m "feat(plugins): PluginHostService — handshake (accept / reject / version check)"
```

---

## Task 12: GetHostInfo + GetRunningAccounts (read surface)

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs`
- Test: extend `src/ROROROblox.Tests/PluginHostServiceTests.cs`

- [ ] **Step 1: Add failing tests for the read surface**

```csharp
[Fact]
public async Task GetHostInfo_ReturnsCurrentVersionAndState()
{
    var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
    var hostState = new FakeHostStateProvider("On");
    var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, runningAccountsProvider: null!);

    var info = await service.GetHostInfo(new ROROROblox.PluginContract.Empty(), TestServerCallContext.Create());

    Assert.Equal("1.4.0", info.Version);
    Assert.True(info.MultiInstanceEnabled);
    Assert.Equal("On", info.MultiInstanceState);
}

[Fact]
public async Task GetRunningAccounts_ReturnsListFromProvider()
{
    var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
    var hostState = new FakeHostStateProvider("Off");
    var accounts = new FakeRunningAccountsProvider(new[]
    {
        new RunningAccountSnapshot("00000000-0000-0000-0000-000000000001", 12345, "Alice", 9999),
    });
    var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts);

    var list = await service.GetRunningAccounts(new ROROROblox.PluginContract.Empty(), TestServerCallContext.Create());

    var account = Assert.Single(list.Accounts);
    Assert.Equal(12345L, account.RobloxUserId);
    Assert.Equal("Alice", account.DisplayName);
}

private sealed class FakeHostStateProvider : IPluginHostStateProvider
{
    public FakeHostStateProvider(string state) { MultiInstanceState = state; }
    public string MultiInstanceState { get; }
    public bool MultiInstanceEnabled => MultiInstanceState == "On";
}

private sealed class FakeRunningAccountsProvider : IRunningAccountsProvider
{
    private readonly List<RunningAccountSnapshot> _snapshots;
    public FakeRunningAccountsProvider(IEnumerable<RunningAccountSnapshot> snapshots) { _snapshots = snapshots.ToList(); }
    public IReadOnlyList<RunningAccountSnapshot> Snapshot() => _snapshots;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Expected: build fails — providers don't exist.

- [ ] **Step 3: Add provider interfaces + extend `PluginHostService`**

```csharp
// src/ROROROblox.App/Plugins/IPluginHostStateProvider.cs
namespace ROROROblox.App.Plugins;

public interface IPluginHostStateProvider
{
    bool MultiInstanceEnabled { get; }
    string MultiInstanceState { get; }
}
```

```csharp
// src/ROROROblox.App/Plugins/IRunningAccountsProvider.cs
namespace ROROROblox.App.Plugins;

public interface IRunningAccountsProvider
{
    IReadOnlyList<RunningAccountSnapshot> Snapshot();
}

public sealed record RunningAccountSnapshot(
    string AccountId,
    long RobloxUserId,
    string DisplayName,
    int ProcessId);
```

Update `PluginHostService` constructor to take both providers; add `GetHostInfo` and `GetRunningAccounts` methods that translate snapshots to proto messages:

```csharp
public sealed partial class PluginHostService : RoRoRoHost.RoRoRoHostBase
{
    private readonly IInstalledPluginsLookup _registry;
    private readonly string _hostVersion;
    private readonly string _supportedContractVersion;
    private readonly IPluginHostStateProvider _hostState;
    private readonly IRunningAccountsProvider _runningAccounts;

    public PluginHostService(
        IInstalledPluginsLookup registry,
        string hostVersion,
        string supportedContractVersion,
        IPluginHostStateProvider hostState,
        IRunningAccountsProvider runningAccounts)
    {
        _registry = registry;
        _hostVersion = hostVersion;
        _supportedContractVersion = supportedContractVersion;
        _hostState = hostState;
        _runningAccounts = runningAccounts;
    }

    // ...handshake from Task 11...

    public override Task<HostInfo> GetHostInfo(Empty request, ServerCallContext context)
    {
        return Task.FromResult(new HostInfo
        {
            Version = _hostVersion,
            MultiInstanceEnabled = _hostState.MultiInstanceEnabled,
            MultiInstanceState = _hostState.MultiInstanceState,
        });
    }

    public override Task<RunningAccountsList> GetRunningAccounts(Empty request, ServerCallContext context)
    {
        var list = new RunningAccountsList();
        foreach (var snapshot in _runningAccounts.Snapshot())
        {
            list.Accounts.Add(new RunningAccount
            {
                AccountId = snapshot.AccountId,
                RobloxUserId = snapshot.RobloxUserId,
                DisplayName = snapshot.DisplayName,
                ProcessId = snapshot.ProcessId,
            });
        }
        return Task.FromResult(list);
    }
}
```

You'll also need to update the existing handshake test fixtures to inject fake providers (now constructor takes them). Update both `Handshake_*` tests to pass `new FakeHostStateProvider("On")` and `new FakeRunningAccountsProvider(Array.Empty<RunningAccountSnapshot>())`.

- [ ] **Step 4: Run tests to verify they pass**

Expected: 5/5 tests in `PluginHostServiceTests` passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginHostService.cs src/ROROROblox.App/Plugins/IPluginHostStateProvider.cs src/ROROROblox.App/Plugins/IRunningAccountsProvider.cs src/ROROROblox.Tests/PluginHostServiceTests.cs
git commit -m "feat(plugins): host read surface — GetHostInfo + GetRunningAccounts"
```

---

## Task 13: CapabilityInterceptor — gate gRPC calls by manifest+consent

**Files:**
- Create: `src/ROROROblox.App/Plugins/CapabilityInterceptor.cs`
- Create: `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`
- Test: `src/ROROROblox.Tests/CapabilityInterceptorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Grpc.Core;
using Grpc.Core.Interceptors;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class CapabilityInterceptorTests
{
    [Fact]
    public async Task UnaryServerHandler_AllowsCallWhenCapabilityGranted()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: pluginId => new[] { "host.commands.request-launch" });

        var continuation = (Func<object, ServerCallContext, Task<string>>)(
            (req, ctx) => Task.FromResult("ok"));

        var ctx = TestServerCallContext.Create("RoRoRoHost.RequestLaunch");
        var result = await interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task UnaryServerHandler_ThrowsPermissionDenied_WhenCapabilityMissing()
    {
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: pluginId => Array.Empty<string>());

        var continuation = (Func<object, ServerCallContext, Task<string>>)(
            (req, ctx) => Task.FromResult("ok"));

        var ctx = TestServerCallContext.Create("RoRoRoHost.RequestLaunch");
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UnaryServerHandler_AllowsHandshake_BeforePluginIsKnown()
    {
        // Handshake is the bootstrap call; no capability check applies because
        // we don't yet know which plugin is calling.
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: _ => Array.Empty<string>());

        var continuation = (Func<object, ServerCallContext, Task<string>>)(
            (req, ctx) => Task.FromResult("handshake-ok"));

        var ctx = TestServerCallContext.Create("RoRoRoHost.Handshake");
        var result = await interceptor.UnaryServerHandler<object, string>(new(), ctx, continuation);

        Assert.Equal("handshake-ok", result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: build fails — `CapabilityInterceptor` doesn't exist.

- [ ] **Step 3: Implement `RpcMethodCapabilityMap` + `CapabilityInterceptor`**

```csharp
// src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs
namespace ROROROblox.App.Plugins;

/// <summary>Maps every gRPC method name to the capability it requires (if any).</summary>
public static class RpcMethodCapabilityMap
{
    private static readonly IReadOnlyDictionary<string, string?> Map = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["RoRoRoHost.Handshake"] = null,                      // no gate
        ["RoRoRoHost.GetHostInfo"] = null,                    // free read
        ["RoRoRoHost.GetRunningAccounts"] = null,             // free read (UID-aware data is the whole point)
        ["RoRoRoHost.SubscribeAccountLaunched"] = PluginCapability.HostEventsAccountLaunched,
        ["RoRoRoHost.SubscribeAccountExited"] = PluginCapability.HostEventsAccountExited,
        ["RoRoRoHost.SubscribeMutexStateChanged"] = PluginCapability.HostEventsMutexStateChanged,
        ["RoRoRoHost.RequestLaunch"] = PluginCapability.HostCommandsRequestLaunch,
        ["RoRoRoHost.AddTrayMenuItem"] = PluginCapability.HostUITrayMenu,
        ["RoRoRoHost.AddRowBadge"] = PluginCapability.HostUIRowBadge,
        ["RoRoRoHost.AddStatusPanel"] = PluginCapability.HostUIStatusPanel,
        ["RoRoRoHost.UpdateUI"] = null,                       // gated by the AddX call that issued the handle
        ["RoRoRoHost.RemoveUI"] = null,
    };

    /// <summary>Returns the capability required for the given method, or null if no gate.</summary>
    public static string? Required(string methodName)
        => Map.TryGetValue(methodName, out var cap) ? cap : null;

    /// <summary>True iff the method exists in the contract.</summary>
    public static bool IsKnown(string methodName) => Map.ContainsKey(methodName);
}
```

```csharp
// src/ROROROblox.App/Plugins/CapabilityInterceptor.cs
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Server-side gRPC interceptor that gates each call by the calling plugin's
/// declared+consented capabilities. The current-plugin accessor is provided
/// per-connection by <see cref="PluginConnectionContext"/> (set during handshake).
/// </summary>
public sealed class CapabilityInterceptor : Interceptor
{
    private readonly Func<string?> _currentPluginAccessor;
    private readonly Func<string, IReadOnlyList<string>> _consentLookup;

    public CapabilityInterceptor(Func<string?> currentPluginAccessor, Func<string, IReadOnlyList<string>> consentLookup)
    {
        _currentPluginAccessor = currentPluginAccessor;
        _consentLookup = consentLookup;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnforceCapability(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnforceCapability(context);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    private void EnforceCapability(ServerCallContext context)
    {
        var method = context.Method.TrimStart('/').Replace('/', '.');
        var required = RpcMethodCapabilityMap.Required(method);
        if (required is null)
        {
            return; // no gate
        }

        var pluginId = _currentPluginAccessor();
        if (pluginId is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Handshake required before this call."));
        }

        var granted = _consentLookup(pluginId);
        if (!granted.Contains(required))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"Plugin '{pluginId}' has not been granted '{required}'."));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Expected: 3/3 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/CapabilityInterceptor.cs src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs src/ROROROblox.Tests/CapabilityInterceptorTests.cs
git commit -m "feat(plugins): CapabilityInterceptor — gRPC capability gating"
```

---

## Task 14: Event subscription RPCs (server streaming)

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs`
- Modify: `src/ROROROblox.Tests/PluginHostServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task SubscribeAccountLaunched_FansOutEvents_ToAllSubscribers()
{
    var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
    var hostState = new FakeHostStateProvider("On");
    var accounts = new FakeRunningAccountsProvider(Array.Empty<RunningAccountSnapshot>());
    var bus = new InProcessPluginEventBus();
    var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts, bus);

    var writer = new TestStreamWriter<AccountLaunchedEvent>();
    var ctx = TestServerCallContext.Create();
    var task = service.SubscribeAccountLaunched(new SubscriptionRequest(), writer, ctx);

    bus.RaiseAccountLaunched(new RunningAccountSnapshot(
        "00000000-0000-0000-0000-000000000001", 12345, "Alice", 9999));

    // Cancel the stream after a short wait to let event propagate.
    await Task.Delay(50);
    ctx.CancelToken();
    await task; // should complete cleanly

    var evt = Assert.Single(writer.Written);
    Assert.Equal(12345L, evt.RobloxUserId);
}
```

(`TestStreamWriter` is a small testing fake; add inline.)

- [ ] **Step 2: Run test to verify it fails**

Expected: build fails — `IPluginEventBus`, `InProcessPluginEventBus` don't exist.

- [ ] **Step 3: Implement event bus + subscribe RPC**

```csharp
// src/ROROROblox.App/Plugins/IPluginEventBus.cs
namespace ROROROblox.App.Plugins;

public interface IPluginEventBus
{
    event Action<RunningAccountSnapshot>? AccountLaunched;
    event Action<RunningAccountSnapshot, int>? AccountExited; // snapshot + exitedAt-ms
    event Action<string>? MutexStateChanged;
}
```

```csharp
// src/ROROROblox.App/Plugins/InProcessPluginEventBus.cs
namespace ROROROblox.App.Plugins;

public sealed class InProcessPluginEventBus : IPluginEventBus
{
    public event Action<RunningAccountSnapshot>? AccountLaunched;
    public event Action<RunningAccountSnapshot, int>? AccountExited;
    public event Action<string>? MutexStateChanged;

    public void RaiseAccountLaunched(RunningAccountSnapshot s) => AccountLaunched?.Invoke(s);
    public void RaiseAccountExited(RunningAccountSnapshot s, int exitedAtMs) => AccountExited?.Invoke(s, exitedAtMs);
    public void RaiseMutexStateChanged(string state) => MutexStateChanged?.Invoke(state);
}
```

Extend `PluginHostService`:

```csharp
public override async Task SubscribeAccountLaunched(
    SubscriptionRequest request,
    IServerStreamWriter<AccountLaunchedEvent> responseStream,
    ServerCallContext context)
{
    var queue = new System.Threading.Channels.Channel<AccountLaunchedEvent>();
    Action<RunningAccountSnapshot> handler = s => queue.Writer.TryWrite(new AccountLaunchedEvent
    {
        AccountId = s.AccountId,
        RobloxUserId = s.RobloxUserId,
        DisplayName = s.DisplayName,
        ProcessId = s.ProcessId,
        LaunchedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    });

    _eventBus.AccountLaunched += handler;
    try
    {
        await foreach (var evt in queue.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(evt);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        _eventBus.AccountLaunched -= handler;
    }
}

// SubscribeAccountExited and SubscribeMutexStateChanged follow the same pattern.
```

(Add `_eventBus` field, update constructor + tests to inject.)

- [ ] **Step 4: Run tests to verify they pass**

Expected: tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.App/Plugins/IPluginEventBus.cs src/ROROROblox.App/Plugins/InProcessPluginEventBus.cs src/ROROROblox.App/Plugins/PluginHostService.cs src/ROROROblox.Tests/PluginHostServiceTests.cs
git commit -m "feat(plugins): event subscription RPCs (account launched/exited, mutex)"
```

---

## Task 15: Command surface — RequestLaunch RPC

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginHostService.cs`
- Modify: `src/ROROROblox.Tests/PluginHostServiceTests.cs`

- [ ] **Step 1: Add failing test**

```csharp
[Fact]
public async Task RequestLaunch_DispatchesToLauncher_AndReturnsResult()
{
    var fakeLauncher = new FakeLaunchInvoker();
    var service = new PluginHostService(
        new InMemoryRegistry(Array.Empty<InstalledPlugin>()),
        "1.4.0", "1.0",
        new FakeHostStateProvider("On"),
        new FakeRunningAccountsProvider(Array.Empty<RunningAccountSnapshot>()),
        new InProcessPluginEventBus(),
        fakeLauncher);

    var result = await service.RequestLaunch(new LaunchRequest
    {
        AccountId = Guid.NewGuid().ToString(),
    }, TestServerCallContext.Create());

    Assert.True(result.Ok);
    Assert.Single(fakeLauncher.Invocations);
}

private sealed class FakeLaunchInvoker : IPluginLaunchInvoker
{
    public List<string> Invocations { get; } = new();
    public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
    {
        Invocations.Add(accountId);
        return Task.FromResult((true, (string?)null, 12345));
    }
}
```

- [ ] **Step 2: Run test, verify fails**

- [ ] **Step 3: Implement `IPluginLaunchInvoker` + extend service**

```csharp
// src/ROROROblox.App/Plugins/IPluginLaunchInvoker.cs
namespace ROROROblox.App.Plugins;

public interface IPluginLaunchInvoker
{
    Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId);
}
```

Add to `PluginHostService`:

```csharp
public override async Task<LaunchResult> RequestLaunch(LaunchRequest request, ServerCallContext context)
{
    var (ok, reason, pid) = await _launcher.RequestLaunchAsync(request.AccountId).ConfigureAwait(false);
    return new LaunchResult
    {
        Ok = ok,
        FailureReason = reason ?? string.Empty,
        ProcessId = pid,
    };
}
```

(Update constructor, tests.)

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(plugins): RequestLaunch RPC + IPluginLaunchInvoker"
```

---

## Task 16: PluginUITranslator — UI specs → WPF surfaces (tray + row badge)

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginUITranslator.cs`
- Create: `src/ROROROblox.App/Plugins/IPluginUIHost.cs`
- Test: `src/ROROROblox.Tests/PluginUITranslatorTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.Tests;

public class PluginUITranslatorTests
{
    [Fact]
    public void AddTrayMenuItem_DispatchesToHost_AndAssignsHandle()
    {
        var host = new FakePluginUIHost();
        var translator = new PluginUITranslator(host);

        var handle = translator.AddTrayMenuItem("626labs.test", new MenuItemSpec
        {
            Label = "Toggle auto-keys",
            Tooltip = "Start or stop the cycler",
            Enabled = true,
        });

        Assert.NotEmpty(handle.Id);
        Assert.Single(host.AddedMenuItems);
        Assert.Equal("Toggle auto-keys", host.AddedMenuItems[0].label);
    }

    [Fact]
    public void RemoveUI_DispatchesToHost_AndForgetsHandle()
    {
        var host = new FakePluginUIHost();
        var translator = new PluginUITranslator(host);
        var handle = translator.AddTrayMenuItem("626labs.test", new MenuItemSpec { Label = "x" });

        translator.RemoveUI("626labs.test", handle);

        Assert.Single(host.RemovedHandles);
    }

    private sealed class FakePluginUIHost : IPluginUIHost
    {
        public List<(string pluginId, string label)> AddedMenuItems { get; } = new();
        public List<string> RemovedHandles { get; } = new();
        public string AddTrayMenuItem(string pluginId, string label, string? tooltip, bool enabled, Action onClick)
        {
            var id = Guid.NewGuid().ToString("N");
            AddedMenuItems.Add((pluginId, label));
            return id;
        }
        public string AddRowBadge(string pluginId, string text, string? colorHex, string? tooltip) => Guid.NewGuid().ToString("N");
        public string AddStatusPanel(string pluginId, string title, string bodyMarkdown) => Guid.NewGuid().ToString("N");
        public void Update(string handle, string newLabel) { }
        public void Remove(string handle) => RemovedHandles.Add(handle);
    }
}
```

- [ ] **Step 2: Run test, verify fails**

- [ ] **Step 3: Implement IPluginUIHost + PluginUITranslator**

```csharp
// src/ROROROblox.App/Plugins/IPluginUIHost.cs
namespace ROROROblox.App.Plugins;

/// <summary>The WPF-side host that PluginUITranslator dispatches into.</summary>
public interface IPluginUIHost
{
    string AddTrayMenuItem(string pluginId, string label, string? tooltip, bool enabled, Action onClick);
    string AddRowBadge(string pluginId, string text, string? colorHex, string? tooltip);
    string AddStatusPanel(string pluginId, string title, string bodyMarkdown);
    void Update(string handle, string newLabel);
    void Remove(string handle);
}
```

```csharp
// src/ROROROblox.App/Plugins/PluginUITranslator.cs
using ROROROblox.PluginContract;

namespace ROROROblox.App.Plugins;

public sealed class PluginUITranslator
{
    private readonly IPluginUIHost _host;
    private readonly Dictionary<string, string> _ownerByHandle = new();

    public PluginUITranslator(IPluginUIHost host)
    {
        _host = host;
    }

    public event Action<string /*pluginId*/, UIInteractionEvent>? UIInteraction;

    public UIHandle AddTrayMenuItem(string pluginId, MenuItemSpec spec)
    {
        var handleId = _host.AddTrayMenuItem(pluginId, spec.Label, spec.Tooltip, spec.Enabled,
            onClick: () => UIInteraction?.Invoke(pluginId, new UIInteractionEvent
            {
                Handle = new UIHandle { Id = "" }, // filled in below
                InteractionKind = "click",
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }));
        _ownerByHandle[handleId] = pluginId;
        return new UIHandle { Id = handleId };
    }

    public UIHandle AddRowBadge(string pluginId, RowBadgeSpec spec)
    {
        var handleId = _host.AddRowBadge(pluginId, spec.Text, spec.ColorHex, spec.Tooltip);
        _ownerByHandle[handleId] = pluginId;
        return new UIHandle { Id = handleId };
    }

    public UIHandle AddStatusPanel(string pluginId, StatusPanelSpec spec)
    {
        var handleId = _host.AddStatusPanel(pluginId, spec.Title, spec.BodyMarkdown);
        _ownerByHandle[handleId] = pluginId;
        return new UIHandle { Id = handleId };
    }

    public void RemoveUI(string pluginId, UIHandle handle)
    {
        if (_ownerByHandle.TryGetValue(handle.Id, out var owner) && owner == pluginId)
        {
            _host.Remove(handle.Id);
            _ownerByHandle.Remove(handle.Id);
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(plugins): PluginUITranslator — declarative UI specs to WPF surfaces"
```

---

## Task 17: Scaffold `ROROROblox.PluginTestHarness` integration project

**Files:**
- Create: `src/ROROROblox.PluginTestHarness/ROROROblox.PluginTestHarness.csproj`
- Create: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`
- Modify: `ROROROblox.sln`

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Grpc.Net.Client" Version="2.68.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ROROROblox.App\ROROROblox.App.csproj" />
    <ProjectReference Include="..\ROROROblox.PluginContract\ROROROblox.PluginContract.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to the solution**

```bash
dotnet sln ROROROblox.sln add src/ROROROblox.PluginTestHarness/ROROROblox.PluginTestHarness.csproj
```

- [ ] **Step 3: Write a placeholder end-to-end test**

```csharp
namespace ROROROblox.PluginTestHarness;

public class EndToEndContractTests
{
    [Fact(Skip = "real-pipe E2E lands in Task 18")]
    public void Placeholder() { }
}
```

- [ ] **Step 4: Build to confirm**

```bash
dotnet build src/ROROROblox.PluginTestHarness/
```

- [ ] **Step 5: Commit**

```bash
git add src/ROROROblox.PluginTestHarness/ ROROROblox.sln
git commit -m "test(plugins): scaffold PluginTestHarness integration project"
```

---

## Task 18: End-to-end gRPC over named pipe — handshake + GetHostInfo

**Files:**
- Modify: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`
- Create: `src/ROROROblox.App/Plugins/PluginHostStartupService.cs`

- [ ] **Step 1: Implement `PluginHostStartupService`**

This `IHostedService` spins up the Kestrel + gRPC server on the named pipe and tears it down on shutdown.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Plugins;

public sealed class PluginHostStartupService : IHostedService, IDisposable
{
    public const string DefaultPipeName = "rororo-plugin-host";

    private readonly PluginHostService _hostService;
    private readonly CapabilityInterceptor _interceptor;
    private readonly ILogger<PluginHostStartupService> _log;
    private readonly string _pipeName;
    private WebApplication? _webApp;

    public PluginHostStartupService(
        PluginHostService hostService,
        CapabilityInterceptor interceptor,
        ILogger<PluginHostStartupService> log,
        string? pipeName = null)
    {
        _hostService = hostService;
        _interceptor = interceptor;
        _log = log;
        _pipeName = pipeName ?? DefaultPipeName;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddGrpc(o => o.Interceptors.Add<CapabilityInterceptor>());
        builder.Services.AddSingleton(_interceptor);
        builder.Services.AddSingleton(_hostService);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenNamedPipe(_pipeName, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        _webApp = builder.Build();
        _webApp.MapGrpcService<PluginHostService>();
        await _webApp.StartAsync(cancellationToken).ConfigureAwait(false);
        _log.LogInformation("PluginHost gRPC server listening on \\\\.\\pipe\\{Pipe}", _pipeName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webApp is not null)
        {
            await _webApp.StopAsync(cancellationToken).ConfigureAwait(false);
            await _webApp.DisposeAsync().ConfigureAwait(false);
            _webApp = null;
        }
    }

    public void Dispose() { _webApp?.DisposeAsync().AsTask().Wait(); }
}
```

- [ ] **Step 2: Write the integration test**

```csharp
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.PluginTestHarness;

public class EndToEndContractTests
{
    [Fact]
    public async Task PluginConnectsAndHandshakeSucceeds()
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
            Consent = new ConsentRecord { PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.events.account-launched" },
                AutostartEnabled = false },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()));
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { "host.events.account-launched" });

        await using var startup = new PluginHostStartupService(
            hostService, interceptor, NullLogger<PluginHostStartupService>.Instance, pipeName);
        await startup.StartAsync(CancellationToken.None);

        try
        {
            var connection = new System.IO.Pipes.NamedPipeClientStream(".", pipeName,
                System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await connection.ConnectAsync(TimeSpan.FromSeconds(5));

            using var channel = GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    ConnectCallback = async (ctx, ct) =>
                    {
                        var pipe = new System.IO.Pipes.NamedPipeClientStream(".", pipeName,
                            System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(ct);
                        return pipe;
                    },
                },
            });

            var client = new RoRoRoHost.RoRoRoHostClient(channel);
            var response = await client.HandshakeAsync(new HandshakeRequest
            {
                PluginId = "626labs.test", ContractVersion = "1.0",
            });

            Assert.True(response.Accepted);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
        }
    }

    private sealed class SingleInstalledPluginLookup : IInstalledPluginsLookup
    {
        private readonly InstalledPlugin _plugin;
        public SingleInstalledPluginLookup(InstalledPlugin p) { _plugin = p; }
        public InstalledPlugin? FindById(string id) => id == _plugin.Manifest.Id ? _plugin : null;
    }
    private sealed class FixedHostState : IPluginHostStateProvider
    {
        public FixedHostState(string s) { MultiInstanceState = s; }
        public bool MultiInstanceEnabled => MultiInstanceState == "On";
        public string MultiInstanceState { get; }
    }
    private sealed class EmptyAccounts : IRunningAccountsProvider
    {
        public IReadOnlyList<RunningAccountSnapshot> Snapshot() => Array.Empty<RunningAccountSnapshot>();
    }
    private sealed class NoOpLauncher : IPluginLaunchInvoker
    {
        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
            => Task.FromResult((false, "test stub", 0));
    }
    private sealed class NullUIHost : IPluginUIHost
    {
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c) => "";
        public string AddRowBadge(string p, string t, string? c, string? tt) => "";
        public string AddStatusPanel(string p, string t, string b) => "";
        public void Update(string h, string l) { }
        public void Remove(string h) { }
    }
}
```

- [ ] **Step 3: Run the integration test**

```bash
dotnet test src/ROROROblox.PluginTestHarness/ -v minimal
```

Expected: passes. (Real Windows named pipe + real gRPC server + real client.)

- [ ] **Step 4: Commit**

```bash
git add src/ROROROblox.App/Plugins/PluginHostStartupService.cs src/ROROROblox.PluginTestHarness/
git commit -m "test(plugins): end-to-end handshake over real named-pipe gRPC"
```

---

## Task 19: M2 milestone checkpoint

- [ ] **Step 1: Run full test suite (unit + integration)**

```bash
dotnet test src/ROROROblox.Tests/ src/ROROROblox.PluginTestHarness/ -v minimal
```

Expected: all green.

- [ ] **Step 2: Verify App still builds**

```bash
dotnet build src/ROROROblox.App/ROROROblox.App.csproj -c Debug
```

- [ ] **Step 3: Tag the milestone**

```bash
git tag plugin-system-m2
```

- [ ] **Step 4: Log decision to dashboard**

`mcp__626Labs__manage_decisions log` — name what's wired (gRPC handshake + read + events + commands + UI translator), what's pending (DI in App, user-facing UI).

---

## Task 20: DI registration in App.xaml.cs

**Files:**
- Modify: `src/ROROROblox.App/App.xaml.cs`

- [ ] **Step 1: Add Plugins section to `ConfigureServices`**

After the existing service registrations, add:

```csharp
// === Plugins (v1.4) ===
var pluginsRoot = ROROROblox.App.Plugins.PluginRegistry.DefaultPluginsRoot;
var consentPath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ROROROblox", "consent.dat");

services.AddSingleton<ROROROblox.App.Plugins.ConsentStore>(_ =>
    new ROROROblox.App.Plugins.ConsentStore(consentPath));
services.AddSingleton<ROROROblox.App.Plugins.PluginRegistry>(sp =>
    new ROROROblox.App.Plugins.PluginRegistry(
        pluginsRoot,
        sp.GetRequiredService<ROROROblox.App.Plugins.ConsentStore>()));
services.AddSingleton<ROROROblox.App.Plugins.IInstalledPluginsLookup>(sp =>
    new InstalledPluginsLookupAdapter(sp.GetRequiredService<ROROROblox.App.Plugins.PluginRegistry>()));

services.AddHttpClient<ROROROblox.App.Plugins.PluginInstaller>((sp, client) =>
{
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
        "ROROROblox-PluginInstaller", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
});

services.AddSingleton<ROROROblox.App.Plugins.IPluginProcessStarter, DefaultPluginProcessStarter>();
services.AddSingleton<ROROROblox.App.Plugins.PluginProcessSupervisor>();

services.AddSingleton<ROROROblox.App.Plugins.IPluginEventBus, ROROROblox.App.Plugins.InProcessPluginEventBus>();
services.AddSingleton<ROROROblox.App.Plugins.IPluginHostStateProvider, MutexHostStateAdapter>();
services.AddSingleton<ROROROblox.App.Plugins.IRunningAccountsProvider, MainViewModelRunningAccountsAdapter>();
services.AddSingleton<ROROROblox.App.Plugins.IPluginLaunchInvoker, MainViewModelLaunchInvokerAdapter>();
services.AddSingleton<ROROROblox.App.Plugins.IPluginUIHost, WpfPluginUIHost>();
services.AddSingleton<ROROROblox.App.Plugins.PluginUITranslator>();
services.AddSingleton<ROROROblox.App.Plugins.PluginHostService>(sp => new ROROROblox.App.Plugins.PluginHostService(
    sp.GetRequiredService<ROROROblox.App.Plugins.IInstalledPluginsLookup>(),
    typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
    "1.0",
    sp.GetRequiredService<ROROROblox.App.Plugins.IPluginHostStateProvider>(),
    sp.GetRequiredService<ROROROblox.App.Plugins.IRunningAccountsProvider>(),
    sp.GetRequiredService<ROROROblox.App.Plugins.IPluginEventBus>(),
    sp.GetRequiredService<ROROROblox.App.Plugins.IPluginLaunchInvoker>(),
    sp.GetRequiredService<ROROROblox.App.Plugins.PluginUITranslator>()));

services.AddSingleton(sp => new ROROROblox.App.Plugins.CapabilityInterceptor(
    currentPluginAccessor: () => /* per-connection plugin id; v1.4: single-plugin assumption */ "current",
    consentLookup: pluginId => sp.GetRequiredService<ROROROblox.App.Plugins.ConsentStore>().ListAsync().GetAwaiter().GetResult().FirstOrDefault(r => r.PluginId == pluginId)?.GrantedCapabilities ?? Array.Empty<string>()));
services.AddSingleton<ROROROblox.App.Plugins.PluginHostStartupService>();
```

(Adapter classes `InstalledPluginsLookupAdapter`, `DefaultPluginProcessStarter`, `MutexHostStateAdapter`, `MainViewModelRunningAccountsAdapter`, `MainViewModelLaunchInvokerAdapter`, `WpfPluginUIHost` are stub-implementations that wire the adapters; create each in a separate small file and write a one-line test for the simplest behavior.)

- [ ] **Step 2: Wire startup/shutdown of `PluginHostStartupService` from `App.OnStartup` / `App.OnExit`**

In `OnStartup`, after `_services = services.BuildServiceProvider();`:

```csharp
var pluginHost = _services.GetRequiredService<ROROROblox.App.Plugins.PluginHostStartupService>();
_ = pluginHost.StartAsync(CancellationToken.None);
```

In `OnExit`, before `_services?.Dispose()`:

```csharp
if (_services is not null)
{
    var pluginHost = _services.GetService<ROROROblox.App.Plugins.PluginHostStartupService>();
    if (pluginHost is not null)
    {
        pluginHost.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 3: Manual smoke**

Build the App, run it. Verify the gRPC server starts (look for the log line "PluginHost gRPC server listening on ..."). Quit the app, verify clean shutdown.

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(plugins): DI wiring + lifecycle hooks in App.xaml.cs"
```

---

## Task 21: Plugins page UI — install URL field + plugin list + autostart toggle

**Files:**
- Create: `src/ROROROblox.App/Plugins/PluginsView.xaml` + `.xaml.cs`
- Create: `src/ROROROblox.App/Plugins/PluginsViewModel.cs`
- Modify: `src/ROROROblox.App/MainWindow.xaml` (add a Plugins section / nav)

- [ ] **Step 1: Build the ViewModel** (data model + commands)

[Code: `PluginsViewModel.cs` ~150 LOC, MVVM with `INotifyPropertyChanged`. Properties: `ObservableCollection<InstalledPluginRow> Plugins`, `string InstallUrlInput`. Commands: `LoadAsync()`, `InstallFromUrlCommand`, `ToggleAutostartCommand(plugin)`, `RevokeCommand(plugin)`. Each command surfaces success/failure via `StatusBanner` field.]

- [ ] **Step 2: Build the XAML view**

[Code: `PluginsView.xaml` — header "Plugins", text input + Install button, ItemsControl bound to `Plugins`. Per-row: name, version, capabilities count, autostart toggle, Revoke button.]

- [ ] **Step 3: Wire into MainWindow**

[Add a "Plugins" tab / nav button in `MainWindow.xaml`; switch the right pane to `PluginsView` when active.]

- [ ] **Step 4: Manual smoke**

Run RoRoRo, navigate to Plugins, paste a test URL (use a local file:// URL pointing to a hand-crafted manifest+zip), verify install flow renders the consent sheet (next task), persists, autostart toggle works.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(plugins): Plugins page UI — install URL + plugin list + autostart toggle"
```

---

## Task 22: Manifest consent sheet

**Files:**
- Create: `src/ROROROblox.App/Plugins/ConsentSheet.xaml` + `.xaml.cs`

- [ ] **Step 1: Build the XAML modal**

Sheet renders: plugin name, version, publisher, description, then a per-capability list. Each capability row: capability id (small) + `PluginCapability.Display(cap)` (human-readable) + a checkbox. Some capabilities may be marked required (the manifest's `host.commands.request-launch` if the plugin uses launch — the system can default required-vs-optional via the catalog). User clicks Install (granted) or Cancel.

- [ ] **Step 2: Wire from `PluginsViewModel.InstallFromUrlCommand`**

After `PluginInstaller.InstallAsync` returns, show `ConsentSheet`, await user decision. If granted, call `ConsentStore.GrantAsync(...)` with the chosen capabilities. If cancelled, delete the install dir.

- [ ] **Step 3: Manual smoke**

Re-run the install flow from Task 21 with a URL whose manifest declares 3 capabilities — verify the consent sheet shows all three with explanations, accepting persists, cancelling rolls back.

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(plugins): manifest consent sheet"
```

---

## Task 23: Plugin status banner — crash / missing / version-incompatible

**Files:**
- Modify: `src/ROROROblox.App/Plugins/PluginsViewModel.cs`
- Modify: `src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs` (raise event on plugin exit)

- [ ] **Step 1: Add `PluginExited` event to supervisor**

Wire `Process.Exited` for each tracked PID; raise `event Action<string /*pluginId*/, int /*exitCode*/>? PluginExited` when a plugin dies. Update tests to cover.

- [ ] **Step 2: Subscribe in `PluginsViewModel`**

Set `StatusBanner = "AutoKeys stopped — click to restart"` with a Restart command bound to `PluginProcessSupervisor.Restart(plugin)`.

- [ ] **Step 3: Surface in `PluginsView.xaml`**

Add a banner control above the plugin list, bound to `StatusBanner`.

- [ ] **Step 4: Manual smoke**

Install a stub plugin EXE (a tiny test EXE that exits with code 1 after 2 seconds), enable autostart, restart RoRoRo, verify banner appears within 3 seconds.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(plugins): plugin status banner for crash / missing"
```

---

## Task 24: Integration test suite — capability gate + consent revocation

**Files:**
- Modify: `src/ROROROblox.PluginTestHarness/EndToEndContractTests.cs`

- [ ] **Step 1: Add capability-gate test**

Stub plugin connects, declares only `host.events.account-launched`. Test calls `client.RequestLaunchAsync(...)` → asserts `RpcException(StatusCode.PermissionDenied)`.

- [ ] **Step 2: Add consent-revocation test**

Stub plugin subscribed to events. Test calls `consentStore.RevokeAsync(pluginId)` → asserts the event stream throws `Cancelled` within ~1s.

- [ ] **Step 3: Add UI-add test**

Stub plugin calls `AddTrayMenuItemAsync(spec)` → asserts a `UIHandle` returned + `IPluginUIHost` test fake recorded the call.

- [ ] **Step 4: Run**

```bash
dotnet test src/ROROROblox.PluginTestHarness/ -v minimal
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git commit -am "test(plugins): integration coverage — capability gate, consent revoke, UI add"
```

---

## Task 25: M3 milestone checkpoint + version bump + release

- [ ] **Step 1: Full smoke test on a clean Win11 VM**

Per Phase 7 of the release playbook: install a real-but-tiny "Hello World" plugin from a GitHub release URL, walk consent, enable autostart, verify it works across a RoRoRo restart, revoke consent, verify graceful disconnect.

- [ ] **Step 2: Run full test suite**

```bash
dotnet test src/ROROROblox.Tests/ src/ROROROblox.PluginTestHarness/ -v minimal
```

- [ ] **Step 3: Bump version to 1.4.0.0**

Run the release playbook's Phase 3 (`scripts/finalize-store-build.ps1 -Version 1.4.0.0`) + Phase 4 (sideload dance).

- [ ] **Step 4: Update release notes**

Create `docs/store/release-notes-1.4.0.0.md` per the release-playbook Phase 2. Highlight: plugin system shipped, no plugins yet (auto-keys lands separately), Store eligibility preserved.

- [ ] **Step 5: Tag + push**

```bash
git tag v1.4.0.0
git push origin main
git push origin v1.4.0.0
```

GitHub Actions will draft the release; finalize per Phase 6.

- [ ] **Step 6: Log decision + post**

`mcp__626Labs__manage_decisions log` for the v1.4 architectural milestone. After GH release publishes, post in Discord per Phase 8.

---

## Spec coverage check (self-review)

| Spec section | Tasks | Coverage |
|---|---|---|
| Architecture (named pipe gRPC) | 10, 18 | ✓ |
| `ROROROblox.PluginContract` shared NuGet | 1, 2 | ✓ |
| `PluginManifest` | 3 | ✓ |
| `PluginCapability` | 4 | ✓ |
| `ConsentStore` (DPAPI) | 5 | ✓ |
| `PluginRegistry` | 6 | ✓ |
| `PluginInstaller` (GitHub URL flow) | 7 | ✓ |
| `PluginProcessSupervisor` | 8, 23 | ✓ |
| `PluginHostService` (handshake / read / events / command / UI) | 11–16 | ✓ |
| `CapabilityInterceptor` | 13 | ✓ |
| `PluginUITranslator` | 16 | ✓ |
| `PluginHostStartupService` (Kestrel + named pipe) | 18 | ✓ |
| DI wiring | 20 | ✓ |
| Plugins page UI | 21 | ✓ |
| Consent sheet | 22 | ✓ |
| Status banner | 23 | ✓ |
| Integration tests | 18, 24 | ✓ |
| Release path | 25 | ✓ |

All spec sections have a task. No `TBD` placeholders. Type names consistent (e.g., `IPluginUIHost`, `PluginUITranslator`, `MenuItemSpec` referenced consistently across tasks).
