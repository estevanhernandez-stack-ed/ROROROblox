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

## The hard part: a floor that survives a user theme

Invariant 6 forbids the obvious fix. `Theme` has exactly ten required slots and
every user theme on disk supplies all ten; an eleventh "boundary" slot breaks
every one of them unless it defaults.

And a default is not enough on its own. All three built-ins set `Navy == Bg`, so
that pattern reads to a theme author as *intended*. Any new token would inherit
the same collapse the moment someone copies the shape of a built-in.

**So the boundary is derived, not declared.** A `ContrastGuard` takes the surface
brush and returns a boundary colour nudged until it clears 3:1 against that
surface — lightening on dark fields, darkening on light ones.

Three properties that make this the right shape:

- **No new token.** Invariant 6 is satisfied by not extending the contract at all.
- **Every existing user theme is fixed on next launch**, without the author
  touching their JSON.
- **It cannot be flattened.** The guarantee is computed from whatever the theme
  supplies, so there is no value a theme can set that defeats it.

This replaces F-025's suggested "accent-set outline or geometry". Geometry would
also survive, but it changes how every secondary control looks in order to fix a
contrast bug — a bigger visual change for a narrower gain.

## What the dictionary owns

A merged `ResourceDictionary`, one file, keyed styles:

- `SecondaryButtonStyle` — the recipe at the centre of F-025/F-030/F-031
- `PrimaryButtonStyle` — already passes; formalised so it stops being hand-copied
- `AppTextBoxStyle` — F-029's one input system
- `CardBorderStyle` — the setting-level container
- `SectionHeadingStyle` — F-026's missing level *above* the card

F-026 is a structure fix, not a colour one: a section and a setting currently
render at identical weight, so eight sibling cards read as eight peers when three
of them are sections containing up to 13 controls. The heading style supplies the
level that never existed.

`PageHeader` (wave 4) already lives as a control and stays one — it has logic.

## Staging — three commits, each independently reviewable

1. **Dictionary + `ContrastGuard` + tests.** No call sites converted. The only
   visible change is that secondary boundaries become legible.
2. **Buttons** — `Primary`/`Secondary` applied across ~dozens of sites (F-025,
   F-030, F-031).
3. **Containers and inputs** — `CardBorder`, `SectionHeading`, `AppTextBox`
   (F-026, F-029, C5).

Stopping after any stage leaves the app coherent.

## Verification

- **Unit:** `ContrastGuard` returns ≥3:1 for every built-in theme, for a
  pathological all-one-colour theme, and for a light-field theme (it must darken
  rather than lighten). Plus a test asserting **every built-in theme's derived
  boundary clears 3:1** — the check that did not exist and would have caught this
  years ago.
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
