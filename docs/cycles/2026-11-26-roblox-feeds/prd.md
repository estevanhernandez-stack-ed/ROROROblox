<!-- Cart cycle #7, step 3. Input: scope.md beside this file. Written to docs/cycles/2026-11-26-roblox-feeds/
     rather than docs/prd.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md treats as history.
     Autonomous run (profile autonomy_level: fully-autonomous, builder: self). Every beat the record did
     not answer is marked "(default — confirm on next interactive run)" and collected at the end. -->

# RoRoRo: Roblox at a glance — Product Requirements

**Cycle:** opened 2026-11-26 on main at v1.40.0.0 · **Target:** v1.41 · **Scope:** `scope.md` beside this file · **Anchor:** *Know before you launch.* (still an assumption, carried from the scope)

## Problem Statement

A clan user about to launch eight alts learns that Roblox shipped an update after the launch has started, from a banner that says "Roblox is updating". When a join fails they learn whether Roblox itself is down from Discord. A one-account player opening the Library cannot see which saved game is busy or which one changed this week without opening the Roblox site. RoRoRo already asks Roblox two of those three questions and throws most of the answer away. The fix is to show what the app already knows, add the one question it does not ask (is Roblox up), and say all of it in the two places people already look: the main window's notice area and the Library.

One thing has to be true first. The update check the whole first goal stands on does not tell the truth today. That is the next section.

## Where the tree corrected the scope

Read and measured on 2026-11-26 against `3a1e042`. Six corrections; the first changes the build order.

1. **The update check answers "pending" on a fully current client.** The installed client reports its version as `0, 748, 0, 7481526`. Roblox's endpoint answers `0.748.0.7481526`. `RobloxUpdateProbe` compares the two as text, so the same version never matches. Run against this machine with the shipped Release assembly: handler read `[0, 748, 0, 7481526]`, endpoint `[0.748.0.7481526]`, installer not running, no churn, `IsUpdatePendingAsync` **True**. The probe's tests feed the comma form to both sides (`RobloxUpdateProbeTests.cs` lines 257 to 323, from the F-104 fix of 2026-08-12), so they pass. Consequence today: on any machine whose client reports the comma form, every batch of two or more accounts with no strap takes the hold path, shows "Roblox is updating", and waits for the first client to attach. Consequence for this cycle: a notice built on the check as it stands would never clear. All eighteen client installs on this machine, back to 0.728 from 2026-07-07, report the comma form. **Not observed live:** this machine's thirty log files hold no batch launch, so the false hold is read from the code and the probe run, not from a hold line in a log.
2. **The drift banner cannot fire, and repairing it by accident would light it for everyone.** The same comma string reaches `RobloxCompatChecker.CheckAsync`, `Version.TryParse` rejects it, and the check returns no drift. This is already on record (`Core/KnownIssues/RobloxVersion.cs`, "deliberately not fixed here", 2026-09-23). What is new: the live feed still says tested up to `0.729.24.7290838` (read from the v1.40.0.0 release asset today) and the client is on 0.748. Share a version parser with that check and every user gets "We've tested up to 0.729.24. Multi-instance might not work" on every start. The scope's priority order puts drift above the update notice, so the new notice would be buried the day it shipped.
3. **The notices are not in the header, and there are five Roblox-side lines, not three.** The update button is in the header. The notices sit in a strip under it and in two banner rows below that (`MainWindow.xaml` rows 3, 4 and 5): the contested-name banner, the known Roblox issues notice (v1.31), the "Roblox is updating" banner during a hold, the drift banner, and the Bloxstrap warning. The scope's order named two of them and left out the known-issues notice and the Bloxstrap warning.
4. **On Roblox's status page, Game Join sits under Creator, and row names repeat.** The page has three groups (User, Player, Creator). Website appears in all three, Games in two, Avatar in three. "The Games row" is ambiguous until the group is named.
5. **The listing's long description names every host the app talks to.** Its privacy paragraph says nothing leaves the machine except Roblox-side calls, the person's own alert services, GitHub releases, and the Microsoft Store. A default-on call to a new host makes that paragraph wrong in seven languages. The scope listed the privacy page and the reviewer letter and not the listing text.
6. **Two listing riders are already done, and a third task's premise is wrong.** The public catalog's support contact reads `mailto:support@626labs.dev` today, so the doubled-t typo (task `0h7s65ljx`) is gone and the first half of `bxranna8z` with it; `README.md` line 83 already says to quit before a Store update. Task `8pm4lzbdv` says the probe compares "like-for-like against the FileVersion dotted-quad". It is not dotted, and correction 1 is the result.

## User Stories

### The update check tells the truth

Nothing else in the first goal can be accepted until this holds. It also fixes a line every clan user may be reading on every batch launch.

- As a clan user launching six alts on a current client, I want RoRoRo to stop telling me Roblox is updating when it is not, so that the line means something on the day it is true.
  - [ ] With the installed client at the version Roblox's endpoint reports, the update check answers "no update", whatever punctuation each side uses to write the number.
  - [ ] The check's tests include the two strings as they arrive in the field: the installed form `0, 748, 0, 7481526` and the endpoint form `0.748.0.7481526`. A test fed those two answers "no update", and the same test fails against today's code.
  - [ ] With the installed client one version behind the endpoint, the check answers "update pending", in both spellings.
  - [ ] A version string that cannot be read at all still counts as unknown, never as a difference. The check never holds a batch because it failed to read something.
  - [ ] A batch launch of two or more accounts on a current client shows no "Roblox is updating" banner and writes no hold line to the log. Accepted on a live batch, not from a test.
  - [ ] A batch launch with a real Roblox update waiting still launches the first account, holds the rest, and releases them when the install is done, as designed in v1.7.
  - [ ] The drift banner's own version read is **not** changed by this work. It stays exactly as it is until Este rules on it (see Open Questions). A fence or a test pins that a current client with the live feed's stale ceiling shows no drift banner after the cycle, the same as before it.

### The update notice comes before the launch

- As a clan user about to launch, I want to know that Roblox has an update waiting before I click anything, so that I expect the first client to sit through an installer and do not think RoRoRo is stuck.
  - [ ] With an update out and not yet installed, opening the main window shows the notice within 60 seconds and without any launch.
  - [ ] The notice says what will happen, in words: Roblox has an update, the next launch installs it. With more than one saved account it adds that Launch all holds the other accounts until the install is done. With exactly one saved account that second clause is absent.
  - [ ] The notice is a statement and not a gate. No launch path waits on it, asks about it, or is blocked by it: the row's Launch, Play as, the account panel's Launch, Launch all, Launch multiple, Squad Launch, Recycle, auto-rejoin and plugin launches all behave as they do without it.
  - [ ] After a launch installs the update, the notice clears by itself within 60 seconds of that client attaching, with no click and no restart.
  - [ ] The notice cannot be dismissed. It is a state, like the update button: it shows while it is true and at no other time. **(default — confirm on next interactive run)**
  - [ ] The check never runs as part of startup. The first one happens no sooner than 30 seconds after the main window is shown. After that it repeats **every 30 minutes while the window is visible (default — confirm on next interactive run)**, once after any client attaches, and once when the window comes back from the tray if the last answer is older than 30 minutes.
  - [ ] While RoRoRo is hidden in the tray, no idle check runs.
  - [ ] While a batch is being held and the "Roblox is updating" banner is on screen, the notice is not also shown. The two never say the same thing twice.
  - [ ] When Bloxstrap or Fishstrap handles launches, the notice is not shown. The strap updates Roblox itself and RoRoRo's hold is skipped, so the notice's sentence would be false. **(default — confirm on next interactive run)**
  - [ ] With Roblox not installed, there is no notice.

- As someone who wants to know what the update is, I want a link beside the notice to Roblox's own notes for that version, so that I can read what changed without searching.
  - [ ] A "What changed" link opens Roblox's release notes for the waiting version in the default browser. RoRoRo opens the address; it does not fetch the page.
  - [ ] The address is on a Roblox-owned domain and is built from the version number alone.
  - [ ] If Roblox's endpoint reports a version **lower** than the one installed (a rollback), the notice still shows, since the next launch still changes the client, and the link is absent.
  - [ ] If a per-version address cannot be relied on without fetching it, the link goes to Roblox's release-notes index instead. The link never lands on a not-found page on the day a notice first appears.

### Roblox's status, only when it is not fine

- As someone whose join just failed, I want RoRoRo to tell me when Roblox itself reports a problem, so that I stop restarting my PC and wait.
  - [ ] When Roblox's status page reports a problem on a watched row, the notice area names the row and the state in the app's language, attributes it to Roblox ("Roblox reports…"), and offers one link to `status.roblox.com`.
  - [ ] Watched rows, by group and name as the page writes them: **Player › Website, Player › Games, Player › Avatar, User › Website, Creator › Game Join. (default — confirm on next interactive run)** A problem on any other row (Studio, Data Store, Asset Delivery, Mobile App, Xbox App, Dashboard, Forum and the rest) shows nothing.
  - [ ] When every watched row is operational, there is no status line. RoRoRo never shows a "Roblox is fine" indicator, a green dot, or any wording that reads as a clean bill of health.
  - [ ] With problems on several watched rows, the line names the worst one and counts the others ("and 2 more").
  - [ ] Each level Roblox's page can report has its own phrase, and the phrases differ in words, not only in colour. A level the app does not recognise shows a general "a problem" phrase rather than nothing and rather than a raw code.
  - [ ] Maintenance that is under way on a watched row is named as maintenance. Maintenance that is only scheduled shows nothing.
  - [ ] **Every word on the line is RoRoRo's own.** No incident title, message, or any other text from the status service is ever drawn in the window, read by a screen reader, or put in a tooltip. The service decides which of the app's sentences shows; it never supplies a sentence. (The known-issues list is signed because whoever controls shown text can show the whole clan a fake warning. This feed is unsigned, so it gets no text at all.)
  - [ ] The line clears on the first read that finds every watched row operational.
  - [ ] An answer is trusted for 30 minutes. If no fresh answer arrives in that time (network down, service unreachable), the line clears instead of going stale. **(default — confirm on next interactive run)**

- As someone leaving RoRoRo running, I want the status read to be light and predictable, so that it never slows a launch or chatters to a third party for no reason.
  - [ ] The first status read happens no sooner than 30 seconds after the main window is shown, never in the startup burst.
  - [ ] After that it repeats **every 10 minutes while the main window is visible (default — confirm on next interactive run)**, and once when the window comes back from the tray if the last answer is older than that.
  - [ ] While RoRoRo is hidden in the tray, no status read happens.
  - [ ] A launch that fails because of the connection, a timeout, or Roblox being unavailable (the v1.38 plain launch errors) asks for one early status read, at most once a minute, so the notice area catches up with what the row just said. **(default — confirm on next interactive run)**
  - [ ] One request per read, for the whole app. Never one per account, never with a cookie, never with any account identifier.

- As someone with a flagged or expired account, I want the status line to be unmistakably about Roblox and not about me, so that I do not read an outage as a ban or a ban as an outage.
  - [ ] The status line never appears on an account row, never changes a row's health state or presence, and never names an account.
  - [ ] When a row says a launch failed and Roblox's page reports no problem, the notice area says nothing. Silence is never worded as "Roblox is up".
  - [ ] The line's wording never mentions bans, captchas, verification or sessions.
  - [ ] The line never reports on RoRoRo itself.

### One Roblox line

- As someone who opens RoRoRo to launch, I want at most one line about Roblox's state at a time, so that the roster is not pushed down by a stack of warnings.
  - [ ] Roblox-side notices share one line with a fixed order, highest first: **contested name, Roblox outage, known Roblox issue, version drift, update waiting.** Only the highest one that is true is drawn.
  - [ ] Each notice keeps the actions it has today when it is the one drawn: Close Roblox for me and Retry on the contested banner; See known issues and Dismiss on the known-issues notice; the status link on an outage; What changed on an update.
  - [ ] A notice hidden by a higher one is not lost and not used up. A one-time notice (known issues) that was hidden still shows, once, when the line is free.
  - [ ] A hidden notice is never invisible: the line shows how many more there are, and they can be read without waiting for the top one to clear. **(the form of that, a count with a tooltip or an expander, is a default — confirm on next interactive run)**
  - [ ] The count and the hidden notices have accessible names; a screen reader hears the drawn notice first, then how many more.
  - [ ] The "Roblox is updating" banner during a hold is not part of the order. It is launch progress, it shows while a hold runs, and the update notice gives way to it.
  - [ ] The Bloxstrap warning stays its own line. It is about the person's own setup, it is dismissible for good, and it is not a Roblox-side event. **(default — confirm on next interactive run)**
  - [ ] Every other row in the notice area is untouched and keeps its own line: launch progress, the accounts-could-not-be-read warning, the idle summary, the frame-rate cap warning, the crash notice, the rating ask.
  - [ ] With an outage, a known issue and an update all true at once, the notice area is exactly as tall as it is with one of them.
  - [ ] Every state of the line is tellable apart with colour removed: under flatline each carries the warning glyph and different words.
  - [ ] Compact mode shows the line the way it shows the contested banner today, in at most two lines of text. **(default — confirm on next interactive run)**
  - [ ] Rendered options for the line in each of its five states, and for the "more" count, are put in front of Este before any XAML is written. The built line is screenshot-compared with the picked option before the work is called done.

### Saved games show their pulse

- As a one-account player in the Library, I want each saved game to show how many people are playing right now and when it last changed, so that I pick what to play without opening the Roblox site.
  - [ ] Each game entry in the Library carries a line reading, for example, "81,827 playing · updated 2 days ago", in grid view and in list view.
  - [ ] The line sits with the entry's existing "last played · time played" line and does not replace it. Yours and the game's are both readable.
  - [ ] The player count is the full number with the app language's grouping, the same style the "From Roblox" search group uses today.
  - [ ] "Updated" is relative for recent changes and a date for old ones, on the same ladder "last played" uses, and goes through `DisplayDate`, so it follows the app's language and not the Windows region.
  - [ ] The line appears in all seven languages without clipping in the grid tile, the list row and the account panel.
  - [ ] A game with nobody in it reads "0 playing". That is information.
  - [ ] A game Roblox no longer returns (deleted, made private) shows no pulse line. The entry itself is unchanged and still removable.
  - [ ] An unsaved Recent entry shows its pulse the same way once its game is known.
  - [ ] The pulse is part of the entry's accessible name and tooltip: name, players, updated.
  - [ ] The pulse is text. It is not a button, it starts nothing, and Play, Play as and every management action behave as before.

- As someone with saved private servers, I want the Library not to mislead me about who is in my server, so that I do not read the whole game's crowd as my server's.
  - [ ] A private-server entry shows when its game last updated and **no player count**. The count Roblox reports is for the whole game, never for one server. **(default — confirm on next interactive run)**

- As a clan user with an account panel open, I want the same pulse on that account's entries, so that the panel and the Library agree.
  - [ ] Game entries in an open account panel carry the same pulse line as the Library.
  - [ ] The same game shows the same numbers in the Library and in every open panel at the same moment. They are one reading shared, not one reading each.
  - [ ] A collapsed panel asks for nothing.

- As someone with a hundred saved games, I want the pulse to cost nothing I can notice, so that the Library stays as fast as it is.
  - [ ] Pulse is read only for entries on screen. Opening the Library with 100 saved games asks about one screenful, not 100.
  - [ ] One screenful is one request, not one request per game.
  - [ ] Scrolling asks for the newly visible entries once, not again on every scroll tick.
  - [ ] A game's pulse is not asked for again within **5 minutes (default — confirm on next interactive run)**, however often the Library or a panel is opened and closed.
  - [ ] A player count older than **15 minutes (default — confirm on next interactive run)** that cannot be refreshed is removed rather than shown stale. The "updated" date stays, since a date does not go stale the same way.
  - [ ] Pulse requests carry no cookie and do not multiply by account: eight saved accounts and one saved game is one question.
  - [ ] Nothing is asked while the Library and every panel are closed, and nothing at startup.
  - [ ] Filtering, searching and switching between grid and list stay as fast as in v1.40. No spinner appears for the pulse; the line is simply absent until the answer arrives.
  - [ ] Rendered options for the grid tile, the list row and the panel entry with the pulse line are put in front of Este before any XAML is written, and the built result is screenshot-compared with the pick.

### Every read fails to silence

- As someone on hotel wifi, I want a feed that cannot be reached to cost me nothing, so that a broken extra never becomes a broken launcher.
  - [ ] For each of the three reads (update, status, pulse): no network, a refused connection, a timeout, a non-success answer, an empty answer, and an answer in a shape the app does not expect all produce the same result: nothing is shown and nothing is delayed.
  - [ ] No launch of any kind waits on any of the three. With all three hosts unreachable, time from click to client is the same as with them reachable.
  - [ ] The main window opens and becomes usable in the same time with all three hosts unreachable.
  - [ ] A feed failure never shows an error to the person: no dialog, no banner, no red text, no toast.
  - [ ] A feed failure never changes an account's health state, presence, or facts.
  - [ ] The status parser is tested against a recorded real incident answer from Roblox's page, not one written to make the parser pass. If Roblox has no incident before `/spec` closes, the service's documented sample is used and marked as such.
  - [ ] If a watched status row disappears or is renamed, the status line stays silent and the log records it once at Warning with the row name, so a support bundle shows why the line never appeared.
  - [ ] Each read's outcome is written to the log at Debug with no payload, so the question "did it ask, and what kind of answer came back" can be settled from a support bundle.
  - [ ] No answer from any feed is written to a plain file in a form that names an account. If anything is kept between runs at all, it goes through `AppDataRoot`.

### The off switch

- As someone who wants RoRoRo to talk to as few hosts as possible, I want one switch that turns the new reads off, so that I do not have to trust a default.
  - [ ] Settings has one switch covering Roblox's status and the game pulse. It is **on** for new installs and for upgrades (ruled 2026-11-26). **(the switch itself is a default — confirm on next interactive run)**
  - [ ] The switch's label says what turns off in plain words, and its description names the status service as a third party.
  - [ ] Turning it off takes effect at once, with no restart: the status line disappears if it was showing, pulse lines disappear from the Library and every panel, and from that moment no request goes to the status host and no pulse request is made. The log of a session started with the switch off shows zero requests to the status host.
  - [ ] Turning it back on brings both back on their normal timing, not as an immediate burst.
  - [ ] The update notice is not behind the switch. Its read has been in the app since v1.7 and goes to Roblox. **(default — confirm on next interactive run)**
  - [ ] The switch is reachable from a control and passes the settings reachability rule.
  - [ ] The switch exports and imports with the rest of the settings the way other switches do, and is not part of account export.

### The privacy page and the listing stay true

- As someone who read the privacy page before installing, I want it to still be true after this update, so that the first third-party call RoRoRo makes by default is one I was told about.
  - [ ] `docs/PRIVACY.md` no longer says the client version is checked "before a batch launch". It says when the check runs now.
  - [ ] `docs/PRIVACY.md` gains a row for the status host: what it is, that it is not Roblox, GitHub or Microsoft, what is sent (the request itself: an IP address and `RORORO/<version>`, and nothing else), what is never sent (no cookie, no account, no identifier), when it runs, and that one switch turns it off.
  - [ ] Every sentence in `docs/PRIVACY.md` that lists who RoRoRo talks to, or says it makes no other connections, is re-read and corrected in the same change.
  - [ ] The games wording covers reading a saved game's player count and last update while the Library is open.
  - [ ] The listing's long description gains one sentence in its privacy paragraph naming the status check, in English and in all six translated sheets, with dated provenance notes, before the listing is written.
  - [ ] The three-surface audit (short description, long description, features) runs against the fresh release notes and is recorded in the submission packet.
  - [ ] The reviewer letter gets its line: a new host, what it is asked, that nothing about the person is sent, default on with a switch.
  - [ ] The hosted privacy page is handed this release's text (#300), so the public page and the repo page agree on the day of the release.
  - [ ] The About page and the README are re-read for any sentence about network calls, and corrected if one exists.
  - [ ] No listing image changes. The variant B experiment runs to 2027-02-20 and no screenshot shows the new line or the pulse before it ends.

### Nothing moves for the six-alt user

- As a clan user, I want this release to change nothing about how I launch except the false "updating" line going away, so that an update does not cost me an evening.
  - [ ] Launch all, Launch multiple, Squad Launch, Recycle, auto-rejoin, join via friend, alerts and every plugin call behave as in v1.40, with one named exception: a batch on a current client no longer holds behind the first account (the first epic).
  - [ ] With no Roblox-side notice true, the main window is pixel-identical to v1.40 with every panel collapsed.
  - [ ] The row's height, order, controls and facts are unchanged.
  - [ ] Startup makes no new network call. The first new read of any kind is at least 30 seconds after the window is shown.
  - [ ] With the main window hidden in the tray, RoRoRo makes exactly the calls v1.40 makes.
  - [ ] The plugin contract is unchanged: no new call, no new capability, no new event, no version bump of the contract package.
  - [ ] Streamer mode is unaffected. Nothing new names an account, and the streamer scenario in the smoke harness still passes untouched.
  - [ ] The ten-slot theme contract is unchanged. Every new colour is an existing slot or a derived brush.
  - [ ] The fences that move (accessible naming, themed status colour literals, label wrap measurement, settings reachability, typed client registration) move in the same commit as the change that moves them.

### Riders

- As the person who keeps the backlog honest, I want the tasks this cycle touches to say what is true afterwards, so that nobody re-derives them.
  - [ ] Task `8pm4lzbdv` (the version-gate rescope) is rewritten, not closed: its claim that the probe compares like for like is corrected with the measurement above, the "tell" half of its third item is marked delivered by the update notice, and what remains is stated (an Update Roblox action with a driver behind it, refusing a multi-launch while stale, and the two open questions about Roblox's own launcher).
  - [ ] Task `0h7s65ljx` (support email typo) closes on the public catalog read of 2026-11-26.
  - [ ] Task `bxranna8z` closes once the What's new template is confirmed to carry the quit-before-updating line; the support contact and the README line are already done.
  - [ ] #300 closes when the hosted privacy page carries this release's text.

- As someone building a theme, I want the dialog's first button not to be cut off, so that I can read it. *Conditional: rides only if it lands before the release branch is cut. The screenshot recapture does not ride.*
  - [ ] The Build a theme dialog's button row fits in all seven languages, with a fit test, and Store screenshot 8 is left alone until the experiment ends (task `kw9psaaah`).

### Release lane

- As the person who ships it, I want the release to prove the three feeds on real conditions, so that the first report is not from the clan.
  - [ ] Every new string exists in English and in all six cultures through the catalog pipeline (`ui-<culture>.json`, then the generator), never by hand in a culture file. "Roblox" stays English.
  - [ ] The translation verifier has reviewed the new keys in all six languages before the tag.
  - [ ] The suite is green on x64 and arm64, and `ci.yml` is green on main before the tag.
  - [ ] **Absent when current:** on a machine whose Roblox client is current, a live run shows no update notice for a full check cycle. This is the acceptance that fails today.
  - [ ] **Present when pending:** on a live run against a real waiting Roblox update, the notice shows before any launch, one launch installs the update, and the notice clears. Roblox ships about weekly, so the build has to be ready before an update lands.
  - [ ] **Status, live shape:** a live run reads Roblox's real status page and shows nothing while it is operational; the recorded incident answer drives the line through the same code path in a test.
  - [ ] **Pulse, live:** a live run with the Library open shows real numbers for a saved game that match Roblox's own page for it within the refresh interval.
  - [ ] The feature map, the feature ledger and the decisions log are updated in the release, and the ship is recorded in the session that ships it.

## What We're Building

Must-have, in build order. Item 1 gates items 2 and 6.

1. **The update check tells the truth.** One version comparison that reads the same version as the same in either spelling, tested with the two strings from the field. The batch hold stops firing on a current client. The drift banner's read is left exactly as it is.
2. **The update notice.** Shown at idle when an update waits, worded by consequence, with a link to Roblox's notes. A statement, never a gate. Clears by itself. Not shown under a strap or during a hold.
3. **The status line.** Roblox's own status for five watched rows, shown only when it is not operational, in the app's own words only, with one link to Roblox's page. Read lightly, only while the window is visible.
4. **One Roblox line.** A fixed order across contested name, outage, known issue, drift and update; one drawn; the rest counted and readable; nothing used up while hidden.
5. **The game pulse.** Players now and last updated on Library games, Recent entries and open panel entries; last updated only on private servers. On-screen entries, one request per screenful, shared between the Library and the panels.
6. **Fail to silence,** across all three, with a recorded real incident answer as the status fixture.
7. **The off switch** for status and pulse, default on, effective at once.
8. **Privacy and listing text:** the privacy page, the long description in seven sheets, the reviewer letter, the hosted page (#300), the three-surface audit. No listing image.
9. **The mock gate:** rendered options for the line and for the pulse before XAML; screenshot comparison before done.
10. **Riders:** the version-gate task rewritten; the two support-contact tasks closed on evidence; the theme dialog if it lands in time.
11. **Release lane:** strings through the catalog, the verifier, both architectures, and the four live acceptances.

## What We'd Add With More Time

- **A phone alert when Roblox reports a problem, or when an update lands.** For an AFK farmer this may be the most useful form of the whole cycle. It needs its own alert kind and crossing rule. First follow-up.
- **An Update Roblox action on the notice,** with the driver behind it. What remains of task `8pm4lzbdv`.
- **"Updated since you last played."** A marker on a game whose last update is newer than your last session. Two dates the app already has once the pulse ships.
- **Sort the Library by players now.**
- **Join a server by player count** (task `1mppzyjnc`). The natural next step once a count is on the tile.
- **More watched rows, per person.** Data Store and Asset Delivery matter to a farmer and not to most people.
- **A heads-up for scheduled maintenance** on a watched row, a day ahead.
- **The Roblox line in the tray tooltip,** for people who never open the window.
- **Clan standings and battle dates** in Ur Score, from the Big Games feed.

## Non-Goals

- **No feed page, news reader or new window.** Ruled 2026-11-26: woven into the notice area and the Library.
- **No game events.** The only path that answers without a login is not in Roblox's reference. The endpoint rule holds.
- **No text from a third party on screen.** The status service picks which of RoRoRo's sentences shows. It never supplies one.
- **No "Roblox is fine".** Operational is silence, in every wording and every state.
- **No gating a launch on status or on a waiting update.** Core observes and tells. The person decides.
- **No version management.** No pinning, no holding an update back, no bootstrapper takeover, no Update Roblox action this cycle.
- **No drift banner repair by side effect.** The banner's fate is a ruling, not a consequence of sharing a parser.
- **No server-level counts.** The pulse is the game's, never a server's.
- **No per-account or cookie-carrying feed.** No notifications, no friend activity.
- **No read while hidden in the tray.** That is the phone-alert follow-up's question, with its own cost.
- **No plugin call for any of this.**
- **No listing image change** before 2027-02-20.

## Open Questions

- **What happens to the drift banner?** It cannot fire, the feed it reads is four months stale, and Roblox ships weekly. Three options: repair it and commit to moving the feed's ceiling with each Roblox release; retire it and take its slot out of the order; leave it as it is. **Este's call. The default carried here is "leave it as it is", which lets `/spec` proceed. Answer before `/build`.** *Ruled 2026-11-26: leave it as it is.*
- **Does the false hold wait for v1.41?** If the clan's machines show it (next question), every Launch all there has been saying "Roblox is updating" on a current client since at least 2026-08-12. It can ship ahead as its own small release or ride this cycle. **Este's call; does not block `/spec`.** *Ruled 2026-11-26: it rides this cycle.*
- **Is the false hold visible on Neb?** This machine has no batch launch in its logs. One search of Neb's logs for "Pre-warm: holding the batch" settles whether the field sees what the code says it should. **Before `/spec` closes; one command.**
- **Where do Roblox's per-version release notes live, and is the address stable?** **`/spec` probes it; the fallback is the index.**
- **How many games can one pulse request carry?** Unknown, from the scope. **`/spec` probes the limit.**
- **What does Roblox's "updated" date track?** A place publish, a settings change, or both. A game that publishes hourly would always read "updated today". **`/spec` measures on three games.**
- **Which levels can Roblox's status page report, and may RoRoRo call the status service's public address from about 6,500 installs?** The level list and any stated limit or term of use come from the service's own documentation, not from memory. **`/spec` reads them. If the terms forbid it, the status goal stops and comes back to Este.**
- **Can a recorded real incident answer be found?** **Before `/spec` closes, per the scope.**
- **Does a private-server entry know which game it belongs to without a new lookup?** **`/spec` checks the store.**
- **Does editing the long description touch what variant B is testing?** The sentence would land on both arms if the experiment varies images only. What the experiment varies is not in the record read here. **Este's answer before submission; does not block `/spec`.**
- **Data Store and Asset Delivery: watched or not?** Left out by default. **Confirm on next interactive run.**

## Assumptions surfaced in this run

Each one sentence to overturn. Filled from the scope, the profile, the tree and the probes; none confirmed by Este in this run.

1. The anchor stays "Know before you launch."
2. The drift banner's read is left untouched, and it keeps its slot in the order.
3. The Roblox-side order is contested name, outage, known issue, drift, update.
4. A hidden notice is shown as a count that can be opened, and a one-time notice is not used up while hidden.
5. The Bloxstrap warning stays its own line, outside the order.
6. The update notice cannot be dismissed, is not behind the off switch, and is not shown when a strap handles launches.
7. Idle update check: 30 seconds after the window shows, then every 30 minutes while visible, after any attach, and on return from the tray.
8. Status read: every 10 minutes while the window is visible, never while in the tray, plus one early read after a connection-kind launch failure, at most once a minute.
9. A status answer is trusted for 30 minutes and then dropped.
10. Watched rows: Player › Website, Games, Avatar; User › Website; Creator › Game Join.
11. No text from the status service is ever shown; every word is the app's own.
12. Pulse: not re-read within 5 minutes; a count older than 15 minutes is removed; the date is kept.
13. A private-server entry shows last updated and no player count.
14. One switch covers status and pulse, default on, effective without a restart.
15. The line shows in compact mode the way the contested banner does.
16. Rendered options come before XAML for the line and the pulse, per the match-the-mock rule.
