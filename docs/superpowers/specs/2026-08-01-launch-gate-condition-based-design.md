# Condition-based launch gate — per-account settings must survive close-together launches

**Date:** 2026-08-01
**Status:** Approved design. Fixes a defect shipping in v1.12.0.0 and earlier.
**Severity:** Silent wrong behavior — no error, no log, the user's configured value simply does not apply.

> ## 🛑 SUPERSEDED (2026-08-02) — this design targets the wrong writer; do not build from it
>
> **This document's central premise is wrong.** It assumes the thing that overwrites a
> per-account FPS cap is *the next launch*, and designs a gate to hold that next launch
> until the previous client has started. Measurement on 2026-08-02 showed the competing
> writer is **the previous client itself**, which re-persists its own value to the shared
> `GlobalBasicSettings_<N>.xml` repeatedly for roughly nine seconds after it starts.
>
> The decisive run: our write for the second account survived **170 milliseconds** before
> the first account's client wrote its own value back over it. The second client then read
> the first account's cap. No fixed delay, and no gate on the next launch, survives that —
> the opponent is not the thing being gated.
>
> Consequently the mechanism this spec describes (`WaitForNewClientAsync`, `SettleGrace`,
> the pid-difference probe) **does not fix the bug it was written for**, and was measured
> not to. Two further defects in it, also measured:
>
> - The first new `RobloxPlayerBeta` pid is frequently not the client. Gaps of 0.023 s and
>   5.92 s were observed between the first new pid and the real one, and the same 26 MB
>   windowless signature appears when a client is *closed* — so the probe can be handed a
>   pid from an unrelated window closing.
> - `SettleGrace` (1 s) is 2–3× short of the measured process-start → config-read interval
>   (1.98 s, 2.25 s, 3.25 s).
>
> **Replacement design:** `docs/superpowers/specs/2026-08-02-settings-quiet-window-design.md`
> — anchor on the settings file going quiet, not on pids or delays.
>
> **Evidence:** `docs/investigations/2026-08-02-launch-gate-smoke-test-negative-result.md`
>
> This document is kept intact because its *diagnosis* of the original 2026-08-01 symptom is
> accurate and its reasoning about `Process.Start` returning early is correct and still
> load-bearing. Only its conclusion about who the competing writer is turned out wrong.
> The two drift notes below concern the implementation that shipped from it and remain valid.
>
> ## ⚠️ Banner-correct (2026-08-02) — two drifts between this design and what shipped
>
> **1. Snapshot placement moved, deliberately, and the new placement is better.**
>
> **Originally proposed** (§Design, step 1): *"Snapshot `GetRunningPlayerPids()` before writing
> settings."* — i.e. at the top of the launch flow, ahead of the auth-ticket network round-trip.
>
> **Actually built:** the snapshot happens in `ExecuteLaunchAsync` / `ExecuteLegacyLaunchAsync`,
> immediately before `Process.Start`, **after** `GetAuthTicketAsync` returns
> (`RobloxLauncher.cs:198`, `:321`). This is not settling for a worse implementation — it closes a
> hole the original plan had: `GetAuthTicketAsync` is a network round-trip with unbounded latency.
> A Roblox client that appeared *during* that call (user double-clicked the desktop icon, an
> unrelated bootstrapper finished) would be absent from a "before" set captured ahead of it, and
> would then false-detect as the launch's own new client on `WaitForNewClientAsync`'s first poll —
> releasing the gate before the real client exists. Snapshotting after the round-trip, right before
> the URI actually fires, closes that window. Approved by two reviewers during the build; see the
> in-code comment at `RobloxLauncher.cs:190-197` for the full reasoning.
>
> **2. "No behavior change when the probe is absent" does not hold for non-`Started` results.**
>
> **Originally proposed** (§"Why these five properties matter"): *"No behavior change when the
> probe is absent. Existing call sites and tests that construct `RobloxLauncher` without a probe
> keep today's semantics exactly."*
>
> **Actually built:** `Failed`, `CookieExpired`, and `Limited` results release the gate immediately
> and never hold — not even the old fixed 250 ms `FFlagReadHold` — regardless of whether a probe is
> wired. Accepted on the grounds that a failed launch produces no client to protect, so holding for
> one serves no purpose. **This has zero production reach**: the shipped app always constructs
> `RobloxLauncher` with a live probe (`App.xaml.cs`'s `IRobloxLauncher` factory), so the "probe
> absent" case is test-only. Flagged here so a future reader comparing this doc to
> `HoldForNewClientAsync` doesn't read the discrepancy as a bug.

## The bug, observed live

Two accounts launched roughly one second apart on 2026-08-01:

- `estehernandez` — FPS configured **Unlimited** (`FpsPresets.Unlimited = 9999`)
- `CElCPapa` — FPS configured **20**

`estehernandez` ran at **20**. Confirmed by the user in-client, and `GlobalBasicSettings_13.xml` held `FramerateCap = 20` afterwards. The account's configured value was silently discarded.

This is not a don't-write default: `Unlimited` is a real value RoRoRo writes. The second account's write landed before the first client had read the file.

## Root cause: the hold is anchored to the wrong event

`RobloxLauncher` serializes launches behind `_launchGate` and holds for `FFlagReadHold` (250 ms) so the launched client can read its settings before the next write:

```csharp
var result = await ExecuteLaunchAsync(cookie, target, browserTrackerId);
await Task.Delay(FFlagReadHold);          // 250 ms, measured from here
```

`ExecuteLaunchAsync` ends at `Process.Start` on a `roblox-player:` URI. **That returns when Windows accepts the protocol-handler invocation** — not when `RobloxPlayerBeta` exists, and nowhere near when it has read `GlobalBasicSettings`.

Between those moments: the shell resolves the protocol handler, the Roblox bootstrapper starts, it may run an update check, and only then does the real client process start and read its settings. That gap is **seconds, unbounded, and variable** with cold start, disk speed, and machine load.

**So no fixed delay is correct.** 250 ms was too small; 3 s would be a larger guess that still fails on a cold start. The constant is anchored to an event with an unbounded gap after it. This is the case for condition-based waiting rather than a timeout.

**Blast radius.** Both settings writers share the problem — `ClientAppSettings.json` (FFlags) and `GlobalBasicSettings_<N>.xml` (the user-facing settings file) are each machine-global and read once at client startup. **Squad Launch is the feature that launches accounts simultaneously**, so it is the worst affected, and it fails silently. Any future per-account setting written to either file inherits this race — which is a standing tax on the whole design direction, not a one-off.

## Design

`RobloxLauncher` gains an optional `IRobloxRunningProbe`, matching how `IClientAppSettingsWriter` and `IGlobalBasicSettingsWriter` are already injected (nullable, feature-degrades when absent). Inside the existing `_launchGate`:

1. **Snapshot** `GetRunningPlayerPids()` before writing settings.
2. Write per-account settings — **unchanged**.
3. `ExecuteLaunchAsync` — **unchanged**.
4. **If the launch started and a probe is available**, poll until a pid appears that was not in the snapshot, then wait a short settle grace. Otherwise fall back to the existing fixed `FFlagReadHold`.
5. Release the gate.

### Constants

| Name | Value | Reasoning |
| --- | --- | --- |
| `NewClientPollInterval` | 250 ms | Cheap — `Process.GetProcessesByName` over a handful of processes. Fast enough that detection latency is not the bottleneck. |
| `NewClientWaitTimeout` | 30 s | Ceiling for a cold start with a bootstrapper update. On expiry, release anyway and degrade to today's behavior rather than hanging. |
| `SettleGrace` | 1 s | The remaining guess — but anchored to *the client process existing* rather than *Windows accepting a URI*. That re-anchoring is the fix; the residual second is small and bounded where the old 250 ms was measured against an unbounded gap. |
| `FFlagReadHold` | 250 ms | Retained unchanged as the no-probe fallback. |

### Why these five properties matter

- **Wait only on success.** A `Failed`, `CookieExpired`, or `Limited` result releases the gate immediately. Failures stay fast; a user without Roblox installed does not eat a 30 s timeout on every click.
- **Hard ceiling.** A launch that never produces a client must not hang Squad Launch. Timeout degrades to current behavior.
- **Snapshot-diffing handles orphans.** Roblox leaves windowless `RobloxPlayerBeta` processes behind on exit (three were present on the test machine 45 minutes after quitting). They are in the snapshot, so they cannot be mistaken for the new client.
- **Delays go through the injected `TimeProvider`**, which `RobloxLauncher` already holds. Tests drive time with a fake and run instantly. **This is what makes a race testable at all** — without it the tests would sleep and stay flaky.
- **No behavior change when the probe is absent.** Existing call sites and tests that construct `RobloxLauncher` without a probe keep today's semantics exactly.

## Testing

xUnit, fake `TimeProvider`, fake `IRobloxRunningProbe`. No test sleeps or launches anything.

- A new pid appearing releases the gate; assert the wait ended on detection, not on timeout.
- No new pid ever appearing releases the gate at the timeout — asserted as a timeout, not a hang.
- A failed launch never waits at all.
- Pre-existing pids (including windowless orphans) do not count as the new client.
- Absent probe falls back to `FFlagReadHold` with unchanged behavior.
- **The end-to-end property:** two sequential launches with different per-account FPS values each write *and retain* their own value — the second write must not land before the first client is detected. This is the test that would have caught the shipped bug; the others are scaffolding around it.

Each test must name the production change that would make it fail. A race-condition test that passes against the broken code is worse than none.

## Out of scope

- **Waiting for the client's main window** instead of process existence. Strictly stronger evidence that settings were read, but adds 10-20 s per launch. Revisit only if the settle grace proves insufficient in the field.
- **Making the settings files non-shared.** Not ours to change; they are Roblox's, machine-global by design.
- **Per-account graphics quality.** Separate feature, justified on performance rather than memory grounds — see `docs/investigations/2026-08-01-graphics-quality-memory-negative-result.md`. It depends on this fix landing first, since it would ride the same shared-file path and inherit the same race.

## Consequence accepted

Squad Launch gets slower — each launch now waits for its client to appear rather than firing at 250 ms intervals. Six accounts move from near-instant to roughly 15-30 s of staggered launching. **Accepted deliberately:** today it is fast and silently wrong, and a per-account setting that does not apply is worse than a slower launch that does.
