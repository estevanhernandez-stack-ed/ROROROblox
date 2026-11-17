# The 30-day board: 10K installs and a better RoRoRo

Evidence sweep run 2026-11-10 on v1.36.0.0. Every number below was read that day from the Store
analytics API, the Store's public search, GitHub, or the code. Re-run the Store numbers with
`python scripts/store-analytics/store_analytics.py`.

## The verdict

**10K is on pace.** RoRoRo had 6,501 Store installs through 2026-11-09 and has averaged 146 a day
over the last 14 full days. The remaining 3,499 needs 117 a day. At the current pace the count
crosses 10,000 around **2026-12-03**, a week inside the 30 days. The pace would have to fall 20%
to miss.

So the work is not finding 3,500 strangers. It is three things, in this order:

1. **Stop losing the people who already arrive.** A launch can dead-end in a Windows dialog RoRoRo
   never sees. About 17 devices a day report a crash or a hang, out of roughly 360 daily users.
2. **Earn the trust the listing is missing.** 20 ratings on 6,501 installs, and the two 1-star
   reviews say "hacked my account" and "stole my robux". A 5-star reviewer wrote that those
   reviews made them wonder whether the app was a Trojan.
3. **Widen the door.** The Store's own search shows RoRoRo for "rororo" and "roblox launcher" and
   for nothing else people type.

Retention is the weak number, not acquisition: 6,501 installs, about 2,000 monthly users, about
360 daily.

## What the evidence says

| Question | Answer | Source |
| --- | --- | --- |
| What is "downloads"? | Store successful installs: 6,501 lifetime. Acquisitions (signed-in only) are 3,310. GitHub asset counts are update polling. | `installs`, `appacquisitions` |
| Pace | 68/day (week of Oct 12), 101, 144, 148 (week of Nov 2) | `installs` by day |
| Daily users | 359 a day over the last 7 full days, 56 new a day | `usagedaily` |
| Where they come from | 6,148 of 6,339 tracked clicks in 90 days are Store traffic. The hub's `site-product` campaign converted 93. | `appchannelconversions` |
| Funnel, 30 days | 1,955 listing views, 1,056 Get (54%), 614 installed (58% of Get), 625 opened | `funnel` (signed-in users only) |
| Markets, 90 days | US 24%, BR 8%, RU 7.5%, PH 5.4%, ID 4.8%, GB 4.5%, PL 4.3%, FR 3.7%, DE 2.6% of 106 markets | `appacquisitions` by market |
| Ratings | 20 lifetime, 4.40 average: sixteen 5-star, two 3-star, two 1-star. Four reviews with text have no reply. | `ratings`, `reviews` |
| Failures, 30 days | 219 devices crashed and 284 hung. All but 87 are unclassified, with no failure name and no stack. Average 17 devices a day. | `failurehits` |
| Store search | Position 1 for "rororo", position 7 for "roblox launcher". Absent from the top 20 for "multi roblox", "roblox multi", "roblox account manager", "roblox alts", "multiple roblox", "roblox accounts", and for "roblox" in US, BR, RU, ID and PH. | `scripts/store-analytics/store_search_rank.py` |
| Web search | Not in the top results for any of 11 queries, the brand query included. No YouTube, Reddit or TikTok mention found. | survey agent, generic web search (not Google) |
| The field | Roblox Account Manager is archived (8.6M lifetime downloads). Bloxstrap removed multi-instance in v2.10.0. MultiBloxy's README says Roblox is blocking it. Three of the most-viewed 2026 PC tutorials (337K, 218K, 93K views) teach closing a handle by hand in Process Explorer. | survey agent, GitHub API and YouTube |

## Tier 1: ship in v1.37

### 1. A launch dead-ends when no `roblox-player:` handler exists

**What happens.** RoRoRo launches by shell-executing a `roblox-player:` link
(`RobloxLauncher.cs:237`, `ProcessStarter.cs:10-19`). With no handler registered, `Process.Start`
does not throw. It returns with no process and Windows opens its own "You'll need a new app to
open this link" dialog. Measured on Windows 11 on 2026-11-10 with an unregistered scheme: no
exception, `OpenWith.exe` started. The `catch (Win32Exception)` at `RobloxLauncher.cs:241` that
is meant to show the Roblox-needed modal never runs. RoRoRo reports the launch as started, and
the row later says "Launch never connected. Check Roblox is current + antivirus isn't blocking."

**Who hits it.** Anyone whose only Roblox is the Microsoft Store edition. Its manifest declares
one protocol, `roblox` (read from `ROBLOXCORPORATION.ROBLOX_2.699.877.0`), never `roblox-player`.
RoRoRo's users arrive through the Store, where "Roblox - Windows" is the first result for every
query. The 3-star review of 2026-11-07 describes this exactly: "it says that i cant launch it
because I don't have an app that can launch this link." How many users this is cannot be measured.

**Fix.** Check for a registered handler before the shell-execute and return
`RobloxNotInstalled` when there is none, which makes the existing modal reachable. Say which case
it is: "You have the Microsoft Store edition of Roblox. RoRoRo needs the one from roblox.com."
The check must never block a launch that would have worked, so it reports "missing" only when
every lookup agrees and treats any read error as "present". About a day, with tests.

### 2. Ask for a rating, once, at a good moment

Nothing in the app invites a rating, a share, or a star. 360 people use it every day and 20 have
ever rated it. On a Store install only, after a launch has succeeded on a third separate day,
show one quiet line with **Rate RoRoRo** and **Not now**, never during a launch and never again
after either answer. Add a permanent **Rate RoRoRo** link on About. The Store's own review dialog
does the rest. Sixteen of twenty ratings are 5-star, so more volume should raise the average and
push the two scare reviews down the page. That is the expectation, not a measurement. About a day.

### 3. The same account can be added twice

`CompleteAddAsync` (`MainViewModel.cs:1693`) never compares the captured Roblox user id with the
saved rows. Re-authenticate has that check; Add does not. A one-account user who adds the same
account twice gets two rows that kick each other, and concludes multi-instance does not work.
Refuse the duplicate with a sentence that says why. Half a day.

### 4. A crash is a silent disappearance, and nobody learns why

The app writes `last-exit.json` on a crash and the next start only logs it. 219 devices crashed
in 30 days and the Store has a stack for 12 of them, which were the composition fault already
fixed in v1.34 (#246). On the start after a crash, say so in one line and offer **Save a support
bundle**. No data leaves the PC unless the person sends it, so the no-telemetry promise holds.
Also wrap `OnStartup`, which is `async void` with no outer catch, so a throw before the window
shows is a double-click that does nothing. About a day.

### 5. Launch failures read as developer text

"Failed to obtain auth ticket: {exception message}" lands on the row in English inside a
localized UI. Map network, timeout and rate-limit failures to three plain sentences, in all seven
languages. Half a day.

### 6. Custom FPS cap

The 5-star review of 2026-10-31 asks for it: their monitors are 75 Hz and 180 Hz and the list
offers neither. `FpsPresets.ClampCustom` already accepts 10 to 9999. Add 75 and 180 to the
presets, or a typed value. A few hours, and it is a reply worth sending to that reviewer.

## Tier 2: no release needed

7. **README.** It says "What's new in v1.21" and "the Store edition is on v1.23.0.0 as of
   2026-08-23". The feature list is the v1.1 list. It is the most-visited page on the repo (184
   unique visitors in 14 days). Its two Store links carry no campaign id, so GitHub's
   contribution to installs cannot be read.
8. **Repo metadata.** The description is "Multiroblox as current as possible". There are no
   topics and no homepage link.
9. **Release notes template.** The v1.36 body's Download link points at the releases page rather
   than the asset, and the body contains an internal line, "Short list, for the GitHub release
   and the Discord post".
10. **Four review replies and three issue replies.** Drafts only; posting is the owner's click.
    #112 (verification on every launch from day two, opened 2026-08-14) has never had a reply,
    and a second person asked for an update on 2026-09-03.
11. **Hub product page.** The hero shows `v1.14` as the latest Windows version (it may be filled
    by script; not confirmed). The RBX15 page never mentions RoRoRo. The live daily-users figure
    runs on the home page and not on the product page, which the hub's own log puts at about
    twenty times the traffic (1,318 visits against 60 in a month; not re-measured here).

## Tier 3: the Store listing (needs the owner's word; listing-only submission)

12. **Title.** "RoRoRo — Multi-launcher for Windows" cannot carry the word Roblox (10.1.1.1
    rejection, v1.1.0.0). It can carry the words people pair with it. The one query that finds the
    app today is the one whose second word is in the title. Candidate: **"RoRoRo: Multi-Instance
    Launcher and Account Manager"**. This is an experiment with a one-day loop: submit, wait for
    certification, re-run `store_search_rank.py`. Whether Store search weights the title this way
    is an inference from one data point.

    **The test, started 2026-11-11 on the owner's word.** Name under test, English listing only:
    `RoRoRo — Multi-Instance Launcher & Account Manager`. Baseline taken 2026-11-11 20:54 CST,
    after v1.37.0.0 published and before any name change, with
    `scripts/store-analytics/store_search_rank.py` (US, en-us, top 20 unless a market is named):

    | Query | Position | Why it is on the list |
    |---|---|---|
    | rororo | 1 | brand |
    | roblox launcher | 7 | the one generic hit; "launcher" is in the title |
    | multi roblox, roblox multi, multiple roblox | absent | 2026-11-10 baseline, unchanged |
    | roblox account manager | absent | both halves are keywords today, neither is in the title |
    | roblox alts, roblox accounts | absent | 2026-11-10 baseline, unchanged |
    | roblox (US, BR, RU, ID, PH) | absent | 2026-11-10 baseline, unchanged |
    | multi instance | 14 | a keyword today; in the new title |
    | alt accounts | 19 | a keyword today; not in either title |
    | multi-instance launcher, instance launcher | absent | in the new title |
    | account manager | absent | a keyword today; in the new title |
    | launcher | absent | in both titles; a crowded word |
    | multi launcher | absent | **in the current title** ("Multi-launcher") and still absent |
    | multibox | absent | a keyword today |

    Publishing v1.37 moved none of the thirteen 2026-11-10 queries. Two things the baseline says
    before the change: keywords alone do rank (positions 14 and 19 come from keywords, not the
    title), and a title word alone does not guarantee a hit ("multi launcher"). So the title
    hypothesis rests on "roblox launcher" and is weaker than it looked.

    **What would count as the title working:** "roblox account manager" entering the top 20, since
    that is the pairing "roblox launcher" already shows, and "multi instance" or "account manager"
    moving up. **What would count as nothing:** the same table. Re-run the same 21 queries once the
    listing is live, and again a few days later; search may lag the listing.

    **First re-run, 2026-11-14 12:49 CST**, about 52 hours after the new English title went live
    (submitted 2026-11-11 22:52 CST as submission `1152921505702382810`; the catalog's
    `LastModifiedDate` is 2026-11-12 14:15 UTC; the API reads `Published`). Same 21 queries, same
    script. Nineteen are unchanged. Two moved, both down, and both read the same on three further
    passes:

    | Query | 2026-11-11 | 2026-11-14 |
    |---|---|---|
    | roblox launcher | 7 | 17 |
    | multi instance | 14 | 17 |
    | roblox account manager | absent | absent |
    | account manager, multi-instance launcher, instance launcher | absent | absent |
    | rororo | 1 | 1 |
    | alt accounts | 19 | 19 |

    By the rule fixed above this is worse than "nothing": the criterion query did not enter, and
    the one generic hit fell ten places. **Not established: that the title caused the fall.** The
    baseline was a single pass and its top-20 lists were not kept, so nobody can say whether the
    sixteen listings now above RoRoRo were below it on 2026-11-11. Store installs had already
    dipped from Monday 2026-11-09 (92, 109, 93, 86 against 148, 126, 155, 149 on the same weekdays a
    week earlier), two days before v1.37 or the name went live, so the dip is not the name's doing.
    Friday 2026-11-13, the first full day under the new name, read 131, and the last day of a pull
    can be incomplete.

    Kept this time, for the next read. "roblox launcher", 2026-11-14, positions 1 to 16: Roblox -
    Windows, One Game Launcher, Hello Neighbor 2 Alpha 1, Epic Games Store, Minecraft Launcher,
    Minecraft: Java & Bedrock Edition for PC, Fortnite, bloxd.io, LEGO Fortnite: Odyssey,
    PlanetCraft: Block Craft Games, Minecraft Dungeons for Windows + Launcher, ARK: Ultimate
    Survivor Edition, Minecraft: Sonic Add-On, Palworld, XBOX, GOG GALAXY. RoRoRo is 17.

    The second read is due 2026-11-16 or later. The way back is the previous name, still reserved,
    in another listing-only submission; if the rank returns after that, the title is the cause.
    **Second read, 2026-11-16 07:15 CST**, four days after the title went live and about 15 hours
    after v1.38.0.0 published. An unscheduled pass at 18:52 CST on 2026-11-15 had matched the
    2026-11-14 table exactly, with the same sixteen listings above RoRoRo on "roblox launcher" in
    the same order. Overnight five queries moved, four of them up:

    | Query | 11-11 | 11-14 | 11-15 18:52 | 11-16 07:15 | Passes on 11-16 |
    |---|---|---|---|---|---|
    | multi instance | 14 | 17 | 17 | **3** | 7 of 7 |
    | alt accounts | 19 | 19 | 19 | **2** | 5 of 7; the other 2 read 19 |
    | multibox | absent | absent | absent | **5** | 7 of 7 |
    | multi-instance launcher | absent | absent | absent | **14** | 4 of 4 |
    | roblox launcher | 7 | 17 | 17 | 17 | 9 of 10; the other read 12 |
    | roblox account manager | absent | absent | absent | absent | 7 of 7 |
    | account manager, instance launcher, multi launcher, launcher | absent | absent | absent | absent | |
    | rororo | 1 | 1 | 1 | 1 | |
    | the eight Roblox-word queries (multi roblox, roblox multi, multiple roblox, roblox alts, roblox accounts, and roblox in US, BR, RU, ID, PH) | absent | absent | absent | absent | |

    Against the rule fixed above: the criterion query, "roblox account manager", still has not
    entered, and "roblox launcher" is still ten places under its baseline. But "multi instance" went
    from 14 to 3, and a title phrase that was absent now ranks ("multi-instance launcher", 14). So
    the verdict of 2026-11-14, "worse than nothing", no longer holds.

    **Not established: what moved it.** Two of the four gains are on words that are keywords and
    not in either title ("alt accounts", "multibox"), which a title change alone does not explain.
    Nothing changed on our side between the two reads twelve hours apart: v1.38 had been live for
    three hours at the first of them. That fits Store search re-indexing the listing some time
    after a publish, and it does not say whether the publish it caught up with was the name
    (2026-11-12) or v1.38 (2026-11-15), which carried the same name and the same keywords. Two
    queries answered with two different positions within minutes ("alt accounts" 2 and 19,
    "roblox launcher" 17 and 12), which reads as an index part-way through a change. Not settled
    until a later read agrees.

    Installs, for the record and not as evidence either way: 7,266 lifetime through 2026-11-15;
    Saturday 192 and Sunday 154 against 166 and 151 a week earlier, after four weekdays well under
    the week before.

    Next read: the evening of 2026-11-16 and again on 2026-11-17. Recommended to the owner: hold
    the name, since a revert now would end the test at its first movement. The call is his.

    **Third read, 2026-11-17 09:45 CST** (the evening read on 2026-11-16 was not taken). The four
    gains hold exactly: "multi instance" 3 (three passes: 3, 3, 3), "alt accounts" 2, "multibox" 5,
    "multi-instance launcher" 14. **"roblox launcher" is now outside the top 20 on three passes**
    (7 before the rename, 17 on 11-14 and 11-16). "roblox account manager", the criterion query, is
    still absent, and the other fifteen queries are unchanged. There is no search-volume data here,
    so whether the lost query outweighs the four gained ones is not known. Installs the week of
    11-08 ran 122 a day against 154 the week before; the weekday dip began on 11-09, three days
    before the rename went live, so it does not settle the question either. The owner held the name
    on 2026-11-16, and held it again on 2026-11-17 with this read in front of him.
13. **Listing languages.** Indonesia is 4.8% of acquisitions and Ukraine 1.8%, with no localized
    listing. Listing-only, per the standing rule that manifest languages need real UI
    localization.
14. **Keyword audit** against the rule that failed Sanduhr and the Mod Launcher: a keyword that is
    another publisher's product title. The sheet leads with `roblox` and has certified every
    time; read the live set before changing anything.

## Tier 4: reach (the owner's clicks; kits prepared here)

15. **The trailer.** Eight language cuts are rendered. Whether the YouTube upload happened is not
    known from here. The top tutorials teach a manual method, and newer ones point at
    closed-source download sites.
16. **Directories.** AlternativeTo lists a three-month-old competitor as "successor of RAM" and
    does not list RoRoRo. `winget install 9NMJCS390KWB` already works and is said nowhere.
17. **The displaced audience.** Users of an archived account manager and of a launcher that
    dropped multi-instance are looking for a replacement. The honest pitch is the one already in
    the copy: the official client, unmodified, no injection, signed by the Store, source open.

## Uncertain, and marked as such

- **Where the 150 Store clicks a day start.** Not from the obvious searches. Related-app rails
  and web traffic that opens the Store client are both possible. Unknown.
- **Why Brazil, Poland and the UK retain so poorly.** Active user-days per acquisition: PH 6.0,
  UA 4.3, IN 3.9, US 3.5, against BR 1.1, GB 1.2, PL 1.2. Two of the three have a localized UI,
  one does not. Telemetry consent differs by region and could explain part of it. Not ruled out:
  a defect that only shows under those cultures. A pt-BR and pl walk on a clean profile would
  settle it.
- **The unclassified crashes and hangs.** No stack exists for them. The named hang,
  `APPLICATION_HANG_ENDTASK_HungThreadIsIdle`, means Windows ended the task while the UI thread
  sat idle, which fits someone killing a tray app that would not close. That is a reading of the
  name, not of a dump. One downloadable cab exists (expires 2026-11-20).

  **Read 2026-11-15.** The cab could not be downloaded. The API's failure-details row for it says
  `cabDownloadable: false`, and `cabdownload` answered 401 with the same credentials that read
  everything else. Its expiry now reads 2026-12-02, not 11-20; which reading was wrong is not known.
  Over 90 days it is the only cab. The `stacktrace` endpoint did answer, with 22 frames: a managed
  exception leaving a window procedure through native frames (`user32!DispatchMessageWorker`,
  `UserCallWinProcCheckWow`, `coreclr!PropagateExceptionThroughNativeFrames`), and no managed frame
  named. It belongs to `CLR_EXCEPTION_80263001`, the desktop-composition crash of #246, fixed in
  v1.35 by `CompositionFaultFilter`; all 12 of its events are on v1.33.0.0. So the cab says nothing
  new. What the pull did show: **both named failures are v1.33.0.0 only** (92 hangs, 12 crashes),
  and every failure on v1.34 and later is unclassified (30 days to 2026-11-15, on 1.34, 1.35, 1.36
  and 1.37: crashes 45, 74, 33, 15; hangs 54, 165, 72, 19). The composition crash being absent after
  v1.35 is therefore not proof the fix holds, because a later one would land in the unclassified
  pile. Microsoft's data will not name these. The route to a stack is v1.38's crash notice and the
  support bundle.
- **Whether the Store edition of Roblox can multi-instance at all** once it is launched. Untested.
- **The `aocr.org` referrer** (96 unique visitors in 14 days). The site is a radiology society
  behind a bot wall. Unexplained.
- **Subreddit and Discord rules** on tools and self-promotion. Not readable from here.

## Leave alone

- The compat drift banner. It can never fire: Roblox reports its version as `0, 747, 0, 7471481`
  and `Version.TryParse` rejects that (`RobloxCompatChecker.cs:88`). The published known-good
  ceiling is 0.729 against an installed 0.747, so fixing the parse alone would put a
  non-dismissible warning on every install. Fix both together or neither.
- Roblox-already-running handling, the silent tray-client takeover, the typed launch results, the
  one-account layout, the absence of startup session validation. All read as correct.

## Lines that do not move

No "Roblox" in the product name. No ban-proof, "safe" or "undetectable" claim anywhere. No
telemetry. No automation against roblox.com. The Store binary never synthesizes input.
