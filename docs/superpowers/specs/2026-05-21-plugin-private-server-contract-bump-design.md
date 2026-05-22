# Plugin contract bump — private-server launch + share (v0.2.0)

> **Status:** design / outline for execution. **Date:** 2026-05-21.
> **Driver:** the `626labs.clan-link` Discord clan-coordination plugin needs to (a) launch an alt into a private-server link it received, and (b) read the private-server link the user is currently in so it can share it. Neither is reachable through the v1.4 plugin contract. This spec is the host-side (RoRoRo) work that unblocks it.

## Why this is needed (the wall)

The plugin's headline flow — *member B one-click-joins the exact private server member A shared* — cannot ship on the v0.1.0 contract:

- **`RequestLaunch` carries only `account_id`** (`src/ROROROblox.PluginContract/Protos/plugin_contract.proto:105`, `// future: place_id ... (not v1)`). It fires the account's *default* launch (`MainViewModelLaunchInvokerAdapter.cs:73` → `LaunchAccountCommand`). No slot for a server target.
- **A plugin can't build the launch itself.** The authenticated launch needs the account's plaintext `.ROBLOSECURITY` cookie → auth-ticket (CSRF dance in `RobloxLauncher`/`IRobloxApi.GetAuthTicketAsync`). The cookie is DPAPI-encrypted, host-only (`IAccountStore.RetrieveCookieAsync`), never exposed to a plugin process.
- **The outbound share target never reaches the plugin.** `AccountLaunchedEvent` (`plugin_contract.proto:86`) has no launch URI / place id / access code. `ServerShareExtractor` (the branch's extractor) needs the launch URI as input, which only the host sees.
- **The single-instance pipe is not a carrier.** `SingleInstanceGuard` (`AppLifecycle/SingleInstanceGuard.cs`) sends a hardcoded `"SHOW"` and forwards no args. Ruled out as a no-contract-change path.

So the launch + the share both require host seams. A contract bump is the path.

## What the host already has (the good news)

The launch machinery is fully landed on HEAD (`v1.7.0-install-deferral`) — this spec mostly *exposes* it:

- `LaunchTarget.PrivateServer(long PlaceId, string Code, PrivateServerCodeKind Kind)` + `LaunchTarget.FromUrl(string)` + `LaunchTarget.TryParseShareLink(...)` (`src/ROROROblox.Core/LaunchTarget.cs`).
- `MainViewModel.ResolveShareUrlAsync(url)` — resolves all three link forms (legacy `privateServerLinkCode=`, `accessCode=` launcher URIs, and the new `roblox.com/share?code=` token via `IRobloxApi.ResolveShareLinkAsync`) into a `LaunchTarget`.
- `MainViewModel.LaunchAccountAsync(summary, overrideTarget)` — single-account launch into a target. `SquadLaunchAsync(target)` — all-eligible. Both retrieve the per-account cookie and call `IRobloxLauncher.LaunchAsync(cookie, target, fpsCap)`.
- `IPrivateServerStore.ListAsync()` → `SavedPrivateServer { PlaceId, Code, CodeKind, PlaceName, RenderName, LastLaunchedAt, ... }`. Sorting by `LastLaunchedAt` desc yields "the server I'm in" (`SquadLaunchWindow.xaml.cs:89`).

The plugin treats a private-server link as an **opaque string**; the host owns all parsing, share-link resolution, cookie handling, and launching.

## The two seams

### Seam A — inbound: launch an alt into a link (plugin → host)

New RPC. Plugin passes `account_id` + a private-server `share_url` (any form). Host resolves it with the *existing* `ResolveShareUrlAsync` and launches via the *existing* `LaunchAccountAsync(summary, target)`.

### Seam B — outbound: read the current private-server link (plugin → host)

New RPC. Returns the most-recently-launched saved private server as a ready-to-post share URL + metadata. Plugin posts it to the clan Discord channel.

Single-account first (mirrors `RequestLaunch`). "Launch all eligible into the link" (squad-all) is an additive follow-up RPC if demand surfaces — keep v0.2.0 tight.

## Proto changes — `src/ROROROblox.PluginContract/Protos/plugin_contract.proto`

Add to the `RoRoRoHost` service:

```proto
  // Command surface (v1.1 additive)
  rpc RequestLaunchByLink(LaunchByLinkRequest) returns (LaunchResult);
  // Query surface (v1.1 additive)
  rpc GetCurrentPrivateServer(Empty) returns (CurrentPrivateServer);
```

Add messages:

```proto
message LaunchByLinkRequest {
  string account_id = 1;   // which alt to launch
  string share_url = 2;    // any Roblox private-server link form; the host resolves it
}

message CurrentPrivateServer {
  bool present = 1;                    // false when the store has no launched private server
  string share_url = 2;               // ready-to-post shareable link
  string place_name = 3;
  int64  place_id = 4;
  int64  last_launched_at_unix_ms = 5;
}
```

Notes:
- **Additive only** — new RPCs + new messages are proto3 wire-compatible. Existing plugins (`hello-plugin`, `rororo-ur-task`) are untouched.
- Keeping the share target as a URL string (not a structured `place_id/code/kind`) means the host owns parsing/resolution end to end and the contract stays small. The receiving plugin pastes whatever Seam B handed it straight into Seam A.

## Capabilities — `src/ROROROblox.App/Plugins/PluginCapability.cs`

Add two constants + catalog entries (the catalog text is the consent-sheet disclosure — write it honestly, this is a powerful grant):

```csharp
public const string HostCommandsLaunchByLink = "host.commands.launch-by-link";
public const string HostQueriesCurrentPrivateServer = "host.queries.current-private-server";

// Catalog:
[HostCommandsLaunchByLink] =
    "Allow the plugin to launch one of your accounts into a Roblox server from a link it provides.",
[HostQueriesCurrentPrivateServer] =
    "Allow the plugin to read the private-server link you most recently launched, so it can share it.",
```

`launch-by-link` is a distinct capability from `host.commands.request-launch` on purpose: launching into an arbitrary, externally-supplied server is more powerful and more privacy-sensitive than launching an account into its own default. Don't fold them.

## Capability map — `src/ROROROblox.App/Plugins/RpcMethodCapabilityMap.cs`

```csharp
["RequestLaunchByLink"] = PluginCapability.HostCommandsLaunchByLink,
["GetCurrentPrivateServer"] = PluginCapability.HostQueriesCurrentPrivateServer,
```

The `CapabilityInterceptor` gates them automatically from this map.

## Invoker — `src/ROROROblox.App/Plugins/IPluginLaunchInvoker.cs`

Extend the seam (or add a sibling interface `IPluginPrivateServerInvoker` if you want to keep launch vs query separated — recommended for single-responsibility):

```csharp
Task<(bool ok, string? failureReason, int processId)> RequestLaunchByLinkAsync(string accountId, string shareUrl);
Task<CurrentPrivateServerInfo?> GetCurrentPrivateServerAsync(); // host-internal DTO, mapped to proto in the service
```

## Adapter — `src/ROROROblox.App/Plugins/Adapters/MainViewModelLaunchInvokerAdapter.cs`

The adapter already holds `MainViewModel`, which has `_privateServerStore`, `ResolveShareUrlAsync`, and `LaunchAccountAsync`. Implementation:

- **`RequestLaunchByLinkAsync(accountId, shareUrl)`**
  1. Reuse the existing GUID-parse + `Accounts.FirstOrDefault` + eligibility checks (expired / launching / running) from `RequestLaunchAsync`.
  2. `var target = await _vm.ResolveShareUrlAsync(shareUrl);` — reject `(false, "Couldn't read that as a server link.", 0)` if `target is not LaunchTarget.PrivateServer` (and decide whether to allow `LaunchTarget.Place` for public-game "come find me").
  3. Marshal to the WPF dispatcher (same pattern as today) and call `await _vm.LaunchAccountAsync(summary, overrideTarget: target);`.
  4. Return `(true, null, 0)` — PID arrives later via `SubscribeAccountLaunched`, same as `RequestLaunch`.

  *Expose `LaunchAccountAsync` / a thin internal launch method to the adapter if it's currently private — keep the public surface minimal.*

- **`GetCurrentPrivateServerAsync()`**
  1. `var servers = await _vm.PrivateServerStore.ListAsync();` (expose the store read).
  2. `servers.Where(s => s.LastLaunchedAt is not null).OrderByDescending(s => s.LastLaunchedAt)` → first, or null.
  3. Build the shareable URL from `PlaceId` + `Code` + `CodeKind` (reuse whatever produces a `roblox.com/share` or `privateServerLinkCode=` URL — add a `SavedPrivateServer.ToShareUrl()` helper in Core if one doesn't exist).
  4. Map to the proto `CurrentPrivateServer` in `PluginHostService`.

## Service — `src/ROROROblox.App/Plugins/PluginHostService.cs`

Add the two overrides next to `RequestLaunch`:

```csharp
public override async Task<LaunchResult> RequestLaunchByLink(LaunchByLinkRequest request, ServerCallContext context)
{
    var (ok, reason, pid) = await _launcher.RequestLaunchByLinkAsync(request.AccountId, request.ShareUrl).ConfigureAwait(false);
    return new LaunchResult { Ok = ok, FailureReason = reason ?? string.Empty, ProcessId = pid };
}

public override async Task<CurrentPrivateServer> GetCurrentPrivateServer(Empty request, ServerCallContext context)
{
    var info = await _launcher.GetCurrentPrivateServerAsync().ConfigureAwait(false);
    if (info is null) return new CurrentPrivateServer { Present = false };
    return new CurrentPrivateServer
    {
        Present = true,
        ShareUrl = info.ShareUrl,
        PlaceName = info.PlaceName,
        PlaceId = info.PlaceId,
        LastLaunchedAtUnixMs = info.LastLaunchedAtUnixMs,
    };
}
```

Capability gates are enforced upstream by the interceptor — bodies assume consent passed.

## Consent sheet

No code change beyond capability registration — `ConsentSheet` renders from the catalog. But review the disclosure copy with care: `launch-by-link` lets a plugin drive your account into a server someone else picked. That sentence must be unmistakable on the sheet.

## Versioning

- **NuGet `ROROROblox.PluginContract` 0.1.0 → 0.2.0** (additive: new RPCs + new capabilities = minor bump, per the AUTHOR_GUIDE versioning policy).
- **`contractVersion` handshake string stays `"1.0"`.** The handshake is an exact string match (`PluginHostService.cs:61`); bumping it to `"1.1"` would reject every existing plugin until rebuilt. Proto additions are wire-compatible and the new surface is capability-gated, so old plugins keep working and the new plugin simply declares the new capabilities. **Do not bump the handshake string** unless a future change is wire-breaking.
- Update `AUTHOR_GUIDE.md` capability tables + `starter/README.md` cheat sheet with the two new capabilities once landed.

## Test plan (TDD — mirror the existing plugin host tests)

- `RpcMethodCapabilityMapTests` — the two new methods map to the two new capabilities; unknown still null.
- `MainViewModelLaunchInvokerAdapterTests` (or new) — `RequestLaunchByLinkAsync` rejects bad GUID / missing account / expired / running; resolves a `privateServerLinkCode=` URL to a `PrivateServer` target and dispatches `LaunchAccountAsync` with it; rejects an unparseable URL. `GetCurrentPrivateServerAsync` returns the most-recently-launched server and null on empty store.
- `PluginTestHarness` end-to-end — a consented plugin calls `RequestLaunchByLink` over the pipe and the host resolves + dispatches; an unconsented plugin gets `PermissionDenied`; `GetCurrentPrivateServer` returns the seeded store's newest entry.
- Reuse the branch's `ServerShareExtractor` fixtures for URL-shape coverage if the share-URL builder needs round-trip tests.

## Out of scope (this spec)

- Squad-all launch-by-link RPC (additive follow-up).
- Host-side rendering of plugin UI (`host.ui.*` still stubbed; the plugin owns its own window/tray).
- The `626labs.clan-link` plugin itself — separate spec; this spec only opens the seams it consumes.
- Hub / clan-battle roster assignment (separate platform decision).

## What this unlocks (the consuming plugin, brief)

Once these land, `626labs.clan-link` is a thin Discord bridge:
- **Outbound:** user clicks "Share to clan" in the plugin's own window → plugin calls `GetCurrentPrivateServer` → posts the `share_url` to the clan Discord channel (webhook or bot).
- **Inbound:** member B's plugin sees the post (bot) / user clicks Join → plugin calls `RequestLaunchByLink(accountId, shareUrl)` → B's alt lands in A's server.
- Presence + "N accounts active" broadcasts port directly from the branch off `host.events.account-launched/-exited` — no new host work.

---

**A 626 Labs product · *Imagine Something Else*.**
