# RORORO — Scope: flatline, the readable theme

Cart cycle on an established app. Substantive design will land in a spec authored during `/spec`;
this doc is the scope contract that spec answers to.

Cycle history: v1.1 scope (multi-instance + saved-account quick-launch) in
[`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md). v1.2
per-account FPS limiter, v1.3.x default-game widget + local rename, v1.4 plugin system, v1.7.0
install deferral. Most recent: the UIA capture tool, merged as PR #102.

## Idea

Ship `flatline` as a real, user-selectable built-in theme: the one that carries no meaning in colour.
One background, one text colour, one accent, maximum legibility. If you cannot distinguish hues, or
you are on a bad panel in bright sun, this is the theme that still works.

## Who it's for

The Pet Sim 99 clan member running eight alts, same as always. Specifically the slice of them who
cannot rely on colour: roughly 8% of men have some form of colour vision deficiency, and RORORO's
entire status vocabulary is currently hue. The cyan-versus-magenta tray ring, the magenta MAIN badge,
the amber expired-session row, the coloured caption swatches. Every one of those says something, and
says it only in colour.

Secondary user, honestly: this project. flatline is the theme that makes colour-only signalling
visible, which is why the glow campaign wanted it.

## The ruling that shapes this cycle

flatline was conceived as an adversarial instrument, authored to make the app fail measurably by
collapsing colour distinction. Este ruled it ships as a **product theme** instead: the accessible one,
sold on legibility rather than on degradation.

That is the better product and it changes the design goal from "collapse distinction" to "carry
distinction without colour." Those are not the same theme, and the difference is the crux of this
cycle.

## In scope

- **`flatline` as a fourth built-in** in `ThemeStore`, `IsBuiltIn = true`, alongside brand, midnight
  and magenta-heat. Hardcoded like the others, no filesystem dependency.
- **A palette that clears the contrast gate rather than dodging it.** See "The gate relationship"
  below. `/spec` picks exact hex values and proves them by running
  `ContrastPairGateTests`.
- **Non-colour redundancy for every status the theme flattens.** This is the real work of the cycle
  and the reason it is not a fifteen-minute palette commit. Where the app says something in hue
  alone, flatline needs that meaning carried some other way: text, shape, weight, position or icon.
  `/spec` enumerates the affected surfaces; the audit register's own colour-only findings are the
  starting list.
- **An adversarial `flatline-lab` test fixture**, NOT a built-in and NOT user-selectable. Preserves
  the campaign's evidence and does something better besides: fed to the contrast gate, it must FAIL.
  A gate that has only ever seen passing themes is an unproven gate.
- **Register reconciliation.** F-031, F-032 and F-050 quote ratios measured from a theme that never
  existed in git. Their rows get updated to say which artifact reproduces which number.

## What's explicitly cut

- **A theme-authoring UI for accessibility presets.** The "+ Build a theme..." builder already
  exists and user themes are JSON. flatline is a built-in, not a new authoring surface.
- **Auto-detecting colour vision deficiency, or any OS high-contrast-mode integration.** Windows has
  its own high-contrast setting; honouring it is a separate cycle with its own semantics. flatline is
  a RORORO theme the user picks, nothing more.
- **Reworking the brand theme.** flatline is an alternative, not a correction. Brand stays cyan
  `#17d4fa` + magenta `#f22f89` on navy `#0f1f31`, always paired, per the canonical design system.
- **Fixing every colour-only signal app-wide.** flatline must carry its own surfaces. Where a fix is
  genuinely theme-independent it can ride along, but this cycle is not the colour-redundancy sweep
  for the whole app. That is register work.
- **Retiring the adversarial theme concept.** It survives as `flatline-lab`, a fixture. Cutting it
  entirely would strand three findings' evidence.
- **Shipping it as the default.** Brand stays default.

## The gate relationship, resolved

`ContrastPairGateTests.EveryDeclaredPairClearsAaUnderEveryTheme` measures every declared
Background/Foreground token pair at 4.5:1 across every built-in, with one exemption: `WhiteBrush` on
`MagentaBrush`, citing F-050, floor 3.20 against a worst measured value of 3.2858 in magenta-heat.

The apparent conflict, that an adversarial theme would redden the gate, does not survive contact with
what the gate actually measures. It measures **foreground against its own fill**. What flatline
collapses is **distinction between semantic elements**: cyan versus magenta as two accents, row
backgrounds against each other, the expired-row amber against the ordinary row. The gate never
measures either of those. They are orthogonal.

So flatline passes by construction, with one real constraint: its single accent is used as a fill
with white text on it, so **white-on-accent must clear the 3.20 exemption floor**, and should target
the full 4.5:1 so the exemption is not needed at all. Under the product-theme ruling that is not a
compromise, it is the goal.

**No gate change, no new exemption, and specifically no concept of a theme that is exempt from
measurement.** That last one was considered and rejected: an "adversarial themes skip the gate" escape
hatch is exactly where a real regression would hide.

## What "done" looks like

A clan member opens Settings, Appearance, picks Flatline, and the app is entirely usable without
seeing a single hue. Every status that was colour is now also words or shape. `dotnet test` is green
including the contrast gate. `scripts/capture-ui.ps1` picks the theme up with no code change and
produces a fourth round of evidence, 56 captures instead of 42. And `flatline-lab` proves the gate
fails when it should.

## Loose implementation notes

Non-binding, refined in `/spec`.

- The `Theme` record's slots collapse naturally: `Bg`, `Navy` and `RowBg` toward one surface value,
  `Cyan` and `Magenta` toward one accent, `White` and `MutedText` toward legible text. `MutedText`
  cannot simply equal `White` — F-032's 1.00:1 is precisely that failure. It needs to stay
  distinguishable from body text while remaining legible on the surface, which is a genuine constraint
  rather than a slot to zero out.
- `RowExpiredBg` and `RowExpiredAccent` are the sharpest colour-only signal in the app. Expired
  sessions are currently amber and nothing else.
- `ThemeService.ApplyTo` derives `InteractiveEdge` via `EdgeRemediation.Decide`. A theme with one
  accent and one surface may trigger the edge-remediation prompt on first selection. Worth checking
  early: a built-in that asks the user a question the moment they pick it is a bad first impression.
- The capture tool matches theme ids on the substring `Id = <id>,`, so the id stays `flatline` for
  tooling continuity regardless of the display name.

## Assumptions surfaced

Per the fully-autonomous contract, these were filled from the record rather than asked.

- **Display name stays "Flatline"** *(default — confirm on next interactive run)*. Este coined it and
  it describes flattening colour rather than flattening a patient. The accessibility promise is
  carried by the theme's description text, not its name. If it reads as "broken" to a clan member,
  `/prd` is the place to change it; the id must stay `flatline` either way.
- **Not the default theme** *(default — confirm on next interactive run)*. Nothing in the record
  suggests replacing brand as the default, and brand is the product's identity.
- **Scope excludes the app-wide colour-redundancy sweep** *(default — confirm on next interactive
  run)*. flatline carries its own surfaces; the rest is register work with its own 51 open rows.

## Distribution audience

Unchanged: Pet Sim 99 clan first, Microsoft Store second. Worth noting for the Store listing that an
accessibility theme is a listing asset rather than a footnote, and it is the kind of thing Store
reviewers read favourably.
