# Store screenshots — RORORO

> Partner Center accepts 1–9 screenshots per device family. Per Sanduhr playbook: 3–6 screenshots showing **different states** of the app proves the multi-feature value claim (10.1.4.4.b). Single-state screenshot reels read as single-view-utility — that's a rejection vector.

## Required dimensions

| Display family | Resolution | Notes |
|---|---|---|
| Desktop (Windows 11) | 1920 × 1080 (16:9) — preferred. 3840 × 2160 (4K) accepted. | Same artwork as the Win10 family. |

Take all screenshots at the **app's default Width=820** plus reasonable height — do NOT capture a window that's been dragged narrow (the row would horizontal-scroll, looks broken in marketing). Capture on a clean Windows 11 desktop with dark mode enabled.

## Capture list (6 screenshots — one per major feature surface)

Each screenshot needs an alt-text caption. Captions go in Partner Center's "Screenshots" section.

### 1. Account list — populated state

**What to show:** MainWindow with 3 saved accounts visible. Each row shows avatar + display name + game dropdown + Launch As + Remove. One account marked MAIN.

**Caption:** "Your saved accounts in one window — DPAPI-encrypted, click Launch As to open any account in its game."

**Setup:** Add three test accounts. Set one as MAIN (cyan star). Pick a different game per account in the dropdown.

### 2. Multi-instance running — three Roblox windows side by side

**What to show:** RORORO MainWindow on the left, three actual Roblox client windows tiled across the rest of the screen. Tray-on icon visible in the system tray.

**Caption:** "Multi-instance with one click — three Roblox clients, three accounts, one PC."

**Setup:** Launch three accounts via *Launch As*. Use Win+Arrow to tile. Capture full screen.

### 3. About box — branding + version

**What to show:** AboutWindow open over MainWindow. Direction C voxel stack visible, version + tagline + 626 Labs attribution.

**Caption:** "Multi-launcher for Windows — a 626 Labs product. Open source under MIT."

**Setup:** Click About in the toolbar. Capture.

### 4. Compact mode — running state

**What to show:** Compact-mode strip with a couple of running accounts and the "Stop" buttons visible.

**Caption:** "Compact mode shows only what's running — pin it to the corner of your screen."

**Setup:** Click Compact in the footer. Have two accounts running.

### 5. Diagnostics — health snapshot

**What to show:** DiagnosticsWindow with system health + Roblox/WebView2 versions + log location. Dark title bar (per the global theming hook).

**Caption:** "Diagnostics shows what RORORO sees right now — save the bundle when filing a bug."

**Setup:** Click Diagnostics in the toolbar. Wait for collection to complete.

### 6. Squad Launch — multi-account into one private server

**What to show:** SquadLaunchWindow with several accounts queued for the same private server URL.

**Caption:** "Squad Launch sends every selected account into the same private server — alts in formation."

**Setup:** Click *Private server* in the toolbar. Pick a server URL.

## Optional 7th screenshot

If we have room, capture **Friend Follow** open over the account list. Adds another social-use-case proof point.

## Capture procedure

1. Run a clean install of RORORO on a Windows 11 VM or fresh user account (avoid personal data leaking into screenshots).
2. Use the Snipping Tool (`Win+Shift+S`) — Window mode for #3, #5, #6; Rectangle mode for full-desktop #2.
3. Save as PNG (Partner Center accepts PNG / JPG; PNG is sharper).
4. Stage in `docs/store/screenshots/` (gitignored — these are big files).
5. Resize if any exceeds 5 MB — Partner Center's per-asset cap.

## Anti-patterns (don't ship)

- Screenshot of an EMPTY account list ("No saved accounts yet") — reads as "this app does nothing."
- Screenshot of an error dialog — even a tasteful one suggests the app is unstable.
- Screenshots with personal Roblox account names visible — use throwaway test accounts.
- Screenshots of the SmartScreen warning during sideload install — this is a sideload-distribution caveat, not a Store-distribution surface; doesn't apply once the Store path is live.
