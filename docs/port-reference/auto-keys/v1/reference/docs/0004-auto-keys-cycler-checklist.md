# Build checklist — Auto-keys cycler (ADR 0004)

**Spec:** `docs/decisions/0004-auto-keys-cycler.md`
**Date:** 2026-05-08
**Slope:** C — auto-keys + stay-awake

Effort scale: **XS** ≤30 min · **S** 1–2 hr · **M** 2–4 hr · **L** 4–8 hr

Build order is dependency-first: domain value types → DI seams → pure validators → state machine → storage integration → UI → integration polish → docs/security. Each item lists what blocks it, what "done" looks like, and the rough effort.

---

## 1 — `AutoKeysStep` value type

**Effort:** XS
**Blocks:** none
**Files:** `App/RORORO/Domain/AutoKeys/AutoKeysStep.swift`

Codable / Equatable / Sendable struct: `(keyCode: CGKeyCode, delayAfter: TimeInterval)`. Helper `AutoKeysStep.spacebar(after:)` constructor for the common case. Unit suffixes (sec/min) are a UI concern — the storage shape is always seconds in `TimeInterval`.

**Acceptance:**
- Round-trips through `JSONEncoder` / `JSONDecoder` with the existing on-disk shape (additive — empty default if missing).
- One unit test: encode → decode → equal.

---

## 2 — `AutoKeysSequence` value type

**Effort:** XS
**Blocks:** none (parallel with #1)
**Files:** `App/RORORO/Domain/AutoKeys/AutoKeysSequence.swift`

Codable / Equatable / Sendable struct holding `[AutoKeysStep]`. Validating initializer rejects > 3 steps. Computed `totalDuration: TimeInterval` returns Σ delayAfter (used by the budget validator).

**Acceptance:**
- Init with > 3 steps throws / returns nil (pick one — failable init is simpler).
- One unit test for the cap.
- One unit test that `totalDuration` matches Σ of input delays.

---

## 3 — `KeyEventPoster` (DI seam)

**Effort:** XS
**Blocks:** #7 (cycler), #11 (integration test)
**Files:** `App/RORORO/Domain/AutoKeys/KeyEventPoster.swift`

Protocol + production implementation. Protocol: `func post(keyCode: CGKeyCode) async`. Production: builds two `CGEvent`s (keyDown + keyUp), posts each to `.cghidEventTap`, with a small ~20ms gap between down and up.

**Acceptance:**
- Compiles with `import CoreGraphics`.
- A `FakeKeyEventPoster` records calls in-order — used by the cycler's unit tests.

---

## 4 — `WindowFocuser` (DI seam)

**Effort:** S
**Blocks:** #7
**Files:** `App/RORORO/Domain/AutoKeys/WindowFocuser.swift`

Protocol + production implementation. Protocol: `func focus(pid: pid_t) async throws`. Production: looks up `NSRunningApplication(processIdentifier: pid)`, calls `.activate(options: .activateIgnoringOtherApps)`, waits the 150ms settle (`Task.sleep`), throws `WindowFocuser.Error.notRunning` if no NSRunningApplication exists for the pid.

**Acceptance:**
- A `FakeWindowFocuser` records focus calls in-order and can be configured to throw on a given pid (for the skip-and-continue test).

---

## 5 — `CycleBudget` pure validator

**Effort:** S
**Blocks:** #7, #10
**Files:** `App/RORORO/Domain/AutoKeys/CycleBudget.swift`

Pure function: `estimate(snapshot: [AutoKeysSequence], loopDelay: TimeInterval, focusOverhead: TimeInterval = 0.2) -> TimeInterval`. Constants exported: `warnThreshold = 18 * 60`, `hardCap = 19 * 60`. Convenience: `state(for: TimeInterval) -> .ok | .warn | .overCap`.

**Acceptance:**
- Unit tests covering: zero accounts, single account zero delays, single account at the cap, single account over cap, multi-account cumulative, threshold edges (17:59 / 18:00 / 18:59 / 19:00).

---

## 6 — `Account.autoKeys` field + global `loopDelay`

**Effort:** S
**Blocks:** #7, #8, #9, #10
**Files:** `App/RORORO/Domain/Account.swift` (modify), settings store (existing or new)

Add `public var autoKeys: AutoKeysSequence?` to `Account` (matches the optional pattern of `framerateCapOverride`, `cookieStatus`). On-disk JSON is additive — a missing field decodes to nil. Add `autoKeysLoopDelay: TimeInterval` (default 14 * 60 = 840s) to whichever settings store currently holds `framerateCap` and friends.

**Acceptance:**
- Existing on-disk `accounts.json` files load unchanged (no re-encode forced).
- Setting `account.autoKeys = AutoKeysSequence(...)` and re-saving round-trips.
- One migration test against a fixture from before this field existed.

---

## 7 — `AutoKeysCycler` actor

**Effort:** L
**Blocks:** #10, #11, #12
**Blocked by:** #1, #2, #3, #4, #5, #6
**Files:** `App/RORORO/Domain/AutoKeys/AutoKeysCycler.swift`

Singleton actor. State machine: `.stopped` / `.running(snapshot)` / `.paused`. Public surface: `start(accounts:loopDelay:) async throws`, `stop()`, observable `state` for the toolbar UI.

Internal:
- Captures the snapshot at start time; stores running pids paired with their `AutoKeysSequence`.
- Acquires `IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleSystemSleep, ...)` and stores the `IOPMAssertionID`. Releases on stop.
- Owns one long-lived loop `Task` that walks the snapshot serially, uses injected `WindowFocuser` + `KeyEventPoster`.
- Catches `WindowFocuser.Error.notRunning` per-account: logs, skips that account for the rest of the iteration, re-evaluates next loop.
- Re-validates the snapshot through `CycleBudget` before each iteration; transitions to `.stopped` with reason if the cap is exceeded mid-flight.

**Acceptance:**
- Unit test (using fakes): two accounts × two-step sequences → fake poster receives the keystrokes in the expected order, fake focuser receives the expected pids in the expected order, across two full loop iterations.
- Unit test: focus failure on account A → cycler skips A and continues to B in the same iteration.
- Unit test: stop() while sleeping in `loopDelay` cancels the loop task and releases the assertion.
- Unit test: cycler refuses to start if the snapshot's estimated time exceeds `CycleBudget.hardCap`.

---

## 8 — `AutoKeysRecorderSheet` modal UI

**Effort:** M
**Blocked by:** #1, #2, #6
**Files:** `App/RORORO/UI/AutoKeys/AutoKeysRecorderSheet.swift`

SwiftUI sheet. State machine: `.awaitingKey(stepIndex)` → `.awaitingDelay(stepIndex, capturedKey)` → repeat for up to 3 → `.summary`. Captures keyCodes via `NSEvent.addLocalMonitorForEvents(matching: .keyDown)` while the sheet is frontmost. Delay input is a number field + sec/min picker; converts to seconds before saving. Live cycle-budget preview at the bottom (uses the snapshot of currently-running accounts + the in-flight sequence). Save button disabled if `CycleBudget.state == .overCap`.

**Acceptance:**
- Manual: open the sheet, press space → captured as keyCode 49. Enter "2" + sec → step 1 saved. Repeat. Save closes the sheet and writes to the account.
- Manual: configure 3 steps with delays totaling well past 19 min → Save is disabled, banner explains why.
- Sheet doesn't capture keys when not frontmost (NSEvent monitor is local to the sheet).

---

## 9 — `AutoKeysRowView` per-account badge

**Effort:** XS
**Blocked by:** #6, #8
**Files:** `App/RORORO/UI/AutoKeys/AutoKeysRowView.swift`

Small inline view on each `AccountRow`. States: "auto-keys: not configured" (muted) / "auto-keys: N keys, total Xs" (active). Tap opens the recorder sheet.

**Acceptance:**
- Manual: configured account shows the active badge with correct counts; un-configured shows the muted state; tap opens the sheet.

---

## 10 — `CyclerToolbarView` global Play/Pause

**Effort:** M
**Blocked by:** #5, #7
**Files:** `App/RORORO/UI/AutoKeys/CyclerToolbarView.swift`

Toolbar item in the main window. Three regions:
- Play/Pause button (binds to `AutoKeysCycler.state`).
- Live estimated-cycle-time label, color-coded by `CycleBudget.state` (.ok green, .warn amber, .overCap red).
- Persistent "auto-keys running" indicator (small dot + subtle pulse) when state is `.running`.

**Acceptance:**
- Manual: Play with no configured accounts → banner "no accounts configured for auto-keys."
- Manual: Play with one configured running account → cycler starts, indicator activates, estimate updates.
- Manual: Pause → indicator clears, IOPMAssertion released (verify via `pmset -g assertions`).

---

## 11 — TCC banner + Accessibility deep-link

**Effort:** S
**Blocked by:** #7, #10
**Files:** UI integration in `CyclerToolbarView` + reuse of existing TCC helper

Reuse the existing Accessibility-permission helper the app uses for hotkeys. On Play, if `AXIsProcessTrusted()` returns false, show a banner: "Auto-keys needs Accessibility permission." Button: "Open Accessibility settings" → `NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)`. Cycler state stays `.stopped`.

**Acceptance:**
- Manual: in System Settings, remove RORORO from the Accessibility list. Press Play. Verify banner + deep-link button works.
- Re-grant, press Play, verify cycler starts.

---

## 12 — Integration test + manual test plan execution

**Effort:** M
**Blocked by:** #7, #8, #9, #10, #11
**Files:** `App/RORORO/RORORO Tests/AutoKeysIntegrationTests.swift` + execute the manual plan from ADR 0004

Integration test: spawn an `NSTextField` in a hidden test window, post a `CGEvent` keyDown+keyUp via the production `KeyEventPoster`, assert the field's stringValue. Skipped on CI via `XCTSkip` if `AXIsProcessTrusted()` is false.

Manual plan from the ADR (5 cases — basic, multi-account ordering, mid-cycle quit, Cmd-Tab steal, TCC denial). Run all five against real Roblox before merge.

**Acceptance:**
- Integration test passes locally with TCC granted.
- All 5 manual cases pass against real Roblox.
- One commit per slope-step is fine, but the manual results land in the PR description (or a follow-up note in the ADR).

---

## 13 — Documentation & Security Verification

**Effort:** S
**Blocked by:** all prior items
**Files:** README.md, docs/PRIVACY.md, docs/security-audit.md, project cspell allowlist

**README:**
- Add an "Auto-keys" section describing the feature, the App-Store-disqualifying posture, and that it requires Accessibility permission.

**PRIVACY.md:**
- Note that auto-keys does not transmit keystrokes anywhere — they're posted locally via `CGEvent` to the user's own Roblox processes. No telemetry, no remote logging.

**security-audit.md:**
- Add a row noting the `CGEvent` post + `NSRunningApplication.activate` use, why it's required, and that the only TCC capability touched is Accessibility.
- Note that the IOPMAssertion is scoped to cycler-running state and released on stop / app-quit.

**cspell allowlist:**
- Add the project-vocab terms flagged on this ADR: `RORORO`, `spacebar`, `keybinds`, `frontmost`, `backgrounded`, `IOPM`, `footgun`, `autoclicker`, `estevanhernandez`, `rororo`. Place in the project's `cspell.json` or equivalent.

**Secrets scan:**
- Run `git secrets --scan` (or the equivalent the project already has) over the diff. Auto-keys touches no credentials, but the audit closes the loop.

**Dependency audit:**
- This slope adds zero new third-party dependencies (all native Apple frameworks: CoreGraphics, AppKit, IOKit). Confirm and note in the audit.

**Deployment security:**
- Auto-keys does not change the signing posture or the Sparkle update channel.
- Confirm the Sparkle EdDSA key handling (ADR 0001 / 0003 territory) is unaffected.

**Acceptance:**
- README, PRIVACY, security-audit reflect the feature.
- cSpell warnings on the ADR, this checklist, and any new code clear (or are deliberately suppressed).
- `git secrets --scan` clean.
- No new dependencies introduced.
- One commit titled `docs(slope-c): auto-keys cycler documentation + security verification`.

---

## Dependency graph

```
1 (Step) ─┐
          ├─→ 6 (Account+settings) ─→ 7 (Cycler) ──┐
2 (Seq)  ─┤                                        ├─→ 10 (Toolbar) ─→ 11 (TCC) ─→ 12 (Tests) ─→ 13 (Docs+Sec)
          │                                        │
3 (Poster) ──────────────────────→ 7 ──────────────┤
4 (Focuser) ─────────────────────→ 7 ──────────────┤
5 (Budget) ──────────────────────→ 7 ──────────────┘
                                    │
                                    └→ 8 (Recorder) ─→ 9 (RowView)
```

**Critical path** (longest dependency chain): 1/2 → 6 → 7 → 10 → 11 → 12 → 13.
Items 8 and 9 sit on a parallel path — UI-only, can land any time after 7 and 6 are in.

## Total effort estimate

3× XS + 4× S + 3× M + 1× L = **~16-22 hours of focused work**. Realistic over 2-3 sessions, depending on how cleanly the cycler actor lands and how chatty Roblox is during manual verification.
