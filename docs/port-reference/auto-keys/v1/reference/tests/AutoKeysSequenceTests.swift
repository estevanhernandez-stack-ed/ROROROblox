// AutoKeysSequenceTests.swift
// Domain value type — ordered list of `AutoKeysStep`. The 3-step cap
// from the original ADR was relaxed in wave 3c; cycle-budget enforcement
// at the recorder + cycler is the real ceiling now.

import XCTest
@testable import RORORO

final class AutoKeysSequenceTests: XCTestCase {

    // MARK: - Construction

    func testInit_AcceptsEmpty() {
        XCTAssertNotNil(AutoKeysSequence(steps: []))
    }

    func testInit_AcceptsOneStep() {
        let seq = AutoKeysSequence(steps: [AutoKeysStep.spacebar()])
        XCTAssertNotNil(seq)
    }

    func testInit_AcceptsThreeSteps() {
        let three = (0..<3).map { _ in AutoKeysStep.spacebar() }
        XCTAssertNotNil(AutoKeysSequence(steps: three))
    }

    func testInit_AcceptsArbitraryLength() {
        // The 3-step cap was lifted in wave 3c — long sequences are
        // allowed; CycleBudget.hardCap is the real ceiling.
        let twenty = (0..<20).map { _ in AutoKeysStep.spacebar(after: 0.1) }
        let seq = AutoKeysSequence(steps: twenty)
        XCTAssertNotNil(seq)
        XCTAssertEqual(seq?.steps.count, 20)
    }

    // MARK: - totalDuration

    func testTotalDuration_EmptyIsZero() {
        let seq = AutoKeysSequence(steps: [])!
        XCTAssertEqual(seq.totalDuration, 0)
    }

    func testTotalDuration_SumsDelays() {
        let seq = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 49, delayAfter: 1.0),
            AutoKeysStep(keyCode: 18, delayAfter: 2.5),
            AutoKeysStep(keyCode: 19, delayAfter: 3.0),
        ])!
        XCTAssertEqual(seq.totalDuration, 6.5, accuracy: 0.0001)
    }

    // MARK: - isEmpty

    func testIsEmpty_TrueWhenNoSteps() {
        let seq = AutoKeysSequence(steps: [])!
        XCTAssertTrue(seq.isEmpty)
    }

    func testIsEmpty_FalseWhenSteps() {
        let seq = AutoKeysSequence(steps: [AutoKeysStep.spacebar()])!
        XCTAssertFalse(seq.isEmpty)
    }

    // MARK: - Codable

    func testRoundTripsThroughJSON() throws {
        let original = AutoKeysSequence(steps: [
            AutoKeysStep.spacebar(after: 2),
            AutoKeysStep(keyCode: 18, delayAfter: 5),
        ])!
        let data = try JSONEncoder().encode(original)
        let decoded = try JSONDecoder().decode(AutoKeysSequence.self, from: data)
        XCTAssertEqual(original, decoded)
    }

    func testDecode_AcceptsArbitraryStepCount() throws {
        // Decode no longer enforces a step cap — wave 3c relaxed the
        // original 3-step ADR rule. Cycle-budget validation in the
        // recorder gates oversized sequences from saving.
        let json = """
        { "steps": [
            { "keyCode": 49, "delayAfter": 0 },
            { "keyCode": 49, "delayAfter": 0 },
            { "keyCode": 49, "delayAfter": 0 },
            { "keyCode": 49, "delayAfter": 0 },
            { "keyCode": 49, "delayAfter": 0 }
        ] }
        """.data(using: .utf8)!
        let seq = try JSONDecoder().decode(AutoKeysSequence.self, from: json)
        XCTAssertEqual(seq.steps.count, 5)
    }

    func testDecode_AcceptsThreeSteps() throws {
        let json = """
        { "steps": [
            { "keyCode": 49, "delayAfter": 0 },
            { "keyCode": 18, "delayAfter": 1 },
            { "keyCode": 19, "delayAfter": 2 }
        ] }
        """.data(using: .utf8)!
        let seq = try JSONDecoder().decode(AutoKeysSequence.self, from: json)
        XCTAssertEqual(seq.steps.count, 3)
        XCTAssertEqual(seq.totalDuration, 3, accuracy: 0.0001)
    }
}
