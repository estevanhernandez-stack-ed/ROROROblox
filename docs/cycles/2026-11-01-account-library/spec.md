<!-- Cart cycle #6, step 4. Inputs: scope.md and prd.md beside this file. Written here rather than
     docs/spec.md, which holds the retired v1.18/v1.21 snapshot CLAUDE.md treats as history.
     Autonomous run (profile autonomy_level: fully-autonomous, builder: self). Every beat the record did
     not answer is marked "(default — confirm on next interactive run)" and collected at the end. -->

# RoRoRo: the account manager and the Library — Technical Spec

Against `main` at `5a8fe61` (v1.35.0.0 shipped). Every file:line below was read this session by three
recon sweeps of the tree, not recalled; where a sweep's finding contradicted the PRD, the tree won and
the correction is listed in §0. Release target: **v1.36.0.0** (default — confirm on next interactive
run; the tag is set at release). Model note: this spec ran on a Fable 5.1 session; the era map says
`/build` and the release run from an Opus session with Sonnet workers.

## The shape of the change, in one paragraph

Three stores already hold everything the panel and the Library show: `accounts.dat` (who),
`session-history.json` plus `session-stats.json` (where each account has been and for how long), and
`favorites.json` plus `private-servers.json` (the shared Library). The main window today renders
exactly one of them. This cycle adds one new network lane (account facts: three legacy cookie
endpoints, one account at a time, never at startup), one per-account view model that joins the three
stores into a panel under the row, and one Library view model that joins two of them into a page you
browse. Nothing moves in the launch pipeline: every new click lands on
`MainViewModel.LaunchAccountAsync(summary, target)` (`MainViewModel.cs:1788`) with a target, exactly
as the row's Launch does, so every guard that exists today is inherited rather than re-implemented.
Everything else is a rider on its own surface.

---

## 0. PRD corrections found while reading the tree

The PRD was written from the scope and the features map. Reading the code changed eight things.

| PRD said | Tree says | Spec does |
|---|---|---|
| Play as reuses "the same account picker Squad Launch uses" | `SquadLaunch/SquadLaunchWindow.xaml` picks a **target**; which accounts go is each row's `IsSelected` dot. There is no account picker anywhere; the nearest list is `Transport/ExportAccountsWindow.xaml`'s checkbox rows. | One new flyout (§4.4). |
| #231: build a tag-time catalog drift gate; fix the publisher | Both landed 2026-10-19 (`86b0670`) and 2026-10-23 (`129e408`): `scripts/check-plugin-catalog-drift.ps1`, wired at `release.yml:88-94`, ran green on the v1.34.0.0 and v1.35.0.0 tag runs; all five catalog rows read `626Labs LLC`. | Rider closes on evidence (§8.6). Tasks `g5hjernie`, `eejy3xvcu` and #231 close. |
| #222 sweep rides "the existing 30 s tick" | No 30 s timer touches strays. `Tray/OrphanedClientSweeper` ticks every 5 s and **adopts**, it never kills. The v1.35 pre-launch clear (`MainViewModel.cs:1947-1955` → `RobloxInstanceStopper.StopIdleTrayClients`) has **no age rule**; its rule is `--launch-to-tray` + no window + untracked (`Core/Diagnostics/TrayClient.cs`). | Conditional sweeper gets its own timer and an age floor (§8.3). |
| Membership is "Premium or none" | Roblox Plus replaced Premium on 2026-04-30. The only documented membership endpoint is marked hidden, "planned for deprecation on 4/30 (Roblox Plus launch)". | Membership ships only on a measured answer (§2.2). Logged to the dashboard 2026-11-01. |
| Recent counts games an account joined by follow | `RecordSessionStartAsync` (`MainViewModel.cs:3258`) writes `GameName = "Following a friend"`, `PlaceId = null` for `LaunchTarget.FollowFriend`; `ISessionHistoryStore` has no way to update a row's game. A follow-marked account, the user the panel exists for, would have an empty Recent. | History gains one patch method; presence fills the row (§3.7). |
| (implicit) rows know their `RobloxUserId` | `AccountUserIdBackfillService` writes the store only; nothing pushes the id into the live `AccountSummary`. The comment at `MainViewModel.cs:1296-1300` saying backfilled accounts "enter the poll snapshot automatically" is wrong. | Backfill raises an event the VM applies (§2.6). |
| Diagnostics "shows the version the About page shows" | `Core/Diagnostics/DiagnosticsCollector.cs` reads the assembly; the helper About uses (`App/Distribution/AppVersion.cs`) is App-internal and Core cannot reference it. | Inject an identity (§8.4). |
| `MainViewModel` is "~4,300 lines" (architecture.md) | 5,497 lines. `MainWindow.xaml` is 2,129. `AccountSummary.cs` is 896. | Numbers corrected in architecture.md in the docs commit; the "new class, not MainViewModel" rule gets more load-bearing, not less. *(2026-11-05: at v1.36 they are 5,687 and 2,585; architecture.md is corrected.)* |
| Play as "obeys every launch rule the row's Launch obeys: the idle tray-client clear, the install deferral gate, the flagged-launch ask" | The install-deferral gate is batch-only: `PreWarmGate.Decide` is reached only from `DispatchBatchAsync` (`MainViewModel.cs:2543-2555`); the row's own `LaunchAccountCommand` (`:300`) never runs it. | Play as shares the row's path and so obeys the two rules the row has; the gate is named as not one of them (§3.4) and stays batch-only. |
| "Health beats presence in the row's secondary line, as Expired does today" for every state | Nothing in the tree; this is a design call. A marked account in a game is the thing the clan user is watching. | Overridden for **Follow-marked only** (§1.1): permanent badge on the name line, secondary line only at rest. Default, assumption 12, Este's to reverse. |

---

## 1. Health: vocabulary, signal, and the follow mark

Implements `prd.md > Health states you can act on`. Gates everything in §2 and §3 that paints a state.

### 1.1 The state set

A new `AccountHealth` enum in `src/ROROROblox.App/ViewModels/AccountHealth.cs`:

```csharp
internal enum AccountHealth { Ok, FollowMarked, Limited, NeedsVerification, Expired }
```

`AccountSummary` gains a computed `Health` (first match wins): `SessionExpired` → `Expired`;
`SessionNeedsVerification` (new runtime flag, §1.2) → `NeedsVerification`; `SessionLimited` →
`Limited`; `JoinViaFriend` → `FollowMarked`; else `Ok`. `NeedsVerification` is compiled in but **only
ever set from the signal in §1.2**; if build item 1 finds no distinct signal, the flag is never set,
the enum member stays (an enum member with no setter costs nothing and keeps the ladder stable), and
the drop is recorded in `docs/decisions.md`.

Each state carries a word, a glyph and an action, so flatline can tell them apart with colour removed:

| State | Word (`Shell_Health_*` key) | Glyph | Brush (all `DynamicResource`, derived from existing slots) | Action |
|---|---|---|---|---|
| Expired | Session expired | ⚠ | `RowExpiredAccentBrush` (as today) | Re-authenticate (`ReauthenticateCommand`, exists) |
| Needs verification | Needs verification | ⚿ | `HealthVerifyBrush` (new derived key) | Open Roblox for this account (`OpenRobloxHomeCommand`, new: `LaunchAccountAsync(summary, LaunchTarget.Home)`) |
| Limited | Limited by Roblox | ⏳ | `MagentaBrush` (as today) | Re-authenticate (as today) |
| Follow-marked | Joins by following | ⇢ | `CyanBrush` | Toggle the mark (`ToggleJoinViaFriendCommand`, exists) |

No new theme slot; **one** derived key, `HealthVerifyBrush`, seeded beside `OnMagentaBrush` in
`App.xaml:44-90` and derived in `ThemeService.ApplySlot` as the midpoint of `RowExpiredAccent` and
`Magenta`, so four states are four colours from three slots and the PRD's "distinct in word, glyph and
colour" holds for all of them; `ContrastPairGateTests` checks it against Bg and RowBg like every derived
brush. The glyphs are plain Unicode in `Segoe UI Symbol` territory, not colour emoji; the v1.33
colour-emoji rider was dropped and this spec does not revive it.

**Where health shows.** `StatusDot` (`AccountSummary.cs:742`) gains the two new rungs.
`SecondaryStatusText` (`:757-834`) gets `NeedsVerification` inserted between Expired and Limited.
**Follow-marked is the one state that does not beat presence** (default — confirm on next interactive
run): a marked account that is in a game still reads "In Pet Sim 99 · 2 h", because that is the fact
the clan user is watching; the mark shows as a persistent badge on the name line and takes the
secondary line only when the account is not running (rung 9, "Ready", becomes "Joins by following ·
Ready"). Expired, Needs verification and Limited beat presence exactly as Expired does today.

**Where the action sits.** The health badge is a `Button` (`GhostButtonStyle`, named
"`<word>`, `<action>`" for the naming fence) whose command is the row in the table. The context menu
keeps `ToggleJoinViaFriendCommand` as it is (`MainWindow.xaml:248-359`).

### 1.2 The signal: what build item 1 measures

Today `RobloxApi` maps 401 → `CookieExpiredException` and 403 → `SessionLimitedException` on the
auth-ticket POST and on presence (`Core/RobloxApi.cs`), and `PresenceService` flips `SessionLimited`
after three consecutive 403s (`LimitedFlipThreshold = 3`). Nothing reads the 403's headers.

Roblox sends challenge headers on a 403 that wants a step-up, not a plain refusal:
`rblx-challenge-id`, `rblx-challenge-type`, `rblx-challenge-metadata` (base64 JSON). Community clients
enumerate `twostepverification`, `captcha`, `reauthentication`, `security-questions`, `proofofwork`,
`chef`, `generic` as the type values ([roblox-api challenge.rs](https://docs.rs/crate/roblox-api/0.1.8/source/src/challenge.rs),
[RoZod security features](https://www.mintlify.com/AlroviOfficial/RoZod/auth/security-features)). Roblox
documents none of this; it is a measured contract or nothing.

**Candidate signal:** a 403 on `POST auth.roblox.com/v1/authentication-ticket` **with**
`rblx-challenge-type` present = Needs verification (type recorded); a 403 **without** the header =
Limited (today's meaning); 401 = Expired. `SessionLimitedException` gains `string? ChallengeType`
(read from the header, null when absent). `RobloxLauncher` maps it to a new `LaunchResult.NeedsVerification(type)`;
`LaunchAccountAsync` sets `SessionNeedsVerification = true` on it, and the capture window's existing
re-auth wording (`CookieCaptureWindow`) is the recovery path, reached through "Open Roblox for this
account" (home launch, where the client itself presents the challenge) rather than cookie recapture.

**Build item 1, the experiment,** before any state is called done: put one account into the captcha
state (the clan's follow-cancel-follow workaround is the documented way in), then record for the
ticket POST and the presence POST: status, every `rblx-*` header, and the body. Record the same for a
known-expired cookie and for a known-limited account. The three triples go to `docs/decisions.md` under
a dated entry and decide:

- whether `NeedsVerification` ships (a 403 with a challenge type that is **not** seen on the limited
  account), and
- whether Limited and Follow-marked may ever merge (task `g5fgibvy7`'s unmeasured equivalence). This
  spec's answer until measured: **no**, a 403 never auto-marks.

`RobloxApiTests` pins the header read in both directions (present → type, absent → null).

### 1.3 Launch all and Launch multiple summary

`LaunchEligibilityResult` (`ViewModels/LaunchEligibility.cs`) already builds the banner from clauses
(`Shell_Launch_Clause_Running`, `_Expired`, `_Limited`, `_Deselected`). It gains a `_FollowMarked`
clause with a count that reads as a sentence (`Shell_Launch_Clause_FollowMarked`, plural: "2 will follow
the main"), and `SquadLaunchPlan.Build` (`ViewModels/SquadLaunchPlan.cs`), which already splits the
batch by `JoinViaFriend`, supplies the count. No behaviour change: marked accounts launch by follow
when the main is joinable and are held with a reason when it is not (`Core/FlaggedLaunchRule.cs`),
which is today's rule, now worded.

---

## 2. Account facts

Implements `prd.md > The row knows who this is` and the cost stories. Build item 2.

### 2.1 The record and where it lives

```csharp
// src/ROROROblox.Core/AccountFacts.cs
public sealed record AccountFacts(
    long? Robux,
    string? Membership,          // "plus" | "premium" | "none" | null = unknown; see §2.2
    int? FriendsOnline,
    DateTimeOffset? FetchedAtUtc,
    string? LastError);          // short, non-secret: "401", "403", "timeout"; shown as the age hint's reason
```

`Account` (`Core/Account.cs`) gains two appended optional parameters: `AccountFacts? Facts = null` and
`string? Note = null`. `StoredAccount` (`AccountStore.cs:767-790`) gains the same two, so facts and
the note live **inside the DPAPI blob** beside the cookie, which is the PRD's default and the only
place Robux may be cached (default — confirm on next interactive run). Nothing new on disk. A legacy
blob loads both as null (no migration, as for every field since `LocalName`); `AccountFactsSchemaTests`
is a copy of `LocalNameSchemaTests` (round-trip, null omitted, legacy loads null).

Touch points for the two fields, from the sweep: the `Account` record, `StoredAccount`, the positional
projection in `ListAsync` (`AccountStore.cs:58`), `AddAsync`'s `new Account(...)` (`:101`), two new
setters on `IAccountStore` (`SetFactsAsync(Guid, AccountFacts)` and `UpdateNoteAsync(Guid, string?)`,
both in the silent-no-op-on-unknown-id family of `SetJoinViaFriendAsync`; a third, `SetPanelExpandedAsync`,
arrives with §3.5), and the five `IAccountStore` fakes: `AccountUserIdBackfillServiceTests.cs:195`, `PresenceServiceTests.cs:701`,
`MainViewModelTests.cs:914` and `:946`, `AutoRejoinWiringTests.cs:1426`.

`AccountSummary` seeds `Facts` and `Note` in its constructor (`:46-67`) and exposes them as notifying
properties; the row binds `RobuxText`, `MembershipText`, `FriendsOnlineText`, `LastPlayedText`,
`NoteFirstLine`, each returning the dash placeholder when the fact is null and the masked placeholder
under streamer mode (§2.5). Nothing on `AccountSummary` ever holds a value from a different account:
facts are set only through `ApplyFacts(Guid, AccountFacts)` keyed by id, the same shape as
`ApplyPresence`.

**The badges and their names.** Each fact renders as a `Border` with `Style="{StaticResource FactBadgeStyle}"`
(new, in `ControlStyles.xaml`, `MetaFontSize`, no colour of its own) wrapping one `TextBlock`, on a
facts line under the status line. Every badge sets `AutomationProperties.Name` to a value-bearing
string the summary builds, `RobuxAccessibleName` → `Loc.Format("Shell_Fact_Robux_Name", value)` →
"Robux, 1,240", and so on for the five; the dash and the masked placeholder read as "Robux, unknown"
and "Robux, hidden". `AccessibleNamingFenceTests` does not scan `TextBlock`, so a small new
`FactBadgeNamingFenceTests` asserts that every element carrying `FactBadgeStyle` sets
`AutomationProperties.Name`, vacuity floor 5.

**The age hint** is one more `MetaFontSize` `TextBlock` at the end of the facts line, bound to
`FactsAgeText`: "Updated 3 min ago" (`Shell_Fact_Updated`) from `Facts.FetchedAtUtc`, or "Updated 2 h
ago · last try failed (403)" (`Shell_Fact_UpdatedFailed`) when `LastError` is set, so a failed request
leaves the previous values standing with the reason beside them. It ticks through the existing
`RefreshRelativeTimes()` and is **not** masked under streamer mode, because an age identifies nobody.

**Last played** is local and never fetched: the account's most recent `LaunchSession` with a place
(`PlaceId != null || IsPrivateServer`) from `ISessionHistoryStore.ListAsync()`, name from the Library's
`LocalName` when the place is saved, else the row's `GameName`, age from `LaunchedAtUtc`. It is computed
by `AccountPanelViewModel` (§3.2) and surfaced on the row through the same object, so the row and the
panel cannot disagree.

### 2.2 The typed client

`IRobloxApi` has nine test fakes (`AccountUserIdBackfillServiceTests.cs:238`,
`CookieCaptureSweepTests.cs:93`, `Discord/DiscordTestHarness.cs:296`, `JoinByLinkSaveTests.cs:199`,
`MainViewModelTests.cs:973` and `:989`, `PresenceServiceTests.cs:741`, `RobloxLauncherTests.cs:708`,
`Rendering/HistoryRowRenderTests.cs:234`) that stop compiling when the interface grows. The facts
endpoints therefore get their own interface, so the cost of a new fake lands in one test file:

```csharp
// src/ROROROblox.Core/IAccountFactsApi.cs
public interface IAccountFactsApi
{
    Task<long?> GetRobuxAsync(string cookie, CancellationToken ct);                         // GET economy.roblox.com/v1/user/currency
    Task<int?>  GetFriendsOnlineCountAsync(string cookie, long userId, CancellationToken ct); // GET friends.roblox.com/v1/users/{userId}/friends/online → data.Length
    Task<MembershipProbe> GetMembershipAsync(string cookie, long userId, CancellationToken ct); // GET premiumfeatures.roblox.com/v1/users/{userId}/validate-membership
}
```

`AccountFactsApi` (`Core/AccountFactsApi.cs`) has **exactly one constructor**,
`(HttpClient http, ILogger<AccountFactsApi> log)`, attaches the cookie per request as
`Cookie: .ROBLOSECURITY=<cookie>` exactly as `RobloxApi` does, and is registered beside it at
`App.xaml.cs:1110-1119` with the same `RORORO/<version>` User-Agent lambda and the same
`SocketsHttpHandler { UseCookies = false }` primary handler (the load-bearing line; a shared handler
factory is **not** introduced, because the rule is easier to see duplicated than hidden). One
`InlineData` plus one registration line in `TypedHttpClientRegistrationTests.cs`.

**Endpoints, as documented by Roblox** (all `type: legacy`, cookie auth, "can incorporate breaking
changes without notice", no stated rate limits):

| Fact | Endpoint | Response | Doc |
|---|---|---|---|
| Robux | `GET https://economy.roblox.com/v1/user/currency` | `{ "robux": 0 }` (int64); 401 denied, 403 "The user is invalid". The per-userId form does **not** exist in the Swagger; the authenticated-user form is exactly right for a per-account cookie. | [Economy v1](https://create.roblox.com/docs/en-us/cloud/reference/domains/economy.md) |
| Friends online | `GET https://friends.roblox.com/v1/users/{userId}/friends/online` | `{ "data": [ UserPresenceResponse… ] }`; the count is `data.Length`. `/v1/my/friends/count` is **total** friends, not online, and is not used. | [Friends v1](https://create.roblox.com/docs/cloud/legacy/friends/v1) |
| Membership | `GET https://premiumfeatures.roblox.com/v1/users/{userId}/validate-membership` | 200 with an undocumented body (historically a bare `true`/`false`); marked hidden, "planned for deprecation on 4/30 (Roblox Plus launch)". | [PremiumFeatures v1](https://create.roblox.com/docs/en-us/cloud/reference/domains/premiumfeatures.md) |

**Membership is gated** the way Needs verification is: build item 2 opens by calling validate-membership
with one Plus cookie and one legacy-Premium cookie and recording status and body in `docs/decisions.md`.
`MembershipProbe` carries `(int Status, string? Body)` so the measurement is the client's own output.
Ships as a badge reading **Plus** or **Premium** only if the endpoint answers for both; if it answers
for neither, the fact is dropped from the row and the privacy line, the enum value stays null for ever,
and the drop is recorded. The PRD's "Premium or none" wording is already stale; the product is Plus.

**Who maps the probe.** `AccountFactsService` owns the mapping, because the client returns a
measurement and the record holds a fact: 200 with body `true` → `"plus"`; 200 with `false` → `"none"`;
anything else → `null` with `LastError` set (a 403 is unknown, not "none"). The endpoint has historically
answered a bare boolean and cannot tell Plus from legacy Premium, and Premium has been closed to new
members since 2026-04-30 and lost its badge on 2026-05-30, so **the badge word is Plus for any `true`**
(default — confirm on next interactive run); if the probe shows the endpoint answers `false` for a known
Plus account, the fact is dropped under the gate above rather than shown wrong.

> **Superseded 2026-11-02** (`docs/decisions.md`, "A membership failure only ever costs the badge"): no membership failure sets `LastError` or abandons the refresh, a 401 or 403 included. A transient one keeps the last known badge; a definitive one drops it to unknown.

A 401 or 403 from any of the three is recorded in `LastError` and shown as the age hint's reason. **It
never sets `SessionExpired` or `SessionLimited`**: `PresenceService` stays the only authority on health
(the PRD's "never flips the account's health state on its own"), and a facts failure is one more reason
to let presence decide, not a second judge.

### 2.3 The service and its triggers

`AccountFactsService` (`src/ROROROblox.Core/AccountFactsService.cs`), a singleton registered beside
`AccountUserIdBackfillService` (`App.xaml.cs:1290`), owns **one worker** over a bounded
`Channel<Guid>` with de-duplication (an account already queued is not queued twice) and the pacing
constant the backfill lane already uses, lifted into a tiny shared static so the two services read one
number:

```csharp
// src/ROROROblox.Core/BackfillPacing.cs
public static class BackfillPacing
{
    public const int InterAccountDelayMs = 2500;
    public const int JitterMs = 500;              // Random.Shared.Next(-JitterMs, JitterMs), as AccountUserIdBackfillService.cs:102-106 does today
}
```

`AccountUserIdBackfillService`'s default parameter switches to the constant; its behaviour does not
change. The worker waits the jittered delay **between** consecutive accounts, so two accounts' facts are
never requested in the same second; one refresh is up to three sequential calls for one account, and
"one account at a time" means the three calls complete before the next account starts.

Three enqueue triggers, and no others:

1. **Panel expand** (`AccountPanelViewModel.Expand()`), skipped when `Facts.FetchedAtUtc` is younger
   than `MinRefreshAge = 60 s` (default — confirm on next interactive run).
2. **Launch end**: `MainViewModel.OnProcessExited` (the branch that stamps the history row's end),
   because the thing most likely to have changed is Robux and last played.
3. **The slow timer**: one `PeriodicTimer` at `TimerTick = 60 s`; each tick enqueues **at most one**
   account, the one with the oldest `FetchedAtUtc` that is older than `TimerRefreshAge = 10 min`
   (default — confirm on next interactive run), skipping expired accounts (no cookie to spend) and
   accounts with `RobloxUserId == null` (friends-online cannot be asked; Robux still can, so those get
   Robux only). With eight accounts every account refreshes about every ten minutes; with more than
   ten, the cadence stretches to N minutes per account rather than exceeding one request per minute,
   which is the honest ceiling.

**Startup rule, enforced in two places.** The timer's first tick is scheduled only after
`AccountUserIdBackfillService.RunAsync` completes inside `RunStartupChecksAsync` (`App.xaml.cs:858`),
plus one full `TimerTick`, so the first fact request is no sooner than the backfill's start plus its
run plus a minute; and the service exposes `RequestCount` so `AccountFactsServiceTests` can assert
**zero** requests after construction, start, and the load of eight accounts, until an expand or a tick.
The v1.1.2 lesson at `App.xaml.cs:899-914` (a startup burst of `users/authenticated` tripped anti-fraud)
is quoted in the service's class comment, because it is the easiest mistake in this cycle.

Each refresh: `RetrieveCookieAsync` → the three calls → `IAccountStore.SetFactsAsync` → raise
`AccountFactsUpdated(Guid, AccountFacts)`; `MainViewModel` subscribes in the constructor beside the
presence events (`:361-363`) and marshals through `_ui.Invoke` to `ApplyFacts`. A write to
`accounts.dat` per refresh is one DPAPI protect of a few kilobytes through the existing gated
tmp-then-`File.Move` path; at steady state with eight accounts that is one write a minute or so, and a
torn write loses nothing by construction.

### 2.4 The note

`Account.Note`, free text, `MaxNoteLength = 200` (default — confirm on next interactive run), trimmed,
empty becomes null, through `IAccountStore.UpdateNoteAsync`. Edited inline in the panel header
(`NoteBox`, a `ui:TextBox` with `MaxLength="200"`, saved on `LostFocus` and on Enter, the pattern of
`AddTagBox` at `MainWindow.xaml:~740`); the row shows `NoteFirstLine` (text up to the first newline,
ellipsised by the TextBlock) and the panel shows all of it. It **travels in export and import**: an
appended optional `string? Note = null` on `AccountExportRecord`
(`Core/Transport/AccountExportRecord.cs`), mapped in `ExportAccountsAsync` (`AccountStore.cs:564-624`)
and `ImportMergeAsync` (`:626-689`), the exact path `Tags` and `LocalName` take; the format version
stays 1 because a missing field reads as null on either side. `AccountStoreTransportTests` gains a
round-trip. Facts do **not** travel: they are a cache, and `AccountExportRecord_DoesNotCarryBrowserTrackerId`
(`AccountStoreTransportTests.cs:57`) gets a sibling asserting the record carries no `Facts`.

### 2.5 Streamer masking

Streamer mode masks names and avatars only (`AccountSummary.RenderName` `:115`, `AvatarDisplaySource`
`:124`, both through `IStreamerIdentityProvider.ForAccount`). The new facts join the same switch, in the
same place: `RobuxText`, `MembershipText` and `FriendsOnlineText` return a masked placeholder
(`Shell_Fact_Masked`, "••••") when `_identity.IsActive`, and `NoteFirstLine` and the panel's note return
empty (hidden entirely: default — confirm on next interactive run). `OnIdentityChanged` raises the five
new properties and their accessible names beside `RenderName`; `FactsAgeText` and `LastPlayedText` are
not masked (an age and a public game name identify nobody). Recent and last played keep game names (public) and show
`RenderName` wherever an account is named, which they get for free by binding the summary. The Play as
flyout (§4.4) binds `RenderName` and `AvatarDisplaySource`, so it is masked by construction.

The smoke harness's streamer scenario (`tools/MetricSmoke/Scenarios.cs:716`, `streamer-mode-masks`)
asserts on webhook bodies and never looks at rows, so the PRD's "one assertion per new fact" lands in
`StreamerIdentityProviderTests` (the unit seam that already covers `RenderName`) as one `[Theory]` row
per fact, not in the harness; the harness gets one line saying so.

### 2.6 The backfill gap

`AccountUserIdBackfillService` raises a new `AccountUserIdResolved(Guid, long)` event after each
`UpdateRobloxUserIdAsync`; `MainViewModel` applies it to the live row, which also means
`PresenceService`'s target delegate (`App.xaml.cs:1244-1255`, filters `RobloxUserId is > 0` from
`AccountsSnapshot`) starts polling a legacy account in the same session instead of after a restart.
The stale comment at `MainViewModel.cs:1296-1300` is corrected in the same commit. One test:
`MainViewModelTests` asserts the row's `RobloxUserId` after the event.

---

## 3. The panel

Implements `prd.md > The panel: where this account has been` and the one-account story's panel half.
Build item 3.

### 3.1 `AccountPanelViewModel`

`src/ROROROblox.App/ViewModels/AccountPanelViewModel.cs`, one per `AccountSummary`, created in
`WireAccountSummary` (`MainViewModel.cs:4746`) and exposed as `AccountSummary.Panel`. It is the only new
code `MainViewModel` learns about: four delegates it is handed at construction, and nothing else.

```csharp
internal sealed class AccountPanelViewModel : INotifyPropertyChanged
{
    public AccountPanelViewModel(AccountSummary owner,
        Func<Task<IReadOnlyList<LaunchSession>>> history,        // ISessionHistoryStore.ListAsync
        Func<Task<IReadOnlyList<LibraryEntry>>> library,         // LibraryCatalog.ListAsync, §4.2
        Func<LaunchTarget, Task> launch,                         // bound by MainViewModel to LaunchAccountAsync(owner, target)
        Func<Task> requestFacts);                                // AccountFactsService.Enqueue(owner.Id)

    public bool IsExpanded { get; set; }                         // persisted per account, §3.5
    public ObservableCollection<PanelEntry> Recent { get; }
    public ObservableCollection<PanelEntry> Favourites { get; }
    public ObservableCollection<PanelEntry> PrivateServers { get; }
    public LaunchTarget HomeTarget => LaunchTarget.Home;          // the one-account "play within two clicks" affordance
    public Task RefreshAsync();                                   // re-queries history + library; called on expand and after a session ends
}

internal sealed record PanelEntry(LibraryEntry Entry, DateTimeOffset? LastPlayedUtc, TimeSpan? TotalPlayed, bool IsSaved);
```

`MainViewModel` does not grow a Recent, a Favourites or a PrivateServers of its own; the scope's "do
not grow `MainViewModel` to carry the panel" is honoured by giving the panel exactly four delegates
and one event hook (`OnProcessExited` calls `summary.Panel.RefreshAsync()` after the history row is
stamped).

### 3.2 Recent: the query

Recent is a query over `session-history.json`, not a store. Over `ISessionHistoryStore.ListAsync()`
(rolling `SessionHistoryStore.MaxRows = 100`): rows where `AccountId == owner.Id` and the row has a
place; grouped by `PrivateServerId ?? PlaceId`; ordered by the group's latest `LaunchedAtUtc`; the first
`RecentCap = 8` groups (default — confirm on next interactive run). Per entry: icon and names from the
Library when the place is saved (`LibraryEntry`), else the row's `GameName` and a lazily fetched icon
(§4.2); last played is the group's latest `LaunchedAtUtc`; total played is the sum of `Duration` over
the group's rows, worded "in recent sessions" because the window is 100 rows, not all time. A
`Save` affordance on an unsaved entry calls `IFavoriteGameStore.AddAsync(placeId, universeId, name,
iconUrl)`, which is why the lazy icon fetch also fetches the universe id.

`LaunchSession` gains an appended optional `Guid? PrivateServerId = null`, stamped by
`RecordSessionStartAsync` when the target came from a saved server, so a private-server Recent entry
can name the server rather than guess from the place; rows written before the field read as null and
fall back to "Private server · `<place>`". Schema-additive, like every history field before it.

"The panel never shows another account's history" is one test with two accounts that played different
games (`AccountPanelViewModelTests`), as the PRD asks.

### 3.3 Favourites and Private servers: the shared Library, per-account age

The panel's Favourites and Private servers are `LibraryCatalog.ListAsync()` (§4.2) filtered by kind, in
the Library's order, each decorated with **this account's** last-played age from the same history
query. They are not a second list and have no store of their own, which kills the sync-bug class before
it exists. Empty states are three one-liners (`Shell_Panel_Empty_Recent`, `_Favourites`,
`_PrivateServers`) saying what to do, and the panel header carries **Open Roblox home** bound to
`launch(HomeTarget)` so a one-account install can play within two clicks of Add account.

### 3.4 Launch from the panel

Every entry's click calls `launch(entry.Entry.Target)`, which `MainViewModel` bound to
`LaunchAccountAsync(owner, target)`. That path runs, in order, the pending-relaunch clear, target
resolution, `DecideFlaggedLaunch` (join-via-friend and main-not-joinable, `:2034`), the cookie read,
`AppStorageDefender`, the idle tray-client clear (`:1947-1955`) and the launch; expired surfaces as
`LaunchResult.CookieExpired` as it does for the row's own button. **Nothing in the panel bypasses a
launch rule because nothing in the panel has its own launch code.** The install-deferral gate is
batch-only today (`DispatchBatchAsync:2467` → `PreWarmGate.Decide`) and stays batch-only; the PRD's
"obeys every launch rule the row's Launch obeys" is satisfied by sharing the row's path, and the gate is
not one of the row's rules.

### 3.5 Expanded state

`Account.PanelExpanded` (`bool?`, appended optional, persisted in the blob through
`IAccountStore.SetPanelExpandedAsync`, silent no-op on unknown id, **not** on `AccountExportRecord`).
Null resolves at load: the main account's panel is expanded; with no main, the first row's (default —
confirm on next interactive run); every other panel is collapsed. After Add account on an empty roster
the new row's panel is expanded without a click (`CompleteAddAsync` sets it when `Accounts.Count == 1`).

**Compact mode needs no work.** `IsCompact` collapses the whole `AccountsItemsControl` `DockPanel` and
shows `CompactRows` through `CompactAccountTemplate` (`MainWindow.xaml:37-185`), which has no panel; the
remembered state is untouched and comes back when compact mode ends. One assertion in
`MainViewModelTests` pins that compact mode never writes `PanelExpanded`.

### 3.6 XAML: where the expander goes

The row is an implicit `<DataTemplate DataType="{x:Type vm:AccountSummary}">` (`MainWindow.xaml:188-1055`)
whose outer `StackPanel` holds the drop line and `AccountRowBorder`. The panel is a third child of that
`StackPanel`, **below** `AccountRowBorder`, with `Visibility` bound to `Panel.IsExpanded`, so:

- the drag ghost (`FindParentRowBorder` → `RenderTargetBitmap` of `AccountRowBorder`,
  `MainWindow.xaml.cs:281-469`) still captures the header only;
- `OnAccountRowDrop` targets are still the headers, so reorder is unchanged;
- `FindRowAccountSummary` (follow chips) and `FocusAccountRow` (container lookup) keep working, because
  the panel is inside the same item container.

The expander is a `ToggleButton` in column 4, left of the caption swatch, styled
`<Style BasedOn="{StaticResource SecondaryToggleButtonStyle}">` (`ControlStyles.xaml:267`, the pattern
`DefaultGameWidget` uses at `MainWindow.xaml:1297`; `ButtonRankFenceTests` fences `ToggleButton`, so an
unstyled one fails), `IsChecked="{Binding Panel.IsExpanded}"`, `AutomationProperties.Name` formatted
from `Shell_Panel_Expander_Name` ("Show recent games for {0}") with `RenderName`, glyph ▸/▾,
keyboard-reachable like every button in that column. Collapsed, the row's height and controls match
v1.35 except the new badges and the expander; a render test pins it (§11).

### 3.8 The one-account window

Implements the anchor epic's "nothing on the first screen says alts, squad, batch or multiple above
the fold when there is one account". `MainViewModel` gains `HasMultipleAccounts => Accounts.Count > 1`
(raised on every `Accounts` change; the follow strip already collapses on `Accounts.Count <= 1` by a
DataTrigger, this is the same rule given a name). Four surfaces bind to it with
`BooleanToVisibilityConverter`: the header's Launch multiple button (`AutomationId="DangerLaunchMultiple"`),
the header's Squad Launch button (`DangerSquadLaunch`), the row's selection dot with its
`MainWindow_InBatches` / `MainWindow_Skipped` label, and the `_FollowMarked` clause of the batch banner
(moot with one account). The controls still exist and still work the moment a second account is added;
with one account they have nothing to do, so hiding them is not a demotion but the truth. The Tools
menu and the tour's "when you want alts" step are the way a one-account user learns they exist.
`MainWindowOneAccountFitsTests` (§6) also asserts those three `AutomationId`s are not visible with one
account and are visible with two.

### 3.7 Follow launches and the history patch

`ISessionHistoryStore` gains `UpdateGameAsync(Guid id, string gameName, long? placeId, Guid? privateServerId)`.
`ApplyPresence` (`MainViewModel.cs:3456`), on the first in-game presence for an account whose open
history row has `PlaceId == null`, patches that row with the presence's place and game name. The
hardcoded `"Following a friend"` at `:3288` becomes `Shell_History_FollowingAFriend` in the same commit.
Result: a follow-marked account's Recent fills in from where presence saw it land, which is the only
truthful source for a follow. `StatsRecordingSessionHistoryStore` forwards the patch as a
`StatsEvent.LaunchRecorded`-shaped correction so `SessionStats.Games` counts it (`SessionStatsStore.cs:112,141`
add nothing today when `PlaceId` is null).

---

## 4. The Library

Implements `prd.md > The Library is a place` and `> Play as`. Build item 4.

### 4.1 Rename

`ShellWindow_Games`, `Shell_Title_Games`, `MainWindow_Games`, `MainWindow_ManageGames` and the
`GamesPage_Games` heading change value to the Library word in English and in all six
`docs/store/translations/ui-<culture>.json` catalogs, then `gen-culture-resx.py` for each culture.
Keys keep their names (renaming 70 `GamesPage_*` keys buys nothing and risks the parity fences). The
rail entry (`Shell/ShellWindow.xaml`), the shell title (`ShellWindow.xaml.cs` `TitleKeys`), the toolbar
button (`AutomationId="ToolbarGames"`, kept) and the Ctrl+G row in `Input/KeyboardVocabulary.cs` read
"Library"; the gesture does not move, so `KeyboardVocabularyTests` sees only a display-name change.
The stale hint `MainWindow_AddGamesFromToolsGames` ("Add games from Tools → Games") is reworded to
point at the Library button, the fix it has needed since Games became a toolbar button.

### 4.2 `LibraryCatalog` and `LibraryViewModel`

`GamesPage` is 407 lines of XAML plus 416 lines of code-behind with no view model and every action in
a click handler. Grid and list views, chips, a merged search-and-paste box and Play as cannot be tested
from a code-behind, so the page gains a view model and the code-behind shrinks to wiring.

```csharp
// src/ROROROblox.Core/Library/LibraryEntry.cs
public enum LibraryEntryKind { Favourite, PrivateServer, Recent }
public sealed record LibraryEntry(
    LibraryEntryKind Kind, long PlaceId, Guid? PrivateServerId, string Name, string? LocalName,
    string? IconUrl, bool IsDefault, DateTimeOffset? LastPlayedUtc, TimeSpan? TotalPlayed, LaunchTarget Target);

// src/ROROROblox.Core/Library/LibraryCatalog.cs — the one join, used by the page and by every panel
public sealed class LibraryCatalog
{
    public LibraryCatalog(IFavoriteGameStore favorites, IPrivateServerStore servers,
                          ISessionStatsStore stats, ISessionHistoryStore history, IGameIconCache icons);
    public Task<IReadOnlyList<LibraryEntry>> ListAsync(CancellationToken ct = default);
}
```

`ListAsync` yields favourites (`FavoriteGame`, `favorites.json`), saved private servers
(`SavedPrivateServer`, `private-servers.json`), and **Recent**: places in `SessionStats.Games` that
are not saved, which is "games played from RoRoRo by any account". `GameStat` gains an appended
`DateTimeOffset? LastPlayedUtc = null` so Recent orders without walking history; `SessionStatsStore`
stamps it on `LaunchRecorded`, and the existing `SessionStatsBackfill` fills it once for old rows
(it is already idempotent and latched by `BackfillCompleted`). Totals are `GameStat.Uptime` (all time)
for the Library and the per-account sum for the panel (§3.2), and each surface says which.

**Icons for unsaved Recent games.** History rows carry no icon. `IGameIconCache`
(`Core/Library/GameIconCache.cs`, plaintext `game-icons.json`: placeId → `{ name, universeId, iconUrl,
fetchedAtUtc }`, non-secret) answers from disk, and on a miss enqueues the place on the **facts worker**
(§2.3, so the one-request-at-a-time rule holds even though these are public no-cookie calls) which calls
`IRobloxApi.GetGameMetadataByPlaceIdAsync` (exists) and raises `IconResolved`. A missing icon paints
the existing placeholder tile until it resolves; nothing is fetched for a place that is not on screen.

```csharp
// src/ROROROblox.App/Games/LibraryViewModel.cs
internal sealed class LibraryViewModel : INotifyPropertyChanged
{
    public ObservableCollection<LibraryEntry> Visible { get; }   // Entries after chips + query
    public LibraryView View { get; set; }                         // Grid | List, persisted (IAppSettings.LibraryView)
    public LibraryFilter Filters { get; set; }                    // [Flags] Recent | Favourites | PrivateServers, persisted (IAppSettings.LibraryFilters)
    public string Query { get; set; }                             // instant local filter on Name and LocalName
    public ObservableCollection<GameSearchResult> FromRoblox { get; } // the "From Roblox" group after Enter with no local match
    public Task SubmitAsync();                                    // Enter: paste → add; else no local match → IRobloxApi.SearchGamesAsync
    public Task PlayAsync(LibraryEntry e, Guid? accountId);       // null = main, else the chosen account
    // SetDefault / SetDefaultServer / Rename / ResetName / Remove: thin passthroughs to the two stores, as today
}
```

**Settings.** Two new `SettingsBlob` parameters (`Core/AppSettings.cs:708-736`): `string LibraryView = "grid"`
and `string LibraryFilters = ""` (comma-separated kind names; empty = everything), two `IAppSettings`
pairs, the four fakes (`MainViewModelTests.cs:1611`, `RobloxLauncherTests.cs:727`,
`StreamerIdentityProviderTests.cs:157`, `Discord/DiscordTestHarness.cs:320`), and reachability through
controls named `LibraryViewToggle` and `LibraryFiltersRow` (the fence's control-edge suffixes). Two keys
rather than one JSON blob because both are scalars with a control each; the alerts cycle's "fewer keys"
rule was about seven generated rows.

### 4.3 Views, chips, search-and-paste

- **Grid** is a `ListBox` whose `ItemsPanel` is WPF-UI's `VirtualizingWrapPanel` (shipped in the pinned
  `WPF-UI 4.3.0`, `ROROROblox.App.csproj:33`; the type is present in `Wpf.Ui.dll`). The 100-entry
  criterion is measured twice: `LibraryGridRenderTests` renders 100 entries at 96 DPI and asserts the
  panel realised fewer containers than entries (virtualisation is on), and `LibraryViewModelTests`
  filters 100 entries on a keystroke under 16 ms. Tiles are icon-first,
  name beneath, Default and Private badges carrying a **word** (`Shell_Squad_Badge_Default`,
  `Shell_History_Badge_Private`, both exist), last played and total as `MetaFontSize` text.
- **List** is one row per entry with the same facts in columns, the shape `History/SessionHistoryPage.xaml`
  already renders.
- **Chips** are three `ToggleButton`s (`SecondaryToggleButtonStyle`, the `ToggleButton` rank;
  `SecondaryButtonStyle` targets `Button` and throws at XAML load on a `ToggleButton`), multi-select,
  a check glyph when selected so flatline can tell, persisted through `LibraryFilters`.
- **One box.** `SearchInput` and `UrlInput` collapse to one `ui:TextBox`. Typing filters `Visible`
  instantly. Enter runs `SubmitAsync`: `LaunchTarget.FromUrl` / `TryParseShareLink`
  (`Core/LaunchTarget.cs`) and `RobloxLauncher.ExtractPlaceId` decide whether the text is a paste; a
  game URL or place id goes through `GetGameMetadataByPlaceIdAsync` → `IFavoriteGameStore.AddAsync` as
  `OnAddClick` does today; a private-server link, which the page cannot take today at all, goes through
  `MainViewModel.ResolveShareUrlAsync` (needs a non-expired cookie; the page is handed the delegate) →
  `IPrivateServerStore.AddAsync`, the path `JoinByLinkSave` and `SquadLaunchWindow.xaml.cs:401` use;
  otherwise `IRobloxApi.SearchGamesAsync` fills `FromRoblox` with the existing Add affordance.
- **Empty Library** shows `GamesPage_EmptyLibrary` (search or paste a link, and saved games appear in
  every account's panel), one sentence each.
- The three hardcoded English `StringFormat`s in `GamesPage.xaml` (`'by {0}'`, `'{0:N0} playing'`,
  `'Place ID {0}'`) and `FavoriteGame.DropdownLabel`'s `" (private server)"` become resx keys in the
  rewrite; `NoRawXamlProseFenceTests` is the reason they could not stay.

### 4.4 Play and Play as

Each entry has **Play** (`CtaButtonStyle`, `AutomationId="DangerPlay"`) and **Play as** (`SecondaryButtonStyle`,
`AutomationId="DangerPlayAs"`); both ids go on `docs/ui-routes.json`'s `denyAids` (validated by
`UiRoutesSchemaTests`). Play calls `PlayAsync(entry, null)`: the main account, or the only account, or
with no main the first row's (default — confirm on next interactive run).

**Play as is a new control**, because no account picker exists (§0): `Games/AccountPickerFlyout.xaml`,
a `ContextMenu` whose `ItemsSource` is `MainViewModel.Accounts` (UI thread; the page runs there),
each item an avatar (`AvatarDisplaySource`), `RenderName` and the health word; Expired items are
disabled with the reason as their tooltip; Follow-marked items launch by follow as everywhere else
because the launch path decides, not the picker. Opened by the Play as button, one per page, positioned
at the button. Masked under streamer mode by binding the masked properties. Every `MenuItem` gets an
`AutomationProperties.Name` even though the naming fence does not scan `MenuItem`, because the next
fence will.

`PlayAsync` resolves the account and calls the delegate `GamesPage` was constructed with:
`App.CreateShellPage` (`App.xaml.cs:1698`) already builds the page beside `MainViewModel` and passes
`libraryChanged`; it gains `(Guid accountId, LaunchTarget target) => MainViewModel.LaunchAccountByIdAsync(accountId, target)`,
a four-line internal wrapper that finds the summary in `Accounts` and calls `LaunchAccountAsync`. The
session therefore records under the chosen account (`RecordSessionStartAsync`), the panel's
`RefreshAsync` fires on exit, and the Library's Recent updates on the next `ListAsync`, which is the
PRD's "agree within one refresh".

### 4.5 What stays in the code-behind

`GamesPage.xaml.cs` keeps the `RenameWindow` and confirm-dialog plumbing (every popup themed, per
house rule; the two `MessageBox` removal confirms become the themed confirm the History page uses)
and the focus-on-load. Everything that decides something moves to `LibraryViewModel` and gets a test.

---

## 5. The tour

Implements `prd.md > One account is a complete install` (tour half). Build item 5.

`About/WelcomeWindow.xaml` (190 lines) has three literal-numbered steps and an inline quick-tips block
(`:113-179`). It is restructured, not rewritten:

1. Hero subtitle `WelcomeWindow_RunMultipleRobloxClientsSide` is replaced by a new key naming the
   launcher first (one account, your games, your settings) and side-by-side clients second.
2. Steps: `ClickAddAccount` (kept) → new `PickOrPlayFromThePanel` (the panel opens under your account;
   Recent, Favourites and Private servers; or open Roblox home) → new `WhenYouWantAlts` (add another
   account; Launch multiple and Squad Launch appear when there are two).
3. The quick tips that only matter with several accounts (☰ reorder, ★ main, the batches dot, Follow
   chips) move under a new heading `WelcomeWindow_WithMoreThanOneAccount`; the swatch, Compact, tray
   and the ^ tip stay above it.

Retired keys (`OpenGamesAndAddThe`, `SearchByNameOrPaste`, `ClickLaunchAsOrLaunch`,
`MultiInstanceIsOnBy`, `RunMultipleRobloxClientsSide`) are removed from `Strings.resx` **and** all six
`ui-<culture>.json` catalogs in the same commit, then regenerated, so `CultureKeyParityFenceTests`
and `LocKeyParityFenceTests` stay green. The sentinel (`.welcome-shown`, `ShouldShowOnStartup(isFirstRun,
accountCount) => isFirstRun && accountCount == 0`) is untouched; `WelcomeSentinelTests` is untouched.
`WindowContentFitsTests` does not cover `WelcomeWindow`; it gains the window, because the tour grows a
heading and must still fit at 760×600. The translation verifier runs on the string delta before the
tag, per CLAUDE.md.

---

## 6. Nothing moves for the six-alt user: the measures

Implements `prd.md > Nothing moves for the six-alt user`. Not a build item; a set of gates the
checklist carries into every item.

| Claim | Measure |
|---|---|
| Launch paths and plugin RPCs unchanged | The existing suite (2,802 unit + 27 harness) is the measure; no RPC is added or changed (facts are not exposed over the contract this cycle). |
| Collapsed row matches v1.35 to the pixel | New `AccountRowCollapsedRenderTests` renders one collapsed row at 96 and 120 DPI, the shape of `HistoryRowRenderTests`, and pins the v1.35 header height as a constant captured **before** the first XAML change lands. |
| One account + open panel fits at 1080p, 100% and 125% | New `MainWindowOneAccountFitsTests` (`[WindowRenderTheory]`, 96 and 120 DPI) renders `MainWindow` at 900×600 with one account and `Panel.IsExpanded = true` and asserts the roster `ScrollViewer`'s `ComputedVerticalScrollBarVisibility == Collapsed`. Today `WindowContentFitsTests` covers six small windows and not `MainWindow`. |
| Startup within 10% with eight accounts | Five cold starts each of v1.35 and the build on the same machine with the same eight-account vault; time from the Serilog banner to the `mainWindow.Show()` log line; recorded in the smoke doc. |
| No startup network calls from panels or facts | `AccountFactsServiceTests` zero-request assertion (§2.3), plus a log grep on the eight-account start: no `AccountFactsApi` request line before the first expand or tick. |
| Ten-slot theme contract unchanged | `ThemedStatusColourTests` offenders stay 0; every new brush is a `DynamicResource` key derived in `App.xaml` (§1.1). |
| 100 saved games scroll smoothly, filter within a keystroke | `LibraryGridRenderTests` (realised containers < entries at 100) and `LibraryViewModelTests` (filter over 100 entries < 16 ms), §4.3. |
| One-account window does not lead with alts | `MainWindowOneAccountFitsTests` asserts `DangerLaunchMultiple`, `DangerSquadLaunch` and the batches dot are not visible with one account, visible with two (§3.8). |

---

## 7. Riders

### 7.1 #221: input credit goes to the account that received it

`Core/Diagnostics/ActivityMonitor.cs` polls `GetLastInputInfo` on a `System.Threading.Timer` at a 1 s
period and, when the tick advanced, credits the account whose window is foreground **at sample time**
(`Sample()`, `:93-111`). The issue's repro (bring B forward, tap, restore C within one second) credits C.

**Fix: sample the foreground at 100 ms and credit the foreground at the input's own tick.**
`LASTINPUTINFO.dwTime` is in the `GetTickCount` domain, so `ISystemInputClock` gains `uint NowTick`
(`Environment.TickCount` cast, same domain); the monitor keeps a ring of
`ForegroundHistory = 32` `(tick, pid)` samples taken every `SamplePeriod = 100 ms` (the one constant that
moves; the `Interlocked` overlap guard stays), and on an advanced input tick credits the ring entry with
the greatest `tick <= inputTick`. The warn-latch evaluation and `WarnThresholdCrossed` keep their meaning;
they just run on a faster tick. Cost: ten `GetForegroundWindow` + `GetWindowThreadProcessId` calls a
second, microseconds each.

**Why not a hook.** `WH_KEYBOARD_LL` or Raw Input with `RIDEV_INPUTSINK` would give the exact instant,
and would also give RoRoRo a keylogger-shaped capability on a Store binary that today "observes" and
never touches input. Refused; the 100 ms ring gets within one human reaction time of the truth without
it.

`ActivityMonitorTests` gains the replay: fake clock, fake foreground, fake input; the 2026-09-29 timeline
from the issue (fg C at 0, fg B at 300, input at 450, fg C at 800, sample at 1000) **fails on the old
code** (credits C) and passes on the new (credits B); a second row puts the switch-back inside the same
100 ms window so the ring, not just the faster period, is what passes. `ActivitySnapshotProvider` and the
`GetAccountActivity` RPC are unchanged; the plugin half (rororo-ur-afk#21) is referenced in the closing
comment.

### 7.2 #260: the cursor is visible

The sweep found no `Cursors.None`, no `.cur`/`.ani`, no `Mouse.OverrideCursor`, no `ShowCursor`/`SetCursor`
anywhere under `src/ROROROblox.App`; the only values in the tree are `Hand` (14 XAML sites, 3 C# sites),
`SizeAll` (the drag grip, `MainWindow.xaml:415`) and `null` (`Tray/AlertBalloon.xaml.cs:115`). The one
cursor-adjacent call is `DragDrop.DoDragDrop` for row reorder, with no `GiveFeedback` handler. **The cause
is environmental until the reporter's evidence says otherwise.**

- **Evidence lane**, in the issue: diagnostics bundle, Windows build, display scale, cursor scheme
  (custom `.cur` schemes and the software "cursor size" accessibility setting are the two WPF suspects),
  and whether the cursor shows in another WPF app on that machine (Windows Terminal settings page is
  one). If environmental, the issue is answered with the workaround and the Known Roblox issues list is
  **not** used. If ours, the fix ships with before/after.
- **Fence either way**: `CursorFenceTests` scans `src/ROROROblox.App/**` XAML and C# as text, forbids
  `Cursors.None`, `Cursor="None"`, `Mouse.OverrideCursor`, `ShowCursor(`, `SetCursor(`, `.cur"` and
  `.ani"`, allows `{Hand, SizeAll, Arrow, null}` only, vacuity floor `CursorSitesFloor = 15` (18 today).

### 7.3 #222: windowless strays

Measurement first: one dev-build day with ten launches, counting windowless `RobloxPlayerBeta` processes
and their ages at end of day (`Get-Process RobloxPlayerBeta | ? MainWindowHandle -eq 0 | select Id, StartTime`),
recorded in the smoke doc. If the v1.35 pre-launch clear already holds the count at or below one between
launches, #222 closes on that number.

If strays still accumulate: `Tray/IdleTrayClientSweeper` on its **own** `PeriodicTimer` at
`SweepInterval = 30 s` (no existing tick touches strays; `OrphanedClientSweeper` is 5 s and adopts),
calling `IRobloxInstanceStopper.StopIdleTrayClients(isTracked, minAge: TimeSpan.FromMinutes(2))`.
`TrayClient.IsIdleTrayClient` gains the age floor over a new `RobloxProcessInfo.StartedAt`
(`RobloxRunningProbe.GetRunningPlayers` reads `Process.StartTime`, which `OrphanedClientSweeper.DefaultEnumerate`
already does for `ClientSnapshot`); the existing rule (`--launch-to-tray` + no window + untracked, null
command line fails closed) is unchanged for the pre-launch clear, which passes `minAge: null`. A client
still creating its window is under two minutes old by any measure; a client another tool is using is
windowed or tracked. `TrayClientTests` gains the age rows.

### 7.4 #52: Diagnostics tells the truth about version and channel

```csharp
// src/ROROROblox.Core/Diagnostics/IInstallIdentity.cs
public interface IInstallIdentity { string Version { get; } InstallChannel Channel { get; } }
public enum InstallChannel { Store, Sideload, DirectDownload, Dev }
```

`App/Distribution/InstallIdentity.cs` composes what exists: `Version = AppVersion.Display(mode)` (the
About page's helper, `About/AboutPage.xaml.cs:35`); `Channel` from `IHostBuildIdentity.InstallFlavour`
(`Plugins/HostBuildIdentity.cs`: `store` / `sideload` / `dev` from `Package.Current.SignatureKind`) with
the unpackaged case split by Velopack's `UpdateManager.IsInstalled` (`Updates/UpdateChecker.cs:53`
already reads it) into Direct download and Dev. `DiagnosticsCollector` takes `IInstallIdentity? identity = null`
(Core keeps its assembly fallback for tests), `DiagnosticsSnapshot` gains `Channel`, the page shows
both, and `BuildSupportBundle` (`Diagnostics/DiagnosticsPage.xaml.cs:215`) writes a `Channel` line
under `App version` in `snapshot.txt`. `InstallFlavour` on the plugin contract does not change.

### 7.5 #232: translations tied to the English long description

`ListingSheetParityFenceTests` reads `docs/store/listing-copy.md` and every `docs/store/listing-copy-<culture>.md`
(six today; cultures discovered from the filenames, floor `TranslatedSheetFloor = 6`), takes the first
fenced block after each of three heading prefixes (`## Short description`, `## Long description`,
`## Product features`, the prefix match `scripts/export-listing-translations.py`'s `block_after_heading`
uses, because the English sheet's headings carry different suffixes), and asserts across all seven: the
long description's `• ` bullet count is equal (14 today), the features block's non-empty line count is
equal (20 today), and the short description is ≤ 200 characters. What's new is excluded: the English
sheet holds a pointer to `whats-new-<version>.md`, not a block. Vacuity floors: bullets ≥ 10, features ≥ 15.
The fence must pass on the rewritten sheets this cycle ships.

### 7.6 #231 and the catalog tasks: done in the tree, close on evidence

`scripts/check-plugin-catalog-drift.ps1` compares each row's `latestVersion` with the repo's latest
non-draft, non-prerelease release **and** that release's `manifest.json`, exits 1 on any status but `ok`,
and runs as `Check plugin catalog drift` at `release.yml:88-94`, after Test and before the Velopack
build, so a disagreement fails the tag and not the main-branch guards: the PRD's criterion, verbatim.
It ran green on the v1.34.0.0 and v1.35.0.0 tag runs. All five rows read `626Labs LLC`; "626 Labs" in
prose is untouched. This cycle's work is: `gh run view --log` for the v1.35 run quoting the step's
output into the closing comments of #231, task `eejy3xvcu` and task `g5hjernie`; and the ur-mcp row is
whatever the gate says at tag time, which is the point.

### 7.7 Listing, support contact, privacy

- **Listing rewrite** on `docs/store/listing-copy.md` and the six sheets: short description leads with
  the launcher and says "alts when you want them" inside 200 characters; the long description's first
  paragraph is the one-account story, the multi-account paragraph loses nothing; the features list adds
  the account manager (facts, health, notes) and the Library (grid, list, filters, play as); dated
  provenance notes in each sheet's blockquote; §7.5's fence green; then the three-surface audit (short,
  long, features against `whats-new-1.36.0.0.md`) recorded in `docs/store/submission-packet-1.36.0.0.md`
  per the playbook's phase 2.
- **Support contact** `support@626labs.dev` and the `supportt@` typo are Partner Center fields on the
  submission; a Store write needs Este's explicit word in the session (house rule), so the checklist
  item is "prepared, with the exact field values", not "written". The public catalog is re-read after
  publish to confirm (tasks `bxranna8z`, `0h7s65ljx`).
- **Quit before updating** goes into `README.md:65`'s Store paragraph and into `whats-new-1.36.0.0.md`'s
  English block (there is no What's-new template file; the per-version file is the template).
- **`docs/PRIVACY.md`**: the network table gains `economy.roblox.com` and `premiumfeatures.roblox.com`
  rows (`friends.roblox.com` already has one; its purpose cell grows "friends online count"); the
  `accounts.dat` row's contents cell gains "cached Robux, membership and friends-online count, and your
  note *(v1.36+)*"; the TL;DR gains the one PRD line (read with the account's own cookie, shown, not
  kept in any plain file). **And a correction found by the sweep:** the Diagnostics-bundle section
  (`:129`) says the bundle carries "a list of saved account display names + IDs" and "the current
  operational log file"; the code writes a count plus 8-character short ids and up to three redacted
  logs. The section is rewritten to match `BuildSupportBundle`, and gains the `Channel` line from §7.4.

### 7.8 vibe-iterate joins the toolchain

`/vibe-iterate:bootstrap` (plugin 1.4.0) classifies `ROROROblox.slnx` → `ROROROblox.App.csproj`
(`WinExe` + `UseWPF`) as `desktop-app` with anchor `.NET 10 WPF`, pins the `PackageReference` set deduped
across the four projects plus `net10.0-windows` as a pseudo-pin with `ecosystem: nuget`, and takes the
competitor URLs as cycle inputs rather than a question: `https://bloxstraplabs.com/wiki/about`,
`https://github.com/bloxstraplabs/bloxstrap/releases`, `https://github.com/imide/fishstrap/releases`,
and `https://github.com/ic3w0lf22/Roblox-Account-Manager/releases` (archived; the seat it vacated is
the one this cycle takes). `.vibe-iterate/config.json` is committed (no secrets; validated against the
plugin's `config.schema.json`). One `/vibe-iterate:competitive` run executes; its output is a merged PR
with an Atlas entry or a recorded measured no-op with the ranked candidate list, and the Atlas entry is
written beside this file as `atlas-competitive-<date>.md`. Quiet hours, player logs and the FastFlags
editor appear in that list ranked, not built. The run is reviewed like any cycle item.

### 7.9 Account-groups spec banner

`docs/superpowers/specs/2026-07-09-account-groups-design.md` still says `Status: Approved-shape`.
Per the house rule (banner-correct, never rewrite), a dated banner goes above the existing one:
abandoned, ruled 2026-08-30 in `docs/decisions.md` (the onboarding-docs rulings entry) and
`docs/features.md:205`; the panel is not a group and gives the idea no new reason.

### 7.10 #230, conditional

Rides only if the host-side fix (task `y6u70eghn`, in progress) merges before `release/v1.36.0.0`
branches. If it does: `HostInfo` gains `alert_sound` (additive, `contract_version` stays `"1.0"`),
which is a field on an existing RPC and so needs no new `RpcMethodCapabilityMap` entry; the plugin half
is theirs. If it does not: the Settings page's Silent hint says what Silent covers (the app's own
balloons; plugins follow their own setting until they read ours), and the release notes name it.

---

## 8. Data model

| Store | Change | Kind |
|---|---|---|
| `accounts.dat` (DPAPI, `StoredAccountsBlob` v1) | `StoredAccount` + `Facts: AccountFacts?`, `Note: string?`, `PanelExpanded: bool?` | additive; legacy loads null |
| `session-history.json` | `LaunchSession` + `PrivateServerId: Guid?`; new `UpdateGameAsync` | additive |
| `session-stats.json` | `GameStat` + `LastPlayedUtc: DateTimeOffset?` | additive; backfill fills once |
| `settings.json` | `LibraryView: string = "grid"`, `LibraryFilters: string = ""` | additive |
| `game-icons.json` | **new**, plaintext, placeId → `{ name, universeId, iconUrl, fetchedAtUtc }` | non-secret cache; safe to delete |
| export file (`RRRACCT` v1) | `AccountExportRecord` + `Note: string?` | additive; version stays 1 |
| `.vibe-iterate/config.json` | **new**, committed | plugin config |

Nothing is removed, nothing branches on a version number, and no new file holds a secret.

---

## 9. File structure: new and changed

```
src/ROROROblox.Core/
├── Account.cs                           # + Facts, Note, PanelExpanded
├── AccountFacts.cs                      # NEW record
├── AccountStore.cs                      # + StoredAccount fields, SetFactsAsync, UpdateNoteAsync, SetPanelExpandedAsync; export/import Note
├── IAccountStore.cs                     # + three members
├── IAccountFactsApi.cs                  # NEW
├── AccountFactsApi.cs                   # NEW typed client, one ctor
├── AccountFactsService.cs               # NEW single-worker lane, three triggers, timer
├── BackfillPacing.cs                    # NEW constants; AccountUserIdBackfillService reads them
├── AccountUserIdBackfillService.cs      # + AccountUserIdResolved event
├── LaunchSession.cs                     # + PrivateServerId
├── ISessionHistoryStore.cs / SessionHistoryStore.cs   # + UpdateGameAsync
├── SessionStats.cs / SessionStatsStore.cs / SessionStatsBackfill.cs  # GameStat.LastPlayedUtc
├── AppSettings.cs / IAppSettings.cs     # + LibraryView, LibraryFilters
├── RobloxApi.cs                         # SessionLimitedException.ChallengeType from rblx-challenge-type
├── Library/
│   ├── LibraryEntry.cs                  # NEW record + kind enum
│   ├── LibraryCatalog.cs                # NEW join over favourites, servers, stats, history, icons
│   └── GameIconCache.cs                 # NEW IGameIconCache + file-backed impl
├── Diagnostics/
│   ├── ActivityMonitor.cs               # 100 ms foreground ring; ISystemInputClock.NowTick
│   ├── IInstallIdentity.cs              # NEW (+ InstallChannel enum)
│   ├── DiagnosticsCollector.cs          # + identity, Channel
│   ├── TrayClient.cs                    # + age floor (conditional, 7.3)
│   └── RobloxProcessInfo.cs             # + StartedAt (conditional, 7.3)
└── Transport/AccountExportRecord.cs     # + Note

src/ROROROblox.App/
├── App.xaml                             # HealthVerifyBrush seed (one derived key)
├── Controls/ControlStyles.xaml          # FactBadgeStyle
├── Theming/ThemeService.cs              # derives HealthVerifyBrush in ApplySlot
├── App.xaml.cs                          # AddHttpClient<IAccountFactsApi,…>; facts service + catalog + icon cache + identity registrations; LaunchAccountByIdAsync delegate into GamesPage
├── ViewModels/
│   ├── AccountHealth.cs                 # NEW enum
│   ├── AccountSummary.cs                # Health, SessionNeedsVerification, facts texts + accessible names + FactsAgeText, note, Panel, masked variants
│   ├── AccountPanelViewModel.cs         # NEW
│   ├── MainViewModel.cs                 # ApplyFacts, ApplyUserIdResolved, HasMultipleAccounts, panel wiring in WireAccountSummary, history patch in ApplyPresence, LaunchAccountByIdAsync, FollowMarked clause
│   ├── LaunchEligibility.cs             # + FollowMarked clause
│   └── SquadLaunchPlan.cs               # exposes the follow count
├── MainWindow.xaml                      # badges, age hint, expander, panel region below AccountRowBorder, note line, HasMultipleAccounts triggers
├── Games/
│   ├── GamesPage.xaml / .xaml.cs        # Library: one box, chips, grid/list, Play / Play as; code-behind shrinks to wiring
│   ├── LibraryViewModel.cs              # NEW
│   └── AccountPickerFlyout.xaml / .cs   # NEW ContextMenu
├── Distribution/InstallIdentity.cs      # NEW
├── Diagnostics/DiagnosticsPage.xaml.cs  # Channel line in page and bundle
├── About/WelcomeWindow.xaml             # restructured tour
├── Tray/IdleTrayClientSweeper.cs        # NEW, conditional (7.3)
├── Input/KeyboardVocabulary.cs          # "Library" display name
├── Shell/ShellWindow.xaml(.cs)          # rail and title read the Library key
└── Properties/Strings.resx              # new keys; retired tour keys removed

src/ROROROblox.Tests/
├── AccountFactsSchemaTests.cs           # NEW (copy of LocalNameSchemaTests)
├── AccountFactsServiceTests.cs          # NEW: zero-at-startup, one-at-a-time, 60 s min age, timer picks oldest, failures never flip health
├── AccountPanelViewModelTests.cs        # NEW: Recent query, cap 8, other-account isolation, follow patch
├── LibraryViewModelTests.cs             # NEW: chips, query, paste routing, Play as resolution
├── LibraryCatalogTests.cs               # NEW
├── ActivityMonitorTests.cs              # + #221 replay rows
├── RobloxApiTests.cs                    # + challenge header read
├── CursorFenceTests.cs                  # NEW
├── ListingSheetParityFenceTests.cs      # NEW
├── FactBadgeNamingFenceTests.cs         # NEW: every FactBadgeStyle element carries AutomationProperties.Name
├── Rendering/AccountRowCollapsedRenderTests.cs   # NEW pixel bar
├── Rendering/MainWindowOneAccountFitsTests.cs    # NEW fit bar at 96/120 DPI + one-account visibility
├── Rendering/LibraryGridRenderTests.cs  # NEW: 100 entries, virtualisation proof
├── Rendering/WindowContentFitsTests.cs  # + WelcomeWindow
├── TypedHttpClientRegistrationTests.cs  # + AccountFactsApi
├── StreamerIdentityProviderTests.cs     # + one [Theory] row per masked fact (§2.5)
├── AccountStoreTransportTests.cs        # + Note round-trip; + record-carries-no-Facts sibling of :57 (§2.4)
├── TrayClientTests.cs                   # + age-floor rows (§7.3, conditional)
├── MainViewModelTests.cs                # + RobloxUserId after AccountUserIdResolved (§2.6); compact mode never writes PanelExpanded (§3.5); fakes
├── UiRoutesSchemaTests.cs               # unchanged; docs/ui-routes.json gains DangerPlay, DangerPlayAs
├── SettingsReachabilityTests.cs         # unchanged; LibraryView, LibraryFilters reachable by control name
├── KeyboardVocabularyTests.cs           # unchanged; Ctrl+G display name reads Library
└── (the IAccountStore, IAppSettings and IRobloxApi fakes listed in §2)

docs/
├── cycles/2026-11-01-account-library/   # this spec; checklist.md next; atlas-competitive-<date>.md from 7.8
├── store/listing-copy*.md, whats-new-1.36.0.0.md, submission-packet-1.36.0.0.md, translations/ui-*.json
├── PRIVACY.md                           # 7.7
├── architecture.md                      # line counts, facts lane, Library join, data-on-disk rows
├── features.md, feature-ledger.md, decisions.md   # at tag
└── superpowers/specs/2026-07-09-account-groups-design.md  # banner (7.9)

.vibe-iterate/config.json                # NEW (7.8)
README.md                                # quit-before-updating line (7.7)
```

---

## 10. Data flow: the two new lanes

```
facts lane (Core, one worker)                          panel (App, per row)
──────────────────────────────                         ────────────────────
expand ─┐                                              IsExpanded=true
launch end ─┼─► Channel<Guid> ─► wait 2.5±0.5 s ─►       │
timer (1/min, oldest>10 min) ┘   RetrieveCookie          ├─► history.ListAsync ─► Recent (≤8, this account)
                                  GET currency           ├─► LibraryCatalog.ListAsync ─► Favourites, Private servers (+ this account's age)
                                  GET friends/online     └─► requestFacts() if Facts older than 60 s
                                  GET validate-membership (gated)
                                  SetFactsAsync ─► accounts.dat (DPAPI)
                                  AccountFactsUpdated ─► _ui.Invoke ─► ApplyFacts(id) ─► row badges

click on any panel or Library entry ─► launch(target) ─► MainViewModel.LaunchAccountAsync(summary, target)
                                                         (flagged rule, cookie, defender, idle-tray clear, launch, history row)
```

Startup: `OnStartup` → … → `mainWindow.Show()` → `RunStartupChecksAsync` → 5 s → user-id backfill
(sequential, 2.5±0.5 s) → **then** the facts timer is armed, first tick a minute later, one account.
Nothing in the facts lane runs before that line.

---

## 11. Fences that move, with their current values

| Fence (file) | Constant today | Why it moves |
|---|---|---|
| `AccessibleNamingFenceTests` | `UnnamedCeiling = 1` (asserted as equality), `ScannedFloor = 120` | every new Button/ToggleButton/TextBox is named; the floor rises to the new scanned count in the same commit; the ceiling does not move |
| `ThemedStatusColourTests` | XAML literal ceiling 25, floor 15; offenders 0 | the `HealthVerifyBrush` seed in `App.xaml` is one more allow-listed literal; the ceiling moves only if that pushes the allowed count past 25 |
| **new** `FactBadgeNamingFenceTests` | floor 5 badges | every `FactBadgeStyle` element names itself with its value, which `AccessibleNamingFenceTests` cannot see (it does not scan `TextBlock`) |
| `TypeLadderFenceTests` | `DistinctRawSizeCeiling = 3` (18, 20, 24 today) | must **not** move: Library tiles and badges use ladder tokens only |
| `WindowContentFitsTests` | six windows, own size, 96 DPI | + `WelcomeWindow` |
| `KeyboardVocabularyTests` | hint floor 8 | no new gesture; Ctrl+G's display name reads Library |
| `SettingsReachabilityTests` | floors 10 / 60 / 6; 8 exemptions | `LibraryView`, `LibraryFilters` reachable by control name; no new exemption |
| `CultureKeyParityFenceTests` | 6 cultures, catalog ≥ 1000 | all new keys through `ui-<culture>.json` + `gen-culture-resx.py`; retired tour keys removed everywhere |
| `UiRoutesSchemaTests` | 7 `denyAids` | + `DangerPlay`, `DangerPlayAs` |
| `ButtonRankFenceTests` | declarations ≥ 100; one XAML exemption | new buttons use rank styles; no new exemption |
| `ModalDefaultButtonSafetyTests`, `WindowChromeFenceTests` | 7 linked modals; 9 destinations, 13 interruptions | no new window this cycle (Play as is a ContextMenu, the panel is in-row) |
| `TypedHttpClientRegistrationTests` | one row per typed client | + `AccountFactsApi` |
| `NoRawXamlProseFenceTests` | — | the three `GamesPage.xaml` StringFormats move to resx |
| **new** `CursorFenceTests` | `CursorSitesFloor = 15` (18 today) | forbids None / custom / OverrideCursor / ShowCursor |
| **new** `ListingSheetParityFenceTests` | sheets ≥ 7, bullets ≥ 10, features ≥ 15 | equal counts across all seven sheets |
| **new** `AccountRowCollapsedRenderTests`, `MainWindowOneAccountFitsTests` | v1.35 header height captured first; 900×600 at 96 and 120 DPI | the pixel bar and the fit bar |

---

## 12. Test strategy

- **Core logic is unit-tested without WPF:** the facts service (zero at startup, one at a time, never
  the same second, 60 s min age, oldest-first timer, failures leave the old value and never flip health),
  the Recent query, the Library join and filters, paste routing, the challenge-header read, the #221
  replay, the age-floor rule, the install channel resolver, the history patch.
- **Fences are deliverables**: `CursorFenceTests` and `ListingSheetParityFenceTests` are items, not side
  effects, and each carries a vacuity floor so an empty scan fails.
- **Render gates** pin the two bars the PRD states in pixels: the collapsed row and the one-account
  window at two DPIs; plus the Library grid's virtualisation at 100 entries (§4.3) and the one-account
  visibility rule (§3.8).
- **Measurements that are not tests** (startup time, the #222 day, the captcha and membership probes)
  are recorded in `docs/store/smoke-2026-xx-xx-account-library.md` and `docs/decisions.md`, and the
  checklist treats "recorded" as done, not "ran".
- **A live smoke** on the clan machine's shape (eight accounts, two expired, one marked, streamer mode
  on and off) is part of done.

---

## 13. Dependencies and external services

| Dependency | Status | Notes |
|---|---|---|
| Roblox Economy v1 `GET /v1/user/currency` | legacy, cookie, no stated limit | [doc](https://create.roblox.com/docs/en-us/cloud/reference/domains/economy.md); build measures behaviour at eight accounts, backfill pacing |
| Roblox Friends v1 `GET /v1/users/{id}/friends/online` | legacy, cookie | [doc](https://create.roblox.com/docs/cloud/legacy/friends/v1); `data[]` of `UserPresenceResponse`; other operations in the same file document 429 "flood limit" |
| Roblox PremiumFeatures v1 `GET /v1/users/{id}/validate-membership` | legacy, cookie, **hidden, deprecation-flagged** | [doc](https://create.roblox.com/docs/en-us/cloud/reference/domains/premiumfeatures.md); gated, §2.2 |
| Roblox challenge headers on 403 | undocumented by Roblox | community-observed names; measured in §1.2 |
| Roblox Plus (replaced Premium 2026-04-30) | product fact | [PCGamesN](https://www.pcgamesn.com/roblox/plus-subscription), [playtoearn](https://playtoearn.com/news/roblox-plus-launches-april-30-at-499-monthly-replacing-premium-with-discount-first-model) |
| Velopack `UpdateManager.IsInstalled` | shipped | channel split in §7.4 |
| WPF-UI (`ui:` controls), `Hardcodet.NotifyIcon.Wpf 2.0.1` | shipped | no version change |
| vibe-iterate 1.4.0 | installed | §7.8 |
| **No new NuGet package.** | | A virtualizing wrap panel is taken from WPF-UI only if the shipped version has one; otherwise the 100-entry bar is measured, not assumed |

---

## 14. Deployment: identity and signing

Unchanged, and flowed through from CLAUDE.md rather than re-asked: Microsoft Store ID `9NMJCS390KWB`,
package identity `626LabsLLC.RoRoRoBlox`, Publisher `CN=177BCE59-0966-4975-9962-10E36652141F`,
`PublisherDisplayName "626Labs LLC"` (no space), Velopack `packId RORORO`, version four-part with
revision `0`, tag `vX.Y.Z.0` drafts the GitHub release through `release.yml` (tests → catalog drift gate
→ Velopack → sign and attach `roblox-compat.json`, `.sig`, `plugins-catalog.json`), Store MSIX built
locally by `scripts/finalize-store-build.ps1` for x64 and arm64, sideload signed with the gitignored
`dev-cert.pfx`. The Store-signed cert and the sideload cert are never the same key. A pre-release
reaches nobody. Runbook: `docs/store/release-playbook.md`; Store writes wait for Este's word.

---

## 15. Key technical decisions

1. **Facts get their own typed client, not members on `IRobloxApi`.** Nine fakes would break; one new
   fake in one new test file is the cost instead. Tradeoff accepted: two classes attach the cookie the
   same way, and the `UseCookies=false` line is duplicated on purpose so the rule is visible at both
   registrations.
2. **Facts cache in the DPAPI blob, not a new file.** Robux is a balance, not a secret, but it is the
   kind of number a screenshot of a plain JSON file would embarrass someone with, and the vault already
   exists. Tradeoff: `accounts.dat` is rewritten on every refresh, bounded to about once a minute at
   steady state.
3. **Health ships on measured signals; follow-marked does not beat presence.** The row's job for a
   marked account in a game is to say where it is. Tradeoff: the mark is quieter than the other states;
   the badge on the name line is the compensation.
4. **One launch path.** The panel and the Library are handed a delegate to `LaunchAccountAsync` and own
   no launch code. Tradeoff: the install-deferral gate stays batch-only, as it is today.
5. **Two new view models, zero growth in `MainViewModel`'s responsibilities.** `AccountPanelViewModel`
   gets three delegates; `LibraryViewModel` gets two stores and one delegate. Tradeoff: `GamesPage`
   is rewritten rather than grown, which is more diff than the scope's "grown" implied and the only way
   grid/list/chips/paste get tests.
6. **#221 is a 100 ms ring, not a hook.** A low-level hook or Raw Input sink is keylogger-shaped on a
   Store binary; the ring gets within a reaction time of the truth and stays observational.
7. **Membership is gated on the Roblox Plus reality**, logged to the dashboard at spec time.
8. **#231 closes on evidence rather than being rebuilt.** The gate shipped 2026-10-19 and has run on two
   tags; the PRD's work item was a freshness miss.

---

## 16. Open issues: what this spec deliberately does not decide

- **The captcha, expired and limited triples.** Measured in build item 1; §1.2 only names what to record.
- **Whether validate-membership answers for Plus.** Measured at the top of build item 2.
- **The panel's exact row layout and the grid tile's proportions.** Judged against the rendered window
  and the mock the design skill produces, not on paper; the fit and pixel bars are the only constraints.
- **Whether the facts lane should also refresh on the Library's Play as.** It refreshes on launch end,
  which covers it; adding a trigger on launch start is a build-time call if the row looks stale.
- **The #222 sweep** exists only if the measured day says so.
- **Store Product Name casing** (RORORO on the Store, RoRoRo elsewhere): listing-side, Este's call at
  submission, not a blocker.
- **Per-account favourites, sorting by time played, a "forgotten" shelf, server names for public
  servers:** the PRD's "with more time" list; the `LibraryEntry` record and `LastPlayedUtc` on
  `GameStat` are shaped so each is a filter or a sort over data that now exists, not a new store.

## Assumptions carried from the PRD, plus this spec's own

The PRD's ten stand (timer 10 min, 60 s min age, cache in the blob, note 200 chars and hidden in streamer
mode, Recent cap 8, first row when no main, one shared Library, follow-marked is the flag, needs
verification on a signal, the vibe-iterate run executes). This spec adds, each one sentence to overturn:

11. Release target is v1.36.0.0.
12. Follow-marked shows in the secondary line only when the account is not running; the badge is
    permanent.
    *Note 2026-11-05:* the badge's place moved. Item 13 (Este's ruling, 2026-11-03) put the health and
    Follow-marked badges at the head of the facts line, not on the name line, because the real row at
    900 px pushed the name to zero width. See `docs/decisions.md`, "Health badges lead the facts line".
13. Facts for an account with no `RobloxUserId` are Robux only until the backfill resolves it.
14. The membership badge reads Plus for any `true` from validate-membership; the endpoint cannot tell
    Plus from legacy Premium, and Premium is closed to new members.
15. Unsaved Recent icons are fetched lazily through the facts worker, on screen only, and cached in a
    plaintext `game-icons.json`.
16. `GamesPage` is rewritten around a view model rather than grown in place.
17. Play as is a `ContextMenu`, not a window, so no modal or chrome fence moves.
18. The one-account fit bar is measured at 900×600 (the XAML default) at 96 and 120 DPI; 125% is 120 DPI.
19. With one account, Launch multiple, Squad Launch and the batches dot are collapsed, not merely moved
    below the fold; they return the moment a second account exists.
20. The facts age hint and last played are shown under streamer mode; only Robux, membership, friends
    online and the note are masked.
