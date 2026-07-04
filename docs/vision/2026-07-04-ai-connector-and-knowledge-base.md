# Next focus — AI connector + PetSim knowledge base

**Date:** 2026-07-04
**Status:** Vision capture (pre-brainstorm). Not scoped, not committed to build. This exists so the next `/brainstorm` starts from a real starting point instead of a cold prompt.
**Building in public:** the development of this is public — design docs, decisions, and the connector itself are meant to be shareable. Write and build accordingly.

---

## The idea, in one line

Let **Claude Code / Claude Desktop drive RoRoRo** (launch accounts, pick a game, follow the main / follow a friend, orchestrate the Ur plugins), and layer on a **weekly-changelog + knowledge-base** brain that turns each week's PetSim 99 changes into concrete guidance: *"these macros from last week may need cleanup; here's one worth recording this week."*

**PetSim 99 first** — it's what the Discord lobby plays, so it's the fastest real-user feedback loop.

---

## The layers (each is a candidate sub-project — own spec → plan → build)

### 1. MCP connector — "drive RoRoRo from Claude" (the MVP)

An MCP server exposing RoRoRo's actions as tools Claude Code / Claude Desktop can call:
- `launch_account(id, game?)`, `launch_all`, `stop_account`
- `follow_friend(account, friendUserId)` / `follow_main(account)` — reuses the follow-launch lane
- `list_accounts`, `account_status` / presence
- select/launch into a specific game or private server

**MVP slice Este named:** launch an account, select a game, follow-the-main. Start there.

**The load-bearing architecture question — how does the MCP server reach RoRoRo?**
- **(a) MCP-as-plugin (favored to explore first).** RoRoRo already hosts a gRPC server on a per-user named pipe with a capability/consent model. An "MCP bridge" plugin exposes RoRoRo actions (and, via the Ur Task action bridge, sibling-plugin actions) as MCP tools. This keeps automation **out of core** and inside the consent-gated plugin wall — the same discipline the marketplace and the Ur family already live under.
- **(b) Standalone MCP server** that talks to RoRoRo over the existing named-pipe gRPC host. Same wall posture, different packaging.
- **(c) In-core MCP surface** — rejected on the same grounds as the marketplace: automation in the Store-listed core reopens the policy-10.2.2 / macro-wall questions. Any MCP/automation surface is a **direct-download / plugin** concern, unpackaged-only.

**The wall still holds.** Driving alts from Claude is automation. Automation stays in a consent-gated plugin, never the Store core. This is a distribution + consent question before it's a feature question.

### 2. Automation reach into the plugins

Beyond launching: connect the MCP layer to the Ur family so Claude can orchestrate the hands and eyes.
- **Ur Task** already exposes an action bridge (`RunMacro` over a named pipe) — the connector could drive "run this macro on these alts."
- **Ur OCR** triggers — "set up a screen trigger that fires macro X."
- The compounding play: Claude reasons about *what* to do (layer 3), the plugins *do* it (Ur Task/OCR), the connector is the wire between them.

### 3. Weekly changelog analysis (PetSim 99 first)

Ingest the game's weekly changelog and produce, per week:
- **Cleanup:** "these macros you recorded last week touch content that changed — worth re-recording or retiring." (Joins the changelog against the user's Ur Task macro library + when they were made.)
- **Suggestions:** "here's a macro worth recording for this week's new event/content."

This is the reasoning layer — an LLM over (the changelog) × (the user's macros/history) × (the knowledge base below).

**Open:** where does the changelog come from — official PetSim patch notes, the game's update feed, Discord, scraping? Provenance and cadence matter.

### 4. Context knowledge base

A curated knowledge base of community experience that grounds layer 3: PetSim **event types**, past macros, past events, what worked and what didn't. Seeded from the Discord lobby's collective experience. RAG-shaped.

**Open:** storage/retrieval shape, how it's seeded + kept current from community input, and provenance/attribution (building in public — credit the lobby's contributions).

---

## Natural sequencing

1. **MCP connector MVP** (layer 1: launch + follow-main + select-game) — decides the transport/wall architecture, the foundation everything else rides on.
2. **Plugin-automation reach** (layer 2) — connector drives Ur Task/OCR.
3. **Changelog analysis + knowledge base** (layers 3-4) — the brain; the biggest new surface (data sourcing, RAG, LLM reasoning) and the one that most benefits from a dedicated brainstorm.

Each layer is independently valuable and independently shippable. Layer 1 is the wall-and-transport decision; don't let layers 3-4's ambition delay proving layer 1.

---

## Open questions for the brainstorm (next session)

- **Transport + home:** MCP-as-plugin vs standalone-over-gRPC vs other. Does the connector become a plugin (favored)?
- **The wall:** confirm the automation-via-MCP posture — consent-gated, unpackaged-only, out of Store core.
- **Action surface v1:** which RoRoRo actions to expose first (launch, follow-main, select-game named as MVP).
- **Changelog sourcing:** official patch notes vs feed vs Discord vs scrape; cadence; reliability.
- **Knowledge base:** storage/RAG shape, seeding from community experience, provenance/attribution, keeping it current.
- **Building-in-public shape:** what's public (repos, design docs, build log), how the lobby contributes to the knowledge base.

---

## Why this fits RoRoRo

RoRoRo is already the ergonomic wrapper for multi-alt Roblox play, with a consent-gated plugin platform (Ur Task/OCR/AFK) and a marketplace to distribute them. This adds a **reasoning + orchestration** layer on top: Claude as the operator, the plugins as the hands, the knowledge base as the memory — pointed at the one game the lobby actually plays. It's the natural next rung after "the ladder is the story" of the Ur family.
