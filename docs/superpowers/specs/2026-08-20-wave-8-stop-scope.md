# Wave 8 — Stop actually stops, and stopping stops costing you your settings

**Findings:** F-111 (Stop does nothing on the first click), F-109 (a killed Roblox saves nothing).
**Branch:** `glow/wave-8-stop`

Both are the same root cause wearing two coats: **nothing in RoRoRo ever checks whether the
process it asked to close actually closed.**

## What was measured before designing anything

Every line here is an observation, not a reading of the source. The two wrong turns earlier in this
cycle both came from testing a *bare* `RobloxPlayerBeta` and generalising to a real session, so
every claim below is tagged with which client it was measured against.

| # | probe | client | result |
|---|---|---|---|
| 1 | `CloseMainWindow()` (posts `WM_CLOSE`) | bare | ignored, alive 15s, returned `true` |
| 2 | `CloseMainWindow()` via the Stop button | **in-game** | ignored, alive 30s+ |
| 3 | second Stop click | **in-game** | exits in 2.2s — `RequestClose` returns `false`, escalates to `Kill` |
| 4 | `WM_SYSCOMMAND`/`SC_CLOSE` (what clicking the X sends) | bare | **exits within 20s** |
| 5 | `WM_SYSCOMMAND`/`SC_CLOSE` | **in-game** | **opens Roblox's own confirm dialog** — "Close Roblox" / "Back to Home" — and waits for a human |
| 6 | hard kill | bare | settings file byte-identical — nothing written |
| 7 | user closing Roblox by hand | **in-game** | file written within 1s: geometry matched the on-screen window exactly |
| 8 | `Fullscreen` true vs false | bare | A/B by window style: `(0,0)` borderless vs `(23,27)` with caption |

**Probe 5 was first recorded as "ignored, no dialog, window still enabled", and that was wrong.**
The dialog is drawn by the Roblox engine INSIDE the game surface, not as an OS window, so UI
Automation cannot see it, no new top-level window appears, and the main window stays enabled — every
signal an automated probe has says nothing happened. The user was looking at the dialog on screen
while the probe reported its absence.

That is the fourth wrong conclusion in this cycle and the only one no amount of instrumentation on
this side would have caught. Recorded because it generalises: **for a game client, "UIA sees no
window" is not evidence that no UI appeared.**

## The two conclusions

**F-111 is straightforwardly fixable.** `RequestClose` returns `Process.CloseMainWindow()`, which
reports whether the message was POSTED, not whether the window closed. The guard
`if (!RequestClose(…)) Kill(…)` therefore never fires on the first click. Ask the real question —
did it exit? — and escalate on the answer.

**F-109 has a graceful path after all, and it is `SC_CLOSE` — not `WM_CLOSE`.** This is the whole
correction. `WM_CLOSE`, which is what `Process.CloseMainWindow()` sends and what RoRoRo has always
used, is ignored outright by every client tested (probes 1 and 2). `SC_CLOSE`, which is what
clicking the X actually sends, closes a bare client outright (probe 4) and raises Roblox's own
"Close Roblox / Back to Home" confirm on an in-game one (probe 5). Confirming it is a clean exit,
and a clean exit persists settings (probe 7).

So RoRoRo has been sending the one message Roblox ignores, for as long as Stop has existed.

The user still has to answer the in-game confirm — RoRoRo will not click it, because driving a game
client's UI is input automation and that wall is deliberate. That is acceptable: pressing Stop and
being asked "Close Roblox?" is a coherent flow. What must not happen is what happens today, where
the first press produces nothing at all.

## The F-109 design, and its honest limit

Roblox persists window state on clean exit **and not before** (probes 6 and 7: nothing mid-session,
everything within a second of the close). `SC_CLOSE` plus a human answering the confirm gets a clean
exit, so in the good case nothing is lost and no fallback is needed.

The fallback is for the case where the user does not answer, or answers "Back to Home", and the
escalation kills: RoRoRo snapshots what it can observe from outside — window rect and fullscreen
state — and writes it into `GlobalBasicSettings_<N>.xml` after the kill.

Precedent: RoRoRo already writes that file for `FramerateCap`, through `GlobalBasicSettingsWriter`,
which patches single nodes and preserves everything else.

**The limit, stated rather than discovered later:** this preserves only what is visible from
outside the process. A graphics-quality or volume change made in Roblox's own settings menu during
that session is still lost, because nothing outside the process can see it. This fixes the reported
complaint — "it keeps opening full screen" — and does not claim to fix more.

## Sequence

1. `ProcessExitVerification` — send `SC_CLOSE` (**not** `WM_CLOSE`), wait a bounded grace, re-check
   `HasExited`, escalate. Pure and testable; no WPF, no real process.
2. Wire it into `StopAccount` so the first click is the only click. Must not block the UI thread —
   and the grace has to be generous enough for a human to answer an in-game dialog, which is a
   different budget from "did the window shut", so the row needs a waiting state.
3. `WindowStatePreserver` — read rect + fullscreen for a pid, write both into the settings file
   through the existing writer.
4. Wire it in ahead of every kill: `StopAccount`, `StopAll`, `StopWindowless`.
5. Verify by BEHAVIOUR against an in-game client. A test against a bare one passes while the button
   stays broken — probes 1 and 4 are the standing proof of that.

## Open question for the user, not for me

Step 4 makes RoRoRo write Roblox's settings file on every stop. That is a wider blast radius than
`FramerateCap` (which only ever touched one node the user had asked RoRoRo to manage). It is
defensible — we are restoring what our own kill destroyed — but it is the user's call, not mine.
