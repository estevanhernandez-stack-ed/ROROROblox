# Run Brief: 241-tray-handoff

## Ask
"Yep, take care of A and B, please." A: stop the tray-client launch handoff at its source. B: the orphan sweeper refuses ambiguous attributions.

## Source
- **Kind:** live diagnosis + issues
- **Location:** #241, #245 (field half), #222; dashboard decisions mA7AwhBBpIXh9S3TVNHc, 8UA7Yzd5fqIIRaJrfhKL, nJRVk2UY6cPwVKzc8t8d; watcher log of 2026-10-26 (scratchpad, not committed)

## Intent
Every RoRoRo launch runs in its own tracked process, so its row owns it and its window gets named. When a client does go unowned anyway, RoRoRo never puts the wrong account's name on it.

## Done when
- Right before every launch, idle tray clients (windowless, untracked, `--launch-to-tray`) are ended; a loading tracked client, a windowed client, and any process whose command line cannot be read are never touched.
- Seamless takeover no longer relaunches a tray client.
- `ClientSuccession` adopts nothing when more than one tracked exit is inside its window; the 2026-10-26 12:42-12:43 timeline is a test that fails on the old rule.
- Live: closing a game, then launching, runs the launch in its own named, tracked process; two launches back to back both land and both are named.
- `dotnet build` + full `dotnet test ROROROblox.slnx -c Release` green; `docs/decisions.md` mirrors the decisions.

## Out of scope
- The crash-restart leftover dialog (#245 comment): a separate design.
- Presence-based attribution: not needed if A holds.
- Clearing idle tray clients between launches (#222's pile-up): they are harmless once they cannot take a launch; cleared at the next launch.

## Inferred
- Tray clients come from seamless takeover AND from Roblox itself on every game close: the closing game spawns a `--launch-to-tray` child (from: watcher 12:42:21).
- An idle tray client takes the next launch; one hosting a game does not (from: watcher 12:18:43, 12:40:12).
- Classify by command line, fail closed: windowless + untracked alone would also kill a hand-started browser launch while it loads, and an age rule misses the fresh child a stop-then-relaunch (Recycle) meets (from: analysis; `RobloxRunningProbe` reports only pid + HasWindow today).
- Read the command line with `NtQueryInformationProcess(ProcessCommandLineInformation)`: no new dependency (from: no existing command-line reader in the repo).
- One clear site, `MainViewModel.LaunchAccountAsync` just before `_launcher.LaunchAsync`, covers every launch path (UI, plugin, Recycle, Squad, auto-rejoin), and runs off the UI thread (kill + exit wait) (from: `LaunchAccountAsync` is the shared path; `AccountRecycler` relaunches through it).
- The "keep Roblox-at-startup in the tray" promise is dropped while RoRoRo runs; Roblox puts its tray client back on its own after a game closes (from: watcher; Este approved A).
- `RobloxTrayLauncher` becomes dead code and is removed with its registration and tests (from: its only caller is seamless takeover).

## Mode
- **Mode:** Attended
- **Triggered by:** attended by default (13:05 local, 6 items; Este at the keyboard for the live check)
