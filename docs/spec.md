# ROROROblox — Technical Spec (pointer stub)

Spec-first Cart cycles. Active cycle's canonical spec lives upstream:

→ [docs/superpowers/specs/2026-05-08-persist-roblox-user-id-design.md](superpowers/specs/2026-05-08-persist-roblox-user-id-design.md)

Cycle history (each cycle's canonical spec is its own durable artifact):

- v1.1 core (multi-instance + accounts + distribution): [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md)
- v1.2 per-account FPS limiter: [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md)
- v1.3.x default-game widget + local rename overlay: [`2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md) (shipped 2026-05-08 via PR #3)
- v1.3.x save-pasted-links: [`2026-05-08-save-pasted-links-design.md`](superpowers/specs/2026-05-08-save-pasted-links-design.md) (shipped 2026-05-08 via PR #5)
- v1.3.x detect-Roblox-already-running + cookie-container fix (cycle 4): [`2026-05-08-roblox-already-running-detect-design.md`](superpowers/specs/2026-05-08-roblox-already-running-detect-design.md) (shipped 2026-05-08 via PR #6, banner-corrected — actual fix was the cookie-container leak; gate is defense-in-depth)
- v1.3.x persist `RobloxUserId` (cycle 5): this cycle

## Section index (for checklist references — current cycle)

- §1 Overview (`RobloxUserId` in-memory only today; persist + eager-backfill so follow works after restart without re-adding accounts)
- §2 Goals and non-goals (persist + opportunistic + eager backfill; no UI surface; no re-resolution; friends-list display drift is a separate cycle)
- §3 Stack (no new dependencies — reuses `IAccountStore`, `IRobloxApi.GetUserProfileAsync`, `App.RunStartupChecksAsync`)
- §4 Architecture and change surface
  - §4.1 `Account.cs` — add `long? RobloxUserId = null` field
  - §4.2 `IAccountStore.UpdateRobloxUserIdAsync(Guid, long)` — granular write
  - §4.3 `MainViewModel.cs` — persist on 3 existing opportunistic-resolution sites (`:543`, `:617`, `:888`)
  - §4.4 `AccountUserIdBackfillService` (NEW) — fire-and-forget eager pass with 2-3s stagger
  - §4.5 Migration — none (JSON serializer reads existing blobs with `null`)
  - §4.6 Follow path — zero code changes (graceful-fail at `:1251` starts succeeding)
- §5 Anti-fraud / rate-limiting discipline (the v1.1.2.0 lesson; how cycle 5 diverges; build-time endpoint-spike candidates)
- §6 Soft-fail discipline (failures NEVER surface; multi-instance launch NEVER blocks)
- §7 Testing (~12-15 unit cases — round-trip, granular write, opportunistic-persist, orchestrator)
- §8 Branch + commit plan (5 commits on `feat/persist-roblox-user-id`)
- §9 Out of scope (deliberate)
- §10 Open questions / future (endpoint trip-wire, cookie payload introspection, generic backfill queue)
- §11 Decisions to log on completion (3 dashboard entries — architecture, rate-limiting, schema migration)

## What's deliberately not in this cycle

- UI surface for backfill progress (silent by design — the value is "next session, follow just works")
- Re-resolution for already-persisted userIds (Roblox account renames are rare and `displayName` is not load-bearing for follow routing)
- Friends-list display drift fix — names + avatars not rendering due to suspected Roblox-side API field rename. Independent root cause, parallel cycle 5.5 candidate
- Aggregate friends sheet, recent-games trail, live "what game is this alt in" — cycle 6+ candidates that build on top of cycle 5
- Endpoint replacement for `GetUserProfileAsync` — listed as build-time spike if the v1.1.2.0 anti-fraud trip-wire still fires for the staggered version

When build reality drifts from the canonical spec — banner-correct at the top of the canonical spec doc per pattern v from Vibe Thesis (per `CLAUDE.md` "Don't rewrite the canonical spec on drift" rule).
