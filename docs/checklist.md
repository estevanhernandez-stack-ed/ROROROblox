# ROROROblox — v1.4 Plugin System Build Checklist

**Cycle:** v1.4 plugin system (out-of-process plugins via named-pipe gRPC)
**Cycle type:** Spec-first cycle (pattern mm). Substantive design at [`docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md`](superpowers/specs/2026-05-09-rororo-plugin-system-design.md). [`spec.md`](spec.md) is a pointer-stub.
**Companion plan:** [`docs/superpowers/plans/2026-05-09-rororo-plugin-system.md`](superpowers/plans/2026-05-09-rororo-plugin-system.md) — 25-task TDD-strict implementation plan that pairs with this checklist (each Cart item below maps to 1–4 plan tasks).
**Build mode:** autonomous-with-verification (three checkpoints: M1 after item 7, M2 after item 14, M3 after item 17, before docs/security pass)
**Comprehension checks:** off
**Git cadence:** commit after each item (or after each TDD step within the companion plan)
**Branch:** `feat/plugin-system` cut from `main` at item 1 start
**Repo:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)

**Effort estimates:** wall-clock guesses for autonomous mode. **Total cycle ≈ 18–24 hours of focused engineering** (multi-day; the largest cycle to date — gRPC + Kestrel + WPF host integration is genuinely heavy).

---

## Milestone 1 — Foundation (items 1–7)

After M1: plugin install/registry/lifecycle work end-to-end on disk; no gRPC plumbing yet.

- [ ] **1. Scaffold `ROROROblox.PluginContract` project + initial `.proto` contract**
  Spec ref: spec § Components → "ROROROblox.PluginContract"
  Effort: ~30–40 min
  Dependencies: none
  What to build: New `src/ROROROblox.PluginContract/` project (netstandard2.1) with `Google.Protobuf` + `Grpc.Tools` + `Grpc.Net.Client` package refs. Add a `Protos/plugin_contract.proto` that defines:
  - Two services: `RoRoRoHost` (Handshake, GetHostInfo, GetRunningAccounts, three SubscribeX server-streaming RPCs, RequestLaunch, AddTrayMenuItem / AddRowBadge / AddStatusPanel / UpdateUI / RemoveUI) and `Plugin` (OnUIInteraction, OnConsentChanged, OnShutdown).
  - Messages: HandshakeRequest/Response, HostInfo, RunningAccount, LaunchRequest/Result, MenuItemSpec, RowBadgeSpec, StatusPanelSpec, UIHandle, AccountLaunchedEvent, AccountExitedEvent, MutexStateEvent, ConsentChangeEvent.
  Wire `Grpc.Tools` codegen with `<Protobuf Include="..." GrpcServices="Both" />`.
  Add the project to `ROROROblox.sln`. Ship `PackageReadme.md` for NuGet packaging.
  Acceptance: `dotnet build src/ROROROblox.PluginContract/` succeeds; generated C# types `RoRoRoHost.RoRoRoHostBase` and `Plugin.PluginBase` exist in `obj/Debug/.../Protos/PluginContractGrpc.cs`.
  Verify: `dotnet build src/ROROROblox.PluginContract/`. Commit: `feat(plugins): scaffold ROROROblox.PluginContract project + gRPC contract`.

- [ ] **2. `PluginManifest` model + JSON parser + capability vocabulary**
  Spec ref: spec § Components → "Manifest format"
  Effort: ~45 min
  Dependencies: item 1
  What to build:
  - `src/ROROROblox.App/Plugins/PluginManifest.cs` — record + `Parse(json)` static. Required fields: schemaVersion, id, name, version, contractVersion, publisher, description, capabilities. Optional: icon, updateFeed. ID must match reverse-DNS regex `^[a-z0-9]+(\.[a-z0-9-]+)+$`. Schema version mismatch / missing required fields throw `PluginManifestException`.
  - `src/ROROROblox.App/Plugins/PluginCapability.cs` — string-constant catalog with two namespaces: `host.*` (gated by gRPC interceptor) + `system.*` (disclosure-only). Initial set: `host.events.account-launched/exited/mutex-state-changed`, `host.commands.request-launch`, `host.ui.tray-menu/row-badge/status-panel`, `system.synthesize-keyboard-input`, `system.synthesize-mouse-input`, `system.watch-global-input`, `system.prevent-sleep`, `system.focus-foreign-windows`. Each entry has a plain-language `Display(cap)` explanation.
  Test files: `PluginManifestTests.cs` (4 cases: valid roundtrip, missing required field throws, unsupported schemaVersion throws, invalid id throws), `PluginCapabilityTests.cs` (5 cases: known/unknown, host vs system namespace, display string).
  Acceptance: 9 new tests passing. Existing 300 tests still green.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PluginManifestTests|PluginCapabilityTests"`. Commit: `feat(plugins): PluginManifest + PluginCapability vocabulary`.

- [ ] **3. `ConsentStore` (DPAPI-encrypted per-plugin consent records)**
  Spec ref: spec § Components → "ConsentStore"; Data flow → "Install flow" step 7
  Effort: ~45–60 min
  Dependencies: item 2
  What to build: `src/ROROROblox.App/Plugins/ConsentStore.cs` — DPAPI-encrypted (per-user, per-machine) JSON list of `ConsentRecord(PluginId, GrantedCapabilities, AutostartEnabled)`. Methods: `ListAsync()`, `GrantAsync(pluginId, caps)`, `RevokeAsync(pluginId)`, `SetAutostartAsync(pluginId, enabled)`. Mirrors the `AccountStore` pattern: read returns empty list on tamper / decryption failure / malformed JSON (don't throw — registry must keep working with a corrupted file).
  Test file: `ConsentStoreTests.cs` (5 cases per spec § Testing): empty-on-missing, grant+roundtrip across new instance, autostart toggle persistence, revoke removes, tampered-file returns empty.
  Acceptance: 5 new tests pass; `git diff` shows only Plugins/ + Tests/ changes.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "ConsentStoreTests"`. Commit: `feat(plugins): ConsentStore — DPAPI-encrypted consent records`.

- [ ] **4. `InstalledPlugin` + `PluginRegistry` (disk scan + manifest+consent pairing)**
  Spec ref: spec § Components → "PluginRegistry"; Data flow → "Startup flow" step 2
  Effort: ~30 min
  Dependencies: items 2, 3
  What to build:
  - `InstalledPlugin.cs` — record `(PluginManifest Manifest, string InstallDir, ConsentRecord Consent)` + computed `ExecutablePath = InstallDir/<id>.exe`.
  - `PluginRegistry.cs` — constructor takes pluginsRoot path + ConsentStore. `ScanAsync()` enumerates subdirs of `%LOCALAPPDATA%\ROROROblox\plugins\`, parses each `manifest.json`, pairs with consent record (defaults to empty/false if no consent yet), skips malformed manifests silently.
  - `DefaultPluginsRoot` static returns `%LOCALAPPDATA%\ROROROblox\plugins\`.
  Test file: `PluginRegistryTests.cs` (4 cases): empty-when-no-plugins, returns-manifest-when-present, skips-malformed, pairs-with-consent.
  Acceptance: 4 new tests pass.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PluginRegistryTests"`. Commit: `feat(plugins): InstalledPlugin + PluginRegistry disk scan`.

- [ ] **5. `PluginInstaller` — GitHub URL → SHA verify → unpack**
  Spec ref: spec § Data flow → "Install flow" steps 1–7
  Effort: ~60–75 min
  Dependencies: items 2, 3, 4
  What to build: `src/ROROROblox.App/Plugins/PluginInstaller.cs`. Constructor takes `HttpClient` + plugins-root path. `InstallAsync(baseUrl, requireCapabilities)`:
  1. GET `<baseUrl>/manifest.json` → `PluginManifest.Parse(...)`.
  2. Validate every entry in `requireCapabilities` is declared in the manifest; throw `PluginInstallerException` otherwise.
  3. GET `<baseUrl>/manifest.sha256` → trim + lowercase.
  4. GET `<baseUrl>/plugin.zip` → SHA256 → compare; throw on mismatch.
  5. Delete-and-recreate `<pluginsRoot>/<plugin-id>/`; extract zip with zip-slip guard (reject any entry whose resolved path escapes the install dir).
  6. On any exception during unpack, best-effort cleanup of the install dir.
  Returns the `InstalledPlugin` (consent default-empty/autostart-off).
  Test file: `PluginInstallerTests.cs` (3 cases): valid-package-extracts, sha-mismatch-cleans-up, missing-required-capability-throws. Uses existing `StubHttpHandler` pattern from `RobloxApiTests`. Use in-memory `ZipArchive` to build test packages.
  Acceptance: 3 new tests pass. Zip-slip guard verified by attempted-escape test.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PluginInstallerTests"`. Commit: `feat(plugins): PluginInstaller — GitHub URL fetch + SHA verify + unpack`.

- [ ] **6. `IPluginProcessStarter` + `PluginProcessSupervisor`**
  Spec ref: spec § Components → "PluginProcessSupervisor"; Error handling → "Plugin process crashes"
  Effort: ~45 min
  Dependencies: item 4
  What to build:
  - `IPluginProcessStarter.cs` — minimal seam: `int Start(pluginId, exePath)` + `void Kill(pid)`.
  - `PluginProcessSupervisor.cs` — `StartAutostart(plugins)` launches every plugin where `Consent.AutostartEnabled`, tracks PIDs by plugin id. `StopAll()` kills all tracked. `Restart(plugin)` kill+start on the same plugin. Thread-safe via lock around the PID dict. `RunningPids` snapshot accessor.
  - `PluginExited` event will be added in item 13 when crash recovery wires up.
  Test file: `PluginProcessSupervisorTests.cs` (3 cases) with a `FakeProcessStarter` that records calls: starts only autostart=true, StopAll kills every tracked, Restart stops-and-starts.
  Acceptance: 3 new tests pass.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PluginProcessSupervisorTests"`. Commit: `feat(plugins): PluginProcessSupervisor — launch + monitor + teardown`.

- [ ] **7. M1 milestone gate**
  Spec ref: all of items 1–6
  Effort: ~15 min
  Dependencies: items 1–6
  What to build: nothing new — verify the foundation is solid.
  - Full test suite: every existing test still green + the ~17 new plugin-related unit tests.
  - App still compiles end-to-end (warning if running RoRoRo blocks the bin-copy step is OK; `obj/.../App.dll` is the truth).
  - Tag: `git tag plugin-system-m1`.
  - Decision log: `mcp__626Labs__manage_decisions log` — name what landed (PluginContract NuGet, manifest+capability vocab, consent/registry/installer/supervisor) and what's next (gRPC plumbing).
  Acceptance: full test suite passes, App builds (or only fails on bin-copy with a running App), tag exists locally, dashboard decision logged.
  Verify: `dotnet test src/ROROROblox.Tests/`; `dotnet build src/ROROROblox.App/`; `git tag --list plugin-system-m1`. Commit: `chore(plugins): M1 milestone — foundation complete`.

---

## Milestone 2 — gRPC plumbing + UI translator (items 8–14)

After M2: a stub plugin connects over a real Windows named pipe, exchanges all RPCs, contributes UI surfaces. End-to-end integration test green.

- [ ] **8. Add `Grpc.AspNetCore` + Kestrel named-pipe transport deps to App**
  Spec ref: spec § Architecture → "Wire transport"; "MSIX size impact"
  Effort: ~10 min
  Dependencies: item 7
  What to build: Modify `src/ROROROblox.App/ROROROblox.App.csproj`:
  - Add `Grpc.AspNetCore` 2.68.0
  - Add `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes` 10.0.0
  - Add `Microsoft.Extensions.Hosting` 10.0.0
  - Add `<ProjectReference Include="..\ROROROblox.PluginContract\..." />`
  Acceptance: `dotnet restore` clean; `dotnet build src/ROROROblox.App/` clean (modulo running-App bin-copy lock).
  Verify: `dotnet build src/ROROROblox.App/`. Commit: `build(plugins): add Grpc.AspNetCore + Kestrel named-pipe transport`.

- [ ] **9. `IInstalledPluginsLookup` + `PluginHostService` (handshake-only)**
  Spec ref: spec § Data flow → "Startup flow" step 4 (handshake); Error handling → "Contract-version mismatch"
  Effort: ~45 min
  Dependencies: items 4, 8
  What to build:
  - `IInstalledPluginsLookup.cs` — single `FindById(string id)` method.
  - `PluginHostService.cs` — partial class extending `RoRoRoHost.RoRoRoHostBase`. Initial impl: `Handshake(request, ctx)` → returns Accepted=true if plugin is installed AND contract version matches; otherwise Accepted=false with reject reason.
  Test file: `PluginHostServiceTests.cs` (3 cases): accepts-matching-version, rejects-version-mismatch, rejects-unknown-plugin-id.
  Acceptance: 3 new tests pass.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PluginHostServiceTests"`. Commit: `feat(plugins): PluginHostService — handshake (accept/reject/version-check)`.

- [ ] **10. Read surface — `GetHostInfo` + `GetRunningAccounts` + provider seams**
  Spec ref: spec § Architecture diagram (UID-aware contract); Data flow → "Event flow"
  Effort: ~30 min
  Dependencies: item 9
  What to build:
  - `IPluginHostStateProvider.cs` — exposes `MultiInstanceEnabled` + `MultiInstanceState`.
  - `IRunningAccountsProvider.cs` — `Snapshot()` returns `IReadOnlyList<RunningAccountSnapshot>` (record: `AccountId, RobloxUserId, DisplayName, ProcessId`). UID-aware per locked decision.
  - Extend `PluginHostService` with two more RPCs that translate snapshots → proto messages.
  - Update existing handshake test fixtures to inject the new provider params.
  Test extensions: 2 new cases in `PluginHostServiceTests.cs` (host-info-returns-state, running-accounts-from-provider).
  Acceptance: 5 total `PluginHostServiceTests` passing.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PluginHostServiceTests"`. Commit: `feat(plugins): host read surface — GetHostInfo + GetRunningAccounts`.

- [ ] **11. `RpcMethodCapabilityMap` + `CapabilityInterceptor`**
  Spec ref: spec § Components → "CapabilityInterceptor"; Trust model (locked decision #7)
  Effort: ~45–60 min
  Dependencies: items 9, 10
  What to build:
  - `RpcMethodCapabilityMap.cs` — `Required(methodName)` returns the capability that gates the method, or `null` if no gate. Handshake/GetHostInfo/GetRunningAccounts/UpdateUI/RemoveUI are ungated. Subscribe* + RequestLaunch + AddTrayMenuItem/RowBadge/StatusPanel each map to their respective `host.*` capability.
  - `CapabilityInterceptor.cs` — `Grpc.Core.Interceptors.Interceptor` impl. Two overrides: `UnaryServerHandler<TReq,TResp>` and `ServerStreamingServerHandler<TReq,TResp>`. Each calls `EnforceCapability(context)` before delegating. Throws `RpcException(StatusCode.PermissionDenied)` when the calling plugin's consent doesn't include the required capability. Throws `FailedPrecondition` if no plugin id is set yet (handshake hasn't completed).
  Test file: `CapabilityInterceptorTests.cs` (3 cases): allows-when-granted, denies-when-missing, allows-handshake-pre-bind.
  Acceptance: 3 new tests pass.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "CapabilityInterceptorTests"`. Commit: `feat(plugins): CapabilityInterceptor — gRPC capability gating`.

- [ ] **12. Event subscription RPCs (server streaming) + `IPluginEventBus`**
  Spec ref: spec § Data flow → "Event flow"; Error handling → "Plugin slow consumer"
  Effort: ~60–75 min
  Dependencies: items 10, 11
  What to build:
  - `IPluginEventBus.cs` — three events: `AccountLaunched`, `AccountExited`, `MutexStateChanged`.
  - `InProcessPluginEventBus.cs` — concrete impl with `Raise*` helpers used by RoRoRo's existing services (will be wired in item 17).
  - Extend `PluginHostService` with the three `SubscribeX` server-streaming RPCs. Each:
    1. Creates a bounded `System.Threading.Channels.Channel<TEvent>`.
    2. Subscribes a handler that translates snapshot → proto + writes to the channel.
    3. `await foreach` reads channel into `responseStream`.
    4. On `OperationCanceledException` (client disconnect), unsubscribes.
    5. 5-second write timeout; if plugin is too slow, the connection is treated as crashed (handled by item 13).
  Test extensions: new `SubscribeAccountLaunched_FansOutEvents` test using `InProcessPluginEventBus.RaiseAccountLaunched` + a `TestStreamWriter` fake.
  Acceptance: event test passes; existing tests still green.
  Verify: `dotnet test src/ROROROblox.Tests/`. Commit: `feat(plugins): event subscription RPCs (account launched/exited, mutex)`.

- [ ] **13. Command surface (`RequestLaunch`) + `IPluginLaunchInvoker` + crash event**
  Spec ref: spec § Data flow → "Launch trigger flow"; Error handling → "Plugin process crashes"
  Effort: ~45 min
  Dependencies: items 6, 11, 12
  What to build:
  - `IPluginLaunchInvoker.cs` — `Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(accountId)`.
  - Extend `PluginHostService` with `RequestLaunch` RPC delegating to the invoker.
  - Add `event Action<string,int>? PluginExited` to `PluginProcessSupervisor`. Subscribe `Process.Exited` for each tracked PID; raise on exit.
  Test extensions: `RequestLaunch_DispatchesToLauncher` test with a fake invoker; supervisor test for `PluginExited`-on-process-exit.
  Acceptance: tests pass.
  Verify: `dotnet test src/ROROROblox.Tests/`. Commit: `feat(plugins): RequestLaunch + plugin-exited event`.

- [ ] **14. UI translator + `IPluginUIHost` + `PluginHostStartupService` + integration test**
  Spec ref: spec § Components → "PluginUITranslator"; Architecture → "Wire transport"; Testing → "Integration"
  Effort: ~120–150 min (the big one)
  Dependencies: items 11, 12, 13
  What to build:
  - `IPluginUIHost.cs` — abstract WPF surface: `AddTrayMenuItem`, `AddRowBadge`, `AddStatusPanel`, `Update`, `Remove`. Returns + accepts opaque handle ids.
  - `PluginUITranslator.cs` — bridges plugin gRPC UI specs into `IPluginUIHost` calls. Tracks owner-plugin-id per handle so `RemoveUI` validates ownership. Exposes `UIInteraction` event that fires when a user clicks a plugin-contributed control.
  - Extend `PluginHostService` with `AddTrayMenuItem` / `AddRowBadge` / `AddStatusPanel` / `UpdateUI` / `RemoveUI` RPCs delegating to `PluginUITranslator`.
  - `PluginHostStartupService.cs` — `IHostedService` that builds a Kestrel-hosted gRPC server on `\\.\pipe\rororo-plugin-host`. Uses `WebApplication.CreateBuilder()` + `ConfigureKestrel(options => options.ListenNamedPipe(...))` + `MapGrpcService<PluginHostService>()`. Starts on `StartAsync`, disposes on `StopAsync`.
  - New test project: `src/ROROROblox.PluginTestHarness/ROROROblox.PluginTestHarness.csproj` (net10.0-windows + xUnit + Grpc.Net.Client + project refs to App + PluginContract). One end-to-end test that:
    1. Spins up `PluginHostStartupService` on a randomized pipe name.
    2. Connects via `NamedPipeClientStream` wrapped in a `GrpcChannel.ForAddress("http://pipe", ...)` with a `SocketsHttpHandler.ConnectCallback` returning the pipe.
    3. Calls `Handshake` and asserts Accepted=true.
  - Add unit tests for `PluginUITranslator` (3 cases): adds-and-assigns-handle, dispatches-to-host, remove-forgets-handle.
  Acceptance: integration test passes against a real Windows named pipe; all unit tests still green.
  Verify: `dotnet test src/ROROROblox.PluginTestHarness/`. Commit: `feat(plugins): UI translator + Kestrel named-pipe gRPC startup + E2E integration test`.

  **CHECKPOINT — M2 milestone gate.** Verify the gRPC plumbing is solid before the user-facing UI work. Tag `plugin-system-m2`. Decision log: name what's wired (gRPC handshake + read + events + commands + UI translator + Kestrel pipe startup), what's pending (DI wiring in App.xaml.cs, Plugins page UI, consent sheet, status banner, release).

---

## Milestone 3 — User-facing UI + composition + release (items 15–17)

After M3: end users can install and manage plugins via RoRoRo's UI; sprint shippable.

- [ ] **15. DI wiring + adapter classes in App.xaml.cs**
  Spec ref: spec § Architecture diagram (PluginHost block); Data flow → "Startup flow"
  Effort: ~90 min
  Dependencies: items 7, 14
  What to build: Register every plugin-side service in `App.OnStartup`'s `ConfigureServices(...)`:
  - `ConsentStore`, `PluginRegistry`, `PluginInstaller` (with HttpClient), `IPluginProcessStarter` (concrete `DefaultPluginProcessStarter` using `Process.Start`), `PluginProcessSupervisor`, `IPluginEventBus`, `PluginHostService`, `CapabilityInterceptor`, `PluginHostStartupService`.
  - Adapter classes wiring existing RoRoRo singletons to plugin interfaces: `MutexHostStateAdapter` (IPluginHostStateProvider over `IMutexHolder`), `MainViewModelRunningAccountsAdapter` (IRunningAccountsProvider over `MainViewModel.Accounts`), `MainViewModelLaunchInvokerAdapter` (IPluginLaunchInvoker delegating to `vm.StartCommand`), `WpfPluginUIHost` (IPluginUIHost over MainWindow + TrayService).
  - Bridge existing RoRoRo events (account launched/exited, mutex state) into `InProcessPluginEventBus.Raise*` calls. Lives in App.xaml.cs's wiring section.
  - Hook `PluginHostStartupService.StartAsync` from `App.OnStartup` and `StopAsync` from `App.OnExit`.
  Acceptance: App launches, log line "PluginHost gRPC server listening on ..." appears, App exits cleanly with the gRPC server torn down.
  Verify: build + run RoRoRo manually; check log output. Commit: `feat(plugins): DI wiring + lifecycle hooks`.

- [ ] **16. Plugins page UI + manifest consent sheet + status banner**
  Spec ref: spec § Data flow → "Install flow" step 3 (consent sheet); Error handling → "Plugin process crashes"
  Effort: ~180–240 min (UI is the long pole)
  Dependencies: item 15
  What to build:
  - `src/ROROROblox.App/Plugins/PluginsView.xaml` + `.xaml.cs` + `PluginsViewModel.cs`. Layout: header "Plugins", paste-URL input + Install button, list of installed plugins. Per-row: name, version, capabilities-count, autostart toggle, Revoke button. ViewModel exposes `LoadAsync`, `InstallFromUrlCommand`, `ToggleAutostartCommand`, `RevokeCommand`.
  - `ConsentSheet.xaml` + `.xaml.cs` — modal shown after `PluginInstaller.InstallAsync` parses + verifies manifest. Renders plugin name/version/publisher/description + per-capability list (id + `PluginCapability.Display(cap)` + checkbox; some required, some optional). Install / Cancel buttons. On Install: `ConsentStore.GrantAsync(...)` with chosen caps. On Cancel: delete the install dir (rollback).
  - Wire into `MainWindow.xaml` — add a "Plugins" nav surface (matches existing nav pattern); switch right pane to `PluginsView`.
  - Subscribe to `PluginProcessSupervisor.PluginExited` in `PluginsViewModel`; set `StatusBanner = "<plugin-name> stopped — click to restart"` with a Restart command. Surface the banner in `PluginsView`.
  Acceptance: install flow works end-to-end with a hand-crafted local manifest+zip (file:// URL); consent persists; autostart toggle works; killing a plugin process from Task Manager surfaces the banner within ~3 seconds.
  Verify: manual smoke test — install, consent, restart RoRoRo, verify autostart fires; kill plugin, verify banner. Commit: `feat(plugins): Plugins page + consent sheet + status banner`.

- [ ] **17. Integration tests (capability-gate + consent-revoke + UI-add) + M3 gate + release prep**
  Spec ref: spec § Testing → "Integration"; Error handling → "User revokes consent mid-session"
  Effort: ~90 min
  Dependencies: item 16
  What to build:
  - Extend `ROROROblox.PluginTestHarness/EndToEndContractTests.cs` with three more cases:
    1. Stub plugin declares only `host.events.account-launched`; calling `RequestLaunchAsync` over the channel asserts `RpcException(StatusCode.PermissionDenied)`.
    2. Stub plugin subscribes to events; calling `consentStore.RevokeAsync(pluginId)` causes the event-stream to terminate with `Cancelled` within 1s.
    3. Stub plugin calls `AddTrayMenuItemAsync(spec)`; `IPluginUIHost` test fake records the call; `UIHandle` returned non-empty.
  - Run full unit + integration suite — must be green.
  - Manual end-to-end smoke on a clean Win11 VM (per Phase 7 of `docs/store/release-playbook.md`):
    a. Install RoRoRo via Setup.exe.
    b. Build a small "Hello world" stub plugin EXE (separate repo; out of scope to ship — this is one-off scaffolding for the smoke).
    c. Paste its GitHub release URL into RoRoRo Plugins → Install. Walk consent.
    d. Enable autostart; restart RoRoRo; verify the stub plugin process starts and the UI surface it added appears.
    e. Revoke consent; verify the plugin disconnects gracefully within seconds.
  - Tag `plugin-system-m3`. Decision log entry for v1.4 milestone (architecture + first-public plugin contract version 1.0).
  Acceptance: 3 new integration tests pass; full suite green; smoke test passes on a clean VM; tag exists.
  Verify: `dotnet test src/ROROROblox.PluginTestHarness/`; manual smoke; `git tag --list plugin-system-m3`. Commit: `test(plugins): integration coverage — gate + consent-revoke + UI-add + M3 gate`.

---

## Milestone 4 — Documentation & Security Verification (item 18)

- [ ] **18. Documentation & Security Verification — README, docs cleanup, secrets scan, dependency audit, deployment security**
  Spec ref: all of items 1–17; release playbook Phase 7 + Phase 2 (release notes)
  Effort: ~120–180 min
  Dependencies: items 1–17
  What to build:

  **Documentation:**
  - Update `README.md` — add a "Plugins (v1.4+)" section: what plugins do, where they live, how to install one (paste URL into Plugins → Install), trust model in plain language, link to plugin author guide.
  - Create `docs/plugins/AUTHOR_GUIDE.md` — write for plugin authors. Sections: what the contract is (link to PluginContract NuGet), capability vocabulary, how to write a manifest, how to produce a SHA-paired zip + manifest.json + manifest.sha256 GitHub release, gRPC client connection example (the `NamedPipeClientStream` + `GrpcChannel` pattern), trust + signing recommendation (plugins should be Authenticode-signed even though RoRoRo doesn't enforce it in v1.4).
  - Update `CLAUDE.md` (project-level) — add the plugin system to "What's where" and add a row for `docs/plugins/AUTHOR_GUIDE.md` to "Common tasks".
  - Update `docs/spec.md` (already done in this cycle) — verify the cycle history line for v1.4 is accurate after merge.
  - Banner-correct the canonical spec at `docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md` ONLY if implementation revealed drift — do NOT rewrite. Pattern v from Vibe Thesis.

  **Secrets scan:**
  - Re-run pre-commit secret-scan locally: `git ls-files | xargs grep -l ".ROBLOSECURITY"` — must return 0 hits across the new code surface.
  - Verify `dev-cert.pfx`, `accounts.dat`, `consent.dat`, `webview2-data/`, `<pluginsRoot>/` are all in `.gitignore`.
  - Add `consent.dat` and `plugins/` to `.gitignore` if not already there.
  - Verify the test harness doesn't accidentally commit any real cookies or pfx bytes.

  **Dependency audit:**
  - `dotnet list package --vulnerable --include-transitive` against every project in the solution. Expect zero high/critical CVEs. If any surface, evaluate (does the vulnerable code path exist in our usage?), upgrade if needed.
  - Verify the new packages added in item 8 (`Grpc.AspNetCore`, `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes`, `Microsoft.Extensions.Hosting`) are at the expected major versions and don't pull in unexpected transitive deps. Check the published trim warnings if any (full-trim is not enabled in self-contained MSIX, but warnings should be reviewed).
  - Review `dotnet publish` output size — confirm the MSIX growth from 77 MB → ~120 MB matches expectations from spec § Architecture.

  **Deployment security:**
  - **Pipe ACL verification:** add a manual smoke step that uses `accesschk` (Sysinternals) or PowerShell to inspect the pipe's DACL on a running RoRoRo instance. Confirm only the current Windows user has access. Document the result in a comment in `PluginHostStartupService.cs` for future audit trails.
  - **Authenticode-sign the published RoRoRo App** as part of release Phase 4 dance per existing playbook; nothing new here — just verify it still works with the gRPC dependency added.
  - **Reviewer letter for v1.4 Store submission** (`docs/store/`) — net-new section explaining the plugin system: "RoRoRo v1.4 introduces an out-of-process plugin system. RoRoRo (Store-distributed) hosts a gRPC server on a per-user ACL'd named pipe. Plugins are SEPARATE products (separate Windows EXEs, separate distributions, sideload-only). RoRoRo never loads, fetches, or bundles plugin code. The Store-listed product's described functionality stays 'multi-launcher.' Plugin install is user-initiated (paste GitHub URL → consent sheet); never auto-fetched. Per Store policy 10.2.2, dynamic-inclusion-of-code is avoided."
  - **Velopack release smoke:** verify the v1.4 update rolls out to existing v1.3.4 installs cleanly via Velopack auto-update. Watch the first 24h after publish for a surge in error reports.

  **Release path:**
  - Bump version to `1.4.0.0` per release playbook Phase 1.
  - Run Phase 3 (`scripts/finalize-store-build.ps1 -Version 1.4.0.0`).
  - Run Phase 4 (sideload dance).
  - Write `docs/store/release-notes-1.4.0.0.md` per Phase 2.
  - Phase 5 (commit + tag + push).
  - Phase 6 (finalize GH Release after workflow drafts it).
  - Phase 7 (Partner Center upload with the new reviewer letter).
  - Phase 8 (Discord announcement after publish).

  Acceptance: README + AUTHOR_GUIDE published; secrets-scan + dependency-audit clean; reviewer letter for v1.4 ready; Velopack roll-out works; clan announcement posted.
  Verify: re-run pre-commit hooks (secret-scan + local-path-guard); `dotnet list package --vulnerable`; manual VM smoke; Partner Center submission goes In-Review; Discord announcement posted.
  Commit: `docs(plugins): v1.4 author guide + reviewer letter + release` (followed by the release-playbook commits).

---

## Cycle close (after item 18)

- [ ] Run `mcp__626Labs__manage_decisions log` for the v1.4 sprint completion.
- [ ] Update `MEMORY.md` to remove the "in-flight" plugin-system memory entry; replace with a final "shipped" entry.
- [ ] Optional `/reflect` pass — write `docs/superpowers/specs/2026-05-09-rororo-plugin-system-reflection.md` with what worked, what surprised, what to carry forward.
- [ ] Plugin author guide announcement post (Discord + GitHub Discussions if enabled) so the audience knows they CAN write plugins, with a pointer to the upcoming `rororo-autokeys-plugin` as the first reference example.
