# v1.20.0.0 smoke run — one button vocabulary

Branch `feat/button-vocabulary` @ `274f003`. Run at least **brand** and **flatline**; flatline is where
a colour-carried distinction collapses and a state that only works by hue stops working.

**Before anything:** check `%LOCALAPPDATA%\ROROROblox\themes` on that machine. This box had
`broken-theme.json` and `proof-theme.json` left over from v1.18's verification, which put a
"Proof (loads next to a broken file)" entry in the picker and a warning line under it. Both deleted
here on 2026-08-11. If that machine has them too, delete them — they are fixtures, not themes.

## 1. The disabled wash — the one thing measurement cannot settle

This is the cycle's only *look* change, and it is the reason for the run. Three windows open with
their primary CTA already disabled, so you see it on first paint without doing anything:

| Where | Reach it | Enables when |
|---|---|---|
| **Join by link** → *Launch* | An account row's **Game** dropdown → *(Paste a link…)* | a link resolves |
| **Export accounts** → *Export…* | Settings → **Accounts** → *Export accounts…* | accounts exist, passphrase clears the floor, both fields match |
| **Import accounts** → *Import* | Settings → **Accounts** → *Import accounts…* | a file and passphrase are supplied |

**What it should look like:** a pale, washed-out cyan block with a dark label you can still read.
Measured 8.96:1 worst case across all four themes.

**What it looked like before:** bright cyan with a mid-grey label at **1.37:1** in brand — very close
to invisible. That shipped in every release up to v1.19.

**Call it wrong if** the button reads as *gone* rather than *unavailable*, or if the pale state reads
as enabled. That is the exact axis C1 caught twice and no test can measure.

Same treatment on the warning rank: **Recycle** and **Re-authenticate** on an account row when they
are not available.

## 2. Games in the toolbar

- Header reads **Games · Settings · Tools**. Games opens the Games window.
- **Tools ▾ no longer lists Games.** Its tooltip should not mention Games either.
- The empty-state *"No saved games yet — Add a game"* widget still opens the same window.

## 3. Hover, everywhere

Hover any button in any window. You should get a **translucent sheen with an outline appearing** —
never Windows' `#BEE6FD` Aero blue, and never the fill being replaced by a darker colour.

Two specific ones that were still broken until this session and are worth a deliberate look:

- **Settings → Alerts and memory →** the two **Show** buttons beside the webhook fields. They were
  still flashing Aero blue on hover after the rest of the app had stopped.
- **The default-game widget** in the header (the toggle, not a button). Fixed at C2; confirm it held.

## 4. Nothing else moved

Every other button should look **identical at rest** to v1.19. That was the cycle's constraint. A
visible change at rest anywhere outside §1 is a regression, not an improvement.

Worth opening because they appear at the worst possible moment: **Roblox already running** and
**Leftover processes**, both of which show during the startup gate.

## Known and deliberate, do not report

- **F-050 is open on purpose.** White-on-magenta still ships on five badges — MAIN ×2, DEFAULT,
  PRIVATE, and the plugin update pill — at 3.79 brand / 4.16 midnight / 3.29 magenta-heat / 4.68
  flatline. The contrast gate cannot see them because it only measures elements declaring both fill
  and label on one tag, and these are a magenta `Border` wrapping a white `TextBlock`. Known, measured,
  recorded; not this cycle's.
- **The default-game widget keeps its own colours** — RowBg fill, cyan edge — rather than the toggle
  rank's. Deliberate: it is a picker, not an action. It is the fence's one exemption.
- Ghost-ranked buttons have no fill by design; their hover is the sheen over whatever hosts them.
