# Run Report: 241-tray-handoff

**6 of 6 done, attended, on `fix/241-tray-handoff`.**

| # | Item | Commit | Check |
|---|---|---|---|
| 1 | The probe reads each client's command line | ceb4235 | 7 tests; live read of a real tray client |
| 2 | StopIdleTrayClients ends idle tray clients only | 768ec3b | mixed-set test kills exactly one of six |
| 3 | Every launch clears idle tray clients first | 24e249f | order tests (launch, plugin, Recycle) fail when moved |
| 4 | Seamless takeover stops relaunching a tray client | f8b771a | no references left; suite green |
| 5 | The sweeper refuses when more than one client just left | 0d4c160 | the real 2026-10-26 timeline fails on the old rule |
| 6 | Decisions, full suite, live check | (this commit) | 2948 unit + 27 harness (1 by-design skip); live (a) and (b) pass |

## Live check (dev Release build, watcher evidence)
- (a) 14:10: a closed game's tray child 60528 ended 14 ms into the next launch; ELeonDog ran in its own pid 56924, named, in Ur Task.
- (b) 14:13: tray child 444 ended; CElCPapa (53644) and ELeonDog (41508) both landed, both named; the second clear left the first account's loading client alone.

## Not this run
- B+ (exclude `--launch-to-tray` orphans from succession): waits on watching a real Roblox update restart.
- The crash-restart leftover-dialog stall: separate design (comment on #245).
