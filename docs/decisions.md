# RoRoRo decisions log

Append-only. Reconstructed on 2026-08-30 from the code, the spec banners under `docs/superpowers/specs/`, and git history; each entry names its evidence so it can be re-checked. Newest at the bottom. The 626 Labs dashboard (`mcp__626Labs__manage_decisions`) remains the primary log for significant decisions; this file is the in-repo mirror a reader can grep. When adding an entry, keep the four fields and cite a commit, spec, or path.

Format:

```
### YYYY-MM-DD — <decision in one line>
**Context:** why the question came up.
**Consequences:** what it constrains or costs today.
**Evidence:** commit / spec / path.
```

---

### 2026-05-03 — WPF + WPF-UI over WinUI 3 for v1
**Context:** Tray residence and Win32 interop needed to be battle-tested; the whole product is a Win32 kernel-object trick.
**Consequences:** Core targets `net10.0-windows`; WPF-affine singletons need dispatcher care (see 2026-08-23); WinUI 3 is "a v2 conversation".
**Evidence:** canonical spec §3 (`docs/superpowers/specs/2026-05-03-rororoblox-design.md`); commit `511c4d7`.

### 2026-05-03 — Clean reimplementation of MultiBloxy's technique, not a fork
**Context:** Provenance is load-bearing for the Store narrative and licensing.
**Consequences:** `MultiBloxy.exe` and `PROVENANCE.txt` are immutable reference files.
**Evidence:** `PROVENANCE.txt`; README "Provenance".

### 2026-05-03 — `ROROROblox.slnx` is the canonical solution; the legacy `.sln` is gitignored
**Context:** Qodo IDE regenerates a legacy `ROROROblox.sln` beside the `.slnx`, missing later projects.
**Consequences:** Every command names the `.slnx`; bare `dotnet build` errors MSB1011 while both exist.
**Evidence:** `511c4d7` (slnx added), `6f2545f` 2026-05-04 (gitignore rule).

### 2026-05-03 — Roblox contract facts caught by the auth-ticket spike
**Context:** The spike found a 415 on the auth-ticket POST without `Content-Type: application/json` on an empty body, and that a `roblox-player:` URI with no `placelauncherurl` opens the client not logged in.
**Consequences:** Both are regression-tested; Home launches later had to use `launchmode:app` to legitimately omit `placelauncherurl` (2026-07-09).
**Evidence:** `process-notes.md` spike section; canonical spec §11; `Tests/RobloxApiTests.cs`.

### 2026-05-03 — Manual smoke on a clean VM instead of end-to-end automation against roblox.com
**Context:** Bot accounts get flagged; flaky CI eats trust.
**Consequences:** Live verification lives in `docs/smoke-*.md` and `docs/superpowers/smoke/`; no test touches the network.
**Evidence:** canonical spec §11; `Tests/StubHttpHandler.cs`.

### 2026-05-04 — Raw Win32 `CreateMutex` via CsWin32, not `System.Threading.Mutex`
**Context:** Explicit handle lifetime plus a watchdog for an externally invalidated handle.
**Consequences:** Core is Windows-only; error-code mapping (6 vs 183) became possible, which enabled the 2026-07-10 outcome split.
**Evidence:** `Core/MutexHolder.cs` header; commit `cd08e60`.

### 2026-05-04 — Whole-blob DPAPI (CurrentUser) for `accounts.dat`; favorites and private servers stay plaintext
**Context:** v1.1 simplicity over per-cookie envelopes; bookmarks are not account access.
**Consequences:** Every vault write is a full decrypt/encrypt cycle; a copied `accounts.dat` is unreadable elsewhere by design; cross-machine transport needed its own passphrase bundle (2026-05-21). Per-cookie encryption never shipped.
**Evidence:** `Core/AccountStore.cs` header; commit `0976b5c`; canonical spec §11.

### 2026-05-04 — Launch through the `roblox-player:` protocol handler via ShellExecute; documented endpoints only
**Context:** Same path Bloxstrap and earlier managers use; respects whatever handler the user has.
**Consequences:** `LaunchResult.Started.Pid` is the short-lived launcher, so `RobloxProcessTracker` matches the real client by start time; strap detection is needed.
**Evidence:** `Core/ProcessStarter.cs`, `Core/RobloxLauncher.cs`, `Core/BloxstrapDetector.cs`.

### 2026-05-04 — No MVVM framework; hand-rolled `INotifyPropertyChanged` and `RelayCommand`
**Context:** Dependency-lean product with an auth-cookie threat model.
**Consequences:** Manual `OnPropertyChanged` fan-out; `RelayCommand.Execute` is `async void` and swallows exceptions, reporting through `OnExceptionSwallowed`.
**Evidence:** `App/ViewModels/RelayCommand.cs`; commit `d2b5062`.

### 2026-05-04 — Closed result unions at the orchestration boundary, typed exceptions at the Roblox boundary
**Context:** Launch had three user-distinct outcomes the row renders differently (`Limited` made it four on 2026-06-30).
**Consequences:** `LaunchResult` (later also `CookieCaptureResult`, `StartupGateResult`, `LaunchTarget`, `StatsEvent`) is an abstract record with sealed nested cases; `RenameResult` and `ImportMergeResult` are flat sealed records; `CookieExpiredException` (401) and `SessionLimitedException` (403) are converted by `RobloxLauncher`.
**Evidence:** `Core/LaunchResult.cs`; commit `cf5cd49`.

### 2026-05-04 — Serilog file sink behind `ILogger<T>`; Microsoft/System namespaces capped at Warning (2026-07-03)
**Context:** HttpClientFactory at Debug wrote ~90% of a 15 MB day.
**Consequences:** All code takes `ILogger<T>`; the version stamp only appears because it is in the output template.
**Evidence:** `App/Logging/AppLogging.cs`; commits `82462d2`, `659e2b2`.

### 2026-05-04 — Velopack update check at startup with a 24 h debounce; download/apply deferred
**Context:** Build-plan item 10; item 11 was to wire download + apply through the tray menu.
**Consequences:** At HEAD the app still only logs "Update available". Whether Velopack's `Update.exe` closes the loop out of process is unverified (2026-08-30).
**Evidence:** `App/Updates/UpdateChecker.cs`; commit `65b9fdc`.

### 2026-05-04 — Store assets come from the 626labs-design skill; the MSIX build refuses placeholders
**Context:** SnipSnap retro: a broken-looking tile is disqualifying even if the app works.
**Consequences:** `build-msix.ps1` logo gate; `-AllowPlaceholders` is an explicit opt-in to a bad Store moment. The shipped mark is script-rendered (`generate-store-assets.ps1`) as the final brand art, not a stub.
**Evidence:** commit `523fe65`; `scripts/build-msix.ps1`.

### 2026-05-04 — Sanduhr-style JSON theme store with a fixed ten-slot contract
**Context:** User themes without a UI flow; dropping a file is the install gesture.
**Consequences:** Ten slots is an invariant; every later colour (interactive edge, on-magenta) is derived, never an eleventh slot.
**Evidence:** `Core/Theming/Theme.cs`; commit `6d45685`.

### 2026-05-06 — Custom `Program.Main` so `VelopackApp.Build().Run()` runs before WPF
**Context:** Velopack install/uninstall/restart hooks must fire first.
**Consequences:** `<StartupObject>` in the csproj; the portable AUMID override later hung off the same Main.
**Evidence:** `App/Program.cs`; commit `faf2782`.

### 2026-05-06 — Velopack `Setup.exe` on GitHub Releases as the clan-direct channel; a tag push yields a draft
**Context:** Store latency; EU clan members; a botched build must not go live silently.
**Consequences:** `release.yml` drafts; running the local script for the same tag duplicates the draft.
**Evidence:** commit `4563e3c`; `.github/workflows/release.yml`.

### 2026-05-06 — Product renamed ROROROblox → RORORO for Store policy 10.1.1.1; User-Agent token became `RORORO/<version>`
**Context:** Partner Center rejection.
**Consequences:** Repo, assembly, data folder and log names keep `ROROROblox`; Velopack `packId RORORO` and `PublisherDisplayName "626Labs LLC"` (no space) are frozen identities. The v1.15 naming pass (2026-08-04, `32374c4`) made the human-facing name `RoRoRo`. The UA token was ruled the intended contract on 2026-08-30.
**Evidence:** commits `b880a53`, `eb579ed`, `ad61890`; `App/App.xaml.cs` typed-client registrations; `docs/store/rename-plan.md`.

### 2026-05-06 — Versions are always `X.Y.Z.0`, csproj and appxmanifest in lockstep
**Context:** Partner Center rejected 1.1.0.1 (non-zero revision), and again 1.3.2.1 on 2026-05-08.
**Consequences:** `finalize-store-build.ps1` hard-fails on a non-`.0` version and patches both files together; resubmissions bump the third component.
**Evidence:** commits `65fea06`, `62f5452`, `c321553`.

### 2026-05-07 — `FramerateCap` in `GlobalBasicSettings_<N>.xml` is the FPS lever, written alongside the FFlag
**Context:** Smoke showed the in-game frame-rate setting beats `DFIntTaskSchedulerTargetFps` for default-config users.
**Consequences:** Two files written per launch; this shared file is what later needed the settle logic (2026-08-02).
**Evidence:** `Core/IGlobalBasicSettingsWriter.cs` banner; commit `f8a8db9`.

### 2026-05-08 — `UseCookies=false` on the `IRobloxApi` HttpClient
**Context:** Every Launch As opened the same account: the default cookie container cached the first `.ROBLOSECURITY`. The 2026-05-08 "already running" gate had diagnosed the wrong cause.
**Consequences:** Every `IRobloxApi` call sets its own `Cookie` header; the spec's premise is recorded as wrong in its banner.
**Evidence:** commit `d2526b8`; `docs/superpowers/specs/2026-05-08-roblox-already-running-detect-design.md` banner.

### 2026-05-08 — MSIX and `Setup.exe` ship self-contained
**Context:** Framework-dependent builds failed on machines without the .NET 10 Desktop Runtime.
**Consequences:** ~90 MB packages; `PublishSingleFile=false` on the Velopack side so it can delta.
**Evidence:** commit `5739214`; `scripts/build-msix.ps1` (`--self-contained true`); `scripts/build-velopack-release.ps1` (`PublishSingleFile=false`).

### 2026-05-08 — Testable orchestration lives in Core even when a spec says App
**Context:** The unit suite references Core; `StartupGate`, `AccountUserIdBackfillService`, `RenameTarget` were specced for App.
**Consequences:** Expect the same placement drift on any pure-logic spec.
**Evidence:** banners on the 2026-05-07 and 2026-05-08 specs.

### 2026-05-09 — Plugins are out-of-process EXEs over named-pipe gRPC; never in-process code
**Context:** Store policy 10.2.2 forbids dynamic code that changes described functionality; the clan wanted macro/AFK features.
**Consequences:** Kestrel + `Microsoft.AspNetCore.App` inside a WPF app; a versioned contract NuGet; the macro wall (the Store binary never synthesizes input; consented plugins may).
**Evidence:** `docs/superpowers/specs/2026-05-09-rororo-plugin-system-design.md` locked decisions; `docs/store/reviewer-letter-1.4.0.0.md`.

### 2026-05-09 — Per-capture GUID WebView2 user-data directories
**Context:** `msedgewebview2` children pinned files in the shared dir, the wipe failed silently, and the second Add Account re-captured the first account's cookie.
**Consequences:** Siblings swept after allocation; the "v1.2 per-account profile" promise was effectively satisfied differently.
**Evidence:** commit `981068a`; `Core/WebView2UserDataDirectory.cs`.

### 2026-05-10 — Plugin identity on the wire is the `x-plugin-id` header; the harness must cover the production shape
**Context:** v1.4's production accessor returned null, so every gated RPC failed; the integration harness hid it by hardcoding an accessor.
**Consequences:** `*_ProductionAccessor_*` harness tests exist; most other harness tests still hardcode the accessor. Identity is spoofable by any same-user process.
**Evidence:** commit `652c43a`; `App/Plugins/CapabilityInterceptor.cs`.

### 2026-05-10 — Integration tests run a real Kestrel + named pipe, in their own project
**Context:** The v1.4 gate needed proof of the real proto, serializer and interceptor pipeline.
**Consequences:** Per-test pipe names; CI must run the whole solution, not unit-only.
**Evidence:** commits `ada5383`, `c05cb2b`; `Harness/EndToEndContractTests.cs`.

### 2026-05-13 — Stamp and defend the account identity in Roblox's `appStorage.json` per launch
**Context:** The chronic captcha cross-branding bug was RoRoRo-side: sibling client writes overwrote the identity.
**Consequences:** One active defender at a time; multilaunch during a Roblox install is a known limitation the install-deferral work addresses.
**Evidence:** commits `8072ee9` (2026-05-13), `6d25f65` (2026-05-21, the install-resilient follow-up); `App/Diagnostics/AppStorageDefender.cs`.

### 2026-05-14 — The sideload dev cert's subject is the Partner Center CN; the keys are still never the same
**Context:** signtool fails 0x8007000b when the manifest Publisher and the signing cert subject differ; the old flow patched the manifest per build.
**Consequences:** No manifest swap for sideload builds (the stale `Package.appxmanifest` comment that says otherwise now causes the error).
**Evidence:** commit `995e79f`; `scripts/generate-dev-cert.ps1`; `docs/store/release-playbook.md`.

### 2026-05-14 — Publish `ROROROblox.PluginContract` to nuget.org; OIDC Trusted Publishing from 2026-07-03
**Context:** Sibling plugin repos needed a consumable package; no stored API key.
**Consequences:** Manual `workflow_dispatch`; versions are immutable; 0.2.0 and 0.5.0–0.7.0 were never pushed.
**Evidence:** commits `b76ad15`, `a9c5e39`, `357c57f`; `.github/workflows/publish-nuget.yml`.

### 2026-05-20 — Presence augments process tracking; a row is Closed only when both agree
**Context:** Clan reports of ghost "Closed" rows: the anti-multilaunch bootstrapper kills the attached pid and respawns the client.
**Consequences:** Presence is authoritative for display, process tracking for actions; a 25 s per-account poll is disclosed in `docs/PRIVACY.md`.
**Evidence:** `docs/superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md`.

### 2026-05-21 — Account transport is a passphrase bundle (PBKDF2-SHA256 600k + AES-256-GCM), no recovery key
**Context:** The clan wanted to move accounts between PCs; the vault is machine-bound by design.
**Consequences:** The only sanctioned path by which cookies leave the machine; wrong passphrase and tamper are indistinguishable on purpose.
**Evidence:** `docs/superpowers/specs/2026-05-21-rororo-account-transport-and-bundle-design.md`; `Core/Transport/AccountTransportService.cs`.

### 2026-05-21 — Install deferral without taking over Roblox's bootstrapper
**Context:** A mid-update multilaunch misrouted identities; Bloxstrap solves it by owning the handler, which violates the no-takeover posture.
**Consequences:** `RobloxUpdateProbe` + `PreWarmGate`; the tracker's attach window stretches to 120 s while the installer runs.
**Evidence:** `docs/superpowers/specs/2026-05-21-rororo-install-deferral-design.md`.

### 2026-05-28 — The singleton name is remote-config data, resolved before acquire; the wire key stays `mutexName`
**Context:** Spec §7.1 and CLAUDE.md had claimed the name was config-driven since v1.1; it was a hardcoded const through v1.6.
**Consequences:** `ResolvedMutexName` seam and a startup-ordering constraint; the spec's `singletonMutexName` rename and "expose the name to plugins" did not ship, and neither is bannered.
**Evidence:** commit `b2119c3`; `docs/superpowers/specs/2026-05-28-remote-config-mutex-name-design.md`; `roblox-compat.json`.

### 2026-05-28 — CI gates on the full solution; typed-client single-constructor guard test
**Context:** A typed-client ctor ambiguity, a non-compiling harness, and a DI break all reached a branch while unit-only CI stayed green.
**Consequences:** "Unit-green is not landable"; test seams on typed clients must be optional parameters, never a second ctor.
**Evidence:** commits `f9017fa`, `580ca6e`; `.github/workflows/ci.yml` header.

### 2026-06-12 — Off-thread readers get an immutable snapshot (`ObservableCollectionMirror`)
**Context:** "Collection was modified" silently killed the v1.5 presence loop.
**Consequences:** Presence, plugin handlers and tracker bridges read `AccountsSnapshot`; a new off-thread reader of `Accounts` is a regression.
**Evidence:** commit `c18bec1`; `Core/ObservableCollectionMirror.cs`.

### 2026-06-30 — A 403 with a valid CSRF token is "Limited", distinct from expired; one rotation retry, never auto-retry
**Context:** Bot-challenge soft locks were being read as expired cookies; a frozen "In game" dot masked it.
**Consequences:** `LaunchResult.Limited`, a magenta row state that auto-heals; re-auth does not clear it.
**Evidence:** commits `a27c387`, `6b02a3d`; `docs/superpowers/specs/2026-06-29-rororo-limited-session-handling-design.md`.

### 2026-07-01 — Activity awareness: core observes only; acting lives in a plugin
**Context:** Batch-launched accounts idle out together and reconnect together; a low-level input hook would read keystrokes system-wide.
**Consequences:** `GetForegroundWindow` + `GetLastInputInfo` correlation; plugin-directed input needs `MarkAccountActive` (2026-07-09).
**Evidence:** `docs/superpowers/specs/2026-07-01-rororo-activity-awareness-design.md`.

### 2026-07-02 — Acquire-first startup gate after Roblox 0.727 became tray-resident
**Context:** A check-then-acquire gate can lose the name between check and acquire.
**Consequences:** `StartupGate.Evaluate` takes the acquire outcome; the model was corrected again on 2026-07-10.
**Evidence:** commits `e4731d5`, `817e8b9`; `docs/superpowers/specs/2026-07-02-rororo-tray-residence-gate-design.md` banner.

### 2026-07-03 — Stable per-account browserTrackerId; in-memory cookie-generation counter
**Context:** A fresh tracker id per launch looked like a new client each time; a presence poll started before a re-auth re-flagged the refreshed session.
**Consequences:** The id lives in the vault but not in exports; the generation is captured before the presence call and compared on the UI thread.
**Evidence:** commits `c28740b`, `ab97f37`.

### 2026-07-04 — The marketplace is gated on a runtime package-identity probe, never a build flag
**Context:** The v1.4 reviewer letter promised the Store binary never reads a curated list from a server.
**Consequences:** `Win32DistributionMode.IsPackaged`; Store users use the web marketplace; the catalog rides every release.
**Evidence:** commit `73761de`; `App/Distribution/IDistributionMode.cs`.

### 2026-07-04 — The MCP connector is a consent-gated plugin in its own repo, not a server inside RoRoRo
**Context:** Automation via Claude is the same posture the plugin system was built for.
**Consequences:** This repo added `GetAccounts` behind `host.queries.accounts` (contract 0.9.0, 2026-08-21/22).
**Evidence:** `docs/superpowers/specs/2026-07-04-mcp-connector-design.md`; commits `64e0c65`, `a39600e`.

### 2026-07-06 — Store OS floor dropped to Windows 10 22H2 (`MinVersion 10.0.19045`)
**Context:** Win10 best-effort support; docs had wrongly said Windows 11 only.
**Consequences:** `docs/index.md` still says Windows 11.
**Evidence:** commit `5410335`; `App/Package.appxmanifest`.

### 2026-07-09 — No default game launches to Roblox home; `DefaultPlaceUrl` becomes vestigial and is deleted 2026-08-21
**Context:** The default game was secretly a requirement, which made Clear default a lie.
**Consequences:** `LaunchTarget.Home` via `launchmode:app`, a new Roblox-protocol dependency; F-093 deleted the setting and the string overload.
**Evidence:** commit `9c6e800` (PR #58); `c66ffc8`; `Core/RobloxLauncher.cs`.

### 2026-07-09 — Trust-aware squad launch: manual `JoinViaFriend` flag, three-phase dispatch, careful mode
**Context:** One account got a CAPTCHA titled with the next account's name; Roblox cross-binds concurrent joins from one device.
**Consequences:** Depends on Roblox's observed gate hierarchy (friend-follow > private-server > public join).
**Evidence:** `docs/superpowers/specs/2026-07-09-trust-aware-squad-launch-design.md`.

### 2026-07-09 — Plugin activity crediting is an explicit `MarkAccountActive` RPC behind its own capability
**Context:** Ur AFK's focus-tap-restore completes between monitor ticks, so the wrong account was credited.
**Consequences:** The competing `ReportAccountActivity` design (same day) was not built; neither spec is bannered.
**Evidence:** `docs/superpowers/specs/2026-07-09-activity-crediting-fix-design.md`; commits `2a97798`, `d17dd2d`.

### 2026-07-10 — The singleton is a name race between Roblox's Event and our Mutex; a peer Mutex holder never blocks
**Context:** Measured against a tray-resident client: the Roblox case never reached `ERROR_ALREADY_EXISTS`; the contested probe used `OpenMutex` only and never fired; Retry needed two presses.
**Consequences:** `MutexAcquireOutcome`, `StartupGateResult.SharedLock`, bounded-poll retry, seamless takeover of windowless tray clients, `IsHeldElsewhere` probing both object types. The canonical spec's body is wrong about the mechanism; only its banner is true.
**Evidence:** commits `b626ae4`, `2faa6c2`; `docs/superpowers/specs/2026-07-10-singleton-name-race-design.md`.

### 2026-07-10 — Streamer mode is persistent costumes, not redaction
**Context:** Blur reads as "something to hide"; promo video and live streams expose the whole roster.
**Consequences:** Every render surface routes names and avatars through `IStreamerIdentityProvider`; `RealRenderName` is what persists.
**Evidence:** `docs/superpowers/specs/2026-07-10-streamer-mode-design.md`; commits `08ec93c`, `4f84905`.

### 2026-07-10 — The capability map is exhaustive and fails closed; agent-ops `StopAccounts` never reaches untracked processes
**Context:** `UpdateUI`/`RemoveUI` shipped ungated because `null` meant both "ungated" and "unknown".
**Consequences:** `AssertExhaustive` before bind; unknown methods are `PermissionDenied`; pids never cross the contract.
**Evidence:** commit `789c5fa`; `App/Plugins/RpcMethodCapabilityMap.cs`.

### 2026-07-11 — arm64 Store MSIX flavor and a native `windows-11-arm` CI lane
**Context:** Partner Center advisory; no Arm dev box exists.
**Consequences:** Every submission uploads two packages; the arm64 lane is the project's only Arm testing.
**Evidence:** commit `94fce55` (PR #66); `.github/workflows/ci.yml`.

### 2026-08-01 — Memory watchdog samples private bytes, warns on three surfaces, never recycles automatically
**Context:** Clients leak ~300 MB/hour idle; working-set trimming blinds that metric on minimized windows.
**Consequences:** Recycle is a user click; the 2026-08-08 headroom scope inverted the cap axis (min, not max) after the cap could never fire on ≥16 GB; the learned footprint (2026-08-21) feeds only the launch advisor and is not persisted.
**Evidence:** `docs/superpowers/specs/2026-08-01-memory-watchdog-design.md` banner; `2026-08-08-rororo-memory-headroom-scope.md`; `Core/Diagnostics/MemoryDefaults.cs`.

### 2026-08-02 — The FPS-cap launch gate was superseded the same day by a settle-across-a-quiet-window design
**Context:** Measurement showed the competing writer is the previous client re-persisting its own cap for ~9 s; our write survived 170 ms.
**Consequences:** `FpsCapSettler` with a proof-of-read gate; the spec's 18 s worst case became `SettleTimeout = 20 s` in code (`a293d6d`), then 45 s (`2325a05`); the 2026-08-01 spec carries a SUPERSEDED banner.
**Evidence:** commits `0052df4`, `b42f9ac`, `2325a05`; both specs' banners.

### 2026-08-02 — Server-instance targeting keeps `(placeId, jobId)` as one record from one presence reading
**Context:** Recycle and Squad Launch matchmade into new servers; mixing the launch place with presence's job id fails because games teleport within a universe.
**Consequences:** A landing miss is a status banner; a full server queues (up to 4 min), never silently matchmakes.
**Evidence:** commit `5818ea8`; `docs/superpowers/specs/2026-08-02-server-instance-targeting-design.md` banner.

### 2026-08-03 — `roblox-compat.json` is detached-signed with ECDSA P-256; verify raw bytes before deserializing
**Context:** The feed can rename the singleton; an unsigned feed on a public URL is a tampering surface.
**Consequences:** Private key exists only as a CI secret; public key pinned in the binary (rotation is a release); missing or bad signature means "no update", never a fallback to unverified content.
**Evidence:** commit `cafa2f9` (PR #77); `Core/RobloxCompatSignature.cs`; `release.yml`.

### 2026-08-03 — Discord ships as two independent off-by-default halves; join confirm keyed on origin; the real application id is committed
**Context:** Supersedes the unmerged May design; a `roblox-rororo:` URI can be fired by any local process; the id travels in every presence payload and is not a secret.
**Consequences:** `discord.dat` is DPAPI; alerts are independent of the app id; the URI scheme registers on every install; the 15 s throttle and webhook retry were not built.
**Evidence:** `docs/superpowers/specs/2026-08-03-discord-presence-alerts-design.md` banner; commits `8c75544`, `59c04fe`.

### 2026-08-04 — Alert cooldown is keyed by (account, kind); the clan webhook gets real names; `WebhookPayload` is a two-string type
**Context:** A memory warning swallowed a genuine drop for the same accounts; a clan channel with masked names is unusable; a webhook post is a broadcast that must never carry a server link.
**Consequences:** A reflection test fails the build if a link-capable field is added.
**Evidence:** `Core/Discord/AlertRouter.cs`, `Core/Discord/WebhookPayload.cs`; commit `4f4ad77` (PR #82).

### 2026-08-04 — A VibeGlow audit produces the findings register; rulings by the user override rows
**Context:** Settings in one place, honest main-window buttons, titles that read like an app.
**Consequences:** Waves 1–21+ from v1.16 through v1.23; a PR that closes a row flips it in the same PR; the two shipped emoji stay and "Multi-Instance" keeps its name (ruled 2026-08-21).
**Evidence:** `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`.

### 2026-08-05 — One component vocabulary in `ControlStyles.xaml`; the interactive edge is derived, not an eleventh slot
**Context:** 63 hand-copied attribute sets drifted; secondary buttons measured 1.14–1.26:1 in every built-in; existing user themes must not be silently repainted.
**Consequences:** Ranks own colour, call sites own layout; user-theme authors are asked once (`EdgeRemediationWindow`).
**Evidence:** `docs/superpowers/specs/2026-08-05-rororo-wave-5-component-vocabulary-scope.md`; commits `bf1fdef`, `deb8d23`.

### 2026-08-09 — Two-phase contrast gate (token arithmetic, then pixels), proven failable
**Context:** Every finding's ratio was unverified arithmetic; nothing in the repo rendered XAML.
**Consequences:** `ContrastPairGateTests` reads the findings register for exemptions; `Sta.cs` is a hand-rolled STA runner (2026-08-10) rather than `Xunit.StaFact`.
**Evidence:** `docs/superpowers/specs/2026-08-09-rororo-rendered-contrast-gate-design.md`; commits `1fcf74d`, `73627db`.

### 2026-08-09 — Tray event subscription becomes a testable table
**Context:** A transposed subscription (Stop all wired to Quit) compiled cleanly and could not be tested.
**Consequences:** `TrayWiring.Connect` + `TrayHandlers`; `TrayWiringTests`.
**Evidence:** commit `f9b6a91`.

### 2026-08-10 — Flatline ships as a real fourth built-in; Cart authors the design directly in `docs/spec.md`, creating an archive obligation
**Context:** Three register rows quoted flatline ratios nothing could reproduce; the v1.17 spec was nearly lost when `docs/spec.md` was overwritten.
**Consequences:** `FlatlineLabGateTests` preserves the adversarial fixture separately; archived specs carry `docs/`-relative links. The Cart loop was retired on 2026-08-30.
**Evidence:** commits `43d224c`, `51f9682`; archive banners on the v1.17–v1.21 specs.

### 2026-08-11 — RoRoRo owns the button `ControlTemplate`; hover and pressed are translucent sheens
**Context:** Every button flashed Aero blue on hover in every theme since v1.1; `Style.Triggers` cannot reach `TargetName=border` setters.
**Consequences:** WPF-UI Button improvements never reach the ranks; `ButtonStateGateTests` forbids literal brushes and Opacity in state setters.
**Evidence:** `docs/superpowers/specs/2026-08-11-rororo-button-vocabulary-design.md`; commit `0f34395`.

### 2026-08-11 — The host pushes its resolved palette to plugins, ungated; discovery is `minHostVersion`
**Context:** Ur Task mirrored built-in palettes by hand and read RoRoRo's settings and themes folder.
**Consequences:** `GetTheme`/`SubscribeThemeChanged`, contract 0.8.0; the wire `contract_version` stays `"1.0"`.
**Evidence:** `docs/superpowers/specs/2026-08-11-rororo-plugin-theme-feed-design.md`; commits `3247232`, `dfb9a0a`.

### 2026-08-12 — Plugin processes are bound to the host by a kill-on-close job object plus a startup sweep
**Context:** Six orphaned `ur-task.exe` processes (~950 MB) with RoRoRo closed; shutdown hooks do not run on a crash.
**Consequences:** `AllowUnsafeBlocks` is on for the whole App project and used only in `PluginJobObject.cs`; the job is unnamed so concurrent RoRoRo instances do not kill each other's plugins.
**Evidence:** commit `410e392`; `App/Plugins/Adapters/PluginJobObject.cs`.

### 2026-08-12 — MSIX packages are named `RORORO-<Flavor>-<arch>-<version>.msix`
**Context:** `dist/` accumulated unversioned packages that looked current and were not.
**Consequences:** `install-local-msix.ps1` was not updated and is stale.
**Evidence:** commit `f15eae7`; `scripts/build-msix.ps1`.

### 2026-08-12 — v1.21.0.0 tagged (published 2026-08-13); v1.18, v1.19, v1.20 were never tagged
**Context:** The Store was parked until the settings backbone was remediated.
**Consequences:** Store users jumped six releases at once; v1.17 had gone out as a pre-release that the updater filters, so the direct channel sat on v1.16 through four releases.
**Evidence:** `docs/feature-ledger.md` header; `git tag`.

### 2026-08-20 — UI marshalling goes through an `IUiDispatcher` seam that runs inline without an Application
**Context:** `Application.Current?.Dispatcher.Invoke` was a silent no-op across the whole suite, so five handler bodies had never executed under test.
**Consequences:** Tests inject a recording dispatcher; `TrayService` still uses the old pattern.
**Evidence:** commit `732e254`; `Core/IUiDispatcher.cs`.

### 2026-08-20 — Windowless leftover Roblox processes are cleared silently; Stop uses `SC_CLOSE` and asks twice before forcing
**Context:** A clean stop always spawns a headless `RobloxPlayerBeta` ~2 s later, so the leftover dialog fired on the ordinary path; `CloseMainWindow` reports "posted", not "closed", so the first Stop click was inert.
**Consequences:** `LeftoverStartupDecision`; `ClientStopSequence`; `autoForceStop` trades Roblox's own settings persistence for immediacy.
**Evidence:** commits `e366b7b`, `75fb205`, `4fb9055`; `docs/superpowers/specs/2026-08-20-wave-8-stop-scope.md`.

### 2026-08-20 — Adopt a self-restarted client only as an unambiguous succession; log the rest
**Context:** A Roblox self-update leaves a bare-titled replacement no row can Stop; re-titling a client the user launched by hand would be a wrong label that gets believed.
**Consequences:** `ClientSuccession` refuses many-to-one; the sweeper runs every 5 s.
**Evidence:** commits `e127d5c`, `cbf227c`; `docs/superpowers/specs/2026-08-14-wave-7-lifetime-scope.md`.

### 2026-08-21 — F-105 root cause: the render harness ran real app startup inside the test host; whole-window gates return to CI
**Context:** "The URI prefix is not recognized" was the single-instance guard calling `Shutdown(0)` inside the tests; nine gates had been skipped on CI since 2026-08-12 for a wedge that was the same defect.
**Consequences:** `App.SuppressStartupForRenderHarness`; `WindowRenderAvailability.SkipReason` (in `Tests/Rendering/WindowRenderFactAttribute.cs`) is always null; `CONTRIBUTING.md`'s skip paragraph went stale (corrected 2026-08-30).
**Evidence:** commits `7dbe997`, `57a7660`, `0416f19`.

### 2026-08-21 — Six modal islands become pages in one `ShellWindow`, after `DiscordConfigService` becomes the single config owner
**Context:** Modality was what made three cached `DiscordConfig` copies safe; the app had zero input bindings.
**Consequences:** `App.OpenShellPage` is the single door; pages implement `IDisposable`; `KeyboardVocabulary` (F-112) ships alongside.
**Evidence:** `docs/superpowers/specs/2026-08-21-rororo-f013-shell-design.md`; commits `ffd9a64`, `50a63a0`.

### 2026-08-21 — The interactive edge is derived against every surface a control lands on; F-050 closes by deriving `OnMagentaBrush`
**Context:** An edge tuned to Navy measured 2.28–2.82:1 on cards; neither White nor Navy reads on magenta in every theme.
**Consequences:** `ContrastPairGateTests.Exemptions` became empty for the first time.
**Evidence:** commit `a38db96`; `App/Theming/ThemeService.cs`.

### 2026-08-22 — Session stats are a durable rollup fed by a decorator over the history store, keyed by ids
**Context:** `session-history.json` held exactly 100 rows spanning 19.9 days; a second call site beside the history write would recreate the F-121 drift shape.
**Consequences:** `session-stats.json`; backfill once (peak concurrency cannot be recovered); the 2026-07-09 presence-driven uptime spec is superseded.
**Evidence:** `docs/superpowers/specs/2026-08-22-rororo-session-stats-design.md`; PR #146.

### 2026-08-23 — WPF-affine DI singletons are constructed through `UiBoundFactory`; history persists `RealRenderName`
**Context:** A threadpool continuation resolved `AlertDispatcher → ITrayService` first and startup died on a cross-thread Freezable (F-122); the history writer was the third site to drift on the rename rule (F-123).
**Consequences:** New UI-owning singletons use the factory; masking is applied at render, keyed on account id.
**Evidence:** commit `0e10bd1` (PR #147); `App/UiBoundFactory.cs`.

### 2026-08-23 — v1.23.0.0 certified and live on the Microsoft Store the day it was submitted
**Context:** Zero-disclosure-change reviewer letter.
**Consequences:** Store and direct channels are both on 1.23; `README.md` "last updated at v1.15" was stale (corrected 2026-08-30).
**Evidence:** `docs/store/submission-packet-1.23.0.0.md`; maintainer confirmation 2026-08-30.

### 2026-08-30 — Onboarding-docs rulings (this file's seed session)
**Context:** A repo-wide discovery pass surfaced contradictions between the docs and the tree.
**Consequences:** The User-Agent contract is `RORORO/<version>`. The macro wall is worded "the Store binary never synthesizes input; consented out-of-process plugins may". The account-groups spec is abandoned; FluentWindow-for-modals, the plugin UI host, and the Discord throttle/retry are parked. The Cart per-cycle files (`docs/scope.md`, `prd.md`, `spec.md`, `checklist.md`, `reflection.md`) are retired as historical snapshots. The Feature Ledger moved to `docs/feature-ledger.md`; `docs/features.md` is now the registry. Auto-update delivery, packaged-build HKCU writes, compat-feed pushes between tags, and the history end-stamp rule remain open questions (see CLAUDE.md).
**Evidence:** this session; `CLAUDE.md`, `docs/features.md`, `docs/architecture.md`.

### 2026-08-30 — Direct-download installs are confirmed check-only; docs stop promising auto-update
**Context:** README, `release.yml` and the playbook promised "auto-update within 24h". Velopack's API docs (`Velopack.xml` in the NuGet cache) say `CheckForUpdatesAsync` only returns an `UpdateInfo`; applying needs `DownloadUpdatesAsync` then `ApplyUpdatesAndRestart`, and `SetAutoApplyOnStartup` only applies already-downloaded packages. Nothing in `src` downloads.
**Consequences:** Direct-download users stay on the version they installed until they run a newer `Setup.exe`; the Discord post must say so. Wiring download + apply (build-plan item 11, open since 2026-05-04) is the top product follow-up.
**Evidence:** `App/Updates/UpdateChecker.cs`; `~/.nuget/packages/velopack/0.0.1298/lib/*/Velopack.xml`; `README.md`, `docs/store/release-playbook.md`, `docs/PRIVACY.md` corrected today.

### 2026-08-30 — The plugin pipe's per-user ACL is verified, not assumed
**Context:** Docs asserted the pipe was ACL'd to the current user; no code sets a `PipeSecurity`.
**Consequences:** Verified on the live `rororo-plugin-host` pipe: owner = current user, one ACE `Allow FullControl` to that user, nothing inherited. The guarantee is Kestrel's `CurrentUserOnly` default, so a framework default change would widen it silently; a test that connects and reads the DACL would close that.
**Evidence:** `PipesAclExtensions.GetAccessControl` over a `NamedPipeClientStream` on 2026-08-30; `App/Plugins/PluginHostStartupService.cs`.

### 2026-08-30 — A compat-only workflow gives the signed feed a no-binary push path
**Context:** The only signed upload was `release.yml` on a tag push, so "a config update within hours" meant a release cycle; the key exists only as a CI secret.
**Consequences:** `.github/workflows/compat.yml` (`workflow_dispatch`, Windows runner because `CompatSigner` references Core) validates the four keys, signs with `ROBLOXCOMPAT_SIGNING_KEY`, and `gh release upload --clobber`s to the current latest release. Exercised the same day: the first dispatch (run 33330524023) no-op re-signed v1.23.0.0's assets, and a client-style download of the new pair verified against the pinned key via `RobloxCompatSignature.Verify` — the acceptance the local suite can only test for rejection. Clients apply on next start (today's startup logs already show `source="RemoteConfig"`, a verified fetch).
**Evidence:** `.github/workflows/compat.yml`; `tools/CompatSigner/Program.cs`.

### 2026-08-30 — Packaged builds: registry writes are virtualized (Join/run-on-login inert), file writes are not (one shared data folder)
**Context:** Run-on-login and the `roblox-rororo:` scheme write HKCU at runtime; the manifest declares only `runFullTrust`.
**Consequences:** Verified live the same day. The Store 1.23 build, launched alone, registered its URI schemes — and the real `HKCU\Software\Classes` entries still pointed at the dev build afterward: packaged registry writes land in the package's virtual hive, invisible to Explorer and Discord, so Join-by-URI and run-on-login are inert on Store/sideload installs (fixes: `uap:Protocol` and `StartupTask` declarations; not scheduled). File writes are NOT virtualized: the same run wrote its startup banner, `last-known-mutex.txt` and `last-update-check.txt` into the real `%LOCALAPPDATA%\ROROROblox`, so every install type shares one data folder and a Store uninstall leaves the vault behind — `docs/PRIVACY.md`'s contrary claims corrected.
**Evidence:** live run of `626LabsLLC.RoRoRoBlox` 1.23.0.0 with the dev build closed, plus registry and package-folder inspection, 2026-08-30.

### 2026-08-30 — Stale in-code comments corrected in one sweep; a corrected comment names the date it went stale
**Context:** The onboarding pass found ~25 comments describing retired behaviour (theme brushes "mutated", Tools "is a Menu", sideload CN swap, memory settings "file-only", F-105-era harness headers, "empty" appsettings, capability map "crashes the app", tray "placeholder" icons, per-plugin pipes, and others).
**Consequences:** Comment-only edits across App, Core, Tests, scripts and docs; the full suite was re-run because the fence tests read source text. The convention going forward: when a comment is corrected, say what it used to claim and when it stopped being true, so the next reader can date it.
**Evidence:** the 2026-08-30 commit touching those files; `CLAUDE.md` "Open questions and known stale spots".

### 2026-08-30 — The history end-stamp joins the both-signals rule; never-attached launches leave uptime (F-125)
**Context:** `RecordSessionEndAsync` fired unconditionally on `ProcessExited` since `009866a` (2026-05-04), sixteen days before the v1.5 rule that a row is Closed only when presence and process tracking agree, and was never revisited; the attach-failed path stamped an end with the failure time beside a comment claiming it did not. Both fed the v1.23 stats page: a bootstrapper-respawned client's uptime ended at the old pid's death, and every "Never connected" launch added a 30-120 s phantom session. No test had ever driven the path (the VM's history fake threw and the fire-and-forget call swallowed it).
**Consequences:** The end-stamp now fires from exactly the branches that stamp `LastClosedAtUtc`; `ISessionHistoryStore` gained `MarkOutcomeAsync` (hint, null end, nothing folds into stats) for launches that never ran; the backfill no longer counts hinted end-less rows as missing ends. Rows written before the fix keep their old stamps. Register row F-125 filed and closed in the same change.
**Evidence:** `Core/ISessionHistoryStore.cs`, `Core/SessionHistoryStore.cs`, `Core/StatsRecordingSessionHistoryStore.cs`, `Core/SessionStatsBackfill.cs`, `App/ViewModels/MainViewModel.cs`; `Tests/SessionHistoryEndStampTests.cs` (7 tests); full suite 1906 + 24 green after.
### 2026-08-30 — Packaged activation goes through the platform's extension points; the registry-virtualization opt-out is rejected
**Context:** The same-day finding above left Join-by-URI and run-on-login inert on packaged installs and said "not scheduled"; it was scheduled that afternoon as the v1.24.0.0 headline.
**Consequences:** The manifest gains `uap10:Protocol` entries for `roblox-rororo` and `discord-<appid>` (`Parameters="%1"`, so the URI arrives as the exact argv shape the unpackaged registry command produces — `JoinUriParser` stays the single inbound path) and a `desktop:StartupTask` (`TaskId RoRoRo`, disabled by default). `PackagedStartupRegistration` drives it over `Windows.ApplicationModel.StartupTask`, which forced the App/Tests/PluginTestHarness TFMs from `net10.0-windows` to `net10.0-windows10.0.19041.0`. `IDistributionMode` moved into DI and now picks the startup implementation and gates the HKCU scheme writes; Lachee's `RegisterUriScheme` still runs everywhere because its internal flag gates `Subscribe(Join)`. `DisabledByUser` surfaces as a dialog naming Settings > Apps > Startup — Windows owns that state and `RequestEnableAsync` cannot flip it. Rejected: `desktop6:RegistryWriteVirtualization` (requires the restricted `unvirtualizedResources` capability, documented for Microsoft-partner games only — and would have left commands pointing at versioned `WindowsApps` paths that die on every Store update) and manifest `Enabled="true"` (forces the product default from off to on).
**Evidence:** `docs/superpowers/specs/2026-08-30-packaged-activation-design.md`; `App/Package.appxmanifest`; `App/Startup/PackagedStartupRegistration.cs`; `Tests/PackagedActivationTests.cs` (manifest fence pins the task id and both scheme names to the constants and `appsettings.json`); suite 1918 + 24 green; the packed sideload MSIX's `AppxManifest.xml` carries all three extensions.
