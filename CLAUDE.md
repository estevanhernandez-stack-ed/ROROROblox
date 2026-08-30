# RoRoRo (repo: ROROROblox)

> **Persona:** inherits **The Architect** from `~/.claude/CLAUDE.md`. Nothing below re-establishes it.

RoRoRo is a Windows desktop app that runs several Roblox clients side by side, each signed in as a different saved account. Free, a 626 Labs product, shipped to a Pet Sim 99 clan and the Microsoft Store (ID `9NMJCS390KWB`, on v1.23.0.0). The user-facing name is **RoRoRo**; `ROROROblox` and `RORORO` stay in identifiers, paths, the Velopack packId, and the HTTP User-Agent.

Maps live next door: [docs/architecture.md](docs/architecture.md) (modules, startup order, data flow), [docs/features.md](docs/features.md) (every feature: paths, gating, status), [docs/decisions.md](docs/decisions.md) (why; append-only), [docs/feature-ledger.md](docs/feature-ledger.md) (what shipped when).

## Build, test, run (verified 2026-08-30, SDK 10.0.203)

```powershell
dotnet build ROROROblox.slnx -c Release                              # 0 errors; ~43 warnings are known noise
dotnet test  ROROROblox.slnx -c Release --no-build                   # 1899 unit + 24 harness pass; 1 harness [Skip] by design
dotnet test  src/ROROROblox.Tests/ -c Release --no-build             # unit only
dotnet test  src/ROROROblox.PluginTestHarness/ -c Release --no-build # named-pipe gRPC integration only
dotnet run --project src/ROROROblox.App                              # not re-verified this pass: the single-instance guard surfaces an existing window
powershell -ExecutionPolicy Bypass -File .claude/hooks/install.ps1   # once per box: pre-commit secret scan + local-path guard
```

- **Always name `ROROROblox.slnx`.** Qodo regenerates a gitignored legacy `ROROROblox.sln` beside it; bare `dotnet build` errors MSB1011 while both exist. CI runs the whole solution on x64 and native arm64; unit-green is not landable.
- **A running dev-build RoRoRo locks `bin\Debug`**, so a Debug build fails at the copy step. Build Release or quit it from the tray. `Get-Process ROROROblox.App | Select-Object Path` tells you which build is up; a Store build and a dev build can coexist.
- There is no `.editorconfig`. Style is csproj flags (`Nullable`, `ImplicitUsings`) plus the fence tests.
- Tests read the source tree from disk (they walk up to the `.slnx`), and `ContrastPairGateTests` parses `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`. A docs edit can fail the suite.

**Ship:** push tag `vX.Y.Z.0` → `release.yml` drafts the Velopack release, then signs and attaches `roblox-compat.json` + `.sig` + `plugins-catalog.json`. Do **not** also upload a local `vpk pack` for that tag (`vpk upload github` / `gh release create` against a release `release.yml` already drafted makes a duplicate). `release.yml` runs the tests but not the `guards` job, so get `ci.yml` green on main first. Leave the manifest patched through the sideload build; `-RestoreManifest` is for iterating, not mid-flow. Store MSIX: `powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 -Version x.y.z.0 -IdentityName 626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName "626Labs LLC"`, then again with `-Architecture arm64`; ship both. Sideload: `scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword <pwd>`. Runbook: `docs/store/release-playbook.md`. Compat-only push (no binary): edit `roblox-compat.json` on main, then Actions → **compat** → Run workflow; it signs and re-attaches the json + `.sig` to the current latest release.

## Hard rules

- **The singleton is a name race, not a lock** (our Mutex vs Roblox's Event; see [architecture.md](docs/architecture.md#the-two-core-ideas)). `ERROR_ALREADY_EXISTS` means a peer tool holds it and multi-instance already works; never block on it.
- **The mutex name comes from `roblox-compat.json`** (remote, signed): resolution is remote → last-known-good cache (`last-known-mutex.txt`) → the hardcoded default in `MutexHolder`. Never hardcode it elsewhere.
- **`UseCookies=false` on the `IRobloxApi` HttpClient is load-bearing.** With a cookie container every alt launches as the first account. No test guards it; a refactor to a shared handler regresses it with a green suite.
- **User-Agent is `RORORO/<version>`.** No `Mozilla/`, no browser spoofing, documented public endpoints only. (The plugin installer sends `ROROROblox-PluginInstaller/<version>`.)
- **Never commit `dev-cert.pfx`/`.cer`, `accounts.dat`, `consent.dat`, `discord.dat`, `webview2-data/`, `/plugins/`, `spike/`, or any `.ROBLOSECURITY` value.** The pre-commit hooks and the CI `guards` job fail on cookie prefixes, key files, and `c:\Users\<name>\` paths. Test fixtures use obviously fake cookies.
- **The macro wall:** the Store binary never synthesizes input or injects into the client. Consented out-of-process plugins (Ur Task, Ur AFK, ur-mcp) may; that is what the plugin system exists for (Store policy 10.2.2). Core observes, plugins act.
- **Typed HttpClient classes have exactly one applicable ctor and take `ILogger<T>`**, or startup crashes at resolve time with a green suite (`TypedHttpClientRegistrationTests`).
- **Themed brushes are replaced, not mutated.** Reference them and the type-ladder tokens with `DynamicResource` only. `ControlStyles.xaml` merges after WPF-UI's dictionaries.
- **A button may not paint itself** (`ButtonRankFenceTests`); hover/pressed are sheens, never Opacity or a Chrome repaint (`ButtonStateGateTests`). A button that needs triggers uses `<Button.Style><Style BasedOn="{StaticResource …ButtonStyle}">` with Visibility/IsEnabled setters only. Fences have vacuity floors and ratchet ceilings that move in the same commit as the change: `AccessibleNamingFenceTests` unnamed ceiling 1 (asserted as equality), `ThemedStatusColourTests` literal ceiling 21, `TypeLadderFenceTests` 3 raw sizes.
- **Off the UI thread read `MainViewModel.AccountsSnapshot`, never `Accounts`.** WPF-affine singletons register through `UiBoundFactory`. Marshal through `IUiDispatcher`, not `Application.Current?.Dispatcher`.
- **Startup order is load-bearing:** theme → resolve mutex name → `TryAcquire` → gate; the plugin pipe binds before the gate modals, autostart after. Resolving `IMutexHolder` earlier freezes the hardcoded name.
- **Every gRPC method needs an `RpcMethodCapabilityMap` entry.** Absence is denial. A missed entry fails `RpcMethodCapabilityMapTests` and the harness's `CapabilityMap_CoversEveryHostMethod`; at runtime the bind task faults, is logged at Debug, and plugins are silently off for the session (the code comments saying it "crashes" are stale). A new RPC also needs a `PluginCapability` entry, a `PluginHostService` override, and usually a provider interface added as an optional ctor parameter so the two test construction sites keep compiling.
- **A new modal must be linked into `src/ROROROblox.Tests/ROROROblox.Tests.csproj`** or `ModalDefaultButtonSafetyTests` never sees it; a new window must be listed in `WindowChromeFenceTests`.
- **A test that builds a `MainViewModel` must dispose the window decorator and call `StopPeriodicRefresh()`;** two ctor timers otherwise leak into other tests.
- **A new setting** is a default parameter on the private `SettingsBlob` record at the bottom of `Core/AppSettings.cs` plus an `IAppSettings` member. Four test files carry private fakes of `IAppSettings` (`MainViewModelTests`, `RobloxLauncherTests`, `StreamerIdentityProviderTests`, `Discord/DiscordTestHarness`) and stop compiling until updated; `SettingsReachabilityTests` requires the key to be reachable from a control or allow-listed.
- **Version lives in the csproj `<Version>`, `Package.appxmanifest`, and the tag;** only `finalize-store-build.ps1` syncs the first two. Fourth component is always `0`. `packId RORORO` and `PublisherDisplayName "626Labs LLC"` (no space) are frozen identities.
- **A GitHub pre-release reaches nobody** (`GithubSource(prerelease:false)`).
- **Don't rewrite the canonical spec on drift; banner-correct it at the top.** Canonical: `docs/superpowers/specs/2026-05-03-rororoblox-design.md` (its body is wrong about the mutex; its banners are right). Later cycle specs sit beside it, dated.
- **Findings register** (`docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`): a PR that closes a row flips it in the same PR; verify against the tree, never a changelog; re-record counts with direction; the "Rulings by the user" section overrides rows (the two emoji stay; "Multi-Instance" keeps its name).
- **Store assets go through the `626labs-design` skill;** `build-msix.ps1` refuses placeholder logos. The Store-signed cert and the sideload cert are never the same key.
- **No end-to-end automation against real roblox.com.** Manual smoke on a clean VM.
- `MultiBloxy.exe` and `PROVENANCE.txt` are immutable reference files.

## Voice and brand (README, Store listing, modals, About)

Cyan `#17d4fa` + magenta `#f22f89` on navy `#0f1f31`; Space Grotesk display, Inter body, JetBrains Mono meta. Tagline *Imagine Something Else.* Builder-to-builder, second person, sentence case, verdict first, specific over generic, no emoji in UI copy, no "seamlessly / unlock / empower". Clan-facing copy (Discord posts, the SmartScreen walkthrough) is the same voice, warmer, no jargon. Tokens: `~/.claude/skills/626labs-design/`.

## Decisions

Log significant decisions to the 626 Labs dashboard (`mcp__626labs-cloud__manage_decisions log`) and mirror them in [docs/decisions.md](docs/decisions.md). The bar: would someone asking "why this approach?" in six months want it? Especially architecture choices, Roblox-side compatibility events (mutex rename, endpoint shifts, handler behaviour), distribution outcomes (Store accept/reject, cert rotation), and deviations from the canonical spec. Skip the routine.

## Open questions and known stale spots (2026-08-30)

- **Auto-update: answered, check-only.** Velopack's own API docs: `CheckForUpdatesAsync` only returns an `UpdateInfo`; applying needs `DownloadUpdatesAsync` then `ApplyUpdatesAndRestart`, and the "apply on startup" option only applies packages already downloaded. Nothing in `src` downloads, so direct-download installs stay on the version they installed until the user runs a newer `Setup.exe`. Wiring download + apply (build-plan item 11, open since 2026-05-04) is the top product follow-up; docs no longer promise auto-update.
- **Plugin pipe ACL: answered, verified live.** On 2026-08-30 the running host's `rororo-plugin-host` pipe had owner = current user and exactly one ACE, `Allow FullControl` to that user (Kestrel's `CurrentUserOnly` default). No code sets it; a Kestrel default change would silently widen it.
- **Compat feed ops: answered and exercised.** `.github/workflows/compat.yml` (manual) signs and re-attaches `roblox-compat.json` + `.sig` to the current latest release; first dispatched 2026-08-30 as a no-op re-sign of v1.23.0.0, and the re-downloaded pair verified against the pinned key. Failure shape is unchanged: a bad signature or a slow startup network keeps the old name via `last-known-mutex.txt` at Debug level, and `knownGoodVersionMax` is still 0.729.24 from 2026-07-10.
- **Packaged builds: answered, verified live, then fixed the same day.** The Store 1.23 build was launched alone on 2026-08-30: its startup registered the URI schemes, yet the real `HKCU\Software\Classes` entries still pointed at the dev build — packaged registry writes land in the virtual hive, so Join-by-URI and run-on-login were inert on Store/sideload installs through v1.23. v1.24 routes both through the manifest (`uap10:Protocol` ×2 with `Parameters="%1"`, `desktop:StartupTask` behind `PackagedStartupRegistration`; spec `docs/superpowers/specs/2026-08-30-packaged-activation-design.md`); the live packaged smoke passed the same day (`docs/store/smoke-2026-08-30-packaged-activation.md`). File writes are NOT virtualized: the same run wrote its log banner and cache files into the real `%LOCALAPPDATA%\ROROROblox`, so Store and dev builds share one data folder and **a Store uninstall does not remove the vault** (`docs/PRIVACY.md` corrected).
- **History end-stamp: fixed (F-125, 2026-08-30).** The end now fires from exactly the branches that stamp the row's close (both signals for presence-capable accounts, exit alone for the rest), and a never-attached launch keeps a null end via `MarkOutcomeAsync` instead of a phantom 30-120 s session. `SessionHistoryEndStampTests` covers store, decorator, backfill and VM. Rows written before the fix keep their old stamps.
- **Code discrepancies left alone:** `ThemeService.QuestionFor` previews the edge against Navy only while `ApplyTo` derives against Navy and RowBg (`EdgeQuestion.cs` documents the Navy-only contract); `DiagnosticsCollector` re-implements the installed-version scan instead of calling `RobloxCompatChecker`.
- **Stale comments were corrected on 2026-08-30** (theme "mutates brushes", Tools "is a Menu", the manifest CN swap, `IAppSettings` "no UI", the F-105-era harness headers, "empty" appsettings, the capability map "crashes", tray placeholders, and a dozen more); each corrected comment now names the date it went stale. `docs/PRIVACY.md` and `docs/store/release-playbook.md` were patched the same day; PRIVACY's Store-uninstall claim was disproven by the packaged-build live test and corrected.
- **Docs to treat as history:** `docs/scope.md`, `prd.md`, `spec.md`, `checklist.md`, `reflection.md` (retired Cart snapshots, v1.18/v1.21), `docs/superpowers/plans/`, `docs/superpowers/HANDOFF-*.md`, `docs/testing/`, `docs/security-audit-2026-05-04.md`, `docs/build-story-*.md`.
