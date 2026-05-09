// AutoKeysCyclerTests.swift
// Covers the actor's loop ordering, focus-failure skip, stop-during-sleep
// cancellation, and budget refusal at start. Real `CGEvent`, `IOKit`, and
// `NSRunningApplication` calls are stubbed via the fake conformances
// below — the production wiring is exercised by the integration test
// (item #12).

import XCTest
import CoreGraphics
@testable import RORORO

final class AutoKeysCyclerTests: XCTestCase {

    // MARK: - Fakes

    final class FakeKeyEventPoster: KeyEventPoster, @unchecked Sendable {
        let lock = NSLock()
        private(set) var posted: [CGKeyCode] = []
        func post(keyCode: CGKeyCode) async {
            lock.lock(); defer { lock.unlock() }
            posted.append(keyCode)
        }
        func snapshot() -> [CGKeyCode] {
            lock.lock(); defer { lock.unlock() }
            return posted
        }
    }

    final class FakeWindowFocuser: WindowFocuser, @unchecked Sendable {
        let lock = NSLock()
        private(set) var focused: [pid_t] = []
        var pidsToFail: Set<pid_t> = []
        func focus(pid: pid_t) async throws {
            lock.lock(); defer { lock.unlock() }
            if pidsToFail.contains(pid) {
                throw WindowFocuserError.notRunning(pid: pid)
            }
            focused.append(pid)
        }
        func snapshot() -> [pid_t] {
            lock.lock(); defer { lock.unlock() }
            return focused
        }
    }

    final class FakePowerAssertion: PowerAssertion, @unchecked Sendable {
        let lock = NSLock()
        private(set) var acquireCount = 0
        private(set) var releaseCount = 0
        private(set) var isHeld = false
        func acquire(reason: String) throws {
            lock.lock(); defer { lock.unlock() }
            guard !isHeld else { return }
            isHeld = true
            acquireCount += 1
        }
        func release() {
            lock.lock(); defer { lock.unlock() }
            guard isHeld else { return }
            isHeld = false
            releaseCount += 1
        }
        func held() -> Bool {
            lock.lock(); defer { lock.unlock() }
            return isHeld
        }
    }

    /// Sleeper that records call sites for assertion + completes with a
    /// short real `Task.sleep` so the loop yields between iterations
    /// without burning CPU. Tight enough that the suite still runs fast.
    final class RecordingSleeper: Sleeper, @unchecked Sendable {
        let lock = NSLock()
        private(set) var sleepRequests: [TimeInterval] = []
        func sleep(seconds: TimeInterval) async throws {
            lock.lock()
            sleepRequests.append(seconds)
            lock.unlock()
            // Yield rather than actually wait `seconds`. The cancellation
            // tests need a real suspension point, so use a 1ms sleep —
            // long enough for `Task.cancel()` to land, short enough to
            // not drag the suite.
            try await Task.sleep(nanoseconds: 1_000_000)
        }
        func snapshot() -> [TimeInterval] {
            lock.lock(); defer { lock.unlock() }
            return sleepRequests
        }
    }

    // MARK: - Helpers

    private func makeCycler(
        poster: KeyEventPoster = FakeKeyEventPoster(),
        focuser: WindowFocuser = FakeWindowFocuser(),
        assertion: PowerAssertion = FakePowerAssertion(),
        sleeper: Sleeper = RecordingSleeper(),
        safety: AutoKeysSafetyMonitor? = nil
    ) -> AutoKeysCycler {
        AutoKeysCycler(
            poster: poster,
            focuser: focuser,
            assertion: assertion,
            sleeper: sleeper,
            safety: safety
        )
    }

    private func waitFor(
        _ condition: @Sendable () async -> Bool,
        timeout: TimeInterval = 2.0
    ) async {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if await condition() { return }
            try? await Task.sleep(nanoseconds: 5_000_000) // 5ms
        }
    }

    // MARK: - Tests

    func testStart_TwoAccountsTwoSteps_PostsInExpectedOrderAcrossTwoIterations() async throws {
        let poster = FakeKeyEventPoster()
        let focuser = FakeWindowFocuser()
        let assertion = FakePowerAssertion()
        let sleeper = RecordingSleeper()
        let cycler = makeCycler(poster: poster, focuser: focuser, assertion: assertion, sleeper: sleeper)

        let seqA = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 49, delayAfter: 0.001), // space
            AutoKeysStep(keyCode: 13, delayAfter: 0.001), // w
        ])!
        let seqB = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 0, delayAfter: 0.001),  // a
            AutoKeysStep(keyCode: 1, delayAfter: 0.001),  // s
        ])!

        try await cycler.start(
            accounts: [
                .init(pid: 100, sequence: seqA, label: "A"),
                .init(pid: 200, sequence: seqB, label: "B"),
            ],
            loopDelay: 0.001
        )

        // Wait until at least the first 8 keystrokes (= two full
        // iterations × 2 accounts × 2 keys each) have landed. The
        // earlier "focused twice" condition could race the second
        // iteration's last keystroke; counting posted events is the
        // tighter check.
        await waitFor {
            poster.snapshot().count >= 8
        }

        await cycler.stop()

        let posted = poster.snapshot()
        let focused = focuser.snapshot()

        // Expected ordering for the first two iterations:
        //   focus 100 → post 49 → post 13 → focus 200 → post 0 → post 1
        //   focus 100 → post 49 → post 13 → focus 200 → post 0 → post 1
        // We assert the first 12 posts + first 4 focuses match that
        // shape exactly — anything additional from a partial third
        // iteration is fine.
        let expectedFirst12: [CGKeyCode] = [49, 13, 0, 1, 49, 13, 0, 1]
        XCTAssertGreaterThanOrEqual(posted.count, expectedFirst12.count)
        XCTAssertEqual(Array(posted.prefix(expectedFirst12.count)), expectedFirst12)

        let expectedFirst4Focuses: [pid_t] = [100, 200, 100, 200]
        XCTAssertGreaterThanOrEqual(focused.count, expectedFirst4Focuses.count)
        XCTAssertEqual(Array(focused.prefix(expectedFirst4Focuses.count)), expectedFirst4Focuses)

        // Wake-lock acquired once on start, released on stop.
        XCTAssertEqual(assertion.acquireCount, 1)
        XCTAssertEqual(assertion.releaseCount, 1)
        XCTAssertFalse(assertion.held())
    }

    func testStart_FocusFailureOnAccountA_SkipsAndContinuesToBSameIteration() async throws {
        let poster = FakeKeyEventPoster()
        let focuser = FakeWindowFocuser()
        focuser.pidsToFail = [100]
        let assertion = FakePowerAssertion()
        let sleeper = RecordingSleeper()
        let cycler = makeCycler(poster: poster, focuser: focuser, assertion: assertion, sleeper: sleeper)

        let seqA = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 49, delayAfter: 0.001),
        ])!
        let seqB = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 13, delayAfter: 0.001),
        ])!

        try await cycler.start(
            accounts: [
                .init(pid: 100, sequence: seqA, label: "A"),
                .init(pid: 200, sequence: seqB, label: "B"),
            ],
            loopDelay: 0.001
        )

        // Wait for B's keystroke (13) to land at least twice — proving
        // the loop didn't get stuck on A's focus failure.
        await waitFor { poster.snapshot().filter { $0 == 13 }.count >= 2 }

        await cycler.stop()

        let posted = poster.snapshot()
        // A's keystroke (49) should NEVER post — focus always fails.
        XCTAssertFalse(posted.contains(49), "A's keystroke posted despite focus failure: \(posted)")
        // B's keystroke (13) posts every iteration.
        XCTAssertGreaterThanOrEqual(posted.filter { $0 == 13 }.count, 2)
        // Focus failures are still recorded as attempts (in pidsToFail
        // they throw before being added to `focused`, but the loop tries
        // each iteration).
    }

    func testStop_DuringLoopDelaySleep_CancelsLoopAndReleasesAssertion() async throws {
        let assertion = FakePowerAssertion()
        // Use a sleeper whose long delay we can stop mid-flight.
        // RecordingSleeper sleeps 1ms regardless; a separate sleeper
        // that genuinely waits a longer interval verifies cancellation
        // semantics.
        let sleeper = LongSleeper(delaySeconds: 30)
        let cycler = makeCycler(assertion: assertion, sleeper: sleeper)

        let seq = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 49, delayAfter: 30),
        ])!

        try await cycler.start(
            accounts: [.init(pid: 100, sequence: seq, label: "A")],
            loopDelay: 30
        )

        // Wait until the loop has entered at least one sleep call.
        await waitFor { sleeper.sleepStarted }

        XCTAssertTrue(assertion.held(), "Wake-lock should be held while running")

        let stopBegin = Date()
        await cycler.stop()
        let stopElapsed = Date().timeIntervalSince(stopBegin)

        // Stop should NOT block until the long sleep finishes (would be
        // 30s); cancellation should cut it short.
        XCTAssertLessThan(stopElapsed, 1.0, "stop() blocked on a sleep that should have been cancelled")

        XCTAssertFalse(assertion.held(), "Wake-lock should be released after stop")
        XCTAssertEqual(assertion.releaseCount, 1)

        // State now reflects user-requested stop.
        let state = await cycler.state
        XCTAssertEqual(state, AutoKeysCycler.State.stopped(reason: .userRequested))
    }

    func testStart_BudgetExceeded_ThrowsAndDoesNotAcquireAssertion() async throws {
        let assertion = FakePowerAssertion()
        let cycler = makeCycler(assertion: assertion)

        // Three steps × 7 minutes each = 21 min, well over the 19-min cap.
        let pathological = AutoKeysSequence(steps: [
            AutoKeysStep(keyCode: 49, delayAfter: 7 * 60),
            AutoKeysStep(keyCode: 13, delayAfter: 7 * 60),
            AutoKeysStep(keyCode: 0, delayAfter: 7 * 60),
        ])!

        do {
            try await cycler.start(
                accounts: [.init(pid: 100, sequence: pathological, label: "A")],
                loopDelay: 0
            )
            XCTFail("start() should have thrown for over-cap snapshot")
        } catch let AutoKeysCycler.StartError.budgetExceeded(estimated, hardCap) {
            XCTAssertEqual(hardCap, CycleBudget.hardCap, accuracy: 0.001)
            XCTAssertGreaterThan(estimated, hardCap)
        } catch {
            XCTFail("Wrong error: \(error)")
        }

        // Wake-lock NEVER acquired — overflow refusal is a pre-flight check.
        XCTAssertEqual(assertion.acquireCount, 0)
        XCTAssertFalse(assertion.held())

        // State stays stopped (never transitioned to .running).
        let state = await cycler.state
        if case .stopped = state {
            // ok
        } else {
            XCTFail("Expected .stopped, got \(state)")
        }
    }

    func testStart_AllEmptySequences_TransitionsToNoTargetsConfigured() async throws {
        let assertion = FakePowerAssertion()
        let cycler = makeCycler(assertion: assertion)

        let empty = AutoKeysSequence(steps: [])!

        try await cycler.start(
            accounts: [.init(pid: 100, sequence: empty, label: "A")],
            loopDelay: 0.01
        )

        let state = await cycler.state
        XCTAssertEqual(state, AutoKeysCycler.State.stopped(reason: .noTargetsConfigured))
        XCTAssertFalse(assertion.held())
    }

    func testObserve_EmitsCurrentStateOnSubscribeAndOnTransitions() async throws {
        let cycler = makeCycler()
        let stream = await cycler.observe()
        var iterator = stream.makeAsyncIterator()

        // First yield is the current state at subscribe time.
        let initial = await iterator.next()
        XCTAssertEqual(initial, AutoKeysCycler.State.stopped(reason: nil))

        let seq = AutoKeysSequence(steps: [AutoKeysStep(keyCode: 49, delayAfter: 0.001)])!
        try await cycler.start(
            accounts: [.init(pid: 100, sequence: seq, label: "A")],
            loopDelay: 0.001
        )

        // Next yield is the .running transition.
        let running = await iterator.next()
        if case let .running(pids) = running {
            XCTAssertEqual(pids, [100])
        } else {
            XCTFail("Expected .running, got \(String(describing: running))")
        }

        await cycler.stop()

        // Final yield is the .stopped(.userRequested) transition.
        let stopped = await iterator.next()
        XCTAssertEqual(stopped, AutoKeysCycler.State.stopped(reason: .userRequested))
    }

    // MARK: - Safety integration (Slope C wave 3, ADR 0004 Decision 9)

    func testEngagement_PausesCyclerAndAutoResumesAfterGrace() async throws {
        let poster = FakeKeyEventPoster()
        let focuser = FakeWindowFocuser()
        let assertion = FakePowerAssertion()
        let sleeper = RecordingSleeper()
        let tapping = FakeEventTapping()
        // 200ms grace so the auto-resume lands inside the test's runtime.
        let safety = AutoKeysSafetyMonitor(
            tapping: tapping,
            config: AutoKeysSafetyConfig(
                killKeyCode: 80,
                gesture: .holdFor(seconds: 1),
                resumeGrace: 0.2
            )
        )
        let cycler = makeCycler(
            poster: poster,
            focuser: focuser,
            assertion: assertion,
            sleeper: sleeper,
            safety: safety
        )

        // Wave 3c: cycler no longer manages safety lifecycle — that
        // moved to AutoKeysCyclerViewModel. Tests that drive engagement
        // events through a fake tapping must start the monitor
        // explicitly before subscribing.
        await safety.start()

        let seq = AutoKeysSequence(steps: [AutoKeysStep(keyCode: 49, delayAfter: 0.001)])!
        try await cycler.start(
            accounts: [.init(pid: 100, sequence: seq, label: "A")],
            loopDelay: 0.01
        )

        // Wait for the loop to fire at least once.
        await waitFor { poster.snapshot().count >= 1 }

        // Inject a mouse-moved event → cycler pauses.
        tapping.send(TappedEvent(kind: .mouseMoved, timestamp: Date(), isSelfTagged: false))

        // Wait until the cycler reflects the paused state.
        await waitFor {
            if case .paused(AutoKeysCycler.PauseReason.userEngaged, _) = await cycler.state {
                return true
            }
            return false
        }

        // After ~200ms (resumeGrace), the cycler should auto-resume.
        await waitFor(timeout: 1.5) {
            if case .running = await cycler.state { return true }
            return false
        }

        let postResumeState = await cycler.state
        if case .running = postResumeState {
            // ok
        } else {
            XCTFail("Expected auto-resume to .running, got \(postResumeState)")
        }

        await cycler.stop()
    }

    // Wave 3c removed cycler's own kill-event handling (it now no-ops
    // on `.killRequested`; the view-model owns kill dispatch to avoid
    // a parallel-handler restart race). The end-to-end kill test moved
    // to view-model territory; the cycler-side test was removed since
    // it asserted a code path that intentionally no longer exists.

    func testExplicitPauseResume_RoundTripsThroughRunning() async throws {
        let cycler = makeCycler()

        let seq = AutoKeysSequence(steps: [AutoKeysStep(keyCode: 49, delayAfter: 0.001)])!
        try await cycler.start(
            accounts: [.init(pid: 100, sequence: seq, label: "A")],
            loopDelay: 0.01
        )

        await cycler.pause()

        let pausedState = await cycler.state
        XCTAssertEqual(pausedState, AutoKeysCycler.State.paused(reason: .userRequested, until: nil))

        await cycler.resume()

        let resumedState = await cycler.state
        if case .running = resumedState {
            // ok
        } else {
            XCTFail("Expected .running after resume, got \(resumedState)")
        }

        await cycler.stop()
    }

    func testSelfTaggedEvents_DoNotPauseTheCycler() async throws {
        let poster = FakeKeyEventPoster()
        let assertion = FakePowerAssertion()
        let tapping = FakeEventTapping()
        let safety = AutoKeysSafetyMonitor(
            tapping: tapping,
            config: .default
        )
        let cycler = makeCycler(poster: poster, assertion: assertion, safety: safety)
        await safety.start()

        let seq = AutoKeysSequence(steps: [AutoKeysStep(keyCode: 49, delayAfter: 0.001)])!
        try await cycler.start(
            accounts: [.init(pid: 100, sequence: seq, label: "A")],
            loopDelay: 0.01
        )

        // Inject a flood of self-tagged events that mimic the cycler's
        // own posted keystrokes coming back via the global monitor.
        for _ in 0..<10 {
            tapping.send(TappedEvent(kind: .keyDown(49), timestamp: Date(), isSelfTagged: true))
            tapping.send(TappedEvent(kind: .keyUp(49), timestamp: Date(), isSelfTagged: true))
        }

        // Give the safety task a chance to consume the events.
        try await Task.sleep(nanoseconds: 100_000_000)

        let state = await cycler.state
        if case .running = state {
            // ok — self-tagged events did NOT pause us.
        } else {
            XCTFail("Cycler paused on self-tagged events: \(state)")
        }

        await cycler.stop()
    }
}

/// Sleeper that observably enters a long sleep so the cancellation test
/// can wait for the loop to be inside a `Task.sleep` before calling
/// `stop()`.
private final class LongSleeper: Sleeper, @unchecked Sendable {
    private let lock = NSLock()
    private var _started = false
    let delaySeconds: TimeInterval

    init(delaySeconds: TimeInterval) {
        self.delaySeconds = delaySeconds
    }

    var sleepStarted: Bool {
        lock.lock(); defer { lock.unlock() }
        return _started
    }

    func sleep(seconds: TimeInterval) async throws {
        lock.lock(); _started = true; lock.unlock()
        try await Task.sleep(nanoseconds: UInt64(delaySeconds * 1_000_000_000))
    }
}
