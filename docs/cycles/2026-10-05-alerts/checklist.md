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

**Amended 2026-10-07, after this item had already shipped.** Este's ruling on the open question
this item left behind: *"an account that isn't in a game should not be getting an idle alert. That's
crazy. It needs to be in game idle."* The alert's value is that Roblox is about to kick the account
out of a game it is earning in; an account on the website, in Studio or offline has nothing to be
kicked out of.

- [x] `ApplyActivityWarnCrossed` drops any crossing whose row is not `InGame`. The gate is at the
      trigger, not in `ActivityMonitor` — `WarnLatched` answers "newly gone quiet", which stays true
      and useful regardless of presence and feeds more than alerts.
- [x] `Invisible` (the presence privacy filter) is dropped too: we cannot see that the account is in
      a game, so we do not claim it is. **Consequence, named not hidden:** a user whose presence
      privacy is on gets no idle alerts at all, and nothing on screen says why. Candidate for a
      later cycle, not this one.
- [x] Per-account inside a coalesced event, not a veto on the event — a mixed roster raises for the
      players and drops the parked.
- [x] The copy stops understating it: the row reads "An account goes idle in a game" and the hint
      says only in-game accounts raise the alert while the chip still appears either way. Both keys
      were already among the 32 English-only ones, so no translation was made false.
- [x] **Three fixtures had to be fixed, which is why this shipped wrong green.**
      `IdleAlertTriggerTests` (×2) and `MainViewModelMarshalledHandlerTests` (×1) added rows
      straight to `vm.Accounts`, where presence defaults to `Offline` — so the suite could not tell
      in-game from parked. A private `InGame(vm, id)` helper now drives presence through
      `ApplyPresence`, and the five new gate tests have an in-game anchor so they cannot be
      satisfied by raising nothing ever.

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

- [x] Markup order still satisfies `AlertsStatusLinePositionFenceTests:46-76`. Nothing moved across
      its three anchors: the pace and the sound went in ABOVE the rows, the memory numbers and the
      idle threshold went in BETWEEN rows, and `OnUnmuteAllClick` → `AlertsStatusLine` →
      `MineWebhookInput` / `PushoverUserKeyInput` is untouched.
- [x] `AccessibleNamingFenceTests` and `SettingsCommitOnEnterFenceTests` move in this commit if the
      page's control count changes. **Corrected by the item 4 agent — the earlier claim here was
      wrong in two ways.** `UnnamedCeiling = 1` is asserted as equality; **`ScannedFloor = 120` is
      `>=`, not equality**. And the scan only counts `Button, ToggleButton, ComboBox, TextBox,
      CheckBox, PasswordBox` — a `Border` or `TextBlock` moves neither number. Re-derive from the
      real assertions before planning around them. `CommittingFieldsOnThePage = 8` IS equality.
      **NEITHER CONSTANT MOVED, and that is the measured outcome rather than an assumption.** Net
      +11 controls (−1 `MuteIdleAlertsToggle`, +4 idle destination boxes, +8 `ComboBox`), all named,
      so unnamed stays at 1 and the floor is a `>=` with more room than before. No `TextBox` was
      added or removed — the three memory numbers MOVED and kept their handlers — so 8 holds.
      Proved by mutation, not by reading: dropping `AlertSoundPicker`'s name took the ceiling to 2,
      and dropping the moved `MemoryReserveMbInput`'s `PreviewKeyDown` named it in the Enter fence.
- [x] `PreferencesCopyTests` holds: second person, every label and hint ends in a period, no hint
      restating its label. Mutation-checked by removing one new hint's final period.
- [x] New settings keys are reachable from a control or allow-listed with a reason.
- [x] No raw font sizes added (`DistinctRawSizeCeiling = 3`). The header row uses `MetaFontSize`;
      everything else `BodyFontSize`.
- [x] The section says, where destinations are chosen, that the tray badge is always on
      (`SettingsPage_TheTrayIconColoursWhatever`, directly under the "tick every place" hint and
      above the pace, so it is read before the first row).

**SPEC §8's OPEN QUESTION, DECIDED: six override pickers, not seven and not three.** A kind gets one
when a repeat for the same cooldown key can arrive with no user action in between — which is the only
case where a quiet period changes an outcome. That is true of drops out, eats memory, goes idle, gets
recycled, auto-rejoin pauses and a metric crossing. It is false of **uptime marks**, and not by a
little: `UptimeMarkTracker.MarkInterval` is two hours and sits UPSTREAM of the router, so every value
such a picker could hold is either a no-op (under two hours) or a silent thinning of the only dead-PC
signal the app has (over it). That cell states the fixed pace instead. Six rather than the three a
"only the noisy ones" reading gives, because a ragged table invites "why not this row" and the
mechanical answer is only short for one row. Reasoning lives beside the uptime row in the markup.

**Two debts item 2 handed forward. Both are this item's to pay.**

- [x] **Delete all THREE `SettingsReachabilityTests` allow-list entries** for `AlertCadenceMinutes`,
      `AlertCadenceOverridesJson` and `AlertSound`. **Corrected by the item 5 agent — it was two
      when item 2 wrote this line, and item 5 added the third on the same expiry.** They exist only
      because the keys shipped ahead of the controls that edit them, and each says so in its own
      text. This file has twice caught an exemption outliving its reason (`DefaultPlaceUrl`,
      `MetricAlertsEnabled`) — both a cycle late. **Done, within the cycle, replaced by a note
      recording what made each reachable.** `MuteIdleAlerts` KEPT, and it stopped being inert on
      this commit: deleting it now fails the fence at `AppSettings.cs:713`, measured, because item 6
      took away its last control and the only live reader left is `IdleMuteMigration` in Core.
- [x] **Add the generation counter to the cadence cache when this item adds the nudge — and to the
      sound cache beside it.** `App.AlertCadenceSetting` is a `volatile` immutable refreshed on the
      30 s tick, with no lock, which is correct while the tick is the only writer. The moment the
      page nudges it on save there are two writers, and that is exactly the race
      `MetricAlertsGateTests` documents: a tick that read before the user's change commits its stale
      value after it. Mirror `SetMetricAlertsGate` / `BeginMetricAlertsGateRead` /
      `TryCommitMetricAlertsGate`. **`App.AlertSoundSetting` (item 5) is the same shape with the same
      single writer, so if the page nudges one it must nudge both, and both need the counter.**
      **Both done, with SEPARATE counters and locks** — one shared bump would make every cadence
      save drop an in-flight sound read, which is a fix manufacturing the staleness it prevents, and
      `AlertCadenceSoundGateTests.TheTwoCountersAreIndependent` pins that. The cadence generation is
      captured before the FIRST of its two reads, not between them: a nudge landing in that gap
      would otherwise pair a pre-change global with a post-change override map and commit a cadence
      that was never in the file.

**What item 6 did NOT cover, for item 11's smoke doc.** Nothing asserts that the page SAVES idle
destinations or that a picker's save reaches the router — both are `async void` handlers on a WPF
page, the seam this repo covers by smoke rather than by unit test. The new strings are in
`Strings.resx` only; the six culture files do not have them, so a non-English install shows them in
English until `scripts/export-ui-strings.py` → translate → `scripts/gen-culture-resx.py` runs. That
is a cycle gap to close before the tag, not an item-6 defect, but it is visible in exactly the
screenshots C2 asks for.

**C2 — eyes on it.** Screenshot every theme, compare against the approved shape, and walk the page
with a keyboard only.

## 6b. The six languages catch up

**Depends on:** 6. **Effort:** M. **Added mid-build, 2026-10-06**, after item 6 shipped 32 new neutral
keys and the build agent flagged that the cultures do not have them. Numbered `6b` rather than
renumbering 7-12, which would churn every reference already written.

Measured, not assumed: `Strings.resx` holds 1,107 keys, `Strings.de.resx` holds 1,075, and the
difference is exactly the 32 this cycle added. A German, French, Russian, Portuguese, Polish or
Spanish install shows the whole new Alerts section in English.

- [x] Run the catalog pipeline — `scripts/export-ui-strings.py` → translate → `scripts/gen-culture-resx.py`
      — with the `PRODUCT_NOUNS` guard, so product nouns stay English. All six cultures now hold
      1,107 keys, matching neutral exactly. `RoRoRo` stays verbatim in the chime option in all six.
      Translations live in `scripts/add-v133-translations.py` so they are reviewable and repeatable,
      not a one-off paste.
- [x] **Wording matched to the shipped catalog, not freshly invented.** Each destination line reuses
      the exact pattern already in use for drop-out, memory, recycle, auto-rejoin-paused and metric
      alerts in that language; the tray hint names the Desktop checkbox by the same word
      `SettingsPage_Desktop` uses there; idle vocabulary follows `SettingsPage_IdleWarnThreshold`.
- [x] **The generator refused the first run, correctly** — four translation rows whose neutral keys
      item 6 deleted (`SettingsPage_MuteIdleAlerts`, the single flag item 3 replaced with
      per-destination ticks; `SettingsPage_RororoShowsOneTrayToast`, the old tray-toast hint; and the
      two card headers `SettingsPage_IdleAccounts` / `SettingsPage_Memory`). Pruned with each reason
      recorded, plus a guard that any OTHER orphan row stops the run instead of being carried.
- [ ] Run the **translation verifier** over the new app strings. **Still open 2026-10-07:** its MCP
      is Connected at the CLI (`project-626labs-translations.web.app/mcp`) but this session's tool
      registry dropped it and `ToolSearch` cannot re-add it. Needs the pending restart, not a
      sign-in. Expect its rule-3 false positive on any quoted UI label and overrule it the way v1.32
      and v1.32.1 did; everything else is a real finding.
- [x] **Added the culture-parity fence that did not exist** — `CultureKeyParityFenceTests`, watched
      failing with exactly 32 in all six before the pipeline ran. It asserts BOTH directions: every
      neutral key present in every culture, and no culture carrying a key neutral has dropped (the
      slower rot — a stale row reads as coverage). Cultures are **discovered from the directory**
      rather than listed, because a hardcoded list is satisfiable by forgetting to add the seventh
      language to it, which is the same shape of hole. Floors on catalog size and culture count stop
      a failed parse or a broken glob from reading as parity. The deliberately-English allow-list
      ships EMPTY: a key the UI can show is a key a translator must see, and product nouns are
      values rather than keys, so they do not belong on it.
- [x] **Wrapping checked by measurement rather than screenshot**, and it passes. Real values from
      the tree (`Width="180"`, `BodyFontSize` 12, `TextWrapping="Wrap"`, body inheriting WPF-UI's
      font stack since no body `FontFamily` is declared): every language maxes at **2 lines, nothing
      reaches 3**. The two labels that wrap in *every* language are pre-existing
      (`AutoRejoinPausesForAnAccount`, `UptimeMarksEvery2Hours`), so two-line labels were already
      the shipped reality; the new idle label joins them in de/es/fr/pt-BR and stays one line in
      en/pl/ru. Measured, not rendered — see the note below.
- [ ] **The visual pass moves to item 11.** A screenshot sweep means launching a second RoRoRo while
      a Store build is live with accounts in games, which is not worth the risk for a check the
      measurement already answers numerically. Item 11 is the live smoke; the German and Polish
      Alerts page gets its eyes there. What to look for, given the measurement: not overflow, but
      that rows in one language now have mixed 1-line and 2-line labels, so the row rhythm is less
      even than English's uniform single line.

## 7. Toggles that save

**Depends on:** 6 (same file). **Effort:** M.

Every `Click`-wired `CheckBox`/`ToggleButton` on the Settings page moves to `Checked`/`Unchecked` or
two-way binding, the way streamer mode was fixed for F-102.

- [x] **Ten controls converted, not nine.** The sweep found `SquadLaunchWindow`'s `CarefulModeToggle`
      carrying the identical defect — and it *saves*, so a UIA toggle of careful mode in the modal
      silently did nothing. Fixed with the nine on this page rather than left for a later cycle,
      because it is the same defect and the fence had to cover it anyway.
- [x] A UIA toggle of each control saves and changes behaviour. The wiring is proved structurally by
      the fence plus two new framework facts in `TogglePatternReachesTheHandlerTests`: that
      `Toggle()` raises `Checked`/`Unchecked` **exactly once** (the file previously pinned only that
      it does NOT raise `Click` — what is broken, never what works, so the fix's own premise was
      unmeasured), and that a programmatic `IsChecked` write raises `Checked` too.
- [x] **`ToggleWiringFenceTests` added and watched failing on all ten first.** Two assertions: no
      toggle wired with `Click`, and no toggle wired for one state only (a `Checked` without an
      `Unchecked` saves on tick and silently keeps the old value on untick, which reads as "it won't
      turn off"). It scans the whole App, not this page, because a fence scoped to one file sends the
      next instance to a different file — which is exactly where the tenth one was. The tag regex is
      multiline on purpose: the first single-line grep of this sweep returned **zero** matches while
      all ten sat in the tree, and the scan floor of 30 is what stops that reading as a clean bill.
- [x] No double-fire, and it is now structurally impossible rather than merely absent: one shared
      handler on `Checked`+`Unchecked` fires once per user action, and the fence forbids a `Click`
      alongside them, which was the only way to get two.
- [x] **The conversion needed guards, which the rewire alone would have missed.** Every one of the
      ten reverts its own checkbox from the stored value when its save throws. While they were
      `Click`-wired an assignment raised nothing, so that was free; `Checked`/`Unchecked` makes the
      rollback re-enter the handler that is handling the failure. `SetToggle` wraps every
      programmatic write in a re-entrancy counter — a counter so nesting is safe, and a flag of its
      own rather than `_suppressClickHandlers`, for the reason `_paintingMetricAlertsToggle` already
      records: that flag is raised and cleared by other concerns, one of them a dispatcher callback
      this page does not schedule.
- [x] **A latent bug fixed on the way past.** The old rollbacks set `_suppressClickHandlers = true`,
      then *awaited* the stored value, then assigned — holding a page-wide suppression flag across an
      await, during which any other control's handler was silently swallowed. `SetToggle` takes the
      value as an argument, so the await completes before the counter is raised and the guard spans
      only the synchronous assignment.
- [x] Checked before converting: none of the ten carries a literal `IsChecked` in XAML, so nothing
      now fires during `InitializeComponent` ahead of the fields it would touch; and nothing outside
      these two files writes their `IsChecked`.

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
