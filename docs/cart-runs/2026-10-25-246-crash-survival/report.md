# Run Report: 246-crash-survival

**5 of 5 done, attended, on `feat/246-crash-survival` (not pushed).**

| # | Item | Commit | Check |
|---|---|---|---|
| 1 | A composition flip is a warning, not a crash | 3ab2e30 | 4 tests; break-it failed as expected |
| 2 | Last exit recorded, reported via HostInfo (0.14.0) | 8aa390d, b007f69 | recorder + HostInfo tests; break-it failed as expected |
| 3 | RegisterApplicationRestart, no reboot | 8b51d86 | flags + source-order tests; break-it failed as expected |
| 4 | Presence logs a change, not a heartbeat | 455b7eb | dedupe test; break-it failed as expected |
| 5 | Decisions mirrored, full suite | f059ec5 | 2925 unit + 27 harness (1 by-design skip) |

## Caught in review
- Item 2 put the `_lastExit` field between the exception nets' doc comment and its method (fixed, b007f69).
- The live smoke showed "ended: unknown at <time>" stamps a killed session's start, not its end. The line now says "started" for unknown; the proto comment says the same.

## Live smoke (Neb, dev Release build)
- First run: `none`. Tray quit: `clean`, read back. Hard kill: `unknown` with the session's start time. Restart registration HRESULT 0 every launch.

## Not proven
- WER restarting the app after a real crash (needs 60 s uptime and a crash).
- A Store update bringing it back; servicing without SessionEnding reads as `unknown`.
- What flips desktop composition on the field machine.

## Left for release
- Publish NuGet 0.14.0 so plugins can read the new HostInfo fields.
