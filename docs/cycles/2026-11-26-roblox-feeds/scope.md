# RoRoRo: Roblox at a glance

**Cycle:** opened 2026-11-26 on main at v1.40.0.0 (`93298eb`) · **Mode:** autonomous, Cart chain · **Target:** v1.41 · **Predecessor:** stability part two and the update button (`docs/cart-runs/2026-11-17-*`)

**Anchor:** *Know before you launch.* (default — confirm on next interactive run)

## Idea

RoRoRo already asks Roblox what is going on and keeps most of the answer to itself. It fetches the live client version, and uses it only to hold a batch launch that is already under way. It fetches a game's record, and keeps only the name. This cycle shows what the app already knows, adds the one thing it does not ask yet (is Roblox itself up), and puts all three where people already look: the main window's header and the Library.

Este's ask, verbatim: "Are there any sort of roblox feeds we can present to our users?" Then: "scope it with Cart."

**Shape, ruled by Este 2026-11-26:** the feeds are woven into existing surfaces. No feed page, no news reader, no new window. See "Rulings".

## Who it's for

- **The clan multi-user**, about to launch six or eight alts. They want to know before they click whether the first client will sit through an installer, and when a join fails, whether that is Roblox or their PC. Today they learn the first from a row that says "Roblox is updating — hold on." after the launch has started, and the second from Discord.
- **The one-account player** in the Library. They want to see which of their games is busy and which one just updated, without opening the Roblox site.

## What recon found (read live, 2026-11-26)

| Feed | Finding | Evidence |
|---|---|---|
| Client version | **Already in the app.** `RobloxUpdateProbe` reads `clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer` and compares it to the installed file version. It runs only inside a batch launch (`PreWarmGate`); a single row launch and Play as never ask. Nothing is shown at idle. | `src/ROROROblox.Core/Diagnostics/RobloxUpdateProbe.cs`; `docs/features.md` rows "Install deferral" and "Play as"; live answer `0.748.0.7481526` |
| Game record | **Already fetched, mostly thrown away.** `games.roblox.com/v1/games?universeIds=` returns `playing`, `updated` and `visits`; the code keeps `name` (`GamesListItem(string Name)`). Saved games store their `UniverseId`. The "From Roblox" search group already shows a player count from a different call. | `src/ROROROblox.Core/RobloxApi.cs` lines 221 and 749; `Core/FavoriteGame.cs`; live answer for Pet Simulator 99: 81,827 playing, updated 2026-11-24 |
| Roblox status | **Reachable, one new host.** `status.roblox.com` is a Status.io page with no feed of its own. Status.io's public status API answers for Roblox's page id: an overall status, three groups (User, Player, Creator) with components including Games and Game Join, plus incidents and maintenance. | `https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d` returned 200, "Operational"; Status.io public status API docs at `kb.status.io` |
| Game events | **Fails the endpoint rule.** The path that answers without a login (`/virtual-events/v1/universes/{id}/virtual-events`) is not in Roblox's reference. The documented path (`/virtual-events/v3/universes/{id}/game-events`) is marked experimental and returned 403 "Invalid authentication data provided" without a key. | both probed 2026-11-26; `create.roblox.com/docs/cloud/reference/features/virtual-events` |
| DevForum RSS | Works (`/c/updates/announcements/36.rss`, `/c/updates/release-notes/62.rss`). Written for developers. | 25 items each; newest "Release Notes for 748", 2026-11-17 |
| Big Games API | Works (`ps99.biggamesapi.io`: clans, clan battle title and dates). One game only. | last battle 2026-11-14 to 2026-11-20 |
| Roblox newsroom | No feed. The old blog feed address serves a web page. | probed |

`docs/PRIVACY.md` already lists the client-version and games hosts. Its wording for the client version says "before a batch launch", which stops being true the moment the check runs at idle.

## Inspiration and references

- **Bloxstrap / Fishstrap**: server location shown as a notification on join, with the detail kept in a menu for people who missed it. The lesson taken: say it once where the person is looking, keep it findable afterwards. https://bloxstraplabs.com/wiki/features/server-location-hints/ · https://github.com/imide/fishstrap
- **The v1.40 update button**: appears in the header only while an update is waiting, says nothing otherwise. The same rule governs every notice here. `docs/features.md` row "Update button (2026-11-17)".
- **The version-drift banner and the contested-name banner**: RoRoRo already has two Roblox-side banners in the main window. This cycle must not make it three stacked lines.
- Design energy: the existing brand and the ten-slot theme contract. No new slot. Every new state carries a non-colour signal so it reads under flatline.

## Goals

1. **The update notice moves ahead of the launch.** When the live client version differs from the installed one, the header says so at idle, in words that name the consequence: the first launch installs the update and RoRoRo holds the rest. It clears by itself when the versions match. It covers every launch path, because it is a notice and not a gate.
2. **Roblox's own status, shown only when it is not fine.** When Roblox reports a problem on a component a player depends on (Website, Games, Game Join, Avatar), the header says which and links to `status.roblox.com`. Operational is silence. RoRoRo never says Roblox is healthy.
3. **Saved games show their pulse.** Each game in the Library and in the account panel carries players now and when it last updated, for entries on screen, refreshed lazily.
4. **One Roblox line, not three.** The header carries a single Roblox-side notice at a time, with a fixed priority: contested name, outage, version drift, update waiting.
5. **The privacy page stays true in the same PR.** The client-version wording is corrected, the status host gets its row, and the reviewer letter gets its line.

## What "done" looks like

- With a Roblox update out and not yet installed, opening RoRoRo shows the notice within a minute and without a launch. Launching one account installs the update; the notice is gone on the next check.
- A "what changed" link beside the notice opens Roblox's release notes for that version in the browser. It is a link, not a fetch.
- With a recorded real incident payload fed through the seam, the header names the affected component and its state in the app's language, and returns to silence when the payload returns to Operational.
- A saved game's row reads, for example, "81,827 playing · updated 2 days ago" in all seven languages without clipping, with the date through `DisplayDate`.
- Every new read fails to silence: no network, a non-200, a changed shape or a timeout shows nothing and delays nothing. No launch waits on any of them.
- No new call carries a cookie. No call multiplies by account. Nothing runs as a startup burst.
- The status and game-pulse reads are on by default (ruled 2026-11-26). One switch in Settings turns them off. (the switch itself is a default — confirm on next interactive run)
- Suite green on x64 and arm64, with every fence ceiling moved in the same commit as its change. The translation verifier has passed the new keys before the tag.
- The update notice is accepted on a live run against a real pending Roblox update (Roblox ships about weekly; release notes for 747 were posted 11-10 and for 748 on 11-17).

## What must not happen

- A feed outage, slowdown or shape change must never block, delay or fail a launch.
- The status line must never report on RoRoRo itself or be read as a verdict on a user's ban, captcha or account.
- The update notice must not contradict the gate: if the gate holds a batch, the notice was already showing.
- The Library must not gain a network call per entry per refresh. One batched call for what is on screen.
- Nothing here may reach the plugin contract. A new RPC is a different cycle.

## Backlog map by surface

| Surface | Rides in this cycle | Left out, named |
|---|---|---|
| Header and Roblox notices | task "[pollinator] Rescope the Windows version-gate spike — RobloxUpdateProbe already answers half of it" (`8pm4lzbdv`): reconcile or close against goal 1 | #303 / task `0l74yg6ny` plugin update notice (Plugins page, own cycle) |
| Library | this cycle's own work | task `1mppzyjnc` "join a server by player count": the natural next step after players-now, its own feature |
| Release and listing | #300 hosted privacy page text (the page changes this release); tasks `bxranna8z` support contact and `0h7s65ljx` support-email typo (listing text, not images) | task `kw9psaaah`: the theme dialog fix may ride, the screenshot 8 recapture may not (variant B experiment runs to 2027-02-20) |
| Elsewhere | | #260, #222, #221, #230, #232, #52, #67, #302; tasks `g5fgibvy7`, `vpk6ay1eu`, `j6agrrvlu`, `23fsyys8f`, `mq6fqtwr3`, `q60h0q3op`, Ur Reset, game map |

## What's explicitly cut

- **Game events.** The only path that works without a login is undocumented, and the documented one wants a key. The repo rule is documented public endpoints only. Este ruled the cut stands (2026-11-26). Revisit if Roblox documents a public read.
- **A feed page, a news reader, DevForum posts inside the app.** Developer news is not what a launcher's users open it for. The release-notes link is the whole of it.
- **Big Games and any single-game feed in core.** Clan standings and battle dates belong in Ur Score, which already speaks to that audience. Filed as a candidate there, not built here.
- **Phone alerts for an outage or an update.** The alerts machinery could carry "Roblox reports a problem" to a phone, and for an AFK farmer that may be the most useful form. It is the first follow-up, not this cycle: it needs its own rule kind and its own crossing logic.
- **Gating or blocking a launch on status.** Core observes and tells. The person decides.
- **Version management.** No pinning, no holding an update back, no bootstrapper takeover. The install-deferral posture stands.
- **Any per-account or cookie-carrying feed** (notifications, friend activity).
- **A plugin RPC for any of this.**
- **Listing images.** No screenshot shows the new header until the experiment ends.

## Loose implementation notes

- The update notice is a second caller of `IRobloxUpdateProbe`, on a slow timer after startup has settled. The probe is already degrade-safe and already on a typed client. The interface may need a member that returns the latest version string for the release-notes link (the major is the second component: `0.748.…` gives 748).
- Status needs a new typed client for `api.status.io`: one applicable constructor, `ILogger<T>`, its own handler, no cookies by construction, User-Agent `RORORO/<version>`. It joins `TypedHttpClientRegistrationTests`. The page id is a constant, with the remote compat feed as a possible home if it ever changes.
- The status fixture is a recorded real incident payload, not one written to make the parser pass. Find one before `/spec` closes; Status.io's docs carry a sample if Roblox has no incident in the window.
- Game pulse extends `GamesListItem` with `playing` and `updated`, batches by `universeIds` (the batch limit is not known: probe it in `/spec`), and rides the existing on-screen-only lane the icon cache uses. A Recent entry already resolves its universe id through `GameIconCache`.
- The header already hosts the drift banner, the contested banner and the update button. Goal 4 is a small priority resolver, testable without a window.
- Strings: about ten new keys, English in `Strings.resx`, the six cultures through `docs/store/translations/ui-<culture>.json` and `gen-culture-resx.py`. "Roblox" stays English.
- A setting means a `SettingsBlob` parameter, an `IAppSettings` member, the four test fakes, and `SettingsReachabilityTests`.
- Any cache goes through `AppDataRoot`. An in-memory cache may be enough; decide in `/spec`.
- Fences likely to move: `AccessibleNamingFenceTests`, `ThemedStatusColourTests`, `LabelWrapMeasurementTests`.

## Rulings

Put to Este as four calls when the scope was written; ruled the same day, 2026-11-26 ("1 woven in, 2 keep it cut, 3 fine, 4 default on").

1. **Woven in.** The header and the Library. No feed page.
2. **Game events stay cut.** The endpoint rule holds; the undocumented path is not used.
3. **The third-party status host is fine.** `api.status.io` receives nothing but the request. It is the first host outside Roblox, GitHub and Microsoft that the app calls without the person opting in, so it gets its own row in `docs/PRIVACY.md` and its own line in the reviewer letter.
4. **Default on.** The off switch was offered in the same option and is carried as a default, not as a ruling.

Still an assumption: the anchor, "Know before you launch."
