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

## Shot list as captured, 2026-08-12 (v1.21.0.0) — supersedes the numbering above

All eight produced by `scripts/capture-ui.ps1 -StoreFrame -Theme brand`, which captures the
window with `PrintWindow` and composites it onto the brand navy. **1920x1080**, which this file
already called preferred, and at that size the window lands at native pixels — the assets these
replace were 3840x2160 with the window upscaled.

| file | surface | why it earns a slot |
|---|---|---|
| `01-accounts-running.png` | Accounts, **three clients live** | The hero. Streamer-mode identities, per-row `At Roblox home / idle / 1.1 GB`, Stop buttons, and `3 Roblox clients running · 3.3 GB` in the status bar. Tells the multi-instance story inside one window. |
| `02-themes.png` | Four themes, 2x2 | One slot for the whole theme range instead of four slots repeating one window. Includes `flatline`, which is the accessibility story. |
| `03-about.png` | About | Branding, version, 626 Labs attribution, MIT. |
| `04-games.png` | Games | Saved games and private servers — the launch-target library. |
| `05-diagnostics.png` | Diagnostics | Health snapshot, versions, log location. |
| `06-history.png` | History | Launch history, per-session. |
| `07-plugins.png` | Plugins | The plugin surface and its consent model. |
| `08-theme-builder.png` | Theme builder | Users can author their own theme, not just pick one. |

Eight is inside Partner Center's 1-9. Captions still need writing per the sections above.

### Two corrections this run made to this file

- **`docs/store/screenshots/` is NOT gitignored.** The procedure below says it is. The five assets
  it replaced were tracked and committed, and so are these. At 42-372 KB each that is fine; the
  "big files" caution applied to the 4K set.
- **The app's default width is 900, not 820.** The procedure says 820. `MainWindow.xaml` declares
  `Width="900" MinWidth="860"`. Moot for the tool, which captures whatever the window is, but the
  number would mislead anyone shooting by hand.

### Still owed, and none of it is automatable from a working desktop

- **Multi-instance, tiled desktop** (old #2). Partly told by `01-accounts-running.png`, but the
  three-Roblox-windows-side-by-side frame needs a full-desktop capture. That was attempted and is
  unshippable from this machine — see the route-audit section in `ui-capture-checklist.md`.
- **Compact mode** (old #4). Reachable via the footer `Compact` button, not a routed surface.
- **Squad Launch** (old #6). On the route file's deny list, and that entry is asserted by
  `UiRoutesSchemaTests` with a hardcoded count — removing it is a deliberate test edit, not a
  route change.
- **Friend Follow** (old #7). No route; needs `-Watch`.
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
