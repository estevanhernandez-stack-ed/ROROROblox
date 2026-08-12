# F-098 — the instrument scope audit

**Date:** 2026-08-11 · **Scope:** every test in `src/ROROROblox.Tests/` that reads an artefact as
text rather than exercising behaviour · **Prompt:** a cross-repo observation, same day —

> every instrument that only reads markup should be audited for whether the behaviour it asserts
> actually lives in markup

One question, asked twelve times. It is cheap, it is mechanical, and it was worth asking.

## The answer up front

**Twelve instruments read markup. Three claim more than they measure, four had already asked this
question and answered it, three are correctly markup-scoped, and two resolve real objects and are
not exposed at all.**

That ratio is better than it looked going in, and the correction is the most useful thing here.
**The first draft of this audit listed `ThemedStatusColourTests` as a gap. It was wrong** — that
gate carries `NoColourLiteralIsConstructedInAppCodeOutsideTheAllowList`, which walks C# source, and
it proved the point by going red at me during the write-up. The audit asserted a blind spot the
suite did not have, in a document about instruments asserting more than they measure.

The headline is not the count. It is that **this repo already knew the answer three times and did
not generalise it.** `MutedTextFenceTests` and `InteractiveEdgeBindingTests` each carry an explicit
code-behind companion test. `SquadLaunchWindow`'s Remove has carried a comment since wave 5 saying
markup sweeps cannot see it. The knowledge was present, local, and written down — and v1.20 still
shipped a markup-only fence claiming 99.1% coverage. The failure was not discovery. It was that a
lesson recorded at one site stayed at that site.

## The register

| Instrument | Asserts | Behaviour actually lives in | Verdict |
|---|---|---|---|
| `MutedTextFenceTests` | the prose token is never a control label | XAML **and** code-behind | **asked** — `NoCodeBehindControlResolvesTheProseTokenForItsForeground` |
| `InteractiveEdgeBindingTests` | the derived edge is never decorative | XAML **and** code-behind | **asked** — `NoControlBuiltInCodeBehindKeepsTheDecorativeBrushAsItsBoundary` |
| `ButtonRankFenceTests` | a button declaration does not paint itself | XAML **and** code-behind | **fixed today** — see F-098's row |
| `WindowTitleConventionTests` | two title rules | XAML **and** code-behind | **gap** — see below |
| `ContrastPairGateTests` | declared colour pairs clear AA | XAML, code-behind, **and composition** | **gap** — see below |
| `FlatlineLabGateTests` | the adversarial theme's pairs fail by name | same as above (shares the scan) | **gap**, inherited |
| `ThemedStatusColourTests` | colour literals, XAML ceiling **and** C# allow-list | XAML **and** C# | **asked** — `NoColourLiteralIsConstructedInAppCodeOutsideTheAllowList` |
| `UiRoutesSchemaTests` | the route file is well-formed | the route file **and the XAML it names** | **gap**, known |
| `XamlStyleIntegrityTests` | XAML parses and styles resolve | XAML | correctly scoped — its subject *is* markup |
| `ExpiredRowRedundancyTests` | expired rows carry a non-colour carrier | XAML | correctly scoped, verified |
| `CommandBindingIntegrityTests` | MainWindow bindings resolve to an `ICommand` | XAML today | correctly scoped, **unguarded** |
| `ButtonStateGateTests` / `RenderedStyleGateTests` | resolved templates and rendered pixels | the objects themselves | **not exposed** — these resolve, they do not read |

## The four gaps, in detail

### 1. `WindowTitleConventionTests` — half the rule reads code, half does not

This is the most interesting result in the audit, because the instrument **already contains the
fix** and applies it to only one of its two assertions.

`NoUserFacingTitleUsesTheRepoName` scans XAML *and* calls `RuntimeTitleAssignments()`, which walks
`.cs` for `Title = "…"`. It was written that way deliberately: `FriendFollowWindow` builds its title
at runtime and shipped the repo name to users for months, and the doc comment says so.

`NoWindowTitleRepeatsTheProductName_ExceptTheOnesAboutTheProduct` — the *primary* rule, the one the
whole convention is about — reads XAML only. A `Title = "RoRoRo — Settings"` written in code-behind
tomorrow passes.

There is a second, subtler edge. `RuntimeTitleAssignments` captures the **literal**, so for
`Title = $"Friends — {ChromeName(current)}"` it sees the interpolation source, not the resolved
string. `ChromeName` returns a streamer alias or an account display name, so nothing leaks today —
but the shape is precisely the bug the test exists to prevent, one level down. An instrument that
reads an interpolated literal is reading the recipe, not the meal.

**Fixed:** the product-name rule now reads runtime assignments too. The interpolation limit is
recorded in the test rather than fixed, because resolving it means executing the app.

### 2. `ContrastPairGateTests` — two blind spots, one already load-bearing

Documented at length in **F-050**: it measures only elements declaring *both* fill and label on one
tag. A magenta `Border` wrapping a white `TextBlock` is invisible, which is how F-050 came to look
resolved while shipping on five badges.

The audit adds a second: it reads XAML, so a colour assigned in C# via `FindResource` is invisible
too. **34 such assignments exist.** Most are guarded by the two fences that did ask the question,
which is why this has not bitten — the coverage is real, it is just not where the gate claims it is.

Not fixed here. Widening this gate is F-050's prerequisite and deserves its own cycle, not a
close-out patch.

### 3. `ThemedStatusColourTests` — not a gap, and the way that was established is the point

The first draft of this section claimed the literal ceiling counts XAML only and that C# literals
were invisible. **False.** Alongside `AllowedXamlLiteralCeiling`, the file carries
`NoColourLiteralIsConstructedInAppCodeOutsideTheAllowList`, which walks App `.cs` for constructed
colours and requires each to be allow-listed *with a written reason* — the caption palette is listed
against spec §7 as per-account identity paint rather than theme paint, which is correct.

How the error surfaced is worth recording, because it is this audit's own subject: the claim was
written from a grep that looked for XAML patterns and found no C# clause, and it survived until the
gate **failed the build during the write-up.** A four-line comment added to `Converters.cs` pushed
two palette entries past the gate's 12-line anchor lookback, they fell off the allow-list, and it
reported them as violations. The document was corrected by the thing it had just mis-described.

One genuine brittleness, recorded rather than re-engineered: `AnchorLookback = 12` is a constant
sized to the palette array's current length. Interleave comments between the anchor and the last
entry and governed literals drop out of reach. It **fails loud rather than passing quietly**, which
is the right direction for a gate to break in, but the failure names the wrong cause — it reports a
policy violation when what happened was a formatting change. `Converters.cs` now carries a note
telling the next person to keep the array compact and put explanations above the declaration.

### 4. `UiRoutesSchemaTests` — shape, never existence

Four tests validate verbs, control types, deny-list membership and surface count. None asks whether
a named element exists in the XAML. Moving Games out of the Tools menu broke surface 08's route with
a green suite, earlier today.

Not fixed: resolving element existence from a route file means either a XAML index or a running app,
and the honest cheap version is a comment saying what the file does not check. Added.

## One finding from a different family, same root

`AutoPalette` exists **twice** — `Converters.cs` as `Color.FromRgb(0x1E, 0x40, 0xAF)` triples, and
`Tray/RobloxWindowDecorator.cs` as `uint` values `0xFF1E40AF`. The only thing holding them together
is a comment: *"keep in sync if either changes."*

**They had already drifted.** `Converters.cs` held `0x07, 0x58, 0x85`; the decorator paints
`0xFF075985`. One digit of green on the "ocean" entry. The palette is Tailwind's and sky-800 is
`#075985`, so the decorator is right and the converter was a transcription slip — meaning the
swatch in Settings has been previewing a colour the Roblox title bar never painted.

Two things worth recording about how that was found. **A manual diff of the two files said they
matched, and it was wrong** — the shell pattern was written for the `Color.FromRgb` form and
silently matched nothing in the `uint` file, so an empty comparison read as agreement. That is this
audit's own subject arriving inside the audit. The test caught what the eyeball check had just
cleared, ten minutes apart.

And the drift is imperceptible on screen. Nobody was ever going to see this; the point is not the
colour, it is that a rule written as *"keep in sync if either changes"* had gone unenforced long
enough to be wrong, and nothing anywhere could have said so.

**Fixed:** `Converters.cs` corrected to `0x07, 0x59, 0x85`, and `CaptionPaletteSyncTests` now
asserts the two copies agree by value. It needed no planted violation to prove it can fail — it went
red on the real drift and green after the fix, which is the better demonstration.

## What this says beyond this repo

The five cases that prompted this audit came from three repos, and they share one shape with the
three here: **green because the gate got narrower, not because the defect left.**

Two things worth carrying:

**A gate can only ever be evidence about the artefact it reads.** Coverage claimed past that is
coverage invented — including by me, in a commit message, hours before this audit.

**And the near-miss is its own failure mode.** The first draft of F-098's finding said the five
code-built buttons were "still flashing Aero blue." Clean, consistent with the cycle's story, and
wrong — they get WPF-UI's implicit style, whose setters are `DynamicResource` but resolve against a
fixed `Theme="Dark"` dictionary. A ten-line probe killed it before it reached a commit message. No
gate catches confident-and-nearly-right, and no walk does either. Only checking does.

## Second pass, same day — CI and the scripts

The first pass stopped at the test suite. This covers the two it named as unfinished.

### CI is clean, and that is the finding

The cross-repo case that prompted all of this was a job whose filters excluded the file under test:
`StandaloneTestsOnly=true`, a suite reporting 268/268, and the number true but not measuring what
anyone thought. Checked here against all four doors that shape can come through:

| Door | Result |
|---|---|
| A `--filter` or property narrowing the run | none — both jobs run `dotnet test ROROROblox.slnx` whole |
| A project on disk missing from `.slnx` | none — all five `src/` projects plus `CompatSigner` are listed |
| Files excluded from compilation | none — no `Compile Remove`, no `EnableDefaultCompileItems` |
| Failures swallowed by `continue-on-error` | one, on *Fetch prior release*, documented as a first-release tolerance |

Two further checks, since a clean sweep is only as good as its questions: the single skipped test is
`EndToEndContractTests`' mid-stream consent revocation, skipped **with a reason and a deferral
target** (v1.5+, plan task 24 step 2) — a legitimate skip, not a hidden one. And no `*Tests.cs` file
exists outside a test project, so nothing on disk is silently uncompiled. The 1,399 `[Fact]`/
`[Theory]` attributes against 1,592 executed cases is `Theory` expansion, not a discrepancy.

**Nothing was found, and that is worth writing down rather than deleting.** An audit that only
records hits teaches the next reader that the clean doors were never checked.

### `count-button-sites.ps1` is not a guard, and read like one

**No workflow invokes it.** The only script CI runs is `build-velopack-release.ps1`. The scanner's
own header advertises a `-Quiet` mode "for CI or a commit message" — for a CI use that never
happened.

So F-068's *0 un-migrated* is a number somebody typed once, and between typings the count could
return to fifty with nothing to say so. The actual guard is `ButtonRankFenceTests`, which runs on
every push and covers strictly more. That is a fine outcome; the risk was that the scanner's
presence in the repo, and its citation in a closed register row, read as protection it does not
provide.

### The 108 / 112 collision

Two committed instruments count buttons and report different totals:

| Instrument | Counts | Total |
|---|---|---|
| `count-button-sites.ps1` | `<Button` and `<ui:Button` in XAML | **108** |
| `ButtonRankFenceTests` | the above plus `ToggleButton`, `RepeatButton` | **112** |
| `ButtonRankFenceTests` | plus buttons constructed in C# | **+5** |

Neither is wrong. They answer different questions, and the narrow one is deliberate — `spec.md §6`
fixed it precisely so F-068's branch-point and close figures compare to each other.

But this cycle already burned days on *55 vs 72 vs 115*, three counts under three unwritten
definitions, and the fix was to write the definition down. Shipping two live instruments with two
numbers and no stated relationship rebuilds the same trap with better paperwork. **Both files now
carry the reconciliation**, so the next person reading 108 against 112 finds the answer instead of
re-deriving it.

## Still not done

- **`capture-ui.ps1`** reads the route file and drives a live app; auditing it means running it
  against a real profile, which is a walk, not a test pass.
- The **packaging scripts** were not examined. They assert about MSIX contents and manifests, which
  is the same question in a different artefact.
