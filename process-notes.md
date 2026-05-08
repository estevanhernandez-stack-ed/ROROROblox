# RORORO — Process Notes

Cart cycle started 2026-05-03 (v1). **Spec-first cycle (pattern mm)** — substantive design done in pre-onboard brainstorming, captured in `docs/superpowers/specs/2026-05-03-RORORO-design.md`. /scope, /spec, /prd, /builder-profile compressed to pointer-stubs + compressed PRD because the substantive thinking happened upstream.

Cycle #2 (v1.3.x default-game widget + local rename) opened 2026-05-07 — see `## /onboard — autonomous run (2026-05-07)` below.

## /scope — autonomous run (2026-05-07, cycle #2)

Spec-first cycle (pattern mm). Inherited an approved 352-line design spec (`docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md`) with §1-12 fully populated and "Approved for implementation planning" status. No architectural decisions still open; no deepening rounds warranted.

Skipped the conversational interview entirely. Triggers on file:

- builder profile autonomy = `fully-autonomous` (locked 2026-04-26, 13/13 completion at unified profile)
- session-start override = "no clarifying questions"
- pacing = brisk
- prior cycle's process-notes prescribed "6-line pointer-stub same shape as cycle #1"
- spec is clean (deepening-rounds-zero habit qualifies)

**What changed in `docs/scope.md`:** rewritten end-to-end. Previous content was cycle #1's v1.1 scope (multi-instance + saved accounts) — that scope's substance lives durably in `2026-05-03-rororoblox-design.md`, so re-preserving it in scope.md was redundant. New scope.md is the v1.3.x cycle pointer-stub: in-scope (default-game widget + per-record `LocalName` overlay across 5 trigger surfaces / 12 render surfaces), out-of-scope (Mac parity port, bulk rename, sync, new private-servers sheet, tray account list, auto-shorten, pencil-on-hover), distribution audience carried unchanged from v1.1. Cycle history line at the top points readers to both prior specs (v1.1 + v1.2) so the per-cycle artifact doesn't accidentally erase the chain.

**Explicit cuts surfaced from the design spec into scope.md** — Mac parity is the load-bearing one. Mac sibling repo exists (memory `RORORO Mac sibling repo`); the v1.3.x cycle is Windows-only deliberately, with the Mac port tracked as the natural follow-on once Windows proves the shape. Pencil-on-hover vs right-click is the second decision worth surfacing — design spec §9 decision 4 records the trade.

**Session/friction loggers:** Cart's plugin data dir at `~/.claude/plugins/data/vibe-cartographer/` still does not exist on this machine. Third cycle confirming the gap (cycle #1 + cycle #2 onboard already flagged). Strong /evolve signal — the loggers should auto-create their data dir or fail loud.

**Decision-log entry to dashboard MCP:** none owed for this command. /scope on a spec-first cycle is mechanical translation, not an architectural fork. The two cycle-#1 spike entries (Content-Type + placelauncherurl) and the cycle-#2 onboard entry remain pending from earlier sessions.

**Handoff:** Run `/prd`. Compressed PRD shape — stories distilled from spec §2 goals + §6 data flows + §7 render surfaces, acceptance criteria distilled from spec §8 edge cases + §12 testing. Same shape as cycle #1's PRD.

## /prd — autonomous run (2026-05-07, cycle #2)

Spec-first cycle (pattern mm) compressed PRD. Skipped the deepening rounds question — spec is clean, fully-autonomous + brisk pacing + Architect persona on file, "no clarifying questions" override active. Followed cycle #1's PRD shape from `docs/prd.md` v1.1: epic → role/want/so-that stories → AC bullets → prioritization + cuts.

**Compression decisions:**

- **Two epics, not three.** Considered a separate "Schema + persistence" epic for the on-disk forward/backward-compat work. Rejected — the JSON shape change is rename-shaped, not its own user story. Folded as Story 2.4 inside Epic 2 (Local rename overlay) where the AC sits next to the user-facing rename behavior it underwrites.
- **10 stories, not 12+.** Cycle #1 ran 11 stories across 2 epics. v1.3.x has less surface area (no auth, no distribution, no DPAPI bucket — that's all v1.1 baggage that's already shipped). 10 stories felt like the right granularity for "stories map cleanly to checklist items" — Epic 1 has 4 stories (one per spec §5.4 widget concern + empty state + compact-mode), Epic 2 has 6 stories (right-click triggers + popup behavior + render-surface coverage + JSON compat + Roblox-refresh decoupling + error edges). Each story has 4-7 AC bullets with no gluing required.
- **Quality bar called out explicitly in Story 2.2.** Rename popup must match `ui:FluentWindow` chrome (spec §3); flagged inline in AC because cycle #1's reflection ([SnipSnap pattern x](https://github.com/estevanhernandez-stack-ed/SnipSnap)) and the builder profile both raise the "won't ship a broken-looking tile even if the rest works" bar — putting it in the AC means /checklist surfaces it in the build sequence rather than letting it slip to "polish later."
- **Render-surface coverage is one story, not 12.** Spec §7 enumerates 12 render surfaces. Listing each as its own story would balloon the PRD without changing the work — the coverage is one binding-pattern change repeated 12 times. Story 2.3 enumerates the surfaces in the AC and the manual smoke checklist in spec §12 covers per-surface regression.

**What's NOT in this PRD that someone might expect:**

- Decision-log AC. Per cycle #1 convention, decisions log to the dashboard MCP separately, not as story AC. Logged once at /onboard for the cycle event; mid-cycle decisions log as they emerge.
- Performance AC for the widget. The data flow is in-memory only (one INPC raise on `DefaultChanged`); spec §6.1 walks the path and there's no measurable user-visible latency to assert against.
- Telemetry / metrics AC. RORORO ships zero telemetry per the canonical v1.1 spec — no v1.3.x add.

**Deepening rounds:** zero. Spec was authored 2026-05-07 with §1-12 fully populated and "Approved for implementation planning" status; design decisions §9 cover the six trade calls; §11 forward-looking captures the cuts. Three Cart cycles in a row now (cycle #1 v1.1, cycle #1 v1.2, cycle #2 v1.3.x) with deepening-rounds-zero on /prd when the spec is clean — that habit is locked.

**Session/friction loggers:** still no `~/.claude/plugins/data/vibe-cartographer/` data dir on this machine. Fourth cycle confirming the gap.

**Decision-log entry to dashboard MCP:** none owed. /prd compression is mechanical translation, not an architectural fork.

**Handoff:** Run `/spec`. Pointer-stub shape with section index — same as cycle #1's `docs/spec.md`. The substantive technical thinking already lives in `docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md` §3-§9; spec.md just becomes the navigable index.

## /spec — autonomous run (2026-05-07, cycle #2)

Spec-first cycle (pattern mm) — `/spec` collapses to a pointer-stub + section index pointing at `docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md` §1-§13. No new technical thinking. Same shape as cycle #1's `docs/spec.md`.

**What landed in `docs/spec.md`:**

- Pointer line + cycle history (v1.1 + v1.2 prior specs + this cycle's spec).
- Section index covering all 13 numbered sections of the canonical spec (cycle #1 indexed §1-§11 + Appendix; this cycle's design spec runs §1-§13 because it has separate §10 "Known UX concern — deferred", §12 testing, §13 references blocks the v1.1 spec didn't carve out).
- "What's deliberately not in this cycle" call-out at the bottom — listed the v1.1 interfaces that **don't** change (`IRobloxApi`, `IRobloxLauncher`, `IMutexHolder`, `ICookieCapture`, `App`/`AppLifecycle`, MSIX/Velopack). The four mutations that DO land (`LocalName: string?` × 3 records, `UpdateLocalNameAsync` × 3 stores, `DefaultChanged` × 1 event) are listed inline. This call-out exists because /checklist will be tempted to grow items for "the rest of the surface area" and the explicit "0× changes to X, Y, Z" line stops that drift before it starts.
- Banner-correct rule reminder at the bottom — when build reality diverges from the canonical, banner-correct the canonical (pattern v from Vibe Thesis), don't rewrite top-to-bottom (per `CLAUDE.md` "Don't rewrite the canonical spec on drift" rule).

**Decisions captured in this run:**

- **No new architectural sections in spec.md.** Considered duplicating the §5 component shapes for offline grokability. Rejected — duplicated content drifts; the pointer-stub IS the contract that `docs/superpowers/specs/...` is canonical. Per CLAUDE.md "spec.md is a Spec-first Cart pointer-stub" — that's a load-bearing convention, not a stylistic choice.
- **Cycle-history line included.** Cycle #1's spec.md didn't have one (no prior cycles); this cycle does. The chain matters because /checklist references will sometimes need to lean on v1.1 / v1.2 specs (e.g., the FPS limiter's `GlobalBasicSettingsWriter` mention is load-bearing for items that touch per-account state).
- **Section index goes deeper than cycle #1.** Cycle #1's index listed §5 sub-bullets but only top-level for §6/§7. This cycle's index lists §5 + §6 + §7 + §8 + §9 + §10 + §11 + §12 + §13 sub-bullets. Reason: this cycle's spec is denser (12 read surfaces, 5 trigger surfaces, 6 decisions, 8 edge cases) — checklist items will reference specific sub-points more often than cycle #1's "go read §5.6 entirely" pattern.

**Deepening rounds:** zero (skipped the question per autonomous-run contract). Spec is clean, locked, "Approved for implementation planning." Four Cart cycles in a row now (cycle #1 v1.1 onboard/scope/prd/spec, cycle #1 v1.2, cycle #2 v1.3.x at /scope, /prd, /spec) with deepening-rounds-zero on translation commands. Pattern mm + clean spec + fully-autonomous = no deepening, every time.

**Session/friction loggers:** still no `~/.claude/plugins/data/vibe-cartographer/` data dir on this machine. Fifth cycle confirming the gap. Strong /evolve signal.

**Decision-log entry to dashboard MCP:** none owed. /spec compression is mechanical translation. The cycle-#1 spike entries (Content-Type + placelauncherurl) and the cycle-#2 onboard entry remain the standing pending list.

**Handoff:** Run `/checklist`. Build mode should match cycle #1: autonomous-with-verification with checkpoints. Sequencing prediction (revisited at /checklist — see `## /checklist — autonomous run` below):

1. **Schema additions first** (records + JSON compat tests) — load-bearing primitive.
2. **Store interface additions** (`UpdateLocalNameAsync` × 3 + `DefaultChanged` event) with unit tests.
3. **`RenameTarget` DTO + `RenameTargetKind` enum** in App project.
4. **`RenameWindow`** XAML + code-behind + `ui:FluentWindow` chrome verification.
5. **`MainViewModel` plumbing** — `DefaultGameDisplay` INPC, `SetDefaultGameCommand`, `RenameItemCommand`, `ResetItemNameCommand`, `DefaultChanged` subscription.
6. **`DefaultGameWidget`** XAML in `MainWindow.xaml` Header Row 2.
7. **Right-click context menus** on the 5 trigger surfaces.
8. **Render-surface coverage pass** — every place that shows `Name`/`DisplayName` switches to `LocalName ?? …`. 12 surfaces from §7.
9. **Manual smoke** — full spec §12 checklist + visual chrome verification on `RenameWindow`.
10. **Docs + release notes draft** — universal final item.

10 items. Cycle #1 ran 12. Smaller surface area = smaller checklist. /checklist will revisit and confirm.

## /checklist — autonomous run (2026-05-07, cycle #2)

Spec-first cycle (pattern mm). Inherited the canonical 352-line design spec + the just-written compressed PRD with 10 stories across 2 epics. Build sequence translation, not deepening.

**Build mode:** autonomous-with-verification, matching cycle #1. **Comprehension checks:** off. **Git cadence:** commit after each item. **No spike** — v1.3.x lives entirely on top of stable v1.1/v1.2 interfaces; no Roblox-side contract surface area to gate against. /spec-time prediction was 10 items; landed at **9** after one collapse.

**The collapse:** /spec-time predicted "Roblox-side refresh decoupling" as its own item (item 9 in the prediction). On second look, that's not a separable build chunk — it's a one-line `with`-expression-preserve-`LocalName` discipline at every existing `IRobloxApi` callback site, plus a unit test. The work belongs naturally inside item 5 (MainViewModel plumbing) where those callbacks live. Folding it in keeps each item single-responsibility instead of fragmenting "ViewModel" across two items. Net: 10 → 9.

**Two checkpoints, deliberately placed:**

- **Checkpoint 1 after item 2** — primitives complete (schema + stores). Confirms the storage layer is bulletproof before UI items lean on it. Cycle #1's equivalent was after item 7 (capture path complete). The earlier checkpoint here reflects a different shape — v1.3.x's load-bearing primitives are entirely Core (no UI yet), so the checkpoint sits at the Core/App boundary.
- **Checkpoint 2 after item 8** — full UI coverage complete. Last gate before docs. Cycle #1's equivalent was checkpoint 2 after item 10 (post-functional-app, pre-packaging). v1.3.x has no packaging change, so the second checkpoint moves up to the render-coverage line.

**Risk callouts logged for /build:**

- **Item 2 (re-add preservation) is the dominant regression risk.** `IFavoriteGameStore.AddAsync` and `IPrivateServerStore.AddAsync` already replace on duplicate keys. The new behavior is "replace EXCEPT preserve `LocalName`." Every `with` expression or constructor call in those replace paths must explicitly thread `LocalName: existing.LocalName`. Item 2's Acceptance includes the regression-guard test, but the audit-every-call-site discipline is on the /build agent.
- **Item 4 (`RenameWindow`) chrome quality bar is non-negotiable.** Builder profile carryover from cycle #1 + spec §3 + pattern x: chrome must match `ui:FluentWindow` of `WebView2NotInstalledWindow` / `RobloxNotInstalledWindow` / `DpapiCorruptWindow` side-by-side. Verify step explicitly calls "compare side-by-side." If it looks placeholder-y, halt + fix before item 5.
- **Item 8 (render coverage across 12 surfaces) is the largest single item by file count.** Flagged for split into 8a (game/server surfaces) + 8b (account surfaces) if it slips past 90 minutes. Same shape as cycle #1's item 9 (MainWindow + 4 modals) split-flag.
- **Follow-strip chips surface is currently `Visibility=Collapsed` (memory `project_rororo_follow_masked_v1.2`).** Item 8 still wires the binding so the eventual un-mask inherits rename support for free. Single-line comment noting the masked state goes in the XAML at item 8 time.

**Spec coverage matrix:**

| Spec section | Checklist item(s) |
|---|---|
| §1 Overview | All items (context) |
| §2 Goals/non-goals | Items 6, 7, 8 (in-scope features) + item 9 (cuts in release notes) |
| §3 Stack | All items (no new deps reaffirmed) |
| §4 Architecture | Items 1, 2, 3, 4, 5 |
| §5.1 Data model | Item 1 |
| §5.2 Store interfaces | Item 2 |
| §5.3 RenameTarget | Item 3 |
| §5.4 DefaultGameWidget | Item 6 |
| §5.5 RenameWindow | Item 4 |
| §5.6 Right-click context menus | Item 7 |
| §6.1 Quick-switch flow | Items 5, 6 |
| §6.2 Rename flow | Items 4, 5, 7 |
| §6.3 Reset flow | Items 4, 5, 7 |
| §7 Render surfaces | Item 8 |
| §8 Edge cases | Items 1, 2, 5 |
| §9 Decisions log | Decision-log entry to dashboard MCP at /reflect or as decisions emerge during /build (not its own item) |
| §10 Known UX concern | Out of scope for v1.3.x; banner reminder in spec.md |
| §11 Forward-looking | Item 9 (release notes) + scope.md (cuts) |
| §12 Testing | Items 1, 2, 5 (unit) + items 4, 6, 7, 8 (manual smoke) + item 9 (release-gate smoke) |
| §13 References | spec.md pointer-stub (already in place) |

Every numbered section maps to at least one item. The two-epic / 10-story PRD compresses cleanly: Epic 1 → items 5 + 6, Epic 2 → items 1 + 2 + 3 + 4 + 5 + 7 + 8.

**Deepening rounds:** zero (skipped per autonomous-run contract). Six Cart commands in a row across two cycles now (cycle #1's /scope, /prd, /spec, /checklist + cycle #2's /scope, /prd, /spec) with deepening-rounds-zero on translation commands when the spec is clean. Pattern locked.

**Session/friction loggers:** still no `~/.claude/plugins/data/vibe-cartographer/` data dir on this machine. Sixth cycle confirming the gap. Strong /evolve signal — three consecutive cycles, both onboard + scope + prd + spec + checklist commands across two cycles all confirming the loggers don't auto-create their data dir or fail loud.

**Decision-log entry to dashboard MCP:** none owed for /checklist itself. The standing pending list (cycle-#1 spike entries Content-Type + placelauncherurl, cycle-#2 onboard entry) carries forward unchanged.

**Handoff:** Run `/build` (or, more accurately, drive item 1 in a /build session). Build mode is autonomous-with-verification — Architect persona + builder mode + brisk pacing + fully-autonomous flag + Verify steps that gate via `dotnet test` or manual smoke at each item completion. Two checkpoints (after item 2 and after item 8) are explicit human-review gates regardless of mode.

## /onboard — autonomous run (2026-05-07)

Cart cycle #2 on this repo (lifetime cycle #14). Returning builder, fully-autonomous on file (locked 2026-04-26 + 13/13 completion at unified profile), explicit "no clarifying questions" at session start. Skipped the conversational interview entirely; pulled values from `~/.claude/profiles/builder.json` + project state.

**Values applied:**

- Persona: `architect` (from `shared.preferences.persona`) — locked, cross-plugin
- Mode: `builder` (from `plugins.vibe-cartographer.mode`)
- Pacing: brisk (consistent with builder mode + Architect persona)
- Autonomy: fully-autonomous (from `docs/builder-profile.md` — local cycle artifact carries the flag; unified profile field was `None`, drift noted)
- Deepening rounds: zero for `/scope`, `/prd`, `/spec` (pattern mm + spec is clean + "Approved for implementation planning" status on the design doc)
- Cycle type: Spec-first (pattern mm) — `docs/superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md` is the substantive design durable-storage; downstream commands compress to pointer-stubs + compressed PRD
- Project goals: "ship Mac-banner parity, Windows-tailored — quick-switch default-game widget + per-record local rename overlay across all three stores" (verbatim from spec §1)
- Project origin: extending existing repo (RORORO v1.2 just shipped per-account FPS limiter; v1.3.x is the next feature add)
- Design direction: WPF-UI by lepoco continues; rename popup MUST match `ui:FluentWindow` chrome of existing modals per spec §3 — quality bar from cycle #1 applies
- Architecture docs: cycle #1 design spec + new cycle #2 design spec, both in `docs/superpowers/specs/`. Stack locked at .NET 10 LTS + WPF + WPF-UI + DPAPI + MSIX + Velopack — no new dependencies for this cycle (spec §3)
- Deployment target: `microsoft-store-msix-velopack` — refreshed for this cycle (unified profile field was stale at `marcus-landing-zone-azure-devops` from cycle #13 Marcus context; updated)
- Distribution audience: Pet Sim 99 clan first, Microsoft Store second (carried from cycle #1 — same audience, same UX bar)

**Defaults / drift surfaced:**

- `(profile drift — confirm on next run)` `plugins.vibe-cartographer.autonomy` was `None` on the unified profile but local cycle artifact says `fully-autonomous`. Honored the local artifact for this run; suggest the next interactive `/onboard` reconcile the two.
- `(profile drift — confirmed)` `plugins.vibe-cartographer.deployment_target` was `marcus-landing-zone-azure-devops` (cycle #13 Marcus). Updated to `microsoft-store-msix-velopack` for this cycle.
- `(no data — defer)` `plugins.vibe-cartographer.build_mode_preference` was `iterative-prototype`. Spec-first cycles don't really fit that framing — leaving as-is rather than overwriting; `/checklist` will set the actual build mode (likely `autonomous-with-verification` matching cycle #1).

**Session/friction loggers:** Cart's plugin data dir at `~/.claude/plugins/data/vibe-cartographer/` still does not exist on this machine (carried from cycle #1 process notes). Skipped `session-logger.start` + `friction-logger.log` calls; no jsonl entries written. `/evolve` should pick this up as a tooling gap the same way cycle #1 flagged it — second cycle confirming the gap is signal.

**Decay check:** skipped per autonomous-run contract (any stale `_meta` field defers and surfaces on the next interactive run; no stamps written).

**Decision-log entry to dashboard MCP:** owed alongside the two cycle-#1 spike entries (Content-Type + placelauncherurl) that are still pending. This session does not have `mcp__626Labs__manage_decisions` available — log all three from a session that does. Decision payload for this onboard: title "RORORO Cart cycle #2 opened (v1.3.x default-game widget + local rename)", category "Architectural / cycle event", description "Spec-first cycle (pattern mm) inheriting an approved 352-line design spec. Stack locked, no new dependencies, no architectural decisions open. /scope, /prd, /spec will run as pointer-stubs + compressed PRD; substance lives upstream. Builder fully-autonomous, Architect persona, builder mode, brisk pacing — same operating shape as cycle #1."

**Handoff:** Run `/clear`, then `/scope`. The pointer-stub will be 6 lines and reference the design spec — same shape as cycle #1's `docs/scope.md`.

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
