# Run Report: restart-skips-leftover-dialog

**3 of 3 done, attended, on `fix/restart-skips-leftover-dialog`.**

| # | Item | Commit | Check |
|---|---|---|---|
| 1 | A Windows restart says so; the leftover decision honours it | dd0a2b7 | 15 tests; restart branch removed → 2 fail |
| 2 | Live: a Windows restart with a game open never asks | (evidence) | 14:29 no dialog, pid 33828 re-attached; 14:30 without the argument the dialog appears |
| 3 | Whole suite, decisions | (this commit) | 2945 unit + 27 harness (1 by-design skip) |

## Not this run
- The BLOCKED modal on a restart (Roblox holds the name): unobserved in the field.
- A real WER or servicing restart: unobserved; the argument path is what they take.
