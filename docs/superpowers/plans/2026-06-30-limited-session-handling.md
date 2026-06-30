# Limited-Session (403 Soft-Lock) Handling — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give RORORO a distinct `SessionLimited` account state for Roblox-flagged (HTTP 403) cookies — detected on launch and via the presence poll — that beats stale presence, gates launches, and auto-heals.

**Architecture:** A new `SessionLimitedException` (mirrors `CookieExpiredException`) is thrown by `RobloxApi` on a 403 that is NOT the CSRF handshake. `RobloxLauncher` maps it to a new `LaunchResult.Limited`; `PresenceService` counts consecutive presence-403s and raises a new `AccountSessionLimited` event after 3. `MainViewModel` flips an `AccountSummary.SessionLimited` flag that wins over stale presence in the row, gates launches via `LaunchEligibility`, and auto-heals on the next successful presence poll.

**Tech Stack:** .NET 10 LTS, C# 14, xUnit, WPF (MVVM). Canonical solution: `ROROROblox.slnx`.

## Global Constraints

- **Build:** `dotnet build ROROROblox.slnx`. **Test:** `dotnet test ROROROblox.slnx`. Never build the stray `ROROROblox.sln`.
- **Commits:** Conventional commits (`feat` / `fix` / `test` / `refactor`). Commit after every task.
- **Secrets:** Cookies / `.ROBLOSECURITY` values are NEVER logged. Log account ids + status only.
- **User-Agent stays `ROROROblox/<version>`** — do not spoof a browser.
- **No auth-ticket auto-retry beyond the single CSRF-rotation retry** in Task 1 (bot-challenge investigation: retrying while challenged burns trust).
- **Limited dot color** = brand magenta `#f22f89` as the default; the final value goes through the `626labs-design` skill before merge (CLAUDE.md: visual surfaces go through the design skill).
- **Spec:** `docs/superpowers/specs/2026-06-29-rororo-limited-session-handling-design.md`. Branch: `feat/limited-session-handling` (already cut; spec already committed).

---

### Task 1: `RobloxApi` — classify the CSRF-authenticated auth-ticket 403 as `SessionLimited`

**Files:**
- Create: `src/ROROROblox.Core/SessionLimitedException.cs`
- Modify: `src/ROROROblox.Core/RobloxApi.cs` (`GetAuthTicketAsync`, ~lines 48-92)
- Test: `src/ROROROblox.Tests/RobloxApiTests.cs`

**Interfaces:**
- Produces: `public sealed class SessionLimitedException : Exception` (parameterless ctor). Thrown when the auth-ticket exchange's CSRF-authenticated POST stays 403 after one rotation retry. Consumed by Task 4 (`RobloxLauncher`) and Task 3 (`PresenceService` — via Task 2).

- [ ] **Step 1: Create the exception type**

```csharp
// src/ROROROblox.Core/SessionLimitedException.cs
namespace ROROROblox.Core;

/// <summary>
/// Thrown when Roblox returns HTTP 403 on a cookie-authenticated request whose cookie is NOT
/// expired (a 401 throws <see cref="CookieExpiredException"/> instead). Signals a flagged /
/// soft-locked session — typically post bot-challenge. The cookie still authenticates; Roblox is
/// forbidding the action. Recovery is re-capture (re-login) or cooldown — NEVER auto-retry.
/// </summary>
public sealed class SessionLimitedException : Exception
{
    public SessionLimitedException() : base("Roblox returned 403 — session is rate-limited / flagged.") { }
}
```

- [ ] **Step 2: Write the failing tests**

Add to `src/ROROROblox.Tests/RobloxApiTests.cs`:

```csharp
[Fact]
public async Task GetAuthTicketAsync_SecondPost403_SameToken_ThrowsSessionLimited()
{
    var (api, stub) = CreateApi();
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-1"))); // handshake
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-1"))); // same token, still forbidden

    await Assert.ThrowsAsync<SessionLimitedException>(() => api.GetAuthTicketAsync(TestCookie));
    Assert.Equal(2, stub.Requests.Count); // no retry when the token did not rotate
}

[Fact]
public async Task GetAuthTicketAsync_SecondPost403_RotatedToken_RetriesOnceThenSucceeds()
{
    var (api, stub) = CreateApi();
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-1"))); // handshake
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-2"))); // rotation
    stub.EnqueueResponse(Response(HttpStatusCode.OK, ("RBX-Authentication-Ticket", "TICKET-OK")));

    var ticket = await api.GetAuthTicketAsync(TestCookie);

    Assert.Equal("TICKET-OK", ticket.Ticket);
    Assert.Equal(3, stub.Requests.Count);
    Assert.Contains(stub.Requests[2].Headers,
        h => h.Key == "X-CSRF-TOKEN" && h.Value.Any(v => v == "tok-2"));
}

[Fact]
public async Task GetAuthTicketAsync_RotationRetryStill403_ThrowsSessionLimited()
{
    var (api, stub) = CreateApi();
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-1")));
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-2")));
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "tok-3")));

    await Assert.ThrowsAsync<SessionLimitedException>(() => api.GetAuthTicketAsync(TestCookie));
    Assert.Equal(3, stub.Requests.Count); // exactly one retry, then give up
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~GetAuthTicketAsync_SecondPost403|FullyQualifiedName~RotationRetry"`
Expected: FAIL — the current code throws `InvalidOperationException("…returned 403")`, not `SessionLimitedException`, and never retries (only 2 requests on the rotation case).

- [ ] **Step 4: Implement the classification + single rotation retry**

In `RobloxApi.cs`, replace the second-POST block (from `using var secondResponse = …` through the `return new AuthTicket(…)`) with a try/finally that allows one reassignment for the rotation retry:

```csharp
// Second POST — exchange cookie + token for ticket. A 403 carrying a DIFFERENT token is a CSRF
// rotation: retry once with the rotated token before declaring the session flagged. A 403 with
// the SAME token (or no token) is a genuine forbidden → SessionLimitedException. Never more than
// one retry (bot-challenge: don't burn trust).
var secondResponse = await PostAuthTicketAsync(cookie, csrfToken).ConfigureAwait(false);
try
{
    ThrowOnAuthFailure(secondResponse);          // 401 -> CookieExpiredException
    ThrowOnContentTypeRejection(secondResponse); // 415 -> helpful InvalidOperationException

    if (secondResponse.StatusCode == HttpStatusCode.Forbidden
        && secondResponse.Headers.TryGetValues("x-csrf-token", out var rotated)
        && rotated.FirstOrDefault() is { Length: > 0 } rotatedToken
        && !string.Equals(rotatedToken, csrfToken, StringComparison.Ordinal))
    {
        secondResponse.Dispose();
        secondResponse = await PostAuthTicketAsync(cookie, rotatedToken).ConfigureAwait(false);
        ThrowOnAuthFailure(secondResponse);
        ThrowOnContentTypeRejection(secondResponse);
    }

    if (secondResponse.StatusCode == HttpStatusCode.Forbidden)
    {
        // Still forbidden with a valid CSRF token → flagged / soft-locked session.
        throw new SessionLimitedException();
    }
    if (!secondResponse.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Roblox auth-ticket endpoint returned {(int)secondResponse.StatusCode}.");
    }
    if (!secondResponse.Headers.TryGetValues("RBX-Authentication-Ticket", out var ticketHeaders))
    {
        throw new InvalidOperationException("Auth ticket response missing RBX-Authentication-Ticket header.");
    }
    var ticket = ticketHeaders.FirstOrDefault();
    if (string.IsNullOrEmpty(ticket))
    {
        throw new InvalidOperationException("RBX-Authentication-Ticket header was empty.");
    }
    return new AuthTicket(ticket, DateTimeOffset.UtcNow);
}
finally
{
    secondResponse.Dispose();
}
```

Ensure `using System.Net;` is present (it is — `HttpStatusCode` is already used).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~RobloxApiTests"`
Expected: PASS — including the existing `GetAuthTicketAsync_HappyPath…` and `…401On…` regression guards (the first-POST 403 handshake must still work).

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/SessionLimitedException.cs src/ROROROblox.Core/RobloxApi.cs src/ROROROblox.Tests/RobloxApiTests.cs
git commit -m "feat(core): classify auth-ticket 403 as SessionLimited (one CSRF-rotation retry)"
```

---

### Task 2: `RobloxApi` — surface the presence-poll 403 as `SessionLimited`

**Files:**
- Modify: `src/ROROROblox.Core/RobloxApi.cs` (`GetPresenceAsync`, ~lines 433-481; add a `ThrowOnSessionLimited` helper near `ThrowOnAuthFailure`, ~line 663)
- Test: `src/ROROROblox.Tests/RobloxApiTests.cs`

**Interfaces:**
- Consumes: `SessionLimitedException` (Task 1).
- Produces: `GetPresenceAsync` throws `SessionLimitedException` on HTTP 403; unchanged on 401 (`CookieExpiredException`) and on 429/network (returns `[]`).

- [ ] **Step 1: Write the failing tests**

Add to `src/ROROROblox.Tests/RobloxApiTests.cs`:

```csharp
[Fact]
public async Task GetPresenceAsync_403_ThrowsSessionLimited()
{
    var (api, stub) = CreateApi();
    stub.EnqueueResponse(Response(HttpStatusCode.Forbidden));

    await Assert.ThrowsAsync<SessionLimitedException>(
        () => api.GetPresenceAsync(TestCookie, new[] { 123L }));
}

[Fact]
public async Task GetPresenceAsync_429_ReturnsEmpty_NotThrow()
{
    var (api, stub) = CreateApi();
    stub.EnqueueResponse(Response((HttpStatusCode)429));

    var result = await api.GetPresenceAsync(TestCookie, new[] { 123L });
    Assert.Empty(result);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~GetPresenceAsync_403|FullyQualifiedName~GetPresenceAsync_429"`
Expected: the 403 test FAILS (current code swallows 403 into `[]`, returns empty instead of throwing). The 429 test passes already (regression guard).

- [ ] **Step 3: Add the helper**

In `RobloxApi.cs`, beside `ThrowOnAuthFailure` (~line 663):

```csharp
private static void ThrowOnSessionLimited(HttpResponseMessage response)
{
    if (response.StatusCode == HttpStatusCode.Forbidden)
    {
        throw new SessionLimitedException();
    }
}
```

- [ ] **Step 4: Wire it into `GetPresenceAsync`**

Inside `GetPresenceAsync`, call it right after `ThrowOnAuthFailure(response);`:

```csharp
            ThrowOnAuthFailure(response);     // 401 -> CookieExpiredException
            ThrowOnSessionLimited(response);  // 403 -> SessionLimitedException (NEW)
```

And add a re-throw clause so the generic catch doesn't swallow it. The catch block becomes:

```csharp
        catch (CookieExpiredException)
        {
            throw;
        }
        catch (SessionLimitedException)
        {
            throw;
        }
        catch
        {
            return [];
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~RobloxApiTests"`
Expected: PASS — presence 403 throws; 401 still `CookieExpiredException`; 429/network still `[]`.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/RobloxApi.cs src/ROROROblox.Tests/RobloxApiTests.cs
git commit -m "feat(core): surface presence-poll 403 as SessionLimited"
```

---

### Task 3: `PresenceService` — consecutive-403 counter + `AccountSessionLimited` event

**Files:**
- Modify: `src/ROROROblox.Core/Diagnostics/IPresenceService.cs` (add the event)
- Modify: `src/ROROROblox.Core/Diagnostics/PresenceService.cs` (counter + catch + reset)
- Modify: `src/ROROROblox.Tests/PresenceServiceTests.cs` (extend `FakeRobloxApi`; new tests)

**Interfaces:**
- Consumes: `SessionLimitedException` (Task 2 makes `GetPresenceAsync` throw it).
- Produces: `event EventHandler<Guid>? AccountSessionLimited;` on `IPresenceService`, raised once when an account's presence poll 403s `LimitedFlipThreshold` (=3) times in a row. Consumed by Task 7 (`MainViewModel`).

- [ ] **Step 1: Add the event to the interface**

In `IPresenceService.cs`, after the `AccountSessionExpired` event (~line 29):

```csharp
    /// <summary>
    /// Fired (payload = the account id) when an account's presence poll returns HTTP 403
    /// (<see cref="SessionLimitedException"/>) three times in a row — its session is flagged /
    /// soft-locked (post bot-challenge), not expired. The ViewModel flips the row to the magenta
    /// "Limited" state. Reset by a successful poll or a 401. Spec §4.5.
    /// </summary>
    event EventHandler<Guid>? AccountSessionLimited;
```

- [ ] **Step 2: Extend `FakeRobloxApi` in the test file**

In `src/ROROROblox.Tests/PresenceServiceTests.cs`, in the `FakeRobloxApi` class (~line 387), add a property and make `GetPresenceAsync` (~line 409) throw for limited cookies. Add the property:

```csharp
        public HashSet<string> SessionLimitedCookies { get; } = new();
```

And as the FIRST line inside `GetPresenceAsync`:

```csharp
        public async Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds)
        {
            if (SessionLimitedCookies.Contains(cookie)) throw new SessionLimitedException();
            // … existing body unchanged …
```

- [ ] **Step 3: Write the failing tests**

Add to `PresenceServiceTests.cs`:

```csharp
[Fact]
public async Task PollOnceAsync_ThreeConsecutive403_RaisesSessionLimitedOnceAtThird()
{
    var accountId = Guid.NewGuid();
    const long userId = 700;
    var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
    var api = new FakeRobloxApi { SessionLimitedCookies = { Cookie } };
    var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

    var limited = new List<Guid>();
    service.AccountSessionLimited += (_, id) => limited.Add(id);

    await service.PollOnceAsync();          // 1
    await service.PollOnceAsync();          // 2
    Assert.Empty(limited);                  // not yet
    await service.PollOnceAsync();          // 3 -> flip
    var raised = Assert.Single(limited);
    Assert.Equal(accountId, raised);
}

[Fact]
public async Task PollOnceAsync_403sThenSuccess_ResetsCounter()
{
    var accountId = Guid.NewGuid();
    const long userId = 701;
    var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
    var api = new FakeRobloxApi { SessionLimitedCookies = { Cookie } };
    var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

    var limited = new List<Guid>();
    service.AccountSessionLimited += (_, id) => limited.Add(id);

    await service.PollOnceAsync();          // 403 #1
    await service.PollOnceAsync();          // 403 #2

    // Roblox lifts the flag: presence now succeeds.
    api.SessionLimitedCookies.Remove(Cookie);
    api.PresenceByCookie[Cookie] = [new UserPresence(userId, UserPresenceType.OnlineWebsite, null, null, null)];
    await service.PollOnceAsync();          // success -> counter reset

    api.SessionLimitedCookies.Add(Cookie);
    api.PresenceByCookie.Remove(Cookie);
    await service.PollOnceAsync();          // 403 #1 (post-reset)
    await service.PollOnceAsync();          // 403 #2 (post-reset)

    Assert.Empty(limited);                  // never hit 3-in-a-row
}

[Fact]
public async Task PollOnceAsync_401_ResetsLimitedCounter()
{
    var accountId = Guid.NewGuid();
    const long userId = 702;
    var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
    var api = new FakeRobloxApi { SessionLimitedCookies = { Cookie } };
    var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

    var limited = new List<Guid>();
    var expired = new List<Guid>();
    service.AccountSessionLimited += (_, id) => limited.Add(id);
    service.AccountSessionExpired += (_, id) => expired.Add(id);

    await service.PollOnceAsync();          // 403 #1
    await service.PollOnceAsync();          // 403 #2

    // Cookie dies (401) — expired supersedes; Limited counter resets.
    api.SessionLimitedCookies.Remove(Cookie);
    api.ThrowCookieExpiredForCookie.Add(Cookie);
    await service.PollOnceAsync();          // 401 -> AccountSessionExpired, reset

    api.ThrowCookieExpiredForCookie.Remove(Cookie);
    api.SessionLimitedCookies.Add(Cookie);
    await service.PollOnceAsync();          // 403 #1 (post-reset)
    await service.PollOnceAsync();          // 403 #2 (post-reset)

    Assert.Empty(limited);
    Assert.Single(expired);
}
```

If `FakeRobloxApi` has no `ThrowCookieExpiredForCookie` set, add it the same way as `SessionLimitedCookies`: a `public HashSet<string> ThrowCookieExpiredForCookie { get; } = new();` and `if (ThrowCookieExpiredForCookie.Contains(cookie)) throw new CookieExpiredException();` at the top of `GetPresenceAsync` (before the SessionLimited check).

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~PresenceServiceTests"`
Expected: the three new tests FAIL to compile/throw — `AccountSessionLimited` event and the counter don't exist yet.

- [ ] **Step 5: Implement the counter + event in `PresenceService`**

Add fields near the top of the class (after `_gameNameCache`, ~line 57):

```csharp
    private readonly ConcurrentDictionary<Guid, int> _consecutiveLimited = new();
    private const int LimitedFlipThreshold = 3;
```

Add the event beside `AccountSessionExpired` (~line 88):

```csharp
    public event EventHandler<Guid>? AccountSessionLimited;
```

In `PollTargetAsync`, the existing `catch (CookieExpiredException)` resets the counter, and a new `catch (SessionLimitedException)` increments it. Replace the existing catch and add the new one:

```csharp
        catch (CookieExpiredException)
        {
            _consecutiveLimited[target.AccountId] = 0;
            _log.LogDebug("Presence for account {AccountId}: cookie expired (401)", target.AccountId);
            AccountSessionExpired?.Invoke(this, target.AccountId);
            return;
        }
        catch (SessionLimitedException)
        {
            var n = _consecutiveLimited.AddOrUpdate(target.AccountId, 1, (_, c) => c + 1);
            _log.LogDebug("Presence for account {AccountId}: 403 (consecutive {N})", target.AccountId, n);
            if (n >= LimitedFlipThreshold)
            {
                AccountSessionLimited?.Invoke(this, target.AccountId);
            }
            return;
        }
```

On a genuine success, reset the counter. Right after the `if (presences.Count == 0) { … return; }` block (~line 215), add:

```csharp
        // Genuine success (non-empty) — Roblox is answering this cookie again; clear any limited streak.
        _consecutiveLimited[target.AccountId] = 0;
```

Note: the empty-list path (429/network) is left UNCHANGED — it neither increments nor resets the streak (blips don't break "consecutive").

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~PresenceServiceTests"`
Expected: PASS — all new + existing presence tests green.

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.Core/Diagnostics/IPresenceService.cs src/ROROROblox.Core/Diagnostics/PresenceService.cs src/ROROROblox.Tests/PresenceServiceTests.cs
git commit -m "feat(core): PresenceService consecutive-403 counter + AccountSessionLimited event"
```

---

### Task 4: `RobloxLauncher` — map `SessionLimitedException` to `LaunchResult.Limited`

**Files:**
- Modify: `src/ROROROblox.Core/LaunchResult.cs` (add the case)
- Modify: `src/ROROROblox.Core/RobloxLauncher.cs` (`ExecuteLaunchAsync` ~line 128-137 and `ExecuteLegacyLaunchAsync` ~line 234-245)
- Test: `src/ROROROblox.Tests/RobloxLauncherTests.cs`

**Interfaces:**
- Consumes: `SessionLimitedException` (Task 1).
- Produces: `public sealed record Limited : LaunchResult;` — returned when the auth-ticket fetch throws `SessionLimitedException`. Consumed by Task 7 (`MainViewModel` launch handler).

- [ ] **Step 1: Add the `Limited` case**

In `LaunchResult.cs`, beside `CookieExpired` (~line 21):

```csharp
    public sealed record Limited : LaunchResult;
```

- [ ] **Step 2: Write the failing tests**

In `src/ROROROblox.Tests/RobloxLauncherTests.cs`, mirror the existing `LaunchAsync_CookieExpired_ReturnsCookieExpiredResult` (line 201) and `LaunchAsync_TypedApi_CookieExpired_ReturnsCookieExpired` (line 332):

```csharp
[Fact]
public async Task LaunchAsync_SessionLimited_ReturnsLimitedResult()
{
    var api = new StubRobloxApi(_ => throw new SessionLimitedException());
    var launcher = CreateLauncher(api);   // same factory the CookieExpired test uses

    var result = await launcher.LaunchAsync("cookie", new LaunchTarget.Place(123));

    Assert.IsType<LaunchResult.Limited>(result);
}
```

Match the exact construction the neighboring `LaunchAsync_CookieExpired_ReturnsCookieExpiredResult` test uses (its launcher factory + `LaunchTarget`). If that test uses the legacy `LaunchAsync(cookie, placeUrl)` overload, add a second test mirroring it so both `ExecuteLaunchAsync` and `ExecuteLegacyLaunchAsync` are covered.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~LaunchAsync_SessionLimited"`
Expected: FAIL — `SessionLimitedException` is currently caught by the generic `catch (Exception ex)` and returned as `LaunchResult.Failed`, not `Limited`.

- [ ] **Step 4: Add the catch in both launch methods**

In `ExecuteLaunchAsync`, between the `catch (CookieExpiredException)` (~line 130) and `catch (Exception ex)` (~line 134):

```csharp
        catch (SessionLimitedException)
        {
            return new LaunchResult.Limited();
        }
```

Add the identical catch in `ExecuteLegacyLaunchAsync` between its `catch (CookieExpiredException)` (~line 238) and `catch (Exception ex)` (~line 242).

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~RobloxLauncherTests"`
Expected: PASS — Limited mapping works; CookieExpired + Started regression guards still green.

- [ ] **Step 6: Commit**

```bash
git add src/ROROROblox.Core/LaunchResult.cs src/ROROROblox.Core/RobloxLauncher.cs src/ROROROblox.Tests/RobloxLauncherTests.cs
git commit -m "feat(core): map SessionLimitedException to LaunchResult.Limited"
```

---

### Task 5: `AccountSummary` — `SessionLimited` flag, magenta dot, precedence over stale presence

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/AccountSummary.cs` (field ~line 16; `StatusDot` ~line 397; `SecondaryStatusText` ~line 409)
- Test: `src/ROROROblox.Tests/AccountSummaryTests.cs`

**Interfaces:**
- Produces: `public bool SessionLimited { get; set; }` on `AccountSummary`; `StatusDot` returns `"magenta"` when limited (and not expired); `SecondaryStatusText` returns `"Limited by Roblox — re-capture or wait"` ahead of `InGame`. Consumed by Task 6 (`LaunchCandidate`) and Task 7 (XAML + VM).

- [ ] **Step 1: Write the failing tests**

Add to `src/ROROROblox.Tests/AccountSummaryTests.cs` (uses the existing `NewSummary()` helper):

```csharp
[Fact]
public void StatusDot_SessionLimited_NotExpired_ReturnsMagenta()
{
    var s = NewSummary();
    s.SessionLimited = true;
    Assert.Equal("magenta", s.StatusDot);
}

[Fact]
public void StatusDot_Expired_BeatsLimited_ReturnsYellow()
{
    var s = NewSummary();
    s.SessionLimited = true;
    s.SessionExpired = true;
    Assert.Equal("yellow", s.StatusDot);
}

[Fact]
public void SecondaryStatusText_Limited_BeatsStaleInGame()
{
    var s = NewSummary();
    s.CurrentGameName = "Pet Sim 99";
    s.PresenceState = UserPresenceType.InGame;   // stale "in game"
    s.SessionLimited = true;

    Assert.Equal("Limited by Roblox — re-capture or wait", s.SecondaryStatusText);
}

[Fact]
public void SecondaryStatusText_Expired_BeatsLimited()
{
    var s = NewSummary();
    s.SessionLimited = true;
    s.SessionExpired = true;
    Assert.Equal("Session expired", s.SecondaryStatusText);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~AccountSummaryTests"`
Expected: FAIL to compile — `SessionLimited` doesn't exist.

- [ ] **Step 3: Add the field**

In `AccountSummary.cs`, add the backing field beside `_sessionExpired` (~line 16):

```csharp
    private bool _sessionLimited;
```

And the property beside `SessionExpired` (~line 107):

```csharp
    /// <summary>
    /// True when Roblox returned HTTP 403 on this account's authenticated requests — a flagged /
    /// soft-locked session (post bot-challenge), distinct from <see cref="SessionExpired"/> (401 =
    /// dead cookie). Cleared by a successful presence poll (auto-heal) or a re-capture. Spec §5.
    /// </summary>
    public bool SessionLimited
    {
        get => _sessionLimited;
        set
        {
            if (SetField(ref _sessionLimited, value))
            {
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(SecondaryStatusText));
            }
        }
    }
```

- [ ] **Step 4: Wire `StatusDot` (Expired wins, then Limited)**

Replace the `StatusDot` expression (~line 397):

```csharp
    public string StatusDot => _sessionExpired
        ? "yellow"
        : _sessionLimited
            ? "magenta"
            : (InGame || _presenceState == UserPresenceType.InStudio || _isRunning) ? "green" : "grey";
```

- [ ] **Step 5: Wire `SecondaryStatusText` (Limited beats InGame)**

In `SecondaryStatusText`, immediately after the `if (_sessionExpired) return "Session expired";` block (step 1, ~line 414) and BEFORE the `if (InGame)` block (step 2), insert:

```csharp
            // 1b. Limited by Roblox (403). Beats stale presence — this is the fix for the frozen
            //     "In game" dot masking a failed launch.
            if (_sessionLimited)
            {
                return "Limited by Roblox — re-capture or wait";
            }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~AccountSummaryTests"`
Expected: PASS — including the existing ghost-fix tests (a non-limited InGame row still shows the game).

- [ ] **Step 7: Commit**

```bash
git add src/ROROROblox.App/ViewModels/AccountSummary.cs src/ROROROblox.Tests/AccountSummaryTests.cs
git commit -m "feat(app): AccountSummary.SessionLimited — magenta dot + precedence over stale presence"
```

---

### Task 6: `LaunchEligibility` — `Limited` skip bucket

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/LaunchEligibility.cs` (`LaunchCandidate`, `LaunchBreakdown`, `Compute`, `NonZeroClauses`)
- Test: `src/ROROROblox.Tests/LaunchEligibilityTests.cs` (create if absent)

**Interfaces:**
- Consumes: `AccountSummary.SessionLimited` (Task 5) via the candidate map (Task 7).
- Produces: `LaunchCandidate` gains a `bool SessionLimited` field (position 3, after `SessionExpired`); `LaunchBreakdown` gains `int Limited`; banners say `"{n} limited"`. Consumed by Task 7 (`ToLaunchCandidate`).

- [ ] **Step 1: Write the failing tests**

Create/extend `src/ROROROblox.Tests/LaunchEligibilityTests.cs`. The helper builds a `LaunchCandidate` (it's `internal` — the test project already has `InternalsVisibleTo`, since `LaunchEligibility` is internal and tested):

```csharp
using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

public class LaunchEligibilityLimitedTests
{
    private static LaunchCandidate Cand(bool selected = true, bool expired = false,
        bool limited = false, bool inGame = false, bool running = false, bool launching = false)
        => new(selected, expired, limited, inGame, running, launching, "Alt");

    [Fact]
    public void Compute_LimitedAccount_GoesToLimitedBucket_NotEligible()
    {
        var result = LaunchEligibility.Compute(new[] { Cand(limited: true) });

        Assert.Empty(result.Eligible);
        Assert.Equal(1, result.Breakdown.Limited);
        Assert.Equal(0, result.Breakdown.Expired);
        Assert.Contains("1 limited", result.ZeroEligibleBanner);
    }

    [Fact]
    public void Compute_ExpiredBeatsLimited_CountsAsExpired()
    {
        var result = LaunchEligibility.Compute(new[] { Cand(expired: true, limited: true) });

        Assert.Equal(1, result.Breakdown.Expired);
        Assert.Equal(0, result.Breakdown.Limited);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~LaunchEligibilityLimitedTests"`
Expected: FAIL to compile — `LaunchCandidate` has no `SessionLimited`, `LaunchBreakdown` has no `Limited`.

- [ ] **Step 3: Add the field to `LaunchCandidate`**

```csharp
internal readonly record struct LaunchCandidate(
    bool IsSelected,
    bool SessionExpired,
    bool SessionLimited,
    bool InGame,
    bool IsRunning,
    bool IsLaunching,
    string Name);
```

- [ ] **Step 4: Add the bucket to `LaunchBreakdown`**

```csharp
internal readonly record struct LaunchBreakdown(int Running, int Expired, int Limited, int Deselected);
```

- [ ] **Step 5: Count it in `Compute` (expired → limited → busy)**

In `LaunchEligibility.Compute`, add a `limited` counter and a clause after the `SessionExpired` check and before `IsBusy`:

```csharp
        var eligible = new List<LaunchCandidate>();
        var running = 0;
        var expired = 0;
        var limited = 0;
        var deselected = 0;

        foreach (var c in candidates)
        {
            if (!c.IsSelected) { deselected++; continue; }
            if (c.SessionExpired) { expired++; continue; }
            if (c.SessionLimited) { limited++; continue; }   // NEW — after expired, before busy
            if (IsBusy(c)) { running++; continue; }
            if (c.IsLaunching) { continue; }
            eligible.Add(c);
        }

        return new LaunchEligibilityResult
        {
            Eligible = eligible,
            Breakdown = new LaunchBreakdown(running, expired, limited, deselected),
        };
```

- [ ] **Step 6: Add the banner clause**

In `LaunchEligibilityResult.NonZeroClauses`, after the expired clause and before deselected:

```csharp
        if (Breakdown.Running > 0) clauses.Add($"{Breakdown.Running} already running");
        if (Breakdown.Expired > 0) clauses.Add($"{Breakdown.Expired} expired");
        if (Breakdown.Limited > 0) clauses.Add($"{Breakdown.Limited} limited");   // NEW
        if (Breakdown.Deselected > 0) clauses.Add($"{Breakdown.Deselected} deselected");
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test ROROROblox.slnx --filter "FullyQualifiedName~LaunchEligibility"`
Expected: PASS — new Limited cases plus existing eligibility tests. (Existing `LaunchBreakdown` constructions in other tests that used the 3-arg form must be updated to the 4-arg form — fix any compile errors by inserting `limited: 0` / the positional `0` for Limited.)

- [ ] **Step 8: Commit**

```bash
git add src/ROROROblox.App/ViewModels/LaunchEligibility.cs src/ROROROblox.Tests/LaunchEligibilityTests.cs
git commit -m "feat(app): LaunchEligibility Limited skip-bucket + banner clause"
```

---

### Task 7: `MainViewModel` + converter + XAML wiring (integration)

**Files:**
- Modify: `src/ROROROblox.App/ViewModels/MainViewModel.cs` (event sub ~line 161; launch handler ~line 1006; `OnAccountPresenceUpdated` ~line 1631; new `OnAccountSessionLimited`; `ToLaunchCandidate` ~line 1080; `MatchEligible` ~line 1096; `LaunchAllCommand` ~line 126; `StartMainCommand` ~line 134)
- Modify: `src/ROROROblox.App/Converters.cs` (`StatusDotBrushConverter` ~line 169-188)
- Modify: `src/ROROROblox.App/MainWindow.xaml` (Launch As button ~line 671-691; Re-authenticate button visibility)

**Interfaces:**
- Consumes: `LaunchResult.Limited` (Task 4), `IPresenceService.AccountSessionLimited` (Task 3), `AccountSummary.SessionLimited` (Task 5), `LaunchCandidate.SessionLimited` (Task 6).
- Produces: end-to-end behavior. Verified by build + the pure tests from Tasks 5/6 plus a manual smoke.

- [ ] **Step 1: Map `LaunchResult.Limited` in the launch handler**

In `LaunchAccountAsync`'s `switch (result)` (~line 988), add a case after `LaunchResult.CookieExpired` (~line 1006):

```csharp
                case LaunchResult.Limited:
                    _log.LogInformation("Account {AccountId} is rate-limited by Roblox (403)", summary.Id);
                    summary.SessionLimited = true;
                    summary.PresenceState = UserPresenceType.Offline;  // drop the stale "In game" dot
                    summary.CurrentGameName = null;
                    summary.InGameSinceUtc = null;
                    summary.StatusText = string.Empty;                 // copy comes from SecondaryStatusText
                    break;
```

- [ ] **Step 2: Subscribe to the presence event**

After `_presenceService.AccountSessionExpired += OnAccountSessionExpired;` (~line 161):

```csharp
        _presenceService.AccountSessionLimited += OnAccountSessionLimited;
```

- [ ] **Step 3: Add the handler (mirror `OnAccountSessionExpired`)**

Beside `OnAccountSessionExpired` (~line 1681):

```csharp
    private void OnAccountSessionLimited(object? sender, Guid accountId)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == accountId);
            if (summary is null) return;
            summary.SessionLimited = true;
            summary.PresenceState = UserPresenceType.Offline;  // clear the frozen "In game"
            summary.CurrentGameName = null;
            summary.InGameSinceUtc = null;
            RelayCommand.RaiseCanExecuteChanged();
        });
    }
```

- [ ] **Step 4: Auto-heal on a successful presence update**

In `OnAccountPresenceUpdated`, right after `if (summary is null) return;` (~line 1632):

```csharp
            // A presence poll landing means Roblox is answering this cookie again — clear Limited.
            summary.SessionLimited = false;
```

- [ ] **Step 5: Thread `SessionLimited` through launch eligibility**

`ToLaunchCandidate` (~line 1080) — add the field in position 3:

```csharp
    private static LaunchCandidate ToLaunchCandidate(AccountSummary a) => new(
        a.IsSelected, a.SessionExpired, a.SessionLimited, a.InGame, a.IsRunning, a.IsLaunching, a.DisplayName);
```

`MatchEligible` filter (~line 1096) — exclude limited:

```csharp
        return ordered
            .Where(a => a.IsSelected && !a.SessionExpired && !a.SessionLimited
                        && !LaunchEligibility.IsBusy(ToLaunchCandidate(a)) && !a.IsLaunching)
            .ToList();
```

`LaunchAllCommand` predicate (~line 126):

```csharp
        LaunchAllCommand = new RelayCommand(LaunchAllAsync, () => !IsBusy && Accounts.Any(a => a.IsSelected && !a.SessionExpired && !a.SessionLimited && !(a.InGame || a.IsRunning)));
```

`StartMainCommand` predicate (~line 134):

```csharp
        StartMainCommand = new RelayCommand(StartMainAsync, () => !IsBusy && Accounts.FirstOrDefault(a => a.IsMain) is { SessionExpired: false, SessionLimited: false, IsRunning: false, InGame: false });
```

- [ ] **Step 6: Add the magenta brush to the dot converter**

In `Converters.cs`, in `StatusDotBrushConverter`, add the brush (after `Yellow`, ~line 175) and the case:

```csharp
    private static readonly System.Windows.Media.SolidColorBrush Magenta =
        new(System.Windows.Media.Color.FromRgb(0xF2, 0x2F, 0x89)); // brand magenta — "attention"
```

```csharp
        return (value as string) switch
        {
            "green" => Green,
            "yellow" => Yellow,
            "magenta" => Magenta,
            _ => Grey,
        };
```

- [ ] **Step 7: Gate the per-row Launch As when Limited**

In `MainWindow.xaml`, the Launch As button's `Button.Style.Triggers` (~line 684) — add a trigger that disables it when limited (sits alongside the existing IsRunning→Collapsed trigger):

```xml
                                        <DataTrigger Binding="{Binding SessionLimited}" Value="True">
                                            <Setter Property="IsEnabled" Value="False" />
                                            <Setter Property="ToolTip" Value="Roblox flagged this account. Re-authenticate, or wait for it to cool down." />
                                        </DataTrigger>
```

Then find the Re-authenticate button (it currently shows on `SessionExpired`) and broaden its visibility to also show on `SessionLimited`:

Run: `grep -n "Reauth\|Re-authenticate\|ReauthenticateCommand" src/ROROROblox.App/MainWindow.xaml`

For the button found, change its `Visibility` binding so it shows when **either** `SessionExpired` **or** `SessionLimited` is true. If it uses a single `BoolToVisibilityConverter` on `SessionExpired`, switch to a `MultiBinding` with `AnyTrueToVisibilityConverter` (already present in `Converters.cs` if a similar multi-bool converter exists; otherwise bind to `SessionExpired` and add a parallel `DataTrigger` on `SessionLimited` setting `Visibility=Visible`). Keep this verify-by-running.

- [ ] **Step 8: Build and run the full suite**

Run: `dotnet build ROROROblox.slnx` then `dotnet test ROROROblox.slnx`
Expected: clean build; all tests green.

- [ ] **Step 9: Manual smoke (no live Roblox needed)**

The pure paths are unit-tested; this confirms the wiring. With the app running and a stubbed/forced Limited state (or after a real 403), verify:
1. A row that 403s on launch shows the magenta dot + "Limited by Roblox — re-capture or wait" (NOT a frozen "In game").
2. Launch As is disabled on that row; Re-authenticate is visible.
3. After a successful re-capture (or a successful presence poll), the row clears back to normal.
4. Batch launch banner reads "… N limited" when a limited account is skipped.

- [ ] **Step 10: Commit**

```bash
git add src/ROROROblox.App/ViewModels/MainViewModel.cs src/ROROROblox.App/Converters.cs src/ROROROblox.App/MainWindow.xaml
git commit -m "feat(app): wire Limited state — VM handler, auto-heal, launch gating, magenta dot"
```

---

## Self-Review

**Spec coverage:**
- §4.1 SessionLimitedException → Task 1 ✓
- §4.2 auth-ticket 403 + rotation retry → Task 1 ✓; presence 403 → Task 2 ✓
- §4.3 LaunchResult.Limited → Task 4 ✓
- §4.4 RobloxLauncher catch → Task 4 ✓
- §4.5 IPresenceService event + counter → Task 3 ✓
- §4.6 AccountSummary flag + dot + precedence → Task 5 ✓
- §4.7 LaunchEligibility bucket → Task 6 ✓
- §4.8 MainViewModel (case, subscription, handler, auto-heal, candidate/predicates) → Task 7 ✓
- §4.9 XAML dot + gating + Re-auth → Task 7 ✓
- §5 precedence (Expired > Limited) → Tasks 5, 6 ✓
- §6 detection two-source → Tasks 1+3 ✓
- §8 magenta default → Task 7 ✓; Global Constraints ✓
- §9 every TDD case → mapped across Tasks 1-6 ✓

**Placeholder scan:** Step 7 of Task 7 (Re-auth button visibility) is the one located-edit rather than verbatim code — acceptable because it's verify-by-running UI and the exact binding must be read from the file; a `grep` command is given to locate it. Everything else is concrete.

**Type consistency:** `SessionLimitedException` (Task 1) used in Tasks 2/3/4. `LaunchResult.Limited` (Task 4) used in Task 7. `AccountSessionLimited` event (Task 3) subscribed in Task 7. `SessionLimited` property (Task 5) read in Tasks 6/7. `LaunchCandidate` 7-field ctor (Task 6) called in Task 7's `ToLaunchCandidate`. `LaunchBreakdown` 4-field ctor (Task 6) — Step 7 note flags fixing existing 3-arg constructions. Names consistent throughout.
