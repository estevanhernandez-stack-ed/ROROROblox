# Store screenshots — RORORO

> Partner Center accepts 1–9 screenshots per device family. Per Sanduhr playbook: 3–6 screenshots showing **different states** of the app proves the multi-feature value claim (10.1.4.4.b). Single-state screenshot reels read as single-view-utility — that's a rejection vector.

> **Count conflict, unresolved as of 2026-08-12.** The line above says 9. The shipped set is **10**.
> Nobody has re-read Partner Center's current limit — the 9 was copied from the Sanduhr playbook,
> not from the upload form. **Check the actual cap before uploading.** If it is 9, drop
> `08-theme-builder.png`: it is the most niche of the ten, and `02-themes.png` already carries the
> theming story. Do not drop `10-multi-instance.png` — it is the only frame that shows the product
> doing the thing the listing is about.

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
| `09-compact.png` | **Compact mode, three clients live** | The pinned strip: three accounts, per-client memory, Stop on each, `3 accounts idle > 12m`, and an Expand link back. Captured with `-NoThemeSwitch`, because compact hides the toolbar the theme picker is reached through. |

Nine, which is exactly Partner Center's ceiling. Captions still need writing per the sections
above.

`09-compact.png` sits small on its canvas -- the compact window is about 490x405 against a
1920x1080 ground. Left as-is deliberately: the negative space IS the feature, and the caption
says "pin it to the corner of your screen". If it reads as sparse in the carousel, re-shoot it
with `-CanvasWidth 1280 -CanvasHeight 720` rather than upscaling the window.

### Captions, written 2026-08-12 against the frames rather than the brief

Partner Center shows these under each screenshot, and they double as the alt text, so they are
written to be useful read aloud with no image at all. Second person, sentence case, no
marketing verbs. **Each one describes what is actually visible in its own frame** — the earlier
captions in this file were written before the shots existed and promised things the frames do not
show.

| file | caption |
|---|---|
| `01-accounts-running.png` | Three accounts running at once, each with its own memory use and its own Stop button. Cookies are encrypted per user with Windows DPAPI and never leave the machine. |
| `02-themes.png` | Four built-in themes. Flatline carries no meaning in colour at all, so nothing is lost to colour blindness, a bad panel, or direct sun. |
| `03-about.png` | Multi-launcher for Windows. Holds the Roblox singleton mutex so the next client opens instead of fighting the first. A clean reimplementation, not a fork. |
| `04-games.png` | Save the games and private servers you actually play, then pick a different one per account before you launch. |
| `05-diagnostics.png` | Diagnostics shows what RoRoRo can see right now: versions, health, and where the logs are, for when you need to file something. |
| `06-history.png` | Every launch is recorded, so you can see which account played what and for how long. |
| `07-plugins.png` | Plugins run as separate processes and ask first. You grant each capability by name, and you can revoke it later. |
| `08-theme-builder.png` | Build a theme from ten colours and it shows up in the picker. It is a JSON file, so you can hand it to someone else. |
| `09-compact.png` | Compact mode shows only what is running. Pin it to a corner of the screen and get back to the game. |
| `10-multi-instance.png` | Eight Roblox clients, eight accounts, one PC. Each window title carries the account signed into it, so you always know which is which. |

**Every claim above is checkable in the frame or in the code.** The mutex line is spec section 7.1,
the DPAPI line is the About box's own text, ten colours is `Theme`'s slot count, and the plugin
consent line is what `ConsentSheet` actually does. Nothing here claims automation, macros or input
scripting, which is a deliberate Roblox-relations line and not an oversight — that is MaCro's
territory and the wall is on purpose.

The names visible in every frame are streamer-mode identities, not real accounts.

### `10-multi-instance.png` is the one frame that is not raw

Every other shot comes out of `capture-ui.ps1` untouched. Shot 10 could not: it is a real 4K desktop
with eight live clients in a real Pet Simulator 99 server, and the game renders **other players'
overhead nameplates**, which streamer mode does not cover. Three real usernames were legible in it.

So the frame is cropped to the tile band and the eight nameplate regions are blurred. Two things
worth knowing before anyone re-shoots or edits it:

- **Automatic detection failed twice, and both failures looked like successes.** A "near-white text
  run" detector reported 32 plates and blurred none of them; a rewrite without the merge step
  reported zero. These scenes are mostly bright, so whiteness does not isolate glyphs — runs either
  swallow the sky or get filtered out for being too wide. The working version is eight hand-measured
  boxes.
- **Verify by magnifying the blurred region, not by looking at the frame.** At review size the
  redaction looked complete all three times. Only a 4-5x crop showed `PapasbbBri` surviving 30 px
  above box 4. Any future edit to this asset gets the same check per tile.

The cleaner fix, if this frame is ever re-shot: put the alts in **different servers** so no client
renders another's nameplate, and no redaction is needed at all.

### Two corrections this run made to this file

- **`docs/store/screenshots/` is NOT gitignored.** The procedure below says it is. The five assets
  it replaced were tracked and committed, and so are these. At 42-372 KB each that is fine; the
  "big files" caution applied to the 4K set.
- **The app's default width is 900, not 820.** The procedure says 820. `MainWindow.xaml` declares
  `Width="900" MinWidth="860"`. Moot for the tool, which captures whatever the window is, but the
  number would mislead anyone shooting by hand.

### Still owed, and none of it is automatable from a working desktop

- ~~**Multi-instance, tiled desktop** (old #2).~~ **Shot 2026-08-12 as `10-multi-instance.png`** —
  eight clients, hand-tiled by Este, cropped and redacted per the section above. The earlier
  attempt from this machine caught client folder names and an Insider watermark and was deleted;
  what shipped is the tile band only, nothing of the desktop behind it.
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
- **In-game frames where other players are on screen.** Streamer mode renames *your* accounts in
  RoRoRo's own UI; it has no reach into the game, so strangers' overhead nameplates render in full.
  Shoot in a private server, or put the alts in separate servers. Redacting afterwards works but is
  the expensive path — see the shot 10 section.
- Screenshots of the SmartScreen warning during sideload install — this is a sideload-distribution caveat, not a Store-distribution surface; doesn't apply once the Store path is live.
