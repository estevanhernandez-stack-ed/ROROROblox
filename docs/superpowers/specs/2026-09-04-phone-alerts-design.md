# RoRoRo — phone alerts: Pushover + ntfy senders on the existing alert seam

---
**Date:** 2026-09-04
**Status:** Approved design — implementation follows in the same session
**Author:** The Architect + Este
**Scope:** The two existing alert kinds (alt dropped out, memory watchdog warning) reach the user's phone through a push provider the user configures once: Pushover (headline — the clan's incumbent app) or ntfy (the free option). The Discord webhook alerts are untouched — Este's ruling (2026-09-04) is that they stay their own separate piece, and the ruling targets the webhook *transport*, not the routing machinery.
**Origin:** Este, 2026-09-04: "we want to work on mobile notifications … even if they have to set something up on their end." Research workflow `wf_ca8b39c5-c0d` (five reports + synthesis) ran the same day.
---

## §0 What was measured and researched before designing

1. **The alert seam is complete and battle-tested.** `AlertsRaised` (wired fire-and-forget at `App/App.xaml.cs:1621-1623`) feeds `AlertDispatcher.DispatchAsync`, which routes through `AlertRouter.Route` — per-account mute, per-(account, kind) 5-minute cooldown, coalescing — then switches on `AlertDestination` (`Local` toast / `Mine` webhook / `Clan` webhook). Triggers originate from the presence path (`MainViewModel.cs:3068`, both-signals drop detection with expected-close suppression — the no-false-pages logic) and the memory watchdog (`:3378`). A destination configured without its endpoint falls back to `Local` (`AlertRouter.cs:68-69`); a dead endpoint is terminal for the session and surfaced in Settings (`AlertDispatcher.MineWebhookRejected` pattern).
2. **`WebhookPayload` is a security boundary as a type** — Title + Body only, structurally unable to carry a private-server link; `DisplayName` is streamer-masked for every destination except Clan. Phone destinations get masked names by default for free.
3. **The sender pattern is established.** `DiscordWebhookSender`: exactly one ctor `(HttpClient, ILogger<T>)` (the non-generic `ILogger` resolve-time crash is fenced by `TypedHttpClientRegistrationTests`), never logs the URL (bearer credential), status-code-driven result enum.
4. **Community research** (workflow, 2026-09-04): the Roblox macro ecosystem's only established phone channel is Discord webhook + ping; dedicated push apps have near-zero footprint — whatever ships here is first-in-category. **Este then identified the clan's actual incumbent: Pushover** — members already have the app, accounts, and the habit.
5. **Pushover:** native APNs/FCM on both platforms; per-user key + per-application token; messages deleted after verified delivery (21-day cap); priority semantics; $4.99 one-time per platform. Pushover's guidance for redistributed apps is per-user application registration, not a shared embedded token.
6. **ntfy:** publish is one HTTP POST to `{server}/{topic}`; the topic is the entire credential (read AND write); free tier ~250 msgs/day per machine; Android delivery good, **iOS honestly shaky** (maintainer's own words; the 2026 revival was still "evaluating" as of 2026-09-03).
7. **Integration-shape pricing** (workflow report, file:line-anchored): a first-party plugin either rides the raw process-exit stream — recreating exactly the false pages the presence path suppresses — or needs a new `SubscribeAlerts` RPC plus an entire second product with its own release train, while the Store edition ships no catalog. Core is decisively cheaper and safer.

## §1 The decision

**One new destination on the existing router; two provider senders; DPAPI-stored provider config; per-user credentials only.**

1. **`AlertDestination.Phone`** joins the enum. `AlertRouter.Resolve` gains the same blank-endpoint fallback arm the webhooks have (no provider configured → `Local`); `AlertDispatcher` gains the `Phone` case, dispatching to the configured provider's sender with the same rejected-endpoint session flag and Settings surfacing the webhooks get.
2. **Two senders, cloning the `DiscordWebhookSender` contract** (one ctor `(HttpClient, ILogger<T>)`, `RORORO/<version>` UA, 10 s timeout, credentials never logged):
   - `PushoverSender` — POST `https://api.pushover.net/1/messages.json` (token, user, title, message, priority). Priority mapping: `AccountDroppedOut` → 1 (bypasses quiet hours — the actionable page), `MemoryWarning` → 0. A 4xx that names an invalid token/user is terminal for the session, like a webhook 404.
   - `NtfySender` — POST `{server}/{topic}` with the title header; server defaults to `https://ntfy.sh`, user-overridable (this absorbs the self-host/Gotify use case). Topics are generated **cryptographically random** on first setup — the topic is a bearer credential for both reading and spoofing. 429 maps to the rate-limited result; the router's cooldown already keeps normal volume far under the free-tier cap.
3. **Config:** a `PhoneNotifyConfig` record (provider; Pushover user key + app token; ntfy topic + server URL) in a DPAPI-encrypted `notify.dat`, `DiscordConfigStore`-style — push credentials are bearer credentials and never sit in plaintext. This keeps `SettingsBlob`, `SettingsReachabilityTests`, and the four `IAppSettings` fakes untouched.
4. **Settings (Alerts page):** a Phone section — provider picker, the provider's one or two paste fields with a `WebhookUrlValidator`-style shape check, a test-notification button (`WebhookProbe` pattern), and `AlertStatusLine` extended so "configured but routed nowhere / rejected endpoint" stays diagnosable. Setup copy steers iPhone members to Pushover; the ntfy option carries an honest Android-first, best-effort-iOS line.
5. **Pushover credential model: per-user application registration** — the wizard walks the member through creating their own application token (one web form) plus their user key. No shared 626 Labs token ships in the binary: a shipped bearer token is extractable, pools the whole clan onto one quota, and contradicts both Pushover's redistribution guidance and this repo's credential posture.
6. **Store consequences, priced in:** the next reviewer letter retires the standing "network behaviour is unchanged" line and discloses `api.pushover.net` + `ntfy.sh` (+ user-set custom server) in the `discord-disclosure.md` mold that has already passed certification — off by default, user-pasted endpoints, DPAPI storage, no telemetry, no manifest/capability change. `docs/PRIVACY.md`'s network table gains the two rows in the same PR.

## §2 Rejected alternatives, with reasons

- **Building on the Discord webhook (@mention ping):** excluded by Este's ruling — the webhook alerts stay their own piece. Recorded honestly: research shows it is the community's only incumbent channel, and relaxing this constraint is the biggest possible ranking-changer.
- **First-party plugin (either variant):** the zero-RPC variant bypasses presence confirmation and expected-close suppression — false 3 a.m. pages by construction; the new-RPC variant costs the full CLAUDE.md new-RPC checklist plus a second product and release train, and Store members have no catalog to install it from.
- **Telegram bot:** best free-tier delivery, but a phone-number-gated account plus the BotFather ritual for an audience that already owns Pushover. Revisit only if the incumbent fact changes.
- **Gotify / Pushbullet:** self-host homework for non-developers; no iOS app and a full-account token, respectively.
- **Shared embedded Pushover application token:** friendlier wizard, but an extractable bearer credential in a shipped binary with a pooled quota.
- **Server-side presence watcher (RoFinder pattern):** the only architecture that survives a dead PC — every option here is fired by the machine itself, so a bluescreen pages nobody. Deliberately unscoped; **this is the known gap in what ships**, and it pairs naturally with presence-as-truth if a future cycle wants it (with its own privacy reckoning).

## §3 What deliberately does not change

- The Discord webhook alerts: config, store, senders, destinations, Settings rows — untouched.
- The alert kinds: the shipped two. New kinds are their own cycle.
- Router semantics: mute, cooldown, coalescing, fallback-to-Local, streamer masking.
- The plugin surface: no new RPC, no contract bump.
- **Accepted rollback hazard (review 2026-09-04):** `AlertDestination.Phone` serializes as the numeric value 4 into `discord.dat`, which older binaries share. A pre-phone binary routes value 4 through its `_ => wanted` arm, matches no dispatch case, and drops the alert silently — no phone, no toast, cooldown stamped. No cheap fix exists in already-shipped binaries; the release notes carry a "set your alert routing again if you roll back" line, and the shared-data-folder coexistence note in CLAUDE.md already warns that a Store build and a dev build share one config.

## §4 Test plan

1. Unit: router `Phone` cases (configured/unconfigured/rejected fallbacks), dispatcher `Phone` case with both providers, sender result mapping over canned responses, credential-shape validators, payload → provider field mapping (priority table).
2. Fences: `TypedHttpClientRegistrationTests` rows for both senders; `AccessibleNamingFenceTests` on the new Settings controls; no new modal, no new window.
3. Full suite x64 + arm64 via CI.
4. Live smoke (needs Este): a real Pushover page and a real ntfy buzz to his phone from a dev build — enable, trigger a synthetic alert, phone buzzes, test button works, bad-token path surfaces in Settings. Same gate discipline as the packaged-activation smoke.

   **Delivery legs executed 2026-09-05 — PASS on both providers.** ntfy first ("ntfy worked"), then Pushover ("pushover works"), both via Test my phone on Este's devices from the dev build at `main`. The real alt-drop page ran 2026-09-05 on the fan-out build — **PASS** ("Live notification test worked"): launched alt, hard-killed client, phone paged. **The §4 smoke is complete.** The smoke's UX feedback (Discord test buttons reading as phone controls, the ntfy install step buried, single-destination routing too narrow) shipped as the alert fan-out change the same day.
5. The reviewer-letter fragment and PRIVACY rows ride the release PR, not a later one — a PR that adds an outbound host updates the disclosure surfaces in the same change.
