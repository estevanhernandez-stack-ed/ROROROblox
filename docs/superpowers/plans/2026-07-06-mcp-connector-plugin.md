# `rororo-ur-mcp` — MCP connector plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **Repo:** NEW repo `rororo-ur-mcp` (create under `%USERPROFILE%\Projects\rororo-ur-mcp`). **Depends on** Plan 1 (host `GetAccounts`, PluginContract NuGet 0.5.0) and Plan 2 (ur-task bridge `ListMacros`/`repeat`/`StopMacro`, ur-task 0.6.0) being **published/available** before Task 2/Task 3 integrate.

**Goal:** A stdio MCP server, packaged as a consent-gated RoRoRo plugin, that lets Claude Code / Claude Desktop drive the recovery loop — launch accounts, select games, follow the main/a friend, list/run/stop Ur Task macros — over two IPC clients (RoRoRo host gRPC + Ur Task bridge JSON).

**Architecture:** C# / .NET 10 console app using the official `ModelContextProtocol` SDK (stdio transport). Two IPC clients behind interfaces (`IRoRoRoHost`, `IUrTaskBridge`) so all tool + name-resolution logic is unit-tested against fakes; the real clients (named-pipe gRPC to `\\.\pipe\rororo-plugin-host`; length-prefixed JSON to `626labs-ur-task`) get one integration test against live pipes. The host client is the installed+consented plugin's identity — its authority is consent, not who spawned it (Claude spawns the process).

**Tech Stack:** .NET 10 / C# 14 (`net10.0-windows`), `ModelContextProtocol` (MCP SDK, stdio), `Grpc.Net.Client` + `ROROROblox.PluginContract` 0.5.0 (generated `RoRoRoHostClient`), `System.Text.Json`, named pipes, xUnit.

## Global Constraints

- **Home = consent-gated plugin, unpackaged-only, autostart OFF** — Claude owns the process lifecycle. Never a Store-core surface.
- **The connector NEVER synthesizes input** — macro tools only *ask* the Ur Task bridge; input-synthesis consent lives in Ur Task. No `SendInput`/keybd_event anywhere in this repo.
- **Plugin identity:** `pluginId` = `626labs.ur-mcp`, `contractVersion` = `"1.0"`, capabilities declared in `manifest.json`: `host.queries.accounts`, `host.commands.request-launch`, `host.commands.launch-target`, `host.queries.current-server`, `host.events.account-launched`, `host.events.account-exited` (only what the tools use).
- **User-Agent / identity is transparent** — no browser spoofing; the connector is identifiable as `626labs.ur-mcp`.
- **Icon + tile via the `626labs-design` skill** — no programmatic placeholder ships (pattern x). Brand: cyan `#17d4fa` + magenta `#f22f89`, navy `#0f1f31`.
- **Errors are actionable, never crashes** — RoRoRo-not-running, Ur-Task-not-running, unknown/ambiguous name, consent-denied, and bridge-busy each map to a clear tool result string (spec §8).
- **Conventional commits** (`feat` / `test` / `docs` / `build` / `chore`).

---

## File Structure

```
rororo-ur-mcp/
  rororo-ur-mcp.csproj              # net10.0-windows console; MCP SDK + gRPC + PluginContract 0.5.0
  Program.cs                        # host builder, stdio transport, DI, tool registration
  manifest.json                    # RoRoRo plugin manifest (id, version, contractVersion, capabilities)
  Ipc/
    IRoRoRoHost.cs                  # host abstraction + connector DTOs + typed exceptions
    RoRoRoHostClient.cs            # real named-pipe gRPC client (handshake + RPCs + error mapping)
    IUrTaskBridge.cs                # bridge abstraction + DTOs + typed exceptions
    UrTaskBridgeClient.cs          # real length-prefixed-JSON client + error mapping
    FrameCodec.cs                   # 4-byte length prefix + UTF-8 JSON (mirrors ur-task's codec)
  Resolution/
    NameResolver.cs                 # name-or-id → id for accounts + macros; ambiguity/unknown errors
  Tools/
    AccountTools.cs                 # list_accounts, launch_account, launch_into_game, follow_main, follow_friend, running_status
    MacroTools.cs                   # list_macros, run_macro, stop_macro
  Assets/                           # icon set via design skill
  README.md                         # install + Claude setup + the recovery scenario
docs/store/ (in the RoRoRo repo)    # catalog entry for 626labs.ur-mcp — added at ship, not here
tests/rororo-ur-mcp.Tests/
  FrameCodecTests.cs
  NameResolverTests.cs
  AccountToolsTests.cs
  MacroToolsTests.cs
  Fakes.cs                          # FakeHost : IRoRoRoHost, FakeBridge : IUrTaskBridge
  IpcIntegrationTests.cs            # [Trait("Category","Integration")] real pipes — manual/gated
```

---

## Task 1: Repo scaffold — csproj, manifest, MCP host that boots

**Files:**
- Create: `rororo-ur-mcp.csproj`, `Program.cs`, `manifest.json`, `.gitignore`, `README.md` (stub)
- Create: `tests/rororo-ur-mcp.Tests/rororo-ur-mcp.Tests.csproj`

**Interfaces:**
- Produces: a buildable console MCP server with stdio transport and assembly tool discovery wired (0 tools until Task 5/6). `git init` done.

- [ ] **Step 1: Initialize the repo.**

```bash
mkdir -p %USERPROFILE%/Projects/rororo-ur-mcp && cd %USERPROFILE%/Projects/rororo-ur-mcp && git init
```

- [ ] **Step 2: Create `rororo-ur-mcp.csproj`.**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>0.1.0</Version>
    <AssemblyName>rororo-ur-mcp</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <!-- Pin to the current published MCP SDK version at implement time. -->
    <PackageReference Include="ModelContextProtocol" Version="*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.7" />
    <PackageReference Include="Grpc.Net.Client" Version="2.68.0" />
    <!-- Host gRPC client (RoRoRoHostClient) generated inside PluginContract 0.5.0 (GrpcServices=Both).
         Fallback if the NuGet feed is unavailable: a cross-repo ProjectReference to
         ..\ROROROblox\src\ROROROblox.PluginContract\ROROROblox.PluginContract.csproj -->
    <PackageReference Include="ROROROblox.PluginContract" Version="0.5.0" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="rororo-ur-mcp.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `manifest.json`** (the RoRoRo plugin manifest — capabilities are exactly what the tools use):

```json
{
  "schemaVersion": 1,
  "id": "626labs.ur-mcp",
  "name": "RoRoRo Ur MCP",
  "version": "0.1.0",
  "contractVersion": "1.0",
  "publisher": "626 Labs LLC",
  "description": "Bridges RoRoRo to Claude Code / Claude Desktop over MCP — launch accounts, follow the main, and run Ur Task macros under your consent. Launched by Claude, not RoRoRo; autostart off.",
  "autostart": false,
  "capabilities": [
    "host.queries.accounts",
    "host.commands.request-launch",
    "host.commands.launch-target",
    "host.queries.current-server",
    "host.events.account-launched",
    "host.events.account-exited"
  ]
}
```

- [ ] **Step 4: Create `Program.cs`** (stdio MCP host; DI for the two clients added in Tasks 2-3, tools discovered from assembly):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// MCP servers speak on stdout/stdin — logs must go to stderr only.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// IPC clients registered in Tasks 2-3:
// builder.Services.AddSingleton<IRoRoRoHost, RoRoRoHostClient>();
// builder.Services.AddSingleton<IUrTaskBridge, UrTaskBridgeClient>();

await builder.Build().RunAsync();
```

> The `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` chain is the SDK's standard shape. Confirm exact method names against the pinned `ModelContextProtocol` version during build; adjust if the fluent API differs.

- [ ] **Step 5: Create the test project** `tests/rororo-ur-mcp.Tests/rororo-ur-mcp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\rororo-ur-mcp.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Add a `.gitignore`** (bin/obj, .pfx, user files) and build to confirm the server compiles and boots.

Run: `dotnet build rororo-ur-mcp.csproj`
Expected: `Build succeeded`. (Optionally: `dotnet run` then send an MCP `initialize` frame via the MCP inspector — the server responds with 0 tools.)

- [ ] **Step 7: Commit.**

```bash
git add -A
git commit -m "chore: scaffold rororo-ur-mcp — stdio MCP server + plugin manifest"
```

---

## Task 2: RoRoRo host client — interface, DTOs, real gRPC-over-named-pipe client, error mapping

**Files:**
- Create: `Ipc/IRoRoRoHost.cs`, `Ipc/RoRoRoHostClient.cs`

**Interfaces:**
- Produces:
  - DTOs: `record HostAccount(string AccountId, long RobloxUserId, string DisplayName, bool IsMain)`, `record RunningAccount(string AccountId, long RobloxUserId, string DisplayName, int ProcessId, long PlaceId, string PlaceName)`, `record CurrentServerInfo(bool Present, string ShareUrl, string PlaceName, long PlaceId)`, `record LaunchOutcome(bool Ok, string? FailureReason, int ProcessId)`.
  - Exceptions: `HostUnavailableException` (pipe absent → "RoRoRo isn't running"), `ConsentDeniedException(string Capability)` (gRPC `PermissionDenied`).
  - `interface IRoRoRoHost { Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken); Task<IReadOnlyList<RunningAccount>> GetRunningAccountsAsync(CancellationToken); Task<CurrentServerInfo> GetCurrentServerAsync(CancellationToken); Task<LaunchOutcome> RequestLaunchAsync(string accountId, CancellationToken); Task<LaunchOutcome> LaunchTargetShareAsync(string accountId, string shareUrl, CancellationToken); Task<LaunchOutcome> LaunchTargetFollowAsync(string accountId, long followUserId, CancellationToken); }`
- Consumes: `ROROROblox.PluginContract` generated `RoRoRoHost.RoRoRoHostClient`, `GetAccountsAsync`, `RequestLaunchAsync`, `RequestLaunchTargetAsync`, `GetRunningAccountsAsync`, `GetCurrentServerAsync`, `Handshake`.

- [ ] **Step 1: Create `Ipc/IRoRoRoHost.cs`** with the interface, DTOs, and exceptions exactly as listed in Interfaces above.

- [ ] **Step 2: Create `Ipc/RoRoRoHostClient.cs`.** Connect over the named pipe (mirror RoRoRo's harness `ConnectChannel`), handshake as the installed plugin, map RPCs to DTOs, and translate transport failures:

```csharp
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using Grpc.Core;
using Grpc.Net.Client;
using ROROROblox.PluginContract;

namespace Rororo.UrMcp.Ipc;

public sealed class RoRoRoHostClient : IRoRoRoHost, IDisposable
{
    private const string PipeName = "rororo-plugin-host"; // \\.\pipe\rororo-plugin-host
    private const string PluginId = "626labs.ur-mcp";
    private const string ContractVersion = "1.0";

    private readonly GrpcChannel _channel;
    private readonly RoRoRoHost.RoRoRoHostClient _client;
    private bool _handshaken;

    public RoRoRoHostClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await pipe.ConnectAsync(TimeSpan.FromSeconds(3), ct); }
                catch (TimeoutException) { throw new HostUnavailableException(); }
                catch (IOException) { throw new HostUnavailableException(); }
                return pipe;
            }
        };
        _channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        _client = new RoRoRoHost.RoRoRoHostClient(_channel);
    }

    private async Task EnsureHandshakeAsync(CancellationToken ct)
    {
        if (_handshaken) return;
        var resp = await Invoke(() => _client.HandshakeAsync(new HandshakeRequest
        {
            PluginId = PluginId,
            ManifestSha256 = ManifestSha256(),
            ContractVersion = ContractVersion,
        }, cancellationToken: ct).ResponseAsync);
        if (!resp.Accepted) throw new HostUnavailableException($"RoRoRo rejected the connection: {resp.RejectReason}");
        _handshaken = true;
    }

    public async Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var resp = await Invoke(() => _client.GetAccountsAsync(new Empty(), cancellationToken: ct).ResponseAsync);
        return resp.Accounts.Select(a => new HostAccount(a.AccountId, a.RobloxUserId, a.DisplayName, a.IsMain)).ToList();
    }

    public async Task<IReadOnlyList<RunningAccount>> GetRunningAccountsAsync(CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var resp = await Invoke(() => _client.GetRunningAccountsAsync(new Empty(), cancellationToken: ct).ResponseAsync);
        return resp.Accounts.Select(a => new RunningAccount(a.AccountId, a.RobloxUserId, a.DisplayName, a.ProcessId, a.PlaceId, a.PlaceName)).ToList();
    }

    public async Task<CurrentServerInfo> GetCurrentServerAsync(CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var r = await Invoke(() => _client.GetCurrentServerAsync(new Empty(), cancellationToken: ct).ResponseAsync);
        return new CurrentServerInfo(r.Present, r.ShareUrl, r.PlaceName, r.PlaceId);
    }

    public async Task<LaunchOutcome> RequestLaunchAsync(string accountId, CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var r = await Invoke(() => _client.RequestLaunchAsync(new LaunchRequest { AccountId = accountId }, cancellationToken: ct).ResponseAsync);
        return new LaunchOutcome(r.Ok, string.IsNullOrEmpty(r.FailureReason) ? null : r.FailureReason, r.ProcessId);
    }

    public async Task<LaunchOutcome> LaunchTargetShareAsync(string accountId, string shareUrl, CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var req = new LaunchTargetRequest { AccountId = accountId, ShareUrl = shareUrl };
        var r = await Invoke(() => _client.RequestLaunchTargetAsync(req, cancellationToken: ct).ResponseAsync);
        return new LaunchOutcome(r.Ok, string.IsNullOrEmpty(r.FailureReason) ? null : r.FailureReason, r.ProcessId);
    }

    public async Task<LaunchOutcome> LaunchTargetFollowAsync(string accountId, long followUserId, CancellationToken ct)
    {
        await EnsureHandshakeAsync(ct);
        var req = new LaunchTargetRequest { AccountId = accountId, FollowUserId = followUserId };
        var r = await Invoke(() => _client.RequestLaunchTargetAsync(req, cancellationToken: ct).ResponseAsync);
        return new LaunchOutcome(r.Ok, string.IsNullOrEmpty(r.FailureReason) ? null : r.FailureReason, r.ProcessId);
    }

    // Translate transport + consent failures into the connector's typed exceptions.
    private static async Task<T> Invoke<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (HostUnavailableException) { throw; }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
        { throw new ConsentDeniedException(ex.Status.Detail); }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.Internal)
        { throw new HostUnavailableException(); }
    }

    private static string ManifestSha256()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "manifest.json");
        if (!File.Exists(path)) return string.Empty;
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    public void Dispose() => _channel.Dispose();
}
```

> The host authenticates by manifest (`FindById` + contract version); the `ManifestSha256` is sent for completeness. If the host enforces sha equality, the shipped `manifest.json` must be byte-identical to the installed copy — it is, since the marketplace installs this same file.

- [ ] **Step 3: Build to confirm it compiles against PluginContract 0.5.0.**

Run: `dotnet build rororo-ur-mcp.csproj`
Expected: `Build succeeded`. (No unit test here — the real client is covered by Task 7's integration test; the tool logic that consumes `IRoRoRoHost` is unit-tested in Task 5 against a fake.)

- [ ] **Step 4: Register in DI.** In `Program.cs`, uncomment/add:

```csharp
builder.Services.AddSingleton<IRoRoRoHost, RoRoRoHostClient>();
```

- [ ] **Step 5: Commit.**

```bash
git add Ipc/IRoRoRoHost.cs Ipc/RoRoRoHostClient.cs Program.cs
git commit -m "feat: RoRoRo host client — named-pipe gRPC, handshake, GetAccounts + launch/follow"
```

---

## Task 3: Ur Task bridge client — frame codec, interface, real JSON-over-pipe client

**Files:**
- Create: `Ipc/FrameCodec.cs`, `Ipc/IUrTaskBridge.cs`, `Ipc/UrTaskBridgeClient.cs`
- Test: `tests/rororo-ur-mcp.Tests/FrameCodecTests.cs`

**Interfaces:**
- Produces:
  - `record MacroInfo(string Id, string Name)`, `record RunMacroResult(bool Ok, string? PlaybackId, string? Reason, string? Detail)`, `record StopResult(bool Ok, int Stopped, string? Reason)`.
  - `BridgeUnavailableException` (pipe absent → "Ur Task isn't available").
  - `interface IUrTaskBridge { Task<IReadOnlyList<MacroInfo>> ListMacrosAsync(CancellationToken); Task<RunMacroResult> RunMacroAsync(string macroId, IReadOnlyList<string> targets, bool repeat, CancellationToken); Task<StopResult> StopMacroAsync(string? playbackId, IReadOnlyList<string>? targets, CancellationToken); }`
  - `FrameCodec.WriteFrameAsync` / `ReadFrameAsync` — 4-byte big-endian length prefix + UTF-8 JSON (byte-compatible with ur-task's `FrameCodec`).
- Consumes: the ur-task wire shape (camelCase JSON): request `{contractVersion:"1.0", method, macroId?, targets?, repeat?, playbackId?, callerPluginId:"626labs.ur-mcp"}`; responses per Plan 2.

- [ ] **Step 1: Write the failing frame-codec round-trip test.** `FrameCodecTests.cs`:

```csharp
using System.IO;
using Rororo.UrMcp.Ipc;
using Xunit;

public class FrameCodecTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsPayload()
    {
        using var ms = new MemoryStream();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"method\":\"ListMacros\"}");
        await FrameCodec.WriteFrameAsync(ms, payload, default);
        ms.Position = 0;
        var back = await FrameCodec.ReadFrameAsync(ms, default);
        Assert.Equal(payload, back);
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter WriteThenRead_RoundTripsPayload`
Expected: FAIL — `FrameCodec` does not exist.

- [ ] **Step 3: Create `Ipc/FrameCodec.cs`** (4-byte big-endian length prefix + UTF-8, 64 KB cap — byte-identical framing to ur-task):

```csharp
using System.Buffers.Binary;

namespace Rororo.UrMcp.Ipc;

internal static class FrameCodec
{
    private const int MaxFrameBytes = 64 * 1024;

    public static async Task WriteFrameAsync(Stream s, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, payload.Length);
        await s.WriteAsync(len, ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var lenBuf = await ReadExactAsync(s, 4, ct);
        if (lenBuf is null) return null;
        var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (len < 0 || len > MaxFrameBytes) throw new InvalidDataException($"Frame length {len} out of range.");
        return await ReadExactAsync(s, len, ct) ?? throw new EndOfStreamException();
    }

    private static async Task<byte[]?> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        if (count == 0) return Array.Empty<byte>();
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n == 0) return read == 0 ? null : throw new EndOfStreamException();
            read += n;
        }
        return buf;
    }
}
```

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter WriteThenRead_RoundTripsPayload`
Expected: PASS.

- [ ] **Step 5: Create `Ipc/IUrTaskBridge.cs`** (interface + DTOs + exception as in Interfaces above).

- [ ] **Step 6: Create `Ipc/UrTaskBridgeClient.cs`** — one connect-send-receive per call over the `626labs-ur-task` pipe:

```csharp
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rororo.UrMcp.Ipc;

public sealed class UrTaskBridgeClient : IUrTaskBridge
{
    private const string PipeName = "626labs-ur-task";
    private const string CallerId = "626labs.ur-mcp";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<MacroInfo>> ListMacrosAsync(CancellationToken ct)
    {
        var resp = await RoundTripAsync(new { contractVersion = "1.0", method = "ListMacros", callerPluginId = CallerId }, ct);
        var list = resp.RootElement.TryGetProperty("macros", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().Select(e => new MacroInfo(e.GetProperty("id").GetString()!, e.GetProperty("name").GetString()!)).ToList()
            : new List<MacroInfo>();
        return list;
    }

    public async Task<RunMacroResult> RunMacroAsync(string macroId, IReadOnlyList<string> targets, bool repeat, CancellationToken ct)
    {
        var resp = await RoundTripAsync(new { contractVersion = "1.0", method = "RunMacro", macroId, targets, repeat, callerPluginId = CallerId }, ct);
        var r = resp.RootElement;
        return new RunMacroResult(
            r.GetProperty("ok").GetBoolean(),
            r.TryGetProperty("playbackId", out var p) ? p.GetString() : null,
            r.TryGetProperty("reason", out var rs) ? rs.GetString() : null,
            r.TryGetProperty("detail", out var d) ? d.GetString() : null);
    }

    public async Task<StopResult> StopMacroAsync(string? playbackId, IReadOnlyList<string>? targets, CancellationToken ct)
    {
        var resp = await RoundTripAsync(new { contractVersion = "1.0", method = "StopMacro", playbackId, targets, callerPluginId = CallerId }, ct);
        var r = resp.RootElement;
        return new StopResult(
            r.GetProperty("ok").GetBoolean(),
            r.TryGetProperty("stopped", out var s) ? s.GetInt32() : 0,
            r.TryGetProperty("reason", out var rs) ? rs.GetString() : null);
    }

    private static async Task<JsonDocument> RoundTripAsync(object request, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(TimeSpan.FromSeconds(3), ct); }
        catch (TimeoutException) { throw new BridgeUnavailableException(); }
        catch (IOException) { throw new BridgeUnavailableException(); }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        await FrameCodec.WriteFrameAsync(pipe, bytes, ct);
        var frame = await FrameCodec.ReadFrameAsync(pipe, ct) ?? throw new BridgeUnavailableException();
        return JsonDocument.Parse(frame);
    }
}
```

- [ ] **Step 7: Register in DI + build.** In `Program.cs`: `builder.Services.AddSingleton<IUrTaskBridge, UrTaskBridgeClient>();`

Run: `dotnet build rororo-ur-mcp.csproj`
Expected: `Build succeeded`.

- [ ] **Step 8: Commit.**

```bash
git add Ipc/FrameCodec.cs Ipc/IUrTaskBridge.cs Ipc/UrTaskBridgeClient.cs tests/rororo-ur-mcp.Tests/FrameCodecTests.cs Program.cs
git commit -m "feat: Ur Task bridge client — length-prefixed JSON over 626labs-ur-task pipe"
```

---

## Task 4: Name resolution — name-or-id → id, with ambiguity/unknown errors

**Files:**
- Create: `Resolution/NameResolver.cs`
- Create: `tests/rororo-ur-mcp.Tests/Fakes.cs` (`FakeHost`, `FakeBridge`)
- Test: `tests/rororo-ur-mcp.Tests/NameResolverTests.cs`

**Interfaces:**
- Produces: `NameResolver` with `Task<string> ResolveAccountAsync(string nameOrId, IReadOnlyList<HostAccount>, ...)` and `Task<HostAccount> ResolveMainAsync(IReadOnlyList<HostAccount>)`, `string ResolveMacro(string nameOrId, IReadOnlyList<MacroInfo>)`. Case-insensitive name match; exact-id passthrough; unknown → `ResolutionException` listing candidates; ambiguous → `ResolutionException` listing the matches. Consumed by Tasks 5-6.
- Consumes: `HostAccount`, `MacroInfo`.

- [ ] **Step 1: Create the fakes.** `Fakes.cs`:

```csharp
using Rororo.UrMcp.Ipc;

internal sealed class FakeHost : IRoRoRoHost
{
    public List<HostAccount> Accounts = new();
    public List<RunningAccount> Running = new();
    public CurrentServerInfo Server = new(false, "", "", 0);
    public Func<string, LaunchOutcome>? OnLaunch;
    public Func<string, long, LaunchOutcome>? OnFollow;
    public bool Unavailable;

    public Task<IReadOnlyList<HostAccount>> GetAccountsAsync(CancellationToken ct)
        => Unavailable ? throw new HostUnavailableException() : Task.FromResult<IReadOnlyList<HostAccount>>(Accounts);
    public Task<IReadOnlyList<RunningAccount>> GetRunningAccountsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<RunningAccount>>(Running);
    public Task<CurrentServerInfo> GetCurrentServerAsync(CancellationToken ct) => Task.FromResult(Server);
    public Task<LaunchOutcome> RequestLaunchAsync(string accountId, CancellationToken ct)
        => Task.FromResult(OnLaunch?.Invoke(accountId) ?? new LaunchOutcome(true, null, 1234));
    public Task<LaunchOutcome> LaunchTargetShareAsync(string accountId, string shareUrl, CancellationToken ct)
        => Task.FromResult(new LaunchOutcome(true, null, 1234));
    public Task<LaunchOutcome> LaunchTargetFollowAsync(string accountId, long followUserId, CancellationToken ct)
        => Task.FromResult(OnFollow?.Invoke(accountId, followUserId) ?? new LaunchOutcome(true, null, 1234));
}

internal sealed class FakeBridge : IUrTaskBridge
{
    public List<MacroInfo> Macros = new();
    public Func<string, IReadOnlyList<string>, bool, RunMacroResult>? OnRun;
    public bool Unavailable;
    public Task<IReadOnlyList<MacroInfo>> ListMacrosAsync(CancellationToken ct)
        => Unavailable ? throw new BridgeUnavailableException() : Task.FromResult<IReadOnlyList<MacroInfo>>(Macros);
    public Task<RunMacroResult> RunMacroAsync(string macroId, IReadOnlyList<string> targets, bool repeat, CancellationToken ct)
        => Task.FromResult(OnRun?.Invoke(macroId, targets, repeat) ?? new RunMacroResult(true, "pb-1", null, null));
    public Task<StopResult> StopMacroAsync(string? playbackId, IReadOnlyList<string>? targets, CancellationToken ct)
        => Task.FromResult(new StopResult(true, 1, null));
}
```

- [ ] **Step 2: Write the failing resolver tests.** `NameResolverTests.cs`:

```csharp
using Rororo.UrMcp.Ipc;
using Rororo.UrMcp.Resolution;
using Xunit;

public class NameResolverTests
{
    private static readonly HostAccount Pokey = new("id-pokey", 111, "Pokey", true);
    private static readonly HostAccount Spud = new("id-spud", 222, "Spud", false);
    private static readonly HostAccount Spud2 = new("id-spud2", 333, "Spud", false);

    [Fact]
    public void ResolveAccount_ByName_CaseInsensitive()
        => Assert.Equal("id-pokey", NameResolver.ResolveAccount("pokey", new[] { Pokey, Spud }));

    [Fact]
    public void ResolveAccount_ByExactId_Passthrough()
        => Assert.Equal("id-spud", NameResolver.ResolveAccount("id-spud", new[] { Pokey, Spud }));

    [Fact]
    public void ResolveAccount_Unknown_Throws_ListingCandidates()
    {
        var ex = Assert.Throws<ResolutionException>(() => NameResolver.ResolveAccount("Ghost", new[] { Pokey, Spud }));
        Assert.Contains("Pokey", ex.Message);
        Assert.Contains("Spud", ex.Message);
    }

    [Fact]
    public void ResolveAccount_Ambiguous_Throws()
    {
        var ex = Assert.Throws<ResolutionException>(() => NameResolver.ResolveAccount("Spud", new[] { Spud, Spud2 }));
        Assert.Contains("ambiguous", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveMain_ReturnsIsMain()
        => Assert.Equal("id-pokey", NameResolver.ResolveMain(new[] { Pokey, Spud }).AccountId);

    [Fact]
    public void ResolveMain_NoMain_Throws()
        => Assert.Throws<ResolutionException>(() => NameResolver.ResolveMain(new[] { Spud }));
}
```

- [ ] **Step 3: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter NameResolverTests`
Expected: FAIL — `NameResolver` / `ResolutionException` do not exist.

- [ ] **Step 4: Create `Resolution/NameResolver.cs`:**

```csharp
using Rororo.UrMcp.Ipc;

namespace Rororo.UrMcp.Resolution;

public sealed class ResolutionException : Exception
{
    public ResolutionException(string message) : base(message) { }
}

public static class NameResolver
{
    public static string ResolveAccount(string nameOrId, IReadOnlyList<HostAccount> accounts)
    {
        var byId = accounts.FirstOrDefault(a => string.Equals(a.AccountId, nameOrId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId.AccountId;

        var byName = accounts.Where(a => string.Equals(a.DisplayName, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1) return byName[0].AccountId;
        if (byName.Count > 1)
            throw new ResolutionException($"'{nameOrId}' is ambiguous — matches {byName.Count} accounts. Use the account id. Candidates: {Names(accounts)}.");
        throw new ResolutionException($"No account named '{nameOrId}'. Known accounts: {Names(accounts)}.");
    }

    public static HostAccount ResolveMain(IReadOnlyList<HostAccount> accounts)
        => accounts.FirstOrDefault(a => a.IsMain)
           ?? throw new ResolutionException("No account is marked as main in RoRoRo. Set a main, or name the account to follow explicitly.");

    public static string ResolveMacro(string nameOrId, IReadOnlyList<MacroInfo> macros)
    {
        var byId = macros.FirstOrDefault(m => string.Equals(m.Id, nameOrId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId.Id;

        var byName = macros.Where(m => string.Equals(m.Name, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1) return byName[0].Id;
        if (byName.Count > 1)
            throw new ResolutionException($"'{nameOrId}' is ambiguous — matches {byName.Count} macros. Use the macro id. Candidates: {MacroNames(macros)}.");
        throw new ResolutionException($"No macro named '{nameOrId}'. Known macros: {MacroNames(macros)}.");
    }

    private static string Names(IReadOnlyList<HostAccount> a) => a.Count == 0 ? "(none)" : string.Join(", ", a.Select(x => x.DisplayName));
    private static string MacroNames(IReadOnlyList<MacroInfo> m) => m.Count == 0 ? "(none)" : string.Join(", ", m.Select(x => x.Name));
}
```

- [ ] **Step 5: Run to verify pass.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter NameResolverTests`
Expected: PASS (all six).

- [ ] **Step 6: Commit.**

```bash
git add Resolution/NameResolver.cs tests/rororo-ur-mcp.Tests/Fakes.cs tests/rororo-ur-mcp.Tests/NameResolverTests.cs
git commit -m "feat: name resolution — account/macro name-or-id with ambiguity + unknown errors"
```

---

## Task 5: Account tools — the six account MCP tools

**Files:**
- Create: `Tools/AccountTools.cs`
- Test: `tests/rororo-ur-mcp.Tests/AccountToolsTests.cs`

**Interfaces:**
- Produces MCP tools (`[McpServerTool]`), each taking `IRoRoRoHost` (injected) + caller params, returning a human-readable string: `list_accounts()`, `launch_account(account)`, `launch_into_game(account, game)`, `follow_main(account)`, `follow_friend(account, friendUserId)`, `running_status()`. Errors from `HostUnavailableException`/`ConsentDeniedException`/`ResolutionException` are caught and returned as clear tool-result text (never thrown out of the tool).
- Consumes: `IRoRoRoHost`, `NameResolver`.

- [ ] **Step 1: Write the failing tool tests.** `AccountToolsTests.cs` (call the tool methods directly with a `FakeHost`):

```csharp
using Rororo.UrMcp.Ipc;
using Rororo.UrMcp.Tools;
using Xunit;

public class AccountToolsTests
{
    private static FakeHost HostWith(params HostAccount[] a) => new() { Accounts = a.ToList() };

    [Fact]
    public async Task LaunchAccount_ByName_CallsHostWithResolvedId()
    {
        string? launched = null;
        var host = HostWith(new HostAccount("id-pokey", 111, "Pokey", true));
        host.OnLaunch = id => { launched = id; return new LaunchOutcome(true, null, 42); };

        var result = await AccountTools.launch_account(host, "Pokey", default);

        Assert.Equal("id-pokey", launched);
        Assert.Contains("Pokey", result);
    }

    [Fact]
    public async Task FollowMain_ResolvesMainUid_AndFollows()
    {
        long followed = 0;
        var host = HostWith(new HostAccount("id-main", 999, "Main", true), new HostAccount("id-alt", 1, "Alt", false));
        host.OnFollow = (id, uid) => { followed = uid; return new LaunchOutcome(true, null, 7); };

        var result = await AccountTools.follow_main(host, "Alt", default);

        Assert.Equal(999, followed);
        Assert.Contains("Main", result);
    }

    [Fact]
    public async Task LaunchAccount_UnknownName_ReturnsCandidateList_NotThrow()
    {
        var host = HostWith(new HostAccount("id-pokey", 111, "Pokey", true));
        var result = await AccountTools.launch_account(host, "Ghost", default);
        Assert.Contains("Pokey", result);
        Assert.Contains("Ghost", result);
    }

    [Fact]
    public async Task Tools_WhenHostDown_ReturnFriendlyMessage()
    {
        var host = new FakeHost { Unavailable = true };
        var result = await AccountTools.list_accounts(host, default);
        Assert.Contains("RoRoRo isn't running", result);
    }

    [Fact]
    public async Task FollowMain_NoFollowTargetFailure_SurfacesFailureReason()
    {
        var host = HostWith(new HostAccount("id-main", 999, "Main", true), new HostAccount("id-alt", 1, "Alt", false));
        host.OnFollow = (id, uid) => new LaunchOutcome(false, "friends-only server", 0);
        var result = await AccountTools.follow_main(host, "Alt", default);
        Assert.Contains("friends-only server", result);
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter AccountToolsTests`
Expected: FAIL — `AccountTools` does not exist.

- [ ] **Step 3: Create `Tools/AccountTools.cs`.** Each tool wraps its body in a shared try/catch that turns the typed exceptions into text:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Rororo.UrMcp.Ipc;
using Rororo.UrMcp.Resolution;

namespace Rororo.UrMcp.Tools;

[McpServerToolType]
public static class AccountTools
{
    [McpServerTool, Description("List all saved RoRoRo accounts (name, whether it's the main, and Roblox user id).")]
    public static Task<string> list_accounts(IRoRoRoHost host, CancellationToken ct) => Guard(async () =>
    {
        var accounts = await host.GetAccountsAsync(ct);
        if (accounts.Count == 0) return "No saved accounts in RoRoRo.";
        return string.Join("\n", accounts.Select(a => $"- {a.DisplayName}{(a.IsMain ? " (main)" : "")} [id {a.AccountId}, uid {a.RobloxUserId}]"));
    });

    [McpServerTool, Description("Launch a saved account by name or id.")]
    public static Task<string> launch_account(IRoRoRoHost host,
        [Description("Account name or id")] string account, CancellationToken ct) => Guard(async () =>
    {
        var accounts = await host.GetAccountsAsync(ct);
        var id = NameResolver.ResolveAccount(account, accounts);
        var name = accounts.First(a => a.AccountId == id).DisplayName;
        var r = await host.RequestLaunchAsync(id, ct);
        return r.Ok ? $"Launched {name} (pid {r.ProcessId})." : $"Couldn't launch {name}: {r.FailureReason}";
    });

    [McpServerTool, Description("Launch a saved account directly into a game or private server (share URL or place id).")]
    public static Task<string> launch_into_game(IRoRoRoHost host,
        [Description("Account name or id")] string account,
        [Description("Share URL, private-server link, or place id")] string game, CancellationToken ct) => Guard(async () =>
    {
        var accounts = await host.GetAccountsAsync(ct);
        var id = NameResolver.ResolveAccount(account, accounts);
        var name = accounts.First(a => a.AccountId == id).DisplayName;
        var r = await host.LaunchTargetShareAsync(id, game, ct);
        return r.Ok ? $"Launched {name} into the target (pid {r.ProcessId})." : $"Couldn't launch {name} into the game: {r.FailureReason}";
    });

    [McpServerTool, Description("Launch an account and follow the main account into whatever server it's in.")]
    public static Task<string> follow_main(IRoRoRoHost host,
        [Description("Account name or id that should follow the main")] string account, CancellationToken ct) => Guard(async () =>
    {
        var accounts = await host.GetAccountsAsync(ct);
        var id = NameResolver.ResolveAccount(account, accounts);
        var name = accounts.First(a => a.AccountId == id).DisplayName;
        var main = NameResolver.ResolveMain(accounts);
        var r = await host.LaunchTargetFollowAsync(id, main.RobloxUserId, ct);
        return r.Ok ? $"{name} is following {main.DisplayName} (pid {r.ProcessId})." : $"Couldn't follow {main.DisplayName}: {r.FailureReason}";
    });

    [McpServerTool, Description("Launch an account and follow a specific friend (by Roblox user id) into their server.")]
    public static Task<string> follow_friend(IRoRoRoHost host,
        [Description("Account name or id")] string account,
        [Description("Roblox user id of the friend to follow")] long friendUserId, CancellationToken ct) => Guard(async () =>
    {
        var accounts = await host.GetAccountsAsync(ct);
        var id = NameResolver.ResolveAccount(account, accounts);
        var name = accounts.First(a => a.AccountId == id).DisplayName;
        var r = await host.LaunchTargetFollowAsync(id, friendUserId, ct);
        return r.Ok ? $"{name} is following friend {friendUserId} (pid {r.ProcessId})." : $"Couldn't follow friend {friendUserId}: {r.FailureReason}";
    });

    [McpServerTool, Description("Show which accounts are running and which game each is in.")]
    public static Task<string> running_status(IRoRoRoHost host, CancellationToken ct) => Guard(async () =>
    {
        var running = await host.GetRunningAccountsAsync(ct);
        if (running.Count == 0) return "No accounts are running.";
        var lines = running.Select(a => $"- {a.DisplayName}: {(a.PlaceId == 0 ? "in game (resolving...)" : a.PlaceName)} [pid {a.ProcessId}]");
        var server = await host.GetCurrentServerAsync(ct);
        var footer = server.Present ? $"\nLast private server: {server.PlaceName} — {server.ShareUrl}" : "";
        return string.Join("\n", lines) + footer;
    });

    private static async Task<string> Guard(Func<Task<string>> body)
    {
        try { return await body(); }
        catch (HostUnavailableException) { return "RoRoRo isn't running — open it and try again."; }
        catch (ConsentDeniedException ex) { return $"Consent not granted for {ex.Capability} — grant it in RoRoRo's Plugins window."; }
        catch (ResolutionException ex) { return ex.Message; }
    }
}
```

> `ConsentDeniedException` must expose a `Capability` property (add it in Task 2's `IRoRoRoHost.cs`).

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter AccountToolsTests`
Expected: PASS (all five).

- [ ] **Step 5: Commit.**

```bash
git add Tools/AccountTools.cs tests/rororo-ur-mcp.Tests/AccountToolsTests.cs
git commit -m "feat: account tools — list/launch/launch-into-game/follow-main/follow-friend/running-status"
```

---

## Task 6: Macro tools — the three macro MCP tools

**Files:**
- Create: `Tools/MacroTools.cs`
- Test: `tests/rororo-ur-mcp.Tests/MacroToolsTests.cs`

**Interfaces:**
- Produces MCP tools: `list_macros()`, `run_macro(targets, macro, repeat=false)`, `stop_macro(playbackId?, targets?)`. Each takes `IUrTaskBridge` + `IRoRoRoHost` (for target name→uid resolution) as injected params. Bridge-busy is surfaced verbatim; bridge-down returns "Ur Task isn't available."
- Consumes: `IUrTaskBridge`, `IRoRoRoHost`, `NameResolver`.

- [ ] **Step 1: Write the failing tool tests.** `MacroToolsTests.cs`:

```csharp
using Rororo.UrMcp.Ipc;
using Rororo.UrMcp.Tools;
using Xunit;

public class MacroToolsTests
{
    [Fact]
    public async Task RunMacro_ResolvesMacroName_AndTargets_ToUids()
    {
        string? ranMacroId = null;
        IReadOnlyList<string>? ranTargets = null;
        bool ranRepeat = false;
        var host = new FakeHost { Accounts = { new HostAccount("id-pokey", 111, "Pokey", true), new HostAccount("id-spud", 222, "Spud", false) } };
        var bridge = new FakeBridge { Macros = { new MacroInfo("m-farm", "Farm") } };
        bridge.OnRun = (mid, targets, repeat) => { ranMacroId = mid; ranTargets = targets; ranRepeat = repeat; return new RunMacroResult(true, "pb-9", null, null); };

        var result = await MacroTools.run_macro(bridge, host, new[] { "Pokey", "Spud" }, "Farm", true, default);

        Assert.Equal("m-farm", ranMacroId);
        Assert.Equal(new[] { "111", "222" }, ranTargets);   // resolved to Roblox uids as decimal strings
        Assert.True(ranRepeat);
        Assert.Contains("pb-9", result);
    }

    [Fact]
    public async Task RunMacro_BridgeBusy_SurfacesReasonVerbatim()
    {
        var host = new FakeHost { Accounts = { new HostAccount("id-pokey", 111, "Pokey", true) } };
        var bridge = new FakeBridge { Macros = { new MacroInfo("m-farm", "Farm") } };
        bridge.OnRun = (mid, t, r) => new RunMacroResult(false, null, "busy", "A sequence is already running.");
        var result = await MacroTools.run_macro(bridge, host, new[] { "Pokey" }, "Farm", false, default);
        Assert.Contains("busy", result);
        Assert.Contains("already running", result);
    }

    [Fact]
    public async Task ListMacros_WhenBridgeDown_ReturnsFriendlyMessage()
    {
        var result = await MacroTools.list_macros(new FakeBridge { Unavailable = true }, default);
        Assert.Contains("Ur Task isn't available", result);
    }

    [Fact]
    public async Task StopMacro_ByPlaybackId_ReturnsStoppedCount()
    {
        var result = await MacroTools.stop_macro(new FakeBridge(), new FakeHost(), "pb-1", null, default);
        Assert.Contains("Stopped", result);
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter MacroToolsTests`
Expected: FAIL — `MacroTools` does not exist.

- [ ] **Step 3: Create `Tools/MacroTools.cs`:**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using Rororo.UrMcp.Ipc;
using Rororo.UrMcp.Resolution;

namespace Rororo.UrMcp.Tools;

[McpServerToolType]
public static class MacroTools
{
    [McpServerTool, Description("List the Ur Task macros available to run.")]
    public static Task<string> list_macros(IUrTaskBridge bridge, CancellationToken ct) => Guard(async () =>
    {
        var macros = await bridge.ListMacrosAsync(ct);
        return macros.Count == 0 ? "No macros recorded in Ur Task."
            : string.Join("\n", macros.Select(m => $"- {m.Name} [id {m.Id}]"));
    });

    [McpServerTool, Description("Run an Ur Task macro on one or more accounts (by name/id), optionally on repeat until stopped.")]
    public static Task<string> run_macro(IUrTaskBridge bridge, IRoRoRoHost host,
        [Description("Account names or ids to run the macro on; or [\"foreground\"] for the focused window")] string[] targets,
        [Description("Macro name or id")] string macro,
        [Description("Loop the macro until stopped")] bool repeat,
        CancellationToken ct) => Guard(async () =>
    {
        var macros = await bridge.ListMacrosAsync(ct);
        var macroId = NameResolver.ResolveMacro(macro, macros);

        IReadOnlyList<string> resolvedTargets;
        if (targets.Length == 1 && string.Equals(targets[0], "foreground", StringComparison.OrdinalIgnoreCase))
            resolvedTargets = new[] { "foreground" };
        else
        {
            var accounts = await host.GetAccountsAsync(ct);
            resolvedTargets = targets.Select(t =>
            {
                var id = NameResolver.ResolveAccount(t, accounts);
                return accounts.First(a => a.AccountId == id).RobloxUserId.ToString();
            }).ToList();
        }

        var r = await bridge.RunMacroAsync(macroId, resolvedTargets, repeat, ct);
        if (!r.Ok) return $"Macro refused ({r.Reason}): {r.Detail}";
        return $"Running '{macro}' on {resolvedTargets.Count} target(s){(repeat ? " on repeat" : "")}. Playback id: {r.PlaybackId}.";
    });

    [McpServerTool, Description("Stop a running macro playback — by playback id, or all if none given.")]
    public static Task<string> stop_macro(IUrTaskBridge bridge, IRoRoRoHost host,
        [Description("Playback id to stop; omit to stop all")] string? playbackId,
        [Description("Account names/ids to stop (reserved; ignored while single-flight)")] string[]? targets,
        CancellationToken ct) => Guard(async () =>
    {
        var r = await bridge.StopMacroAsync(playbackId, targets, ct);
        return r.Ok ? $"Stopped {r.Stopped} playback(s)." : $"Couldn't stop: {r.Reason}";
    });

    private static async Task<string> Guard(Func<Task<string>> body)
    {
        try { return await body(); }
        catch (BridgeUnavailableException) { return "Ur Task isn't available — is it installed and running in RoRoRo?"; }
        catch (HostUnavailableException) { return "RoRoRo isn't running — open it and try again."; }
        catch (ResolutionException ex) { return ex.Message; }
    }
}
```

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter MacroToolsTests`
Expected: PASS (all four).

- [ ] **Step 5: Commit.**

```bash
git add Tools/MacroTools.cs tests/rororo-ur-mcp.Tests/MacroToolsTests.cs
git commit -m "feat: macro tools — list/run(repeat)/stop, targets resolved to Roblox uids"
```

---

## Task 7: MCP server wiring verification + full unit run

**Files:**
- Verify: `Program.cs` (both clients registered, tools discovered)

- [ ] **Step 1: Confirm DI + tool discovery.** Ensure `Program.cs` registers both `IRoRoRoHost`→`RoRoRoHostClient` and `IUrTaskBridge`→`UrTaskBridgeClient` as singletons, and `WithToolsFromAssembly()` is present. Build:

Run: `dotnet build rororo-ur-mcp.csproj`
Expected: `Build succeeded`.

- [ ] **Step 2: Full unit suite green.**

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter "Category!=Integration"`
Expected: all PASS (FrameCodec, NameResolver, AccountTools, MacroTools).

- [ ] **Step 3: MCP inspector smoke (manual).** Run the server and confirm all 9 tools enumerate with their descriptions:

```bash
npx @modelcontextprotocol/inspector dotnet run --project rororo-ur-mcp.csproj
```
Expected: `list_accounts, launch_account, launch_into_game, follow_main, follow_friend, running_status, list_macros, run_macro, stop_macro` all listed. (RoRoRo need not be running to *list* tools; calling them without RoRoRo returns the friendly "RoRoRo isn't running" strings.)

- [ ] **Step 4: Commit (if any wiring changed).**

```bash
git add Program.cs
git commit -m "chore: wire both IPC clients into MCP DI; verify 9-tool surface"
```

---

## Task 8: Integration test against real pipes (gated) + manual recovery smoke

**Files:**
- Create: `tests/rororo-ur-mcp.Tests/IpcIntegrationTests.cs`

**Interfaces:**
- Consumes: real `RoRoRoHostClient` + `UrTaskBridgeClient` against live pipes. Marked `[Trait("Category","Integration")]` so the default unit run skips them (no RoRoRo dependency in CI).

- [ ] **Step 1: Add the integration tests** (run manually with RoRoRo + Ur Task open, the connector installed+consented):

```csharp
using Rororo.UrMcp.Ipc;
using Xunit;

[Trait("Category", "Integration")]
public class IpcIntegrationTests
{
    [Fact]
    public async Task Host_GetAccounts_OverRealPipe()
    {
        using var host = new RoRoRoHostClient();
        var accounts = await host.GetAccountsAsync(default);
        Assert.NotNull(accounts); // with at least one saved account, expect >= 1
    }

    [Fact]
    public async Task Bridge_ListMacros_OverRealPipe()
    {
        var bridge = new UrTaskBridgeClient();
        var macros = await bridge.ListMacrosAsync(default);
        Assert.NotNull(macros);
    }
}
```

- [ ] **Step 2: Run the gated integration tests manually** (RoRoRo + Ur Task running, connector installed+consented):

Run: `dotnet test tests/rororo-ur-mcp.Tests/ --filter "Category=Integration"`
Expected: PASS against the live host + bridge. If `Host_GetAccounts` throws `HostUnavailableException`, RoRoRo isn't running or the plugin isn't installed/consented.

- [ ] **Step 3: Manual recovery-scenario smoke via Claude Code** (spec §1 north star). Add the server: `claude mcp add rororo -- <path to rororo-ur-mcp exe>`, then drive: "launch Pokey, Spud, Clover" → "run the get-in-position macro on all three" → "run the farm macro on repeat" → "stop everything." Confirm each maps to the right tool and RoRoRo/Ur Task act. Record the result in `README.md`'s "verified" note.

- [ ] **Step 4: Commit.**

```bash
git add tests/rororo-ur-mcp.Tests/IpcIntegrationTests.cs README.md
git commit -m "test: gated real-pipe integration + recovery-scenario smoke notes"
```

---

## Task 9: Brand + docs — icon via design skill, README, Claude setup

**Files:**
- Create: `Assets/` icon set, `README.md` (full)

- [ ] **Step 1: Generate the icon set via the `626labs-design` skill** — no programmatic placeholder. Brand cyan `#17d4fa` + magenta `#f22f89`, navy `#0f1f31` field. Produce the plugin icon at the sizes RoRoRo's marketplace requires (match the Ur Task / Ur OCR icon conventions in their repos).

- [ ] **Step 2: Write `README.md`** — clan-warm where it explains setup, builder-to-builder elsewhere. Cover: what it does (drive RoRoRo from Claude), the recovery scenario, install (marketplace → consent sheet → autostart off), Claude Code (`claude mcp add rororo -- <exe>`) + Claude Desktop (`mcpServers` stdio entry), and the "RoRoRo must be running" note. State plainly that macros run through Ur Task's consent, and that the connector never types on its own.

- [ ] **Step 3: Final full build + unit run.**

Run: `dotnet build rororo-ur-mcp.csproj && dotnet test tests/rororo-ur-mcp.Tests/ --filter "Category!=Integration"`
Expected: build succeeds, all unit tests pass.

- [ ] **Step 4: Commit.**

```bash
git add Assets/ README.md
git commit -m "docs: brand icon set (design skill) + README with Claude setup + recovery scenario"
```

> **Ship step (outside this plan):** add the `626labs.ur-mcp` entry to `docs/store/plugins-catalog.json` in the RoRoRo repo, cut a GitHub release with `manifest.json` + `manifest.sha256` + `plugin.zip`, and update ur-task's catalog `latestVersion` to 0.6.0.

---

## Self-Review

**Spec coverage** — §3 three units (host client / bridge client / stdio MCP server) → Tasks 2 / 3 / 1+7. §4 all nine tools → Tasks 5 (six account) + 6 (three macro). §4 name→id resolution → Task 4, used in Tasks 5-6. §4 `follow_main` resolves main's uid from `GetAccounts` → Task 5 `follow_main` + `NameResolver.ResolveMain`. §6 consent posture (host hard-gate returns `PERMISSION_DENIED` → surfaced; macro via Ur Task's own consent) → `ConsentDeniedException` mapping (Task 2) + `Guard` (Task 5). §7 setup UX → Task 9 README + Task 8 `claude mcp add`. §8 every error row → `Guard` handlers + `NameResolver` errors (Tasks 4-6). §9 fake-based unit tests + real-pipe integration + inspector smoke → Tasks 4-8. §2 stdio, Claude-launched, autostart off → manifest (Task 1) + Program stdio (Task 1).

**Placeholder scan** — the deferred specifics are (a) the exact `ModelContextProtocol` fluent-API method names and NuGet version, flagged to confirm at build (greenfield SDK), and (b) the PluginContract NuGet-vs-ProjectReference fallback — both are explicit decisions, not silent TODOs. All tool + resolver + codec code is complete and test-backed.

**Type consistency** — `HostAccount(AccountId, RobloxUserId, DisplayName, IsMain)` matches Plan 1's `SavedAccount` fields and is used identically across `IRoRoRoHost`, `FakeHost`, `NameResolver`, and both tool files. `MacroInfo(Id, Name)` matches Plan 2's `MacroSummary`. `run_macro` resolves account targets to **Roblox uid decimal strings** — matching the ur-task bridge's `Targets` contract ("decimal user-ids, or [\"foreground\"]"). Tool method names match the spec §4 table exactly (`list_accounts` … `stop_macro`). `ConsentDeniedException.Capability` referenced in Task 5 is defined in Task 2.

**Cross-plan dependency** — Tasks 2-3 require Plan 1 (PluginContract 0.5.0 with `GetAccounts`) and Plan 2 (ur-task 0.6.0 bridge methods) published first. Tasks 1, 4, 5, 6 (scaffold + all logic against fakes) can proceed before those land; only the real-client integration (Tasks 2 real calls, 3 real calls, 8) needs them live.
