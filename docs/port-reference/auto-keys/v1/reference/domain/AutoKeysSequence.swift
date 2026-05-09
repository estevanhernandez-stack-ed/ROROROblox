// AutoKeysSequence.swift
// Domain — ordered list of `AutoKeysStep`s for one account (Slope C).
// No artificial step-count cap: the real ceiling is `CycleBudget.hardCap`
// against the total cycle time across all configured accounts. ADR 0004
// Decision 2's original 3-step cap was a UX guardrail; experience showed
// users want longer macros (full keybind rotations, multi-step ability
// chains). Cycle-budget enforcement on the recorder + cycler covers
// the safety case.
//
// `totalDuration` is the input to `CycleBudget.estimate`; an empty
// sequence (or nil on the Account) means the cycler skips the account.

import Foundation

public struct AutoKeysSequence: Codable, Equatable, Sendable {

    public let steps: [AutoKeysStep]

    /// Always-succeeds initializer (was failable when the 3-step cap
    /// was enforced). Kept as a failable init for source compatibility
    /// with existing callsites; never returns nil now.
    public init?(steps: [AutoKeysStep]) {
        self.steps = steps
    }

    /// Σ(delayAfter + intra-repeat gaps) — one walk through the
    /// sequence. Each step contributes `delayAfter` once (after its
    /// last press) plus `(repeatCount - 1) × intraRepeatInterval` for
    /// the gaps between presses. Used by `CycleBudget` to size the
    /// loop against `hardCap`.
    public var totalDuration: TimeInterval {
        steps.reduce(0) { acc, step in
            let repeats = max(0, step.repeatCount - 1)
            return acc + step.delayAfter + Double(repeats) * AutoKeysStep.intraRepeatInterval
        }
    }

    public var isEmpty: Bool { steps.isEmpty }
}
