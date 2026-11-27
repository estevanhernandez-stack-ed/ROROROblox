# C1: the picks for "Roblox at a glance"

Items 11 and 12 (the line, the Library and the panel) build to the picks below and screenshot-compare against them before they close. Nothing else waits on this page. Items 1 to 10 and 13 run without it.

Open `c1-mocks.html` in a browser. The buttons at the top switch between the four built-in themes (Brand, Midnight, Magenta Heat) and Flatline; `?theme=brand|midnight|magenta-heat|flatline` picks one from the address. `c1-brand.png` and `c1-flatline.png` are full-page captures of the same page. Every account, game and server in the mocks is made up, and the contested, known-issue and drift sentences are stand-ins. The five notice sentences and the pulse strings are the spec's own (§7.4).

## The picks

| # | Decision | Options | Plan default |
|---|---|---|---|
| A | Tone of the line | A1 contested, outage and drift in the warning colour, known issue and update in white · A2 all five in the warning colour | A1 (spec §7.1) |
| B | The "+N more" control | B1 panel-expander toggle (open is a cyan outline) · B2 secondary toggle (open is a grey fill) | B1 (spec §7.3) |
| C | The pulse in the grid tile | C1 two more meta lines, the tile grows · C2 one line that wraps · C3 count in the tile, date in the tooltip and the list | C1 (assumption 28) |
| D | List row and panel entry | the joined line "81,827 playing · updated 2 days ago" | as drawn |
| E | A private server | the date, no count | as drawn |
| F | The update notice at 380 px | drops the batch clause | as drawn (assumption 21) |

Reply in any shape. "A1, B1, C3, rest as drawn" works, so does "all defaults" or "C2, everything else as is". Anything not named is taken as drawn.

## Rulings

| # | Ruling | Date |
|---|---|---|
| A | A1: contested, outage and drift in the warning colour; known issue and update in white | 2026-11-26 |
| B | B1: panel-expander toggle | 2026-11-26 |
| C | C3: count in the tile, date in the tooltip and the list | 2026-11-26 |
| D | As drawn | 2026-11-26 |
| E | As drawn | 2026-11-26 |
| F | As drawn | 2026-11-26 |

Este's reply, 2026-11-26 13:33 CST: "A1, B1, C3, everything else looks good". Read as covering the two
layout notes below as well: the contested notice at 380 px keeps the actions to the right of the text as
drawn (no compact wrap rule), and the list row carries the pulse as a second line under the name.

**Reversed and added, 2026-11-26 afternoon, after item 11 and 12 were built and measured** (Este): at 380 px
the line's buttons and the "+N more" toggle drop to a row under the sentence (the real sentences wrapped to 3 to
5 lines beside them; the mock's stand-ins were shorter); the account panel shows the count only, with the date in
the tooltip and the accessible name (the joined line trimmed on every entry); the Library list row keeps the
pulse as a second line spanning the columns under the name, as built.

## What is drawn, against spec §12

- The line in each of its five states at the default width (900) and at 380 px.
- "+2 more", collapsed and expanded, at both widths.
- Tone options A1 and A2, with the two named contrast pairs (RowExpiredAccent and White on RowBg) measured per theme in the mock's own arithmetic.
- The grid tile in C1, C2 and C3, with the private-server variant of each; the C3 tooltip; today's tile for reference; and the pulse states (zero, older than eleven months, count expired, no answer).
- The list row and the panel entry with the pulse, and the private server in all three places.
- The more-count as B1 and as B2: rest, hover and open, then each in place on the strip.
- Extras with no decision attached: the outage wording at all six levels, the counted suffix, the update sentence for one account, and a rollback (the sentence with no link).

## Things to read on the page before ruling

- **380 px and the contested notice.** The spec puts the contested actions to the right of the text. At 380 px that leaves the text about 140 px, and it wraps to four lines (see the contested state at compact). v1.40 stacks those two buttons under the text. If that matters, the fix is a layout rule for compact (buttons wrap beneath the text), and it is not one of the six decisions. Say so and item 11 builds it.
- **The list row grows.** D draws the pulse as a second line under the name, so a row with a pulse is one meta line taller than a row without. The alternative is a new column, which costs width the actions column does not have.
- **Tile heights** are the spec's estimate of a 15 px meta line (C1 276, C2 276, C3 261). The rendering tests measure the real ones.
- **Flatline** carries the notice by the glyph and the words alone. All five states read the same colour there, which is the point of that theme and the reason the tone decision is a Brand, Midnight and Magenta Heat question.
- **Interactive edge.** The border on secondary buttons and the toggle is derived at runtime. Brand's value is exact; Midnight, Magenta Heat and Flatline are close estimates.
