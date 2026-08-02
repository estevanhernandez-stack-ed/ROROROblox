# Settings quiet window — make per-account FPS caps survive close-together launches

> **Banner correction (2026-08-02, Task 4):** §"The warning" proposed the banner scoped to
> "once per launch set when caps differ, not per account" — implying it tracks whichever
> accounts are queued for the next launch. What shipped (`MainViewModel.FpsCapWarningText` /
> `RefreshFpsCapWarning()`) instead computes the mismatch over the **entire visible account
> roster** (`Accounts`), recomputed on load, add, remove, and cap-change — there is no
> "launch set" selection concept wired to it. Consequence: a user with ten accounts split
> across two caps sees the warning even on a launch that only touches accounts sharing one
> cap. Simpler to build and correct in the common case (most users set one cap roster-wide);
> narrowing it to an actual launch selection is a future refinement if it proves noisy.

**Date:** 2026-08-02
**Status:** Approved design.
**Supersedes:** `2026-08-01-launch-gate-condition-based-design.md` (targets the wrong writer).
**Evidence:** `docs/investigations/2026-08-02-launch-gate-smoke-test-negative-result.md`
**Severity:** Silent wrong behavior — no error, no log, the user's configured cap simply does not apply.

## The bug, in one paragraph

Roblox keeps **one** settings file per install — `%LOCALAPPDATA%\Roblox\GlobalBasicSettings_<N>.xml` —
and every client reads `<int name="FramerateCap">` from it at startup. RoRoRo writes each
account's cap into that file immediately before launching. A starting client does not read
it once: it **re-persists its own value repeatedly for roughly nine seconds**. So when two
accounts with different caps launch close together, one client's writeback lands on top of
the other account's value, and a client comes up wearing a cap it was never configured with.

## Why the previous design failed

The 2026-08-01 gate assumed the competing writer was *the next launch*, and held that next
launch until the previous client's process appeared. Measured on 2026-08-02:

```
+  0.00s  WRITE  cap=9999      <- ours, for este (unlimited)
+  0.24s  PID    64968         <- este's client starts
+  2.80s  WRITE  cap=20        <- ours, for CElCPapa
+  2.97s  WRITE  cap=9999      <- este's client writes ITS value back, 170ms later
+  3.04s  PID    11240         <- CElCPapa's client starts, reads 9999
+  5.88s  WRITE  cap=9999
+  5.93s  WRITE  cap=9999
+  9.18s  WRITE  cap=9999
           (silent thereafter)
```

Our write survived **170 milliseconds**. Gating the next launch cannot help when the
overwriting party is a client that already started. Observed result: both clients uncapped,
CElCPapa running este's value despite being configured 20.

The bug is **non-deterministic in both directions** — on 2026-08-01 the second account's
value won and the first lost; here the first won and the second lost. Whichever client reads
last gets whatever happens to be in the file. User reports of this will look inconsistent.

## Design

### The signal

**Wait for the settings file to go quiet.** After a launch, watch
`GlobalBasicSettings_<N>.xml` and consider the launch settled once it has been unmodified
for `QuietDebounce`. One condition covers both the client's read and its writeback storm,
and it self-tunes — a cold start or a loaded machine simply stays noisy longer, where any
fixed delay would be wrong in one direction or the other.

Measured basis: writes stopped at +9.18 s and the file stayed untouched for the remaining
~80 s of observation. Two earlier runs agree (~12 s, then quiet).

### The fast path — most users pay nothing

**The race only exists when consecutive launches want different caps.** If the file already
holds the value the next account needs, there is nothing to protect: skip the wait entirely
and launch at full speed. Most users set one cap across every account and never pay a cent
of latency.

This is not an optimization bolted on afterwards — it is what keeps the feature shippable.
Without it, every squad launch pays for a case almost nobody is in.

### Sequence — verify, do not trust the constant

The wait belongs at the **start** of a launch, not the end, and the design **verifies** its
own write rather than assuming the debounce was long enough.

Per launch, inside the existing `_launchGate` semaphore:

1. Read the cap the file currently holds.
2. **Fast path:** if it already equals this account's cap → write nothing, wait for nothing,
   `Process.Start`, return. This is the common case.
3. Otherwise, up to `MaxWriteAttempts` times:
   a. Wait until the file has been unmodified for `QuietDebounce` (bounded by `QuietWaitTimeout`).
   b. Write this account's cap.
   c. Wait `WriteConfirmWindow`, then re-read.
   d. If the file still holds our value → done, proceed to launch.
      If it was overwritten, a previous client is still settling → loop.
4. `Process.Start`.
5. On exhausting attempts or the timeout, **launch anyway** with the value we last wrote.
   A slow or contended launch must never be aborted — same rule as the previous design.

Why this shape matters: correctness no longer depends on `QuietDebounce` being large enough.
If it is too small, step 3d catches the clobber and retries; the cost is latency, not a wrong
cap. Guessing exactly this class of constant is what produced `SettleGrace = 1 s`, and this
design is built so that guessing wrong degrades to *slower* rather than to *broken*.

### Constants

| Name | Value | Basis |
|---|---|---|
| `QuietDebounce` | 5 s | Must exceed the largest observed gap **between** consecutive client writes (3.25 s, +5.93 → +9.18) with margin. Not correctness-critical — step 3d is the backstop. |
| `WriteConfirmWindow` | 1 s | Our write survived 170 ms before being clobbered in the decisive run; 1 s covers that with headroom without adding much cost. |
| `QuietWaitTimeout` | 30 s | Matches the existing ceiling. Observed settle is ~9–12 s. |
| `MaxWriteAttempts` | 3 | Bounds worst case at roughly `3 × (QuietDebounce + WriteConfirmWindow)`. |
| `QuietPollInterval` | 100 ms | `FileSystemWatcher` is preferred; polling `LastWriteTime` is the fallback. |

**Expected cost when caps differ:** ~10–15 s per account. Fast path: zero.

### What this replaces

`WaitForNewClientAsync`, `NewClientWaitOutcome`, `SnapshotBeforePids`, `NewClientPollInterval`,
`NewClientWaitTimeout`, and `SettleGrace` all go. They aim at the wrong event, and the
pid-difference probe is independently unsound: the first new `RobloxPlayerBeta` pid is
frequently not the client (gaps of 0.023 s and 5.92 s measured), and the same 26 MB windowless
signature appears when a client is *closed*, so an unrelated window closing can satisfy it.

`HoldForNewClientAsync` — the single shared helper extracted in PR #70 — is the seam. Its
internals get replaced; both launch paths keep calling it unchanged.

## The warning

When two or more accounts in a launch set have **different** caps, tell the user before they
wait. Clan-facing voice: plain, no jargon, no apology, and it names the way out.

> **Different FPS caps will slow your launches.**
> Roblox keeps one shared settings file for every client, so RoRoRo waits for each account to
> finish loading before starting the next — about 15 seconds each. Set every account to the
> same cap to launch at full speed.

Shown once per launch set when caps differ, not per account. Not a blocking modal — an
inline banner on the launch surface. The user chose this trade; do not make them re-confirm it.

**Say 15 seconds, not 10.** The measured settle is ~9–12 s, plus the debounce and confirm
window. Promising 15 and delivering 12 is the right direction to be wrong in — a user who
was told 10 and waits 14 thinks the app has hung.

## Non-goals

- **Fixing `ClientAppSettingsWriter`.** It targets the wrong version folder (mtime instead of
  the registered launcher path at `HKCU:\Software\ROBLOX Corporation\Environments\roblox-player`),
  and separately, Roblox denies `DFIntTaskSchedulerTargetFps` as local configuration. Both are
  real; neither is this spec. Tracked in `docs/features.md`.
- **Per-account graphics quality.** Same shared file, same race, already measured as a
  non-lever for memory. Out of scope.
- **Removing per-account caps.** Considered and rejected — Este chose per-account with a
  warning over a single global cap.

## Logging

`RobloxLauncher` currently has **no `ILogger`**, which is why the previous design's outcome was
discarded and why today's entire investigation had to be reconstructed from Roblox's logs and
file timestamps rather than our own. Add one, and log:

- **Fast path taken** (debug) — file already held the right cap, no wait. Confirms in a support
  bundle that a fast launch was correct rather than merely lucky.
- **Quiet wait** (info) — how long it took, and whether it ended in quiet or in timeout.
- **Write clobbered, retrying** (warning) — attempt number. This is the race being caught and
  beaten; if it appears often, the debounce is too low.
- **Attempts exhausted** (error) — the launch proceeds with a cap that may be wrong. This is the
  only path where the original bug can still reach a user, so it must be impossible to miss.

Never log a cookie or any account identifier beyond the existing account GUID.

## Testing

- **Unit:** the quiet-wait primitive against an injected clock and a fake file-change source —
  same `TimeProvider` discipline as the rest of the codebase. No test sleeps in real time; no
  test may fail by hanging.
- **Unit:** the same-cap fast path performs no write and no wait. Prove it discriminates by
  mutation.
- **Unit:** timeout releases rather than aborting.
- **Manual, and required — no automated test substitutes for it:** two accounts with
  *different* caps, launched close together, each verified in-game. Every automated check
  passed on the previous design while the bug remained. Values must differ, or the run proves
  nothing — that mistake cost a full smoke-test cycle on 2026-08-02.

## Open questions

- **RESOLVED — `QuietDebounce` is no longer correctness-critical.** An earlier revision proposed
  2 s, which is *shorter* than the largest observed inter-write gap (3.25 s) and would have
  declared quiet during the +5.93 → +9.18 s window. Rather than hunt for a safe constant, the
  sequence now writes, confirms, and retries on clobber (§Sequence step 3). The debounce is set
  to 5 s for latency, and being wrong costs time rather than correctness.
- **Does `MaxWriteAttempts = 3` ever exhaust in practice?** If it does, the launch proceeds with
  a possibly-wrong cap and the user gets the old bug silently. This must be logged loudly
  (see Logging) so it surfaces in a support bundle rather than as "sometimes my FPS is wrong."
- Does a fully-started client ever write the file again mid-session (user changes an in-game
  setting)? If so, a later launch's wait could be extended by an unrelated client. Bounded by
  the timeout, but worth knowing.
- Does the quiet window hold for three or more simultaneous launches, where several clients
  are in their writeback storm at once?
