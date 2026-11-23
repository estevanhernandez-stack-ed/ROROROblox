# The gap between Get and installed

**Date:** 2026-11-23. **Data through:** 2026-11-21. **Read-only:** Store analytics API and the public Store catalog. Nothing was changed.

## Verdict

The Store funnel says 58% of people who click Get go on to install. That number does not show people failing to install RoRoRo, and nothing here is a fix that moves the 10K date.

- The 58% is not a clean measurement. Like for like it is about 65 to 68%.
- RoRoRo still reads about 20 points below two sibling apps on the same account. The cause is not established. Every explanation that would mean "installs are failing" was tested and did not hold.
- By the install counter that feeds the download number, RoRoRo gets 2.07 installs for every signed-in acquisition, the highest of the six apps on the account.

## What the funnel is

Microsoft's definitions (Acquisitions report, "Acquisition funnel"):

| Step | Definition |
|---|---|
| Page views | Views of the listing, including people not signed in. Excludes people who opted out of sharing this. |
| Acquisitions ("Get") | New customers who got a licence **while signed in to a Microsoft account**, within 48 hours of viewing the listing. |
| Installs | Customers who installed the app after acquiring it. |
| Usage | Customers who used the app after installing it. |

So the funnel only follows signed-in people who viewed the listing first. Over 90 days that is 1,325 funnel installs against 6,763 installs in total: about one install in five.

## What was measured

### 1. The rate is steady, so it is not a release

Weekly, for the twelve weeks from 2026-08-30: between 57% and 68% in every week but the oldest (81%, on 70 acquisitions, at the edge of the report's 90-day memory). No release moved it.

### 2. The funnel does not add up

- Filtered to one market at a time, the top 40 markets hold 1,440 funnel installs. Unfiltered, the whole world holds 1,325.
- "Used" is larger than "installed" in nine of the twelve weeks, and larger than "Get" in four.

The steps are counted separately and loosely. The unfiltered 58% understates the rate; summed by market it is 67.7% (1,440 of 2,128).

### 3. One in eight acquisitions never installs, on every app

The acquisitions report has a bucket labelled Windows 10 with device type "Unknown". Over 90 days:

| App | Acquisitions | In that bucket | Installs from it |
|---|---|---|---|
| RoRoRo | 3,261 | 416 (13%) | 21 |
| RBX15 | 280 | 24 (9%) | 1 |
| 626 Mod Launcher | 164 | 18 (11%) | 0 |
| Right Click to PNG | 49 | 5 (10%) | 0 |
| Sanduhr | 31 | 4 (13%) | 0 |
| SnapSnip | 16 | 2 (12%) | 0 |

It is in all six apps at about the same share, in 58 markets, across all age groups, and it grows and shrinks with ordinary traffic. Windows 11 has no such bucket. What these acquisitions are is not known. They are the Store's, not RoRoRo's.

### 4. RoRoRo is still lower than its siblings

Same twelve markets, same 90 days, each market filtered separately:

| App | Get | Installed | Rate |
|---|---|---|---|
| RoRoRo | 1,661 | 1,083 | 65% |
| RBX15 | 133 | 117 | 88% |
| 626 Mod Launcher | 73 | 62 | 85% |

United States alone: RoRoRo 67% (382 of 567), the two siblings together 84% (86 of 102). The difference is several standard errors wide; it is not noise.

The shortfall is concentrated. Germany 40%, Poland 44%, France 46%, Denmark 47%, United Kingdom 52%, Brazil 52%, Belgium 53%. At the other end: Philippines 91%, Turkey 92%, Mexico 94%, India 83%, Indonesia 75%. In Germany, France, Poland, the United Kingdom and Brazil the two siblings install at 40 of 46.

### 5. Explanations tested

| Explanation | Result |
|---|---|
| A release broke installs | No. Steady for twelve weeks, across every release from 1.24 to 1.40. |
| Market mix | No. The gap holds inside the United States alone. |
| Age rating blocks child accounts | No. RoRoRo's ratings are identical to RBX15's in every system (ESRB E, PEGI 3, USK Everyone). |
| The download is too big (112 MB against 73 to 88 MB) | Not supported. The markets with the slowest connections install best. |
| The six-language package (2026-09-07) | Not supported. Spanish-speaking Mexico, Argentina and Colombia install at 94 to 100%; Russia at 71%; the United Kingdom and Denmark, not localized, are low. Installs per acquisition on Windows 11 in the low markets did not fall in September (2.74 in August, 2.28, 3.01, 2.97 after). |
| Windows 10 below the 22H2 floor | Real mechanism, no visible cost. See below. |

### 6. The Windows 10 floor

A device below a package's minimum Windows version can still acquire it and then cannot install it. Three independent readings:

- RoRoRo was Windows 11 only until 2026-07-06. Before that date Windows 10 shows 28 acquisitions and 0 installs. Windows 10 installs begin the week the floor dropped to 22H2.
- Right Click to PNG and SnapSnip (floor 10.0.22621) show 6 Windows 10 PC acquisitions and 0 installs.
- RBX15, Mod Launcher and Sanduhr (floor 10.0.17763) show Windows 10 PC acquisitions and installs about equal.

RoRoRo's floor is 10.0.19045 (22H2). Windows 10 builds 19041 to 19044 share its core and are shut out. But the cost does not show: on Windows 10 PCs RoRoRo gets 0.80 of the installs per acquisition it gets on Windows 11 PCs, and the siblings with the 17763 floor get 0.77 and 0.63. If every bit of RoRoRo's Windows 10 shortfall were the floor, it would be about 4 installs a day; the sibling comparison says it is less.

### 7. What the install counter says

| | Signed-in acquisitions | Installs | Installs per acquisition |
|---|---|---|---|
| RoRoRo, 90 days | 3,261 | 6,763 | 2.07 |
| RoRoRo, lifetime | 3,891 | 7,891 | 2.03 |
| RBX15, 90 days | 280 | 340 | 1.21 |
| 626 Mod Launcher, 90 days | 164 | 207 | 1.26 |

More than half of RoRoRo's installs have no signed-in acquisition behind them: people who are not signed in to the Store, second and third PCs, and reinstalls (Microsoft counts all three as installs and does not count updates). The Philippines runs at 3.8 installs per acquisition.

## The likeliest reading, not proven

RoRoRo has the largest pool of installs with no licence behind them, and it is largest in the same markets where the funnel reads lowest (Windows 11 installs per acquisition: 2.2 to 3.0 in the low markets, 1.6 to 2.5 in the high ones, and higher in the low markets in every month since July). A person in that pool who later opens the listing while signed in picks up a licence, and the funnel records an acquisition with no install after it, because the install came first. That would lower the funnel rate without one failed install.

This is an inference. The API cannot follow one customer through the steps, so it cannot be confirmed or ruled out from here.

## What this means for 10K

- No install failure was found to fix.
- Lowering the floor to 10.0.19041 is safe in principle (same Windows core as 22H2, and the target framework is already `net10.0-windows10.0.19041.0`) and worth a few installs a day at the very most. Not a 10K lever. It would need its own check that every manifest extension behaves on 19041 to 19044.
- The download number counts every install, including the ones the funnel cannot see. The funnel is a poor instrument for RoRoRo; the install counter is the one to watch.

## Method

All calls are `GET https://manage.devcenter.microsoft.com/v1.0/my/analytics/{report}` with `applicationId`, dates 2026-08-24 to 2026-11-21 unless stated.

- `funnel`: unfiltered for 7, 14, 30, 60 and 90 days; one call per week for twelve weeks; `filter=market eq 'XX'` for the top 40 markets; `deviceType`, `campaignId` and `ageGroup` filters (age returned data only under Unknown); six 15-day windows for two market groups using `or`.
- `appacquisitions`: grouped by `osVersion`, `deviceType`, `storeClient`, `acquisitionType`, `ageGroup`, `market`, and pairs of those; weekly by `date,market,osVersion` from 2026-05-01.
- `installs`: grouped by `osVersion`, `deviceType`, `market`, `packageVersion`, and pairs; weekly by `date,market,osVersion` from 2026-05-01.
- The same `funnel`, `appacquisitions` and `installs` calls for the five other apps on the account.
- Public catalog (`displaycatalog.mp.microsoft.com/v7.0/products/{id}`) for package size, minimum version, dependencies and content ratings.

Raw responses stayed outside the repo.

## Not checked

- What the Windows 10 "Unknown device" acquisitions are.
- Whether a device below the floor sees a warning before it acquires.
- The one review that reports "no app can launch this link" on launch (2026-11-07). That reads like a PC without the Roblox desktop client, which is a first-run problem, not an install problem.
