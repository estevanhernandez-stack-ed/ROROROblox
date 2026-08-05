# Wave 4 scope — page chrome

> Goal 4, verbatim from Este: *"we should look at the titles on some of the pages
> because it looks more like we were getting an app ready for a hackathon where we
> wanted it to kind of explain the page at the top and look like an ad more than
> an app."*

This is the last of the four campaign goals with nothing shipped against it.
Waves 1–3 moved things (Games naming, the Settings rail, the Tools container);
this one changes how every window announces itself.

## Batch — 7 findings

| id | sev × vis | what it is |
|---|---|---|
| F-004 | 5 × 5 | 25 window titles, 7 competing conventions, no stated rule |
| F-005 | 5 × 5 | the same destination called Settings and Preferences (residue) |
| F-007 | 4 × 5 | the two-tone header copied 13× at 3 sizes and 4 grammars |
| F-014 | 4 × 3 | a 55-word, four-sentence "subtitle" on Games |
| F-015 | 3 × 4 | the subtitle slot has no length or count contract |
| F-016 | 3 × 4 | the Settings subtitle that was a table of contents |
| F-017 | 3 × 4 | 11 windows that each subtitle themselves |

Coherent because they are one mechanism, not seven: **title bar, header, subtitle
— the three lines at the top of every window.** Fixing them separately would mean
touching the same 13 files three times.

**F-016 is already satisfied** and needs only verification, not work: wave 2
collapsed the Settings header and the enumerating subtitle went with it. It is in
the batch so its row closes with evidence rather than on memory.

## Evidence — the title inventory, counted 2026-08-04

25 XAML titles plus one set at runtime. Grouped by the convention they follow:

**A. `RoRoRo -- X` — 7 windows.** Diagnostics · Games · History · Plugins ·
Install plugin · Settings · Build a theme.

This is F-004's named defect, and it is literally the "looks like an ad" reading:
Windows already shows the app name in the taskbar and Alt-Tab. Printing it again
inside the title spends the most valuable words in the window on something the OS
said first.

**B. Bare destination noun — 8 windows.** Join by link · Squad Launch · Rename ·
Private server · Export accounts · Import accounts · Pick a title-bar color ·
Add Roblox account — log in.

**C. Problem statement — 6 windows.** Saved accounts can't be unlocked · Leftover
Roblox processes · Roblox is already running · Roblox needed · Stop all Roblox
instances · Microsoft WebView2 needed.

**D. Product-name prose — 2 windows.** About RoRoRo · Welcome to RoRoRo.

**E. The repo name, at runtime — 1 window.** `FriendFollowWindow.xaml.cs:133`
sets `"ROROROblox -- Friends -- {name}"`. Three parts, the only one built on the
repo name, and the only title assembled in code. **`ROROROblox` is a code
identifier; the user-facing brand is RoRoRo.** This one is a brand bug on top of
a convention bug.

**F. The product itself — MainWindow, `RoRoRo`.** Correct as-is.

B, C and D are already right — they are the rule, discovered rather than
invented. The work is A and E.

## The rule (three lines, to land in the conventions brief)

1. **The title bar names the destination, and nothing else.** No product name —
   Windows supplies it. `Diagnostics`, not `RoRoRo -- Diagnostics`.
2. **The header matches the title bar.** Same word, so a window called one thing
   in Alt-Tab is not called another inside.
3. **Destinations take a noun. Interruptions state the problem.** `Export
   accounts` versus `Roblox is already running` — the distinction 18 of 25
   windows already make.

Exception, deliberate: **MainWindow and About keep the two-tone wordmark.** They
are the two surfaces whose subject IS the product.

## The header — one control, not thirteen

13 windows hand-roll the `Product / Descriptor` device at **three sizes** (20px ×2,
22px ×9, 24px ×1) and four grammars. That is F-007's mechanism, and it is why the
sizes drifted: there was nothing to drift from.

Ship a `PageHeader` control taking `Page` and optional `Descriptor`, with
separator, size, weight and casing fixed inside. Thirteen near-copies become
thirteen usages.

Structure-first, per invariant 1: the header's identity must survive a flattened
palette, so hierarchy is carried by size and weight, and the cyan/magenta duo is
decoration on top — never the thing that makes it readable. It gets a flatline
capture like the rail did.

## The subtitle — a contract, finally

F-015's finding is that the slot has no rules, so it absorbed a help paragraph on
one page and a single grey line on another. The contract:

- **One line. Under 90 characters. Never a table of contents.**
- A subtitle that lists what is on the page rots at the next release — that was
  F-016 exactly, and the Settings header no longer has it.
- **Delete** where the page title already says it (per F-017: Accounts, Settings,
  Games, History).
- **Shorten** Diagnostics and Plugins.
- **Keep** the six that explain something genuinely non-obvious.
- **F-014** — the Games 55-word four-sentence paragraph, including a rhetorical
  question — moves to the controls it describes, per C7's subordinate-by-position
  rule.

## Not in this wave

- **C5** — grouping inside a Settings page is still fill-only. Real, open, and a
  different mechanism (cards, not chrome).
- **F-001 residue** — Stop all, Open log folder, Welcome tour need view-model
  commands before they can join Tools.
- **F-013** — the six modal islands. A much larger change with a stated
  prerequisite.

## Risks

**This wave touches ~15 files to change strings and one shared control.** The
blast radius is wide and shallow, which is the dangerous shape: nothing here
fails loudly.

- **Capture routes break on copy changes.** `ui-routes.json` invokes by visible
  name and wave 3 already broke six routes this way. Every renamed surface gets
  its route checked in the same commit, not the close-out.
- **`PageHeader` is a new shared control**, so a mistake in it is a mistake in 13
  windows at once. The XAML Style scanner (merged `9102a40`) now catches the
  resource-resolution class of that mistake at build time; it did not exist when
  the last shared-control bug shipped.
- **Titles appear in support bundles and log lines.** Renaming a window can
  orphan a grep someone relies on. `DiagnosticsWindow.xaml.cs:220` already writes
  "ROROROblox support snapshot" into a file users forward.

## Verification

- Unit: the title rule is testable the same way the Style scanner is — walk every
  `Window` in XAML, assert no title contains the product name except MainWindow
  and About, and assert none contains `ROROROblox`. That test would have caught
  the FriendFollow leak years ago and keeps the rule from rotting.
- Runtime: capture all renamed surfaces under `brand`, `magenta-heat`, `flatline`.
- The `PageHeader` gets a flatline capture specifically, per invariant 1.
