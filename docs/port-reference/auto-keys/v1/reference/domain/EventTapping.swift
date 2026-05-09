// EventTapping.swift
// Domain — DI seam over global mouse + key event monitoring for the
// auto-keys safety detector (Slope C, ADR 0004 Decision 9). The
// production conformance registers `NSEvent` global + local monitors
// for `.mouseMoved` and `.keyDown` / `.keyUp`; tests substitute a fake
// that injects events via a `send` method.
//
// "Self-tagged" events: every `CGEvent` the cycler posts is stamped with
// `eventSourceUserData = AutoKeysCyclerSourceTag`. The tap reads the
// same field on incoming events and reports `isSelfTagged: true` so the
// safety monitor can ignore them — otherwise the cycler would pause
// itself on every keystroke it fires.

import AppKit
import CoreGraphics
import Foundation

/// Magic constant tagged onto every `CGEvent` the cycler posts. ASCII
/// "RORO" packed into 32 bits. The safety monitor ignores events whose
/// `eventSourceUserData` matches this value. Picked to be visibly
/// recognizable in a debugger; collisions with other apps' tags are
/// extremely unlikely.
public let AutoKeysCyclerSourceTag: Int64 = 0x52_4F_52_4F // "RORO"

public struct TappedEvent: Equatable, Sendable {
    public enum Kind: Equatable, Sendable {
        case mouseMoved
        case keyDown(CGKeyCode)
        case keyUp(CGKeyCode)
    }
    public let kind: Kind
    public let timestamp: Date
    /// True iff the event's `eventSourceUserData` equals
    /// `AutoKeysCyclerSourceTag`. The safety monitor ignores these so
    /// the cycler doesn't pause on its own keystrokes.
    public let isSelfTagged: Bool
    /// Held modifier flags at the moment the event fired, masked to
    /// `KillKeyCombo.relevantModifierMask` (Shift / Control / Option /
    /// Command). Empty for `.mouseMoved`. Used by the safety monitor
    /// to match composite kill-key gestures like Shift+F19.
    public let modifiers: UInt

    public init(kind: Kind, timestamp: Date, isSelfTagged: Bool, modifiers: UInt = 0) {
        self.kind = kind
        self.timestamp = timestamp
        self.isSelfTagged = isSelfTagged
        self.modifiers = modifiers
    }
}

public protocol EventTapping: Sendable {
    /// Begin capturing events. Returns a stream the safety monitor walks
    /// in a long-lived task. Stopping cancels the underlying monitors.
    func start() -> AsyncStream<TappedEvent>
    /// Tear down the underlying monitors. Idempotent.
    func stop()
}

/// Production conformance backed by `NSEvent` global + local monitors.
/// Global fires when our app is NOT frontmost; local fires when events
/// are routed to our app. Both together cover all input regardless of
/// which app is frontmost.
///
/// Input Monitoring TCC is required for the global `.keyDown` /
/// `.keyUp` monitors; without it those handlers silently never fire.
/// The recorder checks consent via `IOHIDCheckAccess` before kicking
/// off the cycler and surfaces a banner with a deep-link if denied.
public final class NSEventTapping: EventTapping, @unchecked Sendable {

    private let lock = NSLock()
    private var globalMonitors: [Any] = []
    private var localMonitors: [Any] = []
    private var continuation: AsyncStream<TappedEvent>.Continuation?

    public init() {}

    public func start() -> AsyncStream<TappedEvent> {
        AsyncStream { [weak self] continuation in
            guard let self else { continuation.finish(); return }
            self.lock.lock()
            self.continuation = continuation
            self.installMonitorsLocked()
            self.lock.unlock()

            continuation.onTermination = { @Sendable [weak self] _ in
                self?.stop()
            }
        }
    }

    public func stop() {
        lock.lock(); defer { lock.unlock() }
        for m in globalMonitors { NSEvent.removeMonitor(m) }
        for m in localMonitors  { NSEvent.removeMonitor(m) }
        globalMonitors.removeAll()
        localMonitors.removeAll()
        continuation?.finish()
        continuation = nil
    }

    /// Caller must hold `lock`.
    private func installMonitorsLocked() {
        let mask: NSEvent.EventTypeMask = [.mouseMoved, .keyDown, .keyUp]

        let globalHandler: (NSEvent) -> Void = { [weak self] event in
            self?.deliver(event: event, isLocal: false)
        }
        let localHandler: (NSEvent) -> NSEvent? = { [weak self] event in
            self?.deliver(event: event, isLocal: true)
            return event
        }

        if let g = NSEvent.addGlobalMonitorForEvents(matching: mask, handler: globalHandler) {
            globalMonitors.append(g)
        }
        if let l = NSEvent.addLocalMonitorForEvents(matching: mask, handler: localHandler) {
            localMonitors.append(l)
        }
    }

    private func deliver(event: NSEvent, isLocal: Bool) {
        guard let kind = Self.classify(event) else { return }
        let isSelfTagged = Self.isSelfTagged(event)
        let modifiers = event.modifierFlags.rawValue & KillKeyCombo.relevantModifierMask
        let tapped = TappedEvent(
            kind: kind,
            timestamp: Date(),
            isSelfTagged: isSelfTagged,
            modifiers: modifiers
        )
        lock.lock()
        let cont = continuation
        lock.unlock()
        cont?.yield(tapped)
    }

    private static func classify(_ event: NSEvent) -> TappedEvent.Kind? {
        switch event.type {
        case .mouseMoved:
            return .mouseMoved
        case .keyDown:
            return .keyDown(CGKeyCode(event.keyCode))
        case .keyUp:
            return .keyUp(CGKeyCode(event.keyCode))
        default:
            return nil
        }
    }

    private static func isSelfTagged(_ event: NSEvent) -> Bool {
        guard let cg = event.cgEvent else { return false }
        let userData = cg.getIntegerValueField(.eventSourceUserData)
        return userData == AutoKeysCyclerSourceTag
    }
}
