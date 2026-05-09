// AutoKeysStepTests.swift
// Domain value type — keystroke + delay-after.

import XCTest
@testable import RORORO

final class AutoKeysStepTests: XCTestCase {

    func testRoundTripsThroughJSON() throws {
        let original = AutoKeysStep(keyCode: 49, delayAfter: 2.5)
        let data = try JSONEncoder().encode(original)
        let decoded = try JSONDecoder().decode(AutoKeysStep.self, from: data)
        XCTAssertEqual(original, decoded)
    }

    func testSpacebarConvenience_DefaultDelay() {
        let step = AutoKeysStep.spacebar()
        XCTAssertEqual(step.keyCode, 49)
        XCTAssertEqual(step.delayAfter, 0)
    }

    func testSpacebarConvenience_CustomDelay() {
        let step = AutoKeysStep.spacebar(after: 5)
        XCTAssertEqual(step.keyCode, 49)
        XCTAssertEqual(step.delayAfter, 5)
    }
}
