# Capture checklist — glow campaign: Settings + navigation

> **F-013 shell fold, 2026-08-21.** Games, Settings, History, Diagnostics, Plugins and About are
> now PAGES of one non-modal shell window with a persistent left rail; the six windows are gone.
> The routes still resolve because the shell's title tracks the active page (the captured Window
> name IS the destination noun), Settings keeps its inner `SettingsNav` rail, and the toolbar and
> Tools-menu doors are unchanged — they navigate the shell instead of opening a dialog. One
> captured name changed: About is now titled `About`, not `About RoRoRo` (the product-name
> exemption belonged to the window, and retired with it). Rows below that name the old window
> files describe where the surface CAME from; the current files are `*Page.xaml` beside them.

> **Corrected 2026-08-09 by measurement against the running app.** Three constraints below are
> false and the capture tool does not honour them.
>
> - "A popup is not in the main window's automation subtree even while open" is false. All eight
>   Tools items resolve from the main window. The five Tools destinations are routed, not watched.
> - "The rail's pages cannot be routed" is false. They expose `SelectionItemPattern`, and
>   `Select()` routes them. The claim conflated "carries no InvokePattern" with "cannot be routed".
> - The `flatline` round cannot run: the app ships `brand`, `midnight` and `magenta-heat`, and
>   `flatline` does not exist. The tool enumerates themes at runtime, so the round appears when the
>   theme does.
>
> The webhook warning below stands unchanged. `scripts/capture-ui.ps1` also refuses mechanically,
> but UIA text is not the rendered pixels, so the warning is backup rather than obsolete.

Scoped campaign. In-scope surfaces are the ones the goal names; neighbors are captured as
context for the consistency lens, not as fix targets. Evidence lands in `docs/ui-evidence/`
(gitignored) as `NN-<surface>--<theme>.png`.

**Themes, four rounds, enumerated from the ThemePicker at runtime:** `brand` (default) ·
`midnight` (built-in, cooler and dimmer) · `magenta-heat` (built-in, flips the emphasis) ·
`flatline` (built-in since v1.17, 2026-08-10; it began as this campaign's adversarial theme and
shipped as the fourth built-in: achromatic throughout, one page surface, one row surface, one text
colour, one light and one dark accent; text stays legible so only colour-borne distinctions
collapse). This paragraph said "three rounds" and called `flatline` campaign-only until
2026-08-30; the tool never hardcoded the list, so it captured all four the day `flatline` shipped.

> **Before you capture `preferences-alerts` (03b): that page renders live webhook URLs in full.**
> A Discord webhook URL is a bearer credential — the token segment is the entire auth, and anyone
> holding it can post to the channel. Wave 2's own scripted capture produced exactly such a PNG
> (deleted; the evidence dir is gitignored, so nothing was committed). Clear both webhook fields
> before capturing that page, or redact the PNG before it leaves this machine. Tracked as **F-076**,
> whose fix is to mask the stored value behind an explicit reveal.

**Correction, 2026-08-11 — the two paragraphs that used to sit here were wrong, and they were wrong
in the direction that costs manual work.** They said rail pages and the five Tools destinations
"cannot be routed" and that those routes were "gone from `ui-routes.json`," leaving `03-preferences`
as the only route-driven surface. `docs/ui-routes.json` contradicts every part of that: rail pages
03a-03d carry `select` steps, and 05 / 06 / 07 / 09 carry `expand` steps. The routes were restored
at some point and this file was never told. Verified by reading the route file and
`scripts/capture-ui.ps1`, not by reading a wave summary.

**The resolver has four verbs, not one.** `docs/ui-routes.json` does not drive everything by
`InvokePattern` — `$script:VerbPattern` in `scripts/capture-ui.ps1` maps `invoke` → `InvokePattern`,
`select` → `SelectionItemPattern`, `expand` → `ExpandCollapsePattern`, `close-window` →
`WindowPattern`, and resolution requires the verb's pattern as well as the type and name. That
pattern requirement is the disambiguator, not a formality: five elements in this app are named
"Settings" and only the Button carries Invoke.

**So the rail pages route.** Their `ListBoxItem`s expose `SelectionItemPattern`, which is exactly
what `select` asks for (surfaces 03a-03d).

**And the Tools destinations route.** A `ContextMenu` popup is genuinely outside the main window's
automation subtree, but the route never walks down to the `MenuItem` from the window root — it
resolves `Tools` itself, which is in the subtree, and `expand`s it. That works because
`ToolsDropDownButton` supplies `IExpandCollapseProvider` (`Controls/ToolsDropDownButton.cs:124-152`)
on top of the button peer. Wave 3's accessibility fix is the same thing that made these four
routable; the two were never separate.

**Games left the popup in v1.20** and is a main-window Button again, so surface 08 drops its
`expand` step entirely. `03-preferences`'s invoke name is `Settings` (the `⚙` glyph came off in
F-012).

**Still genuinely not routable:** the tray menu (surface 11) and the library picker (22). The route
file's own `_note` on each states why, and both reasons survive this correction.

## Route audit, 2026-08-12 — F-098's deferred item, run at the Store recapture it was parked for

F-098 left `capture-ui.ps1` unaudited on the grounds that auditing it means driving a live app,
so it is a walk rather than a test pass. This is that walk.

`-SelfTest` passes. `-Verify` against a live v1.21 build resolved and opened **all 14 routable
surfaces cleanly**, with 4 documented skips (02 needs a zero-account profile, 11 tray menu, 21
squad-launch is deny-listed, 22 join-by-link has no addressable anchor). **No route drift** — the
surface-08 correction made at v1.20 still holds, and nothing else moved under v1.21.

One defect in the harness, found by running it: **the script throws when invoked with a
POSIX-style relative path** (`powershell -File scripts/capture-ui.ps1`), because `$PSScriptRoot`
comes back empty and `Join-Path` rejects it. Invoke it by absolute path. Not fixed here; it is a
one-line guard and belongs to whoever next touches the script.

**The routes are current. What is stale is the output.**

## Store screenshots are staler than the scope note claimed

Measured 2026-08-12 against `docs/store/screenshots/`. All five shipped assets are 3840x2160
full-desktop captures, and all five predate the UI they depict:

| asset | captured | predates |
|---|---|---|
| `01-accounts-streamer-mode.png` | 2026-08-03 | v1.17 flatline through v1.21 |
| `03-about.png` | **2026-07-10** | v1.16 nav rail through v1.21 — six releases |
| `05-diagnostics.png` | **2026-07-10** | six releases |
| `06-squad-launch.png` | 2026-08-03 | v1.17 through v1.21 |
| `07-friend-follow.png` | **2026-07-10** | six releases |

The v1.21 scope note said the Store shots were "from Aug 3 and predate four releases". Three of
the five are a month older than that and predate six. The scope was written from the newest file's
date rather than from each file's.

**Two of the checklist's six were never captured at all** — #2 multi-instance and #4 compact mode,
which are also the only two requiring real Roblox clients on screen.

## What the capture tool can and cannot do for the Store set

`capture-ui.ps1` uses `PrintWindow` against a single HWND cropped to
`DWMWA_EXTENDED_FRAME_BOUNDS`. It produces **window** images for design evidence. Every shipped
Store asset is a **full-desktop 4K** frame. So the tool is not the instrument for this set, and a
recapture that used it would silently change the listing's visual format.

Two live constraints for whoever shoots them:

- **The profile on this machine has eight real saved accounts.** The account-list shot must run
  with streamer mode ON — which is exactly what `01-accounts-streamer-mode.png` did, and the
  checklist's own anti-pattern list forbids shipping real account names.
- **A full-desktop capture takes the whole desktop**, including whatever else is open. The
  checklist already says to shoot on a clean VM or fresh user account; that is a privacy
  requirement here and not a polish note.
## In scope

| NN | Surface | Why it is in scope | How to reach it |
| --- | --- | --- | --- |
| 01 | `main-window` | Hosts the buttons under review. The streamer-mode switch moved to Settings in wave 1 (F-008). | App launch |
| 02 | `main-window-empty` | Empty state — "Add your first account"; the chrome with nothing to hide behind | Launch with no accounts |
| 03 | `preferences` | THE area. Wave 2 (F-002/F-003) replaced the one tall scroll with a five-page nav rail; this row now captures the **Startup** page, the rail's default landing | Settings button, or tray → Settings |
| 04 | `preferences-scrolled` | ~~The bottom of the scroll~~ — **retired by wave 2.** There is no fold to fall below; the rail is the answer to "is anything undiscoverable" | n/a |
| 03a | `preferences-accounts` | Rail page 2. Three cards — the densest page, so the one where C5's fill-only grouping bites hardest under flatline | Settings → rail → Accounts |
| 03b | `preferences-alerts` | Rail page 3. Idle threshold + the Discord alert routing and both webhook fields | Settings → rail → Alerts & memory |
| 03c | `preferences-discord` | Rail page 4. Rich-presence toggles + the live status line | Settings → rail → Discord |
| 03d | `preferences-appearance` | Rail page 5. Theme picker + theme-builder entry | Settings → rail → Appearance |
| 05 | `about` | ~~Candidate for relocation into Settings~~ — wave 3 answered it: About is not a setting (it writes nothing), so it lives in Tools | **Tools ▾ → About** (`-Watch`) |
| 06 | `history` | ~~The open question~~ — wave 3 answered it. A tool, not a preference: verb-shaped, episodic, writes nothing to settings.json | **Tools ▾ → History** (`-Watch`) |
| 07 | `diagnostics` | Same answer as history (F-001) | **Tools ▾ → Diagnostics** (`-Watch`) |
| 08 | `games` | `Games/GamesWindow.xaml`, titled "Games" — renamed in wave 1 (F-006), product prefix dropped in wave 4 (F-004). Was `Settings/SettingsWindow.xaml` titled "RoRoRo -- Library": a class named SettingsWindow, in a folder named Settings, that is the game library. | **Games button** — route-driven again as of v1.20 |
| 09 | `plugins` | ~~Reached by a main-window button~~ — moved into Tools by wave 3 (F-009) | **Tools ▾ → Plugins** (`-Watch`) |
| 10 | `theme-builder` | A settings-adjacent tool that already lives outside Settings | Preferences → theme area |
| 11 | `tray-menu` | The other navigation surface entirely — it duplicates several main-window buttons | Right-click tray icon |

## Neighbors (context only — one hop)

| NN | Surface | Why captured |
| --- | --- | --- |
| 20 | `welcome` | First-run chrome; sets the title/heading convention a new user meets first |
| 21 | `squad-launch` | A main-window button target — checks whether "buttons lead where they say" holds |
| 22 | `join-by-link` | Same |
| 23 | `export-accounts` | A settings-shaped task that lives outside Settings |

## Out of scope

Error and consent modals (`DpapiCorrupt`, `RobloxAlreadyRunning`, `RobloxNotInstalled`,
`WebView2NotInstalled`, `LeftoverProcesses`, `StopAllConfirm`, `JoinRequest`, `ConsentSheet`,
`Rename`, `CaptionColorPicker`, `FriendFollow`, `CookieCapture`, `ImportAccounts`). They are
interruptions, not navigation — except that their **titles** feed the goal-4 title inventory,
which is gathered from source rather than screenshots.

## Title inventory (from source — no capture needed)

**Seven** competing conventions across 25 windows — corrected from "six" by the audit's skeptic
pass, which found the one this list called absent. Recorded here because it is the goal-4 evidence
and does not need a screenshot to be true:

- ~~**`RoRoRo -- X`** — Diagnostics, History, Preferences, Plugins, Library, Build a theme, Install plugin~~
  **Retired by wave 4 (F-004).** Those seven now read `Diagnostics`, `History`, `Settings`, `Plugins`,
  `Games`, `Build a theme`, `Install plugin` — the title bar names the destination, not the product.
  Enforced by `WindowTitleConventionTests`, so a regression fails the build rather than a review.
- **Prose with the app name** — "About RoRoRo", "Welcome to RoRoRo"
- **Bare noun** — "Join by link", "Squad Launch", "Rename", "Export accounts", "Import accounts", "Private server"
- **Problem statement** — "Roblox is already running", "Roblox needed", "Saved accounts can't be unlocked", "Microsoft WebView2 needed", "Leftover Roblox processes"
- **Imperative** — "Pick a title-bar color", ~~"Add Roblox account — log in"~~, "Stop all Roblox instances"
  **Partly retired by wave 13 (F-061).** The cookie-capture window now reads `Add account`, a bare
  noun, because that window showed three casings of one action at once — "Add Roblox account — log
  in" in the title bar, "Add Roblox Account" in its own header, "+ Add Account" on the button that
  opened it. The other two examples still stand, so the convention is not gone, only smaller. Struck
  rather than edited: this list is the goal-4 evidence, and evidence that gets quietly updated stops
  being evidence.
- ~~**Absent** — `FriendFollowWindow.xaml` sets no `Title`~~ **Wrong, and the reason the count
  was seven not six.** It sets one in code-behind: `FriendFollowWindow.xaml.cs:133` built
  `"ROROROblox -- Friends -- {name}"` at runtime — the only three-part title, the only one
  assembled in code, and the only one carrying the REPO name into user-facing chrome. A
  XAML-only sweep could not see it. Wave 4 changed it to `Friends — {name}` and added a test
  that scans code-behind assignments too, so this class of miss fails the build now.

## Capture notes

- One monitor, one scale factor, for a whole round — Windows scale changes pixel dimensions.
- Switch the app's theme between rounds; re-run with the new `-Theme` label.
- `-DumpUia` on at least the `brand` round: the trees are route-map material and Narrator
  groundwork, and they record the accessible names the audit will want for the navigation lens.
