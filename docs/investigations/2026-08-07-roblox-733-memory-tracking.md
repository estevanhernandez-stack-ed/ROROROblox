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

## RETRACTED 2026-08-08: the "updater kill" conclusion was wrong

**The three clients that exited at 09:40 were closed by Este.** They had hit a captcha on
reconnect and never got back into a game, so he closed them manually. Nothing terminated them.

**How the error was made, because the mechanism matters more than the mistake.** I asked Este
directly whether he had closed those three, then answered my own question from logs rather than
waiting for him. The inference was: *RoRoRo issued no stop command, therefore the user did not
close them.* That is invalid. RoRoRo's stop log covers only RoRoRo's **own** stop path — closing
a Roblox window directly never touches our app, and produces exactly the bare `pid exited` line
we observed. The absence of a stop command was never evidence of anything.

The installer really did run at 09:40:37–40, and three clients really did exit at 09:40:32/37/41.
The correlation was real and coincidental. Three sources agreeing on a timestamp confirmed only
**when**, never **why** — and I read "why" into it.

**What survives, and what does not:**

| claim | status |
|---|---|
| 733 did not raise per-client memory | **stands** — measured independently |
| No Roblox crash records in 21 days | **stands** — but now unremarkable, since no unexplained death occurred here |
| Roblox shipped two 733 builds on 2026-08-07 | **stands** |
| Roblox's updater terminates live clients | **RETRACTED — no evidence** |
| The clan's drop-outs are explained | **RETRACTED — still unexplained** |

**The one real new clue, from Este:** those three hit a **captcha on reconnect** and could not
get back into a game. That is worth chasing — this repo already has captcha history (the
AppStorageDefender work in v1.4.2.0). A reconnect-captcha loop looks nothing like a crash, but to
a user whose alt never comes back it may well be the same complaint.

**And this is a second, better argument for F-081.** Had the exit line carried a reason — even
just "process exited with code 0 after a window close" versus "terminated externally" — this
error would have been impossible to make. The gap did not merely cost a morning of
reconstruction; it let a wrong answer look confirmed.

---

*The section below is preserved as originally written, for the reasoning trail. Its conclusion
is withdrawn.*

## RESOLVED, same day: it is Roblox's updater killing live clients

The open question — window vanishes, or stays up with an error? — came back **vanishes**.
That rules out disconnect and points at process death. Windows records every application
fault, so the evidence was already on disk.

**There are zero Roblox crash records on this machine in 21 days.** No `Application Error`,
no WER archive, no dump. A process that dies without a fault record was not crashing; it was
terminated.

### What actually happened

Roblox shipped **two client builds today**, an hour and three-quarters apart:

| build | version dir | installed (local) |
|---|---|---|
| `0.733.0.7330989` | `d584fb6c…` | 07:51 |
| `0.733.603.7330990` | `7d4de67b…` | **09:40** |

Three clients died at the second install, and the three sources line up to the second:

```
09:40:31   Roblox session log ACEC8 stops     (no teardown marker)
09:40:32   RoRoRo: pid 44112 exited for account c30a1f44
09:40:36   Roblox session log 921FF stops     (no teardown marker)
09:40:37   Roblox installer: "Reporting Installer Start"
09:40:37   RoRoRo: pid 31684 exited for account f9c5eee7
09:40:40   Roblox installer: "Reporting Installer Success"
09:40:41   RoRoRo: pid 53392 exited for account caa05bf6
```

A clean Roblox exit writes `RobloxStarter destroyed` / `User exit app`. None of the three
did. **RoRoRo issued no stop command** — the log shows it only observing the exits — and
Este did not close them.

### Why this fits every observation

- **Window vanishes, no error** — nothing errored; the process was terminated.
- **No crash record** — it was not a fault.
- **Every account manager equally** — this is Roblox's own updater, entirely outside any manager.
- **Started with 733** — 733 pushed at least two builds inside two hours. Each push takes out
  live clients. More hot-fix builds means more kills, which reads exactly as "accounts
  dropping out sooner."
- **Memory reads normal** — memory was never the mechanism.

### One observation held loosely

It killed the three **highest-memory** clients (3236–3280 MB) and left five smaller ones
(2578–2749 MB) alive. That may be memory-related, or it may be that those three were simply
the newest, launched at 08:37–08:39 into busier worlds. **Not enough evidence to claim a
selection rule** — recorded so a future occurrence can confirm or kill it.

## Conclusion (revised 2026-08-08)

**733 did not raise per-client memory.** That result was measured and it stands. The release
note was a coincidence — precisely the trap it was set up to be.

**The drop-outs remain unexplained.** The updater-kill answer was withdrawn (see the retraction
above). No unexplained client death has been observed on this machine. The next real lead is the
captcha-on-reconnect behaviour, not memory and not the updater.

## What this exposed about our own instrumentation

`RobloxProcessTracker.cs:385` logs **that** a client exited — pid and account id. That line is
what pinned the timing here, so it earned its keep. What it does not carry is **why**: no exit
code, no last-known private bytes, no session duration.

*(An earlier version of this section, and of F-081, said we "record nothing when a client ends."
That was wrong, and it mispriced the fix as a new teardown path. It is one enriched log
statement.)*

Because of that gap, answering this took a 45-minute live test plus Windows event logs plus
Roblox's own client logs, reconstructed by hand. With exit code, last memory reading, session
duration — and a distinct line when several clients exit inside a few seconds, which is the
updater-kill signature — the same question would have been a grep.

## What to tell the clan

It is not their PC, not their RAM, and not their account manager. Roblox is shipping frequent
client updates and each one closes running clients. The practical mitigation is the same one
that already exists for the memory case: relaunch after an update lands. Worth saying plainly,
because the natural assumption is that something on their end is broken.
