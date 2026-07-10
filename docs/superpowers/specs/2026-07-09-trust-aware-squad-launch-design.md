# RORORO — trust-aware squad launch design

---
**Date:** 2026-07-09
**Status:** Approved-shape (from the 2026-07-09 CAPTCHA diagnosis conversation) — ready for implementation plan
**Author:** The Architect + Este
**Scope:** Stop Roblox's game-join CAPTCHA from breaking squad launches: route challenge-prone accounts into the server via **friend-follow** (the gate Roblox always admits), and offer a **careful mode** that serializes joins so no two accounts are ever mid-join simultaneously.
**Origin:** Live squad-launch failure 2026-07-09 — one account challenged (with the WRONG account's name on the challenge), diagnosed to Roblox cross-binding concurrent joins from one device. Decision log `bsvxc1ZvSP9MHMQbnHXU` carries the log-evidenced diagnosis.
**Roblox-contract dependency:** the gate hierarchy (friend-follow > private-server entry > direct join) and the concurrency cross-bind are Roblox-side behaviors. Re-verify before building if time has passed; this spec's value dies if Roblox equalizes the gates.
---

## 1. Problem & evidence

An 8-account squad launch to a private server: dispatches exactly 5s apart, RoRoRo's ticket→client binding clean 8/8 (log-verified). One account — a habitually-challenged one — got an in-client CAPTCHA **titled with the next account's name** and never entered; the user recovered it minutes later via friend-follow, which admitted it instantly.

Mechanism: the 5s `InterLaunchThrottle` separates *dispatches*, but join negotiation runs 15–45s, so several accounts are always **mid-join concurrently**. Roblox's challenge service keys its pending challenge to the newest ticket redemption from the device, cross-binding identities. We can't fix that; we can control concurrency and route around the strict gate.

Observed gate hierarchy (repeated, now log-anchored): **friend-follow always admits > private-server direct entry usually admits > public direct join most-challenged.**

## 2. Design decisions

1. **Per-account "Join via friend" flag (manual, v1).** A per-account preference — "this account gets challenged; route it via a friend when squad-launching." The user knows which accounts are challenge-prone (his three); auto-detection is deferred (§3). Persisted alongside existing per-account prefs; surfaced as a row-level toggle in the account's context/edit surface (exact placement decided at plan time against the current row menu — the row is already busy, per the batching-UX backlog item).
2. **Squad launch becomes trust-aware:**
   - **Phase 1 — direct joins:** all non-flagged accounts dispatch exactly as today (5s throttle, pre-warm first).
   - **Phase 2 — anchor confirmation:** wait until at least one Phase-1 account's presence reads **InGame** (the "anchor"). Timeout (90s) → flagged accounts fall back to direct dispatch with the same throttle (never strand them).
   - **Phase 3 — friend-follow the anchor:** each flagged account launches with `LaunchTarget.FollowFriend(anchorUserId)` — the existing, shipped launch shape — landing in the same private server instance. 5s throttle between flagged accounts too.
3. **Anchor choice:** the first Phase-1 account to reach InGame. If a flagged account isn't friends with the anchor, Roblox's follow simply lands it at home (documented best-effort behavior of follow) — acceptable v1; the UI notes "requires friendship with your squad" on the toggle. No friendship pre-verification calls in v1 (§3).
4. **Careful mode (the blunt instrument):** a Squad Launch checkbox — "Careful mode: wait for each account to land before launching the next." Serializes ALL joins: dispatch → wait for that account's presence = InGame (timeout 90s → proceed anyway) → next. Kills the concurrency window entirely; costs ~30-60s per account. Persisted as a settings preference; default OFF. Careful mode and friend-routing compose (flagged accounts still follow the anchor; waits apply between every dispatch).
5. **Presence latency is accepted:** the 25s presence poll bounds "landed" detection. Phase-2/careful-mode waits poll the existing presence state (no new Roblox calls); worst case adds one poll interval per wait. Good enough — this flow trades speed for reliability by design.
6. **Surfacing:** the existing "(n of total)" launch banner gains phase-aware copy — "waiting for [anchor] to land…", "[account] joining via [anchor]" — factual, never predictive. Toast/banner on fallback ("[account] fell back to direct join — anchor didn't land in time").

## 3. Non-goals (v1)

- **No auto-detection of challenge-prone accounts** (join-stall heuristics, challenge sniffing). Manual flag first; detection is a follow-up informed by usage.
- **No friendship pre-verification** against `friends.roblox.com` before following. Follow's own best-effort behavior is the fallback; one extra API surface avoided.
- **No changes to Launch As / Launch-multiple** (public-game batch) in v1 — same mechanism applies there in principle, but the spec's evidence is squad/PS-shaped; extend later if the clan hits it on public batches.
- **No captcha automation of any kind** — permanent wall (MaCro territory, and challenge tampering is exactly what Roblox punishes). We route around the gate; we never touch the gate.
- **No Store-listing/reviewer-letter delta:** no new endpoints (FollowFriend + presence already ship), no new capabilities.

## 4. Architecture

- **Core — per-account flag:** `JoinViaFriend` (bool, default false) on the saved-account record (same additive-defaulted-field pattern as `SavedPrivateServer.IsDefault` from #54 — every construct/copy site carries it; tolerant JSON load defaults false).
- **Core — pure planner:** `SquadLaunchPlan.Build(accounts, flags) → (directBatch, flaggedBatch)` — pure, unit-testable split + ordering (flagged last), mirroring the house pure-decision pattern (`MarketplacePlan`, `FriendSourcePlan`, `PreWarmGate`).
- **App — `SquadLaunchAsync` orchestration:** Phase 1 reuses `ReleaseBatchAsync` unchanged; Phase 2 is a poll-wait on the anchor's presence (reuse the `PreWarmGate`-style predicate + deadline shape); Phase 3 dispatches flagged accounts with `LaunchTarget.FollowFriend(anchor.UserId)` through the same throttle loop. Careful mode wraps each dispatch in the same wait-for-InGame predicate.
- **App — UI:** the per-account toggle (placement per plan), the Squad Launch careful-mode checkbox, banner copy. Theme tokens only; resource-order lesson applies.
- **Persistence:** account flag rides the accounts store (DPAPI envelope unchanged — it's a preference field, not a secret); careful-mode rides settings.json.

## 5. Edge cases

- **No non-flagged accounts in the squad** (all flagged): no anchor possible → all fall back to direct with throttle (+ banner explaining why). Careful mode still applies if on.
- **Anchor never lands (timeout):** flagged accounts fall back to direct; banner says so. Never silently skip an account.
- **Flagged account's follow lands at home** (not friends / privacy): Roblox-side best-effort — the account is running and signed in; user joins manually from there. Banner keeps the launch marked dispatched, presence shows NotInGame; no retry loop in v1.
- **Anchor leaves the game mid-phase-3:** follows target the userId; Roblox resolves their CURRENT location at follow time — a departed anchor lands followers wherever the anchor now is. Acceptable v1 risk (window is seconds); careful users use careful mode.
- **SessionLimited/expired flagged accounts:** excluded by the existing eligibility rules before planning (unchanged).
- **Squad launch with zero flagged accounts:** byte-identical behavior to today (Phase 2/3 skip when flaggedBatch is empty) — the feature is invisible until a flag is set.

## 6. Testing

- **Unit (pure):** `SquadLaunchPlan.Build` — split/order (flagged last), all-flagged → empty direct batch, zero-flagged → empty flagged batch; anchor-wait predicate (landed/timeout/no-anchor) as a pure function with injected clock, mirroring `PreWarmGate`'s tests.
- **Unit (store):** `JoinViaFriend` round-trips; legacy accounts load false; every construct site carries it (the #54 lesson, same test shape).
- **Unit (launcher):** none needed — `FollowFriend` URI building already tested/shipped.
- **Manual smoke (gates merge):** flag one challenge-prone account → squad launch → it enters via the anchor, banner narrates phases; all-flagged fallback; careful mode serializes (watch the banner cadence); zero-flagged squad behaves exactly as today.

## 7. What ships

`JoinViaFriend` flag + store plumbing; `SquadLaunchPlan` + anchor-wait predicate (pure, tested); phase-aware `SquadLaunchAsync` orchestration; careful-mode setting + checkbox; per-account toggle UI; banner copy; smoke checklist. Own subagent-driven cycle (~5-6 tasks) on `feat/trust-aware-squad-launch`.

**Success metric (the honest one):** Este's three challenge-prone accounts land in the clan's private server on the first squad launch, zero CAPTCHAs, for a week of dailies.
