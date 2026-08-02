# Settings quiet window — make per-account FPS caps survive close-together launches

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

### Sequence

Per launch, inside the existing `_launchGate` semaphore:

1. Read the cap the file currently holds.
2. If it already equals this account's cap → write nothing, launch, **return immediately**.
3. Otherwise: write the cap, launch, then wait until the file has been unmodified for
   `QuietDebounce`, capped at `QuietWaitTimeout`.
4. Timeout releases anyway. A slow launch must never be aborted — same rule as before.

### Constants

| Name | Value | Basis |
|---|---|---|
| `QuietDebounce` | 2 s | Longest observed gap between consecutive client writes is 3.25 s (+5.93 → +9.18); 2 s risks an early release on a slow machine, so see Open Questions. |
| `QuietWaitTimeout` | 30 s | Matches the existing ceiling. Observed settle is ~9–12 s. |
| `QuietPollInterval` | 100 ms | `FileSystemWatcher` is preferred; polling `LastWriteTime` is the fallback. |

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
> finish loading before starting the next — about 10 seconds each. Set every account to the
> same cap to launch at full speed.

Shown once per launch set when caps differ, not per account. Not a blocking modal — an
inline banner on the launch surface. The user chose this trade; do not make them re-confirm it.

## Non-goals

- **Fixing `ClientAppSettingsWriter`.** It targets the wrong version folder (mtime instead of
  the registered launcher path at `HKCU:\Software\ROBLOX Corporation\Environments\roblox-player`),
  and separately, Roblox denies `DFIntTaskSchedulerTargetFps` as local configuration. Both are
  real; neither is this spec. Tracked in `docs/features.md`.
- **Per-account graphics quality.** Same shared file, same race, already measured as a
  non-lever for memory. Out of scope.
- **Removing per-account caps.** Considered and rejected — Este chose per-account with a
  warning over a single global cap.

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

- **Is `QuietDebounce = 2 s` enough?** The largest observed inter-write gap is 3.25 s, which is
  *longer* than the proposed debounce — meaning a 2 s debounce could have declared quiet during
  the +5.93 → +9.18 s gap and released early. This needs resolving before implementation:
  either raise the debounce above the largest observed gap with margin (4 s), or gate the wait
  on a second condition. **Do not implement 2 s on the strength of this document.**
- Does a fully-started client ever write the file again mid-session (user changes an in-game
  setting)? If so, a later launch's wait could be extended by an unrelated client. Bounded by
  the timeout, but worth knowing.
- Does the quiet window hold for three or more simultaneous launches, where several clients
  are in their writeback storm at once?
