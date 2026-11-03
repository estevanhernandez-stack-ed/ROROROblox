<!-- Cart cycle #6, step 3. Input: scope.md beside this file. Written to docs/cycles/2026-11-01-account-library/
     rather than docs/prd.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md treats as history.
     Autonomous run (profile autonomy_level: fully-autonomous, builder: self). Every beat the record did
     not answer is marked "(default — confirm on next interactive run)" and collected at the end. -->

# RoRoRo: the account manager and the Library — Product Requirements

**Cycle:** opened 2026-11-01 on main at v1.35.0.0 · **Scope:** `scope.md` beside this file · **Anchor:** *Worth installing with one account.*

## Problem Statement

RoRoRo already holds a complete one-account launcher: FPS cap, Keep windowed, phone and Discord alerts, themes, a games library, join by link, session history. A person who installs it with one account never finds any of that, because the first screen is a roster built for six alts, and a roster of one reads as empty. The 300 daily clan users have the opposite gap: a row tells them a name and a presence, and nothing else about the account they are about to launch. Expired, rate-limited and captcha-walled accounts all look like "something is wrong" with no next move. The Games page is a settings form (search, paste, save, set default) rather than a place anyone would open to decide what to play. This cycle fixes both from the same shape: the roster stays home, each row opens into that account's panel, and the Games page grows into the Library.

## User Stories

### One account is a complete install

The anchor epic. If this fails the rest is decoration.

- As someone who installed RoRoRo for the FPS cap and alerts and has exactly one Roblox account, I want the first screen to look like a launcher that knows me, so that I do not wonder whether I installed a clan tool by mistake.
  - [ ] After Add account on a fresh install, the roster shows one row and that row's panel is **open**, without a click.
  - [ ] The open panel shows three groups with headings: **Recent**, **Favourites**, **Private servers**. On a fresh install all three are empty, and each empty state says what to do in one line: play something, save a game from the Library, paste a private-server link.
  - [ ] The panel carries a plain launch affordance that opens Roblox home for this account when nothing is saved yet, so a one-account user can play within two clicks of adding the account.
  - [ ] Nothing on the first screen says "alts", "squad", "batch" or "multiple" above the fold when there is one account. Those controls still exist and still work, but the one-account window does not lead with them.
  - [ ] The window with one account and the panel open fits the default window size without a scrollbar on a 1080p display at 100% and 125% scale.

- As a first-time user, I want the welcome tour to speak to me first and to alts second, so that the first minute matches what I came for.
  - [ ] The tour's opening line names the launcher (one account, your games, your settings) before it names side-by-side clients.
  - [ ] Step order is: add an account, pick or play a game from the panel, then "when you want alts" as the last step.
  - [ ] The row quick-tips that only matter with several accounts (batches, follow, main star) sit under an "with more than one account" heading inside the tour.
  - [ ] The tour still shows only on first run with an empty roster, and the sentinel rule is unchanged.
  - [ ] Every tour string exists in English and all six cultures through the catalog pipeline, and the verifier has reviewed the delta before tag.

### The row knows who this is

- As someone with several saved accounts, I want each row to show what the account is and how it is doing, so that I pick the right one without launching it to find out.
  - [ ] Every row shows: avatar, display name, local name when one is set (local name wins as today), presence (unchanged ladder), **Robux**, **membership** (Premium or none), **friends online** (a count), **last played** (game name and how long ago), and the first line of a **private note**.
  - [ ] Facts that are not yet known show an unobtrusive placeholder (a dash), never a zero and never a stale number from a different account.
  - [ ] Facts refresh on three triggers only: when the account's panel is expanded, when a launch for that account ends, and on a slow timer. **Timer default: every 10 minutes per account, staggered, one account at a time (default — confirm on next interactive run).**
  - [ ] Facts **never** refresh as a startup sweep. On launch, with eight accounts saved, the log shows zero fact requests before the first panel expand or the first timer tick, and the first timer tick is no sooner than the existing backfill lane's start.
  - [ ] Fact requests go one account at a time with the backfill lane's pacing (2.5 s ± 0.5 s between accounts). Two accounts' facts are never requested in the same second.
  - [ ] A fact request that fails (rate limit, offline, expired cookie) leaves the previous value in place with an age hint, and never flips the account's health state on its own.
  - [ ] Robux is shown and not kept in any plain-text file. If a cached copy exists so the row can paint before the first refresh, it lives only where the cookie lives, or not at all. **Default: cached in the vault blob alongside the account record (default — confirm on next interactive run).**
  - [ ] Every new badge has an accessible name that reads the fact and its value ("Robux, 1,240"), and the naming fence ceiling moves in the same commit.

- As a clan user who runs the same eight alts daily, I want to attach a note to an account, so that I remember what it is for without a naming scheme.
  - [ ] A note is free text, local, up to **200 characters (default — confirm on next interactive run)**, edited inline from the panel header, saved on focus loss, no dialog.
  - [ ] The row shows the note's first line; the panel shows all of it.
  - [ ] The note travels with the account in export and import, the way tags do.
  - [ ] Clearing the note removes the line from the row; nothing else moves.

- As someone who watches the roster all evening, I want the facts to cost nothing I can notice, so that eight rows of facts do not make RoRoRo feel slower.
  - [ ] With eight accounts and every panel collapsed, RoRoRo makes no more Roblox calls per minute than v1.35 does today, except the slow timer's own, which is at most one account per tick.
  - [ ] Expanding and collapsing a panel repeatedly does not re-request facts younger than **60 seconds (default — confirm on next interactive run)**.

### Health states you can act on

- As someone whose account stopped launching, I want the row to say which kind of stopped it is, so that the next move is obvious.
  - [ ] Three health states are distinct in word, glyph and colour: **Expired** (re-authenticate), **Needs verification** (open Roblox and complete the challenge), **Follow-marked** (this account joins by following, see the next story). They never share a glyph.
  - [ ] Each state names its action in the row's secondary line and offers that action one click away: Expired offers Re-authenticate; Needs verification offers Open Roblox for this account; Follow-marked offers the mark toggle.
  - [ ] A state is shown only when a measured signal backs it. **Needs verification ships only if a signal distinct from Expired and from the plain 403 limit is found and recorded during build.** If no distinct signal exists, the row keeps today's two words (Expired, Limited by Roblox) and the PRD's third state is dropped with a note in decisions.md, not shipped on a guess.
  - [ ] Under the flatline theme, every state is still tellable apart with colour removed: a glyph or a word differs for each.
  - [ ] Health beats presence in the row's secondary line, as Expired does today.

- As someone with a captcha-prone alt, I want to mark it so that Launch all never fires it directly and never silently drops it, so that my follow-cancel-follow workaround becomes the app's behaviour.
  - [ ] The mark is the existing per-account **Join via friend** flag, named as a health state on the row, not a second flag. One flag, one meaning.
  - [ ] Launch all and Launch multiple surface marked accounts in the batch summary ("2 will follow the main") instead of a count nobody reads. They are launched by follow when the main is joinable and held with a reason when it is not, which is today's rule, now visible.
  - [ ] The mark is set and cleared from the row's context menu and from the health badge. "It's fixed, join directly" still clears it.
  - [ ] An account that returns a 403 (Limited by Roblox) is **not** automatically marked. The equivalence between the 403 and the in-client captcha is unproven (task `g5fgibvy7` cluster note). The first build task runs the experiment: put one account into the captcha state and record what the ticket and presence endpoints return. The result goes to decisions.md and decides whether Limited and Follow-marked may ever merge.

### The panel: where this account has been

- As a clan user, I want to open an account's row and see where it has been, so that rejoining yesterday's server or game is one click, not a search.
  - [ ] Each row has an expander. Expanding shows that account's **Recent** games: the distinct games from this account's launch history, most recent first, each with icon, name, when it was last played, and how long it was played in total.
  - [ ] Recent is derived from session history and private-server history. **Window: the account's rows within the rolling 100-row history, capped at the 8 most recent distinct games (default — confirm on next interactive run).**
  - [ ] A Recent entry for a game that is not in the Library offers **Save** inline; saving adds it to the shared Library.
  - [ ] A Recent entry for a private server shows the server's name and the place, with the Private badge the Library already uses.
  - [ ] **Favourites** and **Private servers** in the panel are the shared Library, in the Library's order, with this account's last-played age on each. They are not a second list.
  - [ ] One click on any panel entry launches **this** account into it, with the same guards the row's Launch has (expired, marked, main not joinable). Nothing in the panel bypasses a launch rule.
  - [ ] The panel never shows another account's history. Tested with two accounts that played different games.

- As someone with six alts, I want the panels out of the way until I ask, so that my roster looks the way it did yesterday.
  - [ ] The main account's panel starts expanded on first run after upgrade; every other panel starts collapsed.
  - [ ] Expanded or collapsed is remembered per account across restarts. It is not a global setting and it is not exported.
  - [ ] With no main set, the first row's panel starts expanded **(default — confirm on next interactive run)**.
  - [ ] Collapsing every panel yields a roster whose row height and controls match v1.35 to the pixel, except for the new badges and the expander.
  - [ ] Compact mode collapses all panels and hides the expanders; leaving compact mode restores each panel's remembered state.
  - [ ] The expander is a real toggle with an accessible name ("Show recent games for Pokey") and works from the keyboard.

### The Library is a place

- As a one-account player, I want a page I open to decide what to play, so that RoRoRo replaces the Roblox website for that moment.
  - [ ] The Games page is renamed **Library** on the rail, in the window title, and in every string that pointed at it. The shortcut is unchanged.
  - [ ] The Library shows one combined set of **Favourites**, **Private servers**, and **Recent** (games played from RoRoRo by any account that are not saved), each entry with icon, name, local name when set, Default and Private badges, last played and total time.
  - [ ] Two views, **grid** and **list**, switched by a toggle that is remembered. Grid shows icon-first tiles; list shows one row per game with the same facts in columns.
  - [ ] A filter row with three chips, **Recent**, **Favourites**, **Private servers**, multi-select, remembered. With none selected everything shows.
  - [ ] One search box. Typing filters the Library instantly by name and local name. Pressing Enter with no local match runs the Roblox search the page already has, and results appear in a clearly separate "From Roblox" group with the existing Add affordance.
  - [ ] Paste a game URL, a place id, or a private-server link into the same box to add it, as the two inputs do today. The two inputs collapse to one.
  - [ ] Set default game, set default server, rename, reset name, remove: all still reachable from each entry and keyboard-operable.
  - [ ] Empty Library on a fresh install shows one invitation: search or paste a link, with a sentence that says saved games appear in every account's panel.
  - [ ] With 100 saved games the grid scrolls smoothly and the filter responds within a keystroke, no spinner.
  - [ ] The page reads under flatline: badges carry a word, selected chips carry a check glyph, not only a fill.

### Play as

- As someone with a main and alts, I want to pick who plays from the Library, so that the Library is not main-only.
  - [ ] Each Library entry has **Play** and a **Play as** flyout. Play launches the main account; with one account it launches that account. **With no main set, Play launches the first row's account (default — confirm on next interactive run).**
  - [ ] Play as lists every saved account with avatar, name and health state. Expired accounts are listed but disabled with the reason; follow-marked accounts launch by follow as everywhere else.
  - [ ] The flyout is the same account picker Squad Launch uses, not a new control.
  - [ ] Playing from the Library records the session in history under the chosen account and updates that account's Recent and last played, so the panel and the Library agree within one refresh.
  - [ ] Play as from the Library obeys every launch rule the row's Launch obeys: the pre-launch idle tray-client clear, the install deferral gate, the flagged-launch ask.

### Streamer mode covers the new facts

- As a streamer, I want everything new to be masked the way names are, so that turning on streamer mode is still one switch.
  - [ ] With streamer mode on, Robux, membership and friends online show as masked placeholders, not real values, and the private note is hidden entirely **(default — confirm on next interactive run)**.
  - [ ] Recent and last played keep game names (games are public) but show the masked account identity wherever an account is named.
  - [ ] The Library's Play as flyout shows masked names and avatars.
  - [ ] The smoke harness's streamer scenario gains one assertion per new fact.

### Nothing moves for the six-alt user

- As a clan user, I want this release to change nothing about how I launch, so that an update does not cost me an evening.
  - [ ] Launch all, Launch multiple, Squad Launch, Recycle, auto-rejoin, join via friend, alerts and every plugin RPC behave exactly as in v1.35. The existing test suite is the measure, plus the harness.
  - [ ] Row order, drag to reorder, the main star, tags and the tag filter are unchanged.
  - [ ] The main window's startup time with eight accounts is within 10% of v1.35 on the same machine. Panels and facts add no startup network calls.
  - [ ] The ten-slot theme contract is unchanged. Every new colour is a `DynamicResource` of an existing slot or a derived brush.
  - [ ] The fences that will move (accessible naming, themed status colour literals, type ladder raw sizes, window content fits) move in the same commit as the change that moves them.

### Rider: input credit goes to the right account (#221)

- As a keep-alive plugin user, I want RoRoRo to credit input to the account that actually received it, so that an idle alt is not reported as active and kicked.
  - [ ] Input is attributed to the account whose window was foreground **when the input happened**, not when the sample ran. The issue's reproduction (bring B forward, tap, restore C within one second) credits B, not C.
  - [ ] A plugin reading account activity for an account that received no input sees its idle age keep climbing.
  - [ ] The fix is covered by a test that replays the 2026-09-29 timeline from the issue and fails on the old code.
  - [ ] The plugin-side half (rororo-ur-afk#21) is referenced in the closing comment; this cycle does not change the plugin.

### Rider: the cursor is visible (#260)

- As a laptop user, I want to see my mouse cursor inside RoRoRo, so that I can use it without a touch screen.
  - [ ] The report is reproduced or ruled with evidence. Evidence means: the reporter's diagnostics bundle, Windows build, display scale, cursor scheme (custom .cur schemes and software cursor size are the two WPF suspects), and whether the cursor is visible in other WPF apps on that machine.
  - [ ] Nothing in RoRoRo sets the cursor to none; a fence asserts that no XAML or code sets `Cursor` to None or to a custom cursor resource, so the class of bug cannot enter later.
  - [ ] If the cause is environmental, the issue is answered with the workaround and the Known Roblox issues list is **not** used (it is for Roblox-side problems).
  - [ ] If the cause is ours, the fix ships this cycle and the issue closes with the measured before and after.

### Rider: windowless strays (#222)

- As someone who runs RoRoRo all day, I want yesterday's leftover client processes to stop piling up, so that my memory is not spent on ghosts.
  - [ ] A one-day run with ten launches is measured on a dev build: the count of windowless `RobloxPlayerBeta` processes at end of day, and their ages.
  - [ ] If v1.35's pre-launch idle tray-client clear already holds the count at or below one between launches, #222 closes with that measurement as the evidence.
  - [ ] If strays still accumulate, a periodic sweep on the existing 30 s tick ends windowless, untracked tray clients older than two minutes, using the same rule as the pre-launch clear, never a client that is still creating its window, and never a client another tool is using.

### Release and listing: launcher first

- As a Store browser with one account, I want the listing to tell me RoRoRo is for me, so that I install it.
  - [ ] The short description leads with the launcher (your games, FPS cap, alerts to your phone, themes) and says "alts when you want them" within its 200 characters.
  - [ ] The long description's first paragraph is the one-account story; the multi-account story is its own paragraph and loses nothing it claims today.
  - [ ] The features list adds the account manager (facts, health states, notes) and the Library (grid, list, filters, play as).
  - [ ] All seven sheets (English plus six) carry the same edit, with dated provenance notes, before the listing is written.
  - [ ] The three-surface audit (short, long, features) runs against the fresh release notes and is recorded in the submission packet.
  - [ ] Support contact in Partner Center reads `support@626labs.dev`, the doubled-t typo is gone, and the public catalog is re-read after publish to confirm (tasks `bxranna8z`, `0h7s65ljx`).
  - [ ] The What's new template and README say to quit RoRoRo before updating from the Store (the second half of `bxranna8z`).
  - [ ] The privacy doc gains one line: Robux and membership are read with the account's own cookie, shown, and not kept in any plain file.

### Rider: translations tied to the English long description (#232)

- As the person who edits the listing, I want a fence that fails when the six sheets drift from the English one, so that the next edit cannot ship six wrong listings.
  - [ ] A test reads the seven listing sheets, discovers cultures from the directory, and fails if bullet counts or enumeration lengths in the long description differ across sheets. It has a vacuity floor so an empty directory fails.
  - [ ] The fence passes on the rewritten sheets this cycle ships.

### Rider: a tag cannot republish a stale catalog row (#231, `eejy3xvcu`, `g5hjernie`)

- As the person who tags a release, I want the tag to refuse when the plugin catalog disagrees with a live plugin release, so that direct-download users are not offered old plugins again.
  - [ ] Before the catalog is attached on a tag, each row's latest version is compared with its repository's latest non-draft, non-prerelease release; a disagreement fails the tag step, not the main-branch guards.
  - [ ] The catalog's publisher reads `626Labs LLC` on all five rows (entity reference, no space), and "626 Labs" with a space is untouched in prose.
  - [ ] The ur-mcp row matches the live release at tag time.

### Rider: Diagnostics tells the truth about version and channel (#52)

- As someone filing a bug, I want the Diagnostics page to say which version and which channel I am on, so that triage starts from facts.
  - [ ] Diagnostics shows the version the About page shows (package version when packaged, assembly otherwise) and a channel line: Store, direct download, or dev.
  - [ ] The support bundle carries the same two facts.

### vibe-iterate joins the toolchain

- As the builder, I want a competitive read of Bloxstrap and Fishstrap that turns into ranked single-account candidates, so that the next cycle's one-account features are chosen from evidence.
  - [ ] `.vibe-iterate/config.json` exists with the app type, framework pins and both competitor URLs.
  - [ ] One `competitive` run has executed. Its output is either a merged PR with an Atlas entry naming what was and was not copied, or a recorded measured no-op with the candidate list.
  - [ ] The Atlas entry lives beside the cycle docs.
  - [ ] Quiet hours, player logs and the FastFlags editor appear in the candidate list ranked, not built.

### Conditional rider: Silent means silent for plugins (#230, `y6u70eghn`)

Rides only if the in-progress host-side fix lands before the cycle's first release branch. Otherwise it is named in the release notes and left.

- As someone who set RoRoRo to Silent, I want plugins to go quiet too, so that Silent means the whole app.
  - [ ] A plugin can read the host's sound preference through the contract, additively.
  - [ ] Settings wording says what Silent covers if the contract change does not ship.

## What We're Building

Must-have, in build order. The first item gates the health states; facts and the panel paint them.

1. **The health experiment and vocabulary.** Run the captcha experiment (task `g5fgibvy7`), record it, and settle the state set: Expired, Follow-marked (the existing flag, renamed on the surface), and Needs verification only if a distinct signal exists. Each state gets a glyph, a word and an action.
2. **Account facts on the row.** Robux, membership, friends online, last played, note. Refresh on expand, on launch end and on the slow timer; never at startup; one account at a time at backfill pacing. Robux never in a plain file.
3. **The per-account panel.** Expander per row, Recent from history, Favourites and Private servers from the shared Library, one-click launch as this account, state remembered per account, main open by default, others collapsed, compact mode collapses all.
4. **The Library.** Rename, combined set with Recent, grid and list, three filter chips, one search-and-paste box, Play and Play as, all existing management actions kept.
5. **The first-run tour** rewritten for one account first.
6. **Streamer masking** of every new fact and the note.
7. **#221** input attribution fix with the timeline test.
8. **#260** diagnosed with the evidence bar above; fixed if ours, answered if not; cursor fence either way.
9. **#222** measured; closed on evidence or swept on the tick.
10. **Release lane.** Diagnostics version and channel; catalog publisher and tag-time drift gate; listing-sheet parity fence; listing rewrite on all seven sheets; three-surface audit; support contact and typo; privacy line; translation verifier on the string delta.
11. **vibe-iterate** bootstrapped and one competitive run executed.
12. **Account-groups spec banner** saying abandoned, pointing at the 2026-08-30 ruling.
13. **#230** if its fix lands in time.

## What We'd Add With More Time

- **The home-screen flip.** Library as home with the roster as a rail. Revisit if one-account users still bounce after this ships.
- **Per-account favourites.** Today the Library is shared; a per-account "mine" filter is the cheap version if a clan asks.
- **Sorting the Library by total time played or by last played**, and a "forgotten" shelf, the Playnite move.
- **Quiet hours, player logs, FastFlags editor**, ranked by the vibe-iterate run, not committed here.
- **Richer Recent:** server names for public servers, who was online with you, from presence snapshots.
- **A health history line** per account: when it expired, when it was marked, when it recovered.
- **Game icons in the panel header** and a per-account banner colour drawn from the caption colour.

## Non-Goals

- **No home-screen flip this cycle.** It rewrites the 2,129-line window the 300 look at daily. The roster stays home.
- **No account groups.** Abandoned 2026-08-30; the spec gets a banner saying so. The panel is not a group.
- **No startup per-account validation or fact sweep.** Removed in v1.1.2 for tripping Roblox anti-fraud. Facts refresh lazily or not at all.
- **No writes to Roblox accounts.** RoRoRo reads Robux, membership and friends; it never friends, unfriends, follows, trades or spends. Core observes.
- **No new theme slot and no new window.** The panel lives in the main window; the Library is the Games page grown.
- **No Linux (#259).** WPF, DPAPI and the Win32 name race rule it out; the issue gets a straight answer.
- **No in-game anything.** The macro wall holds: core observes, consented plugins act.
- **No plugin host or AI connector work** beyond the two in-progress catalog tasks and the conditional #230 rider.
- **No new alert kinds.** A health-state change is a row state, not an alert, this cycle.
- **No change to the Store Product Name** unless Este rules on the casing question in the listing file at submission time.

## Open Questions

- **Is the 403 the captcha?** Nobody has measured whether Limited by Roblox (an API 403) and the in-client captcha are the same condition. **Answer during build, first task, before any health state is called done.** The state set in item 1 depends on it.
- **What signal says "needs verification"?** The capture window already words the re-auth case, but no row state exists because no distinct signal has been named. **/spec proposes candidates (ticket response shape, authenticated-user response, challenge headers); build measures; the state ships only on a measured signal.**
- **Which endpoints carry Robux and membership, and at what rate limit?** Documented Roblox endpoints with the account's own cookie, same handler rule, same User-Agent. **/spec names them; build measures the limits with eight accounts at backfill pacing.**
- **#260 root cause.** Environmental or ours. **Reproduce first; the reporter's bundle decides the lane.**
- **#222 after v1.35.** The pre-launch clear may already hold the count. **One measured day decides close or sweep.**
- **Which account does Play use with no main set?** Default is the first row. **Confirm on next interactive run; can wait for /spec.**
- **How much of the note shows in streamer mode?** Default is hidden entirely. **Confirm on next interactive run; can wait for /spec.**
- **Store Product Name casing** (RORORO on the Store, RoRoRo everywhere else). Listing-side, casing-only, does not reopen 10.1.1.1. **Este's call at submission; not a blocker.**
- **Does the panel's Recent count a game the account joined by follow?** It should, since history records it under the follower. **Verify against the history store at /spec.**

## Assumptions surfaced in this run

Each one sentence to overturn. Filled from the scope, the profile and the repo record; none confirmed by Este in this run.

1. Slow fact timer: every 10 minutes per account, staggered, one account at a time.
2. A fact younger than 60 seconds is not re-requested on panel expand.
3. Cached facts live in the DPAPI vault blob alongside the account, or nowhere; never in `settings.json` or a new plain file.
4. Note is 200 characters, inline-edited, exported like tags, hidden in streamer mode.
5. Recent per account is capped at the 8 most recent distinct games inside the rolling history.
6. With no main, the first row's panel starts expanded and Library Play launches the first row's account.
7. Favourites and private servers remain one shared Library; the panel orders them, it does not own them.
8. The follow-marked health state is the existing Join via friend flag given a face, not a second flag.
9. Needs verification ships only on a measured signal; otherwise the row keeps today's two words.
10. The vibe-iterate competitive run is executed in this cycle and its candidates are ranked, not built.
