# ADR 0004 — Auto-keys cycler (serial multi-window keystroke driver)

**Date:** 2026-05-08
**Status:** Accepted
**Slope:** C (auto-clicker + stay-awake), pivoted from clicks to keys mid-brainstorm

## Background

Roblox's AFK timeout disconnects an idle account after ~20 minutes. RORORO Mac's headline use case is running multiple Roblox instances at once; without a way to keep each instance alive, the multi-instance value proposition collapses on long sessions.

The original Slope C scope (recorded at end of 2026-05-07 session) was a mouse-click auto-clicker — record 2-3 click positions, fire each on its own timer. Brainstorming on 2026-05-08 pivoted in two passes:

1. **Mouse → keyboard.** Roblox keybinds let the user trigger equipment with a key press; jump-spam (spacebar) defeats AFK by itself. Keys are also free of the "don't move the window" footgun that plagues coordinate-based clicks.
2. **Concurrent per-account → serial multi-window cycle.** Posting `CGEvent`s to a backgrounded process via `CGEventPostToPid` is technically possible but games are inconsistent receivers — a known reliability risk. Walking each window in turn (focus → fire → next) sidesteps the problem entirely: every post lands in the frontmost window, which macOS handles deterministically.

The serial-cycle model also reframes the AFK budget cleanly: as long as the cycle revisits each window inside its 20-minute timer, every account stays alive. The user's stated tolerance is "less than 15 min, stretch to 18, hard cap 19."

This ADR locks the model and the surface area before any code is written. The feature is **explicitly App-Store-disqualifying** (ethically welded — capability + clear posture) per the founding instruction in `feedback_app_store_posture` memory; this is not a regression to ship.

## Decision 1 — Serial multi-window cycle, focus-then-fire

**Decision:** Auto-keys runs as a single global cycle that walks each running account's Roblox window in turn. For each account: focus the window via `NSRunningApplication.activate(options: .activateIgnoringOtherApps)`, wait a 150ms settle, fire the account's keystroke sequence (each step: keyDown + keyUp + per-step delay). After the last account's last delay, sleep the configured loop delay, then repeat from the first account.

**Rationale:** Eliminates the `CGEventPostToPid` reliability spike — every keystroke is posted to whatever process is frontmost, which is the one we just focused. macOS's frontmost-window event routing is rock solid. Trade-off: only one account is "active" at any instant, but with a 20-minute AFK budget per window, even a 10-account cycle has enough headroom (see Decision 4).

**Consequences:** Cycle steals focus from whatever else the user is doing on their Mac when its turn comes around. Documented limitation. The persistent toolbar indicator (Decision 6) keeps this from being surprising.

## Decision 2 — Per-account keystroke sequence, capped at 3 keys

**Decision:** Each `Account` gains an optional `autoKeys: AutoKeysSequence?` field. An `AutoKeysSequence` is an ordered list of up to 3 `AutoKeysStep` entries; each step is `(keyCode: CGKeyCode, delayAfter: TimeInterval)`. Empty / nil sequence = account is skipped by the cycler.

**Rationale:** Three keys cover the realistic use cases — jump-spam (spacebar) plus two keybinds for equipment activation. Capping at 3 protects the cycle budget (Decision 4) and keeps the recorder UX (Decision 5) bounded to a known-finite flow. macOS keyCodes (49 = spacebar, 18-29 = number row, etc.) store layout-independent intent — the user's recording survives a keyboard layout change.

**Consequences:** Power users wanting longer sequences (4+ keys) are not served in v1. If demand surfaces, the cap can be raised without changing the data model — only the recorder UI and validator constants move.

## Decision 3 — Empty sequence = skip, no implicit default

**Decision:** A new account ships with `autoKeys = nil`. The cycler skips accounts with nil or empty sequences. There is no implicit "default to spacebar" behavior.

**Rationale:** Zero-config = zero behavior change. Users who don't engage auto-keys see no change in launch flow, no surprise keystrokes, no TCC prompt fired by accident. Matches the Decision 4 posture from ADR 0001 ("defaults are no-op until the user opts in").

**Consequences:** First-time users must explicitly configure at least one account before Play does anything. Acceptable — the toolbar shows "no accounts configured" state when Play is pressed against an empty snapshot.

## Decision 4 — Cycle budget: target 15 min, warn 18, hard cap 19

**Decision:** The cycle-budget validator computes estimated cycle time as Σ(per-step delays across all running accounts in the snapshot) + global loop delay + (200ms × N accounts focus overhead). Constants: warn threshold 18 min, hard cap 19 min. Save / Play is disabled above the hard cap. The cycler re-validates the snapshot at Play time to handle the "config was fine for 4 accounts, now 8 are running" case.

**Rationale:** Roblox's AFK timer is ~20 minutes per window. The cycle must revisit each window before its individual timer expires. 15 min is the comfortable target, 18 the stretch, 19 the absolute ceiling — past that, the first window's timer will fire before the cycle returns. The 200ms focus-overhead constant is a measured-on-this-machine estimate; if Roblox is slow to settle on busy systems the constant gets bumped (it's a single named constant in `CycleBudget.swift`).

**Consequences:** Users with many accounts running and long per-step delays will hit the cap and be forced to choose: fewer accounts, shorter delays, or shorter loop delay. The validator's live preview (in the recorder sheet and toolbar) makes the trade-off visible while the user is configuring, not after the fact.

## Decision 5 — Interactive recorder UX, no edit-in-place

**Decision:** The user configures an account's sequence through a modal recorder sheet on the account row. Flow: "Press your first key" → user presses a key (captured as keyCode) → "Delay after?" → user enters a number and toggles sec/min → "Press your next key, or Done" → repeat to a max of 3 keys. The resulting sequence is shown in a read-only summary on the account row. To change a sequence, the user re-records.

**Rationale:** The recorder mirrors how the user described the feature ("record your keys… press a key, then delay after… same process until they finish"). Interactive capture is faster for first-time setup and avoids a custom keyboard-key picker UI. No edit-in-place keeps the surface area small — one recorder, one summary, no second editing screen to design and maintain.

**Consequences:** Users who want to change one delay must re-record the whole sequence. Acceptable for v1 — re-recording 3 keys takes ~10 seconds. If friction surfaces, an editable-row view can be added later (the data model supports it as-is).

## Decision 6 — Global Play/Pause + persistent toolbar indicator

**Decision:** The cycler is controlled by a single global Play/Pause toggle in the main window's toolbar. While running, the toolbar shows the live estimated cycle time and a small persistent "auto-keys running" indicator. There is no per-account start/stop and no auto-start-on-launch.

**Rationale:** Matches the user's mental model ("once they hit play..."). One toggle = one mental object to track. Auto-start-on-launch was considered and rejected for v1: it doubles the UI surface (a per-account checkbox + a settings page) and the "leave it running by accident" failure mode is hard to undo when the user has Cmd-Tabbed away. Manual start keeps the user in the loop.

**Consequences:** Users who launch alts in waves must press Play after each wave (the cycler doesn't dynamically pick up newly-launched accounts mid-cycle — the snapshot is taken at Play time). Pause-then-Play to refresh. Documented as expected behavior.

## Decision 7 — Stay-awake via `IOPMAssertion`

**Decision:** While the cycler is running it holds an `IOPMAssertionCreateWithName` of type `kIOPMAssertionTypePreventUserIdleSystemSleep`. The assertion is acquired on Play and released on Pause / app quit / cycle-budget validation failure.

**Rationale:** The lower-level IOPM API is the documented macOS path for "I'm doing meaningful work, don't sleep on me." Equivalent to `NSProcessInfo.beginActivity` for our needs but more explicit — the assertion lifetime maps 1:1 to the cycler's running state. Sparkle, dock badges, and other app-lifecycle assertions are unaffected.

**Consequences:** Mac stays awake while the cycle is running. Closing the lid won't stop the cycle by itself (the user must Pause or quit). Documented in the recorder footer.

## Decision 8 — TCC piggybacks on the existing Accessibility prompt

**Decision:** Posting `CGEvent`s requires Accessibility (TCC) permission. The cycler does not trigger a separate prompt — it reuses the existing Accessibility consent path the app already uses for hotkeys. If TCC is denied at Play time, the cycler refuses to start and surfaces a banner with a deep-link button to System Settings → Privacy & Security → Accessibility.

**Rationale:** One consent for everything event-related keeps the user-facing posture clean. No surprise prompts, no two-step grant.

**Consequences:** Users who haven't granted Accessibility for hotkeys yet will hit the prompt the first time they press Play instead of the first time they configure a hotkey. Same UX, different trigger point — acceptable.

## Decision 9 — Safety controls (added 2026-05-08)

**Decision:** The cycler grows two safety mechanisms that operate independently of any UI control:

1. **Engagement pause.** A global `NSEvent` monitor watches for human input (mouse moves OR key presses). On any input the cycler immediately transitions to `.paused(.userEngaged)` — releases focus, stops firing, surfaces a banner ("Auto-keys paused. Double-tap [key] / hold [key] for 1s to stop, or wait 5s to resume."). Five clean seconds → auto-resume. Continued input keeps extending the pause.
2. **Kill key.** The user picks a designated key during recorder setup (default suggestion: F19 or a rare modifier combo) and a gesture: either hold-for-1s OR double-tap-within-600ms. The user picks the gesture in setup; we don't impose one. Triggering the gesture stops the cycler hard (`.stopped(.userKilled)`).

**Rationale:** The cycler steals focus from whatever the user is doing on their Mac. If the only Stop control is a button in our toolbar, the user reaching for that button can land their click on a Roblox window mid-focus-grab — firing whatever Roblox keybind is under the cursor, or worse, taking a hit from an in-game enemy because the user's mouse is now driving game input. The escape hatch has to be input-channel-independent: a global key gesture works regardless of which window is frontmost, and the engagement pause means returning to the keyboard is itself a "wait, I'm here now" signal that buys the user time before the cycler grabs focus again.

**Self-event tagging:** Every `CGEvent` the cycler posts is tagged with a custom `eventSourceUserData` value (`0x524F524F` = ASCII "RORO"). The engagement monitor reads the same field on incoming events and ignores anything tagged with our value — otherwise the cycler would pause itself on every keystroke it fires.

**Two TCC prompts, not one.** This decision overrides the "one consent for everything" posture in Decision 8 — posting `CGEvent`s still requires Accessibility, and the global input monitor for the engagement detector requires Input Monitoring. They're separate TCC buckets in macOS 14+; the recorder setup screen explains both before either is requested. Users who decline Input Monitoring fall back to a degraded mode: kill key still works (it's a regular keyDown monitor that happens to need Input Monitoring too — so really if they decline, the kill key also fails; the cycler refuses to start in that case with a banner pointing to the setting).

**Consequences:**
- Mouse-move-during-AFK-grind pauses the cycle. Documented behavior. The 5 s auto-resume keeps it from being annoying when the user nudges the mouse on the way back from the bathroom.
- The banner is rendered via `NSPanel` (floats above Roblox without stealing focus) so it's visible even when Roblox is frontmost.
- Hold-vs-double-tap is a per-user preference, not a global default — picked once during recorder setup, persisted in `LaunchSettingsStore`.
- Over time we may want a "do not pause on mouse" power-user toggle (some users genuinely afk-grind with the mouse moving from a wireless desk dance). Out of scope for v1.

## Implementation map

| Layer | File | What it does |
| --- | --- | --- |
| Domain | `App/RORORO/Domain/AutoKeys/AutoKeysSequence.swift` | Value type, list of `AutoKeysStep`, max 3. |
| Domain | `App/RORORO/Domain/AutoKeys/AutoKeysStep.swift` | `(keyCode: CGKeyCode, delayAfter: TimeInterval)`. |
| Domain | `App/RORORO/Domain/AutoKeys/AutoKeysCycler.swift` | Singleton actor. State machine: `.stopped(reason?)` / `.running(pids)` / `.paused(reason)`. Owns the loop `Task` and the `IOPMAssertionID`. |
| Domain | `App/RORORO/Domain/AutoKeys/AutoKeysSafetyMonitor.swift` | Global `NSEvent` monitor for mouseMoved + keyDown. Recognizes the configured kill-key gesture (hold-1s OR double-tap). Emits `.userEngaged` / `.killRequested` to the cycler. Tags from `KeyEventPoster` skip self-events. |
| Domain | `App/RORORO/Domain/AutoKeys/CycleBudget.swift` | Pure `estimate(accounts:loopDelay:) -> TimeInterval` + threshold constants. |
| Domain | `App/RORORO/Domain/AutoKeys/KeyEventPoster.swift` | Thin wrapper over `CGEvent.post` for testability (DI seam). |
| Domain | `App/RORORO/Domain/AutoKeys/WindowFocuser.swift` | `focus(pid:) async throws` via `NSRunningApplication.activate`. |
| Account model | `App/RORORO/Domain/Account.swift` (existing) | Add `autoKeys: AutoKeysSequence?` field. |
| Settings | `App/RORORO/Domain/AppSettings.swift` (existing or new) | Global `loopDelay: TimeInterval`. |
| UI | `App/RORORO/UI/AutoKeys/AutoKeysRecorderSheet.swift` | Modal recorder, step-by-step capture. |
| UI | `App/RORORO/UI/AutoKeys/AutoKeysRowView.swift` | Per-account-row badge: "auto-keys: 3 keys" / "not configured." |
| UI | `App/RORORO/UI/AutoKeys/CyclerToolbarView.swift` | Global Play/Pause + live cycle estimate + warn/cap state + running indicator. |

## Testing

- **Unit:**
  - `CycleBudget.estimate` — pure function, full coverage of warn/cap thresholds, edge cases (zero accounts, all-empty sequences).
  - `AutoKeysCycler` state machine — DI fakes for `KeyEventPoster` and `WindowFocuser`. Verify ordering, pause/resume, focus-failure recovery (skip-and-continue), snapshot re-validation on Play.
- **Integration:**
  - One XCTest posts a `CGEvent` keyDown+keyUp into an `NSTextField` in a test window, asserts the field's value. Skipped on CI (no TCC in headless runners); runs locally with Accessibility granted to the test runner.
- **Manual test plan** (lives in the recorder sheet's developer notes):
  1. Configure one account with `[spacebar, 0]`. Press Play. Verify spacebar fires once per loop in the Roblox window. Pause. Verify stop.
  2. Configure two accounts with sequences `[space, 2s]` and `[1, 5s][2, 5s]`. Press Play. Verify focus walks A → B → A → B and keys land in the right window.
  3. Quit Roblox on Account A mid-cycle. Verify cycler logs, skips A, continues with B.
  4. Cmd-Tab to Safari mid-cycle. Verify cycler steals focus back when its turn comes.
  5. Toggle Accessibility off in System Settings. Press Play. Verify banner + deep-link.

## Approaches considered and rejected

- **Concurrent per-PID posting (`CGEventPostToPid`).** Killed by reliability concerns — games are inconsistent receivers when backgrounded. Serial cycle achieves the same outcome (every account kept alive) without the spike.
- **Frontmost-only with no cycle.** Would mean the user must manually keep each Roblox instance focused — defeats the multi-instance use case.
- **Editable-row sequence editor.** Killed for v1 — interactive recorder is faster for first-time setup, and re-recording is cheap. Data model leaves the door open if friction surfaces.
- **Per-account auto-start on launch.** Killed for v1 — doubles UI surface and creates a "running by accident" failure mode that's hard to undo. Manual Play keeps the user in the loop.
- **Configurable simple-mode "spacebar every 15 min."** Folded into the general design — same outcome falls out of `[spacebar, 0]` + 14-min loop delay against one account.

## References

- Founding posture: `~/.claude/projects/-Users-estevanhernandez-projects-rororo-mac/memory/feedback_app_store_posture.md` (capability ambition + ethical clarity, App Store opt-out accepted).
- Original Slope C scope (clicks): `~/.claude/projects/-Users-estevanhernandez-projects-rororo-mac/memory/project_next_feature_autoclicker.md` — superseded by this ADR.
- Sibling ADR shape: `docs/decisions/0001-launch-settings-writers.md`.
- Plan-of-record: `~/.claude/plans/plan-mac-native-woolly-pascal.md`.
