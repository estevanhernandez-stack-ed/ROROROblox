# RORORO — v1.17.0 Flatline Build Checklist

**Cycle:** v1.17.0.0 — flatline, the readable theme (current shipped: 1.16.0.0)
**Cycle type:** Cart-authored spec. [`docs/spec.md`](spec.md) is the canonical technical artifact for
this cycle, not a pointer-stub — Este's ruling this session routes design through Cart. Prior cycles'
canonical specs are indexed in that file's appendix and are not superseded.
**Anchor:** carry distinction without colour.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A (autonomous)
- **Git:** Commit after each item. Conventional commits. Branch `feat/flatline-theme` — already cut,
  already carries scope + PRD + spec.
- **Verification:** Yes — **C1 after item 3**, **C2 after item 7**. C1 exists because item 3 changes
  the *default* theme, not just flatline. C2 is the evidence gate: the full [`spec.md > §11.3`](spec.md)
  manual smoke plus eyes on 56 PNGs.
- **TDD:** strict on items 1 and 5 (both are pure measurement against `ContrastGuard`). Item 3's fence
  is written first and must go red while the converters still exist — that is the assertion. Item 4's
  INPC wiring gets a test. Items 2 and 7 are verify-by-running. Items 6 and 8 are audit + doc.

## Effort

**Total ≈ 6-7 hours.** No new dependencies, no contract change, no spike — the palette hexes were
already proven by measurement at `/spec` time (`spec.md > §11.1`). Heaviest by a distance is **item 3**
(four binding sites plus four collateral files); flag for a split into 3a/3b if it passes 90 minutes.

---

## Checklist

- [x] **1. Flatline as a fourth built-in**
  Spec ref: `spec.md > §4.1 The palette` + `§4.2 Two design rules` + `§4.3 Measured result` + `§4.4 Selection raises nothing`
  What to build: append the fourth `Theme` record to `ThemeStore.BuildBuiltIns()` in
  `src/ROROROblox.Core/Theming/ThemeStore.cs`, `Id: "flatline"`, `IsBuiltIn: true`, hexes exactly as
  §4.1 states them. Nothing else changes — no `Theme` contract growth, no loader change, no
  `EdgeRemediation` change, no picker wiring. Extend `ThemeStoreTests`: the three existing
  `Assert.Contains(list, t => t.Id == …)` facts at [`:30-32`](../src/ROROROblox.Tests/ThemeStoreTests.cs#L30-L32)
  gain a `flatline` sibling, and the id-collision fact at
  [`:122`](../src/ROROROblox.Tests/ThemeStoreTests.cs#L122) gets a `flatline.json` twin so a user file
  cannot displace the built-in.
  Acceptance: `prd.md > Story 1.1` + `1.2` + `1.4`. Four entries in Settings → Appearance → Theme.
  `ContrastPairGateTests.EveryDeclaredPairClearsAaUnderEveryTheme` enrols flatline automatically and
  every declared pair clears 4.5:1 with **zero new exemptions** — white on the dark accent measures
  4.68:1, above AA outright, so the F-050 exemption is not load-bearing here. Selecting it raises no
  prompt, no modal, no warning. Theme appears with the themes folder empty, missing or unreadable.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~ThemeStore|FullyQualifiedName~ContrastPairGate|FullyQualifiedName~EdgeRemediation"`.
  Then run the app and do §11.3 items 1-5 by hand — pick it (no prompt), watch the main window repaint
  with no restart, restart and confirm it stuck, **drop a hand-written `flatline.json` into
  `%LOCALAPPDATA%\ROROROblox\themes\` and confirm the built-in wins**, delete the folder entirely and
  confirm flatline still appears. Reading `ThemeStore.cs:71-76` is not verification.
  Commit: `feat(theme): flatline as a fourth built-in`.

- [x] **2. The description line under the picker**
  Spec ref: `spec.md > §4.5 The description sentence` + `§12 Data model`
  What to build: new `src/ROROROblox.App/Theming/ThemeDescriptions.cs` — `internal static class` with
  `public static string? For(string id)`, a `ToLowerInvariant()` switch keyed by theme id, returning
  null for anything unknown. **App layer only. Not an eleventh `Theme` slot** — the contract stays at
  ten required fields so every user JSON on disk stays valid untouched, which is the invariant
  [`ContrastGuard.cs:15-23`](../src/ROROROblox.Core/Theming/ContrastGuard.cs#L15-L23) argues in the
  codebase's own voice. Render as a wrapping `TextBlock` directly below the picker in `PageAppearance`
  ([`PreferencesWindow.xaml:408-412`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L408-L412)),
  `MutedTextBrush`, 11px, `Visibility` collapsed when `For(id)` returns null; `OnThemeChanged` in the
  code-behind sets it. **All four built-ins get a sentence** — three blank lines out of four reads as a
  bug. Flatline's draft copy is in §4.5; polish it here, second person, clan-facing register, no
  "WCAG", no "contrast ratio", no "CVD".
  Acceptance: `prd.md > Story 1.3`. Choosing or focusing Flatline surfaces one plain sentence saying it
  stays readable without relying on colour. The sentence is reachable in the app — a README paragraph
  does not satisfy this. A user theme shows no line and no empty gap. No tooltip anywhere: hover is
  neither choosing nor focusing, and a tooltip risks the `Id = <id>,` substring `capture-ui.ps1` reads
  out of the picker item's UIA name, which item 7 depends on.
  Verify: `dotnet build ROROROblox.slnx`, open Settings → Appearance, arrow through all four themes and
  read each line, then select a user theme and confirm the line collapses cleanly rather than leaving a
  hole. Commit: `feat(theme): one-sentence description under the theme picker`.

- [x] **3. Status colour comes from the theme** ⚠ largest item
  Spec ref: `spec.md > §5.1 The defect` + `§5.2 Decision` + `§5.3 Status-to-slot mapping` + `§5.4 Is the new path measured?`
  What to build: **delete** `StatusDotBrushConverter` and `IdleChipBrushConverter` from
  [`Converters.cs:169-218`](../src/ROROROblox.App/Converters.cs#L169-L218) and replace every binding
  with a `Style` + `DataTrigger` setting `{DynamicResource}`. **Not** "teach the converter to read
  `Application.Current.Resources`" — `IValueConverter.Convert` re-runs on binding-source change, not on
  resource-dictionary change, and `ApplySlot` *replaces* the brush instance rather than mutating it, so
  a converter-fetched brush is stale the moment the theme changes. It would pass review and fail
  Story 1.1's live repaint. The idiom already ships twice in this same file, at
  [`:213-231`](../src/ROROROblox.App/MainWindow.xaml#L213-L231) and
  [`:394-407`](../src/ROROROblox.App/MainWindow.xaml#L394-L407).
  Mapping per §5.3, every slot already exists: `green`→`WhiteBrush`, `yellow`→`RowExpiredAccentBrush`,
  `magenta`→`MagentaBrush`, `grey`→`MutedTextBrush`; chips take `RowExpiredAccentBrush` when warning and
  `MutedTextBrush` otherwise. **Four binding sites, not three** — [`:388`](../src/ROROROblox.App/MainWindow.xaml#L388)
  status dot, [`:419`](../src/ROROROblox.App/MainWindow.xaml#L419) idle chip,
  [`:433`](../src/ROROROblox.App/MainWindow.xaml#L433) row memory chip, and
  [`:78`](../src/ROROROblox.App/MainWindow.xaml#L78) the **compact-mode** row's memory chip, which the
  spec's first draft missed and which the capture round is least likely to have open. Four collateral
  files land in the same commit or the build breaks: `App.xaml:23-24` resource declarations, the whole
  of `ConvertersTests.cs`, the `<see cref="IdleChipBrushConverter"/>` at `AccountSummary.cs:268`, and the
  stale comment at `MainWindow.xaml:425-426`.
  New fence `src/ROROROblox.Tests/ThemedStatusColourTests.cs`: (a) neither converter type exists in the
  App assembly — deletion is the assertion, it cannot be half-done; (b) no `Color.FromRgb` or literal
  `#RRGGBB` `SolidColorBrush` is constructed in App code outside a named allow-list, that list being
  exactly §7's out-of-scope set — `Converters.cs`'s caption `AutoPalette` and `RobloxWindowDecorator`'s
  per-account palette — each entry carrying its reason inline.
  Acceptance: `prd.md > Story 3.1`. Switching to Flatline leaves no brand hue on the main window, in
  standard **and** compact row modes. All four dot states stay at distinct values (13.17:1 / 9.68:1 /
  4.98:1 / 2.81:1 against the row). The fence goes red while a converter still exists and green after.
  **This is a fence, not a gate** — it proves the colours come from the theme, not that they are
  legible; `ContrastPairGateTests` structurally cannot see a `DataTrigger` setter, before or after.
  Do not describe it as gated.
  Verify: write the fence first and watch it fail. Then `dotnet test ROROROblox.slnx`, full suite green.
  Then run the app and look at **brand**, not just flatline — §5.3 changes the default theme's active
  dot from green `#4FE08C` to white. **Checkpoint C1**: that is a visible change to the product's
  identity theme and it wants a human yes before items 4-7 build on top of it.
  Commit: `fix(theme): status and chip colours resolve from the active theme`.

- [x] **3a. The fifth status-colour site** *(added at `/build`, after item 6 found it)*
  Spec ref: `spec.md > §5.1 The defect` + `§5.2 Decision` + `§5.3 Status-to-slot mapping` — extends item 3
  Why it exists: item 6's register pass found `MainWindow.xaml:1877` + `:1884` — the status bar's
  live-process dot, a literal `<SolidColorBrush Color="#4FE08C" />` in a `Setter.Value`, swapping to
  `#4A5C70` at zero clients. The same brand green and grey the deleted `StatusDotBrushConverter` held.
  Shipped with F-080 in PR #96, present on `main`, so not a regression from this cycle. §5.3 enumerated
  four status-colour sites; the app has five. **Both of item 3's fences are structurally blind to it** —
  `ThemedStatusColourTests` walks `*.cs`, `ContrastPairGateTests` reads `{DynamicResource}` attributes,
  and a raw hex in a `Setter.Value` matches neither.
  What to build: the two literals resolve from the theme, same `Style` + `DataTrigger` + `{DynamicResource}`
  idiom item 3 used, same mapping — live → `WhiteBrush`, zero → `MutedTextBrush`. Then extend
  `ThemedStatusColourTests` with a third fact scanning App **XAML** for literal `#RRGGBB` outside an
  allow-list, where each allow-list entry cites the open register row that owns it. A literal is
  permitted only when a finding already tracks it; that is what stops a sixth site hiding the same way.
  Close **F-088** in the register in the same commit, per `CLAUDE.md`'s same-PR rule.
  Acceptance: `prd.md > Story 3.1` becomes literally true — switching to Flatline leaves no brand hue
  on the main window, status bar included. The new fact goes red while a literal is unowned and green
  after. F-085, F-066 and `AboutWindow`'s logo hexes stay out of scope, allow-listed by finding id.
  Verify: write the fact first and watch it fail. Then `dotnet test ROROROblox.slnx`, full suite green.
  Commit: `fix(theme): status bar dot resolves from the active theme`.

- [x] **3b. The selection dot** *(added at `/build`, after item 3a's own scan found it)*
  Spec ref: `spec.md > §5.2 Decision` + `§5.3 Status-to-slot mapping` — extends item 3a
  Why it exists: item 3a's XAML fence enumerated all 101 colour literals in App markup and attributed
  every one. That scan turned up `App.xaml:53-94`, `SelectionDotStyle`'s `ControlTemplate`: ring
  `#4A5C70`, checked fill and checked stroke `#17D4FA`, hover stroke `#9AA8B8`. Used once, at
  [`MainWindow.xaml:531`](../src/ROROROblox.App/MainWindow.xaml#L531) — the account row's batch-selection
  toggle, so it ships on **every row**, and the ring is drawn whether or not the row is selected. Under
  flatline it renders brand cyan on an achromatic field. Owned by no row until item 3a opened **F-089**.
  This does **not** contradict `§6.4`, which recorded the toggle clean for *redundancy* — it carries its
  state in shape, filled versus hollow, and that is untouched here. Colour-only was never the claim; a
  brand hue the theme cannot reach is a separate defect.
  What to build: four attribute values bound to slots that already exist — ring → `MutedTextBrush`,
  checked fill and checked stroke → `CyanBrush`, hover stroke → `MutedTextBrush`. No shape change, no
  trigger change, no new slot. Then **retire F-089's allow-list entry** from `ThemedStatusColourTests`
  and drop the ceiling from 101 to 97: an allow-list entry that outlives its finding is precisely the
  defect `NoExemptionOutlivesItsFinding` exists to catch, and leaving it would let a future literal
  inherit a closed row's permission. Close **F-089** in the same commit.
  Acceptance: `prd.md > Story 3.1` is literally true for the main window's steady state — no brand hue
  under flatline on any always-visible surface. F-085 (Bloxstrap banner) and F-066 (mutex-recovery
  banner) stay open by decision: both are conditional surfaces, and both belong to F-068's shared
  banner/button recipe rather than to a theme cycle.
  Verify: retire the allow-list entry first and watch the fence go red on the four hexes — that is the
  assertion. Then `dotnet test ROROROblox.slnx`, full suite green.
  Commit: `fix(theme): selection dot resolves from the active theme`.

- [x] **4. Non-colour redundancy, for every theme**
  Spec ref: `spec.md > §6.1 Account state` + `§6.2 Expired sessions` + `§6.3 Warning chips` + `§6.4 Verified clean`
  What to build: three devices, all of them extensions of something the app already does.
  (a) **Expired row gains a 3px left rule** bound to `RowExpiredAccentBrush` at
  [`:217-218`](../src/ROROROblox.App/MainWindow.xaml#L217-L218). The rule is the device because
  Preferences' nav rail already carries selection on a 3px bar plus weight rather than a fill, which is
  F-002's shipped fix — one vocabulary, not a second one invented here.
  (b) **`AccountSummary.IdleText` prefixes `"▲ "` when `IdleWarn` is true**
  ([`:288-297`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L288-L297)), extending the glyph the
  memory chip already ships. **The wiring detail that will bite:** `IdleText` is recomputed on
  `SinceActivity` change ([`:260`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L260)) but *not*
  on `IdleWarn` change — the `IdleWarn` setter must also raise `OnPropertyChanged(nameof(IdleText))`
  or the glyph lands a tick late and looks fine on a slow-moving row.
  (c) **Compat banner takes the same `▲` prefix**
  ([`:1516-1522`](../src/ROROROblox.App/MainWindow.xaml#L1516-L1522)), one warning vocabulary across
  the window.
  Do **not** touch `SecondaryStatusText`, the selection toggle, the MAIN pill or `InteractiveEdgeBrush`
  — §6.1 and §6.4 verified all four already carry their state in words or shape. Ornament for its own
  sake is a regression in a theme whose whole argument is legibility. And **no `if (theme == flatline)`
  anywhere**: numbered non-goal 6, ships for all four themes, and it is the constraint most likely to
  break under build pressure.
  Acceptance: `prd.md > Story 2.1` + `2.2` + `2.3` + `2.4`. An expired row is identifiable with
  `RowExpiredBg` flattened to `RowBg` at test time. The idle chip's warn state is distinguishable from
  its ordinary state with colour removed. A test asserts `IdleWarn` flipping raises `IdleText`. `▲` is
  Segoe UI geometric, `Emoji_Presentation=No` — it does not trip the register's invariant-5 emoji rule.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~AccountSummary|FullyQualifiedName~MemoryChipFormatter|FullyQualifiedName~XamlStyleIntegrity|FullyQualifiedName~ExpiredRowRedundancy"`,
  then run the app in all four themes and confirm the rule and the glyphs read the same in brand as in
  flatline. Cover the status dot on a screenshot with all four states present — still readable.
  Commit: `feat(ui): non-colour redundancy for expired rows and warning chips`.

- [x] **5. flatline-lab and the assertion that fails on purpose**
  Spec ref: `spec.md > §8.1 Why it exists` + `§8.2 The fixture` + `§8.3 It reproduces the register's numbers` + `§8.4 The failing assertion` + `§8.5 One-line change`
  What to build: `static readonly Theme FlatlineLab` **in the test project only**, hexes per §8.2. Not
  in `BuildBuiltIns()`, not in the picker, never written to the user themes folder, not selectable by
  any path a user can reach. New `src/ROROROblox.Tests/FlatlineLabGateTests.cs` resolves it through
  `ThemeService.ApplyTo` exactly as `ContrastPairGateTests.ResolveTheme` does
  ([`:115-145`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L115-L145)) and asserts the AA
  measurement **fails**, with the failure attributed: parse-health first (every resolved slot returns a
  non-null ratio) so "fails for the stated reason" is separable from "fails because the theme is
  broken", then named-pair assertions to two decimals — `WhiteBrush` on `MagentaBrush` at 2.99:1 below
  the exemption's own 3.20 floor, `NavyBrush` on `CyanBrush` at 4.34:1 below 4.5. A bare failure count
  proves nothing. Assert the recorded register ratios directly so the fixture cannot drift from the
  numbers it exists to preserve. Plus the one-liner in `BuiltInThemes()`'s guard message
  ([`:163-165`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L163-L165)): it says four and names
  flatline.
  Acceptance: `prd.md > Story 4.1`. The fixture reproduces F-031's 1.00:1 and 4.34:1, F-032's 1.00:1,
  F-050's 2.99:1 and F-002's 1.00:1. It puts 4 pairs below AA and drops the exempted pair below its
  floor, so it trips **both** branches of `EveryDeclaredPairClearsAaUnderEveryTheme` including
  `EXEMPTED PAIR GOT WORSE`. The gate can go red — that is the deliverable, and it is what makes a
  green run mean something.
  Verify: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~FlatlineLabGate|FullyQualifiedName~ContrastPairGate"`. Sanity check the
  reconstruction: the fixture's `WhiteBrush` vs `NavyBrush` should measure 12.98:1, which is F-031's
  4.34 × F-050's 2.99 — two ratios recorded months apart in separate findings, mutually consistent.
  If that number is off, the fixture is wrong, not the register.
  Commit: `test(theme): flatline-lab fixture proves the contrast gate can fail`.

- [x] **6. Reconcile every number that goes false on merge**
  Spec ref: `spec.md > §10.1 Register rows` + `§10.2 In-code claims` + `§10.3 A third stale claim`
  What to build: per `CLAUDE.md`'s findings-register rule, every row flips in the same PR that ships
  the change. In `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`: **F-031**
  and **F-032** and **F-002** each state which artifact reproduces which number — `flatline-lab` owns
  the adversarial ratios, shipped flatline measures 1.51:1 / 12.84:1, 2.65:1 and 1.33:1 respectively,
  all better than brand's. **F-050 stays `open`** — this cycle ships a theme that does not need the
  exemption, it does not implement F-050's fix direction; flipping that row auto-deletes the gate's
  exemption via `NoExemptionOutlivesItsFinding` and turns brand (3.79:1), midnight (4.16:1) and
  magenta-heat (3.29:1) red — **all three** pre-flatline built-ins, not two. (Corrected 2026-08-10:
  this said two and omitted midnight, which measures 4.16:1 against an `AaThreshold` of 4.5.)
  Fix the **citation** at [`:285`](superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md#L285):
  the fixture definition is at `docs/ui-capture-checklist.md:21-23`, not `:8-9` (which is the nav-rail
  correction). Two **new rows**: the Bloxstrap banner's un-themed literals `#3F3000` / `#8F7000`
  ([`:1528-1532`](../src/ROROROblox.App/MainWindow.xaml#L1528-L1532), fix belongs with F-068), and the
  gate's `MutedTextBrush` blind spot per §10.3.
  In code: rewrite `ContrastPairGateTests`' class doc
  ([`:36-45`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L36-L45)) — every clause about flatline
  "not a shipped theme" is false on merge. Correct `MutedTextFenceTests`' doc
  ([`:10-13`](../src/ROROROblox.Tests/MutedTextFenceTests.cs#L10-L13)): the 1.00:1 belongs to
  `flatline-lab`, shipped flatline measures 2.65:1. Correct the **"9 distinct pairs"** claim in three
  places ([`:52`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L52),
  [`:181`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L181),
  [`:193`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L193)) — the app ships 8 since PR #100
  merged a pair.
  Acceptance: `prd.md > Story 5.1` + `5.2`. Every updated row verified **against the tree**, not against
  a changelog — "wave N said it closed this" is not evidence. F-050 still reads `open`. No behaviour
  change in any test, only prose.
  Verify: `dotnet test ROROROblox.slnx`, full suite green and the count unchanged from item 5 — this
  item asserts nothing new. Re-read each edited row against the file it cites.
  Commit: `docs(register): reconcile flatline ratios against the artifacts that produce them`.

- [x] **7. The fourth capture round** *(script half done; the eyes-on half is C2, still owed)*
  Spec ref: `spec.md > §9 The fourth capture round` + `§11.3 Manual, and non-negotiable`
  What to build: nothing. **No edit to `scripts/capture-ui.ps1`** — `Get-AvailableThemes` enumerates the
  live picker and matches on the `Id = <id>,` substring
  ([`:635-664`](../scripts/capture-ui.ps1#L635-L664)), and the expected-count guard multiplies by the
  theme count it found ([`:977`](../scripts/capture-ui.ps1#L977)). Both numbers are derived. If this
  item needs a script edit, item 2 broke the UIA name and that is the bug to fix. Run the round, land
  `run-flatline.json` beside the three existing manifests in `docs/ui-evidence/` (gitignored), and
  **look at the PNGs**.
  Acceptance: `prd.md > Story 6.1`. 56 captures against today's 42 — each existing manifest records 14
  surfaces / 14 ok, so a fourth round of 14 is the expected shape. `run-flatline.json` present. The
  round is the evidence items 3 and 4 are signed off against; a green test run is not the acceptance
  criterion, a human looking at the pixels is.
  Verify: `powershell -ExecutionPolicy Bypass -File scripts/capture-ui.ps1`. **Checkpoint C2** — walk
  the full §11.3 list: no prompt on selection, live repaint, restart persistence, user-JSON collision,
  deleted themes folder, cover-the-dot readability, **the brand captures** (item 3 changed the default
  theme), and the 56-count. **Do not capture `preferences-alerts` with live webhook URLs in the
  fields** — a Discord webhook URL is a bearer credential (F-076); the script refuses mechanically but
  UIA text is not rendered pixels, so the manual step stands.
  Commit: `docs(evidence): fourth capture round under flatline`.

- [x] **8. Documentation & Security Verification**
  Spec ref: `spec.md > §2 Runtime, deployment, identity and signing` + `§16 Open issues` + `prd.md > What we're building` + `CLAUDE.md > What NOT to do`
  What to build: version to **1.17.0.0** in lockstep — `src/ROROROblox.App/ROROROblox.App.csproj`
  `<Version>` and `Package.appxmanifest` `Identity Version`, both currently 1.16.0.0. No new
  capabilities: `runFullTrust` only. Update [`docs/features.md`](features.md) — the canonical feature
  ledger, updated on every release tag — with the v1.17 row. Clan-facing release notes plus the Store
  "What's new" copy; an accessibility theme is a listing asset, so lead with it rather than
  footnoting it. Carry §16's open issues forward so they are not silently dropped: the gate cannot see
  `MutedTextBrush` since PR #100, the gate cannot see style-resolved brushes so item 3 is fenced rather
  than gated, the Bloxstrap literals, and `ui-routes.json` declaring 18 surfaces against 14 captured.
  Security pass: secrets scan (no `.ROBLOSECURITY`, no `dev-cert.pfx`, no `accounts.dat`, no
  `*.rororo-accounts`, no live webhook URL in any committed capture or doc); local-path grep for
  `c:\Users\` in committable code; `dotnet list ROROROblox.slnx package --vulnerable`. Log the cycle's
  decisions to the dashboard: flatline is an achromatic ramp not a flat surface, two accent lightnesses
  disproved by enumeration, converters deleted rather than taught to resolve, active mapped to
  `WhiteBrush` at the cost of brand's green dot, description as App-layer lookup not an eleventh slot,
  F-050 deliberately left open.
  Acceptance: version lockstep confirmed in both files. `docs/features.md`, `docs/spec.md`,
  `docs/checklist.md` and the register all describe the app that exists. No secrets, no local paths,
  deps clean or documented. Decisions logged. Branch PR-ready to `main`.
  Verify: pre-commit hooks pass; `dotnet build ROROROblox.slnx` clean; `dotnet test ROROROblox.slnx`
  full suite green; `git diff main --stat` reviewed for anything that should not ship.
  Commit: `docs: v1.17.0 flatline docs sync + security verification`.
