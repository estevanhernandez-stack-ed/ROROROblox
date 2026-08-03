# Handoff — server-instance targeting (Recycle back into the *same* server)

> **CLOSED 2026-08-02 — built on `feat/server-instance-targeting`.** All four items shipped
> (primitive, presence plumb-through, Recycle, Squad Launch), plus the verification the open design
> question asked about — answered as *status banner, no row affordance, no auto-retry*. Deviations
> from the spec are bannered at the top of
> [`specs/2026-08-02-server-instance-targeting-design.md`](specs/2026-08-02-server-instance-targeting-design.md).
> **Still manual:** the live smoke (launch, recycle, confirm the same server) — this feature cannot
> be verified against real roblox.com from CI. **Still open:** Recycle is only visible while a
> memory warning is latched, so the rejoin is not reachable outside that state.

**Written:** 2026-08-02, end of a long session. **Status:** spec written, spike verified live,
**nothing built.** Start here.

**Start the new session with:** *"Read `docs/superpowers/HANDOFF-2026-08-02-server-instance-targeting.md` and build it."*

---

## The goal

A clan member asked that **Recycle put them back into the same *instance* of the game**, not
just the same game. Today `LaunchTarget.Place` means "this game, any server with room," so a
recycled client lands wherever — away from the squad it was playing with. Same problem for
Squad Launch.

Este's scope call, verbatim: **"let's do the primitive and then recycle and squad launch"** —
with three dependent features explicitly staying on the backlog (see below).

---

## Already proven by a live spike — do NOT re-derive this

The expensive discovery is done. Treat these as established facts, not hypotheses.

**1. The URI shape works.**

```
https://assetgame.roblox.com/game/PlaceLauncher.ashx
  ?request=RequestGameJob
  &browserTrackerId=<btid>
  &placeId=<placeId>
  &gameId=<jobId>
  &isPlayTogetherGame=false
```

Verified live 2026-08-02: job id `fcbe3a36-d655-41da-ba8a-8280f5709568` was identical before
and after a recycle, and still held 34 seconds later. `RequestGameJob` sits alongside the
existing `RequestGame` / `RequestPrivateGame` / `RequestFollowUser` shapes in
`RobloxLauncher.BuildPlaceLauncherUrl`.

**2. `placeId` and `jobId` are a MATCHED PAIR. This is the one that will bite you.**

Both must come from **the same presence snapshot**. Pairing the *launch target's* `placeId`
with presence's `jobId` produces:

> "This experience has ended, or the server became unavailable unexpectedly due to a system error."

Why: Pet Sim (and others) teleport between places *inside a universe*. The entry place
(`8737899170`) is not the place presence reports the account is actually in
(`140403681187145`). Use presence's `PlaceId` **and** presence's `GameJobId`, or neither.

Este caught this himself during the spike — *"I don't think I was the only person in that
server so it shouldn't have shut when I recycled"* — before the logs did.

**3. Presence already carries the job id; we throw it away.**

`UserPresence` has `GameJobId`. `RobloxApi` parses Roblox's `gameId` field into it. But
`MainViewModel`'s presence consumer passes `GameJobId: null` onward — that discard is the only
reason this feature doesn't already have its input.

**4. `PrivateServer` targets must NOT be upgraded to `GameJob`.** The access/link code already
identifies exactly one server. Leave that path alone.

---

## Where the artifacts are

| What | Where |
|---|---|
| **Design spec** | `docs/superpowers/specs/2026-08-02-server-instance-targeting-design.md` — **now on `main`** |
| **Spike code** | commit `b2899d2` on branch `spike/game-job-targeting` |
| Dependent backlog items | `docs/features.md` — rejoin-after-death, regroup, plugin live-server-identity |

### The spike branch is NOT mergeable — fold pieces in by hand

`b2899d2` was built to answer one question fast and says so in its own commit message. It
contains:

- a **deliberately broken XAML hack** that forces the Recycle button visible on *every* running
  row (marked `REVERT`) so the rejoin could be tested without driving a client to the memory cap
- `SPIKE`-tagged logging throughout, including an `Info`-level presence log that would be noise
  in production
- comments written as hypotheses ("unverified parameter shape") that are now settled fact

Read it for the verified mechanics — the URI construction, the presence plumb-through, the
`LaunchTarget.GameJob` record — and re-implement cleanly. **Do not merge that branch.**

---

## What to build

1. **The primitive.** `LaunchTarget.GameJob(long PlaceId, string JobId)` plus its
   `BuildPlaceLauncherUrl` arm. Testable in isolation; the spike's version is close to right.
2. **Stop discarding the job id.** Thread `GameJobId` from presence through to the row so a
   consumer can read "which server is this account in *right now*."
3. **Recycle.** Upgrade a `Place` target to `GameJob` when presence has a job id for that
   account — using presence's place, per the matched-pair rule. Leave `PrivateServer` alone.
4. **Squad Launch.** Same upgrade for the batch path.

**Out of scope** (backlog, all noted as depending on this): rejoin-after-death, regroup
("send my others here"), and the plugin `host.queries.current-server` extension to live job ids.

---

## The open design question — Este asked for this explicitly

> *"can we tell when the user doesn't get back into the right session, can we give options or
> remedy?"*

This needs a real decision, not an implementation guess. The raw material: after a launch,
presence will report which job the account actually landed in. Compare it to the one requested.
If they differ — server filled up, shut down, or the job expired — what do we do? Say nothing?
A row-level note? Offer a retry? Decide this deliberately; it is the difference between a
feature that feels reliable and one that silently doesn't work sometimes.

---

## Repo constraints that cost time today

- **Build `dotnet build ROROROblox.slnx`; test `dotnet test ROROROblox.slnx`.** `.slnx` is
  canonical. A bare `dotnet build` errors MSB1011 while a stray `.sln` exists.
- **Close `ROROROblox.App` before building** — it locks `ROROROblox.Core.dll` (MSB3027).
  Scoping at `ROROROblox.Tests.csproj` does **not** dodge it; that project references App.
- **No test may sleep in real time; none may fail by hanging.** xUnit has no default timeout.
  A `.WaitAsync(TimeSpan.FromSeconds(5))` ceiling that elapses only on failure is the pattern.
- **The `FakeTimeProvider` trap:** `Task.Delay(…, TimeProvider, …)` flips its own status
  synchronously when the clock advances, but the awaiting continuation resumes asynchronously.
  Advancing in one jump can arm a timer against a clock that has stopped moving — a permanent
  stall that looks like slowness. `FpsCapSettlerTests.cs` has a stepped pump helper.
- Pre-commit hooks `secret-scan` and `local-path-guard`. Conventional commits.
- **Spec drift gets banner-corrected, never rewritten** — hard rule in `CLAUDE.md`.
- **Do not run automated end-to-end against real roblox.com.** Manual smoke on a real rig is the
  trade. This feature's verification is inherently manual: launch, recycle, confirm the same
  server.

---

## The lesson from today, because it applies directly

The FPS-cap fix took **three** mechanisms. The first two shipped with clean test suites and did
not work:

- One gated on the launched client's *process* appearing — but Roblox starts more than one
  process per launch and the first is frequently not the client (measured gaps of 0.02 s and
  5.92 s), and *closing* a client produces a lookalike. It passed ten reviews and 1015 tests.
- The second had the right signal in the wrong position. It passed four more review rounds.

What settled it was a ninety-second experiment against the real system — overwrite a file, watch
what the client does — not more reasoning.

**Two habits worth carrying into this feature:**

1. **Measure the system before designing against a model of it.** This feature is already ahead
   here: the spike measured the URI shape and the matched-pair rule. Don't add unmeasured
   assumptions on top.
2. **A test that passes whether or not the behaviour exists is worse than no test.** Five
   separate reviews on the FPS work each caught one. For each test, name the specific production
   change that would make it fail — and when it isn't obvious, mutate the production code, watch
   it go red, and revert.

---

## Current repo state

`main` is at **v1.13.0.0** (tagged, CI green). The GitHub draft release and the Partner Center
submission are Este's to action. Store MSIX for both architectures is built in `dist/`.
Working tree clean.
