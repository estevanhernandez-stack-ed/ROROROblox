# Adaptive client footprint — scope

**Follow-up to:** PR #96 (F-082, the memory-headroom wave)
**Status:** proposed, not started

**One sentence:** RoRoRo's memory warnings ask a constant measured on one machine what a Roblox
client costs, while the watchdog has been measuring the real answer on *this* machine every thirty
seconds the whole time.

---

## Why

#96 fixed the inverted axis — a per-client cap that scaled *up* with installed RAM and so could
never fire — and added a headroom trigger that can actually see an oversubscribed machine. Those
are right regardless of what follows and should ship as they are.

But the pre-launch advisor and the "how many fit" arithmetic both lean on
`MemoryDefaults.ExpectedClientMb = 2650`, measured on **one** machine (47 GB, Pet Sim 99, Roblox
733). Every part of that is a variable: hardware, game, and client build. A threshold tuned to the
wrong number either cries wolf or never fires, and both failures end the same way — the warning
gets ignored.

Este's framing, which is the correct one: *can't it be smart?* It can. `MemoryWatchdog` already
samples every tracked client's private bytes on a 30-second timer. The number is already in hand.

---

## What gets learned

**One value: this machine's typical steady-state client footprint.** Not peak, not median —
roughly p75 of *settled* clients. Peak overestimates on transient spikes and would suppress
launches that would have been fine; median underestimates and lets the machine oversubscribe.

### Only from settled clients

A client that has been up for two minutes is still filling caches. `MemoryWatchdog` already
computes a per-account growth rate and already refuses to claim a slope before
`MinimumObservation` (10 minutes) — the same gate decides what is eligible to teach. A sample
counts only when that client's growth has gone flat.

### THE TRAP: never learn while under pressure

**This is the part that will silently invert the feature if it is missed.**

When a machine is oversubscribed, Windows trims working sets, and `PrivateMemorySize64` reads
*lower*. So the moment the user is in trouble is exactly the moment clients look cheap. Learn from
those samples and the footprint shrinks, the estimate says more clients fit, the warning gets
quieter — and it gets quieter *because* things got worse.

A feedback loop that disables the alarm in proportion to the fire.

**Rule: samples taken while `BelowReserve` is true, or while the machine is otherwise paging, are
discarded outright.** The learner only listens to a comfortable machine. This deserves its own
test with a name that says so.

---

## Cold start and guard rails

| | value | why |
|---|---|---|
| seed | 2650 MB | today's constant, used until enough settled samples exist |
| minimum samples | 20 settled readings | below this, the seed governs |
| floor | 1200 MB | a Roblox client has never been observed near this; anything lower means we measured something that was not a game client |
| ceiling | 5000 MB | above this we are learning from an anomaly, not a norm |

The floor and ceiling are not decoration. **A learned threshold that drifts is worse than a fixed
one that is wrong**, because a fixed wrong number is at least predictable and reproducible in a bug
report. The clamps are what keep one pathological session from teaching the app something absurd
and permanent.

---

## Storage, and one invariant that must not bend

Persists to `settings.json` alongside the other memory values, as `int?` — `null` meaning "not
learned yet", the same shape `MemoryReserveMb` / `MemoryCapMb` already use.

**`IAppSettings.GetMemoryCapMbAsync` states that a non-null stored value is a deliberate user
override and must never be silently re-derived.** Learning must not violate that. A user who set
their own cap keeps it, forever, and the learned footprint feeds only the values they have *not*
pinned. There is a test for this in #96 already; it extends to cover the learner.

## Staleness

A Roblox client build changes what it costs — that is the whole reason this investigation started.
The learned value records the client version it was learned from, and resets when the major client
version changes. Better a cold start on the seed than a confident number describing a build nobody
is running.

---

## What it feeds, and what it does not

**Feeds:** `LaunchHeadroomAdvisor` (how many fit), and the "room for about N more" line in the
pre-launch dialog.

**Does NOT feed the per-client anomaly cap.** That is a fixed 4 GB line by design after #96 —
"this one client is abnormal" is a judgement about Roblox clients in general, not about this
machine. Learning it would reintroduce the F-082 shape by a different route: a machine that runs
heavy would teach itself that heavy is normal and stop warning.

**Does NOT feed the headroom trigger.** Free memory against the reserve is a measurement, not an
estimate. Nothing to learn.

---

## Testing

- A settled, comfortable machine converges on its real footprint within the sample minimum.
- **Samples taken under pressure are discarded** — the feedback-loop guard, tested by feeding a
  sequence where trimmed readings would drag the learned value down, and asserting it does not move.
- Growing clients do not teach; only settled ones do.
- Floor and ceiling clamp an absurd input on both sides.
- A user-pinned `MemoryCapMb` survives every learning cycle.
- Cold start uses the seed and behaves exactly as #96 does today — so this change is invisible
  until it has evidence, which is the point.
- A client-version change resets to the seed.

---

## Evidence still wanted

`scripts/measure-client-memory.ps1` (on main, `9e43c31`) collects the same two figures from any
machine. Numbers from Este's 32 GB box and from clan members calibrate the **seed and the clamps** —
they are not the runtime thresholds. Once learning ships, per-machine measurement becomes a sanity
check that convergence lands somewhere sensible, rather than the source of truth.

---

## Not in scope

- **Per-game footprints.** Pet Sim is not every game and a heavy experience will read differently.
  Real, and deferred: one value per machine is a large improvement over one value per universe, and
  splitting by game multiplies the cold-start problem by the number of games somebody plays.
- **Sharing measurements between users.** That is telemetry. This app does not have any, and adding
  it for a threshold is a bad trade.
- **Auto-acting on the estimate.** It advises. #96's reasoning holds: a forecast should not veto
  what somebody does with their own machine.
