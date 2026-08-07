# Roblox 733 "Improves memory-tracking system" — did it cause the drop-outs?

**Date:** 2026-08-07 · **Client build:** `version-d584fb6c717a43d9` (733, landed on this
machine 07:51) · **RoRoRo:** v1.16.0

## The report

Clan members reported accounts dropping out sooner, starting roughly with 733. Users of
*other* account managers reported the same, so the cause was assumed to be Roblox-side
rather than RoRoRo. Release 733's top live note reads, verbatim: **"Improves
memory-tracking system."**

Working hypothesis (Este): the memory-tracking subsystem is a documented memory consumer,
so a change intended as an improvement could have regressed for some configurations — and
Roblox's aggregate telemetry would not see a regression concentrated in the small
population running six-plus clients.

That is a coherent hypothesis with a plausible reason it would ship and a plausible reason
it would be invisible upstream. It is also **not what the data shows on this machine.**

## Result: no memory regression detected

45-minute run, 8 clients, sampled every 30s (the watchdog's own 15-minute cadence gives
~3 points, which cannot separate a raised floor from a steeper slope).

| measure | pre-733 baseline | 733 run | read |
|---|---|---|---|
| starting MB per client | 2267–2679 (median first-seen) | **2532** median | dead centre — floor NOT raised |
| growth MB/hr | **883** (Aug 2, the one clean fresh-session sample) | **628** median (sampler), 335 (watchdog), range 235–1012 | inside the old client's range |
| closures in 45 min | — | **zero** | — |

If 733 had added fixed per-client tracking overhead, the starting number is what would have
moved. It did not.

## A flaw in the first comparison, recorded because it cut against the first answer

The initial matched-window script bucketed the baseline by *"first 45 minutes each account
was **observed**"* — which is not the first 45 minutes of its **session**. On Aug 1 the
watchdog picked up clients that had already run for hours. The tell is in the numbers:
slopes of **−27, +10, −27 MB/hr** (a plateaued client) and peaks of 4504 MB (not where a
client sits 45 minutes in).

Comparing today's fresh launches against those would have produced a large fake regression
and "confirmed" the hypothesis. **The only honest pre-733 comparison point is the Aug 2
sample, and 733 is below it.**

## The stronger negative: they did not close

Extended observation to **1h02m**, 8 clients, and — importantly — **with no auto-clicker
running**. The accounts reset in place from idle, but **not one process died**. Total
private bytes across clients: 23.0 GB.

Under the reported symptom this should have been the easy case to reproduce. It did not
reproduce.

## Confounders, stated rather than buried

- **Client count differs.** Baseline was 2–3 clients; this run was 8. Available memory went
  26.7 GB → 13.5 GB. At that pressure Windows trims working sets for reasons unrelated to 733.
- **"First-seen" is not launch time** — it is the first 15-minute watchdog tick after a
  client appears, so up to 15 minutes into a session. Biases both sides similarly.
- **Launch order and world matter more than age.** The 08:37/08:39 follow-friend joins were
  the *highest* memory (3236–3280 MB) despite being the *youngest*. Per-client memory tracks
  what world the client landed in, not how long it has been up.
- **One machine, one game.** A regression that only appears on other hardware or other
  experiences is not excluded.

## Conclusion and where to look next

On this machine, on this game, **733 did not raise per-client memory and did not close
clients**. The release note is most likely a coincidence — which is precisely the trap it
was set up to be: the top note mentioned memory, and the symptom sounded like memory.

The drop-outs are real and cross-manager, so something changed. It does not look like RAM.

**The unanswered diagnostic question, and it is cheap:** when an account drops, does the
Roblox window *disappear*, or does it stay open showing an error? Gone means a crash.
Still-there-with-a-message means a disconnect or session expiry — a different investigation
entirely, and one that would explain why memory reads normal.

## What this exposed about our own instrumentation

**RoRoRo logs when a client starts and nothing about why it ended.** We watch the process
vanish and record nothing — no exit code, no last-known memory, no elapsed session. That is
why this question needed a live test instead of being answerable from logs already on disk.

Filed as a follow-up: log exit code + last-known memory + session duration at teardown. It
makes every future "Roblox broke something" question measurable retroactively.
