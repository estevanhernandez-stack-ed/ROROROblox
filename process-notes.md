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

---

## /checklist — autonomous run (2026-05-20, cycle v1.5.0 presence account-UX)

Spec-first cycle (pattern mm). Canonical spec authored this session via brainstorming: [`docs/superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md`](docs/superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md), approved (augment approach). Skipped the conversational interview — builder profile is `fully-autonomous` (locked 2026-04-26), brisk pacing, Architect persona, deepening-rounds-zero on a clean spec. Same "autonomous run" shape as cycles #1-#2.

**Sequencing rationale (dependency-aware):**

- **Core before VM before UI.** Items 1-2 (`PresenceService` in Core) have no dependency on the WPF layer and are pure TDD — they land first so the data source exists before anything consumes it. Item 3 (`AccountSummary` reconciliation) is also pure VM logic, TDD, depends only on the presence enum already in Core. Item 4 wires the two together into `MainViewModel` + DI — the heaviest item and the only one that touches app lifecycle.
- **Riskiest external surface early.** Item 1 hits `presence.roblox.com` — the new Roblox-side dependency. Putting it first means if the presence contract surprises us (rate limits, self-visibility under invisible mode), we find out before building four items on top of it.
- **Launch multiple after the status fix (item 5).** The "Launch multiple does nothing" symptom is downstream of the ghost (phantom-running starves eligibility), so the eligibility change has to read the *reconciled* state from items 3-4. Building it earlier would mean wiring against a state model that doesn't exist yet.
- **Anti-ghost `OnProcessExited` change rides in item 4**, not its own item — it's a 3-line change that only makes sense once the presence subscription and `RequestImmediateRefreshAsync` (item 2) are both present to confirm the close.

**Methodology:** autonomous-with-verification. Two checkpoints — C1 after item 4 (first runnable; ghost visibly fixed on a respawned client), C2 after item 6. TDD-strict on items 1-3, 5; items 4 + 7 are verify-by-running. Commit after each item, conventional commits. Branch `v1.5.0-presence-account-ux` already cut; spec committed at `18bec93`; decision logged to dashboard (`cHX5g7nOQeDmSHWjqPym`).

**Item count:** 7. Tighter than the 8-12 "typical" because this is a focused credibility hotfix, not a feature cycle — tags + private-server picker (v1.5.1) and import/export (deferred) were carved out at brainstorm time, so the spec is deliberately narrow. Final item is Documentation & Security Verification per the skill contract, with the cookie-leak audit (`dpapi-cookie-blast-radius`) called out explicitly because the new presence path decrypts cookies per poll.

**Session/friction loggers:** Cart's plugin data dir at `~/.claude/plugins/data/vibe-cartographer/` still absent on this machine — fourth cycle confirming the gap (cycles #1, #2 already flagged). Standing /evolve signal; loggers should auto-create their data dir or fail loud. Skipped the JSONL session/friction logging accordingly; user-facing artifacts (checklist.md, this section) are the durable record.

**Handoff:** Run `/build`. Autonomous through the checklist, pausing at C1 (after item 4) and C2 (after item 6).

## /build — autonomous run (2026-05-20, cycle v1.5.0 presence account-UX)

All 7 items complete on branch `v1.5.0-presence-account-ux`. Final state: `dotnet build ROROROblox.sln` 0 errors, **404/404 tests passing** (was 363 at cycle start; +41 across the cycle). Each item dispatched to a general-purpose subagent (TDD-strict on items 1-3, 5; verify-by-running on item 4); orchestrator marked items complete + ran the two checkpoints.

**Items 1-2 (PresenceService, Core):** poll loop + game-name cache; resilience (401→expired signal, empty-list→hold-last, concurrency cap + jitter, fast-confirm `RequestImmediateRefreshAsync`). Verified against source that `GetPresenceAsync` swallows non-401 to empty list → that's the hold-last signal; populated-Offline = genuinely offline. 429 true-backoff documented as a limitation (RobloxApi would need to surface the status code).

**Item 3 (AccountSummary):** presence-aware reconciliation. Headline ghost-fix test: `IsRunning=false, InGame=true` → "In {game}", never "Closed".

**Item 4 (VM + DI wiring):** lazy DI delegate to avoid the MainViewModel↔PresenceService construction cycle; anti-ghost `OnProcessExited` rewrite (defers close-stamp to presence). Checkpoint C1.

**C1 finding → fix:** builder verified rows show the game, then flagged an account that exited a game but stayed in the client read a vague "Connecting…→Running". Added explicit "At Roblox home" (OnlineWebsite presence, or a settled live client not in-game) + "In Studio" states. Builder confirmed "switches to roblox home fast." Robust to whichever presence type Roblox returns at-home (unconfirmed which).

**Item 5 (Launch multiple):** extracted a pure `LaunchEligibility` helper (testable without the VM) over mocking; eligibility = `!(InGame || IsRunning)`; pre-snapshot presence refresh; never-silent banners with skip-reason ("6 dispatched · 1 already running"). Checkpoint C2.

**C2 finding → accepted:** builder hit a just-closed alt being skipped on instant retry. Root cause is Roblox's own presence propagation lag (upstream of our poll cadence) — `IsRunning` flips instantly but Roblox keeps reporting in-game for a few seconds. The 25s-cadence race is closed; the Roblox-side lag is not, without trusting local process-exit over presence for eligibility (reopens a bootstrapper-respawn edge). Builder accepted; recorded in spec Risks. StartMain CanExecute also brought onto the augment rule.

**Items 6-7:** version 1.5.0.0 (csproj + manifest lockstep), clan-facing release notes + Store listing "What's new", cookie-leak audit (dpapi-cookie-blast-radius: PASS — cookie never logged/persisted outside DPAPI), local-path grep clean, no vulnerable deps, docs synced.

**Mid-build scope event:** clan asked for private-server management "like the games library" (selectable per-account, not Squad-into-one-server). Builder chose finish-v1.5.0-first; sharpened requirement captured in spec out-of-scope as the **v1.5.1** shape (with account tags). Cross-machine import/export still deferred to its own cycle.

**Repo hygiene flag (RESOLVED 2026-05-20):** `ROROROblox.slnx` is canonical — it's the only git-tracked solution, CI `release.yml` restores/tests against it, and it holds all 5 real projects (incl. `PluginTestHarness`) while excluding the gitignored `spike/`. The `ROROROblox.sln` was an UNTRACKED local stray that Qodo IDE auto-regenerates (already gitignored, and incomplete — missing `PluginTestHarness`). Cycle builds used the stray `.sln` explicitly, which silently skipped the integration-test project. Fix: deleted the local stray; `.gitignore` already covers it; CLAUDE.md "Common tasks" now says build/test with `ROROROblox.slnx`. Bare `dotnet build` resolves to `.slnx` cleanly until Qodo regenerates the stray again — build `ROROROblox.slnx` explicitly to be safe.

**Session/friction loggers:** Cart plugin data dir still absent (4th cycle) — JSONL logging skipped, durable record is here. Standing /evolve signal.

**Handoff:** branch ready for PR to `main`. Store-MSIX + Velopack release is builder-driven (memory: "I drive the full release through Store MSIX build"). Next feature cycle: v1.5.1 (private-server library + account tags).

---

## /checklist — autonomous run (2026-05-21, cycle v1.6.0 account transport + bundle)

Spec-first cycle. Canonical spec authored this session: `docs/superpowers/specs/2026-05-21-rororo-account-transport-and-bundle-design.md`. Skipped the interview (fully-autonomous, brisk, deepening-rounds-zero — same as prior cycles). v1.5.0 shipped (PR #24, released v1.5.0.0 to GitHub; Store submission builder-driven). Tags graduated from v1.5.1 into v1.5.0; v1.6.0 absorbed the rest.

**Bundle (5):** account transport (anchor, security-sensitive) · saved private servers in the dropdown · tag UI (collapsed "+" chip + filter) · fix/restore Follow · cross-cutting security pass.

**Brainstorm decisions (transport):** PBKDF2-SHA256 @600k + AES-256-GCM (dependency-free); merge-by-userId import (non-destructive); full per-account setup travels; enforced passphrase + strength meter. Two scope corrections from the builder mid-design: (1) private servers already exist + are renamable — the fix is just populating the dropdown, NOT a library overhaul; (2) the open empty add-tag bar becomes a collapsed "+" chip you engage.

**Sequencing rationale:**
- **Follow diagnostic is item 1, a read-only GATE.** It was masked because it broke; root-cause first so we learn early whether the fix lands this cycle (item 8) or splits out. Builder explicitly wanted it gated first.
- **Transport crypto early (items 2-4), riskiest + security-sensitive.** Core service (2) -> AccountStore export/merge (3) -> a dedicated crypto-hardening + cookie-audit pass (4) BEFORE any UI touches it.
- **Transport UI (5) = C1** — first end-to-end export->import. Private servers (6) + tag UI (7) + Follow fix (8) = C2. Item 9 = mandatory Documentation & Security Verification (app-wide cookie audit, deliberate-export disclosure updates, gitignore `*.rororo-accounts`).

**Item count:** 9. autonomous-with-verification, TDD-strict on Core/VM (2,3,6,7), verify-by-running on UI (5,8) + investigation (1) + audit (4,9).

**New gitignore need flagged:** `*.rororo-accounts` export bundles contain encrypted cookies — never commit. Item 9 adds the rule; pre-commit secret-scan is the backstop.

**Session/friction loggers:** Cart plugin data dir still absent (5th cycle).

**Handoff:** Run `/build`. Autonomous; C1 after item 5, C2 after item 8; Follow scope confirmed at item 1 before item 8.

## /build — autonomous run (2026-05-21, cycle v1.6.0 account transport + bundle)

All 10 items complete on branch `v1.6.0-account-transport`. Final: `dotnet build ROROROblox.slnx` 0 errors, **519 unit + 5 harness (1 skipped) green** (was 450 at cycle start; +69). Each item dispatched to a subagent; two checkpoints + several builder course-corrections.

**Items 1-5 (transport):** Follow diagnostic gate (item 1) found Follow was never masked — corrected memory + spec, reshaped item 8. Crypto core (2: PBKDF2-600k + AES-256-GCM, versioned bundle), AccountStore export/merge (3), security gate (4: dpapi audit PASS + hardening tests), export/import UI + passphrase strength gate (5). **C1 passed** (builder tested export→import).

**C1 fix:** builder couldn't find Export in "Settings" — item 5 had put it in `SettingsWindow` (the *Games* window); the gear "⚙ Settings" opens `PreferencesWindow`. Relocated the entry points there (commit 64a9329).

**Items 6-8:** saved private servers in the per-account dropdown (6, low-blast-radius FavoriteGame extension), tag "+" chip + reorder-safe filter (7), Follow land-at-home guard unified across all 3 follow paths via tested `EvaluateFollow` (8). **C2 passed** — builder confirmed PS ✓, tag chip ✓, **follow ✓** (clears the Roblox-side `RequestFollowUser` gate; Follow ships).

**C2 finding → item 9 folded in:** during C2 the builder hit a Roblox install box mid-launch → the WRONG account launched (with captcha). Root cause: the AppStorageDefender's fixed 12s window expired before the install-delayed client read the identity. Confirmed item 6 did NOT cause it (untouched account-identity path) — pre-existing. Builder chose to fold a hardening into v1.6.0: defender now defends until the client CONSUMES the identity (attach + grace) capped at ~120s, attach-fail no longer disposes. The full Bloxstrap-style install *deferral* is its own future cycle (the multilaunch-during-install edge remains).

**Item 10 (security pass + docs):** app-wide cookie audit — 7/8 PASS; 2 findings fixed (FriendFollowWindow held the cookie as a class field → per-call retrieval; 2 test stubs interpolated the fake cookie into exception messages). PRIVACY.md corrected for the deliberate-export reality + new export/import section. `*.rororo-accounts` gitignored. Deps clean, no local paths.

**Next-cycle backlog surfaced this run:** "Roblox install/bootstrapper interruption" — Bloxstrap-style install suppression/deferral + the multilaunch-during-install identity edge. This is the clan's recurring "black installer" pain.

**Session/friction loggers:** Cart plugin data dir still absent (5th cycle).

**Handoff:** branch PR-ready. Per the release-workflow memory, I drive the Store MSIX + sideload + reviewer letter + GitHub release; builder's only step is the Partner Center submit click.

## /checklist — autonomous run (2026-05-21, cycle v1.7.0 install-deferral + launch-lane reliability)

Spec-first. Canonical spec `docs/superpowers/specs/2026-05-21-rororo-install-deferral-design.md`, synthesized from two investigation docs this session: the Bloxstrap update-deferral mechanism study + a vibe-iterate launch-lane slate (low-cost riders). Scope locked by builder.

**Cycle shape (credibility lane):** rebuild Bloxstrap's "update once, then launch the batch" at RoRoRo's layer WITHOUT bootstrapper takeover (posture: documented endpoints, no handler takeover). Core: update-pending detection (RobloxPlayerInstaller.exe process + version pre-check reusing RobloxCompatChecker — no spike) → pre-warm batch launch → version pre-check skip → updating-UX. Riders folded from the iterate slate (all ride the same install-detection signal): install-aware ProcessAttachFailed messaging, install-aware tracker attach-timeout (lockstep with the v1.6.0 defender's 120s), strap-aware skip (BloxstrapDetector + Fishstrap).

**Sequencing:** detection signal first (item 1 — everything consumes it), then strap-detect (2) + tracker-timeout (3), then the pre-warm gate (4, the core), updating-UX (5, C1), attach-fail messaging (6), docs/security (7). 7 items, ~4-6h.

**Iterate-pass result worth noting:** the slate retired the cycle's only spike (version-GUID read already exists via RobloxCompatChecker) and produced two evidence-backed NON-findings (launch path already MessageBox-free; RobloxAlreadyRunning modal already hard-blocks correctly) — so no busywork there. Scope-creep kept out: log retention, Studio bootstrap, Fishstrap static-dir/channel (the takeover wall).

**Handoff:** Run `/build`. Autonomous; C1 after item 5.

## /build — autonomous run (2026-05-21, cycle v1.7.0 install-deferral + launch-lane reliability)

All 7 items complete on branch `v1.7.0-install-deferral`. Final: `dotnet build ROROROblox.slnx` 0 errors, **575 unit + 5 harness (1 skipped) green** (was 519 at cycle start; +56). Each item dispatched to a subagent.

**Items 1-3 (foundation):** `RobloxUpdateProbe` (1 — IsInstallerRunning + degrade-safe IsUpdatePendingAsync; corrected the slate's assumption — GetInstalledRobloxVersion returns the FileVersion, so compare the CDN `version` field not the GUID). Strap-aware detection (2 — Fishstrap added + `IsStrapHandlingLaunches()`, distinct from the Bloxstrap-only FFlag-banner check). Install-aware tracker timeout (3 — extends 30s→120s while the installer runs, lockstep with the v1.6.0 defender).

**Item 4 (core):** pre-warm gate — pure `PreWarmGate.Decide` + `PreWarmWaitComplete` (12 tests), `DispatchBatchAsync` orchestration, DI-wired probe + detector. No-update / strap paths unchanged (normal speed); only the update-pending path holds the batch behind one update. `RobloxUpdating` flag left as the item-5 seam.

**Item 5 (C1):** branded "Roblox is updating — hold on" banner bound to `RobloxUpdating`. C1 verification caveat surfaced honestly — the pre-warm LOGIC is unit-tested, but the live banner/deferral can't be triggered on demand (needs a real pending Roblox update); builder accepted finishing the cycle + real-smoking later.

**Item 6 (rider):** install-aware `ProcessAttachFailed` messaging via `PreWarmGate.AttachFailedMessage` — installer running → "Roblox is updating" instead of the scary "check antivirus" copy.

**Item 7 (docs/security):** spec marked implemented; 2 dashboard decisions logged (install-deferral-at-our-layer rationale + the two new degrade-safe Roblox-side compat dependencies: `RobloxPlayerInstaller.exe` name + `clientsettingscdn.roblox.com/v2/client-version` endpoint); no local paths; no vulnerable deps; secret-scan clean.

**Iterate-pass payoff (this lane):** the vibe-iterate slate retired the only spike (version read already in RobloxCompatChecker) and caught 2 non-findings (launch path already MessageBox-free; RobloxAlreadyRunning already hard-blocks) — zero busywork. Scope-creep kept out: log retention, Studio bootstrap, Fishstrap static-dir (the takeover wall).

**Open at cycle end:** real-smoke the banner/deferral at the next actual Roblox update. The full Bloxstrap-style install *deferral/suppression* (and the multilaunch-during-install identity edge) remains a future cycle — this cycle handles the *interruption* at our layer, not Roblox's update cadence itself.

**Handoff:** branch PR-ready. I drive the release (version 1.7.0.0, Store MSIX + sideload + reviewer letter + GitHub release); builder's only step is the Partner Center submit.

## /scope — flatline as a built-in theme (2026-08-10)

Cart cycle entered after PR #102 (the UIA capture tool) merged. Este's process ruling this session:
route spec work through Vibe Cartographer rather than superpowers brainstorming, because the capture
tool's defects were nearly all authored in the plan document rather than in implementation.

**Wrong turn worth recording:** I first invoked `/iterate`, reading it as the compressed loop for an
established app. It is a hackathon polish pass over already-built code. `/scope` is the entry for a
feature cycle here, since `docs/checklist.md` is cycle-shaped and overwritten each round.

**Autonomy:** profile is `fully-autonomous`, pacing brisk, persona Architect. Flowed through every
beat the record answered. One genuine fork was escalated, correctly per the contract's "confirmations
exist for genuine forks the record can't resolve."

**The fork, and how it moved the cycle.** flatline arrived as an adversarial QA instrument whose job
was to collapse colour distinction so colour-only signalling failed measurably. Shipping it
user-selectable makes it a product, and a product needs a reason to exist in the picker. I put the
tension to Este directly: product theme (accessible, legibility-maximising, passes the gate) versus
instrument-that-ships (stays adversarial, preserves the findings' evidence), and named the cost —
a flatline that passes every contrast check has stopped demonstrating F-032's 1.00:1.

He ruled **product theme**. That changed the design goal from "collapse distinction" to "carry
distinction without colour," which is a materially different theme and a materially larger cycle: the
real work is now non-colour redundancy for every status the app currently says in hue alone.

**Consequence I scoped in rather than buried:** the ruling strands F-031, F-032 and F-050's evidence.
Resolved with a `flatline-lab` test fixture, not a built-in and not user-selectable, which preserves
those numbers AND earns its place by being fed to the contrast gate to prove it FAILS. A gate that has
only ever seen passing themes is unproven. Better outcome than either horn of the original fork.

**Crux resolved analytically rather than by discussion.** The feared conflict — an adversarial theme
reddening `ContrastPairGateTests` — does not survive reading what the gate measures. It measures
foreground against its own fill; flatline collapses distinction BETWEEN semantic elements. Orthogonal.
One real constraint remains: the single accent is a fill with white text on it, so white-on-accent must
clear the F-050 exemption floor of 3.20 and should target 4.5:1 outright. Explicitly rejected: any
notion of a theme exempt from measurement, which is where a real regression would hide.

**Active shaping:** Este drove the decisive call in four words and it was the right one. He did not
re-litigate the crux analysis or the cut list, consistent with the profile's zero-deepening-rounds
habit when the analysis is clean.

**Handoff:** `/prd`.

## /prd — flatline requirements (2026-08-10)

Zero deepening rounds, consistent with the profile's habit when the upstream analysis is clean. The
`/scope` crux was already resolved analytically, so there was nothing to sharpen by asking; the value
this step added came from recon instead.

**What changed versus the scope doc.** Scope described the redundancy work as "enumerate the affected
surfaces, the register's colour-only findings are the starting list." Reading the tree first turned up
a category the register does not cover and scope did not anticipate: two WPF converters hardcode their
status colours in C#. `StatusDotBrushConverter` holds four RGB literals and `IdleChipBrushConverter`
holds two (`Converters.cs:169-218`). `ThemeService.ApplyTo` cannot reach either. Under flatline they
paint brand green, amber and magenta onto a monochrome field, which reads as half-painted rather than
flat, and lands squarely against the "won't ship a broken-looking tile" bar. That became Epic 3 and it
sequences AHEAD of the redundancy work in the build order, because a status dot still glowing brand
green makes every redundancy screenshot useless as evidence.

**Second find, smaller but load-bearing.** Two test files carry flatline claims in prose that go false
on merge. `ContrastPairGateTests`' class doc says flatline "was never committed as a ThemeStore entry"
and the gate "cannot reproduce those numbers" (`:36-45`); `MutedTextFenceTests` cites "1.00:1 under
flatline" (`:10-13`), a number that belongs to `flatline-lab` after this cycle. Scope's register
reconciliation covered three rows in a markdown table and missed both. Same defect class, different
file type. Folded into Epic 5.

**A scope worry closed rather than carried.** Scope flagged that a one-accent theme might trip the
edge-remediation prompt on first selection, calling it a bad first impression. It cannot:
`EdgeRemediation.Decide` returns `DeriveSilently` for any built-in (`EdgeRemediation.cs:45`). Kept as
an acceptance criterion anyway, phrased as verify-on-screen rather than verify-on-paper, because
"the code says it can't happen" is how it happens.

**New non-goal, surfaced here.** No theme-conditional UI. No `if (theme == flatline)` branch anywhere.
A redundancy that only appears in one theme is a costume, and it would make the four capture rounds
disagree with each other by design. This is the constraint most likely to be violated under build
pressure, which is why it is a numbered non-goal rather than a note.

**Scope guard.** Two temptations named and pushed to "with more time": extending the contrast gate to
cover converter-supplied brushes (Phase 2 gate work with its own design), and a theme-level "this
theme is monochrome" declaration (real, premature before one such theme exists).

**Left open deliberately.** Display name still defaults to "Flatline" with a recommendation to keep
it; scope routed the call here and the id stays `flatline` regardless, so it blocks nothing. Where
Story 1.3's one-sentence description lives is a genuine `/spec` fork, constrained by the codebase's
stated invariant that the theme contract does not grow.

**Session logging:** appended directly to the Cart session log rather than through the runtime, which
is not wired in this environment.

**Handoff:** `/spec`.

## /spec — flatline technical blueprint (2026-08-10)

Zero deepening rounds, fifth consecutive translation command with none. This one earned it
differently from the others though: the value did not come from asking, it came from **measuring**.
The PRD routed two genuine forks and one "resolved by measurement" item here, and all three were
settled against the running code rather than by argument.

**The method is the point, and it is written into the spec.** Every ratio in `docs/spec.md` was
produced by resolving candidate palettes through the app's own `ThemeService.ApplyTo` into a real
`ResourceDictionary` and measuring with `ContrastGuard.RatioBetween`, against the pair list scanned
live out of the XAML. That ran inside the test project as a temporary xUnit fact, deleted after the
numbers were recorded; the tree is clean. Writing a spec full of hand arithmetic for a cycle whose
whole subject is three findings that quote unverifiable numbers would have been repeating the defect
while describing it.

The method validated itself before being trusted: it reproduces brand's 3.79:1, midnight's 4.16:1
and magenta-heat's 3.29:1 on F-050's pair, F-031's 1.26:1, F-032's 2.42:1, and the
`#1F3149 -> #5E6B7C` derived edge that `Theme.cs:52` records. Six numbers, three files, all matched
before a single new value was proposed.

**Fork 1 — the description sentence. Resolved: App-layer lookup, no eleventh slot.** The codebase
argues its own case here; `ContrastGuard.cs:15-23` already explains why an eleventh `Theme` slot
breaks every user theme on disk. A tooltip was rejected on two grounds: hover is not "focus", and it
risks the `Id = <id>,` substring the capture tool reads out of the picker's UIA name, which Epic 6
depends on.

**Fork 2 — the converters. Resolved: delete both, use Style + DataTrigger.** The obvious answer,
"teach the converter to read `Application.Current.Resources`", is wrong and quietly so.
`IValueConverter.Convert` re-runs on binding-source change, not on resource-dictionary change, and
`ApplySlot` *replaces* the brush instance rather than mutating it, so a converter-fetched brush goes
stale the moment the theme changes. It would have looked correct in review and failed the live
repaint Story 1.1 requires. `{DynamicResource}` inside a `DataTrigger` setter is the fix, and
`MainWindow.xaml` already uses that idiom twice.

**A design call scope did not anticipate: flatline is a ramp, not a flat surface.** Scope's
implementation note had `Bg`, `Navy` and `RowBg` collapsing toward one value. That would reproduce
F-002's own defect (cards vanish, 1.00:1) while calling itself an accessibility fix. CVD affects hue
discrimination, not luminance, so the honest reading of "carries no meaning in colour" is achromatic
*ramp*. Shipped flatline separates rows from the page at 1.33:1 against brand's 1.09:1 — the theme
ends up better at the thing the register faults the default for.

**A claim I nearly shipped, disproved by enumeration.** I was about to write that a single accent
value cannot serve both `NavyBrush on CyanBrush` (22 sites) and `WhiteBrush on MagentaBrush` (8) at
AA. False — 26 solutions exist. The real constraint is sharper and worth more: every one of them
forces a page at `#040404` or darker and caps RowBg-vs-Bg separation at 1.024:1. So a single accent
does not fail the gate, it forces the theme to reproduce F-002. That is the argument in the spec,
and it is enumerated rather than asserted. Two accent lightnesses, both achromatic.

**Third stale in-code claim, found by measurement and not in scope or the PRD.**
`ContrastPairGateTests` says "9 distinct pairs" in three places. The app ships 8. Verified against
two commits rather than assumed: the scan returns 44/9 at `1fcf74d` (where the gate was authored)
and 44/8 at HEAD, because `2c9ab16` — the F-032 fix — rebound three `MutedTextBrush` foregrounds to
`WhiteBrush` and merged a pair. Nothing fails, `MinimumPairs` is 6. The consequence underneath it is
the real find: **the gate can no longer see `MutedTextBrush` at all**, because its own fix removed
the only declared pair using that token. Flatline's muted values are measured in the spec and
asserted nowhere. That is a register row, and it is the kind of blind spot that only shows up if you
run the scan instead of reading it.

**Fixture reconstruction turned out to be self-verifying.** The original flatline JSON is gone —
themes folder empty, no flatline captures survive — so `flatline-lab` was reconstructed from the
ratios the register records. F-031's 4.34:1 and F-050's 2.99:1 were recorded in separate findings;
they multiply to 12.98, and the reconstructed fixture's White-vs-Navy measures exactly 12.98:1.
Numbers written months apart are mutually consistent with one achromatic one-accent theme, which
means the reconstruction is faithful rather than invented. The fixture puts 4 pairs below AA and
drops the exempted pair below its 3.20 floor, so it trips both branches of the gate. It can go red.

**Scope guard held.** Two things were named and pushed out rather than absorbed: extending the gate
to style-resolved brushes (Phase 2, own design) and F-050's actual fix. F-050 explicitly stays open
— closing it auto-deletes the exemption via `NoExemptionOutlivesItsFinding` and turns brand and
magenta-heat red. One new out-of-scope find recorded rather than fixed: the Bloxstrap banner's
literal `#3F3000`/`#8F7000`, which belongs with F-068.

**Cost named rather than buried.** Mapping active status to `WhiteBrush` changes brand's active dot
from green to white. That is a visible change to the default theme, not just to flatline, and it
wants eyes on the brand capture at a checkpoint. Cyan was the tempting alternative and was rejected
on measurement: it collides with `RowExpiredAccent` at 1.00:1 under flatline.

**Artifact shape changed this cycle.** `docs/spec.md` is the canonical technical spec rather than a
pointer-stub, per Este's ruling that Cart drives spec work here. The prior cycles' spec index is
preserved as an appendix so the chain is not erased.

**Session/friction loggers:** Cart's plugin data dir still absent on this machine. Nth cycle
confirming it. Durable record is this file.

**Handoff:** `/checklist`. Predicted shape: 8-9 items. Item 1 is the palette + built-in record
(everything depends on the theme existing), item 2 is the converter deletion (ahead of redundancy,
because a brand-green dot makes every flatline screenshot useless as evidence), then redundancy,
then `flatline-lab` in parallel, then reconciliation, then the capture round last as the verification
step. Two checkpoints worth placing: after the converter work (brand captures, not just flatline —
the default theme changes) and after redundancy.

## /checklist — flatline build sequence (2026-08-10)

Zero deepening rounds, sixth consecutive translation command with none. **8 items**, inside the
predicted 8-9. Autonomous-with-verification, two checkpoints, commit after each item on the existing
`feat/flatline-theme` branch. Effort ≈ 6-7 hours, no spike — the palette was proven by measurement at
`/spec` time, so there is no Roblox-side or arithmetic gate left to clear before code.

**The value this command added was recon, not sequencing.** The sequence was already settled by the
`/spec` handoff and survived contact unchanged. What did not survive was the spec's own site list.

**Spec drift caught pre-build, corrected inline.** `spec.md > §5.1` listed two `IdleChipBrushConverter`
binding sites. A grep of the tree returns three: `MainWindow.xaml:78` is the **compact-mode** row's
memory chip, bound to `MemoryWarning` exactly as `:433` is in the standard row. Four binding sites
total. Missing it would have shipped brand amber on an achromatic field in the one row template the
capture round is least likely to have open — the half-painted defect Story 3.1 exists to kill, hiding
in the mode nobody screenshots. Also unlisted, and each one a build break rather than a cosmetic miss:
`App.xaml:23-24` declares both converters as `StaticResource` keys (a resource entry naming a deleted
type fails the build), the whole of `ConvertersTests.cs` asserts `IdleChipBrushConverter`'s literal
RGB, `AccountSummary.cs:268` carries a `<see cref="IdleChipBrushConverter"/>`, and
`MainWindow.xaml:425-426` names the converter in a comment that goes false on merge. Fixed inline in
`spec.md` §5.1 and §13 per the repo's pre-build-catch precedent (the 2026-05-03 spike outcome
established it: banner-correct is for post-build divergence, pre-build catches go inline), and named
again inside checklist item 3 so `/build` cannot miss them.

**Sequencing rationale, dependency-first:**

- **Item 1 is the theme record** because everything downstream needs flatline to exist as something
  you can select. It is also self-gating: `ContrastPairGateTests` enrols any `IsBuiltIn` theme
  automatically, so a wrong hex reddens the build immediately rather than at review.
- **Item 2 (description line) sits second and small.** Different project, different concern
  (presentation copy, App layer), and it must land before item 7 because it touches the picker item
  whose UIA name `capture-ui.ps1` reads. If item 7 ever needs a script edit, item 2 broke the
  `Id = <id>,` substring — that is the diagnosis, written into item 7 so nobody patches the script.
- **Item 3 (converters) ahead of item 4 (redundancy)**, carried straight from `/prd` and `/spec`. A
  status dot still glowing brand green makes every redundancy screenshot useless as evidence.
- **Item 5 (`flatline-lab`) is genuinely parallel** to 3 and 4 — test project only, no App dependency.
  Placed after them rather than before because it is the least likely to reveal something that
  reshapes the others.
- **Item 6 (reconciliation) after 5**, because it cites `flatline-lab` by file and the file has to
  exist to be cited.
- **Item 7 (capture round) last of the build items** because it verifies 1 through 4.

**Two checkpoints, and C1 is the unusual one.** C1 lands after item 3 rather than at a natural
"first runnable" boundary because item 3 changes the **default** theme: mapping active status to
`WhiteBrush` shifts brand's active dot from green `#4FE08C` to white. Cyan was the tempting
alternative and collides with `RowExpiredAccent` at 1.00:1 under flatline, so the trade is correct,
but a visible change to the product's identity theme wants a human yes before three more items build
on top of it. C2 is the evidence gate at item 7 — the full `spec.md > §11.3` manual list plus eyes on
56 PNGs, including the brand round.

**Deliberately not split:** item 3 is the heaviest by a distance (four binding sites, four collateral
files, a new fence test) and carries a 90-minute split flag rather than a pre-emptive 3a/3b. Splitting
it would put the deletion and the fence in separate commits, and the fence's whole assertion is that
the deletion happened — half-done is the state it exists to prevent.

**Deliberately not its own item:** the `spec.md > §11.3` manual smoke. Its eight steps distribute
naturally into the Verify fields of items 1, 3, 4 and 7 where they actually gate something, and the
full list re-runs at C2. Prior cycles collapsed the same standalone-smoke item for the same reason.

**Risk callouts for `/build`:**

- **The four collateral files in item 3 land in the same commit or the build breaks.** `App.xaml` is
  the sharp one — a `StaticResource` declaration naming a deleted type is a build failure, not a
  warning, and it is not in the same file as anything else being edited.
- **`IdleWarn`'s setter must raise `OnPropertyChanged(nameof(IdleText))`** (item 4b). `IdleText` is
  recomputed on `SinceActivity` change but not on `IdleWarn` change, so the `▲` lands a tick late and
  looks perfectly fine on a slow-moving row. This is the miss that ships.
- **F-050 stays `open`** (item 6). `NoExemptionOutlivesItsFinding` deletes the gate's exemption the
  moment that row stops being open, which tightens the gate on brand (3.79:1) and magenta-heat
  (3.29:1) and turns both red. Flipping it casually breaks the build for a reason that will not look
  connected to the flip.
- **No `if (theme == flatline)` anywhere** (item 4). Numbered non-goal 6, and the constraint most
  likely to be violated under build pressure because it is always the cheapest local fix.
- **Item 3's fence is a fence, not a gate.** `ContrastPairGateTests` structurally cannot see a
  `DataTrigger` setter, before or after. An unmeasured fix is acceptable; an unmeasured fix described
  as gated is not, and the checklist says so in the item.

**Spec coverage matrix:**

| Spec section | Checklist item(s) |
|---|---|
| §1 Stack | All items (no new dependencies reaffirmed) |
| §2 Runtime / identity / signing | Item 8 (version lockstep 1.16.0.0 → 1.17.0.0) |
| §3 Architecture — governed vs escaped paths | Items 1, 3 (context for both) |
| §4.1-4.4 Palette, design rules, measurement, no-prompt | Item 1 |
| §4.5 Description sentence | Item 2 |
| §5.1-5.4 Converters, mapping, fence | Item 3 |
| §6.1-6.4 Redundancy + verified-clean | Item 4 |
| §7 Out of scope | Item 3 (the allow-list *is* §7's set) + item 6 (new Bloxstrap row) |
| §8.1-8.5 flatline-lab | Item 5 |
| §9 Fourth capture round | Item 7 |
| §10.1-10.3 Register + in-code reconciliation | Item 6 |
| §11.1 Measurement method | Items 1, 5 (both re-run it) |
| §11.2 Automated | Items 1, 3, 4, 5, 6 |
| §11.3 Manual | Items 1, 3, 4, 7 (distributed) + C2 (full list) |
| §12 Data model | Item 2 (the contract does not grow — that is the item's constraint) |
| §13 File structure | All items |
| §14 Data flow | Items 1, 3 |
| §15 Key technical decisions | Item 8 (all six log to the dashboard) |
| §16 Open issues | Item 8 (carried forward, not dropped) |

Every numbered section maps to at least one item. PRD coverage is complete too: Epic 1 → items 1-2,
Epic 2 → item 4, Epic 3 → items 3 + 6, Epic 4 → item 5, Epic 5 → item 6, Epic 6 → item 7.

**Active shaping:** none this run — no interactive beats, autonomous contract. The one place the
record could not answer was whether to split item 3, and the split flag defers that to wall-clock at
`/build` rather than guessing now.

**Session/friction loggers:** Cart's plugin data dir still absent on this machine. Nth cycle
confirming it. Durable record is this file.

**Handoff:** `/build`. Autonomous through the checklist, halting at C1 (after item 3) and C2 (after
item 7).

---

## /build

Autonomous. **Ten items shipped against eight planned**, nine commits, one checkpoint answered and
one still owed. Suite went 1391 → 1411 with no regression at any step. Build held at 31 warnings /
0 errors throughout, all pre-existing.

### The cycle's actual shape

Items 1-8 ran as `/checklist` sequenced them. **3a and 3b were added mid-build**, both from defects
`spec.md` never enumerated, both approved by Este rather than deferred. That is the story of this
cycle and it is worth stating plainly rather than as a footnote:

`prd.md > Story 3.1` claims *"switching to Flatline leaves no brand hue on the main window."* That
claim was falsified twice **after** the item meant to satisfy it had shipped and been signed off.

- Item 3 rebound the four status-colour sites `spec.md > §5.3` enumerated. Signed off at **C1**.
- Item 6's register pass found **F-088** — a fifth site, the status bar's live-process dot, two
  literals nested in a `Setter.Value`, shipped with F-080 in PR #96 and present on `main`. Both of
  item 3's fences were structurally blind to it: one walks `*.cs` and this is XAML, the other reads
  `Background=` / `Foreground=` attributes and this is a literal in a style setter.
- Item 3a fixed it and added a third fence fact scanning App **XAML** under one rule: **a literal is
  permitted only when an OPEN register row already owns it**, each allow-list entry citing the id
  inline.
- That fence's own first run found **F-089** — four un-themed hexes in `SelectionDotStyle`, on every
  account row, ring always drawn. The same claim, false a second time.
- Item 3b fixed it, retired F-089's allow-list entry **before** the fix so the fence went red on all
  four hexes first, and dropped the ceiling 101 → 97.

**The lesson, and it generalises past this repo.** Enumerating defect sites by reading a spec finds
what the spec's author saw. Sites five and six were found by a mechanical scan whose rule forces
every exception to name an open finding — and site six was found by the fence written to fix site
five. The fence is a better artifact than either fix. It is also the thing that lets the cycle claim
completeness honestly: the literal inventory is now measured at 97 occurrences, every one attributed
to a finding or to `App.xaml`'s seed dictionary, rather than asserted.

### Checkpoints

**C1 (after item 3) — answered.** Brand's active dot green `#4FE08C` → white. Approved. The four
picker sentences were reviewed in the same beat and shipped as drafted, so `spec.md > §16`'s "copy
polish at `/build`" is resolved rather than open. Cyan was the tempting mapping and is wrong on
measurement: under flatline `CyanBrush` and `RowExpiredAccentBrush` are the same value, landing
active and expired at 1.00:1.

**C2 (after item 7) — owed.** The script half ran: **56 of 56 captures, 0 failed**,
`run-flatline.json` beside the other three. All four rounds were re-shot rather than only flatline,
so the brand captures reflect item 3's change. The eyes-on half is a human's and has not happened.

### What the agent could and could not verify

Verified in pixels from the capture round: item 3a's status-bar dot renders grey under flatline;
item 2's sentence renders and wraps under the picker; brand is structurally identical to flatline
with hue intact. Everything the theme governs is achromatic under flatline.

Not verified, and not claimed: **no test in this project loads a `Window`.** A green suite is not
evidence that anything renders. `spec.md > §11.3`'s eight manual beats are all owed.

**Observation for a later cycle, not a defect against this one.** The flatline captures still show
coloured avatar rings and caption swatches. `spec.md > §7` scopes those out as per-account identity
paint, arguing identity is not colour-only because the account name is right there, and that holds.
But `scope.md` opened this cycle by listing "the coloured caption swatches" as a colour-only signal,
so a clan member picking flatline for colour-vision reasons still sees colour. Worth a row.

### Verification defect found in this checklist

Every `Verify:` field shipped with `--filter "ThemeStore*|ContrastPairGate*"`. VSTest's filter
grammar has no glob wildcards: that expression matches **zero tests** and the run reports success.
A checkpoint could have been signed off on nothing having executed. Corrected to
`FullyQualifiedName~` form in three places and each one run. **This will recur in the next
Cart-authored checklist unless the template changes** — flagging for `/evolve`.

### Repo-hygiene findings, all pre-existing, none fixed here

- **The pre-commit hooks are not installed on this checkout.** `.git/hooks/pre-commit` is absent, so
  the secret-scan and local-path guards never fired on any of this cycle's nine commits. Both were
  run manually against the staged set and came back clean, so the commits are sound — but the
  protection was off the whole time. `.claude/hooks/install.ps1` is the fix. Same failure class as
  the `--filter` defect above: a guard that reports nothing because it never ran.
- **`docs/features.md` was a release behind**, not merely missing v1.17. v1.16.0.0 was tagged and
  published 2026-08-06 and never got a ledger row. Both rows added.
- **`.gitnexus/` is 74 MB tracked** and `meta.json` holds a hardcoded machine path.
- **The per-account identity palette lives in three hand-synced copies** in three encodings, and no
  comment names all three.

### Register

**34 clean · 54 open · 1 closed-as-ruled · 89 total.** The cycle opened F-085, F-086, F-087, F-088,
F-089 and closed two of them (F-088, F-089) in the same session it opened them. **F-050 stays
`open`** and was re-verified byte-identical after every register edit — `NoExemptionOutlivesItsFinding`
reads its last pipe-delimited cell and would auto-delete the gate's exemption, reddening brand
(3.79:1) and magenta-heat (3.29:1), for a reason that would not look connected to the flip.

### Sharpest result

`flatline-lab` reproduced all six recorded register ratios **exact to four decimals on first run**,
with no hex tuning, and the cross-check held: the fixture's `WhiteBrush` vs `NavyBrush` measures
12.9831:1, which is F-031's 4.34 × F-050's 2.99. Two ratios recorded months apart in separate
findings are mutually consistent with a single reconstructed theme. The register had been telling the
truth for months; nothing had ever re-derived it.

**Handoff:** C2, then `/iterate` or `/reflect`.

---

# Process Notes — v1.18 cycle (Settings becomes a place)

## /scope

**Zero deepening rounds, and this cycle earns that more than any prior one.** The habit is
"zero when the spec is clean"; here the recon was not merely clean, it was *measured hours earlier*.
All 51 open register rows were re-verified against the tree before scoping, with per-row evidence
committed to `docs/superpowers/research/2026-08-10-register-reverification/`. There was no discovery
left for an interview to do.

**The interview beats were flowed rather than asked**, per the fully-autonomous contract in the guide
SKILL. What the mandatory questions would have produced was already on disk:

- *Brain dump* — the register plus four batch reports.
- *Research and reaction* — the re-verification IS the research. A web search for inspiring examples
  would have been noise on a remediation cycle against an existing app.
- *Sharpen the gaps* — the genuine gaps are design forks, not unknowns. They are in
  "Assumptions surfaced" with defaults chosen and marked for confirmation.
- *What's NOT in scope* — the builder set three hard exclusions before scope opened.

**The cluster was chosen from data, not instinct.** The 51 open rows were bucketed by fix affinity:
Preferences/Settings 13, accessible naming 9, copy 8, dialog vocabulary 5, button/banner 3, tail 13.
Settings won on coherence per row, not on count alone — it is one surface with one story, and the
re-verification had already shown several of its rows to be far cheaper than rated.

**Two rows were kept with their edges named rather than smuggled in.** F-037 is a nine-window sweep
and F-046 spans four surfaces and touches F-068's shared-style territory. Both are in, both are
flagged in scope.md with an explicit instruction to `/spec` about where F-046's line has to hold. The
alternative — quietly counting them as Preferences rows — is how a 13-row cycle becomes a 61-site one.

**Active shaping:** the builder set the cycle's three exclusions unprompted (F-050, F-091, F-068),
picked the cluster from four offered shapes, and had already made the two strategic calls this scope
rests on — defer the Store, publish v1.17 as a pre-release. The agent's contribution was the
bucketing and the assumption defaults.

**One thing caught in the seam.** `docs/spec.md` is overwritten every Cart round. Every prior cycle
compressed it to a pointer-stub with the real design under `docs/superpowers/specs/`, so overwriting
cost nothing — but v1.17 was the first cycle where Cart authored the design in `spec.md` directly,
making it canonical and the next round's overwrite destructive. Archived as
`2026-08-10-rororo-flatline-theme-design.md` before starting, and `CLAUDE.md`'s file table corrected
to state the lifecycle and the archive obligation. This was only cheap to notice in the one moment
between cycles.

**Handoff:** `/prd`.

## /prd

**Zero deepening rounds again, same justification.** The re-verification supplied what a PRD
interview normally extracts: current behaviour with file citations, counts with direction, and fix
directions already written per row. The beats were flowed rather than asked.

**Five epics from the scope's five groups**, one-to-one. Grouping held under expansion, which is
usually the test of whether a cluster was real or convenient.

**Three things sharpened that the scope left soft:**

1. **Epic ordering is now an open question rather than an assumption.** Epic 1 adds four memory
   controls to the Alerts page; Epic 2 fixes the grouping defect on that same page. Doing them in
   that order means building on a structure you are about to change. Doing them inverted means
   designing hierarchy against a page about to gain a card. `/spec` picks; the PRD refuses to guess.
2. **Epic 4.3's boundary is written into the story, not the preamble.** "Assign by consequence" is
   unbounded on its face and F-068 is 61 sites away. The acceptance criteria say the story defines
   the variant and applies it on this cycle's surfaces, and that it drops to F-068's cycle if the
   line cannot hold.
3. **Story 2.2 forces a statement about accessible naming.** F-052 is not in this cycle and 0 of 137
   declarations carry a name. Naming Preferences alone is defensible; doing it silently is the thing
   that makes a register row wrong later.

**One acceptance criterion added that no row asked for.** Story 1.1 requires out-of-range input to be
refused visibly. Every row in this cluster is a variant of "the app knows something and does not say
it", and a megabyte field that silently accepts a negative number is that defect one level down.

**One "what we'd add" that came out of the diagnosis rather than the list:** a settings-schema test
that fails the build when a persisted field has no UI. F-023 existed for months because nothing
connected "persisted" to "reachable" — the row is a symptom of a missing check.

**Handoff:** `/spec`.
