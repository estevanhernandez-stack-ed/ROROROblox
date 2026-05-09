// AutoKeysSafetyConfig.swift
// Domain — value types describing the cycler's safety surface (Slope C,
// ADR 0004 Decision 9). Two pieces of user choice land here:
//
//   - which key is the kill key (a single CGKeyCode the user records during
//     setup; default suggestion is F19 = keyCode 80, which most Mac
//     keyboards lack physically and so is a near-zero-collision pick),
//   - which gesture stops the cycler (hold-for-1s OR double-tap-within-600ms).
//
// The hold-vs-double-tap choice is per-user: hold is mechanically safer
// (zero false-positive risk), double-tap is faster on the user's
// muscle memory. The recorder shows both and the user picks.

import CoreGraphics
import Foundation

/// Kill key + held modifiers (Slope C wave 3b). Modifiers are an
/// `OptionSet`-style bitmask of Shift / Control / Option / Command.
/// Storing as a bare `UInt` (not `NSEvent.ModifierFlags`) keeps this
/// type free of AppKit imports and makes Codable round-tripping
/// straightforward. Compare against `event.modifierFlags.rawValue`
/// after masking to the relevant bucket.
public struct KillKeyCombo: Codable, Equatable, Sendable {
    public let keyCode: CGKeyCode
    /// Required modifier bitmask. Match by equality after masking the
    /// incoming event flags to `KillKeyCombo.relevantModifierMask`. A
    /// value of 0 means the kill key is the bare keyCode (no modifier
    /// required) — pressing it with a stray modifier won't match.
    public let modifiers: UInt

    public init(keyCode: CGKeyCode, modifiers: UInt = 0) {
        self.keyCode = keyCode
        self.modifiers = modifiers
    }

    /// Bits we care about. Excludes lock states (caps lock, fn) and
    /// device-side flags. Matches `NSEvent.ModifierFlags.deviceIndependentFlagsMask`'s
    /// shift / control / option / command bits.
    /// Shift = 1<<17, Control = 1<<18, Option = 1<<19, Command = 1<<20.
    public static let relevantModifierMask: UInt =
        (1 << 17) | (1 << 18) | (1 << 19) | (1 << 20)
}

public enum KillGesture: Codable, Equatable, Sendable {
    /// Press and hold the kill key for `seconds` to stop the cycler.
    /// Default 1.0s. Releasing before the deadline cancels the gesture.
    case holdFor(seconds: TimeInterval)
    /// Press the kill key twice within `withinSeconds` to stop the cycler.
    /// Default 0.6s. Slower second tap resets the recognizer.
    case doubleTap(withinSeconds: TimeInterval)

    public static let defaultHold: KillGesture = .holdFor(seconds: 1.0)
    public static let defaultDoubleTap: KillGesture = .doubleTap(withinSeconds: 0.6)
}

public struct AutoKeysSafetyConfig: Codable, Equatable, Sendable {
    /// Kill key + required modifiers. Default F19 with no modifiers —
    /// most Mac keyboards skip F19 physically, making it a near-zero-
    /// collision pick. The recorder lets the user choose any other
    /// key (with optional Shift / Control / Option / Command modifiers)
    /// and warns against ones bound in Roblox.
    public let killKey: KillKeyCombo
    /// Gesture the user picked to trigger the kill. Either hold or
    /// double-tap; see `KillGesture`.
    public let gesture: KillGesture
    /// Grace period (seconds) after a `.userEngaged` pause before the
    /// cycler auto-resumes. Continued user input keeps extending the
    /// pause. Default 5s. ADR 0004 Decision 9.
    public let resumeGrace: TimeInterval

    public static let defaultKillKeyCode: CGKeyCode = 80 // F19

    public init(
        killKey: KillKeyCombo = KillKeyCombo(keyCode: AutoKeysSafetyConfig.defaultKillKeyCode),
        gesture: KillGesture = .defaultDoubleTap,
        // Default 1.5s — long enough to give the user a beat to abort
        // (double-tap kill) but short enough that just moving the mouse
        // to RORORO's toolbar doesn't stick the cycler in pause limbo.
        // Non-extending: subsequent engagement events while already
        // paused do NOT push the deadline out.
        resumeGrace: TimeInterval = 1.5
    ) {
        self.killKey = killKey
        self.gesture = gesture
        self.resumeGrace = resumeGrace
    }

    /// Convenience for callers that don't care about modifiers.
    public init(
        killKeyCode: CGKeyCode,
        gesture: KillGesture = .defaultHold,
        resumeGrace: TimeInterval = 5.0
    ) {
        self.init(
            killKey: KillKeyCombo(keyCode: killKeyCode),
            gesture: gesture,
            resumeGrace: resumeGrace
        )
    }

    public static let `default` = AutoKeysSafetyConfig()

    // MARK: - Codable migration

    private enum CodingKeys: String, CodingKey {
        case killKey       // new shape (post wave 3b modifier support)
        case killKeyCode   // legacy shape (pre wave 3b)
        case gesture
        case resumeGrace
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        if let combo = try? container.decode(KillKeyCombo.self, forKey: .killKey) {
            self.killKey = combo
        } else {
            // Legacy: bare keyCode, no modifiers. Migrate forward.
            let legacyCode = try container.decode(CGKeyCode.self, forKey: .killKeyCode)
            self.killKey = KillKeyCombo(keyCode: legacyCode)
        }
        self.gesture = try container.decode(KillGesture.self, forKey: .gesture)
        self.resumeGrace = try container.decode(TimeInterval.self, forKey: .resumeGrace)
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(killKey, forKey: .killKey)
        try container.encode(gesture, forKey: .gesture)
        try container.encode(resumeGrace, forKey: .resumeGrace)
    }
}

/// Events the safety monitor emits to the cycler.
public enum EngagementEvent: Equatable, Sendable {
    /// Any human input that wasn't a self-tagged cycler keystroke.
    /// The cycler responds by pausing and starting the resume-grace
    /// timer; continued input keeps extending the pause.
    case userEngaged
    /// The configured kill gesture completed (hold deadline reached, or
    /// second tap landed within the double-tap window). The cycler
    /// responds by stopping with `.stopped(.userKilled)`.
    case killRequested
}
