# Run Report: v139-stability

**8 of 8 done, attended, on `fix/v1.39-stability` (local only, not pushed).** Base `dbefc85`.

| # | Item | Commit | Check |
|---|---|---|---|
| 1 | A saved vault survives a torn write | 733be1b | 13 new tests; 5 fail with the fallback removed |
| 2 | An unchanged facts refresh does not rewrite the vault | 0b6affb | 8 new tests; 3 fail on the old comparison |
| 3 | A client exiting while RoRoRo exits cannot crash it | d1aaf5d, 756871b | 11 new tests; both shutdown tests fail with the guard removed |
| 4 | Relaunching a running account keeps the live client tracked | 52f5dc7 | 8 new tests; the slot test fails with the unconditional remove restored |
| 5 | A session that expires mid-game stops reading as in game | 8db3448 | 3 new tests; the main one fails with the presence clear removed |
| 6 | An outside close ends RoRoRo; the X still hides it | b9e633c (plan), d9a8a56 | 19 new tests, ten runs without a flake; both break-its fail as expected |
| 7 | No memory warning with nothing running | d4f0e27 | 5 new tests; 2 fail with the gate removed |
| 8 | Decisions, stored-files tables, full suite | c979122 | 3,596 unit and 27 harness pass; 11 unit and 1 harness skip by design |

Full suite at the last commit: `dotnet build ROROROblox.slnx -c Release`, 0 errors; `dotnet test ROROROblox.slnx -c Release --no-build`, 3,596 + 27 passed, 0 failed. The v1.38 packet recorded 3,528 unit tests; the unit project now has 3,596.

## Caught in review

- **Item 1 went back once.** The first build recovered from the backup on every read, logged the recovery warning each time (the vault is read about every 25 seconds), and left the damaged file on disk until some later save. It now repairs the main file at once and warns once.
- **Item 3 shipped a test that passed alone and failed in the full suite** (`756871b` fixes it). It assumed `Application.Current` was null; other tests create one. The item 4 worker's full run caught it. From item 4 on, the full unit project ran before every commit.
- **Item 6 was redesigned before it was built** (`b9e633c`). The first plan marked user closes by watching for `SC_CLOSE`, so any in-app close it missed would have made the X quit the app. The built rule identifies the outside close instead and hides for everything else.
- **Planning found the vault's own no-op check never skipped.** It compared a record that includes the fetch time. That became item 2.

## Measured during the run

- `taskkill` without `/F` posts `WM_CLOSE` through the message queue, to a visible and to a hidden window. Measured with a throwaway probe window, not RoRoRo.
- The WPF-UI 4.3.0 title bar's X calls `Window.Close()` (read from the IL), and a test driving a real `ui:TitleBar` classifies it as an in-app close.
- `Dispatcher.Invoke` throws `TaskCanceledException` on the calling thread when shutdown aborts a queued call. A test reproduces it on a real dispatcher, so board item 2's mechanism is confirmed, not inferred.
- Tray Quit closes no Roblox client. The comment in `App.RequestShutdown` that says it does is not true of the code.

## Not proven

No dev build was launched: it has no data-folder override and would have used the owner's real `accounts.dat`. Everything below needs a real build.

- The vault recovering on a real install: truncate a test vault and start the app.
- A PC shutdown or logoff with clients open leaving no crash notice on the next start.
- The X, Alt+F4 and the taskbar's Close hiding the window; `taskkill` without `/F` and Task Manager's End task ending the process. What End task sends was not measured directly.
- A relaunched account keeping its row when the older client is closed.
- An account whose session expires mid-game, then its client closing.
- The tray staying quiet on a busy PC with no client running.
- Whether Roblox clients close when RoRoRo quits, for a reason outside the code. The owner was asked.

## Left alone, by decision

- The user is still not told when an expired alt drops. That needs a new alert.
- Plugins hear `AccountLaunched` twice for a relaunch with no exit between; a relaunch may leave the first launch's History row without an end. Neither was traced.
- About a dozen direct `Dispatcher.Invoke` call sites outside `WpfUiDispatcher` are unguarded and untraced.
- After a re-login the old cookie sits in the backup for one save.
- "Updated N ago" can read up to six hours after a restart until the account's first refresh.
- The comment in `App.RequestShutdown` was not corrected, pending the owner's answer.

## Left for release

Nothing here is tagged, pushed or submitted. Still to do, per `docs/store/release-playbook.md`: the owner's smoke, the version bump to 1.39.0.0, release notes, `docs/features.md` and the ledger, the translation gate (this run added no strings), the listing audit, the reviewer letter, packages and the Store submission. The board this run came from is on another branch (PR #289); its rows are not flipped here.

To push and open the PR:

```bash
git push -u origin fix/v1.39-stability
gh pr create --base main --head fix/v1.39-stability --title "fix: v1.39 stability" --body-file <body>
```

Pushing waits for the owner's word.
