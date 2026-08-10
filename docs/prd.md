# RORORO — Product Requirements: flatline, the readable theme

Expands [`docs/scope.md`](scope.md). Technical design lands in `/spec`; nothing here picks a hex,
a token or a mechanism. Every claim about current behaviour was read out of the tree on 2026-08-10
and cites the file and line it came from, because three register rows already quote numbers from a
theme that was never in git and this cycle is the one that stops doing that.

**Anchor:** carry distinction without colour. Every story below answers to that line.

## Problem statement

RORORO says almost everything it needs to say in hue. Account state is an 8px coloured dot
([`MainWindow.xaml:387`](../src/ROROROblox.App/MainWindow.xaml#L387)), an expired session is an amber
row ([`:217`](../src/ROROROblox.App/MainWindow.xaml#L217)), a warning chip is amber text
([`:419`](../src/ROROROblox.App/MainWindow.xaml#L419)), the main account is a magenta pill. Roughly
8% of men have some form of colour vision deficiency, and the clan this ships to is eight-alt Pet Sim
99 players on whatever laptop they own. For that slice the app is not hard to read, it is missing
information.

Flatline is the theme that carries no meaning in colour: one surface, one text colour, one accent,
maximum legibility. Shipping it is the forcing function. A theme with nothing left to say in hue
makes every colour-only signal visible, and the fix for each one is a fix in every theme.

## User stories

Epic headings are stable addresses. `/spec` and `/checklist` reference them by name.

### Epic 1 — Picking Flatline

**Story 1.1 — A fourth built-in in the picker.**
As a clan member who cannot rely on colour, I want an accessible theme already in the app, so I do
not have to find, download or author a JSON file to read the thing I use every day.

- [ ] Settings, Appearance, Theme lists a fourth entry after Magenta Heat.
- [ ] `ThemeStore.BuildBuiltIns()` returns four records; the new one is `Id: "flatline"`,
      `IsBuiltIn: true`, hardcoded exactly like the other three
      ([`ThemeStore.cs:202-251`](../src/ROROROblox.Core/Theming/ThemeStore.cs#L202-L251)).
- [ ] It appears with `%LOCALAPPDATA%\ROROROblox\themes\` empty, missing, or unreadable. No
      filesystem dependency.
- [ ] Dropping a user file named `flatline.json` into the themes folder does not replace it. The
      built-in wins and the user file is dropped, per the existing id-collision rule
      ([`ThemeStore.cs:71-76`](../src/ROROROblox.Core/Theming/ThemeStore.cs#L71-L76)). Verify by
      dropping one, not by reading the code.
- [ ] Selecting it repaints the main window immediately, no restart. Secondary dialogs adopt on next
      open, matching the copy already shown under the picker
      ([`PreferencesWindow.xaml:407`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L407)).
- [ ] The choice survives an app restart.

**Story 1.2 — It does not ambush you the moment you pick it.**
As someone trying an unfamiliar theme, I want selecting it to just work, so my first impression is
not a dialog asking me to approve something I did not do.

- [ ] Selecting Flatline raises no edge-remediation prompt, no warning, no modal of any kind.
- [ ] Verified by selecting it in a running build. The record says this is already guaranteed,
      `EdgeRemediation.Decide` returns `DeriveSilently` for any `IsBuiltIn` theme
      ([`EdgeRemediation.cs:45`](../src/ROROROblox.Core/Theming/EdgeRemediation.cs#L45)), which
      resolves the concern scope.md raised. Resolved on paper is not the same as verified on screen.
- [ ] `InteractiveEdgeBrush` still clears 3:1 against the flatline surface, so buttons keep a visible
      boundary in a theme built from one surface colour.

**Story 1.3 — Knowing what it is for at the moment of choosing.**
As a non-technical clan member scrolling a theme list, I want one plain sentence telling me what
Flatline is for, so a name I do not recognise does not read as "broken mode."

- [ ] Choosing or focusing Flatline surfaces one sentence saying it is built to stay readable without
      relying on colour.
- [ ] Builder-to-builder, second person, no jargon. No "WCAG", no "contrast ratio", no "CVD".
- [ ] The sentence is reachable in the app. A README paragraph does not satisfy this.
- [ ] Where it lives is `/spec`'s call. Constraint to weigh, not a decision to make here: `Theme` has
      no description field, and the codebase's stated invariant is that the theme contract does not
      grow, so every JSON already on disk stays valid without its author touching it
      ([`ThemeService.cs:238-246`](../src/ROROROblox.App/Theming/ThemeService.cs#L238-L246)). An
      eleventh slot is the expensive answer and needs to be argued, not assumed.

**Story 1.4 — A palette that clears the gate outright.**
As the person who has to trust this theme, I want it to pass the contrast gate with no exemption of
its own, so "accessible" is a measurement and not a claim.

- [ ] Every declared Background/Foreground pair clears 4.5:1 under flatline, with zero new
      exemptions and no change to the gate's existing one.
- [ ] Enforced automatically. `ContrastPairGateTests` measures every `IsBuiltIn` theme the real
      `ThemeStore` returns
      ([`ContrastPairGateTests.cs:153-167`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L153-L167)),
      so flatline enrols itself the moment it ships. Nothing to wire.
- [ ] White on the single accent clears 4.5:1, so the F-050 exemption is not load-bearing for this
      theme. The exemption's 3.20 floor is the failure bar, not the target.
- [ ] `MutedText` stays distinguishable from body text and legible on the surface. F-032's 1.00:1 is
      exactly the failure to avoid: collapsing muted into white is not "one text colour", it is
      deleting a distinction the app uses in ~104 places
      ([`MutedTextFenceTests.cs:10-13`](../src/ROROROblox.Tests/MutedTextFenceTests.cs#L10-L13)).
- [ ] If the ratio alone cannot carry "secondary" in a one-text-colour theme, weight, size or indent
      carries it and the theme's muted slot stops trying to. Either resolution is acceptable; leaving
      it at 1.00:1 is not.

### Epic 2 — Reading status without colour

The real work of the cycle. `/spec` enumerates the full surface list; these are the requirements it
answers to.

**Story 2.1 — Telling account states apart.**
As a user running eight alts, I want to know which are running, expired and idle without seeing the
dot's colour, so a glance at the list still tells me where to look.

- [ ] With the status dot covered, a screenshot in flatline still distinguishes running, expired,
      idle and attention states.
- [ ] The dot is never the sole carrier of any state.
- [ ] `SecondaryStatusText` sits directly beside the dot today
      ([`MainWindow.xaml:391`](../src/ROROROblox.App/MainWindow.xaml#L391)). Where the words already
      carry the state, `/spec` says so and changes nothing. Ornament added for its own sake is a
      regression in a theme whose whole argument is legibility.

**Story 2.2 — Spotting an expired session.**
As a user whose cookie has gone stale, I want the expired row to announce itself, so I fix it before
I hit Launch and watch it fail.

- [ ] An expired row is identifiable in flatline with its background flattened to the ordinary row
      surface. Text, weight, icon or position carries it.
- [ ] Amber is currently the entire signal, across four sites: row background and border
      ([`:217-218`](../src/ROROROblox.App/MainWindow.xaml#L217-L218)), status foreground
      ([`:399`](../src/ROROROblox.App/MainWindow.xaml#L399)), and the standalone banner
      ([`:1516-1522`](../src/ROROROblox.App/MainWindow.xaml#L1516-L1522)). All four are in scope.
- [ ] The same treatment reads correctly in brand, midnight and magenta-heat. It ships for every
      theme.

**Story 2.3 — Reading a warning chip.**
As a user watching an alt climb toward a memory cap, I want the warning state to be legible without
amber, so I act on it in any theme.

- [ ] The idle chip's warn state is distinguishable from its ordinary state without colour
      ([`:417-419`](../src/ROROROblox.App/MainWindow.xaml#L417-L419)).
- [ ] The memory chip's latched state likewise
      ([`:431-433`](../src/ROROROblox.App/MainWindow.xaml#L431-L433)).
- [ ] Precedent to extend rather than invent: the memory chip already prefixes `▲` when a cap or
      projection trigger latches ([`:422-426`](../src/ROROROblox.App/MainWindow.xaml#L422-L426)).
      The app solved this once already. The cheapest correct answer is probably the same device on
      the idle chip.

**Story 2.4 — Leaving alone what already works.**
As the builder, I want the surfaces that already carry non-colour redundancy recorded as verified, so
`/spec` does not re-audit them and `/build` does not churn them.

- [ ] The selection toggle carries state in shape, filled versus hollow
      ([`:435-440`](../src/ROROROblox.App/MainWindow.xaml#L435-L440)). No work.
- [ ] The MAIN pill carries its meaning in the word MAIN
      ([`:374-380`](../src/ROROROblox.App/MainWindow.xaml#L374-L380)). Magenta is emphasis on top of
      text, not the message. No work.
- [ ] `InteractiveEdgeBrush` already derives a boundary that clears 3:1 under any theme. No work.
- [ ] Recorded in the spec as checked and clean, with the line reference. "We looked and it was
      fine" is a finding.

### Epic 3 — Colour the theme cannot reach

Surfaced during `/prd` recon, not in scope.md. The largest single risk to this cycle.

**Story 3.1 — Status colours that ignore the theme entirely.**
As a user who picked a monochrome theme, I want the app to actually go monochrome, so Flatline looks
deliberate rather than half-painted.

- [ ] Switching to Flatline leaves no brand hue on the main window. Verified by eye against the
      capture round, not by reasoning about the code.
- [ ] Two converters hardcode RGB values in C# and never read the active theme:
      `StatusDotBrushConverter` holds green `#4FE08C`, yellow `#F1B232`, magenta `#F22F89` and grey
      `#4A5C70` ([`Converters.cs:169-198`](../src/ROROROblox.App/Converters.cs#L169-L198));
      `IdleChipBrushConverter` holds amber `#F1B232` and muted `#8A93A0`
      ([`:205-218`](../src/ROROROblox.App/Converters.cs#L205-L218)). `ThemeService.ApplyTo` cannot
      touch either. Under flatline they paint brand colours onto a flat field.
- [ ] Every colour the row paints for status resolves from the active theme, or is deliberately
      theme-independent with the reason stated in the spec.
- [ ] The contrast gate cannot see these. It scans XAML for elements declaring both
      `Background="{DynamicResource …}"` and `Foreground="{DynamicResource …}"` inline
      ([`ContrastPairGateTests.cs:56-64`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L56-L64)),
      and a converter-supplied brush is neither. Whatever `/spec` picks, it states plainly whether
      the new path is measured or unmeasured. An unmeasured fix is acceptable; an unmeasured fix
      described as gated is not.

**Story 3.2 — Naming what is out of the theme's reach.**
As a reviewer three months from now, I want the surfaces this cycle deliberately did not touch named
with their reasons, so nobody re-discovers them as bugs.

- [ ] Tray icons are four static assets, `tray-on` / `tray-off` / `tray-warn` / `tray-error`
      ([`src/ROROROblox.App/Tray/Resources/`](../src/ROROROblox.App/Tray/Resources/)). Not themed,
      and distinct files rather than one file recoloured, which is redundancy of the right kind.
- [ ] `RobloxWindowDecorator` paints per-account title bars from an 8-entry palette plus a magenta
      main-account colour ([`RobloxWindowDecorator.cs:36-51`](../src/ROROROblox.App/Tray/RobloxWindowDecorator.cs#L36-L51)).
      That is per-account identity paint on a Windows surface the theme does not own, and the window
      title already carries the account name, so identity is not colour-only there.
- [ ] Both named as out of scope in the spec with that rationale. Silently skipped is how they come
      back.

### Epic 4 — Proving the gate can fail

**Story 4.1 — An adversarial fixture that fails on purpose.**
As the person relying on the contrast gate, I want proof it can go red, so a green run means
something.

- [ ] `flatline-lab` exists as a test fixture. Not in `BuildBuiltIns()`, not in the picker, not
      written to the user themes folder, not selectable by any path a user can reach.
- [ ] A test resolves it through the app's own path, `ThemeService.ApplyTo`, exactly as
      `ResolveTheme` does for the shipped themes
      ([`ContrastPairGateTests.cs:115-145`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L115-L145)),
      and asserts the AA measurement FAILS.
- [ ] The failure is attributed. At least one named pair is asserted below 4.5:1 by name, so the test
      fails for the stated reason and not because a slot is missing or a hex will not parse. A
      malformed fixture that "fails" proves nothing.
- [ ] It reproduces the numbers the register argues from. F-032's MutedText-versus-White at 1.00:1 is
      the specific one to preserve.
- [ ] `BuiltInThemes()`'s guard message still says "the 3 built-in themes (brand, midnight,
      magenta-heat)" ([`:163-165`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L163-L165)). It
      says four and names flatline.

### Epic 5 — Reconciling the numbers

**Story 5.1 — Register rows that say which artifact produced which number.**
As anyone reading the audit register, I want every flatline ratio traceable to something that exists,
so I can re-measure it instead of trusting it.

- [ ] F-031, F-032 and F-050 each state which artifact reproduces which number. After this ships,
      `flatline-lab` reproduces the adversarial ratios and shipped `flatline` produces different,
      passing ones. Both are stated; conflating them is the defect being fixed.
- [ ] Rows flip in the same PR that ships the change, per the repo's findings-register rule. Not a
      follow-up doc, not the next wave's close-out.
- [ ] Every updated row was verified against the tree. "The scope doc said so" is not evidence.
- [ ] F-050 stays open unless the work actually closes it. `NoExemptionOutlivesItsFinding` deletes
      the gate's exemption automatically when that row stops being open
      ([`:254-288`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L254-L288)), so flipping it
      casually tightens the gate on brand and magenta-heat as a side effect.

**Story 5.2 — In-code claims that go false on merge.**
As a developer reading the tests, I want the comments to describe the app that exists, so the tests
stay the honest record they were written to be.

- [ ] `ContrastPairGateTests`' class doc currently states flatline "is NOT covered here, because it
      is not a shipped theme" and "was never committed as a `ThemeStore` entry", and that the gate
      "cannot reproduce those numbers"
      ([`:36-45`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L36-L45)). Every clause is false
      the moment this ships. Updated in the same PR.
- [ ] `MutedTextFenceTests`' doc cites "1.00:1 under flatline"
      ([`:10-13`](../src/ROROROblox.Tests/MutedTextFenceTests.cs#L10-L13)). That number belongs to
      `flatline-lab` after this cycle, not to a theme users can pick. Updated in the same PR.
- [ ] Not in scope.md. Surfaced at `/prd`; two files, both small, both load-bearing for the "this
      gate is honest about what it cannot see" framing that makes the gate worth having.

### Epic 6 — A fourth round of evidence

**Story 6.1 — Capture picks it up with no code change.**
As the builder, I want a flatline round of screenshots, so the redundancy stories are verified
against pixels rather than intent.

- [ ] `scripts/capture-ui.ps1` produces a fourth round with no edit to the script. Grounded:
      `Get-AvailableThemes` enumerates the live picker and matches on the `Id = <id>,` substring
      ([`capture-ui.ps1:635-664`](../scripts/capture-ui.ps1#L635-L664)), and the expected-count guard
      multiplies by the theme count it found ([`:977`](../scripts/capture-ui.ps1#L977)). The id stays
      `flatline` for exactly this reason, whatever the display name ends up being.
- [ ] The run yields 56 captures against today's 42, same per-theme surface count as the other three
      rounds. Neither number is hardcoded anywhere; both are derived, and `docs/ui-routes.json`
      declares more surfaces than any round captures.
- [ ] `run-flatline.json` lands alongside the three existing manifests.
- [ ] The flatline round is the evidence Epic 2 and Story 3.1 are signed off against. Somebody looks
      at the PNGs.

## What we're building

Ordered by dependency, not by importance.

1. **Flatline as a fourth built-in** with a palette that clears 4.5:1 outright, no new exemption
   (Epic 1). Everything else depends on the theme existing.
2. **Theming the colours the theme cannot currently reach** (Story 3.1). Ahead of the redundancy
   work, because a status dot that stays brand-green under flatline makes every redundancy
   screenshot unreadable as evidence.
3. **Non-colour redundancy** for account state, expired sessions and warning chips, shipped for
   every theme (Epic 2). The substance of the cycle.
4. **`flatline-lab` and its failing assertion** (Epic 4). Independent of 2 and 3; can land in
   parallel.
5. **Register and in-code reconciliation** (Epic 5). Same PR as the change it describes.
6. **The fourth capture round** (Epic 6). Last, because it is the verification step for 1 through 3.

## What we'd add with more time

- **A theme-level declaration that a theme is monochrome**, so future surfaces can ask rather than
  every author remembering. Real, and premature before one such theme exists.
- **Extending the contrast gate to converter-supplied brushes.** Story 3.1 exposes the blind spot;
  closing it is Phase 2 gate work with its own design.
- **The app-wide colour-only sweep.** The register has 51 open rows and its own sequencing.
- **A shipped `flatline-lab`-style fixture per failure mode**, so the gate is proven against more
  than one way of being wrong.
- **Store listing treatment.** An accessibility theme is a listing asset, not a footnote, and
  reviewers read it favourably. Copy work, not build work.

## Non-goals

1. **No theme-authoring UI for accessibility presets.** "+ Build a theme..." already exists and user
   themes are JSON. Flatline is a built-in.
2. **No colour-vision-deficiency detection and no Windows high-contrast integration.** Windows owns
   its own high-contrast semantics; honouring them is a separate cycle. Flatline is a theme the user
   picks.
3. **No rework of the brand theme.** Flatline is an alternative, not a correction. Brand stays cyan
   `#17d4fa` and magenta `#f22f89` on navy `#0f1f31`.
4. **Not the default.** Brand stays default and stays the product's identity.
5. **Not the app-wide colour-redundancy sweep.** Flatline carries its own surfaces. A genuinely
   theme-independent fix can ride along; the other 51 rows are register work.
6. **No theme-conditional UI.** Surfaced at `/prd`: no `if (theme == flatline)` branch, anywhere. A
   redundancy that only appears in one theme is a costume, it doubles the surface every future change
   has to be verified against, and it would make the capture rounds disagree with each other by
   design.
7. **The adversarial theme is not retired.** It survives as `flatline-lab`. Cutting it entirely
   strands three findings' evidence.

## Open questions

**Before `/spec`:**

- **Display name.** Default is "Flatline", per scope.md, carried there as an assumption. Scope
  routed the decision here, so: it is Este's coinage, it describes flattening colour rather than
  flattening a patient, and Story 1.3's sentence is what actually carries the accessibility promise
  to a clan member. Recommend keeping it. The id stays `flatline` either way, so a change is one
  string and breaks nothing.
- **Where Story 1.3's sentence lives.** A tooltip, a line under the picker, or an eleventh theme
  slot. Weigh against the contract-does-not-grow invariant before reaching for the slot.
- **Whether `MutedText` can carry "secondary" at all** in a one-text-colour theme, or whether weight
  and indent take that job. Interacts with `MutedTextFenceTests`, which fences the token to prose,
  so a change here has a test with an opinion about it.

**During `/spec`, resolved by measurement:**

- **The exact hexes.** Proven by running `ContrastPairGateTests`, not by arithmetic in a doc. That
  is the whole lesson of the three rows this cycle is reconciling.
- **How Story 3.1's converters get their colours.** New theme slots, a resolve at brush-application
  time, or something else. Constrained by the contract-does-not-grow invariant and by whether the
  result is measurable.

**Can wait until `/build`:**

- **Which specific device carries each redundancy.** Icon, prefix glyph, weight or word. Story 2.3
  has a strong precedent in the memory chip's `▲`; the rest is a design call best made against the
  flatline captures.
