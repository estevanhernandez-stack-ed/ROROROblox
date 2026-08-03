# Launch gate smoke test — negative result, and a version-folder targeting bug

> **Revision note (same day).** The first version of this document blamed a Roblox client
> update shipped this morning. That was wrong — the evidence showed a version *already
> installed the day before* becoming the active launch target. Cause B is rewritten below;
> the withdrawn claim is preserved in the git history of this file, not silently deleted.
> Credit to Este for rejecting the conclusion on the grounds that no update had visibly
> fired. The lesson is the standing one: a clean before/after correlation in a log is not
> a cause, and "a version string changed" is not the same fact as "a new version shipped."

**Date:** 2026-08-02
**Build under test:** `fix/launch-gate-condition-based` @ `31af029`, Debug, `Core.dll` built 14:40:40 (verified to contain `WaitForNewClientAsync`, `NewClientWaitOutcome`, `SnapshotBeforePids`, `HoldForNewClientAsync`)
**Related:** PR #70, `docs/superpowers/specs/2026-08-01-launch-gate-condition-based-design.md`

## Verdict

**The condition-based launch gate did not fix the wrong-FPS-cap bug.** The 2026-08-01
repro still reproduces on the gate build.

Three findings. **Two of the three are ours**, and the one that is not cannot be dated
from the available evidence.

- **A — the settle grace is too short.** Ours. Confirmed by artifact, not just timing.
- **B — FFlag writes can land in a version folder Roblox is not launching from.** Ours,
  previously invisible, and it masked C for at least four days.
- **C — Roblox denies the local FPS FFlag when it does arrive.** Theirs. Duration unknown.

---

## Cause A — the settle grace is too short (ours)

The gate waits for a new `RobloxPlayerBeta` pid, then holds `SettleGrace` (1 s) before
releasing. That is anchored far better than the old fixed 250 ms hanging off
`Process.Start`, but it still fires **before the client has read its settings.**

Measured on this run:

| Event | este (pid 33608) | CElCPapa (pid 43324) |
|---|---|---|
| Process appears (`Get-Process` StartTime) | 15:41:19 | 15:41:20 |
| Client init t=0 (from its own log) | 15:41:21.000 | 15:41:23.000 |
| Config read (`DFIntTaskSchedulerTargetFps`, t+0.255 s) | 15:41:21.255 | 15:41:23.255 |
| **Process start → config read** | **~2.25 s** | **~3.25 s** |

The gate released este's launch at 15:41:20.166 — roughly 1.0 s after its pid appeared,
exactly as designed. CElCPapa's dispatch began at 15:41:20.545 and its settings write
landed somewhere in 15:41:20.5–21.8. **Este did not read its config until 15:41:21.255**,
so the overwrite arrived first.

The gap between "pid exists" and "client has read its settings" measured **2.25 s and
3.25 s** on a warm machine. `SettleGrace` is 1 s. The window the gate was built to close
is still open by 1–2 seconds.

Note the shape of the error: the spec correctly identified that `Process.Start` returning
is not the event we care about. We then replaced it with *pid appears + a fixed 1 s*,
which is a better proxy anchored to a real event — but still a fixed delay against an
unbounded, machine-dependent interval. Same class of mistake, one step further along.

`Get-Process` StartTime has ~1 s granularity, so treat 2.25 / 3.25 as ±1 s. The direction
is unambiguous: the read happens seconds after the pid, not within one.

## Cause A — direct artifact evidence

The race is not inferred from timing alone. The file itself proves it.

`version-18b8f177aa0f4699\ClientSettings\ClientAppSettings.json`, last written
**15:41:20** — CElCPapa's launch — contained exactly:

```json
{
  "DFIntTaskSchedulerTargetFps": 20
}
```

Este's client read that file at **15:41:21.255**, 1.1 s later. It read CElCPapa's value.
Este is configured "unlimited," which per `ClientAppSettingsWriter`'s contract should emit
`FFlagTaskSchedulerLimitTargetFpsTo2402` above 240 — that key is absent entirely. Este's
write was clobbered wholesale before its own client ever opened the file.

## Cause B — CORRECTED: no Roblox update fired; our writes were landing in a dead folder

**An earlier revision of this document claimed Roblox shipped a client update at ~10:28
today that locked `DFIntTaskSchedulerTargetFps` against local override. That claim is not
supported by the evidence and is withdrawn.**

What the evidence actually shows:

| Version folder | Product version | Installed | Has `ClientAppSettings.json`? |
|---|---|---|---|
| `version-145f189a6a974303` | 0.732.23.7321040 | 7/29 08:42 | **no** |
| `version-18b8f177aa0f4699` | 0.732.598.7321379 | **8/1 07:55** | **yes (ours)** |

`0.732.598` was installed **the day before**, not this morning. The 10:28 installer run
downloaded nothing — `File already exists for dc589dfc… Skipping download` — and was a
**relaunch/re-register**: `-relaunch version-18b8f177aa0f4699 -background -channel
zrbxtelemetry-rearch-flag-removal`, ending with
`Set the launcher for Roblox as: …\version-18b8f177aa0f4699\RobloxPlayerBeta.exe`.

So what changed at 10:28 was **which version folder Roblox launches from**, not the client
build and not Roblox's flag policy.

That reframes the denial completely. `ClientAppSettingsWriter.NewestActiveVersionFolder`
picks its target by newest `RobloxPlayerBeta.exe` write time, which resolved to
`0.732.598` from 8/1 onward. But until 10:28 Roblox was still *launching* `0.732.23` —
a folder with no `ClientAppSettings.json` at all.

**Before 10:28 our FFlag writes were landing in a folder the running client never read.**
No local config existed for the running version, so nothing was denied, so nothing was
logged. The denial did not start this morning; **our writes only started reaching a
running client this morning.** How long Roblox has denied this flag is not determinable
from these logs, and this document makes no claim about it.

The user's read was correct: this is our side.

### The real our-side bug: version-folder targeting can miss

`NewestActiveVersionFolder` resolves by file mtime. Roblox resolves by whatever its
installer last registered. Those diverged for roughly four days (8/1 07:55 → 8/2 10:28),
during which every FFlag write RoRoRo made was silently discarded into an inactive folder
with no error, no log line, and no user-visible symptom.

That is worth its own fix independent of the launch gate: read the registered launcher path
(the installer writes it, and `UpdateController` lines in the client log confirm it) rather
than guessing from mtime.

## Cause C — the FFlag is denied when it does arrive (theirs, timing unknown)

Both clients logged:

```
"DFIntTaskSchedulerTargetFps": 20
Warning [FLog::FlagFetchingStarterModule] Denied local configuration for: DFIntTaskSchedulerTargetFps
```

Roblox refuses the local `ClientAppSettings.json` override for this flag. It is the only
denial in the log — no other key RoRoRo writes is rejected.

Every client run on this machine today:

| Time | Client version | Local file present for that version? | Denial? |
|---|---|---|---|
| 08:26 | `0.732.23.7321040` | no | no |
| 10:14 | `0.732.23.7321040` | no | no |
| 10:20 | `0.732.23.7321040` | no | no |
| 10:25 | `0.732.23.7321040` | no | no |
| **10:28** | **installer re-registers 0.732.598 as the launch target** | — | — |
| 10:28 | `0.732.598.7321379` | yes | **denied** |
| 10:30 | `0.732.598.7321379` | yes | **denied** |
| 15:41 | `0.732.598.7321379` | yes | **denied** |
| 15:41 | `0.732.598.7321379` | yes | **denied** |
| 15:42 | `0.732.598.7321379` | yes | **denied** |

The correlation is real but the middle column is the explanation, not the version column.
Every "no denial" row is a run where **no local flag file existed for the version being
launched** — so there was nothing to accept or deny. The moment our writes and Roblox's
launch target aligned, the denial appeared on every single run.

**This document therefore makes no claim about when Roblox started denying this flag.**
It may be brand new; it may be months old. We could not have observed it before today
because our flag was never reaching a running client. Establishing the duration would
need a deliberate test — drop a hand-written `ClientAppSettings.json` into an older
version folder and launch it directly — which has not been run.

**What still works:** `GlobalBasicSettings_<N>.xml`'s `<int name="FramerateCap">` — the
lever the 2026-05 investigation already identified as the one that actually governs
default-config users. On-disk after this run: `FramerateCap=20`, CElCPapa's value, written
last. That is consistent with both clients showing 20.

**Not yet measured:** *when* the client reads `GlobalBasicSettings`. The 0.255 s figure
above is the `ClientAppSettings` read. The `GlobalBasicSettings` read time is unknown and
could be earlier or later. This matters, because it — not the FFlag read — is the interval
the settle grace actually needs to cover.

---

## What this means for PR #70

The branch is **not wrong**, and its safety properties all held: no hang, no stall, correct
anchoring, correct multi-launch sequencing, clean mutex acquisition, automatic stray
cleanup. The gate did exactly what it was specified to do. The specification was
insufficient.

Options, in rough order of preference:

1. **Measure the real read, then anchor to it.** Instrument when a client opens
   `GlobalBasicSettings_<N>.xml` (Sysinternals Process Monitor, or a handle poll). Once we
   know the true event, decide whether to anchor on it or on a milestone that provably
   follows it.
2. **Anchor on a later observable milestone.** The client's `MainWindowHandle` becoming
   non-zero is a real, already-probed event that is definitively after early init. More
   honest than any fixed number, at the cost of a longer hold.
3. **Raise `SettleGrace` on the measured data** (≥ 3–4 s). Cheapest, keeps the current
   shape, and is still a guess — it would need to hold on cold-start and slow machines,
   where the interval is longer than anything measured here.
4. **Stop sharing the file during the window.** Structurally correct, largest change.

Merging PR #70 as-is is defensible — it is a strict improvement on the shipped behaviour
and its follow-ups are filed. But it must not be described in release notes as fixing the
wrong-FPS-cap bug, because it does not.

Fix B first, and independently. It is a plain bug with a plain fix, it costs nothing to
correct, and while it stands, every future FFlag measurement on any machine is suspect —
including any attempt to date finding C.

## Open questions

- When does the client read `GlobalBasicSettings_<N>.xml` relative to process start? This
  is the number the settle grace actually has to cover, and it is still unmeasured.
- How long has `DFIntTaskSchedulerTargetFps` been denied? Not determinable from these logs.
- Does `GlobalBasicSettings`' `FramerateCap` remain honoured, or is it being tightened too?
  It is doing all the real work today, so this is the load-bearing question.
- How often does the mtime-vs-registered-launcher divergence happen in the wild? Four days
  on one machine is one sample.

## Log this to the dashboard

Two entries, and note that the first supersedes anything logged earlier today about a
Roblox client update — **no such update occurred.**

1. **Gotcha discovered (ours).** `ClientAppSettingsWriter` targets the version folder with
   the newest `RobloxPlayerBeta.exe` mtime; Roblox launches whatever its installer last
   registered. These diverged 8/1 07:55 → 8/2 10:28, and every FFlag write in that window
   was silently discarded into an inactive folder — no error, no log, no symptom. Fix by
   reading the registered launcher path instead of guessing from mtime.
2. **Roblox-side compatibility event (theirs, undated).** Client `0.732.598.7321379`
   denies local configuration of `DFIntTaskSchedulerTargetFps`
   (`FLog::FlagFetchingStarterModule`). The FFlag path for the FPS cap does not work on
   this version. `GlobalBasicSettings_<N>.xml`'s `FramerateCap` remains the working lever,
   consistent with the 2026-05 finding. **We cannot say when this started** — finding 1
   hid it from us until today.

---

# Addendum — measured 2026-08-02 16:12–16:22

Four measurement runs on the live app. These change the diagnosis and point at a fix.

## Finding D — the first new pid is frequently not the client

`WaitForNewClientAsync` takes the **first** new `RobloxPlayerBeta` pid the probe returns
and treats it as "our client arrived." Observed across runs, that pid often is not.

| Run | First new pid | Real client | Gap |
|---|---|---|---|
| 16:12 | 60464 — exited, never got a window | 5692 (2462 MB, window) | 0.023 s |
| 16:20 | 15932 — 26 MB, no window, survived | 50828 (2511 MB, window) | **5.92 s** |

In the 16:20 run the gate would have latched pid 15932 at +0 s, held its 1 s settle, and
released **roughly five seconds before the real client process even started.**

The 26 MB windowless signature also appears when a client is *closed* — pids 62912, 38584,
58388 and 15932 all match it. So the probe can hand the gate a pid produced by the user
closing a different window, entirely unrelated to the launch in flight.

No unit test could have caught this: every test probe in the suite returns exactly one
well-behaved pid that appears once and stays. The author of the measurement script (me)
made the identical mistake twice before noticing.

## Finding E — the settle grace is off by 2–3x, confirmed three times

Process start → client reads its config, from the client's own logs:

| Run | Interval |
|---|---|
| 15:41 este | 2.25 s |
| 15:41 CElCPapa | 3.25 s |
| 16:17 este | 1.98 s |

`SettleGrace` is 1 s.

## Finding F — the client rewrites the shared file for ~12 s after launch

This is the one that reframes the bug. Watching
`%LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml` across a single launch:

```
change #1  16:20:58.362  (+0 s)      <- ours, pre-Process.Start
change #2  16:21:00.052  (+1.69 s)   <- client
change #3  16:21:03.879  (+5.52 s)   <- client
change #4  16:21:06.944  (+8.58 s)   <- client
change #5  16:21:10.423  (+12.06 s)  <- client
(then silent for the remaining 48 s of the observation window)
```

**A starting client does not read this file once — it re-persists its own value
repeatedly for about twelve seconds.** So the race runs in both directions:

- our write for account B lands before A's client reads → A gets B's cap (the original bug), and
- our write for account B lands during A's writeback window → **A's client overwrites it,
  and B reads A's cap.**

It also explains the 15:41 symptom exactly. Este read 20 before it could keep 9999, then
faithfully wrote 20 back. Everything converged on 20.

**The writes stop.** Twelve seconds of contention, then quiet. That quiet is the fix.

## The fix this points to

Stop inferring the safe moment from pids and fixed delays. **Anchor on the file itself
going quiet.** After a launch, watch `GlobalBasicSettings_<N>.xml` and wait until it has
been unmodified for a short debounce (~2 s). That single condition covers the client's
read *and* its writeback storm, needs no pid heuristics, and self-tunes to machine speed —
a slow cold start simply stays noisy longer.

Pair it with the free correctness win: **the race only exists when consecutive launches
want different caps.** If every account shares a value, the file already holds it and
there is nothing to protect — launch at full speed. Most users set one cap, so most users
pay nothing, and the ~12 s per-launch cost lands only on the configuration that actually
needs it.

Sequence per launch, when caps differ: write cap → launch → wait for the file to go quiet
→ next launch.

### What still needs proving

The debounce design is sound on one run's data. Before building it:

- Reproduce the quiet window with **differing** caps, which is the case that matters and
  the case no run so far has exercised (every run used matching values, so no write could
  be distinguished from another by content).
- Confirm 12 s is typical rather than lucky — cold start, a busy machine, and a
  three-account sequence.
- Confirm a fully-started client stays quiet, so the debounce cannot be defeated by an
  older client writing during a later launch.

If the quiet window turns out not to exist under load, the honest answer is a single
global cap rather than per-account, and the feature gets retired rather than patched.

---

# Decisive run — 2026-08-02 16:27, differing caps

First run where writes are attributable **by content**: este = 9999 (unlimited),
CElCPapa = 20. Launched close together, este first.

```
+  0.00s  WRITE  cap=9999      <- ours, for este
+  0.24s  PID    64968         <- este's client starts
+  2.80s  WRITE  cap=20        <- ours, for CElCPapa
+  2.97s  WRITE  cap=9999      <- este's client writes ITS value back, 170ms later
+  3.04s  PID    11240         <- CElCPapa's client starts, reads 9999
+  5.88s  WRITE  cap=9999
+  5.93s  WRITE  cap=9999
+  9.18s  WRITE  cap=9999
           (silent for the remaining ~80s of the window)
```

Observed result: **both clients uncapped.** Este ran its own correct value; CElCPapa —
configured 20 — ran este's.

## What this settles

**1. The competing writer is the previous client, not the next launch.** Our write for
CElCPapa survived **170 milliseconds**. No fixed delay, and no amount of tuning one,
survives that. The launch gate was built to win a race against the next `LaunchAsync`;
the actual opponent is a client that keeps writing for nine seconds after it starts.
That is why PR #70 could be correct in every detail and still not fix the bug.

**2. The bug is non-deterministic in both directions.** At 15:41 the second account's
value won and the first lost. Here the first won and the second lost. Whichever client
happens to read last gets whatever is in the file at that instant. A user can see the
symptom either way round, which also means bug reports about it will look inconsistent.

**3. Launch order does not determine client start order.** Este was launched first;
Este's client did start first this run — but in the user's report of an earlier attempt,
CElCPapa's window opened first despite launching second. `Process.Start` on a
`roblox-player:` URI returns once Windows accepts the URI; everything downstream runs on
Roblox's schedule. RoRoRo's semaphore serializes *our* work and nothing else. Any design
reasoning in terms of "the first client" or "the second client" is unsound.

**4. The quiet window is real and is the anchor.** Writes stopped at +9.18s and the file
stayed untouched for the remaining ~80 seconds. Had CElCPapa's 20 been written after that
quiet, no writer remained to clobber it.

## The fix, now measured rather than reasoned

- **Write cap → launch → wait for `GlobalBasicSettings_<N>.xml` to go quiet (~2s
  debounce) → next launch.** One condition covers the read and the writeback storm, and it
  self-tunes: a slow cold start simply stays noisy longer.
- **Only serialize when consecutive caps differ.** If the file already holds the right
  value there is nothing to protect. Most users set one cap and pay nothing.
- **Retire the pid-based gate.** It aims at the wrong event, and its first-new-pid
  heuristic is a coin flip regardless (gaps of 0.023s and 5.92s measured between the first
  new pid and the real client; the 26 MB windowless signature also appears when a client
  is *closed*).

## Cost, and the product question

~10 seconds per account, and only when consecutive caps differ. An eight-account squad
launch with mixed caps costs over a minute of staggering.

That is a real price for a feature whose value is unclear, and it deserves an explicit
decision rather than an implementation. **One global cap for all clients** removes the
race entirely, costs nothing, and may be what multi-instance users actually want. Per-account
caps should survive only if someone genuinely needs different values simultaneously.

---

# The quiet-window build also fails — and the measurement that fixes it (2026-08-02 evening)

The `fix/settings-quiet-window` branch was built, reviewed four times, and manually
verified. **It does not fix the bug either.** Here is what happened and what we now know.

## The failed verification

Three accounts, three different caps, launched close together on the branch build
(`5fc5cd2`):

| Account | Configured | Ended up running |
|---|---|---|
| estehernandez (1st) | Unlimited (9999) | **20** |
| CElCPapa (2nd) | 20 | **45** |
| ELeonDog (3rd) | 45 | 45 |

Each account got **the next account's value**. And there was no perceptible delay.

## The log says exactly why

```
20:20:10.765  A: "FPS cap 9999 already on disk; no write, no wait."   <- FAST PATH
20:20:11.132  A launches
20:20:12.914  B: quiet wait (pre-write) settled after 0.0004s          <- instant credit
              B writes 20
20:20:22.859  B: quiet wait (post-write) settled after 9.93s
20:20:23.140  B launches
20:20:23.140  C: quiet wait (pre-write) settled after 0.0002s
              C writes 45
20:20:34.881  C: quiet wait (post-write) settled after 11.74s
20:20:35.207  C launches
```

The file already held 9999 from earlier testing, so A took the fast path — wrote nothing,
waited nothing, launched at :11. A's *client* does not read the file until seconds later.
B wrote 20 at :12.9. **A read B's value.** Same shape B → C.

**The design error:** the settle protects *our write* from the previous client. Nothing
protects the *newly launched client's read* from the next account's write. And "the file
is quiet" turns out to be ambiguous in a way the design never accounted for — immediately
after a launch, quiet means the client **has not started writing yet**, not that it has
finished. So the next launch credits instant quiet and writes straight into the window
where the previous client is about to read.

Note the irony: **PR #70's post-launch hold was in the right position.** Its signal (the
first new pid) was a coin flip, so it never worked — but *where* it waited was correct.
This branch replaced a right-place/wrong-signal mechanism with a right-signal/wrong-place one.

## The measurement — is a client's first write-back proof that it has read?

The proposed fix is to hold after launching client N until N writes the file. That is only
sound if the client **reads before it first writes**. Tested directly:

Set the file to 9999 and the account to Unlimited, so RoRoRo takes its fast path and writes
nothing — making the first mtime change unambiguously the *client's*. The instant it
appeared, overwrite with a sentinel of 20 and watch what the client does next.

```
client pid 14524 started   20:44:11.490
CLIENT'S FIRST WRITE       20:44:14.370   +2.88s after process start, cap=9999
SENTINEL 20 written        20:44:14.420   +50ms after the client's write
client write #1            20:44:17.359   cap=9999    <- restored its own value
final cap on disk: 9999
```

**The client overwrote the sentinel with 9999.** It was holding 9999 in memory, which it
could only have obtained by reading the file before its first write-back. Confirmed
independently in-game: that client ran uncapped.

**Conclusion: a client's first write-back is a valid proof-of-read.**

### Two constraints the same measurement establishes

**1. The interval is not fixed.** First write-back landed at **+2.88 s** on this run and
**+7.07 s** on an earlier one — a 2.5x spread on the same machine, minutes apart. No
constant is correct. The hold must be condition-based, which this signal now permits.

**2. The signal alone is insufficient.** The client keeps re-persisting its own value for
seconds afterwards (measured earlier at ~9-12 s of storm). Writing the next account's cap
the moment the write-back appears would simply be clobbered.

## The corrected design

Per launch, when the next account's cap differs:

1. **Wait for at least one write by the launched client** — proves it has read.
2. **Then wait for quiet** — proves its write-back storm has finished.
3. Write the next account's cap.
4. Wait for quiet again and re-read to confirm it survived; retry on clobber.
5. Launch.

Steps 2-5 already exist and work — the post-write waits measured 9.93 s and 11.74 s and did
exactly their job. **Step 1 is the whole missing piece.** Concretely: the pre-write quiet
wait must not credit prior quiet when the previous action was a launch whose client has not
yet written. Everything else stands.

## Status of `fix/settings-quiet-window`

The branch is a genuine improvement on both halves it does cover, and deleting PR #70's pid
gate was correct regardless. **But it does not fix the wrong-FPS-cap bug and must not be
described as doing so.** The fast path — which is what keeps the feature affordable — is
precisely where the hole is.
