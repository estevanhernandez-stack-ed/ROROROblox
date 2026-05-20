# RoRoRo v1.5.0 — Presence-based account status + Launch multiple hardening

> **Status:** Design approved (augment approach) 2026-05-20. Pre-implementation.
> **Cycle:** v1.5.0 (credibility hotfix). Tags + private-server picker deferred to v1.5.1; cross-machine account import/export deferred to its own cycle.
> **Current source:** v1.4.3.0. Reported-against build: Store v1.3.4.0.

## Why this exists

Clan feedback (K0ii Discord, 2026-05-20) surfaced three issues, two of which make RoRoRo look broken to the whole clan:

1. **The ghost.** A user (WitheredZack) had multiple Roblox clients genuinely running, but RoRoRo's UI showed only the most-recently-launched account as running and marked the rest "Closed." Confirmed by Este from the user's screenshots — the clients were alive.
2. **"Launch multiple does nothing."** Same user. Most likely the ghost starving eligibility — phantom-running accounts make `LaunchAllCommand` see nothing eligible, so it no-ops (or disables the button) with no explanation.
3. **Silent skip (Este's own "6 of 7").** Log-confirmed: on 2026-05-20 12:46:24, Launch multiple evaluated 6 eligible of 7 accounts. The excluded account (CECPapa, `f9c5eee7`) was genuinely still running — its `RobloxPlayerBeta.exe` (pid 45580) exited 67 ms *after* the eligibility snapshot. The launcher did the right thing; it just never told Este *why* the 7th sat out. The success banner only ever mentions deselected accounts (`MainViewModel.cs:804`), never "already running."

### Banner correction — the mutex misdiagnosis

An earlier pass theorized the ghost was the user's multi-instance not holding (mutex not acquired → each launch boots the prior client). **That was wrong.** The user's later screenshots show multiple clients running simultaneously, so their multi-instance works. The clients are alive; RoRoRo's *local process tracking* is losing them. Recorded here so the design doesn't rest on a dead hypothesis.

### Root cause of the ghost (leading hypothesis)

The user reported a "black installer" popping up during multilaunch — Roblox's anti-multilaunch bootstrapper. When it fires, the `RobloxPlayerBeta.exe` RoRoRo attached to (`RobloxProcessTracker`, `RobloxProcessTracker.cs`) exits and Roblox respawns the real client under a new pid we never claimed. `OnProcessExited` flips `IsRunning = false` and stamps `LastClosedAtUtc = now` (`MainViewModel.cs:1097-1100`), so the row reads "Closed" while the window is still up. Only the most-recent account shows running because its bootstrapper hasn't reshuffled yet.

This exact mechanism is unconfirmed (no logs from the user's machine). The chosen fix works **regardless** of which way local process tracking breaks — that is its central virtue.

## The decision: augment, don't replace

Roblox exposes a presence API already wired into the codebase: `IRobloxApi.GetPresenceAsync(cookie, userIds)` → `presence.roblox.com/v1/presence/users`, returning `UserPresenceType` (`InGame`) + `PlaceId` (`IRobloxApi.cs:58`, `UserPresence.cs`). An account can **always see its own presence**, so querying each alt with its own cookie returns server-truth state untouched by privacy filters.

Three approaches were weighed:

| Approach | Verdict |
|---|---|
| **Replace** process tracking with presence polling | Rejected. No pid → close/kill dies. Presence lags 10-30 s after launch → blank row exactly when feedback is wanted. |
| **Augment** — presence authoritative for *display* (in-game + game name); process tracking retained for *actions* (pid, instant launch feedback, startup re-attach) | **Chosen.** Kills the ghost, delivers "show the game," keeps close/kill, contained blast radius. |
| **Patch the process tracker** (continuous title re-attach) | Rejected. Chases the fragile mechanism that's failing (non-unique display names, bootstrapper races) and still doesn't show the game. |

**The load-bearing rule:** the two signals cover each other's blind spots. Process tracking covers presence's lag and privacy gaps; presence covers process tracking's pid-loss. A row shows **active** if `InGame` (presence) **OR** `IsRunning` (process). A row shows **Closed** only when **both** agree it's gone. The ghost cannot survive this — once presence reports `InGame`, a lost pid can no longer force "Closed."

## Components

### 1. `PresenceService` (new) — `src/ROROROblox.Core/Diagnostics`

Lives in Core alongside `RobloxProcessTracker` — no WPF dependencies, depends only on `IRobloxApi` + `IAccountStore` (both Core), and is unit-testable with a stub API. Polls each account's own presence on a timer and raises change events the ViewModel consumes, mirroring the existing `IRobloxProcessTracker` event pattern. Exposed via an `IPresenceService` interface for DI + test substitution.

**Polling loop**
- `PeriodicTimer`, default **25 s** (constant, tunable).
- Each tick: snapshot non-expired accounts that have a non-null `RobloxUserId`.
- Per account: `RetrieveCookieAsync(id)` → `GetPresenceAsync(cookie, [userId])` → take the single `UserPresence`.
- If `InGame` with a `PlaceId`: resolve game name via `GetGameMetadataByPlaceIdAsync(placeId)`, cached by `PlaceId` (`ConcurrentDictionary<long,string>`) so repeated polls don't refetch.
- Raise `AccountPresenceUpdated(accountId, presenceType, placeId, gameName)`.

**Concurrency / rate limits**
- Stagger calls across the interval (small concurrency cap, e.g. `SemaphoreSlim(4)`, plus jitter) — never N simultaneous POSTs.
- On HTTP 429: back off for the remainder of the cycle; do not hammer.
- On `CookieExpiredException` (401): raise an expired signal so the row flips to "Session expired."
- On any other failure: **hold last-known state** (never flip to Closed on a failed poll); log at Debug.

**Fast-confirm hook (resolves presence lag + the ghost together)**
- When `RobloxProcessTracker` raises `ProcessExited` for an account, `PresenceService` triggers an **immediate** presence re-poll for that one account instead of waiting for the next 25 s tick.
- If presence then says not-in-game → the close is confirmed and the row flips to "Closed."
- If presence still says `InGame` (the ghost case — process gone, game still up) → the row stays "In <game>."
- This makes process-exit a *hint to re-check*, with presence as the arbiter. It also shrinks the close-detection lag from up to 25 s down to one round-trip.

**Secret hygiene**
- Cookies are used only in-memory for the HTTPS call and are **never logged** (per CLAUDE.md DPAPI rule). The `dpapi-cookie-blast-radius` audit must pass against this service.
- DPAPI decrypt happens per poll per account. Acceptable at a 25 s cadence over non-expired accounts; do **not** cache plaintext cookies in memory to "optimize" — that widens the secret-exposure surface. Revisit only if measured as a real cost.

**Lifecycle**
- DI singleton, started after the account list loads, stopped on app exit — same shape as the process tracker.

### 2. Status reconciliation — `AccountSummary` (`ViewModels/AccountSummary.cs`)

New presence-derived state on the row:
- `UserPresenceType PresenceState` (default `Offline`)
- `string? CurrentGameName`
- `long? CurrentPlaceId`
- `DateTimeOffset? InGameSinceUtc` (first tick we saw `InGame`, for the duration label)
- `bool InGame => PresenceState == UserPresenceType.InGame`

**`StatusDot`** precedence: expired → `yellow`; `InGame || IsRunning` → `green`; else `grey`.

**`SecondaryStatusText`** precedence (replaces the current `IsRunning`-only logic at `AccountSummary.cs:224-250`):
1. `SessionExpired` → `"Session expired"`
2. `InGame` → `"In {CurrentGameName} · {age since InGameSinceUtc}"` (falls back to `"In {game}"` when no since-time; `"In a game"` when the name hasn't resolved yet)
3. `IsRunning && !InGame` → `"Connecting…"` for the first ~60 s after launch; after that, fall back to `"Running"` (process alive, presence not reporting in-game — e.g. at the Roblox menu, or presence set to invisible)
4. non-empty `StatusText` (launch error) → show it
5. presence-confirmed close (`LastClosedAtUtc` set, `!InGame && !IsRunning`) → `"Closed {ago}"`
6. `LastLaunchedAt` → `"Last launched {ago}"`
7. `"Ready"`

**Anti-ghost change in `MainViewModel.OnProcessExited`** (`MainViewModel.cs:1091`):
- Still clear `RunningPid`, set `IsRunning = false` (the pid is genuinely gone).
- **Do not** unconditionally stamp `LastClosedAtUtc`. The close is stamped only when presence confirms not-in-game (via the fast-confirm re-poll). While `InGame` is true, the row keeps showing "In <game>."

### 3. Launch multiple hardening — `MainViewModel`

**Eligibility** (`LaunchAllAsync` `MainViewModel.cs:771`, and the mirror in `SquadLaunchAsync` `:848`):
- Change the "is this account busy" test from `!a.IsRunning` to `!(a.InGame || a.IsRunning)` — an account genuinely in a game (even if its pid was lost) is correctly skipped; a truly-closed account (both false) is correctly included.

**Pre-snapshot refresh (closes the 67 ms race):**
- Before computing the eligibility set, await a one-shot presence refresh of the selected accounts. This makes a just-closed client resolve to not-in-game before the snapshot, instead of being counted as running.

**Never silently no-op:**
- When `targets.Count == 0`, the banner already explains (`:783`), but only with a deselected count. Extend it to the full Squad-modal breakdown — `"Nothing to launch — 7 in a game, 0 expired, 0 deselected."`
- On a partial launch, replace the bare `"{n} clients dispatched"` banner with a skip-reason tail: **`"Launched 6 · 1 already in a game (skipped)."`** This fixes the silent 6-of-7.

**`LaunchAllCommand` CanExecute** (`MainViewModel.cs:104`): update to `!(InGame || IsRunning)` so the button enables/disables against true state. When disabled, the button tooltip should say why (all in game / all expired / all deselected).

**The v1.3.4 "In Batches radio":** does not exist in current source — Launch multiple is a direct button (`MainWindow.xaml:979`). The confusion resolves on update; no new control is added. The built-in throttle (5 s between launches, `MainViewModel.cs:801`) stays.

## Data flow

```
PeriodicTimer tick (25s)  ─┐
ProcessExited fast-confirm ─┴─▶ PresenceService.Poll(account)
                                   │  RetrieveCookieAsync(id)
                                   │  GetPresenceAsync(cookie,[userId])
                                   │  PlaceId ─▶ game-name cache ─▶ GetGameMetadataByPlaceIdAsync
                                   ▼
                           AccountPresenceUpdated(id, type, placeId, gameName)
                                   ▼  (Dispatcher.Invoke)
                           AccountSummary: PresenceState / CurrentGameName / InGameSinceUtc
                                   ▼
                           StatusDot + SecondaryStatusText recompute  ─▶ row re-renders
                                   │
                           LaunchAll eligibility = !(InGame || IsRunning) && selected && !expired && !launching
```

## Error handling / edge cases

- **Presence poll fails (network/5xx):** hold last-known state. A failed poll never closes a row.
- **401 from presence:** flip the row to session-expired (the cookie died between launches).
- **429:** back off for the cycle; resume next tick.
- **Invisible / restricted presence on the user's own account:** self-query *should* still report in-game, but if it returns not-in-game while the process is alive, the `IsRunning` fallback keeps the row showing "Running" — the augment model degrades to honest, not to Closed.
- **Account with no `RobloxUserId` yet** (pre-backfill): skipped by the poller until backfill resolves it; the row uses process-only state meanwhile.
- **Account running but never launched by RoRoRo** (manual launch, survived a RoRoRo restart): presence catches it by userId regardless of local process state — a bonus correctness win.
- **Game-name resolution fails:** show "In a game" rather than a PlaceId; retry name resolution on the next poll.

## Testing

Unit + reconciliation tests only — no end-to-end against real roblox.com (CLAUDE.md rule; bot accounts get flagged).

- **`PresenceService`** (stub `IRobloxApi`): event raised on change; game-name cache hit avoids refetch; 401 → expired signal; 429 → backoff; generic failure → last-known held; fast-confirm re-poll fires on `ProcessExited`.
- **`AccountSummary` reconciliation** (table-driven over `SessionExpired × InGame × IsRunning × LastClosed`): asserts `StatusDot` + `SecondaryStatusText`. The ghost case is the headline: `IsRunning=false, InGame=true` → "In <game>", **not** "Closed."
- **Launch multiple eligibility:** in-game-but-pid-lost → excluded; both-false → included; pre-snapshot refresh flips a just-closed account to eligible; partial-launch banner contains the skip-reason tail; zero-eligible banner contains the full breakdown.

## Out of scope (and where it goes)

- **Tags / notes per account** (PS99, RCU, PLAZA) → **v1.5.1**. `Account.Tags`, chips + filter.
- **Saved private servers in the per-account picker** → **v1.5.1**. `IPrivateServerStore` exists; surface it in the per-row Join-by-link dropdown rather than Squad-only.
- **Cross-machine account import/export** → **deferred to its own cycle.** DPAPI is per-user/per-machine by design; this is the per-cookie-encryption portability work, security-sensitive, not a hotfix.
- **The Roblox anti-multilaunch bootstrapper itself** → not ours to fix. The presence approach sidesteps its effect on our UI.

## Risks / open questions

- **Presence rate limits at high account counts (50+).** Mitigated by stagger + cache + 429 backoff. The actual limits are unconfirmed — do **not** assume; if a heavy-account user reports throttling, measure before tuning the cadence. (Flag for a short spike if it bites.)
- **DPAPI decrypt cost per poll.** Acceptable at 25 s over non-expired accounts. Plaintext cookie caching is explicitly off the table for secret-hygiene reasons unless measured as a real problem.
- **Presence self-visibility under "invisible" mode** is not verified against live Roblox. Covered defensively by the `IsRunning` fallback; worth a one-account manual check during build.

## Decisions to log (626 Labs Dashboard)

- Presence API as the authoritative source of account running-state (augment over replace); process tracking retained for pid + instant feedback.
- Mutex-misdiagnosis correction (recorded above; the ghost is local-tracking pid-loss, not multi-instance failure).
- Roblox-side compatibility dependency added: `presence.roblox.com/v1/presence/users` shape + self-presence visibility. If Roblox changes presence privacy or the endpoint, the ghost-fix degrades to process-only — log any such drift.
