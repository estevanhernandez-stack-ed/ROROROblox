# Launch / install lane — iterate slate (low-cost bundle candidates)

**Branch:** `v1.5.0-presence-account-ux` (read-only scan; doc-only commit)
**Opened:** 2026-05-21
**Posture:** Ptolemy — regression-aware, user-trust-aware, small-diff-preferred. One lane only (launch + install reliability). Out of scope per `.vibe-iterate/config.json`: input-automation, macro-tooling, client-injection (the MaCro wall). Documented-endpoints-only / no-handler-takeover posture per CLAUDE.md.

This slate is the **adjacent cheap-win layer** around the named install-deferral cycle (pre-warm `(b)` / version pre-check `(a)` / updating-UX `(e)`, see `docs/investigations/2026-05-21-bloxstrap-update-deferral.md`). It does NOT re-list those three core items — it finds the riders that ship cheaply alongside them, and flags the tempting-but-separate.

---

## Scan findings (what's already there vs rough)

Read of the live launch/install surfaces turned up four facts that change the cost math:

1. **The launch path is already free of disruptive message boxes.** Every `MessageBox.Show` in the tree (`SessionHistoryWindow`, `Preferences`, `Settings`, `Import/Export`, `MainViewModel:1563`) is a settings/confirm dialog, NOT a launch-path popup. Launch errors already route to inline `summary.StatusText` / `StatusBanner` (`MainViewModel.cs:985,990,1034,1549`). **The "replace launch-path message boxes with inline status" candidate is a non-finding for the launch path** — we already match Bloxstrap's "no more disruptive message boxes" there. (Settings-dialog confirms are correct as modals; leave them.)
2. **`BloxstrapDetector` already exists** (`src/ROROROblox.Core/BloxstrapDetector.cs`) — reads `HKCU\Software\Classes\roblox-player\shell\open\command` and substring-matches `Bloxstrap`. Today it only drives a one-time dismissible FFlag-override warning banner (`MainViewModel.cs:1771-1775`). It does **not** detect Fishstrap, and is **not** consulted to skip RoRoRo's own update logic. The (d) fast-follow is mostly wiring an existing detector to a new decision, not new detection from scratch.
3. **`RobloxCompatChecker` already enumerates `%LOCALAPPDATA%\Roblox\Versions\version-*` and reads the installed version** via `FileVersionInfo` on the newest dir holding `RobloxPlayerBeta.exe` (`RobloxCompatChecker.cs:88-123`). The investigation's `[SPIKE NEEDED]` "which installed version is current" read is **already solved in-tree** — pick the most-recently-written `version-*` dir with the player exe. The (a) version pre-check can reuse this exact read instead of spiking it cold.
4. **The 30s attach timeout is fixed and install-unaware** (`RobloxProcessTracker.cs:17` `DefaultAttachTimeout = 30s`). `OnProcessAttachFailed` already documents that during a long install the RPB spawns after 30s and deliberately leaves the defender to its 120s cap (`MainViewModel.cs:1531-1554`) — but the tracker still *fires the failure event* at 30s, so the row flips to a scary "Launch never connected" message while the install is genuinely still running.

---

## Ranked slate (trust value ÷ effort)

Effort: S = under a day / one small file. M = a couple files + a test. L = multi-file or new subsystem.
Posture: clean / needs-care / no.

### Rank 1 — Tighten `ProcessAttachFailed` messaging to distinguish "updating" from "failed" — **S, clean, FOLD IN**
- **What:** Before `OnProcessAttachFailed` writes "Launch never connected. Check Roblox is current + antivirus isn't blocking." (`MainViewModel.cs:1549`), check whether a `RobloxPlayerInstaller.exe` is (or recently was) running. If so, swap to an honest "Roblox is updating — RoRoRo is waiting" state instead of the failure copy.
- **Why:** Right now a perfectly healthy mid-install launch shows the user a red-flag "never connected / check antivirus" message at the 30s mark — actively erodes trust on exactly the slow case the whole cycle targets. The fix is a one-line process probe (we already do `Process.GetProcessesByName` everywhere) + a branch on the status string.
- **Bundle:** This IS the install-aware face of `(e)`. Folds in directly — it's the same `RobloxPlayerInstaller.exe` watch the cycle already needs.

### Rank 2 — Make the tracker attach-timeout install-aware (extend during a detected install) — **S/M, clean, FOLD IN**
- **What:** When `TrackLaunchAsync` hits its 30s deadline, before declaring failure, check for a running `RobloxPlayerInstaller.exe`. If present, extend the deadline (mirror the defender's 120s upper bound) rather than firing `ProcessAttachFailed`. This is the exact gap the v1.6.0 defender already names — the defender holds to 120s but the tracker bails at 30s, so the two are out of lockstep.
- **Why:** Closes a real false-negative: a clan member on a cold/updating install gets a spurious failure event (history row stamped "Never connected", scary status) for a launch that succeeds 20s later. Reliability + trust, low diff — one conditional in the poll loop's deadline computation. Pairs structurally with Rank 1 (same installer probe, same lockstep-with-defender reasoning).
- **Bundle:** Fold in. The pre-warm gate `(b)` and this share the "watch `RobloxPlayerInstaller.exe`" primitive — build it once, both consume it.

### Rank 3 — Extend strap detection to Fishstrap + reuse for update-deferral skip — **S/M, clean (detect+inform), FOLD IN as the (d) seed**
- **What:** Broaden `BloxstrapDetector` to also match `Fishstrap` (one more substring + rename to `StrapDetector` or add `IsStrapHandler()`). Then, in the install-deferral cycle's pre-check: when a strap owns the `roblox-player:` handler, **skip RoRoRo's own version pre-check / pre-warm wait** — the strap already updates proactively before `RobloxPlayerBeta` runs (per the investigation §1). This is the cheap, posture-clean half of fast-follow (d).
- **Why:** The clan already runs Bloxstrap. Doing our own pre-check on top of a strap that already updated up front is wasted latency and a second cook in the kitchen. The detector exists; this is wiring + one substring. Trust win: faster launches for strap users, no double-update.
- **Posture:** clean for *detect-and-skip-our-own-work* and *detect-and-recommend*. **Do NOT co-drive the strap's mutex** — that's the `[NEEDS LIVE VERIFICATION]` part the investigation flagged; keep it out of this cycle.
- **Bundle:** Fold in the detect-and-skip slice (it's a guard the pre-check needs anyway). Keep the clan-facing "install Fishstrap" recommendation as the separate fast-follow.

### Rank 4 — Reuse `RobloxCompatChecker`'s installed-version read to retire the (a) spike — **S, clean, FOLD IN (de-risks the cycle)**
- **What:** Not a feature — a build-sequencing win. The cycle's `(a)` version pre-check has a `[SPIKE NEEDED]` for "which installed version is current." `RobloxCompatChecker.GetInstalledRobloxVersion()` already does exactly that (newest `version-*` dir with `RobloxPlayerBeta.exe`). Lift/share that read; compare its dir's GUID against `clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer`'s `clientVersionUpload`. The spike collapses to "confirm the field name on a live box."
- **Why:** Removes the only flagged unknown blocking `(a)`. Pure de-risk, near-zero new code (refactor an existing private static into something shared, or just call the same pattern).
- **Bundle:** Fold in — it's plumbing the cycle already depends on.

### Rank 5 — Verify the RobloxAlreadyRunning hard-block guidance is correct + hard enough — **S, clean, FOLD IN (verification, near-zero diff)**
- **What:** Confirm the existing modal matches the verified mutex-recovery rule (memory: recovery needs BOTH Roblox AND RoRoRo quit; closing only Roblox is not enough; the modal must hard-block).
- **Finding:** It already does. `RobloxAlreadyRunningWindow.xaml:42-55` lists "1. Close Roblox / 2. Close RoRoRo / 3. Re-open RoRoRo", the only button is "Quit RoRoRo" (`IsDefault`+`IsCancel`, no in-place recovery), and `App.OnStartup` shuts down after it. **This is correct and hard — no change needed.** Listed only to close the task's question with evidence. One optional micro-tweak if touched: the body says "won't work right" — could state the consequence harder ("the first account gets kicked"), but that's polish, not a fix.
- **Bundle:** No code. Verification done here.

### Rank 6 — Inline "Roblox is updating" row/banner state during pre-warm — **S, clean, FOLD IN (this is `(e)` proper)**
- **What:** The user-facing surface of the cycle: while the pre-warm gate `(b)` holds the batch waiting on `RobloxPlayerInstaller.exe`, show "Roblox is updating — holding the rest of your launches" in `StatusBanner`, and "Waiting for Roblox update" per-row instead of a stalled "Launching...". Mirrors Bloxstrap's "Waiting for other instances..." serialization UX.
- **Why:** Honest UX for the exact slow path. Without it the batch silently stalls between launches and the clan reads it as a hang. Tiny — it's `StatusBanner` string writes gated on the installer probe the cycle already adds.
- **Bundle:** Fold in — this is literally cycle item `(e)`, listed here only because it's the UX expression of the Rank 1/2 installer probe.

### Rank 7 — Log retention / cap (Bloxstrap parity: "only 15 most recent kept") — **S, clean, SEPARATE**
- **What:** Bloxstrap caps its log files at the 15 most recent. Worth a glance at RoRoRo's logging to add a retention cap if logs grow unbounded.
- **Why:** Hygiene, not launch reliability. Genuinely low-cost but **not in this lane** — it's a logging/housekeeping concern, not launch/install trust. Note it for a future hygiene pass, keep it out of the install-deferral cycle.
- **Bundle:** Separate. Scope-creep guard.

### Rank 8 — Studio bootstrap support (Bloxstrap parity) — **L, needs-care, SEPARATE (likely no)**
- **What:** Bloxstrap/Fishstrap bootstrap Roblox **Studio** as a launch target.
- **Why:** RoRoRo is a player multi-instance tool for the Pet Sim 99 clan; Studio is a different audience and a different launch contract. Materially larger than the lane, no clan demand signal. **Out of scope** — note and move on.
- **Bundle:** Separate / decline.

### Rank 9 — Fishstrap static-directory model / channel control — **L, no, SEPARATE (decline)**
- **What:** Fishstrap's version-hashed-to-binary-type dir model + update-skipping/channel control.
- **Why:** This is the bootstrapper-takeover path the investigation already ranked dead-last `(c) — DO NOT adopt`. Owning Roblox's version-dir lifecycle is a different product and a permanent maintenance tax. Same wall as MaCro. **Decline.**
- **Bundle:** Separate / decline. Listed only to explicitly close the radar item.

---

## Fold-in recommendation

**Fold these 4 into the install-deferral cycle as cheap riders** (all share the `RobloxPlayerInstaller.exe` probe / installed-version read the cycle already needs — near-zero marginal cost):

1. **Rank 1 — Install-aware `ProcessAttachFailed` messaging (S).** Stop showing "never connected / check antivirus" when Roblox is mid-update. The honest face of `(e)`.
2. **Rank 2 — Install-aware tracker attach-timeout (S/M).** Extend the 30s deadline while an installer runs, so the tracker and the 120s defender stop disagreeing. Closes a real false-negative.
3. **Rank 3 — Fishstrap-aware strap detection + skip-our-own-pre-check (S/M).** Existing `BloxstrapDetector` + one substring; when a strap owns the handler, don't double-update. The clean half of `(d)`.
4. **Rank 4 — Reuse `RobloxCompatChecker`'s installed-version read (S).** Retires the cycle's only `[SPIKE NEEDED]`. Pure de-risk.

(Rank 5 is verification-only — already correct, no diff. Rank 6 is cycle item `(e)` itself.)

**Keep OUT of this cycle (scope-creep guard):**
- **Rank 7 — log retention** — real but a hygiene-pass concern, not the launch lane.
- **Rank 8 — Studio bootstrap** — different audience + launch contract, no clan signal.
- **Rank 9 — Fishstrap static-dir / channel takeover** — the already-declined `(c)` bootstrapper-takeover path. Same wall as MaCro.

**Explicit non-finding:** the "replace disruptive launch-path message boxes with inline status" candidate — the launch path is **already** message-box-free and routes errors to inline status. We already have Bloxstrap's "no more disruptive message boxes" property on the launch surface. No work needed there.

---

## Source map

- Launch path / inline-status (no MessageBox): `src/ROROROblox.Core/RobloxLauncher.cs:147,261`; `src/ROROROblox.App/ViewModels/MainViewModel.cs:889-1000,1007-1059,1134-1175`.
- ProcessAttachFailed messaging: `src/ROROROblox.App/ViewModels/MainViewModel.cs:1531-1554`.
- Tracker fixed 30s timeout: `src/ROROROblox.Core/Diagnostics/RobloxProcessTracker.cs:17,98-136`.
- Defender 120s cap + named gap: `src/ROROROblox.App/Diagnostics/AppStorageDefender.cs:49-54`; `MainViewModel.cs:929`.
- Strap detection (exists, Bloxstrap-only): `src/ROROROblox.Core/BloxstrapDetector.cs`; `MainViewModel.cs:1771-1775`.
- Installed-version read (retires the (a) spike): `src/ROROROblox.Core/RobloxCompatChecker.cs:88-123`.
- RobloxAlreadyRunning hard-block (verified correct): `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml:42-74`; `App.xaml.cs` startup gate.
- Compat / version-drift banner surface: `MainViewModel.cs:726-736`; `MainWindow.xaml:1161-1162`.
- Radar competitor items mined: `.vibe-iterate/radar.cache.json` (Bloxstrap: log retention, Studio bootstrap, ipinfo error notifications; Fishstrap: static-directory model, channel control).
