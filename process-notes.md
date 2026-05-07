# RORORO — Process Notes

Cart cycle started 2026-05-03. **Spec-first cycle (pattern mm)** — substantive design done in pre-onboard brainstorming, captured in `docs/superpowers/specs/2026-05-03-RORORO-design.md`. /scope, /spec, /prd, /builder-profile compressed to pointer-stubs + compressed PRD because the substantive thinking happened upstream.

## /checklist

**Cycle type:** Spec-first cycle (mm). Inherited a 345-line technical spec; this command's job is translation, not deepening.

**Build mode:** autonomous-with-verification. Architect persona + builder mode + brisk pacing + fully-autonomous flag in unified profile. Verification checkpoints happen at items 1 (spike gate — HARD halt if it fails), 4 (after primitives — mutex + store), 7 (after capture path — primitives + launcher + capture), and 11 (before docs/security). Items 1 and 12 are explicit human-review gates regardless of mode.

**Comprehension checks:** off (autonomous mode skips this question per skill spec).

**Git cadence:** commit after each checklist item. Item 1 (spike) lives in `spike/auth-ticket/` and is gitignored — it's a verification gate, not deliverable code. Real first commit of source is item 2.

**Sequencing rationale:**

- **Item 1 spike first** because spec §10 says it's mandatory before implementation. If the auth-ticket flow has shifted since the spec was written, design needs adapting before committing to the architecture.
- **Items 2-4 build the load-bearing primitives** with no UI dependencies: AppLifecycle (composition root) → MutexHolder → AccountStore. These get unit-tested in isolation.
- **Items 5-7 build the consumers of those primitives**: IRobloxApi → RobloxLauncher → CookieCapture. Each pulls from the layer below.
- **Items 8-9 build the UI surface**: TrayService → MainWindow + ViewModel + error modals. UI last among build items so it has real seams to bind against, not stubs.
- **Item 10 (auto-update + remote config)** comes after the app is functional because Velopack needs a real release pipeline and remote config needs the app's startup flow. Both fetch from the same GitHub-hosted artifact.
- **Item 11 (MSIX + Store)** deliberately last in the build because packaging is the slow feedback loop — we don't want it blocking iteration on items 2-10.
- **Item 12 (docs + security)** is the universal final item per Cart convention.

**Spec coverage:** Every numbered spec section (1-11) maps to a checklist item. §7's six error buckets are distributed into the items that emit them (4, 6, 7, 9, 10) per the data-flow architecture, not bundled into a separate item. This keeps each item atomic and testable.

**Explicit cuts:** v1.2 features (per-cookie encryption, per-account WebView2 profiles, auto-tile, live running indicator) are NOT in the checklist. These are tracked in spec §10 deferred section and PRD P1.

**Item count:** 12 items (within the 8-12 target band; spike is item 1 to make the gate visible — without it the count would be 11).

**Deepening rounds:** zero (per builder profile habit across 9+ Cart cycles when spec is clean — this cycle qualifies, the spec is 345 lines of locked design).

**Risk callouts logged for /build:**

- Item 1 is a HARD halt-and-update-spec gate. Do not skip even if it looks like setup.
- Item 9 (MainWindow + 4 modals) is the largest item; flag if it slips past 90 minutes.
- Item 11 must use the design skill (or careful manual asset work) to produce real Store icons — programmatic placeholders are disqualifying per pattern (x) from SnipSnap retro.
- WebView2 runtime is bundled into the Store MSIX (per spec §7.3); sideload MSIX assumes WebView2 is preinstalled on Win11 (it is, evergreen).

**Session/friction loggers:** Cart's plugin data dir at `~/.claude/plugins/data/vibe-cartographer/` does not yet exist on this machine. Skipped session-logger.start + friction-logger.log calls; no jsonl entries written. /evolve should pick this up as a tooling gap (the loggers should auto-create their data dir, or the plugin should fail loud rather than silent).

## Spike outcome 2026-05-03

Item 1 (auth-ticket spike) ran cleanly on the second pass. First pass caught a real Roblox-side contract evolution: the auth-ticket POSTs now return **415 Unsupported Media Type** without an explicit `Content-Type: application/json` header, even on empty-body POSTs. v1.0 of the canonical spec didn't capture this — it predated the contract change.

**Outcome shape:**

- First pass (no `Content-Type`): step 1 returned 403 + `X-CSRF-TOKEN` (length 12) as documented. Step 2 returned **415 Unsupported Media Type** (`response headers: Date, Server, Cache-Control, Transfer-Encoding, Strict-Transport-Security, x-terms-message, X-Frame-Options, roblox-machine-id, x-roblox-region, x-roblox-edge, report-to, nel`).
- Surgical fix: set `request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")` on both POSTs.
- Second pass: step 1 returned 403 + token, step 2 returned **200 OK + `RBX-Authentication-Ticket` (length 448)**. CSRF dance + ticket exchange both PASS.

**Validate-only mode passed.** The full launch path (`Process.Start` of the constructed `roblox-player:` URI → user-eyes verification of "is the test account signed in") is the next step before declaring item 1 complete.

**Decision shape:** pre-build drift caught at the gate, NOT post-build divergence. Surgical inline edits to the canonical spec are the right shape (banner-correct pattern v applies after items have been built; pre-build catches go inline + decisions log entry). Updated:

- Spec §5.7 (`IRobloxApi`) — added the `Content-Type: application/json` requirement
- Spec §6.2 step 1 (Launch As data flow) — added the `Content-Type` header to the documented POST shape
- Spec §11 (Decisions log) — new row capturing the spike-time discovery + the pre-build vs post-build framing
- Checklist item 5 — added the `Content-Type` requirement to "What to build" + a 415 regression guard test in the Acceptance criteria

**Decision-log entry to dashboard MCP:** owed. This session does not have `mcp__626Labs__manage_decisions` available; log the entry from a session that has the dashboard MCP wired. Decision payload: title "Auth-ticket POST requires Content-Type: application/json", category "Roblox-side compatibility event", description "Caught at spike-time before any production code committed to the architecture; spec v1.0 didn't capture it; pre-build drift, not post-build divergence; spec §5.7 + §6.2 + §11 + checklist item 5 updated inline."

**Spike status:** validate-only passed. Full launch path pending. Item 1 is not yet complete; do not start item 2 until the full launch verifies an actual Roblox window opens signed in as the test account.

### Update — full launch result

First full-launch run (no `--place` arg, no `placelauncherurl` in URI): Roblox opened with the **correct cached account** (test-account avatar + username top-right) but the session was **"not logged in"** — the new auth ticket never got exchanged for a game-server connection. The launcher had a ticket but no destination to validate it against.

**Second contract finding:** `placelauncherurl` is required for the auth handshake to complete. Without it, RobloxPlayerLauncher opens but can't establish a session. Surgical fix in the spike: default `placeUrl` to `https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=<id>&placeId=920587237&isPlayTogetherGame=false` (Adopt Me — known-stable test target).

Second full-launch run (with the default placelauncherurl): Roblox launched into Adopt Me as the test account, signed in correctly. **Item 1 PASSED.**

**Spec impact:**

- §5.6 (`IRobloxLauncher`) — paragraph added: `placelauncherurl` is required; `LaunchAsync(cookie, placeUrl: null)` must resolve to a default before URI construction
- §10 deferred — added: per-account default place URL (v1.2 candidate)
- §11 decisions log — second new row capturing the spike-time discovery
- Checklist item 6 (`RobloxLauncher`) — "What to build" now spells out the null-`placeUrl` resolution path: app-level default stored in `settings.json`, first-launch prompt seeds it, editable in main-window settings. Per-account default place is explicitly v1.2.

**Decision-log entry to dashboard MCP:** owed alongside the Content-Type entry. This session does not have `mcp__626Labs__manage_decisions` available; log both from a session that does. Decision payload for the second entry: title "Auth handshake requires placelauncherurl in roblox-player URI", category "Roblox-side compatibility event", description "Caught at spike-time before any production code committed; without placelauncherurl Roblox opens with cached account but is not logged in. Spec §5.6 + §10 + §11 updated inline. v1.1 uses an app-level default URL stored in settings.json; per-account default place deferred to v1.2."

**Total spike findings:** 2 (Content-Type + placelauncherurl). Both caught BEFORE production code committed to the architecture — item 1 did exactly the gating job spec §10 designed it for. Net cost of the spike: ~30 min wall-clock, two surgical spec edits, zero rework of production code (because there is no production code yet).

**Item 1 status: DONE.** Ready to proceed to item 2 (solution scaffold + AppLifecycle).

---

## /checklist — v1.2 Discord clan-coordination cycle

Cycle started 2026-05-06 from a `/vibe-iterate competitive` scan against Bloxstrap / Fishstrap / Roblox Account Manager. Outcome: ship the Discord clan-coordination layer (rich presence + server-share party Join + opt-in clan-channel webhook) as v1.2.0.

**Cycle type:** Feature extension to v1.0. Spec at [`docs/superpowers/specs/2026-05-06-discord-clan-coordination-design.md`](docs/superpowers/specs/2026-05-06-discord-clan-coordination-design.md). Extends — does NOT replace — canonical at [`docs/superpowers/specs/2026-05-03-rororoblox-design.md`](docs/superpowers/specs/2026-05-03-rororoblox-design.md).

**Build mode:** autonomous-with-verification. Same posture as v1.0. Architect persona + Builder mode + brisk pacing + fully-autonomous flag in unified profile (promoted 2026-04-26 after 9+ Cart cycles). Verification checkpoints at items 7 (after Layer 1+2 functional, no UI yet) and 10 (after brand assets land).

**Comprehension checks:** off (autonomous default).

**Git cadence:** commit after each item. Single PR shipping all three layers per spec §11 decision (override on small-diff-preferred posture).

**Sequencing rationale:**

- **Item 1 (administrative gate)** — Este creates Discord application + drops AppId in `appsettings.json`. /build hard-pauses if AppId is unset.
- **Items 2-3 (zero-dep primitives)** — DiscordConfigStore + ServerShareExtractor. Pure, testable, no external dependencies.
- **Item 4 (Layer 1 service)** — DiscordRichPresenceService against Lachee.DiscordRichPresence. State machine + reconnect backoff + branded asset keys.
- **Item 5 (Layer 2 outbound)** — RobloxLauncher modification to call ServerShareExtractor + SetParty. Optional dependency keeps existing tests green.
- **Item 6 (architectural gap-closer)** — IAccountLifecycle + AccountLifecycleTracker. **Spec §5.8 assumed this existed in canonical §5; it doesn't.** Item 6 lands the abstraction so item 7 has something to hook.
- **Item 7 (HostedService — wires it all together)** — DiscordPresenceLifecycle. Subscribes to lifecycle events, handles JoinRequested, posts webhooks. The keystone item.
- **CHECKPOINT 1** after item 7 — Layers 1+2 functional manual smoke before adding webhooks + UI.
- **Item 8 (Layer 3 service)** — DiscordWebhookService. Branded embed + threshold-crossing logic + silent-fail wrap.
- **Item 9 (Settings UI)** — Discord Integrations panel. WPF-UI styled, brand tokens via 626labs-design, regex-validated webhook URL.
- **Item 10 (brand assets — human review pause)** — 4 Discord PNGs + webhook avatar via 626labs-design skill. Eyes-on bar applies (pattern x).
- **CHECKPOINT 2** after item 10 — full feature stack manual smoke.
- **Item 11 (final doc + security)** — README v1.2 section, canonical spec extension (NOT banner-correct), CONTRIBUTING.md clan-admin setup, security audit append, dependency audit, local-path audit.

**Spec coverage:** Every numbered new-spec §5 component (5.1-5.8) maps to a checklist item. Data flows §6.1-6.3 distribute across items 4-8. Error handling §7 distributes per-item. Distribution §9 (brand assets) hits item 10. Open items §10: AppId at item 1, webhook avatar at item 10, threshold-config locked at fixed-4.

**Item count:** 11 build items + 2 checkpoints + final doc/security = 13 entries. Within Cart's 8-12 item target band (counting build items only).

**Deepening rounds:** zero (per builder profile habit; spec is 600+ lines of locked design with explicit hand-off section to Cart).

**Risk callouts logged for /build:**

- **Item 1 administrative gate.** /build must hard-pause if `Discord:ApplicationId` is unset in `appsettings.json`. Do not proceed to item 2 without confirming Este completed the dev portal step.
- **Item 6 closes a real spec gap.** `IAccountLifecycle` doesn't exist in canonical §5 despite spec §5.8 assuming it. /build must implement the abstraction in item 6, not assume it.
- **Item 9 size watch.** Settings UI is the largest single item. Flag if it slips past 90 minutes for split into 9a (master toggle) + 9b (webhook config + validation).
- **Item 10 human review.** Brand asset pack must pass human eyes-on review against `~/.claude/skills/626labs-design/colors_and_type.css` even in autonomous mode (pattern x — won't ship broken-looking tile).
- **Single PR posture override.** Small-diff-preferred posture is intentionally bent for this feature; documented in spec §11 + §12. /build should NOT split into multiple PRs without explicit user confirmation.

**Session/friction loggers:** Cart's plugin data dir at `~/.claude/plugins/data/vibe-cartographer/` still does not exist on this machine (carried over from v1.0 cycle). Skipped session-logger.start + friction-logger.log calls; no JSONL entries written. Already filed for /evolve via the v1.0 cycle.

**Atlas + Dashboard cross-reference:**

- Atlas entry: [`.vibe-iterate/atlas.jsonl`](.vibe-iterate/atlas.jsonl) — `competitive` / `queued` (flips to `shipped` on merge with PR URL appended).
- Dashboard decision: `PlA9sfFzlQ2qINOO1kLB` against project `PBWgg5mimZyAzAG3niAp` (RORORO).
- Vibe-iterate flash notes: `~/vibe-iterate-flash-notes.md` carries the standing principle "missing-mode = evolve candidate" and the open candidate `/vibe-iterate:infer`. Discord clan-coordination cycle did not surface new evolve candidates.
