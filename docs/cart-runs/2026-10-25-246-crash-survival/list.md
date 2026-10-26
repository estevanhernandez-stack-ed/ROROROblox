# Run List: 246-crash-survival

Branch `feat/246-crash-survival`, attended, commit per item. Items run in order; 2 and 3 both touch App startup.

- [x] **1. A composition flip is a warning, not a crash**
  Spec ref: `#246 > Change: if it was a crash, fix the crash`; log `rororoblox-20261024.log` lines 4234, 9998
  What to build: In `App.WireGlobalExceptionHandlers` (`src/ROROROblox.App/App.xaml.cs:3505`), mark `args.Handled = true` and log at Warning when the exception is a transient DWM composition fault. Decide that in a pure, testable static predicate (`IsTransientCompositionFault(int hresult, string? stackTrace)`): true only for HResult `0x80263001` (DWM_E_COMPOSITIONDISABLED) with a `System.Windows.Shell.WindowChromeWorker` frame. Everything else keeps today's path (Error log, unhandled, crash). Update the method's doc comment, which says none of the nets recover. Size S.
  Acceptance: Done when the predicate is true for the real stack from the field log and false for the same HResult with no WindowChromeWorker frame, false for another HResult with the WindowChromeWorker frame, and false for a null stack; the handler sets Handled only on true.
  Verify: `dotnet test src/ROROROblox.Tests/ -c Release --filter <new test class>` passes; break the predicate (drop the frame check) and see a test fail.
  Stop class: go

- [x] **2. The next session knows how the last one ended, and HostInfo says so**
  Spec ref: `#246 > Either way: write a last-exit reason the next session reports through HostInfo`
  What to build: A Core recorder (`LastExitRecorder` or similar) over a small `last-exit.json` in the data folder. `Begin()` reads the previous record and writes `running`; `RecordClean()`, `RecordSessionEnding(reason)`, `RecordCrash(Exception)` write synchronously and never throw. The previous exit is one of `none` (first run), `clean`, `session-ending`, `crash` (with type + HResult + message, never anything cookie-bearing), `unknown` (record still said running: killed, power loss, or servicing that gave no notice); an unreadable file reads as `unknown`. Wire it in App: `Begin` early in startup with an Info log of the previous exit, `RecordCrash` in the AppDomain handler, `RecordSessionEnding` in `SessionEnding`, `RecordClean` in `OnExit`. Add `last_exit_reason` (string), `last_exit_detail` (string), `last_exit_at_unix_ms` (int64) to `HostInfo` in `plugin_contract.proto` with a 0.14.0 comment, answer them in `PluginHostService.GetHostInfo` through an optional ctor parameter (so both test construction sites compile), bump `ROROROblox.PluginContract.csproj` to 0.14.0, add a line to `docs/plugins/AUTHOR_GUIDE.md`. Size M.
  Acceptance: Done when recorder tests cover all five previous-exit outcomes plus a corrupt file, a crash record carries no exception data beyond type, HResult and message, `GetHostInfo` returns the three fields from the provider (and empty/`none` with no provider), and the harness `CapabilityMap_CoversEveryHostMethod` still passes (no new RPC).
  Verify: `dotnet build ROROROblox.slnx -c Release`, then the recorder and `PluginHostServiceTests` filters pass; break the running→unknown rule and see a test fail.
  Stop class: go

- [x] **3. RoRoRo comes back by itself**
  Spec ref: `#246 > register for restart (RegisterApplicationRestart)`
  What to build: Add `RegisterApplicationRestart` to `src/ROROROblox.App/NativeMethods.txt` (CsWin32) and call it once at startup, after the single-instance gate passes, with no command line and flags `RESTART_NO_REBOOT` (crash and servicing restart; reboot does not, because run-on-login is opt-in). Log the HRESULT at Debug, never throw. Keep the flag choice in a named constant. Do not call it from tests: a registered test host would be restarted by WER. Size S.
  Acceptance: Done when the app builds, the call is wired once at startup after the gate, and a test pins the flags to "no reboot, crash and patch allowed".
  Verify: `dotnet build ROROROblox.slnx -c Release` and the flags test pass. Live crash-restart is a manual smoke (WER only restarts a process that ran 60 s), noted in the PR.
  Stop class: go

- [x] **4. Presence logs a change, not a heartbeat**
  Spec ref: dashboard decision `jmJJRVXYtxz5vSOIzZVN`; `src/ROROROblox.Core/Diagnostics/PresenceService.cs:291`
  What to build: Log the `Presence for account {AccountId}: {PresenceType}` Debug line only when that account's presence type or place id differs from the last one logged for it (per-account last-logged state, thread-safe; polls run concurrently). The event still raises every poll; only the log changes. Size S.
  Acceptance: Done when a test with the existing `CapturingLogger` shows three identical polls give one line, a changed state gives a second line, and a second account logs its own first line; `AccountPresenceUpdated` still fires every poll.
  Verify: `dotnet test src/ROROROblox.Tests/ -c Release --filter PresenceServiceTests` passes; remove the dedupe and see the new test fail.
  Stop class: go

- [x] **5. Whole suite green, decisions mirrored**
  Spec ref: repo CLAUDE.md > Decisions; `docs/decisions.md`
  What to build: Append the #246 decisions (root cause, narrow match, last-exit file + HostInfo 0.14.0, `RESTART_NO_REBOOT`, presence dedupe) to `docs/decisions.md` in its existing format. Run the full suite.
  Acceptance: Done when `dotnet build ROROROblox.slnx -c Release` has 0 errors and `dotnet test ROROROblox.slnx -c Release --no-build` passes in full (unit + harness).
  Verify: the two commands above.
  Stop class: go
