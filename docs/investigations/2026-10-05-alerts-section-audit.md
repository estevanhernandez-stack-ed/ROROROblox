# The alerts section, as it actually is (2026-10-05)

Read-only audit behind four complaints: alerts fire too often with no way to tune it, the section
is scattered, memory alerts arrive with memory watching off, and the sound is the stock Windows
notification sound. Live evidence is from the Store 1.31.0.0 build running on Este's rig this
morning; code citations are against `main` at `5a64104`.

## 1. Cadence: there is exactly one knob, and it is hardcoded

`AlertRouter.Cooldown` (`src/ROROROblox.Core/Discord/AlertRouter.cs:57`) is `TimeSpan.FromMinutes(5)`,
a `static readonly` with no setter, no `IAppSettings` field and no UI. It is the only cooldown in
the app.

- **Key:** `(AccountId, Kind)`, plus `MetricId` when the kind is `MetricBreach`
  (`AlertRouter.cs:16-43`). Two different rules on the same metric id share one slot.
- **Check:** `nowUtc - last > Cooldown` (`AlertRouter.cs:92`) — strict. A repeat landing at exactly
  5:00 is suppressed.
- **Store:** in-memory (`src/ROROROblox.App/Discord/AlertDispatcher.cs:74`), lost on restart, and
  stamped *after* the sends (`:168`), so racing dispatches can both pass.

The 10/4 log reproduces this precisely: a memory warning at 23:28:03, a projection crossing at
23:33:03 that sent nothing (exactly 5:00, strict `>`), then a headroom crossing at 23:34:03 that
did send. From the outside that reads as random.

Everything else that governs timing is also hardcoded: the metric grouping window is 5 s fixed, not
sliding (`src/ROROROblox.Core/Metrics/MetricBreachBatcher.cs:55`); the watchdog samples every 30 s
(`MemoryWatchdog.cs:21`); uptime marks every 2 h (`UptimeMarkTracker.cs:16`); auto-rejoin allows 3
drops per hour (`AutoRejoinMonitor.cs:81-98`).

**Idle alerts bypass the cooldown entirely.** `IdleAlertPresenter` calls `ITrayService.ShowToast`
directly (`src/ROROROblox.App/Notifications/IdleAlertPresenter.cs:21`), so the router never sees
them. They are governed only by a per-account edge latch (`ActivityMonitor.cs:12`) and the global
mute.

**Per-rule timing is hand-edited JSON.** `windowMinutes`, `threshold`, `alertWhenBelow` and
`tellMeWhenItRecovers` live in `metric-rules.json` with no UI
(`src/ROROROblox.App/Metrics/LocalFileMetricRuleSource.cs:165,224,234`).

**Fan-out multiplies everything.** One event routes to up to four destinations, each a separate
notification. Este's 10/5 metric alerts hit tray + Discord "Mine" + phone for every single event.

### What the user can actually set today

Idle threshold (a 4-item combo: 10/12/15/18 min), memory reserve MB, memory cap MB, projection warn
minutes. Everything else is a boolean or a destination tick.

## 2. Consolidation: three cards, two stores, one split brain

One file holds all of it: `src/ROROROblox.App/Preferences/SettingsPage.xaml` (1150 lines). The nav
item "Alerts & memory" shows `PageAlerts` (278-980), which is three separate cards:

| Card | Lines | Holds |
|---|---|---|
| Idle accounts | 281-317 | mute toggle, threshold combo |
| Alerts | 325-792 | 5 routing rows x 4 destinations, metric opt-in, muted-accounts line, status line, 2 webhooks, phone provider + Pushover/ntfy fields |
| Memory | 823-979 | watch toggle, reserve MB, cap MB, projection minutes |

The seams, in order of how much they cost a reader:

1. **Memory is in two places.** The toggle that turns memory watching on is in the Memory card at
   line 830. Where its alerts *go* is row 2 of the Alerts grid at line 373 — roughly 460 lines of
   markup apart, under a different heading.
2. **Idle alerts are not in the alert grid at all.** No routing row, no destinations, no cooldown —
   a tray toast and nothing else. The one alert kind with its own section is the one kind the alert
   system does not handle.
3. **One row, two stores.** `MetricAlertsEnabledToggle` writes `settings.json` through
   `IAppSettings`; the four destination boxes beside it write encrypted `discord.dat` through
   `DiscordConfig`. Same visual row, two persistence paths, two failure modes.
4. **Discord is split by surface, not by concern.** Webhook URLs and "Send test" are on the Alerts
   page; Discord presence and Join are on their own Discord page (982-1016) with a second status
   line.
5. **Per-account mute lives in the main window.** The action is a row context menu
   (`MainWindow.xaml:325-343`); Settings shows only a count and "Unmute all".
6. **Streamer mode reaches in from another page.** The toggle is on the Accounts page (186-206) but
   re-masks the alerts card's secrets, force-hides the ntfy QR, and makes reveal cost two clicks.

## 3. Memory alerts with watching off: two defects, not one

**Defect A — the toggle is startup-only.** `MemoryWatchdogEnabled` is read exactly once, at
`src/ROROROblox.App/App.xaml.cs:1749`, inside `WireMemoryWatchdogAsync`. All it decides is whether
`watchdog.Start()` creates the 30 s timer. `MemoryWatchdog` has no settings dependency at all, and
all three `PressureCrossed` subscribers — the view model's alert builder
(`MainViewModel.cs:388-408`), the tray badge (`App.xaml.cs:1793-1834`) and the plugin gRPC stream
(`App.xaml.cs:2669-2682`) — subscribe unconditionally and never re-check the flag. `Stop()` has no
production call site. The Settings handler (`SettingsPage.xaml.cs:2491-2508`) only persists the
value: it never resolves `IMemoryWatchdog`, never stops the timer, and the page never says a
restart is needed. Turning it off mid-session does nothing. Turning it *on* mid-session is equally
inert.

Compare `MetricAlertsEnabled`, which gets a live push (`App.xaml.cs:128 SetMetricAlertsGate`) plus a
30 s re-read, and the idle settings, which the Settings page re-pushes per edit. The memory section
has neither.

**Defect B — resolved, and it was not a defect.** There are two installs. The machine this audit ran
on reads `"memoryWatchdogEnabled": true` because the box was never unticked here; Este was looking at
his second machine, where the box shows clear. So the save works and the contradiction was two
settings files, not a persistence bug. The evidence that got there is worth keeping, because it is
what ruled a clobber out:

- Not a stale-snapshot clobber: `AppSettings.LoadAsync` has no cache (`AppSettings.cs:534`, no
  `_cache` field anywhere), and every setter is load-modify-save under `_gate`.
- Not a default reset: the file still holds customized values (`idleWarnThresholdMinutes: 12`,
  `launchWindowed: true`, window bounds), so no fallback-to-defaults write happened.
- Not a silent save failure: the handler catches, shows `MemorySettingsWarning`, and reverts the
  checkbox from disk.

Confirmed by Este the same morning: the box reads unchecked on his machine and a warning still
arrived. That is Defect A exactly as the code predicts — unticked mid-session, timer already
running, nothing stops it.

Fixed on `fix/memory-watch-live-toggle`: `MemoryWatchdogGate` owns whether the watchdog samples, the
Settings toggle nudges it the moment it saves, and the view model's 30 s tick re-reads the setting so
a hand-edited settings.json still counts. `MemoryWatchdog.Stop` now retracts its last snapshot, so
turning watching off cannot leave a warning badge pinned on with nothing left to clear it. The
reserve, cap and projection numbers ride the same refresh and are live within a tick too.

**Three memory-flavoured alert sources are not gated by this toggle at all:**

- The pre-launch RAM headroom modal (`MainViewModel.cs:2291-2299`) has no setting whatsoever. With
  the watchdog off it reads an empty snapshot, lands on `Verdict.Unknown` and waves the launch
  through — disabled by accident rather than by design.
- Plugin metric alerts gated only by `MetricAlertsEnabled`: a rule whose metric id is `memory` or
  `ram` renders as a memory alert and the watchdog toggle has no say.
- The Recycled alert quotes reclaimed RAM and is gated by its own destinations; with the watchdog
  off it simply ships without the figure.

## 4. The sound

`TrayService.ShowToast` calls `_taskbarIcon.ShowBalloonTip(title, message, BalloonIcon.Info)`
(`src/ROROROblox.App/Tray/TrayService.cs:337-347`) — the legacy shell balloon, which always plays
the system notification sound. Verified against the shipped assembly
(`Hardcodet.NotifyIcon.Wpf 2.0.1`): `BalloonFlags` is public and *does* include `NoSound` and
`RespectQuietTime`, but the only overload that accepts flags is **non-public**
(`ShowBalloonTip(String, String, BalloonFlags, IntPtr)`). The public surface is the two
`ShowBalloonTip` overloads and `ShowCustomBalloon(UIElement, PopupAnimation, int?)`.

There is no audio asset in the repo and no toast package referenced. `Microsoft.Windows.CsWin32` is
already a dependency, and the packaged manifest carries a real Store identity
(`Package.appxmanifest:15`).

Three routes:

- **A. Silence the shell balloon.** Reach `NoSound` by reflection (fragile against a package bump)
  or call `Shell_NotifyIcon` ourselves via CsWin32 (two code paths writing one icon). Keeps the
  stock look. Cheapest, ugliest.
- **B. Themed custom balloon.** `ShowCustomBalloon` is public and supported: no OS sound at all,
  our brushes, our chime or silence, and it satisfies the "every popup themed" rule. Costs us the
  Action Center entry, quiet-hours handling becomes ours, and a WPF popup may not paint over an
  exclusive-fullscreen Roblox client.
- **C. Real Windows toasts.** Silent or custom audio, lands in Action Center, honours quiet hours.
  Free in the packaged Store build thanks to the identity above; the direct-download Velopack build
  needs an AUMID shortcut plus a COM activator. Two build shapes diverging is the exact class of bug
  that bit v1.23 with registry virtualization.

## 5. Fences any of this work has to move in the same commit

- `SettingsCommitOnEnterFenceTests.cs:40` — `CommittingFieldsOnThePage = 8`, asserted as equality.
  Any new committing text box (a cadence number, for instance) moves this constant.
- `AccessibleNamingFenceTests.cs:51,57` — `UnnamedCeiling = 1` and `ScannedFloor = 120`, both
  asserted as exact equality. Every added control needs an accessible name *and* a floor bump.
- `AlertsStatusLinePositionFenceTests.cs:46-76` — markup order is fenced: routing rows →
  Unmute all → `AlertsStatusLine` → `MineWebhookInput` / `PushoverUserKeyInput`. A reshuffle has to
  respect this or move the fence.
- `StreamerSecretFieldFenceTests.cs:35` — `RevealTogglesOnThePage = 5`, exact, and each must appear
  in `SecretFields()`.
- `SettingsReachabilityTests.cs:156-200` — a new setting needs an accessor pair *or* a control named
  `<Name>` with an approved suffix. Comments and dotted member reads do not count.
- `PreferencesCopyTests.cs:109-308` — second person, no first person, every label and hint ends in a
  period, no hint restating its label.
- `TypeLadderFenceTests.cs:44` — 3 distinct raw sizes; new markup uses ladder tokens.

## 6. Also noticed

- The watchdog heartbeat logged `0 client(s)` all morning while three accounts were in game
  (`rororoblox-20261005.log`, 09:32/09:47/10:03). If that is not expected, memory watching is blind
  in Este's normal configuration and the toggle argument is moot for him.
- The running build is Store **1.31.0.0**, though 1.32 shipped on 09-30. The Store has not pushed
  the update to this machine.
