# Server-instance targeting — rejoin a specific Roblox server

**Date:** 2026-08-02
**Status:** Approved design. Core behaviour **verified live** — see Evidence.
**Driver:** User report — Recycle returns you to the game but not the server you were in.
**Depends on:** [`2026-08-01-launch-gate-condition-based-design.md`](2026-08-01-launch-gate-condition-based-design.md) must land first. Squad Launch consumes the same shared-settings launch path and inherits its race.

## Why

`LaunchTarget.Place(placeId)` means *"this game, any server with room."* Roblox matchmakes. So:

- **Recycle** puts you back in the game but almost certainly a different server — undermining the one-click remedy the whole v1.12 memory watchdog points at. If Recycle costs you your spot, people stop pressing it.
- **Squad Launch cannot target a public server at all.** `SelectedTarget` is typed `LaunchTarget.PrivateServer?` — it is structurally private-server-only. There is currently no way to get a roster into the same public server.

Private servers are already correct: `PrivateServer(placeId, code, kind)` identifies one specific server, so Recycle already returns you to it. This spec changes nothing there.

## Evidence — what the spike proved

Verified live on 2026-08-02 against Pet Sim, branch `spike/game-job-targeting`.

**1. Presence reports the server-instance id for your own accounts.** Our own docstring warned `GameJobId` is *"populated only when the user's privacy settings allow visibility to the requesting cookie's owner."* It populates, and is stable across consecutive polls. This was a genuine unknown that could have killed the design.

**2. The PlaceLauncher shape works.**

```
?request=RequestGameJob&browserTrackerId=…&placeId=…&gameId=<jobId>&isPlayTogetherGame=false
```

This was a hypothesis from pattern-matching the other request types (`accessCode`→`accessCode`, `linkCode`→`linkCode`, `userId`→`RequestFollowUser&userId`), with **no authoritative source**. Roblox's *documented* deep link (`roblox://…&gameInstanceId=…`) names the same concept but **carries no auth ticket**, so it cannot launch as a specific alt and is unusable for multi-account. PlaceLauncher had to be verified empirically, and now has been:

```
10:30:14  presence  job=fcbe3a36-d655-41da-ba8a-8280f5709568
10:30:30  RECYCLE   -> GameJob(place=140403681187145, jobId=fcbe3a36-…)
10:30:30  client stopped, relaunched as pid 48560
10:31:04  presence  job=fcbe3a36-d655-41da-ba8a-8280f5709568   <- same server, 34s later, fully loaded
```

**3. `placeId` and `jobId` are a MATCHED PAIR, and both must come from presence.** This is the finding that would otherwise have shipped as a bug.

Many games — Pet Sim included — teleport players between places inside one universe. The place you *launched into* is not the place you are *in*. The first spike attempt paired the launch target's `placeId` (`8737899170`) with presence's `jobId` (which belonged to place `140403681187145`) and produced:

> *"This experience has ended, or the server became unavailable unexpectedly due to a system error."*

The server had not ended. We sent an address that does not exist. **Requirement: take both halves from the same presence snapshot, never mix a launch-target place with a presence job.**

**4. A bad pair fails loudly, not silently.** The worst plausible outcome was Roblox accepting a malformed request and quietly matchmaking elsewhere — success-shaped failure. It does not; it rejects with a visible error. That simplifies the fallback considerably.

## The primitive

**`LaunchTarget.GameJob(long PlaceId, string JobId)`** — a new variant, building the URI above. `JobId` is URL-escaped.

**Retain `GameJobId`.** Presence already returns it and `RobloxApi` already parses it into `UserPresence.GameJobId`; the pipeline discarded it at the ViewModel boundary. Carry it on `AccountPresenceEventArgs` and store it on `AccountSummary.CurrentGameJobId`, alongside the existing `CurrentPlaceId`.

**One invariant, stated once and enforced at the construction site:** a `GameJob` target may only be built from a single presence snapshot's `(PlaceId, GameJobId)`. Any code path that sources them separately is wrong. This belongs in a small factory rather than being open-coded at each call site, precisely because the spike proved how easy it is to get wrong.

## Consumers

### Recycle

Before stopping, read the account's current `(placeId, jobId)` from presence. If both are present **and** the resolved target is a `Place`, launch as `GameJob`. Otherwise launch exactly as today.

`PrivateServer` is never upgraded — its code already identifies one server.

### Squad Launch

Allow a **public** place as a squad target, which is currently impossible. Launch the first account into the place, wait for presence to report its `(placeId, jobId)`, then launch the remainder as `GameJob` against that pair.

The wait is the cost. Presence polls every 25 s, which is too slow to sit through. `PresenceService` already exposes an immediate-refresh hook (`RequestImmediateRefreshAsync`, wired today to process-exit); use it to poll that one account aggressively until a job id appears, with a bounded timeout. On timeout, fall back to launching the rest as plain `Place` — a scattered squad is worse than a slow one, but far better than no squad.

## Verification and remedy — do not trust the launch result

A launch returning `Started` means a process started, not that it landed in the right server. **Verify with presence**, the same way the spike did by hand:

After a `GameJob` launch, watch that account's presence until it reports `InGame`, then compare the reported `jobId` to the requested one.

| Outcome | Meaning | Response |
| --- | --- | --- |
| jobId **matches** | Landed in the intended server | Silent success |
| jobId **differs** | Roblox matchmade elsewhere — full server, or a shape we do not understand | Surface it; offer **Try again** |
| never reaches `InGame` within the window | Launch failed or was rejected | Surface it; offer **Try again** |

This is deliberately robust to *why* it missed. We have verified the bad-pair failure mode; we have **not** characterised what Roblox does when the requested server is **full** — it may reject, or it may silently matchmake. Presence comparison catches both without us needing to know which.

**Remedy is offered, not automatic.** Every retry is another client restart — disruptive, and pointless against a genuinely full server. Surface the miss on the row with a one-click retry and let the user decide. Auto-retry is explicitly out of scope until we have field data on how often misses happen and why.

For Squad Launch the miss is more visible and matters more: report which accounts did not make it into the shared server, since "we are all together" is the entire point of the feature.

## Failure modes

| Case | Behaviour |
| --- | --- |
| No `jobId` known (offline, privacy, pre-first-poll) | Launch as `Place`. Log why. Verified working — the spike's first attempt hit this and fell back cleanly. |
| `jobId` known but no `placeId` | Launch as `Place`. Never construct a half-pair. |
| Target is `PrivateServer` | Never upgraded. |
| Server full / gone / stale id | Launch attempt fails or lands elsewhere; presence verification catches it; user offered retry. |
| Presence verification times out | Treat as a miss, not a success. Silence is not confirmation. |

**No failure path may leave the account outside the game.** "Back in the game, wrong server" is today's behaviour and an acceptable floor. Stranding an account is not.

## Testing

xUnit, injected fakes, no real launches.

- URI construction for `GameJob` — exact shape, and `jobId` URL-escaped.
- **The matched-pair invariant:** constructing a `GameJob` from mismatched sources is impossible or rejected. This is the test that encodes the spike's hard-won finding; name it so its purpose survives without this document.
- Recycle upgrades `Place` → `GameJob` when a full presence pair exists; does not upgrade when either half is missing; never upgrades `PrivateServer`.
- Presence verification: matching jobId → success; differing jobId → miss; never-`InGame` → miss.
- Squad Launch: first account launches as `Place`; the rest launch as `GameJob` against its reported pair; on job-id timeout all fall back to `Place`.

Each test must name the production change that would make it fail.

## Out of scope — queued in the feature ledger

- **Rejoin-after-death** — relaunch a client killed by RAM exhaustion into the server it was in. Closes the loop on the v1.12 memory work.
- **Regroup ("send my others here")** — row action putting the rest of the roster in one account's server.
- **Plugin live server identity** — `host.queries.current-server` today exposes only the last private-server link; extending it to live job ids lets a plugin coordinate a roster. Contract bump.
- **Automatic retry** on a verification miss — needs field data first.
