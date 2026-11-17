# Run Brief: stability-part-two

## Ask
"do all 6", in answer to the six candidates listed for the next release on 2026-11-16 and again on 2026-11-17.

Added mid-run, 2026-11-17 10:00: "can we fix those items not changed, but that need to?", in answer to the three things item 3's report said it had left alone. That is item 8.

## Source
- **Kind:** a list the owner picked from, planned here
- **Location:** `docs/cycles/2026-11-16-quality-and-stability/board.md` (row 4's open half, rows 8 and 9, the small items), `docs/cart-runs/2026-11-16-data-folder-override/report.md` (finding 2), and two dashboard tasks from 2026-11-03 (About shortcut list, Library dates).

## Intent
The next release closes what v1.39 left open: one alert that never fires, one close request that is ignored, three ways the app can die or fail on a rare path, and two places a translated UI still speaks English. With the exit fix and the test-folder override already on main, that is the release.

## Done when
- An account whose session expired while its client was running raises the dropped-out alert when that client exits, and does not raise it for a close the user asked for.
- A polite close from outside the process ends RoRoRo while a startup dialog is in front, and the dialog's own close button still behaves as it does today.
- A failure on the follow-chip and pasted-link launch paths is logged and shown, and the app stays up.
- A store file that parses but has the wrong shape no longer stops the app from starting.
- One bad row in a store file costs that row and not the file; Start fresh on a damaged vault either works or says it could not; an account list left empty by a failed load says why.
- A plugin's stop request for a client that has already exited returns cleanly.
- The About page's shortcut list and the Library's older dates follow the UI language.
- `dotnet build ROROROblox.slnx -c Release` has 0 errors and the full unit and harness suites pass.

## Out of scope
- **Board row 10** (two accounts cross-wired in a batch). Plausible only; needs a reproduction first.
- **The measure-first list** (the updater's 60-second wait, presence polling with no backoff, idle cost).
- **Auto-rejoin for an expired account.** The cookie is dead, so a rejoin cannot work; item 1 is the alert only.
- **A second launch while a startup dialog is open exiting silently.** Same area as item 2, a different defect; left on the board.
- **Version stamp, Store submission, tag, release notes.** A separate step on the owner's word.

## Inferred
Each line is something the owner did not say.
- One commit per item on `fix/stability-part-two`, attended, as in the last two runs. (default)
- Item 1 reuses `AlertKind.AccountDroppedOut` and its existing routing, so no new setting and no new translated string. (default — a new alert kind would need settings UI in seven languages)
- Item 2 covers the three startup dialogs seen in the 2026-11-16 live run and in the code: already-running, leftover clients, first-run Welcome. (from: `App.xaml.cs` and `MainWindow.OnLoaded`)
- Items 2 and 3 are reproduced before they are fixed: item 2 with a measurement of what `taskkill` delivers, item 3 with a failing test. (from: the board marks both as not reproduced)
- Item 6 adds translated strings for de, es, fr, pl, pt-BR and ru through the catalog pipeline, and the translation verifier runs on the new keys before any tag. (from: the keystone's localization rule)
- The orchestrator does one live run of the finished branch on a test folder at the end. (from: that is what the override was built for)

## Mode
- **Mode:** Attended, autonomous, one commit per item.
- **Triggered by:** attended by default (09:24 local, six items).
