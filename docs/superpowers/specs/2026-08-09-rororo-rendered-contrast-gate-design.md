# Contrast gates: token pairs, then pixels

**Date:** 2026-08-09
**Motivation:** no automated check in this repo verifies contrast, and nothing renders XAML at all
**Related:** the glow campaign (`.vibe-glow/state.json`), F-031 (derived edge), F-032 (control labels),
F-050 (CTA contrast, open)

---

## Two phases, cheapest first

**Phase 1 — the token-pair gate.** Static. Scans app XAML for elements carrying both a
`Background` and a `Foreground` `DynamicResource`, resolves both under each theme, and asserts
WCAG ratios. Measured today: **44 elements across 18 files, collapsing to 9 distinct token
pairs.**

**Phase 2 — the rendered gate.** Instantiates each keyed control style, renders it, and measures
actual pixels. Covers what arithmetic structurally cannot.

### Why this order

| | Phase 1 | Phase 2 |
|---|---|---|
| elements covered | 44, via 9 pairs | 5 styles (~39 call sites) |
| cost | XAML scan + `RatioBetween` | STA runner, pixel sampling, DPI, glyph handling |
| reuses existing infra | yes — `XamlStyleScanner` | no, all new |
| flake risk | near zero | real; this repo already carries two timing-flaky tests |
| can surprise you | **no** — it is the same arithmetic the findings already use | **yes** — templates, failed resolution, alpha |

Phase 1 is roughly a quarter of the cost for nine times the element reach, and it **de-risks
Phase 2**: both need `ThemeService.ApplyTo`, so Phase 1 proves theme resolution works outside a
live `Application` before STA and pixels are stacked on top of it.

Phase 2 is not redundant after Phase 1. It is the only one that can contradict the token math,
and it is a capability this repo has never had.

### What Phase 1 finds today

Computed against the brand theme, all 9 pairs:

```text
fill                     text                      ratio    AA 4.5
CyanBrush                NavyBrush                 9.39:1   PASS  x22
MagentaBrush             WhiteBrush                3.79:1   FAIL  x8
RowExpiredAccentBrush    NavyBrush                 8.85:1   PASS  x3
NavyBrush                MutedTextBrush            6.88:1   PASS  x3
NavyBrush                WhiteBrush               16.66:1   PASS  x2
RowBgBrush               WhiteBrush               15.33:1   PASS  x2
RowBgBrush               CyanBrush                 8.64:1   PASS  x2
NavyBrush                CyanBrush                 9.39:1   PASS  x1
BgBrush                  WhiteBrush               16.66:1   PASS  x1
```

Exactly one failure, and it is already known: **F-050**, white on magenta, 8 sites. Phase 1's
value on day one is therefore not discovery — it is turning a documented finding into a live gate,
and stopping the next bad pair from landing. The `magenta-heat` and `flatline` themes are not
computed here and may surface more; the gate runs all three.

**This is where the exemption question actually lives** — see Phase 1's "Exemptions" below.

---

## Phase 1: the token-pair gate

### What it scans

Every element start tag in app XAML carrying **both** `Background="{DynamicResource X}"` and
`Foreground="{DynamicResource Y}"`. Reuses `XamlStyleScanner.EnumerateAppXamlFiles()`, the same
scaffolding the two existing fences use.

Deliberately narrow, and the narrowness is the point: when an element declares both halves of the
pair inline, the surface a reader contrasts against is stated in the markup and needs no inference.
Elements that inherit a fill from an ancestor are **out of scope for Phase 1** — determining what
is actually behind them is the composition problem, and guessing would produce confident wrong
numbers, which is worse than silence.

### What it asserts

For each distinct `(fill, text)` pair, under each of `brand`, `magenta-heat` and `flatline`:

**Foreground vs fill >= 4.5:1** — WCAG AA. The app's body text is 11px, so the large-text
allowance does not apply.

Ratios come from `ContrastGuard.RatioBetween(surface, candidate)`, the app's own function. Both
arguments are `#RRGGBB` strings, which is what the resolved theme slots already are, so Phase 1
needs no colour conversion at all. A `null` return means a slot did not parse and **fails the
test** rather than being coerced.

Themes resolve through the same `ThemeService.ApplyTo(ResourceDictionary, Theme)` seam Phase 2
needs, which is why building Phase 1 first de-risks Phase 2.

### Exemptions

`WhiteBrush` on `MagentaBrush` measures **3.79:1** and fails. Eight elements use it, and F-050 —
open — records that the best theme-derived foreground reaches only 4.40:1, still under AA. A flat
gate goes red on day one against a documented, hard finding.

So Phase 1 carries an exemption table where **each entry names the register row justifying it**,
plus a meta-test asserting:

> No exemption names a finding whose status is `clean`.

Fix F-050 and its exemption stops being valid, forcing removal and tightening the gate
automatically.

This is the part worth defending. An exemption list that outlives its justification is how a gate
quietly becomes decoration — the same drift the 2026-08-09 reconciliation pass existed to correct.
Tying exemptions to rows makes the gate a live tracker of contrast debt rather than something that
gets weakened whenever it complains.

**Exactly one exemption ships: `WhiteBrush` on `MagentaBrush`, citing F-050.** Every other pair
passes AA under brand. If `magenta-heat` or `flatline` surface further failures, each needs its own
register row before it may be exempted — a failure without a row is a bug, not debt.

### What Phase 1 does not cover

- **Inherited fills.** An element setting only `Foreground` is invisible here. That is most text in
  the app, and it is Phase 2's territory or nobody's.
- **Anything arithmetic cannot see.** Template overrides, failed `DynamicResource` resolution,
  alpha compositing. Phase 1 restates the token math faithfully; it cannot contradict it. That is
  the entire reason Phase 2 exists.
- **Styles.** Pairs declared in `ControlStyles.xaml` setters rather than on elements are Phase 2's.

### Phase 1 acceptance

1. `ThemeService.ApplyTo(ResourceDictionary, Theme)` exists; `Apply` delegates to it; existing theme
   tests stay green.
2. The scan finds 44 elements / 9 distinct pairs today, asserted as a floor so a broken scan fails
   loudly rather than passing vacuously.
3. All 9 pairs assert against all 3 themes via `ContrastGuard.RatioBetween`; a null return fails.
4. One exemption, naming F-050, and a meta-test proving an exemption that names a `clean` row fails.
5. Proven by watching it fail: point a passing pair at a failing token, confirm the failure names
   the pair, the theme and the ratio, then restore.

---

## Phase 2: the rendered gate

## Why

Every contrast ratio in every glow finding is **calculated from token values, never observed on a
screen**. The 1.00:1 that justified F-032, the 1.26:1 that justified F-031, the 3.79:1 in F-050 —
all arithmetic over theme JSON. That arithmetic has been right so far, but nothing checks it
against a pixel, and a `Setter` is not a pixel: a control template can override what a setter asks
for, a `DynamicResource` that fails to resolve falls back silently, and alpha composites.

The gap has cost real verification twice this week. F-001 shipped a menu whose rendering no test
covered; F-032 shipped label colours the same way. Both closed on a manual smoke, and the F-001
smoke was never recorded as run.

### What this is not

`XamlStyleIntegrityTests` argues against a construct-each-window smoke test, and it is right:

> WPF ResourceDictionaries create their resources LAZILY [...] Construct GamesWindow with an empty
> game library and it loads perfectly. Catching it that way needs every window AND populated data
> for every branch that renders a different template.

This design does not construct windows. It instantiates **each style directly**, which forces every
style to materialise rather than hoping a render path touches one, and needs no data setup. The
objection is answered by construction, not argued away.

## The seam

`ThemeService.Apply` reads `Application.Current?.Resources` (`ThemeService.cs:185`) and then calls
`ApplySlot(resources, …)` for eleven theme slots. `ApplySlot` already takes the dictionary as a
parameter; only the caller is bound to the live `Application`.

Extract the eleven calls:

```csharp
internal void ApplyTo(ResourceDictionary resources, Theme theme)
```

The existing path becomes `ApplyTo(Application.Current!.Resources, theme)`. No behaviour change.
Themes become resolvable in a test without an `Application`, which is the whole unlock.

## The harness

**A hand-rolled STA runner, not a package.** WPF visuals need an STA thread with a `Dispatcher`.
`Xunit.StaFact` would do it, but this repo is deliberately dependency-lean and ships to the
Microsoft Store with an auth-cookie threat model. A ~20-line helper — start an STA thread, pump a
`Dispatcher`, run a delegate, marshal exceptions back — costs less than the supply-chain surface.

Per style, per theme, the harness:

1. builds a fresh `ResourceDictionary` merging `App.xaml`'s brushes and `Controls/ControlStyles.xaml`
2. applies one theme through the new seam
3. instantiates the style's `TargetType`, applies the style, places it on a known surface
4. renders through `RenderTargetBitmap`
5. samples, and computes ratios with **`ContrastGuard.RatioBetween(surface, candidate)`** — the
   app's own contrast function, not a reimplementation. If the gate and the app ever disagree about
   what 3:1 means, that disagreement is itself worth failing on.

`RatioBetween` is `public static double?` and takes two `#RRGGBB` **strings**, returning null when
either will not parse. Sampled pixels are `Color` values, so the harness formats them to hex before
calling it. That is deliberate rather than reaching for the private `Ratio((double,double,double),
…)` overload: the public entry point is the one the app itself uses, it composites a translucent
candidate over the surface first, and a null return is a real signal the harness must assert on
rather than silently coerce to zero.

**Styles are enumerated from the markup**, not hand-listed, so a style added later is covered
automatically — or the enumeration's own count assertion fails and says so.

### Sampling

- **Fill** — the bitmap's modal colour. A large uniform region, robust to antialiasing.
- **Foreground** — render with a test string at a deliberately large font size and take the pixel
  furthest from the fill. The large size keeps glyph cores solid so antialiasing does not skew the
  sample. We are measuring colour, not layout, so the size is free.
- **Edge** — sampled from the border band.

## Classification: the seven styles are not alike

`Controls/ControlStyles.xaml` holds seven keyed styles. Asserting the same rule against all seven
would be wrong, and the file says so itself.

| style | target | treatment |
|---|---|---|
| `PrimaryButtonStyle` | Button | full: foreground vs fill, edge vs fill |
| `SecondaryButtonStyle` | Button | full |
| `SecondaryStrongButtonStyle` | Button | full |
| `AppTextBoxStyle` | TextBox | **surface-supplied** (see below) |
| `AppPasswordBoxStyle` | PasswordBox | **surface-supplied** |
| `CardBorderStyle` | Border | **excluded** — decorative |
| `SectionHeadingStyle` | TextBlock | **excluded** — prose, no fill of its own |

**Surface-supplied styles.** The input styles carry no `Background` setter, deliberately. Their own
comment:

> NO Background setter, and that is deliberate. Two fills are in use and both are right:
> `RowBgBrush` where the field sits on a card (Export, Import, Games) and `NavyBrush` where it sits
> on window chrome (Preferences). A field takes its fill from the surface behind it.

So "its own fill" does not exist for these. The harness places them on **each** of those two real
surfaces and asserts under both. That is not a workaround — it is a small, genuine piece of
composition coverage, because the file already tells us which two surfaces are legitimate.

**Exclusions.** `CardBorderStyle` is a `Border` drawing a card edge — a separator, and WCAG 1.4.11
does not govern separators. That is the same role split `InteractiveEdgeBindingTests` already
enforces, and this gate must not contradict it. `SectionHeadingStyle` is a `TextBlock`: prose, with
no fill of its own and no interactive boundary. Both exclusions are asserted as a closed list, so a
style added later cannot land in "excluded" by accident.

## The assertions

Two rules, both relationships rather than absolute colours — required by the glow invariant that
*"a finding that prescribes a color is invalid"*, since users ship their own JSON themes:

1. **Foreground vs fill >= 4.5:1.** WCAG AA. The app's body text is 11px, so the large-text
   exemption does not apply.
2. **Edge vs fill >= 3:1.** WCAG 1.4.11, for styles that draw one.

Against all three themes: `brand`, `magenta-heat`, and `flatline` — the adversarial theme authored
for this campaign, which is where colour-borne distinctions collapse.

### Phase 2 needs no exemptions, and that is a finding in itself

An earlier draft attached the exemption table to this phase, because F-050 measures the magenta
CTA at **3.79:1 brand / 2.99:1 flatline** and a flat AA gate would go red against a documented,
open finding.

**Verification moved it to Phase 1 instead.** The magenta CTAs do not use a keyed style. All 13
magenta-filled surfaces in the app — `MainWindow.xaml:57`, `:168`, `:376`, `:907`, `:1339`,
`:1383`, `GamesWindow.xaml:242`, `PluginsWindow.xaml:112`, `:218`, `:261`,
`PreferencesWindow.xaml:350`, `:418`, `SquadLaunchWindow.xaml:62` — set
`Background="{DynamicResource MagentaBrush}"` inline. None goes through `ControlStyles.xaml`.

So Phase 2 never sees them and ships with **zero exemptions**. Phase 1 does see them — that is
where the exemption for F-050 lives, and where the register-row machinery is built. Phase 2 simply
reuses it if it ever needs one.

### The real consequence: this gate is narrower than the problem

`ControlStyles.xaml`'s own comment says it was written because **63 hand-copied attribute sets
across 15 files** repeat the same themed attributes, "which is why they drifted and why every
previous fix had to be applied 63 times." Seven keyed styles exist today. The migration is
incomplete, and everything not yet migrated is invisible to this gate — including every CTA in
F-050, the finding most likely to motivate building it.

That is not a reason to skip it. A gate over the shared dictionary is exactly what makes migrating
the remaining sites *worth doing*, because each migration moves a control from unguarded to
guarded. But the spec would be lying if it implied the app's contrast risk is covered. It covers
the styles, and the styles are a minority of the surfaces.

**Stated plainly so nobody reads a green suite as "contrast is verified."**

## What this does not cover

Stated in the class doc, not left implied:

- **Composition.** Each style is measured in isolation (except the two surface-supplied inputs).
  This proves a style's own fill/foreground/edge relationship, not that a control sits on the
  surface a real page gives it.
- **Runtime brushes.** Anything produced by a converter, set from a view-model, or applied by a
  trigger at runtime is invisible here.
- **Layout.** Nothing about size, spacing, overlap or truncation. This is a colour gate.
- **Windows.** No window is constructed, by design — see "What this is not".

The manual smoke remains the only check on whole-page rendering. This narrows what it has to catch,
it does not replace it.

## Cost

One production seam (an extracted method, no behaviour change), one STA helper, one test file.
Roughly 5 styles x 3 themes plus the input styles on 2 surfaces each = about 21 measured cases,
expressed as a handful of xUnit theories rather than 21 hand-written facts.

## Acceptance

1. `ThemeService.ApplyTo(ResourceDictionary, Theme)` exists; `Apply` delegates to it; existing theme
   tests stay green.
2. A style renders under all three themes and produces a measurable fill, foreground and edge.
3. Both rules assert, using `ContrastGuard.RatioBetween`, and a null return fails the test rather than being coerced.
4. Phase 2 ships with zero exemptions, and a comment records why: the magenta CTAs that would have
   needed one are inline-styled, so they are Phase 1's to guard, not this phase's.
5. The style enumeration fails loudly if a style is added to `ControlStyles.xaml` and classified
   nowhere.
6. The gate is proven by watching it fail: force a style's foreground to its own fill, confirm rule
   1 fails and names the style and theme, restore.
