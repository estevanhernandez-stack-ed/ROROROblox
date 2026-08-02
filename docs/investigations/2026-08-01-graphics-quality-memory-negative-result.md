# Graphics quality does not reduce Roblox client memory — measured, negative result

**Status:** **Closed — feature rejected on evidence.** Do not re-propose without new data.
**Date:** 2026-08-01
**Driver:** [`2026-08-01-long-session-window-death.md`](2026-08-01-long-session-window-death.md) — the Roblox client leaks; we went looking for a way to shrink the baseline so the ceiling arrives later.

## The hypothesis, and why it was reasonable

Every Roblox client starts around 2.5 GB before any leak accumulates. That is asset load, not leak. If lowering graphics quality shrank that baseline, three clients starting 800 MB lighter would be 2.4 GB of headroom you never have to reclaim — a permanent win, better than Recycle's periodic one.

`GlobalBasicSettings_<N>.xml` holds the user's own in-game settings, including the graphics slider. RoRoRo already writes `FramerateCap` there for the per-account FPS limiter, so the mechanism, the code path, and the risk posture were all proven. **Critically it is not an FFlag** — it is the same file Roblox rewrites when you drag the slider in-game — which matters because the clan is wary of FFlags after reports of bans.

It looked like the cheapest large win available.

## Result: under 1%

Measured on a live rig, two real accounts, same game (Pet Sim public server), both clients at 20 FPS in **both** legs so frame rate could not confound it. Plateau medians over the last ~2.5 minutes of each leg, after memory stopped climbing.

| Account | quality 10 / granular 21 | quality 1 / granular 1 | Delta | |
| --- | --- | --- | --- | --- |
| CElCPapa | 2,484 MB | 2,466 MB | **−18 MB** | −0.7% |
| estehernandez | 2,603 MB | 2,574 MB | **−29 MB** | −1.1% |
| **Total** | **5,088 MB** | **5,040 MB** | **−48 MB** | **−0.9%** |

Maximum quality to minimum quality bought **less than one percent**. For scale:

- Smaller than the ordinary variance *between* the two clients (~120 MB), which differed only by which public server they landed on.
- Roughly **two minutes** of the leak at the rate measured the same day (~1,000 MB/hr/client under active play).
- Recycle reclaims ~1–2 GB in one click. This is noise beside it.

## Validity — the result is not an artifact

A near-zero delta has an obvious alternative explanation: the setting never applied. Ruled out.

- **The write survived to launch.** `SavedQualityLevel=1` / `GraphicsQualityLevel=1` were still on disk with the clients running — RoRoRo's `FramerateCap` write at launch merges rather than overwrites, so it preserved them.
- **The engine applied it.** The in-game Graphics Quality slider read **1** during the low leg. Roblox honored the external write at the engine level, not merely on disk.
- **FPS was constant across legs.** Both clients ran at 20 FPS in both legs, so the only variable that changed was quality.
- **Both legs plateaued.** Readings were taken after drift fell under 5 MB per two minutes, not off the loading ramp.

## Why, in hindsight

The graphics slider governs **render** work — shadows, lighting, draw distance, texture filtering — which is GPU-side. Client RAM is dominated by the engine itself, the game's instance tree and scripts, and asset data that is resolution-independent. Pet Sim is instance-heavy. Turning quality down makes the GPU's life easier and barely touches system memory.

Incidentally: at quality 1 the game **did not look noticeably worse** in Pet Sim. So the slider is cheap to turn down — it simply is not where the RAM lives.

## Other levers in that file: none

Every field in `GlobalBasicSettings` was reviewed. It governs presentation and input — render quality, camera, sensitivity, volume, UI toggles. Nothing controls asset residency. If the graphics slider moves memory by 0.9%, nothing else in the file will do better. `MasterVolume` was the only adjacent candidate and is already `0` on the test machine.

## What we learned that does matter

Two things outlived the feature.

**1. Roblox honors external writes to `GlobalBasicSettings` and persists them on exit.** Confirmed twice. The client reads the file at startup and rewrites it with in-session values when it closes. Consequences:

- Any per-account setting we write becomes the user's **global** default after that client exits. The shipped FPS limiter already behaves this way — the test machine's global `FramerateCap` was 20 because RoRoRo wrote it. The multi-instance community treats this as normal, so it needs disclosure, not a restore mechanism.
- Ordering matters when experimenting: write *after* clients exit, never while they run, or their exit stomps the change.

**2. `SavedQualityLevel` is an enum, not a number.** It is `<token>` typed — `Enum.SavedQualitySetting`, where `0` = Automatic and `1`–`10` are the slider notches. `GraphicsQualityLevel` is the granular 1–21 internal level and moves with it (`10 ↔ 21`, `0 ↔ 0`). Both must be written together. Confirmed by comparing the player file against the Studio file, then verified in-engine.

## Related bug found while setting this up

The measurement setup surfaced a **shipped defect in the per-account FPS limiter**, filed separately: the 250 ms `FFlagReadHold` is too short, so back-to-back launches lose per-account settings. See the follow-up spec. It matters here because any future per-account setting written to this shared file inherits the same race — which is a second, independent reason this design direction was more expensive than it looked.

## Decision

**Rejected.** Do not build a per-account graphics-quality setting for memory reasons. The lever is inert.

If someone later wants graphics quality exposed for **GPU/frame-rate** reasons, that is a different feature with a different justification, and this document says nothing against it. It says only that it will not save meaningful RAM.

**Recycle remains the only thing that reclaims Roblox client memory on Windows**, and it is already shipped in v1.12.0.0.
