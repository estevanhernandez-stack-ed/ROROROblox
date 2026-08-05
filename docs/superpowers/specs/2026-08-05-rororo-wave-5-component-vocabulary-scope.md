# Wave 5 scope — the component vocabulary

> Six findings that are one finding: **grouping and affordance are carried by a
> fill, and the fill is the same colour as what it sits on.**

## Batch — 6 findings + a convention

| id | sev × vis | what it is |
|---|---|---|
| F-035 | 5 × 3 | `App.xaml` has 10 brushes, 9 converters, and **one** keyed control style |
| F-025 | 5 × 5 | 8 secondary controls rest on a border alone; all collapse under a flat theme |
| F-030 | 4 × 5 | four secondary-button recipes; the common one binds `Navy` == `Bg` |
| F-031 | 4 × 5 | that boundary measures ~1.2:1 against WCAG's 3:1 floor |
| F-029 | 4 × 5 | two input systems — 11 plain `TextBox` vs 2 `ui:TextBox` |
| F-026 | 4 × 5 | one card primitive carrying "section" and "setting" at identical weight |
| C5 | — | grouping is a filled card and only a filled card |

F-035 is the mechanism the other five need. It has the lowest visibility score in
the batch and is the reason the rest are expensive.

## The measurement this wave is justified by

Computed from the verbatim slot values in `ThemeStore.cs:202-250`. WCAG 1.4.11
requires **3:1** for a non-text interactive boundary.

| theme | Navy == Bg | secondary edge (Divider on Navy) | primary edge (Cyan on Navy) |
|---|---|---|---|
| **brand** (shipped default) | yes | **1.26:1 — FAIL** | 9.39:1 pass |
| midnight | yes | **1.16:1 — FAIL** | 8.05:1 pass |
| magenta-heat | yes | **1.14:1 — FAIL** | 4.90:1 pass |

Not a flatline artifact — it ships in the default theme today, at a fifth of the
required contrast. Not "the palette is too dark" — the primary recipe passes at
9.39:1 in that same theme. **One recipe, never contrast-checked**, on the nav
band, Remove, Reroll all identities, and 8+ other buttons.

## REVISED 2026-08-05 — the first version of this section was wrong

> Este's objection: *"we're going to have users that may have already created
> their own themes. So we need it to be friendly so that their theme doesn't
> change too much."* Testing that objection is what found the real bug. The
> original plan is left below the correction, because the reasoning matters.

**What the original plan actually produces**, computed rather than assumed:

| theme | `Divider` as authored | after guarding |
|---|---|---|
| brand | `#1F3149` — dark navy hairline | **`#5E6B7C`** — mid grey |
| midnight | `#162232` | **`#5A626D`** |
| magenta-heat | `#2D1832` | **`#6C5D70`** |

Applying that would repaint every row separator, every card edge and every
divider in the app from a dark hairline to mid grey. That is not a contrast fix,
it is a different theme — and it would do it to every user theme on disk without
asking.

### The real bug: `Divider` has two jobs

Working out why the number came back so extreme is what found it. `Divider` is
used as:

1. a **decorative separator** between rows and around cards — where a faint
   1.26:1 hairline is correct, intended, and exactly what an author chose; and
2. the **boundary of an interactive control** — where WCAG 1.4.11 requires 3:1.

**1.4.11 governs UI component boundaries, not decorative separators.** Treating
one token as though it had one job is what made the fix look ten times more
invasive than it is. The original section's error was not the derivation — it was
applying it to a token that is mostly not an interactive boundary.

### The corrected shape

**Secondary controls stop using `Divider` as their only affordance.** A derived
`InteractiveEdgeBrush` is computed by `ContrastGuard` from the surface, and is
consumed *only* by interactive control styles.

- **`Divider` is untouched.** Row separators and card edges render exactly as
  every author wrote them. Zero change where people would notice most.
- **The visible change is confined to control borders** — precisely where the
  affordance was missing and where 1.4.11 actually applies.
- **Still no new token**, so invariant 6 holds and no user theme file changes.

`ContrastGuard` survives unchanged, including its twelve tests. It now supplies
one derived brush instead of overriding a shared one.

This still replaces F-025's "accent-set outline or geometry": geometry would
survive a flat theme too, but it restyles every secondary control to fix a
contrast bug, which is the same over-reach in a different costume.

### Remediation on first launch — Este's ask, narrowed to fit

Even confined to control borders, a user theme's buttons will look different
after this update. That is a fix, not a break, but it is still their theme
changing without being asked.

On first launch after the update, when **the active theme is a user theme** whose
interactive edge had to be derived:

- one dialog, once, explaining what changed and why, with a before/after swatch;
- a choice — **use the accessible edge** (recommended, default) or **keep my
  theme exactly as authored**;
- the answer remembered *per theme*, so switching themes can re-ask but the same
  theme never asks twice.

Built-in themes get no dialog. They are ours, the bug is ours, and asking
permission to fix our own defect is theatre.

Declining is honoured — it is their theme. The dialog is where accessibility and
authorship are reconciled, rather than one silently overruling the other.

---

<details>
<summary>Original section, superseded — kept because the reasoning is why the correction exists</summary>

Invariant 6 forbids the obvious fix. `Theme` has exactly ten required slots and
every user theme on disk supplies all ten; an eleventh "boundary" slot breaks
every one of them unless it defaults.

And a default is not enough on its own. All three built-ins set `Navy == Bg`, so
that pattern reads to a theme author as *intended*. Any new token would inherit
the same collapse the moment someone copies the shape of a built-in.

**So the boundary is derived, not declared.** A `ContrastGuard` takes the surface
brush and returns a boundary colour nudged until it clears 3:1 against that
surface — lightening on dark fields, darkening on light ones.

*Correct as far as it went. The unexamined step was assuming the derived value
should replace `DividerBrush` app-wide.*

</details>

## What the dictionary owns

A merged `ResourceDictionary`, one file, keyed styles:

- `SecondaryButtonStyle` — the recipe at the centre of F-025/F-030/F-031, and the
  only style that binds `InteractiveEdgeBrush`
- `PrimaryButtonStyle` — already passes; formalised so it stops being hand-copied
- `AppTextBoxStyle` — F-029's one input system
- `CardBorderStyle` — the setting-level container. Binds **`DividerBrush`**, not
  the derived edge: a card is not an interactive control, and this is where the
  revision above stops the change from spreading
- `SectionHeadingStyle` — F-026's missing level *above* the card

Plus one derived resource, `InteractiveEdgeBrush`, recomputed by
`ThemeService.ApplySlot` on every theme change and consumed by interactive styles
only. It is a resource, not a theme slot — nothing new enters the JSON contract.

F-026 is a structure fix, not a colour one: a section and a setting currently
render at identical weight, so eight sibling cards read as eight peers when three
of them are sections containing up to 13 controls. The heading style supplies the
level that never existed.

`PageHeader` (wave 4) already lives as a control and stays one — it has logic.

## Staging — three commits, each independently reviewable

1. **`ContrastGuard` + tests.** Pure logic, no call sites, nothing visible.
   **Done — `a42ca77`.**
2. **Dictionary + `InteractiveEdgeBrush` wired into `ThemeService.ApplySlot`.**
   Still no call sites converted; the brush exists and updates on theme change,
   but nothing consumes it yet. Nothing visible.
3. **Buttons** — `Primary`/`Secondary` applied across ~dozens of sites (F-025,
   F-030, F-031). **The first stage anything changes on screen**, and the change
   is confined to control borders.
4. **The remediation dialog** — first-launch detection, before/after swatch,
   per-theme answer. Ships with or immediately after stage 3; it must not land
   *after* a user has already been surprised.
5. **Containers and inputs** — `CardBorder`, `SectionHeading`, `AppTextBox`
   (F-026, F-029, C5).

Stopping after any stage leaves the app coherent. Stages 1 and 2 are invisible by
construction, which is deliberate: the widest-blast-radius mechanism lands and
gets reviewed before anything depends on it.

## Verification

- **Unit:** `ContrastGuard` returns ≥3:1 for every built-in theme, for a
  pathological all-one-colour theme, and for a light-field theme (it must darken
  rather than lighten). Plus a test asserting **every built-in theme's derived
  boundary clears 3:1** — the check that did not exist and would have caught this
  years ago. **Done: twelve tests in `a42ca77`.**
- **Unit, added by the revision:** a test that `CardBorderStyle` and every
  separator bind `DividerBrush` and **not** `InteractiveEdgeBrush`. The whole
  correction above is the claim that the derived edge stays off decorative
  surfaces; that claim needs a guard, or the next person to reach for a visible
  border will quietly undo it.
- **Static:** the XAML Style scanner (merged `9102a40`) already fails the build on
  a style that cannot construct. That safety net did not exist when the last
  shared-control bug shipped; it covers this wave's largest risk.
- **Runtime:** captures under `brand`, `magenta-heat`, `flatline`. Flatline is the
  proof — under a one-colour theme every secondary control must still show an
  edge.

## Risks

- **Widest blast radius in the campaign.** A shared dictionary is inherited by
  every window; a mistake in it is a mistake everywhere at once. Mitigated by the
  Style scanner and by staging.
- **A derived colour is computed per theme change**, so `ThemeService.ApplySlot`
  gains work on the theme-switch path. It runs once per switch, not per render.
- **Converting a button changes its metrics.** Padding and font sizes were
  hand-copied and are not uniform; consolidating will move some buttons by a few
  pixels. That is the point, but it means per-theme captures matter more than
  usual.
- **Scope creep back toward the original plan.** The correction above holds only
  as long as `InteractiveEdgeBrush` stays confined to interactive controls. The
  pull to "just use the visible one everywhere, it looks cleaner" is exactly how
  the first version of this scope went wrong, and it would silently repaint every
  user's theme. That is what the new binding test is for.
- **The dialog is the one place this wave touches consent, not pixels.** Getting
  it wrong — nagging, or changing a theme without asking — costs more trust than
  the contrast bug costs accessibility. It ships with stage 3 or not at all.
