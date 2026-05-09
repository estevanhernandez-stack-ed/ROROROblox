// AutoKeysSafetyMonitorTests.swift
// Drives the safety monitor with a fake `EventTapping` so we can inject
// arbitrary mouse / key events and assert what the monitor emits to its
// `observe()` stream. Production NSEvent wiring + Input Monitoring TCC
// are out of scope here; the integration test (item #12 / wave 4) covers
// the production path.

import XCTest
import CoreGraphics
@testable import RORORO

/// Module-internal so AutoKeysCyclerTests can drive the safety integration
/// with synthesized events too.
final class FakeEventTapping: EventTapping, @unchecked Sendable {
    let lock = NSLock()
    private var continuation: AsyncStream<TappedEvent>.Continuation?
    private(set) var startCount = 0
    private(set) var stopCount = 0

    func start() -> AsyncStream<TappedEvent> {
        lock.lock()
        startCount += 1
        lock.unlock()
        return AsyncStream { c in
            self.lock.lock()
            self.continuation = c
            self.lock.unlock()
        }
    }

    func stop() {
        lock.lock()
        stopCount += 1
        continuation?.finish()
        continuation = nil
        lock.unlock()
    }

    func send(_ event: TappedEvent) {
        lock.lock()
        let c = continuation
        lock.unlock()
        c?.yield(event)
    }
}

final class AutoKeysSafetyMonitorTests: XCTestCase {

    // MARK: - Helpers

    private func collect(
        _ stream: AsyncStream<EngagementEvent>,
        count: Int,
        timeout: TimeInterval = 2.0
    ) async -> [EngagementEvent] {
        var collected: [EngagementEvent] = []
        let deadline = Date().addingTimeInterval(timeout)
        var iterator = stream.makeAsyncIterator()
        while collected.count < count, Date() < deadline {
            if let event = await iterator.next() {
                collected.append(event)
            }
        }
        return collected
    }

    // MARK: - Tests

    func testStart_BeginsTappingAndStopReleases() async {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(tapping: tapping)

        await monitor.start()
        XCTAssertEqual(tapping.startCount, 1)
        XCTAssertEqual(tapping.stopCount, 0)

        await monitor.stop()
        XCTAssertEqual(tapping.stopCount, 1)
    }

    func testMouseMoved_EmitsUserEngaged() async {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(tapping: tapping)
        await monitor.start()
        let stream = await monitor.observe()

        tapping.send(TappedEvent(kind: .mouseMoved, timestamp: Date(), isSelfTagged: false))

        let events = await collect(stream, count: 1)
        XCTAssertEqual(events, [.userEngaged])

        await monitor.stop()
    }

    func testKeyDown_EmitsUserEngaged() async {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(tapping: tapping)
        await monitor.start()
        let stream = await monitor.observe()

        // Some non-kill key.
        tapping.send(TappedEvent(kind: .keyDown(13), timestamp: Date(), isSelfTagged: false))

        let events = await collect(stream, count: 1)
        XCTAssertEqual(events, [.userEngaged])

        await monitor.stop()
    }

    func testSelfTaggedEvents_AreIgnored() async {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(tapping: tapping)
        await monitor.start()
        let stream = await monitor.observe()

        // Self-tagged keystroke (the cycler firing) — must NOT engage.
        tapping.send(TappedEvent(kind: .keyDown(49), timestamp: Date(), isSelfTagged: true))
        tapping.send(TappedEvent(kind: .keyUp(49), timestamp: Date(), isSelfTagged: true))

        // Then a non-tagged event so we have something to wait for.
        tapping.send(TappedEvent(kind: .mouseMoved, timestamp: Date(), isSelfTagged: false))

        let events = await collect(stream, count: 1)
        XCTAssertEqual(events, [.userEngaged], "Self-tagged events should be filtered out")

        await monitor.stop()
    }

    func testHoldGesture_FiresKillAfterDeadline() async throws {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(
            tapping: tapping,
            config: AutoKeysSafetyConfig(
                killKeyCode: 80,
                gesture: .holdFor(seconds: 0.05),
                resumeGrace: 5
            )
        )
        await monitor.start()
        let stream = await monitor.observe()

        let now = Date()
        tapping.send(TappedEvent(kind: .keyDown(80), timestamp: now, isSelfTagged: false))

        // Kill-key keyDown no longer broadcasts `.userEngaged` (wave 3c
        // bug fix — was causing a race where the first tap of a
        // double-tap triggered an engagement pause that swallowed the
        // second tap's kill-toggle effect). Now: only `.killRequested`
        // fires, after the hold deadline.
        let events = await collect(stream, count: 1, timeout: 1.0)
        XCTAssertEqual(events, [.killRequested])

        await monitor.stop()
    }

    func testHoldGesture_KeyUpBeforeDeadline_DoesNotFireKill() async throws {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(
            tapping: tapping,
            config: AutoKeysSafetyConfig(
                killKeyCode: 80,
                gesture: .holdFor(seconds: 0.5),
                resumeGrace: 5
            )
        )
        await monitor.start()
        let stream = await monitor.observe()

        tapping.send(TappedEvent(kind: .keyDown(80), timestamp: Date(), isSelfTagged: false))
        // Release well before the 500ms deadline.
        try await Task.sleep(nanoseconds: 50_000_000) // 50ms
        tapping.send(TappedEvent(kind: .keyUp(80), timestamp: Date(), isSelfTagged: false))

        // Wave 3c: kill-key keyDown no longer fires `.userEngaged`,
        // so we never expect any output from this gesture — keyUp
        // before the hold deadline cancels the hold timer cleanly.
        // Race the deadline + a buffer; if killRequested arrives, the
        // cancel didn't take.
        let waitDeadline = Date().addingTimeInterval(0.7)
        while Date() < waitDeadline {
            try? await Task.sleep(nanoseconds: 100_000_000)
        }
        // Send a mouseMoved as a sentinel; the next event yielded
        // must be that sentinel, not a stray kill.
        tapping.send(TappedEvent(kind: .mouseMoved, timestamp: Date(), isSelfTagged: false))
        var iterator = stream.makeAsyncIterator()
        let first = await iterator.next()
        XCTAssertEqual(first, .userEngaged, "Sentinel mouseMoved should be the first event since kill-key was released early.")

        await monitor.stop()
    }

    func testDoubleTapGesture_FiresKillOnSecondTapWithinWindow() async throws {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(
            tapping: tapping,
            config: AutoKeysSafetyConfig(
                killKeyCode: 80,
                gesture: .doubleTap(withinSeconds: 0.6),
                resumeGrace: 5
            )
        )
        await monitor.start()
        let stream = await monitor.observe()

        let t0 = Date()
        tapping.send(TappedEvent(kind: .keyDown(80), timestamp: t0, isSelfTagged: false))
        tapping.send(TappedEvent(kind: .keyDown(80), timestamp: t0.addingTimeInterval(0.2), isSelfTagged: false))

        // Wave 3c: kill-key keyDowns don't fire `.userEngaged`. The
        // second tap completes the double-tap → only `.killRequested`.
        let events = await collect(stream, count: 1, timeout: 1.0)
        XCTAssertEqual(events, [.killRequested])

        await monitor.stop()
    }

    func testDoubleTapGesture_SecondTapOutsideWindow_DoesNotFire() async throws {
        let tapping = FakeEventTapping()
        let monitor = AutoKeysSafetyMonitor(
            tapping: tapping,
            config: AutoKeysSafetyConfig(
                killKeyCode: 80,
                gesture: .doubleTap(withinSeconds: 0.1),
                resumeGrace: 5
            )
        )
        await monitor.start()
        let stream = await monitor.observe()

        let t0 = Date()
        tapping.send(TappedEvent(kind: .keyDown(80), timestamp: t0, isSelfTagged: false))
        // Second tap arrives 200ms later — outside the 100ms window.
        tapping.send(TappedEvent(kind: .keyDown(80), timestamp: t0.addingTimeInterval(0.2), isSelfTagged: false))

        // Wave 3c: kill-key keyDowns no longer fire `.userEngaged`,
        // and the second tap is outside the double-tap window so no
        // `.killRequested` either. Send a sentinel and verify nothing
        // fired before it.
        try await Task.sleep(nanoseconds: 200_000_000)
        tapping.send(TappedEvent(kind: .mouseMoved, timestamp: Date(), isSelfTagged: false))
        var iterator = stream.makeAsyncIterator()
        let first = await iterator.next()
        XCTAssertEqual(first, .userEngaged, "Only the sentinel mouseMoved should appear; the kill-key taps should have produced nothing.")

        await monitor.stop()
    }
}
