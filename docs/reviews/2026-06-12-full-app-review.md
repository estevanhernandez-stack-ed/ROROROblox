# RoRoRo full-app review — 2026-06-12

> Multi-agent review at v1.7.0.0 (main, d7f7e38). Seven parallel reviewers (architecture,
> correctness, security, plugins, tests, ux-features, release) + adversarial verification of
> every critical/high finding. 16 agents, 78 findings, 9 high-severity claims verified —
> all 9 confirmed real (3 downgraded to medium on severity after verification, 0 refuted).
> Raw per-dimension findings + verifier reasoning: [2026-06-12-raw-findings.txt](2026-06-12-raw-findings.txt).

## Verdict

The app is in genuinely good shape — better than most v1.x indie desktop software. Core
layering is clean (verified: zero UI deps in Core), the past war wounds are fenced with
regression tests that cite their bugs, crypto on the v1.6 export is textbook
(AES-256-GCM + PBKDF2 600k), and the UX has designed empty states everywhere. **Nothing
found rises to critical.** No cookie-leak path exists in logging, telemetry, or the plugin
wire.

The six confirmed-high findings cluster into three stories:

1. **The presence loop has a kill switch** — one unhandled exception silently and
   permanently reverts the app to the pre-v1.5 ghost-closed behavior.
2. **Auto-update doesn't exist** — the Velopack updater checks but never downloads or
   applies. Every clan member on the Setup.exe lane is frozen at their install version
   forever, while release.yml tells you rollout is automatic.
3. **The plugin system's UI third is a stub shipped as working** — host.ui.* renders
   nothing, four releases running, while AUTHOR_GUIDE documents it as functional.

Plus one CI gap that violates the repo's own stated mandate (no secret-scan twin in CI).

---

## Confirmed high-severity findings (all adversarially verified)

### H1. Presence poll loop dies permanently on any unexpected exception
`src/ROROROblox.Core/Diagnostics/PresenceService.cs:110-127` + `App.xaml.cs:369-376`

`RunLoopAsync` catches only `OperationCanceledException`. Two realistic triggers fault the
loop: (a) the snapshot delegate LINQ-enumerates `MainViewModel.Accounts` (a UI-thread
`ObservableCollection`) from a threadpool tick — a concurrent add/remove/drag-reorder
throws; (b) `AccountStore.RetrieveCookieAsync` throws `KeyNotFoundException` for an
account removed mid-tick, and the store re-reads `accounts.dat` every poll so a transient
AV/backup file lock also kills it. Once faulted, `Start()`'s `_loopTask is not null` guard
makes it **unrecoverable for the session** — no log line the user will see, no restart.
Verifier confirmed end-to-end and noted it's slightly *worse* than claimed (the
irrecoverable-Start guard). Only the ambient 25s loop dies; launch-time refresh paths
survive. **Fix is small:** catch-log-continue per tick + a thread-safe account snapshot
mirror. The same mirror retires a cluster of four more cross-thread enumeration sites in
the plugin adapters and App event bridges.

### H2. Velopack auto-update is check-only — clan installs never update
`src/ROROROblox.App/Updates/UpdateChecker.cs:54-60`

`CheckForUpdatesAsync` logs "Update available" at Information and stops. Zero calls to
`DownloadUpdatesAsync`/`ApplyUpdatesAndRestart` anywhere in src; no tray update menu
exists. Meanwhile `release.yml:162` prints "Auto-update will roll out to existing installs
within 24h (debounced)" — false; the 24h debounce gates a no-op. Store-lane users update
via Microsoft, but the Setup.exe clan lane silently never receives binary fixes — including
the v1.6 security pass — and it undercuts the spec §7.1 "ship a fix within hours" story.
**Fix:** wire download + a tray/banner "restart to update" action; interim cheap win:
reuse the drift-banner surface to show "v{X} is out" with a link (an afternoon).

### H3. host.ui.* is a logging stub shipped as a working API across four releases
`src/ROROROblox.App/Plugins/Adapters/WpfPluginUIHost.cs:6-21`

The registered production `IPluginUIHost` logs and returns GUID handles — no tray item,
badge, or panel ever renders, and `UpdateUI` is a no-op. AUTHOR_GUIDE lists all three UI
capabilities as working and its recipe implies a visible tray item; the consent sheet asks
users to grant capabilities that render nothing. Worst failure mode for a plugin author:
no error, no result. **Fix:** land the real tray/badge wiring (translator + handle layer
already exists and is tested) or banner-correct the guide now.

### H4. Secret scan + local-path guard have no CI twin
`.github/workflows/ci.yml:35-51`

Both guards live only in `.git/hooks/pre-commit` — per-machine, not in clones, bypassed by
`--no-verify`. CLAUDE.md mandates "the pre-commit hook AND CI must fail loud." A
`.ROBLOSECURITY` value or `c:\Users\<name>` path committed from an un-hooked box sails
through. **Fix:** one fast ubuntu job running the same two scripts over the full tree;
make build-test depend on it.

### Downgraded after adversarial verification (real, but medium)

- **Plugin pipe identity is self-asserted x-plugin-id** (`CapabilityInterceptor.cs:61`) —
  every mechanism claim verified (handshake is advisory, GetRunningAccounts is ungated,
  forgery works), but the pipe's per-user ACL means exploitation requires same-user code
  that could already DPAPI-decrypt `accounts.dat` directly. Known v1.5+ deferral. The
  legitimately-wrong parts: AUTHOR_GUIDE falsely claims handshake rejection blocks RPCs,
  and a `PluginHostService.cs:333` comment claims enforcement that exists nowhere — fix
  the docs now, bind identity at handshake in v1.5.
- **PluginInstaller accepts plain http URLs** (`PluginInstaller.cs:40`) — MITM can swap
  zip + same-origin SHA together; but documented distribution is GitHub https and the
  trust model already accepts arbitrary user-pasted URLs. One guard clause fixes it.
- **RobloxCompatChecker.CheckAsync has zero tests** — the drift-banner half is fully
  untested (the mutex half has 11 tests), but its fail-quiet fallbacks are by design;
  needs an installed-version seam before tests are even writable.

---

## Top medium findings worth a slot in the next cycle

| Finding | Where | Effort |
|---|---|---|
| `RelayCommand` swallows ALL exceptions, and several command bodies are unguarded — a throw after a successful launch skips `TrackLaunchAsync` (untracked client), failed RemoveAccount/ReauthenticateAsync vanish silently | `RelayCommand.cs:31-43`, `MainViewModel.cs:991` | small |
| `Process.Exited` subscribed AFTER `EnableRaisingEvents` — fast exit in the window = permanent ghost "running" row; Roblox's anti-multilaunch kill makes this routine, not rare | `RobloxProcessTracker.cs:326-343` | small |
| WebView2 capture profile (live logged-in Roblox session) never wiped — survives account removal; the only sweep runs on the NEXT capture. Contradicts the spec's wipe-per-capture intent | `CookieCapture.cs:60` | small |
| `PluginProcessSupervisor.StopAll` has zero callers — every app exit orphans plugin processes; plugin crash is silent unless the Plugins window is open | `PluginProcessSupervisor.cs:125`, `App.xaml.cs:972-985` | small |
| `ConsentStore` is the only store without atomic writes — torn `consent.dat` silently wipes all plugin consents; plus a full file-read + DPAPI decrypt per gated RPC (sync-over-async) | `ConsentStore.cs:95-101`, `App.xaml.cs:495-510` | small |
| Manifest `autostartDefault` is dead on arrival — `GrantAsync` always persists autostart=false on first install (masked because the installer starts the plugin for the current session) | `ConsentStore.cs:43`, `PluginInstaller.cs:160-168` | small |
| Contract versioning is strict string equality — first additive bump to "1.1" rejects every 1.0 plugin, contradicting the guide's semver promise | `PluginHostService.cs:61` | small |
| Unknown RPC methods are ungated by default (fail-open capability map) — add a reflection test that every RPC has a map entry | `RpcMethodCapabilityMap.cs:30-33` | small |
| Transient `IOException` on accounts.dat read shows the scary DPAPI-corrupt modal for a one-tick file lock | `AccountStore.cs:583-590` | small |
| StartupGate hard-blocks even when RoRoRo launched the running clients — RoRoRo crash mid-session locks the user out until they close every alt; the re-attach scanner path is unreachable | `App.xaml.cs:86-93` | medium |
| Version stamping: 3 sources, 2 manual sync scripts, no CI guard — exact recurrence vector for the 1.3.4-vs-1.4 bug. 3-line tag==csproj==manifest assert in release.yml | `scripts/build-msix.ps1:154`, `release.yml` | small |
| Reviewer letter claims stop-all is "same-user only" — enforced by OS ACLs, not code; if RoRoRo ever runs elevated it WILL kill other users' clients. Add a session/owner filter | `RobloxRunningProbe.cs:17` | small |
| `_pendingSessionByAccountId` plain Dictionary mutated from UI + tracker callback threads → `ConcurrentDictionary` | `MainViewModel.cs:57` | small |
| AppStorageDefender stamps identity BEFORE launch resolves — failed launches keep defending the wrong identity for up to 120s | `MainViewModel.cs:948-983` | small |
| `build-msix.ps1` hardcodes `$env:USERPROFILE\.dotnet\dotnet.exe` — Store build breaks on any standard machine | `scripts/build-msix.ps1:154` | small |
| MainViewModel is 2,349 lines / 19 ctor deps — extract LaunchOrchestrator first (it's also the plugin seam and unlocks headless launch-flow testing); one extraction per cycle | `MainViewModel.cs` | large |
| UI-translator round-trip test sends no x-plugin-id header — same blindspot class as the fixed one, different method family | `EndToEndContractTests.cs:188-212` | small |
| ci.yml double-runs every PR commit on Windows runners — scope push to main | `ci.yml:14-16` | small |
| 4th version segment silently stripped for Velopack — tagging v1.7.0.1 collides with v1.7.0.0 | `build-velopack-release.ps1:50` | small |
| CLAUDE.md documents `src/RORORO.Package/` wapproj that doesn't exist — real flow is `scripts/build-msix.ps1` | CLAUDE.md | small |

Low-severity items (mutex watchdog WAIT_TIMEOUT logic, CSRF 403 no-retry, presence
Stop/Dispose ODE window, hardcoded brand hexes in two modals, stale `src/src/` dirs,
unpinned vpk, `.claude/626labs-context.md` needs gitignoring, dead `ValidateSessionsAsync`,
double-hyphen in the capture window title, etc.) are in the raw findings file.

---

## Feature roadmap — ranked by clan value vs effort

1. **"Fix it for me" on the mutex hard-block modal** (medium effort, highest value).
   The modal currently hands a confused non-technical user a 3-step manual dance. v1.7
   already shipped both building blocks — stop-all-instances and tray mutex re-acquire —
   the startup path just doesn't compose them. One button converts the product's most
   alienating moment into one-click recovery.
2. **Launch-by-tag / saved squads** (cheap version: a day). Tags exist but only filter
   visibility. "Launch filtered" when a tag filter is active reuses `DispatchBatchAsync`
   unchanged. Full version later: named launch groups (accounts + game/private server).
3. **Auto-arrange Roblox windows** (medium). RoRoRo already tracks every client HWND for
   caption tinting. A tile/grid "Arrange windows" button is plain `SetWindowPos` — window
   management, not input automation, so it's inside the product wall. Colored, tiled,
   identifiable windows is the screenshot that sells the app.
4. **Bulk FPS cap** (small). "Main uncapped, alts at 24" is the dominant clan pattern;
   today it's 8 identical dropdown clicks. `OnFpsCapChangedAsync` already exists; bulk is
   a loop.
5. **Export-backup nudge** (small). Export is the only way logins survive a reinstall
   (DPAPI by design) but it's buried in Preferences. One-time banner at ~3 saved accounts.
6. **Update-available banner** (small). Interim step for H2 — reuse the drift banner.
7. **"Stop all" from the main window footer** (small). It already shows "N clients
   running"; tray-only is invisible to non-technical users.
8. **Welcome window "Add my first account" CTA** (small). Step 1 of the welcome IS that
   button — let the window fire it.
9. **Presence health watchdog + diagnostics row** (medium). Pairs with H1: last-tick
   timestamp, consecutive-failure count, self-restart with backoff.
10. **Plugin event callback channel** (large, v1.5 design input). A server-streaming
    `SubscribePluginEvents` RPC fits the existing one-directional architecture and unblocks
    interactive plugin UI + consent-revocation push + graceful shutdown.

Quick copy fixes while in there: tooltip says 1.5s throttle (it's 5s — for a 10-alt batch
that's expecting 15s and experiencing 45s, app reads as hung); welcome says "Launch All"
(button is "Launch multiple"); StatusBanner never clears for the whole session;
remove-account confirm is a raw Win32 MessageBox in an otherwise fully-branded app.

---

## What's genuinely strong (keep doing this)

- **Comments carry the postmortems.** UseCookies=false documents the exact cookie-bleed
  bug it prevents; tests cite the commits of the bugs they guard. Rare and valuable.
- **Transport crypto is textbook** — AEAD, OWASP-floor KDF, fresh salt/nonce, fail-closed
  import with no oracle, keys zeroed. Verifier found nothing.
- **The known harness blindspot is verifiably closed** — EndToEndContractTests now mirrors
  production x-plugin-id wiring with the bug commit cited.
- **Failure isolation is consistent** — corrupt consent.dat degrades to "no plugins," not
  a blocked launch; startup checks isolate independently.
- **Anti-vacuous test discipline** — the concurrency-cap test asserts the cap is REACHED,
  not just respected; cookies verified DPAPI-ciphertext by reading raw bytes.
- **UX empty states everywhere, skip-reasons on batch launches, install-aware error copy** —
  nothing ever looks broken, users learn why instead of guessing.

## Suggested sequencing

- **Patch release (v1.7.1):** H1 presence loop containment + snapshot mirror, H4 CI scan
  job, https guard on PluginInstaller, RelayCommand logging, Exited-subscription order,
  WebView2 profile wipe, tooltip/welcome copy fixes. All small, all shippable in one lane.
- **v1.8 cycle:** H2 update apply path (+ interim banner), H3 host.ui decision (wire or
  banner-correct), "Fix it for me" mutex button, launch-by-tag cheap version, bulk FPS,
  StatusBanner auto-clear, version-stamp CI guard.
- **v1.9 / plugin cycle:** connection-bound plugin identity, contract semver parsing,
  consent store atomic writes + cache, supervisor StopAll on exit, callback channel design,
  MainViewModel LaunchOrchestrator extraction.
