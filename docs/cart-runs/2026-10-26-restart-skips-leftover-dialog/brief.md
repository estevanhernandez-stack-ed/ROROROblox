# Run Brief: restart-skips-leftover-dialog

## Ask
"let's work on the dialogue issue if that's going to get us through."

## Source
- **Kind:** issue
- **Location:** GitHub #255 (the Oct 24 21:51 stall; comment on #245)

## Intent
When Windows restarts RoRoRo after a crash or a Store update, startup carries on with no one at the keyboard: live games keep running and get re-attached, headless strays get cleared, plugins start.

## Done when
- `RegisterApplicationRestart` registers a command line, `--restarted-by-windows`.
- Startup launched with that argument never shows the leftover-process dialog: windowless strays are cleared silently, windowed clients are left running, and startup continues with a log line saying so.
- Startup without it behaves exactly as today (windowed leftover → the dialog).
- `dotnet build` + full `dotnet test ROROROblox.slnx -c Release` green; `docs/decisions.md` mirrors the decision.
- Live: the dev build started with the argument while a game is open shows no dialog, and the game's row re-attaches.

## Out of scope
- The BLOCKED modal (Roblox holds the singleton name): a different gate; a restart that hits it still asks. Named in the report, not built.
- Skipping the dialog for a hand-launched RoRoRo after a crash: a person is present, so asking is fine.

## Inferred
- The dialog is informational when the name is held, and its default (Continue) changes nothing (from: `App.xaml.cs` F-108 comment at the Ask branch, `LeftoverCleanupAction.None`).
- Windows passes the registered command line to restarts after a crash (WER) and after servicing (Restart Manager), so the argument marks exactly the no-human path (from: `RegisterApplicationRestart` contract; #252 registered with no command line).
- Unknown arguments are safe: `OnStartup` only picks a join URI out of `e.Args` (from: `App.xaml.cs` joinArg).
- Windowed clients are never closed unattended; they are left for the startup re-attach scan, which matches `Roblox - {name}` titles (from: F-110's rule, `RunningRobloxScanner`).

## Mode
- **Mode:** Attended
- **Triggered by:** attended by default (14:31 local, 3 items)
