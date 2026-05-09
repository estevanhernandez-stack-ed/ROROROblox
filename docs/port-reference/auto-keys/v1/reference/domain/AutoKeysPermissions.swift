// AutoKeysPermissions.swift
// Domain — TCC consent helpers for the auto-keys cycler (Slope C, ADR
// 0004 Decisions 8 + 9). Two privacy buckets matter:
//
//   - Accessibility (`AXIsProcessTrusted`) — needed to post `CGEvent`
//     keystrokes via the cycler's `KeyEventPoster`.
//   - Input Monitoring (`IOHIDCheckAccess(.listenEvent)`) — needed for
//     the safety monitor's global `NSEvent` listeners. Without it the
//     engagement detector + kill-key recognizer silently no-op.
//
// macOS 14+ keeps these in separate panes of System Settings. The
// recorder + toolbar surface deep-links to each pane so the user
// can grant both without navigating Settings manually.

import AppKit
import ApplicationServices
import CoreGraphics
import Foundation
import IOKit.hid

public enum AutoKeysPermissions {

    public enum Status: Equatable, Sendable {
        case granted
        case denied
        case notDetermined
    }

    /// True iff the app is in System Settings → Privacy & Security →
    /// Accessibility AND has the toggle on. The bare check; no prompt.
    public static func accessibilityStatus() -> Status {
        AXIsProcessTrusted() ? .granted : .denied
    }

    /// Status of the global-event-listener bucket (Input Monitoring).
    /// `IOHIDCheckAccess` distinguishes denied vs not-yet-asked, so the
    /// recorder UI can show "request" vs "open settings to grant" with
    /// the right copy.
    public static func inputMonitoringStatus() -> Status {
        let raw = IOHIDCheckAccess(kIOHIDRequestTypeListenEvent)
        switch raw {
        case kIOHIDAccessTypeGranted:    return .granted
        case kIOHIDAccessTypeDenied:     return .denied
        case kIOHIDAccessTypeUnknown:    return .notDetermined
        default:                         return .notDetermined
        }
    }

    /// Trigger the system Input Monitoring prompt. macOS shows the
    /// native dialog the first time; subsequent calls are no-ops if
    /// already granted, and silently fail if the user denied.
    @discardableResult
    public static func requestInputMonitoring() -> Bool {
        IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
    }

    /// Request Accessibility — mirrors macRo's
    /// `Permissions.requestAccessibility()` pattern:
    ///
    ///   1. `AXIsProcessTrustedWithOptions` fires the native prompt
    ///      ("Allow RORORO to control your computer?") on first call.
    ///      No-op once granted/denied.
    ///   2. Open Settings directly to the Accessibility pane via the
    ///      `Privacy_Accessibility` anchor on
    ///      `com.apple.preference.security`. macRo's same URL works on
    ///      macOS 14+ — earlier "blank pane" + "lands on General"
    ///      reports we hit were sequencing artifacts (Settings already
    ///      open elsewhere). The combination of native prompt + URL
    ///      navigation is the resilient pattern.
    public static func openAccessibilitySettings() {
        let options: [String: Bool] = ["AXTrustedCheckOptionPrompt": true]
        _ = AXIsProcessTrustedWithOptions(options as CFDictionary)
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    /// Request Input Monitoring — same shape as the Accessibility flow.
    /// `IOHIDRequestAccess` fires the native prompt; the URL navigates
    /// Settings to the Input Monitoring pane via the `Privacy_ListenEvent`
    /// anchor.
    public static func openInputMonitoringSettings() {
        IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent") {
            NSWorkspace.shared.open(url)
        }
    }
}
