// CycleBudgetTests.swift
// Pure validator — cycle estimate + state thresholds.

import XCTest
@testable import RORORO

final class CycleBudgetTests: XCTestCase {

    // MARK: - estimate

    func testEstimate_ZeroAccounts_OnlyLoopDelay() {
        let est = CycleBudget.estimate(snapshot: [], loopDelay: 60)
        XCTAssertEqual(est, 60, accuracy: 0.001)
    }

    func testEstimate_SingleAccount_ZeroDelays_OnlyFocusOverhead() {
        let seq = AutoKeysSequence(steps: [AutoKeysStep.spacebar(after: 0)])!
        let est = CycleBudget.estimate(snapshot: [seq], loopDelay: 0)
        XCTAssertEqual(est, CycleBudget.defaultFocusOverhead, accuracy: 0.001)
    }

    func testEstimate_MultiAccount_Cumulative() {
        let a = AutoKeysSequence(steps: [AutoKeysStep(keyCode: 49, delayAfter: 5)])!
        let b = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 18, delayAfter: 2),
            AutoKeysStep(keyCode: 19, delayAfter: 3),
        ])!
        let est = CycleBudget.estimate(snapshot: [a, b], loopDelay: 10)
        // 5 + (2+3) + (0.2 * 2) + 10 = 20.4
        XCTAssertEqual(est, 20.4, accuracy: 0.001)
    }

    func testEstimate_CustomFocusOverhead() {
        let seq = AutoKeysSequence(steps: [AutoKeysStep.spacebar(after: 1)])!
        let est = CycleBudget.estimate(snapshot: [seq], loopDelay: 0, focusOverhead: 0.5)
        // 1 + 0.5 + 0 = 1.5
        XCTAssertEqual(est, 1.5, accuracy: 0.001)
    }

    // MARK: - state

    func testState_BelowWarn_IsOK() {
        XCTAssertEqual(CycleBudget.state(for: 17 * 60), .ok)
    }

    func testState_AtWarn_IsWarn() {
        XCTAssertEqual(CycleBudget.state(for: 18 * 60), .warn)
    }

    func testState_BetweenWarnAndCap_IsWarn() {
        XCTAssertEqual(CycleBudget.state(for: 18 * 60 + 30), .warn)
    }

    func testState_AtHardCap_IsOverCap() {
        XCTAssertEqual(CycleBudget.state(for: 19 * 60), .overCap)
    }

    func testState_AboveHardCap_IsOverCap() {
        XCTAssertEqual(CycleBudget.state(for: 25 * 60), .overCap)
    }

    func testState_JustBelowWarn_IsOK() {
        XCTAssertEqual(CycleBudget.state(for: 18 * 60 - 0.001), .ok)
    }

    func testState_JustBelowCap_IsWarn() {
        XCTAssertEqual(CycleBudget.state(for: 19 * 60 - 0.001), .warn)
    }
}
