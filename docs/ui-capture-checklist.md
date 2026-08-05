# Capture checklist — glow campaign: Settings + navigation

Scoped campaign. In-scope surfaces are the ones the goal names; neighbors are captured as
context for the consistency lens, not as fix targets. Evidence lands in `docs/ui-evidence/`
(gitignored) as `NN-<surface>--<theme>.png`.

**Themes, three rounds:** `brand` (default) · `magenta-heat` (built-in, flips the emphasis) ·
`flatline` (adversarial, authored for this campaign — one background colour, one text colour, one
accent; text stays legible so only colour-borne distinctions collapse).

> **Before you capture `preferences-alerts` (03b): that page renders live webhook URLs in full.**
> A Discord webhook URL is a bearer credential — the token segment is the entire auth, and anyone
> holding it can post to the channel. Wave 2's own scripted capture produced exactly such a PNG
> (deleted; the evidence dir is gitignored, so nothing was committed). Clear both webhook fields
> before capturing that page, or redact the PNG before it leaves this machine. Tracked as **F-076**,
> whose fix is to mask the stored value behind an explicit reveal.

**Rail pages are `-Watch` captures, not routes.** `docs/ui-routes.json` drives surfaces by
`InvokePattern` on a visible name. The rail's pages sit behind `ListBoxItem`s, which expose
`SelectionItemPattern` and no `InvokePattern`, so they cannot be routed — same rule the routes
file's own `_note` states for the tray menu and the library picker. Select the rail item, then
capture the foreground window.

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
| 05 | `about` | Candidate for relocation into Settings | About button |
| 06 | `history` | Candidate — but a tool, not a preference. The open question. | History button |
| 07 | `diagnostics` | Candidate — same open question as history | Diagnostics button |
| 08 | `games` | `Games/GamesWindow.xaml`, titled "RoRoRo -- Games" — renamed in wave 1 (F-006). Was `Settings/SettingsWindow.xaml` titled "RoRoRo -- Library": a class named SettingsWindow, in a folder named Settings, that is the game library. | Games button |
| 09 | `plugins` | Reached by a main-window button; a tools-container candidate | Plugins button |
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

Six competing conventions across 25 windows. Recorded here because it is the goal-4 evidence and
does not need a screenshot to be true:

- **`RoRoRo -- X`** — Diagnostics, History, Preferences, Plugins, Library, Build a theme, Install plugin
- **Prose with the app name** — "About RoRoRo", "Welcome to RoRoRo"
- **Bare noun** — "Join by link", "Squad Launch", "Rename", "Export accounts", "Import accounts", "Private server"
- **Problem statement** — "Roblox is already running", "Roblox needed", "Saved accounts can't be unlocked", "Microsoft WebView2 needed", "Leftover Roblox processes"
- **Imperative** — "Pick a title-bar color", "Add Roblox account — log in", "Stop all Roblox instances"
- **Absent** — `FriendFollowWindow.xaml` sets no `Title`

## Capture notes

- One monitor, one scale factor, for a whole round — Windows scale changes pixel dimensions.
- Switch the app's theme between rounds; re-run with the new `-Theme` label.
- `-DumpUia` on at least the `brand` round: the trees are route-map material and Narrator
  groundwork, and they record the accessible names the audit will want for the navigation lens.
