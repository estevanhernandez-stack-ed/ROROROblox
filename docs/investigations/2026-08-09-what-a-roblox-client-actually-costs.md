# What a Roblox client actually costs, and why we were measuring the wrong thing

**Date:** 2026-08-09
**Machine:** 31.9 GB, Ryzen 7 5800X3D (16T), Windows 11 26220
**Game:** Pet Sim 99, active play
**Affects:** `scripts/measure-client-memory.ps1`,
`docs/superpowers/specs/2026-08-09-rororo-adaptive-footprint-scope.md`,
`MemoryDefaults.ExpectedClientMb`

---

## Summary

Four findings, in ascending order of how much they should change the plan.

1. The measurement script draws its conclusion from samples the spec says to discard, and on a
   small machine it will do this every time.
2. Graphics quality, across the entire slider, is worth **64 MB or less**. It is not a variable
   worth modelling.
3. The dominant variable is **which account is logged in**. One account ran 514 MB above the
   median of its five siblings, stably, on the same machine with identical settings.
4. The 2650 MB constant was calibrated against a **light** player's accounts. The users who most
   need the warning are the ones it will underestimate.

The recommendation that follows from all four: **do not build a per-machine footprint learner.**
The entity is wrong.

---

## What was run

| | Run A | Run B |
|---|---|---|
| graphics quality | 1 (min) | 10 (max), Mode=Manual |
| clients | 8 | 6 |
| samples (game clients) | 452 | 342 |
| taken below the 2615 MB reserve | **447 (99%)** | 6 (1.8%) |
| settled + clean samples used | n/a | 210 |
| median of per-client peaks | 2361 MB | 2425 MB |
| highest client | 2710 MB | 2939 MB |

Reserve is 2615 MB on this machine (`MemoryDefaults.ReserveMb(31.9 GB)`). "Settled" is the flat
tail after aggregate growth stopped, matching the spec's own eligibility rule.

Raw samples are committed alongside so every figure below can be re-derived:

- `data/2026-08-09-runA-gfx1-8clients.csv`
- `data/2026-08-09-runB-gfx10-6clients.csv`

Columns are `timestamp,pid,mb,availableMb`. Clients under 500 MB are helper processes (crash
handler, tray-launched client at a menu) and are excluded from every figure here, matching the
script's own floor.

---

## Finding 1: the measurement traps itself

Run A reported:

```
per-client median  : 2361 MB
per-client peak    : 2710 MB
-> this machine runs LIGHTER by 289 MB per client
```

Read naively, that is a second data point saying `ExpectedClientMb = 2650` is ~11% too high.

It is not a data point. 99% of its samples were taken while the machine sat below reserve. The
adaptive-footprint scope already names this hazard for the learner:

> When a machine is oversubscribed, Windows trims working sets, and `PrivateMemorySize64` reads
> *lower*. So the moment the user is in trouble is exactly the moment clients look cheap.
> **Rule: samples taken while `BelowReserve` is true [...] are discarded outright.**

The learner is specified to discard these. The script that gathers evidence for retuning the
constant applies no such guard and prints a confident verdict built almost entirely on them.

### The structural half

Two conditions cannot both hold on a small machine:

1. To measure a *multi-client* footprint, run many clients.
2. To measure a *trustworthy* footprint, stay above reserve.

Eight clients at ~2.4 GB is ~19 GB against ~22 GB usable here. Condition 1 forces condition 2 to
fail. Only machines with genuine headroom can satisfy both, which is why the original 47 GB
numbers are the trustworthy ones: that machine had room.

This also bounds what a learner can ever do. With the pressure guard correctly in place, a small
oversubscribed machine will rarely accumulate clean settled samples, because it is under pressure
precisely when clients are running. It falls back to the constant permanently. **The learner helps
most on the machines whose warnings matter least.**

### A withdrawn claim

An earlier draft cited `WorkingSet64 < PrivateMemorySize64` as corroborating evidence of trimming.
Withdrawn. Private bytes counts committed-but-not-resident pages, so that gap is normal for any
process, and it persisted unchanged (6 of 6 clients, ~27%) after the machine returned above
reserve. A signal that reads the same with and without pressure is not measuring pressure. The
argument above rests on the sample split, which does not depend on it.

---

## Finding 2: graphics quality is not the variable

Run A was recorded at graphics quality **1**. Run B at **10**. The full range of the slider.

```
Run B median of peaks : 2425 MB
Run A median of peaks : 2361 MB
delta                 :   64 MB  (3%)
```

And 64 MB is an **upper bound, not an estimate**. Run A was deflated by trimming, so its true
value is higher than 2361, which makes the true delta smaller than 64 MB.

This was worth testing because graphics settings are a variable the scope does not list (it names
hardware, game, and client build) and one the user can change at any moment with no event to hook.
The concern was reasonable and the data rejects it. Graphics quality needs recording for
completeness, not modelling.

---

## Finding 3: the account is the variable

Run B, settled window, per client:

| pid | avg MB | peak | min | range |
|---|---|---|---|---|
| **47072 (estehernandez, MAIN)** | **2915** | 2939 | 2887 | 52 |
| 42336 | 2500 | 2529 | 2494 | 35 |
| 42344 | 2409 | 2425 | 2400 | 25 |
| 25616 | 2401 | 2412 | 2396 | 16 |
| 26504 | 2383 | 2395 | 2365 | 30 |
| 45328 | 2361 | 2379 | 2355 | 24 |

Identified from the launch log:

```
11:28:50.312  Launcher pid 47072 for "8fb7b3e1-ff6f-4c03-81e8-7d824d7b7f82"
11:28:50.361  Pre-warm complete: installer gone + estehernandez attached.
```

The main account ran **514 MB above the median alt** (2915 vs 2401), a 21% premium.

The obvious mechanism is loaded account state. In Pet Sim 99 the main account carries the largest
pet collection, the deepest inventory, the most unlocked content. That is data the client has to
hold resident.

### The premium splits, and an accident proved it

Partway through the run the auto-clicker was left off, dropping in-game activity across the set.
This was unplanned and it separates two things the designed experiment could not:

| pid | clicker on (12:26-12:47) | clicker off (12:47-13:13) | drift |
|---|---|---|---|
| 25616 | 2401 MB | 2407 MB | +6 |
| 26504 | 2383 MB | 2389 MB | +6 |
| 42336 | 2500 MB | 2499 MB | -1 |
| 42344 | 2409 MB | 2414 MB | +5 |
| 45328 | 2361 MB | 2357 MB | -4 |
| **47072 (MAIN)** | **2915 MB** | **2861 MB** | **-54** |

The five alts drift within +/-6 MB, which is sampling noise. The main is the only client that
moved, and it moved down when the clicking stopped.

So the 514 MB premium is two components:

- **~454 MB persists with activity off.** Account state: inventory, pets, unlocked content. This
  is the dominant term and it is a property of the account.
- **~54 MB tracks activity.** Roughly 10% of the premium, and the first direct evidence that
  in-game activity costs measurable memory.

Both matter for calibration, and they compound in the same direction for the same users. See
Finding 4.

The process set was stable across the whole 58-minute run (6 distinct pids, no exits, no
relaunches) and available memory never fell below 3165 MB, so neither window is pressure-affected.

Strip the main out and the five alts span 2361-2500, a **139 MB band**. A single constant
describes alts perfectly well. It is main-grade accounts that break it.

### Scale of the variables, measured

| variable | effect |
|---|---|
| graphics quality, full slider | <= 64 MB |
| hardware (32 GB vs 47 GB, as claimed by the contaminated run) | 289 MB |
| **account: main vs alt, same machine, same settings** | **514 MB, stable** |

The variable the spec proposes to learn is smaller than the variable it does not model at all,
and the one it does not model is the only one that stayed stable enough to measure cleanly.

---

## Finding 4: the constant is calibrated on the wrong population

This is the finding that should change priorities, and it came from Este directly:

> "I play lightly. I bet those guys are playing pretty heavily. So if they have accounts that are
> as strong as my main across all of them [...] that would add up quickly."

Every number above comes from one light player's account set: one developed main and five thinner
alts. The clan members RoRoRo exists to serve play hard, and their alts are not thin. An account
set where *every* account is main-grade is the normal case for them, not the exceptional one.

The arithmetic, using this machine's own measurements:

| scenario | per client | 8 clients | 10 clients |
|---|---|---|---|
| all light alts | 2401 MB | 18.8 GB | 23.4 GB |
| current constant | 2650 MB | 20.7 GB | 25.9 GB |
| **all main-grade** | **2915 MB** | **22.8 GB** | **28.5 GB** |

Against the 16 GB machine the spec singles out (~12.9 GB usable): the constant predicts room for
4 clients; main-grade accounts fit 4 as well but with 1.1 GB less slack, and at 10 clients the
constant underestimates the real requirement by **2.6 GB**.

**The bias runs the wrong way.** The constant was measured on light accounts, so it underestimates
for heavy players. Heavy players are the ones running the most clients, on whatever hardware they
own, and are therefore the population most likely to oversubscribe. The warning is calibrated
loosest exactly where it needs to be tightest.

This is the same shape as Finding 1. Both failures make the numbers look safer in precisely the
population that is not safe.

### The two terms compound on the same users

Este's further hypothesis: a stronger account generates more in-game activity per unit time (more
clicks, more breaks, more entities and effects resolving), so cost grows *faster* than account
strength rather than in step with it.

The auto-clicker accident in Finding 3 gives this its first direct support. Activity is worth
about 54 MB on a developed account, measured. Small against 454 MB of account state at one light
player's click rate, but it is not zero, and it is the term that scales with how hard someone
plays.

Both terms load onto the same population:

| | light player (measured here) | heavy player (extrapolated) |
|---|---|---|
| accounts at main grade | 1 of 6 | most or all |
| clicker running | intermittently, 1 account | continuously, every account |
| per-client account state | 2401 MB alt / 2861 MB main | ~2861 MB across the set |
| per-client activity term | ~54 MB on one client | ~54 MB on every client |
| **8 clients** | **~19.0 GB** | **~23.3 GB** |

The 54 MB term is measured at one player's click rate on one account. Whether it stays at 54 MB
at a heavy player's rate, or grows with it, is the open question, and it is the one worth
measuring next. Nothing here distinguishes "bigger inventory" from "busier scene" as the driver.
Testing it needs a heavy player's account set, not this one.

---

## What this means for the spec

The adaptive-footprint scope proposes learning **this machine's** typical client footprint. The
measurements say the machine is not the entity that varies.

- Machine-to-machine: 289 MB, and that figure comes from a contaminated run, so it is unproven.
- Account-to-account on one machine: 514 MB, stable, clean, reproducible.

`MemoryWatchdog` already samples per-account and already holds the account id. **Learning
per-account is strictly more accurate than per-machine and no harder to build.** The data is
already in the right shape; only the key changes.

Per-account learning also dissolves the cold-start problem. A machine must accumulate clean
samples across a whole session to learn anything, and a squeezed machine never will. An account
carries its learned value forward from every session it has ever run, including sessions on a
comfortable machine, and applies it the first time it launches on a tight one.

---

## Recommendations

1. **Re-key the learner from machine to account.** The single change that matters. Everything
   below is smaller.
2. **Guard the measurement script.** Exclude below-reserve samples from the reported median and
   peak, or refuse to report when too few clean samples remain.
3. **Print the pressure fraction** in the paste-back block. "99% of samples taken below reserve"
   would have stopped Run A's verdict at a glance.
4. **Record graphics quality and the account set** with every measurement. The original 2650/3280
   is missing both fields, which is why it cannot be compared against anything.
5. **Do not lower `ExpectedClientMb = 2650`.** Clean, at max graphics, this machine's median alt
   is 2425 and its main is 2915. The constant sits between them and errs toward warning early,
   which is the correct direction for a forecast that advises rather than blocks.
6. **Make the pre-launch advisor account-aware.** `LaunchHeadroomAdvisor.Evaluate` multiplies
   `ExpectedClientMb x N`, treating all N as identical. Main + 5 alts costs ~14.9 GB; six alts
   costs ~14.4 GB. Small at six, material at ten, and larger for anyone whose accounts are all
   developed.
7. **Get a heavy player's numbers before tuning anything.** One clan member running the script for
   an hour would test Finding 4 directly and is worth more than any further run on this machine.

---

## What verified cleanly

Independent of the footprint question, this session confirmed two things PR #96 set out to fix.

**F-082, the headroom trigger.** Predicted reserve 2613 MB from the arithmetic; actual 2615 MB.

```
[WRN] memory headroom crossed: 545 MB free, below the 2615 MB reserve
      (8 client(s) holding 14579 MB)
```

The footer rendered `8 Roblox clients running - [warn] 14.2 GB`. This is the case that was
structurally silent before #96: eight plateaued clients produce no growth for the projection axis
and no single anomaly for the per-client cap, so both older axes stayed quiet while the machine
ran out of memory.

**F-081, exit logging.**

```
RobloxPlayerBeta pid 45924 exited for account "a2a37ba8-..." (exit code 0, up 43m10s)
```

Exit code 0 and a 43-minute uptime identify a user-initiated close. That is exactly the
distinction whose absence produced a wrong conclusion on 2026-08-07, when three user-closed
clients were read as Roblox's updater killing them.

**Also observed:** available memory recovered from 78 MB to ~2340 MB within two minutes of the
launch storm settling, with no client exiting. A latch firing on the spike alone would re-arm
repeatedly against a machine that is no longer in trouble. The existing deadband is doing more
work than the four-crossings shape from the Aug 1 logs suggested.
