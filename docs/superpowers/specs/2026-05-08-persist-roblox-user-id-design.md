# Persist `RobloxUserId` on saved accounts — Design Spec

**Version:** v1.3.x feature (cycle 5 — follows cycle-4 cookie-container fix)
**Date:** 2026-05-08
**Status:** Approved for implementation planning
**Branch (implementation):** `feat/persist-roblox-user-id` (cut from `main` after spec lands)
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

## 1. Overview

The follow feature (per-row "Follow:" chips, "Friends" button → `FriendFollowWindow`, `LaunchTarget.FollowFriend`) needs each saved account's Roblox `userId` to render meaningfully. Today, `RobloxUserId` is in-memory-only on `AccountSummary` (the runtime ViewModel wrapper) — not persisted. Three existing paths set it lazily:

- `MainViewModel.cs:617` — when an account is first added (uses `captured.UserId` from cookie capture)
- `MainViewModel.cs:543` — during session validation passes (`profile.UserId` from `GetUserProfileAsync`)
- `MainViewModel.cs:888` — when the user opens the Friends modal for an account (lazy resolve)

**Every app restart loses the cache.** Until the user touches the cookie via one of those paths, `RobloxUserId is null` and `MainViewModel.cs:1251` surfaces a graceful-fail StatusBanner: *"Couldn't follow {target.DisplayName} — Roblox userId not yet known."*

**The fix is two-part:** persist `RobloxUserId` in the saved-account record, and run a one-time eager backfill on first launch of v1.next so existing users don't have to re-add accounts or wait for ad-hoc cookie touches. Once persisted, follow works without ceremony for all accounts on every subsequent session.

The technical core is small: one new optional field on `Account`, one new granular `IAccountStore` method, three persist-on-resolve glue points in `MainViewModel`, and a fire-and-forget background worker that stagger-resolves missing userIds.

## 2. Goals and non-goals

**Goals (cycle 5):**

- Add `long? RobloxUserId` to the persisted `Account` record. JSON serializer reads existing `accounts.dat` blobs with `null` for the new field — zero migration script, DPAPI envelope shape unchanged.
- Add `IAccountStore.UpdateRobloxUserIdAsync(Guid accountId, long userId)` — granular write so backfill doesn't have to round-trip the full Account record on every account.
- Persist whenever existing code currently sets `AccountSummary.RobloxUserId` (3 call sites). Opportunistic backfill survives restarts even without the eager pass.
- Run a one-time eager backfill on first launch of v1.next — fire-and-forget worker triggered ~5s after `MainWindow.Show()` from the existing `RunStartupChecksAsync` surface. Sequentially resolves missing userIds with 2-3s stagger between accounts, idempotent (skips accounts that already have a userId).
- Soft-fail discipline: backfill failures NEVER surface to user (no banner, no toast, no log above Debug level). Multi-instance launch path NEVER blocks on backfill.

**Non-goals (cycle 5):**

- **UI surface for backfill progress.** Zero user-visible. The whole point is "the next time you open RoRoRo, follow just works."
- **Re-resolution for already-persisted userIds.** If a Roblox user account is renamed server-side, the persisted `(userId, displayName)` pair could go stale. Roblox account renames are rare and `displayName` is not load-bearing for follow (we route by userId). Defer to a future cycle if it surfaces.
- **Validation of returned userId.** We trust whatever `GetUserProfileAsync` (or whatever endpoint we settle on) returns. No cross-check against the cookie's encoded identity.
- **Fix the friends-list display bug** (cycle 5.5 candidate — names + avatars not rendering due to suspected Roblox-side API field drift). Independent root cause; doesn't block cycle 5.
- **Aggregate online-friends sheet** (cycle 6+ candidate). Builds on top of cycle 5 + cycle 5.5; not in scope here.
- **Recent-games trail per account** (cycle 6+ candidate). Independent.
- **Live "what game is this alt in" in the row** (cycle 6+ candidate). Independent.

## 3. Stack

No new dependencies. Reuses what's already in the app:

- `IAccountStore` + DPAPI-encrypted `accounts.dat` — existing storage substrate.
- `IRobloxApi.GetUserProfileAsync(cookie)` — existing API call that already returns `UserProfile.UserId`. Used today by `MainViewModel.cs:541` for validation passes and `:887` for Friends-modal lazy resolve.
- `App.xaml.cs:RunStartupChecksAsync` — the existing fire-and-forget startup surface where update checks + compat banner already run. New backfill orchestrator lives next to those.
- `Microsoft.Extensions.Logging.Abstractions` — already in Core for `ILogger`.

## 4. Architecture and change surface

Five files. Schema + storage + glue + new orchestrator + tests.

### 4.1 `src/ROROROblox.Core/Account.cs` (MODIFIED)

Add one optional field:

```csharp
public sealed record Account(
    Guid Id,
    string DisplayName,
    string AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLaunchedAt,
    bool IsMain = false,
    int SortOrder = 0,
    bool IsSelected = true,
    string? CaptionColorHex = null,
    int? FpsCap = null,
    string? LocalName = null,
    long? RobloxUserId = null);  // NEW (cycle 5, 2026-05-08)
```

Field placed at the end of the parameter list as an optional with default `null` so existing call sites compile without change. JSON serializer (System.Text.Json) reads old blobs with `RobloxUserId == null`, writes new blobs with the field present.

### 4.2 `src/ROROROblox.Core/IAccountStore.cs` (MODIFIED) + `AccountStore.cs` (MODIFIED)

Add a granular write method to the interface:

```csharp
/// <summary>
/// Persist the resolved Roblox <paramref name="userId"/> for the saved account
/// identified by <paramref name="accountId"/>. Idempotent — no-op if the account
/// already has the same value. Throws if the account doesn't exist.
/// </summary>
Task UpdateRobloxUserIdAsync(Guid accountId, long userId);
```

`AccountStore` implementation: load full account list, find the matching account, replace via `with` syntax (`existing with { RobloxUserId = userId }`), persist the encrypted blob. Single round-trip per call. Throws `KeyNotFoundException` if `accountId` isn't found (defensive — backfill orchestrator only calls with IDs it just enumerated, but the contract is honest).

### 4.3 `src/ROROROblox.App/ViewModels/MainViewModel.cs` (MODIFIED)

Three call sites where `summary.RobloxUserId` is set today gain a parallel persist call wrapped in a soft-fail try/catch. Pattern:

```csharp
summary.RobloxUserId = profile.UserId;
try
{
    await _accountStore.UpdateRobloxUserIdAsync(summary.Id, profile.UserId);
}
catch (Exception ex)
{
    _log.LogDebug(ex, "Couldn't persist RobloxUserId for {AccountId}; will retry on next resolution.", summary.Id);
}
```

The three sites:

- **`:543` (validation pass)** — already inside a session-validation loop with its own outer try/catch. Add the persist call after the in-memory set.
- **`:617` (account add)** — already inside `OnCookieCapturedAsync`. The captured `UserId` comes from cookie capture, not GetUserProfileAsync; persist that value too.
- **`:888` (Friends modal lazy resolve)** — already inside `OpenFriendFollowAsync`. Add the persist call after the in-memory set.

Soft-fail at each site is mandatory. Persistence failure NEVER bubbles to the user-visible action.

### 4.4 `src/ROROROblox.App/Startup/AccountUserIdBackfillService.cs` (NEW)

Fire-and-forget orchestrator. Triggered from `App.xaml.cs:RunStartupChecksAsync` ~5s after `MainWindow.Show()` (next to the existing update-check + compat-banner fire-and-forgets). Body:

```csharp
public sealed class AccountUserIdBackfillService
{
    private const int InterAccountDelayMs = 2500;  // 2.5s stagger; jittered ±500ms

    private readonly IAccountStore _store;
    private readonly IRobloxApi _api;
    private readonly ILogger<AccountUserIdBackfillService> _log;

    public AccountUserIdBackfillService(IAccountStore store, IRobloxApi api, ILogger<AccountUserIdBackfillService> log) { ... }

    /// <summary>
    /// One-time eager pass. Resolves and persists RobloxUserId for every saved account
    /// that doesn't already have one. Idempotent — once persisted, never re-runs for
    /// that account on subsequent invocations. Soft-fails silently per account.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var accounts = await _store.ListAsync().ConfigureAwait(false);
        var missing = accounts.Where(a => a.RobloxUserId is null).ToList();
        if (missing.Count == 0) return;

        _log.LogInformation("Backfill: {Count} accounts missing RobloxUserId; resolving sequentially.", missing.Count);

        foreach (var account in missing)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var cookie = await _store.RetrieveCookieAsync(account.Id).ConfigureAwait(false);
                if (string.IsNullOrEmpty(cookie)) continue;
                var profile = await _api.GetUserProfileAsync(cookie).ConfigureAwait(false);
                if (profile.UserId <= 0) continue;
                await _store.UpdateRobloxUserIdAsync(account.Id, profile.UserId).ConfigureAwait(false);
                _log.LogDebug("Backfilled RobloxUserId={UserId} for {AccountId}", profile.UserId, account.Id);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Backfill skipped {AccountId}; will retry next session.", account.Id);
            }

            await Task.Delay(InterAccountDelayMs + Random.Shared.Next(-500, 500), ct).ConfigureAwait(false);
        }
    }
}
```

DI registration in `App.xaml.cs:ConfigureServices` next to the existing Startup-namespace services. Trigger in `RunStartupChecksAsync`:

```csharp
try
{
    var backfill = _services.GetRequiredService<AccountUserIdBackfillService>();
    await Task.Delay(5_000).ConfigureAwait(false);  // let MainWindow paint + Multi-Instance settle
    await backfill.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    _log?.LogDebug(ex, "Backfill threw; ignoring.");
}
```

### 4.5 Migration — none required

Existing `accounts.dat` blobs decode cleanly into the new `Account` shape because `RobloxUserId` has a default of `null`. No version bump on the DPAPI envelope. No one-shot migration script. First write of an account after upgrade includes the new field; subsequent reads round-trip correctly.

### 4.6 Follow path — zero code changes

The graceful-fail at `MainViewModel.cs:1251-1256` starts succeeding once persisted userIds exist. No ergonomic improvements to the follow UI in cycle 5 — the value is in the data layer being correct.

## 5. Anti-fraud / rate-limiting discipline

This is the v1.1.2.0 lesson, restated. The startup session-validation pass (`vm.ValidateSessionsAsync()`) was removed because hitting `users.roblox.com/v1/users/authenticated` for every saved account at startup pattern-matched Roblox's anti-fraud heuristics. That endpoint is the same one our `GetUserProfileAsync` call uses today. The risk is real and we're not pretending it isn't.

**How cycle 5's eager backfill diverges from the v1.1.2.0 trip-wire:**

| Dimension | Removed v1.1.2.0 validation pass | Cycle 5 eager backfill |
|---|---|---|
| **Trigger** | Every app startup | First launch only — idempotent, skipped once each account is backfilled |
| **Concurrency** | All accounts in parallel (within `Task.WhenAll`) | Sequential, one account at a time |
| **Pacing** | None (back-to-back) | 2-3 second stagger with ±500ms jitter |
| **Timing** | At startup, before user interaction | ~5 seconds after MainWindow.Show, after Multi-Instance is up |
| **Scope** | Every account every time | Only accounts where `RobloxUserId is null` |

**Practical impact for a returning user with 6 backfilled accounts on v1.next launch:** zero API calls. Service inspects accounts.dat, sees all 6 have `RobloxUserId` set, returns. Cost is one disk read.

**Practical impact for a new user upgrading from v1.1.2.0 with 6 unpersisted accounts:** ~15-18 seconds of background HTTP activity, 2-3 seconds apart, 5 seconds after MainWindow shows. Looks like normal user-driven browsing of Roblox profile pages, not bulk programmatic enumeration.

**Open question — endpoint choice (build-time spike if needed):** `users.roblox.com/v1/users/authenticated` is the same endpoint v1.1.2.0 flagged. If smoke testing shows the trip-wire still fires for the staggered version, swap to a less-heuristic-sensitive endpoint that returns userId as a side-effect. Candidates worth investigating during build:

- `auth.roblox.com/v1/account/pin/status` — returns identity-bound state, may include userId
- `users.roblox.com/v1/users/authenticated/agreement-versions` — variant of the same endpoint, possibly different heuristic profile
- Decode userId directly from `.ROBLOSECURITY` cookie payload — Roblox cookies are opaque tokens, but the embedded session token MAY include userId in a JWT-shaped form. Worth a 5-minute inspection.

If smoke testing shows the trip-wire holds, banner-correct this section with the verified endpoint + retain the stagger.

## 6. Soft-fail discipline

Every awaited call in the backfill orchestrator AND in the three opportunistic-persist sites is wrapped in `try/catch`. Failure consequences:

| Failure mode | Effect | Recovery |
|---|---|---|
| Cookie retrieval throws (DPAPI corrupt, file missing) | Account skipped; `LogDebug` only | Retry next session |
| `GetUserProfileAsync` throws (network, 401, anti-fraud reverification) | Account skipped; `LogDebug` only | Retry next session |
| `UpdateRobloxUserIdAsync` throws (disk full, permission, account deleted mid-pass) | Account skipped; `LogDebug` only | Retry next session |
| `Task.Delay` cancelled (app shutting down) | Loop terminates cleanly, no banner | Resume on next session for un-completed accounts |

Above-`Debug`-level log entries are reserved for the one-line summary at the start (`"Backfill: N accounts missing RobloxUserId..."`). Per-account success is `LogDebug` only — silent in production logs unless verbose-mode is enabled.

The user is never shown a "backfill failed" message. Multi-instance launch is never blocked on backfill state.

## 7. Testing (TDD, ~12 unit cases)

Three test files. All fakes hand-rolled (zero new dependencies — same pattern as cycles 3 + 4).

**`src/ROROROblox.Tests/AccountStoreRobloxUserIdTests.cs`** (NEW). Targets `AccountStore` round-trip with the new field. Cases:

1. `Account written with RobloxUserId=42 round-trips correctly.`
2. `Account written without RobloxUserId reads back null.`
3. `Old blob without the field decodes with RobloxUserId == null` (forward-compat — write a fixture file representing v1.1.2.0 shape, read it via current code).
4. `UpdateRobloxUserIdAsync sets the field on a previously-null account.`
5. `UpdateRobloxUserIdAsync is idempotent — calling twice with same value is a no-op.`
6. `UpdateRobloxUserIdAsync throws KeyNotFoundException when account doesn't exist.`

**`src/ROROROblox.Tests/AccountUserIdBackfillServiceTests.cs`** (NEW). Targets the orchestrator. Hand-rolled `FakeAccountStore` + `FakeRobloxApi`. Cases:

1. `RunAsync with all accounts already backfilled is a no-op` (zero API calls, zero store writes).
2. `RunAsync with all accounts missing resolves and persists each one in order.`
3. `RunAsync with mixed (some have userId, some null) only resolves the null ones.`
4. `RunAsync continues to next account when GetUserProfileAsync throws on one.`
5. `RunAsync continues to next account when UpdateRobloxUserIdAsync throws on one.`
6. `RunAsync respects CancellationToken — stops mid-loop, doesn't leave partial state.`

**`src/ROROROblox.Tests/MainViewModelPersistUserIdTests.cs`** (NEW or extension of existing). Each of the three opportunistic-persist sites verified to call `IAccountStore.UpdateRobloxUserIdAsync` at the right moment. ~3 cases (one per site). Existing test pattern from `JoinByLinkSaveTests` + `RenameDispatchTests` applies directly.

Total: ~12-15 cases. Existing 280 tests must continue green.

## 8. Branch + commit plan

**Branch:** `feat/persist-roblox-user-id` cut from `main` after this spec lands. Five commits:

1. `feat(core): Account.RobloxUserId field + AccountStore round-trip + 6-case TDD test suite`
2. `feat(core): IAccountStore.UpdateRobloxUserIdAsync granular write`
3. `feat(app): MainViewModel — persist RobloxUserId on opportunistic resolution paths`
4. `feat(app): AccountUserIdBackfillService — eager one-time backfill with stagger + 6-case TDD test suite`
5. `docs: README + spec banner-correct (only on drift)`

PR opens against `main`. Review checklist: full `dotnet test` green, manual smoke on a clean Win11 box (start v1.next from bin/Debug, verify backfill log line appears once with "N accounts missing" then "0 accounts missing" on second start, verify follow chips render correctly for all accounts on second start).

## 9. Out of scope (deliberate)

- UI surface for backfill progress (silent by design)
- Re-resolution for already-persisted userIds
- Roblox API contract drift fixes for the friends-list display bug (cycle 5.5)
- Aggregate friends sheet, recent-games trail, live game-in-row (cycle 6+)
- Endpoint replacement for `GetUserProfileAsync` if the trip-wire holds — listed as build-time spike in §5

## 10. Open questions / future

- **Endpoint trip-wire (build-time spike).** If staggered + first-launch-only timing still triggers Roblox's anti-fraud heuristic, swap endpoints. §5 lists candidates.
- **Cookie payload introspection.** `.ROBLOSECURITY` may encode userId in a structured way that lets us skip the API call entirely. 5-minute inspection during build phase. If it works, eager-backfill becomes a pure local operation with zero network calls.
- **Concurrency cap.** Current design is fully sequential. If we ever expand this to other one-time-pass features, we may want a generic "rate-limited backfill queue" abstraction. Not for cycle 5.

## 11. Decisions to log on completion

After implementation merges, log to the 626 Labs Dashboard via `mcp__626Labs__manage_decisions log`:

- **Architectural choice:** "Cycle 5 persists `RobloxUserId` on the `Account` record (was in-memory-only on `AccountSummary`). Backfill is split: opportunistic at the 3 existing resolution paths (validation pass, account add, Friends modal), plus a one-time eager pass triggered ~5s after MainWindow.Show via fire-and-forget. Reason: opportunistic alone leaves a window where existing accounts' userIds aren't known; eager-only would re-run unnecessarily. Combined approach is idempotent (once persisted, both paths skip)."
- **Rate-limiting discipline:** "Backfill is sequential with 2-3s stagger ± 500ms jitter, runs once-per-account (idempotent), 5s after MainWindow.Show. Diverges from the v1.1.2.0 startup-validation trip-wire on five dimensions (idempotent, sequential, paced, post-paint, scoped to missing-only). If the v1.1.2.0 anti-fraud heuristic still fires during smoke, swap GetUserProfileAsync to a less-flagged endpoint per spec §5 candidates."
- **Schema migration:** "Added `long? RobloxUserId` to the persisted `Account` record. Default is `null`. No DPAPI envelope bump, no migration script. JSON serializer reads existing v1.3.1.0 blobs cleanly; first opportunistic or eager resolve writes the field. Forward-compat verified by a fixture-based test that decodes a synthesized v1.3.1.0-shape blob with the current Account record."
