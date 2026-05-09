# ROROROblox — Technical Spec (pointer stub)

Spec-first Cart cycles. Active cycle's canonical spec lives upstream:

→ [docs/superpowers/specs/2026-05-08-roblox-already-running-detect-design.md](superpowers/specs/2026-05-08-roblox-already-running-detect-design.md)

Cycle history (each cycle's canonical spec is its own durable artifact):

- v1.1 core (multi-instance + accounts + distribution): [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md)
- v1.2 per-account FPS limiter: [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md)
- v1.3.x default-game widget + local rename overlay: [`2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md) (shipped 2026-05-08 via PR #3)
- v1.3.x save-pasted-links: [`2026-05-08-save-pasted-links-design.md`](superpowers/specs/2026-05-08-save-pasted-links-design.md) (shipped 2026-05-08 via PR #5)
- v1.3.x detect-Roblox-already-running (cycle 4): this cycle

## Section index (for checklist references — current cycle)

- §1 Overview (Roblox-running-before-RoRoRo breaks multi-instance; hard-block modal at startup before mutex.Acquire)
- §2 Goals and non-goals (probe → modal → shutdown; fail-open on probe failure; no runtime detection; no in-place recheck)
- §3 Stack (no new dependencies — `Process.GetProcessesByName` already used in `RobloxProcessTracker.cs:188` and `DiagnosticsCollector.cs`)
- §4 Architecture and change surface
  - §4.1 `IRobloxRunningProbe` interface (Core)
  - §4.2 `RobloxRunningProbe` impl — thin `Process.GetProcessesByName("RobloxPlayerBeta")` wrapper
  - §4.3 `RobloxAlreadyRunningWindow` modal — cycle-2 chrome (plain Window with BgBrush), single `Quit RoRoRo` button
  - §4.4 `StartupGate` class — probe-driven `ShouldProceed()` decision, fail-open on exception
  - §4.5 `App.xaml.cs` wire-up — gate runs BEFORE `mutex.Acquire()` (line 91)
- §5 Soft-fail discipline (false-positive unrecoverable; false-negative recoverable; bias to fail-open)
- §6 Testing (5 unit cases on `StartupGate.ShouldProceed`, real probe wrapper integration via manual smoke)
- §7 Branch + commit plan (5 commits on `feat/roblox-already-running-detect`)
- §8 Out of scope (deliberate)
- §9 Open questions / future (`Environment.Exit` hard-fallback if `Shutdown` hangs; localization; telemetry)
- §10 Decisions to log on completion (3 dashboard entries — architecture, UX, insertion-point)

## What's deliberately not in this cycle

- Runtime detection (Roblox launching after RoRoRo is up — that path works correctly, RoRoRo's mutex hold defeats Roblox's singleton check)
- In-place recheck affordance ("I closed Roblox, try again" — verified-broken recovery model rules this out)
- `MutexHolder.ReleaseAndReacquire()` — would enable an in-place recheck we don't want
- `RobloxStudio.exe` detection (different singleton scope, doesn't conflict)
- Bloxstrap-specific handling (Bloxstrap launches `RobloxPlayerBeta.exe` — same detection covers it)
- Cross-Windows-user-session detection (edge case; reliable detection requires elevated process inspection)

When build reality drifts from the canonical spec — banner-correct at the top of the canonical spec doc per pattern v from Vibe Thesis (per `CLAUDE.md` "Don't rewrite the canonical spec on drift" rule).
