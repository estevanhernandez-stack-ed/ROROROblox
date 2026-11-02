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

## Controlled A/B, pending C2

The 10% startup bar (spec §6) is decided here, on a machine with no live session:

1. Build two dev builds in Release: a `5a8fe61` worktree (v1.35) and the `cycle/v1.36-account-library` branch tip.
2. Same machine, same eight-account vault (copy of the same data folder), no Roblox client running, no other RoRoRo running.
3. Five cold starts of each, alternating A, B, A, B...; quit the app fully between starts.
4. For each start read the same two log lines: the `ROROROblox starting` banner and the following `Startup mutex:` line; record the delta in ms and the class of the start (anything but a clean start is excluded and repeated).
5. Record the five numbers and the median per build in a table here. Pass: the branch median is within 10% of the `5a8fe61` median, with zero `AccountFactsApi` request lines before the first expand or tick.

Not run yet.

## #222: windowless strays (measurement from v1.35)

Measurement per spec §7.3: one day with ten launches, counted at the end of the day with
`scripts/count-windowless-clients.ps1` (read-only; process id and start time only). The day starts on v1.35
(Store build). If the count holds at or below one between launches, #222 closes on that number; item 12 reads this table.

| Timestamp | RobloxPlayerBeta total | Windowless | Windowless (pid, start) |
|---|---|---|---|
| 2026-11-01 21:53 -06:00 | 1 | 0 | none |
