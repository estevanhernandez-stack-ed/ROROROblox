# RORORO — v1.7.0 Install-Deferral + Launch-Lane Reliability Build Checklist

**Cycle:** v1.7.0 (no-takeover Roblox-update deferral + launch-lane riders)
**Cycle type:** Spec-first cycle (pattern mm). Canonical spec: [`docs/superpowers/specs/2026-05-21-rororo-install-deferral-design.md`](superpowers/specs/2026-05-21-rororo-install-deferral-design.md). Design inputs: the two investigation docs under `docs/investigations/2026-05-21-*`. [`spec.md`](spec.md) is a pointer-stub.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A (autonomous)
- **Git:** Commit after each item. Conventional commits. Branch `v1.7.0-install-deferral` (cut from `main`; carries the two investigation docs + the spec).
- **Verification:** Yes — **C1 after item 5** (pre-warm + updating-UX runnable; simulate an update-pending launch). The real install-interruption is a manual smoke whenever Roblox next updates — no E2E against real roblox.com.
- **TDD:** strict for Core + ViewModel logic (items 1-4, 6 — detection + gating + timeout + messaging decisions are pure-extractable). UI wiring (item 5) + docs (item 7) are verify-by-running.

## Effort

Smaller than v1.6.0 — mostly process-watching + gating logic + small UI. **Total ≈ 4-6 hours.** Heaviest: item 4 (pre-warm sequencing in the batch-launch path). No spike — the version read reuses `RobloxCompatChecker`.

---

## Checklist

- [x] **1. Update-pending detection (Core, TDD)**
  Spec ref: `spec.md > Components > 1. Update-pending detection`
  What to build: a small `IRobloxUpdateProbe` / `RobloxUpdateProbe` in `src/ROROROblox.Core/Diagnostics/` exposing `bool IsInstallerRunning()` (scan for `RobloxPlayerInstaller.exe`, mirroring `RobloxProcessTracker`'s process-scan) and `Task<bool> IsUpdatePendingAsync()` (compare `RobloxCompatChecker.GetInstalledRobloxVersion()` to the documented `clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` GUID via the existing HttpClient; on any network/parse failure return false = "don't block"). Posture-clean (one process scan + one documented GET). Consumed by items 3-6.
  Acceptance: installer-running detected when present; update-pending true on version mismatch, false on match, false-on-failure (degrade safe). New tests pass (stub the process scan + version/CDN).
  Verify: `dotnet test ROROROblox.slnx --filter "RobloxUpdateProbe*"`. Commit: `feat(launch): RobloxUpdateProbe — installer-running + update-pending detection`.

- [x] **2. Strap-aware detection (TDD)**
  Spec ref: `spec.md > Components > Riders > 7. Strap-aware skip`
  What to build: extend the existing `BloxstrapDetector` (reads the `roblox-player:` handler-registry key) to also recognize **Fishstrap** and expose `bool IsStrapHandlingLaunches()` ("a strap is the registered handler, so it updates proactively itself"). Detect only — no co-driving the strap's mutex.
  Acceptance: Bloxstrap handler → true; Fishstrap handler → true; vanilla Roblox → false. Tests pass.
  Verify: `dotnet test ROROROblox.slnx --filter "BloxstrapDetector*|StrapDetect*"`. Commit: `feat(launch): strap-aware detection (Bloxstrap + Fishstrap handler)`.

- [x] **3. Install-aware tracker attach-timeout (Core, TDD)**
  Spec ref: `spec.md > Components > Riders > 6. Install-aware tracker attach-timeout`
  What to build: `RobloxProcessTracker` bails at a fixed 30s (`RobloxProcessTracker.cs:17`), out of lockstep with the v1.6.0 defender's 120s. When `RobloxUpdateProbe.IsInstallerRunning()` is true during the attach wait, extend the deadline (~120s family cap) so an install-delayed `RobloxPlayerBeta` still attaches instead of a false `ProcessAttachFailed`. Inject the probe (or a delegate) for testability.
  Acceptance: installer present → tracker waits past 30s + still attaches a late RPB; no installer → unchanged (30s). Tests pass.
  Verify: `dotnet test ROROROblox.slnx --filter "RobloxProcessTracker*"`. Commit: `fix(launch): extend tracker attach-timeout while Roblox installer is running`.

- [x] **4. Pre-warm batch launch + version pre-check gating (ViewModel, TDD the gate)**
  Spec ref: `spec.md > Components > 2. Pre-warm batch launch` + `3. Version pre-check` + `Data flow`
  What to build: in `LaunchAllAsync` / `SquadLaunchAsync`, gate the batch: strap handles launches (item 2) → launch all as today (skip pre-warm); else no update pending (item 1) → launch all as today; else (update pending) → launch the FIRST account, **wait** until `IsInstallerRunning()` is false AND the first account's `RobloxPlayerBeta` attached, **then** release the rest. Extract the gating decision (should-pre-warm + wait-condition) into a pure tested helper; wire the VM to it. Reuse the existing 5s inter-launch throttle for post-pre-warm releases.
  Acceptance: strap present → no pre-warm; no update → no pre-warm (normal speed); update pending → batch holds after #1 until installer-clear + #1 attach, then releases. Pure-gate tests cover all three branches.
  Verify: `dotnet test ROROROblox.slnx --filter "PreWarm*|LaunchGate*"`. Commit: `feat(launch): pre-warm the first client through a pending Roblox update before the batch`.

- [x] **5. Updating-UX (App)**
  Spec ref: `spec.md > Components > 4. Updating-UX`
  What to build: a clear "Roblox is updating — hold on" status (StatusBanner + the launching account's row state) during the item-4 pre-warm wait, cleared when the batch releases. Brand-styled, consistent with existing launch banners.
  Acceptance: pending-update batch shows the updating banner + clears once the batch proceeds; no spurious banner on the no-update path.
  Verify: `dotnet build ROROROblox.slnx`; simulate an update-pending launch (stubbed probe) and watch the banner. **Checkpoint C1.** Commit: `feat(launch): 'Roblox is updating' UX during pre-warm`.

- [x] **6. Install-aware ProcessAttachFailed messaging (App, rider)**
  Spec ref: `spec.md > Components > Riders > 5. Install-aware ProcessAttachFailed messaging`
  What to build: in `OnProcessAttachFailed`, branch the row message on `RobloxUpdateProbe.IsInstallerRunning()` — installer running → "Roblox is updating — hold on" instead of the current "Launch never connected. Check Roblox is current + antivirus isn't blocking." Keep the failure copy for the genuine no-installer case.
  Acceptance: installer running at attach-fail → updating copy; not running → existing failure copy. Test the branch.
  Verify: `dotnet test ROROROblox.slnx --filter "MainViewModel*|AttachFailed*"`. Commit: `fix(launch): install-aware ProcessAttachFailed messaging`.

- [ ] **7. Documentation & Security Verification**
  Spec ref: `spec.md > Decisions to log` + `spec.md > Testing` + `CLAUDE.md > What NOT to do`
  What to build: sync `docs/` (spec status → implemented, checklist ticks, spec.md pointer). Record the new compat-risk dependencies (`RobloxPlayerInstaller.exe` name + `clientsettingscdn.roblox.com/v2/client-version` GUID) as decisions + confirm they degrade gracefully. Secrets scan + local-path grep (no `c:\Users\` in committable code). `dotnet list ROROROblox.slnx package --vulnerable`. Branch ready for PR to `main`. (Version bump + Store/Velopack release is the builder-driven release flow, separate.)
  Acceptance: docs current; no secrets/local-paths; deps clean or documented; compat-risk decisions logged. Branch PR-ready.
  Verify: pre-commit hooks pass; `dotnet build ROROROblox.slnx` clean; full suite green. Commit: `docs: v1.7.0 install-deferral docs sync + compat-risk decisions`.
