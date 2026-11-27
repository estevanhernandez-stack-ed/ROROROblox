# Smoke record: v1.41 "Roblox at a glance" (cycle 2026-11-26-roblox-feeds)

Machine: Dunder. Date: 2026-11-26. Base commit: `4fae67e` (v1.40 code, no source change yet). This file is the
record for every measured row in the cycle; later items append below.

## Item 1, part 2: startup baseline (not taken)

The five starts of the untouched Release build could not be made. At 2026-11-26 13:2x a Store-installed RoRoRo
(`626LabsLLC.RoRoRoBlox_1.40.0.0_x64`, pid 23280) and a Roblox client (`RobloxPlayerBeta`, pid 57116) were both
running, and the cycle's rules forbid starting a dev build beside them or closing either (the single-instance
guard would surface the running window, and a start can close a windowless tray client). Re-checked with
`Get-Process ROROROblox.App, RobloxPlayerBeta` after the render baseline was written: both still up.

| Run | Start banner to first line after `mainWindow.Show()` (`App.xaml.cs:864`) |
|---|---|
| 1 to 5 | not measured here; taken at C2: 1349, 1375, 1293, 1304, 1296 ms (see "C2: the live runs", row d) |
| Median | 1304 ms (C2, start banner to the `Velopack reports not-installed` line) |

The timings move to C2's A/B, where the before and after builds are started under the same conditions on a test
folder (`RORORO_DATA_DIR` set to an absolute temp path). Folder shape to use there: a fresh empty folder, so the
run measures cold first-run startup with no accounts.

## Item 1, part 3: the tray baseline is replaced

The spec's "request log of a session left in the tray" cannot be read from a log. `Logging/AppLogging.cs:46`
reads `.MinimumLevel.Override("System", LogEventLevel.Warning)`, verified in the tree at `4fae67e`. `HttpClient`
logs its request lines under `System.Net.Http.HttpClient.*` at Information, which that override drops, so no list
of v1.40's calls exists in any log file.

"Exactly the calls v1.40 makes while hidden" is carried instead by each new lane's own Debug line (items 5 and 6)
and the fake-clock tests, and is read live in item 8.

## Item 1, part 1: render baseline (for reference)

`Rendering/RobloxLineRenderTests.cs` pins, on `4fae67e`: grid rows 2 to 5 at 0 DIP, roster top at 161 DIP
(content 887 DIP wide, the 900 px window) and 48 DIP (content 365 DIP wide, compact 380 px window). PNGs for the
eyes-on compare at C2: `docs/cycles/2026-11-26-roblox-feeds/mocks/baseline/v140-quiet-900.png` and
`v140-quiet-380.png`, written by `RORORO_CAPTURE_ROBLOX_LINE=1` on the same test class.

## Item 3: the built probe on this machine

Read 2026-11-26 through the real `RobloxUpdateProbe` (real registry, real file versions, real
`clientsettingscdn` GET) from a throwaway test, deleted afterwards; the app was not started and no RoRoRo or
Roblox process was touched.

| Read | String |
|---|---|
| `roblox-player` handler version | `0, 748, 0, 7481526` |
| newest install (`RobloxPlayerBeta.exe` FileVersion) | `0, 748, 0, 7481526` |
| endpoint `version` | `0.748.0.7481526` |

`GetUpdateStateAsync` answered `Current`, direction `None`; `IsUpdatePendingAsync` answered `False`. The
PRD measured `True` on the same three strings under the text compare.

## Item 8: the live read waits

Not made. Checked 2026-11-26 14:03 with `Get-Process ROROROblox.App, RobloxPlayerBeta`: the Store RoRoRo
(`626LabsLLC.RoRoRoBlox_1.40.0.0_x64`, pid 23280) and a Roblox client (pid 57116) were both still running, and the
cycle's rules forbid starting a dev build beside them or closing either.

The run moves to C1/C2, on a build with the line drawn, with `RORORO_DATA_DIR` set to an absolute temp folder. What it
has to show, read from `$env:RORORO_DATA_DIR\logs\*.log` against the start banner:

| Row | Expected |
|---|---|
| first 30 s after the window shows | no `roblox-status read` line |
| after the settle | exactly one `roblox-status read: Ok`, and one `client-version check` line ending `Current` |
| twelve minutes hidden in the tray | no line from either lane (the twelve-minute wait is part of the run) |
| window shown again | both lanes resume |

Until then the same rules are carried by the fake-clock tests (`RobloxFeedsTests`, `RobloxFeedsWiringTests`) and the
construction of both services from the real composition root (`RobloxFeedsWiringTests.ProductionDi_...`).

## C2: the live runs (2026-11-26, 22:43 to 23:4x CST)

Machine: Dunder. Este closed the Store RoRoRo and the Roblox clients at 22:42; `Get-Process ROROROblox.App,
RobloxPlayerBeta` answered nothing before every start, and no RoRoRo or Roblox process other than the dev builds
named here ran during the runs. Builds: **before** = `4fae67e` (v1.40 code) built Release in a detached worktree;
**after** = `6719619` (the cycle head) built Release in the repo. Every start used `RORORO_DATA_DIR` on a fresh,
empty absolute folder under `%TEMP%\claude\` (the live run's folder also held a copy of the real `favorites.json`,
one saved game, Pet Simulator 99, universe 3317771874; no `accounts.dat`, no account). Each dev process was
stopped by pid after its read.

**The startup metric.** Item 1 named "the first line written after `mainWindow.Show()`". Every line in that spot is
asynchronous except one: `UpdateChecker`'s `Velopack reports not-installed` at the end of `RunStartupChecksAsync`'s
local chain (`App.xaml.cs:929`, `:981`), which on the cycle build runs after the feeds' settings read and
`Start()`. The time is the start banner to that line. One warm-up start per build was made first and discarded.

### (d) Startup against item 1's median (fills item 1's table)

| Run | before (v1.40) ms | after (v1.41) ms |
|---|---|---|
| 1 | 1349 | 1379 |
| 2 | 1375 | 1275 |
| 3 | 1293 | 1316 |
| 4 | 1304 | 1308 |
| 5 | 1296 | 1286 |
| **Median** | **1304** | **1308** |

Starts alternated before/after. The 4 ms difference is inside each build's own 80 to 100 ms spread. Zero ERR/FTL
lines in any of the ten logs. **Met.**

### (e) Every host unreachable

`HTTPS_PROXY` and `HTTP_PROXY` set to `http://127.0.0.1:9` (checked closed first: `ConnectionRefused`). The
process honours it: the log's failures read `actively refused it. (127.0.0.1:9)` through `EstablishProxyTunnelAsync`.

| Run | before ms | after ms |
|---|---|---|
| 1 | 2942 | 2942 |
| 2 | 2945 | 2948 |
| 3 | 2928 | 2965 |
| **Median** | **2942** | **2948** |

Both builds slow by the same ~1.6 s (v1.40's own `roblox-compat.json` fetch timing out before the gate); the
cycle adds nothing to it. A 75 s run of the after build with the same proxy: the first feed read came 32.1 s after
the Velopack line, `roblox-status read: "Network" (HTTP none)` and `client-version check: ... latest null ->
"Unknown" "None"`, both at Debug; zero ERR/FTL lines; the window's UI Automation tree held no text from the line
(capture checked by eye: the first-run welcome over an empty roster, nothing in row 3). **Met.**

### Item 8 and (b): the settle and the live status read

Started 22:49:41.7, banner 22:49:42.070, Velopack line 22:49:43.888.

| Row | Expected | Read |
|---|---|---|
| first 30 s after the window shows | no `roblox-status read` line | none at T+20 s; the first is at 22:50:13.962, **30.07 s** after the Velopack line |
| after the settle | one `roblox-status read: Ok`, one `client-version check` ending `Current` | `roblox-status read: "Ok" (HTTP 200)` once; `client-version check: handler 0, 748, 0, 7481526, installed 0, 748, 0, 7481526, latest 0.748.0.7481526 -> "Current" "None"` once |
| (b) nothing shown while operational | no line | the main window's UI Automation tree at T+50 s held no Roblox-line text |

### (c) The pulse, live

The Library opened through UI Automation at 22:50:32.6; `game-pulse read: "Ok" (HTTP 200, 1 ids)` at
22:50:33.249. At 22:50:47.7 the list row read `🎲 Pet Simulator 99! 👑 · 60,194 playing · updated 4 hr ago`; at
22:50:48.1 Roblox's own `games.roblox.com/v1/games?universeIds=3317771874` (the source of the game page's Active
count) answered `playing=60134`, `updated=2026-11-27 00:36:33 UTC` (18:36 CST, 4 h 14 min earlier). The count
is 0.1% apart across 15 s on a live number; the date matches. **Met.**

### Item 8: twelve minutes in the tray, then shown again

Same process (pid 81264). The main window was hidden with a **sent** `WM_CLOSE` at 22:50:48.6 (X hides to the tray;
a posted close would have quit the app). Feed lines in the log before hiding: 5.

| Row | Expected | Read |
|---|---|---|
| twelve minutes hidden | no line from either lane | at 23:02:53.6 still 5 feed lines, 0 added; the log's last line of any kind was the 22:50:33.249 pulse read; app alive |
| window shown again | both lanes resume | a second launch with the same `RORORO_DATA_DIR` exited 0 at 23:02:57 and the single-instance guard surfaced the window; `roblox-status read: "Ok"` at 23:02:54.190 (status was overdue: 12 min against its 10 min interval); the update lane read at 23:20:13.967, exactly 30 min after its last read at 22:50:13, its interval |

**Met.** (A first script crashed at 22:50:48 after sending the close, on a handle lookup of the now-hidden window;
the app was untouched, and a continuation script picked up the same process and the same timeline.)

### (a) Absent when current: thirty-five minutes visible

Visible from 23:02:57 to 23:37:58. Checked every five minutes through UI Automation: 0 Roblox-line names at each of
the seven checks, window found, app alive. The log over the stretch: `roblox-status read: "Ok" (HTTP 200)` at
23:02:54, 23:13:14, 23:23:14 and 23:33:14 (every 10 min on the 30 s tick); one `client-version check: ... latest
0.748.0.7481526 -> "Current" "None"` at 23:20:14. Zero ERR/FTL lines in the whole 48-minute log. Process stopped by
pid at 23:37:59. **Met.**

## Item 14: seven languages

Run 2026-11-26 22:5x on `6719619` plus the theme-builder rider.

- **Lint:** `python scripts/translate-cycle.py --stages lint` exit 0; six cultures, 1288 keys each, product nouns and
  format tokens preserved, 0 cross-language parity candidates.
- **Key counts:** `Strings.resx` and the six culture files hold 1288 `<data>` entries each.
- **No hand edits:** `python scripts/gen-culture-resx.py` re-run for de, es, fr, pl, pt-BR and ru; `git status` on
  `src/ROROROblox.App/Properties/` showed no change, so every culture resx is exactly what its `ui-<culture>.json`
  generates.
- **The three rules, read from the generated resx** (a script over the 26 keys the cycle added, all seven
  languages): "Roblox" stays English in every status and update sentence and the setting label; the many-account
  update sentence carries the `MainWindow_LaunchMultiple` button's own label in each language («Mehrere starten»,
  «Iniciar varias», « Lancer plusieurs », and so on; the spec's "Launch all" is this button, see the banner owed in
  item 16); the row name follows a colon in all six status sentences (French with its non-breaking space). All
  seven languages pass.
- **The verifier.** The local verifier failed (2026-11-26): `docs/store/ui-translations-1.41-delta.json` (the
  export sliced to the 26 new keys, 156 pairs) ingested, then `run_translation_review` judged 0 pairs, for all six
  languages and again for German alone. Run instead on 2026-11-27 through the cloud verifier, from the pushed
  branch's raw URL, one language per call: **156 of 156 pairs judged** (dataset commit `6719619`). 149 approve, 7
  revise:

  | Language | Verdict | Revise | Disposition |
  |---|---|---|---|
  | de | 25 approve | `RobloxLine_Update_Many` | **overruled** (rule 3 false positive, below) |
  | es | 25 approve | `RobloxLine_Update_Many` | **overruled** |
  | fr | 25 approve | `RobloxLine_Update_Many` | **overruled** |
  | pl | 25 approve | `RobloxLine_Update_Many` | **overruled** |
  | pt-BR | 25 approve | `RobloxLine_Update_Many` | **overruled** |
  | ru | 24 approve | `RobloxLine_Update_Many`; `SettingsPage_RobloxFeeds_Description` (minor) | first **overruled**; second **fixed** |

  *The overrule, by name:* rule 3 keeps in-app UI labels in English "because the interface is in English". RoRoRo's
  interface is localized in all seven languages, and the many-account update sentence quotes the `Launch multiple`
  button by the label that language's user actually sees («Mehrere starten», « Lancer plusieurs », …). Putting
  the English label there would name a button that is not on their screen. This is the known rule-3 false positive
  on quoted UI labels the checklist names.

  *The fix:* Russian «идёт на Status.io, стороннему сервису, который…» had the apposition in the dative after на
  (accusative); now «идёт на Status.io — сторонний сервис, который…», the verifier's suggestion, in
  `docs/store/translations/ui-ru.json` and `scripts/add-v141-translations.py`, then `gen-culture-resx.py ru`.
- **The theme-dialog rider (task `kw9psaaah`): fits, built.** `ThemeBuilderButtonRowFitTests` measured the old
  single right-aligned row first: a button ran past the left edge in **every** language at the default 640 DIP,
  English included ("Open themes folder" at -8.1 DIP; French -185.2, German -170.5, Russian two buttons out), and at
  the 540 minimum French needs 489 DIP for the three utility buttons alone against 484. The row is now two: Open
  themes folder, Share and Import on a left-aligned `WrapPanel`, Cancel and Save and apply right-aligned beneath. All
  70 button placements (5 buttons, 7 languages, 2 widths) sit inside the declared width less the root's margins.
  Store screenshot 8 is left alone (the listing experiment runs to 2027-02-20) and still shows the old row.

## Item 16: the security pass (2026-11-27)

Taken on `cycle/v1.41-roblox-feeds` before the merge, 35 commits over `main`.

| Check | Result |
|---|---|
| `git log -p main..cycle/v1.41-roblox-feeds` for the cookie prefix the guards scan for | 0 hits; 0 for a `ROBLOSECURITY=` value |
| The same history for user-profile paths | 0 hits |
| Key, cert or data files added (`.pfx`, `.cer`, `.dat`, `.bak`, `.tmp`) | none |
| `probes/`, `mocks/`, `Fixtures/RobloxStatus/` for identity | public status pages only (Roblox's, two other companies' named in `PROVENANCE.md`); the only addresses are OSUOSL's public support mailbox, quoted in its own public maintenance notice; no account, no cookie, no person. The mocks are rendered PNGs and one HTML page of fake rows |
| `dotnet list ROROROblox.slnx package --vulnerable --include-transitive` | no vulnerable packages in any of the eight projects |
| `PackageReference` / `PackageVersion` diff against `main` | none added, none changed |
| `dpapi-cookie-blast-radius` over the `src/` diff | no finding at any severity: the new log lines carry outcomes, counts and HTTP codes; no cookie or account id reaches a request, a log or an exception; `RobloxLauncher.cs` is untouched; the launch path only calls `RequestEarlyStatusRead()` and `ReportUpdateState()`; every anonymous client has its own handler |
| The anonymous clients from the real graph | `AnonymousClients_PrimaryHandler_HasUseCookiesFalseAndNoRedirects` reads `IRobloxStatusApi`, `IGamePulseApi` and now `IRobloxUpdateProbe` from `ConfigureServices` |

**The input-validation re-read.** The cycle's untrusted input is two remote answers, plus the update probe's,
which is older.

- **Status (`RobloxStatusApi`).** The address is a constant. The body cap is 512 KB, checked on a declared
  `Content-Length` and again while streaming, so an oversized body is never buffered whole. A cell is kept only
  with two 24-hex ids and an integer code. The DTOs have no text property, and the log line carries the outcome
  and HTTP code. 403 and 429 read as Refused. Held.
- **Pulse (`GamePulseApi`).** The address is a constant base with invariant-formatted integer ids, at most 50,
  and an id the request did not ask for is dropped. The count range is 0 to 100,000,000 and the date range is
  2006-01-01 to a day ahead; a value outside either is dropped for that game alone. The DTO reads `id`,
  `playing` and `updated` only. **Found and fixed: the body had no cap.** It was read whole with
  `ReadAsByteArrayAsync` after `ResponseHeadersRead`. It now goes through the same streamed reader as status
  (`Core/Feeds/FeedBody`) with a 1 MB cap, against a live five-game answer of 7,075 bytes (about 70 KB for a
  full batch). `OversizedBody_WithNoDeclaredLength_IsShape` was red with the old read put back and green after.
- **Update probe (`RobloxUpdateProbe`).** Older (v1.7). It used the default handler, with a cookie container
  and followed redirects. It carries no account, so nothing leaked, but it was the one Roblox client unlike
  the rest. It now takes the anonymous handler. The CDN answered 200 directly with no `Set-Cookie` on
  2026-11-27, and a 3xx lands in the probe's existing non-success path (no update).
