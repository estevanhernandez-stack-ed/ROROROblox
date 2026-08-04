# Smoke sheet — per-account FPS cap, on screen

**Why this exists.** v1.13 shipped a fix for per-account FPS caps surviving close-together
launches. The last *measured* result in this repo predates that fix and was a failure:
2026-08-02, three accounts at three caps, each came up running the **next** account's value
(`docs/investigations/2026-08-02-launch-gate-smoke-test-negative-result.md`). The confirming
measurement after the fix is not written down anywhere. This sheet settles that, and also checks
the Discord/alerts work did not disturb it.

**Build:** `test/discord-combined` — PR #81 + #82 merged locally, built 08:12.
`src\ROROROblox.App\bin\Release\net10.0-windows\ROROROblox.App.exe`
1266 unit + 18 integration green.

> Confirm you're on the right binary — Store v1.3.4 and this both report assembly version 1.3.4:
> `Get-Process ROROROblox.App | Select-Object Path` must read `bin\Release`.
> This build's exe also now reports **FileDescription: RoRoRo** (it said `ROROROblox.App` before
> today), so that's a second, faster tell in Task Manager's Details tab.

---

## The test

Three different caps, launched close together. Close together is the whole failure mode — the
bug was never visible launching one client at a time.

### 1. Set three accounts to three clearly different caps

Use values that are unmistakable on a counter: **20**, **60**, **240**. Avoid two accounts sharing
a cap — when every account wants the same number there is nothing to swap, and the feature is
free (that's the documented fast path, and it will pass whether or not the fix works).

### 2. Launch all three as close together as you can

Squad Launch, or three Launch As clicks back to back. Don't wait for one to finish loading.

### 3. Read the ACTUAL frame rate in each client

In each Roblox window: **Shift + F5** brings up Roblox's performance stats overlay, which shows
live FPS. Let each client sit a few seconds — the number settles after load.

**This is the measurement.** The dropdown in RoRoRo is what we *asked* for; the overlay is what
Roblox is *doing*. The 2026-08-02 failure looked completely correct in RoRoRo's UI.

### 4. Record what you see

| Account | Cap set in RoRoRo | FPS on screen (Shift+F5) |
|---|---|---|
| 1 | 20 | |
| 2 | 60 | |
| 3 | 240 | |

**Pass:** each window holds its own number.
**Fail:** any window shows another account's cap — that's the original bug, and the shape of the
mismatch matters. Note *which* account got *whose* value.

---

## What the log should say

`%LOCALAPPDATA%\ROROROblox\logs\rororoblox-<date>.log`

Each launch runs the settler, which reports one of four outcomes:

- `AlreadySet` — the file already held this cap. Expected when relaunching the same account.
- `Settled` — written, then re-confirmed after a full quiet window. **This is the good one.**
- `Exhausted` — gave up; launched anyway with whatever was on disk. **A cap may be wrong.**
- `WriteFailed` — the write itself failed.

Also useful:

```
Quiet wait (pre-write) settled after ...
Quiet wait (post-write) settled after ...
Quiet wait (...) timed out after ... without settling
```

A `timed out` on the post-write phase means our value was still being fought over when the budget
ran out. If the on-screen result is wrong, that line names the launch that lost.

---

## Two things that would invalidate a pass

Worth knowing before trusting a green result:

- **The FFlag version-folder bug is still open.** `ClientAppSettingsWriter.NewestActiveVersionFolder`
  picks its target by newest `RobloxPlayerBeta.exe` mtime, while Roblox launches whatever its
  installer last registered. Measured ~4 days of divergence on this rig, during which every FFlag
  write went silently into an inactive folder. The FPS cap's primary lever is
  `GlobalBasicSettings` (`<int name="FramerateCap">`), not the FFlag, so this should not affect
  the result — but if the numbers come out wrong in a way the settler log cannot explain, this is
  the first thing to check. See the feature ledger's In-flight section.
- **A pass here does not clear the flaky test.** `FpsCapSettlerTests` fails intermittently under
  parallel load and PR #80's fix did not hold. That is a test-harness problem — this sheet
  measures the feature, which is a different question. Do not read one as evidence for the other.

## What to send back

The filled-in table, and the log if anything mismatched. A pass gets written into the feature
ledger so the next person does not have to re-derive whether this was ever confirmed.
