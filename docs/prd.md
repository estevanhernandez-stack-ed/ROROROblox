# RORORO — Product Requirements: one button vocabulary

Expands [`docs/scope.md`](scope.md). Technical design lands in `/spec`; nothing here picks a style
key, a template shape or a trigger mechanism.

**Anchor:** a button should look like the theme in every state, not just at rest.

## Problem statement

RoRoRo has four button ranks that work — `PrimaryButtonStyle`, `SecondaryButtonStyle`,
`SecondaryStrongButtonStyle`, `DestructiveButtonStyle` — and 22 files that still hand-copy button
attributes instead of using them. Three cycles have stepped around those files, and each one that
touched a button and hand-copied its attributes made the migration bigger.

That is the tidiness half, and it is the half the register has always described. **The recon for this
PRD found the other half, and it is not a tidiness problem.** Every one of those four styles declares
`BasedOn="{StaticResource {x:Type Button}}"` and sets four or five properties: background,
foreground, border brush, border thickness, sometimes weight. **None of them says anything about
hover, pressed or disabled.** `Controls/ControlStyles.xaml` contains zero `ControlTemplate` and zero
`Style.Triggers`. `ThemeService` writes eleven brush keys, none of which belong to the control
library. So whatever the inherited template does on hover, RoRoRo has no influence over it and no
test that looks at it.

## The measurement, what it found, and the part that is not settled

Scope claimed "every button breaks its theme on hover" from a grep. That is an inference, and this
project has recently shipped several confident claims that turned out false, so it was measured
before being written into requirements.

**What was measured.** The v1.17 render harness composes the real dictionaries in App.xaml's order —
WPF-UI's `ThemesDictionary` (Dark), its `ControlsDictionary`, then `ControlStyles.xaml` — and applies
a theme through the app's own `ThemeService.ApplyTo`. A button carrying `SecondaryButtonStyle` was
built from that dictionary and its resolved `ControlTemplate` was read directly.

**What it says**, verbatim from the template's own triggers:

```text
IsMouseOver == True   ->  Background=#BEE6FD, BorderBrush=#3C7FB1
IsPressed   == True   ->  Background=#C4E5F6, BorderBrush=#2C628B
IsEnabled   == False  ->  Background=#F4F4F4, BorderBrush=#ADB2B5, Foreground=#838383
```

Those are **hardcoded literals, not resource references**, and `#BEE6FD` is classic Windows Aero
light blue. Nothing in that list can follow a theme, because nothing in it is bound to one.

**Two failed approaches worth recording**, because they are the reason the number above is a trigger
dump rather than a rendered pixel. Forcing `VisualStateManager.GoToState(btn, "MouseOver")` produced
a pixel identical to resting — which read as "hover changes nothing" until a control was added for
it. Forcing `Disabled` and `Pressed` also produced identical pixels, and `GoToState` returned
**False** for every state, because this template uses classic property triggers and has no visual
state groups at all. **The first probe was measuring my state-forcing, not the button.** Caught only
because the control was built before the result was believed. That is the sixth instance this session
of a check reporting something other than what it claims, and the first one caught inside the
instrument rather than downstream of it.

**What is NOT settled, and it changes the cycle's shape.** The template above is the **OS Aero**
template, not WPF-UI's. Two readings, and they cannot be told apart from inside the test suite:

- **(a) The running app resolves the same template.** Then hovering any button in RoRoRo paints
  `#BEE6FD` over the active theme, and has always done so. Very visible, and the cycle is urgent.
- **(b) Only the harness resolves it.** Implicit-style lookup through `Application.Resources`
  behaves differently from a hand-composed `ResourceDictionary`, so the app may well get WPF-UI's
  template while the harness does not. Then hover is probably fine — **and the v1.17 rendered
  contrast gate has been measuring a different template than the app renders**, which is a defect in
  the gate rather than in the buttons, and a worse one.

**ANSWERED 2026-08-11 by looking: reading (a) holds.** Este hovered a button on a running build and
photographed it — the fill is unmistakably the pale Aero blue, on a dark themed window, next to an
unhovered sibling that is correctly grey. **Every button in RoRoRo has been flashing `#BEE6FD` on
hover, in every theme, for the app's entire life.**

Two consequences, and the second one is a relief:

- **The cycle is urgent and the most visible work in four rounds.** This is not a tidiness project
  with a theming angle; it is a theming defect on the most-used surface in the app, and it fires on
  every pointer movement.
- **The render harness is NOT the thing that is wrong.** Reading (b) is eliminated, which means the
  v1.17 rendered contrast gate resolves the same template the app does. Its results stand.

## User stories

Epic headings are stable addresses. `/spec` and `/checklist` reference them by name.

### Epic 0 — Find out which cycle this is

**Story 0.1 — Settle the template question before anything is built.**
As the person about to spend a cycle on buttons, I want to know whether the hover defect is real in
the running app, so that the work is aimed at the actual bug.

- [ ] Launch RoRoRo under **flatline** and hover a button on the main window. Record the colour.
- [ ] If it is a pale blue, reading (a) holds: the app runs the OS template and every button in the
      app has always flashed Aero blue on hover.
- [ ] If it is a subtle dark grey, reading (b) holds: the app runs WPF-UI's template and the
      **render harness is the thing that is wrong** — a separate and higher-priority finding, because
      the v1.17 contrast gate's results are measured through it.
- [ ] Whichever it is, it goes in the spec as a recorded measurement with the date, not as a
      remembered conclusion.

**RESOLVED 2026-08-11 before `/spec`.** Reading (a). Epic 1 is "add themed hover states" and there is
no gate to fix first. The story stays in the document as the record of how it was decided, and its
acceptance criteria are met.

### Epic 1 — A button looks like the theme in every state

**Story 1.1 — Hover and pressed follow the theme.**
As a clan member who picked flatline because colour was a problem, I want a button to stay
achromatic when I hover it, so that the theme I chose survives contact with the pointer.

- [ ] Hover, pressed and disabled appearances for all four ranks derive from themed brushes. **No
      hardcoded colour literal appears in any of them** — the same rule the XAML literal fence
      already enforces elsewhere in the app.
- [ ] Switching theme at runtime updates the hover appearance without a restart, by the same
      mechanism resting colours already use.
- [ ] The states are distinguishable from resting **and from each other** in every built-in theme
      including flatline, where they cannot rely on hue.
- [ ] A disabled button reads as disabled without depending on colour alone.

**Story 1.2 — The states clear the contrast floor they are held to at rest.**
As someone who cannot rely on hue, I want a hovered button to remain as legible as a resting one.

- [ ] Foreground against the hovered fill meets the same threshold the resting pair is held to.
- [ ] The interactive edge in every state clears WCAG 1.4.11's 3:1 against the surface behind it.
- [ ] The existing contrast gate covers the new states, or the spec says plainly why it cannot.

### Epic 2 — The vocabulary gets adopted

**Story 2.1 — `MainWindow.xaml` stops declaring its own button colours.**
As whoever maintains this next, I want the app's most-looked-at file to use the shared ranks, so the
largest single source of new recipes stops producing them.

- [ ] `MainWindow.xaml`'s un-migrated button declarations take an existing rank.
- [ ] Nothing changes appearance at rest. **A migration that looks different is a regression**, not
      an improvement, unless the spec named that site as a deliberate re-rank.
- [ ] A site that genuinely needs a look no rank provides is **recorded as a finding, not
      hand-rolled**.

**Story 2.2 — The long tail, as far as it goes.**
As a builder with a fixed cycle, I want the remaining files walked worst-first, so stopping early
still banks the most debt.

- [ ] Files are taken in descending order of un-migrated site count.
- [ ] Each file's count before and after is recorded.
- [ ] **A file that cannot be migrated without inventing a rank stops the item and opens a row**
      rather than growing the vocabulary mid-sweep.

**Story 2.3 — F-046 closes with it.**
As the person who left it open at the end of v1.18, I want the row that was blocked on this to close
in the cycle that unblocks it.

- [ ] `PluginsWindow`'s Remove — the row's headline evidence, a hand-rolled magenta fill — takes
      `DestructiveButtonStyle`.
- [ ] The remaining destructive sites are assigned by the same by-name rule v1.18 established, never
      by sweep.

### Epic 3 — The count means something

**Story 3.1 — One scanner definition, written down.**
As anyone reading this row in three months, I want its number to be reproducible.

- [ ] The spec defines what counts as an un-migrated site, precisely enough to re-run.
- [ ] The definition is run at the branch point and at the end, and both numbers are recorded with
      the direction.
- [ ] The register row records the definition alongside the count, so the next re-measure compares
      like with like.

**Why this story exists.** The register says 55 un-migrated sites after v1.18. A scanner run while
scoping counted **72** by a cruder definition — any `<Button>` whose opening tag lacks a
`*ButtonStyle` reference. The file count agrees exactly at **22**, so the two disagree about what a
*site* is rather than about what is in the tree. Two earlier cycles cited a "63 across 15 files"
figure that reproduces at no commit. A row whose number cannot be reproduced cannot be used to size
work, and this row has been used to size work three times.

### Epic 4 — The next hand-copy is caught

**Story 4.1 — A fence, if it can be written honestly.**
As whoever adds a button next cycle, I want the build to tell me I hand-copied a recipe.

- [ ] A new `Button` declaration that sets colour properties inline instead of taking a rank fails
      the build.
- [ ] Every exemption names its reason inline, following the rule the existing fences use.
- [ ] **If the exemption list is large enough that the fence mostly measures its own allow-list, the
      fence is not shipped and the story is closed with that finding recorded.** A gate that passes
      because everything is exempted is worse than no gate — it reports coverage it does not have.

## What we're building

| # | Deliverable | Verified by |
| --- | --- | --- |
| 0 | The template question settled by looking at the running app | Eyes, recorded in the spec |
| 1 | Themed hover / pressed / disabled for all four ranks | Contrast gate + render harness |
| 2 | Contrast floors held in the new states | Existing gate, extended |
| 3 | `MainWindow.xaml` migrated | Count before/after + eyes-on, no visual change at rest |
| 4 | The tail migrated worst-first, as far as it goes | Per-file counts |
| 5 | F-046 closed | The row's own evidence site |
| 6 | A written scanner definition and two runs of it | Reproducible by re-running |
| 7 | A fence, or a recorded finding explaining why not | Build fails on a planted violation |

## What we'd add with more time

- **Borders.** 60 of 76 still hand-themed. A real debt and a different one.
- **The other 16 control types.** Buttons are where the recipes concentrated; they are not the only
  place a literal can hide.
- **A hover state for the row surfaces**, which have the same "resting is themed, interactive state
  is not" shape.

## Non-goals

- **A component library.** Four ranks with states, not a design system. Proposing a `Card` primitive
  means scope has been left.
- **Inputs.** Already 11 of 11 migrated.
- **Re-ranking anything for taste.** A migration that changes how a button looks at rest is a
  regression unless the spec named that site deliberately.
- **F-050.** Standing exclusion: flipping it auto-deletes the contrast gate's exemption and reddens
  three built-in themes.
- **`rororo-ur-task`.** v1.19's plugin leg is unmerged and unverified; two open cross-repo cycles is
  how both stall.

## Edge cases surfaced

1. **A rank that needs a fifth sibling.** Expected in the tail. It is a finding and a recorded
   addition, not a blocker — the four current ranks were themselves discovered by sweeps.
2. **WPF-UI updates its Button template.** If the fix rides on overriding that template, a package
   bump can silently revert it. The spec should say whether the approach survives that.
3. **A button inside a `ControlTemplate` rather than a page.** The scanner counts declarations; a
   templated button may have no declaration to migrate.
4. **`CyanCtaButton` still survives at four sites**, including `PluginsWindow.xaml:23`. It predates
   the vocabulary and is neither migrated nor a rank.
5. **The render harness may not reproduce the app's implicit-style resolution.** Story 0.1's second
   branch. If true, every result the v1.17 gate has produced needs re-reading.

## Open questions

| Question | Needs answering |
| --- | --- |
| ~~**Does the running app hover to `#BEE6FD`, or does only the harness?**~~ **CLOSED 2026-08-11 — the app does.** Confirmed by eye on a running build. Reading (a). The harness is vindicated; the buttons are not. | Done. Epic 0 no longer gates the cycle. |
| **Override the inherited template, or add `Style.Triggers` on top of it?** Triggers are smaller and survive a WPF-UI bump; a full template is more control and more surface. | At `/spec`. |
| **Can the fence be written without a disqualifying allow-list?** | At `/spec`, and "no" is an acceptable answer that closes Story 4.1 with a finding. |
| **Does the contrast gate extend to non-resting states, or is that a new gate?** | At `/spec`. |
