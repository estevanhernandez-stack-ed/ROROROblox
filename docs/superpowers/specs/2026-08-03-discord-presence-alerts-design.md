# Discord rich presence + per-account alerts

**Date:** 2026-08-03
**Status:** Approved design, unbuilt.
**Supersedes:** [`2026-05-06-discord-clan-coordination-design.md`](2026-05-06-discord-clan-coordination-design.md)
(on branch `feat/discord-clan-coordination`, unmerged) — see §3 for what changes and why.
**Depends on:** v1.14 server-instance targeting
([`2026-08-02-server-instance-targeting-design.md`](2026-08-02-server-instance-targeting-design.md)) — shipped.

## 1. Why now

The May design was built and abandoned at **zero adopters**. RoRoRo has **806**. That changes three
things:

- **The feature is worth its cost.** A status line nobody sees is a hobby; a status line 806 people
  see is distribution — every presence is the product introducing itself to a Discord full of
  Roblox players.
- **Mistakes scale.** A default that leaks one private-server link now leaks 806. A confusing
  setup field is not one puzzled user, it is a support queue.
- **The hard part got easier.** May's design strained to describe *which server* a friend should
  join, without a primitive for it. `ServerInstance(PlaceId, JobId)` shipped 2026-08-02. A Discord
  Join secret is that pair, and nothing else.

Two other things arrived since May: streamer mode (v1.11), which this design has to honor on the
way out the door, and the memory watchdog (v1.12), which supplies one of the two alert triggers
for free.

## 2. Goals and non-goals

**Goals**

1. Discord shows what the **roster** is doing — the fleet, not one account. This is the thing only
   RoRoRo can say.
2. A clan member can **Join** from Discord and land in the same server, public or private.
3. Two alerts — **account dropped out** and **memory warning** — reach the user when they are not
   at the PC.
4. Presence costs the user **no setup**. Alerts cost setup only if they want them on their phone.

**Non-goals**

- No bot, no hosted service, no backend of any kind. Every install talks to its own Discord client
  and its own webhook. Nothing is shared, so nothing scales.
- No telemetry. Unchanged posture, and it means we are blind to where users stall (§5.4).
- No new alert triggers beyond the two above. Session-expired and landed-elsewhere were considered
  and cut — see §11.
- Nothing touching the Roblox client. The MaCro wall is unmoved: presence reads state RoRoRo
  already has.

## 3. What this supersedes from the May design

The May spec is sound on stack and structure, and its late commits solved genuinely hard problems
(§10). Three of its decisions do not survive contact with 806 users.

**3.1 `discord-config.json` gets DPAPI. May said no.** Its rationale: *"webhook URL is a
clan-shared resource, not a per-user secret. Encryption would suggest sensitivity that isn't
there."* That was defensible when there was one webhook pointing at one clan channel. It is wrong
now for two reasons. First, this design has **two** destinations and one of them is a private
channel only the user reads — that is per-user by construction. Second, a webhook URL *is* a
credential: anyone holding it posts to that channel as you, with no further authentication. The
file today sits in plaintext next to a DPAPI-encrypted account vault, which is an inconsistency
with no argument behind it. Webhook URLs move into DPAPI, same envelope as `accounts.dat`.

**3.2 The trigger set changes.** May had `onLaunch`, `onPrivateServerJoin`, `onNAccountsActive`
(fixed threshold of 4). This design ships `AccountDroppedOut` and `MemoryWarning` — the two that
matter when nobody is watching. Launch and squad-formed events are clan-flavored noise at eight
accounts; the fixed-4 threshold decision is moot because the trigger is gone.

**3.3 `AccountLifecycleTracker` is not harvested.** May wrote its own lifecycle tracker because
nothing else knew when an account went down. `RobloxProcessTracker` + `PresenceService` (v1.5,
hardened through v1.14) now own that, with the augment rule that survives a lost pid. Reusing the
May tracker would fork a signal that took four releases to get right.

## 4. Architecture

Two layers, split on testability: everything with a hard question in it is pure; everything that
touches the world is thin enough to read in one screen.

### `ROROROblox.Core/Discord/` — no UI, no Windows, no network

| Unit | Responsibility |
|---|---|
| `DiscordConfigStore` | Enabled flags, the two webhook destinations, per-trigger routing. **DPAPI-encrypted** (§3.1). |
| `PresencePayloadBuilder` | Pure. Roster snapshot in → Discord presence fields out (details / state / party / timestamps / secret). |
| `AlertRouter` | Pure. `(trigger, config, recent history)` → `Destination` (`Mine` \| `Clan` \| `Local` \| `None`), including coalescing and cooldown. |
| `WebhookPayload` | The type `DiscordWebhookSender` accepts. **Cannot represent a private-server link** — see §7.2. |

`PresencePayloadBuilder` being pure is what makes the interesting question a unit test: *what does
presence say when three of eight accounts are in one server and five are elsewhere?* That is a
table of cases, not a thing to check by squinting at Discord.

### `ROROROblox.App/Discord/` — the world-facing shell

| Unit | Responsibility |
|---|---|
| `DiscordPresenceService` | Owns the Lachee IPC connection. Subscribes to `MemoryWatchdog`, `RobloxProcessTracker`, `PresenceService`. Pushes what the builder computes. Reconnects when Discord restarts; **a missing Discord client is a no-op, never a dialog**. |
| `DiscordWebhookSender` | HTTPS POST + retry with backoff. Accepts only `WebhookPayload`. |
| `DiscordJoinListener` | Inbound: URI-scheme dispatch → single-instance relay → `ServerInstanceTargeting.Upgrade` → normal launch path. |

### Stack

`Lachee.DiscordRichPresence` (MIT, netstandard2.0, no native deps) — kept from May. Rolling our own
IPC buys nothing.

**No new package capabilities.** Discord IPC is a local named pipe (`\\.\pipe\discord-ipc-N`),
covered by the existing `runFullTrust`; outbound HTTPS needs no declaration. The Store consequence
is a disclosure, not a manifest change (§9).

## 5. The user-facing design

### 5.1 Presence is roster-level

One Discord presence per person; eight accounts. Presence describes the fleet:

```
Playing Roblox
Pet Simulator 99!
8 accounts in one server · 2h14m
[ Join ]
```

State lines by situation:

| Roster state | State line |
|---|---|
| All running accounts in one server | `8 accounts in one server` |
| Split across servers | `8 accounts · 3 in this server` |
| One account | `1 account` |
| In game, server unknown (privacy/pre-poll) | `8 accounts` (no Join) |
| Nothing running | Presence cleared entirely |

Elapsed time comes from the earliest still-running account's in-game stamp — the run's age, not the
newest launch.

### 5.2 Join, including private servers

Join carries `ServerInstance(PlaceId, JobId)` for public servers and the private-server code for
private ones. The joining RoRoRo resolves it through the same `LaunchTarget` path a paste would
take.

**Private-server Joins warn the joiner:** *"This is a private server — you may be denied entry if
you're not on its list."* Roblox does the permission check server-side, so a clan member without
access gets bounced; saying so up front beats a mystery failure.

**Recorded, because it was raised and overruled:** handing a private-server link to Discord means
the credential travels to Discord's servers and into any client that can see the Join button. That
link is the whole credential — there is no second factor — and it is the same link streamer mode
hides behind a reveal-only pill inside RoRoRo. Este's call is that clan reach is worth it, with the
joiner warned. This design honors that for **presence**, and does not extend it to **webhooks**
(§7.2), because a channel post is a broadcast to everyone who ever reads that channel, including
people who join it later.

### 5.3 Alerts: two triggers, two controls

Triggers: **account dropped out** (client died or left unexpectedly) and **memory warning** (the
watchdog crossed that account's threshold).

Routing is **per-trigger**; muting is **per-account**. Two controls, no matrix — eight accounts ×
two triggers × three destinations would be 48 switches nobody finishes setting.

```
Settings → Discord
  Dropped out     → [ My channel   ▾ ]
  Memory warning  → [ Local only   ▾ ]

Row:  CaptainNoodle   [●] alerts
      BaronBloxwell   [○] muted
```

`AlertRouter` coalesces: eight accounts crossing a memory threshold in one watchdog sweep is one
message naming eight accounts, not eight messages. Per-account cooldown prevents a flapping client
from paging someone every thirty seconds.

### 5.4 Setup that survives users

**The tiering is the accessibility design.** Presence needs *no account, no server, no webhook, no
permissions* — Discord's client is already running and RoRoRo talks to it over a local pipe. One
toggle. Alerts have two tiers: **local** (zero setup, uses the existing tray notification) and
**phone** (needs a webhook).

This matters more than it looks. "Server Settings → Integrations → Webhooks" assumes **the user
owns a Discord server**. Many clan members have only ever *joined* one. There is no webhook to copy
if there is no server to put it in, and no amount of paste-validation fixes that. So the phone tier
opens with *"Do you have your own Discord server?"* → **No** → a three-click guide (`+` → Create My
Own → Skip) → then the webhook steps. Nobody hits that wall unless they walk toward it, and they
get real value without ever going near it.

**The paste field names what it actually got:**

| Pasted | Response |
|---|---|
| `discord.gg/…` | "That's a server invite. You need a webhook — Server Settings → Integrations → Webhooks → New." |
| `discord.com/channels/…` | "That's a link to the channel, not a webhook. Same channel, different button." |
| A bot token | "That's a bot token — don't share that anywhere. A webhook URL starts with `discord.com/api/webhooks/`." |
| A valid webhook | `GET` it and show **"Posts to #rororo-alerts in Este's Server"** — so a clan webhook pasted into the personal slot is caught before it matters. |

Surrounding text is tolerated: the field extracts the URL from whatever was pasted around it.

**A Send test button** posts a real message through the real path. Not validation theatre — "it
says connected but nothing arrives" gets found during setup instead of at 3am.

**A status line, always visible, in plain words:** *"Connected to Discord as @este"* · *"Discord
isn't running — presence starts when it does"* · *"No alerts routed anywhere yet."* That last one
is the silliest failure and the most likely: everything enabled, nothing routed, no feedback.

**We are blind here by design.** No telemetry means no funnel data on where setup stalls. Accepted
— the privacy posture is worth more than the instrumentation.

### 5.5 Defaults

Everything off. No presence, no webhooks, nothing outbound until someone turns it on. For 806
people the safe default is silence.

## 6. Data flows

**Presence update.** Any of (process attached/exited, presence poll landed, memory pressure
crossed) → `DiscordPresenceService` builds a roster snapshot → `PresencePayloadBuilder` → IPC push.
Throttled to at most one push every 15 s; Discord ignores faster updates anyway.

**Join, inbound.** Discord Join click → `roblox-rororo:` URI → already-running instance via the
single-instance relay → parse to `LaunchTarget` → private-server warning if applicable → existing
launch path. A cold start queues the join until the roster has loaded.

**Alert.** Trigger fires → `AlertRouter` (per-account mute → per-trigger destination → coalesce →
cooldown) → `Local` raises the existing tray notification; `Mine`/`Clan` build a `WebhookPayload`
and POST.

## 7. Privacy and security

### 7.1 Streamer mode is honored outbound

Streamer mode hides names, avatars, and private-server links **inside** RoRoRo. If presence ignored
it, a streamer would flip it on, feel covered, and broadcast their real alt names to everyone
watching their Discord. Presence and webhooks render through the same `IStreamerIdentityProvider`
the UI uses: masked names in, masked names out. Same promise, honored on the way out the door.

### 7.2 Webhooks never carry a private-server link

Not "we don't send it" — `WebhookPayload` **cannot represent one**. Presence Join secrets go to
people who can see your Join button; a channel post goes to everyone who ever reads that channel,
including people who join it next year. Different mechanism, different blast radius, different
rule.

### 7.3 Webhook URLs are DPAPI-encrypted

Per §3.1. A webhook URL is a bearer credential and belongs in the same envelope as the account
vault.

### 7.4 What leaves the machine

Presence: game name, account count, elapsed time, masked-or-real display names per streamer mode,
and a join secret. To the user's own Discord client over a local pipe, then to Discord.
Webhooks: the alert text, to the URL the user configured. Nothing else, nowhere else, ever.

## 8. Error handling

| Case | Behavior |
|---|---|
| Discord not installed / not running | Feature silently idle. Status line says so. Never a dialog. |
| Discord restarts mid-session | Reconnect with backoff; presence restored from current roster. |
| Webhook returns 404 (deleted) | Disable that destination, surface it in Settings with a re-add prompt. Never retry a 404 forever. |
| Webhook 429 (rate limited) | Honor `Retry-After`; coalesce anything queued behind it. |
| Webhook 5xx / network | Retry with backoff, cap the attempts, drop with a log line. An alert is not worth a queue that outlives the reason for it. |
| Join arrives for an unknown place | Fall back to opening the game; never strand the joiner at a dead URI. |
| Join arrives while no accounts are configured | Normal empty-state, not an error. |

The standing rule: **no Discord failure may affect a Roblox launch.** Presence is a passenger.

## 9. Store and reviewer implications

No new package capabilities (§4). What is new and must be disclosed in the next reviewer letter:

- A third-party library (`Lachee.DiscordRichPresence`, MIT) in the signed package.
- A local named-pipe IPC channel to the user's own Discord client.
- A new outbound host (`discord.com`) for webhooks, user-configured, off by default.
- A URI-scheme registration for inbound joins.

Framing, consistent with prior letters: this is the same category as the existing plugin host —
local IPC the user opts into — plus an outbound call to an address the user typed in themselves.

## 10. Harvest list from the May branch

Read `origin/feat/discord-clan-coordination` for the mechanics; re-implement against current
primitives. Do **not** merge it — it is three months stale on a codebase that moved v1.5 → v1.14.

**Take (the expensive parts):**

- `ServerShareExtractor` — launch-URI → shareable server, with its fixture set covering public,
  private-linkCode, private-accessCode, launcher-linkCode, malformed, and missing-key. The fixtures
  are worth more than the code.
- `LacheeDiscordRpcClientAdapter` + `IDiscordRpcClient` — the seam that makes IPC mockable.
- The four hard-won fixes in the late commits, each of which cost a debugging session:
  `Subscribe(EventType.Join)` (the join command was never delivered without it), `%1` in the URI
  scheme registry entry (inbound args were being dropped), the compact JoinSecret format (Lachee
  caps secrets at 128 chars), and URI-scheme registration being a precondition for Discord
  accepting `SetPresence` with secrets at all.
- `docs/themes/discord-asset-brief.md` — the Discord asset specs.

**Leave:**

- `AccountLifecycleTracker` (§3.3).
- The webhook trigger set and the fixed-4 threshold (§3.2).
- The plaintext config store (§3.1).
- Its presence payload shape — it was per-account; this design is roster-level.

**Already done, out of band:** the Discord application ("ROROROblox") is registered. The
application ID is injected via `appsettings.json`, never hardcoded. Asset upload status to the
developer portal needs confirming before the presence art shows correctly.

## 11. Out of scope

- **Session-expired and landed-elsewhere alerts.** Considered, cut. Both are already visible on the
  row when you are at the machine, and neither is worth a phone buzz.
- **Ask-to-Join approval gating.** Proposed as a middle path for private servers; overruled in
  favor of open Join plus a joiner warning (§5.2). Revisit if a link leaks in practice.
- **Per-account presence.** Discord shows one presence per person; roster-level is the decision.
- **Configurable alert thresholds.** The memory trigger reuses the watchdog's existing thresholds,
  which already derive from installed RAM.
- **A clan-wide dashboard / bot.** That is a service, with hosting, moderation, and an abuse
  surface. This design has no backend and should keep it that way.
