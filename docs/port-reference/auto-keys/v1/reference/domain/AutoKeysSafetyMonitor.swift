// AutoKeysSafetyMonitor.swift
// Domain — actor that watches global mouse + key events and emits two
// signals to the cycler (ADR 0004 Decision 9):
//
//   - `.userEngaged` whenever a non-self-tagged input event lands. Any
//     mouse move or key press counts. The cycler responds by pausing
//     and starting a 5-second resume-grace timer.
//   - `.killRequested` when the configured kill-key gesture completes.
//     Either hold-for-N-seconds or two-presses-within-N-seconds, per
//     `AutoKeysSafetyConfig`.
//
// Self-tagged events (`isSelfTagged == true` per `EventTapping`) are
// dropped without emit — they're our own posted keystrokes coming back
// up through the global monitor.

import CoreGraphics
import Foundation

public actor AutoKeysSafetyMonitor {

    /// Process-lifetime monitor — booted once by `AutoKeysCyclerViewModel`
    /// after Input Monitoring is granted, then never stopped. Both the
    /// view-model (always-on, for kill-key-as-toggle when cycler is
    /// stopped) and the cycler (only while running, for engagement +
    /// kill-while-running) subscribe via `observe()`. Multiple
    /// subscribers each receive every event.
    public static let shared = AutoKeysSafetyMonitor(
        tapping: NSEventTapping(),
        config: .default
    )

    private let tapping: EventTapping
    private var config: AutoKeysSafetyConfig
    private var subscriptionTask: Task<Void, Never>?
    private var continuations: [UUID: AsyncStream<EngagementEvent>.Continuation] = [:]

    // Kill-gesture state.
    private var holdTimer: Task<Void, Never>?       // for .holdFor
    private var lastKillTap: Date?                  // for .doubleTap

    public init(
        tapping: EventTapping,
        config: AutoKeysSafetyConfig = .default
    ) {
        self.tapping = tapping
        self.config = config
    }

    /// Begin watching events. Subsequent calls to `start` while already
    /// running tear down the previous subscription first — keeps the
    /// underlying tap from leaking. Reconfiguration during a run uses
    /// `updateConfig(_:)` instead.
    public func start() {
        subscriptionTask?.cancel()
        let stream = tapping.start()
        subscriptionTask = Task { [weak self] in
            for await event in stream {
                await self?.handle(event: event)
                if Task.isCancelled { return }
            }
        }
    }

    public func stop() {
        subscriptionTask?.cancel()
        subscriptionTask = nil
        tapping.stop()
        holdTimer?.cancel()
        holdTimer = nil
        lastKillTap = nil
    }

    /// Swap the safety config without restarting the underlying tap.
    /// Resets in-flight gesture state so a half-completed hold from the
    /// old config doesn't bleed into the new one.
    public func updateConfig(_ newConfig: AutoKeysSafetyConfig) {
        config = newConfig
        holdTimer?.cancel()
        holdTimer = nil
        lastKillTap = nil
    }

    /// Read the current safety config. The cycler caches `resumeGrace`
    /// from this at start so its engagement handler doesn't bounce into
    /// the monitor on every event.
    public func currentConfig() -> AutoKeysSafetyConfig {
        config
    }

    public func observe() -> AsyncStream<EngagementEvent> {
        AsyncStream { continuation in
            let id = UUID()
            continuations[id] = continuation
            continuation.onTermination = { @Sendable [weak self] _ in
                Task { await self?.removeContinuation(id) }
            }
        }
    }

    // MARK: - Event handling

    private func handle(event: TappedEvent) async {
        if event.isSelfTagged { return }

        switch event.kind {
        case .mouseMoved:
            broadcast(.userEngaged)

        case .keyDown(let keyCode):
            // Diagnostic log — useful to confirm whether the kill combo
            // is even being seen + why a near-miss didn't match.
            NSLog("[RORORO] safety: keyDown code=\(keyCode) mods=\(event.modifiers) — kill expects code=\(config.killKey.keyCode) mods=\(config.killKey.modifiers)")
            // Exclude the kill key from broadcasting `.userEngaged`.
            // The kill key is the user's INTENTIONAL control gesture
            // (start/pause/resume); treating it as engagement caused a
            // race where the first tap of a double-tap paused the
            // cycler via engagement, then the second tap's
            // `.killRequested` saw state == .paused and called resume —
            // so the user's "stop / pause" gesture actually restarted.
            if matchesKillCombo(keyCode: keyCode, modifiers: event.modifiers) {
                NSLog("[RORORO] safety: kill key matched — gesture=\(config.gesture)")
                handleKillKeyDown(at: event.timestamp)
                return
            }
            broadcast(.userEngaged)

        case .keyUp(let keyCode):
            // Kill-key release matters only for hold gestures. We don't
            // require the modifier to still be held for the keyUp —
            // releasing either part of the combo cancels the hold.
            if keyCode == config.killKey.keyCode {
                handleKillKeyUp()
            }
        }
    }

    /// Match `keyCode + modifiers` against the configured kill combo.
    /// Modifier match is by exact equality after masking — extra held
    /// modifiers (e.g. Shift+F19 when the user only configured F19)
    /// won't match, preventing accidental kills from stray modifiers.
    private func matchesKillCombo(keyCode: CGKeyCode, modifiers: UInt) -> Bool {
        guard keyCode == config.killKey.keyCode else { return false }
        return (modifiers & KillKeyCombo.relevantModifierMask) == config.killKey.modifiers
    }

    private func handleKillKeyDown(at timestamp: Date) {
        switch config.gesture {
        case .holdFor(let seconds):
            // Schedule a future broadcast at the deadline. The keyUp
            // handler cancels it if the user releases early.
            holdTimer?.cancel()
            holdTimer = Task { [weak self, seconds] in
                try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                if Task.isCancelled { return }
                await self?.fireKill()
            }

        case .doubleTap(let within):
            if let last = lastKillTap, timestamp.timeIntervalSince(last) <= within {
                broadcast(.killRequested)
                lastKillTap = nil
            } else {
                lastKillTap = timestamp
            }
        }
    }

    private func handleKillKeyUp() {
        if case .holdFor = config.gesture {
            holdTimer?.cancel()
            holdTimer = nil
        }
    }

    private func fireKill() {
        broadcast(.killRequested)
        holdTimer = nil
    }

    private func broadcast(_ event: EngagementEvent) {
        if event == .killRequested {
            NSLog("[RORORO] safety: BROADCAST killRequested to \(continuations.count) subscriber(s)")
        }
        for continuation in continuations.values {
            continuation.yield(event)
        }
    }

    private func removeContinuation(_ id: UUID) {
        continuations[id] = nil
    }
}
