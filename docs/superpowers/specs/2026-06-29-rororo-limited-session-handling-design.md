# RORORO — "Limited" (Roblox-flagged session) handling — Design Spec

**Version:** v1.8.x feature add (403 soft-lock handling)
**Date:** 2026-06-29
**Status:** Approved for implementation planning
**Branch (implementation):** `feat/limited-session-handling` (cut from `main`)
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

---

> ## ⚠️ Banner-correct (2026-06-30, post-build — user-reported)
>
> **§1.1's trigger attribution is refined.** The spec framed the 403 as "two accounts reset simultaneously and tripped the captcha." The actual trigger, at least for ItsJustEstePapa, was a **user-initiated "suspicious activity" verification** — the user flagged the account, and Roblox put it into a recurring human-verification (reCAPTCHA) state. The *mechanism* is identical (a flagged session 403s authenticated requests), so the detection + handling design below is unaffected — `SessionLimited` is trigger-agnostic.
>
> **Two behavioral facts learned post-build** (see `docs/investigations/2026-05-07-account-bot-challenge.md`, 2026-06-30 entry):
>
> 1. **The captcha is a Roblox client GAME-JOIN gate, not a web-login gate.** It fires when the client tries to join a game, not at roblox.com login. So `— re-capture or wait` is only half-right: **re-auth (WebView2 re-login) does NOT clear this flavor** (web login has no captcha). The recovery is to solve the check in the client at join, or wait for auto-heal once trust rebuilds.
> 2. **Follow-a-friend joins bypass the gate; cold place-joins don't.** Roblox trusts social joins. RORORO already supports this via `LaunchTarget.FollowFriend` (`RequestFollowUser`), so a flagged account can still get in via its Follow target — but only in the join-gate flavor (auth-ticket fetch still succeeds), not when the cookie 403s at the ticket endpoint itself. Whether to surface a one-click "Join via friend" on Limited rows is an OPEN decision (wall tension — see the follow-up scope doc), not built here.
>
> The body below remains as originally written per the "banner-correct, don't rewrite" rule (CLAUDE.md).

## 1. Overview

After a captcha/bot-challenge event (two accounts reset simultaneously and tripped Roblox's
"Verifying you're not a bot" gate), the affected accounts' `.ROBLOSECURITY` cookies enter a
**flagged-but-alive** state. Roblox returns **HTTP 403 (not 401)** on their authenticated
requests — the auth-ticket fetch, the presence poll, and the client-side ticket redemption.

RORORO has no state for this. The 403 falls through to generic failure handling, so the user sees:

- **"Launch As" does nothing** — the auth-ticket fetch 403s → `LaunchResult.Failed` sets the row's
  `StatusText`, but [`SecondaryStatusText`](../../../src/ROROROblox.App/ViewModels/AccountSummary.cs)
  checks `InGame` (step 2) **before** `StatusText` (step 5), so a frozen-stale presence dot masks
  the error entirely.
- **The "In game" dot lies** — the presence poll 403s every 25 s and `PresenceService` holds
  last-known state forever, so the green "In game" dot freezes on stale data.

### 1.1 Evidence (root cause confirmed, 2026-06-29)

From `%LOCALAPPDATA%\ROROROblox\logs\rororoblox-20260629.log`:

| Account | Roblox userId | Behavior today |
|---|---|---|
| ItsjustesteAgain (`1485b563…`) | 7201794546 | `Launch failed: auth-ticket endpoint returned 403` ×5; **546** presence-poll failures |
| ItsJustEstePapa (`cd708455…`) | 5455395770 | Auth-ticket fetch **succeeds**; **client** 403s redeeming the ticket (the error dialog) |
| estehernandez (`8fb7b3e1…`) | 1647274201 | Launches clean; **0** presence-poll failures |

**The asymmetry is the whole proof:** at 19:55 one account's auth-ticket POST got 403 while
another's got 200 — same API, same minute, same machine, same IP. A global Roblox-0.727 API break
would 403 everyone. Healthy account: 0 presence failures. Flagged account: 546. **This is
per-cookie session degradation, not a Roblox API change.** The installed-client version drift
(0.722 → 0.727) is a separate operational issue (tray-residence, orphan processes, mutex ordering)
and is **out of scope here** — see §11.

**403, not 401 is the tell:** a dead cookie returns 401 → `CookieExpiredException` → the existing
"Session expired" path. A *flagged* cookie returns 403 → no handler today.

## 2. Goals and non-goals

**Goals:**

- Introduce a distinct **`SessionLimited`** account state (separate from `SessionExpired`) that
  honestly reflects "Roblox flagged this cookie — it still authenticates, but actions are forbidden."
- **Detect** it from two sources:
  1. **Launch (immediate):** the auth-ticket exchange's CSRF-authenticated POST returning 403.
  2. **Presence (proactive):** N consecutive 403s on the background presence poll.
- **Surface** it above stale presence so the row stops lying, with a clear, honest message and a
  distinct dot.
- **Gate** launches for a Limited account (per-row Launch As + batch exclusion) — implementing the
  bot-challenge investigation's "suppress auth-ticket retries while a challenge is in flight; don't
  burn trust."
- **Recover** via the existing re-auth (WebView2 re-login) path, and **auto-heal** when a later
  presence poll succeeds (Roblox lifted the flag after cooldown).

**Non-goals (deliberate — see §11):**

- Adding CSRF / `X-CSRF-TOKEN` to the presence endpoint. Healthy cookies poll presence fine (0
  failures). The 403 is the cookie being flagged, not a missing header. Adding it fixes a
  non-problem.
- Auto-retrying the auth-ticket on 403. The bot-challenge investigation is explicit: retrying while
  a challenge is in flight burns freshly-built trust.
- Any challenge bypass, auto-solve, or header spoofing. Anti-cheat walls are intentional; RORORO
  sits on this side of them (CLAUDE.md "What NOT to do").
- The 0.727 tray-residence / orphan-process / mutex-ordering work. Separate track.
- The `roblox-compat.json` known-good pin bump (0.722 → 0.727). Separate quick win; cosmetic banner
  only, gates nothing.

## 3. Stack

No new dependencies. Extends existing seams:

- `CookieExpiredException` (Core) — the new `SessionLimitedException` mirrors it exactly.
- `IPresenceService.AccountSessionExpired` event (Core) — the new `AccountSessionLimited` event
  mirrors it.
- `LaunchResult` discriminated union (Core) — add a `Limited` case beside `CookieExpired`.
- `AccountSummary` (App) — add `SessionLimited` beside `SessionExpired`, same `INotifyPropertyChanged`
  pattern.
- `LaunchEligibility` (App) — add a `Limited` skip bucket beside `Running`/`Expired`/`Deselected`.
- `ReauthenticateAsync` (App) — the existing recovery path; no change beyond clearing the new flag.

## 4. Architecture and change surface

### 4.1 `src/ROROROblox.Core/SessionLimitedException.cs` (NEW)

```csharp
namespace ROROROblox.Core;

/// <summary>
/// Thrown when Roblox returns HTTP 403 on a cookie-authenticated request whose cookie is NOT
/// expired (a 401 would throw <see cref="CookieExpiredException"/> instead). Signals a
/// flagged / soft-locked session — typically post-bot-challenge. The cookie still authenticates;
/// Roblox is forbidding the action. Recovery is re-capture (re-login) or cooldown — NEVER auto-retry.
/// </summary>
public sealed class SessionLimitedException : Exception
{
    public SessionLimitedException() : base("Roblox returned 403 — session is rate-limited / flagged.") { }
}
```

### 4.2 `src/ROROROblox.Core/RobloxApi.cs` (MODIFIED)

**Auth-ticket — classify the CSRF-authenticated 403.** In `GetAuthTicketAsync`, the *first* POST's
403 is the normal CSRF handshake (it carries `x-csrf-token`) and must NOT be treated as Limited.
Only the *second* POST (sent WITH a valid token) returning 403 is the signal. Add a single
**CSRF-rotation retry** first to avoid false-Limiting on token rotation, then classify:

```csharp
// after ThrowOnAuthFailure(secondResponse) + ThrowOnContentTypeRejection(secondResponse):
if (secondResponse.StatusCode == HttpStatusCode.Forbidden)
{
    // Token rotation? If a fresh token came back and we haven't retried, retry once.
    if (secondResponse.Headers.TryGetValues("x-csrf-token", out var rotated)
        && rotated.FirstOrDefault() is { Length: > 0 } newToken
        && !rotatedAlready)
    {
        // one retry with newToken …
    }
    // Still 403 with a valid token → flagged session.
    throw new SessionLimitedException();
}
if (!secondResponse.IsSuccessStatusCode)
    throw new InvalidOperationException($"Roblox auth-ticket endpoint returned {(int)secondResponse.StatusCode}.");
```

**Presence — surface the 403.** Add a `ThrowOnSessionLimited(response)` helper (throws
`SessionLimitedException` on 403) and call it in `GetPresenceAsync` alongside `ThrowOnAuthFailure`.
Add a sibling re-throw so it propagates past the generic swallow:

```csharp
catch (CookieExpiredException) { throw; }
catch (SessionLimitedException) { throw; }   // NEW — don't swallow into the empty-list path
catch { return []; }
```

Net effect: presence 403 → `SessionLimitedException` (distinct), every other non-401 failure →
empty list → hold-last-known (unchanged).

### 4.3 `src/ROROROblox.Core/LaunchResult.cs` (MODIFIED)

Add a `Limited` case to the union:

```csharp
public sealed record Limited : LaunchResult;   // beside Started / CookieExpired / Failed
```

### 4.4 `src/ROROROblox.Core/RobloxLauncher.cs` (MODIFIED)

Both `ExecuteLaunchAsync` and `ExecuteLegacyLaunchAsync` catch the new exception around the
`GetAuthTicketAsync` call, between the `CookieExpiredException` and generic `Exception` catches:

```csharp
catch (CookieExpiredException) { return new LaunchResult.CookieExpired(); }
catch (SessionLimitedException) { return new LaunchResult.Limited(); }   // NEW
catch (Exception ex) { return new LaunchResult.Failed($"Failed to obtain auth ticket: {ex.Message}"); }
```

### 4.5 `src/ROROROblox.Core/Diagnostics/IPresenceService.cs` + `PresenceService.cs` (MODIFIED)

**Interface:** add `event EventHandler<Guid>? AccountSessionLimited;` beside `AccountSessionExpired`.

**Service:** a per-account consecutive-403 counter
(`ConcurrentDictionary<Guid,int> _consecutiveLimited`) and a threshold constant
`LimitedFlipThreshold = 3`. In `PollTargetAsync`:

```csharp
catch (CookieExpiredException) { _consecutiveLimited[id] = 0; AccountSessionExpired?.Invoke(...); return; }
catch (SessionLimitedException)
{
    var n = _consecutiveLimited.AddOrUpdate(id, 1, (_, c) => c + 1);
    _log.LogDebug("Presence for account {AccountId}: 403 (consecutive {N})", id, n);
    if (n >= LimitedFlipThreshold) AccountSessionLimited?.Invoke(this, id);
    return;
}
```

Counter semantics (consecutive = "in a row, blips don't break the chain"):

| Poll outcome | Counter | Event |
|---|---|---|
| 403 (`SessionLimitedException`) | increment | `AccountSessionLimited` at ≥3 |
| success (presence list non-empty) | reset to 0 | `AccountPresenceUpdated` (VM auto-heals) |
| 401 (`CookieExpiredException`) | reset to 0 | `AccountSessionExpired` (expired supersedes) |
| empty list (429 / network / blip) | unchanged | none (hold last-known) |

The N=3 threshold at the 25 s cadence surfaces a flag within ~75 s while a single transient 403
never flips the row.

### 4.6 `src/ROROROblox.App/ViewModels/AccountSummary.cs` (MODIFIED)

Add the flag and weave it into the two derived properties:

```csharp
private bool _sessionLimited;
public bool SessionLimited
{
    get => _sessionLimited;
    set { if (SetField(ref _sessionLimited, value)) {
        OnPropertyChanged(nameof(StatusDot));
        OnPropertyChanged(nameof(SecondaryStatusText));
    } }
}
```

**`StatusDot`** — Expired wins, then Limited, then active/idle:

```csharp
public string StatusDot =>
    _sessionExpired ? "yellow"
    : _sessionLimited ? "magenta"          // distinct "needs attention" — see §8 dot-color note
    : (InGame || _presenceState == UserPresenceType.InStudio || _isRunning) ? "green"
    : "grey";
```

**`SecondaryStatusText`** — insert Limited at **step 2**, immediately after Expired and **before
`InGame`** (this is the fix for "says in game, nothing happens"):

```
1. SessionExpired      → "Session expired"
2. SessionLimited      → "Limited by Roblox — re-capture or wait"   ← NEW, beats stale presence
3. InGame              → "In {game} · {age}"
4. … (unchanged)
```

When `SessionLimited` flips true via the presence path, the VM also clears the stale in-game fields
(`PresenceState → Offline`, `CurrentGameName → null`, `InGameSinceUtc → null`) so the dot can't stay
green.

### 4.7 `src/ROROROblox.App/ViewModels/LaunchEligibility.cs` (MODIFIED)

Add `SessionLimited` to `LaunchCandidate`, a `Limited` count to `LaunchBreakdown`, and a skip
clause. Priority over selected accounts: **expired → limited → busy**. Banner clause:
`"{n} limited"`. `IsBusy` is unchanged (Limited is its own skip reason, not "busy").

### 4.8 `src/ROROROblox.App/ViewModels/MainViewModel.cs` (MODIFIED)

- **Launch handler** (`case LaunchResult.Limited:`) — beside `CookieExpired`:

  ```csharp
  case LaunchResult.Limited:
      _log.LogInformation("Account {AccountId} is rate-limited by Roblox (403)", summary.Id);
      summary.SessionLimited = true;
      summary.PresenceState = UserPresenceType.Offline;   // drop the stale dot
      summary.CurrentGameName = null;
      summary.InGameSinceUtc = null;
      summary.StatusText = string.Empty;                  // copy comes from SecondaryStatusText
      break;
  ```

- **Subscribe** to `_presenceService.AccountSessionLimited += OnAccountSessionLimited;` (beside the
  existing `AccountSessionExpired` wire-up at line 160-161). Handler flips `SessionLimited = true`
  + clears stale presence on the matching `AccountSummary` (UI thread marshalling like the existing
  expired handler).

- **Auto-heal** — in `OnAccountPresenceUpdated`, clear `SessionLimited = false` (a successful poll
  means Roblox lifted the flag). One line.

- **Per-row Launch gating** — the row's Launch As `CanExecute` excludes `SessionLimited` accounts
  (same place `SessionExpired` is excluded today). Batch launch already routes through
  `LaunchEligibility` (§4.7).

### 4.9 Row XAML (MODIFIED, minimal)

- Dot brush: map `"magenta"` → brand magenta `#f22f89` (§8).
- The existing **Re-auth ("Re")** button is the primary CTA on a Limited row (already present in the
  row template, partially visible past "Launch As").
- Launch As button disabled/gated binding when `SessionLimited` (mirror the expired binding).

## 5. State model + precedence

`SessionExpired` and `SessionLimited` are independent bools. **Expired always wins** (a 401 means the
cookie is genuinely dead — re-login is mandatory; a 403 means flagged-but-alive — re-login OR
cooldown). Precedence is encoded once in `StatusDot` and once in `SecondaryStatusText`; nothing else
needs to reason about both.

## 6. Detection summary

| Source | Trigger | Latency | Why |
|---|---|---|---|
| Launch | auth-ticket CSRF-authenticated POST → 403 (after one rotation retry) | immediate | user-initiated, definitive |
| Presence | 3 consecutive poll 403s | ~75 s | proactive — warns before the user clicks; fixes the frozen dot |

The client-side redemption 403 (ItsJustEstePapa: launch "succeeds" from RORORO's view, the Roblox
*client* 403s) is not directly observable by RORORO. It's caught by the presence path instead —
the same flagged cookie that 403s the client also 403s presence, so the account flips Limited within
~75 s. This coherence is why the two-source design covers both failure shapes.

## 7. Error handling / soft-fail

- Every new classification is additive and fail-safe: an unclassified failure still falls through to
  the existing empty-list / generic-`Failed` paths. We never *replace* a working path, only branch
  403 out of it.
- The CSRF-rotation retry is capped at **one** attempt — no hammering (investigation: don't burn
  trust).
- Auto-heal is one-directional and cheap: a successful presence poll clears the flag; nothing else
  has to fire.

## 8. Open visual call — Limited dot color

Default: **brand magenta `#f22f89`** — distinct from expired-yellow, reads as "attention," on-brand.
Alternative: amber/orange, the conventional "warning" hue, but close enough to expired-yellow to
risk confusion. Final color goes through the `626labs-design` skill before merge (CLAUDE.md: visual
surfaces go through the design skill). This is the one deferred visual decision; everything else is
logic.

## 9. Testing (TDD)

Pure/extractable logic is TDD-first per repo convention. New + modified test cases:

**Core — `RobloxApiAuthTicketTests` (extend):**
1. Second auth-ticket POST → 403 **with valid CSRF** → throws `SessionLimitedException`.
2. *First* POST → 403 (CSRF handshake) → does NOT throw Limited (reads token, proceeds).
3. Second POST → 403 with a *rotated* `x-csrf-token` → retries once, succeeds → returns ticket.
4. Second POST → 403 twice (rotation retry still 403) → throws `SessionLimitedException`.
5. Second POST → 401 → still `CookieExpiredException` (regression guard).

**Core — `RobloxApiPresenceTests` (extend):**
6. Presence 403 → throws `SessionLimitedException` (not swallowed to `[]`).
7. Presence 401 → `CookieExpiredException` (regression). Presence 429/network → `[]` (regression).

**Core — `PresenceServiceTests` (extend):**
8. 3 consecutive 403s → raises `AccountSessionLimited` once at the 3rd.
9. 403, 403, success → counter resets, no `AccountSessionLimited`.
10. 403 ×3 then success → `AccountSessionLimited` fired, then a later `AccountPresenceUpdated`
    (auto-heal signal).
11. 401 mid-streak → resets the Limited counter + raises `AccountSessionExpired`.

**App — `LaunchEligibilityTests` (extend):**
12. A `SessionLimited` selected account → `Limited` bucket, excluded from eligible, banner reads
    "{n} limited".
13. Priority: an account both expired and limited counts once, as Expired.

**App — `AccountSummaryTests` (extend):**
14. `SessionLimited` true → `StatusDot` == "magenta" (when not expired).
15. `SecondaryStatusText` precedence: Limited beats InGame (set both, assert the Limited string).
16. Expired beats Limited.

UI wiring (XAML bindings, the VM event subscription, dot brush) is verify-by-running.

## 10. Decisions to log (626 Dashboard, on completion)

- **Root-cause finding:** the post-1.7 launch failures are per-cookie 403 session degradation
  (captcha/bot-challenge soft-lock), **not** a Roblox 0.727 API change — proven by per-account
  asymmetry (one account 200, another 403, same minute; 0 vs 546 presence failures). Tag: Roblox
  compatibility event.
- **Architectural choice:** distinct `SessionLimited` state (403) separate from `SessionExpired`
  (401), because 403≠401 and the recoveries differ (re-capture-or-cooldown vs mandatory re-login).
- **UX / Roblox-relations choice:** gate launches while Limited + no auto-retry on 403, per the
  2026-05-07 bot-challenge investigation ("suppress retries, don't burn trust"). Auto-heal on a
  successful presence poll so a cooled-down account self-recovers without forced re-login.

## 11. Out of scope (separate tracks)

- **Roblox 0.727 tray-residence** — orphan windowless `RobloxPlayerBeta` processes, the
  must-fully-close-from-tray requirement, mutex-ordering rework for the now-common "Roblox already
  running" case. Its own spec.
- **`roblox-compat.json` pin bump** (0.722 → 0.727) — cosmetic drift-banner only; quick win, no
  functional coupling here.
- **Presence CSRF / header changes** — explicitly not needed (healthy cookies poll fine).

## 12. Open questions / future

- **Threshold tuning.** N=3 at 25 s is a first guess. If real-world flagging surfaces too slowly or
  flaps, tune the constant — flag for `/reflect`.
- **First-time explainer.** v1 ships row copy only. A one-time "what does Limited mean" toast/modal
  is a forward-looking polish item, not v1.
- **Telemetry.** How often Limited fires in the wild would inform whether richer UX is justified.
  Rides on the existing crash-report opt-in surface if/when that lands.
