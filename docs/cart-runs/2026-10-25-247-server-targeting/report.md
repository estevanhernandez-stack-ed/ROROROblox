# Run Report: 247-server-targeting

**4 of 4 done, attended, on `feat/247-server-targeting`.**

| # | Item | Commit | Check |
|---|---|---|---|
| 1 | The contract can name one server | 2fc2a08 | build; AUTHOR_GUIDE recipe |
| 2 | The host launches exactly that server, or refuses | 83ca0b1 | adapter + host tests; Upgrade inserted → test saw `Place`, failed |
| 3 | Consent line names one specific server, 7 languages | a210962, f001ceb | parity fence; one-line regen per culture |
| 4 | Decisions mirrored, full suite | (this commit) | 2938 unit + 27 harness (1 by-design skip) |

## Caught in review
- ru carried an awkward "по другу" forward from the old string; ru and pl reworded to "follow a friend" (f001ceb). Non-native pass: the translation verifier runs on this delta before the tag.

## Found on the way (not this run)
- A crash restart with a client still open parks on the leftover-process dialog (62 min in the field, Oct 24 21:51). Logged on the dashboard, posted to #245.

## Not proven
- A real plugin joining a real server: needs a plugin on NuGet 0.14.0.
- No landing verification for plugin launches; plugins read `GetRunningAccounts`.
