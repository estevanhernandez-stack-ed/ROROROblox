# RoRoRo Plugin System — Design Spec

> **Canonical spec.** When build reality drifts from this doc, banner-correct at the top of this file (pattern v from Vibe Thesis) — name what was originally proposed vs what was actually built. Do NOT rewrite top-to-bottom; that destroys /reflect-time framing.

**Date:** 2026-05-09
**Status:** Accepted (brainstorming complete; ready for implementation plan)
**Companion:** [`docs/store/release-playbook.md`](../../store/release-playbook.md), [`docs/port-reference/auto-keys/v1/`](../../port-reference/auto-keys/v1/)
**Cycle:** v1.4 plugin system; auto-keys plugin lands in v1.5+ (separate sprint, sibling repo)

---

## Why this exists

RoRoRo Windows ships through the Microsoft Store (ID `9NMJCS390KWB`) and a Setup.exe / sideload MSIX. The Pet Sim 99 clan audience is asking for macro / AFK-defeat features; the macOS sibling has shipped these as auto-keys (Slope C wave 3c — see [`docs/port-reference/auto-keys/v1/`](../../port-reference/auto-keys/v1/)). Microsoft Store policy **10.2.2** explicitly forbids "dynamic inclusion of code that fundamentally changes the described functionality" — auto-keys (synthesizing keyboard input to defeat a third-party game's idle timer) is the textbook violation if bundled into the Store binary.

This spec defines a plugin system that resolves the conflict: keep RoRoRo in the Store, ship macro / automation features as **separately distributed plugin EXEs** that communicate with RoRoRo over named-pipe IPC. The Store-listed RoRoRo never loads, fetches, or contains plugin code — its described functionality stays "multi-launcher." Plugin authors build separate signed Windows binaries, distributed via GitHub releases, that the user installs explicitly through RoRoRo's plugin installer.

---

## Locked decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Sprint scope: plugin SYSTEM only — no plugins built this sprint | Solid-sprint shape; auto-keys is its own later sprint with the bundle as reference |
| 2 | Architecture: out-of-process child + named-pipe IPC | Microsoft Store policy 10.2.2 forces this; in-process AssemblyLoadContext is policy-incompatible |
| 3 | Extension surface: events + UI extensions + launch trigger | Sufficient for auto-keys without giving plugins keys to the kingdom |
| 4 | Excluded surface: writes to favorites / accounts / session-history stores | Blast-radius minimization; future v2 conversation if demand surfaces |
| 5 | UID-aware contract | Plugin must know which Roblox user-IDs are currently live so it works with partial-account scenarios |
| 6 | First consumer: auto-keys plugin in sibling repo `rororo-autokeys-plugin` | Auto-keys bundle ([`docs/port-reference/auto-keys/v1/`](../../port-reference/auto-keys/v1/)) is the spec input — engine logic, safety monitor, budget guard, kill-key gesture port from Mac to Windows per `notes.md` |
| 7 | Trust model: manifest capabilities + user consent | VS Code / Windows-UAC pattern; plugin manifest declares capabilities, user consents per-capability on first install |
| 8 | Install UX: in-app installer takes a GitHub release URL | User-initiated download (Store-policy 10.2.2 clean — not auto-fetch); polling-style update via Velopack on plugin side |
| 9 | Plugin lifecycle: per-plugin opt-in "Enable on RoRoRo start" toggle, default off | User controls which plugins run; supervisor launches + monitors plugin processes |
| 10 | IPC contract shape: gRPC over named pipes | Microsoft-canonical; strongest type safety via `.proto` codegen; server streaming first-class for events; pluginauthors use any gRPC-supporting language |

---

## Architecture

Three first-class components, two repos:

```
┌─────────────────────────────────┐         ┌──────────────────────────┐
│      RoRoRo (Host)              │         │   Plugin EXE             │
│      ROROROblox.App.exe         │         │   e.g. AutoKeys.exe      │
│      Microsoft Store + sideload │         │   sideload only          │
│                                 │         │                          │
│   ┌─────────────────────────┐   │         │   ┌──────────────────┐  │
│   │ PluginHost (gRPC server)│◀──┼────────┼───┤ gRPC client      │  │
│   │ PluginRegistry          │   │  named pipe   Plugin runtime    │  │
│   │ ManifestValidator       │   │  \\.\pipe\rororo-plugin-host    │  │
│   │ ConsentStore (DPAPI)    │   │         │   └──────────────────┘  │
│   │ PluginInstaller         │   │         │                          │
│   │ PluginProcessSupervisor │   │         │  ships its own:          │
│   │ PluginUITranslator      │   │         │   - manifest.json        │
│   └─────────────────────────┘   │         │   - icon, descriptions   │
│                                 │         │   - update-feed URL      │
│   declared UI surfaces v1:      │         │                          │
│   - tray menu items             │         └──────────────────────────┘
│   - per-account row badges      │
│   - status panels               │
└─────────────────────────────────┘
                ▲
                │
         ┌──────┴───────────────────┐
         │ ROROROblox.PluginContract│ ← shared NuGet, .proto + codegen
         │ (new project)            │   referenced by both sides
         └──────────────────────────┘
```

**Wire transport.** Named pipe `\\.\pipe\rororo-plugin-host`, ACL'd to the current Windows user (the user RoRoRo is running as) — connections from other users on the same machine are rejected at the OS level. gRPC over the pipe (`Grpc.AspNetCore` with the `ListenNamedPipe` API shipped in .NET 8+). RoRoRo is the gRPC server; plugins are clients.

**Why this shape.** Store policy 10.2.2 forced out-of-process; everything downstream falls out of that. Per-user pipe ACL keeps unrelated processes off the connection. `ROROROblox.PluginContract` as a shared codegen project means contract drift is a compile error — plugin authors clone the contract NuGet, RoRoRo's manifest validator typechecks capability declarations against the same proto.

**MSIX size impact.** `Grpc.AspNetCore` adds ~50 MB to the self-contained MSIX (current 77 MB → ~120 MB). Worth the cost for type safety + Microsoft-canonical pattern; debated against StreamJsonRpc (lighter) and rejected in favor of compile-time contract enforcement.

---

## Components

### `ROROROblox.PluginContract` (new project)

- `.proto` files defining the gRPC service surface + DTOs
- Generated C# bindings shipped as a NuGet package (consumed by both RoRoRo and plugin authors)
- Versioned independently from RoRoRo app version — plugins target a specific contract version, RoRoRo's handshake check rejects incompatible versions cleanly
- Initial proto sketch (finalized in implementation plan):

```proto
service RoRoRoHost {
  rpc Handshake(HandshakeRequest) returns (HandshakeResponse);

  // Read surface
  rpc GetHostInfo(Empty) returns (HostInfo);
  rpc GetRunningAccounts(Empty) returns (RunningAccountsList);  // UID-aware

  // Server-streaming events (plugin subscribes once, receives events as they happen)
  rpc SubscribeAccountLaunched(SubscriptionRequest) returns (stream AccountLaunchedEvent);
  rpc SubscribeAccountExited(SubscriptionRequest) returns (stream AccountExitedEvent);
  rpc SubscribeMutexStateChanged(SubscriptionRequest) returns (stream MutexStateEvent);

  // Command surface
  rpc RequestLaunch(LaunchRequest) returns (LaunchResult);

  // UI surface (v1: 3 surfaces)
  rpc AddTrayMenuItem(MenuItemSpec) returns (UIHandle);
  rpc AddRowBadge(RowBadgeSpec) returns (UIHandle);
  rpc AddStatusPanel(StatusPanelSpec) returns (UIHandle);
  rpc UpdateUI(UIUpdate) returns (Empty);
  rpc RemoveUI(UIHandle) returns (Empty);
}

service Plugin {
  // RoRoRo pushes UI events back to plugin
  rpc OnUIInteraction(UIInteractionEvent) returns (Empty);
  rpc OnConsentChanged(ConsentChangeEvent) returns (Empty);
}
```

### `ROROROblox.App/Plugins/` (new module in existing project)

- **`PluginHostService`** — gRPC server impl. Hosted via `IHostedService` started in `App.OnStartup`. Spins up the named pipe listener.
- **`PluginRegistry`** — singleton, owns `IReadOnlyList<InstalledPlugin>`. Reads manifests from `%LOCALAPPDATA%\ROROROblox\plugins\<plugin-id>\manifest.json` on startup.
- **`PluginInstaller`** — orchestrates the GitHub-URL install flow: download → SHA verify → manifest parse → consent prompt → unpack → register.
- **`ConsentStore`** — DPAPI-encrypted record of which plugins the user consented to and which capabilities they granted. Mirrors `IAccountStore`'s pattern (DPAPI roundtrip + tamper detection on read).
- **`PluginProcessSupervisor`** — when "Enable on RoRoRo start" is on, launches the plugin EXE on `App.OnStartup`, monitors crash, exposes restart command. Tears down all plugin processes on `App.OnExit`.
- **`PluginUITranslator`** — receives declarative UI specs from plugins (gRPC), renders WPF surfaces. v1 supports 3 surfaces: tray menu items, per-account row badges, status panels.

### Plugin EXE (separate repo, e.g., `rororo-autokeys-plugin`)

- Bundles `ROROROblox.PluginContract` NuGet
- Implements gRPC client that connects to RoRoRo's pipe on startup
- Ships `manifest.json` declaring: name, version, capabilities, contract-version, update-feed URL, icon
- Sideload-only distribution: signed by 626 Labs cert (different cert from RoRoRo Store cert), GitHub Release for binaries
- Self-updates via Velopack on its own feed, independent of RoRoRo

### Manifest format (next to plugin EXE)

```json
{
  "schemaVersion": 1,
  "id": "626labs.auto-keys",
  "name": "Auto-keys",
  "version": "0.1.0",
  "contractVersion": "1.0",
  "publisher": "626 Labs LLC",
  "description": "Multi-window keystroke cycler — defeats Roblox AFK timer.",
  "capabilities": [
    "host.events.account-launched",
    "host.events.account-exited",
    "host.commands.request-launch",
    "host.ui.tray-menu",
    "host.ui.row-badge",
    "system.synthesize-keyboard-input",
    "system.watch-global-input"
  ],
  "icon": "icon.png",
  "updateFeed": "https://github.com/626labs/rororo-autokeys-plugin/releases.atom"
}
```

Capabilities split into two namespaces:
- `host.*` — what the plugin asks RoRoRo for. Gated by gRPC interceptor on every call.
- `system.*` — what the plugin does locally on the user's machine. Disclosed for consent but not enforced by RoRoRo (the plugin runs as its own process; RoRoRo can't sandbox it). Honesty disclosure for the consent UI.

---

## Data flow

### Install flow (user-initiated)

```
1. User: opens RoRoRo → Plugins → Install plugin → pastes GitHub release URL
2. PluginInstaller: GET <url>/manifest.json → parse → schema validate
3. RoRoRo: render Manifest Consent Sheet
   ─ shows: name, publisher, version, contract version
   ─ shows: capabilities (each with plain-language explanation)
   ─ user toggles per-capability consent (some required, some optional)
4. User: clicks Install
5. PluginInstaller: GET <url>/<plugin>.zip → SHA256 verify against manifest.sha256
6. Unpack to %LOCALAPPDATA%\ROROROblox\plugins\<plugin-id>\
7. ConsentStore: persist consent record (DPAPI-encrypted, mirrors AccountStore)
8. PluginRegistry: append to in-memory list
9. UI: shows plugin in Plugins list, "Enable on RoRoRo start" toggle (default off)
```

### Startup flow (every RoRoRo launch)

```
1. App.OnStartup: PluginHostService starts gRPC server on \\.\pipe\rororo-plugin-host
2. PluginRegistry: scans %LOCALAPPDATA%\ROROROblox\plugins\, parses manifests
3. PluginProcessSupervisor: for each plugin where ConsentStore.AutostartEnabled:
     a. Process.Start(plugin.exe)
     b. Track PID
4. Plugin (independent process):
     a. Connect to \\.\pipe\rororo-plugin-host
     b. Handshake — send manifest hash + contract version
     c. RoRoRo: verify hash matches installed manifest, contract version compatible
     d. RoRoRo: install gRPC interceptor that gates each method by declared+consented capabilities
     e. Plugin: subscribe to events via gRPC server-streaming RPCs
     f. Plugin: declare UI surfaces via AddTrayMenuItem / AddRowBadge / etc.
5. RoRoRo MainWindow: renders the merged UI
```

### Event flow (per user action)

User adds a Roblox account → AccountStore raises event → MainViewModel handles → PluginHostService fans out to subscribed plugins via the open server-streaming RPC. Backpressure: if a plugin is slow, RoRoRo's stream-write timeout fires after 5s and the plugin's connection is treated as crashed.

### UI interaction flow

User clicks a tray menu item the plugin added → RoRoRo's `MenuItem.Click` → fires `OnUIInteraction(handle, event)` to the owning plugin → plugin handles → may call `RequestLaunch(...)` back.

### Launch trigger flow (plugin-initiated)

Plugin calls `RequestLaunch(accountId)` → RoRoRo's gRPC interceptor checks `host.commands.request-launch` is declared + consented → calls `IRobloxLauncher.LaunchAsync(...)` → returns result.

---

## Error handling

| Failure | Detection | Recovery |
|---|---|---|
| Plugin process crashes | `Process.Exited` on supervisor's tracked PID | Supervisor logs, surfaces non-blocking status banner: "AutoKeys stopped — click to restart". UI elements added by that plugin are torn down. RoRoRo never crashes from plugin failure. |
| Named-pipe disconnect mid-session | gRPC stream throws `RpcException(StatusCode.Unavailable)` | Treat as plugin-crash class; same recovery |
| Contract-version mismatch at handshake | `PluginHostService` rejects with `PERMISSION_DENIED` and reason | Plugin gets clear error; RoRoRo logs; UI shows "Plugin <name> incompatible — needs RoRoRo ≥ X / contract ≥ Y" |
| Plugin calls method without declared capability | gRPC interceptor reads manifest + consent, returns `PERMISSION_DENIED` | Plugin author bug — surfaces in their dev cycle. RoRoRo logs at warn level. |
| User revokes consent for a capability mid-session | RoRoRo: `OnConsentChanged` push to plugin, 5s grace, then kill connection if plugin doesn't comply | Plugin should gracefully stop using the revoked capability; if it doesn't, supervisor tears down |
| Plugin EXE deleted on disk | Supervisor sees `Process.Start` throw `FileNotFoundException` | Mark plugin as "not installed" in registry, surface "AutoKeys missing — reinstall?" UI |
| Manifest schema mismatch (newer plugin, older RoRoRo) | `PluginInstaller` refuses install at consent step | Surface: "This plugin needs RoRoRo ≥ X. Update RoRoRo." |
| Plugin's own self-update fails | Plugin's responsibility (Velopack on its side) | RoRoRo unaware; plugin handles |

**Non-failures we accept:**

- Plugin starts slowly — RoRoRo doesn't block on plugin startup (gRPC connection happens async; UI renders without plugin surfaces until plugin connects).
- Multiple plugins racing to add the same tray menu position — last-writer-wins on collision; documented.

---

## Testing

**Unit (xUnit, mirrors existing patterns in `src/ROROROblox.Tests/`):**

- `PluginManifestTests` — schema parse + reject malformed
- `ConsentStoreTests` — DPAPI roundtrip + tamper detection (model: `AccountStoreTests`)
- `PluginInstallerTests` — `StubHttpHandler`-backed download + SHA verify + manifest validation
- `PluginRegistryTests` — disk-scan + version comparison + capability lookup
- `PluginProcessSupervisorTests` — mock `IProcessStarter`, verify launch / kill / crash-recovery semantics

**Integration (new project `ROROROblox.PluginTestHarness`):**

- Stand up real `PluginHostService` over an in-process named pipe
- A `TestPluginClient` exercises the full contract: handshake → subscribe → event → UI-add → command → consent-revoke → disconnect
- Capability gating coverage: declare capability X, try to call method Y → assert `PERMISSION_DENIED`
- Contract-version-mismatch coverage: stub plugin advertises contract `2.0`, RoRoRo at `1.0` → reject

**End-to-end (manual, carry forward to release smoke per spec §8):**

Clean Win11 VM, install RoRoRo via Setup.exe, paste real AutoKeys plugin GitHub release URL, walk consent, enable autostart, restart RoRoRo, verify autoplay starts, click stop, kill RoRoRo, verify plugin process tears down cleanly.

**No real-Roblox automation in CI** — same discipline as the existing test suite. The plugin contract is testable without Roblox.

---

## Open questions for next sprint

These didn't block this design but need decisions before implementation kicks off:

1. **Versioning policy.** When does RoRoRo bump the contract version (`ROROROblox.PluginContract` major)? Breaking changes to method signatures only, or also new capability namespaces? Plugin authors need clear semver guidance.
2. **Sample plugin shape.** What's the in-bundle dummy plugin that exercises the contract for integration tests? Suggest: a `RoRoRoSample` plugin that adds a tray menu item "Hello from plugin" and logs every event it receives. ~200 lines, doubles as documentation.
3. **Plugin distribution UX polish.** Does the in-app installer support a curated "626 Labs plugins" list as a default starting point, or is it strictly URL-paste? UX-only call, doesn't change architecture.
4. **Telemetry.** Does RoRoRo log which plugins are installed / running for diagnostics? Privacy-policy implications — if logged, the privacy policy needs to disclose.
5. **Cross-machine sync.** When v1.6+ adds account-sync across machines, do plugin install lists sync too? Out of scope for this spec.

---

## Cross-machine inputs

The macOS sibling's auto-keys feature is captured at [`docs/port-reference/auto-keys/v1/`](../../port-reference/auto-keys/v1/) (vibe-taker bundle landed in commit 890ac49). The bundle's `notes.md` documents Mac→Windows API translations; `architecture.md` describes the engine state machine + DI seams; `reference/docs/0004-auto-keys-cycler.md` is the canonical 9-Decision ADR.

**Bundle posture warning** — the bundle's notes recommend "the WPF port should be a portable EXE / signed installer, not Microsoft-Store-bound." That posture was written before plugin-architecture was on the table. With this spec, the auto-keys EXE *is* the "portable signed installer" the Mac team described — it just lives in a sibling repo and communicates with Store-distributed RoRoRo over named pipe IPC. RoRoRo Windows stays in the Store.

The auto-keys plugin lands in a new sibling repo (`rororo-autokeys-plugin`) in a follow-up sprint, after this plugin system ships and `ROROROblox.PluginContract` is on NuGet.

---

## References

- Auto-keys reference bundle: [`docs/port-reference/auto-keys/v1/`](../../port-reference/auto-keys/v1/)
- Microsoft Store policy 10.2.2: https://learn.microsoft.com/en-us/windows/apps/publish/store-policies
- gRPC over named pipes: https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes
- Existing canonical RoRoRo design: [`docs/superpowers/specs/2026-05-03-RORORO-design.md`](2026-05-03-RORORO-design.md)
- Release playbook (Phase 7 Partner Center expectations apply when this lands): [`docs/store/release-playbook.md`](../../store/release-playbook.md)
- Active brainstorming memory: `~/.claude/projects/C--Users-estev-Projects-ROROROblox/memory/project_rororo_plugin_system_design.md`
