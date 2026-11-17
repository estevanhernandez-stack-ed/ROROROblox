# Run Report: data-folder-override

Branch `feat/data-folder-override`, five items, attended, 2026-11-16. All five done. Nothing pushed.

## What exists now

A dev build started with `RORORO_DATA_DIR=<absolute folder>` keeps all of RoRoRo's own data in that folder, says so in the log and the window title, and leaves the registry alone. Shipped builds ignore the variable. Suite at the end: 3,715 unit and 27 harness pass, 0 build errors.

| Item | Commit | What it did |
|---|---|---|
| 1 | `b82c936` | `AppDataRoot` is the one place that decides the data folder; 23 sites moved; a fence with an exact allow-list of the ten Roblox-folder sites. |
| 2 | `6550166` | The override: a pure decision, configured once in `Program.Main` before the first read of `settings.json`. |
| 3 | `09673ab` | The marker (Warning line, title tag) and the registration guards. |
| 4 | this file | Three live runs. |
| 5 | docs commit | Keystone, `docs/decisions.md`, `docs/features.md`. |

## Item 4: the live runs

Release dev builds, each on an empty folder under the session's temp directory. The owner's RoRoRo was closed. His Roblox client was running (one windowed client, in game) for all three, by his word.

Before and after each run a script recorded every file under the real `%LOCALAPPDATA%\ROROROblox` (path, length, write time: 1,679 files) and five registry values (the `roblox-rororo` scheme and its command, the Discord scheme command, the two run-on-login values).

| Run | Build | Exe path | Real folder | Registry | Exit after the close request |
|---|---|---|---|---|---|
| A | branch at `09673ab` | the repo's `bin\Release` | 0 differences | 0 differences | 2.5 s, code 0, **logged** "ServiceProvider.DisposeAsync did not complete within 2s; abandoning." |
| B | `09673ab` + PR #293 merged | a scratch worktree | 0 differences | 0 differences | 0.41 s, code 0, no such line |
| C | same as B | a scratch worktree | 0 differences | 0 differences | 0.58 s, code 0, no such line |

Observed in every run:

- The second log line is the Warning: `RORORO_DATA_DIR is in use: this run keeps its data in <folder>, and leaves the machine-wide registrations (join links, run on login) alone.`
- Both titles read `RoRoRo [test data]` (seen as the top-level window's text).
- The test folder ended with ten files: `.language-notice-shown`, `.welcome-shown`, `known-issues.cache.json` and its `.sig`, `last-exit.json`, `last-known-mutex.txt`, `last-update-check.txt`, `session-stats.json`, `settings.json`, and the log.
- The singleton name was acquired beside the running client (`acquired=true`), the leftover-process dialog appeared, a `WM_CLOSE` on it answered "None", and the client was still running afterwards with its title unchanged.
- No plugins started (`0 plugin(s)`), no update check (`Velopack reports not-installed`).

### What the runs do and do not prove

- **The registry check is real only in runs B and C.** The `roblox-rororo` command already pointed at the repo's `bin\Release` exe, which is run A's exe, so an unguarded write in run A would have written the same value. Runs B and C came from another path and the value did not move.
- **The run-on-login guard was not exercised live.** Neither value existed before the runs, and nothing toggles the setting at startup. The tests cover it (`DataRootRegistrationGuardTests`, against scratch keys).
- **The Discord path was not exercised live.** A fresh profile does not connect presence. Covered by a source fence and the scheme-fixup test only.
- **No account was added,** so `accounts.dat`, `consent.dat` and `webview2-data` were never created in the test folder. Their paths are pinned by `AppDataRootOverrideTests`, not by these runs.

### PR #293 (exit deadlock): its missing live check

Run A is the unfixed build and shows the line that 17 of 17 earlier exits showed. Runs B and C carry the fix and exit in about half a second without it. Two of two. The scratch worktree and its throwaway branch were removed afterwards.

## Found along the way

1. **Caught in review, fixed before commit.** Item 3's first build assigned both window titles on every start and called it a no-op when the override is off. It is not: the titles are live `{loc:Loc}` bindings and an assignment replaces a binding with a fixed string, so every shipped build would have lost live language switching on the main window's title. The suite was green. The tag is now applied only inside the override check, and `Wiring_TitlesAreAssignedOnlyInsideTheOverrideCheck` fails if either title is assigned anywhere else.
2. **New, not fixed: a polite outside close during a startup dialog does nothing.** In run A the first `taskkill` (no `/F`) was consumed by the leftover-process dialog, and five more while the first-run Welcome window was up left the app running. Closing the Welcome window directly and then sending the close request to the main window quit cleanly. My reading, not measured further: `taskkill` posts `WM_CLOSE` to unowned top-level windows only, the Welcome window is owned by the hidden main window, and the main window's outside-close quit is not wired until startup finishes. This is the v1.39 End-task fix not covering the startup dialogs. Backlog.
3. **A stray folder, the owner's to remove:** `C:\rororo-never-created\x`, left by item 2's worker during a break-it step. The owner said he will take care of it. Item 3's brief forbade fixed paths at a drive root; none appeared.
4. **Worth a later look:** any other place that assigns a property carrying a `{loc:Loc}` binding has the same language-freezing effect as finding 1.

## What the override does not isolate

The single-instance guard, the plugin pipe (`rororo-plugin-host`), the singleton name and Roblox's own folders are machine-wide. One RoRoRo at a time. An account launched from a test profile still writes the real Roblox client's settings (FPS cap, `appStorage.json`), by design. The tray scanner reads the titles of running Roblox windows; it read the owner's and wrote nothing.

## Open, waiting on the owner

- Push this branch and open a PR.
- Merge PR #293, now that it has a live check.
- The brief's assumption that this rides in the next release with the exit fix.
