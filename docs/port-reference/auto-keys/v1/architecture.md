# Architecture — auto-keys

## Summary

Serial multi-window keystroke cycler that defeats Roblox's ~20-minute AFK timeout across N concurrent accounts on macOS. The cycler walks each running window in turn — focus → fire keystroke sequence → settle → advance — instead of posting events to backgrounded PIDs concurrently. This avoids `CGEventPostToPid`'s reliability spike (games are inconsistent receivers when backgrounded) and leans on macOS's deterministic frontmost-window event routing.

Built on a 9-Decision ADR (`reference/docs/0004-auto-keys-cycler.md`). The engine is an actor + state machine; safety, budget, focus, and event-posting are DI seams so the actor stays unit-testable without grabbing the system event tap.

## Components

### Domain (engine) — `reference/domain/` (13 files)

- `AutoKeysCycler.swift` — actor singleton. State machine: `.stopped(reason)` / `.running(pids)` / `.paused(reason, until?)`. Owns the loop `Task` and the `IOPMAssertionID` wake-lock. Re-validates `CycleBudget` mid-flight; bails to `.stopped(.budgetExceeded)` if the user reconfigures past `hardCap`.
- `AutoKeysCyclerViewModel.swift` — bridges the actor to SwiftUI. Owns kill-key-as-toggle (start from `.stopped`, stop from `.running`/`.paused`). Single owner — splitting kill handling between cycler and view-model created a restart-loop race during wave 3a.
- `AutoKeysSafetyMonitor.swift` — actor. Watches global mouse + key events via `EventTapping`. Emits `.userEngaged` (any non-self-tagged input) and `.killRequested` (configured combo, hold-for-N or double-tap-within-N). Self-tagged events (cycler's own posted keystrokes) are dropped.
- `AutoKeysSafetyConfig.swift` — kill-key combo, gesture (`.holdFor(seconds)` or `.doubleTap(within)`), `resumeGrace`.
- `AutoKeysPermissions.swift` — TCC checks for Accessibility (CGEvent post) + Input Monitoring (NSEvent global monitor). The two buckets are separate on macOS 14+.
- `CycleBudget.swift` — pure `estimate(snapshot:loopDelay:) → TimeInterval` + threshold constants (warn 18min, hardCap 19min). Roblox AFK math, isolated as a pure function.
- `KeyEventPoster.swift` — DI seam over `CGEvent.post`. Production tags every event with `eventSourceUserData = 0x524F524F` (ASCII "RORO") so the safety monitor can drop self-events.
- `WindowFocuser.swift` — DI seam. AX-first (`AXUIElementSetAttributeValue` + `kAXRaiseAction`); `NSRunningApplication.activate()` fallback for the rare case AX fails. Polls `NSWorkspace.frontmostApplication` to confirm focus landed before returning.
- `EventTapping.swift` — DI seam over the global `NSEvent` monitor.
- `PowerAssertion.swift` — DI seam over `IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleSystemSleep)`. Acquired on Play, released on Stop.
- `Sleeper.swift` — DI seam over `Task.sleep(nanoseconds:)` for testability.
- `AutoKeysSequence.swift` — value type, ordered `[AutoKeysStep]`. No artificial cap (originally 3, lifted in wave 3c — `CycleBudget.hardCap` is the real ceiling).
- `AutoKeysStep.swift` — `(keyCode: CGKeyCode, delayAfter: TimeInterval, repeatCount: Int)`. Stores virtual key codes (49 = spacebar, 18-29 = number row), not characters, so layout changes don't break recordings. Repeats fire at fixed 0.7s gap between presses.

### UI — `reference/ui/` (5 SwiftUI files)

- `CyclerToolbarView.swift` — global Play/Pause + live cycle estimate + warn/cap state + "auto-keys running" indicator.
- `AutoKeysRecorderSheet.swift` — modal step-by-step capture: "press key → delay-after → next or done."
- `AutoKeysSafetySetupSheet.swift` — kill-key + gesture configuration; both TCC prompts explained before either is requested.
- `AutoKeysStatusPanel.swift` — running cycler diagnostic panel (current target, next target, deadline).
- `AutoKeysRowBadge.swift` — per-account row badge ("auto-keys: 3 keys" / "not configured").

### Tests — `reference/tests/` (5 XCTest files)

- `AutoKeysCyclerTests.swift` — state machine + focus-failure recovery (skip-and-continue) + snapshot re-validation.
- `AutoKeysSafetyMonitorTests.swift` — kill gesture + engagement detection + self-event drop.
- `CycleBudgetTests.swift` — pure-function full coverage of warn/cap thresholds, edge cases.
- `AutoKeysSequenceTests.swift` — value-type behavior + Codable round-trip.
- `AutoKeysStepTests.swift` — step type behavior + backward-compat decoder (missing `repeatCount` defaults to 1).

### Spec — `reference/docs/` (2 files)

- `0004-auto-keys-cycler.md` — **canonical 9-Decision ADR**. Read first when porting.
- `0004-auto-keys-cycler-checklist.md` — implementation checklist used during the build.

## Data flow

```
  Account.autoKeys: AutoKeysSequence?  ─┐
                                        │
  RunningPid (from RunningAccountTracker)┼──▶ Cycler.Target ──┐
                                        │                     │
  AutoKeysSafetyConfig (kill combo)     │                     ▼
        │                               │            ┌── AutoKeysCycler ──┐
        ▼                               │            │                    │
  AutoKeysSafetyMonitor ────observe()──▶│            │  start(targets,    │
        ▲                               │            │        loopDelay)  │
        │                               │            │       │            │
        │ taps                          │            │       ▼            │
        │                               │            │   acquire IOPM     │
  EventTapping (global NSEvent)         │            │       │            │
        │                               │            │       ▼            │
        ▼                               │            │   loop while       │
  drops self-tagged events              │            │   !cancelled:      │
  (eventSourceUserData == "RORO")       │            │     waitWhilePaused│
                                        │            │     budget recheck │
                                        │            │     for target:    │
                                        │            │       focus(pid) ──┼─▶ AX setFrontmost
                                        │            │       for step:   │     + kAXRaiseAction
                                        │            │         post(key)─┼─▶ CGEvent.post
                                        │            │           × repeat│     (tagged "RORO")
                                        │            │         sleep     │
                                        │            │     sleep loopDelay│
                                        │            │                    │
                                        │            │  state stream ─────┼─▶ AsyncStream<State>
                                        │            │  progressCallback ─┼─▶ (current, next, key)
                                        │            └────────────────────┘
                                        │
                                        ▼
                              UI: CyclerToolbarView,
                                  AutoKeysStatusPanel,
                                  AutoKeysRowBadge
```

## Key files

- `reference/docs/0004-auto-keys-cycler.md` — canonical spec; read first.
- `reference/domain/AutoKeysCycler.swift` — engine entry point; the actor.
- `reference/domain/AutoKeysSafetyMonitor.swift` — input-channel-independent escape hatch.
- `reference/domain/CycleBudget.swift` — the 20-minute AFK math, isolated as a pure function.
- `reference/domain/KeyEventPoster.swift` — self-event tagging trick (Mac-specific; Windows analogue: `KEYBDINPUT.dwExtraInfo` + `LLKHF_INJECTED` flag in low-level hook).
- `reference/domain/WindowFocuser.swift` — cross-app focus on macOS 14+ (the `NSRunningApplication.activate()` no-op gotcha and the AX-first fix).

---

<sub>vibe-taker autonomous read at 2026-05-09T16:17:59Z. Hand-edit anything the agent missed; this file is human-authoritative.</sub>
