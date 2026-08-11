# RORORO — v1.20.0 Button Vocabulary Build Checklist

**Cycle:** v1.20.0.0 — one button vocabulary (current shipped: 1.19.0.0 on `main`, untagged)
**Cycle type:** Remediation + one new primitive.
[`docs/spec.md`](spec.md) is the canonical technical artifact. **Archive it into
`docs/superpowers/specs/` before the next Cart round** — item 10 owns that.
**Anchor:** a button should look like the theme in every state, not just at rest.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A
- **Git:** Commit after each item. Conventional commits. Branch `feat/button-vocabulary`, already
  cut and already carrying scope + PRD + spec, rebased onto merged `main`.
- **Verification:** Yes — **C1 after item 4**, **C2 after item 6**. C1 is the look-at-it gate: the
  template and the new ranks exist and `MainWindow` uses them, so a regression is visible before 21
  more files inherit it. C2 is the whole-surface eyes-on before the gates and docs.
- **TDD:** strict on **item 2** (the scanner is this cycle's measuring instrument and its baseline
  must be recorded before anything moves) and **item 8** (the state gate must be shown failing
  against a deliberately broken template). Items 1 and 3 are verify-by-render; 4-6 verify-by-eye;
  7 is a by-name assignment; 9-10 are audit.

## Effort

**Total ≈ 6-8 hours.** No new dependencies, no contract change, no spike. Heaviest is **item 4**
(30 sites in the app's most-looked-at file, spanning five intents) and **item 3** (two new ranks that
31 sites depend on). Item 4 is flagged for a 4a/4b split if it passes 90 minutes.

## The measurement this checklist is sized against

Run at the branch point with `spec.md > §6`'s definition, before anything moved:

**72 un-migrated sites across 22 files, out of 116 total button declarations.**

Neither 55 (the register) nor any earlier figure is adopted — both were measured under unknown
definitions, and one of them ("63 across 15 files") reproduces at no commit. Item 2 commits the
script so this number is reproducible and the closing figure is comparable to this one.

`MainWindow.xaml` holds **30 of the 72** — 42% of the debt in one file. The next-largest is 6.

## What the recon found that the spec did not

Bucketing the 72 by fill turned up **two intents the vocabulary has no rank for**, which is why item
3 exists and why it precedes every migration item:

| Intent | Sites | Rank today |
| --- | --- | --- |
| Cyan-filled CTA — `CyanBrush` ×17 plus raw `#17D4FA` ×6 | **23** | **none** |
| Magenta-filled action — `Stop`, `Squad Launch` | **8** | **none** |
| Raw `#22314A` — **in no palette slot at all** | **7** | **none, and unthemed** |
| Transparent / ghost | 5 | none |
| Navy / Bg / RowBg | 9 | Secondary ranks fit |
| No `Background` — inherits | 17 | likely fine |

`#17D4FA` **is** `CyanBrush`'s hex, so those six are the same intent written by hand.
`PrimaryButtonStyle` is a Navy fill with a cyan *border* — migrating a cyan-filled CTA onto it
**changes how it looks**, which `spec.md > §3` calls a regression. Without item 3, §3's "a site
needing a look no rank provides stops the item" rule fires on the first file and the cycle stalls at
item 4.

**`#22314A` is the sharper find.** It is not `Bg` (`#0F1F31`), not `RowBg` (`#15263A`), not `Divider`
(`#1F3149`). Seven buttons are painted a colour the theme system has never heard of, so they stay
navy-blue under flatline no matter what this cycle does to the template. That is F-068's actual
defect in its purest form.

## A note on test filters

`--filter "Foo*|Bar*"` **matches zero tests** — VSTest's grammar has no glob wildcards and the run
reports success having executed nothing. This checklist uses `FullyQualifiedName~` throughout.

---

## Checklist

- [x] **1. The template, and states that follow the theme**
  Spec ref: `spec.md > §2`, `spec.md > §0.2`
  What to build: `AppButtonTemplate` in `Controls/ControlStyles.xaml` — a `Border x:Name="Chrome"`
  wrapping a `ContentPresenter`, with `ControlTemplate.Triggers` for `IsMouseOver` → `RowBgBrush`,
  `IsPressed` → `DividerBrush`, `IsEnabled=False` → `Opacity 0.45` plus `MutedTextBrush` foreground.
  Every value a `{DynamicResource}`; **no colour literal anywhere in the new content.** All four
  existing ranks take `Template="{StaticResource AppButtonTemplate}"`.
  Acceptance (`prd.md > Story 1.1`): the four ranks look **unchanged at rest** in all four built-in
  themes; hover and pressed differ from rest and from each other; nothing in the new markup is a hex
  literal.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~Contrast|FullyQualifiedName~Render"`
  green — those gates measure resting appearance, so **them passing is the proof that rest did not
  move.** Then run the app and hover a button: the fill must be a theme colour, not `#BEE6FD`.

- [x] **2. The scanner, and the baseline it records**
  Spec ref: `spec.md > §6`
  What to build: `scripts/count-button-sites.ps1` implementing §6's definition verbatim, printing a
  total, an un-migrated count and a per-file breakdown. Record its **branch-point output** in the
  commit message and in the F-068 row.
  Acceptance (`prd.md > Story 3.1`): re-running it now prints **72 across 22 files**, matching the
  figure this checklist is sized against.
  Verify: run it; confirm 72/22. **Then hand-edit one file to add a styled button and re-run** — the
  count must drop by exactly one, then revert. A counter that cannot be moved on demand is not
  measuring anything, which is the defect this row has suffered from three times.

- [x] **3. The two missing ranks**
  Spec ref: `spec.md > §2`, and the recon table above
  What to build: `CtaButtonStyle` (cyan fill — the 23-site intent) and a decision on the
  magenta-filled 8. **Both go through the contrast gate before any site adopts them:** the
  foreground on `CyanBrush` and on `MagentaBrush` must clear the same floor the resting pairs are
  held to, and if it cannot, **the rank's foreground changes rather than the floor.** Also decide the
  `#22314A` seven — they are unthemed and cannot stay literal.
  Acceptance: each new rank has a measured foreground/fill ratio recorded in the commit; no rank
  ships whose own pair fails.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ContrastPairGate"` green
  with the new ranks included in whatever it enumerates. **If a rank cannot clear its floor, say so
  and change the rank** — this cycle does not get to lower a bar it inherited.

- [ ] **4. `MainWindow.xaml` — 30 sites**
  Spec ref: `spec.md > §3`
  What to build: migrate all 30 using the ranks from items 1 and 3. Five intents are present (cyan
  CTA, magenta, `RowExpiredAccent`, transparent, raw hex) — assign by intent, not by convenience.
  Acceptance (`prd.md > Story 2.1`): the scanner reports MainWindow at **0**; **nothing looks
  different at rest**; the two raw-hex sites (`#17D4FA`, `#22314A`) are gone.
  Verify: run the app in **flatline and brand**, compare against a pre-item screenshot, and hover
  several buttons. A visible change at rest is a regression to fix, not to accept. Split 4a/4b if
  this passes 90 minutes.
  → **CHECKPOINT C1.**

- [ ] **5. The top tail — 5 files, 20 sites**
  Spec ref: `spec.md > §3`
  What to build: `PluginsWindow` (6), `GamesWindow` (4), `RobloxAlreadyRunningWindow` (4),
  `LeftoverProcessesWindow` (3), `PreferencesWindow` (3). **`PluginsWindow`'s Remove belongs to item
  7** — leave it.
  Acceptance: the scanner shows those five at 0 except the one Remove; no resting change.
  Verify: open each window and compare. `LeftoverProcessesWindow` and `RobloxAlreadyRunningWindow`
  appear during the startup gate, so they are seen at the worst possible moment.

- [ ] **6. The long tail — 16 files, 22 sites**
  Spec ref: `spec.md > §3`
  What to build: the remaining files, worst-first. **A site needing a look no rank provides opens a
  row and stops the item** — it does not get hand-rolled and it does not grow the vocabulary
  mid-sweep.
  Acceptance: the scanner total reaches **0**, or the shortfall is named alongside the row that
  explains it.
  Verify: run the scanner; open the two or three most-used of these windows.
  → **CHECKPOINT C2.**

- [ ] **7. F-046 closes**
  Spec ref: `spec.md > §4`
  What to build: `PluginsWindow`'s Remove — a hand-rolled magenta fill and the row's headline
  evidence — takes `DestructiveButtonStyle`. Confirm the by-name destructive list is otherwise
  unchanged: Remove on the account row, Clear history, Stop all confirm.
  Acceptance (`prd.md > Story 2.3`): Remove carries the rank; no fourth site was added by judgement.
  Verify: open the Plugins window in flatline. Destructive must read as destructive **without
  colour** — that is the rank's whole design.

- [ ] **8. The state gate, written to fail first**
  Spec ref: `spec.md > §5`
  What to build: `Tests/Rendering/ButtonStateGateTests.cs` using §5's option (1) — resolve the
  `ControlTemplate`, read each trigger's setters, assert no value is a hardcoded literal and that
  each resolved state pair clears its floor. **Do not attempt `VisualStateManager.GoToState`:** it
  returns False on this template because it has no visual state groups, and a `/prd` probe already
  lost twenty minutes to that.
  Acceptance (`prd.md > Story 1.2`): the gate covers hover, pressed and disabled for all ranks.
  Verify: **break the template on purpose** — put a literal `#BEE6FD` in the hover trigger — and
  confirm the gate goes red naming that trigger, then restore it. If it cannot be made to fail, the
  gate is not shipped and the item closes with that finding recorded.

- [ ] **9. The fence, or the finding**
  Spec ref: `spec.md > §0.4`, `prd.md > Story 4.1`
  What to build: a test that fails the build when a `Button` declaration sets colour properties
  inline instead of taking a rank. Every exemption names its reason inline.
  Acceptance: the fence fails on a planted violation and passes on the migrated tree.
  **Explicit abort:** if the exemption list is large enough that the fence mostly measures its own
  allow-list, **do not ship it.** Close the story, record how many exemptions it would have needed
  and why, and say so in the commit. A gate that passes because everything is exempted reports
  coverage it does not have.

- [ ] **10. Documentation, security, and the numbers**
  Spec ref: `spec.md > §9`
  What to build: version `1.19.0.0` → `1.20.0.0` in csproj and `Package.appxmanifest`, lockstep.
  **Flip F-068 and F-046 to closed**, recording the scanner definition beside the closing count and
  noting plainly that it is comparable to the branch point and to nothing before it. Update
  `docs/features.md`. Sync `CLAUDE.md`'s file table. **Archive `docs/spec.md` →
  `docs/superpowers/specs/2026-08-11-rororo-button-vocabulary-design.md`.** Security pass:
  local-path grep, dependency audit, secret scan.
  Acceptance: both register rows carry the definition and both counts; versions lockstep; spec
  archived.
  Verify: `dotnet test ROROROblox.slnx` green; re-run the scanner and confirm the number in the row
  matches what the script prints.

---

## Checkpoints

**C1 (after item 4)** — the look-at-it gate. The template, both new ranks, and the app's most-visible
file land together. A regression here is cheap; the same regression found after 21 more files is not.

**C2 (after item 6)** — every migration done, before the gates and the close-out. The last point at
which a wrong rank assignment is cheap to change.

## What this cycle must not do

- **Do not migrate to `ui:Button`.** Cut in `spec.md > §0.3`: a 116-site control-type swap with its
  own regression surface, and §0.2 means we would still need our own template.
- **Do not change how any button looks at rest**, except `PluginsWindow`'s Remove in item 7.
- **Do not lower a contrast floor to make a new rank fit.** Change the rank.
- **Do not ship a fence that passes by exemption**, or a state gate that cannot be made to fail.
- **Do not touch borders** — 60 of 76 still hand-themed, a real debt and a different cycle.
- **Do not start on F-050.** Standing exclusion, unchanged.
