# Friends-from-main follow — design

**Date:** 2026-07-04
**Status:** Approved design, pre-plan.
**Origin:** The "join-via-friend on Limited rows" idea from the Limited-cycle follow-ups
([`2026-06-30-rororo-limited-followups.md`](2026-06-30-rororo-limited-followups.md) §5), re-scoped
during brainstorming.

---

## 1. What this is (and the reframe)

Add a **source toggle** to the existing per-account Friends picker so the user can browse their
**main account's** friends (default) or **this account's** own friends, while the account whose row
the picker was opened on is always the one that **launches** — following the picked friend into their
server.

### The reframe (why the original targeting was wrong)

The follow-ups doc pitched "Join via friend" as an action **on Limited rows**. Digging into the launch
path showed that anchor is mis-targeted:

- **What RORORO flags "Limited" (magenta rows):** accounts returning **HTTP 403 on the auth-ticket or
  presence API**. Every launch — follow-a-friend included — fetches an auth-ticket first
  ([`RobloxLauncher.ExecuteLaunchAsync`](../../../src/ROROROblox.Core/RobloxLauncher.cs) calls
  `GetAuthTicketAsync` before it ever builds the follow URI), so a 403-on-ticket account **cannot
  follow-join either** — the ticket 403s before the follow happens.
- **What follow-join actually rescues:** the **captcha-gated** accounts — a suspicious-activity check
  on a cold game-join. Those launch fine from RORORO's view (auth-ticket succeeds, client opens, the
  captcha is a **client-side game-join gate**). RORORO **cannot see that captcha**, so it never flags
  them Limited.

So "join-via-friend on Limited rows" points at the rows it can't help, and the rows it can help aren't
Limited. **Flavor-detection for the captcha case is essentially impossible — it is invisible to us.**

### Decision (Este, 2026-07-04)

- **Dropped:** flavor-detection and the Limited-row anchor.
- **Kept:** a **general, any-row** friends-from-main launch, built by **extending the existing
  per-account Friends picker** rather than adding a new surface. The user knows which alts are
  captcha-gated; the feature gives them the tool without RORORO needing to detect anything.

### Governance note (the wall)

The follow-ups doc flagged this as brushing the "no-evade-Roblox-trust-gates" wall (Este's call). This
framing **defuses that concern**: follow-launch **already ships** (the per-account Friends picker and
the drag-to-follow `FollowAltAsync` both do it today). Pointing the picker at a different friends list
is not a new evasion mechanism — no automation, no injection, no captcha auto-solve, no spoofing. It is
the same manual, user-initiated follow that already exists, sourced from the user's real social list.

---

## 2. Mental model

- The picker is opened **per row**. The row it is opened on = the account that will **launch**.
- The **source toggle** changes only **whose friends list you browse** — the launch identity never
  changes.
- Default source is the **main** account (`Accounts.FirstOrDefault(a => a.IsMain)`), because alts
  typically have empty friend lists and the main is the real social account.

Data flow: open picker on row **R** → resolve main **M** → window shows **M's** friends by default →
user picks in-game joinable friend **F** → window returns `LaunchTarget.FollowFriend(F.UserId)` →
caller re-runs `EvaluateFollow` → `LaunchAccountAsync(R, overrideTarget: FollowFriend(F))`. The list
came from M (M's cookie + M's userId); the launch is R (R's cookie + R's auth-ticket).

---

## 3. Components

### 3.1 `FriendFollowWindow` (extend)

[`src/ROROROblox.App/Friends/FriendFollowWindow.xaml.cs`](../../../src/ROROROblox.App/Friends/FriendFollowWindow.xaml.cs)

Today the window takes a single identity `(accountId, accountUserId, accountDisplayName)` = "whose
friends to list," and returns a `FollowFriend` target. It **never launches** — the caller does. That
separation is exactly what this feature needs, so the launch path is untouched.

Changes:

- Constructor gains a **second, optional source identity** (the main: id, userId, display name) and a
  **default-source** flag. When the second identity is absent (no main, or main == the opened row),
  the window behaves exactly as today (single source, no toggle).
- A **two-state toggle** at the top of the window: `[Main]'s friends` / `[This account]'s friends`.
  Switching source re-runs `RefreshAsync` against the selected identity's cookie + userId.
- **When the browsed list belongs to a different account than the launcher** (the two-source case,
  browsing main's friends from an alt's row), the window **header names both roles** so the split is
  never ambiguous: *"Browsing [Main]'s friends · [Alt] will follow."* In the single-source case (no
  main, or the browsed list is the launcher's own), the header is just the account name, as today.
- A one-line **friends-only caveat** near the list: following works into public servers; a
  friends-only server the launching account isn't a friend of will land at home.
- Cookie hygiene **unchanged**: each `RefreshAsync` fetches the plaintext cookie fresh into a local
  that falls out of scope after the API calls; it is never retained on the window. This now applies to
  both identities.

The existing `EvaluateFollow` land-at-home guard (friend must be `InGame` with a joinable place) still
gates each Follow button — it is source-agnostic.

### 3.2 `MainViewModel.OpenFriendFollowAsync` (extend)

[`src/ROROROblox.App/ViewModels/MainViewModel.cs`](../../../src/ROROROblox.App/ViewModels/MainViewModel.cs) (~line 1454)

Before constructing the window, resolve the **main source identity**:

- Find the main (`MainAccount`). If none, or if it **is** the opened row, pass no second identity
  (single-source window).
- Ensure the main's `RobloxUserId` is resolved — reuse the same on-demand resolve the method already
  does for the opened row (retrieve cookie → `GetUserProfileAsync` → cache + persist, soft-fail).
- The **launch line is unchanged**: it still launches `summary` (the opened row) with the picked
  target.

### 3.3 Testable source-resolution seam (new, small)

The window is WPF (manual-smoke per house convention). Extract the **source-resolution decision** into a
small method testable without WPF: given the opened row and the accounts, produce the pair of source
identities (row, and optionally main) plus the default source. This isolates the branch logic
(no main / main == row / main userId unresolved) from the window so it can be unit-tested.

---

## 4. Edge cases & copy

| Case | Behavior |
|---|---|
| **No main picked** | The "main's friends" toggle option is disabled with a hint ("Pick a main account to browse their friends"). Default falls back to the opened row's own friends. |
| **Main == opened row** | One identity only; the toggle collapses (both sides are the same list). Behaves as today. |
| **Main session expired / limited** | Fetching main's friends surfaces a clear, account-named message ("Main (Name)'s session expired — re-authenticate it, or view this account's friends"). User can toggle to the row's own list as fallback. |
| **Main userId not yet resolved** | Resolve on demand (soft-fail); if resolution fails, disable the main source with the same hint. |
| **Friends-only server bounce** | Best-effort (Este's call). No friendship detection. One honest caveat line; the existing land-at-home guard already blocks join-privacy-hidden friends. |

All user-facing copy follows the RORORO voice (builder-to-builder, second person, sentence case, no
"empower/leverage/seamlessly," no emoji).

---

## 5. Testing

- **Window:** manual smoke on a real multi-account setup (house convention for WPF windows — matches
  `FriendFollowWindow`'s existing coverage posture). Smoke: open on an alt → sees main's friends →
  toggle to alt's own → Follow an in-game friend → alt launches into that server.
- **Source-resolution seam:** unit tests for main-present, no-main fallback, main == row dedup, and
  main-userId-unresolved.
- **`EvaluateFollow`:** already unit-tested; unchanged.
- **No end-to-end against real roblox.com** (per repo policy) — the auth-ticket + follow path is
  exercised by manual smoke.

---

## 6. Out of scope (YAGNI)

- Flavor-detection of the captcha vs 403 Limited states (impossible / invisible — see §1).
- The Limited-row anchor and any Limited-specific "Join via friend" button.
- Friendship detection between the launching alt and the target (friends-only precision).
- Any change to `FollowAltAsync` (drag-to-follow-alt) or the launch/auth-ticket path.
- Persisting a per-row source preference — the default is always main; the toggle is per-open.

---

## 7. Decision log (to mirror to the dashboard on build)

- **Re-scope:** dropped flavor-detection + Limited anchor; the captcha flavor that follow-join rescues
  is invisible to RORORO, so the feature is a general any-row action, not Limited-gated.
- **Extend, don't add:** built into the existing `FriendFollowWindow` + `OpenFriendFollowAsync` rather
  than a new surface — one entry point, one code path.
- **Default source = main:** alts have empty friend lists; the main is the useful one.
- **Friends-only = best-effort + caveat copy:** no friendship detection; public servers dominate the
  clan use case, and the cost/complexity of per-friend friendship checks isn't justified.
- **Wall:** general follow-from-main is the same manual follow-launch that already ships; not a new
  trust-gate evasion.
