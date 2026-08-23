# RORORO — Technical Spec: the numbers the app already knows

**Cycle:** v1.23.0.0 · **Written:** 2026-08-22 · **Scope:** local session statistics, no telemetry

A stats surface built from launch history RoRoRo already writes to disk. Nothing new is collected,
nothing leaves the machine, and the published privacy promise is unchanged.

---

## §0 The measurement that shaped this design

Everything below follows from one number, measured against the live file before any design work.

`SessionHistoryStore.MaxRows = 100`, pruned on every write (`SessionHistoryStore.cs:14`, `:71-77`).
On the author's machine at time of writing, that file held **exactly 100 rows spanning 19.9 days**
— roughly five launches a day.

So the raw history is a **rolling ~20-day window that is already full and already discarding**.
Any statistic phrased as "all time", "this year", or "since you installed it" is not merely
unavailable — it is being actively thrown away right now, at about five rows a day.

That kills the obvious implementation (read the history file, sum it) before it is written, and it
is the reason this cycle adds a store rather than a page.

Also measured in that same window, and used as the acceptance baseline in §7:

| Fact | Value |
| --- | --- |
| Rows with an end timestamp | 93 of 100 |
| Measurable uptime in the window | 18.9 hours |
| Distinct games | 3 (Pet Sim 76, Roblox home 22, Following a friend 2) |
| Accounts appearing | 8 of 8 |

## §1 What ships

Six numbers. Chosen because each is either a sum the rollup makes cheap, or a record no other tool
on the machine can produce.

1. **Peak concurrent alts** — the most clients running at once, ever. This is the flagship. It is
   the product's entire thesis expressed as one number, and nothing else the user owns can tell
   them. It is also the only stat here that cannot be reconstructed after the fact (§3.3).
2. **Total uptime across all alts** — the headline sum.
3. **Per-alt leaderboard** — launches and uptime per account.
4. **Most-played game** — by uptime, not launch count; a game you open and quit is not your
   most-played.
5. **Longest single session.**
6. **Day streak** — consecutive local days with at least one launch.

## §2 Data model — `session-stats.json`

Lives beside `session-history.json` in `%LOCALAPPDATA%\ROROROblox\`. Five collections, all sums or
records, none of them per-session rows:

- **Per-account**, keyed by `accountId` — launches, total uptime, last seen
- **Per-game**, keyed by `placeId` — launches, total uptime, last known name
- **Per-day**, keyed by local date (`yyyy-MM-dd`) — launches, uptime
- **Records** — peak concurrent alts, longest single session (duration + accountId + placeId + when)
- **Integrity** — count of sessions that never recorded an end

Growth is bounded by accounts × games × days rather than by launches: on the order of 8 + 3 + 365
entries a year. Under 100 KB annually, against a history file that is already 54 KB for twenty days.

### §2.1 Keys are ids; names are resolved at render

**No display name is ever stored in this file.** Not for accounts, and the game name that is stored
is explicitly "last known" and never a key.

This is F-A's lesson applied before the defect exists rather than after. `LaunchSession` bakes
`AccountDisplayName` in at write time (`MainViewModel.cs:2824`), which is why History shows a stale
Roblox display name after a local rename, and why `SessionHistoryPage` — holding no account roster
— cannot repair it at render. That is the third site of a shape the v1.10 window-title fix already
corrected twice.

A rollup keyed by name would be worse than the original defect, because a rename would not merely
mislabel a row, it would **split a lifetime total in two**.

### §2.2 The rollup needs one fold tier of its own

Per-account is bounded by how many accounts exist; per-game by how many games are actually played.
**Per-day is the only collection that grows forever**, at 365 entries a year.

That is a write-path problem before it is a disk problem. `ApplyAsync` is read-modify-write on every
session end (§3.1), so a file growing linearly forever recreates the exact objection this design
raised against simply raising `MaxRows`: a five-year file is roughly 365 KB rewritten every time a
client closes. Slower clock, same mistake.

**Decision: keep ~400 days raw, fold older days into per-month buckets.** Four hundred covers
thirteen months, which is what "this year" and any year-over-year comparison need. Months accrue at
twelve a year. The file goes effectively flat — after ten years, ~400 daily plus ~120 monthly
entries.

**Streaks must therefore be records, not derivations.** A streak spanning the fold boundary cannot
be recovered from monthly totals, so current-streak-start and longest-streak-ever are maintained
incrementally as days land and are never recomputed from buckets. With that, folding is lossless
for every stat in §1, and day buckets exist only to serve recent detail such as busiest day.

Folding runs at most once per session end, and only when the raw-day count exceeds the threshold.

## §3 Components

Three, each with one job.

### §3.1 `SessionStatsStore` (Core)

Owns the file. `ApplyAsync(StatsEvent)` and `ReadAsync()`. A `SemaphoreSlim(1,1)` gate matching
`SessionHistoryStore`'s existing shape, because both are read-modify-write over one JSON file and
a launch can race a session end.

### §3.2 `StatsRecordingSessionHistoryStore` (Core)

A decorator over `ISessionHistoryStore`. `AddAsync` records a launch; `MarkEndedAsync` records a
duration. Swapped in at registration; **no call site moves.**

This shape is deliberate and is the direct lesson of F-121. That defect existed because a fix landed
at one call site while a second call site kept the old form, with a comment asserting they matched.
A decorator means there is still exactly one call path into history, so stats cannot silently
disagree with what History shows. The alternative — calling a stats service from `MainViewModel`
alongside the history call — creates the second site on day one.

The decorator must pass through faithfully, including exceptions. A stats failure must never fail a
launch record; a swallowed inner call would be a data-loss bug wearing a feature's clothes (§7).

### §3.3 Concurrency hook (App wiring)

One line where `IRobloxProcessTracker` events are already wired: on `ProcessAttached`, push
`Attached.Count` to the store, which keeps the maximum.

This is separate from the decorator because peak concurrency is **not a property of any session**.
It is a property of a moment, and it cannot be recovered later — two sessions overlapping in the
history file tells you they overlapped, but the rows for the third and fourth alt may already have
been pruned out from under you. It has to be sampled while true.

## §4 Backfill

On first run, seed the rollups from whatever rows the history file still holds, then set a version
flag so it never runs twice.

Without this the page reads zero on the day it ships, which is a bad first impression of a feature
whose entire appeal is an accumulated number. With it, the author's machine starts at ~20 days and
18.9 hours. The backfill is explicitly best-effort: it recovers uptime, per-account, per-game, and
per-day, and it **cannot** recover peak concurrency (§3.3) — that record starts at the highest
concurrency observed after install, and the UI must not imply otherwise.

## §5 Honesty rules

The failure mode for a stats page is not crashing. It is being quietly wrong, which is
unfalsifiable from the user's side and erodes trust in every other number on the screen.

- **Unended sessions are excluded from uptime and counted out loud.** Seven of the measured hundred
  had no end timestamp. The page says "N sessions didn't record an end" in small text. Silently
  dropping them undercounts; silently attributing them a duration invents data.
- **Durations clamp at zero.** A clock change or a bad row must not poison a lifetime total.
- **A corrupt or unreadable stats file starts fresh and logs.** It never blocks a launch. Stats are
  the least important thing this app does.
- **Peak concurrency is labelled as "since install"** where backfill could not reach, rather than
  implied to be all-time.

## §6 Decisions taken, with their costs

**Streaks count local days.** "Days in a row I played" is a human notion and a UTC boundary would
break a streak at 7pm local. Cost, stated: travel across time zones or a DST shift can miscount by
one day. Accepted — the alternative is a streak that breaks for reasons the user cannot see.

**Stats live on the existing Session History page**, not a new navigation entry. Same subject, page
already exists. It gains one dependency, the account roster, to resolve names per §2.1 — which is
the same seam F-A's eventual fix requires, so the work serves both.

**Uptime ranks the most-played game, not launch count.** A game opened and quit five times is not
more played than one session of three hours.

## §7 What gets tested

- Rollup arithmetic: launches, uptime sums, per-account, per-game, per-day.
- Peak concurrency moves only upward, and never downward on detach.
- Streaks across a gap, across a month boundary, and across a DST transition.
- Corrupt-file recovery: unreadable JSON yields a fresh store and a log line, not a throw.
- Duration clamping: a negative span contributes zero, not a negative total.
- Unended sessions are counted and excluded, and the count is what the UI reads.
- **Decorator pass-through**: the inner `ISessionHistoryStore` receives every call unchanged, and an
  exception from the stats side does not prevent the history write. This is the F-121 guard — the
  test exists because the failure it catches is invisible until someone's history stops recording.
- Folding: days beyond the threshold collapse into the right month, totals survive the fold, and
  the fold is idempotent.
- **Streak records survive a fold.** A streak established before the boundary still reads correctly
  after older days become months — the test that proves streaks are records rather than derivations.
- Backfill is idempotent: running it twice does not double any total.
- Backfill runs against a **committed fixture** shaped like §0's measurement — 100 rows, 93 with
  end timestamps, 3 games, 8 accounts — and produces that fixture's known totals. The fixture is
  checked in and anonymised; no test may read the developer's live `session-history.json`, which
  differs per machine, is empty in CI, and would make the suite pass or fail on whoever ran it.

## §8 File structure

```text
src/ROROROblox.Core/
  SessionStats.cs                        read model + StatsEvent
  ISessionStatsStore.cs
  SessionStatsStore.cs                   file + gate
  StatsRecordingSessionHistoryStore.cs   decorator
src/ROROROblox.App/
  App.xaml.cs                            registration swap + concurrency hook
  History/SessionHistoryPage.xaml(.cs)   stats surface + roster dependency
src/ROROROblox.Tests/
  SessionStatsStoreTests.cs
  StatsRecordingSessionHistoryStoreTests.cs
  SessionStatsBackfillTests.cs
```

## §9 What this cycle must not do

- **No telemetry, of any kind, opt-in or otherwise.** `docs/PRIVACY.md` promises "No telemetry. No
  analytics. No third-party tracking." in its TL;DR — the most-read line of a published Store
  privacy policy. Nothing here sends anything anywhere. If a future cycle wants that, it is a
  privacy-policy rewrite and a Store listing change, not a feature.
- **No new manifest capability.** `Package.appxmanifest` declares `runFullTrust` only, and this
  cycle gives it no reason to declare more.
- **Do not raise `MaxRows` "while we're here."** The history store rewrites the whole file on every
  write; a larger cap puts a proportionally larger read-modify-write on the launch path. The rollup
  exists so the cap does not have to move.
- **Do not fix F-A in this cycle.** It is a real defect and the roster dependency added in §6 is the
  seam it needs, but a name-resolution change to the history writer is its own row and its own test.
  Adding it here would let a stats cycle quietly change what History displays.
