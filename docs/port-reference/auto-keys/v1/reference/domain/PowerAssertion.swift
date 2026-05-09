// PowerAssertion.swift
// Domain — DI seam over `IOPMAssertionCreateWithName` for the auto-keys
// cycler (Slope C). Per ADR 0004, while the cycler is running we hold a
// `kIOPMAssertionTypePreventUserIdleSystemSleep` assertion so the
// machine doesn't slip into idle sleep mid-cycle and clip the next
// focus + post pass.
//
// The protocol exists so `AutoKeysCycler` is testable without poking the
// real IOKit power-management surface. Production conformance is
// `IOPMPowerAssertion`; tests substitute a recording fake.
//
// Lifecycle: `acquire(reason:)` succeeds at most once; subsequent calls
// without an intervening `release()` no-op (idempotent — the cycler may
// transition through stop→start without losing the wake lock).

import Foundation
import IOKit
import IOKit.pwr_mgt

public protocol PowerAssertion: Sendable {
    /// Take the wake-lock. Idempotent — second call without release no-ops.
    func acquire(reason: String) throws
    /// Release the wake-lock. Idempotent — releasing a never-acquired
    /// assertion no-ops.
    func release()
}

public enum PowerAssertionError: Error, Equatable {
    /// IOKit returned a non-success code from `IOPMAssertionCreateWithName`.
    case ioKitError(Int32)
}

/// Production conformance. Owns a single `IOPMAssertionID`; serialized
/// access is the responsibility of the actor that holds it (the cycler).
public final class IOPMPowerAssertion: PowerAssertion, @unchecked Sendable {

    private var assertionID: IOPMAssertionID?

    public init() {}

    public func acquire(reason: String) throws {
        guard assertionID == nil else { return }
        var id: IOPMAssertionID = 0
        let result = IOPMAssertionCreateWithName(
            kIOPMAssertionTypePreventUserIdleSystemSleep as CFString,
            IOPMAssertionLevel(kIOPMAssertionLevelOn),
            reason as CFString,
            &id
        )
        guard result == kIOReturnSuccess else {
            throw PowerAssertionError.ioKitError(result)
        }
        assertionID = id
    }

    public func release() {
        guard let id = assertionID else { return }
        IOPMAssertionRelease(id)
        assertionID = nil
    }
}
