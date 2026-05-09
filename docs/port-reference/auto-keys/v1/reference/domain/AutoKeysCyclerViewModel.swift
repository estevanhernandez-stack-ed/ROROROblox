// AutoKeysCyclerViewModel.swift
// Domain (UI-glue) — `@MainActor @Observable` bridge between the
// `AutoKeysCycler` actor and SwiftUI (Slope C wave 3). The cycler's
// `state` and `observe()` AsyncStream live behind actor isolation;
// SwiftUI binds to plain stored properties. The view-model subscribes
// to the stream once, republishes the latest state to the main actor,
// and exposes plain methods (`play()`, `pause()`, `resume()`, `stop()`)
// the toolbar + banner views can call.
//
// Building `[Target]` at play time:
//   1. Walk `AccountStore.shared.accounts` for entries with non-nil,
//      non-empty `autoKeys`.
//   2. Look up each one's pid in `RunningAccountTracker.shared` —
//      accounts that aren't currently running are skipped.
//   3. The result feeds `cycler.start(accounts:loopDelay:)`.

import Foundation
import Observation
import UserNotifications

@MainActor
@Observable
public final class AutoKeysCyclerViewModel {

    public static let shared = AutoKeysCyclerViewModel()

    /// Latest cycler state, mirrored from the actor. UI binds to this.
    public private(set) var state: AutoKeysCycler.State = .stopped(reason: nil)
    /// Latest estimated cycle time over the snapshot the toolbar built —
    /// recomputed on every `refreshEstimate()` call. Tied to the
    /// CycleBudget warn/cap thresholds for color-coded label rendering.
    public private(set) var lastEstimate: TimeInterval = 0
    /// Banner string when permissions are missing or no accounts are
    /// configured. Surfaced in the toolbar / recorder; nil = no issue.
    public private(set) var preflightMessage: String?
    /// Last error from a `play()` attempt — used for an alert / banner.
    public private(set) var lastError: String?
    /// Wall-clock when `play()` last transitioned the cycler to
    /// `.running`. Toolbar's TimelineView reads this to show elapsed
    /// time. Nil when stopped.
    public private(set) var runStartTime: Date?
    /// Label of the account the cycler is currently focused on, or nil
    /// if between iterations. Updated by a cycler callback.
    public private(set) var currentTargetLabel: String?
    /// Label of the next account in the cycle order. Lets the toolbar
    /// surface "now firing X, next: Y" so the user knows what to expect.
    public private(set) var nextTargetLabel: String?
    /// When the cycler is between iterations (loopDelay sleep), this is
    /// the wall-clock at which the next iteration will start. The
    /// toolbar's TimelineView reads this to render a countdown.
    public private(set) var nextIterationAt: Date?
    /// Pretty name of the key being pressed at this instant on the
    /// current target (e.g. "W", "Space"). Nil between steps and
    /// between iterations. Status panel + menu bar use this to show
    /// "Pressing W on Alice".
    public private(set) var currentStepKeyName: String?

    private let cycler: AutoKeysCycler
    private let store: AccountStore
    private let tracker: RunningAccountTracker
    private let settings: LaunchSettingsStore
    private let safety: AutoKeysSafetyMonitor
    private var observerTask: Task<Void, Never>?
    private var safetyTask: Task<Void, Never>?
    private var safetyBooted: Bool = false

    private init() {
        self.cycler = .shared
        self.store = .shared
        self.tracker = .shared
        self.settings = .shared
        self.safety = .shared
        startObserving()
        bootSafetyIfPermitted()
    }

    /// Test seam — production callers use `.shared`.
    init(
        cycler: AutoKeysCycler,
        store: AccountStore,
        tracker: RunningAccountTracker,
        settings: LaunchSettingsStore,
        safety: AutoKeysSafetyMonitor
    ) {
        self.cycler = cycler
        self.store = store
        self.tracker = tracker
        self.settings = settings
        self.safety = safety
        startObserving()
    }

    // No deinit — `.shared` is process-lifetime; the test-seam initializer
    // also holds the task for the test's duration. Cancellation isn't a
    // concern here since the cycler's `observe()` stream cleans up
    // continuations on its own when consumers disappear.

    // MARK: - Public API

    /// Build a snapshot from configured + currently-running accounts and
    /// hand it to the cycler. Surfaces permission / no-target errors via
    /// `preflightMessage` rather than throwing.
    public func play() async {
        preflightMessage = nil
        lastError = nil

        // Permission preflight. Both Accessibility (for posting) and
        // Input Monitoring (for the safety monitor) must be granted.
        if AutoKeysPermissions.accessibilityStatus() != .granted {
            preflightMessage = "Auto-keys needs Accessibility permission to send keystrokes. Open System Settings → Privacy & Security → Accessibility, then press Play again."
            return
        }
        if AutoKeysPermissions.inputMonitoringStatus() != .granted {
            preflightMessage = "Auto-keys needs Input Monitoring to detect the kill key + pause when you move the mouse. Open System Settings → Privacy & Security → Input Monitoring, then press Play again."
            return
        }

        // Permissions are good — make sure the safety monitor is up
        // (covers the case where the user just granted Input Monitoring
        // and the auto-boot at init was a no-op).
        bootSafetyIfPermitted()

        // Best-effort fill: scan running Roblox processes and match by
        // bundle name. Catches windows launched in a prior RORORO session
        // (or via Roblox.com directly with our URL handler).
        tracker.backfillFromRunningProcesses()

        let targets = buildTargets()
        guard !targets.isEmpty else {
            // Distinguish "you haven't recorded any sequences" from
            // "you have sequences but no Roblox windows are running for
            // those accounts" — different remediation each.
            if settings.autoKeysStayAwakeMode {
                preflightMessage = "Stay-awake mode is on, but no Roblox windows are running. Launch a Roblox account from RORORO first."
            } else {
                let configured = store.accounts.contains { ($0.autoKeys?.isEmpty == false) }
                if configured {
                    preflightMessage = "You've recorded auto-keys sequences, but none of those accounts have a running Roblox window. Launch one first via the per-row Launch As button."
                } else {
                    preflightMessage = "No accounts have auto-keys configured. Tap the AUTO-KEYS chip on any account row to record a sequence — or flip on Stay-awake mode in the toolbar menu."
                }
            }
            return
        }

        // Stay-awake mode pace: 30s between iterations. Active-macro
        // pace: whatever the user set (default 0 = back-to-back).
        let loopDelay = settings.autoKeysStayAwakeMode
            ? LaunchSettingsStore.stayAwakeLoopDelay
            : settings.autoKeysLoopDelay
        let snapshot = targets.map(\.sequence)
        let estimate = CycleBudget.estimate(snapshot: snapshot, loopDelay: loopDelay)
        lastEstimate = estimate

        do {
            try await cycler.start(accounts: targets, loopDelay: loopDelay)
            runStartTime = Date()
        } catch let AutoKeysCycler.StartError.budgetExceeded(estimated, hardCap) {
            lastError = "Cycle estimate is \(formatSeconds(estimated)) — over the \(formatSeconds(hardCap)) hard cap. Trim sequences or run fewer accounts."
        } catch {
            lastError = "Couldn't start auto-keys: \(error.localizedDescription)"
        }
    }

    public func pause() async {
        await cycler.pause()
    }

    public func resume() async {
        await cycler.resume()
    }

    public func stop() async {
        await cycler.stop()
    }

    /// Clear the preflight + last-error banners. Called from the
    /// toolbar's alert buttons so dismissing the alert actually clears
    /// the state — without this, the alert re-presents on next render.
    public func clearMessages() {
        preflightMessage = nil
        lastError = nil
    }

    /// Boot the always-on safety monitor if Input Monitoring is granted.
    /// Idempotent — second/third calls are no-ops once it's running.
    /// Called from init AND before each `play()` so a just-granted
    /// permission picks up cleanly. Once booted, the monitor stays up
    /// for the rest of the process; both the cycler (when running) and
    /// the view-model (always) subscribe to its events.
    public func bootSafetyIfPermitted() {
        if safetyBooted {
            NSLog("[RORORO] safety: boot called but already booted; skipping")
            return
        }
        let status = AutoKeysPermissions.inputMonitoringStatus()
        guard status == .granted else {
            NSLog("[RORORO] safety: boot deferred — Input Monitoring status is \(status)")
            return
        }
        safetyBooted = true
        let combo = settings.autoKeysSafety.killKey
        NSLog("[RORORO] safety: booting monitor (kill keyCode=\(combo.keyCode), modifiers=\(combo.modifiers))")
        Task { [weak self] in
            guard let self else { return }
            // Push the latest config in case the user changed it before
            // the monitor was booted.
            await self.safety.updateConfig(self.settings.autoKeysSafety)
            await self.safety.start()
            await self.subscribeToSafetyEvents()
            NSLog("[RORORO] safety: monitor running + view-model subscribed")
        }
    }

    /// Push the latest safety config to the running monitor. Called by
    /// the Safety Setup sheet after Save so a kill-key change takes
    /// effect immediately without restarting the cycler.
    public func refreshSafetyConfig() {
        let new = settings.autoKeysSafety
        Task { [safety] in
            await safety.updateConfig(new)
        }
    }

    private func subscribeToSafetyEvents() async {
        safetyTask?.cancel()
        let stream = await safety.observe()
        safetyTask = Task { [weak self] in
            for await event in stream {
                if Task.isCancelled { return }
                await self?.handleSafetyEvent(event)
            }
        }
    }

    /// Always-on dispatcher — fires regardless of cycler state. The
    /// cycler also has its own subscription that runs only while
    /// running; we DON'T duplicate engagement-pause here (that's
    /// cycler's concern). The view-model handles the toggle case:
    /// kill key when the cycler is stopped → start the cycler so the
    /// user doesn't have to come back to RORORO's toolbar to play.
    private func handleSafetyEvent(_ event: EngagementEvent) async {
        NSLog("[RORORO] safety: view-model received \(event)")
        // Engagement (mouse / non-kill keypress) → fire a one-shot
        // notification so the user knows the cycler paused. Cycler's
        // own subscription handles the actual state transition.
        if event == .userEngaged {
            // Only notify when going INTO a fresh pause from running —
            // not on every mouse move while already paused. The cycler
            // extends the deadline silently on extra engagement events.
            if case .running = await cycler.state {
                notifyEngagementPause()
            }
            return
        }
        guard event == .killRequested else { return }
        let state = await cycler.state
        switch state {
        case .stopped:
            NSLog("[RORORO] safety: kill from .stopped → starting cycler")
            await play()
        case .running:
            // Kill key from running PAUSES (does not stop). Hard stop
            // is the toolbar button. This matches the user's mental
            // model: kill key = "I'm here, hold up"; toolbar stop =
            // "I'm done, shut it down."
            NSLog("[RORORO] safety: kill from .running → pausing cycler")
            await cycler.pause()
        case .paused:
            // Kill key from paused RESUMES. Either auto-resume hasn't
            // fired yet (engagement pause, 5s grace), or it's a manual
            // userRequested pause — either way, resume.
            NSLog("[RORORO] safety: kill from .paused → resuming cycler")
            await cycler.resume()
        }
    }

    /// Send a banner notification informing the user the cycler paused
    /// because of detected user activity. Requests Notifications
    /// authorization on first use; subsequent calls just post.
    private func notifyEngagementPause() {
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert, .sound]) { _, _ in
            // Even if denied, attempt to post — macOS will silently
            // drop. Authorization request is fire-and-forget.
            let content = UNMutableNotificationContent()
            content.title = "Paused due to mouse movement"
            content.body = "Auto-resume in 1.5s, or use your kill-key gesture to resume now. To stop entirely, click Stop in the RORORO toolbar."
            let request = UNNotificationRequest(
                identifier: UUID().uuidString,
                content: content,
                trigger: nil
            )
            center.add(request) { _ in }
        }
    }

    /// Recompute the cycle-time estimate from the live snapshot. Cheap;
    /// the toolbar calls this when accounts list mutates so the label
    /// reflects warn / overCap state without waiting for Play.
    public func refreshEstimate() {
        let targets = buildTargets()
        let snapshot = targets.map(\.sequence)
        let loopDelay = settings.autoKeysStayAwakeMode
            ? LaunchSettingsStore.stayAwakeLoopDelay
            : settings.autoKeysLoopDelay
        lastEstimate = CycleBudget.estimate(
            snapshot: snapshot,
            loopDelay: loopDelay
        )
    }

    // MARK: - Helpers

    private func buildTargets() -> [AutoKeysCycler.Target] {
        // Skip accounts whose cookie is known-expired — Roblox is
        // sitting at the login screen, no in-game effect from firing
        // keys at it. The per-row Login expired badge already tells
        // the user; cycler matches that signal.
        func isEligible(_ account: Account) -> Bool {
            if account.cookieStatus == .expired {
                NSLog("[RORORO] cycler: skipping userId=\(account.userId) (\(account.displayName)) — cookie expired")
                return false
            }
            return true
        }

        // Stay-awake mode: synthesize `[spacebar, 1s]` for every running
        // account, regardless of saved per-account autoKeys. Saved
        // macros are not touched on disk — toggling stay-awake off
        // restores everyone to their custom sequences.
        if settings.autoKeysStayAwakeMode {
            let stayAwakeSequence = AutoKeysSequence(steps: [
                .spacebar(after: LaunchSettingsStore.stayAwakeStepDelay)
            ])!
            return store.accounts.compactMap { account in
                guard isEligible(account) else { return nil }
                guard let pid = tracker.pid(for: account.userId) else { return nil }
                return AutoKeysCycler.Target(
                    pid: pid,
                    sequence: stayAwakeSequence,
                    label: account.displayName
                )
            }
        }

        return store.accounts.compactMap { account in
            guard isEligible(account) else { return nil }
            guard let sequence = account.autoKeys, !sequence.isEmpty else { return nil }
            guard let pid = tracker.pid(for: account.userId) else { return nil }
            return AutoKeysCycler.Target(
                pid: pid,
                sequence: sequence,
                label: account.displayName
            )
        }
    }

    private func startObserving() {
        observerTask = Task { [weak self] in
            guard let stream = await self?.cycler.observe() else { return }
            for await new in stream {
                if Task.isCancelled { return }
                await MainActor.run {
                    self?.state = new
                    // Clear progress + run timer when the cycler stops.
                    if case .stopped = new {
                        self?.runStartTime = nil
                        self?.currentTargetLabel = nil
                        self?.nextTargetLabel = nil
                        self?.nextIterationAt = nil
                        self?.currentStepKeyName = nil
                    }
                }
            }
        }
        // Wire the progress callback so the toolbar sees current/next.
        // When `current == nil` we're between iterations — compute the
        // countdown end time from the active loop-delay setting.
        Task { [weak self] in
            await self?.cycler.setProgressCallback { current, next, keyCode in
                Task { @MainActor in
                    guard let self else { return }
                    self.currentTargetLabel = current
                    self.nextTargetLabel = next
                    self.currentStepKeyName = keyCode.map { prettyKeyName($0) }
                    if current == nil {
                        let loopDelay = self.settings.autoKeysStayAwakeMode
                            ? LaunchSettingsStore.stayAwakeLoopDelay
                            : self.settings.autoKeysLoopDelay
                        self.nextIterationAt = Date().addingTimeInterval(loopDelay)
                    } else {
                        self.nextIterationAt = nil
                    }
                }
            }
        }
    }

    private func formatSeconds(_ seconds: TimeInterval) -> String {
        let mins = Int(seconds) / 60
        let secs = Int(seconds) % 60
        if mins > 0 {
            return "\(mins)m \(secs)s"
        } else {
            return "\(secs)s"
        }
    }
}
