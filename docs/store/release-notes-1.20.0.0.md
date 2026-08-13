# RoRoRo v1.20.0.0 — release notes

> **Written retrospectively on 2026-08-12.** v1.20 merged as PR #110 on 2026-08-11 and was never
> tagged, because the Store submission was parked. Reconstructed from the archived spec
> (`docs/superpowers/specs/2026-08-11-rororo-button-vocabulary-design.md`) and the feature ledger.

Every button in RoRoRo has been flashing the wrong colour since v1.1, and nobody noticed for
nineteen releases. This release fixes that, and the fix is bigger than it sounds.

## Short list, for the GitHub release and the Discord post

```
• Buttons no longer flash pale Windows blue when you hover them. They have done that in every theme since v1.1 — including flatline, the theme whose entire job is carrying no meaning in colour.
• Hover and pressed now work the same way on every button: a translucent sheen laid OVER the button's own colour, with an outline. That means it reads correctly on a bright cyan button, on a dark navy one, and on a theme nobody has written yet.
• Disabled buttons are readable now. On bright-filled buttons the label was measuring 1.29:1 against its own background — effectively invisible. Three windows opened with a button in that state.
• Two toggles in Preferences were still flashing Windows blue after everything else had stopped. Found by a new test, not by eye.
• Games is back on the toolbar next to Settings, instead of buried in a menu.
• 63 hand-copied sets of button styling across 22 files collapse into seven kinds of button plus one for toggles. A build check now stops the 64th from being written the old way.
```

---

## Longer form

### What was actually wrong

A WPF button that does not declare its own hover, pressed and disabled appearance inherits Windows'.
Windows' hover is `#BEE6FD` — pale Aero blue. Every button in RoRoRo was doing that, in every theme,
since v1.1.

On the brand theme it was easy to miss: a pale blue flash on a dark navy app reads as *some* kind of
feedback, and the eye forgives it. On **flatline** it is a contradiction. Flatline exists so that no
information is carried in colour at all — so it survives a bad monitor, direct sunlight, and colour
blindness. A theme built on that promise was flashing blue on every hover.

### Why the fix is a template and not a colour

The naive fix is to set a hover colour per theme. That does not work, because the correct hover
colour depends on what the button already is. A sheen that reads on a dark navy secondary button
disappears on a bright cyan call-to-action, and vice versa.

So RoRoRo now owns the button template outright. Hover and pressed are a **translucent sheen laid
over** the button's existing fill, plus an outline. Because it composites rather than replaces, the
same rule produces correct feedback on a dark button, a bright button, and on a theme that does not
exist yet. That last part matters — users author their own themes, and the app cannot be re-tuned
for each one.

### 63 into 8

Button styling was hand-copied 63 times across 22 files. Those collapse onto **seven ranks** — the
vocabulary of what a button *means*, not what it looks like — plus one for toggles.

A build fence now enforces it: a button declaration may not paint itself. If someone writes the 64th
hand-copied set, the build fails and says so. That is the part that keeps this fixed rather than
fixed-once.

### Two gates, two shipped defects found immediately

Both new tests failed on their first run, against real defects that had shipped:

- **Disabled labels on bright-filled buttons measured 1.29:1** against their own background. The AA
  floor is 4.5:1. Three windows in the app opened with a button already in that state, meaning users
  were looking at an effectively invisible label at the moment the window appeared.
- **Two Preferences toggles were still flashing Aero blue** after every other control had stopped.
  Nobody saw them. A test measuring pixels did.

Neither was found by looking at the app. Both were found by a gate that measures what actually got
painted, which is the argument for having them.
