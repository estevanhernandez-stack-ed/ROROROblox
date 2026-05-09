// AutoKeysSafetySetupSheet.swift
// One-time global setup for the cycler's safety controls (Slope C
// wave 3b, ADR 0004 Decision 9). The user picks a kill key + the
// gesture (hold-1s OR double-tap), and we surface the dual-TCC
// posture (Accessibility + Input Monitoring) so the prompts aren't
// surprising at Play time. Re-runnable from Settings to change either.

import AppKit
import CoreGraphics
import SwiftUI

struct AutoKeysSafetySetupSheet: View {

    @Binding var isPresented: Bool

    @State private var capturedKeyCode: CGKeyCode = AutoKeysSafetyConfig.defaultKillKeyCode
    @State private var capturedModifiers: UInt = 0
    @State private var capturing: Bool = false
    @State private var gesture: KillGesture = .defaultHold
    @State private var resumeGrace: TimeInterval = 5

    @State private var accessibility: AutoKeysPermissions.Status = .notDetermined
    @State private var inputMonitoring: AutoKeysPermissions.Status = .notDetermined

    private let settings = LaunchSettingsStore.shared

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.lg) {
            header

            divider

            killKeySection

            divider

            gestureSection

            divider

            permissionsSection

            Spacer(minLength: 0)

            footer
        }
        .padding(Theme.Spacing.lg)
        .frame(width: 520, height: 620)
        .background(Theme.Color.bgPage)
        .onAppear {
            // Hydrate from existing config on re-entry.
            let existing = settings.autoKeysSafety
            capturedKeyCode = existing.killKey.keyCode
            capturedModifiers = existing.killKey.modifiers
            gesture = existing.gesture
            resumeGrace = existing.resumeGrace
            refreshPermissions()
        }
    }

    // MARK: - Sections

    private var header: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text("Hotkey setup")
                .font(Theme.Font.heading2)
                .foregroundStyle(Theme.Color.fg1)
            Text("Pick the global hotkey that starts the cycler when stopped, stops it when running, and pauses it when you grab the mouse. Required before Play.")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
        }
    }

    private var killKeySection: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            Text("Kill key")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
                .textCase(.uppercase)
                .tracking(0.7)

            HStack(spacing: Theme.Spacing.md) {
                Text(prettyKeyCombo(keyCode: capturedKeyCode, modifiers: capturedModifiers))
                    .font(Theme.Font.mono)
                    .foregroundStyle(Theme.Color.fg1)
                    .padding(.horizontal, Theme.Spacing.md)
                    .padding(.vertical, Theme.Spacing.sm)
                    .background(Theme.Color.bgRaised)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.sm))

                Button(capturing ? "Press a key…" : "Change") {
                    capturing.toggle()
                }
                .keyboardShortcut(.defaultAction)
                .background(KeyCaptureRepresentable(capturing: $capturing) { code, mods in
                    capturedKeyCode = code
                    capturedModifiers = mods
                    capturing = false
                })
            }

            Text("Pick a key NOT bound in your Roblox keybinds (function keys F13–F19 are safest). Hold Shift / Control / Option / Command while you press to record a combo (e.g. ⇧F19). Pressing the combo twice / holding it stops the cycler from anywhere on your Mac.")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg3)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var gestureSection: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            Text("Start / stop macro")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
                .textCase(.uppercase)
                .tracking(0.7)

            HStack(spacing: Theme.Spacing.sm) {
                gestureChip(
                    label: "Hold for 1s",
                    selected: isHold,
                    onTap: { gesture = .holdFor(seconds: 1.0) }
                )
                gestureChip(
                    label: "Double-tap",
                    selected: !isHold,
                    onTap: { gesture = .doubleTap(withinSeconds: 0.6) }
                )
            }

            Text(isHold
                 ? "Press and hold for one full second. Lower false-positive risk."
                 : "Two presses inside 600ms. Faster on muscle memory.")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg3)
        }
    }

    private var permissionsSection: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            Text("Permissions needed")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
                .textCase(.uppercase)
                .tracking(0.7)

            permissionRow(
                title: "Accessibility",
                detail: "Lets RORORO post keystrokes into your Roblox windows.",
                status: accessibility,
                action: AutoKeysPermissions.openAccessibilitySettings
            )
            permissionRow(
                title: "Input Monitoring",
                detail: "Lets RORORO see your kill-key presses + pause when you move the mouse.",
                status: inputMonitoring,
                action: {
                    AutoKeysPermissions.requestInputMonitoring()
                    AutoKeysPermissions.openInputMonitoringSettings()
                }
            )
        }
    }

    private var footer: some View {
        HStack {
            Button("Cancel") { isPresented = false }
            Spacer()
            Button("Save") {
                save()
                isPresented = false
            }
            .keyboardShortcut(.defaultAction)
        }
    }

    private var divider: some View {
        Rectangle()
            .fill(Theme.Color.bgRaised)
            .frame(height: 1)
    }

    // MARK: - Helpers

    private var isHold: Bool {
        if case .holdFor = gesture { return true }
        return false
    }

    private func gestureChip(label: String, selected: Bool, onTap: @escaping () -> Void) -> some View {
        Button(action: onTap) {
            Text(label)
                .font(Theme.Font.bodySmall)
                .foregroundStyle(selected ? Color.white : Theme.Color.fg2)
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .background(
                    RoundedRectangle(cornerRadius: Theme.Radius.pill)
                        .fill(selected ? Theme.Color.brandCyan : Theme.Color.bgRaised)
                )
        }
        .buttonStyle(.plain)
    }

    private func permissionRow(
        title: String,
        detail: String,
        status: AutoKeysPermissions.Status,
        action: @escaping () -> Void
    ) -> some View {
        HStack(alignment: .top, spacing: Theme.Spacing.md) {
            Circle()
                .fill(status == .granted ? Theme.Color.stateOk : Theme.Color.stateWarn)
                .frame(width: 8, height: 8)
                .padding(.top, 6)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(Theme.Font.body).foregroundStyle(Theme.Color.fg1)
                Text(detail)
                    .font(Theme.Font.bodySmall)
                    .foregroundStyle(Theme.Color.fg3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            if status != .granted {
                Button("Open") { action() }
                    .font(Theme.Font.bodySmall)
            } else {
                Text("Granted")
                    .font(Theme.Font.monoMicro)
                    .foregroundStyle(Theme.Color.stateOk)
            }
        }
    }

    private func refreshPermissions() {
        accessibility = AutoKeysPermissions.accessibilityStatus()
        inputMonitoring = AutoKeysPermissions.inputMonitoringStatus()
    }

    private func save() {
        settings.setAutoKeysSafety(
            AutoKeysSafetyConfig(
                killKey: KillKeyCombo(
                    keyCode: capturedKeyCode,
                    modifiers: capturedModifiers
                ),
                gesture: gesture,
                resumeGrace: resumeGrace
            )
        )
        // Flip the "user has explicitly configured safety" flag so the
        // toolbar's Play button stops gating on the setup sheet from
        // here on. Re-runnable from Settings to change either field.
        UserDefaults.standard.set(true, forKey: "rororo.autoKeys.safety.configured")
        // Push the new config to the running safety monitor so the
        // kill key change takes effect immediately (no app restart, no
        // cycler restart).
        AutoKeysCyclerViewModel.shared.refreshSafetyConfig()
        // If Input Monitoring was just granted in the permissions
        // section above, boot the monitor now so the kill-key-as-toggle
        // path is live without waiting for a Play click.
        AutoKeysCyclerViewModel.shared.bootSafetyIfPermitted()
    }
}

/// AppKit shim for one-shot keyDown capture while the sheet is frontmost.
/// SwiftUI's `.onKeyPress` is iOS 17+/macOS 14+ but doesn't capture
/// modifier-free function-key presses cleanly — NSEvent local monitor
/// is the load-bearing path. The handler receives both the keyCode and
/// the modifier bitmask masked to the relevant Shift/Control/Option/
/// Command bits, so callers can record composite kill-key gestures
/// like Shift+F19.
struct KeyCaptureRepresentable: NSViewRepresentable {

    @Binding var capturing: Bool
    let onKey: (CGKeyCode, UInt) -> Void

    func makeNSView(context: Context) -> NSView {
        let view = NSView(frame: .zero)
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        if capturing && context.coordinator.monitor == nil {
            context.coordinator.install { code, mods in
                onKey(code, mods)
            }
        } else if !capturing && context.coordinator.monitor != nil {
            context.coordinator.uninstall()
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    static func dismantleNSView(_ nsView: NSView, coordinator: Coordinator) {
        coordinator.uninstall()
    }

    final class Coordinator {
        var monitor: Any?
        func install(handler: @escaping (CGKeyCode, UInt) -> Void) {
            uninstall()
            monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
                let mods = event.modifierFlags.rawValue & KillKeyCombo.relevantModifierMask
                handler(CGKeyCode(event.keyCode), mods)
                return nil // swallow — sheet's text fields shouldn't see it
            }
        }
        func uninstall() {
            if let m = monitor { NSEvent.removeMonitor(m); monitor = nil }
        }
    }
}

/// Pretty-print a key + modifiers combo — "⇧F19", "⌃⌥P", or just the
/// bare key name when no modifiers are held. Modifier order follows
/// Apple's convention (⌃⌥⇧⌘). Falls back through `prettyKeyName` for
/// the key portion.
func prettyKeyCombo(keyCode: CGKeyCode, modifiers: UInt) -> String {
    var prefix = ""
    if (modifiers & (1 << 18)) != 0 { prefix += "⌃" }  // Control
    if (modifiers & (1 << 19)) != 0 { prefix += "⌥" }  // Option
    if (modifiers & (1 << 17)) != 0 { prefix += "⇧" }  // Shift
    if (modifiers & (1 << 20)) != 0 { prefix += "⌘" }  // Command
    return prefix + prettyKeyName(keyCode)
}

/// Map a CGKeyCode → human-readable name for the recorder UI. macOS
/// virtual keyCodes are physical-position constants — they don't shift
/// with the active keyboard layout, so this lookup is layout-independent
/// (a Dvorak user pressing the physical "Q" position still gets keyCode
/// 12). Falls back to "key #N" for keys outside this table (numpad
/// arithmetic, IME-only keys, etc.).
func prettyKeyName(_ code: CGKeyCode) -> String {
    switch code {
    // Letters (US-QWERTY physical layout).
    case 0:   return "A"
    case 1:   return "S"
    case 2:   return "D"
    case 3:   return "F"
    case 4:   return "H"
    case 5:   return "G"
    case 6:   return "Z"
    case 7:   return "X"
    case 8:   return "C"
    case 9:   return "V"
    case 11:  return "B"
    case 12:  return "Q"
    case 13:  return "W"
    case 14:  return "E"
    case 15:  return "R"
    case 16:  return "Y"
    case 17:  return "T"
    case 31:  return "O"
    case 32:  return "U"
    case 34:  return "I"
    case 35:  return "P"
    case 37:  return "L"
    case 38:  return "J"
    case 40:  return "K"
    case 45:  return "N"
    case 46:  return "M"
    // Number row.
    case 18:  return "1"
    case 19:  return "2"
    case 20:  return "3"
    case 21:  return "4"
    case 22:  return "6"
    case 23:  return "5"
    case 25:  return "9"
    case 26:  return "7"
    case 28:  return "8"
    case 29:  return "0"
    // Punctuation that survives most layouts.
    case 24:  return "="
    case 27:  return "-"
    case 30:  return "]"
    case 33:  return "["
    case 39:  return "'"
    case 41:  return ";"
    case 42:  return "\\"
    case 43:  return ","
    case 44:  return "/"
    case 47:  return "."
    case 50:  return "`"
    // Whitespace + control.
    case 36:  return "Return"
    case 48:  return "Tab"
    case 49:  return "Space"
    case 51:  return "Delete"
    case 53:  return "Escape"
    case 76:  return "Numpad Enter"
    case 117: return "Forward Delete"
    case 114: return "Help"
    case 115: return "Home"
    case 116: return "Page Up"
    case 119: return "End"
    case 121: return "Page Down"
    // Arrows.
    case 123: return "←"
    case 124: return "→"
    case 125: return "↓"
    case 126: return "↑"
    // Function row.
    case 122: return "F1"
    case 120: return "F2"
    case 99:  return "F3"
    case 118: return "F4"
    case 96:  return "F5"
    case 97:  return "F6"
    case 98:  return "F7"
    case 100: return "F8"
    case 101: return "F9"
    case 109: return "F10"
    case 103: return "F11"
    case 111: return "F12"
    case 105: return "F13"
    case 107: return "F14"
    case 113: return "F15"
    case 106: return "F16"
    case 64:  return "F17"
    case 79:  return "F18"
    case 80:  return "F19"
    case 90:  return "F20"
    // Numpad digits.
    case 82:  return "Numpad 0"
    case 83:  return "Numpad 1"
    case 84:  return "Numpad 2"
    case 85:  return "Numpad 3"
    case 86:  return "Numpad 4"
    case 87:  return "Numpad 5"
    case 88:  return "Numpad 6"
    case 89:  return "Numpad 7"
    case 91:  return "Numpad 8"
    case 92:  return "Numpad 9"
    default:  return "key #\(code)"
    }
}
