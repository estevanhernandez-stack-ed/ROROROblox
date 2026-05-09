// AutoKeysStatusPanel.swift
// Floating, always-on-top status panel for the auto-keys cycler
// (Slope C wave 3c). When the cycler is running or paused, this panel
// is visible above ALL apps (including frontmost Roblox windows) so
// the user can see what step is firing on which account, the run
// timer, the countdown to the next iteration — without Cmd-Tabbing
// back to RORORO.
//
// Implementation: a tiny SwiftUI view hosted in a borderless `NSPanel`.
// `.nonactivatingPanel` style so clicking it doesn't steal focus from
// the user's game. `.statusBar` window level so it floats above
// fullscreen Roblox windows. We toggle visibility by observing the
// cycler view-model's state.

import AppKit
import SwiftUI

@MainActor
final class AutoKeysStatusPanelController: NSObject, NSWindowDelegate {

    static let shared = AutoKeysStatusPanelController()

    private var panel: NSPanel?
    private var hostingView: NSHostingView<AutoKeysStatusPanelView>?
    private var visibilityTask: Task<Void, Never>?

    /// UserDefaults key for the panel's last drag position. Saved on
    /// every move so dragging once persists the spot for every future
    /// run.
    private static let frameOriginKey = "rororo.autoKeys.statusPanel.origin"

    private override init() { super.init() }

    /// Begin watching the cycler state. Show the panel when running or
    /// paused; hide when stopped. Idempotent.
    func start() {
        guard visibilityTask == nil else { return }
        visibilityTask = Task { @MainActor [weak self] in
            // Poll the view-model state on every observe-stream tick.
            // The view-model already mirrors cycler state to its own
            // @Observable property, so we just react to state changes.
            // Use a 0.5s interval — light enough not to be a battery
            // concern, fast enough that show/hide feels instant.
            while !Task.isCancelled {
                let state = AutoKeysCyclerViewModel.shared.state
                let shouldShow: Bool
                switch state {
                case .stopped: shouldShow = false
                case .running, .paused: shouldShow = true
                }
                if shouldShow {
                    self?.showPanel()
                } else {
                    self?.hidePanel()
                }
                try? await Task.sleep(nanoseconds: 500_000_000)
            }
        }
    }

    func stop() {
        visibilityTask?.cancel()
        visibilityTask = nil
        hidePanel()
    }

    private func showPanel() {
        if let panel, panel.isVisible { return }
        if panel == nil {
            buildPanel()
        }
        panel?.orderFrontRegardless()
    }

    private func hidePanel() {
        panel?.orderOut(nil)
    }

    private func buildPanel() {
        let view = AutoKeysStatusPanelView()
        let host = NSHostingView(rootView: view)
        host.frame = NSRect(x: 0, y: 0, width: 360, height: 60)

        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 360, height: 60),
            styleMask: [.borderless, .nonactivatingPanel, .utilityWindow],
            backing: .buffered,
            defer: false
        )
        panel.contentView = host
        panel.isFloatingPanel = true
        // Background-drag is enabled — the user grabs the rounded card
        // anywhere and drops it where they want it. We listen for the
        // window's didMove notification (via NSWindowDelegate) and
        // persist the new origin to UserDefaults so the position
        // survives panel hide/show + app restart.
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.level = .statusBar  // floats above fullscreen
        panel.collectionBehavior = [
            .canJoinAllSpaces,
            .stationary,
            .ignoresCycle,
            .fullScreenAuxiliary
        ]
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.delegate = self

        // Position: prefer the user's last drag location; fall back to
        // top-right corner of the main screen.
        if let saved = loadSavedOrigin() {
            panel.setFrameOrigin(saved)
        } else if let screen = NSScreen.main {
            let frame = screen.visibleFrame
            let x = frame.maxX - 380
            let y = frame.maxY - 80
            panel.setFrameOrigin(NSPoint(x: x, y: y))
        }

        self.panel = panel
        self.hostingView = host
    }

    // MARK: - Position persistence

    private func loadSavedOrigin() -> NSPoint? {
        let defaults = UserDefaults.standard
        guard defaults.object(forKey: Self.frameOriginKey) != nil else { return nil }
        let dict = defaults.dictionary(forKey: Self.frameOriginKey)
        guard let x = dict?["x"] as? CGFloat,
              let y = dict?["y"] as? CGFloat else { return nil }
        return NSPoint(x: x, y: y)
    }

    private func saveOrigin(_ point: NSPoint) {
        UserDefaults.standard.set(
            ["x": point.x, "y": point.y],
            forKey: Self.frameOriginKey
        )
    }

    nonisolated func windowDidMove(_ notification: Notification) {
        Task { @MainActor [weak self] in
            guard let self, let panel = self.panel else { return }
            self.saveOrigin(panel.frame.origin)
        }
    }
}

/// SwiftUI content of the floating status panel. Pulls live from the
/// cycler view-model — same data surface as the toolbar, but rendered
/// large and standalone so it's legible from anywhere on screen.
private struct AutoKeysStatusPanelView: View {

    @State private var vm = AutoKeysCyclerViewModel.shared

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            HStack(spacing: Theme.Spacing.md) {
                statusDot
                VStack(alignment: .leading, spacing: 2) {
                    Text(headline(at: context.date))
                        .font(Theme.Font.bodySmall)
                        .fontWeight(.semibold)
                        .foregroundStyle(Theme.Color.fg1)
                        .lineLimit(1)
                    Text(detail(at: context.date))
                        .font(Theme.Font.monoMicro)
                        .foregroundStyle(Theme.Color.fg2)
                        .tracking(0.4)
                        .lineLimit(1)
                }
                Spacer(minLength: 0)
                stopButton
            }
            .padding(.horizontal, Theme.Spacing.md)
            .padding(.vertical, Theme.Spacing.sm)
            .background(
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .fill(Theme.Color.bgPage.opacity(0.92))
            )
            .overlay(
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .strokeBorder(Theme.Color.bgRaised, lineWidth: 1)
            )
        }
    }

    /// Always-visible Stop button on the panel. Fully halts the cycler
    /// (releases the wake-lock, drops focus tracking). Distinct from
    /// the kill-key gesture which is pause/resume — clicking this is
    /// the explicit "I'm done" action. The panel disappears on stop.
    @ViewBuilder
    private var stopButton: some View {
        if case .stopped = vm.state {
            EmptyView()
        } else {
            Button {
                Task { await vm.stop() }
            } label: {
                Image(systemName: "stop.circle.fill")
                    .font(.system(size: 20))
                    .foregroundStyle(Theme.Color.stateDanger)
            }
            .buttonStyle(.plain)
            .help("Stop auto-keys entirely. Use the kill-key gesture instead to pause and resume without stopping.")
        }
    }

    private var statusDot: some View {
        Circle()
            .fill(dotColor)
            .frame(width: 10, height: 10)
    }

    private var dotColor: Color {
        switch vm.state {
        case .stopped:                    return Theme.Color.fg3
        case .running:                    return Theme.Color.stateOk
        case .paused(.userEngaged, _):    return Theme.Color.stateWarn
        case .paused(.userRequested, _):  return Theme.Color.stateInfo
        }
    }

    /// Top line — what's happening at a glance.
    private func headline(at date: Date) -> String {
        switch vm.state {
        case .stopped:
            return "Auto-keys stopped"
        case .paused(.userEngaged, _):
            return "Paused — mouse movement"
        case .paused(.userRequested, _):
            return "Paused"
        case .running:
            if let now = vm.currentTargetLabel {
                if let key = vm.currentStepKeyName {
                    return "Pressing \(key) on \(now)"
                }
                return "Focusing \(now)"
            }
            if vm.nextIterationAt != nil {
                return "Cycle complete — waiting"
            }
            return "Running"
        }
    }

    /// Bottom line — timer + next account.
    private func detail(at date: Date) -> String {
        let elapsed = vm.runStartTime.map { Int(date.timeIntervalSince($0)) } ?? 0
        let runTime = formatMinSec(elapsed)
        if case .running = vm.state, vm.currentTargetLabel == nil,
           let next = vm.nextIterationAt {
            let remaining = max(0, Int(next.timeIntervalSince(date)))
            let countdown = formatMinSec(remaining)
            if let label = vm.nextTargetLabel {
                return "NEXT FIRE IN \(countdown) → \(label) · RAN \(runTime)"
            }
            return "NEXT FIRE IN \(countdown) · RAN \(runTime)"
        }
        if case .running = vm.state, let next = vm.nextTargetLabel,
           let now = vm.currentTargetLabel, next != now {
            return "NEXT \(next) · RAN \(runTime)"
        }
        if case .running = vm.state {
            return "RAN \(runTime)"
        }
        if case .paused = vm.state {
            return "Use kill-key gesture to resume · RAN \(runTime)"
        }
        return ""
    }

    private func formatMinSec(_ totalSeconds: Int) -> String {
        let mins = totalSeconds / 60
        let secs = totalSeconds % 60
        return String(format: "%d:%02d", mins, secs)
    }
}
