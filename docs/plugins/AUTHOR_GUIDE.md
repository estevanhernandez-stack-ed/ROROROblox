# RoRoRo Plugin Author Guide

> **Audience:** developers who want to write a plugin that runs alongside RoRoRo on Windows. **Status:** v1.4 contract (`ROROROblox.PluginContract` v0.1.0). The contract will receive a major bump if it changes incompatibly; minor bumps for additive changes.

## What a plugin is

A plugin is **a separately distributed Windows EXE** that connects to RoRoRo over a per-user named pipe (`\\.\pipe\rororo-plugin-host`) on RoRoRo's gRPC server, exchanges typed messages, and contributes UI surfaces back to RoRoRo. It is its own product — not bundled into RoRoRo's installer, not auto-fetched, never loaded into RoRoRo's process.

Why this shape: Microsoft Store policy 10.2.2 forbids dynamic inclusion of code that changes the described functionality of a Store-listed app. RoRoRo ships through the Store; plugins ship through GitHub releases. The wall is intentional and load-bearing for RoRoRo's Store eligibility.

## What you need

- **Visual Studio 2022 17.13+ or `dotnet` SDK 10.0+** (any C# IDE works).
- **Windows 11.** Plugins are .NET on Windows; the Mac sibling (`rororo-mac`) has its own track.
- **`ROROROblox.PluginContract` NuGet** (v0.1.0+) — pulled into your project. The package ships the `.proto` definitions + generated C# bindings for both the host-side and plugin-side service.

## Project setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ROROROblox.PluginContract" Version="0.1.0" />
    <PackageReference Include="Grpc.Net.Client" Version="2.70.0" />
  </ItemGroup>
</Project>
```

Reference the contract NuGet, take a dependency on `Grpc.Net.Client` for the wire transport, and you're set.

## Connect — minimal handshake

```csharp
using System.IO.Pipes;
using Grpc.Core;
using Grpc.Net.Client;
using ROROROblox.PluginContract;

const string PluginId = "yourcompany.your-plugin";
const string PipeName = "rororo-plugin-host";

using var channel = GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler
    {
        ConnectCallback = async (ctx, ct) =>
        {
            var pipe = new NamedPipeClientStream(".", PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(ct);
            return pipe;
        },
    },
});

var client = new RoRoRoHost.RoRoRoHostClient(channel);

// REQUIRED on every call: the x-plugin-id header. RoRoRo's CapabilityInterceptor
// reads this header to identify which plugin is calling and looks up its consent.
// Calls without this header throw FailedPrecondition on every gated RPC
// (Subscribe*, RequestLaunch, AddTrayMenuItem/RowBadge/StatusPanel).
var headers = new Metadata { { "x-plugin-id", PluginId } };
var callOptions = new CallOptions(headers: headers);

var handshake = await client.HandshakeAsync(new HandshakeRequest
{
    PluginId = PluginId,
    ContractVersion = "1.0",
}, callOptions);

if (!handshake.Accepted)
{
    Console.Error.WriteLine($"Rejected: {handshake.RejectReason}");
    return;
}

Console.WriteLine($"Connected to RoRoRo {handshake.HostVersion}.");
```

The handshake is the FIRST call after the pipe connects. If RoRoRo doesn't recognize your plugin id (you haven't been installed) or rejects your contract version, every subsequent RPC fails. Surface the reject reason — your dev cycle is faster.

**The `x-plugin-id` header is non-negotiable** — every call needs it (handshake included for consistency, gated calls require it). Pass `callOptions` (unary) or `headers` (streaming) on each invocation, or wrap the client in a custom `CallInvoker` that injects it automatically.

## Capability vocabulary

Plugins declare capabilities in their manifest. Each capability falls into one of two namespaces:

### `host.*` — what you ask RoRoRo for

Gated by the gRPC interceptor. If you call a method whose required capability isn't granted, you get `RpcException(StatusCode.PermissionDenied)`.

| Capability | Lets you call |
|---|---|
| `host.events.account-launched` | `SubscribeAccountLaunched` (server-streaming) |
| `host.events.account-exited` | `SubscribeAccountExited` |
| `host.events.mutex-state-changed` | `SubscribeMutexStateChanged` |
| `host.commands.request-launch` | `RequestLaunch(accountId)` — ask RoRoRo to launch an alt |
| `host.ui.tray-menu` | `AddTrayMenuItem` — contribute a tray-menu entry |
| `host.ui.row-badge` | `AddRowBadge` — paint a per-account badge in RoRoRo's main window |
| `host.ui.status-panel` | `AddStatusPanel` — contribute a status panel pane |

The following are **ungated** — every plugin can call them after handshake:
`Handshake`, `GetHostInfo`, `GetRunningAccounts`, `UpdateUI`, `RemoveUI`.

### `system.*` — what you do locally on the user's machine

Disclosure-only. RoRoRo can't sandbox your process — you're a separate EXE. `system.*` capabilities are how you tell users honestly what they're consenting to.

| Capability | What it discloses |
|---|---|
| `system.synthesize-keyboard-input` | You synthesize keyboard input system-wide |
| `system.synthesize-mouse-input` | You synthesize mouse input system-wide |
| `system.watch-global-input` | You read keyboard / mouse globally |
| `system.prevent-sleep` | You keep the user's PC from sleeping while you run |
| `system.focus-foreign-windows` | You activate / focus other applications' windows |
| `system.read-screen` | You capture and read pixels from the user's screen |

Declare every `system.*` capability that matches your behavior. Honest disclosure is the contract — users are reading it on the consent sheet to decide whether to trust you.

## Manifest format

Sit `manifest.json` next to your EXE in the install root (`%LOCALAPPDATA%\ROROROblox\plugins\<plugin-id>\`):

```json
{
  "schemaVersion": 1,
  "id": "yourcompany.your-plugin",
  "name": "Your Plugin",
  "version": "0.1.0",
  "contractVersion": "1.0",
  "publisher": "Your Company LLC",
  "description": "One-sentence pitch — shown on the consent sheet.",
  "capabilities": [
    "host.events.account-launched",
    "host.ui.tray-menu",
    "system.synthesize-keyboard-input"
  ],
  "icon": "icon.png",
  "updateFeed": "https://github.com/yourcompany/your-plugin/releases.atom"
}
```

**`id` rules:** reverse-DNS form, lowercase letters / digits / hyphens, at least one dot. Regex: `^[a-z0-9]+(\.[a-z0-9-]+)+$`. RoRoRo rejects anything else at install time.

**`schemaVersion`** is currently `1`. Future versions will bump if the manifest shape changes.

**`contractVersion`** is the gRPC contract version (currently `"1.0"`). RoRoRo's handshake rejects mismatched contract versions cleanly with a clear error.

**`updateFeed`** is optional — point at your GitHub Releases atom feed if you want to wire your own update flow (Velopack on the plugin side is fine).

## Distribution — GitHub release shape

Your release tag is whatever; the three artifacts are fixed:

```
manifest.json          — your manifest, exactly as above
manifest.sha256        — single-line lowercase SHA-256 of plugin.zip
plugin.zip             — your build output (EXE + manifest.json + icon + any deps)
```

The SHA pin is non-negotiable. RoRoRo's installer fetches `manifest.json` first, then `manifest.sha256`, then `plugin.zip` — and refuses to extract if the actual SHA-256 doesn't match.

The user pastes the **directory URL** (the parent path that contains those three artifacts) into RoRoRo → Plugins → Install. RoRoRo appends `/manifest.json`, `/manifest.sha256`, `/plugin.zip` to the URL. If your release lives at `https://github.com/yourcompany/your-plugin/releases/download/v0.1.0/`, that's the URL.

## Trust + signing recommendation

RoRoRo doesn't enforce Authenticode signing on plugins in v1.4. **You should still sign your EXE.** Reasoning:

- Windows SmartScreen treats unsigned EXEs as suspicious. Even with sideload-via-RoRoRo, a Defender alert on first run hurts your install rate.
- Any plugin that declares `system.synthesize-keyboard-input` or `system.watch-global-input` will trigger Windows Security heuristics. A signed binary survives the heuristic; an unsigned one gets quarantined.
- Future RoRoRo versions may allowlist signed publishers. Better to be signed from day one than retrofit later.

626 Labs plugins (auto-keys when it lands) sign with a 626 Labs LLC code-signing cert that's separate from the RoRoRo Store cert. You'll want your own cert (or a 626 Labs LLC cert if you're a 626 Labs plugin author) — don't share keys.

## What you can NOT do (yet)

- **Mutate RoRoRo state** — write to favorites / accounts / session-history stores. Plugins observe + add UI + trigger launches; they cannot edit. Future v2 conversation if the demand surfaces.
- **Mid-stream consent revocation** — when a user revokes a capability you've already used to subscribe to events, v1.4 doesn't kill the open stream. Your plugin keeps receiving events until you reconnect or RoRoRo restarts. v1.5+ will close the stream within a 5s grace window. Don't depend on stream-cancel-on-revoke for v1.4.
- **In-process load** — there's no AssemblyLoadContext path. Out-of-process is the only architecture. This is permanent — Store policy 10.2.2 makes the alternative ineligible for the Store.
- **Bidirectional UI events** — the v1.4 contract has `Plugin.OnUIInteraction` defined in the proto, but RoRoRo doesn't currently push interactions back to plugins. Click handling on plugin-contributed tray items is a stub. v1.5+ work.

## Recipes

### Subscribe to account launches + react

```csharp
using var stream = client.SubscribeAccountLaunched(new SubscriptionRequest(), headers);
await foreach (var evt in stream.ResponseStream.ReadAllAsync())
{
    Console.WriteLine($"{evt.DisplayName} (uid {evt.RobloxUserId}) launched, pid {evt.ProcessId}");
    // your react logic here
}
```

Streaming RPCs take `Metadata` directly (not `CallOptions`). Pass the same `headers` from the connect example.

The stream stays open as long as the connection is alive. RoRoRo uses bounded channels with `DropOldest` on the host side — if your plugin is too slow to read, you'll silently miss old events while the freshest ones still arrive. Don't sleep inside the consumer loop.

### Add a tray menu item

```csharp
var handle = await client.AddTrayMenuItemAsync(new MenuItemSpec
{
    Label = "Toggle automation",
    Tooltip = "Pauses or resumes the cycler.",
    Enabled = true,
}, callOptions);
// stash handle.Id if you want to later UpdateUI or RemoveUI it
```

The handle id is RoRoRo's — opaque to you. Pass it back when you call `UpdateUI` or `RemoveUI`. RoRoRo enforces that only the plugin that added a UI element can update or remove it.

### Ask RoRoRo to launch an account

```csharp
var result = await client.RequestLaunchAsync(new LaunchRequest
{
    AccountId = "00000000-0000-0000-0000-000000000001", // RoRoRo's internal Guid
}, callOptions);
if (!result.Ok)
{
    Console.Error.WriteLine($"Launch failed: {result.FailureReason}");
}
```

Account ids come from `GetRunningAccounts()` (already-running) or via `SubscribeAccountLaunched` events. There's no "list all saved accounts" RPC in v1.4 — by design, plugins shouldn't enumerate the user's full account inventory.

## Versioning policy (provisional)

- `ROROROblox.PluginContract` follows semver. Breaking changes to method signatures or message shapes bump the major.
- New capabilities (additive) bump the minor.
- RoRoRo's host-side handshake check rejects `contractVersion` mismatches strictly — there is no auto-negotiation in v1.4.
- The contract version is independent from RoRoRo's app version. RoRoRo 1.4 ships contract 1.0; RoRoRo 1.5 might still ship contract 1.0, or might bump to 1.1 — track the NuGet version, not the app version.

## Where to ask

- **Issues / feature asks:** open an issue at [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox).
- **Reference implementation:** the auto-keys plugin (lands in `rororo-autokeys-plugin` in a follow-up sprint) is the canonical first example. Check there once it ships.
- **Security disclosures:** see RoRoRo's main repo's security policy. Plugin-side bugs that affect host-process security are taken seriously even though plugins are sideload-only.

---

**A 626 Labs product · *Imagine Something Else*.**
