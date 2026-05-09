// CyclerToolbarView.swift
// Toolbar item + paused banner for the auto-keys cycler (Slope C wave
// 3b, ADR 0004 Decisions 6 + 9). The toolbar surfaces:
//   - Play / Pause / Resume button (driven by AutoKeysCyclerViewModel.state)
//   - Live cycle estimate, color-coded by CycleBudget.State
//   - Persistent "running" indicator while .running
// On Play, if safety isn't yet configured the toolbar opens the
// AutoKeysSafetySetupSheet first; only after that closes does the
// cycler kick off.

import SwiftUI

struct CyclerToolbarView: View {

    @State private var vm = AutoKeysCyclerViewModel.shared
    @State private var showSafetySetup: Bool = false
    @State private var showAlert: Bool = false

    private let settings = LaunchSettingsStore.shared

    var body: some View {
        HStack(spacing: Theme.Spacing.sm) {
            // Three states for the status label:
            //   1. Running + currently focusing a target → "NOW X → NEXT Y · 0:23"
            //   2. Running but between iterations (in loopDelay) → "NEXT in 0:27 → NEXT account: Y"
            //   3. Stopped → cycle estimate
            if isRunning, let now = vm.currentTargetLabel {
                runStatusLabel(now: now, next: vm.nextTargetLabel)
            } else if isRunning, let next = vm.nextIterationAt {
                countdownLabel(nextAt: next, nextLabel: vm.nextTargetLabel)
            } else {
                estimateLabel
            }
            splitButton
        }
        .sheet(isPresented: $showSafetySetup) {
            AutoKeysSafetySetupSheet(isPresented: $showSafetySetup)
        }
        .onAppear {
            vm.refreshEstimate()
        }
        .alert(
            "Auto-keys",
            isPresented: Binding(
                get: { vm.preflightMessage != nil || vm.lastError != nil },
                set: { newValue in
                    // SwiftUI calls the setter with `false` when the
                    // alert dismisses. Clear the underlying state so
                    // the next state evaluation doesn't re-present.
                    if !newValue { vm.clearMessages() }
                }
            )
        ) {
            // Show "Open Settings" only when the message points at a TCC
            // bucket; otherwise just OK is enough.
            if let msg = vm.preflightMessage, msg.contains("Accessibility") {
                Button("Open Settings") {
                    AutoKeysPermissions.openAccessibilitySettings()
                    vm.clearMessages()
                }
            }
            if let msg = vm.preflightMessage, msg.contains("Input Monitoring") {
                Button("Open Settings") {
                    AutoKeysPermissions.openInputMonitoringSettings()
                    vm.clearMessages()
                }
            }
            Button("OK") { vm.clearMessages() }
        } message: {
            Text(vm.preflightMessage ?? vm.lastError ?? "")
        }
    }

    // MARK: - Subviews

    /// Auto-keys split-button: tapping the icon runs the state-dependent
    /// primary action (Play / Pause / Resume / Stop); tapping the
    /// chevron opens a menu with config affordances (Safety setup,
    /// Cycle pace) that are otherwise unreachable once the cycler is
    /// running. Pattern lifted from `AccountsListView.splitLaunchButton`.
    private var splitButton: some View {
        Menu {
            Button {
                showSafetySetup = true
            } label: {
                Label("Hotkey setup…", systemImage: "keyboard.badge.ellipsis")
            }
            // Mode toggle — non-destructive. Custom per-account macros
            // stay on disk; stay-awake mode just synthesizes spacebar
            // sequences at runtime. Toggling off restores everyone.
            Button {
                settings.setAutoKeysStayAwakeMode(!settings.autoKeysStayAwakeMode)
                vm.refreshEstimate()
            } label: {
                Label(
                    settings.autoKeysStayAwakeMode
                        ? "✓ Stay-awake mode (spacebar / 30s)"
                        : "Stay-awake mode (spacebar / 30s)",
                    systemImage: "moon.zzz"
                )
            }
            // Active-macro pace — only relevant when stay-awake is OFF.
            // Hidden when stay-awake mode is on, since the cycler uses
            // its own fixed 30s pace then.
            if !settings.autoKeysStayAwakeMode {
                Menu("Cycle pace") {
                    let current = settings.autoKeysLoopDelay
                    Button(current == 0 ? "✓ Immediate" : "Immediate") {
                        settings.setAutoKeysLoopDelay(0)
                    }
                    Button(current == 30 ? "✓ 30 s between cycles" : "30 s between cycles") {
                        settings.setAutoKeysLoopDelay(30)
                    }
                }
            }
        } label: {
            Label(buttonLabel, systemImage: buttonIcon)
                .foregroundStyle(buttonColor)
        } primaryAction: {
            primaryAction()
        }
        .menuStyle(.borderlessButton)
        .help(buttonHelp)
    }

    /// "Now: X · Next: Y · 0:23" — surfaces what the cycler is currently
    /// firing at + what's coming + a live elapsed timer. TimelineView
    /// re-renders every second so the timer ticks without a manual
    /// Combine pipeline. Reads `vm.runStartTime` for the start moment.
    private func runStatusLabel(now: String, next: String?) -> some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            HStack(spacing: 4) {
                Circle()
                    .fill(Theme.Color.stateOk)
                    .frame(width: 6, height: 6)
                Text(statusText(now: now, next: next, at: context.date))
                    .font(Theme.Font.monoMicro)
                    .foregroundStyle(Theme.Color.fg2)
                    .tracking(0.4)
            }
        }
    }

    /// "NEXT IN 0:27 → Y" — between iterations, count down to the next
    /// fire moment and which account leads it. Same TimelineView trick
    /// as the run status — re-renders every second.
    private func countdownLabel(nextAt: Date, nextLabel: String?) -> some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            HStack(spacing: 4) {
                Circle()
                    .fill(Theme.Color.stateInfo)
                    .frame(width: 6, height: 6)
                Text(countdownText(nextAt: nextAt, nextLabel: nextLabel, now: context.date))
                    .font(Theme.Font.monoMicro)
                    .foregroundStyle(Theme.Color.fg2)
                    .tracking(0.4)
            }
        }
    }

    private func countdownText(nextAt: Date, nextLabel: String?, now: Date) -> String {
        let remaining = max(0, Int(nextAt.timeIntervalSince(now)))
        let elapsed = vm.runStartTime.map { Int(now.timeIntervalSince($0)) } ?? 0
        let countdown = formatMinutesSeconds(remaining)
        let runTime = formatMinutesSeconds(elapsed)
        // Both timers labeled clearly so the user can tell countdown
        // from elapsed at a glance — earlier feedback was that the two
        // ticking values were being confused for one number going up.
        if let nextLabel {
            return "NEXT FIRE IN \(countdown) → \(nextLabel) · RAN \(runTime)"
        }
        return "NEXT FIRE IN \(countdown) · RAN \(runTime)"
    }

    private func statusText(now: String, next: String?, at date: Date) -> String {
        let elapsed = vm.runStartTime.map { Int(date.timeIntervalSince($0)) } ?? 0
        let timer = formatMinutesSeconds(elapsed)
        if let next, next != now {
            return "NOW \(now) → \(next) · RAN \(timer)"
        }
        return "NOW \(now) · RAN \(timer)"
    }

    private func formatMinutesSeconds(_ totalSeconds: Int) -> String {
        let mins = totalSeconds / 60
        let secs = totalSeconds % 60
        return String(format: "%d:%02d", mins, secs)
    }

    private var estimateLabel: some View {
        HStack(spacing: 4) {
            if isRunning {
                Circle()
                    .fill(Theme.Color.stateOk)
                    .frame(width: 6, height: 6)
            }
            Text(estimateText)
                .font(Theme.Font.monoMicro)
                .foregroundStyle(estimateColor)
                .tracking(0.4)
        }
    }

    // MARK: - State helpers

    private var isRunning: Bool {
        if case .running = vm.state { return true }
        return false
    }

    private var isPaused: Bool {
        if case .paused = vm.state { return true }
        return false
    }

    private var buttonLabel: String {
        switch vm.state {
        case .stopped:                    return "Auto-keys"
        case .running, .paused:           return "Stop"
        }
    }

    private var buttonIcon: String {
        switch vm.state {
        case .stopped:                    return "play.circle"
        // Stop icon for any active state — running, engagement-paused,
        // user-requested-paused. Toolbar is the kill switch.
        case .running, .paused:           return "stop.circle.fill"
        }
    }

    private var buttonColor: Color {
        switch vm.state {
        case .stopped:                    return Theme.Color.fg2
        // Red while running, amber when engagement-paused (so the user
        // sees a state hint), red when user-paused. All three present
        // the SAME tap action (stop), but the color carries the
        // running-vs-paused distinction.
        case .running:                    return Theme.Color.stateDanger
        case .paused(.userEngaged, _):    return Theme.Color.stateWarn
        case .paused(.userRequested, _):  return Theme.Color.stateDanger
        }
    }

    private var buttonHelp: String {
        switch vm.state {
        case .stopped:
            return "Start auto-keys. Builds a snapshot from running accounts that have a sequence configured. Or use your kill-key gesture to start from any app."
        case .running:
            return "Stop auto-keys entirely. To pause without stopping, use your kill-key gesture instead."
        case .paused(.userEngaged, _):
            return "Cycler paused — mouse/keyboard activity detected. Will auto-resume in a moment. Click to STOP. To resume immediately, use your kill-key gesture."
        case .paused(.userRequested, _):
            return "Cycler is paused. Click to STOP. To resume, use your kill-key gesture."
        }
    }

    private var estimateText: String {
        if vm.lastEstimate <= 0 {
            return "—"
        }
        let mins = Int(vm.lastEstimate) / 60
        let secs = Int(vm.lastEstimate) % 60
        if mins > 0 {
            return "\(mins)m \(secs)s"
        }
        return "\(secs)s"
    }

    private var estimateColor: Color {
        switch CycleBudget.state(for: vm.lastEstimate) {
        case .ok:      return Theme.Color.fg3
        case .warn:    return Theme.Color.stateWarn
        case .overCap: return Theme.Color.stateDanger
        }
    }

    // MARK: - Actions

    private func primaryAction() {
        switch vm.state {
        case .stopped:
            // Gate behind safety setup the first time.
            if !hasConfiguredSafety() {
                showSafetySetup = true
                return
            }
            Task { await vm.play() }
        case .running, .paused:
            // Toolbar tap = HARD STOP from ANY active state. The kill-
            // key gesture is the pause/resume path. This invariant
            // matters during engagement pauses: the user has at most
            // 1.5s to reach the toolbar; if the button were "resume"
            // during pause, they'd never be able to actually STOP
            // (every click would just resume → run → engagement pause
            // → repeat). One tap always progresses toward stopped.
            Task { await vm.stop() }
        }
    }

    /// Heuristic: if the safety config still equals the bare default
    /// (F19 + hold-1s + 5s) AND the user has never touched it, prompt
    /// the setup. Once they save anything (even re-saving the defaults),
    /// `setAutoKeysSafety` writes to UserDefaults and the key exists,
    /// which we read indirectly by checking… actually simpler: write
    /// a marker on first save.
    private func hasConfiguredSafety() -> Bool {
        // Treat any explicit save as having configured. Default config
        // is what we ship; users opt-in via the setup sheet.
        UserDefaults.standard.bool(forKey: "rororo.autoKeys.safety.configured")
    }
}
