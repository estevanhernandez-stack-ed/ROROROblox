# Notes — auto-keys

## Why this exists

Roblox disconnects an idle account after ~20 minutes. RORORO Mac's headline use case is running multiple Roblox instances at once; without a way to keep each instance alive, multi-instance value collapses on long sessions. Auto-keys is the AFK defeater — pick a key (typically spacebar), pick a delay, and the cycler walks each open window firing your sequence in turn.

The design pivoted twice during brainstorm: mouse-clicks → keyboard (keys are layout-stable and free of "don't move the window" footguns), and concurrent per-account → serial multi-window cycle (avoids the `CGEventPostToPid` reliability spike for backgrounded games). The serial-cycle model also reframes the AFK budget cleanly: as long as the cycle revisits each window inside its 20-minute timer, every account stays alive. User's stated tolerance: "less than 15 min, stretch to 18, hard cap 19."

This bundle exists to support a port to RORORO Windows (the C# / WPF sibling, `github.com/estevanhernandez-stack-ed/ROROROblox`). Engine, UI, tests, and the canonical ADR are captured together so the WPF builder has the full surface — engine logic ports as-is in shape, the SwiftUI views become reference for the WPF re-skin, and the tests double as a behavior spec.

## Gotchas

- **Mac-only frameworks throughout.** Engine depends on `CoreGraphics` (`CGEvent.post`), `AppKit` (`NSRunningApplication`, `NSWorkspace`, `NSEvent` global monitor), `ApplicationServices` (`AXUIElement*`), and `IOKit` (`IOPMAssertion*`). Windows port maps each: `CGEvent.post` → `SendInput`; `NSRunningApplication.activate` → `SetForegroundWindow` + the `AttachThreadInput` workaround for the foreground-lock; `NSEvent.addGlobalMonitor` → `SetWindowsHookEx WH_KEYBOARD_LL` + `WH_MOUSE_LL`; `IOPMAssertion` → `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`; `kAXRaiseAction` → `BringWindowToTop` / `SetWindowPos HWND_TOP`.

- **Self-event tagging trick is load-bearing.** `KeyEventPoster` stamps every posted event with `eventSourceUserData = 0x524F524F` (ASCII "RORO"). The safety monitor reads the same field on incoming events and skips matches — otherwise the engagement detector would pause the cycler on every keystroke it fires. **Windows analogue:** pass a marker through `KEYBDINPUT.dwExtraInfo` in the `SendInput` call, then in the low-level keyboard hook callback check `LLKHF_INJECTED` AND read `dwExtraInfo` from the `KBDLLHOOKSTRUCT`. Use the same `0x524F524F` magic value to keep the bundles symmetric across OSes.

- **TCC permissions are split into two buckets on macOS 14+.** Posting `CGEvent`s requires Accessibility; the global `NSEvent` monitor requires Input Monitoring (separate TCC entry). Users who deny Input Monitoring lose the kill-key — the cycler refuses to start in that case with a banner pointing to the setting. Windows doesn't have an equivalent permission gate, so the WPF port skips this layer entirely (single check or nothing).

- **`NSRunningApplication.activate()` silently no-ops when the calling app isn't frontmost.** Once the cycler steals focus the first time, RORORO is no longer frontmost, and subsequent `activate()` calls no-op — every keystroke routes to whatever window the user is looking at. Fix in `WindowFocuser.swift`: AX-first (`AXUIElementSetAttributeValue` for `kAXFrontmostAttribute` + `kAXRaiseAction` on the main window), `activate()` as fallback. Windows has its own version (`SetForegroundWindow` only honors the foreground-lock timeout) — standard workaround is `AttachThreadInput` to attach the calling thread's input queue to the target's, then `SetForegroundWindow`, then detach.

- **Focus must be CONFIRMED, not assumed.** `WindowFocuser` polls `NSWorkspace.frontmostApplication` every 25ms up to `settleDelay` (500ms) until the target's pid is frontmost, then adds a 250ms post-confirm grace before returning. Dropping the poll caused the first-iteration "didn't have time to jump" bug — keys arrived before macOS finished delivering focus. Windows port: poll `GetForegroundWindow()` similarly; the input pipeline grace applies the same way.

- **Budget guard re-validates mid-flight, not just at start.** The cycler re-runs `CycleBudget.estimate` inside the loop on every iteration. Reconfiguring mid-run to push past `hardCap` (19 min) bails out cleanly to `.stopped(.budgetExceeded)`. Start-time-only validation lets users wedge themselves into a budget-violating state by adding accounts after Play. Preserve this in the Windows port.

- **Engagement pause does NOT extend on continued input.** First mouse move OR key press transitions `.running → .paused(.userEngaged, deadline)`. Subsequent events do NOT push the deadline forward. The earlier always-extending design made the toolbar unreachable when the user was actively trying to reach for it. 5s grace fires once, period.

- **Kill key is excluded from `.userEngaged` broadcast.** Original design: first tap of a double-tap was paused-via-engagement; second tap's `.killRequested` saw `state == .paused` and called resume — so the user's "stop" gesture restarted the cycler. Special-case the kill-key combo in the safety monitor's keyDown handler: if it matches the kill combo, route to gesture detection and `return` before broadcasting engagement.

- **State-machine race lives in the view-model, not the cycler.** Original design had both the cycler AND the view-model handling kill events; cycler tore down first → state became `.stopped(.userKilled)` → view-model saw `.stopped` and called play() → restart loop. Kill handling is now consolidated into `AutoKeysCyclerViewModel.swift`. Single owner. Don't split it back.

- **`AutoKeysSequence` failable initializer is now always-succeeds.** Wave 3c lifted the original 3-step cap (decision: `CycleBudget.hardCap` is the real ceiling, not an arbitrary count). The `init?(steps:)` is kept for source-compat with existing call sites — it never returns nil. Windows port can drop the failable init wholesale.

- **`AutoKeysStep.repeatCount` defaults to 1 in the custom decoder.** Pre-wave-3c `accounts.json` files load cleanly because the Codable decoder treats missing `repeatCount` as 1. Same backward-compat trick will apply to the Windows port's settings file when it ships its own repeat-N support.

- **Repeats fire at a fixed 0.7s gap between presses.** Capped a bit faster than 1/sec; faster repeats either coalesce in Roblox's input layer or look janky to the user. The interval is exposed as `AutoKeysStep.intraRepeatInterval` — bump only with a documented reason.

- **Empty / nil sequence = account is skipped.** Zero-config = zero behavior change. Users who don't engage auto-keys see no change in launch flow, no surprise keystrokes, no TCC prompt fired by accident. There is no implicit "default to spacebar" — user must explicitly configure.

- **App-Store-disqualifying by design, ethically welded.** This is not a regression to ship. The feature is opt-in, uses public APIs only (no injection / no patching / no observation evasion), but its purpose — defeating an idle timer on a third-party game — is one Apple won't approve. RORORO Mac is distributed via signed-DMG + Sparkle, not the App Store. Document the same posture on the Windows side; the WPF port should be a portable EXE / signed installer, not Microsoft-Store-bound.

## Tradeoffs preserved

- **Serial cycle over concurrent posting.** Concurrent `CGEventPostToPid` would let every account stay "always alive" but games are inconsistent receivers when backgrounded. Serial cycle accepts that only one account is "active" at any instant; the AFK math still works because each window's 20-min timer is generous against even a 10-account cycle. Don't refactor back to concurrent — the reliability spike is real.

- **Recorder UX over edit-in-place sequence editor.** Changing one delay means re-recording the whole sequence. Acceptable for v1 — re-record is ~10 seconds. Data model supports edit-in-place if the friction surfaces; only the UI moves. Don't pre-build the editable-row view; let the friction earn it.

- **Manual Play, no auto-start-on-launch.** Auto-start doubles UI surface (per-account toggle + a settings page) and creates a "running by accident" failure mode that's hard to undo when the user has Cmd-Tabbed away. Manual Play keeps the user in the loop. Document on the Windows side too.

- **Two TCC prompts, even though it's "extra friction."** The original posture (one consent for everything event-related) hits a wall when Input Monitoring landed in macOS 14+ as a separate bucket from Accessibility. The setup sheet explains both before either is requested. Windows port has no equivalent split — the friction goes away.

- **No "do not pause on mouse" power-user toggle in v1.** Some users genuinely afk-grind with the mouse moving (wireless desk dance, second-monitor activity). The engagement pause penalizes them. We accepted the friction in v1; if it surfaces, the toggle is a small `AutoKeysSafetyConfig` field.

- **Step cap was lifted, not re-imposed.** ADR 0004 Decision 2's original 3-step cap was a UX guardrail; experience showed users want longer macros (full keybind rotations, multi-step ability chains). `CycleBudget.hardCap` is the real ceiling. Windows port should follow — don't re-introduce an arbitrary count limit.

---

<sub>vibe-taker autonomous read at 2026-05-09T16:17:59Z. Empty sections are explicit ("None known.") rather than absent.</sub>
