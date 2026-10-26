# Run List: restart-skips-leftover-dialog

Branch `fix/restart-skips-leftover-dialog`, attended, commit per item.

- [x] **1. A Windows restart says so, and the leftover decision honours it**
  Spec ref: `#255 > Change`
  What to build: In `src/ROROROblox.App/ApplicationRestart.cs`, register `RegisterApplicationRestart` with a command line holding one argument, a public const `RestartArgument = "--restarted-by-windows"` (the API takes the arguments only, not the exe path; check CsWin32's parameter type), and a static `bool IsWindowsRestart(IEnumerable<string> args)`. In `src/ROROROblox.App/AppLifecycle/LeftoverStartupDecision.cs`, add a disposition for the unattended case (e.g. `ContinueUnattended`: clear windowless strays, leave windowed clients, no dialog) and a `restartedByWindows` parameter to `Decide` (default false so existing callers and tests keep today's behaviour). In `App.OnStartup`'s Leftover branch, pass `ApplicationRestart.IsWindowsRestart(e.Args)` and handle the new disposition: clear windowless strays if any, log at Information ("Startup: restarted by Windows with N windowed client(s) open — continuing without asking; tagged clients re-attach"), no dialog. Make sure the argument is never mistaken for a join URI. Comments cite #255 and the Oct 24 62-minute stall. Size S.
  Acceptance: Done when tests show: restart + windowed → the new disposition (no Ask); restart + only windowless → ClearSilently; restart + nothing → Nothing; no restart → exactly today's results; `IsWindowsRestart` true only when the argument is present (case-insensitive, among other args); the restart flags test still pins RESTART_NO_REBOOT and the command line holds the argument.
  Verify: `LeftoverStartupDecisionTests` + `ApplicationRestartFlagsTests` filters pass; drop the restart branch and see a test fail.
  Stop class: go

- [x] **2. Live: a Windows restart with a game open never asks**
  Spec ref: brief > Done when (live line)
  What to build: With Este: open a game from the dev build, quit RoRoRo from the tray with the game still running, start the dev exe with `--restarted-by-windows`. Expect no dialog, the "continuing without asking" log line, and the game's row re-attached ("Re-attached pid"). Then start once without the argument with a game open: the dialog still appears.
  Acceptance: Done when both runs behave as described, with log lines as evidence.
  Verify: log grep for the startup lines and "Re-attached pid".
  Stop class: go

- [x] **3. Whole suite, decisions**
  Spec ref: repo CLAUDE.md > Decisions
  What to build: Append the decision to `docs/decisions.md`. Full build + suite.
  Acceptance: Done when the suite passes in full.
  Verify: `dotnet build ROROROblox.slnx -c Release`; `dotnet test ROROROblox.slnx -c Release --no-build`.
  Stop class: go
