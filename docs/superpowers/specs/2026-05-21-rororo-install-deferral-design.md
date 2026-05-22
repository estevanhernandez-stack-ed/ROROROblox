# RoRoRo v1.7.0 — Roblox install-deferral (no-takeover) + launch-lane reliability riders

> **Status:** Implemented 2026-05-21 on branch `v1.7.0-install-deferral` (items 1-7). Build matches design. The pre-warm logic + detection + tracker extension + gating are unit-tested; the live banner/deferral is a manual smoke for the next real Roblox client update (no on-demand trigger).
> **Cycle:** v1.7.0 (launch/install reliability). Credibility lane.
> **Design inputs:** [`docs/investigations/2026-05-21-bloxstrap-update-deferral.md`](../../investigations/2026-05-21-bloxstrap-update-deferral.md) (mechanism + options) and [`docs/investigations/2026-05-21-launch-lane-iterate-slate.md`](../../investigations/2026-05-21-launch-lane-iterate-slate.md) (the low-cost riders).

## Why this exists

When Roblox needs a client update, `RobloxPlayerBeta.exe` (the registered `roblox-player:` handler since March 2024) checks its version GUID at launch and, on a mismatch, spawns `RobloxPlayerInstaller.exe` (the "black box") **reactively, mid-launch**, before the game proceeds. RoRoRo launches *through* Roblox's own client (`RobloxLauncher.cs:147,261`), so a launch during an update pending state hits that box — which, in a multilaunch, can land the wrong account (the v1.5/v1.6 captcha-identity bug), throw a scary "check your antivirus" error at the 30s tracker timeout, and generally interrupt the batch.

Bloxstrap avoids this by **replacing the bootstrapper** and updating proactively (and serializing multilaunch behind a mutex so the update runs once before all instances). We are **not** doing that — replacing the handler / managing the version is a different product and violates RoRoRo's documented-endpoints, no-takeover, Roblox-relations posture. Instead this cycle rebuilds Bloxstrap's "**update once, then launch the batch**" outcome at RoRoRo's own layer, using only process-watching + one documented endpoint.

## Components

### 1. Update-pending detection
- **In-progress signal:** `RobloxPlayerInstaller.exe` running = an update is installing right now. (Process scan, same family as `RobloxProcessTracker`.)
- **Pending signal (pre-launch):** compare the installed version to current. **Reuse `RobloxCompatChecker.GetInstalledRobloxVersion()`** (already reads the current `version-*` dir) against the documented `clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` GUID. This retires the investigation's only `[SPIKE NEEDED]` — no cold spike.
- Both signals are posture-clean: one process check + one documented GET. No handler takeover, no version management.

### 2. Pre-warm batch launch
In a multilaunch (Launch multiple / Private server), launch the **first** account, then **wait for the update to clear** — `RobloxPlayerInstaller.exe` gone AND the first `RobloxPlayerBeta` attached — **before releasing the rest of the batch**. The update happens once, up front, on the first launch; the remaining launches find a matching version and never trigger the installer. This is Bloxstrap's serialize-the-update behavior at our layer.

### 3. Version pre-check (skip the wait when clean)
Before a batch, run the pending-signal check (component 1). If no update is pending, **skip the pre-warm wait** entirely — normal multilaunch speed is unchanged on the common path. The pre-warm cost is paid only when an update actually needs to land.

### 4. Updating-UX
A clear "**Roblox is updating — hold on**" status during the pre-warm wait (banner/row state), so a slow update reads as an intended hold, not a hang. Pairs with the v1.6.0 AppStorageDefender (which already holds identity through the install).

### Riders (low-cost, same install-detection signal — from the iterate slate)

- **5. Install-aware `ProcessAttachFailed` messaging (S).** Today a mid-update launch shows "Launch never connected. Check antivirus is not blocking" at the 30s mark — scary on exactly the slow case this cycle targets. Branch the message on "is `RobloxPlayerInstaller.exe` running" → "Roblox is updating, hold on" instead of the failure copy.
- **6. Install-aware tracker attach-timeout (S/M).** `RobloxProcessTracker` bails at a fixed 30s (`RobloxProcessTracker.cs:17`) while the v1.6.0 defender holds 120s — out of lockstep. Extend the tracker deadline when `RobloxPlayerInstaller.exe` is running, so an install-delayed RPB still attaches instead of registering a false attach-failure.
- **7. Strap-aware skip (S/M).** `BloxstrapDetector` already exists and reads the handler-registry key. Add a Fishstrap substring and **consult it**: if a strap is the registered handler (it updates proactively itself), skip our version pre-check / pre-warm to avoid a double-update. The clean half of "lean on Bloxstrap" — detect, don't co-drive its mutex.

## Data flow

```
Launch multiple / Private server batch
  → strap-aware check (7): is Bloxstrap/Fishstrap the handler? → if yes, skip pre-warm (strap handles it)
  → else version pre-check (1,3): installed GUID vs CDN → no update? skip wait, launch all as today
  → update pending: pre-warm (2) — launch #1, show "Roblox is updating" (4),
       wait until RobloxPlayerInstaller.exe gone AND #1 attached (tracker timeout extended, 6)
  → release the rest of the batch (now version matches; no per-launch installer)
ProcessAttachFailed anywhere → installer running? → "Roblox is updating" message, not "launch failed" (5)
```

## Testing

Unit + reconciliation; no E2E against real roblox.com.
- **Detection logic:** installer-process-present + version-match/mismatch → correct "update pending" decision (pure, with a stub process/version probe).
- **Pre-warm sequencing:** with update pending, the batch holds after #1 until the clear-signal; with no update, no hold (releases immediately). Extract the gating decision as a pure tested unit.
- **Messaging branch (5):** installer-running → updating copy; not-running → failure copy.
- **Tracker timeout (6):** deadline extends while installer present.
- **Strap-aware (7):** Bloxstrap/Fishstrap handler detected → pre-warm skipped.
- Manual smoke: launch during an actual Roblox update, confirm the intended account lands + the updating-UX shows.

## Out of scope

- **Bootstrapper / handler takeover** (Bloxstrap's mechanism) — different product, against posture. Explicitly rejected.
- **Co-driving a strap's update mutex** — detect-and-skip only; don't reach into Bloxstrap/Fishstrap internals.
- Log retention, Studio bootstrap, Fishstrap static-directory/channel control — out of lane / scope-creep (per the slate).
- The multilaunch-during-install identity edge that the v1.6.0 defender named — the pre-warm here largely prevents the trigger; the defender remains the backstop.

## Risks / open questions

- **Pre-warm latency:** the first launch + wait adds time only when an update is pending (rare). Acceptable; the version pre-check keeps the common path fast.
- **Installer-process name stability:** keying on `RobloxPlayerInstaller.exe` is a Roblox-side contract — if Roblox renames it, the detection degrades to the version pre-check + the v1.6.0 defender backstop. Log it as a compat-risk axis.
- **CDN version endpoint:** documented + already in the `roblox-compat` family; if it shifts, the pending-check degrades gracefully (fall back to the installer-process signal).

## Decisions to log

- Install-deferral rebuilt at RoRoRo's layer (pre-warm + version pre-check + process-watching) instead of Bloxstrap-style bootstrapper takeover — preserves the documented-endpoints/no-takeover posture.
- New Roblox-side compat dependency: `RobloxPlayerInstaller.exe` process name + `clientsettingscdn.roblox.com/v2/client-version` GUID. Both degrade gracefully if Roblox changes them.
