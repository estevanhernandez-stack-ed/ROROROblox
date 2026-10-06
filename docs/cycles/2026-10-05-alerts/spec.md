<!-- Cart cycle #5, step 4. Inputs: scope.md and prd.md beside this file. Written here rather than
     docs/spec.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md treats as history. -->

# RoRoRo v1.33 — Technical Spec

Against `main` at `cc9e340` (v1.32.1.0 shipped). Every file:line below was read, not recalled.

## The shape of the change, in one paragraph

Today three code paths can put something on a user's screen: `AlertDispatcher`, which honours
destinations, mute and a cooldown; `WireMemoryWarningTray`, which honours nothing; and
`IdleAlertPresenter`, which honours a single mute flag. This cycle collapses that to one path, moves
the cooldown from a constant to a setting, and replaces the OS balloon with one we draw. Everything
else in the cycle is downstream of that collapse or is an independent rider.

---

## 1. One path to the screen

### What exists

| Path | Entry | Honours |
|---|---|---|
| Router | `App.xaml.cs:1929` → `AlertDispatcher.DispatchAsync` | destinations, per-account mute, 5-min cooldown, coalescing |
| Memory tray | `App.xaml.cs:1858` `WireMemoryWarningTray` → `TrayService.ShowMemoryWarning` | nothing |
| Idle | `MainViewModel.cs:3589` → `IdleAlertPresenter.Notify` → `TrayService.ShowToast` | `MuteIdleAlerts` only |

### What changes

**`WireMemoryWarningTray` stops calling `ShowMemoryWarning`.** It keeps exactly one job: the badge.
`tray.SetMemoryWarning(true)` stays unconditional and uncadenced — it is a colour change, not an
interruption, and it is what keeps memory pressure visible when every destination is off (PRD,
"still find out"). The balloon half of that method goes; the `MemoryWarning` trigger the view model
already raises (`MainViewModel.cs:4237` `BuildMemoryAlerts`) is the only thing that reaches a screen,
and it goes through the dispatcher like everything else.

**`IdleAlertPresenter` becomes a trigger source, not a presenter.** `MainViewModel.cs:3589-3595`
raises `AlertsRaised` with a new `AlertKind.AccountIdle` instead of calling the presenter. The class
is deleted; `IdleAlertPresenterTests` is rewritten against the trigger shape. The per-account
`WarnLatched` edge latch in `ActivityMonitor.cs:115-118` stays — it is "has this account newly gone
idle", which is a different question from "may we speak", and conflating them is how the memory
cap/cooldown confusion started.

**`AlertKind` gains `AccountIdle`** (`Core/Discord/AlertTrigger.cs:11`). Append only — the enum is
serialized into `discord.dat` by position in the destination lists.

**`DiscordConfig` gains `IdleDestinations`** with the same migration shape as
`AutoRejoinPausedDestinations` (`DiscordConfig.cs:58-70`): a config written before the field existed
reads as `[Local]`, not `[]`, because idle alerts show a desktop toast today and an upgrade must not
silence them.

**`MuteIdleAlerts` migrates once and then stops being a control.** On first run after upgrade, a
`true` value sets `IdleDestinations = []` and the key is thereafter ignored. It stays in
`SettingsBlob` (removing it would break older blobs) and goes on `SettingsReachabilityTests`'
allow-list with that reason written in, because its control disappears from the page.

### Why not gate the tray path instead

Keeping `WireMemoryWarningTray` and teaching it to read destinations would mean two places that know
the routing rules, and the one most likely to be edited in a hurry is the one with no test. The PRD's
acceptance criterion "exactly one balloon per event, not two" is only checkable if there is one
producer.

---

## 2. Cadence

### The setting

Two new `IAppSettings` members on the private `SettingsBlob` record at the bottom of
`Core/AppSettings.cs` (the house rule for a new setting):

```csharp
int AlertCadenceMinutes = 5,                      // 0 = every time
string AlertCadenceOverridesJson = ""             // kind -> minutes, absent = follow global
```

`0` means "every time" rather than a nullable, because `0` is already this codebase's idiom for a
deliberate off (`MemoryCapMb`, `AppSettings.cs:616-617`). Per-kind overrides ride as a small JSON map
rather than one key per kind: seven keys would be seven `IAppSettings` members, seven fakes in four
test files, and seven reachability entries for a feature whose rows are generated from one table.

**Four test files carry private fakes of `IAppSettings`** (`MainViewModelTests`, `RobloxLauncherTests`,
`StreamerIdentityProviderTests`, `Discord/DiscordTestHarness`) and stop compiling until updated. That
is the known cost of a new setting here, not a surprise.

### The router

`AlertRouter.Cooldown` (`AlertRouter.cs:57`) stops being `static readonly` and becomes a value passed
to `Route`. The key is unchanged — `AlertCooldownKey.For(t)`, per account and kind, plus metric id for
`MetricBreach` (`:16-43`).

**The boundary comparison changes from `>` to `>=`** (`AlertRouter.cs:92`). Today a repeat landing at
exactly the cadence is suppressed, which is why 23:33:03 produced nothing and 23:34:03 produced an
alert on 2026-10-04. A test pins the boundary in both directions.

**Cadence is evaluated once per event, before fan-out.** It already is — `Route` groups by kind and
produces one `RoutedAlert` per destination from a single cooldown check (`:93-102`) — so the PRD's
"not once per destination" criterion is satisfied by not breaking it. A test pins it, because it is
the kind of thing a refactor silently inverts.

**The stamp-after-send race stays, documented.** `AlertDispatcher.cs:52-57,168` stamps the cooldown
after the sends complete, so two racing dispatches can both pass. It is named as known and deliberately
unfixed in the code today; this cycle does not widen it and does not fix it. Fixing it is a separate
change with its own failure modes.

---

## 3. The notification we draw

### Replacing the balloon

`TrayService.ShowToast` and `ShowMemoryWarning` (`Tray/TrayService.cs:337`, `:151-163`) call
`_taskbarIcon.ShowBalloonTip(...)`, which is the shell balloon: it always plays the system
notification sound, and `BalloonFlags.NoSound` is reachable only through a non-public overload
(verified against the shipped `Hardcodet.NotifyIcon.Wpf 2.0.1` assembly by reflection, 2026-10-05).

**Use `ShowCustomBalloon(UIElement, PopupAnimation, int?)`** — public, supported, and it draws no OS
chrome and plays no OS sound. The balloon content is a small themed `UserControl`
(`App/Tray/AlertBalloon.xaml`) using `DynamicResource` brushes only, per the themed-brush rule.

**Consequences accepted:** no Action Center entry (the shell balloon has none either), and quiet-hours
behaviour becomes ours (today's balloon does not honour Focus Assist in any way we control). Both are
recorded in the PRD's non-goals.

### The sound

New setting `AlertSound` (`Silent | Chime | WindowsDefault`, default `Chime`). Played from the
balloon's show path, not from the dispatcher, so one event is one sound regardless of how many
destinations it fans out to.

- **Chime** — a bundled `.wav`, played with `System.Media.SoundPlayer` (no new dependency, no
  `MediaPlayer` dispatcher affinity). Synthesised rather than licensed: short, quiet, two soft tones.
- **WindowsDefault** — `SystemSounds.Asterisk.Play()`.
- **Silent** — nothing.

**Open, to be measured during build:** whether a WPF popup paints over an exclusive-fullscreen Roblox
client. If it does not, say so in the release notes rather than pretending; the phone destination is
the answer for that case and already exists.

---

## 4. One section

`Preferences/SettingsPage.xaml` keeps its nav item ("Alerts & memory"), and the three cards collapse
into one per-kind table plus the memory numbers beside their row. Ordering is constrained by
`AlertsStatusLinePositionFenceTests:46-76`: routing rows → muted-accounts line and Unmute all →
`AlertsStatusLine` → `MineWebhookInput` / `PushoverUserKeyInput`. The new cadence column lives inside
the routing rows, so the fence holds without moving.

**Fences that move in the same commit, with their current values:**

| Fence | Constant | Why it moves |
|---|---|---|
| `SettingsCommitOnEnterFenceTests:40` | `CommittingFieldsOnThePage = 8` (equality) | memory numbers keep their text boxes; any added committing field moves this |
| `AccessibleNamingFenceTests:51,57` | `UnnamedCeiling = 1`, `ScannedFloor = 120` (both equality) | every new control needs a name and the floor rises |
| `TypeLadderFenceTests:44` | `DistinctRawSizeCeiling = 3` | only if new markup introduces a raw size; it should not |
| `StreamerSecretFieldFenceTests:35` | `RevealTogglesOnThePage = 5` | unchanged unless a credential field moves |
| `SettingsReachabilityTests` | — | new keys need a reachable control or an allow-list entry with a reason |
| `PreferencesCopyTests` | — | every new label and hint: second person, ends in a period, no hint restating its label |

---

## 5. Toggles that tell the truth

`MemoryWatchdogEnabledToggle` is wired to `Click` (`SettingsPage.xaml:830-835`), and
`TogglePattern.Toggle()` raises `Checked`/`Unchecked`, never `Click` — measured live on 2026-10-05: UIA
flipped the box to Off, reported Off, and saved nothing. This is F-102, which was fixed for the
streamer-mode toggle by moving it to two-way binding (`SettingsPage.xaml.cs` ~line 130).

**Every `Click`-wired `CheckBox`/`ToggleButton` on the page moves to `Checked`/`Unchecked` handlers or
two-way binding**, and a new `ToggleWiringFenceTests` fails the build when a new one appears. The fence
is the deliverable — the fix without it is one release from regressing.

---

## 6. Riders

### `RORORO_LAUNCH_REASON`

`DefaultPluginProcessStarter.Start(pluginId, exePath)` (`Plugins/Adapters/`) gains a reason parameter
and sets `psi.Environment["RORORO_LAUNCH_REASON"]`; `UseShellExecute = false` is already set, which is
the condition that makes `Environment` work. `PluginProcessSupervisor.Start` (`:213`) passes it; the
four call sites supply it — `StartAutostart` → `autostart`, the Launch click (`PluginsViewModel.cs:378`)
→ `manual`, post-install (`:234`) → `install`, the update relaunch (`:293`) → `update`.
`contractVersion` does not move. An env var, not argv, because an unknown argument is not inert for a
third-party executable we do not control.

### Colour emoji

WPF renders no colour glyphs in any version. Evaluate `Emoji.Wpf` and `iNKORE.UI.WPF.Emojis` — both
drop-in `TextBlock` replacements reading the **system** Segoe UI Emoji, no bundled images, no embedded
font. **Accept only if:** the licence suits a Store binary, the control honours `DynamicResource`
`Foreground` and the type-ladder tokens, and it carries an `AutomationProperties.Name` path.
**If any fails, drop the rider** — hand-rolling a colour-glyph renderer is not in this cycle. Swap
sites: the account row title, the Games page rows, saved private servers.

### The About marks

Two drawn marks, following the avatar pattern (`StreamerMode/Avatars/sources/*.svg` with the consumed
`*.png` committed beside them), because WPF has no SVG renderer:

- **626Labs wordmark with LLC**, bottom of About, always visible, 40px minimum.
- **"Koii 4 eva" wordmark** in EsteFont Pro, revealed by the egg.

Neither needs the font in the binary. The gradient is a `LinearGradientBrush` behind an `OpacityMask`
of the mark, so colour comes from theme brushes and one asset serves all four themes; the existing
`DropShadowEffect` stays. The six `AboutPage_Koii4Eva` resx entries retire in the same commit —
all seven were byte-identical, so nothing is lost. Egg counting already moved to `EasterEggCounter`
(PR #228).

---

## 7. Test strategy

- **Core logic is unit-tested**: cadence resolution (global, override, every-time), the `>=` boundary,
  once-per-event fan-out, the idle-destination migration, the `MuteIdleAlerts` one-time migration,
  `EasterEggCounter` (done).
- **Fences are deliverables**, not side effects: `ToggleWiringFenceTests` is as much the point as the
  toggle fix.
- **The WPF seam stays smoke-covered**, per `MetricAlertsGateTests`' own note: the balloon's
  appearance, the fade, the sound, the fullscreen question and the colour emoji all need eyes.
- **A live smoke doc** like `smoke-2026-10-05-memory-watch-live-toggle.md` is part of done, not a
  follow-up.

## 8. What this spec deliberately does not decide

- The chime's exact waveform — a build-time artefact, judged by ear.
- Whether every kind gets a cadence override control or only the noisy ones. Six overrides is six more
  controls on a page being simplified; decide against the built page, not on paper.
- The stamp-after-send cooldown race. Named, unchanged, out of scope.
