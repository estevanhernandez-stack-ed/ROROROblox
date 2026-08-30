# RoRoRo feature registry

What exists right now, where it lives, how it is switched on, and whether it is alive. Compiled 2026-08-30 against HEAD (v1.23.0.0) by reading the code; every path below was opened. Version-by-version history (when each thing shipped, what the release said) lives in [feature-ledger.md](feature-ledger.md); how the pieces fit is in [architecture.md](architecture.md).

**Path shorthand.** `App/` = `src/ROROROblox.App/`, `Core/` = `src/ROROROblox.Core/`, `Tests/` = `src/ROROROblox.Tests/`, `Harness/` = `src/ROROROblox.PluginTestHarness/`. Line numbers are omitted on purpose.

**Status vocabulary.** `active` = wired into the running app and read in code. `half-finished` = scaffold exists, the loop is not closed. `parked` = approved, deliberately not built yet. `abandoned` = ruled dead 2026-08-30. `dead` = code path removed or never called. `legacy` = superseded but still present. **(unverified)** = the status or intent was inferred from a name, comment, or doc rather than confirmed; or the maintainer could not confirm it when asked.

**Gating vocabulary.** `always` = no switch. `settings:<key>` = a key in `settings.json` (defaults in `Core/AppSettings.cs`). `remote` = `roblox-compat.json` on the latest GitHub release.

## Launch and multi-instance

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Singleton name race | Creates a Mutex named `Local\ROBLOX_singletonEvent` before Roblox creates its Event; typed outcome Acquired / HeldByRoblox / HeldByCompatibleTool. | `Core/MutexHolder.cs`, `Core/MutexAcquireOutcome.cs` | always at startup; tray Multi-Instance toggle | active |
| Config-driven mutex name | Name resolves remote `mutexName` → `last-known-mutex.txt` → hardcoded default, written into a DI seam before `IMutexHolder` exists. The spec's `singletonMutexName` rename was never adopted. | `Core/RobloxCompatChecker.cs`, `App/Startup/ResolvedMutexName.cs` | remote | active |
| Signed compat feed | ECDSA P-256 detached `.sig` verified over the raw bytes before deserializing; bad or missing = no update, never unverified content. | `Core/RobloxCompatSignature.cs`, `Core/RobloxCompatSigningKey.cs`, `tools/CompatSigner/Program.cs` | always; CI secret `ROBLOXCOMPAT_SIGNING_KEY` signs (`release.yml` on a tag, `compat.yml` on demand) | active |
| Roblox version-drift banner | Installed `RobloxPlayerBeta` FileVersion vs `knownGoodVersionMin/Max`. | `Core/RobloxCompatChecker.cs`, `App/ViewModels/MainViewModel.cs` | remote | active |
| Startup gate | `TryAcquire` → Clean / SharedLock / Blocked / Leftover; the branch decisions are pure and tested. | `Core/Diagnostics/StartupGate.cs`, `App/AppLifecycle/BlockedStartupDecision.cs`, `App/AppLifecycle/LeftoverStartupDecision.cs` | always | active |
| Seamless takeover | On Blocked with only windowless tray clients: stop them, re-acquire by polling every 150 ms for up to 3 s, relaunch `RobloxPlayerBeta --launch-to-tray`. | `Core/Diagnostics/SeamlessTakeover.cs`, `Core/RobloxTrayLauncher.cs`, `App/App.xaml.cs` | runtime condition | active |
| Start anyway + contested watcher | Escape hatch on the Blocked modal; a 5 s watcher banners contention whenever RoRoRo does not hold the name. | `Core/Diagnostics/MutexContestedWatcher.cs`, `App/AppLifecycle/BlockedStartupDecision.cs` | always | active |
| Auth-ticket launch | Two POSTs to `auth.roblox.com/v1/authentication-ticket` (CSRF dance), then a `roblox-player:` URI via ShellExecute. | `Core/RobloxApi.cs`, `Core/RobloxLauncher.cs`, `Core/ProcessStarter.cs` | always | active |
| `LaunchTarget` union | DefaultGame / Home / Place / GameJob / PrivateServer (LinkCode vs AccessCode, not interchangeable) / FollowFriend; `FromUrl` parser; share-link resolution. | `Core/LaunchTarget.cs`, `Core/RobloxApi.cs` | always | active |
| Launch to home | No default favorite → `launchmode:app` with no `placelauncherurl`. | `Core/RobloxLauncher.cs`, `Core/LaunchTarget.cs` | automatic | active |
| Per-account FPS cap | Writes `DFIntTaskSchedulerTargetFps` and `GlobalBasicSettings` `FramerateCap` (the one that wins), then settles the write across a quiet window (up to 45 s when consecutive caps differ). Presets 20–240 + Unlimited. | `Core/FpsCapSettler.cs`, `Core/GlobalBasicSettingsWriter.cs`, `Core/ClientAppSettingsWriter.cs`, `Core/FpsPresets.cs` | per-account `FpsCap` non-null | active |
| Install deferral / pre-warm gate | Detects a pending Roblox update (process scan + one documented CDN GET), launches the first client, holds the batch up to 120 s; skipped when Bloxstrap/Fishstrap owns the handler. | `Core/Diagnostics/RobloxUpdateProbe.cs`, `App/ViewModels/PreWarmGate.cs`, `Core/BloxstrapDetector.cs` | batch launches | active |
| appStorage.json identity defender | Stamps the account into Roblox's `appStorage.json` and re-stamps against sibling writes until the launched client attaches plus a 10 s grace (~12 s typical, capped at 120 s during a Roblox install); one active defender at a time. | `App/Diagnostics/AppStorageDefender.cs` | per launch, account has `RobloxUserId` | active |
| Stable browserTrackerId | 13-digit id generated once per account, persisted in the vault, excluded from export. | `App/ViewModels/MainViewModel.cs`, `Core/AccountStore.cs` | always | active |
| Bloxstrap / Fishstrap detection | Reads the `roblox-player` handler command from HKCU; two scopes (FFlag banner vs strap-handles-launches). | `Core/BloxstrapDetector.cs` | always | active |
| Limited-session (403) handling | 403 with a valid CSRF token → `SessionLimitedException`, one rotation retry, `LaunchResult.Limited`; presence flips the row after 3 consecutive 403s and auto-heals. | `Core/SessionLimitedException.cs`, `Core/RobloxApi.cs`, `Core/Diagnostics/PresenceService.cs` | always | active |

## Accounts and cookies

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| DPAPI account vault | All accounts in one JSON blob under `ProtectedData.Protect(CurrentUser)`; decrypt failure raises `AccountStoreCorruptException` → `DpapiCorruptWindow`. | `Core/AccountStore.cs`, `App/Modals/DpapiCorruptWindow.xaml.cs` | always | active |
| WebView2 cookie capture | `roblox.com/login` in WebView2 with a fresh GUID user-data dir per capture; siblings swept; stays open through a mid-2FA 401. | `App/CookieCapture/CookieCapture.cs`, `App/CookieCapture/CookieCaptureWindow.xaml.cs`, `Core/WebView2UserDataDirectory.cs` | user: Add account / Re-authenticate | active |
| Account export / import | Passphrase bundle (PBKDF2-SHA256 600k + AES-256-GCM, BCL only); merge by `RobloxUserId`; export limited to accounts that have one; import cannot steal main. | `Core/Transport/AccountTransportService.cs`, `App/Transport/ExportAccountsWindow.xaml.cs`, `App/Transport/ImportAccountsWindow.xaml.cs` | Settings buttons | active |
| RobloxUserId backfill | ~5 s after first paint, sequential, 2.5 s ± 0.5 s apart, missing ids only (anti-fraud pacing). | `Core/AccountUserIdBackfillService.cs`, `App/App.xaml.cs` | automatic | active |
| Account tags + filter | Up to 8 free-text chips per account; filter box hides non-matching rows. | `App/ViewModels/AccountSummary.cs`, `Core/Account.cs` | always | active |
| Local renames | Games, private servers, accounts; `LocalName` wins everywhere a name is shown. | `Core/RenameTarget.cs`, `Core/RenameDispatch.cs`, `App/Modals/RenameWindow.xaml.cs` | context menus | active |
| Environment-failure modals | Roblox not installed; WebView2 runtime missing (hands off the evergreen installer); DPAPI blob unreadable. | `App/Modals/RobloxNotInstalledWindow.xaml.cs`, `App/Modals/WebView2NotInstalledWindow.xaml.cs`, `App/Modals/DpapiCorruptWindow.xaml.cs` | on failure | active |
| Startup session validation | Per-account validation against `users.roblox.com` at startup. | (removed) | — | dead: removed in v1.1.2 because it tripped Roblox anti-fraud; do not add back |

## Runtime awareness (`Core/Diagnostics/`)

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Process tracking | Polls `RobloxPlayerBeta` every 750 ms for 30 s (120 s while the installer runs), claims the earliest unclaimed pid, raises ProcessAttached/Exited; five-step attach order is load-bearing. | `Core/Diagnostics/RobloxProcessTracker.cs` | always | active |
| Presence polling (presence-as-truth) | Every 25 s each account polls its own presence with its own cookie; a row is Closed only when presence and process tracking agree; stale-401 guard via cookie generation. | `Core/Diagnostics/PresenceService.cs`, `App/ViewModels/MainViewModel.cs` | always, accounts with `RobloxUserId` | active |
| Window decorator + re-attach scanner | Re-titles each client `Roblox - {name}` every 1.5 s and tints the DWM caption; the startup scanner parses that title to re-attach. | `App/Tray/RobloxWindowDecorator.cs`, `App/Tray/RunningRobloxScanner.cs`, `App/Tray/RobloxWindowTitle.cs` | always | active |
| Orphan sweeper + client succession | Every 5 s, adopts a bare-titled self-restarted client when the exit/orphan pairing is unambiguous; still-unowned pids logged once per set. | `App/Tray/OrphanedClientSweeper.cs`, `Core/Diagnostics/ClientSuccession.cs` | always | active |
| Activity / idle awareness | 1 s foreground-pid + `GetLastInputInfo` correlation; coalesced idle toast at the threshold; core observes only. | `Core/Diagnostics/ActivityMonitor.cs`, `App/Notifications/IdleAlertPresenter.cs` | `settings:idleWarnThresholdMinutes` (15), `settings:muteIdleAlerts` | active |
| Memory watchdog | 30 s private-bytes sample per tracked pid; per-client cap, aggregate growth projection, absolute headroom; warns only (tray, row chip, plugin stream), never recycles. | `Core/Diagnostics/MemoryWatchdog.cs`, `Core/Diagnostics/MemoryPressureEvaluator.cs`, `Core/Diagnostics/MemoryDefaults.cs` | `settings:memoryWatchdogEnabled` (true); `memoryReserveMb` / `memoryCapMb` (null = derive, 0 = off); `projectionWarnMinutes` | active |
| Footprint learner + launch headroom advisor | Learns this machine's per-client cost (p75 of settled samples) and feeds only the batch-launch "room for N more" dialog. | `Core/Diagnostics/ClientFootprintLearner.cs`, `Core/Diagnostics/LaunchHeadroomAdvisor.cs`, `App/Modals/LaunchHeadroomWindow.xaml.cs` | batch launch only | active |
| `NoteClientVersion` reset-on-upgrade | Drops learned samples when the Roblox build changes. | `Core/Diagnostics/ClientFootprintLearner.cs` | no production caller | half-finished, intent (unverified) |
| Recycle | Stops a client and relaunches it into the same server. | `Core/Diagnostics/AccountRecycler.cs`, `Core/ServerInstanceTargeting.cs`, `App/ViewModels/MainViewModel.cs` | button visible only while a memory warning is latched, or `settings:alwaysShowRecycle` | active |
| Server-instance targeting | `(placeId, jobId)` taken from one presence reading; a stale GameJob degrades to Place; a full server queues (verdict waits up to 4 min). | `Core/ServerInstance.cs`, `Core/ServerInstanceTargeting.cs`, `App/ViewModels/ServerLandingGate.cs` | automatic when presence knows the server | active |
| Client stop sequence | `SC_CLOSE` (Roblox ignores `WM_CLOSE`), ask again at 2 s, re-check each tick, force after the 10 s grace, write window state back; shared by the Stop button and plugin `StopAccounts`. | `Core/Diagnostics/ClientStopSequence.cs`, `Core/Diagnostics/RobloxInstanceStopper.cs`, `App/Plugins/Adapters/ProcessTrackerAccountStopper.cs` | `settings:autoForceStop` (false) collapses the grace | active |
| Stop all / clear strays | Hard-kill lane that waits on every probed pid. Tray/Tools "Stop all" confirms whenever any client is running; the startup recovery lanes (Close Roblox for me, leftover clean-up) confirm only when a windowed client exists. | `Core/Diagnostics/RobloxInstanceStopper.cs`, `App/Modals/StopAllConfirmWindow.xaml.cs`, `App/ViewModels/MainViewModel.cs`, `App/App.xaml.cs` | tray / Tools | active |
| Diagnostics page + support bundle | Best-effort probes (versions, installs, RAM, multi-instance state), log path, snapshot. Has its own installed-version scan that duplicates the compat checker's. | `Core/Diagnostics/DiagnosticsCollector.cs`, `App/Diagnostics/DiagnosticsPage.xaml.cs` | Shell page | active |

## Launch surfaces

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Squad Launch | Mass launch into one private server or public place; saved servers listed default-first, then most recent; accepts pasted links. | `App/SquadLaunch/SquadLaunchWindow.xaml.cs`, `App/SquadLaunch/SquadLaunchOrdering.cs`, `App/ViewModels/MainViewModel.cs` | toolbar button + shortcut | active |
| Trust-aware squad launch | Per-account `JoinViaFriend` flag; direct batch → 90 s anchor wait for a landed userId → follow-dispatch the flagged batch; careful mode serializes. | `App/ViewModels/SquadLaunchPlan.cs`, `App/ViewModels/AnchorGate.cs`, `App/ViewModels/LaunchEligibility.cs` | row context menu flag; `settings:carefulSquadLaunch` | active |
| Friends picker / follow-from-main | Per-row Friends modal (In game / Online / Offline); when a main account exists its list shows by default with a toggle back to the row's own list; `EvaluateFollow` guard shared with the follow-alt chip. | `App/Friends/FriendFollowWindow.xaml.cs`, `App/Friends/FriendSource.cs` | row button | active |
| Join by link | Paste a URL into a per-row sentinel for a one-shot launch, optionally saving to the library. | `App/JoinByLink/JoinByLinkWindow.xaml.cs`, `Core/JoinByLinkSave.cs` | per-row dropdown | active |
| Games library | Search, add by URL or place id, set/clear default game and server, rename, remove. | `App/Games/GamesPage.xaml.cs`, `Core/FavoriteGameStore.cs`, `Core/PrivateServerStore.cs` | Shell page | active |
| Default private server | A default separate from the default game; Squad Launch lists it first with a Default badge; removing it leaves zero default. | `Core/SavedPrivateServer.cs`, `Core/PrivateServerStore.cs` | Games page | active |
| Launch main on startup | ~5 s plus backfill time after show; skipped if no main, main running, any Roblox running, or main expired. | `App/App.xaml.cs`, `Core/AppSettings.cs` | `settings:launchMainOnStartup` (false) | active |

## Sessions and stats

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Session history | Rolling 100-row window (`session-history.json`), one row per launch. Since 2026-08-30 (F-125) the end follows the row's both-signals rule: presence-confirmed for presence-capable accounts, exit alone for the rest; a launch that never attached keeps a null end with a "Never connected" hint (`MarkOutcomeAsync`) and stays out of the stats uptime. Display names baked at write time. | `Core/SessionHistoryStore.cs`, `Core/LaunchSession.cs`, `App/History/SessionHistoryPage.xaml.cs` | always | active |
| Session stats rollup | Decorator over the history store folds every launch/end into `session-stats.json` (totals, streaks stored as records); peak concurrency and per-alt landing streaks are fed straight from `App.xaml.cs` off `ProcessAttached` and presence-confirmed InGame. | `Core/StatsRecordingSessionHistoryStore.cs`, `Core/SessionStatsStore.cs`, `App/History/SessionStatsPresenter.cs`, `App/App.xaml.cs` | always; local only | active |
| Account stats: uptime + per-game time | The 2026-07-09 presence-driven design. | `docs/superpowers/specs/2026-07-09-account-stats-uptime-design.md` | — | legacy: superseded by the v1.23 rollup |

## Theming and UI

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Built-in themes | brand (default), midnight, magenta-heat, flatline (achromatic). Ten authored slots; the count is an invariant. | `Core/Theming/ThemeStore.cs`, `Core/Theming/Theme.cs` | `settings:activeThemeId` | active |
| User JSON themes | Any snake_case `*.json` in `themes\`; invalid files are dropped silently; no hex validation. | `Core/Theming/ThemeStore.cs`, `docs/themes/template.json` | drop a file | active |
| Theme builder + embedded AI prompt | Copies `docs/themes/AGENT_PROMPT.md` (embedded into the exe by the csproj) to the clipboard, accepts pasted JSON, saves and applies. | `App/Theming/ThemeBuilderWindow.xaml.cs`, `App/ROROROblox.App.csproj` | Settings › Appearance | active |
| Derived interactive edge + remediation question | Edge colour derived from Divider to clear 3:1 against Navy and RowBg; built-ins derive silently, a user theme is asked once. | `Core/Theming/ContrastGuard.cs`, `Core/Theming/EdgeRemediation.cs`, `App/Modals/EdgeRemediationWindow.xaml.cs` | `settings:edgeRemediationAnswers` per theme | active |
| Derived `OnMagentaBrush` | Label colour on magenta fills, picked then nudged to 4.5:1. | `App/Theming/ThemeService.cs` | always | active |
| Theme apply by brush replacement | `ApplySlot` replaces brush instances in `Application.Resources`; only `DynamicResource` survives; the applied palette is read back as `ResolvedPalette`. | `App/Theming/ThemeService.cs`, `App/App.xaml` | always | active |
| Button rank vocabulary + owned templates | Ten `*ButtonStyle` ranks over three owned templates; hover/pressed are translucent sheens, never a Chrome repaint or Opacity. | `App/Controls/ControlStyles.xaml` | always | active |
| Type ladder + font roles | 22 / 14 / 12 / 11 px tokens plus Display and Mono families, all `DynamicResource`. | `App/Controls/ControlStyles.xaml` | always | active |
| ShellWindow (F-013) | Games, Settings, History, Diagnostics, Plugins, About as lazily built `UserControl` pages behind one rail; every door goes through `App.OpenShellPage`. | `App/Shell/ShellWindow.xaml.cs`, `App/Shell/ShellPage.cs`, `App/App.xaml.cs` | always | active |
| Settings page | Five sections behind an inner rail; toggles persist immediately, no Apply; disposes its singleton subscriptions. | `App/Preferences/SettingsPage.xaml.cs` | Shell page | active |
| About page + welcome tour | Brand mark with fixed brushes on a themed plate, easter egg, shortcut list; tour shows on first run when the list is empty (`.welcome-shown` sentinel written inside that branch). | `App/About/AboutPage.xaml.cs`, `App/About/WelcomeWindow.xaml.cs` | sentinel + empty list | active |
| Keyboard vocabulary | One shortcut table feeds MainWindow and Shell bindings, menu hints, and the About list; `Ctrl+Shift+R/P/A/L` reserved for Ur Task's global hotkeys. | `App/Input/KeyboardVocabulary.cs` | always | active |
| Dark title bar for plain windows | DWM immersive dark mode on every `Window`; only MainWindow is a WPF-UI `FluentWindow`. | `App/Theming/WindowTheming.cs`, `App/MainWindow.xaml` | always | active |
| FluentWindow chrome for modals | Converting the 21 plain windows to `FluentWindow` + `TitleBar`. | `docs/superpowers/specs/2026-07-09-themed-window-chrome-design.md` | — | parked |
| Caption colour picker | Per-account DWM title-bar colour; swatches whose fill is the value (fenced exemption). | `App/Theming/CaptionColorPickerWindow.xaml.cs`, `App/Converters.cs` | row swatch click | active |
| Window placement memory | All-four-or-nothing rect with an on-screen guard; saved before hide-to-tray, restored after compact-mode restore. | `Core/WindowPlacement.cs`, `App/MainWindow.xaml.cs` | always | active |
| Sticky compact mode | Status-bar Compact/Expand toggle persisted. | `Core/AppSettings.cs`, `App/ViewModels/MainViewModel.cs` | `settings:compactMode` | active |
| Streamer mode | Persistent fake names (98-name pool) and avatars (12 PNGs) across rows, window titles, friends, rename lines and Discord; masks the account manager, never in-game. Toggles are two-way bound, not clicked. | `Core/StreamerMode/StreamerIdentityProvider.cs`, `App/Tray/StreamerModeFlag.cs`, `Core/StreamerMode/FileStreamerIdentityStore.cs` | `settings:streamerMode` (false) | active |
| Tray | Hardcodet `TaskbarIcon` with per-state ICOs, memory-warn overlay that never overrides Error, balloon click replays the account; wiring is a testable table. | `App/Tray/TrayService.cs`, `App/Tray/TrayWiring.cs` | always | active |
| Tools dropdown | A Button owning a ContextMenu with an ExpandCollapse automation pattern (not a Menu; two nearby comments say otherwise). | `App/Controls/ToolsDropDownButton.cs` | always | active |

## Discord

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Rich presence + idle card | One roster-level card via Lachee `DiscordRichPresence` over Discord's local pipe; idle payload when nothing runs; pushes are unthrottled. | `App/Discord/DiscordPresenceService.cs`, `Core/Discord/PresencePayloadBuilder.cs`, `App/Discord/Internal/LacheeDiscordRpcClientAdapter.cs` | `Discord:ApplicationId` non-empty in `appsettings.json` (the committed file ships it) and `PresenceEnabled` in `discord.dat` | active |
| Discord Join, outbound and inbound | Pipe-delimited join secret (<128 chars); inbound via the in-client Join button or the `roblox-rororo:` URI (cold start or single-instance relay); confirm keyed on origin. The URI path is inert on packaged builds — verified live 2026-08-30 (packaged HKCU writes are virtualized). | `Core/Discord/JoinSecretCodec.cs`, `App/Discord/InboundJoinDispatcher.cs`, `App/Discord/JoinUriScheme.cs`, `App/Modals/JoinRequestWindow.xaml.cs` | `PresenceEnabled && JoinEnabled` | active |
| Alerts: dropped out, memory warning | Pure router (per-account mute, 5-min cooldown per (account, kind), coalescing, desktop fallback) then tray toast or webhook POST; the clan destination gets real names. | `Core/Discord/AlertRouter.cs`, `App/Discord/AlertDispatcher.cs`, `App/Discord/DiscordWebhookSender.cs`, `Core/Discord/WebhookPayload.cs` | destinations in `discord.dat` (default None); independent of the app id | active |
| Webhook UX | Paste validation, masking with explicit reveal, channel-name probe, Send test, in-memory 404 latch, honest status line. | `Core/Discord/WebhookUrlValidator.cs`, `Core/Discord/WebhookUrlMasker.cs`, `App/Discord/WebhookProbe.cs`, `Core/Discord/AlertStatusLine.cs` | Settings › Alerts | active |
| Discord config single owner | DPAPI `discord.dat`; serialized `MutateAsync`, torn-free `Current`, `Changed` raised inside the write gate. | `Core/Discord/DiscordConfigService.cs`, `Core/Discord/DiscordConfigStore.cs` | always registered | active |
| Presence push throttle (15 s) | Spec §6. | `docs/superpowers/specs/2026-08-03-discord-presence-alerts-design.md` | — | parked |
| Webhook retry / backoff | Spec §8 (429 Retry-After, 5xx backoff). | same spec; `App/Discord/DiscordWebhookSender.cs` | — | parked |
| May 2026 clan-coordination design | Per-account presence, plaintext config, lifecycle triggers. | branch `feat/discord-clan-coordination` only | — | legacy, unmerged |

## Plugins

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Named-pipe gRPC host | Kestrel on `\\.\pipe\rororo-plugin-host` (one shared pipe); binds before the gate modals; a bind failure (including an unmapped RPC) disables plugins for the session with only a Debug log, whatever the code comments say about crashing. Per-user ACL verified on the live pipe 2026-08-30: one ACE, FullControl to the current user only (Kestrel's `CurrentUserOnly` default; nothing in code sets it). | `App/Plugins/PluginHostStartupService.cs`, `App/Plugins/PluginHostService.cs` | always | active |
| Manifest + capability vocabulary | `schemaVersion 1`; 14 enforced `host.*` capabilities, 6 disclosure-only `system.*`. | `App/Plugins/PluginManifest.cs`, `App/Plugins/PluginCapability.cs` | always | active |
| Capability interceptor + exhaustive map | Identity is the `x-plugin-id` header; absence from the map is denial; `AssertExhaustive` runs before bind. | `App/Plugins/CapabilityInterceptor.cs`, `App/Plugins/RpcMethodCapabilityMap.cs` | always | active |
| Consent store + sheet | DPAPI `consent.dat`; revoke means uninstall; an existing record never re-prompts. | `App/Plugins/ConsentStore.cs`, `App/Plugins/ConsentSheet.xaml.cs`, `App/Plugins/PluginsViewModel.cs` | always | active |
| SHA-verified installer | https-only; `manifest.json` + `manifest.sha256` + `plugin.zip`; zip-slip guard; stops a running plugin first. | `App/Plugins/PluginInstaller.cs` | user | active |
| Process supervision + job object | Unnamed kill-on-close job (the only unsafe code), startup orphan sweep, launch failures visible only in the host log. | `App/Plugins/PluginProcessSupervisor.cs`, `App/Plugins/Adapters/PluginJobObject.cs` | always; failure degrades | active |
| Event streams | AccountLaunched / AccountExited / MutexStateChanged / MemoryPressure over bounded channels. | `App/Plugins/InProcessPluginEventBus.cs`, `App/Plugins/PluginHostService.cs` | `host.events.*` | active |
| Command / query surface | RequestLaunch, RequestLaunchTarget (can hold the RPC up to ~45 s), GetCurrentServer, GetRunningAccounts, GetAccounts (0.9.0), GetAccountActivity, MarkAccountActive, StopAccounts (runs the stop sequence since F-121), GetHostInfo. | `App/Plugins/PluginHostService.cs`, `App/Plugins/Adapters/` | `host.commands.*` / `host.queries.*`; `Handshake`, `GetHostInfo`, `GetRunningAccounts` are ungated | active |
| Theme feed | `GetTheme` / `SubscribeThemeChanged`, eleven slots, deliberately ungated. | `App/Plugins/Adapters/ThemeFeedAdapter.cs` | always | active |
| Marketplace | `plugins-catalog.json` from the latest release; Available section and update badges. | `App/Plugins/PluginCatalogClient.cs`, `App/Plugins/MarketplacePlan.cs`, `App/Plugins/PluginsViewModel.cs` | `!Win32DistributionMode.IsPackaged` (runtime probe) | active |
| Duplicate folder resolution | Keep-the-first with a standing banner. | `App/Plugins/PluginDuplicates.cs` | always | active |
| Plugin UI surfaces | `AddTrayMenuItem` / `AddRowBadge` / `AddStatusPanel` issue handles; `WpfPluginUIHost` is a logging stub. | `App/Plugins/Adapters/WpfPluginUIHost.cs`, `App/Plugins/PluginUITranslator.cs` | `host.ui.*` | half-finished, parked |
| Manifest `autostartDefault` | Parsed and validated; `InstallAsync` discards it, so a first install is always autostart-off. | `App/Plugins/PluginInstaller.cs`, `App/Plugins/PluginsViewModel.cs` | manifest field | half-finished, intent (unverified) |
| Host → plugin callbacks | `Plugin` service (`OnUIInteraction`, `OnConsentChanged`, `OnShutdown`); the host never builds a client. | `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` | — | dead, intent (unverified) |
| Mid-stream consent revocation | Cancelling open streams on revoke. | `Harness/EndToEndContractTests.cs` (skipped test) | — | half-finished since v1.4 |
| `ROROROblox.PluginContract` NuGet | `netstandard2.1`; ships the `.proto` under `content/`; manual OIDC publish. nuget.org holds 0.1.0, 0.3.0, 0.4.0, 0.8.0, 0.9.0. | `src/ROROROblox.PluginContract/ROROROblox.PluginContract.csproj`, `.github/workflows/publish-nuget.yml` | `workflow_dispatch` | active |
| MCP connector (`rororo-ur-mcp`) | A consent-gated plugin in a sibling repo, launched by Claude over stdio; this repo contributed `GetAccounts`. | `docs/superpowers/specs/2026-07-04-mcp-connector-design.md`, `docs/store/plugins-catalog.json` | plugin consent | active, code lives elsewhere |

## Lifecycle, distribution, update

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| Velopack-first entry + portable AUMID | `VelopackApp.Build().Run()` before WPF; a `.portable` marker one level up gets its own AppUserModelID. | `App/Program.cs` | always / marker file | active |
| Single-instance guard + pipe relay | Mutex `Local\ROROROblox-app-singleton` + pipe; `InboundJoinRelay` is the exception boundary that keeps the listener alive. | `App/AppLifecycle/SingleInstanceGuard.cs`, `App/Discord/InboundJoinRelay.cs` | always | active |
| Hide-to-tray + explicit shutdown | `ShutdownMode=OnExplicitShutdown`; X hides; the process ends via tray Quit, the startup gate's Quit, or losing the single-instance mutex. | `App/App.xaml`, `App/MainWindow.xaml.cs` | always | active |
| Serilog logging | Daily file, 25 MB roll, 30 files; app namespaces Debug, Microsoft/System Warning; `{Version}` in the template. | `App/Logging/AppLogging.cs` | always | active |
| Update check (Velopack) | Once per 24 h via `GithubSource(prerelease:false)`; logs "Update available" and stops. No download/apply call exists in `src`. | `App/Updates/UpdateChecker.cs` | Velopack `IsInstalled` | half-finished: check-only, confirmed 2026-08-30 against Velopack's API (nothing applies unless the app calls `DownloadUpdatesAsync` + `ApplyUpdatesAndRestart`); direct-download installs stay on the version they installed |
| Distribution-mode probe | `GetCurrentPackageFullName`; consumed only by the marketplace gate. | `App/Distribution/Win32DistributionMode.cs` | runtime | active |
| Run on login | HKCU `Run` value `RORORO` = exe path. | `App/Startup/StartupRegistration.cs` | Settings toggle | active on unpackaged builds; on Store/sideload builds the HKCU write lands in the package's virtual hive and does nothing (verified live 2026-08-30) |
| Render-harness startup suppression | Static flag the test harness sets so `new App()` does not run real startup. | `App/App.xaml.cs`, `Tests/Rendering/WindowRenderHost.cs` | tests only | active |

## Build, CI, tooling

| Feature | What it does | Paths | Gating | Status |
|---|---|---|---|---|
| CI gate | `guards` (full-tree secret scan + local-path guard) then full-solution build+test on `windows-latest` and `windows-11-arm`. | `.github/workflows/ci.yml` | push to main, PRs | active |
| Tag-triggered Velopack release | Draft GitHub Release, then sign and upload `roblox-compat.json` + `.sig` + `plugins-catalog.json`. | `.github/workflows/release.yml`, `scripts/build-velopack-release.ps1` | tag `v*.*.*(.*)` | active |
| Compat-only push | Signs and re-attaches `roblox-compat.json` + `.sig` to the current latest release with no tag and no binary. | `.github/workflows/compat.yml` | `workflow_dispatch` | active (first dispatch 2026-08-30: no-op re-sign of v1.23.0.0's assets; the re-downloaded pair verified against the pinned key) |
| Store MSIX build | Version sync (X.Y.Z.0 rule) then `build-msix.ps1 -Store`, x64 and arm64, unsigned. | `scripts/finalize-store-build.ps1`, `scripts/build-msix.ps1` | manual | active |
| Sideload MSIX + dev cert | `generate-dev-cert.ps1` (subject = the Partner Center CN, so no manifest swap) + `build-msix.ps1 -Sideload`. | `scripts/generate-dev-cert.ps1`, `scripts/build-msix.ps1` | manual | active |
| Local MSIX install / uninstall | `install-local-msix.ps1` expects the pre-2026-08-12 unversioned `dist\RORORO-Sideload.msix`; the uninstall script queries by package identity and is unaffected. | `scripts/install-local-msix.ps1`, `scripts/uninstall-local-msix.ps1` | manual | half-finished (stale) |
| Portable zip | `vpk pack` emits `RORORO-win-Portable.zip`; attached to releases; no script names it. | `App/Program.cs`, `README.md` | — | active (unverified provenance) |
| Pre-commit hooks | Secret scan + local-path guard, git-installed per box; CI runs the same scripts. | `.claude/hooks/install.ps1`, `.claude/hooks/pre-commit-secret-scan.sh`, `.claude/hooks/pre-commit-local-path-guard.sh` | per-box install | active |
| Asset generators + logo gate | `generate-store-assets.ps1` renders the 52 Store logos and 9 listing graphics; `generate-tray-icons.ps1` renders on/off/error (the warn pair was added by hand); `build-msix.ps1` refuses placeholder logos. | `scripts/generate-store-assets.ps1`, `scripts/generate-tray-icons.ps1`, `scripts/build-msix.ps1` | manual | active |
| UI capture harness | UIA-driven per-theme evidence PNGs and Store screenshots; routes in `docs/ui-routes.json` (schema tests check shape only). | `scripts/capture-ui.ps1`, `docs/ui-routes.json`, `docs/ui-capture-checklist.md` | manual, needs a running app | active |
| Button-site counter | Measurement, not a gate (always exits 0); the gate is `ButtonRankFenceTests`. | `scripts/count-button-sites.ps1` | manual | active (instrument) |
| Client memory sampler | Samples `RobloxPlayerBeta` private bytes to calibrate `MemoryDefaults`. | `scripts/measure-client-memory.ps1` | manual | active (unverified) |
| Auth-ticket spike | The item-1 hard gate for the auth-ticket contract. | `spike/auth-ticket/` (gitignored) | manual | unknown: present on the author's box only |

## Test instruments

| Family | What it enforces | Paths | Status |
|---|---|---|---|
| Fences | Source-scanning rules with vacuity floors and ratchet ceilings: button ranks, colour literals, accessible names, type ladder, window chrome and sizing, dismiss order, keyboard vocabulary, brand name, interactive-edge binding, XAML style integrity, command bindings, settings reachability. | `Tests/*FenceTests.cs`, `Tests/ThemedStatusColourTests.cs`, `Tests/XamlStyleIntegrityTests.cs`, `Tests/CommandBindingIntegrityTests.cs`, `Tests/SettingsReachabilityTests.cs`, `Tests/KeyboardVocabularyTests.cs`, `Tests/InteractiveEdgeBindingTests.cs` | active |
| Gates | `ContrastPairGateTests` (token pairs through the real theme service; exemptions must cite an open register row), `FlatlineLabGateTests`, `Rendering/RenderedStyleGateTests`, `Rendering/ButtonStateGateTests`, whole-window renders (`AboutMark`, `BannerPair`, `HistoryRow`, `WindowContentFits`, `ConsentSheetFooter`). | `Tests/ContrastPairGateTests.cs`, `Tests/Rendering/` | active; whole-window gates run on CI since 2026-08-21 |
| Wiring | Real `App.ConfigureServices` resolution: theme feed, saved accounts, session stats (plus one test in `RobloxLauncherTests`); the typed-client single-ctor guard re-registers the five clients in its own container. | `Tests/TypedHttpClientRegistrationTests.cs`, `Tests/ThemeFeedWiringTests.cs`, `Tests/SavedAccountsWiringTests.cs`, `Tests/SessionStatsWiringTests.cs` | active |
| Modal safety | Parses modal XAML copied into the test output; a new modal must be linked in the Tests csproj. | `Tests/ModalDefaultButtonSafetyTests.cs`, `Tests/ROROROblox.Tests.csproj` | active |
| Integration harness | Real Kestrel + per-test pipe + `Grpc.Net.Client`; most tests hardcode the plugin accessor, only `*_ProductionAccessor_*` tests use the header path production depends on. | `Harness/EndToEndContractTests.cs` | active |
| CI skip of whole-window gates | `[WindowRenderFact]` skipping on CI with `RORORO_FORCE_WINDOW_RENDER`. | `Tests/Rendering/WindowRenderFactAttribute.cs` | dead: removed 2026-08-21, the attribute remains as the single future skip site |

## Unbuilt, superseded, or ruled dead

| Item | Where the design lives | Status |
|---|---|---|
| Account groups (named launch sets) | `docs/superpowers/specs/2026-07-09-account-groups-design.md` | abandoned (ruled 2026-08-30) |
| Per-cookie encryption + per-account WebView2 profiles | promised for "v1.2" in the canonical spec; per-capture GUID dirs shipped instead | never built, intent (unverified) |
| `RobloxLauncher.NormalizeToPlaceLauncherUrl` | `Core/RobloxLauncher.cs` | legacy, no production callers (still exercised by `Tests/RobloxLauncherTests.cs`) |
| `singletonMutexName` JSON key rename | `docs/superpowers/specs/2026-05-28-remote-config-mutex-name-design.md` §1 | never adopted; wire key is `mutexName` |
