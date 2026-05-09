// WindowFocuser.swift
// Domain — DI seam for cross-app focus (Slope C). Per ADR 0004
// Decision 1, the cycle walks each running Roblox window in turn:
// focus → settle → fire keys. This type owns the focus call + the
// post-focus settle; `KeyEventPoster` owns the keystrokes themselves.
//
// macOS 14+ activation gotcha: `NSRunningApplication.activate()` only
// reliably brings another app forward when the *calling* app is itself
// frontmost. Once the cycler steals focus the first time, RORORO is
// no longer frontmost, and subsequent `activate()` calls silently
// no-op — so the cycler thinks it's switching windows but every
// keystroke routes to whatever Roblox window the user is looking at.
//
// Fix: use the Accessibility API (`AXUIElementCreateApplication` +
// `kAXFrontmostAttribute`) which works cross-app regardless of caller
// state. We already need Accessibility TCC for posting events, so no
// new permission. We keep `NSRunningApplication.activate()` as a
// fallback for the (rare) case where AX fails.
//
// Throws `WindowFocuserError.notRunning` when the pid is no longer alive.

import AppKit
import ApplicationServices
import Foundation

public protocol WindowFocuser: Sendable {
    func focus(pid: pid_t) async throws
}

public enum WindowFocuserError: Error, Equatable {
    case notRunning(pid: pid_t)
}

public struct NSRunningApplicationFocuser: WindowFocuser {

    /// Time to wait after focus before treating it as landed.
    /// Tunable; bump if Roblox is slow to settle on busy systems.
    public let settleDelay: TimeInterval

    public init(settleDelay: TimeInterval = 0.500) {
        self.settleDelay = settleDelay
    }

    public func focus(pid: pid_t) async throws {
        guard let app = NSRunningApplication(processIdentifier: pid) else {
            throw WindowFocuserError.notRunning(pid: pid)
        }

        // Path 1: Accessibility API — BOTH set the app frontmost AND
        // raise the main window. The frontmost set alone isn't always
        // enough; raising the window forces it to the front of the
        // window stack and pulls the app forward, equivalent to a user
        // click. Together they're the most reliable cross-app focus
        // mechanism on macOS 14+ when the calling app isn't frontmost.
        let appElement = AXUIElementCreateApplication(pid)
        let frontmostResult = AXUIElementSetAttributeValue(
            appElement,
            kAXFrontmostAttribute as CFString,
            kCFBooleanTrue
        )

        var raiseResult: AXError = .success
        var mainWindow: AnyObject?
        let getResult = AXUIElementCopyAttributeValue(
            appElement,
            kAXMainWindowAttribute as CFString,
            &mainWindow
        )
        if getResult == .success, let window = mainWindow {
            // Force-cast is safe — AXUIElementCopyAttributeValue returns
            // an AXUIElement for window-typed attributes per Apple docs.
            raiseResult = AXUIElementPerformAction(
                window as! AXUIElement,
                kAXRaiseAction as CFString
            )
        }

        NSLog("[RORORO] focuser: pid=\(pid) AX setFrontmost=\(frontmostResult.rawValue) raise=\(raiseResult.rawValue)")

        // Path 2: NSRunningApplication.activate() fallback. When AX is
        // failing wholesale, at least try this — works on the first
        // iteration where RORORO is still frontmost.
        if frontmostResult != .success && raiseResult != .success {
            await MainActor.run {
                _ = app.activate()
            }
            NSLog("[RORORO] focuser: AX both paths failed for pid=\(pid); fell back to NSRunningApplication.activate")
        }

        // Verify focus actually landed instead of just sleeping a fixed
        // delay. Poll `NSWorkspace.frontmostApplication` until its PID
        // matches our target, with `settleDelay` as the timeout.
        // Solves the race where the cycler fired the key before macOS
        // had finished delivering focus — keys ended up routed to the
        // previously-frontmost window. After a confirmed match we add
        // a small extra grace so the window's input pipeline is ready.
        let pollInterval: TimeInterval = 0.025  // 25ms
        let deadline = Date().addingTimeInterval(settleDelay)
        var landed = false
        while Date() < deadline {
            let front = await MainActor.run { NSWorkspace.shared.frontmostApplication?.processIdentifier }
            if front == pid {
                landed = true
                break
            }
            try? await Task.sleep(nanoseconds: UInt64(pollInterval * 1_000_000_000))
        }
        if landed {
            NSLog("[RORORO] focuser: pid=\(pid) is frontmost — proceeding")
            // Post-confirm grace. Roblox's input pipeline takes a beat
            // longer than just "I'm frontmost" to actually accept
            // CGEvents — 250ms covers the window-active-but-not-yet-
            // listening gap. The user's first-iteration "didn't have
            // time to jump" came from this gap being too short.
            try? await Task.sleep(nanoseconds: 250_000_000)
        } else {
            NSLog("[RORORO] focuser: pid=\(pid) did NOT become frontmost within \(settleDelay)s; proceeding anyway")
        }
    }
}
