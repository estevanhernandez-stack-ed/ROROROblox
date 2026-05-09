// AutoKeysStep.swift
// Domain — single keystroke + repeat count + delay-after for the
// auto-keys cycler (Slope C).
//
// Stores a macOS virtual keyCode (49 = spacebar, 18-29 = number row, etc.)
// rather than a character, so the captured intent survives a keyboard
// layout change. `delayAfter` is how long the cycler sleeps before
// advancing to the next step in the sequence; the on-disk shape is
// always seconds in `TimeInterval`. `repeatCount` lets one step fire
// the same key N times (some Roblox keybinds are spammable — e.g.
// repeat-press to swing a weapon twice). Repeats fire at a fixed
// 0.7 s interval — capped a bit faster than 1/sec; faster repeats
// would either coalesce in the engine or look janky to the user.
//
// Codable shape lives inside `Account.autoKeys`. Custom decoder
// defaults `repeatCount` to 1 when missing so pre-wave-3c json files
// load cleanly.

import CoreGraphics
import Foundation

public struct AutoKeysStep: Codable, Equatable, Sendable {
    public let keyCode: CGKeyCode
    public let delayAfter: TimeInterval
    /// How many times to fire the keystroke before moving on. Default 1
    /// (single press). Cap at 20 — anything more is almost certainly a
    /// mistake and would push the cycle past `CycleBudget.hardCap`.
    public let repeatCount: Int

    /// Fixed gap between consecutive presses within one step.
    /// 0.7 s = a beat slower than 1/sec, fast enough to feel like
    /// "spamming a button" but slow enough that Roblox doesn't
    /// coalesce the events.
    public static let intraRepeatInterval: TimeInterval = 0.7
    public static let maxRepeatCount: Int = 20

    public init(keyCode: CGKeyCode, delayAfter: TimeInterval, repeatCount: Int = 1) {
        self.keyCode = keyCode
        self.delayAfter = delayAfter
        self.repeatCount = min(max(1, repeatCount), Self.maxRepeatCount)
    }

    /// The "keep me alive" default — spacebar with the given delay-after.
    /// Spacebar is virtual keyCode 49 on every macOS layout.
    public static func spacebar(after delay: TimeInterval = 0) -> AutoKeysStep {
        AutoKeysStep(keyCode: 49, delayAfter: delay)
    }

    // MARK: - Codable migration

    private enum CodingKeys: String, CodingKey {
        case keyCode
        case delayAfter
        case repeatCount
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let code = try container.decode(CGKeyCode.self, forKey: .keyCode)
        let delay = try container.decode(TimeInterval.self, forKey: .delayAfter)
        // Pre-wave-3c json files don't carry repeatCount; default 1.
        let count = try container.decodeIfPresent(Int.self, forKey: .repeatCount) ?? 1
        self.keyCode = code
        self.delayAfter = delay
        self.repeatCount = min(max(1, count), Self.maxRepeatCount)
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(keyCode, forKey: .keyCode)
        try container.encode(delayAfter, forKey: .delayAfter)
        try container.encode(repeatCount, forKey: .repeatCount)
    }
}
