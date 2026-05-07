# ROROROblox v1.2 — Discord clan-coordination build checklist

**Cycle type:** Feature extension to v1.0. Spec at [`docs/superpowers/specs/2026-05-06-discord-clan-coordination-design.md`](superpowers/specs/2026-05-06-discord-clan-coordination-design.md). Extends canonical at [`docs/superpowers/specs/2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md).

**Build mode:** autonomous-with-verification (checkpoints after items 7 and 10)
**Comprehension checks:** off (autonomous mode default for experienced + Architect persona + Builder mode + brisk pacing — same posture as v1.0)
**Git cadence:** commit after each item
**Branch:** `feat/discord-clan-coordination` from `main` (post v1.1.2.0)
**PR:** single PR shipping all three layers per spec §11 decision (override on small-diff-preferred posture; rationale in spec)
**Repo:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)
**Atlas entry:** [`.vibe-iterate/atlas.jsonl`](../.vibe-iterate/atlas.jsonl) — `competitive` / `queued` (flips to `shipped` on merge)
**Dashboard decision:** `PlA9sfFzlQ2qINOO1kLB` (project `PBWgg5mimZyAzAG3niAp`)

---

- [ ] **1. Discord app registration + NuGet + DI scaffolding (administrative gate)**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §3 Stack additions`, `§5.8 DI wiring`, `§10 Open items`
  What to build: **Este action (offline):** create a Discord application at https://discord.com/developers/applications named "ROROROblox", record the Application ID. **Build action:** add the AppId to `src/ROROROblox.App/appsettings.json` under key `Discord:ApplicationId` (string). Add `DiscordRichPresence` NuGet (Lachee, ≥1.2.x stable) to `ROROROblox.App.csproj`. Stub the three new Core interfaces in `src/ROROROblox.Core/`: `IDiscordPresence`, `IDiscordWebhook`, `IDiscordConfig`. Stub the supporting records: `RichPresenceState`, `PresenceMode` enum, `DiscordWebhookEvents`, `DiscordConfigSnapshot`, `JoinRequestedEventArgs`. Stub the three implementations in `src/ROROROblox.App/Discord/`: `DiscordRichPresenceService`, `DiscordWebhookService`, `DiscordConfigStore` — empty bodies + `NotImplementedException` so the build is green but runtime would throw. Register all three as singletons in `App.xaml.cs.OnStartup`. Add `DiscordPresenceLifecycle : IHostedService` stub + DI registration.
  Acceptance: `dotnet build` clean (zero warnings beyond NuGet metadata). DI container resolves all four registrations without exception. App startup completes when `Discord:ApplicationId` is set; logs a warning + skips Discord init if unset (graceful degrade — Layer 1 OFF). 61/61 existing tests still green.
  Verify: `dotnet build && dotnet run`. App starts. Console logs no Discord-related exceptions. Commit: `feat(discord): scaffolding — NuGet, interfaces, DI registration, AppId wiring`. **HARD GATE: do NOT start item 2 if `Discord:ApplicationId` is unset — pause and prompt Este.**

- [ ] **2. DiscordConfigStore + IDiscordConfig**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.6 IDiscordConfig + DiscordConfigStore`
  What to build: Implement `DiscordConfigStore` per spec §5.6. File path: `%LOCALAPPDATA%\ROROROblox\discord-config.json` (sibling to `accounts.dat`). Atomic write via `.tmp` + `File.Replace(dst, tmp, null)`. On load: missing file → return `(false, null, AllOff)` defaults. Malformed JSON → log warning at `ILogger<DiscordConfigStore>`, preserve the corrupt file as `discord-config.json.corrupt-{yyyyMMdd-HHmmss}`, return defaults. `FileSystemWatcher` on the file → fire `IDiscordConfig.Changed` event on save (so consumers reload without app restart). **NOT DPAPI-encrypted** — webhook URL is a clan-shared resource, not a per-user secret (spec §11 decision).
  Acceptance: `dotnet test --filter DiscordConfigStoreTests` covers: roundtrip (save → load → equal), missing-file defaults, malformed-JSON recovery + corrupt-file preservation, atomic-write fault injection (kill mid-write, dst file intact), file-watcher fires `Changed` on external write.
  Verify: `dotnet test`. Manual: edit the JSON file in a text editor while app is running, observe `Changed` debug log. Commit: `feat(core): DiscordConfigStore with atomic JSON + file watcher`.

- [ ] **3. ServerShareExtractor (pure)**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.5 ServerShareExtractor`, `§6.2 server-share + party-join flow`
  What to build: Static class `ROROROblox.App.Discord.ServerShareExtractor`. Single method: `static string? TryExtractPrivateServerUrl(string launchUri)`. Algorithm per spec §5.5:
  1. Decode the `roblox-player:` URI body to plus-separated `key:value` list.
  2. Look up `placelauncherurl` value.
  3. URL-decode it.
  4. If decoded value contains `accessCode=` query param OR `privateServerLinkCode=` path segment → return as `string`.
  5. Else → return `null`.

  Golden test fixtures at `src/ROROROblox.Tests/Discord/Fixtures/`: `launch-uri-private.txt`, `launch-uri-public.txt`, `launch-uri-malformed.txt`, `launch-uri-missing-key.txt`. Each fixture is the raw URI string (one per file).
  Acceptance: Private fixture returns the share URL containing `accessCode` or `privateServerLinkCode`. Public fixture returns null. Malformed (truncated, bad escape sequences) returns null without throwing. Missing-key (no `placelauncherurl=`) returns null.
  Verify: `dotnet test --filter ServerShareExtractorTests`. Commit: `feat(app): ServerShareExtractor with golden fixtures`.

- [ ] **4. DiscordRichPresenceService (Layer 1)**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.2 DiscordRichPresenceService`, `§6.1 rich-presence update flow`
  What to build: Implement `IDiscordPresence` per spec §5.2. Owns `DiscordRpcClient` from Lachee.DiscordRichPresence. Constructor: `(IDiscordConfig config, ILogger<DiscordRichPresenceService> log, IConfiguration appConfig)` — pulls AppId from `appConfig["Discord:ApplicationId"]`. `StartAsync` no-ops if `config.RichPresenceEnabled == false`; else `client.Initialize()` + `Invoke()` connects to local Discord IPC named pipe. `UpdateStateAsync(RichPresenceState)` translates → `DiscordRPC.RichPresence` with branded asset keys hardcoded to `idle_large` / `active_large` / `idle_small` / `active_small` (these match Discord developer portal slot names from item 10). `SetPartyAsync(serverShareUrl)` → presence with `Party { Id = SHA256(url).Substring(0,16), Privacy = Public, Max = 6 }` and `Secrets { JoinSecret = Base64(url) }`. `ClearPartyAsync` → presence without party state. `client.OnJoin` event → raise `IDiscordPresence.JoinRequested` with `Base64Decode(args.Secret)` as the URL. Reconnect strategy: on `OnConnectionFailed` event, schedule reconnect at `5/10/20/40/60s` exponential backoff via `System.Threading.Timer`; never give up until `DisposeAsync`. Subscribe to `config.Changed` → if `RichPresenceEnabled` flipped OFF, call `client.ClearPresence()` + `client.Deinitialize()`; if flipped ON, restart connection.
  Acceptance: Mock `IDiscordRpcClient` (extracted via wrapper interface to keep Lachee.DiscordRpcClient mockable). Verify `Initialize()` called only when enabled. State transitions update presence with correct mode + asset keys. Reconnect backoff schedule matches 5/10/20/40/60s. `JoinRequested` decodes Base64 join secret. Config-changed events flip the connection state correctly.
  Verify: `dotnet test --filter DiscordRichPresenceServiceTests`. Manual: with `RichPresenceEnabled = true` + real Discord client running, observe presence appears in user's Discord profile within 1s. Commit: `feat(app): DiscordRichPresenceService — Layer 1 rich presence`.

- [ ] **5. RobloxLauncher hookup for server-share + SetParty (Layer 2 outbound)**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §6.2 server-share + party-join flow (outbound)`
  What to build: Modify `RobloxLauncher.LaunchAsync` (existing in `ROROROblox.Core` from v1.0 item 6) — after the URI is built but before `Process.Start`, call `ServerShareExtractor.TryExtractPrivateServerUrl(uri)`. After `Process.Start` succeeds, fire-and-forget `IDiscordPresence.SetPartyAsync(shareUrl)` if non-null, else `ClearPartyAsync()`. **DiscordPresence is injected as optional dependency** (`IDiscordPresence?`) — null in tests + when Discord disabled, no-op when null. Never let Discord errors fail the launch — wrap calls in `try/catch` + log warning. Add `IDiscordPresence?` param to `RobloxLauncher` constructor.
  Acceptance: Test (with mock `IDiscordPresence`): launch with private-server URI calls `SetPartyAsync(extractedUrl)`. Test: launch with public-game URI calls `ClearPartyAsync`. Test: `IDiscordPresence` is null → no-op, launch still succeeds. Test: `IDiscordPresence.SetPartyAsync` throws → launch result still `Started`, exception logged. **Existing 61 tests still green** — RobloxLauncher contract unchanged for callers without Discord.
  Verify: `dotnet test --filter RobloxLauncherTests`. Commit: `feat(core): RobloxLauncher hooks ServerShareExtractor + SetParty (optional dep)`.

- [ ] **6. IAccountLifecycle + AccountLifecycleTracker (new abstraction)**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.8 DI wiring` (closes the assumption that `IAccountLifecycle` exists in canonical §5)
  What to build: **New abstraction not present in v1.0.** Spec §5.8 assumes `IAccountLifecycle` from canonical §5; it doesn't exist there. This item closes the gap.
  - `IAccountLifecycle` interface in Core: events `event EventHandler<AccountStartedEventArgs> AccountStarted` and `event EventHandler<AccountStoppedEventArgs> AccountStopped`. Method `void NotifyStarted(Account account, int processId)` (called by MainViewModel after a successful launch).
  - `AccountStartedEventArgs(Account account, int processId, int currentActiveCount)`, `AccountStoppedEventArgs(Account account, int exitCode, int currentActiveCount)`.
  - `AccountLifecycleTracker : IAccountLifecycle` in App. Maintains `Dictionary<int, Account>` of active processes. `NotifyStarted` adds + subscribes `Process.GetProcessById(processId).Exited` → on exit, removes + raises `AccountStopped`. Thread-safe via `lock(_gate)`.
  - Modify `MainViewModel.LaunchAccountCommand` (existing v1.0 item 9): on `LaunchResult.Started(processId)`, call `_accountLifecycle.NotifyStarted(account, processId)` before returning.
  - Register `IAccountLifecycle` as singleton in `App.xaml.cs.OnStartup`.

  Acceptance: Test: `NotifyStarted` raises `AccountStarted` with current count = 1. Test: `Process.Exited` fires → `AccountStopped` raised with count = 0. Test: parallel `NotifyStarted` calls thread-safe (no count drift under stress). Test: process killed externally between `NotifyStarted` and event handler attach → `AccountStopped` still fires (race-condition coverage). MainViewModel test: launch flow calls `NotifyStarted` exactly once on success, zero times on failure.
  Verify: `dotnet test --filter AccountLifecycleTrackerTests --filter MainViewModelTests`. Commit: `feat(core): IAccountLifecycle + AccountLifecycleTracker (closes spec §5.8 assumption)`.

- [ ] **7. DiscordPresenceLifecycle (HostedService) — wires it all together**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.8 DI wiring`, `§6.1 rich-presence update flow`, `§6.2 party-join flow (inbound)`
  What to build: Implement `DiscordPresenceLifecycle : IHostedService` in App per spec §5.8. Constructor: `(IDiscordPresence presence, IDiscordWebhook webhook, IAccountLifecycle lifecycle, IRobloxLauncher launcher, IAccountStore accountStore, ILogger<DiscordPresenceLifecycle> log)`.
  - `StartAsync(ct)`: call `_presence.StartAsync(ct)`. Subscribe `_presence.JoinRequested` → handler resolves the most-recently-launched account from `_accountStore` (max `LastLaunchedAt`) → calls `_launcher.LaunchAsync(account.Id, joinArgs.ServerShareUrl)`. Subscribe `_lifecycle.AccountStarted` → updates presence to `(AccountsActive, currentCount, "Multi-clienting")` + calls `_webhook.PostLaunchAsync(currentCount, ct)` + `_webhook.PostAccountThresholdAsync(currentCount, ct)`. Subscribe `_lifecycle.AccountStopped` → updates presence; if count drops to 0, set `Idle`.
  - `StopAsync(ct)`: unsubscribe events + `_presence.DisposeAsync()`.
  - All event handlers wrap in try/catch + log; never let Discord errors propagate to event sources.

  Acceptance: Integration test wires real `DiscordPresenceLifecycle` + fake `IDiscordPresence` + fake `IDiscordWebhook` + fake `IAccountLifecycle` + fake `IRobloxLauncher` + fake `IAccountStore`. Simulate: lifecycle fires `AccountStarted` → presence updated + webhook called (if enabled) + threshold logic checks. JoinRequested → launcher called with most-recent account + share URL. StartAsync/StopAsync lifecycle correct.
  Verify: `dotnet test --filter DiscordPresenceLifecycleTests`. Commit: `feat(app): DiscordPresenceLifecycle wires presence + webhook + lifecycle events`.

- [ ] **CHECKPOINT 1** (after item 7 — Layers 1+2 functional, no webhook + no UI yet)
  Manual review: real Discord client running, real Roblox installed, `discord-config.json` hand-edited to `{ richPresenceEnabled: true, webhookUrl: null, webhookEvents: { all OFF } }`. Walk:
  1. Launch RORORO → no presence yet (no accounts active).
  2. Launch one account into Pet Sim 99 public game → presence shows "1 account active" with NO Join button.
  3. Launch second account into a private server → presence updates count + Join button appears.
  4. From a second Windows machine with RORORO + a different Discord account, click Join on this user's presence → Roblox opens to the same private server.
  5. Quit Discord client → RORORO continues to function; relaunch Discord → presence reconnects within 60s.

  If anything is shaky, **fix before item 8**. Webhook + Settings UI work will paper over IPC bugs.

- [ ] **8. DiscordWebhookService (Layer 3)**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.3 IDiscordWebhook`, `§5.4 DiscordWebhookService`, `§6.3 webhook payload`
  What to build: Implement `DiscordWebhookService` per spec §5.4. Constructor: `(IDiscordConfig config, IHttpClientFactory httpFactory, ILogger<DiscordWebhookService> log)`. Use `httpFactory.CreateClient("DiscordWebhook")` + 5s per-call timeout.
  - `PostLaunchAsync(int accountCount, ct)`: early-return if `!config.WebhookEvents.OnLaunch || string.IsNullOrEmpty(config.WebhookUrl)`. POST branded embed per spec §6.3 (color decimal `1561082` = `0x17D4FA`, footer "ROROROblox · Imagine Something Else.").
  - `PostServerJoinAsync(string serverShareUrl, ct)`: similar gate; embed includes `url` field for clickable title.
  - `PostAccountThresholdAsync(int accountCount, ct)`: tracks `_lastFiredAt`. Only fires on **crossing** 4+ from below; never re-fires while staying above; resets after 30-min quiet window.
  - All `Post*` methods catch `HttpRequestException`, `TaskCanceledException`, `JsonException`. Log at `LogLevel.Warning` (URL redacted to scheme + host only — `https://discord.com/...`). **Never throws to caller.**
  - On HTTP 5xx: retry once with 1s delay. On 4xx: no retry. On rate-limit (429): respect `Retry-After` header up to 30s, then drop.
  - Log file at `%LOCALAPPDATA%\ROROROblox\discord.log` (rolling, 1MB cap).

  Acceptance: Mocked HttpClient via `HttpMessageHandler`. Tests:
  - PostLaunchAsync OFF → no HTTP call.
  - PostLaunchAsync ON + invalid URL → no HTTP call (validated via regex first).
  - PostLaunchAsync ON + valid URL → POST with branded embed (verify color, footer, timestamp).
  - PostAccountThresholdAsync below 4 → no call.
  - Cross 4 from below → fires once.
  - Stay above 4 → does NOT re-fire.
  - 30-min reset + cross 4 again → fires.
  - HTTP 4xx → logs warning, does not throw.
  - HTTP 5xx → retries once, then logs.
  - HTTP 429 with `Retry-After: 5` → waits 5s, retries.

  Verify: `dotnet test --filter DiscordWebhookServiceTests`. Manual smoke deferred to CHECKPOINT 2 (after Settings UI lands so config can be set via UI). Commit: `feat(app): DiscordWebhookService — Layer 3 clan-channel webhook`.

- [ ] **9. Settings UI: Discord Integrations panel**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §5.7 Settings UI — Discord Integrations panel`
  What to build: New WPF page per spec §5.7 layout. Files:
  - `src/ROROROblox.App/Settings/DiscordIntegrationsView.xaml` — XAML matching spec §5.7 layout. WPF-UI styled. Cyan accent (`#17D4FA`), navy field (`#0F1F31`), Inter body, JetBrains Mono small labels.
  - `src/ROROROblox.App/Settings/DiscordIntegrationsViewModel.cs` — exposes `IsRichPresenceEnabled`, `WebhookUrl`, `IsWebhookUrlValid`, `OnLaunchEnabled`, `OnPrivateServerJoinEnabled`, `OnNAccountsActiveEnabled`, `IRelayCommand SaveCommand`. Validation regex: `^https://discord\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$`. Per-event toggles `IsEnabled`-bound to `IsWebhookUrlValid`.
  - Wire into existing `MainWindow` settings surface (or new sub-page) — match the project's existing settings pattern from v1.0.
  - On save: `IDiscordConfig.SaveAsync(snapshot, ct)` → file watcher fires `Changed` → all subscribers reload.
  - Brand assets: invoke `626labs-design` skill for any new visual elements (toggle styling, validation indicator). NO programmatic placeholders per CLAUDE.md hygiene rule + pattern (x).

  Acceptance: WPF designer preview renders without errors. Master toggle persists across app restart. Invalid URL → red border + helper text "Webhook URL must come from your clan's Discord channel admin." Valid URL → toggles enabled. Save fires `IDiscordConfig.Changed`; `DiscordRichPresenceService` reloads its enabled state without app restart.
  Verify: WPF designer eyes-on. Manual: open settings → toggle master → restart → verify toggle persists. Enter invalid URL → red border. Enter valid URL → toggles enabled. Save → check `discord-config.json` reflects new state. Commit: `feat(app): Settings · Discord Integrations panel`.

- [ ] **10. Brand asset pack via 626labs-design skill**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §9.2 Brand assets`, `§9.3 Webhook avatar`
  What to build: **Pause for human review even in autonomous mode** — pattern (x) from SnipSnap retro applies. Invoke `626labs-design` skill to produce 4 PNGs per spec §9.2 sizes:
  - `idle_large` (1024×1024) — RORORO logo on cyan field
  - `active_large` (1024×1024) — RORORO logo with magenta accent ring
  - `idle_small` (256×256) — 626 Labs mark
  - `active_small` (256×256) — 626 Labs mark with magenta active dot

  Plus webhook avatar:
  - `docs/assets/rororoblox-webhook-avatar.png` (256×256 minimum, served via GitHub Pages)

  **Este action (offline):** upload the four PNGs to Discord developer portal asset slots matching the keys above. **Build action:** commit webhook avatar to `docs/assets/`, write asset specs to `docs/themes/discord-asset-brief.md` for future reference, update `DiscordWebhookService` payload `avatar_url` to the live Pages URL: `https://estevanhernandez-stack-ed.github.io/ROROROblox/assets/rororoblox-webhook-avatar.png`.
  Acceptance: All four Discord assets visible in dev portal preview matching brand tokens. Webhook avatar PNG accessible via `curl -I` to the live URL returns 200 + `Content-Type: image/png`. Eyes-on review passes the "won't ship a broken-looking tile" bar. **No programmatic placeholders survive into this commit.**
  Verify: Manual eyes-on review against `~/.claude/skills/626labs-design/colors_and_type.css`. `curl -I https://...` confirms 200. Commit: `feat(brand): Discord asset pack via 626labs-design skill`.

- [ ] **CHECKPOINT 2** (after item 10 — full feature stack present, brand assets live)
  Manual review: walk every spec §8 manual smoke scenario. Real Win11 VM, real Discord client, real test webhook in a throwaway Discord server. Walk:
  1. Toggle rich-presence ON in Settings → presence appears with branded `idle_large` asset.
  2. Launch one account into Pet Sim 99 public game → presence updates with `active_large` + count "1 account active", NO Join button.
  3. Launch second account into a private server → Join button appears.
  4. Configure webhook URL in Settings → toggle "I start ROROROblox" ON → restart RORORO → verify embed posts to test channel with brand cyan border + "ROROROblox · Imagine Something Else." footer + branded webhook avatar.
  5. Toggle "I join a private server" ON → join private server → verify embed with clickable URL.
  6. Run 4+ accounts → verify "I have 4+ accounts running" embed fires once. Stay above 4 → no re-fire.
  7. Set malformed webhook URL → verify red border + per-event toggles disabled.
  8. Quit Discord client mid-session → RORORO continues launching accounts (no crash). Restart Discord → presence reconnects.
  9. Hand-edit `discord-config.json` to malformed JSON → restart RORORO → verify graceful recovery (defaults loaded, corrupt file preserved with timestamp).

  If anything is shaky, **fix before item 11**.

- [ ] **11. Documentation & Security Verification**
  Spec ref: `2026-05-06-discord-clan-coordination-design.md > §11 Decisions log`, `§12 Cart hand-off`; canonical `2026-05-03-rororoblox-design.md > §3 Stack` and `§11 Decisions log` (extension, NOT banner-correct)
  What to build:

  **Documentation:**
  - `README.md` — add a "Discord clan integration (v1.2)" section: what the three layers do, how to enable each, screenshot of presence in a Discord profile, screenshot of Settings · Discord Integrations panel, screenshot of a webhook embed in a clan channel. Link to clan-admin webhook setup in CONTRIBUTING.md.
  - Canonical spec extension (NOT banner-correct — this is an extension, not drift): append to `docs/superpowers/specs/2026-05-03-rororoblox-design.md` §3 (Stack — "DiscordRichPresence (Lachee) added in v1.2; see [v1.2 spec]") and §11 Decisions log (new row pointing at 2026-05-06 spec + summary "Discord clan-coordination layer adds rich presence + party Join + clan-channel webhook in v1.2.0").
  - `CONTRIBUTING.md` — new section "Discord integration setup for clan admins": how to create a Discord application (Discord developer portal), how to upload the asset pack, how to create a clan-channel webhook, how to share the webhook URL with clanmates.
  - `docs/index.md` GitHub Pages landing — update with v1.2.0 release section + screenshot.
  - Update CLAUDE.md "Common tasks" table: add row for "Configure Discord integration" pointing to new Settings panel.

  **Security verification:**
  - Secrets scan: `git ls-files | xargs grep -lE "discord\.com/api/webhooks/[0-9]+/[A-Za-z0-9_-]+"` should return zero hits (webhook URLs are user data, never committed). `git ls-files | xargs grep -lE "ROBLOSECURITY=|-----BEGIN|MIIB"` still zero. (These are existing v1.0 invariants; verify still hold.)
  - Verify `.gitignore` covers: `discord-config.json` (sanity — this lives in LocalAppData, but defend against a developer accidentally placing one in repo), `discord.log`, `discord-config.json.corrupt-*`. Existing entries (`accounts.dat`, `webview2-data/`, etc.) remain.
  - Dependency audit: `dotnet list package --vulnerable --include-transitive`. Categorize per pattern (ll) from wbp-azure: actionable vs documented-and-mitigated. Append findings to `docs/security-audit-2026-05-04.md` (existing v1.0 audit) under a new `## v1.2 — Discord clan-coordination` heading.
  - Input validation audit: webhook URL regex (`^https://discord\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$`) limits the surface. `DiscordWebhookService` log statements MUST redact full URL — log only scheme + host + `…redacted`. Grep for `_log.*WebhookUrl` to verify no full-URL log statements survived.
  - Local-path audit per pattern (kk): `git ls-files | xargs grep -lE "c:\\\\Users\\\\|C:/Users/"` returns zero hits except docs allowlist (`docs/security-audit-*.md`, `process-notes.md`, `PROVENANCE.txt`).

  Acceptance: README v1.2 section renders cleanly on github.com (manual eyes-on). Canonical spec extension shows in §3 + §11 without rewriting the body. Three security greps return clean. Vulnerability scan output is categorized and committed. All nine CHECKPOINT 2 manual smoke scenarios pass on a clean Win11 VM. GitHub Pages landing reflects v1.2.0.
  Verify: Run the three grep commands above. Walk the README install + Discord-config path on a clean Win11 VM. Walk the canonical spec to confirm §3 and §11 extensions are visible. Commit: `docs+security: v1.2 Discord clan-coordination final pass`.

---

## ✓/△ Embedded feedback

✓ **Sequencing:** Administrative gate (item 1, Discord app + AppId) before any code work; primitives (config + extractor) before consumers (presence service + lifecycle). Layer 2 outbound (item 5) splits from Layer 2 inbound (item 7) so each item touches one component. **Item 6 closes a real spec gap** — `IAccountLifecycle` was assumed to exist in canonical §5 but doesn't; the new abstraction lands as its own atomic item before DiscordPresenceLifecycle hooks it. CHECKPOINT 1 (after item 7) catches IPC + party-join issues before webhook + UI doubles the surface. CHECKPOINT 2 (after item 10) catches asset issues before docs.

✓ **Granularity:** 11 build items + 2 checkpoints + final doc/security = 13 entries. Each completable in one /build session. Items 4, 7, and 9 are the heaviest (~250+ LOC each). Most others are 100-150 LOC + tests.

✓ **Spec coverage:** Every numbered spec §5 component (5.1-5.8 in the new spec) maps to an item: §5.2 → item 4, §5.3+5.4 → item 8, §5.5 → item 3, §5.6 → item 2, §5.7 → item 9, §5.8 → items 1+6+7. Data flows §6.1-6.3 distribute across items 4-8. Error handling §7 distributes per-item. Distribution §9 hits item 10. Open items §10: AppId resolved at item 1, webhook avatar at item 10, threshold-config locked-in at fixed-4 (no checklist item needed).

△ **Risk point — administrative gates:** Item 1 (Discord app creation) and item 10 (Este uploads to dev portal) require offline action by Este. /build pauses if `Discord:ApplicationId` is unset; surfaces clearly + waits for user input. Item 10's brand-asset review is a deliberate human-eyes-on pause even in autonomous mode (pattern x).

△ **Scope watch — item 9 (Settings UI):** Largest single item by LOC. If it slips past 90 minutes, flag for split into 9a (master toggle + ViewModel persistence) and 9b (webhook URL + per-event toggles + validation + brand styling).

△ **Architectural gap surfaced — item 6:** The new spec assumed `IAccountLifecycle` from canonical §5; doesn't exist there. Item 6 closes the gap atomically. Once shipped, it's a clean abstraction available to v1.3+ features (telemetry-free analytics, run-time crash recovery, etc.).
