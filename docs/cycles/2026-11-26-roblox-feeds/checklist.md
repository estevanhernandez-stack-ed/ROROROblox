<!-- Cart cycle #7, step 5. Input: spec.md beside this file (every "Spec ref" below points into it).
     Written here rather than docs/checklist.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md
     treats as history. Autonomous run (profile autonomy_level: fully-autonomous, builder: self); every
     beat the record did not answer is marked "(default — confirm on next interactive run)" and collected
     at the end. Every item carries the five fields /build reads: title, Spec ref, What to build,
     Acceptance, Verify. Recon against HEAD 3a1e042 on 2026-11-26. -->

# RoRoRo v1.41 — Build Checklist: Roblox at a glance

Sixteen items, two halts. The order is dependency order with three deliberate moves away from the
spec's §19 list, each explained where it lands:

- **The mock options are rendered at the start, not at the halt** (item 2). They need nothing from the
  code, and the only thing Este's pick blocks is XAML. Rendering them first takes his reading time off
  the critical path.
- **Everything is wired before anything is drawn** (items 8 and 9, ahead of C1). The coordinator, the
  gate hand-off and the off switch go live on the branch with no pixel changed, so the timing rules and
  the truthful check can be read from a real log before the first line of XAML.
- **Strings land with the item that adds them** (items 7, 9, 10), through one cumulative script. Last
  cycle planned a single sweep and `CultureKeyParityFenceTests` overturned it on day one.

Sixteen is above the usual Cart count and it is the real size: one Core fix that can ship alone, two
typed clients, a coordinator, a resolver, two drawn surfaces, a setting, a privacy change in seven
sheets and a release lane with acceptances only Roblox can schedule. Last cycle ran sixteen. Every
merge on offer fuses a Core item with a XAML item or hides a halt.

**Two checkpoints where the build halts for Este:**
**C1 after item 10** (the picks from item 2's rendered options, and the one Neb command; it holds items
11 and 12 and nothing else), and **C2 after item 12** (eyes on the built line and the Library against
the picks; the dev-build live acceptances are read back).
Two more gates are built in rather than numbered: **update day** (item 15, two acceptances that need a
real Roblox update and real accounts) and **the Store write** in item 16, which waits for Este's
explicit word in the session, per house rule.

**The calendar is part of the plan.** 0.748 has been live since 2026-11-16 and 0.749 had not appeared
by 2026-11-26 (`probes/README.md`), so the next client build could land any day; the eight-week pattern
says a Monday or Tuesday, which makes 11-30 or 12-01 the likeliest (an inference from the pattern, not
a fact about Roblox's schedule). Item 15 needs the line drawn (item 11) on a runnable build when that
happens. Miss it and the tag waits for the build after.

Effort: **S** under an hour, **M** a few hours, **L** most of a day. Sized for an Opus session
dispatching one Sonnet worker per item, with the orchestrator verifying every report against the tree
(the era map's `bulk` under `judgment`).

## Build Preferences

- **Build mode:** Autonomous. Cart drives; each item dispatches to a worker, and the orchestrator
  verifies the worker's report against the tree before the box is checked. Run from an **Opus** session;
  this plan was written on Fable 5.2 and the era map says builds and releases run on Opus with Sonnet
  workers.
- **Comprehension checks:** N/A (autonomous).
- **Git:** the cycle docs (`prd.md`, `spec.md`, `probes/`, this file) are uncommitted on
  `docs/roblox-feeds-scope` as this is written, and that branch has no upstream. `/build` opens by
  committing them there, then cuts `cycle/v1.41-roblox-feeds` from that head, so the plan and the code
  reach `main` in one PR **(default — confirm on next interactive run)**. One commit per item,
  conventional commits, the session's attribution trailer. **A fence ceiling or floor moves in the same
  commit as the change that moves it, never in a follow-up.** Pre-commit hooks installed once per box
  (`.claude/hooks/install.ps1`). One PR to `main` after item 14 with `ci.yml` green on x64 and native
  arm64; items 15 and 16 run from `main` and `release/v1.41.0.0` per the playbook. The build never
  writes to a Store surface.
- **Verification:** Yes. Two halts (C1, C2), update day, and the Store-write gate. Every item's Verify
  is a runnable command with its expected output where one exists. Visual checks go to a human and are
  collected at the halts, not sprinkled through the items.
- **Check-in cadence:** N/A (autonomous). Per-run unattended mode is Este's opt-in at the `/build`
  confirmation (`references/unattended-run.md`); the halts and the Store gate hold either way.
- **Standing gates carried into every item:**
  - The full suite stays green: `dotnet test ROROROblox.slnx -c Release --no-build`, 0 failed, 1 harness
    skip by design. A filtered run is not the commit gate (memory, 2026-11-16).
  - No launch path awaits a feed. No new RPC, no capability, no contract package bump.
  - No new call carries a cookie or an account. Nothing new runs at startup.
  - Every new colour is a `DynamicResource` of an existing slot. No new theme slot.
  - No text from Status.io reaches the window, a tooltip, a screen reader or the log.
  - `RobloxCompatChecker.cs`, row 4's border and the drift string are not edited (ruled 2026-11-26).
  - No listing image changes before 2027-02-20.
  - **A live run never closes anything to make room.** Before starting a dev build:
    `Get-Process ROROROblox.App, RobloxPlayerBeta -ErrorAction SilentlyContinue`. If either is up the
    live step waits and says so; a start on a test folder can still close a windowless Roblox client
    (memory, `reference_rororo_dev_build_test_folder`). Live runs set `RORORO_DATA_DIR` unless the item
    says real accounts.
  - A worker that finds the tree disagrees with this plan stops and says so. It does not build to the
    plan.

---

## 1. Pin the bars before anything moves

**Spec ref:** `spec.md > §19 Build order` (item 1), `§16 Tests and fences` (the pixel-identical row),
`§7.3 XAML`. **Depends on:** nothing. **Effort:** S.

**What to build:** Two baselines taken on v1.40 code, before the first source change, because neither
can be taken afterwards. A third the spec asked for cannot be taken at all, and this item records why.

1. `Rendering/RobloxLineRenderTests.cs`, baseline facts only: the real `MainWindow` drawn through the
   existing render host (`ThemedWindowRender`, the `AccountRowCollapsedRenderTests` shape) with no
   Roblox-side notice true and every panel collapsed, at the default width and at 380 px. It pins the
   bounds of the notice area as `V140…` constants written from the measured values, with a comment
   naming the commit: rows 2 to 5 at zero height and the roster's top offset. PNG captures of both
   widths go to `mocks/baseline/` for the eyes-on compare at C2. The pin is layout bounds, not PNG
   bytes, so it holds on both CI architectures **(default — confirm on next interactive run)**.
2. The startup baseline: five starts of the untouched Release build on a test data folder; time from
   the log's start banner to the first line written after `mainWindow.Show()` (`App.xaml.cs:864`); the
   five numbers, their median, the machine and the folder's shape go in
   `docs/store/smoke-2026-11-26-roblox-feeds.md` (create it; it is the record for every measured row in
   this cycle).
3. **The tray baseline is replaced.** The spec's "request log of a session left in the tray" cannot be
   read: the app's log overrides the `System` category to Warning (`Logging/AppLogging.cs:46`), which
   drops `HttpClient`'s request lines, so no list of v1.40's calls exists in a log. The smoke doc says
   so. "Exactly the calls v1.40 makes while hidden" is carried instead by each new lane's own Debug
   line (items 5 and 6) and the fake-clock tests, and is read live in item 8.

**Acceptance:**
- [x] `RobloxLineRenderTests` passes on the untouched tree at both widths, and fails when a constant is
      changed by one pixel (mutation-checked, the red recorded in the commit message).
- [x] The smoke doc exists with five startup timings, their median, the machine and the folder shape.
      If a live start could not be made (a RoRoRo or a Roblox client was running), it says exactly that
      and the timings move to C2's A/B.
- [x] The smoke doc records the tray-baseline correction with the file and line.
- [x] No file under `src/ROROROblox.App` or `src/ROROROblox.Core` is changed by this item.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~RobloxLineRenderTests"`
→ passed. `git diff --name-only main...HEAD -- src/ROROROblox.App src/ROROROblox.Core` → empty.

## 2. The options are rendered and waiting

**Spec ref:** `spec.md > §12 The mock gate`, `§7.3`, `§8.4` (Layout). **Depends on:** nothing; runs
beside items 3 to 10. **Effort:** M.

**What to build:** No code. Through the `626labs-design` skill, one page of rendered options at
`mocks/c1-mocks.html` with full-page captures `c1-brand.png` and `c1-flatline.png` and a
`mocks/README.md` holding the pick table, the same format as last cycle's C1. Every account and game is
made up, and no sentence is taken from a real status page.

| # | Decision | Options | Plan default |
|---|---|---|---|
| A | Tone of the line | A1 contested, outage and drift in the warning colour, known issue and update in white · A2 all five in the warning colour | A1 (spec §7.1) |
| B | The "+N more" control | B1 panel-expander toggle · B2 secondary toggle | B1 (spec §7.3) |
| C | The pulse in the grid tile | C1 two more meta lines, tile grows · C2 one line that wraps · C3 count in the tile, date in the tooltip and the list | C1 (assumption 28) |
| D | List row and panel entry | the joined line "81,827 playing · updated 2 days ago" | as drawn |
| E | A private server | the date, no count | as drawn |
| F | The update notice at 380 px | drops the batch clause | as drawn (assumption 21) |

Also drawn, with no decision attached: the line in each of its five states at the default width and at
380 px, and "+2 more" collapsed and expanded.

**Acceptance:**
- [x] The page shows every state in spec §12's list, switchable across the four built-in themes, with
      flatline captured.
- [x] `mocks/README.md` names each decision, its options and the plan default, and says a reply in any
      shape works ("A1, B1, C3, rest as drawn").
- [x] Nothing in `mocks/` names a real account, and nothing under `docs/store/` changes.
- [x] When Este rules, the pick and its date are written into the README. A ruling can arrive at any
      time before C1.

**Verify:** `ls docs/cycles/2026-11-26-roblox-feeds/mocks/` → `README.md`, `c1-mocks.html`,
`c1-brand.png`, `c1-flatline.png`. The looking is Este's, at C1 or sooner.

## 3. The update check tells the truth

**Spec ref:** `spec.md > §4.1 Version comparison`, `§4.2 Update state`, `§4.4 The drift banner's read
is pinned, not repaired`, `§16`. **Depends on:** nothing. **Effort:** M.

**What to build:** Core and two fakes; its own commit, able to ship alone. **Red first:** add the field
strings to `RobloxUpdateProbeTests` (handler `0, 748, 0, 7481526`, endpoint `0.748.0.7481526`, expects
no update) and see it fail on today's code; the red goes in the commit message. Then `VersionMatch` and
`Compare`, parsing both sides with the existing `RobloxVersion.TryParseInstalled`;
`Diagnostics/RobloxUpdateState.cs`; `GetUpdateStateAsync` and `CompareInstalledTo` on
`IRobloxUpdateProbe` with rules 1 to 5; `IsUpdatePendingAsync` reduced to `Kind == Pending`; the one
Debug line per call. The two fakes get the members (`Discord/DiscordTestHarness.cs`,
`MainViewModelTests.cs`). `RobloxCompatDriftPinTests`: the fact that `Version.TryParse` rejects the
comma form, and the source fence over `RobloxCompatChecker.cs`. `MainViewModel` is not touched here:
the gate becomes truthful through `IsUpdatePendingAsync`, and its hand-off to the coordinator is item 8.

**Acceptance:**
- [x] The two field strings answer "no update", and that test was seen failing before the fix.
- [x] One version behind answers pending and `Newer` in all four spellings (comma or dotted local
      against dotted or comma endpoint). An endpoint lower than the install answers pending and `Older`.
- [x] An unreadable local string is unknown and never a difference: with one readable side the readable
      side decides; with none the state is `Unknown`. Roblox not installed is `Unknown`.
- [x] `RobloxCompatChecker.cs` and `PreWarmGate` are not in the diff. The drift fence fails when a
      `RobloxVersion.` reference is added to the checker (mutation-checked).
- [x] On this machine the built probe now answers `Current` where the PRD measured `True`; the three
      strings and the answer are in the smoke doc.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~RobloxUpdateProbeTests|FullyQualifiedName~RobloxCompatDriftPinTests|FullyQualifiedName~PreWarmGate"`
→ all passed. `git diff --name-only main...HEAD -- src/ROROROblox.Core/RobloxCompatChecker.cs` → empty.

## 4. Roblox's status: a client that keeps no text, an evaluator, four recorded answers

**Spec ref:** `spec.md > §6.1 RobloxStatusApi`, `§6.2 The watch table and the evaluator`, `§6.3 Levels`,
`§6.4 Fixtures`, `§9` (status column). **Depends on:** nothing. **Effort:** M.

**What to build:** `Core/Feeds/`: `FeedOutcome`, `IRobloxStatusApi`, `RobloxStatusApi` (one constructor,
15 s timeout, 512 KB cap, DTOs of two ids and a code), `RobloxStatusSnapshot`, `RobloxStatusWatch` (the
five pairs), `RobloxStatusEvaluator`, `RobloxLinks` (the two addresses). Registration in
`RobloxHttpClients.cs` with `UseCookies = false` and `AllowAutoRedirect = false`, and the
`TypedHttpClientRegistrationTests` additions the spec lists. The four recorded answers copied from
`probes/` to `src/ROROROblox.Tests/Fixtures/RobloxStatus/` with a `PROVENANCE.md`, and a test that
compares each fixture with its probe file byte for byte. `Feeds/RobloxStatusApiTests.cs`,
`Feeds/RobloxStatusEvaluatorTests.cs`. `scripts/capture-roblox-status.ps1`.

**Acceptance:**
- [x] Roblox's own answer: all five watched pairs are found and the reading is `None`.
- [x] The 2022-03-28 answer, with that page's own watch table: one row id reads 400 under one group and
      300 under another; two open incidents and eleven scheduled maintenances change nothing.
- [x] The 2022-11-29 answer: 500 outranks 200, and maintenance under way reads `Maintenance`.
- [x] The 2026-11-06 answer: a field the parser has never seen is ignored.
- [x] Two pairs of one row count as one row. A watched pair missing from the answer is unknown, and
      `onMissing` is called for that row.
- [x] No DTO has a `string` other than `Id`; the snapshot and the reading hold no `string`. Adding a
      `Name` property fails the reflection test (mutation-checked).
- [x] No network, timeout, 403, 429, 500, empty body, not JSON, wrong shape and an oversized body each
      map to their `FeedOutcome`. The method never throws.
- [x] No fixture is edited. Anything written by hand is named `synthetic-*` and exists only for a
      missing code, an oversized body or a non-hex id.
- [x] From the real `ConfigureServices` graph: no cookies, no redirects, `RORORO/<version>`, one
      constructor.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~RobloxStatus|FullyQualifiedName~TypedHttpClientRegistrationTests"`
→ all passed. `pwsh scripts/capture-roblox-status.ps1` → writes one dated JSON under `probes/` (one
public GET, nothing sent but the request).

## 5. Game pulse: one request per screenful, kept in memory

**Spec ref:** `spec.md > §8.1 GamePulseApi`, `§8.2 GamePulseService`, `§9` (pulse column).
**Depends on:** 4 (`FeedOutcome`, and both touch `RobloxHttpClients.cs` and the registration tests, so
they run in sequence). **Effort:** M.

**What to build:** `Core/Library/`: `IGamePulseApi`, `GamePulseApi` (50 ids, its own interface so no
`IRobloxApi` fake moves), `IGamePulseService`, `GamePulseService` (interest by owner, 250 ms debounce,
the startup gate, the three lifetimes, in memory only). Typed-client registration beside the status
client. `Library/GamePulseApiTests.cs`, and `Library/GamePulseServiceTests.cs` on `FakeTimeProvider`
with a counting fake.

**Acceptance:**
- [x] 50 ids are one request; 51 throws `ArgumentException`; 120 ids are three requests one after
      another, never in parallel.
- [x] Nothing is asked before the `AllowReadsFrom` instant, and nothing ever when it was never called.
- [x] Ten interest changes inside 250 ms are one request.
- [x] A game is not asked about again within 5 minutes of its last attempt, good or failed. `Refused`
      holds every read for 5 minutes.
- [x] At 15 minutes the count reads null, the date stays, and `Changed` is raised for that game.
- [x] A game absent from a good answer has no view. A 200 without `data` is `Shape`, not "every game is
      gone". An out-of-range count or date is dropped for that game alone.
- [x] Interest is the union across owners; an empty set removes the owner; with no interest there is no
      timer.
- [x] `SetEnabled(false)` clears every reading, raises `Changed`, asks nothing and keeps the interest;
      turning it back on asks at the next pass, once.
- [x] From the real graph: the pulse client carries no cookie and follows no redirect.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~GamePulse|FullyQualifiedName~TypedHttpClientRegistrationTests"`
→ all passed.

## 6. The coordinator decides when anything is asked

**Spec ref:** `spec.md > §5.1 RobloxFeeds`, `§5.2 The schedule is a pure function`, `§5.4 Failure
behaviour`, `§9` (update column). **Depends on:** 3, 4. **Effort:** M.

**What to build:** `App/Feeds/FeedSchedule.cs` (`FeedOptions`, `FeedClock`, `Decide`) and
`App/Feeds/RobloxFeeds.cs`, modelled on `Updates/UpdateAvailability.cs`: its own `TimeProvider` timer,
single flight per lane, `Changed`, the attach re-read through `CompareInstalledTo`, the Debug outcome
lines, the once-per-run Warning for a missing row. `Feeds/FeedScheduleTests.cs` and
`Feeds/RobloxFeedsTests.cs` on a fake clock. No registration and no caller yet: that is item 8.

**Acceptance:**
- [x] Nothing is due until 30 s after the window is first visible, and nothing while it is not visible.
- [x] Update: every 30 minutes while visible; never under a strap, and a pass that sees a strap sets
      the state to `Unknown`.
- [x] Status: every 10 minutes; 60 minutes after a 403 or 429; an early read is honoured once, no
      sooner than 60 s after the last attempt.
- [x] Becoming visible runs `Decide` at once.
- [x] Pending, window not visible, a client attaches, the local versions now match: the state is
      `Current` and zero requests were made. An attach while nothing is pending reads nothing.
- [x] A failed update read gives `Unknown`. A failed status read keeps the reading until it is 30
      minutes old, then `None`, and the expiry is evaluated while hidden.
- [x] `Start()` reads nothing. With status disabled, two hours of fake clock with the window visible
      make zero status requests.
- [x] `Changed` never throws into the timer, and a second tick during a read starts no second read.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~FeedScheduleTests|FullyQualifiedName~RobloxFeedsTests"`
→ all passed.

## 7. One resolver picks the sentence, and the sentences exist in seven languages

**Spec ref:** `spec.md > §7.1 The resolver`, `§7.2 View model`, `§7.4 Strings`. **Depends on:** 3, 4.
**Effort:** M.

**What to build:** `App/Notices/RobloxNoticeResolver.cs` (pure) and `RobloxLineViewModel.cs`;
`MainViewModel.RobloxLine`, `Recompute()` on every input the spec names, and the two commands that open
constants from `RobloxLinks`. The seventeen `RobloxLine_*` keys: English in `Strings.resx` by hand, the
six cultures through a new cumulative `scripts/add-v141-translations.py` (the shape of
`add-v136-translations.py`) and `gen-culture-resx.py`. Wording is matched to the shipped catalog, and
"Launch all" inside the update sentence is the button's own label in each language.
**`SetSuppressed` is not deleted here.** The window still binds the old texts until item 11, and
deleting the suppression first would draw the contested banner and the known-issues notice together.
Nothing visible changes in this item.

**Acceptance:**
- [x] The order is contested, outage, known issue, drift, update. `Top` is the first that is true and
      `More` is the rest in order. The drift input is always empty in the app, and the order is still
      tested with a drift text present.
- [x] The update notice is present only when the state is pending and the hold banner is not showing.
      The link is present for `Newer` only; a rollback has the sentence and no link.
- [x] One saved account reads the short sentence, several read the long one, and compact always reads
      the short one.
- [x] An outage names the worst row after a colon, in that level's own sentence, with the counted
      suffix when other rows are affected.
- [x] A known issue behind a higher notice is in `More` and is not dismissed by being hidden.
- [x] No English sentence contains "ban", "captcha", "verification", "session", "account" or "RoRoRo"
      (a test over the seventeen values).
- [x] `IsExpanded` resets when the count reaches zero.
- [x] All seven resx hold the same keys; the script is committed and a second run changes nothing.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~RobloxNoticeResolverTests|FullyQualifiedName~CultureKeyParityFenceTests|FullyQualifiedName~LocKeyParityFenceTests"`
→ all passed. `python -I scripts/add-v141-translations.py` twice → the second run reports no change.

## 8. Everything is wired and nothing is drawn

**Spec ref:** `spec.md > §5.3 Wiring`, `§4.3 The batch gate`, `§9` (the structural test).
**Depends on:** 5, 6, 7. **Effort:** M.

**What to build:** `UpdateAvailability`'s wiring, step for step (spec §5.3): the two singletons
registered (`RobloxFeeds` beside `UpdateAvailability`, `GamePulseService` beside `LibraryCatalog`);
handed to the view model as properties, so no constructor and no test construction site changes;
`Start()` in `RunStartupChecksAsync`; disposal at shutdown; `MainWindow.xaml.cs` pushing
`IsVisible && WindowState != Minimized`; the tracker's `ProcessAttached` hook; the early status read in
the failed arm of `LaunchAccountAsync` for `NoConnection`, `TimedOut` and `RobloxUnavailable`;
`DecidePreWarmAsync`'s third step through `GetUpdateStateAsync` with `ReportUpdateState`; the pulse
gate set from `ReadsAllowedFrom`. `NoFeedInLaunchPathFenceTests`. The feeds default to enabled until
item 9 adds the setting, on the branch only. The window binds nothing new.

**Acceptance:**
- [x] The full suite is green and the app starts. Wiring is where a resolve-time crash hides behind a
      green unit run.
- [x] `NoFeedInLaunchPathFenceTests` is green, and fails when an `await` on `RobloxFeeds` is added to a
      launch method (mutation-checked). `Core/RobloxLauncher.cs` references none of the three types.
- [x] With the fake probe answering `Current`, the batch gate returns `LaunchAllNow` and the state
      reaches the coordinator. With `Pending` it holds as in v1.7.
- [x] The early read is requested for the three connection kinds only, and nothing is awaited.
- [x] `StopPeriodicRefresh()` unsubscribes from `Changed`; no test leaks a timer.
- [x] **Live, the agent's, on a test data folder:** no `roblox-status read` line in the first 30 s
      after the window shows, then exactly one, `Ok`; one `client-version check` line ending `Current`;
      twelve minutes hidden in the tray add no line from either lane; showing the window resumes them.
      The lines are in the smoke doc. If a RoRoRo or a Roblox client is running, this run waits for C1.

**Verify:** `dotnet test ROROROblox.slnx -c Release --no-build` → 0 failed, 1 skipped.
`Select-String -Path "$env:RORORO_DATA_DIR\logs\*.log" -Pattern "roblox-status read|client-version check"`
→ the lines above, with their timestamps against the start banner.

## 9. One switch turns the new reads off

**Spec ref:** `spec.md > §10 The off switch`. **Depends on:** 8. **Effort:** S.

**What to build:** `SettingsBlob.RobloxFeedsEnabled = true`; the `IAppSettings` pair; the four private
fakes; `RobloxFeedsEnabledToggle` on the Settings page in the row shape of `AlwaysShowRecycleToggle`,
`Checked` and `Unchecked` handlers, pushing through `_mainViewModel.SetRobloxFeedsEnabled`; the setting
read before `Start()`; the two strings through the cumulative script. The setting and its control land
in one commit, so `SettingsReachabilityTests` never needs an allow-list entry. It is a plain checkbox
row with no option to pick, which is why it does not wait for C1.

**Acceptance:**
- [x] A `settings.json` with no such key reads true: on for new installs and for upgrades.
- [x] Off takes effect at once: the status reading is `None`, every pulse is cleared, and both change
      events are raised.
- [x] A session started with the switch off: two hours of fake clock, window visible, interest
      declared, zero requests to the status host and zero pulse requests.
- [x] Back on: the first status read comes after the 30 s settle and the pulse asks at its next pass,
      one request each.
- [x] With the switch off and an update pending, the update notice is still the top notice.
- [x] `SettingsReachabilityTests` and `ToggleWiringFenceTests` are green, and the fence's `Exemptions`
      table gains no entry (it finds the setting by reflection; a floor moving is fine).
- [x] The label and the description exist in seven languages, and the description names Status.io as a
      third party.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~SettingsReachabilityTests|FullyQualifiedName~ToggleWiringFenceTests|FullyQualifiedName~RobloxFeedsTests|FullyQualifiedName~GamePulseServiceTests"`
→ all passed. `git diff main...HEAD -- src/ROROROblox.Tests/SettingsReachabilityTests.cs | grep "^+.*RobloxFeedsEnabled"`
→ nothing.

## 10. An entry knows its game, and the pulse has words

**Spec ref:** `spec.md > §8.3 Where a universe id comes from`, `§8.6 Formatting`. **Depends on:** 5, 9.
**Effort:** M.

**What to build:** The half of the pulse that is not XAML, so it lands ahead of C1.
`LibraryEntry.UniverseId`, filled in both places an entry is built (`LibraryCatalog`, and
`AccountPanelViewModel.Synthesize`) in the spec's four-step order. `NoteOnScreen` and
`RequestVisibleIcons` ask the icon cache when an entry has no icon **or no universe id**, and the
universe-only request is made only while the switch is on and after the pulse's gate.
`Games/PulseText.cs` (`Count`, `Updated`, `Joined`). `DisplayDate.MonthYear`. The
`Library_Pulse_Updated` key through the cumulative script. Nothing is shown yet.

**Acceptance:**
- [x] The fill order holds: a saved game's own id; a saved game of the same place; the icon cache;
      otherwise null.
- [x] A saved private server with a thumbnail and no known universe costs one request through the
      existing icon lane, only with the switch on and only after the gate. The panel's load path still
      asks for nothing, and an entry with no icon is asked about exactly as in v1.40.
- [x] The count is the full number grouped by the app's language. Zero reads "0 playing". A private
      server never has a count.
- [x] "Updated" uses the "last played" ladder inside eleven months and a month and year beyond it. With
      the app language and the Windows region set apart, the text follows the app language.
- [x] Both halves join with the existing joiner, and each drops on its own.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~PulseText|FullyQualifiedName~DisplayDate|FullyQualifiedName~LibraryCatalogTests|FullyQualifiedName~AccountPanelViewModel"`
→ all passed.

---

**C1 — the picks, and one command.** Both Este's; neither may already be waiting on him if he read
item 2's page while the Core was being built.

- *The picks.* A to F from `mocks/README.md`, in any reply shape. Items 11 and 12 build to them and
  screenshot-compare before they close (house rule: match the mock).
- *The Neb search* (spec §20), one command on Neb:
  `Select-String -Path "$env:LOCALAPPDATA\ROROROblox\logs\rororoblox-*.log" -Pattern "Pre-warm: holding the batch" | Measure-Object`.
  A count or a zero changes no code. It decides whether the release note says the false hold "was
  showing" or "could show".
- *The agent's half:* item 8's live read, if it had to wait.

**What C1 holds:** items 11 and 12, the XAML. **What it does not hold:** item 13, which describes
behaviour the Core has already fixed. If no pick has come when the build reaches here, it does item 13
and then stops. It does not draw to the defaults **(default — confirm on next interactive run)**.

---

## 11. The line is drawn

**Spec ref:** `spec.md > §7.3 XAML`, `§7.5 Accessibility`, `§7.6 Compact`, `§7.7 Fences and pinned
tests that move`, `§7.1` (the suppression), `§16`. **Depends on:** 7, 8, the C1 picks. **Effort:** L.

**What to build:** `MainWindow.xaml`, the row 3 strip only: the contested child replaced by the line,
the known-issues child removed, the strip's collapse trigger on `RobloxLine.HasNotice`. One
`RobloxNoticeTemplate` used for `Top` and for each item of `More`; declared button slots with fixed
ranks and bound visibility; the more-count toggle in the picked rank with its spoken name.
`KnownIssuesNoticeModel.SetSuppressed` deleted with both call sites, and in the same commit the two
tests that become "it is in `More`", the deleted model test, and the two stale comments. Every fence in
§7.7 moved in this commit. `LabelWrapMeasurementTests` gains the line at both widths in seven cultures.
The two contrast pairs measured in four themes. `RobloxLineRenderTests` grows from item 1's baseline to
the built states. Rows 2, 4 and 5 are not touched.

**Acceptance:**
- [x] Each of the five notices, when it is the one drawn, has the actions it has today: Close Roblox
      for me and Retry; the status link; See known issues and Dismiss; none for drift; What changed.
- [x] With an outage, a known issue and an update all true, the notice area is exactly as tall as with
      one, at the default width and at 380 px.
- [x] With no notice true, item 1's constants hold unchanged.
- [x] A known issue that was hidden shows once when the line is free. Dismissing it from the line or
      from the inline list uses it up, and nothing else does.
- [x] The toggle speaks "2 more Roblox notices"; the tab order is the text, its buttons, the toggle;
      nothing is in a tooltip.
- [x] Under flatline every state carries the glyph and different words.
- [x] Every status and update sentence fits two lines at both widths in seven languages, measured.
- [x] Both contrast pairs pass AA in four themes, or the failing state's tone was changed at the pick
      and the change is recorded. A brush is never changed to make a pair pass.
- [x] `BannerRecipeTests.cs` and `Rendering/BannerPairRenderTests.cs` are not in the diff and are
      green. The naming fence's unnamed ceiling is still 1.
- [x] The built line is screenshot-compared with the pick in brand and flatline; the pairs are saved
      under `mocks/compare/`.

**Verify:** `dotnet test ROROROblox.slnx -c Release --no-build` → 0 failed, 1 skipped.
`git diff --name-only main...HEAD -- src/ROROROblox.Tests/BannerRecipeTests.cs src/ROROROblox.Tests/Rendering/BannerPairRenderTests.cs`
→ empty. `ls docs/cycles/2026-11-26-roblox-feeds/mocks/compare/` → the line's pairs.

## 12. The Library and the panel show the same pulse

**Spec ref:** `spec.md > §8.4 Library`, `§8.5 Account panel`, `§20` (recycled containers and
`Loaded`). **Depends on:** 5, 10, the C1 picks. **Effort:** L.

**What to build:** **It opens with the measurement the spec owes:** a rendering test that scrolls a
100-entry list and counts the interest set, before the four-event design is relied on. The spec states
WPF's documented behaviour and has not measured it in this tree; if the tree disagrees, stop and say
so. Then: `LibraryViewModel` takes `IGamePulseService` in `BuildLibraryPage`; `Loaded`, `Unloaded`,
`DataContextChanged` and `IsVisibleChanged` on the template roots feed one on-screen set; the window's
`StateChanged` empties and restores it; `Detach()` clears it. `LibraryItem` gains the pulse texts and
`EntryName`, and `TooltipText` gains the pulse. The tile, the row and their sizes follow the pick.
`PanelEntry.Pulse` with its copy constructor, replaced in place on `Changed`, with interest declared
while the panel is expanded and the main window visible. `Rendering/LibraryPulseRenderTests.cs`, and
wrap surfaces for the tile, the row and the panel entry.

**Acceptance:**
- [x] Grid and list both carry the pulse beside the "last played" line, never in its place.
- [x] 100 saved games: one screenful is one request; scrolling asks for the newly visible entries
      once; switching between grid and list asks for nothing already read.
- [x] The same game reads the same numbers in the Library and in every open panel: one reading.
- [x] A collapsed panel declares no interest. A panel open at startup asks nothing before the gate.
- [x] A private server shows the date and no count. A game Roblox no longer returns shows no line and
      can still be removed. A "From Roblox" result is unchanged.
- [x] The entry's accessible name and tooltip carry name, players, updated. The pulse is text and
      starts nothing; the button names are unchanged.
- [x] No spinner and no placeholder. The line is absent until an answer arrives.
- [x] Minimised or hidden means an empty interest set; restored declares it again.
- [x] Seven languages without clipping in the tile, the row and the panel, measured.
- [x] The built tile, row and panel entry are screenshot-compared with the pick; pairs under
      `mocks/compare/`.

**Verify:** `dotnet test ROROROblox.slnx -c Release --no-build` → 0 failed, 1 skipped.
`dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~LibraryPulseRenderTests|FullyQualifiedName~LabelWrapMeasurementTests"`
→ all passed.

---

**C2 — eyes on it, and the dev build read back.**

- *Este's eyes.* The line in each state and the Library, list and panel against the picks
  (`mocks/compare/`), in the four themes and flatline, at 100% and 125%, default and compact. German
  and Russian, the longest strings. Filtering, searching and switching views in the Library feel as
  they did in v1.40.
- *The agent's live acceptances,* dev build on a test data folder, written to the smoke doc:
  (a) **absent when current:** thirty-five minutes visible, no update notice;
  (b) **status, live shape:** Roblox's real page read, nothing shown while it is operational;
  (c) **pulse, live:** one saved game's numbers against Roblox's own page for it, inside the refresh
  interval;
  (d) startup against item 1's median;
  (e) **every host unreachable:** the same build started with `HTTPS_PROXY` pointed at a closed local
  port, so every request is refused: the window opens in the same time, nothing is shown, nothing
  errors. If the process does not honour the variable, the row is marked not run and the silence tests
  stand as the record **(default — confirm on next interactive run)**.

**What C2 holds:** the PR in item 14 and everything after it. From here item 15 can begin on any day
Roblox ships.

---

## 13. The privacy page, the listing and the letter say what the app now does

**Spec ref:** `spec.md > §11 Privacy, listing and letter text`. **Depends on:** 8, 9. It may run ahead
of the C1 picks. **Effort:** M.

**What to build:** `docs/PRIVACY.md`: the client-version wording, the games wording, the new
`api.status.io` row, and the four sentences that say who RoRoRo talks to, each found by its content
(the spec's line numbers are from 2026-11-26 and will have moved). The listing's privacy paragraph
gains its one sentence in `listing-copy.md` and the six sheets, with a dated provenance note on each,
and `listing-translations-1.41-delta.json`. `WHATS_NEW_VERSION` becomes `1.41.0.0` in
`scripts/export-listing-translations.py`. `docs/store/whats-new-1.41.0.0.md`, carrying the
quit-before-updating line and the false-hold fix worded by the Neb result.
`reviewer-letter-1.41.0.0.md` and `.paste.txt`. `README.md`'s network sentence.
`docs/store/submission-packet-1.41.0.0.md` with the three-surface audit and the text for the hosted
privacy page (#300). Nothing is written to Partner Center, and no image changes.

**Acceptance:**
- [x] `PRIVACY.md` no longer says the client version is checked only before a batch launch. It has a
      row for the status host that says what it is, that it is not Roblox, GitHub or Microsoft, what is
      sent, what is never sent, when it runs, and which switch stops it.
- [x] Every sentence in `PRIVACY.md` that lists hosts or says there are no other connections is true
      of the build. The three measured rows from 2026-10-11 are untouched.
- [ ] The listing sentence exists in English and in six languages with dated provenance, and the
      listing verifier has reviewed the six.
- [x] The audit table in the packet maps every claim on the short description, the long description
      and the features list to where it is in the tree. Verified against the tree, never a changelog.
- [x] The reviewer letter names the new host, what it is asked, that nothing about the person is sent,
      and that it is on by default with a switch.
- [x] `ListingSheetParityFenceTests` is green. No image under `docs/store/` is in the diff.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~ListingSheetParityFenceTests|FullyQualifiedName~ContrastPairGateTests"`
→ all passed (the second because a docs edit can fail the suite).
`git diff --name-only main...HEAD -- docs/store | grep -i -E "\.(png|jpe?g|webp)$"` → nothing.
`grep -c "api.status.io" docs/PRIVACY.md` → 1 or more.

## 14. Seven languages, verified and measured, and the cycle PR

**Spec ref:** `spec.md > §7.4 Strings`, `§7.6 Compact`, `§16`; CLAUDE.md's generated-resx rule.
**Depends on:** 7, 9, 10, 11, 12, 13. **Effort:** M.

**What to build:** The verifier half of the pipeline; the keys already landed with their items.
`python scripts/translate-cycle.py --stages lint`; `python scripts/export-ui-translations.py`, the
dataset sliced to the twenty keys and their plural forms, ingested and reviewed per language through
the translation verifier; its known rule-3 false positive on quoted UI labels overruled by name and
everything else fixed in `ui-<culture>.json`, then regenerated. The wrap measurement re-run across
every surface this cycle added.
**One conditional rider** (task `kw9psaaah`): a fit test for the Build a theme dialog's button row in
seven languages, and its fix. It is tried once here. If it does not fit, it is cut, the task stays
open and the ledger says so. Store screenshot 8 is left alone either way.
**Then the PR:** `cycle/v1.41-roblox-feeds` → `main`, `ci.yml` green on x64 and native arm64.

**Acceptance:**
- [x] All seven resx hold the same key count; the parity fences are green in both directions; no
      culture resx was edited by hand.
- [x] The verifier run is in the smoke doc with a verdict per language and each overrule named.
      *(2026-11-27: the cloud verifier, 156 of 156 judged; six rule-3 overrules named, one Russian fix.)*
- [x] "Roblox" is English everywhere. "Launch all" in the update sentence matches the button's label in
      each language. The row name follows a colon in every language.
- [x] The lint stage exits 0.
- [x] The theme dialog's fit test is green in seven languages, or the cut is recorded on the task.
- [ ] The PR's checks are green on both architectures, including the `guards` job.

**Verify:** `python scripts/translate-cycle.py --stages lint` → exit 0.
`dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~CultureKeyParityFenceTests|FullyQualifiedName~LocKeyParityFenceTests|FullyQualifiedName~LabelWrapMeasurementTests"`
→ all passed. `gh pr checks` → all green.

## 15. Update day: the acceptances only Roblox can schedule

**Spec ref:** `spec.md > §16` (the live acceptance table, rows four to six), `§17` decision 4, `§20`.
**Depends on:** 11 for the drawn half; the log half can be caught from item 8 on. **Effort:** S of
work, set by Roblox's calendar.

**What to build:** Nothing. Three runs, recorded in the smoke doc with times. The build under test is
the Release build of the merged `main`, run **without** `RORORO_DATA_DIR` and with every other RoRoRo
closed, because the sideload MSIX cannot be installed beside the Store build
**(default — confirm on next interactive run)**.

1. *Any day, Este's hands:* a batch of two or more accounts on a current client.
2. *The day Roblox ships, whoever is at the machine:* open RoRoRo before launching anything. The time
   the endpoint first reports the new version and whether `…/release-notes/release-notes-<N>` answers
   at that moment are both written down: that is the data spec decision 4 asks for.
3. *Same day, Este's hands:* Launch all with the update still waiting.

If Roblox ships before item 11 is built, the log half is still taken (`client-version check` ending
`Pending Newer`, and the hold line), and the drawn half waits for the next build.
`scripts/capture-roblox-status.ps1` is run if Roblox reports an incident at any point before the tag;
that capture joins the fixtures as the first real Roblox incident answer.

**Acceptance:**
- [ ] Run 1: no "Roblox is updating" banner, and no `Pre-warm: holding the batch` line in the log.
- [ ] Run 2: the notice is on the line before any launch; one launch installs the update; the notice
      clears within 60 seconds of that client attaching, with no click.
- [ ] Run 3: the first account launches, the rest are held behind the "Roblox is updating" banner, and
      they are released when the install is done. While the hold banner shows, the notice does not.
- [ ] "What changed" opens the release-notes index, and the page is not a not-found page.
- [ ] The tag in item 16 does not exist before runs 1 to 3 are recorded, or Este has ruled in the
      session that it ships without one of them, and that ruling is in `docs/decisions.md`.

**Verify:** `Select-String -Path "$env:LOCALAPPDATA\ROROROblox\logs\rororoblox-*.log" -Pattern "Pre-warm: holding the batch|client-version check"`
→ run 1 shows a `Current` line and no hold line; runs 2 and 3 show `Pending Newer`, then a hold line
for run 3 only, then `Current`.

## 16. Documentation, security verification, and the 1.41.0.0 ship

**Spec ref:** `prd.md > What We're Building`, `prd.md > Riders`, `spec.md > §2 Runtime and
deployment`, `§13 Data model`, `§14 File structure`, `§18 Dependencies`, CLAUDE.md > Ship.
**Depends on:** everything. **Effort:** L, most of it waiting on CI, certification and Este's word.

**What to build:**

*Docs.* `docs/architecture.md` gains the coordinator, the two anonymous clients, the resolver and the
three data flows of spec §15. `docs/features.md` gains the 1.41 rows and `docs/feature-ledger.md` the
1.41 line. `docs/decisions.md` mirrors every dashboard decision this cycle logged. `CLAUDE.md`: the
test counts re-recorded, the typed-client rule naming the anonymous-clients theory, the Store version
line. The spec's §0 and the three assumption lists re-read; every assumption the build overturned is
marked where it stands, here and there.

*Security.* The pre-commit hooks and the CI `guards` job are the floor. Above them: the cycle's history
(`git log -p main..cycle/v1.41-roblox-feeds`, taken before the merge) searched for the cookie prefix
the guards scan for and for user-profile paths; `probes/`, `mocks/` and `Fixtures/RobloxStatus/`
re-read for identity (public answers from named status pages are fine; an account is not);
`dotnet list ROROROblox.slnx package --vulnerable --include-transitive` clean, or each finding named
with a decision; no new `PackageReference` (spec §1); the `dpapi-cookie-blast-radius` agent over the
final diff of `src/ROROROblox.Core/`, with the two new clients confirmed cookie-free from the real
graph. This cycle's untrusted input is two remote answers, so the input-validation pass re-reads
exactly these: the 512 KB cap, the 24-hex id filter, the range checks on count and date, the DTOs with
no text, and the two opened addresses being constants. No key, no secret and no `.env` exist in this
app; its secrets are cookies in DPAPI, and that is what the pass checks.

*Riders.* Task `8pm4lzbdv` rewritten, not closed: its like-for-like claim corrected with the PRD's
measurement, the "tell" half marked delivered, what remains stated. Task `0h7s65ljx` closed on the
public catalog read of 2026-11-26. Task `bxranna8z` closed once `whats-new-1.41.0.0.md` carries the
quit-before-updating line. #300 closed when the hosted privacy page carries item 13's text.

*Ship.* `release/v1.41.0.0`: `finalize-store-build.ps1 -Version 1.41.0.0 -IdentityName
626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName
"626Labs LLC"`, then again with `-Architecture arm64`. PR to `main`, `ci.yml` green on both
architectures, and green again on `main` after the merge. Tag `v1.41.0.0` → `release.yml` drafts the
Velopack release; **no local `vpk upload`**. The GitHub release published as Latest with real notes.
The Store submission through the store-api loop **only on Este's explicit word in the session**, and
only after his answer on whether the long-description sentence touches what variant B varies (spec
§20): listing and captions before packages, both MSIX, the reviewer letter. After publish: the public
catalog is the live signal, since the API lags Partner Center; the v1.40 update button's Store check
gets its first live test and what it does is written down; the ship is recorded in `features.md`, the
ledger, `decisions.md`, the dashboard project version and the cycle memory **in the same session**. A
clan post is drafted for Este's click, since the false-hold fix is the clan's.

**Acceptance:**
- [ ] `dotnet build ROROROblox.slnx -c Release` → 0 errors. The full suite passes with 1 skip by
      design. `ci.yml` is green on x64 and arm64 on `main`.
- [ ] Every fence constant that moved this cycle moved in the commit that moved it.
- [ ] The secrets search returns nothing; the vulnerability listing is clean or decided; the
      blast-radius run has no open finding; the input-validation re-read is written down.
- [ ] README, `architecture.md`, `features.md`, `feature-ledger.md`, `PRIVACY.md`, `decisions.md` and
      `CLAUDE.md` describe the tree on `main`, spot-checked claim by claim.
- [ ] The four tasks and #300 say what is true: rewritten, closed on named evidence, or left open with
      the reason.
- [ ] GitHub release `v1.41.0.0` is Latest and not a draft, with `roblox-compat.json`, `.sig` and
      `plugins-catalog.json` attached by `release.yml`. The Store submission is certified and
      published, or the exact reason it is not is in the packet.
- [ ] The dashboard project version reads 1.41.0.0, the ship decision is logged, and the cycle memory
      says shipped.

**Verify:** `gh release view v1.41.0.0 --json isLatest,isDraft,assets` → `isLatest: true`,
`isDraft: false`, the three signed assets beside the Velopack set. `gh run list --workflow=release.yml -L 1`
→ success. `gh run list --workflow=ci.yml --branch main -L 1` → success. The public Store page shows
1.41.0.0.

---

## Where each spec section is built

| Spec | Item |
|---|---|
| §4 The update check | 3, and the gate hand-off in 8 |
| §5 The coordinator | 6, wiring in 8 |
| §6 Roblox's status | 4 |
| §7 One Roblox line | 7 (resolver, view model, strings), 11 (XAML, fences, the suppression) |
| §8 Game pulse | 5 (client, service), 10 (universe id, words), 12 (Library, panel) |
| §9 Every read fails to silence | 4, 5, 6 per lane; the structural fence in 8; live at C2 |
| §10 The off switch | 9 |
| §11 Privacy, listing, letter | 13 |
| §12 The mock gate | 2, ruled at C1, compared in 11 and 12 |
| §16 Live acceptances | 8 and C2 (the agent's three), 15 (the three that need Roblox or real accounts) |
| §19 item 1 Baselines | 1 |
| §20 Open issues | Neb at C1; variant B before the Store write in 16; recycled containers opens 12 |

## What needs a person, and when

| Where | Needs | Who | When |
|---|---|---|---|
| Item 2 → C1 | the picks A to F from the rendered options | Este | any time after item 2; holds items 11 and 12 only |
| C1 | one command on Neb | Este | by C1; changes wording, not code |
| Items 1, 8, C2 | a stretch with no RoRoRo and no Roblox client running on Dunder | Este, by not being mid-session | each live run waits for it and says so |
| C2 | eyes on the line and the Library against the picks | Este | after item 12 |
| Item 15, run 1 | a batch on a current client, real accounts | Este | any day after the PR |
| Item 15, runs 2 and 3 | a real waiting Roblox update, real accounts for run 3 | whoever is at the machine; Este for run 3 | the day Roblox ships |
| Item 16 | whether the listing sentence touches what variant B varies | Este | before the Store write |
| Item 16 | the explicit word for the Store write | Este | the session that submits |
| Item 16 | the click on the clan post | Este | after publish |

## Assumptions this plan adds (each one sentence to overturn)

The PRD's sixteen and the spec's twenty-one stand. This checklist adds:

38. The pixel bar is pinned as layout bounds, not PNG bytes; the captures are kept for eyes.
39. No v1.40 list of tray-time requests is taken, because the log cannot give one; the new lanes' own
    Debug lines and the fake-clock tests carry "exactly the calls v1.40 makes".
40. The mock options are rendered at the start of the build, and a ruling can arrive any time before
    C1.
41. A pick not made at C1 holds items 11 and 12 only; item 13 runs ahead, and the build does not draw
    to the defaults.
42. The off switch lands before the line and the pulse are drawn, with its control in the same commit,
    so no reachability exemption is ever written; between items 8 and 9 the reads are on with no
    switch, on the branch only.
43. Strings land with the item that adds them, through `scripts/add-v141-translations.py`; item 14 is
    the verifier and the measurement, not a sweep.
44. The cycle docs are committed on `docs/roblox-feeds-scope`, the cycle branch is cut from that head,
    and one PR reaches `main` after item 14.
45. The build under test for the real-account acceptances is the Release build of merged `main` run
    without `RORORO_DATA_DIR`.
46. The tag waits for one real Roblox update after the line is built; nothing simulates "pending" in
    its place.
47. A live run waits when a RoRoRo or a Roblox client is running; it never closes one.
48. "Every host unreachable" is run live with a dead per-process proxy, if the process honours it.
49. The theme dialog rider is tried once, inside item 14, and cut if it does not fit.
50. The build runs from an Opus session with Sonnet workers; this plan was written on Fable 5.2 and
    makes no claim about where a worker will struggle.
