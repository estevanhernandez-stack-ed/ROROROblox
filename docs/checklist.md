# RORORO — v1.18.0 Settings Remediation Build Checklist

**Cycle:** v1.18.0.0 — Settings becomes a place (current shipped: 1.17.0.0, pre-release)
**Cycle type:** Remediation. [`docs/spec.md`](spec.md) is the canonical technical artifact for this
cycle. **Archive it into `docs/superpowers/specs/` before the next Cart round overwrites it** —
v1.17's was nearly lost that way.
**Anchor:** every page holds what its name promises.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A (autonomous)
- **Git:** Commit after each item. Conventional commits. Branch `feat/settings-remediation` — already
  cut, already carries scope + PRD + spec.
- **Verification:** Yes — **C1 after item 4**, **C2 after item 10**. C1 is the structural gate: items
  1-4 change how every Settings page is grouped and add the first new controls in the corrected
  structure. C2 is the whole-surface eyes-on before docs.
- **Check-in cadence:** N/A (autonomous)
- **TDD:** strict on **item 2** (the reachability fence must go red on the four unreachable watchdog
  settings *before* item 3 makes it green — that ordering is the item's entire proof), **item 6**
  (persistence round-trip), and **item 8** (copy assertions). Items 1, 5, 7, 9, 10 are
  verify-by-running. Items 11 and 12 are audit + doc.

## Effort

**Total ≈ 7-9 hours.** No new dependencies, no Core contract change, no spike. Heaviest is **item 1**
(structural, touches all five pages) and **item 3** (four controls plus validation plus its status
plumbing). Flag item 1 for a split into 1a/1b if it passes 90 minutes.

## A note on test filters

`--filter "Foo*|Bar*"` **matches zero tests** — VSTest's grammar has no glob wildcards and the run
reports success having executed nothing. This checklist uses `FullyQualifiedName~` throughout. That
defect shipped in every Verify field of the v1.17 checklist and a checkpoint could have been signed
off on nothing having run. Do not "simplify" these back.

---

## Checklist

- [x] **1. A section is a heading, not another card** ⚠ largest item
  Spec ref: `spec.md > §3.1 The hierarchy answer is a style that already ships`
  What to build: adopt `SectionHeadingStyle`
  ([`ControlStyles.xaml:143-148`](../src/ROROROblox.App/Controls/ControlStyles.xaml#L143-L148)) as
  the level above the card across `PreferencesWindow.xaml`. It is already what the page hand-writes —
  13px SemiBold White — and its `Margin="0,18,0,6"` is the missing level. Headings stand **outside**
  the cards. The three hand-rolled headings at `:179-181`, `:251-252` and `:400-402` become the style;
  cards holding a single control collapse into the section they belong to rather than each wearing
  full chrome. Measured contents today: 9 cards at 1/1/2/1/2/2/**8**/2/3. **The register row's "10" does not
  reproduce** — the big Alerts card holds 8 focusable controls; 8 plus the idle card's 2 is the Alerts
  *page* total of 10, so the row appears to have attributed a page total to a card. Every other figure
  in the sequence reproduces exactly. Recorded per `CLAUDE.md`'s re-measure rule.
  **Do NOT add a second container primitive** — no `SubCardBorderStyle`, no `GroupBox`, no `Expander`.
  A second container is the two-meanings defect restated, which is the row itself.
  Acceptance: `prd.md > Story 2.1` + `2.2`. A card holding one control and a card holding ten no
  longer carry identical weight. The distinction survives with colour removed. **Note the framing here was wrong and is
  corrected:** flatline is not the weak case for grouping-by-fill, it is the strongest — RowBg on Bg
  measures 1.09 brand, 1.08 midnight, 1.08 magenta-heat, **1.33 flatline**, and all four are under
  3:1. Fill was thin everywhere. Check all four, and check that the hierarchy rests on weight and
  rhythm rather than on any fill. Worst-case linear focus run does not grow past 12. The rail's group-to-group movement
  is confirmed, not rebuilt.
  Verify: `dotnet build ROROROblox.slnx`, then open Settings and walk all five pages in **all four
  themes**. Tab through one page start to finish and count the stops.
  Commit: `refactor(settings): a section is a heading, not another card`.

- [ ] **2. The fence that makes an unreachable setting fail the build**
  Spec ref: `spec.md > §4.2 The check that makes this class of defect fail a build`
  What to build: new `src/ROROROblox.Tests/SettingsReachabilityTests.cs`. Every property on the
  settings record is either referenced by name in App XAML or code-behind, or appears in a named
  exemption list **carrying its reason inline**. Same shape and same rule as the XAML literal fence
  v1.17 shipped: an exemption names why, so an unreachable setting is a decision rather than an
  oversight. Reuse the repo-root discovery the existing scanners use; **no hardcoded absolute
  paths** — a pre-commit hook blocks them and CI scans the full tree.
  **TDD, and the ordering is the proof:** write it now, watch it go **red naming
  `MemoryWatchdogEnabled`, `MemoryReserveMb`, `MemoryCapMb`, `ProjectionWarnMinutes`** — the four
  settings persisted at [`AppSettings.cs:322-392`](../src/ROROROblox.Core/AppSettings.cs#L322-L392)
  with zero `.xaml` references. Item 3 turns it green. A fence written after the fix proves nothing.
  Acceptance: `prd.md > What we'd add`. The fence goes red on exactly those four and green after item
  3. A fifth persisted setting added later with no UI fails the build.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~SettingsReachability"` — quote the
  red output. Commit: `test(settings): fence every persisted setting to a reachable control`.

- [ ] **3. The memory watchdog gets the page it is already named for**
  Spec ref: `spec.md > §4.1 The four watchdog settings`
  What to build: all four watchdog settings become editable on the **Alerts & memory** page
  ([`:80`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L80)), as a section per item 1,
  beside the alert routing already there. One bool, three numeric. Round-trip through the existing
  `IAppSettings` accessors — **no second source of truth**.
  **Validation is visible, not silent.** An out-of-range value is refused or clamped *in the UI* with
  the reason shown, reported through item 7's status-line mechanism. A setter that clamps quietly is
  indistinguishable from one that worked, which is this cluster's defect one level down. This is the
  one acceptance criterion no register row asked for and the one most likely to be dropped as "not in
  the row."
  Acceptance: `prd.md > Story 1.1`. All four editable, persisting across restart. Out-of-range input
  refused visibly. Hand-editing `settings.json` still works and the UI reflects it on next open. Item
  2's fence goes green.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~SettingsReachability|FullyQualifiedName~AppSettings"`.
  Then set each value, restart, confirm it stuck; enter a negative megabyte figure and read the
  message. Commit: `feat(settings): memory watchdog controls on the Alerts and memory page`.

- [ ] **4. Careful mode gets a home**
  Spec ref: `spec.md > §3.2 Careful mode goes on Startup`
  What to build: **a third ROW inside Startup's card**, not a third card — item 1 collapsed that
  page's two single-control cards into one card with two rows, so the shape this item was written
  against no longer exists. Match the two rows above it exactly: SemiBold 13px `CheckBox` label plus
  a muted 11px hint at `Margin="22,4,0,0"`, with `Margin="0,14,0,0"` on the row panel per the
  call-site margin rule item 1 followed. Intent unchanged, shape corrected after item 1 landed. Binds
  `IAppSettings.CarefulSquadLaunch`, the same value
  [`SquadLaunchWindow.xaml.cs:59,73,79`](../src/ROROROblox.App/SquadLaunchWindow.xaml.cs#L59) reads
  and writes. **The in-modal toggle stays** — this is a mirror, not a move. Both surfaces re-read on
  open; neither caches. **Do not rename the nav item**: `ui-routes.json` declares these pages as
  capture surfaces and a label change churns the capture round.
  Acceptance: `prd.md > Story 1.2`. Reachable from Settings, same persisted value, both surfaces in
  sync on next open.
  Verify: toggle in Settings, open Squad Launch, confirm it agrees; toggle there, reopen Settings,
  confirm again. **Checkpoint C1** — items 1-4 changed how every page groups and added the first
  controls into that structure. Walk all five pages in all four themes before item 5 builds on it.
  Commit: `feat(settings): careful squad launch mirrored into Startup`.

- [ ] **5. Alerts admits what it owns, and the theme prompt is reversible**
  Spec ref: `spec.md > §4.3 Muted accounts and the theme re-ask`
  What to build: two small additions reading existing state. **(a)** the Alerts section shows a muted-
  account count plus an unmute-all affordance, both reading the set the view-model already
  materialises at [`App.xaml.cs:1376`](../src/ROROROblox.App/App.xaml.cs#L1376) — no new persistence.
  Zero muted renders as absence, not a stray `0`. **(b)** a re-ask affordance on Appearance calling
  the existing theme-consent setter, discoverable without knowing the prompt ever happened.
  Acceptance: `prd.md > Story 1.3` + `1.4`.
  Verify: mute two accounts from the row context menu, open Settings, confirm the count; unmute-all
  and confirm both the count and the rows. Then decline the theme prompt and find your way back
  without editing JSON. Commit: `feat(settings): muted-account count, unmute all, and a theme re-ask`.

- [ ] **6. Compact mode survives a restart**
  Spec ref: `spec.md > §7 Persistence`
  What to build: `CompactMode` joins the settings record;
  [`MainViewModel.cs:695-709`](../src/ROROROblox.App/ViewModels/MainViewModel.cs#L695-L709) persists
  on set — it is a plain in-memory `SetField` today with nothing writing it to disk — and `MainWindow`
  restores it on load. **No migration:** the record's defaulted fields load cleanly on an existing
  file, which `Core/AppSettings.cs:463-465` already documents.
  Acceptance: `prd.md > Story 5.1`. Toggle compact, restart, still compact. An existing
  `settings.json` with no `CompactMode` field loads without error and defaults off.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~AppSettings|FullyQualifiedName~MainViewModel"`.
  Then toggle, quit **from the tray** (X minimises), relaunch. Commit: `feat(settings): compact mode persists across restart`.

- [ ] **7. Failure says so, in the app's warning voice**
  Spec ref: `spec.md > §5 Failure says so, using the pattern already on the page`
  What to build: a `ThemeStatusLine` sibling to `AlertsStatusLine`
  ([`:357-358`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L357-L358)) in the Theme
  section. **One deliberate divergence from the pattern being mirrored:** a failure message takes
  `RowExpiredAccentBrush` and the `▲` prefix, **not** `CyanBrush`. Cyan is the accent — the same
  treatment a success would get — and v1.17 established `RowExpiredAccentBrush` + `▲` as this app's
  warning vocabulary across expired rows, idle chips, memory chips and the compat banner. Leave
  `AlertsStatusLine` alone; retro-fitting it is not this cycle's row.
  Covers both Epic 3 stories: a failed theme persist (theme **still applies live** — the session is
  not degraded) and an unreadable theme file, **named**. Also correct the "Open themes folder" tooltip
  to say reopen this page rather than promising a restart the code does not require. Success stays
  silent; a status line that speaks on every save is noise.
  Acceptance: `prd.md > Story 3.1` + `3.2`. A folder with one bad file among good ones still loads the
  good ones.
  Verify: make the settings write fail (deny write on `settings.json`), pick a theme, confirm it
  applies and says so, restart and confirm the message was true. Drop a malformed `.json` in the
  themes folder and read the report. Commit: `feat(settings): report theme persist and load failures`.

- [ ] **8. One voice**
  Spec ref: `spec.md > §6 Voice and weight` (copy half)
  What to build: the six settings across the run-on-login and Discord cards go to **second person
  with terminal periods**. The run-on-login hint currently reads *"Adds a value under HKCU Run.
  Removes it when unchecked."* — a registry path presented as an effect, to an audience that does not
  have one, whose first clause also restates the checkbox above it. Replace with the effect and the
  reassurance the label does not already give. **No first person anywhere** — one setting speaks as
  "I" today. Clan-facing register per `CLAUDE.md`: no jargon, no "seamlessly", em-dashes minimal.
  New `src/ROROROblox.Tests/PreferencesCopyTests.cs` asserts it: second person, terminal periods, no
  first-person pronouns, no line duplicating the label above it.
  Acceptance: `prd.md > Story 4.1`.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~PreferencesCopy"`. Then read all
  five pages aloud — a voice change is heard, not seen.
  Commit: `docs(settings): one voice across every setting on the page`.

- [ ] **9. The loudest control does something**
  Spec ref: `spec.md > §6 Voice and weight` (Close-button half)
  What to build: sweep the nine Close buttons to the secondary treatment. Measured today: **5
  accent-filled, 4 secondary** — half the app already does this, so it is a decide-and-sweep, not a
  fix-one-window. Diagnostics is the existing precedent and does not change. Each window's own primary
  action keeps its fill.
  Acceptance: `prd.md > Story 4.2`. No window's loudest control is its dismissal.
  Verify: open all nine dialogs and confirm the filled control is the one the window exists to
  perform. Commit: `fix(ui): dismissal takes the secondary treatment everywhere`.

- [ ] **10. Destructive actions look destructive** ⚠ boundary item
  Spec ref: `spec.md > §6` (destructive variant) + `§9.6`
  What to build: add `DestructiveButtonStyle` to `ControlStyles.xaml` — there are two ranks and no
  destructive one — and **assign it to exactly these three sites, by enumeration, not by sweep**:
  (a) **Remove**, on the account row — destroys a saved account and its cookie.
  (b) **Clear**, in History — destroys the session log.
  (c) **Stop all Roblox instances** — halts every running client at once.
  **A site not on that list is F-068's, not this cycle's.** If applying the variant starts requiring a
  judgement call about a fourth button, that is the signal `prd.md > Story 4.3` named: **drop this
  item** rather than drag F-068's 61 flat call sites forward. Say so in the commit if it happens.
  Acceptance: `prd.md > Story 4.3`. The variant exists, three sites carry it, and the count of
  migrated button call sites has not moved beyond those three.
  Verify: `dotnet test ROROROblox.slnx`, full suite. Then look at the three sites in all four themes.
  **Checkpoint C2** — the whole surface is done. Walk every page and every dialog, all four themes,
  and re-run the §8 manual list from `spec.md`.
  Commit: `feat(ui): a destructive button rank, assigned by enumeration`.

- [ ] **11. Flip all thirteen rows in the same PR**
  Spec ref: `CLAUDE.md > Findings register` + `spec.md > §10 Open issues`
  What to build: per this repo's rule, **a PR that closes a register row flips that row in the same
  PR**. Flip F-019, F-020, F-023, F-024, F-026, F-033, F-037, F-043, F-046, F-051, F-053, F-062,
  F-078 with evidence naming the artifact that closed each. Re-derive the status totals from the
  cells; expect **51 open → 38**. Record any count that moved with its direction.
  **F-050 stays `open`** — flipping it auto-deletes the contrast gate's exemption and reddens brand
  3.79:1, midnight 4.16:1 and magenta-heat 3.29:1 against a 4.5 threshold. Verify its cell is
  byte-identical when done. **F-052, F-068 and F-091 stay open and un-nibbled.**
  Acceptance: `prd.md > Non-goals`. Every flipped row verified **against the tree**, not against this
  checklist.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~ContrastPairGate"` — the exemption
  parse still finds F-050 `open`. Commit: `docs(register): close the thirteen Settings rows`.

- [ ] **12. Documentation & security verification**
  Spec ref: `spec.md > §2` + `§10 Open issues` + `CLAUDE.md > What NOT to do`
  What to build: version to **1.18.0.0** in lockstep — `ROROROblox.App.csproj` `<Version>` and
  `Package.appxmanifest` `Identity Version`. No new capabilities: `runFullTrust` only. Add the v1.18
  row to [`docs/features.md`](features.md) — the ledger silently skipped v1.16 entirely and was caught
  a release late, so check the previous row exists too. Clan-facing release notes. Carry §10's open
  issues forward: F-050, F-052, F-068, F-091, and `AlertsStatusLine`'s `CyanBrush`.
  **Archive `docs/spec.md` into `docs/superpowers/specs/` as this cycle's canonical artifact** before
  anything overwrites it.
  Security pass: secrets scan, local-path grep for `c:\Users\` in committable code,
  `dotnet list ROROROblox.slnx package --vulnerable`. Log the cycle's decisions to the dashboard.
  Acceptance: version lockstep in both files. `features.md`, `spec.md`, `checklist.md` and the register
  all describe the app that exists. No secrets, no local paths, deps clean or documented. Branch
  PR-ready to `main`.
  Verify: pre-commit hooks pass; `dotnet build ROROROblox.slnx` clean; `dotnet test ROROROblox.slnx`
  full suite green; `git diff main --stat` reviewed. Commit: `docs: v1.18.0 settings remediation docs sync + security verification`.
