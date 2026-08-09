# F-032 — control labels stop borrowing the prose token

**Date:** 2026-08-09 · **Finding:** F-032 (AX-7), register score 4/5 · **Wave:** glow, post-6
**Register:** `docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`
**Sibling shipped:** F-031 (AX-5) — `InteractiveEdgeBrush`, the ≥3:1 derived control boundary

---

## What is actually wrong

`SecondaryButtonStyle` sets `Foreground` to `MutedTextBrush` — the same token helper prose uses.
Eighteen buttons inherit it, and five more controls bind it inline.

**This is not a legibility failure, and the spec should not claim one.** Measured against the
default brand theme:

```text
White on Navy  : 16.66:1
Muted on Navy  :  6.88:1   <- the current secondary label, passes WCAG AA comfortably
Muted vs White :  2.42:1   <- the defect
```

The defect is semantic. Control labels and explanatory prose share one token, and the separation
between that token and the primary one is 2.42:1 in brand and **1.00:1 under the adversarial
flatline theme**. Under flatline nothing distinguishes a control's label from the paragraph beside
it. The register states this precisely — *"C7's collapse and C6's affordance loss share one root
cause"* — and the fix is to stop making colour carry a job it cannot carry under a theme the app
explicitly supports.

## Why this is now cheap

**F-031 already shipped the replacement affordance.** `InteractiveEdgeBrush` is derived by
`ContrastGuard` from whatever the active theme supplies and is guaranteed ≥3:1 under any theme,
including user-authored ones. It sits on *both* secondary styles today. The thing that marks a
control as a control already exists and does not depend on colour.

**Wave 6 centralised the recipe.** Before `Controls/ControlStyles.xaml`, this was 63 hand-copied
attribute sets across 15 files. Now it is two setters. The same wave that spread the defect — the
count went 11 → 15 between the audit and the 2026-08-09 reconciliation — is the wave that made it
a one-line fix.

**Wave 6 also kept the muted label on purpose,** and that deserves stating rather than quietly
overriding. Its comment reads: *"Folding it into the quiet secondary would have fixed the contrast
bug by demoting a dialog's main action to muted text, which is a legibility trade nobody asked
for. Three recipes existed; three styles name them."* That reasoning was sound about
`SecondaryStrong`. What it did not settle is whether *quiet* secondary should carry its quietness
in colour. This spec says no, and moves it to weight.

## The change

### Three recipes stay three recipes

Distinguished by border and weight instead of by label colour:

| style | fill | label | edge | weight | call sites |
|---|---|---|---|---|---|
| `PrimaryButtonStyle` | Navy | White | **Cyan** | (unset) | — |
| `SecondaryStrongButtonStyle` | Navy | White | InteractiveEdge | **SemiBold** | 16 |
| `SecondaryButtonStyle` | Navy | White | InteractiveEdge | **Normal** | 18 |

Two edits in `Controls/ControlStyles.xaml`: `SecondaryButtonStyle`'s `Foreground` becomes
`WhiteBrush`, and **both** secondary styles gain an explicit `FontWeight`.

Explicit on both is deliberate. Leaving quiet secondary's weight implicit means it inherits
whatever the ambient default happens to be, and the distinction this whole change rests on stops
being stated anywhere a reader can find it.

**Verified safe:** no call site of either style sets `FontWeight` inline, so the style setters win
cleanly. In WPF a local value beats a style setter, so this was checked rather than assumed.

### Five inline control labels

Controls that bypass the shared styles and bind the token directly:

| site | control |
|---|---|
| `MainWindow.xaml:579` | `×` dismiss button |
| `MainWindow.xaml:1233` | "Manage games…" |
| `MainWindow.xaml:1798` | Compact toggle |
| `PreferencesWindow.xaml:313` | webhook "Show" toggle (mine) |
| `PreferencesWindow.xaml:337` | webhook "Show" toggle (clan) |

All five are unambiguously control labels; none is prose. Each moves to `WhiteBrush`.

### What does NOT change

**The ~104 prose uses of `MutedTextBrush`.** Helper text, empty states, `"Follow:"`, memory chips.
Muted is correct there, and the distinction between prose and control label is the entire finding.
A change that swept the token app-wide would delete the app's secondary-text vocabulary to fix a
problem prose does not have — the same mistake `InteractiveEdgeBindingTests` documents its own
wave nearly making with `DividerBrush`.

## The fence

Four clauses in `src/ROROROblox.Tests/MutedTextFenceTests.cs`, mirroring
`InteractiveEdgeBindingTests`. Reuses `XamlStyleScanner.EnumerateAppXamlFiles()`.

**1. No interactive element binds `MutedTextBrush` as `Foreground` inline.** The discriminator is
control-by-type **or** control-by-behaviour: `Button`, `MenuItem`, `ToggleButton`, `CheckBox`,
`RadioButton`, `ComboBox`, `ComboBoxItem`, `ListBoxItem`, `TabItem`, `Hyperlink`, `TextBox`,
`PasswordBox`, `Slider`, `ui:ToggleSwitch` — plus anything `IsInteractive` already catches.

**2. No control style sets `MutedTextBrush` as `Foreground`.** The clause that matters most, and
the one clause 1 structurally cannot provide: **a `<Style>` is not an interactive element.** An
inline-only fence sails straight past `SecondaryButtonStyle` — which is exactly how wave 6 baked
this into the shared dictionary while a contrast-focused wave was in flight. The failure mode was
centralisation, so the fence has to watch the centre.

**3. Controls built in code-behind do not resolve `MutedTextBrush` for their foreground.**
`ControlStyles.xaml` names two the styles cannot reach — `CaptionColorPickerWindow`'s palette
swatches and `SquadLaunchWindow`'s Remove — and states plainly that a change there does not reach
them. `InteractiveEdgeBindingTests` already carries this clause for borders. Without it, the two
known style-proof controls are the obvious place for this to regrow.

**4. The fence sees the app it claims to.** Assert the scan found a substantial number of
`MutedTextBrush` bindings overall (≈109 today; assert a floor comfortably under that, e.g. 50). A
regex that silently matches nothing passes clauses 1–3 while checking nothing. Same guard as
`TheFenceSeesTheAppItClaimsTo` and `CommandBindingIntegrityTests`.

**Prose stays explicitly legal.** The fence asserts a role boundary, not a token ban.

### Shared discriminator

`IsInteractive` currently lives private inside `InteractiveEdgeBindingTests`. It moves to
`XamlStyleScanner`, which already hosts the scan helpers both fences use, and both call it.

Two fences needing the same "is this a control?" judgement is precisely where a second, subtly
different copy gets written — and a role-based rule that disagrees with itself between two tests
is worse than either rule alone. This is a small refactor of code the change already touches, not
opportunistic restructuring.

### Proving it

The fence is proven by watching it fail: revert `SecondaryButtonStyle`'s `Foreground` to
`MutedTextBrush`, confirm clause 2 fails and names it, restore. A gate nobody has watched fail is
a gate nobody knows works — and this repo has already lost three months to a bug hiding behind a
green suite.

## Deviation from the register's fix direction

The register's fix reads: *"Bind control labels to the primary text token; carry 'secondary' in
weight + the AX-5 boundary; extend Preferences' indent device to the main window."*

The first two clauses are what this spec builds. **The third is dropped.** No "indent device"
exists in `PreferencesWindow.xaml` or anywhere else in the tree — the string appears in no source
file, and F-032 is the only register row that mentions it. Inventing one to satisfy the sentence
would be building from fiction, which is the precise failure the 2026-08-09 reconciliation pass
existed to correct.

Recorded here rather than silently omitted, per the register rule set in `CLAUDE.md`.

## Testing

New: `MutedTextFenceTests` (4 clauses above).
Changed: `InteractiveEdgeBindingTests` calls the relocated `IsInteractive`.

Suite must stay green at its current count plus the new tests. No behavioural test can prove the
labels *look* right — nothing in this repo loads XAML, a position
`XamlStyleIntegrityTests` argues at length. The fence proves the role boundary holds; a human
confirms the render.

## Acceptance

F-032 closes when:

1. Both secondary styles bind `WhiteBrush` and carry an explicit `FontWeight`.
2. The five inline control labels bind `WhiteBrush`.
3. The four fence clauses pass, and clause 2 has been watched failing.
4. `IsInteractive` has one definition, used by both fences.
5. The prose uses of `MutedTextBrush` are untouched — a diff that changes ~104 sites is wrong.
6. **The F-032 row is flipped to `clean` in the same PR** (`CLAUDE.md`, Findings register).
