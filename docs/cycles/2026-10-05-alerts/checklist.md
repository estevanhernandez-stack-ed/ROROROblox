<!-- Cart cycle #5, step 5. Input: spec.md beside this file. Execution is superpowers
     subagent-driven-development with per-task review, this repo's established pairing. -->

# RoRoRo v1.33 — Build Checklist

Twelve items. Order is dependency order, not importance order: the routing collapse (items 1-3) has to
land before the page (6) or the sound (4-5) means anything.

**Two checkpoints where the build halts for Este's eyes:**
**C1 after item 3** (the routing collapse is live and provable) and
**C2 after item 6** (the page is built and must be looked at).

Effort: **S** under an hour, **M** a few hours, **L** most of a day.

---

## 1. `AccountIdle` joins the alert vocabulary

**Depends on:** nothing. **Effort:** S.

Append `AccountIdle` to `AlertKind` (`Core/Discord/AlertTrigger.cs:11`). Add `IdleDestinations` to
`DiscordConfig` with the `AutoRejoinPausedDestinations` migration shape (`DiscordConfig.cs:58-70`):
a config written before the field existed reads `[Local]`, not `[]`.

- [ ] A `discord.dat` with no `IdleDestinations` resolves to `[Local]` through `DestinationsFor`.
- [ ] Append-only: no existing kind's position in any destination list moves.
- [ ] `WebhookPayload` renders the new kind with wording that reads as a sentence, not a label.

## 2. Cadence becomes a setting

**Depends on:** nothing (parallel with 1). **Effort:** M.

`AlertCadenceMinutes` (default 5, `0` = every time) and `AlertCadenceOverridesJson` on `SettingsBlob`
at the bottom of `Core/AppSettings.cs`, plus `IAppSettings` members. `AlertRouter.Cooldown` stops
being `static readonly` and becomes a value `Route` is given.

- [ ] Four test files' private `IAppSettings` fakes updated (`MainViewModelTests`,
      `RobloxLauncherTests`, `StreamerIdentityProviderTests`, `Discord/DiscordTestHarness`) — they
      stop compiling until they are.
- [ ] **`>` becomes `>=` at `AlertRouter.cs:92`**, with a test pinning both sides of the boundary: a
      repeat at exactly the cadence fires; one a second early does not.
- [ ] `0` means every alert passes.
- [ ] An override for one kind does not change another kind's pace.
- [ ] Cadence is evaluated once per event, not once per destination — pinned by a test, because a
      refactor inverts this silently.
- [ ] Default stays 5 minutes: an upgrade changes nobody's behaviour.

## 3. Collapse the three paths to one

**Depends on:** 1, 2. **Effort:** M. **→ C1 halts here.**

`WireMemoryWarningTray` keeps `SetMemoryWarning(true)` and loses its balloon. `IdleAlertPresenter` is
deleted; `MainViewModel.cs:3589-3595` raises an `AccountIdle` trigger instead. `MuteIdleAlerts`
migrates once to `IdleDestinations = []`, stays in `SettingsBlob`, and goes on the
`SettingsReachabilityTests` allow-list with the reason written in.

- [ ] Unticking Desktop for a kind produces no balloon and no sound for it.
- [ ] Ticking Desktop produces exactly **one** balloon per event, not two.
- [ ] A per-account mute silences the desktop for that account.
- [ ] The tray badge still colours on every crossing with every destination off.
- [ ] `ActivityMonitor`'s `WarnLatched` edge latch is untouched — "newly idle" is not "may we speak".
- [ ] `IdleAlertPresenterTests` is rewritten against the trigger, not deleted silently.

**C1 — live check before going on.** With accounts running: force a memory crossing with Desktop
unticked (silence), then ticked (one balloon), then with the account muted (silence), then with
cadence at 1 minute and at every-time. Write it up as it runs.

## 4. The balloon we draw

**Depends on:** 3. **Effort:** M.

`App/Tray/AlertBalloon.xaml`, a themed `UserControl` shown via
`TaskbarIcon.ShowCustomBalloon(UIElement, PopupAnimation, int?)`. `TrayService.ShowToast` and
`ShowMemoryWarning` stop calling `ShowBalloonTip`.

- [ ] `DynamicResource` brushes only; it follows all four themes, checked in each.
- [ ] No OS sound plays from the balloon itself.
- [ ] Title and body still obey `PayloadLimits`' desktop envelope (63/255) or the limits move
      deliberately with the tests that hold them.
- [ ] **Click-to-focus is restored and generalised — item 3 broke it, invisibly.** `ShowToast` gains
      an optional trailing account id; the dispatcher supplies it when a coalesced group resolves to
      exactly one account; the drawn balloon carries it and a click focuses that row. It then works
      for every single-account kind, not just memory warnings. A three-account group carries no id
      and is honestly unclickable.
- [ ] `TrayService.ShowMemoryWarning` and `_lastMemoryWarningAccountId` are removed with the rest of
      the chain once the new path works — item 3 left them standing precisely because deleting them
      would have made this criterion unimplementable, and said so.
- [ ] A new window/control goes in the fence lists it belongs to (`WindowChromeFenceTests` if it is a
      window — it should not be).

## 5. The sound is a choice

**Depends on:** 4. **Effort:** S + an asset.

`AlertSound` setting (`Silent | Chime | WindowsDefault`, default `Chime`), played from the balloon's
show path so one event is one sound regardless of fan-out.

- [ ] Chime is a bundled `.wav` played with `System.Media.SoundPlayer`; synthesised, short, quiet,
      two soft tones. Judged by ear before the item closes.
- [ ] `WindowsDefault` is `SystemSounds.Asterisk`; `Silent` plays nothing.
- [ ] Changing the setting takes effect on the next alert with no restart.

## 6. One alerts section

**Depends on:** 1, 2, 5. **Effort:** L. **→ C2 halts here.**

Three cards become one per-kind table — **does it fire / how often at most / where does it go** — with
the memory numbers beside the memory row and idle in the grid.

- [ ] Markup order still satisfies `AlertsStatusLinePositionFenceTests:46-76`.
- [ ] `AccessibleNamingFenceTests` and `SettingsCommitOnEnterFenceTests` move in this commit if the
      page's control count changes. **Corrected by the item 4 agent — the earlier claim here was
      wrong in two ways.** `UnnamedCeiling = 1` is asserted as equality; **`ScannedFloor = 120` is
      `>=`, not equality**. And the scan only counts `Button, ToggleButton, ComboBox, TextBox,
      CheckBox, PasswordBox` — a `Border` or `TextBlock` moves neither number. Re-derive from the
      real assertions before planning around them. `CommittingFieldsOnThePage = 8` IS equality.
- [ ] `PreferencesCopyTests` holds: second person, every label and hint ends in a period, no hint
      restating its label.
- [ ] New settings keys are reachable from a control or allow-listed with a reason.
- [ ] No raw font sizes added (`DistinctRawSizeCeiling = 3`).
- [ ] The section says, where destinations are chosen, that the tray badge is always on.

**Two debts item 2 handed forward. Both are this item's to pay.**

- [ ] **Delete all THREE `SettingsReachabilityTests` allow-list entries** for `AlertCadenceMinutes`,
      `AlertCadenceOverridesJson` and `AlertSound`. **Corrected by the item 5 agent — it was two
      when item 2 wrote this line, and item 5 added the third on the same expiry.** They exist only
      because the keys shipped ahead of the controls that edit them, and each says so in its own
      text. This file has twice caught an exemption outliving its reason (`DefaultPlaceUrl`,
      `MetricAlertsEnabled`) — both a cycle late.
- [ ] **Add the generation counter to the cadence cache when this item adds the nudge — and to the
      sound cache beside it.** `App.AlertCadenceSetting` is a `volatile` immutable refreshed on the
      30 s tick, with no lock, which is correct while the tick is the only writer. The moment the
      page nudges it on save there are two writers, and that is exactly the race
      `MetricAlertsGateTests` documents: a tick that read before the user's change commits its stale
      value after it. Mirror `SetMetricAlertsGate` / `BeginMetricAlertsGateRead` /
      `TryCommitMetricAlertsGate`. **`App.AlertSoundSetting` (item 5) is the same shape with the same
      single writer, so if the page nudges one it must nudge both, and both need the counter.**

**C2 — eyes on it.** Screenshot every theme, compare against the approved shape, and walk the page
with a keyboard only.

## 7. Toggles that save

**Depends on:** 6 (same file). **Effort:** M.

Every `Click`-wired `CheckBox`/`ToggleButton` on the Settings page moves to `Checked`/`Unchecked` or
two-way binding, the way streamer mode was fixed for F-102.

- [ ] A UIA toggle of each control saves and changes behaviour — the measured failure was
      `MemoryWatchdogEnabledToggle` reporting Off while the watchdog ran.
- [ ] **New `ToggleWiringFenceTests` fails when a new `Click`-wired toggle appears.** The fence is the
      deliverable; the fix without it is one release from regressing.
- [ ] No double-fire: the handler must not run twice per user click after the change.

## 8. `RORORO_LAUNCH_REASON`

**Depends on:** nothing. **Effort:** S.

`DefaultPluginProcessStarter.Start` sets `psi.Environment["RORORO_LAUNCH_REASON"]`;
`PluginProcessSupervisor.Start` passes it; four call sites supply `autostart`, `manual`, `install`,
`update`.

- [ ] All four paths set it, including the update relaunch at `PluginsViewModel.cs:293`.
- [ ] No argv change; a plugin that ignores it is unaffected.
- [ ] `contractVersion` does not move; no RPC, capability or consent change.
- [ ] `DefaultPluginProcessStarterTests` covers each reason.
- [ ] Tell the Ur Score session when it lands on main.

## 9. Colour emoji in titles

**Depends on:** nothing. **Effort:** M, **and droppable.**

Evaluate `Emoji.Wpf` and `iNKORE.UI.WPF.Emojis` — drop-in `TextBlock` replacements reading the system
Segoe UI Emoji.

- [ ] Licence suits a Store binary; the dependency is pinned and its size recorded.
- [ ] The control honours `DynamicResource` `Foreground` and the type-ladder tokens.
- [ ] `AutomationProperties.Name` still works, so the naming fence holds.
- [ ] Swapped at the account row title, the Games page rows and saved private servers; titles without
      emoji are unchanged and nothing shifts.
- [ ] **If any check fails, drop the rider and say so.** Hand-rolling a colour-glyph renderer is not
      in this cycle.

## 10. The About page signs itself

**Depends on:** nothing. **Effort:** M + assets.

Two drawn marks following the avatar pattern (`sources/*.svg` kept, PNG committed): the 626Labs
wordmark with LLC at the bottom, always visible, 40px minimum; the "Koii 4 eva" wordmark revealed by
the egg.

- [ ] Neither mark requires EsteFont Pro inside the binary.
- [ ] Gradient as a `LinearGradientBrush` behind an `OpacityMask`, colours from theme brushes, so one
      asset serves all four themes; the existing `DropShadowEffect` stays.
- [ ] The six `AboutPage_Koii4Eva` resx entries retire in this commit — all seven were byte-identical.
- [ ] The egg still fires on six or seven clicks (`EasterEggCounterTests`, PR #228).
- [ ] Marks come through the `626labs-design` skill, not freehand.

## 11. Live smoke, written as it runs

**Depends on:** 1-10. **Effort:** M.

`docs/smoke-2026-10-XX-alerts-cycle.md`, in the shape of
`smoke-2026-10-05-memory-watch-live-toggle.md`: pass conditions written **before** the run, a
confound control where one exists, and the result filled in as it happens.

- [ ] Every PRD acceptance criterion that needs eyes or a log is walked.
- [ ] The fullscreen question is answered: does the themed balloon paint over a fullscreen Roblox
      client? If not, it goes in the release notes rather than being quietly hoped away.
- [ ] Mute the memory alert's Discord and phone destinations before forcing crossings; the clan does
      not need the test traffic.

## 12. Documentation & Security Verification

**Depends on:** 1-11. **Effort:** M.

- [ ] `docs/features.md` rows updated for every changed feature; `docs/feature-ledger.md` gains its
      row **in the session the release tags**, which is its own rule and was unfollowed for four
      releases until 2026-10-06.
- [ ] `docs/decisions.md` carries the cycle's real forks, and the dashboard mirror is attempted.
- [ ] The findings register (`2026-08-04-rororo-settings-ui-audit-findings.md`) has rows flipped for
      anything this cycle closes, verified against the tree, with counts re-recorded and a direction.
- [ ] Secret scan and local-path guard green: no hardcoded user-profile path, no cookie prefix.
      (Spelling that path out as an *example* trips the guard too — it blocked this checklist's own
      first commit, which is the guard working.)
- [ ] New dependency (if item 9 lands) audited: licence, publisher, size, pinned version.
- [ ] `ROROROblox.slnx` builds on x64 and native arm64 in CI, suite green on both.
- [ ] Release paperwork follows the playbook, including the reviewer letter — which Phase 2 now names
      after it was missed on 1.32.1.
