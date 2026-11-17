# Quality and stability board

Sweep run 2026-11-16 on `main` at `dbefc85` (v1.38.0.0 shipped). Four sources: three read-only code
audits (the exit path, the crash surface, long-session drift), and 30 days of RoRoRo's own logs on
one PC (Dunder: 62 sessions, 498,804 lines, 2026-09-30 to 2026-11-15, v1.31 to v1.36). Nothing was
built or run. Paths are under `src/`.

**How to read the confidence column.** *Confirmed*: the code path was read end to end and the
claim was re-checked by a second reading, or the log shows it. *Plausible*: the path is confirmed
and one link is inferred (usually a Windows or WPF behaviour nobody ran). *Speculative*: a lead.
Nothing below was reproduced on a running app, and that is the first job for whatever gets picked.

## The verdict

The exit path Windows uses for logoff, shutdown and a Restart Manager close is sound. The defects
sit around it, and the two worth doing first are not crashes:

1. **The account vault can be lost, and since v1.36 it is written far more often.**
2. **v1.38's crash notice can lie.** A Roblox client that exits while RoRoRo is exiting can turn a
   normal quit into a recorded crash.

Microsoft's failure data cannot rank any of this (see the 2026-11-10 board, "Read 2026-11-15"), so
the ranking below is by what a user loses and how sure the reading is.

## Where this stands after v1.39.0.0 (written 2026-11-16, the afternoon it shipped)

Rows 1, 2, 3, 4, 5 and 7 shipped in v1.39.0.0 the same day (`docs/cart-runs/2026-11-16-v139-stability/`,
PRs #290 and #291). The rows below are left as they were written that morning; this table is the
current state, and it wins where the two disagree.

| # | Row | State | What changed since the morning |
|---|---|---|---|
| 1 | The account vault can be lost | **Shipped** (`733be1b`, `0b6affb`) | Backup, self-repair, and the write frequency. Planning found the vault's own no-op check never skipped, so it was rewritten on every facts refresh, not merely often. Not seen rescuing a real damaged file |
| 2 | A normal quit recorded as a crash | **Shipped** (`d1aaf5d`) | No longer "plausible": a test reproduces the throw on a real dispatcher. Quit itself closes no client (settled by the owner's smoke), so the trigger is a Windows shutdown or logoff with clients open |
| 3 | A relaunch loses the live client | **Shipped** (`52f5dc7`) | `Superseded` on the older client's exit |
| 4 | Expiry mid-game freezes the row | **Shipped in part** (`8db3448`); **the alert half is fixed on main's next release** (`f72ba48`, 2026-11-17) | The row and History shipped in v1.39. The process exit now raises the dropped-out alert for an expired or rate-limited account |
| 5 | An outside close cannot end the process | **Shipped** (`d9a8a56`) | Measured: `taskkill` posts `WM_CLOSE` through the queue, and the owner's smoke saw Task Manager's End task take the new path and exit cleanly |
| 6 | Exit cleanup does not finish | **Fixed and merged** (PR #293, `21754d3`, 2026-11-16), unreleased | Root cause: a deadlock. `OnExit` blocked the UI thread while the container disposed the tray icon on a pool thread, and the tray's dispose calls `Dispatcher.Invoke`. Seen in two stack captures. Live on 2026-11-16: 2.5 s and the timeout line without the fix, 0.41 s and 0.58 s and no line with it |
| 7 | Memory warning with nothing running | **Shipped** (`d4f0e27`) | Tests only. The owner's smoke did not exercise it: free memory stayed above the reserve |
| 8 | Two entry points can take the app down | **Fixed** (`a43ca8a`, 2026-11-17), unreleased | Reproduced: the exception escaped both paths. One catch where they converge; the row shows the failure by type |
| 9 | A wrong-but-parseable store file crashes every start | **Fixed** (`295082f`, `76205d7`, 2026-11-17), unreleased | Wider than listed: six stores threw. Good rows are kept, the vault takes its backup or corrupt path, and Start fresh re-reads before it resets |
| 10 | Two accounts cross-wired in a batch | Open | Unchanged |

**New rows the build turned up** (as of 2026-11-17: the first and the last are fixed, and so is the small item about a plugin's stop on a client that already exited, `1cb5254`; the other three are open):

- **No alert when an expired alt drops** (the open half of row 4). The dropped-out alert is raised only from the presence path, which an expired account has left.
- **Plugins hear `AccountLaunched` twice for a relaunch,** with no exit between. Not traced.
- **A relaunch may leave the first launch's History row without an end.** Not traced.
- **About a dozen direct `Dispatcher.Invoke` call sites** outside `WpfUiDispatcher` have no shutdown guard (`TrayService`, `App.xaml.cs`, `SingleInstanceGuard`, `ThemeService`, `MainAvatarTrayPainter`). Read from a grep, not traced.
- **A dev build cannot be pointed at another data folder,** so any live test of unreleased code runs on the developer's real accounts file. That is why v1.39 reached the owner's smoke with no live run behind it.

**Added 2026-11-17, from the next release's build (`docs/cart-runs/2026-11-17-stability-part-two/`):**

- **Fixed there:** an outside close during a startup dialog (the rule moved from the main window to the UI thread, `fc0c86e`); the About shortcut list and dates in the UI language (`34d1986`).
- **Open, new:** a lock on the vault at startup shows the corrupt dialog instead of offering a retry (no longer dangerous; the wording is wrong for the case). About a dozen `async void` handlers in the Games, History, export, import and theme windows were skimmed and not traced. `HandleExited` reads `Process.Id` unwrapped on a shutdown-only race. A dev-build start performs seamless takeover like any start, so a test-folder run closes a windowless Roblox tray client.

**Tier 3, measure before touching: one of four is now measured, and the answer is no.** On
2026-11-16 at 15:54 CST the Store build (1.38.0.0, running from `WindowsApps`) launched one account.
Asked directly with `GetPackageFullName`: `ROROROblox.App.exe` carries
`626LabsLLC.RoRoRoBlox_1.38.0.0_x64__wz1chhb2h2v4a`; the `RobloxPlayerBeta.exe` it launched, a direct
child by parent pid, and that client's `RobloxCrashHandler.exe` both answer 15700, no package
identity. `tasklist /apps` agrees. **So a Roblox crash or hang is not filed under RoRoRo's package, and
the unclassified failures in the Store data are RoRoRo's own process.** It also means a Store update
does not have to end the Roblox clients. One PC, one launch, the `roblox-player:` path; a client
started another way was not tried. The other three (the updater's 60-second wait, presence polling,
idle cost) are still unmeasured.

**One reading from the morning that the day corrected:** v1.39 certified in under 36 minutes, after
19, 23 and about 25 hours on the three releases before it. Nothing here explains either number.

## Tier 1: do first

| # | What the user loses | Mechanism | Confidence |
|---|---|---|---|
| 1 | **Every saved account.** The app opens empty and the next change makes it permanent. | `accounts.dat` is rewritten whole by temp file plus `File.Move`, with no flush to disk and no backup (`ROROROblox.Core/AccountStore.cs`, `SaveAsync`). A zero-length file reads as "no accounts" without complaint (same file, the `encrypted.Length == 0` branch). The file has twenty call sites for `SaveAsync`; since v1.36 one of them is `SetFactsAsync`, which runs on every facts refresh, about every ten minutes per account (711 refreshes in this PC's logs). The vault used to be written on a user action and is now written all session. | Code confirmed. The torn file after a power loss or a hard kill is inferred; not observed. |
| 2 | **Trust in the crash notice,** and possibly a relaunch nobody asked for. "RoRoRo closed unexpectedly last time" after a normal Quit or a PC shutdown with clients open. | `Process.Exited` runs on a pool thread into `HandleExited`, which has try/finally and no catch (`ROROROblox.Core/Diagnostics/RobloxProcessTracker.cs`). It reaches `MainViewModel.OnProcessExited`, then `WpfUiDispatcher.Invoke`, an unguarded `dispatcher.Invoke` (`ROROROblox.App/Threading/WpfUiDispatcher.cs`). While `OnExit` holds the UI thread the call queues; dispatcher shutdown aborts it and it throws `TaskCanceledException` on the pool thread. That is process-fatal, `RecordCrash` overwrites the clean record, and logging has already shut down. The repo already knows this throw: the memory-watchdog subscriber in `MainViewModel` is wrapped for exactly it; `OnProcessAttached`, `OnProcessExited` and `OnProcessAttachFailed` are not. Found independently by two audits. | Plausible. This PC's logs hold no `crash` record in 62 sessions, but almost none of them ended with clients open. |
| 3 | **A live client RoRoRo no longer sees.** The row reads not running, Stop does nothing, plugins lose it. | Launching an account that is already running overwrites its slot (`_attachedByAccount[accountId] = slot`). When the old process exits, `HandleExited` removes by account id alone, taking the new client's slot with it, and `OnProcessExited` clears `IsRunning` with no pid check. The panel's Launch, Play as and Follow all allow launching a running account. | Code confirmed. That Roblox leaves the superseded client open long enough is inferred. |
| 4 | **An alert and a rejoin.** A cookie that expires while the alt is in a game freezes the row as in game; when the client later drops there is no alert, no auto-rejoin, and History never ends. | `ApplySessionExpired` sets the flag and nothing else (`MainViewModel`), expired accounts leave the presence poll, so `InGame` stays true. `ApplySessionLimited` already clears presence; this one does not. | Code confirmed. Frequency unknown. |

## Tier 2: stability

| # | What happens | Mechanism | Confidence |
|---|---|---|---|
| 5 | **A polite close from outside cannot end the process.** Task Manager's End task hides the window; Windows then kills it and files a hang with an idle UI thread, which is the one hang bucket Microsoft names (`APPLICATION_HANG_ENDTASK_HungThreadIsIdle`). | `MainWindow.OnClosing` cancels and hides unless `App.IsShuttingDown`, and nothing tells a click on the X from an external `WM_CLOSE`. | Code confirmed; the Windows side is inferred. |
| 6 | **Exit cleanup does not finish.** Likely a tray icon left behind after Quit until it is hovered. | On this PC every one of the 15 logged exits on v1.35 hit "ServiceProvider.DisposeAsync did not complete within 2s; abandoning" (29 of 41 exits across all versions). `OnExit` blocks the UI thread on that wait while the container disposes on a pool thread, and `TrayService.Dispose` disposes a WPF-hosted icon from there. One audit predicted a throw; the log shows a timeout and never the "threw" line, so which disposable blocks is **not pinned**. | Log confirmed. Cause open. |
| 7 | **The tray warns about memory when RoRoRo is running nothing.** | The free-memory axis in `MemoryWatchdog` (F-082) does not check that any client exists. In these logs it crossed 547 times, 219 of them with zero clients, 64 of those on v1.36 in about 14 hours of running. Each crossing sets the tray's warning badge; the balloon is skipped. The default reserve is 8% of RAM, clamped to 1 to 4 GB, which a busy PC is under routinely. | Confirmed, log and code. |
| 8 | **Two entry points can take the app down.** RoRoRo vanishes on a follow-chip click or a pasted-link launch if the vault write fails. | `LaunchAccountAsync` is try/finally with no catch; `OnFollowChipClick` and `OnGameComboBoxSelectionChanged` are `async void` and reach it. From the Launch button the same exception is swallowed by `RelayCommand`. | Plausible. |
| 9 | **A parseable but wrong store file crashes every start.** | `MainWindow.OnLoaded` awaits `LoadAsync` with no try; the stores map only `IOException` and `JsonException`. `{"version":1}` gives a null list in three stores. The app never writes such a file. | Path confirmed; rare. |
| 10 | **Two accounts can be cross-wired in a batch.** Titles, Stop and activity credit land on the other alt. | Each waiting launch claims the earliest unclaimed Roblox process inside a 3-second grace, and nothing orders the waiters. `FifoClaim_ConcurrentLaunches_AttachDistinctProcesses` accepts either assignment. | Plausible. |

## Tier 3: measure before touching

- **Do Roblox clients carry RoRoRo's package identity on a Store install?** They are started with
  `UseShellExecute` from the packaged process and the manifest declares no breakaway. If they do,
  their hangs and crashes may be filed under RoRoRo, and a Store update has to end every client or
  fail. That would explain both the volume of unclassified failures and "quit before updating".
  **One minute to measure:** Store build, launch a client, Task Manager, Details tab, add the
  "Package name" column. Speculative until then, and the highest-value measurement on this page.
- **Does the direct-download updater end a running session?** `UpdateChecker` calls
  `WaitExitThenApplyUpdates` at check time, and Velopack 0.0.1298 documents that the updater "will
  only wait for 60 seconds before giving up". Updates do reach clients: each release's packages are
  downloaded about once per checking client (v1.35: 391 feed reads, 54 package downloads), which
  fits Velopack's apply-at-startup default rather than the exit hook the code comment describes.
  Not known: what the updater does to RoRoRo at the 60-second mark. A VM test settles it.
- **Presence polls every saved account every 25 seconds,** running or not, four at a time, with no
  backoff and no timeout on the client (100 seconds by default). A 429 is read as an empty answer
  at the same cadence. Behaviour confirmed; the rate-limit consequence is speculative.
- **Idle cost.** Per client, a title and a DWM attribute are written every 1.5 seconds whether or
  not they changed; a full process snapshot runs every 5 seconds; input is sampled at 10 Hz with no
  accounts. Unmeasured, and the only lead on the v1.29 "makes my pc stutter" review.

## Field reports

| Report | Explained? |
|---|---|
| #222, windowless strays | Partly. The two-minute sweep covers one shape (windowless, carries `--launch-to-tray`, untracked, readable start time). It declines silently, so nobody knows what it skipped. Log each decline once, with the reason. |
| #221, input credited to the wrong account | Partly. The foreground is recorded at 100 ms instants, so a focus, tap and restore inside one gap still credits the restored window. Item 3 also drops the activity record. |
| #260, invisible mouse | Not explained. One lead: `AttachThreadInput` to the foreground thread in `SingleInstanceGuard`. |

## Small, kept so they are not lost

- `AppStorageDefender.CheckAndRestamp`: a fire-and-forget task releases `_restampLock` after
  `DisposeAsync` disposed it (five times in the logs), and `ReadCurrentUsernameAsync` throws
  `ArgumentException` when Roblox's own `appStorage.json` holds a duplicate key (once:
  `UpdateControllerCacheTimestamp`). Both fault unobserved; the second skips that identity check.
- `RobloxProcessTracker.Kill` and `RequestClose` read `Process.Id` on a process that is already
  gone, so a plugin's StopAccount fails with "No process is associated with this object" (twice).
- A second launch while a startup gate dialog is open exits silently: `StartListening` runs after
  the modals.
- One off-thread read of `Accounts` in the startup auto-launch (`App.xaml.cs`, after a
  `ConfigureAwait(false)`). Caught, so the cost is a skipped auto-launch.
- `last-exit.json` cannot tell a Store-update close from a PC shutdown; recording the
  `ENDSESSION_CLOSEAPP` bit would.
- `PackagedStartupRegistration` waits on the UI thread with no timeout.
- `release.yml` installs `vpk` unpinned while the library is pinned at 0.0.1298. Noticed, not examined.

## A design choice, not a defect

`DispatcherUnhandledException` logs and deliberately leaves `Handled` false: "Silent crash is worse
than loud crash." That makes every UI-thread exception fatal. The audit counted 67 named `async void`
methods in App, 32 of them with an await outside any try (counted by pattern, not re-counted here). Commands are safe (`RelayCommand` swallows and
reports). Now that v1.38 has a crash notice and a support bundle, handling non-fatal types with a
visible notice is a real alternative. The owner's call.

## Read and found sound

Session end: no hook intercepts `WM_QUERYENDSESSION`, the exit record is written synchronously,
and WPF's shutdown ignores the hide-to-tray cancel. Every wait in `OnExit` is bounded. No
first-party foreground thread. Tracked plugin children are killed on exit. End task, kill and power
loss read `unknown`, so they raise no false crash notice. The facts lane: one worker, paced, 15 s
timeout. Pid reuse: the tracker holds the handle until exit. Overlap guards on all five thread
timers. Logs capped at 30 files; history at 100 rows. No animation that runs forever.

Already fixed and seen in the logs only on old versions: the composition crash (#246, twice on
v1.32 and v1.33), and presence logging, which was 96% of all log lines until v1.35 cut it from
about 28,000 lines a day to about 170.

## Not reached

Synchronous UI handlers, XAML converters and `SettingsPage` sync paths. Discord RPC, Hardcodet
internals, toast and sound paths. gRPC handlers, the plugin installer. `ThemeStore`,
`ConsentStore`, `DiscordConfigStore` corruption handling. `FpsCapSettler`, `GameIconCache`, avatar
bitmap lifetime, WebView2 teardown. Native render-thread and out-of-memory failures on PCs running
many clients, which would fit "no stack" better than any managed finding here.

## One caution about the log evidence

Fifteen of this PC's sessions ended `unknown`. They are not a field signal: all are two-minute runs
on 2026-11-03 and 2026-11-05 that cycle the same themes 24 seconds apart, which matches the
screenshot capture harness killing the app. One PC's logs describe one PC.
