# RORORO — Scope: one button vocabulary

The largest consistency debt in the app, and the one three cycles have routed around. F-068 (`2/2`),
with F-046 (`3/3`) stranded behind it.

Cycle history: v1.17 shipped flatline and the rendered contrast gate. v1.18 furnished the Settings
shell, closing 12 of 13 rows. v1.19 gave plugins a theme feed — **built, PRs open, and F-091 still
open pending an eyes-on walk in a second process.** Last published artifact remains **v1.17.0.0**, a
pre-release. Store submission still parked.

## Idea

**Three cycles have now stepped around the same 22 files, and each one left the vocabulary a little
more expensive to adopt.**

`Controls/ControlStyles.xaml` ships `PrimaryButtonStyle`, `SecondaryButtonStyle`,
`SecondaryStrongButtonStyle` and `DestructiveButtonStyle`. They work. v1.18 proved it: nine Close
buttons adopted `SecondaryStrongButtonStyle` and the offender count went **down** rather than up,
because adopting a rank costs nothing while hand-copying attributes to reach the same look adds
another recipe.

But the migration stalled. Wave 5 moved 35 sites in one pass, v1.18 moved six more as a side effect
of a different row, and nothing else has moved. `MainWindow.xaml` alone still holds **30** of the
un-migrated declarations — more than the next seven files combined.

And the half nobody has started is the half that makes the vocabulary worth having:
**`ControlStyles.xaml` contains zero `ControlTemplate` and zero `Style.Triggers`.** Hover and pressed
still come from WPF-UI's dictionary, which `ThemeService` never touches. So every button in the app,
migrated or not, **breaks its theme the moment the pointer is over it.**

That last point reframes the row. F-068 has been read as a tidiness project. It is a theming defect
with a tidiness project attached, and the theming half is the part that is 0% done.

## Who it's for

**The clan member who picked flatline and hovers over a button.** Same person v1.17 and v1.19 were
for, hitting the same class of defect from a third direction: they chose the theme that carries no
meaning in colour, and WPF-UI's stock hover paints brand blue onto it. Not on a plugin window this
time. On the main window.

Secondary: **whoever does the next cycle.** Every cycle that touches a button and hand-copies its
attributes makes the migration bigger. That has happened three times.

## The measurement, and one number I will not assert

Re-measured against the tree on 2026-08-11 before scoping, per the register's own rule that an open
row is not static:

- **22 files carry un-migrated buttons.** Matches the v1.18 re-count exactly.
- **`MainWindow.xaml` holds 30**, the next-largest is `PluginsWindow.xaml` at 6. This is not evenly
  distributed work; it is one file and a long tail.
- **`ControlStyles.xaml`: 0 `ControlTemplate`, 0 `Style.Triggers`.** Independently confirmed. The
  template-trigger half has not started.

**The site count does not reconcile and I am not going to pick a side at scope time.** The register
says 55 un-migrated after v1.18. A scanner run here counts 72 by a cruder definition — any `<Button>`
whose opening tag lacks a `*ButtonStyle` resource reference. The file count agreeing at 22 while the
site count differs by 17 means the two scanners disagree about what a site *is*, not that something
moved. **`/spec` reconciles this with one scanner definition written down, before any item is sized
against it.** Two prior cycles cited a "63 across 15 files" figure that reproduces at no commit;
unsourced numbers are how that happens.

## In scope

**1. The template-trigger half, first, because it is the reason to do any of this.**
Hover and pressed states for every rank in `ControlStyles.xaml`, driven by themed brushes rather than
WPF-UI's dictionary. Until this exists, migrating a button buys consistency of resting state only.

**2. `MainWindow.xaml`'s 30 sites.** The dominant file. Doing it alone would nearly halve the debt.

**3. The long tail**, as far as it goes — 21 files, and the honest expectation is that some of them
reveal a rank the vocabulary does not have yet.

**4. F-046, which has been waiting on exactly this.** It stayed open at the end of v1.18 because
`PluginsWindow`'s Remove is a hand-rolled magenta fill and holding the line meant not fixing it. Its
fix direction names shared button styles. This is that cycle.

**5. A fence, if one can be written honestly.** Something that fails the build when a new `Button`
declaration hand-copies a recipe instead of taking a rank. v1.17's XAML literal fence and v1.18's
settings-reachability fence are the shape. **Whether this one is writable is a genuine question, not
a formality** — the 22 files contain legitimate exceptions, and a fence with a large allow-list
measures its own allow-list.

## What "done" looks like

Hovering any button in the app keeps the theme. Under flatline the hover is a grey, not brand blue.
`MainWindow.xaml` declares no button colours of its own. F-068 and F-046 both close, with the site
count reported against a scanner definition written into the spec rather than recalled.

## What's explicitly cut

- **A component library.** This is four button ranks with states, not a design-system rewrite. The
  moment it starts proposing a `Card` primitive it has left scope.
- **Inputs and borders.** The register measures 11 of 11 inputs already done and 60 of 76 borders
  still hand-themed. Borders are a real debt and a different one.
- **F-050.** Standing exclusion, unchanged: flipping it auto-deletes the contrast gate's exemption
  and reddens three built-in themes.
- **Anything in `rororo-ur-task`.** v1.19's plugin leg is unmerged and unverified. Two open
  cross-repo cycles at once is how both stall.

## Assumptions surfaced

Per the fully-autonomous contract, filled from the record. Each is a real fork `/spec` should confirm
or overturn.

- **Template-triggers ship before any migration** *(default — confirm)*. Migrating first means every
  migrated button still breaks on hover and nobody can see progress. Triggers first means the sites
  already migrated improve for free the moment they land.
- **`MainWindow.xaml` is its own item, probably its own two** *(default — confirm)*. 30 sites in the
  app's most-looked-at file, and the file where a regression is most visible.
- **The 22 files are walked in descending order of site count** *(default — confirm)*, so the cycle
  can stop anywhere and have banked the most debt per item.
- **A new rank is a finding, not a blocker** *(default — confirm)*. If the tail needs a fifth rank it
  gets added and recorded, rather than treated as scope creep — the four existing ranks were
  themselves discovered this way.

## Distribution audience

Unchanged: Pet Sim 99 clan first, Store second, Store parked. Worth saying that this is the first
cycle in four whose result is **visible on the main window on first launch** — v1.17 shipped a theme
most people will not switch to, v1.18 furnished a Settings page most people open twice, and v1.19 is
invisible unless you run a plugin. This one is the one they see.
