# RORORO — account stats: uptime + per-game play time design

---
**Date:** 2026-07-09
**Status:** Approved-shape (queue spec — build whenever) — mini-spec, ready for a plan (this one warrants a fuller brainstorm before building — it's the largest of the queue)
**Author:** The Architect + Este
**Scope:** A stats surface where a user sees each account's **uptime** (time signed in / in-game), **presence-aware** (driven by actual player state), and — because presence carries the place — **per-game play time**.
**Origin:** Este — "a user stats section where they can see their up time, but it needs to be online aware, so aware of a player's state, which is perfect because then we can have per game play time."
**Builds on:** `PresenceService` (25s poll, already tracks `InGame`/`CurrentPlaceId`/`CurrentGameName`/`InGameSinceUtc` per `AccountSummary`).
---

## 1. Problem & the opportunity

There's no history of how long accounts have been playing, or where. The insight Este names: presence is ALREADY online-aware (it polls each account's state every 25s and records `CurrentPlaceId`/`InGameSinceUtc`), so uptime and per-game play time are a **recording + aggregation layer on top of the presence stream that already exists** — not new instrumentation. The hard part isn't detection; it's durable accumulation and honest attribution across sessions.

## 2. The core model

Presence gives, per account, a live `(state, placeId, gameName, inGameSince)` sampled every 25s. Stats = accumulate that into durable per-account, per-place time buckets.

**Design decisions:**

1. **Session-based accumulation, not poll-counting.** When an account transitions to `InGame` at place P, open a session `(accountId, placeId, gameName, startUtc)`. When it leaves (state changes, place changes, process exits, or app closes), close it and add `duration` to the durable `(accountId, placeId)` bucket. Place *change* while in-game (rare — hopping servers of different places) closes one session and opens the next. This is more accurate than summing poll intervals and survives the 25s granularity honestly (a session is bounded by real start/end presence events, ±one poll).
2. **What "uptime" means — pick one, state it:** **in-game time** (sum of session durations) is the honest, useful number for a Roblox multi-launcher (a signed-in-but-at-home account isn't "playing"). Total signed-in time is a weaker metric; v1 = **in-game uptime**, with "currently in-game since X" as the live readout. (If total-signed-in is wanted too, it's a second bucket — additive later.)
3. **Durable storage:** `stats.json` (`%LOCALAPPDATA%\ROROROblox\`, not secret) — per account: a list of `(placeId, gameName, totalSeconds, lastPlayedUtc, sessionCount)` buckets + a running total. Written on session close + on app exit (flush open sessions with their current duration so a crash loses ≤ one poll, not a whole session). Gate/atomic-write discipline like the other stores.
4. **Attribution honesty:** play time credits the place the account was actually in (from presence `CurrentPlaceId`), not the launch target — a user who launched to game A but the account followed a friend into game B gets B credited. Presence is truth; launch intent isn't.
5. **The 25s granularity is disclosed, not hidden:** durations are ±25s at the edges. The UI shows rounded friendly figures ("2h 14m") and never implies stopwatch precision. A session shorter than one poll interval may not register — acceptable and noted (you didn't really "play" a 10-second join).
6. **Presence-fix synergy:** the `GetAccountActivity` crediting fix (separate spec) is about idle *input*; this is about *presence*. Independent signals — but both make the app's "what's this account doing" story truthful. Stats does NOT depend on the activity fix.

## 3. The surface

A **Stats** view (its own window or a main-window panel — decided at brainstorm; a window keeps the busy main view uncluttered):
- **Per account:** total in-game time, current session ("in [game] for 34m" when live), a per-game breakdown (top games by time, with place name + thumbnail), last-played.
- **Roll-ups:** total across all accounts; most-played game clan-wide; today / this week windows (if buckets carry enough granularity — v1 may be all-time only, windows additive).
- **Live-updating** while open (subscribe to presence like the idle chip does).
- Reset / clear-stats affordance (per account + all), confirm-gated.

## 4. Non-goals (v1)

- No server-side / cross-machine aggregation (local only, like everything else).
- No sub-poll precision, no exact join/leave timestamps (presence is 25s-grained).
- No total-signed-in-vs-in-game split (in-game only v1).
- No charts/graphs in v1 (numbers + per-game list first; dataviz is a polish pass).
- No plugin exposure of stats v1 (additive query later if a leaderboard plugin wants it — natural fit).
- No historical backfill (stats start accumulating from the feature's ship; no reconstruction of past play).

## 5. Architecture

- **Core:** `IStatsStore` (`stats.json`) — per-account place buckets + totals; `RecordSessionAsync(accountId, placeId, gameName, durationSeconds, endUtc)`; `GetAsync(accountId)` / `ListAllAsync()`; `ResetAsync(accountId?)`. Gate + atomic write + tolerant load, same store discipline.
- **Core/App — session tracker:** `PlaySessionTracker` subscribes to the presence stream (`PresenceService.AccountPresenceUpdated`), maintains open sessions keyed by account, and on close writes to `IStatsStore`. Pure session-math (`open → close → duration`, place-change handling) extracted testable (inject a clock; feed synthetic presence transitions). Flush-open-on-dispose for app-exit.
- **App — VM + view:** `StatsViewModel` (per-account rows + roll-ups, live from presence); `StatsWindow`. Theme tokens; define-before-use.
- **Wiring:** the tracker starts with the app (alongside `PresenceService`/`ActivityMonitor`), disposed on exit (flush). No new Roblox calls — it's downstream of the existing presence poll.

## 6. Edge cases

- **App crash mid-session** → open sessions flushed on exit; a hard crash loses ≤ the time since last flush (flush on each poll-close + periodic). Reopened app starts fresh sessions from current presence.
- **Account in-game across an app restart** → the old session closed at last-known (exit flush); a new session opens on the next presence read. Slight seam (the restart gap isn't credited) — honest, noted.
- **Place hop (same game, new server)** → same placeId → session continues. **Different place** → close + open (credits both).
- **Presence flaps** (InGame→NotInGame→InGame within a poll or two) → debounce: a gap shorter than ~one poll interval doesn't close-and-reopen (avoids fragmenting one play session into many). Tunable; documented.
- **Account deleted** → its stats can be pruned (hook `AccountStore.RemoveAsync`) or kept as orphan history — decide at brainstorm (leaning prune, matching groups).
- **Clock changes / DST** → durations computed from monotonic UtcNow deltas, not wall-clock arithmetic.

## 7. Testing

- **Unit (pure session math):** open→close credits correct duration; place-change closes+opens; flap-debounce; exit-flush credits the open session; multiple accounts independent — all via injected clock + synthetic presence transitions (no live Roblox, no WPF).
- **Unit (store):** bucket accumulation, round-trip, reset, tolerant load, prune-on-delete.
- **Manual smoke:** launch an account into a game → after some minutes the Stats view shows accruing in-game time + the right game; hop games → both credited; close/reopen the app → totals persist; reset clears; live readout updates while playing.

## 8. What ships

`IStatsStore`; `PlaySessionTracker` (pure session math + presence subscription + exit-flush); `StatsViewModel` + `StatsWindow`; prune hook; tests per §7. Own cycle (~5-6 tasks). **Recommend a fuller `/brainstorm` before the plan** — the surface (window vs panel), the uptime definition, time-window roll-ups, and the delete-prune-vs-keep call are worth confirming interactively; the model above is the approved shape, not the final word.

**The through-line:** presence already knows *what* and *where* every 25s. Stats just remembers it, honestly, and adds it up.
