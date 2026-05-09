// Sleeper.swift
// Domain — DI seam over `Task.sleep` for the auto-keys cycler (Slope C).
// The cycler's loop wants to sleep `step.delayAfter` between keystrokes
// and `loopDelay` between iterations. Production calls real `Task.sleep`;
// tests substitute a fake that records call sites and yields immediately,
// so a 14-minute loopDelay doesn't bog down the test suite.

import Foundation

public protocol Sleeper: Sendable {
    /// Sleep for `seconds` (`TimeInterval`). Throws on cancellation —
    /// callers wanting fire-and-forget should `try?` it.
    func sleep(seconds: TimeInterval) async throws
}

public struct TaskSleeper: Sleeper {
    public init() {}

    public func sleep(seconds: TimeInterval) async throws {
        guard seconds > 0 else { return }
        try await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
    }
}
