# Smoke record: v1.36 account library

The per-cycle record for every "measured, not tested" row in the cycle spec (§12). Cycle docs:
`docs/cycles/2026-11-01-account-library/`. Baseline commit: `5a8fe61` (v1.35.0.0).

## Machine and vault

- Machine: DUNDER-MIFFLAN, Windows 11 Pro 10.0.26220, RTX 4070, 32 GB RAM.
- Vault shape: 8 distinct saved accounts (count only; the field logs' presence lines show 8 distinct account ids).

## Collapsed row pixel bar

Pinned in `src/ROROROblox.Tests/Rendering/AccountRowCollapsedRenderTests.cs`, measured on v1.35
(`5a8fe61`) before any XAML change: the `AccountRowBorder` header of one collapsed account row,
rendered through the real `MainWindow` template, flatline theme, 900x600.

| DPI | Header height (device px) | Constant |
|---|---|---|
| 96 | 92 | `V135RowHeight96` |
| 120 | 115 | `V135RowHeight120` |

Layout runs once in DIPs (92); the 120 figure is that row rendered at 125%. Re-running layout at a real
125% was tried and was order-dependent in the shared host thread, so it is not a baseline.

## v1.35 startup baseline (field starts, harvested)

Measure: the Serilog banner (`ROROROblox starting (v1.35.0, ...`) to the next `Startup mutex:` line, the last
log before `mainWindow.Show()` (App.xaml.cs has no `Show()` log line; none was added). Source: the packaged
**Store** build's own logs, `%LOCALAPPDATA%\ROROROblox\logs\rororoblox-YYYYMMDD.log`, every v1.35.0 banner from
2026-10-26 to 2026-11-01. These are **field starts of the packaged Store build on the builder's working machine**,
not controlled cold starts: no RoRoRo or Roblox process was started, stopped or restarted to produce them,
because the builder's live Store RoRoRo and Roblox client were running.

| # | Banner (local) | Banner to `Startup mutex:` | Between the pair | Class |
|---|---|---|---|---|
| 1 | 2026-10-26 21:00:20.615 | 1831 ms | StartupGate clean start; plugin autostart (1 plugin process started) | clean |
| 2 | 2026-10-31 08:38:33.965 | 2106 ms | StartupGate `Blocking`; `Seamless takeover` of 1 windowless tray client; `Killing 2 plugin process(es)` left over | not clean |

- v1.35.0 starts found: **2**. Qualifying clean starts: **1** (fewer than the five asked for, so the target is not met; no numbers were invented).
- Median of the clean starts: 1831 ms (n=1).
- Median of all starts: 1968.5 ms (n=2).
- No start in the window showed a `leftover dialog answered` line.
- Eight-account note: both starts are the same vault shape; start 2 pays for a takeover and a plugin kill, which is why it is excluded from the clean figure.

Treat this as a rough anchor, not the 10% bar. n=1 cannot support a percentage; the bar is set by the controlled A/B below.

## C2 run order (prepared 2026-11-02, items 1-7 built)

Both A/B builds exist, built in Release and not launched:

- **A, v1.35:** a detached `5a8fe61` worktree at `../_scratch/rororo-v135-ab/`, exe under `src/ROROROblox.App/bin/Release/net10.0-windows10.0.19041.0/`.
- **B, the branch:** this repo's `src/ROROROblox.App/bin/Release/net10.0-windows10.0.19041.0/` (the `net10.0-windows` folder beside it is a stale August build; ignore it).

1. **Probes**, any time, Store RoRoRo may stay up: `mocks/README.md`, Half 2.
2. **Quiet the machine:** close every Roblox client, quit the Store RoRoRo from the tray.
3. **Startup A/B:** the protocol below. Este starts and quits; the session reads the two log lines per start.
4. **Eyes-on walk, build B:** checklist C2 (a), (b) and (d), plus three open calls:
   - **Remove clips at 900 px.** Measured in the render harness, v1.35 already cuts it by 15 px with games saved and 64 px with none (the empty games box widens to fit its hint); the panel expander adds 32. Confirm with real fonts, then pick: cap the empty games box's width, move the expander into the name column, or move Remove to the right-click menu. **Corrected 2026-11-02:** these clip figures came from a harness helper that measured the actions' right edge from the roster ScrollViewer's outer edge, which counts its 24 DIP left padding, then compared it with the inner viewport, which does not. Every figure was 24 DIP too high; see docs/decisions.md, "Remove never clipped after E1".
   - **One-account fit.** A fresh install and a light panel (3 Recent) fit at 900x600; 4 or more Recent entries scroll the roster, and Recent caps at 8. Keep, or lower what the panel shows.
   - **The expander's open state** is the house grey press fill, not the mock's cyan outline.
5. **One-account path:** park the data folder aside, start build B, Add account: the panel opens with three empty states and Open Roblox home launches that account. Restore the folder.
6. **Probe gate (e):** whatever step 1 measured decides Needs verification and the Plus badge.

## C2 eyes-on walk, 2026-11-02 (build B, Este)

Eight accounts on this machine's vault, maximized window, build B at `084203e`, then again at `d0b5c4a` after the rework.

| # | Check | Result |
|---|---|---|
| 1 | Facts line; Plus on the main and on no alt | pass (Plus on the main only; all eight probes answered 200 live) |
| 2 | Streamer mode: Robux masked, no note | pass |
| 3 | Panel contents (re-walked on the rework: three per column, count, See all) | pass |
| 4 | Drag-reorder with a panel open | pass |
| 5 | Keyboard (re-walked on the rework: expander before the name in Tab order) | pass |
| 6 | Compact mode hides and restores the expanders and panels | pass |

Este's calls at C2:
- **Remove at 900 px:** move the expander into the name column (7b), placed before the name (E1).
- **Panel capacity:** show fewer, so three per column with a count and See all (P1).
- **The expander's open state:** cyan, as in the mock.
- **Color on the facts line:** a new ask, picked as F2 (`mocks/c2-rework.html`).
- **Expired rows:** the colored text falls back to white there; fine as built.
- **The Library order** in the panel stays as built.
- **The 900 px minimum:** the game picker's MinWidth goes from 160 to 132 so the MAIN pill fits beside the expander. Remove fits there with or without the change; the "still clips by 14" was the 24 DIP measuring error below.

## C2 one-account path, 2026-11-03 (build B at `e3d0db0`, Este)

The real data folder (1,677 files) was renamed aside and restored afterwards with an identical file count and `accounts.dat` size. The rename was refused at first: three `626labs.ur-mcp` MCP servers run from `ROROROblox\plugins\`, so a Claude session holding the `rororo` tools pins the folder. They were stopped with Este's word. B then started on an empty folder with a clean gate (`singleton name acquired, no leftover Roblox processes`), and no Roblox client was running.

| Check | Result |
|---|---|
| First run (welcome, language notice) | pass |
| Add one account | pass: facts fetched once at add, all three 200 |
| With one account the panel is open without the expander | pass |
| Empty states read right; nothing clips | pass |
| Open Roblox home | pass: log `target=Home`, `Flagged-launch decision ... "Direct"`, signed in as the added account |
| Quit | exit code 0; one WRN: the login WebView2 folder was still locked at sweep time (retried next start) |

The test's data folder is kept at `%LOCALAPPDATA%\ROROROblox.fresh-c2-20261103`; it holds a DPAPI-wrapped cookie, so it is never committed and should be deleted once C2 is signed off.

Open from the walk: Este asked whether the Plus badge can be Roblox's own Plus icon. Not decided here.

## C3 Library walk, 2026-11-03 (build B at `6f34258`, Este)

Eight real accounts (one follow-marked, none expired), then the 100-favourite run: `favorites.json` was backed up, seeded to 100 entries (1 real, 99 with fake place ids, half carrying one real icon URL so no icon lookups fired), and restored afterwards to its 1 real entry.

| Check | Result |
|---|---|
| a. Scroll 100 favourites, grid and list | pass |
| b. Type three letters: the filter keeps up, no spinner | pass |
| c. Long names trim with an ellipsis | pass |
| 1. Grid and list: live icons, Default and Private words, last played and total | pass |
| 2. Chips filter, show the check, toggle with Space | pass |
| 3. The one box: three letters, a game URL, a place id, a private-server link | pass |
| 4. Play as: the follow-marked account reads "Joins by following"; a pick launches as that account | pass (log: estehernandez, `target=Place`, `Direct`) |
| 5. Streamer mode masks the picker | pass |
| 6. Keyboard: box focused on load, Enter submits, Tab reaches Play and Play as, arrows walk the picker | pass |
| 7. Four themes | pass |
| Windows at 125% (roster, panel, facts line, Library) | pass, nothing clipped |

Expired accounts in Play as: none on this roster; covered by `LibraryViewModelTests` (disabled in the picker, `CookieExpired` if forced) and the item-9 flyout captures.

Este's calls at C3:
- **4 tiles across at 900 px** (the mock drew 6): keep. German "Spielen als" needs the 144 px tile.
- **The tile's ⋯ menu** (not in the mock): keep.
- **The chips:** the mock's rounded pills, on = Cyan outline and label; built the same day (`PillToggleButtonStyle`).
- **Panel order** and the rest of C2's calls stand.

Two things the walk surfaced:
- **The render suite is not DPI-neutral.** With Windows at 125%, eight row-height pins failed by 1-2 px on a clean `HEAD` as well, and every capture re-rendered differently. CI runs at 100%. Run the suite and the captures here at 100% only.
- **A windowless Roblox from the C2 Open Roblox home test** was still running 40 minutes later (B's tracker followed the launcher pid and logged it as exited). Filed on the dashboard; not yet shown to be ours.

## Probes, 2026-11-02 (C2 step 1, first pass)

All eight saved accounts, `--live`, 15:24-15:25 UTC, reports in `docs/cycles/2026-11-01-account-library/probes/` (named by local-id prefix; no names, no server ids).

| Request | Result on all 8 |
|---|---|
| ticket POST 1 (CSRF handshake) | 403, no `rblx-*` headers |
| ticket POST 2 (with the token) | 200, ticket minted, nothing launched |
| presence POST | 200 (the main in game, seven offline) |
| validate-membership GET | 200: `true` for the main (`8fb7b3e1`), `false` for the seven alts |

- **Membership:** the endpoint answers for every cookie and distinguishes the accounts. It ships as Plus if Este confirms the main is a Plus (or legacy Premium) member and the alts are not.
- **Needs verification:** not measured. No account was in the captcha, expired or limited state, so no 403 with a `rblx-challenge-type` exists to compare. It stays unset until a captcha-state account (the clan's follow-cancel-follow) and, ideally, a limited one are probed.
- The first live run's report carried the main's Roblox name and its live `gameId` (server instance id); the repo is public. Deleted unpublished; the probe now names reports by local-id prefix and scrubs `gameId`/`jobId`, with tests.

## Controlled A/B, run 2026-11-02 (C2 step 3)

The 10% startup bar (spec §6) is decided here, on a machine with no live session:

1. Build two dev builds in Release: a `5a8fe61` worktree (v1.35) and the `cycle/v1.36-account-library` branch tip.
2. Same machine, same eight-account vault (copy of the same data folder), no Roblox client running, no other RoRoRo running.
3. Five cold starts of each, alternating A, B, A, B...; quit the app fully between starts.
4. For each start read the same two log lines: the `ROROROblox starting` banner and the following `Startup mutex:` line; record the delta in ms and the class of the start (anything but a clean start is excluded and repeated).
5. Record the five numbers and the median per build in a table here. Pass: the branch median is within 10% of the `5a8fe61` median, with zero `AccountFactsApi` request lines before the first expand or tick.

**Run 2026-11-02, 21:38-21:52 local**, this machine, the live data folder (both builds read the same eight-account vault), no Roblox client, Ur Task autostart on for both (one plugin process per start). A script launched the builds alternately and Este quit each from the tray. A is the `5a8fe61` worktree build; B is the branch build of 20:57 (app code at `85f1443`). Both banners read v1.35.0, so starts were matched to builds by launch time.

| Start | Banner to `Startup mutex:` | Banner to theme | Theme to gate (compat fetch) | Gate to mutex | Class |
|---|---|---|---|---|---|
| A1 | 2551 ms | 264 | 931 | 1356 | not clean: swept 2 plugin processes the Store RoRoRo left running (F-101); excluded |
| B1 | 2160 ms | 258 | 750 | 1152 | clean |
| A2 | 2053 ms | 107 | 926 | 1020 | clean |
| B2 | 1564 ms | 107 | 476 | 981 | clean |
| A3 | 1521 ms | 100 | 479 | 942 | clean |
| B3 | 1859 ms | 104 | 787 | 968 | clean |
| A4 | 1556 ms | 107 | 478 | 971 | clean |
| B4 | 1867 ms | 107 | 795 | 965 | clean |
| A5 | 1471 ms | 100 | 469 | 902 | clean |
| B5 | 1554 ms | 107 | 484 | 963 | clean |

- **As specified** (banner to `Startup mutex:`): A median 1538.5 ms (n=4), B median 1859 ms (n=5), **+20.8%. Fails as measured.**
- **Why that is not the branch.** Theme to gate is one awaited network call, `RobloxCompatChecker.ResolveMutexNameAsync` (the signed `roblox-compat.json` fetch, 2 s bound). Neither that file nor its HttpClient registration changed since `5a8fe61`. It is bimodal in both builds: about 470-485 ms (A3, A4, A5, B2, B5) or about 750-930 ms (A1, A2, B1, B3, B4). B drew the slow mode in 3 of 5 starts, A in 1 of its 4 clean ones.
- **Ruling, Este, 2026-11-02: the bar is judged on startup minus the compat fetch** (banner to theme plus gate to mutex). A: 1002, 1042, 1078, 1127, median 1060 ms. B: 1070, 1072, 1072, 1088, 1410, median 1072 ms. **+1.1%. Pass.**
- Cross-check, fast-fetch starts only, full window: A 1471, 1521, 1556 (median 1521); B 1554, 1564 (median 1559). +2.5%.
- **Facts lane: zero `AccountFactsApi` request lines before the first tick, on every B start.** B1 stayed open 283 s untouched: its first request came 69 s after the banner, then one account per 60 s tick, 12 calls, all 200 (membership included). B2-B5 quit inside 60 s with zero calls.
- Not done: A1 was excluded and not repeated, so A is n=4.

## #222: windowless strays (measurement from v1.35)

Measurement per spec §7.3: one day with ten launches, counted at the end of the day with
`scripts/count-windowless-clients.ps1` (read-only; process id and start time only). The day starts on v1.35
(Store build). If the count holds at or below one between launches, #222 closes on that number; item 12 reads this table.

| Timestamp | RobloxPlayerBeta total | Windowless | Windowless (pid, start) |
|---|---|---|---|
| 2026-11-01 21:53 -06:00 | 1 | 0 | none |
| 2026-11-03 11:10 -06:00 | 1 | 0 | none |

## Item 13: the translation verifier on the cycle's string delta (2026-11-03)

**Slice.** `docs/store/ui-translations-1.36-delta.json`: the fields of `ui-translations.json` that are new or changed against main's committed export. That's 111 fields (104 new, 7 changed), 666 pairs across fr, de, ru, pt-BR, pl and es.

**Which verifier.** The hosted verifier ran, fed the raw GitHub URL at each commit. The local `translation-verifier` MCP ingested the file but judged 0 pairs on every call, even a single language, so it was not used. Ingest found no cap violations.

**Pass 1 at `caf5830`.** 18 pairs came back revise; 11 fixes were applied in `d088a46`:

| Language | Field | Ruling |
|---|---|---|
| fr, de, ru, pt-BR, es | `WelcomeWindow_PickOrPlayFromThePanel_Detail` | Fixed. The Open Roblox home label was dropped in as an infinitive beside an imperative. The verifier flagged ru and es; rule 7 carried the fix to fr, de and pt-BR. pl already read right. |
| de | `Library_NoMatch` | Fixed: "Keine Treffer in deiner Bibliothek." |
| fr | `Shell_Panel_Meta_InRecent` | Fixed: "dans les sessions récentes". |
| ru | `Library_Col_LastPlayed`, `Shell_Fact_LastPlayed_Name` | Fixed: "Последний запуск" in both, for one term across fields (rule 6). |
| ru | `Shell_Panel_Expander_Name` | Fixed: "для {0}". |
| ru | `MainWindow_AddGamesFromToolsGames` | Fixed in pass 1 to "в Библиотеке". Pass 2 asked for the accusative "в Библиотеку", applied in `125a8cf`. |
| ru | `Shell_Fact_FriendsOnline_other` | **Overruled.** Unreachable: `Plurals.Category` returns one, few or many for ru integer counts, never "other". |
| pl | `Shell_Fact_FriendsOnline_few` | **Overruled.** "2 znajomych" is the standard form with digits. |
| fr, es, ru, pt-BR, pl, de | rule 3 on quoted UI labels | **Overruled**, the known false positive. The UI is localized, and every quoted label (Launch multiple, the tray menu items, Library, Recent) matches its own on-screen translation in that language. |

**Pass 2 at `d088a46`.** All 11 fixes cleared. 17 pairs came back revise: 15 were the rule-3 false positive, overruled as above; ru `Shell_Fact_FriendsOnline_other` was overruled again; ru `MainWindow_AddGamesFromToolsGames` was fixed (accusative). No other finding.
