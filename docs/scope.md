# RORORO — v1.21 scope: the surfaces behind the buttons

**Cycle:** v1.21.0.0 · **Mode:** autonomous, two waves · **Branch:** `feat/pre-store-remediation`
**Predecessor:** v1.20.0.0 one button vocabulary, merged as PR #110
([spec](superpowers/specs/2026-08-11-rororo-button-vocabulary-design.md))

**Anchor:** v1.20 gave the app's buttons one vocabulary. Everything they sit *on* still writes its own
colours, and flatline is where that shows.

## Why now, and why this order

The Store cut needs screenshots. The five in `docs/store/screenshots/` are from **Aug 3** — before
v1.16's nav rail, v1.17's flatline, v1.18's Settings and v1.20's buttons. Every one shows an app that
no longer exists, so a full recapture is coming regardless.

`docs/ui-capture-checklist.md` shoots **flatline** deliberately: it is the adversarial theme where a
colour-carried distinction collapses, so it is the frame that exposes an un-themed surface. **The
rows in wave 1 are precisely what will look broken in those frames.** Capture first and the sequence
becomes shoot → notice → fix → reshoot. That is the whole sequencing argument: **fix, then shoot.**

## The problem, stated once

A themed app has one job under a theme it did not anticipate: keep meaning legible without relying on
hue. v1.17 built flatline to prove it, v1.20 fixed every button. What is left is the *chrome* —
banners, cards, list rows — and it is still writing hexes by hand. **62 hex literals across 10 XAML
files** at HEAD, which is the same count F-066 recorded and therefore the one number in this document
that has not moved.

## Verified at HEAD, 2026-08-11 — because the last cycle was nearly sized against fiction

Every row below was re-read against the tree before it entered this scope. v1.20 was scoped against a
register whose top finding was ~60% already shipped, and the fix for that was to stop trusting rows.
Two of the five wave-1 candidates changed shape under verification:

| Row | Register says | Tree says |
|---|---|---|
| F-085 | `MainWindow.xaml:1630-1644`, three literals | **Confirmed**, lines now `:1592,1593,1606` |
| F-066 | in-scope residue at `MainWindow:1474,1477` + `About:96` | **Reduced.** Those MainWindow lines no longer hold literals. Residue is `MainWindow:1535` (`#F1B232`) + `About:96` (`#15263A`) — two, both with existing tokens |
| F-063 | 8 literal brushes, unbound canvas, `:96` `#15263A` | **Confirmed**, 8 present; `:96` overlaps F-066 |
| F-065 | rows built on `RowBgBrush` alone | **Confirmed**, `SessionHistoryWindow.xaml.cs:155` |
| F-022 | 45 words, action buried last | **Partly stale.** The copy has been rewritten since the row; it now leads with impact rather than diagnosis, but the actionable clause is still last and it is still ~45 words |

**F-066 has largely collapsed into F-063.** After verification its only unique site is one literal in
the mutex-recovery region. It should be scoped as part of the same sweep and closed with it, not
carried as a separate row.

## THE QUESTION THIS CYCLE MUST ANSWER FIRST

**F-085 is not a rebind, and finding that out is what makes this a cycle rather than a chore.**

The app already has a themed banner recipe. `MainWindow.xaml:223-224` binds `RowExpiredBgBrush` as
the surface and `RowExpiredAccentBrush` as the boundary — v1.17 established that pair across expired
rows, idle chips and the compat banner. So the obvious move is to point the Bloxstrap banner at it
and be done.

**The Bloxstrap banner's own comment forbids that:**

> Warm amber tone distinct from the red-ish compat banner above it.

The literal was a decision, not an oversight. Both banners live in the same `Grid`, both are
independently visible, and **they can show at once.** Rebinding Bloxstrap onto the one warning pair
the app owns makes two banners that say different things look identical — trading a theming bug for a
meaning bug, which is the trade v1.20's `DestructiveButtonStyle` note argues against at length.

**Half of that fork closed on inspection, and the answer inverts the comment.** Read at HEAD: the
compat banner is `Grid.Row="4"` and **is** themed — `Background="{DynamicResource RowExpiredBgBrush}"`,
`BorderBrush` and text on `RowExpiredAccentBrush`. Bloxstrap is `Grid.Row="5"`, directly beneath it,
frozen. So:

- **Under brand** the two are genuinely distinct: bright amber border and text against olive border
  and cream text.
- **Under flatline** the compat banner goes grey (`#3D3D3D` / `#D4D4D4`) and Bloxstrap stays amber.

**The distinction survives flatline only because one banner ignores the theme.** It is not being
carried by a design decision any more; it is being carried by the defect. Fixing the theming
necessarily destroys it unless something replaces it — which is the real shape of this item, and it
is the opposite of "just rebind it."

What remains open is only *how* to replace it, and that is a measurement wave 1 item 1 owns:

1. **Does the distinction need to survive at all?** Two stacked banners that can co-show, saying
   different things, is a case for keeping them apart. Two that never co-show in practice is not.
2. **If it must survive, it comes from a derived value or a non-colour carrier, never a new slot.**
   Invariant 6 holds: every user theme on disk supplies ten slots and an eleventh breaks them all.
   The app already has the pattern for this — `InteractiveEdgeBrush` is derived by `ContrastGuard`
   from what a theme supplies, and v1.17's own answer to "colour cannot carry this" was the `▲`
   glyph and a left rule, both of which survive greyscale.

The likely shape, stated so the spec can disagree with something concrete: **one themed warning
recipe for both banners, with the distinction re-carried by a non-colour difference** rather than by
a second hue the theme contract cannot supply.

## Wave 1 — the surfaces flatline exposes

Everything here is visible in a Store screenshot.

- **F-085** (2/2) Bloxstrap banner, three literals, no token matches them. Blocked on the question
  above. **Its stated dependency cleared this cycle** — the row says it "belongs with F-068's shared
  button/banner style work", and F-068 closed in v1.20.
- **F-063** (2/2) About: 8 literal `SolidColorBrush` resources, an unbound canvas, and `#15263A`
  where `RowBgBrush` holds that exact value. Flatline renders "a hard dark rectangle behind the icon".
- **F-065** (2/2) History rows are `RowBgBrush` alone and vanish under flatline. The date-group
  heading survives, so the fix is a non-fill boundary, not a new fill.
- **F-066** (2/2) reduced to two rebinds; closes with F-063.

## Wave 2 — polish, copy, and the gate that unblocks F-050

- **F-086** (3/1) The contrast gate measures **no `MutedTextBrush` pair at all** — ~113 bindings
  unmeasured. A small named-pair list closes it. **This is F-050's prerequisite:** F-050's fix cannot
  be verified by a gate that cannot see its pairs, which is exactly how it came to look resolved.
- **F-087** (1/1) A colour branch in C# (`ConsentSheet.xaml.cs:90-92`) that belongs in XAML as a
  `DataTrigger`. Verified present and unchanged.
- **F-021** (2/2) Games empty state sends the user to a closed window for something that saves itself.
- **F-022** (2/2) FPS banner: re-measure first, the copy moved since the row was written.
- **F-070**, **F-074** (2/2) copy.
- **Rulings, not builds:** **F-095** is fixed and open only for its surfacing half; **F-098** is
  partly fixed. Both need a decision recorded rather than work.

## Explicit cuts

- **F-052 — borders, 60 of 76 controls, all 26 XAML files.** Reads 4/2 and is not a 2-effort job.
  v1.20's checklist deferred it by name as its own cycle and that call stands.
- **F-050.** Needs F-086 first. Taking it now means fixing something the suite cannot confirm.
- **The other ~55 hex literals**, which sit in out-of-scope modals. This cycle takes the surfaces a
  screenshot shows; the rest is the same sweep on a later pass.
- **No new theme slot.** Invariant 6 holds — anything this cycle needs is derived from what a theme
  already supplies.
- **The screenshot recapture itself.** It follows this cycle; it is not in it.

## What must not happen

- **Do not collapse the two banners' distinction without measuring it.** That is the cycle's opening
  item, and a wrong answer there is a meaning bug shipped to fix a theming bug.
- **Do not change a surface's resting look** except where the row is the resting look being wrong.
- **Do not lower a contrast floor** to make a rebind fit. Change the recipe.
- **Do not let a copy row ship on its register text.** F-022 already drifted; re-read before rewriting.
