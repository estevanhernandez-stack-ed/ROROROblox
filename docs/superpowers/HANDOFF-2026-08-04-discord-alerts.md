# Handoff — 2026-08-04, Discord alerts + presence diagnostics

## Where things stand

Two PRs open, both need your merge. One known-red, and it isn't their fault.

| PR | Branch | State |
|---|---|---|
| **#81** | `feat/discord-diagnostics` | Connect/drop/reconnect/push logging for presence. **x64 lane RED — the FpsCapSettler flake, not this PR.** arm64 green. |
| **#82** | `feat/discord-alerts` | Plan 2 complete, 8 tasks, plus every fix from tonight's smoke run. Checks were still running at wrap. |

`test/discord-combined` is a local-only branch (both PRs merged, for the smoke build). **Don't push it** — it exists so one binary could carry both. Every commit on it has been cherry-picked onto `feat/discord-alerts`; nothing is stranded.

Local Release build at `src\ROROROblox.App\bin\Release\net10.0-windows\ROROROblox.App.exe` is `test/discord-combined` as of 00:34.

## The thing I got wrong

**PR #80's flake fix did not work, and it's on main.** I root-caused the FpsCapSettler flake to scheduler-turn starvation, replaced the fixed-yield pump with condition-based waiting, verified it, and merged. CI on #81 then failed on `PostWriteQuietWait_CompetingWriteLandsInsideTheWindow_ForcesARetry` — the exact test that fix targeted. A second test in the same class (`QuietFileThenSurvivingWrite_Settles`) also failed once locally under load and passed three times in isolation.

So the mechanism I proved (0 scheduler turns → `Exhausted` at 47s fake time; ≥1 turn → `Settled` at 13.1s) was real, but the fix does not cover whatever CI's load profile does. **Treat this class as unfixed.** Do not merge anything on the strength of a green run alone until it's understood; re-run and check whether the failure is this test before assuming a real break.

Next step is not another patch. Three fix attempts across two sessions have not held, which per systematic-debugging means the architecture of that test is the suspect — the pump budget (47s of fake time) deliberately exceeds the settler's own 45s deadline, so a lagging settler gets driven past its budget by the test harness itself. That design may be the problem, not the pump.

## What shipped tonight

Plan 2 in full — 8 tasks, ~90 tests. Alerts for dropped-out and memory-warning, routed per-trigger, muted per-account, coalesced per sweep, with a 5-minute cooldown.

Then the smoke run found five real bugs the 1247-green suite did not:

1. **Non-generic `ILogger` broke DI at resolve time**, which killed alert wiring, which is where I'd put the Preferences factory — so the whole Settings window stopped opening. Optional feature took out a core one. Guard test extended and verified by reverting the fix.
2. **Config changes only reached the dispatcher when Preferences closed.** Set a destination, sit watching with Settings open, get nothing. Now every save writes the cache.
3. **Deliberate closes fired dropped-out alerts.** The memory alert says "Recycle suggested," Recycle stops the client, so following one alert produced another. Stop / Stop all / Recycle / quit now mark closes as expected for 60s.
4. **Cooldown was keyed per account, not per (account, kind)** — a memory warning silenced a genuine crash alert for five minutes. Measured live: warning 00:13:55, real close 00:14:21 swallowed.
5. **No clan webhook field.** The router and both dropdowns already supported a Clan destination; only the paste field was missing, so picking it routed nowhere and fell back to desktop.

Plus the clan channel exemption from streamer mode, per Este.

## Open, in rough priority

- **Rename `ROROROblox.App.exe` → `RoRoRo.exe`.** Discord's game detection is earned by installs and keys off the executable name. Every hour anyone runs it credits the current, off-brand name. Touches the MSIX manifest, Velopack, the run-on-login entry, and the URI-scheme command — cheap now, expensive later.
- **`PRIVACY.md` owes a Discord section** — new outbound host (`discord.com`), local named-pipe IPC, DPAPI-stored webhook URLs. Este's own privacy-accuracy commit `6579b23` is on main and this feature invalidates it. Blocks the next Store submission alongside `docs/store/discord-disclosure.md`.
- **Document the presence visibility limit in the Settings UI** — while Roblox runs, friends see Roblox, not RoRoRo. Users will report this as a bug otherwise.
- **The FpsCapSettler flake** (see above).
- **Preferences restructure + move streamer mode into it** — top of the feature-ledger backlog, per Este.
- **Discord-restart reconnect** — still unmeasured. Lachee auto-reconnects (500ms→60s backoff) and re-synchronises presence itself, so the earlier "dead until I toggle it" was probably the backoff. #81's logging is what will confirm or kill that; the test is in the smoke sheet, step 10.
- **Alert delivery has no integration test.** Unit coverage is good; nothing exercises trigger → dispatch → real HTTP. Send test is that check, done by hand.
- Este's Discord webhook token from the earlier session still wants rotating (it was in plaintext and printed to a transcript).

## Environment left clean

`settings.json` restored — `memoryCapMb` is back to default (the 1500 MB smoke override is removed, backup file deleted). Without that, normal sessions would fire memory warnings constantly.

Still outstanding from earlier sessions: the stale run-on-login registry entry pointing at `bin\Debug`.
