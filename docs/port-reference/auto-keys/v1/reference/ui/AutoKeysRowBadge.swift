// AutoKeysRowBadge.swift
// Per-account row entry into the recorder sheet (Slope C wave 3b).
// Tiny — a chevron-style chip that surfaces "auto-keys: not configured"
// vs "auto-keys: 2 keys, 6.0s" and opens `AutoKeysRecorderSheet` on tap.
// Renders inside `AccountsListView`'s row, alongside the FPS chip.

import SwiftUI

struct AutoKeysRowBadge: View {

    let account: Account
    let onTap: () -> Void

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 6) {
                Image(systemName: account.autoKeys?.isEmpty == false
                      ? "keyboard.fill"
                      : "keyboard")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundStyle(Color.white.opacity(0.9))
                Text(label)
                    .font(Theme.Font.monoMicro)
                    .foregroundStyle(Color.white.opacity(0.85))
                    .tracking(0.4)
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(helpText)
    }

    private var label: String {
        guard let seq = account.autoKeys, !seq.isEmpty else {
            return "AUTO-KEYS"
        }
        return "\(seq.steps.count) KEY\(seq.steps.count == 1 ? "" : "S") · \(formatSeconds(seq.totalDuration))"
    }

    private var helpText: String {
        guard let seq = account.autoKeys, !seq.isEmpty else {
            return "Auto-keys not configured for this account. Click to record a keystroke sequence the cycler will fire while you AFK."
        }
        return "Auto-keys: \(seq.steps.count) key\(seq.steps.count == 1 ? "" : "s"), totalling \(formatSeconds(seq.totalDuration)). Click to edit or clear."
    }

    private func formatSeconds(_ seconds: TimeInterval) -> String {
        if seconds >= 60 {
            return String(format: "%.1fM", seconds / 60)
        }
        return String(format: "%.1fS", seconds)
    }
}
