# RoRoRo — Feature Ledger

> **What this is.** The canonical inventory of every user-facing feature RoRoRo ships, with the version it landed in and where it lives in the tree. Three consumers: the 626labs.dev product page (`626labs-hub/rororo.html`), vibe-iterate (this is the shipped-feature baseline `/vibe-iterate:competitive` and `/vibe-iterate:feature-add` diff against), and marketing copy (Store listing, TikTok, Discord posts).
>
> **Maintenance rule.** Update this file in the same session a release tags — add the row, move anything from In-flight to its area table, bump the Current version line. A ship that outruns its ledger costs a re-discovery later (see the Projects keystone's 2026-07-06 Sanduhr case).
>
> **Version attribution.** Best-effort from tags, release runbooks, and commit history. There is no v1.2.x or v1.3.0.0 tag — those dev cycles folded into the first published v1.3 tag (`v1.3.1.0`).

**Current shipped version: `v1.12.0.0`** (tagged 2026-08-01, Store candidate). Headline: the memory watchdog. Store ID `9NMJCS390KWB`, listed as "RORORO."

## Version timeline (quick map)

| Version | Headline |
|---|---|
| v1.1 | The core: multi-instance mutex hold, account vault, Launch As, tray, Velopack, remote compat config, Squad Launch + Friend Follow v1, Store submission |
| v1.2 (folded into v1.3.1) | Per-account FPS limiter, Bloxstrap detection |
| v1.3.x | Join by link, local renames, default-game widget, compact mode, userId backfill |
| v1.4 | Plugin system: named-pipe gRPC host, consent sheets, SHA-verified installer; Ur Task ships |
| v1.5 | Presence ("which game" per row), account tags, batch-launch skip reporting |
| v1.6 | Saved private servers, account export/import, tag filter |
| v1.7 | Install-deferral / pre-warm launch, strap-aware launch, presence resilience |
| v1.8 | Startup gate (tray-residence detection), idle awareness, Limited-by-Roblox state, Preferences, activity query for plugins; Win10 22H2 floor (Store) lands v1.9 |
| v1.9 | In-app plugin marketplace, friends-from-main picker, Win10 22H2 Store floor |
| v1.10 | Trust-aware Squad Launch (3-phase + careful mode), seamless singleton takeover, default private server, launch-to-home, plugin agent-ops (MarkAccountActive, StopAccounts) |
| v1.11 / v1.11.1 | Streamer mode (98-name pool, 12 avatars, reveal-only server links), portable-build taskbar identity, real-version diagnostics fix |
| v1.12 | Memory watchdog: per-account memory, RAM-exhaustion warning, one-click Recycle, stray cleanup, contract 0.7.0 memory-pressure capability. Also Arm64 Store MSIX flavor + native Arm64 CI lane (merged post-v1.11.1, first shipped here). |

## Multi-instance core

| Feature | Description | Version | Code |
|---|---|---|---|
| One-click multi-instance | Squats the Roblox singleton name (`Local\ROBLOX_singletonEvent` by default) so multiple clients run side by side; tray toggle on/off. No injection, no client modification. | v1.1 | `src/ROROROblox.Core/MutexHolder.cs`, `src/ROROROblox.App/Tray/` |
| Remote mutex-name resolution | The mutex name resolves remote config → last-known-good cache → hardcoded default, 2s-bounded and degrade-safe — a Roblox rename ships as a config update, not a binary. (Heuristic *auto-detection* of a rename is backlog, not this.) | v1.1 | `src/ROROROblox.Core/RobloxCompatChecker.cs` (`ResolveMutexNameAsync`), `MutexNameSource.cs` |
| Launch As (per-account quick launch) | Launches a saved account via Roblox's documented cookie → auth-ticket → `roblox-player:` URI flow. | v1.1 | `src/ROROROblox.Core/RobloxLauncher.cs`, `RobloxApi.cs` |
| Launch multiple (batch) | Launches every eligible account in one pass with ~5s inter-launch throttle; reports dispatched vs skipped. | v1.1; skip reporting v1.5 | `src/ROROROblox.App/ViewModels/MainViewModel.cs`, `LaunchEligibility.cs` |
| Version-drift banner | Warns when installed Roblox is outside the known-good range from remote config. | v1.1 | `src/ROROROblox.Core/RobloxCompatChecker.cs` |
| Bloxstrap/Fishstrap awareness | Detects a registered strap handler; skips pre-warm, warns on FPS-cap interplay. | v1.2 detect; v1.7 strap-aware skip | `src/ROROROblox.Core/BloxstrapDetector.cs` |
| Install-deferral / pre-warm launch | On a pending Roblox update: launch first client, hold the batch until installer clears; "Roblox is updating — hold on" UX. | v1.7.0 | `src/ROROROblox.Core/Diagnostics/RobloxUpdateProbe.cs`, `src/ROROROblox.App/ViewModels/PreWarmGate.cs` |
| Tray-residence startup gate | Detects a windowless Roblox holding the lock; offers "Close Roblox for me" / Retry / continue; live contested-lock banner. | v1.8.0 | `src/ROROROblox.Core/Diagnostics/StartupGate.cs`, `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.*` |
| Seamless singleton takeover | Silently closes a tray-only client, grabs the lock, re-spawns a tray client — no popup unless a real game window is open. | v1.10.0 | `src/ROROROblox.Core/Diagnostics/SeamlessTakeover.cs`, `RobloxTrayLauncher.cs` |
| "Start anyway" escape hatch | Proceed without the lock when another compatible tool already holds it. | v1.1; restored v1.10.0 | `src/ROROROblox.App/AppLifecycle/BlockedStartupDecision.cs` |
| Leftover-process cleanup | Surfaces orphaned Roblox processes from a prior session; continue or clean up. | v1.8.0 | `src/ROROROblox.App/Modals/LeftoverProcessesWindow.*` |

## Account management

| Feature | Description | Version | Code |
|---|---|---|---|
| Embedded login capture | Add alts via WebView2 Roblox login; only the `.ROBLOSECURITY` cookie is captured — the password never touches the process. | v1.1 | `src/ROROROblox.App/CookieCapture/` |
| DPAPI-encrypted vault | Cookies in `accounts.dat`, whole-blob DPAPI, tied to the Windows user — a copied file won't decrypt elsewhere. | v1.1 | `src/ROROROblox.Core/AccountStore.cs` |
| DPAPI-corrupt recovery | Detects an undecryptable vault; Start Fresh / Quit flow. | v1.1 | `src/ROROROblox.App/Modals/DpapiCorruptWindow.*` |
| Session-expired re-auth | Yellow row state + re-auth via the login modal; fresh cookie, no data loss. | v1.1 | `src/ROROROblox.App/ViewModels/MainViewModel.cs` |
| Main account + tray double-click | Designate a main; double-click the tray to launch it; optional auto-launch on startup. | v1.1 | `src/ROROROblox.App/Tray/` |
| Per-account FPS limiter | Per-row FPS-cap dropdown writing Roblox's settings pre-launch; Bloxstrap-conflict warning. | v1.2 line | `src/ROROROblox.Core/FpsPresets.cs`, `ClientAppSettingsWriter.cs` |
| Local rename overlay | Right-click Rename on any account/game/server; local display name everywhere, Roblox-side names untouched. | v1.3.x | `src/ROROROblox.Core/RenameDispatch.cs`, `src/ROROROblox.App/Modals/RenameWindow.*` |
| Account tags + filter | Free-text tag chips (PS99, RCU, …) with a filter box. | tags v1.5.0; filter v1.6.0 | `src/ROROROblox.Core/Account.cs` |
| Roblox userId persistence + backfill | Stores each account's userId (backfilled for legacy vaults) to power presence/friends. | v1.3.x | `src/ROROROblox.Core/AccountUserIdBackfillService.cs` |
| Account export / import | All accounts to one passphrase-encrypted file; merge-import elsewhere, dupes skipped. Fully offline. | v1.6.0 | `src/ROROROblox.Core/Transport/` |
| Presence-driven game status | Each row shows the real game from Roblox presence ("In Pet Sim 99") on ~25s poll. | v1.5.0 | `src/ROROROblox.Core/Diagnostics/PresenceService.cs` |
| False-"Closed" OR-logic | Row stays active if either presence or window tracking says so. | v1.5.0; hardened v1.7.1 | `PresenceService.cs`, `RobloxProcessTracker.cs` |
| Limited-by-Roblox state | Detects soft-locks, magenta dot, holds the account out of launches, auto-clears. | v1.8.0 | `src/ROROROblox.Core/SessionLimitedException.cs` |
| Idle awareness | Per-row idle chip, amber past threshold, one tray notification; tunable + mutable in Preferences. Last-input timestamp only. | v1.8.0 | `src/ROROROblox.Core/Diagnostics/ActivityMonitor.cs` |

## Game / server routing

| Feature | Description | Version | Code |
|---|---|---|---|
| Default game + saved-games library | Set a default game URL (or per-account custom); Set/Clear from the library. | v1.1; refined v1.3.x | `src/ROROROblox.Core/FavoriteGameStore.cs` |
| Default-game toolbar widget | Toolbar readout + dropdown picker + empty-state routing. | v1.3.x | `src/ROROROblox.App/MainWindow.xaml*` |
| Join by link (+ Save to library) | Paste any game or private-server share URL to launch there; optional save toggle. | v1.3.x | `src/ROROROblox.App/JoinByLink/` |
| Saved private servers | Save, rename, pick per account; route different alts into different servers in one batch. | v1.6.0 | `src/ROROROblox.Core/PrivateServerStore.cs` |
| Default private server | DEFAULT badge, sorts to top, clearable. | v1.10.0 | `PrivateServerStore.cs` |
| Launch-to-home | No default set → launch lands on Roblox home instead of failing. | v1.10.0 | `src/ROROROblox.Core/LaunchTarget.cs` |
| Squad Launch (trust-aware, 3-phase) | Set of accounts into one private server: direct → anchor on #1 → follow; careful mode waits for each to land; per-account "join via friend" toggle. | v1.1-era; trust-aware v1.10.0 | `src/ROROROblox.App/SquadLaunch/` |
| Friend Follow | Follow a friend into their server; picker browses the main account's friends, not just saved accounts. | v1.1-era; friends-from-main v1.9.0 | `src/ROROROblox.App/Friends/` |

## Plugin system (Windows)

| Feature | Description | Version | Code |
|---|---|---|---|
| Out-of-process plugin host | Plugins are separate signed EXEs on a per-user named-pipe gRPC server; a plugin crash never takes RoRoRo down. | v1.4.0 | `src/ROROROblox.App/Plugins/PluginHostService.cs` |
| Shared contract NuGet | `ROROROblox.PluginContract` — `.proto` + bindings, versioned independently (currently 0.6.0). | v1.4.0 | `src/ROROROblox.PluginContract/` |
| Per-capability consent sheet | Every declared `host.*`/`system.*` capability itemized in plain language; per-toggle grants, DPAPI-stored in `consent.dat`. | v1.4.0 | `src/ROROROblox.App/Plugins/ConsentStore.cs` |
| SHA-verified GitHub installer | Paste a release URL; manifest + `manifest.sha256` verified before unpack. HTTPS-only since v1.7.1. | v1.4.0 | `src/ROROROblox.App/Plugins/PluginInstaller.cs` |
| Fail-closed capability interceptor | Every gated RPC checked against consented capabilities; app refuses to start if the map doesn't cover every method; UI handles owner-scoped. | v1.4.0; hardened v1.10.0 | `src/ROROROblox.App/Plugins/CapabilityInterceptor.cs` |
| Plugin UI surfaces | Tray-menu items, per-account row badges, status panels. | v1.4.0 | `src/ROROROblox.App/Plugins/Adapters/WpfPluginUIHost.cs` |
| Autostart + supervision | Per-plugin autostart (default off); supervisor monitors crashes, tears down on exit. | v1.4.0 | `src/ROROROblox.App/Plugins/PluginProcessSupervisor.cs` |
| Launch-target RPC | Plugins launch an account into a specific server; host owns link parsing + cookies. | contract 0.2.0 | `Adapters/MainViewModelLaunchInvokerAdapter.cs` |
| Current-server query | Read the most-recently-launched private-server share link. | contract 0.2.0 | `src/ROROROblox.App/Plugins/` |
| Account-activity query | Consent-gated per-account idle time (timestamps only) — built for Ur AFK. | v1.8.0 / contract 0.3.0 | `Plugins/ActivitySnapshotProvider.cs` |
| MarkAccountActive | Keep-alive plugins credit activity after acting on a window so idle warnings don't misfire. | v1.10.0 / contract 0.5.0 | `Plugins/AccountActivityMarker.cs` |
| StopAccounts (agent-ops) | Plugins ask RoRoRo to close clients it launched (graceful, hard-kill fallback). | v1.10.0 / contract 0.6.0 | `Adapters/ProcessTrackerAccountStopper.cs` |
| In-app plugin marketplace | "Available" section fed by remote catalog; one-click install, update badges. Unpackaged builds only (Store policy 10.2.2); Store users get the web marketplace. | v1.9.0 | `Plugins/PluginCatalogClient.cs`, `PluginsViewModel.cs` |

**The Ur plugin family** (626 Labs LLC, separate repos, sideload-only): **Ur Task** (per-window macro record/play + action bridge, first plugin, with v1.4), **Ur OCR** (screen-region text/color triggers → keybinds, bridges to Ur Task), **Ur AFK** (focuses idle alts and taps Space before the ~20-min timeout; min host v1.8). Third-party authoring documented in [`docs/plugins/AUTHOR_GUIDE.md`](plugins/AUTHOR_GUIDE.md). Live versions/installs: `626labs-hub/data/rororo-plugins.json` (nightly refresh).

## Update / config / distribution

| Feature | Description | Version | Code |
|---|---|---|---|
| Velopack auto-update | Direct-download builds update from GitHub Releases; Store builds via Store. In-app updater is check-only through v1.11 — download+apply still deferred. | v1.1 | `src/ROROROblox.App/Updates/UpdateChecker.cs` |
| Remote `roblox-compat.json` | Startup fetch: known-good Roblox range + mutex name. | v1.1 | `src/ROROROblox.Core/RobloxCompatChecker.cs` |
| Remote plugin-catalog fetch | Marketplace catalog from GitHub Releases — metadata + install URLs only, never code. | v1.9.0 | `Plugins/PluginCatalogClient.cs` |
| Sideload MSIX + self-signed cert | `RORORO-Sideload.msix` + `dev-cert.cer` alongside `Setup.exe`. | v1.1 | `scripts/build-msix.ps1` |
| Distribution-mode detection | Packaged vs unpackaged at runtime, gating marketplace visibility (policy 10.2.2). | v1.9.0 | `src/ROROROblox.App/Distribution/` |
| Run-on-login + single instance | Optional HKCU Run entry; second launch surfaces the existing window. | v1.1 | `src/ROROROblox.App/AppLifecycle/SingleInstanceGuard.cs` |
| Microsoft Store listing | Live as "RORORO" (renamed from "ROROROblox" for policy 10.1.1.1), Store-signed, SmartScreen-free. | v1.1.2.0 | `docs/store/listing-copy.md` |
| Windows 10 22H2 Store floor | Store OS floor dropped from Win11-only. | v1.9.0 | `Package.appxmanifest` |

## UI/UX

| Feature | Description | Version | Code |
|---|---|---|---|
| State-coloured tray icon | Cyan = lock held, slate = off, magenta = error; renders the main account's avatar; right-click menu. | v1.1 | `src/ROROROblox.App/Tray/TrayService.cs` |
| Theme store + themed dialogs | Theme store with per-account caption colors and a theme builder (AI-prompt template included). | v1.1 | `src/ROROROblox.App/Theming/` |
| Compact mode | Collapses header chrome so the account list isn't squeezed. | v1.3.x | `src/ROROROblox.App/ViewModels/CompactEmptyState.cs` |
| First-run welcome | Onboarding naming the real "Launch multiple" button. | v1.1; fixed v1.7.1 | `src/ROROROblox.App/About/WelcomeWindow.*` |
| Settings + Preferences | Settings (games, accounts, export/import); Preferences (idle threshold + mute, streamer toggle). | v1.1; Preferences v1.8.0 | `src/ROROROblox.App/Settings/`, `Preferences/` |
| Session history | Launch history rendering local names. | v1.1 | `src/ROROROblox.Core/SessionHistoryStore.cs` |
| **Streamer mode** | One flip disguises the whole roster — 98-name pool, 12 hand-drawn avatars — across rows, pickers, history, banners, modals, and Roblox window titles. Persists across restarts; per-account or whole-roster reroll; private-server links behind a reveal-only pill. Masks RoRoRo's window, not in-Roblox identity. | v1.11.0 / v1.11.1.0 | `src/ROROROblox.Core/StreamerMode/`, `src/ROROROblox.App/StreamerMode/` |
| Portable-build taskbar identity | Own AppUserModelID so a stale install shortcut can't blank the taskbar icon. | v1.11.1.0 | `src/ROROROblox.App/` |

## Diagnostics / health / guardrails

| Feature | Description | Version | Code |
|---|---|---|---|
| Diagnostics window + bug-report bundle | One-button support bundle (logs, versions, system health). | v1.1 | `src/ROROROblox.App/Diagnostics/DiagnosticsWindow.*` |
| Real-version reporting | System health reports the app's actual version (was reading the unversioned Core assembly). | fixed v1.11.1.0 | `src/ROROROblox.Core/Diagnostics/DiagnosticsCollector.cs` |
| Structured logging (Serilog) | Errors land in `%LOCALAPPDATA%\ROROROblox\logs\`; noise cut in v1.9. | v1.1 | `src/ROROROblox.App/Logging/AppLogging.cs` |
| App-storage defender | Guards the storage dir, recovers from transient file locks. | v1.7.x | `src/ROROROblox.App/Diagnostics/AppStorageDefender.cs` |
| Resilient presence poll | One failed cycle no longer kills status updates for the session. | v1.7.1 | `PresenceService.cs` |
| WebView2-missing affordance | Detects missing runtime, hands the user Microsoft's installer. | v1.1 | `src/ROROROblox.App/Modals/WebView2NotInstalledWindow.*` |
| Roblox-not-installed modal | Friendly Download / "I have Bloxstrap" paths. | v1.1 | `src/ROROROblox.App/Modals/RobloxNotInstalledWindow.*` |
| Memory watchdog | Samples each launched client's private bytes every 30s; dual triggers (per-client cap + machine-wide RAM-exhaustion projection) with latch hysteresis so a client oscillating at a threshold can't spam warnings. Thresholds derive from installed RAM. | v1.12 | `src/ROROROblox.Core/Diagnostics/MemoryWatchdog.cs`, `MemoryDefaults.cs` |
| Memory warning surfaces | Amber row chip + tray icon state + balloon; chip and Recycle also present in compact mode. | v1.12 | `MainWindow.xaml`, `Tray/TrayService.cs` |
| One-click Recycle | Stops one account's client and relaunches it into the same `LaunchTarget`; resets the watchdog baseline. Process exit is the only reclaim Windows offers for the Roblox leak. | v1.12 | `src/ROROROblox.Core/Diagnostics/AccountRecycler.cs` |
| Stray cleanup | Closes only the windowless leftovers Roblox abandons on exit; never touches a client with a game window. | v1.12 | `RobloxInstanceStopper.StopWindowless`, `Modals/LeftoverProcessesWindow.*` |
| Fail-closed window probe | An unreadable `MainWindowHandle` now reports *windowed*, so a live game is never mistaken for an orphan and silently closed. | v1.12 (#68) | `src/ROROROblox.Core/Diagnostics/RobloxRunningProbe.cs` |
| Memory curve in the log | One Information line every 15 minutes with each client's bytes + growth rate — the artifact that makes a "my windows closed" report diagnosable. Version now stamped on every log line. | v1.12 | `MemoryWatchdog.cs`, `Logging/AppLogging.cs` |
| RAM in System health | Total/available RAM + per-account memory in the diagnostics panel and support bundle. | v1.12 | `src/ROROROblox.Core/Diagnostics/DiagnosticsCollector.cs` |

## In-flight / queued (not shipped — keep OFF the hub page)

- **Rejoin-after-death** — when a client dies (RAM exhaustion, crash), relaunch it into the server it was in. Closes the loop on the v1.12 memory work and is the automated-reconnect piece. Depends on server-instance targeting.
- **Regroup ("send my others here")** — row action: pick an account, send the rest of the roster to its current server. Recovery when a squad scatters. Depends on server-instance targeting.
- **Plugin: live server identity** — `host.queries.current-server` today exposes only the last private-server link. Extend to live job IDs so a plugin can coordinate a roster itself. Contract bump. Depends on server-instance targeting.
- **Account groups** — named "launch these together" sets; spec approved, unbuilt (`docs/superpowers/specs/2026-07-09-account-groups-design.md`).
- **Account stats: uptime + per-game play time** — presence-driven recording; spec approved, unbuilt (`…account-stats-uptime-design.md`).
- **Themed window chrome** — WPF-UI FluentWindow/TitleBar across main + modals; spec approved, unbuilt (`…themed-window-chrome-design.md`).
- **In-app updater download+apply** — deferred v1.8 → v1.9 → still check-only.
- **Ruled out (measured, do not re-propose):** per-account graphics quality as a memory lever — max→min quality moved client RAM by 0.9% on a live rig; the slider is GPU-side, not asset residency. See `docs/investigations/2026-08-01-graphics-quality-memory-negative-result.md`.
- **Backlog:** About-box version from MSIX manifest; WebView2 white-screen reload hint; `NeedsReverification` vs `SessionExpired`; per-cookie encryption envelope; per-account WebView2 profiles; crash-report opt-in; winget manifest; heuristic mutex-rename auto-detection.
- **Surface `NewClientWaitOutcome.TimedOut` from the condition-based launch gate** — currently discarded at `RobloxLauncher.cs:637`. Matters in a documented-common failure mode: when the singleton mutex is not held, Roblox absorbs launches 2..N into the existing client and no new pid ever appears, so every launch after the first burns the full 30s ceiling — an 8-account Squad Launch goes from ~40s to ~4.5min with no log line and no banner. `RobloxLauncher` has no `ILogger`; cheapest fix is returning the outcome from `HoldForNewClientAsync` and letting `MainViewModel` log or banner it. See `docs/superpowers/specs/2026-08-01-launch-gate-condition-based-design.md`.
- **FFlag writes can land in a version folder Roblox isn't launching from** — `ClientAppSettingsWriter.NewestActiveVersionFolder` picks its target by newest `RobloxPlayerBeta.exe` mtime; Roblox launches whatever its installer last registered. Measured divergence of ~4 days on Este's rig (8/1 07:55 → 8/2 10:28), during which every FFlag write was silently discarded into an inactive folder — no error, no log line, no symptom. Fix by reading the registered launcher path. **Do this before trusting any future FFlag measurement.** See `docs/investigations/2026-08-02-launch-gate-smoke-test-negative-result.md`.
- **Launch gate settle grace is too short** — measured: the client reads its config 2.25s and 3.25s after its pid appears; `SettleGrace` is 1s, so the next launch's write still wins. The `GlobalBasicSettings` read time — the one that actually matters — is still unmeasured. PR #70 is a strict improvement but does NOT fix the wrong-FPS-cap bug; don't claim it in release notes.
- **Launch-gate post-timeout false detect** — if launch A times out, launch B snapshots an empty-of-A set and A's late-arriving client can satisfy B's first poll. Inherent to pid-set differencing; worth a line in the spec's accepted-consequences section.
- **Launch-gate settle-delay cancellation is untested** — the doc comment on `WaitForNewClientAsync` claims cancellation propagates from both delays; the test only covers the poll delay. Moot while production passes `CancellationToken.None`.
- **Plugin `RequestLaunchTarget` can now block ~31s** — `MainViewModelLaunchInvokerAdapter.cs:110` awaits the launch, so the gRPC handler at `PluginHostService.cs:385` holds open for the full launch-gate ceiling. If RoRoRo Ur Task sets a client-side deadline under 30s, plugin-triggered launches will report `DEADLINE_EXCEEDED` while the launch actually succeeds. Needs one grep in the Ur Task repo before the next plugin release.
- **Launch-gate snapshot-degradation test signals via timeout, not assertion** — `LaunchAsync_ProbeThrowsOnSnapshot_DegradesToTheFixedHold_...` goes red by hitting its 5s `.WaitAsync` ceiling rather than by a failed assertion. Bounded and CI-safe (5s on regression, 0s on a pass), but weaker than an in-band assertion. Hardening target if the file is touched again: assert the probe call count inside the poll loop, or drive the release with a synchronization primitive.
- **Windowless Roblox husks accumulate during a session** — closing a client leaves a ~26 MB windowless `RobloxPlayerBeta` behind. Seven accumulated across one working session (2026-08-02). `StartupGate` sweeps them at launch, so a restart clears them, but nothing reclaims them mid-session — a long multi-instance session keeps growing them. They also pollute any pid-difference heuristic, since a close produces a "new" pid indistinguishable from a launch. Consider a periodic stray sweep on the existing ticker.
- **Vision:** AI connector (MCP — Claude drives RoRoRo) + PetSim knowledge base (`docs/vision/2026-07-04-ai-connector-and-knowledge-base.md`); Ur Reset plugin (auto-relaunch stuck accounts).

## Privacy posture (constant across versions — quote freely in marketing)

No telemetry, no analytics. The password never touches the process — login is Roblox's own page in WebView2. Cookies are DPAPI-encrypted, never logged, and leave the PC only via user-initiated passphrase-encrypted export. Idle tracking is a Windows last-input timestamp, never keystroke content. Plugins are out-of-process, consent-gated per capability, SHA-verified at install. HTTP requests identify as `ROROROblox/<version>` — no browser spoofing.
