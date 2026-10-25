# Field report triage: four days of agent-driven play (2026-10-21 to 25)

Source: a friction log from an agent that drove RoRoRo + Ur MCP + Ur Task + Ur OCR near-continuously for four
days on a Store-only install. Eight friction points and a wishlist. This file sorts each into **host** (this repo)
or **plugin** (filed as issues in the plugin repos), records what the code says about each claim, and orders the
host work.

The one-line read: the front door works (launch from chat, first try); everything that went wrong is the host's
plugin contract being **fire-and-forget where an agent needs a confirmed state**: stop returns before clients exit,
account calls answer during load, tracking forgets a client that reconnects, and a plugin that dies at launch is
only visible in a log.

## What the code says about each claim

| # | Claim | Owner | Verdict from the tree |
|---|---|---|---|
| 1 | Plugins exit within a second with `0x80008085` | host | **Plausible, not reproduced.** Plugins are self-contained .NET apphosts. `0x80008085` is in the .NET host's own failure range (believed to be "could not resolve the host's own path"; confirm against `error_codes.h` before writing the fix). On a Store-only install the plugins folder exists only in the package's `LocalCache` (settled 2026-10-11); the packaged host passes the *virtual* `%LOCALAPPDATA%\ROROROblox\plugins\…\x.exe` path to `Process.Start` (`DefaultPluginProcessStarter.BuildStartInfo`), and an apphost resolving its own location from that path is the likely break. Neb cannot show it: the real folder exists here, so writes land in place. |
| 2 | Clients untracked after Roblox's Reconnect or a relaunch | host + ur-mcp | Unverified. `RunningAccount` carries `process_id` and `roblox_user_id`; the open question is whether Reconnect replaces the process (new pid the tracker never adopts) or only recreates the window (title decorator loses it). Needs one live reconnect with the log at Debug. |
| 3 | `launch_into_game` said "Known accounts: (none)", a minute later all three were back | host + ur-mcp | Consistent with a host that had just (re)started: `GetAccounts` has no loading state, so an unloaded store reads as empty. `HostInfo` has no readiness field. |
| 4 | Launch right after stop is declined | host + ur-mcp | Confirmed by the contract: `StopAccountsResult` is `stopped_count` + `failed_account_ids`. It reports a kill was sent, not that the process is gone. |
| 5 | RoRoRo itself closed on the morning of Oct 24 | host | **Hypothesis, check first:** v1.34 was published to the Store on the evening of Oct 23. A Store update closes a running MSIX app to service it. That fits both #5 and the 9:51 PM empty list in #3 if the app was restarting. Logs on the farm machine settle it (Store-only install: `…\Packages\626LabsLLC.RoRoRoBlox_wz1chhb2h2v4a\LocalCache\Local\ROROROblox\logs`). |
| 6 | Agent needed primitive input, not recorded macros | ur-task / ur-mcp | Plugin side by the macro wall (core observes, plugins act). Host already exposes `process_id` per account, enough to find the window. |
| 7 | Needed OCR text back, not a keybind trigger | Ur-OCR / ur-mcp | Plugin side. |
| 8 | No way to see the game from MCP | ur-mcp | Plugin side. Capture by pid → main window, no host change. |
| W | `join_server(account, gameInstanceId)` | host + ur-mcp | `LaunchTargetRequest` has `share_url` and `follow_user_id`, no game instance, though the host targets server instances internally since v1.14. Server listing is a documented public endpoint and belongs in ur-mcp. |

## Host plan (v1.35 candidates, in order)

1. **H1, packaged plugin launch (blocker).** Reproduce first, the 2026-10-11 way: park the real
   `%LOCALAPPDATA%\ROROROblox` aside, run the Store build, install one plugin, read the exit code. Fix: before
   `Process.Start`, resolve the exe to its real on-disk path (open it and read `GetFinalPathNameByHandle`, which
   returns the redirected location) and launch that, with the working directory to match. Add a fixture that
   fails if the start path is the virtual one when packaged. Same release: **plugin health in the Plugins page**,
   meaning last exit code + uptime + one sentence ("exited at launch: the .NET host could not start"), so a kid
   does not need a log.
2. **H2, readiness.** `HostInfo` gains `accounts_loaded` and `plugins_ready` (contract 0.13.0). `GetAccounts`,
   `RequestLaunch` and `RequestLaunchTarget` wait, bounded at about 10 s, for the account store to load instead
   of answering empty; past the bound they fail with a distinct `reason_code` (`accounts_loading`), never an empty
   list.
3. **H3, stop that confirms.** `StopAccountsRequest` gains `wait_for_exit` + `timeout_ms`;
   `StopAccountsResult` gains `exited_account_ids` and `still_running_account_ids`. Also `process_ids` on the
   request, so an untracked client can be stopped (only Roblox client processes; refuse any other pid).
4. **H4, re-adoption.** After the reconnect repro: if the pid changes, the tracker re-adopts a new Roblox
   process whose user id matches a saved account; if only the window changes, the title decorator re-attaches.
   `GetRunningAccounts` lists untracked Roblox processes separately rather than hiding them.
5. **H5, staying up.** Settle #5 from the farm machine's log before building anything. If it was the Store
   update, register for restart (`RegisterApplicationRestart`, which servicing honours for packaged apps) so
   RoRoRo comes back by itself after an update. If it was a crash, fix that crash. Either way, write a
   last-exit reason the next session can report through `HostInfo`.
6. **H6, server targeting.** `LaunchTargetRequest` gains `place_id` + `game_instance_id`, routed into the
   existing v1.14 targeting path (a full server queues, never silently matchmakes; keep that).

Each contract change is additive, needs `RpcMethodCapabilityMap` coverage where a method is new (none of the
above adds a method), and moves the contract minor version once for the release.

## Plugin issues filed

Filed in the plugin repos, each naming the host item it waits on. The repos are public, so the issues carry the
defect and nothing about who was playing or which accounts.

Host: H1 #242 (blocker), H2 #243, H3 #244, H4 #245, H5 #246, H6 #247.
ur-mcp: #6 loading vs empty (waits H2), #7 rejoin/hop (H3), #8 stop by pid + untracked list (H3, H4),
#9 screenshot (no host change), #10 join_server (H6). Ur Task #45 input primitives, Ur OCR #12 read_region (both
carry the ownership question below).

## Open questions for Este

- Which machine ran the farm? Its log answers #5 and probably #3.
- Input primitives (#6): Ur Task as the input layer that ur-mcp drives, or ur-mcp sends input itself? There is no
  plugin-to-plugin channel today, so the first needs one; the second does not.
