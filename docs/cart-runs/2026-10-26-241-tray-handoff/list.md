# Run List: 241-tray-handoff

Branch `fix/241-tray-handoff`, attended, commit per item. 1 → 2 → 3 in order; 4 and 5 are independent; 6 last.

- [x] **1. The probe can tell a tray client from a game**
  Spec ref: brief > Inferred (classify by command line)
  What to build: `RobloxProcessInfo` gains an optional `string? CommandLine = null` (so existing fakes compile). `RobloxRunningProbe.GetRunningPlayers` fills it through a small command-line reader using `NtQueryInformationProcess` with `ProcessCommandLineInformation` (60) on a `PROCESS_QUERY_LIMITED_INFORMATION` handle; null on any failure, never throws. A pure static classifier, e.g. `RobloxProcessInfo.IsIdleTrayClient(Func<int,bool> isTracked)` or a static helper: true only for `HasWindow == false` AND not tracked AND command line contains `--launch-to-tray` (ordinal, case-insensitive); null command line → false. Size M.
  Acceptance: Done when the reader returns this test process's own command line (compare with `Environment.CommandLine` content) and null for a dead pid; the classifier is true for an untracked windowless tray client and false for: windowed, tracked, null command line, a `roblox-player:` command line.
  Verify: `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter <new classes>`; drop the command-line check and see a test fail.
  Stop class: go

- [x] **2. The stopper can end idle tray clients and nothing else**
  Spec ref: brief > Done when (first line)
  What to build: `IRobloxInstanceStopper.StopIdleTrayClients(Func<int,bool> isTracked)` using item 1's classifier and the existing `KillAndWaitAll` (label "StopIdleTrayClients"); returns the count; probe failure stops nothing; logs which pids it ended at Information. Update every `IRobloxInstanceStopper` fake. Size S.
  Acceptance: Done when a fake-probe test with a mixed set (idle tray, tracked loading client, windowed game, unreadable command line, untracked `roblox-player:` loader) kills exactly the idle tray pid.
  Verify: `RobloxInstanceStopperTests` filter passes; loosen the filter and see it fail.
  Stop class: go

- [x] **3. Every launch clears idle tray clients first**
  Spec ref: brief > Inferred (one clear site)
  What to build: In `MainViewModel.LaunchAccountAsync`, immediately before `_launcher.LaunchAsync(...)`, call `_instanceStopper.StopIdleTrayClients(pid => <tracker knows pid>)` off the UI thread (`await Task.Run(...)`), never throwing into the launch (log at Debug on failure). Use the tracker's attached pids for `isTracked` (App wires the sweeper with `tracker.Attached.Values.Any(p => p.Pid == pid)`). Comment why, citing #241 and the 2026-10-26 live test (a closing game spawns a tray child; an idle tray client takes the next launch). Size S.
  Acceptance: Done when a MainViewModel test shows the clear runs before the launcher is called (order recorded by fakes), runs for a plugin launch and a Recycle relaunch too (they share the path), and a throwing stopper does not stop the launch.
  Verify: the new tests pass; move the call after `LaunchAsync` and see the order test fail. Respect the CLAUDE.md MainViewModel test cleanup rule.
  Stop class: go

- [x] **4. Seamless takeover stops relaunching a tray client**
  Spec ref: `App.TrySeamlessTakeoverAsync` ("put a tray client back")
  What to build: Remove the `RelaunchToTray` step and its comment's promise; replace with a comment saying why (#241: the tray client takes the next launch). Remove `IRobloxTrayLauncher`, `RobloxTrayLauncher`, its DI registration and `RobloxTrayLauncherTests` (no other callers; confirm with grep). Size S.
  Acceptance: Done when seamless takeover still closes windowless clients and reclaims the name, no code references `RelaunchToTray`, and the suite builds and passes.
  Verify: grep for `RelaunchToTray|RobloxTrayLauncher` returns nothing in src; full unit suite passes.
  Stop class: go

- [x] **5. The sweeper refuses when more than one client just left**
  Spec ref: brief > Done when (ClientSuccession line); decision 8UA7Yzd5fqIIRaJrfhKL
  What to build: In `ClientSuccession.Attribute`, when more than one live exit (inside `Window`) exists, attribute nothing. Update the class doc ("ONLY UNAMBIGUOUS CASES") to name this rule and the 2026-10-26 case. `OrphanedClientSweeper` logs the refusal at Information once per orphan set (it already latches reports; reuse that). Size S.
  Acceptance: Done when a test built from the real timeline (exits 39596 ELeonDog 12:42:23.023, 45652 CElCPapa 12:42:56.857, 32012 ELeonDog 12:42:57.570; orphan 47024 started 12:42:21.235; now 12:43:01.706) attributes nothing and FAILS on the old rule (which gave ELeonDog); the ordinary one-exit-one-orphan case still attributes; existing `ClientSuccessionTests` stay green or are updated with a stated reason.
  Verify: `ClientSuccessionTests` filter passes; revert the rule and see the timeline test fail.
  Stop class: go

- [x] **6. Whole suite, decisions, live check**
  Spec ref: repo CLAUDE.md > Decisions; brief > Done when (live line)
  What to build: Append the decision to `docs/decisions.md`. Full build + suite. Live with Este and the watcher on a Release dev build: (a) close a game so Roblox spawns a tray child, then launch: the launch runs in its own pid, named, tracked; the tray child is ended; (b) two launches back to back: both land, both named, no orphan.
  Acceptance: Done when the suite passes in full and both live checks pass, with watcher lines as evidence.
  Verify: the commands above plus the watcher log.
  Stop class: go
