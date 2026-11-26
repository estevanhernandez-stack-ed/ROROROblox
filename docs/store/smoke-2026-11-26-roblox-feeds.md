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
| 1 to 5 | not measured |
| Median | not measured |

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
