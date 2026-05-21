# RORORO — v1.5.0 Presence Account-UX Build Checklist

**Cycle:** v1.5.0 (credibility hotfix — presence-based account status + Launch multiple hardening)
**Cycle type:** Spec-first cycle (pattern mm). Canonical spec: [`docs/superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md`](superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md). [`spec.md`](spec.md) is a pointer-stub.

## Build Preferences

- **Build mode:** Autonomous
- **Comprehension checks:** N/A (autonomous)
- **Git:** Commit after each item. Conventional commits (`feat` / `fix` / `test` / `refactor` / `docs` / `build`). Branch `v1.5.0-presence-account-ux` (already cut from `main`; spec committed at `18bec93`).
- **Verification:** Yes — checkpoints. **C1 after item 4** (first runnable: rows read "In <game>"; ghost gone on a respawned client). **C2 after item 6** (Launch multiple skip-reason + race). Manual smoke is the v1 trade per canonical spec § Testing — no E2E against real roblox.com.
- **Check-in cadence:** N/A (autonomous)
- **TDD:** strict for Core + ViewModel logic (items 1-3, 5 test-first). UI/DI wiring (item 4) and docs (item 7) are verification-by-running, not unit-tested.

## Effort

Focused hotfix. **Total ≈ 5-7 hours** of autonomous engineering. Heaviest item is 4 (VM/DI wiring + the OnProcessExited anti-ghost change); riskiest external surface is the presence endpoint (item 1).

---

## Checklist

- [x] **1. `IPresenceService` + `PresenceService` poll loop with presence→event mapping + game-name cache**
  Spec ref: `spec.md > Components > 1. PresenceService`
  What to build: New `src/ROROROblox.Core/Diagnostics/IPresenceService.cs` (interface + `AccountPresenceUpdated` event carrying `accountId`, `UserPresenceType`, `placeId`, `gameName`) and `PresenceService.cs` (no WPF deps; lives beside `RobloxProcessTracker`). Constructor: `IRobloxApi`, `IAccountStore`, `ILogger`, poll interval (default 25s), and a snapshot-provider delegate yielding the pollable account set (`id`, `RobloxUserId`, `SessionExpired`). `PeriodicTimer` loop: for each non-expired account with non-null `RobloxUserId`, `RetrieveCookieAsync(id)` → `GetPresenceAsync(cookie, [userId])` → map the single `UserPresence`; if `InGame` with a `PlaceId`, resolve the game name via `GetGameMetadataByPlaceIdAsync(placeId)` cached in a `ConcurrentDictionary<long,string>`; raise `AccountPresenceUpdated`. `Start()` / `Stop()`.
  Acceptance: `InGame` presence raises the event with a resolved game name; a second poll for the same `PlaceId` does **not** refetch metadata (cache hit); `Offline`/`OnlineWebsite` raises the event with a null game name. Existing test suite stays green.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PresenceServiceTests"` (stub `IRobloxApi` + `IAccountStore`). Commit: `feat(presence): PresenceService poll loop + game-name cache`.

- [x] **2. `PresenceService` resilience + fast-confirm re-poll**
  Spec ref: `spec.md > Components > 1. PresenceService` (Concurrency / rate limits, Fast-confirm hook) + `Error handling / edge cases`
  What to build: 401 / `CookieExpiredException` → raise an expired signal so the VM can flip the row to session-expired. HTTP 429 → back off for the remainder of the cycle. Any other failure → **hold last-known** (never emit an event that would clear running state). Concurrency cap (`SemaphoreSlim`) + small jitter so N accounts don't fire simultaneously. Add `RequestImmediateRefreshAsync(accountId)` — an out-of-band single-account poll (the `ProcessExited` subscription is wired in item 4; the method + its behavior land here with tests).
  Acceptance: 401 path raises the expired signal; 429 sets backoff and skips the rest of the cycle; a generic poll failure holds last state (no spurious not-in-game event); `RequestImmediateRefreshAsync` polls exactly the one account.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "PresenceServiceTests"`. Commit: `feat(presence): resilience (401/429/hold-last) + fast-confirm re-poll`.

- [x] **3. `AccountSummary` status reconciliation**
  Spec ref: `spec.md > Components > 2. Status reconciliation`
  What to build: Add `PresenceState` (`UserPresenceType`), `CurrentGameName`, `CurrentPlaceId`, `InGameSinceUtc`, and computed `InGame`. Rewrite `StatusDot` (expired→yellow; `InGame || IsRunning`→green; else grey) and `SecondaryStatusText` with the spec's precedence: Session expired ▸ "In {game} · {age}" ▸ "Connecting…" (first ~60s) / "Running" (after) ▸ explicit `StatusText` ▸ "Closed {ago}" (presence-confirmed, both false) ▸ "Last launched {ago}" ▸ "Ready". Raise the correct `OnPropertyChanged` when `PresenceState` / `CurrentGameName` / `IsRunning` change.
  Acceptance: table-driven tests over `SessionExpired × InGame × IsRunning × LastClosed`. Headline case: `IsRunning=false, InGame=true` → "In {game}", **not** "Closed". `InGame=false, IsRunning=true` within 60s → "Connecting…"; after 60s → "Running". Both false + `LastClosedAtUtc` set → "Closed {ago}".
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "AccountSummaryTests"`. Commit: `feat(status): presence-aware AccountSummary reconciliation`.

- [x] **4. Wire `PresenceService` into `MainViewModel` + DI + lifecycle; defer close-stamp to presence**
  Spec ref: `spec.md > Components > 1` (Lifecycle) + `Components > 2` (anti-ghost `OnProcessExited` change) + `Data flow`
  What to build: Register `IPresenceService` → `PresenceService` as a DI singleton in `App.xaml.cs`, supplying the account-snapshot delegate from `MainViewModel.Accounts`. `MainViewModel` subscribes to `AccountPresenceUpdated` → `Dispatcher.Invoke` → update the matching `AccountSummary` (`PresenceState` / `CurrentGameName` / `CurrentPlaceId` / `InGameSinceUtc`); subscribe the expired signal → set `SessionExpired`. Start the service after accounts load; stop on app exit. Change `OnProcessExited` (`MainViewModel.cs:1091`): clear `RunningPid` + set `IsRunning=false`, but **do not** stamp `LastClosedAtUtc` while `InGame`; instead call `PresenceService.RequestImmediateRefreshAsync(accountId)` so presence confirms or refutes the close (presence stamps `LastClosedAtUtc` only when it sees not-in-game).
  Acceptance: app builds and runs; launching an alt makes its row read "Connecting…" then "In {game}"; a client that Roblox's bootstrapper respawns (tracked pid exits, window stays up) keeps the row "In {game}" instead of flipping to "Closed".
  Verify: `dotnet build` then run; launch 2-3 alts and watch the rows transition. **Checkpoint C1.** Commit: `feat(status): wire presence into MainViewModel + defer close-stamp to presence`.

- [x] **5. Launch multiple hardening**
  Spec ref: `spec.md > Components > 3. Launch multiple hardening`
  What to build: In `LaunchAllAsync` (`MainViewModel.cs:771`) and `SquadLaunchAsync` (`:848`), change the busy test from `!a.IsRunning` to `!(a.InGame || a.IsRunning)`. Before the eligibility snapshot, await a one-shot `PresenceService` refresh of the selected accounts (closes the 67ms race). Replace the zero-eligible banner with the full breakdown ("Nothing to launch — N in a game, M expired, K deselected"). On a partial launch, append the skip-reason tail ("Launched 6 · 1 already in a game (skipped)"). Update `LaunchAllCommand` CanExecute (`MainViewModel.cs:104`) to `!(InGame || IsRunning)`, and set the Launch multiple button tooltip to explain when it's disabled.
  Acceptance: an in-game-but-pid-lost account is excluded; a both-false account is included; the pre-snapshot refresh flips a just-closed alt to eligible; the success banner contains the skip-reason tail; the zero-eligible banner shows the breakdown.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "MainViewModel*"` + manual: close one alt and immediately hit Launch multiple — confirm it's included and the banner names the skips. **Checkpoint C2.** Commit: `fix(launch): presence-aware eligibility + never-silent no-op + skip-reason feedback`.

- [ ] **6. Bump to 1.5.0.0 + release notes**
  Spec ref: `spec.md > Why this exists` + `CLAUDE.md > Conventions`
  What to build: Bump `<Version>` to `1.5.0.0` in the app csproj (and any version-paired manifests — keep them in lockstep per CLAUDE.md). Write v1.5.0 release notes (what changed, builder-to-builder voice): the ghost fix ("your alts now show the game they're actually in, and stop falsely reporting closed"), Launch multiple now tells you what it skipped and why. Note in the canonical spec's status line that build matches design, or banner-correct if reality drifted during items 1-5.
  Acceptance: version reads `1.5.0.0` everywhere it's declared; release notes exist and read in-voice; no version-pair drift.
  Verify: `dotnet build` succeeds at the new version; grep confirms a single consistent version string. Commit: `build: bump to 1.5.0.0 + v1.5.0 release notes`.

- [ ] **7. Documentation & Security Verification**
  Spec ref: `spec.md > Testing` + `spec.md > Risks / open questions` + `CLAUDE.md > What NOT to do`
  What to build: Confirm `docs/` artifacts reflect reality (spec, this checklist, `spec.md` pointer-stub updated to make v1.5.0 the current cycle). Run the secrets scan — **`PresenceService` must never log a cookie or `.ROBLOSECURITY` value** (run the `dpapi-cookie-blast-radius` agent against the new presence path). Run the one-line local-path grep (no `c:\Users\<name>\` in committable code, per pattern kk). Run `dotnet list package --vulnerable` / `dotnet build -warnaserror` sanity on the changed projects. Confirm `.gitignore` still covers `accounts.dat`, `consent.dat`, `*.pfx`, `webview2-data/`. The Store-MSIX + Velopack release itself is builder-driven post-merge (memory: "I drive the full release through Store MSIX build") — this item readies the branch, it does not cut the release.
  Acceptance: no cookie/secret in logs or staged files; no machine-local paths; dependency check clean or findings documented; docs current. Branch ready for PR to `main`.
  Verify: `dpapi-cookie-blast-radius` agent reports clean on the presence path; pre-commit secret-scan + local-path-guard hooks pass; `dotnet build` clean. Commit: `docs: v1.5.0 spec/checklist sync + security verification`.
