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

- [x] **Five paths, not four.** The sweep found `PluginsViewModel.cs:432` as well — the crash
      banner's Restart button. Given its own `restart` reason rather than folded into `manual`,
      because it is the one case a plugin has a real motive to treat differently: it is coming back
      from a death, not being started fresh. Autostart is `App.xaml.cs:3205`, install `:234`, update
      `:293` (the one the checklist named), manual `:378`, restart `:432`.
- [x] **A `PluginLaunchReason` enum, not the raw strings.** Five call sites across two files; a typo
      in one would be a value the plugin silently never matches. `ToWireValue` is the only place the
      spelling lives, it throws on an unmapped member, and a test asserts every member maps to a
      distinct non-empty value — so a sixth reason cannot be added without a wire name.
- [x] **The parameter is required, with no default.** A default would let a new launch path compile
      while telling every plugin the wrong story about why it is running, and no value is honestly
      right for "the caller did not say". It earned itself immediately: the compiler named seven
      existing call sites that a default would have waved through.
- [x] `Restart` takes the reason rather than hardcoding `restart`, because `Start` routes through it
      when the plugin is already running — an update that happens to relaunch a live plugin is still
      an *update* to that plugin, which is exactly the case Ur Score asked for. Pinned by
      `Start_OnAnAlreadyRunningPlugin_KeepsTheCallersReasonRatherThanCallingItARestart`.
      `StartAutostart` takes no reason at all, so no caller can claim a sweep was a click.
- [x] No argv change, verified: nothing in `Plugins/` sets `psi.Arguments`. A plugin that never
      reads the variable is byte-for-byte unaffected.
- [x] `contractVersion` does not move; no RPC, capability or consent change. Verified against the
      diff rather than asserted — no changed line in this commit mentions any of the three, and the
      app-side diff is four files of thread-through.
- [x] `DefaultPluginProcessStarterTests` covers each reason, asserted on the `ProcessStartInfo`
      through a new internal `BuildStartInfo` seam: a real child cannot be asked what its
      environment was, since `Start` takes no arguments to pass a probe. **The wire strings are
      spelled out literally in the test**, not read back through `ToWireValue`, which would pass if
      someone renamed one. Plus the trap worth a test of its own: `UseShellExecute` must stay false,
      because `ProcessStartInfo` throws on `Start` when `Environment` is populated and it is true —
      and that throw would land on every plugin launch, not only the ones reading the variable.
- [ ] Tell the Ur Score session when it lands on main. **Still open** — the branch is unmerged and
      untagged, so there is nothing for Ur Score to build against yet.

## 9. Colour emoji in titles

**Depends on:** nothing. **Effort:** M, **and droppable.**

Evaluate `Emoji.Wpf` and `iNKORE.UI.WPF.Emojis` — drop-in `TextBlock` replacements reading the system
Segoe UI Emoji.

### DROPPED, 2026-10-07 — and criterion 5 is the one being exercised, not dodged

Evaluated both candidates and the surfaces. Four findings, any one of which would be enough; the
fourth is the one that ends it.

- [x] **Licence: neither is clean for a Store binary.** `Emoji.Wpf` 0.3.4 (Nov 2022, 1.11 MB,
      469.6K downloads) is **WTFPL** — not OSI-approved, and its transitive dependency is literally
      named `Stfu`. A 626 Labs LLC Store submission whose dependency manifest reads that way is a
      brand problem before it is a legal one. `iNKORE.UI.WPF.Emojis` 0.3.6.4 (Jan 2024, 2.07 MB) is
      **MIT**, which is fine on its face — but it is plainly a repackage of the same 0.3.x
      `Emoji.Wpf` lineage (same version line, same description wording), so the MIT badge covers a
      fork rather than independently reviewed code.
- [x] **Transitive weight.** The MIT one pulls in `iNKORE.UI.WPF` — an entire third-party WPF
      control library — to colour emoji. For 7.3K total downloads. Both are also stale against
      .NET 10: they would be consumed through their `netcoreapp3.1` / `net6.0-windows` assets.
- [x] **It would punch holes in four existing fences.** `NoRawXamlProseFenceTests`,
      `MutedTextFenceTests`, `DismissOrderFenceTests` and `PreferencesCopyTests` all key on the
      literal element name `TextBlock`. Swapping the type makes those elements invisible to the
      prose, muted-text and dismiss-order scans unless every fence learns the new name — a cosmetic
      change quietly shrinking four safety nets, with ratchet ceilings asserted as equality having
      to move alongside.
- [x] **It is not a drop-in on the surface that matters.** On the account row the game title is a
      `<Run>` inside a composed `TextBlock` (`MainWindow.xaml:98`, beside a separator Run and the
      status Run). Both libraries replace `TextBlock` and `RichTextBox`; **neither offers a `Run`.**
      The primary named surface cannot take either library without restructuring the one line that
      also carries the chip trigger logic `TriggeredStatusColourGateTests` and
      `ThemedStatusColourTests` fence.
- [x] **Measured what saying no actually costs, and it is cosmetics only.** Rendered four titles
      through WPF and counted pixels: **0% saturated pixels** in every case, so emoji render
      entirely greyscale — and the glyphs are present (U+1F43E, U+1F383, U+1F36C all in Segoe UI
      Emoji 1.7) with correct metrics. Titles today are legible monochrome line art, **not tofu
      boxes**. Nobody is failing to read a game name; it is just not in colour.

**Verdict: dropped.** Criterion 5 pre-authorised exactly this, and the honest reading is that a
colour-only gain does not buy a WTFPL-or-forked dependency, a transitive UI framework, four
narrowed fences and a restructured status line.

**If it is ever wanted,** the clean route is to render only the emoji runs as `DrawingImage`s off the
font's COLR table — which is the hand-rolled colour-glyph renderer this item's own criterion rules
out of this cycle. It would need its own cycle, its own fence updates, and a `Run`-level answer.
Item 12's "new dependency audited" line is consequently a no-op.

## 10. The About page signs itself

**Depends on:** nothing. **Effort:** M + assets.

Two drawn marks following the avatar pattern (`sources/*.svg` kept, PNG committed): the 626Labs
wordmark with LLC at the bottom, always visible, 40px minimum; the "Koii 4 eva" wordmark revealed by
the egg.

- [x] **Neither mark requires EsteFont Pro inside the binary.** Both are outlines. The 626Labs
      wordmark is lifted from the design skill's already-outlined official SVG — the brand rule is
      "use the files, don't retype it… never for shipped work" — and "Koii 4 eva" was converted from
      EsteFont Pro Bold to paths once by `scripts/make-koii-mark.py`. The licensed face ships
      nowhere.
- [x] **Geometry, not a PNG — a deviation from the avatar pattern this item cited.** That pattern
      commits a rendered PNG, which suits a 256px circle-cropped character. These are wordmarks with
      a 40px floor that must stay crisp at any DPI, and WPF draws a `Path` natively, so a raster step
      would only lose fidelity. It also needs no rasteriser, which this box does not have (cairosvg
      and skia both absent). Generated into `About/Marks/Marks.xaml` by
      `scripts/gen-about-marks-xaml.py`, because 65 KB of path data should not be hand-diffed.
      `FillRule="Nonzero"` is explicit: WPF defaults to `EvenOdd` while SVG defaults to `Nonzero`,
      and these came out of an SVG pen.
- [x] **No `OpacityMask`, and the gradient is a brush — this is where the drafted recipe was wrong.**
      A mask over a tinted rectangle is what an *image* asset needs; geometry takes a `Fill`
      directly. More importantly the first attempt published a `Color` beside every theme brush so
      the stops could read `{DynamicResource CyanColor}` — **it renders transparent.** A
      `GradientStop` is a `Freezable`, not a framework element, and a dynamic reference inside one
      never finds the dictionary. The build was clean; the page would have shipped an invisible mark.
      `ThemeService` now assembles the whole gradient as a frozen `DuoBrush` from the theme's own
      ends, and `Path.Fill` reads it as an ordinary DP lookup. The speculative Colour surface was
      reverted rather than left in unused.
- [x] **One duo per page, which the draft would have broken.** The brand reserves the cyan-to-magenta
      gradient for "dark marquee moments only, **one per page**", and forbids recolouring the
      official marks outside their three inks. So the duo goes on the egg — the actual marquee moment
      — and the 626Labs wordmark stays single-ink, filled from the theme's own text brush. All four
      built-in themes are dark grounds (`#0F1F31`, `#0A1320`, `#1A0F1F`, `#101010`), which is the ink
      the Logos README assigns `-dark` to, and reading the brush rather than the asset's baked
      `#ffffff` keeps it legible on a user-authored light theme too.
- [x] The existing `DropShadowEffect` stays, byte-identical. Its `ThemedStatusColourTests` allow-list
      anchor moved from `<TextBlock.Effect>` to `<Path.Effect>` in the same commit, since only the
      element holding the glow changed.
- [x] **All seven `AboutPage_Koii4Eva` entries retired**, not six — the neutral catalogue counts too,
      and `CultureKeyParityFenceTests` now asserts both directions, so leaving the cultures would
      have failed the fence item 6b just added. Routed through the pipeline's `STALE` list with the
      reason recorded, then all six catalogues regenerated; every catalogue is at 1,106 keys.
- [x] The egg still fires on six or seven clicks: `EasterEggCounterTests` arrived with PR #228,
      merged to main and rebased under this branch before the mark went in. The element is now a
      `Path` named `EasterEggMark`; the counter, the fade and the visibility flip are untouched.
- [x] Marks come through the `626labs-design` skill, not freehand — the official outlined wordmark
      for one, the skill's own EsteFont Pro Bold for the other.
- [x] **New `AboutMarkGateTests` renders pixels rather than reading the dictionary**, which is the
      only reason the transparent-gradient bug was caught. Also pins the duo brush's ends to the
      theme's, and asserts both geometries parse to real wordmark-shaped bounds — a truncated
      `Figures` string parses happily into an empty geometry that looks like a layout bug. It reads
      `Marks.xaml` off disk rather than through a `pack://` URI, because that scheme is only
      registered once an `Application` exists, which made the clause pass or fail on test order.

**Not done here:** nobody has looked at it. The marks are measured, not seen — item 11's live smoke
is where they get eyes, and the wordmark's clear space and the egg's 26px height are the two
judgement calls most likely to want nudging.

## 11. Live smoke, written as it runs

**Depends on:** 1-10. **Effort:** M.

`docs/smoke-2026-10-XX-alerts-cycle.md`, in the shape of
`smoke-2026-10-05-memory-watch-live-toggle.md`: pass conditions written **before** the run, a
confound control where one exists, and the result filled in as it happens.

**Script written and waiting, 2026-10-07: `docs/smoke-2026-10-07-alerts-cycle.md`.** Ten runs with
pass conditions written BEFORE the run, in the shape of the memory-watch smoke. Needs Este; nothing
in it can be driven headless.

- [ ] Every PRD acceptance criterion that needs eyes or a log is walked. The script's ten runs map to
      them: destination obeyed (run 1, the bug that started the cycle), our themed balloon (2),
      click-through (3), the three sounds (4), the quiet period and Every time (5), in-game idle (6),
      fullscreen (7), the two About marks (8), four themes plus German and Polish (9), and a toggle
      driven by keyboard rather than mouse (10).
- [ ] The fullscreen question is answered: does the themed balloon paint over a fullscreen Roblox
      client? If not, it goes in the release notes rather than being quietly hoped away. **Run 7, and
      either answer passes as long as it is recorded.**
- [ ] Mute the memory alert's Discord and phone destinations before forcing crossings; the clan does
      not need the test traffic. **In the script's "Before starting", with a reminder to put them
      back.**
- [ ] **Confound controls are in the script where one exists** — run 1 re-ticks Desktop and forces
      another crossing, because "no notification" proves nothing if the crossing was not firing. Run
      5 carries the warning that the build's first cadence test was worthless: the re-arm cycle was
      shorter than the quiet period, so every crossing legitimately fired.
- [ ] **Starting theme and UI language recorded up front and restored at the end** — a previous
      session left the theme on flatline after claiming a byte-identical restore.

## 12. Documentation & Security Verification

**Depends on:** 1-11. **Effort:** M.

- [x] **`docs/features.md`: two rows were stale, one badly.** *Activity / idle awareness* still cited
      `App/Notifications/IdleAlertPresenter.cs`, which item 3 deleted, and `settings:muteIdleAlerts`
      as a live gate — it now survives only as the one-time migration read, verified against the tree
      (`App.xaml.cs:2101`). Rewritten to describe the `AlertKind.AccountIdle` routing, the in-game
      gate and its named consequence. *Alerts* gained the idle kind, the configurable quiet period,
      and the drawn balloon that now obeys the destination grid, the mute and the cadence — it
      previously obeyed none of them.
- [x] **CLAUDE.md carried two stale numbers**, both corrected: the verify line said `1899 unit + 24
      harness` (now 2,802 + 27) and the fence list said `ThemedStatusColourTests literal ceiling 21`
      when the constant has been 25 since item 6.
- [ ] `docs/feature-ledger.md` gains its row **in the session the release tags**, which is its own
      rule. **Deliberately not done now** — the branch is untagged, and doing it early is how it fell
      four releases behind in the first place.
- [x] **`docs/decisions.md` carries the cycle's real forks** — six entries dated 2026-10-07: the
      in-game idle ruling, the parity fence that measured the wrong parity, the toggle guard, the
      launch reason, the dropped emoji rider and the About marks.
- [x] **The dashboard mirror is not just attempted, it landed** — six decisions logged
      (`LiuSTb0ByWZE84Nfv7PR`, `eu3h1KeyzNzWsUCfi4Ch`, `OvSdNJI3DMRrbtyPIZ0Q`, `JVIxMFJjyHhAxJEDHXWr`,
      `pVXdwjKky0RjZQBC9Y0y`, `4fMzrNjy8q3Ocw2N0gZG`, `zROC3r1JYH7vX4bDKoT4`). This session's MCP tool
      registry dropped the dashboard mid-session and `ToolSearch` could not re-add it, so each was
      logged through `claude -p` in a fresh process, which still had the tools. The service was never
      down — `claude mcp list` showed it Connected throughout.
- [x] **The findings register closes no rows, and that is the honest answer.** No open row touches
      this cycle's surfaces: the alerts work came from Este's complaint plus a fresh audit, not from
      the settings-UI design review this register tracks. So no counts moved and none were invented.
      What the register did gain is the **F-102 generalisation**: that row fixed the streamer toggle
      and the tray item and stopped, and ten more `Click`-wired toggles turned up two months later.
      Recorded in the row with the lesson — pinning the framework fact is not the same as fencing the
      pattern.
- [x] **Secret scan and local-path guard green on every commit**, and swept across the whole tree
      rather than only staged files. No key material tracked. The two cookie-prefix hits are the
      capture tool's own *detection pattern*, not cookies. **Pre-existing exposure worth naming,
      since this repo is PUBLIC:** three tracked files carry absolute user-profile paths — the guard's own
      explanatory comment, `PROVENANCE.txt` (which CLAUDE.md declares immutable), and
      `docs/reviews/2026-06-12-raw-findings.txt` (a historical transcript). None were introduced by
      this cycle and none were touched. It leaks a username already public through the repo owner, so
      it is low impact, but it is Este's call whether the review transcript gets scrubbed.
- [x] New dependency audited: **no-op, no dependency was added** — item 9 was dropped, with the
      licence and size facts recorded there rather than here.
- [x] **`ROROROblox.slnx` builds on x64 and native arm64 in CI, suite green on both** — run
      `37679432136` on `3e406df`, both architectures pass in 2m11s, alongside the `secret-scan +
      local-path guard` job and GitGuardian. Note the trigger: `ci.yml` scopes `push` to main and
      relies on `pull_request` for branch commits, so pushing the branch alone ran nothing — draft
      **PR #229** is what makes this check exist. Locally 2,802 unit + 27 harness, 1 harness skip by
      design.
- [ ] Release paperwork follows the playbook, including the reviewer letter — which Phase 2 now names
      after it was missed on 1.32.1. **Blocked on item 11**, the live smoke, which needs Este.
