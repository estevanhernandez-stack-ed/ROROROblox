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
