# Discord clan-coordination design (v1.2)

**Status:** active spec · Cart-ready · single-PR target
**Author:** The Architect (Cart-bound build, Ptolemy-discovered scope)
**Canonical parent:** [`2026-05-03-rororoblox-design.md`](2026-05-03-rororoblox-design.md) — this spec **extends** §3 (Stack), §5 (Components), §11 (Decisions). It does **not** override or banner-correct the canonical.
**Atlas entry:** `competitive`/`queued`, see [`.vibe-iterate/atlas.jsonl`](../../../.vibe-iterate/atlas.jsonl)
**Dashboard decision:** logged 2026-05-06 against project `PBWgg5mimZyAzAG3niAp`

---

## 1. Overview

RORORO becomes the clan's coordination layer on Discord. Three integrated surfaces in one feature:

1. **Layer 1 — Branded rich presence.** Per-user Discord status that reflects RORORO state (idle / N accounts active). Branded asset pack from the `626labs-design` skill.
2. **Layer 2 — Server-share party button.** When the user is in a Roblox private server, rich presence carries a Discord party "Join" button. One click → clanmate's RORORO opens Roblox to the same private server.
3. **Layer 3 — Clan-channel webhook.** Optional opt-in. Clan admin creates a Discord webhook in their channel; clanmates paste the URL into RORORO settings. Per-event toggles let RORORO post launch / server-join / N-accounts-active to the clan channel with a branded embed.

**Why all three in one PR:** Layer 2 is the actual unlock; Layer 1 is its surface; Layer 3 is the active-broadcast escalation. They compose. Shipping them together avoids a two-cycle feature reveal that would land confused for the clan ("wait, the buttons don't do anything yet?"). The diff is bigger than small-diff-preferred posture defaults to, but the strategic-relevance gain (clan-coordination layer in one ship) is the override case the posture allows.

## 2. Goals and non-goals

### Goals

- **Brand-spread to Pet Sim 99 clan via Discord** — the audience already lives there.
- **One-click squad-up** — replace DM-and-paste server links with the Discord party Join button.
- **Active broadcast for clan events** — webhook posts let clan admins coordinate trade events / hardcore raids.
- **Opt-in cascading** — three independent opt-in gates (master · webhook URL · per-event). Default OFF at every layer.
- **Zero Roblox-side contract surface** — no new compat-risk axis; Discord-side only.
- **Brand-consistent every surface** — assets through the `626labs-design` skill; no programmatic placeholders.

### Non-goals

- **Voice channel auto-join.** Clan-tier feature, not v1.2.
- **Reading clan-channel messages.** RORORO is a one-way poster, never a bot/listener.
- **Discord OAuth login.** RORORO doesn't manage Discord identity; clan handles it.
- **Auto-discover clan webhooks.** Hard rule — webhook URL is pasted manually.
- **Roblox-side modifications.** MaCro-wall (canonical §11): no FastFlags, no mods, no injection.
- **Telemetry on usage.** Hard rule — no phone-home.
- **Friend-/group-/avatar-tracking.** Out of scope (we're a launcher, not a Roblox social client).

## 3. Stack additions

Adds to canonical §3:

| Package | Version | Why |
|---|---|---|
| `DiscordRichPresence` (Lachee) | latest stable (≥1.2.x) | Discord IPC client. MIT, netstandard2.0, no native deps |
| `System.Net.Http.Json` | already in .NET 10 BCL | Webhook POST with `HttpClient.PostAsJsonAsync` |

No new capabilities required in `Package.appxmanifest`. Discord IPC = local named pipe (`\\.\pipe\discord-ipc-N`); already covered by `runFullTrust`. Webhook HTTPS doesn't require declaration.

## 4. Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  WPF App (existing)                                          │
│                                                              │
│  ┌────────────────────────┐    ┌─────────────────────────┐   │
│  │  RobloxLauncher        │───▶│  ServerShareExtractor   │   │
│  │  (existing, hooked)    │    │  (new, pure)            │   │
│  └────────────────────────┘    └────────────┬────────────┘   │
│                                             │                │
│                                             ▼                │
│  ┌────────────────────────┐    ┌─────────────────────────┐   │
│  │  AccountLifecycle      │───▶│  DiscordRichPresence    │   │
│  │  events (existing)     │    │  Service (new)          │───┼─┐
│  └────────────┬───────────┘    └─────────────────────────┘   │ │
│               │                              ▲               │ │
│               │                              │ OnJoin        │ │
│               │                              │               │ │
│               ▼                              │               │ │
│  ┌────────────────────────┐    ┌─────────────┴───────────┐   │ │
│  │  DiscordWebhook        │    │  IDiscordConfig         │   │ │
│  │  Service (new)         │◀───│  (new, per-machine)     │   │ │
│  └────────────┬───────────┘    └─────────────────────────┘   │ │
│               │                                              │ │
│               │                                              │ │
│  ┌────────────┴───────────┐    ┌─────────────────────────┐   │ │
│  │  Settings ·            │    │  discord-config.json    │   │ │
│  │  Discord Integrations  │◀──▶│  (new, NOT DPAPI)       │   │ │
│  └────────────────────────┘    └─────────────────────────┘   │ │
└──────────────────────────────────────────────────────────────┘ │
                                                                 │
   ┌───────────────────────────────────────────────────────────┐ │
   │  External                                                 │ │
   │                                                           │ │
   │  Discord IPC ◀────────── named pipe ────────────────────────┘
   │  (per-user)                                               │
   │                                                           │
   │  discord.com/api/webhooks/... ◀── HTTPS POST ─────────────┐
   │  (per-clan)                                               │ │
   └───────────────────────────────────────────────────────────┘ │
                                              ▲                  │
                                              └──────────────────┘
                                              (DiscordWebhookService)
```

Key seams:

- **DiscordRichPresenceService** is the only consumer of `DiscordRichPresence`/`DiscordRpcClient`. The rest of the app talks to it through `IDiscordPresence`.
- **DiscordWebhookService** is the only consumer of `HttpClient` for webhook POST. The rest of the app talks to it through `IDiscordWebhook`.
- **ServerShareExtractor** is a pure static helper — input is the launch URI, output is `string?`. Trivially testable.
- **IDiscordConfig** is read-mostly, written-rarely (only when settings UI saves). Singleton-cached, file-watcher reloaded.

## 5. Components & interfaces

All new code lives under `src/ROROROblox.App/Discord/` unless otherwise noted. Interfaces live in `src/ROROROblox.Core/` per canonical §5 boundary rules.

### 5.1 `IDiscordPresence` (Core)

```csharp
namespace ROROROblox.Core;

public interface IDiscordPresence : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
    Task UpdateStateAsync(RichPresenceState state, CancellationToken ct);
    Task SetPartyAsync(string serverShareUrl, CancellationToken ct);
    Task ClearPartyAsync(CancellationToken ct);

    event EventHandler<JoinRequestedEventArgs>? JoinRequested;
}

public sealed record RichPresenceState(
    PresenceMode Mode,
    int ActiveAccountCount,
    string? CurrentActivity);

public enum PresenceMode
{
    Idle,
    AccountsActive,
    InPrivateServer
}

public sealed class JoinRequestedEventArgs(string serverShareUrl) : EventArgs
{
    public string ServerShareUrl { get; } = serverShareUrl;
}
```

### 5.2 `DiscordRichPresenceService` (App)

- Implements `IDiscordPresence`. Singleton, registered in DI.
- Constructor: `(IDiscordConfig config, ILogger<DiscordRichPresenceService> log)`.
- `StartAsync` no-ops if `config.RichPresenceEnabled == false`. Otherwise creates `DiscordRpcClient` with the registered Discord application ID (see §9.2).
- Subscribes to `DiscordRpcClient.OnJoin` → raises `JoinRequested` event with the join secret as the share URL.
- Reconnect strategy: 5s exponential backoff up to 60s on connection failure; never give up. Stops on `DisposeAsync`.
- Asset keys hardcoded to match the brand asset pack (see §5.7).

### 5.3 `IDiscordWebhook` (Core)

```csharp
namespace ROROROblox.Core;

public interface IDiscordWebhook
{
    Task PostLaunchAsync(int accountCount, CancellationToken ct);
    Task PostServerJoinAsync(string serverShareUrl, CancellationToken ct);
    Task PostAccountThresholdAsync(int accountCount, CancellationToken ct);
}
```

### 5.4 `DiscordWebhookService` (App)

- Implements `IDiscordWebhook`. Singleton, registered in DI.
- Constructor: `(IDiscordConfig config, IHttpClientFactory httpFactory, ILogger<DiscordWebhookService> log)`.
- Each `Post*` method early-returns if the corresponding event toggle is OFF or webhook URL is unset/invalid.
- POSTs JSON to `config.WebhookUrl` with branded embed (see §6.3).
- Catches `HttpRequestException`, `TaskCanceledException`, `JsonException`. Logs at warning. **Never throws** to caller — webhook posting is best-effort, never blocks launch.
- Per-call timeout: 5 seconds. Discord webhooks rate-limit at 30/min globally; we won't hit that with single-user volume.

### 5.5 `ServerShareExtractor` (App, pure)

```csharp
namespace ROROROblox.App.Discord;

public static class ServerShareExtractor
{
    /// <returns>
    /// The private-server share URL extracted from a roblox-player:// launch URI,
    /// or null if the URI targets a public game or cannot be parsed.
    /// </returns>
    public static string? TryExtractPrivateServerUrl(string launchUri);
}
```

Algorithm:
1. Decode `roblox-player:` URI to plus-separated key/value list.
2. Look up `placelauncherurl` key.
3. URL-decode the value.
4. If decoded value contains `accessCode=` query param OR `privateServerLinkCode=` path segment → return as `string`.
5. Else → return `null`.

Golden test fixtures: `tests/Discord/Fixtures/launch-uri-*.txt` (private-server, public-game, malformed, missing-key).

### 5.6 `IDiscordConfig` + `DiscordConfigStore` (Core/App)

```csharp
namespace ROROROblox.Core;

public interface IDiscordConfig
{
    bool RichPresenceEnabled { get; }
    string? WebhookUrl { get; }
    DiscordWebhookEvents WebhookEvents { get; }

    Task SaveAsync(DiscordConfigSnapshot snapshot, CancellationToken ct);
    event EventHandler? Changed;
}

public sealed record DiscordConfigSnapshot(
    bool RichPresenceEnabled,
    string? WebhookUrl,
    DiscordWebhookEvents WebhookEvents);

public sealed record DiscordWebhookEvents(
    bool OnLaunch,
    bool OnPrivateServerJoin,
    bool OnNAccountsActive)
{
    public static DiscordWebhookEvents AllOff { get; } = new(false, false, false);
}
```

`DiscordConfigStore` (App):
- Path: `%LOCALAPPDATA%\ROROROblox\discord-config.json` (same dir as `accounts.dat`).
- Atomic write: write to `.tmp`, `File.Replace` to final.
- On load: missing file → return `(false, null, AllOff)`. Malformed JSON → log warning, return defaults, do NOT throw.
- File watcher fires `Changed` event on save (so services reactive without app restart).
- **NOT DPAPI-encrypted.** Webhook URL is a clan-shared resource, not a per-user secret. Opt-in is the consent surface; encryption would suggest sensitivity that isn't there.

### 5.7 Settings UI — Discord Integrations panel

New WPF page:

- `src/ROROROblox.App/Settings/DiscordIntegrationsView.xaml`
- `src/ROROROblox.App/Settings/DiscordIntegrationsViewModel.cs`

Layout (top to bottom):

```
┌─────────────────────────────────────────────────────┐
│ Discord                                             │
│                                                     │
│ ☐  Show RORORO in your Discord status              │
│                                                     │
│    When enabled, your Discord profile shows you     │
│    have ROROROblox running. No account names, no    │
│    Roblox usernames — only that the app is open     │
│    and how many accounts are active.                │
│                                                     │
│ ─────────────────────────────────────────────────── │
│                                                     │
│ Clan channel webhook                                │
│                                                     │
│ Webhook URL                                         │
│ [https://discord.com/api/webhooks/...           ]   │
│ Get this from your clan's Discord channel admin.    │
│                                                     │
│ Post to clan channel when:                          │
│ ☐  I start ROROROblox                               │
│ ☐  I join a private server                          │
│ ☐  I have 4+ accounts running at once               │
│                                                     │
│ All defaults OFF. Each clanmate opts in separately. │
└─────────────────────────────────────────────────────┘
```

Validation:
- Webhook URL regex: `^https://discord\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$`. On invalid + non-empty → red border + helper text.
- Per-event toggles disabled when webhook URL is empty or invalid.

### 5.8 DI wiring (App `Program.cs`)

Register on startup:

```csharp
services.AddSingleton<IDiscordConfig, DiscordConfigStore>();
services.AddSingleton<IDiscordPresence, DiscordRichPresenceService>();
services.AddSingleton<IDiscordWebhook, DiscordWebhookService>();
services.AddHostedService<DiscordPresenceLifecycle>();
```

`DiscordPresenceLifecycle` (new `IHostedService`):
- On `StartAsync`: calls `IDiscordPresence.StartAsync` (no-op if disabled).
- Subscribes to `IDiscordPresence.JoinRequested` → calls `IRobloxLauncher.LaunchAsync` with the share URL using the most recently used account (per existing launcher contract).
- Subscribes to `IAccountLifecycle.AccountStarted` / `AccountStopped` events (existing in canonical §5) → updates rich presence + posts webhook.
- On `StopAsync`: disposes `IDiscordPresence`.

## 6. Data flows

### 6.1 Rich-presence update flow (Layer 1)

1. App starts → `DiscordPresenceLifecycle.StartAsync` → `IDiscordPresence.StartAsync`.
2. If `config.RichPresenceEnabled == false` → no Discord client created. Idle.
3. Else: `DiscordRpcClient.Initialize()` → connects to local Discord IPC pipe.
4. User clicks "Launch" on account row → existing `RobloxLauncher.LaunchAsync` succeeds → fires `AccountStarted` event.
5. `DiscordPresenceLifecycle` increments active count → `IDiscordPresence.UpdateStateAsync(new(AccountsActive, N, "Multi-clienting"))`.
6. Discord IPC translates → user's Discord status reflects the change within ~1s.

### 6.2 Server-share + party-join flow (Layer 2)

**Outbound (this user shares):**

1. User clicks "Launch" → `RobloxLauncher.LaunchAsync` builds `roblox-player:` URI.
2. Before invoking `Process.Start`, the launcher passes the URI to `ServerShareExtractor.TryExtractPrivateServerUrl`.
3. If non-null: `IDiscordPresence.SetPartyAsync(shareUrl)` → Discord IPC populates party state with `partyId` (hash of share URL) and `joinSecret` (the URL itself, base64-encoded).
4. Discord renders "Join" button on this user's status for clanmates who can see it.

**Inbound (clanmate clicks Join):**

1. Clanmate's Discord client → fires `OnJoin(joinSecret)` event over IPC.
2. Clanmate's `DiscordRichPresenceService` → raises `JoinRequested` with decoded share URL.
3. `DiscordPresenceLifecycle` → calls `IRobloxLauncher.LaunchAsync(mostRecentAccount, shareUrl)`.
4. RORORO launches Roblox to the same private server.

**Edge cases:**
- Public game (no `accessCode`) → `ServerShareExtractor` returns null → `ClearPartyAsync` called → no Join button.
- Multiple accounts launched into different private servers → most-recently-launched wins (sets the current party).
- All accounts closed → `ClearPartyAsync` + `UpdateStateAsync(Idle, 0, null)`.

### 6.3 Webhook payload (Layer 3)

`PostLaunchAsync`:

```json
{
  "username": "ROROROblox",
  "avatar_url": "https://626labs.com/assets/rororoblox/webhook-avatar.png",
  "embeds": [{
    "title": "Started ROROROblox",
    "description": "{accountCount} accounts queued",
    "color": 1561082,
    "footer": { "text": "ROROROblox · Imagine Something Else." },
    "timestamp": "2026-05-06T22:53:33Z"
  }]
}
```

(`color: 1561082` = `0x17D4FA` = brand cyan as decimal.)

`PostServerJoinAsync` adds a `url` field on the embed pointing to the server share URL — Discord renders it as a clickable embed title.

`PostAccountThresholdAsync` posts only on the *crossing* of the threshold (4+ accounts) — not every increment past it. Resets after a 30-minute quiet window.

## 7. Error handling

| Failure | Response | Surface to user |
|---|---|---|
| Discord client not running | `StartAsync` skips connect; retries on 5s exponential backoff up to 60s | Status bar: "Discord not detected" (only if rich-presence toggle ON) |
| Discord IPC pipe disconnect mid-session | `DiscordRpcClient` auto-reconnects (library default) | Silent unless 60s+ persistent — then status bar message |
| Webhook URL invalid (regex fail) | Settings UI red border + helper text; per-event toggles disabled | Inline validation |
| Webhook POST 4xx (rate limit, bad URL post-validation) | Log warning; no retry on 4xx; do retry once on 5xx | Toast: "Couldn't post to clan channel — check the webhook URL" (max 1/hour) |
| Webhook POST timeout | Log warning; no retry | Silent (best-effort posture) |
| `discord-config.json` corrupt | Log warning; load defaults (all OFF); preserve corrupt file as `discord-config.json.corrupt-{ts}` for debug | Toast on next launch: "Discord settings reset — see logs" |
| `ServerShareExtractor` parse error | Return null; log debug | Silent (Layer 2 falls back to Layer 1 only) |
| Launch URI without `placelauncherurl` (public game) | Return null; clear party state | Silent (correct behavior) |

**Hard rule:** No Discord-related failure may break account launching. Every Discord call wraps in try/catch and logs; the launcher path is invariant.

## 8. Testing

### Unit tests (xUnit, mockable interfaces)

- `ServerShareExtractorTests` — golden fixtures for all four cases (private, public, malformed, missing key).
- `DiscordWebhookServiceTests` — mocked `HttpClient` via `IHttpClientFactory.CreateClient`. Verify embed shape, color, footer, threshold-crossing logic.
- `DiscordConfigStoreTests` — load/save round-trip, atomic-write fault injection (file-locked mid-write), missing-file defaults, malformed-JSON recovery.
- `DiscordRichPresenceServiceTests` — mocked `DiscordRpcClient`. Verify state transitions (Idle → AccountsActive → InPrivateServer → Idle), reconnect backoff, JoinRequested event raises with decoded URL.

### Integration tests

- `DiscordPresenceLifecycleTests` — wire all services with in-memory fakes; simulate full flow: app start → account launch → state update → server join → webhook post → account close → idle.

### Manual smoke (per canonical §8)

Required before merge:
1. Real Windows 11 VM, real Discord client running, real test webhook (fresh channel in throwaway server).
2. Toggle rich-presence ON → launch one account into Pet Sim 99 public game → verify presence shows "1 account active" with NO Join button.
3. Launch second account into a private server → verify presence updates + Join button appears.
4. From a second Windows machine with RORORO + a different Discord account, click Join → verify Roblox opens to the same private server.
5. Configure webhook URL + toggle "I start ROROROblox" ON → restart RORORO → verify embed posts to test channel with brand cyan + tagline.
6. Set malformed webhook URL → verify red border, no posts attempted.
7. Quit Discord client mid-session → verify RORORO continues launching accounts (no Discord-related crash) and reconnects when Discord restarts.

## 9. Distribution

### 9.1 Discord application registration

One-time setup (handled by Este pre-build):
- Create a Discord application at https://discord.com/developers/applications named "ROROROblox".
- Application ID injected at build time via `appsettings.json` (NOT hardcoded in source).
- Asset pack uploaded to Discord developer portal:
  - `idle_large` — RORORO logo on cyan field (1024×1024)
  - `active_large` — RORORO logo with magenta accent ring (1024×1024)
  - `idle_small` — 626 Labs mark (256×256)
  - `active_small` — 626 Labs mark with magenta active dot (256×256)

### 9.2 Brand assets

All four PNGs generated through the `626labs-design` skill, NOT programmatically. Pattern x from SnipSnap retro applies: Discord asset slots are public-facing surfaces. Reviewers + clanmates will both see them.

Asset specs in [`docs/themes/discord-asset-brief.md`](../themes/discord-asset-brief.md) (new file, written by Cart's checklist step).

### 9.3 Webhook avatar

Hosted on the GitHub Pages landing site (existing surface). Public URL stable; not bundled with the MSIX (cuts package size).

## 10. Open items

- [ ] Discord app ID assignment — Este creates the app and provides the ID before Cart's build step. (Cart should pause at the wiring step if `appsettings.Discord:ApplicationId` is unset.)
- [ ] Webhook avatar URL — needs the file uploaded to GitHub Pages first (`docs/assets/rororoblox-webhook-avatar.png`); Cart can stub with a placeholder URL and Este uploads later.
- [ ] Decision: should the "I have 4+ accounts running" threshold be configurable in v1.2, or fixed at 4? Default to **fixed at 4** unless Este overrides — keeps the settings UI from sprawling.

## 11. Decisions log

Logged 2026-05-06 against project `PBWgg5mimZyAzAG3niAp`. Key calls captured here for spec-time reference; full decision text lives on the dashboard.

- **Single-PR ship of all three layers** — over staged v1.2/v1.3 carve. Rationale: the layers compose; staged ship would land confused for the clan. Posture override accepted because strategic-relevance > small-diff for the clan-coordination unlock.
- **`discord-config.json` not DPAPI-encrypted** — webhook URL is a clan-shared resource, not a per-user secret. Encryption would suggest sensitivity that isn't there.
- **`Lachee.DiscordRichPresence` over rolling-our-own IPC** — saves ~600 LOC and a year of maintenance for ~negligible binary-size cost.
- **Fixed 4-account webhook threshold (v1.2)** — settings simplicity. Configurable threshold can land in v1.3+ if the clan asks.
- **No Roblox-side surface touch** — Layer 2's share URL is extracted from the launch URI we already build; no injection, no protocol observation, no Hyperion proximity. Stays inside the MaCro-wall (canonical §11).

## 12. Cart hand-off

Cart command sequence (run by user when ready):

1. `/vibe-cartographer:checklist` — Cart reads this spec + the canonical and emits a sequenced build plan to `docs/checklist-discord-clan-coordination.md` (separate from the v1.0 checklist; this is its own mini-cycle).
2. **User reviews the checklist.** Stop here if anything is wrong.
3. `/vibe-cartographer:build` — autonomous mode. Cart implements item by item. Stop conditions:
   - Any test failure
   - `appsettings.Discord:ApplicationId` unset (open item §10)
   - DPAPI envelope touch (out of scope, indicates drift)
   - Settings UI break in WPF preview
4. Manual smoke per §8 before merge.
5. Atlas entry: `outcome` flips `queued` → `shipped` and `pr` populates.
6. Decision-log entry on dashboard with PR URL appended.

## Appendix A — Out-of-scope alternatives that were considered

These are gaps from `/vibe-iterate:competitive` that were declined for v1.2; preserved here for /reflect-time review:

- **Update channels (live / beta).** Genuinely valuable for compat-risk insurance but a different shape of work. Queued separately as v1.4 candidate.
- **Custom bootscreen / themes.** Bloxstrap differentiates by theming Roblox; we differentiate by branding our own surfaces. Different game.
- **FastFlag editor / mod manager / Hyperion bypassing.** All three cross the MaCro-wall. Decline is permanent, not deferred.
- **Telemetry opt-in.** Hard rule per guide §Hard rules.
- **Roblox version pinning.** Compat-risk centroid; we wrap the launcher, we're not the version oracle.
- **RAM-style social features (trade/friend/avatar tracking).** Out of scope (we're a launcher, not a Roblox social client).

---

**End of spec.** Cart-ready. ~1,150 LOC estimate. Hand-off command sequence in §12.
