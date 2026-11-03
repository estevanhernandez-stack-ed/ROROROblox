# RoRoRo: the account manager and the Library

**Cycle:** opened 2026-11-01 on main at v1.35.0.0 (`5a8fe61`) · **Mode:** autonomous, Cart chain · **Predecessor:** v1.33 alerts cycle (`docs/cycles/2026-10-05-alerts/`) and the v1.35 field-report fixes (#241-#255)

**Anchor:** *Worth installing with one account.* (default — confirm on next interactive run)

## Idea

RoRoRo has about 300 daily users and one story: run several Roblox clients side by side, each signed in as a different saved account. The next version keeps that story and adds the two it has been quietly building toward: a roster that is a real account manager (what each account is, how it is doing, where it has been), and a game Library that is a place you browse rather than a settings page you visit. A person with one account should open RoRoRo and see a launcher that knows them, not an empty roster.

The shape Este chose: **the roster stays home.** Each account row opens into a panel with that account's recent games and a games picker; the main account's panel is open by default, every panel is collapsible and remembered. The Games page grows into the full Library. Nothing about the multi-account front door moves.

## Who it's for

Two users, and the first must lose nothing.

- **The clan multi-user** (the 300). Opens RoRoRo, launches six alts into one server, watches rows. For them this cycle is account health at a glance (expired vs needs-2FA vs captcha-marked), a per-account history they can open or ignore, and the backlog fixes that have been biting them (#260, #112/#27, #222).
- **The one-account player** who would install Bloxstrap or Fishstrap today. They want the FPS cap, Keep windowed, alerts to a phone, themes, a library of their games and private servers, and a launcher that remembers where they played. RoRoRo already has every one of those features. What it lacks is a first screen that shows them.

## Inspiration and references

- **Roblox Account Manager (ic3w0lf22)**: the account-list-plus-server-browser the clan compares us to. Archived; last release 3.7.2, 2024-07-18. The seat is open. https://github.com/ic3w0lf22/Roblox-Account-Manager
- **Bloxstrap** and **Fishstrap**: what one-account players install. FastFlags editor, FPS unlocker, fonts, player and message logs, themeable launcher. This is the bar for "worth it with one account." https://bloxstraplabs.com/wiki/about · https://github.com/imide/fishstrap
- **Playnite**: the library sensibility. Grid, list and details views, fast filtering, keyboard-first, aggregation that surfaces what you forgot you had. https://api.playnite.link/docs/manual/gettingStarted/playniteDesktopMode.html
- **Our own v1.21 scope** for the house style of a cycle doc (verified rows, named cuts, "what must not happen"): `docs/scope.md`.
- One SEO source claims a Roblox staffer said in April 2026 that multiple clients on one PC will not be supported. **Unverified**; recorded because, if true, one-account value is insurance and not just growth. Nothing here depends on it.

Design energy: the existing brand (cyan `#17d4fa`, magenta `#f22f89`, navy `#0f1f31`, Space Grotesk / Inter / JetBrains Mono). No new slot; the ten-slot theme contract is an invariant. The panel and the Library must read under flatline, which means every new status carries a non-colour signal.

## Goals

1. **One account is a complete install.** Fresh install, one account added, no empty-roster feeling: the row shows who this is and the panel shows where to go.
2. **The roster is an account manager.** At a glance per account: avatar, display and local name, presence, Robux, membership, friends online, last played, a private note, and a health state that distinguishes expired, needs-2FA and captcha-marked. Este: "yes to all," including the API cost, under the pacing we already live by.
3. **The Library is a place.** Recent, favourites and private servers in one browsable page with grid and list views and a filter; one click launches, with "play as" when there is more than one account.
4. **The backlog rides along by surface.** Every open task or issue that lands on the account row, the panel, the Library or the release goes in this cycle; the rest is named and left.
5. **vibe-iterate joins the toolchain.** Bootstrapped in this repo, with one `competitive` run against Bloxstrap and Fishstrap to produce the single-account feature candidates this scope cannot guess at.

## What "done" looks like

- A one-account install shows a roster of one with the panel open: Recent (from session history), Favourites, Private servers, each one click to launch. The first-run tour speaks to one account first and alts second.
- Every row carries the account facts above. Facts refresh on panel expand, on a launch ending, and on a slow timer; never as a startup burst. Streamer mode masks Robux and friends the way it masks names.
- Health states are distinct and actionable: Expired (re-authenticate), Needs 2FA (verify, with the task's distinction built), Captcha-marked (mark, acknowledge at Launch All, unmark; task `g5fgibvy7`).
- The Games page is the Library: grid and list, search, filter by recent / favourite / private server, per-game "play as" picker.
- Multi-account users see the same roster; non-main panels start collapsed; nothing about Squad Launch, Launch multiple, Recycle, auto-rejoin or alerts changes behaviour.
- #260 (cursor invisible inside RoRoRo, touch works) is understood and fixed or ruled with evidence.
- The Store snippet and long description say "launcher first, alts when you want them"; the listing audit runs on all three surfaces per the release rule; the support email typo is fixed in the same submission.
- `.vibe-iterate/config.json` exists and one competitive run has merged a PR or recorded a measured no-op.
- Suite green on x64 and arm64 with every fence ceiling moved in the same commit as its change.

## Backlog map by surface

| Surface | Rides in this cycle | Left out, named |
|---|---|---|
| Account row and panel | task "Distinguish session expired vs needs-2FA" (`vpk6ay1eu`); task "Captcha-marked accounts" (`g5fgibvy7`, high); #112 and #27 (captcha loop reports); #221 (ActivityMonitor credits the wrong account) | |
| Window and chrome | #260 invisible cursor (filed 2026-11-01); #222 windowless strays (verify against v1.35's idle tray-client clear, then close or keep) | |
| Library | this cycle's own work | |
| Release and listing | tasks: support contact (`bxranna8z`), support-email typo (`0h7s65ljx`), catalog publisher (`g5hjernie`, in progress), catalog stale at tag time (`eejy3xvcu`, in progress, #231); #232 (translations untied from the English long description); #52 (Diagnostics reports true version and channel) | |
| Plugins | #230 / task `y6u70eghn` (Silent is not silent) only if the in-progress fix lands first | #67 host window geometry (own cycle) |
| Elsewhere | | AI connector (`q60h0q3op`, specced, its own three-repo cycle); Ur Reset plugin idea; game-map idea; version-gate spike rescope; #259 Linux (answer honestly: WPF, DPAPI and the Win32 name race rule it out) |

## What's explicitly cut

- **The home-screen flip** (library as home, roster as a rail). It is the better product on paper and it rewrites a 2,129-line window the 300 look at daily. Revisit after this ships if one-account users still bounce.
- **Account groups.** The spec file still says "Approved-shape" in its own banner; `docs/decisions.md` (2026-08-30) and `features.md` say abandoned. The ruling wins, and this cycle gives it no new reason. The spec gets a banner saying so.
- **Startup per-account validation.** Removed in v1.1.2 because it tripped Roblox anti-fraud. The new account facts never run as a startup sweep; they join the backfill lane's pacing (2.5 s ± 0.5 s, sequential) and refresh lazily.
- **Linux (#259).** Out, with a straight answer on the issue.
- **A new theme slot or a new window.** The panel lives in `MainWindow`; the Library is the Games page grown.
- **Anything in-game.** The macro wall holds: core observes, plugins act.
- **Plugin host and AI connector work** beyond the two in-progress catalog tasks.
- **Quiet hours, player logs, FastFlags editor** from the Bloxstrap/Fishstrap list. Candidates for the vibe-iterate run to rank, not commitments here.

## Assumptions carried (each one sentence to overturn)

1. Every account gets a panel; main starts expanded, others collapsed.
2. The note is local, free text, exported with the account like tags.
3. Robux and membership read through documented Roblox endpoints with the account's own cookie, same `UseCookies=false` handler rule, same `RORORO/<version>` User-Agent; exact paths verified at `/spec`, not assumed here.
4. Facts are cached on disk only as far as the UI needs to paint before the first refresh; Robux is never written to a plaintext store.
5. vibe-iterate's PR is reviewed like any cycle item; its Atlas entry lives beside the cycle docs.

## Loose implementation notes (non-binding; `/spec` decides)

- **Data already exists.** `LaunchSession` carries `AccountId`, `GameName`, `PlaceId`, `IsPrivateServer`, timestamps; `session-stats.json` has per-game time; `Account.LastLaunchedAt` and `AvatarUrl` exist; `friends.roblox.com/v1/users` and `thumbnails.roblox.com` are already called. Recent-per-account is a query, not a store.
- **New Core service**, `AccountFactsService` or similar: one typed client, one ctor, `ILogger<T>` (the single-ctor guard is why). Rides the backfill lane. Exposes a snapshot the VM reads; off the UI thread read `AccountsSnapshot`, never `Accounts`.
- **Panel view model as its own class.** `MainViewModel` is ~4,300 lines; a per-account `AccountPanelViewModel` keeps the growth out of it and gives the fences a smaller target.
- **Expanded state** is a per-account field (like `JoinViaFriend`), not a global setting, so the "new setting" tax (SettingsBlob + four fakes + reachability) is paid once if at all.
- **Library views** on the existing `GamesPage` (407 + 416 lines): grid/list toggle, filter chips, a "play as" flyout that reuses the account picker Squad Launch already has.
- **Fences that will move:** `AccessibleNamingFenceTests` (every new badge named), `ThemedStatusColourTests` (health states via `DynamicResource`, non-colour carrier for each), `TypeLadderFenceTests`, `WindowContentFits`. Move the ceiling in the same commit.
- **Streamer mode** must mask the new facts; the masking surface is the account manager by contract.
- **#260** is a WPF cursor question first (`Cursor="None"`, a custom cursor resource, a touch-laptop driver), not a Roblox one; reproduce before theorising.
- **Privacy doc** gains a line: Robux and membership are read with the account's own cookie, shown, not kept.

## What must not happen

- Do not reintroduce a per-account call at startup. That is the v1.1.2 anti-fraud lesson and it is the easiest mistake in this cycle.
- Do not change what a six-alt user sees first. Collapsed panels, same roster, same launch paths.
- Do not let a new status speak in colour alone. Flatline is the test frame.
- Do not grow `MainViewModel` to carry the panel. New class.
- Do not ship the listing rewrite without the three-surface audit; v1.25's privacy paragraph was the near-miss.
