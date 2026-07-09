# Smoke checklist — default private server + Library cleanup

**Branch:** `feat/default-private-server` · **Spec:** [`../specs/2026-07-07-default-private-server-design.md`](../specs/2026-07-07-default-private-server-design.md)

The store + ordering logic is unit-tested; the two windows are manual-smoke by house convention.

## Setup
- [ ] Quit the installed RoRoRo from the tray (single-instance guard).
- [ ] Build + run the dev build; have 2+ saved private servers and 1+ saved game.

## Library (Games button → RoRoRo / Library)
- [ ] **1. Set default:** server row → **Set default** → cyan DEFAULT badge appears next to PRIVATE, row gets a cyan border, its Set-default button becomes **Clear default**.
- [ ] **2. Switch:** Set default on a second server → badge + border MOVE (exactly one default).
- [ ] **3. Clear:** **Clear default** → no badge anywhere; button flips back to Set default on all rows.
- [ ] **4. Game default untouched:** the game marked DEFAULT keeps its badge through all of the above; Launch As still goes to the default game.
- [ ] **5. Rename the default server** → badge survives. **Remove the default server** → default gone (no other server promoted).
- [ ] **6. Layout:** with 1 game + 1 server, sections sit directly under each other — no dead gap. Header text wraps (no clipping) and mentions both defaults. Many rows → one shared scrollbar.
- [ ] **7. Theming:** switch to a custom theme → badge/border/buttons recolor with the theme (no stuck default-palette colors).

## Squad Launch (Private server toolbar button)
- [ ] **8. Pre-selection:** with a default set, it lists FIRST with a cyan border + DEFAULT tag; Launch all on it works.
- [ ] **9. No default:** clear the default → order returns to most-recent-first, no highlight (today's behavior).

## Result
- [ ] All pass → merge-ready. Anything off → note the check + what you saw; fix pass before merge.
