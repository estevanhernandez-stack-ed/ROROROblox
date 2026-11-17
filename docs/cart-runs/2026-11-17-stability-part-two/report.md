# Run Report: stability-part-two

Branch `fix/stability-part-two`, eight items, attended, 2026-11-17. All eight done. Nothing pushed. Suite at the end: 3,947 unit and 27 harness pass, 0 build errors.

## What the next release now contains

Already on main since v1.39.0.0: the exit fix (PR #293) and the dev-build test folder (PR #294). This branch adds:

| Item | Commit | What a user gets |
|---|---|---|
| 1 | `1cb5254` | A plugin's stop for a client that already exited returns cleanly instead of failing. |
| 2 | `a43ca8a` | A launch that fails before the client starts is shown on the row. It used to end the app from the follow chip and the pasted-link modal, and leave the row on "Launching..." from the Launch button. |
| 3 | `295082f` | A store file that parses with the wrong shape no longer crashes every start. A damaged vault goes to its backup or the corrupt dialog, never to an empty list. |
| 4 | `f72ba48` | An account whose session expired or was rate-limited now raises the dropped-out alert when its client exits. |
| 8 | `76205d7` | One bad row costs that row, not the file. Start fresh works or says it could not, and re-reads first. An account list left empty by a failed load says why. |
| 5 | `fc0c86e` | A polite close from outside ends the app whichever window it lands on, startup dialogs included. |
| 6 | `34d1986` | The About page's shortcut list and dates on screen follow the UI language. |
| 7 | this commit | Live runs and docs. |

Item 8 was added by the owner mid-run, in answer to the three things item 3's report said it had left alone.

## Live runs (item 7)

Release dev builds of the finished branch (`34d1986`) under `RORORO_DATA_DIR`, each on its own folder. The owner's RoRoRo was closed; one windowed Roblox client of his was running for G1 to G4.

| Run | Setup | What was done | Result |
|---|---|---|---|
| G1 | empty folder | one `taskkill` (no `/F`) with the leftover-clients dialog up | Exit in 0.36 s, code 0. Log: "Startup abandoned at the leftover dialog: an outside close request arrived." The main window never appeared |
| G2 | empty folder | leftover dialog closed by a sent close, then one `taskkill` with the first-run tour up | Leftover answered "None" and startup resumed; then exit in 0.35 s through the quit path |
| G3 | four wrong-shape files seeded (`favorites.json` and `session-history.json` as `{"version":1}`, `private-servers.json` as `{}`, `session-stats.json` with `"accounts":null`) | start, dialogs closed normally, one `taskkill` at the main window | Started. Four warnings, one per file, file name only. Exit in 0.35 s, code 0 |
| G4 | `accounts.dat` seeded with eight junk bytes | start, Start Fresh pressed in the corrupt dialog (UI Automation), dialogs closed normally, one `taskkill` at the main window | The file was set aside as `accounts.dat.corrupt-<time>` with its eight bytes intact, the app carried on to the tour and the main window, exit in 0.34 s, code 0 |

Before and after G1 to G4 the real `%LOCALAPPDATA%\ROROROblox` (1,679 files) and the five registry values showed 0 differences, and the owner's Roblox client was still running with its title unchanged after every run.

Earlier the same day, on the build before item 6: one `taskkill` with the tour up exited in 0.39 s and 0.33 s (two runs), and an outside close at the main window after the tour was closed normally exited in 0.33 s. None of the exits logged "DisposeAsync did not complete".

### Not exercised live

- **The already-running dialog.** It needs a Roblox client that holds the singleton name, which a client launched under RoRoRo does not. Covered by a test on the real dialog.
- **Task Manager's End task.** `taskkill` only, as in v1.39.
- **The expired-account alert, the launch-failure row, the salvage of a file with one bad row, the shortcut list in another language, a Start fresh that cannot move a file, and a Start fresh over a vault that turns out to be readable.** Tests only. The last two use real DPAPI and real files.

## What review changed

Five of the seven built items changed in review. Each of these passed the worker's own suite as delivered.

1. **Item 2: the row showed the raw exception message.** A store error's message names a path under the user's profile, and a row is on screen for anyone watching a stream. The row now shows the exception's type.
2. **Item 3: a row with a missing field made the whole vault "damaged".** That would have put every account in a vault that opens today behind the corrupt dialog, or rolled it back one save where a backup exists. Such a row is repaired instead.
3. **Item 4: a Windows shutdown would have paged the user for every expired account.** The worker found the gap and rightly did not set the shutting-down flag at session end. The existing one-minute expected-close stamp fits, and session-ending now applies it.
4. **Item 8: Start fresh would have set aside a healthy vault.** The worker measured that the corrupt dialog also appears for a vault that is only locked, and its own test asserted that once the lock was gone Start fresh moved the good vault and its good backup aside. Start fresh now re-reads first.
5. **Item 5: the fix did not fix the reported case.** The worker's probe was sound, but its conclusion that the tour case was already covered was wrong, and so was my first fix. Three live runs: no effect with the main window hidden; no effect with it shown, because the request went to a toast popup; exit in 0.39 s once the rule moved to the UI thread.

Item 6 changed in one line (a blocking `Dispatcher.Invoke` in a culture-changed handler).

## Things the owner should know

- **A test-folder run closed one of his Roblox processes.** At 10:36 a dev-build start found one windowless Roblox tray client holding the singleton name and closed it: RoRoRo's seamless takeover, the same as any start. The override isolates files and the registry, not Roblox processes. From then on a process check precedes every dev run.
- **His installed Store build was still 1.38.0.0** at 12:32, a day after v1.39 published. It was running, which holds a Store update back.
- **Twelve new strings in six languages** (two from item 8, ten from item 6) have not been through the translation verifier. That needs a pushed commit, and it is a gate before any tag.
- **`docs/PRIVACY.md` and `README.md` name v1.40** nowhere; the two new set-aside file names are listed without a version. The reviewer letter for the release should name them.

## Left open

- A lock on the vault at startup still shows the corrupt dialog, whose text blames a Windows restore. No longer dangerous. A retry needs wording in seven languages.
- A row of the wrong JSON type still makes its file unparseable.
- About a dozen `async void` handlers in the Games, History, export, import and theme windows were skimmed and not traced.
- `HandleExited` reads `Process.Id` unwrapped on a shutdown-only race.
- After an outside close during a startup dialog the log carries "An outside close request arrived during startup." after the line that abandons startup. Cosmetic.
- Whether the History page re-renders its dates on a live language switch was not checked.
- The sentence on a failed-launch row says it could not sign the account in, which is loose for a disk error.
- Board row 10 (two accounts cross-wired in a batch) and the measure-first list are untouched, as planned.

## Open, waiting on the owner

- Push this branch and open a PR.
- The translation verifier on the twelve new keys, once pushed.
- The version for the release, the Store submission and the tag.
- The name test: hold or revert. The third read is on the 2026-11-10 board.
