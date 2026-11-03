<!-- Cart cycle #6, step 5. Input: spec.md beside this file (every "Spec ref" below points into it).
     Written here rather than docs/checklist.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md
     treats as history. Autonomous run (profile autonomy_level: fully-autonomous, builder: self); every
     beat the record did not answer is marked "(default — confirm on next interactive run)" and collected
     at the end. Every item carries the five fields /build reads: title, Spec ref, What to build,
     Acceptance, Verify. -->

# RoRoRo v1.36 — Build Checklist: the account manager and the Library

Sixteen items, three halts. Order is dependency order and it is **Core first**: nothing paints until
the stores, the facts lane and the two joins exist and are tested without WPF, because every surface in
this cycle is a view over data that already exists (spec, first paragraph). The first item moves no
feature at all; it pins the numbers the rest of the cycle is measured against, which cannot be captured
once the first XAML change lands.

Sixteen is above the usual Cart count, and that is the honest size: a shipped Store app's release cycle
with two new surfaces, nine riders and a release lane. The alerts cycle ran thirteen. Consolidating
would hide a halt or fuse a Core item with a XAML item, and both are the kind of seam a worker gets
wrong.

**Three checkpoints where the build halts for Este's eyes or hands:**
**C1 after item 5** (the Core is in; mocks and probes: Este picks the rendered shape of the row, the
panel and the Library, and runs the two live measurements the row's vocabulary depends on),
**C2 after item 7** (the roster is built: one-account install and eight-alt roster, eyes on both), and
**C3 after item 9** (the Library is built and must be looked at).
A fourth halt is built in rather than numbered: the Store write in item 16 waits for Este's explicit
word in the session, per house rule.

Effort: **S** under an hour, **M** a few hours, **L** most of a day. Sized for an Opus session
dispatching one Sonnet worker per item with the orchestrator verifying every report against the tree
(the era map's `bulk` under `judgment`).

## Build Preferences

- **Build mode:** Autonomous. Cart drives; each item dispatches to a worker, and the orchestrator
  verifies the worker's report against the tree before the box is checked. This repo's established
  pairing (alerts cycle, v1.20 cycle). Run from an **Opus** session; this plan was written on Fable 5.1
  and the era map says builds and releases run on Opus with Sonnet workers.
- **Comprehension checks:** N/A (autonomous).
- **Git:** branch `cycle/v1.36-account-library` off `main` at `5a8fe61` (the alerts cycle's
  `cycle/v1.33-alerts` shape). One commit per item, conventional commits, the session's attribution
  trailer. **A fence ceiling or floor moves in the same commit as the change that moves it, never in a
  follow-up.** Pre-commit hooks installed once per box (`.claude/hooks/install.ps1`). One PR to `main`
  after item 13 with `ci.yml` green on x64 and native arm64; items 14-16 run from `main` and
  `release/v1.36.0.0` per the playbook. The build never writes to a Store surface.
- **Verification:** Yes. Three halts (C1, C2, C3) plus the Store-write gate. Every item's Verify is a
  runnable command with its expected output where one exists; only visual checks go to a human, and
  those are collected at the halts rather than sprinkled through the items.
- **Check-in cadence:** N/A (autonomous). Per-run unattended mode is Este's opt-in at the `/build`
  confirmation (`references/unattended-run.md`); the three halts and the Store gate hold either way.
- **Standing gates carried into every item** (spec §6): the existing suite stays green (2,802 unit +
  27 harness, 1 skip by design); no new RPC and no RPC change; every new colour is a `DynamicResource`
  of an existing slot or a derived brush; nothing this cycle adds makes a network call at startup;
  `MainViewModel` learns delegates and events, not features; a worker that finds the tree disagrees
  with this plan stops and says so rather than building to the plan.

---

## 1. Pin the bars before anything moves

**Spec ref:** `spec.md > §6 Nothing moves for the six-alt user` (pixel bar, startup bar), `§7.3`
(the #222 measurement), `§11` (`AccountRowCollapsedRenderTests`). **Depends on:** nothing.
**Effort:** S, plus a day of wall clock for the #222 count that runs in the background.

**What to build:** Three baselines captured on v1.35 (`5a8fe61`), before the first XAML change,
because none of them can be taken afterwards.

1. `Rendering/AccountRowCollapsedRenderTests.cs`: renders one collapsed `AccountSummary` row through
   the real `MainWindow` template at 96 and 120 DPI (the shape of `HistoryRowRenderTests`) and pins the
   header `ActualHeight` as `V135RowHeight96` / `V135RowHeight120` constants written from the measured
   values, with a comment naming the commit they were taken at.
2. The startup baseline: five cold starts of the v1.35 build on this machine with the same eight-account
   vault; time from the Serilog banner to the `mainWindow.Show()` log line; the five numbers, their
   median, the machine and the vault shape go in `docs/store/smoke-2026-11-xx-account-library.md`
   (create it; the per-cycle smoke doc is the record for every "measured, not tested" row in §12).
3. The #222 day starts now, on v1.35: `scripts/count-windowless-clients.ps1` (new, read-only:
   `Get-Process RobloxPlayerBeta | Where-Object MainWindowHandle -eq 0 | Select-Object Id, StartTime`,
   appending a timestamped row to the smoke doc), run at the end of a day with ten launches. Item 12
   reads the result.

**Acceptance:**
- [x] `AccountRowCollapsedRenderTests` passes on the untouched v1.35 row at both DPIs, and fails when a
      constant is changed by one pixel (mutation-checked, the red recorded in the commit message).
      *(`8a4e6b0`: 92 px at 96 DPI, 115 at 120. The 120 value is the 96 layout rendered at 125%, the
      `HistoryRowRenderTests` shape; layout-at-125% stays an eyes-on check at C2.)*
- [x] The smoke doc exists with five v1.35 startup timings, the median, the machine and the vault shape.
      *(Partly met, by evidence: the field logs hold only two v1.35 starts, one of them clean (1,831 ms).
      Este's live session ruled out cold starts tonight, so the five timings and the 10% bar move to
      C2's controlled A/B, a `5a8fe61` worktree against the branch. Dashboard `pkpDK4VluGe1wf1TBEUH`.)*
- [x] The count script is committed, prints no account identity (process id and start time only), and
      the smoke doc has its first row.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~AccountRowCollapsedRenderTests"`
→ 2 passed. `git diff --stat main` touches only the new test, the script and the smoke doc: no XAML.

## 2. Health has a vocabulary, and a probe that can measure it

**Spec ref:** `spec.md > §1.1 The state set`, `§1.2 The signal`, `§1.3 Launch all and Launch multiple
summary`. **Depends on:** 1. **Effort:** M.

**What to build:** `AccountHealth { Ok, FollowMarked, Limited, NeedsVerification, Expired }` and the
computed `Health` on `AccountSummary` (first match wins, §1.1). `SessionLimitedException.ChallengeType`
read from `rblx-challenge-type` on the auth-ticket and presence 403s, null when absent.
`LaunchResult.NeedsVerification(type)` and `SessionNeedsVerification` on the summary, **compiled in and
set only from that signal**; if C1 finds no distinct signal the flag never sets and the enum member
stays. The health badge as a `GhostButtonStyle` `Button` with word, glyph and action per the §1.1 table
(`OpenRobloxHomeCommand` is the one new command: `LaunchAccountAsync(summary, LaunchTarget.Home)`).
`HealthVerifyBrush` seeded in `App.xaml` and derived in `ThemeService.ApplySlot` as the midpoint of
`RowExpiredAccent` and `Magenta`. The Follow-marked badge on the name line, taking the secondary line
only at rest (rung 9 reads "Joins by following · Ready"). The `Shell_Launch_Clause_FollowMarked`
sentence in the batch banner from `SquadLaunchPlan.Build`'s count; no behaviour change in the batch.

And the probe: `tools/EndpointProbe/`, a console app referencing Core in the shape of
`tools/MetricSmoke`. It lists saved accounts by local name and id, **never prints a cookie**, and for a
chosen account records status, every `rblx-*` header and the body for the ticket POST, the presence POST
and `validate-membership`, writing a redacted markdown report under
`docs/cycles/2026-11-01-account-library/probes/`. It exists so C1's measurements are the client's own
output and never a hand-typed request with a cookie in a shell history.

**Acceptance:**
- [x] `RobloxApiTests` pins the header read both ways: a 403 carrying `rblx-challenge-type: captcha` →
      `ChallengeType == "captcha"`; a bare 403 → null; a 401 → `CookieExpiredException` as before.
- [x] Four states are four colours from three slots: `ContrastPairGateTests` passes `HealthVerifyBrush`
      against Bg and RowBg in all four themes; `ThemedStatusColourTests` offenders stay 0 (the seed is
      one more allow-listed literal; the ceiling moves only if the allowed count passes 25, in this commit).
- [x] Under flatline the four states differ with colour removed: a `[Theory]` over the four asserts
      distinct `(Glyph, Word)` pairs.
- [x] `NeedsVerification` has exactly one setter path (`LaunchAccountAsync` on
      `LaunchResult.NeedsVerification`); a 403 without the header still flips `SessionLimited` through
      `PresenceService` only; **a 403 never sets `JoinViaFriend`** (test).
- [x] A marked account in a game reads "In `<game>` · `<time>`" in the secondary line; a marked account
      at rest reads "Joins by following · Ready" (`AccountSummaryTests`).
- [x] `AccessibleNamingFenceTests`: the badge is named "`<word>`, `<action>`"; `UnnamedCeiling` stays 1;
      `ScannedFloor` rises to the new count in this commit.
- [x] `AccountRowCollapsedRenderTests` from item 1 still passes: the badge sits on existing lines and
      adds no height.
- [x] The probe prints no cookie under any flag: run against a fake vault holding an obviously fake
      cookie and grep stdout and the report for it; the report lands under `probes/` where the pre-commit
      cookie-prefix scan sees it like any other file.
- [x] Launch all with two marked accounts and a joinable main shows "2 will follow the main"; held with
      a reason when the main is not joinable, as today (`LaunchEligibilityTests`).

*Built: 5b9c0b7 (probe), b6cbab7 (health), 1adebae (one HealthActionCommand, no Command setter in the badge style). 3,005 unit + 27/1 harness green. ScannedFloor 120 to 212, status-literal ceiling 25 to 26. Badges on the name line; the cyan at-rest dot is Este's to veto at C1. Strings ride scripts/add-v136-translations.py per item (dashboard dZevmkvgsfVYYF9asVwO).*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~RobloxApiTests|FullyQualifiedName~AccountSummaryTests|FullyQualifiedName~LaunchEligibilityTests|FullyQualifiedName~ContrastPairGateTests|FullyQualifiedName~AccessibleNamingFenceTests"`
→ all passed. `dotnet run --project tools/EndpointProbe -- --list` → local names and ids only; the
cookie warning prefix the guards scan for appears nowhere in stdout.

## 3. The facts lane, Core only

**Spec ref:** `spec.md > §2.1` (record, store fields, setters), `§2.2` (typed client, endpoints,
membership gate), `§2.3` (service, triggers, startup rule), `§2.6` (backfill gap), `§8` (the
`accounts.dat` row). **Depends on:** 1. Item 2 is independent and may run alongside. **Effort:** L.

**What to build:** `AccountFacts` record. `Account` and `StoredAccount` gain `Facts`, `Note` and
`PanelExpanded`, all three appended optionals in **one** change so the five `IAccountStore` fakes are
edited once (`AccountUserIdBackfillServiceTests.cs:195`, `PresenceServiceTests.cs:701`,
`MainViewModelTests.cs:914` and `:946`, `AutoRejoinWiringTests.cs:1426`). `IAccountStore.SetFactsAsync`,
`UpdateNoteAsync`, `SetPanelExpandedAsync` (silent no-op on unknown id, the `SetJoinViaFriendAsync`
family). `Note` on `AccountExportRecord`, mapped in export and import, format version stays 1; `Facts`
never travels. `IAccountFactsApi` + `AccountFactsApi` (exactly one ctor taking `HttpClient` and
`ILogger<AccountFactsApi>`, cookie attached per request, `MembershipProbe (Status, Body)`), registered at
`App.xaml.cs:1110-1119` with its own `SocketsHttpHandler { UseCookies = false }` and the
`RORORO/<version>` User-Agent, duplicated on purpose beside `RobloxApi`'s. `Core/BackfillPacing.cs`
constants, `AccountUserIdBackfillService` switched to them with no behaviour change.
`AccountFactsService`: one worker over a de-duplicating `Channel<Guid>`; three enqueue triggers (expand,
launch end, and a 60 s `PeriodicTimer` that picks the single stalest account older than 10 min, skipping
expired accounts and giving `RobloxUserId == null` accounts Robux only); the timer armed only after
`AccountUserIdBackfillService.RunAsync` completes inside `RunStartupChecksAsync` plus one tick;
membership mapping (200 `true` → "plus", 200 `false` → "none", anything else → null with `LastError`);
a 401 or 403 records `LastError` and **never** touches `SessionExpired` or `SessionLimited`;
`AccountFactsUpdated` event. `AccountUserIdResolved` event on the backfill service, applied by
`MainViewModel.ApplyUserIdResolved` to the live row; the stale comment at `MainViewModel.cs:1296-1300`
corrected in the same commit. `MainViewModel.ApplyFacts(Guid, AccountFacts)` keyed by id through
`_ui.Invoke`; `AccountSummary.Facts` and `Note` seeded in the ctor and notifying. The v1.1.2 anti-fraud
lesson (`App.xaml.cs:899-914`) quoted in the service's class comment.

**Acceptance:**
- [x] `AccountFactsSchemaTests` (copy of `LocalNameSchemaTests`): round-trip of `Facts`, `Note`,
      `PanelExpanded`; null omitted; a v1.35 blob loads all three null.
- [x] `AccountFactsServiceTests`: `RequestCount == 0` after construction, start and the load of eight
      accounts, until an expand or a tick; one account's three calls complete before the next account
      starts; consecutive accounts are 2.0–3.0 s apart on a fake clock; an expand on facts younger than
      60 s enqueues nothing; a tick picks the oldest account older than 10 min and only one; an expired
      account is never picked; a 401/403 from any call leaves the previous values standing, sets
      `LastError`, and the fake `PresenceService` sees no health change.
- [x] `TypedHttpClientRegistrationTests` gains the `AccountFactsApi` row and the single-ctor guard
      passes; one new assertion reads the registration's primary handler and requires
      `UseCookies == false` for **both** `RobloxApi` and `AccountFactsApi` (CLAUDE.md says no test guards
      this today; this cycle adds a second place to get it wrong, so the guard arrives with it).
- [x] `AccountStoreTransportTests`: `Note` round-trips through export and import; the sibling of `:57`
      asserts the export record carries **no** `Facts`.
- [x] `MainViewModelTests`: a row's `RobloxUserId` is set after `AccountUserIdResolved`; `ApplyFacts`
      for id A never changes row B.
- [x] `AccountUserIdBackfillServiceTests` green and unedited after the constant lift.
- [x] The `dpapi-cookie-blast-radius` agent run over `AccountFactsApi`, `AccountFactsService`,
      `AccountStore` and `tools/EndpointProbe`: no path logs, throws with, or serialises a cookie value;
      each finding fixed in this commit or named in its message.
- [x] No XAML in this commit.

*Built: 0f7ad4d (facts lane), 2b2bf9a (audit fixes: no redirects on the facts client, control-character cookies refused at the store door and skipped on import, redacted ToString on the three cookie records with a fence, validate-membership off until C1). 3,107 unit + 27/1 harness green. Blast-radius audit: no reachable leak; every finding fixed in 2b2bf9a except RobloxApi's redirect handling, deferred outside the cycle (dashboard OqR5dQUIXq4ALHKc7aaQ). The two-minute dev-build log grep moves to C2; its grep must exclude the System.Net.Http.HttpClient.IAccountFactsApi category.*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~AccountFacts|FullyQualifiedName~TypedHttpClientRegistrationTests|FullyQualifiedName~AccountStoreTransportTests|FullyQualifiedName~AccountUserIdBackfill"`
→ all passed. `dotnet build ROROROblox.slnx -c Release` → 0 errors. Dev build with eight accounts, wait
two minutes, search the app's log folder under `%LOCALAPPDATA%\ROROROblox` for `AccountFactsApi` → zero
lines (there is no panel yet, so zero full stop).

## 4. The Library join, Core only

**Spec ref:** `spec.md > §4.2` (`LibraryEntry`, `LibraryCatalog`, `GameIconCache`,
`GameStat.LastPlayedUtc`), `§3.2` (`LaunchSession.PrivateServerId`), `§3.7` (`UpdateGameAsync`, the
follow patch), `§8`. **Depends on:** nothing in 2 or 3; parallel-safe. **Effort:** M.

**What to build:** `Core/Library/LibraryEntry.cs` (record + `LibraryEntryKind`). `LibraryCatalog.ListAsync`
joining favourites, saved servers and Recent (unsaved places in `SessionStats.Games`, "played from
RoRoRo by any account"), each with `LastPlayedUtc`, `TotalPlayed` (`GameStat.Uptime`, all time),
`IsDefault`, `Target`. `GameStat.LastPlayedUtc` stamped on `LaunchRecorded` and filled once by
`SessionStatsBackfill` (already idempotent, latched by `BackfillCompleted`). `IGameIconCache` with the
file-backed `game-icons.json` (placeId → name, universeId, iconUrl, fetchedAtUtc; non-secret, safe to
delete); a miss enqueues a place lookup on item 3's worker (the channel gains a second message kind;
`IRobloxApi.GetGameMetadataByPlaceIdAsync` exists) raising `IconResolved`; nothing fetched for a place
not on screen. `LaunchSession.PrivateServerId` stamped by `RecordSessionStartAsync` for saved-server
targets. `ISessionHistoryStore.UpdateGameAsync(id, gameName, placeId, privateServerId)`, with
`StatsRecordingSessionHistoryStore` forwarding a `LaunchRecorded`-shaped correction so
`SessionStats.Games` counts it. `ApplyPresence` patches an open row with `PlaceId == null` on the first
in-game presence. The hardcoded `"Following a friend"` at `MainViewModel.cs:3288` becomes
`Shell_History_FollowingAFriend`, in English and all six catalogs, regenerated.

**Acceptance:**
- [x] `LibraryCatalogTests`: a favourite, a saved server and an unsaved played place come back as three
      kinds; a place that is both favourite and played comes back once, as Favourite, with the stat's
      `LastPlayedUtc`; order within kind is `LastPlayedUtc` desc; empty stores yield an empty list.
- [x] `SessionStatsStoreTests`: `LastPlayedUtc` stamps on `LaunchRecorded`; the backfill fills a legacy
      row exactly once.
- [x] `GameIconCacheTests`: a hit answers from disk with no request; a miss enqueues exactly one lookup
      and the file gains the row on `IconResolved`; the file deleted at runtime is recreated and nothing
      else is lost.
- [x] `SessionHistoryStoreTests`: `UpdateGameAsync` patches game and place on one row and no other; a
      legacy row reads `PrivateServerId == null`.
- [x] `MainViewModelTests`: a follow launch whose first in-game presence names a game ends with a
      history row carrying that place, and the stats store counted it.
- [x] `LocKeyParityFenceTests` and `CultureKeyParityFenceTests` green (the one key this item adds is in
      all seven files; item 13 sweeps the rest of the cycle's strings).
- [x] No XAML in this commit.

*Built: eb80db9. 3,134 unit + 27/1 harness green. Two deviations, both improvements: the follow patch reaches stats as a new StatsEvent.GamePlaceResolved rather than a literal LaunchRecorded, which would double-count the account's launches, day bucket and streak; and SessionStatsBackfill gains a one-time GameLastPlayedFilled path, because existing users are already latched by Backfilled and would never get LastPlayedUtc. ListAsync over 25 unsaved places fetches nothing. MainViewModel +59/-5 cumulative from 3974579.*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~LibraryCatalogTests|FullyQualifiedName~GameIconCacheTests|FullyQualifiedName~SessionStatsStore|FullyQualifiedName~SessionHistoryStore|FullyQualifiedName~ParityFence"`
→ all passed.

## 5. `AccountPanelViewModel`, tested without WPF

**Spec ref:** `spec.md > §3.1` (the class and its four delegates), `§3.2` (the Recent query), `§3.3`
(shared Library, per-account age), `§3.5` (expanded state), `§2.1` (last played is computed here).
**Depends on:** 3, 4. **Effort:** M. **→ C1 halts after this item.**

**What to build:** `ViewModels/AccountPanelViewModel.cs`, created in `WireAccountSummary`
(`MainViewModel.cs:4746`) and exposed as `AccountSummary.Panel`, handed exactly `history`, `library`,
`launch` (bound to `LaunchAccountAsync(owner, target)`) and `requestFacts`
(`AccountFactsService.Enqueue(owner.Id)`). `Recent` as the §3.2 query: this account's rows with a place,
grouped by `PrivateServerId ?? PlaceId`, latest first, `RecentCap = 8`, total worded "in recent
sessions". `Favourites` and `PrivateServers` from `LibraryCatalog.ListAsync` in the Library's order,
decorated with this account's last-played age. `LastPlayed` surfaced to the row's `LastPlayedText`
through the same object so row and panel cannot disagree. `IsExpanded` persisted through
`SetPanelExpandedAsync`, resolved at load (main expanded; no main → the first row; others collapsed) and
set on `CompleteAddAsync` when `Accounts.Count == 1`. `Expand()` calls `requestFacts` only when
`Facts.FetchedAtUtc` is older than 60 s. `RefreshAsync` called from `OnProcessExited` after the history
row is stamped, and the facts enqueue (§2.3 trigger 2) placed there too. `HomeTarget`. `MainViewModel`
gains the wiring and the two hooks and nothing else.

**Acceptance:**
- [x] `AccountPanelViewModelTests`: two accounts that played different games never see each other's rows
      (the PRD's named test); nine distinct games yield eight entries; a private-server row groups by
      server id, not place; an unsaved Recent entry has `IsSaved == false` and its `Save` calls
      `IFavoriteGameStore.AddAsync` with the icon cache's universe id; `Favourites` order equals the
      catalog's order; `Expand()` with facts 30 s old does not call `requestFacts`, 61 s old does.
- [x] `MainViewModelTests`: with a main set, only the main's panel resolves expanded at load; with no
      main, the first row's; a persisted `PanelExpanded = true` survives reload; compact mode on and off
      never calls `SetPanelExpandedAsync`; after Add account on an empty roster the new panel is expanded
      without a click.
- [x] `MainViewModel` grew by delegates, events and hooks only: no new `ObservableCollection` on it, and
      `git diff --stat main -- src/ROROROblox.App/ViewModels/MainViewModel.cs` under 150 lines added for
      items 3-5 combined.
- [x] No XAML in this commit.

*Built: ecbf904 (panel VM), 53c2189 (unsaved private-server Recent groups never launch the public place; every row's last played primed at load from one disk read; open panels request icons only after the backfill, App.xaml.cs:952). 3,168 unit + 27/1 harness green. MainViewModel +104/-5 for items 3-5 measured from 3974579 (item 2's +53 sits before that point; from main the figure is +157). Deviations accepted: three optional ctor dependencies beyond the four delegates, public types, the refresh hook at the end of RecordSessionEndAsync (dashboard h0KDE5em5ZSzwhgLxsPe).*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~AccountPanelViewModelTests|FullyQualifiedName~MainViewModelTests"`
→ all passed. `dotnet test ROROROblox.slnx -c Release --no-build` → 0 failed, 1 skipped.

**C1 — mocks and probes, before a line of XAML.** Two halves, both Este's.

*Mocks.* Through the `626labs-design` skill, render options for: the row with its facts line (a new
line under status versus inline on the status line; collapsing when empty versus always-shown dashes),
the expanded panel (three groups, header with the note and Open Roblox home), the Library in grid and in
list, and the Play as flyout. Este rules on taste from rendered options, not descriptions; the picks are
saved beside this file under `mocks/`, and items 6, 7 and 9 build to them and screenshot-compare before
they close (house rule: match the mock).

*Probes.* With `tools/EndpointProbe`: one account in the captcha state (follow-cancel-follow is the
documented way in), one known-expired cookie and one known-limited account give three triples (status,
`rblx-*` headers, body) for the ticket POST and the presence POST; `validate-membership` runs for one
Plus cookie and, if one exists, one legacy-Premium cookie. Results go to `docs/decisions.md` under a
dated entry and decide, before item 6 paints: whether `NeedsVerification` ships (a 403 with a challenge
type **not** seen on the limited account), whether the membership badge ships (answers for both, or the
fact is dropped), and whether Limited and Follow-marked may ever merge (the spec's answer until measured:
no). **A probe Este cannot run tonight does not block items 6-9:** the enum member stays unset and the
badge stays hidden until the measurement exists, and the gate is re-checked at C2.

## 6. The row shows who this is

**Spec ref:** `spec.md > §2.1` (badges, names, age hint), `§2.4` (the note on the row), `§2.5`
(streamer masking), `§6` (pixel bar), `§11` (`FactBadgeNamingFenceTests`). **Depends on:** 5 and the
C1 mock. **Effort:** M.

**What to build:** The facts line per the C1 pick: five `FactBadgeStyle` `Border`s (Robux, membership,
friends online, last played, the note's first line), each with a value-bearing
`AutomationProperties.Name` from the summary (`Shell_Fact_*_Name`: "Robux, 1,240"; "Robux, unknown" for
the dash; "Robux, hidden" when masked). Robux, membership and friends online read `Shell_Fact_Masked`
("••••") under streamer mode; the note is hidden entirely. `FactsAgeText` at the end of the line
("Updated 3 min ago" / "Updated 2 h ago · last try failed (403)") ticking through
`RefreshRelativeTimes()`, unmasked. `FactBadgeStyle` in `ControlStyles.xaml` at `MetaFontSize` with no
colour of its own. `OnIdentityChanged` raises the new properties and their names. The membership badge
reads **Plus** for any `true`, and is absent entirely if C1 dropped the fact.

**Acceptance:**
- [x] `FactBadgeNamingFenceTests` (new): every element carrying `FactBadgeStyle` sets
      `AutomationProperties.Name`; vacuity floor 5; watched failing first by removing one name.
- [x] `StreamerIdentityProviderTests`: one `[Theory]` row per masked fact (Robux, membership, friends
      online → "••••"; note → empty) and two rows asserting the age hint and last played are **not**
      masked; the harness's `streamer-mode-masks` scenario gains one comment line saying where these live.
- [x] `AccountSummaryTests`: null facts render the dash and the name reads "unknown"; a `LastError` of
      "403" renders the previous Robux beside the failed age hint; facts for id A can only reach row A
      (through `ApplyFacts`).
- [x] `AccountRowCollapsedRenderTests`: a collapsed row with every fact null and no note matches item 1's
      constants exactly at both DPIs; a row with facts populated is taller by exactly the delta the C1
      mock set, pinned as a second constant **(default — confirm on next interactive run: the facts line
      collapses when it has nothing to say, so an upgraded roster looks like v1.35 until the first
      refresh)**.
- [x] `AccessibleNamingFenceTests` floor moved in this commit; `TypeLadderFenceTests` unmoved
      (`MetaFontSize` only); `ThemedStatusColourTests` unmoved.
- [x] Screenshots of the row with facts at 100% and 125%, in all four themes and under flatline,
      compared against the C1 mock and saved under `mocks/compare/`.

*Built: 3997bde + 6ddfaa0. The facts line is a second grid row spanning the name and actions columns (the mock's slimmer actions column hid that one line cannot fit in the 160 px name column); populated row 114/142 px, collapsed still 92/115. AccessibleNamingFenceTests floor did not move: the badges are Borders, which that fence does not scan; FactBadgeNamingFenceTests (floor 5, watched failing) covers them. FactBadgeStyle is excluded from RenderedStyleGateTests with a reason (it paints no fill; its text is the measured text pairs). Membership badge built, collapsed until the probes. Eyes-on Verify (dev build, eight accounts, streamer mode) moves to C2. Suite: 3,195 unit + 1 opt-in skip; harness 27 + 1.*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~FactBadgeNamingFenceTests|FullyQualifiedName~StreamerIdentityProviderTests|FullyQualifiedName~AccountRowCollapsedRenderTests|FullyQualifiedName~AccessibleNamingFenceTests|FullyQualifiedName~TypeLadderFenceTests"`
→ all passed. Dev build, eight accounts, streamer mode on: every Robux badge reads "••••" and no note is
visible.

## 7. The panel opens, and one account is a complete install

**Spec ref:** `spec.md > §3.3` (empty states, Open Roblox home), `§3.4` (launch from the panel), `§3.6`
(XAML placement, the expander), `§3.8` (the one-account window), `§2.4` (note editing in the header),
`§6` (fit bar, one-account visibility). **Depends on:** 6 and the C1 mock. **Effort:** L.
**→ C2 halts after this item.**

**What to build:** The panel as the third child of the row template's outer `StackPanel`, below
`AccountRowBorder`, `Visibility` bound to `Panel.IsExpanded`, so the drag ghost and drop targets still
capture the header only. The expander `ToggleButton` in column 4, styled
`<Style BasedOn="{StaticResource SecondaryToggleButtonStyle}">` (**not** `SecondaryButtonStyle`, which
targets `Button` and throws on a `ToggleButton` at XAML load), `IsChecked="{Binding Panel.IsExpanded}"`,
named from `Shell_Panel_Expander_Name` with `RenderName`, glyph ▸/▾, keyboard-reachable. Three groups
with headings and the three one-line empty states (`Shell_Panel_Empty_*`); `Save` inline on unsaved
Recent entries; the Private badge on server entries; the header with `NoteBox` (`ui:TextBox`,
`MaxLength="200"`, saved on `LostFocus` and Enter, the `AddTagBox` pattern at `MainWindow.xaml:~740`)
and **Open Roblox home** bound to `launch(HomeTarget)`. Every entry's click calls `launch(entry.Target)`
and nothing else. `HasMultipleAccounts` on `MainViewModel`, collapsing `DangerLaunchMultiple`,
`DangerSquadLaunch`, the row's selection dot with its `MainWindow_InBatches` / `MainWindow_Skipped`
label, and the `_FollowMarked` banner clause when there is one account.

**Acceptance:**
- [x] `MainWindowOneAccountFitsTests` (new, `[WindowRenderTheory]` at 96 and 120 DPI): `MainWindow` at
      900×600 with one account and `Panel.IsExpanded = true` → the roster `ScrollViewer`'s
      `ComputedVerticalScrollBarVisibility == Collapsed`; `DangerLaunchMultiple`, `DangerSquadLaunch`
      and the batches dot not visible with one account, visible with two.
- [x] `AccountRowCollapsedRenderTests` still passes with the expander present (it sits on an existing
      line; if the C1 mock put it elsewhere, the constant moves in this commit with the reason).
- [x] `AccessibleNamingFenceTests`: the expander, `NoteBox`, Open Roblox home, every entry and every
      `Save` are named; floor moved in this commit; ceiling stays 1.
- [x] `ButtonRankFenceTests`: no new exemption; the expander passes as a styled `ToggleButton`.
- [x] `MainViewModelTests`: a panel entry click reaches `LaunchAccountAsync(owner, target)` with the
      entry's target and runs `DecideFlaggedLaunch` (a marked account's panel launch becomes a follow; an
      expired account's returns `CookieExpired`); the panel owns no launch code
      (`grep -c "RobloxLauncher" src/ROROROblox.App/ViewModels/AccountPanelViewModel.cs` → 0).
- [x] Drag-reorder with a panel open: the ghost bitmap is the header's size and a drop on an expanded
      row reorders as before (manual, recorded at C2).
- [x] Compact mode hides the expanders and panels and restores them on exit without writing
      `PanelExpanded` (item 5's test still green with the XAML in place).
- [x] Keyboard: Tab reaches the expander, Space toggles it; the panel's entries are in the tab order
      while open and skipped while closed.
- [x] Screenshot compare against the C1 panel mock, saved under `mocks/compare/`.

*Built: dcb387f + f0681a1 + 19e6c87 + the orchestrator's expander fix. B1 three columns below AccountRowBorder; the expander binds its glyph (a Content trigger in a button style broke the house rule, and the test's style check passed vacuously until it required an explicit style). Fit bar: a fresh install and a light panel (3/2/1) fit at 900x600; 4+ Recent entries scroll the roster (363 vs 327), Recent caps at 8: Este's call at C2. Launch-path assertions live in AccountPanelSurfaceTests. Panel launches and Open Roblox home are on the ui-routes deny list (DangerPanelLaunch, DangerOpenRobloxHome). PARTLY, for C2: the drag-reorder walk with a panel open (a test proves the panel is a sibling below the header), the real Tab/Enter/Esc walk, the dev-build Verify walk. Remove already clips at 900 px in v1.35 (15 px with games, 64 without) [corrected 2026-11-02: both figures over-counted by the roster's 24 DIP padding] and the expander adds 32: Este's call at C2. AccessibleNamingFenceTests floor 212 to 217. Suite: 3,239 unit + 3 opt-in skips; harness 27 + 1.*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~MainWindowOneAccountFitsTests|FullyQualifiedName~AccountRowCollapsedRenderTests|FullyQualifiedName~FenceTests"`
→ all passed. Fresh profile (park `%LOCALAPPDATA%\ROROROblox` aside), dev build, Add account → the
row's panel is open, three empty states read, Open Roblox home launches that account: two clicks from
Add to a client.

**C2 — the roster, eyes on it.** (a) The one-account path above at 100% and 125%, in all four themes
and under flatline; nothing above the fold says alts. (b) Eight accounts, two expired, one marked: every
panel collapsed, the roster looks like v1.35 plus a facts line and an expander; expand two panels and
each Recent is its own account's; the marked account in a game still says where it is. (c) Startup: five
cold starts against item 1's baseline, within 10%, recorded in the smoke doc; the log shows no
`AccountFactsApi` line before the first expand or the first tick. (d) Streamer mode on: facts masked,
note gone, game names kept. (e) C1's probe gate re-checked: if the measurements now exist,
`NeedsVerification` and the membership badge are in or out per `decisions.md`. Write it up as it runs.

## 8. The Library: rename, view model, settings

**Spec ref:** `spec.md > §4.1 Rename`, `§4.2 LibraryViewModel` and settings, `§4.5` (what stays in
the code-behind). **Depends on:** 4 (the catalog). Not 7, so this may run while C2 waits.
**Effort:** M.

**What to build:** The rename: `ShellWindow_Games`, `Shell_Title_Games`, `MainWindow_Games`,
`MainWindow_ManageGames` and `GamesPage_Games` change **value** to the Library word in English and all
six catalogs; keys keep their names. `KeyboardVocabulary`'s Ctrl+G display name. The stale
`MainWindow_AddGamesFromToolsGames` hint reworded to point at the Library button.
`Games/LibraryViewModel.cs` with `Visible`, `View`, `Filters`, `Query`, `FromRoblox`, `SubmitAsync`
(paste routing: `LaunchTarget.FromUrl` / `TryParseShareLink` / `RobloxLauncher.ExtractPlaceId` decide; a
game URL or place id → `GetGameMetadataByPlaceIdAsync` → `IFavoriteGameStore.AddAsync`; a private-server
link → the `ResolveShareUrlAsync` delegate → `IPrivateServerStore.AddAsync`; otherwise
`SearchGamesAsync` → `FromRoblox`), `PlayAsync(entry, accountId)` resolving null to main, then the only
account, then the first row; the five management passthroughs. `SettingsBlob` gains
`LibraryView = "grid"` and `LibraryFilters = ""`; two `IAppSettings` pairs; the four fakes
(`MainViewModelTests.cs:1611`, `RobloxLauncherTests.cs:727`, `StreamerIdentityProviderTests.cs:157`,
`Discord/DiscordTestHarness.cs:320`). `App.CreateShellPage` (`App.xaml.cs:1698`) hands the page
`(accountId, target) => MainViewModel.LaunchAccountByIdAsync(accountId, target)`, the four-line wrapper.

**Acceptance:**
- [x] `LibraryViewModelTests`: chips multi-select and persist; none selected shows everything; `Query`
      filters on `Name` and `LocalName`; 100 entries filter under 16 ms; Enter with a local match calls
      nothing on Roblox; Enter with no match fills `FromRoblox`; a place-id paste adds a favourite; a
      share-link paste adds a server through the delegate; **a non-Roblox host, a `javascript:` string or
      an arbitrary sentence adds nothing, launches nothing, and is reported as not understood**;
      `PlayAsync(e, null)` resolves main → only → first row in that order.
- [x] `SettingsReachabilityTests`: `LibraryView` and `LibraryFilters` are on the allow-list **with an
      expiry note naming item 9**, which item 9 deletes when it adds `LibraryViewToggle` and
      `LibraryFiltersRow`. The alerts cycle learned this the slow way; this cycle writes the expiry in.
- [x] `KeyboardVocabularyTests` green with the display-name change; no gesture moved.
- [x] `LocKeyParityFenceTests` and `CultureKeyParityFenceTests` green: the renamed values and the
      reworded hint are in all seven files, regenerated.
- [x] The rail, the shell title and the toolbar button read "Library" in a dev build; Ctrl+G still opens
      it.

*Built: 70936e2. LibraryViewModel, the rename in seven languages (Library/Bibliothek/Biblioteca/Bibliothèque/Biblioteka/Библиотека, the same words item 7's empty states use), two settings allow-listed with an item-9 expiry. Paste routing is gated by LibraryPaste.Classify (http/https, host roblox.com or a subdomain on the IDN form, no userinfo) BEFORE the shipped parsers, because LaunchTarget.FromUrl, TryParseShareLink and ExtractPlaceId ignore the host. The empty games-box hint is now 'Add in the Library' (18 chars, was 28; de longest at 28, was up to 45). LaunchAccountByIdAsync did not exist and was added. The dev-build check (rail, title, toolbar read Library; Ctrl+G) moves to C3. Suite: 3,303 unit + 3 skips; harness 27 + 1. Render-test flake under load found here and reproduced on 364494e, so not item 8's.*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~LibraryViewModelTests|FullyQualifiedName~SettingsReachabilityTests|FullyQualifiedName~KeyboardVocabularyTests|FullyQualifiedName~ParityFence"`
→ all passed. `python scripts/gen-culture-resx.py de` (and the other five) → no orphan-row refusal.

## 9. The Library is a place: grid, list, chips, one box, Play as

**Spec ref:** `spec.md > §4.3 Views, chips, search-and-paste`, `§4.4 Play and Play as`, `§4.5`, `§6`
(the 100-entry bar), `§11` (`UiRoutesSchemaTests`, `NoRawXamlProseFenceTests`). **Depends on:** 8 and
the C1 mock. **Effort:** L. **→ C3 halts after this item.**

**What to build:** `GamesPage.xaml` rewritten to the C1 mock. Grid as a `ListBox` whose `ItemsPanel` is
WPF-UI's `VirtualizingWrapPanel` (pinned 4.3.0; the type is in `Wpf.Ui.dll`), icon-first tiles with
Default and Private **word** badges, last played and total at `MetaFontSize`. List as one row per entry
in the `SessionHistoryPage` shape. Three `SecondaryToggleButtonStyle` chips with a check glyph when
selected. `SearchInput` and `UrlInput` collapse to one `ui:TextBox`. The `GamesPage_EmptyLibrary`
invitation. Play (`CtaButtonStyle`, `AutomationId="DangerPlay"`) and Play as (`SecondaryButtonStyle`,
`DangerPlayAs`) per entry, both ids added to `docs/ui-routes.json`'s `denyAids`.
`Games/AccountPickerFlyout.xaml`: a `ContextMenu` over `MainViewModel.Accounts` with
`AvatarDisplaySource`, `RenderName` and the health word; Expired items disabled with the reason as
tooltip; every `MenuItem` named. The three hardcoded `StringFormat`s and `FavoriteGame.DropdownLabel`'s
suffix become resx keys. The two `MessageBox` confirms become the themed confirm the History page uses.
Code-behind shrinks to wiring, `RenameWindow` and focus-on-load. Item 8's two reachability exemptions
deleted.

**Acceptance:**
- [x] `LibraryGridRenderTests` (new): 100 entries at 96 DPI realise fewer containers than entries; grid
      and list both render the Default and Private words, not fills alone.
- [x] `UiRoutesSchemaTests` green with `DangerPlay` and `DangerPlayAs` in `denyAids`.
- [x] `NoRawXamlProseFenceTests` green: no English `StringFormat` left in `GamesPage.xaml`.
- [x] `SettingsReachabilityTests`: both item 8 exemptions deleted in this commit; keys reachable by
      control name.
- [x] `AccessibleNamingFenceTests` floor moved; ceiling stays 1; every chip, button and the box named;
      every flyout `MenuItem` named although the fence does not scan them yet.
- [x] `ModalDefaultButtonSafetyTests` and `WindowChromeFenceTests` unchanged: the flyout is a
      `ContextMenu`, not a window. `grep -rn "MessageBox" src/ROROROblox.App/Games/` → 0.
- [x] `LibraryViewModelTests` or `MainViewModelTests`: Play as for account B records the session under B
      and B's panel `RefreshAsync` fires on exit; Play as on an expired account is disabled in the picker
      and returns `CookieExpired` if forced through the VM; a marked account launches by follow.
- [x] Streamer mode on: the flyout shows masked names and avatars (one test on the item template's
      bindings, plus eyes at C3).
- [x] Keyboard-only: chips toggle with Space, the box takes focus on load, Enter submits, every entry's
      Play and Play as is reachable, the flyout walks with arrows.
- [x] Screenshot compare against the C1 Library mock (grid, list, empty, flyout) under `mocks/compare/`.

*Built: e2a6ce4. GamesPage rewritten to C1 tiles (ListBox over ui:VirtualizingWrapPanel, whose Orientation axes are inverted: Vertical lays out rows) and D2 Play as (AccountPickerFlyout, a themed ContextMenu; only Expired is disabled). Chip check glyphs are bound TextBlocks, not trigger setters; 13 mutations watched failing. Deviations: no themed confirm existed (History uses a stock MessageBox), so Remove asks in an in-page bar; NoRawXamlProseFenceTests never read StringFormat inside a binding, LibraryPageXamlTests is the real guard; the grid is 4 across at 900 px, not the mock's 6, because the content area is 684 px and German 'Spielen als' needs a 144 px tile (C3 call); AccessibleNamingFenceTests floor unmoved at 217 (13 scanned controls swapped for 13); denyAids 9 to 11 (DangerPlay, DangerPlayAs). For C3: the 100-favourite dev-build walk, the real keyboard walk, the more-button menu, live icons, streamer mode on the real roster, 125%. Suite: 3,340 unit + 5 opt-in skips; harness 27 + 1.*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~LibraryGridRenderTests|FullyQualifiedName~UiRoutesSchemaTests|FullyQualifiedName~NoRawXamlProseFenceTests|FullyQualifiedName~SettingsReachabilityTests|FullyQualifiedName~LibraryViewModelTests"`
→ all passed. Dev build with 100 favourites seeded into `favorites.json`: scroll the grid, type three
letters, no spinner, the filter keeps up with typing.

**C3 — the Library, eyes on it.** Grid and list in all four themes and flatline at 100% and 125%; the
empty Library on a fresh profile; paste a game URL, a place id and a private-server link into the one
box; Play as with eight accounts including two expired and one marked, streamer mode on and off; compare
every view against the C1 mock. Write it up as it runs.

## 10. The tour speaks to one account first

**Spec ref:** `spec.md > §5 The tour`, `§11` (`WindowContentFitsTests` + `WelcomeWindow`).
**Depends on:** 7, 9 (the steps describe the panel and the Library). **Effort:** M.

**What to build:** `About/WelcomeWindow.xaml` restructured, not rewritten: a new hero subtitle key naming
the launcher first (one account, your games, your settings) and side-by-side clients second; steps
`ClickAddAccount` (kept) → `PickOrPlayFromThePanel` (new) → `WhenYouWantAlts` (new); the quick tips that
only matter with several accounts (☰ reorder, ★ main, the batches dot, Follow chips) under
`WelcomeWindow_WithMoreThanOneAccount`; the swatch, Compact, tray and ^ tips stay above it. The five
retired keys (`OpenGamesAndAddThe`, `SearchByNameOrPaste`, `ClickLaunchAsOrLaunch`, `MultiInstanceIsOnBy`,
`RunMultipleRobloxClientsSide`) removed from `Strings.resx` **and** all six catalogs in the same commit,
regenerated. The sentinel (`.welcome-shown`, `ShouldShowOnStartup`) untouched.

**Acceptance:**
- [x] `WindowContentFitsTests` gains `WelcomeWindow` at its own size (760×600) and passes at 96 DPI with
      the new heading.
- [x] `WelcomeSentinelTests` untouched and green.
- [x] `LocKeyParityFenceTests` and `CultureKeyParityFenceTests` green with the retired keys gone from all
      seven files and the new keys in all seven.
- [x] `NoRawXamlProseFenceTests` green; `AccessibleNamingFenceTests` unmoved or moved in this commit.
- [x] Reading order on a fresh profile: launcher, your games, your settings, then side-by-side clients;
      alts are the last step; the multi-account tips sit under their heading.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~WindowContentFitsTests|FullyQualifiedName~WelcomeSentinelTests|FullyQualifiedName~ParityFence|FullyQualifiedName~NoRawXamlProseFenceTests"`
→ all passed.

*Built: 0e0d4cc. Hero, steps and the tips split per spec 5; the four multi-account tips sit under WithMoreThanOneAccount, the swatch, Compact, tray and ^ tips above it. The five retired keys are gone from Strings.resx and all six catalogs (nothing else in src used them), the six new keys added via scripts/add-v136-translations.py (STALE prunes the retired ones) and the six culture resx regenerated. Found while capturing: the step rows were a horizontal StackPanel, so every detail line ran off the right edge unwrapped (step 1 too, in v1.35); each step is now a two-column Grid so details wrap. That made en want 804.7 px against 760, so the window is Height 810 (width 600 and MinHeight 600 unchanged). [Superseded by the orchestrator the same day: back to 760, paid for by tighter spacing, because an 810 window hides Got it on a 768 px laptop; the title space and the tray tip's stray newline fixed alongside.] WindowContentFitsTests gained WelcomeWindow. WelcomeTourTests pins the reading order (watched failing with the heading swapped onto the swatch tip) and carries the opt-in item10-tour-{en,de,ru}-{brand,flatline}-96dpi.png captures: en fits with no scroll; de and ru scroll 43 px (extent 660.8 vs viewport 618.2), the Follow tip below the fold, nothing clipped. No fence moved. Suite: 3,385 unit + 7 skips; harness 27 + 1.*

## 11. Riders in code: #221, #52, the cursor fence, the sheet-parity fence

**Spec ref:** `spec.md > §7.1` (#221 ring), `§7.4` (#52 install identity), `§7.2` (the cursor fence
half of #260), `§7.5` (#232 fence). **Depends on:** nothing; parallel-safe with everything above and
placed here so the halts come first. **Effort:** M.

**What to build:** (a) `ActivityMonitor`: `SamplePeriod = 100 ms`, a `ForegroundHistory = 32` ring of
`(tick, pid)`, `ISystemInputClock.NowTick` in the `GetTickCount` domain; on an advanced input tick credit
the ring entry with the greatest `tick <= LASTINPUTINFO.dwTime`; the `Interlocked` overlap guard stays;
**no hook** (a `WH_KEYBOARD_LL` or Raw Input sink is keylogger-shaped on a Store binary). (b)
`IInstallIdentity` and `InstallChannel { Store, Sideload, DirectDownload, Dev }` in Core;
`App/Distribution/InstallIdentity.cs` composing `AppVersion.Display(mode)` and
`IHostBuildIdentity.InstallFlavour`, the unpackaged case split by `UpdateManager.IsInstalled`;
`DiagnosticsCollector(IInstallIdentity? identity = null)` keeping its assembly fallback;
`DiagnosticsSnapshot.Channel`; the page line; the `Channel` line under `App version` in `snapshot.txt`.
(c) `CursorFenceTests`: scans `src/ROROROblox.App/**` XAML and C# as text, forbids `Cursors.None`,
`Cursor="None"`, `Mouse.OverrideCursor`, `ShowCursor(`, `SetCursor(`, `.cur"`, `.ani"`; allows Hand,
SizeAll, Arrow and null; `CursorSitesFloor = 15` (18 today). (d) `ListingSheetParityFenceTests`: seven
sheets discovered from `docs/store/listing-copy*.md`; the first fenced block after each of three heading
prefixes; equal `• ` bullet count in the long description (14 today), equal non-empty feature lines (20
today), short description ≤ 200 characters; floors 7 / 10 / 15; What's new excluded.

**Acceptance:**
- [x] `ActivityMonitorTests` replay of the 2026-09-29 timeline (fg C at 0, fg B at 300, input at 450, fg
      C at 800, sample at 1000) credits B; **the same test against the pre-change code credits C** (run it
      red first and record the red in the commit message); a second row puts the switch-back inside one
      100 ms window so the ring, not the faster period, is what passes; an account with no input keeps its
      idle age climbing.
- [x] `grep -rn "SetWindowsHookEx\|WH_KEYBOARD_LL\|RegisterRawInputDevices" src/` → 0.
- [x] `ActivitySnapshotProvider` and the `GetAccountActivity` RPC unchanged: no diff under `Plugins/`,
      `RpcMethodCapabilityMapTests` and the harness green.
- [x] `InstallIdentityTests`: the four channels resolve from their inputs; the Diagnostics page and
      `snapshot.txt` show the About page's version string and a channel word; Core with no identity
      injected still answers from the assembly.
- [x] `CursorFenceTests` passes on the tree and fails when a `Cursor="None"` is planted (mutation-checked).
- [x] `ListingSheetParityFenceTests` passes on today's seven sheets and fails when one bullet is deleted
      from one sheet (mutation-checked).

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~ActivityMonitorTests|FullyQualifiedName~InstallIdentityTests|FullyQualifiedName~CursorFenceTests|FullyQualifiedName~ListingSheetParityFenceTests"`
→ all passed. `dotnet test src/ROROROblox.PluginTestHarness/ -c Release --no-build` → 27 passed, 1 skipped.

## 12. Riders on evidence: #260, #222, #231 and its tasks, the groups banner, #230

**Spec ref:** `spec.md > §7.2` (#260 evidence lane), `§7.3` (#222 close or sweep), `§7.6` (#231 closes
on evidence), `§7.9` (account-groups banner), `§7.10` (#230, conditional). **Depends on:** 1 (the #222
day), 11 (the cursor fence exists). **Effort:** S, or M if the #222 sweep is needed.

**What to build:** (a) **#260:** comment on the issue stating what the sweep found (no `None`, no custom
cursor, a fence now in place) and asking for the evidence lane: diagnostics bundle, Windows build, display
scale, cursor scheme and the software cursor-size setting, and whether the cursor shows in another WPF app
(Windows Terminal's settings page is one). If the bundle shows ours, the fix rides this cycle with before
and after; if environmental, answer with the workaround and do **not** touch the Known Roblox issues list.
(b) **#222:** read item 1's smoke-doc rows. At or below one windowless client between launches → close
with the numbers. Otherwise `Tray/IdleTrayClientSweeper` on its **own** 30 s `PeriodicTimer`
(`OrphanedClientSweeper` is 5 s and adopts; nothing existing kills), calling
`StopIdleTrayClients(isTracked, minAge: TimeSpan.FromMinutes(2))`; `RobloxProcessInfo.StartedAt`; the age
floor on `TrayClient.IsIdleTrayClient` with the pre-launch clear passing `minAge: null` so its rule is
unchanged. (c) **#231, tasks `eejy3xvcu` and `g5hjernie`:** `gh run view <v1.35 tag run> --log` for the
`Check plugin catalog drift` step, the output quoted into the three closing comments, both tasks marked
Done on the dashboard. (d) **The banner** atop `docs/superpowers/specs/2026-07-09-account-groups-design.md`:
dated, abandoned, ruled 2026-08-30 in `decisions.md` and `features.md:205`; the panel is not a group.
(e) **#230:** if task `y6u70eghn`'s host fix merged before this item runs, `HostInfo.alert_sound`
additive with `contract_version` unchanged; otherwise the Settings page's Silent hint says what Silent
covers and the release notes name it.

**Acceptance:**
- [x] #260 has the evidence request posted; its lane is recorded in `docs/decisions.md` when the reporter
      answers; the cycle does not wait on the answer.
- [x] #222 is closed with the smoke-doc numbers quoted, **or** `TrayClientTests` has the age rows (under
      two minutes never swept; windowed never swept; tracked never swept; null command line fails closed)
      and the sweeper's timer is its own.
- [x] #231, `eejy3xvcu` and `g5hjernie` are closed with the quoted step output; nothing in
      `plugins-catalog.json` was edited by hand this cycle.
- [x] The banner sits above the existing "Approved-shape" line, dated, pointing at the ruling; nothing
      below it was rewritten.
- [ ] #230 is in (field on `HostInfo`, harness green, `RpcMethodCapabilityMapTests` untouched) or named in
      the Silent hint and the release notes.

**Verify:** `gh issue view 260 --comments | tail -40` shows the request. `gh issue view 231 --json state`
→ CLOSED. `git diff main -- docs/superpowers/specs/2026-07-09-account-groups-design.md` is additive at
the top only.

## 13. Every new string in seven languages

**Spec ref:** `spec.md > §4.1`, `§5`, CLAUDE.md's generated-resx rule, `§11`
(`CultureKeyParityFenceTests`). **Depends on:** 2, 4, 6, 7, 8, 9, 10, 12 (every item that added a key).
**Effort:** M.

> **Revised at build time (2026-11-01, dashboard `dZevmkvgsfVYYF9asVwO`).** `CultureKeyParityFenceTests`
> fails the suite the moment a neutral key is missing from any culture resx, so one sweep here would leave
> items 2 to 12 red. Each key-adding item now writes its keys into all seven files as it lands, through
> the cumulative `scripts/add-v136-translations.py` (created in item 2) and `gen-culture-resx.py`. This
> item shrinks to the verifier run over every key that script holds, the wrap measurement and the German
> build. The first acceptance line is then a re-check, not new work.

**What to build:** The catalog pipeline for the whole cycle's delta, planned here rather than discovered
mid-build as the alerts cycle's 6b was: `python scripts/export-ui-translations.py`, slice
`docs/store/ui-translations.json` to the new and changed keys, translate with the `PRODUCT_NOUNS` guard
(RoRoRo, Roblox, Robux and Plus stay English), write into all six `ui-<culture>.json`,
`python scripts/gen-culture-resx.py <culture>` for de, es, fr, pl, pt-BR and ru, then the translation
verifier on the delta through its MCP, overruling its known rule-3 false positive on quoted UI labels and
fixing everything else.

**Acceptance:**
- [x] `Strings.resx` and all six culture resx hold the same key count; `CultureKeyParityFenceTests` green
      in both directions.
- [x] The translations live in a committed, re-runnable script (`scripts/add-v136-translations.py`), not a
      paste; the generator refuses no orphan rows.
- [x] The verifier run is recorded in the smoke doc with per-language verdicts and each overrule named.
- [x] Label wrap measured, not assumed, the way 6b did it: the new fact names, health words, chip labels
      and tour steps at their declared widths max at two lines in every language.
- [ ] A German dev build (the longest strings) shows the row, the panel, the Library and the tour without
      truncation; screenshots in the smoke doc.
      *(2026-11-03: the real windows were rendered in German by the test harness, `mocks/compare/item13-de-*` and
      `item10-tour-de-*`, with no truncation. A live German dev build has not been looked at yet; it needs the app
      running, so it waits for Este's machine time.)*

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~CultureKeyParityFenceTests|FullyQualifiedName~LocKeyParityFenceTests"`
→ all passed. **Then the PR:** `cycle/v1.36-account-library` → `main`, `ci.yml` green on x64 and arm64,
merged the way this repo merges. Items 14-16 run from `main`.

## 14. vibe-iterate joins the toolchain

**Spec ref:** `spec.md > §7.8`. **Depends on:** 13 (runs on `main` after the PR so a competitive PR
never collides with the cycle branch). **Effort:** M, mostly the run's own.

**What to build:** `/vibe-iterate:bootstrap` with the inputs the spec fixes (`desktop-app`, anchor `.NET
10 WPF`, the `PackageReference` set deduped across the four projects plus `net10.0-windows` as a nuget
pseudo-pin, the four competitor URLs: both Bloxstrap pages, Fishstrap releases, the archived
Roblox-Account-Manager releases). `.vibe-iterate/config.json` committed and validated against the
plugin's `config.schema.json`. One `/vibe-iterate:competitive` run. Its Atlas entry written beside this
file as `atlas-competitive-<date>.md`. Its PR, if any, reviewed like a cycle item and merged or declined
with the reason.

**Acceptance:**
- [ ] `.vibe-iterate/config.json` exists, validates, holds no secret, names all four competitor URLs.
- [ ] One competitive run executed; the Atlas entry names the candidates considered, what was and was not
      copied, and ranks quiet hours, player logs and the FastFlags editor without building them.
- [ ] If the run shipped a PR: reviewed, suite green on both architectures, merged or closed with the
      reason in the Atlas entry. If a measured no-op: the ranked candidate list is in the entry.

**Verify:** `ls .vibe-iterate/config.json docs/cycles/2026-11-01-account-library/atlas-competitive-*.md`
→ both exist. `git log --oneline -3 -- .vibe-iterate/` shows the commit on `main`.

## 15. Release copy: the listing says launcher first

**Spec ref:** `spec.md > §7.7 Listing, support contact, privacy`, `§7.5`. **Depends on:** 13 (the
feature set is final), 11 (the parity fence). **Effort:** M.

**What to build:** `docs/store/listing-copy.md` and the six sheets rewritten: the short description leads
with the launcher (your games, FPS cap, alerts to your phone, themes) and says "alts when you want them"
inside 200 characters; the long description's first paragraph is the one-account story and the
multi-account paragraph loses nothing it claims today; the features list adds the account manager (facts,
health states, notes) and the Library (grid, list, filters, play as); a dated provenance note in each
sheet's blockquote. `docs/store/whats-new-1.36.0.0.md` with the English block carrying "quit RoRoRo before
updating from the Store". `README.md:65`'s Store paragraph gains the same line. `docs/PRIVACY.md`: an
`economy.roblox.com` row and, only if membership shipped, a `premiumfeatures.roblox.com` row;
`friends.roblox.com`'s purpose cell grows "friends online count"; the `accounts.dat` contents cell gains
the cached facts and the note *(v1.36+)*; the TL;DR gains the one PRD line; the Diagnostics-bundle section
rewritten to match `BuildSupportBundle` (a count plus 8-character short ids, up to three redacted logs,
the new `Channel` line). The Partner Center field values for support contact (`support@626labs.dev`
replacing the doubled-t typo) **prepared in the submission packet, not written**. The three-surface
audit (short, long, features against `whats-new-1.36.0.0.md`) and the reviewer letter recorded in
`docs/store/submission-packet-1.36.0.0.md` per the playbook's phase 2.

**Acceptance:**
- [ ] `ListingSheetParityFenceTests` green on the rewritten seven sheets.
- [ ] The listing verifier has reviewed the six translated sheets against the English; verdicts in the
      packet.
- [ ] Every claim on the three surfaces is true of the build on `main`: the audit's table in the packet
      maps claim → where it is in the tree, verified against the tree and never against a changelog.
- [ ] PRIVACY.md's three measured rows from 2026-10-11 are untouched; every new row names its endpoint and
      purpose; "shown, not kept in any plain file" appears once.
- [ ] The packet carries the exact Partner Center field values for support contact and the reviewer
      letter; nothing was written to Partner Center.

**Verify:** `dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter "FullyQualifiedName~ListingSheetParityFenceTests|FullyQualifiedName~ContrastPairGateTests"`
→ passed (the second because it parses a docs file, and a docs edit can fail the suite). Each sheet's
short description measured ≤ 200 characters.

## 16. Documentation, security verification, and the 1.36.0.0 ship

**Spec ref:** `prd.md > What We're Building`, `spec.md > §8 Data model`, `§9 File structure`, `§12 Test
strategy` (the live smoke), `§14 Deployment`, CLAUDE.md > Ship. **Depends on:** everything.
**Effort:** L, most of it waiting on CI, certification and Este's word.

**What to build:**

*Docs.* `docs/architecture.md` corrected: `MainViewModel` 5,497 lines before and the count after,
`MainWindow.xaml` 2,129, the facts lane, the Library join, the §8 data-on-disk rows including
`game-icons.json`. `docs/features.md` gains the 1.36 rows (facts, health states, note, panel, Library,
Play as, tour, #221, #52, the new fences) and `docs/feature-ledger.md` the 1.36 line. `docs/decisions.md`
carries the C1 probe entries, the #260 lane, the #222 ruling, and mirrors every dashboard decision this
cycle logged. The spec's §0 and the three assumption lists re-read; any assumption the build overturned
is marked where it stands.

*Security.* The pre-commit hooks and the CI `guards` job are the floor. Above them: `git log -p main..HEAD`
on the cycle branch grepped for the cookie warning prefix the guards scan for, for `.ROBLOSECURITY=`
followed by anything but an obvious fake, and for hardcoded user-profile paths (the local-path guard's pattern); the `probes/` reports and `mocks/`
re-read for identity (local names are fine; user ids and cookies are not); `game-icons.json` and
`.vibe-iterate/config.json` hold nothing personal; `dotnet list ROROROblox.slnx package --vulnerable
--include-transitive` clean or every finding named with a decision; no new `PackageReference` (spec §13);
the `dpapi-cookie-blast-radius` agent re-run over the final diff of `src/ROROROblox.Core/`; item 8's
rejection cases re-read as the cycle's input-validation surface (the one box is the only place untrusted
text enters); the cert rules unchanged (Store-signed and sideload never the same key; `dev-cert.pfx`
untracked). There is no `.env` in this app: its secrets are cookies in DPAPI, and that is what the pass
checks.

*Smoke.* The §12 live smoke on the clan machine's shape: eight accounts, two expired, one marked, streamer
mode on and off, panels open and closed, Library grid and list, Play as, a follow launch filling Recent;
startup within 10% of item 1's baseline; all in `docs/store/smoke-2026-11-xx-account-library.md`.

*Ship.* `release/v1.36.0.0`: `finalize-store-build.ps1 -Version 1.36.0.0 -IdentityName
626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName
"626Labs LLC"` for x64, then again with `-Architecture arm64` (it syncs csproj and manifest; the fourth
component is 0). PR to `main`, `ci.yml` green on both architectures. Tag `v1.36.0.0` → `release.yml`
drafts the Velopack release and runs the catalog drift gate; **no local `vpk upload`**. Publish the GitHub
release as Latest with real notes. The Store submission through the store-api loop **only on Este's
explicit word in the session**: listing and captions applied before packages per the write-order rule,
the support contact set from item 15's packet, both MSIX uploaded, the reviewer letter attached.
Post-publish: re-read the public catalog for the support email; record the ship in `features.md`, the
ledger, `decisions.md`, the dashboard project version and the cycle memory **in the same session**; draft
the clan Discord post for Este's click.

**Acceptance:**
- [ ] `dotnet build ROROROblox.slnx -c Release` → 0 errors; `dotnet test ROROROblox.slnx -c Release --no-build`
      → every unit and harness test passed, 1 skipped by design; `ci.yml` green on x64 and arm64 on `main`.
- [ ] Every fence constant that moved this cycle moved in the commit that moved it
      (`git log -S"UnnamedCeiling" -S"ScannedFloor" -S"CursorSitesFloor" --oneline main` shows no
      fence-only commits).
- [ ] The secrets grep over the cycle's history returns nothing; the vulnerability listing is clean or
      decided; the blast-radius run has no open finding.
- [ ] README, architecture.md, features.md, feature-ledger.md, PRIVACY.md and decisions.md describe the
      tree on `main`, spot-checked claim by claim.
- [ ] The smoke doc has the live smoke, the startup comparison and the #222 numbers.
- [ ] GitHub release `v1.36.0.0` is Latest and not a draft, with `roblox-compat.json`, `.sig` and
      `plugins-catalog.json` attached by `release.yml`; the Store submission is certified and published, or
      the exact reason it is not is in the packet; the public catalog shows `support@626labs.dev`.
- [ ] The dashboard project version reads 1.36.0.0, the ship decision is logged, and the cycle memory
      says shipped.

**Verify:** `gh release view v1.36.0.0 --json isLatest,isDraft,assets` → `isLatest: true`,
`isDraft: false`, the three signed assets beside the Velopack set. `gh run list --workflow=release.yml -L 1`
→ success. The Store listing page shows 1.36.0.0 and its support link reads `support@626labs.dev`.

---

## What needs a person, and when

| Item | Needs | Who | When |
|---|---|---|---|
| C1 probes | an account in the captcha state; a Plus cookie; a legacy-Premium cookie if one exists | Este | after item 5; a probe not yet run does not block 6-9 |
| C1 mocks | the pick from rendered options | Este | after item 5, before item 6 |
| 1 → 12, the #222 day | ten launches over a day on the current build | Este | starts at item 1, read at item 12 |
| 12, #260 | the reporter's bundle | the reporter, via the issue | not awaited |
| 16, the Store write | the explicit word | Este | the session that submits |
| 16, the clan post | the click | Este | after publish |

## Assumptions this plan adds (each one sentence to overturn)

The PRD's ten and the spec's ten stand. This checklist adds:

21. The facts line collapses when every fact is null and the note is empty, so a v1.35 row survives the
    upgrade to the pixel until the first refresh; the populated row's height delta is whatever the C1
    mock sets.
22. The branch is `cycle/v1.36-account-library`; one PR to `main` after item 13; the release lane runs
    from `main` and `release/v1.36.0.0`.
23. Items 2, 4 and 11 may run in parallel with their neighbours; the three halts are the only ordering a
    person feels.
24. A C1 probe that cannot be run the night the build reaches it does not hold the build: the code is
    shaped so an unmeasured state never sets, and the gate is re-checked at C2.
25. The probe is a Core-referencing console tool under `tools/`, not a script with a cookie in a shell.
26. The catalog sweep is one planned item (13) after the string-adding items, not per-item regeneration,
    because the generator refuses orphan rows and one sweep is one refusal to handle.
27. vibe-iterate runs on `main` after the cycle PR merges, so a competitive PR never collides with the
    cycle branch.
28. The build runs from an Opus session with Sonnet workers; this plan was written on Fable 5.1 and
    makes no claim about where a worker will struggle.
