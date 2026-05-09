# ROROROblox — persist `RobloxUserId` Build Checklist

**Cycle:** v1.3.x persist `RobloxUserId` (cycle 5 — follows cycle-4 cookie-container fix + Roblox-already-running gate, which shipped 2026-05-08 via PR #6 / v1.3.1.0)
**Cycle type:** Spec-first cycle (pattern mm). Substantive design at [`docs/superpowers/specs/2026-05-08-persist-roblox-user-id-design.md`](superpowers/specs/2026-05-08-persist-roblox-user-id-design.md). [`spec.md`](spec.md) is a pointer-stub to that canonical doc.
**Build mode:** autonomous-with-verification (one checkpoint after item 4 — backfill orchestrator wired; verify behavior on a real box before doc/security pass)
**Comprehension checks:** off
**Git cadence:** commit after each item
**Branch:** `feat/persist-roblox-user-id` cut from `main` at item 1 start (already current)
**Repo:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)

**Effort estimates:** wall-clock guesses for autonomous mode. Total cycle ≈ 90–120 minutes including checkpoint.

---

- [ ] **1. `Account.RobloxUserId` field + AccountStore round-trip + 6-case TDD test suite**
  Spec ref: `spec.md > §4.1`, `§4.5`; canonical spec §4.1, §4.5, §7
  Effort: ~25–30 min
  Dependencies: none — Core-only schema change, no glue yet
  What to build: Test-driven implementation. Write the 6 round-trip tests first, watch them fail, then make the schema change to green.
  - **New test file:** `src/ROROROblox.Tests/AccountStoreRobloxUserIdTests.cs`. Targets `AccountStore` round-trip with the new field. Hand-rolled fakes (zero new dependencies — same pattern as cycles 3 + 4). 6 cases per spec §7:
    1. `Account written with RobloxUserId=42 round-trips correctly` → write Account, read back, verify field.
    2. `Account written without RobloxUserId reads back null` → omit field, read, assert null.
    3. `Old blob without the field decodes with RobloxUserId == null` → write a synthesized v1.3.1.0-shape JSON blob to the encrypted store path (use the existing AccountStore's encryption layer with a fixture record that lacks RobloxUserId), then read via current code. **This is the critical migration test — must fail loud if the JSON serializer's default-handling regresses.**
    4. `UpdateRobloxUserIdAsync sets the field on a previously-null account` → covered in item 2 but stub the call surface here for the field-shape test.
    5. `UpdateRobloxUserIdAsync is idempotent` → calling twice with same value is a no-op (no second disk write).
    6. `UpdateRobloxUserIdAsync throws KeyNotFoundException when account doesn't exist` → defensive contract test.
  - **Implementation:** modify `src/ROROROblox.Core/Account.cs`. Add `long? RobloxUserId = null` as the LAST parameter of the record (after `LocalName`). Place at end of param list with default `null` so existing call sites compile without change. JSON serializer (System.Text.Json) handles null-on-read of old blobs automatically because of the default-value semantics.
  - **Stub `UpdateRobloxUserIdAsync`** in `IAccountStore.cs` + `AccountStore.cs` for tests 4-6 to compile (tests 4-6 actually exercise this method which is fully implemented in item 2). Stub returns `Task.CompletedTask` for test 4 (no-op when already set), throws for test 6 (account-not-found). Item 2 lands the full impl.
  Acceptance: All 6 new tests pass. Existing `AccountStoreTests.cs` still green. `dotnet build src/ROROROblox.Core` clean. `git diff` shows changes only in `Account.cs`, `IAccountStore.cs`, `AccountStore.cs`, and the new test file. No `.csproj` / `.slnx` / `Directory.Packages.props` changes.
  Verify: `dotnet test --filter AccountStoreRobloxUserIdTests`. `dotnet test` (full suite — 280+ existing tests must stay green; the migration test #3 is the regression guard against a forward-compat break). Commit: `feat(core): Account.RobloxUserId field + AccountStore round-trip + 6-case TDD test suite`.

- [ ] **2. `IAccountStore.UpdateRobloxUserIdAsync` granular write**
  Spec ref: `spec.md > §4.2`; canonical spec §4.2
  Effort: ~15 min
  Dependencies: item 1 (needs the field on Account + the test stubs to be live)
  What to build: Replace the item-1 stubs with the real implementation in `src/ROROROblox.Core/AccountStore.cs`.
  - Body: load full account list via existing internal-load helper (whatever `ListAsync` uses), find the matching account by `Id`, replace via `existing with { RobloxUserId = userId }`, persist the encrypted blob via the existing internal-write helper. Single round-trip per call.
  - **Idempotence:** if `existing.RobloxUserId == userId` already, return `Task.CompletedTask` without writing. Saves a disk + DPAPI round-trip on every backfill orchestrator no-op pass.
  - **Contract:** throws `KeyNotFoundException` if `accountId` doesn't match any saved account. Defensive — backfill orchestrator only calls with IDs it just enumerated, but the contract is honest.
  - Tests 4, 5, 6 from item 1 should now go from "pending stub returns" to "real-impl passes."
  Acceptance: Tests 4, 5, 6 pass with the real implementation. `dotnet test` full suite green. The granular write is verifiably more efficient than a full account-list rewrite (the implementation only modifies one record's encrypted slot, not all of them).
  Verify: `dotnet test --filter AccountStoreRobloxUserIdTests`. `dotnet test` (full suite). Commit: `feat(core): IAccountStore.UpdateRobloxUserIdAsync granular write`.

- [ ] **3. MainViewModel — persist `RobloxUserId` on opportunistic resolution paths**
  Spec ref: `spec.md > §4.3`; canonical spec §4.3, §6
  Effort: ~20 min
  Dependencies: item 2 (needs `UpdateRobloxUserIdAsync` to be callable)
  What to build: Wire the persist call at three existing sites in `src/ROROROblox.App/ViewModels/MainViewModel.cs` per spec §4.3. Each site gains a parallel `await _accountStore.UpdateRobloxUserIdAsync(summary.Id, userId)` after the in-memory set, wrapped in a soft-fail try/catch with `_log.LogDebug` only.
  - **Site 1 — `:543` (validation pass):** already inside a session-validation loop with its own outer try/catch. Add the persist call after `summary.RobloxUserId = profile.UserId;`. Inner try/catch is mandatory — persist failure must not bubble to the outer validation flow's banner.
  - **Site 2 — `:617` (account add):** already inside `OnCookieCapturedAsync`. The captured `UserId` comes from cookie capture (`captured.UserId`), not GetUserProfileAsync. Persist that value too.
  - **Site 3 — `:888` (Friends modal lazy resolve):** already inside `OpenFriendFollowAsync`. Add the persist call after `summary.RobloxUserId = userId;`.
  - **New test file (or extension):** `src/ROROROblox.Tests/MainViewModelPersistUserIdTests.cs`. Hand-rolled `FakeAccountStore` with an `UpdateRobloxUserIdAsync` capture-list. 3 cases (one per site) — verify each call site invokes `UpdateRobloxUserIdAsync` with the right `(accountId, userId)` pair at the right moment in the flow.
  - **Soft-fail invariant** (spec §6 — non-negotiable): persist throws → caller continues, single LogDebug entry, no banner, no toast, no UI surface. Add a 4th defensive case if convenient: `_accountStore.UpdateRobloxUserIdAsync throws → site 1's validation pass still succeeds for the in-memory state.`
  Acceptance: 3 new tests pass (or 4 with the soft-fail defensive case). Existing tests still green. Three sites each persist on the happy path; persist failures stay invisible. Manual smoke (deferred to checkpoint): one Launch As after upgrade should backfill via site 2 even if the eager pass at item 4 hasn't fired yet.
  Verify: `dotnet test --filter MainViewModelPersistUserIdTests`. `dotnet test` (full suite). Commit: `feat(app): MainViewModel — persist RobloxUserId on opportunistic resolution paths`.

- [ ] **4. `AccountUserIdBackfillService` — eager one-time backfill with stagger + 6-case TDD test suite**
  Spec ref: `spec.md > §4.4`, `§5`, `§7`; canonical spec §4.4, §5, §6, §7
  Effort: ~30–40 min
  Dependencies: item 2 (the write API) + item 3 (the persist-on-resolve invariant for opportunistic recovery if eager pass fails)
  What to build: Test-driven implementation — write the 6 orchestrator tests first, watch them fail, implement to green.
  - **New test file:** `src/ROROROblox.Tests/AccountUserIdBackfillServiceTests.cs`. Hand-rolled `FakeAccountStore` (with `ListAsync`, `RetrieveCookieAsync`, `UpdateRobloxUserIdAsync` capture-lists) + `FakeRobloxApi` (with `GetUserProfileAsync` capture-list + per-call result/throw config — same pattern as the cycle-3 `FakeRobloxApi`). Use `NullLogger<AccountUserIdBackfillService>.Instance` or a `ListLogger<T>` capture-fake (same pattern as cycle-4 `StartupGateTests`). 6 cases per spec §7:
    1. `RunAsync with all accounts already backfilled is a no-op` → zero `GetUserProfileAsync` calls, zero `UpdateRobloxUserIdAsync` calls. Confirms the idempotence-skip works at the orchestrator level.
    2. `RunAsync with all accounts missing resolves and persists each one in order` → N `GetUserProfileAsync` calls, N `UpdateRobloxUserIdAsync` calls, in account-list order.
    3. `RunAsync with mixed (some have userId, some null) only resolves the null ones` → exactly the missing count of API + persist calls.
    4. `RunAsync continues to next account when GetUserProfileAsync throws on one` → N-1 successful persists, 1 LogDebug warning, no exception bubbles.
    5. `RunAsync continues to next account when UpdateRobloxUserIdAsync throws on one` → same shape; persist-fail doesn't poison subsequent accounts.
    6. `RunAsync respects CancellationToken — stops mid-loop, doesn't leave partial state` → cancel after first persist, verify only first account was persisted.
  - **Implementation:** `src/ROROROblox.App/Startup/AccountUserIdBackfillService.cs` per spec §4.4. Body matches the spec verbatim: list accounts → filter to missing → log one INFO line with the count → loop with try/catch + 2.5s + ±500ms jitter delay. Constructor takes `IAccountStore`, `IRobloxApi`, `ILogger<AccountUserIdBackfillService>`. Lives in `App/Startup/` next to existing `IStartupRegistration` (run-on-login) — both are "things that run during app startup."
  - **DI registration in `App.xaml.cs:ConfigureServices`** — register as singleton next to the existing Startup-namespace services:
    ```csharp
    services.AddSingleton<AccountUserIdBackfillService>();
    ```
  - **Trigger in `App.xaml.cs:RunStartupChecksAsync`** — add a new try-block after the existing fire-and-forget startup checks (compat banner, scan, auto-launch-main) per spec §4.4:
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
  - **Anti-fraud spike (spec §5, build-time):** smoke against a real account during checkpoint. If the v1.1.2.0 trip-wire fires (surprise 2FA prompt or session-expired flag during backfill), pause and try the candidate endpoints from spec §5 in this order before swapping:
    1. Decode userId directly from `.ROBLOSECURITY` cookie payload (5-min inspection — if it's a structured token with userId, we skip the API call entirely)
    2. `auth.roblox.com/v1/account/pin/status` — different endpoint, may not flag
    3. `users.roblox.com/v1/users/authenticated/agreement-versions` — variant of the same endpoint, possibly different heuristic
    If the first endpoint that works is found, swap the orchestrator's call site + banner-correct the canonical spec at item 5.
  Acceptance: All 6 new tests pass. `dotnet test` full suite still green. `App.xaml.cs` diff scope: DI registration line + RunStartupChecksAsync trigger block. Zero csproj/slnx changes.
  Verify: `dotnet test --filter AccountUserIdBackfillServiceTests`. `dotnet test` (full suite). `git diff src/ROROROblox.App/App.xaml.cs` shows ONLY the two intended edits. Commit: `feat(app): AccountUserIdBackfillService — eager one-time backfill with stagger + 6-case TDD test suite`.

- [ ] **CHECKPOINT 1** (after item 4 — backfill wired; manual smoke per spec §5 + §7 before docs/security pass)
  Run on the actual dev box (the one Este used for cycle-4 smoke — has multiple saved accounts with mixed `RobloxUserId is null` state from accounts.dat written before this cycle).
  - **Path A — fresh-start backfill:** Quit any running RoRoRo. `dotnet build`, then start `bin/Debug/.../ROROROblox.App.exe`. Open the log file at `%LOCALAPPDATA%\ROROROblox\logs\rororoblox-*.log`. **Verify:** within ~5–25s of MainWindow.Show, log shows ONE info line `"Backfill: N accounts missing RobloxUserId; resolving sequentially."` then N debug lines `"Backfilled RobloxUserId={UserId} for {AccountId}"`. No warning entries, no banner in the UI, no surprise 2FA prompts. Multi-instance still ON (cyan tray icon, mutex held).
  - **Path B — second-launch idempotence:** quit + restart RoRoRo. **Verify:** log shows NO backfill activity (the "missing" count would be zero, and the spec says we don't even log the info line in that case). Single disk read + return. The Friends modal on any account renders the chip strip with all OTHER accounts' chips visible (no "userId not yet known" graceful-fail messages).
  - **Path C — anti-fraud trip-wire check:** during Path A's backfill, watch a saved account's session state in the UI. **If any account flips to `SessionExpired` or surfaces a "verify your identity" prompt during the backfill window, the v1.1.2.0 trip-wire is still active.** Pause the cycle and run the endpoint spike per item 4's anti-fraud bullet. If clean, proceed.
  - **Path D — fault-injection smoke (optional):** temporarily throw `new HttpRequestException("smoke")` at the top of `IRobloxApi.GetUserProfileAsync`, rebuild, restart RoRoRo. **Verify:** backfill loop hits the throw on every account, log shows N debug warnings, NO multi-instance regression, NO banner. Revert before item 5.
  - **Path E — follow-feature unblock:** still in Path B's second-launch state, click any account's `Follow:` chip targeting another account. **Verify:** launch dispatches; previously-broken graceful-fail no longer triggers because both accounts now have persisted `RobloxUserId`.
  - If any path is shaky (especially Path C — anti-fraud regression is the highest risk), fix before proceeding to item 5. The endpoint spike option exists exactly for this.

- [ ] **5. Documentation & Security Verification**
  Spec ref: Cart-required final + `spec.md > §11 Decisions to log on completion`; canonical spec §11
  Effort: ~10–15 min
  Dependencies: item 4 + checkpoint 1 (all paths smoked clean, including anti-fraud check)
  What to build (verification + documentation, no production code):
  - **README touch-up:** if any user-visible flow text mentions the follow feature or "userId not yet known" error, add a line about the auto-resolve behavior. If the README doesn't surface this, no update needed (the whole point is silent improvement).
  - **Spec banner-correct (only on drift):** if endpoint swap happened in item 4 (anti-fraud trip-wire fired), banner-correct `docs/superpowers/specs/2026-05-08-persist-roblox-user-id-design.md` per pattern v from Vibe Thesis. Name the original endpoint (`GetUserProfileAsync`) vs the swapped-to endpoint + the smoke result that justified the swap. **Do NOT rewrite the spec top-to-bottom.** Other drift triggers below.
  - **Secrets scan:** `git diff main...HEAD` reviewed for `.ROBLOSECURITY` literals, PFX/cert bytes, or any cookie-shaped string. Pre-commit hook should have run clean on every cycle commit; confirm. Manual eyeball for belt-and-suspenders.
  - **Local-path audit:** `git diff main...HEAD` reviewed for `c:\Users\` references in committable code (per pattern kk from wbp-azure). Pre-commit hook covers this; manual confirm anyway.
  - **Dependency audit:** `git diff main...HEAD -- '*.csproj' 'Directory.Packages.props' '*.sln' '*.slnx'` should show **zero changes** — this cycle adds zero new dependencies (canonical spec §3 explicit). Any non-zero diff is a red flag.
  - **Deployment-security check:** DPAPI envelope shape unchanged (cycle 5 adds a JSON field, NOT a new envelope version). Confirm by inspecting `IAccountStore.WriteAsync` / encrypt path call sites in the diff (should be no envelope-version bump or magic-byte change). Verify no test-file fixtures contain `01 00 00 00 D0 8C 9D DF` byte changes.
  - **Forward-compat regression test (already exists from item 1):** confirm `AccountStoreRobloxUserIdTests` test #3 (old blob without field decodes with `null`) is in the test suite and still green. This is the load-bearing test that makes the migration a no-op.
  - **Decisions log to 626 Labs Dashboard** via `mcp__626Labs__manage_decisions log`, three entries per canonical spec §11:
    1. Architectural — "Cycle 5 persists `RobloxUserId` on the `Account` record (was in-memory-only on `AccountSummary`). Backfill split: opportunistic at 3 existing resolution paths (validation pass, account add, Friends modal) + one-time eager pass triggered ~5s after MainWindow.Show via fire-and-forget. Reason: opportunistic alone leaves a window where existing accounts' userIds aren't known; eager-only would re-run unnecessarily. Combined approach is idempotent."
    2. Rate-limiting discipline — "Backfill is sequential with 2-3s stagger ± 500ms jitter, runs once-per-account (idempotent), 5s after MainWindow.Show. Diverges from v1.1.2.0 startup-validation trip-wire on five dimensions (idempotent, sequential, paced, post-paint, scoped to missing-only). [If endpoint was swapped: add the swapped endpoint + smoke result that justified the swap.]"
    3. Schema migration — "Added `long? RobloxUserId` to the persisted `Account` record. Default `null`. No DPAPI envelope bump, no migration script. JSON serializer reads existing v1.3.1.0 blobs cleanly; first opportunistic or eager resolve writes the field. Forward-compat verified by fixture-based test that decodes a synthesized v1.3.1.0-shape blob with the current Account record."
    Tag all three with the bound RORORO project ID (`PBWgg5mimZyAzAG3niAp`).
  - **Final test suite pass:** `dotnet test` — full suite, all green. Tests added in items 1, 3, 4 + every existing test from cycles 1-4.
  Acceptance: `dotnet test` 100% green. `git diff main...HEAD --stat` shows expected files only (`Account.cs`, `IAccountStore.cs`, `AccountStore.cs`, `AccountUserIdBackfillService.cs`, `App.xaml.cs`, `AccountStoreRobloxUserIdTests.cs`, `MainViewModelPersistUserIdTests.cs`, `AccountUserIdBackfillServiceTests.cs`, optional README touch + optional canonical-spec banner). Pre-commit hooks (`secret-scan`, `local-path-guard`) ran clean across all cycle commits. Three decisions logged to dashboard. No new dependencies in any `.csproj`. Spec banner-correct only if drift was real.
  Verify: `dotnet test`. `git diff --stat main...HEAD`. `git log main..HEAD --oneline` reviewed for shape (5 commits visible — items 1-4 + this one). Browse the dashboard to confirm three new decisions tagged with the RORORO project ID. Commit (only if README/spec changed): `docs: README + spec banner-correct (only on drift)`.

---

## Risk callouts logged for `/build`

- **Item 4's anti-fraud trip-wire is the highest cycle risk.** v1.1.2.0 explicitly removed a startup validation pass for hitting `users.roblox.com/v1/users/authenticated`. Cycle 5's backfill calls the SAME endpoint via `GetUserProfileAsync`, just smarter timing (sequential, stagger, idempotent, post-paint, missing-only). If the heuristic still fires, Path C of the checkpoint catches it. The endpoint-spike candidates are pre-listed in spec §5 + item 4 — work them in order, banner-correct on swap.
- **Item 1 test #3 (forward-compat blob decode) is the schema-migration regression guard.** If this test ever turns flaky or gets skipped, ship is blocked — it's the only defense against a future change to `Account` quietly breaking existing users' accounts.dat reads. Don't accept "it builds, ship it" without this test green.
- **Soft-fail discipline at the 3 opportunistic-persist sites is non-negotiable.** Per spec §6, persist failure NEVER bubbles. The "persist-fail blocks the validation pass" regression class would surface as Multi-Instance going to Error state when accounts.dat is full / locked / corrupt — which is exactly the wrong moment to add a new failure surface. Fault-injection test (item 3 case 4) is the regression guard.
- **Item 4's 5-second post-MainWindow.Show delay is a vibe number, not a calibrated measurement.** Choose adjustment if smoke shows the backfill firing before mutex.Acquire settles or before the user's first interaction. 5s comfortably covers `RunStartupChecksAsync`'s existing fire-and-forgets; if it interferes, drop to 3s or push to 10s.
- **Cycle 5.5 (friends-list display drift) is parallel and independent.** Don't conflate. If during Path E of the checkpoint the chip strip renders correctly userId-wise but friend names + avatars are still missing, that's confirmation cycle 5.5 is still needed. Note for the next cycle, don't try to fix it here.

## Spec banner-correct triggers

If any of these happen during build, banner-correct the canonical spec at `docs/superpowers/specs/2026-05-08-persist-roblox-user-id-design.md` per pattern v from Vibe Thesis:

- **Endpoint swap.** v1.1.2.0 trip-wire fires during Path C smoke and we swap `GetUserProfileAsync` for one of the spec §5 candidates. Banner-correct names the swap + the smoke evidence.
- **Cookie payload introspection works.** If the 5-minute spec-§5 spike on `.ROBLOSECURITY` reveals a structured payload with userId, the orchestrator becomes pure-local with zero network calls. That's a meaningful architectural shift — banner-correct names it as the path taken instead of the network-call approach.
- **Backfill timing.** 5s post-MainWindow.Show ends up too short or too long; ship with the verified-good number named.
- **Stagger interval.** 2-3s ± 500ms ends up needing adjustment (faster if smoke shows zero anti-fraud signal, slower if there's any flag).
- **Site placement.** One of the 3 opportunistic-persist sites turns out to be in a flow we don't actually want to persist from (e.g., validation pass during a recovery scenario). Banner-correct names the moved/removed site.
- **`UpdateRobloxUserIdAsync` signature.** If implementation reveals a cleaner shape (e.g., `(Account account)` instead of `(Guid id, long userId)`), banner-correct names the actual signature.

If none of these happen — implementation matched spec — skip banner-correct entirely. Banner-corrects are for genuine drift, not ceremony.
