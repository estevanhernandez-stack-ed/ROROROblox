# Ur family arc — design

> **Date:** 2026-07-02 · **Status:** approved (brainstorm session, 626labs-hub seat)
> **Scope:** the RoRoRo plugin family as a product line — what it is, what ships next, how it grows. Not an implementation spec for any single plugin; each ship item below gets its own spec/plan in its own repo.

## Context — what exists today

As of 2026-07-02, the family is three plugins plus the host that grew the doorways they need:

| Piece | State | Notes |
|---|---|---|
| **RoRoRo** (host) | v1.8.0.0 released 2026-07-02 | Tray-residence gate, Limited-account detection, idle awareness + consent-gated `GetAccountActivity` plugin query — built as Ur AFK's doorway. Release notes promise Ur AFK ("watch this space"). |
| **Ur Task** | v0.3.1 stable · v0.4.0 cut, at rc1 | Portable macro recorder/player. v0.3.0 shipped the **action bridge** — named-pipe server (`\\.\pipe\626labs-ur-task`, current-user only) accepting `RunMacro` from sibling plugins, pref-gated, ack-on-accept, re-entry guard. v0.4.0 adds window-relative mouse macros (schema v3) + STACK/GRID window arranging. |
| **Ur OCR** | v0.2.0 stable · v0.3.0 at rc1 | Screen-region OCR/color triggers. v0.2.0 = trigger authoring suite (live match meter, dry-run). v0.3.0-rc1 = **bridge client** — a trigger fires a Ur Task macro. |
| **Ur AFK** | v0.1 merged 2026-07-02, unreleased | One job: keep idle alts alive (countdown pill, single Space tap, F8 skip). Was blocked on the host's activity query; RoRoRo 1.8 shipping today removed the blocker. Icon is still a borrowed ur-task placeholder — pre-ship design gate. |

Ur OCR + Ur Task working together over the bridge is the perception→action loop — all local, account-safe. Ur AFK is the simplified entry point for users who want zero learning curve.

## The thesis

**The Ur family is a ladder of consent-gated automation for RoRoRo-managed alts. The ladder is the story, the bridge is how the family compounds, and trust is the bar every rung clears.**

Three arcs were weighed and merged:

- **The Ladder** (user-level tiers) is the adoption story — it matches how clan members actually pick up tools, one rung of trust at a time.
- **The Body** (eyes/hands/heartbeat over a consent-gated nervous system) is the growth mechanic — every new plugin should multiply with the existing ones via the bridge, not just add.
- **The Trust Line** (automation you can recommend on camera) is the constant bar — narrow declared capabilities, visible countdowns, abort keys, captcha-touching is a permanent no.

A pure ladder undersells the bridge; a pure body-metaphor delays the cheap AFK win behind bridge hardening. The hybrid keeps the ship order honest.

### The rungs

| Rung | Plugin(s) | Who it serves |
|---|---|---|
| 1 | **Ur AFK** — one toggle, one key ever sent | The clan member installing their first plugin and reading their first consent sheet |
| 2 | **Ur Task** — record once, play on any alt | The regular who wants the grind automated |
| 3 | **Ur OCR + Ur Task** over the bridge | The power user building perception→action automations |

## Ship order — next 90 days

**Now:**

1. **Ur AFK v0.1 release.** Unblocked as of today. Gates: real icon through the `626labs-design` skill (placeholder disqualifies per the SnipSnap retro rule), signed release artifacts (`manifest.json` + `manifest.sha256` + `plugin.zip`), README install section replacing build-from-source. Cashes the 1.8 release notes' promise.
2. **Bridge pair to stable.** Smoke the OCR-trigger→Task-macro loop end-to-end (Ur OCR `docs/smoke-checklist.md`), then promote Ur Task v0.4.0 and Ur OCR v0.3.0 out of rc.

**Next:**

3. **Repo rename: `Ur-OCR` → `rororo-ur-ocr`.** The csproj is already `rororo-ur-ocr.csproj`, both siblings follow the `rororo-ur-*` convention, and the README's install/clone links already point at `rororo-ur-ocr` — which currently 404s. One GitHub rename fixes naming consistency and heals the links at once (GitHub redirects the old slug). Verified live on current main 2026-07-02, not a stale-clone artifact.
4. **Ur AFK v0.2.** Rebind UI (the documented F8-clash gap), plus the composition move: optionally fire a user-chosen Ur Task macro as the keep-alive instead of bare Space. Puts all three plugins on the bridge and turns rung 1 into an on-ramp to rung 2.

**Then:**

5. **Plugin #4** — see scoring below.

## Plugin #4

Scored on rung-fit × bridge-composition × clan pain × effort:

- **Session Stats, reframed as the family ledger — the pick.** Not just uptime chips: the family's memory. Per-account uptime and launches, plus what the hands did (Task playbacks) and what the eyes fired (OCR triggers). Read-only, zero `system.*` capabilities — the safest consent sheet in the family, which is exactly what a rung-1 observer should feel like. Under the body metaphor it's the memory; under the ladder it's rung 1; under trust it's proof that observers are welcome. The ledger reframe is what earns it bridge-composition points the original pitch lacked.
- **Discord Rich Presence — second, deliberately timed.** The strongest viral surface on the bench (clan-visible presence demos RoRoRo to non-users organically), but it should land *with* the 626 Labs Discord server from the release-channel plan, not before. Parked until the server stands up.
- **Auto-Relaunch on Exit** — still backlog; re-evaluate after Ur AFK ships (both touch the "keep farming without supervision" surface; see whether crash-relaunch becomes the next ask).

## Article trigger (626labs-hub follow-up)

The hub's follow-up Field Note — sequel to the 2026-05-06 "recommend on camera" Medium piece — publishes **after** items 1 and 2 land, so all three plugins are installable at publish time. Working angle: *the launcher grew a family* — eyes, hands, heartbeat, and the consent-gated nervous system connecting them. The hub's stale `site.json` copy ("Now at v1.7", "the first plugin, RoRoRo Ur Task") rides along in the same PR. Owned by the 626labs-hub repo, triggered by Ur AFK's release going live.

## Open questions

- Does Ur AFK v0.2's bridge delegation need a new capability disclosure (it would invoke another plugin's `system.*` behavior indirectly)? Resolve during v0.2 scoping.
- Session Stats ledger: does reading Task/OCR activity require new host event streams, or do the plugins publish their own activity over the bridge? Shapes the contract work — scope before committing to #4.
- `rororo-mac` parity with the plugin family is explicitly out of scope for this arc.
