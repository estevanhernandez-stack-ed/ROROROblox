// AutoKeysRecorderSheet.swift
// Modal recorder for one account's auto-keys sequence (Slope C wave 3b,
// ADR 0004 Decision 5). Step-by-step capture: press a key, set the
// delay-after, optionally add 1-2 more steps, save. Up to 3 steps.
//
// Live cycle preview (`CycleBudget.estimate`) shows whether saving the
// current sequence keeps the global cycle under the warn / hard cap.
// Save disables when over cap.

import AppKit
import CoreGraphics
import SwiftUI

struct AutoKeysRecorderSheet: View {

    @Binding var isPresented: Bool
    let account: Account

    private let store = AccountStore.shared
    private let settings = LaunchSettingsStore.shared

    @State private var draftSteps: [AutoKeysStep] = []
    @State private var capturingKey: Bool = false
    @State private var pendingKeyCode: CGKeyCode? = nil
    @State private var pendingDelaySeconds: Double = 2.0
    @State private var pendingDelayUnit: DelayUnit = .seconds
    /// Pending repeat count for the in-flight step. Default 1; user
    /// bumps via stepper for spammable keybinds. Capped at
    /// `AutoKeysStep.maxRepeatCount` (20).
    @State private var pendingRepeatCount: Int = 1
    /// Mirror of the global `LaunchSettingsStore.autoKeysLoopDelay`,
    /// surfaced here so the user sees + sets it alongside the per-step
    /// delays. It IS global (one cycle has one inter-iteration pause),
    /// but the user's mental model puts it next to step delays —
    /// last-writer-wins across recorders.
    @State private var loopDelayValue: Double = 0
    @State private var loopDelayUnit: DelayUnit = .seconds

    private enum DelayUnit: String, CaseIterable, Identifiable {
        case seconds, minutes
        var id: String { rawValue }
        var label: String { self == .seconds ? "sec" : "min" }
        var multiplier: Double { self == .seconds ? 1 : 60 }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.lg) {
            header

            divider

            stepsList

            divider

            captureSection

            divider

            loopDelaySection

            Spacer(minLength: 0)

            cyclePreview

            footer
        }
        .padding(Theme.Spacing.lg)
        .frame(width: 520, height: 600)
        .background(Theme.Color.bgPage)
        .onAppear {
            if let existing = account.autoKeys {
                draftSteps = existing.steps
            }
            // Hydrate the loop-delay control from the global setting.
            // Show in minutes if the value is ≥ 60s, else seconds.
            let current = settings.autoKeysLoopDelay
            if current >= 60 {
                loopDelayValue = current / 60
                loopDelayUnit = .minutes
            } else {
                loopDelayValue = current
                loopDelayUnit = .seconds
            }
        }
    }

    // MARK: - Sections

    private var header: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
            Text("Auto-keys for \(account.displayName)")
                .font(Theme.Font.heading2)
                .foregroundStyle(Theme.Color.fg1)
            Text("Add as many keys as you want. Each fires in order with the delay you set before the cycler moves on. Cycle-budget gauge below is the real ceiling.")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
        }
    }

    private var stepsList: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            Text("Sequence (\(draftSteps.count) step\(draftSteps.count == 1 ? "" : "s"))")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
                .textCase(.uppercase)
                .tracking(0.7)

            if draftSteps.isEmpty {
                Text("No steps yet. Press the capture button below to record your first key.")
                    .font(Theme.Font.bodySmall)
                    .foregroundStyle(Theme.Color.fg3)
            } else {
                ForEach(Array(draftSteps.enumerated()), id: \.offset) { idx, step in
                    HStack(spacing: Theme.Spacing.md) {
                        Text("\(idx + 1).")
                            .font(Theme.Font.mono)
                            .foregroundStyle(Theme.Color.fg3)
                            .frame(width: 22, alignment: .leading)
                        Text(stepKeyLabel(step))
                            .font(Theme.Font.mono)
                            .foregroundStyle(Theme.Color.fg1)
                            .padding(.horizontal, Theme.Spacing.md)
                            .padding(.vertical, 4)
                            .background(Theme.Color.bgRaised)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.sm))
                        Text("→ then wait \(formatDelay(step.delayAfter))")
                            .font(Theme.Font.bodySmall)
                            .foregroundStyle(Theme.Color.fg2)
                        Spacer()
                        Button("✕") {
                            draftSteps.remove(at: idx)
                        }
                        .buttonStyle(.plain)
                        .foregroundStyle(Theme.Color.fg3)
                    }
                }
            }
        }
    }

    private var captureSection: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            if let captured = pendingKeyCode {
                HStack(spacing: Theme.Spacing.md) {
                    Text("Captured:")
                        .font(Theme.Font.bodySmall)
                        .foregroundStyle(Theme.Color.fg2)
                    Text(prettyKeyName(captured))
                        .font(Theme.Font.mono)
                        .foregroundStyle(Theme.Color.fg1)
                        .padding(.horizontal, Theme.Spacing.md)
                        .padding(.vertical, 4)
                        .background(Theme.Color.bgRaised)
                        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.sm))
                    Spacer()
                    Button("Re-capture") {
                        pendingKeyCode = nil
                        capturingKey = true
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(Theme.Color.fg3)
                }

                HStack(spacing: Theme.Spacing.md) {
                    Text("Repeat:")
                        .font(Theme.Font.bodySmall)
                        .foregroundStyle(Theme.Color.fg2)
                    Stepper(value: $pendingRepeatCount, in: 1...AutoKeysStep.maxRepeatCount) {
                        Text(pendingRepeatCount == 1 ? "1×" : "\(pendingRepeatCount)×")
                            .font(Theme.Font.mono)
                            .foregroundStyle(Theme.Color.fg1)
                            .frame(minWidth: 36, alignment: .leading)
                    }
                    Text(pendingRepeatCount > 1
                         ? "(0.7s between presses)"
                         : "")
                        .font(Theme.Font.monoMicro)
                        .foregroundStyle(Theme.Color.fg3)
                    Spacer()
                }

                HStack(spacing: Theme.Spacing.md) {
                    Text("Delay after:")
                        .font(Theme.Font.bodySmall)
                        .foregroundStyle(Theme.Color.fg2)
                    TextField("Delay", value: $pendingDelaySeconds, format: .number)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 80)
                    Picker("", selection: $pendingDelayUnit) {
                        ForEach(DelayUnit.allCases) { unit in
                            Text(unit.label).tag(unit)
                        }
                    }
                    .pickerStyle(.segmented)
                    .frame(width: 110)
                    Spacer()
                    Button("Add step") {
                        commitStep()
                    }
                    .keyboardShortcut(.defaultAction)
                }
            } else {
                Button(capturingKey ? "Press a key…" : "Capture next key") {
                    pendingKeyCode = nil
                    capturingKey = true
                }
                .buttonStyle(.borderedProminent)
                .background(KeyCaptureRepresentable(capturing: $capturingKey) { code, _ in
                    // Per-account sequence stores bare keyCode only —
                    // modifiers are a kill-key concern (ADR 0004
                    // Decision 9). If a user holds Shift while pressing
                    // P, we record keyCode 35 ("P") and let the engine
                    // fire it without modifiers when cycling.
                    pendingKeyCode = code
                    capturingKey = false
                })
            }
        }
    }

    /// Per-recording surface for the global cycle's "wait this long after
    /// the last step before starting the next iteration" delay. The
    /// underlying value is global (one cycle = one inter-iteration
    /// pause) — last writer wins across recorders. Shown here next to
    /// the per-step delays because that's where it lives in the user's
    /// mental model: "step 1 wait, step 2 wait, …, then loop wait."
    private var loopDelaySection: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
            Text("Repeat cycle after last step")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
                .textCase(.uppercase)
                .tracking(0.7)

            HStack(spacing: Theme.Spacing.md) {
                TextField("Delay", value: $loopDelayValue, format: .number)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 80)
                Picker("", selection: $loopDelayUnit) {
                    ForEach(DelayUnit.allCases) { unit in
                        Text(unit.label).tag(unit)
                    }
                }
                .pickerStyle(.segmented)
                .frame(width: 110)
                Spacer()
            }

            Text("After the cycler walks every account's sequence, it waits this long before returning to the first account. Set to 0 for back-to-back loops.")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg3)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var cyclePreview: some View {
        let estimate = previewEstimate
        let state = CycleBudget.state(for: estimate)
        return HStack(spacing: Theme.Spacing.sm) {
            Circle().fill(stateColor(state)).frame(width: 8, height: 8)
            Text("Estimated cycle: \(formatSeconds(estimate))")
                .font(Theme.Font.bodySmall)
                .foregroundStyle(Theme.Color.fg2)
            Spacer()
            if state == .overCap {
                Text("Over hard cap (\(formatSeconds(CycleBudget.hardCap)))")
                    .font(Theme.Font.bodySmall)
                    .foregroundStyle(Theme.Color.stateDanger)
            } else if state == .warn {
                Text("Approaching cap")
                    .font(Theme.Font.bodySmall)
                    .foregroundStyle(Theme.Color.stateWarn)
            }
        }
    }

    private var footer: some View {
        VStack(spacing: Theme.Spacing.sm) {
            // Per-recording affordance: copy this account's draft to
            // every other saved account. Stays here because it's about
            // the current draft. The stay-awake preset (which is a
            // GLOBAL operation that overwrites every account) lives in
            // Safety Setup, not here.
            if AccountStore.shared.accounts.count > 1 {
                HStack(spacing: Theme.Spacing.sm) {
                    Button {
                        applyToAllAccounts()
                        isPresented = false
                    } label: {
                        Label("Save + apply to all accounts", systemImage: "square.on.square")
                    }
                    .disabled(saveDisabled || draftSteps.isEmpty)
                    .help("Save the current sequence and copy it to every other saved account. Useful when all your accounts share the same Roblox keybinds.")

                    Spacer()
                }
            }

            HStack {
                Button("Cancel") { isPresented = false }
                if !draftSteps.isEmpty {
                    Button("Clear sequence", role: .destructive) {
                        draftSteps = []
                    }
                }
                Spacer()
                Button("Save") {
                    save()
                    isPresented = false
                }
                .keyboardShortcut(.defaultAction)
                .disabled(saveDisabled)
            }
        }
    }

    private var divider: some View {
        Rectangle()
            .fill(Theme.Color.bgRaised)
            .frame(height: 1)
    }

    // MARK: - Helpers

    private var saveDisabled: Bool {
        // Save only enabled when the draft is valid AND total cycle is
        // under the cap. Empty sequence is allowed (clears the field).
        if let _ = AutoKeysSequence(steps: draftSteps) {
            return previewEstimate >= CycleBudget.hardCap
        }
        return true
    }

    /// Build a snapshot for the preview: every other configured account
    /// PLUS the in-flight draft for this account. Uses the in-flight
    /// loop-delay value (not the persisted one) so the preview reflects
    /// what the user is editing right now.
    private var previewEstimate: TimeInterval {
        let others = AccountStore.shared.accounts
            .filter { $0.userId != account.userId }
            .compactMap { $0.autoKeys }
        let draft = AutoKeysSequence(steps: draftSteps) ?? AutoKeysSequence(steps: [])!
        let inFlightLoopDelay = loopDelayValue * loopDelayUnit.multiplier
        return CycleBudget.estimate(
            snapshot: others + [draft],
            loopDelay: inFlightLoopDelay
        )
    }

    private func commitStep() {
        guard let code = pendingKeyCode else { return }
        let seconds = pendingDelaySeconds * pendingDelayUnit.multiplier
        draftSteps.append(AutoKeysStep(
            keyCode: code,
            delayAfter: seconds,
            repeatCount: pendingRepeatCount
        ))
        pendingKeyCode = nil
        pendingDelaySeconds = 2
        pendingDelayUnit = .seconds
        pendingRepeatCount = 1
    }

    private func save() {
        // Auto-commit a pending capture. If the user filled out the
        // delay field but didn't click "Add step" before hitting Save,
        // their intent was clearly to keep that step — committing it
        // before save matches what they meant.
        if pendingKeyCode != nil {
            commitStep()
        }
        let sequence: AutoKeysSequence?
        if draftSteps.isEmpty {
            sequence = nil
        } else {
            sequence = AutoKeysSequence(steps: draftSteps)
        }
        store.setAutoKeys(userId: account.userId, sequence: sequence)
        // Persist the in-flight loop-delay too. Global setting, so
        // last-writer-wins across recorders.
        let loopDelaySeconds = loopDelayValue * loopDelayUnit.multiplier
        settings.setAutoKeysLoopDelay(loopDelaySeconds)
    }

    /// Save the draft to this account, then copy the same sequence to
    /// every other saved account. Use when all accounts share the same
    /// Roblox keybinds (the user's stated common case).
    private func applyToAllAccounts() {
        save()
        let sequence = AutoKeysSequence(steps: draftSteps)
        for other in store.accounts where other.userId != account.userId {
            store.setAutoKeys(userId: other.userId, sequence: sequence)
        }
    }

    private func stateColor(_ s: CycleBudget.State) -> Color {
        switch s {
        case .ok:      return Theme.Color.stateOk
        case .warn:    return Theme.Color.stateWarn
        case .overCap: return Theme.Color.stateDanger
        }
    }

    private func stepKeyLabel(_ step: AutoKeysStep) -> String {
        let name = prettyKeyName(step.keyCode)
        return step.repeatCount > 1 ? "\(name) ×\(step.repeatCount)" : name
    }

    private func formatDelay(_ seconds: TimeInterval) -> String {
        if seconds >= 60 {
            let mins = seconds / 60
            return String(format: "%.1fm", mins)
        }
        return String(format: "%.1fs", seconds)
    }

    private func formatSeconds(_ seconds: TimeInterval) -> String {
        let mins = Int(seconds) / 60
        let secs = Int(seconds) % 60
        if mins > 0 {
            return "\(mins)m \(secs)s"
        }
        return "\(secs)s"
    }
}
