# Follow restore — root-cause diagnostic (v1.6.0 item 1, READ-ONLY gate)

> **Date:** 2026-05-21 · **Branch:** `v1.6.0-account-transport` · **Scope:** investigate only, no source changes.
> **Gate decision this note feeds:** is item 8 ("Fix + restore the Follow feature") in-cycle or split to its own cycle?

## TL;DR

The premise is wrong, and that is the finding. **The Follow feature is NOT masked in committed source.** There is no unconditional `Visibility=Collapsed` on either follow surface anywhere in the tree — and there never has been, across the entire git history. Both follow surfaces are wired, visible, and reachable on this branch.

The "masked in v1.2.0.0, restore via 3 XAML edits" claim in project memory (`project_rororo_follow_masked_v1.2`) and echoed in the v1.6.0 spec (§5, line 93) and process-notes (cycle #2, line 110) **does not match the code.** It was never applied to committed XAML. The unmask edit count is **zero** — there is nothing to unmask.

What remains is a *functional* question that the code alone can't answer: **does Roblox's `RequestFollowUser` launch path still work, and does friend-presence still return enough to follow?** That needs a live two-account smoke test, not a rebuild. Leading hypothesis if it ever did break for users: **friend-presence privacy** (a friend's `placeId`/`gameJobId` returns null unless their privacy + the server allowlist permit it) plus the silent land-at-home failure mode — which reads as "broken" to a user but is Roblox doing exactly what it's documented to do.

**Recommendation: IN-CYCLE.** Item 8 collapses from "investigate-then-fix-then-unmask" to "verify with a live follow + tighten the no-op feedback if it bounces to home." Small. See [Scope recommendation](#scope-recommendation).

---

## 1. The two follow surfaces (both live)

There are two distinct "follow" features, easy to conflate:

### A. Follow-an-alt strip (per-row chips) — `MainWindow.xaml:311-394`
Click a chip representing another *saved account* to launch this row's account into that account's current server.
- Chip click → `OnFollowChipClick` (`MainWindow.xaml.cs:326-336`) → `MainViewModel.FollowAltAsync` (`MainViewModel.cs:1510-1540`) → `LaunchTarget.FollowFriend(targetUserId)` → `LaunchAccountAsync`.
- Visibility: strip default is `Visibility="Visible"` (`MainWindow.xaml:319`). It collapses ONLY when `Accounts.Count` is 0 or 1 (`MainWindow.xaml:321-326`) — correct UX (nothing to follow with ≤1 account), not masking. The self-chip hides via `EqualsToCollapseConverter` on `Id` (`MainWindow.xaml:358-366`) — also correct (can't follow yourself).

### B. Friends modal (per-row "Friends" button) — `MainWindow.xaml:548-558`
Opens a modal listing the account's *Roblox friends* with live presence; click "Follow" on an in-game friend.
- Button → `OpenFriendFollowCommand` (`MainViewModel.cs:113, 195`) → `OpenFriendFollowAsync` (`MainViewModel.cs:986-1049`) → `FriendFollowWindow` (`Friends/FriendFollowWindow.xaml.cs`).
- Modal → `_api.GetFriendsAsync` + `_api.GetPresenceAsync`, groups by presence, "Follow" sets `SelectedTarget = new LaunchTarget.FollowFriend(friend.UserId)` (`FriendFollowWindow.xaml.cs:235-240`).
- Visibility: the button has **no** `Visibility` setter. Its only conditional is `IsEnabled="{Binding SessionExpired, Converter={StaticResource InverseBoolConverter}}"` (`MainWindow.xaml:558`) — disabled when the session is expired, visible always.

Both paths converge on `LaunchTarget.FollowFriend(userId)` → `RobloxLauncher.BuildPlaceLauncherUrl` (`RobloxLauncher.cs:329-332`), which emits:
```
https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&browserTrackerId={btid}&userId={ff.UserId}
```
wrapped into the standard `roblox-player:1+launchmode:play+gameinfo:{ticket}+...+placelauncherurl:{escaped}` URI (`RobloxLauncher.cs:427-455`). Identical machinery to the working Place/PrivateServer launches — only the `request=` shape differs.

## 2. Masking mechanism + exact unmask edits

**There is no masking mechanism in source. Unmask edit count: 0.**

Evidence:
- `MainWindow.xaml` contains 20 `Collapsed` occurrences total — every one is a conditional pattern (session-expired pills, empty-state hints, self-chip hide, count≤1 strip collapse). None unconditionally hides a follow surface.
- The "Friends" button has carried only an `IsEnabled` binding (never a `Visibility=Collapsed`) in every commit that touched it: `4e1ce83`, `009866a`, `c9ee778`, `4ac3ae8`, `d80caa9` (verified by reading `Content="Friends"` context at each).
- The cycle #2 process-notes (line 110) *planned* to add "a single-line comment noting the masked state … in the XAML at item 8 time." That comment was never added either — the comment block at `MainWindow.xaml:311-315` describes normal count-based collapse, not masking.

**Conclusion:** the masking either never shipped, or lived only in an uncommitted working tree at v1.2 capture time and was overwritten by the cycle #2 wiring work (`c9ee778`, which fully wired the binding "so the eventual un-mask inherits rename support for free"). Either way, committed code has no mask. The memory note and the spec §5 / process-notes references are **stale** — they propagated the v1.2 working-tree assumption forward without re-checking the code.

> Verify-before-synthesizing: I flagged this contradiction (memory + spec say "Collapsed"; code says "Visible") and resolved it against the code directly rather than trusting the prior note. The code wins.

## 3. Git archaeology

| Commit | What it did to Follow |
|---|---|
| `ee0180a` | `refactor(launcher): typed LaunchTarget union` — introduced `FollowFriend` + `RequestFollowUser` shape |
| `774d590` | `feat(ui): Squad Launch + Friend Follow surfaces` — Friends button + modal, follow-an-alt routing |
| `4e1ce83` | `feat(ui): follow-an-alt + drag-ghost preview` — the per-row chip strip |
| `c9ee778`, `d80caa9`, `4ac3ae8` | later touches (rename render coverage, RoRoRo brand, tags) — Friends button stayed visible/enabled-bound throughout |

- `RequestFollowUser` appears in exactly TWO commits (`ee0180a` introduce, `774d590` use) and was **never modified after** — no "fix the follow URI" commit ever happened. If the launch *shape* were the break, we'd expect a touch here. There isn't one.
- No commit message in history contains "mask", "hide", or "collapse" applied to the follow feature. The v1.2.0.0 follow-up tracking commit (`0be3fbf`) lists three deferrals (WebView2 white-screen, scroll affordance, About-box version) — **Follow is not among them.**

This is the strongest evidence the "broke, so we masked it" story was a working-tree decision that never reached committed source with a rationale.

## 4. Root-cause hypothesis (functional, grounded in code)

Since there's no code-level mask to explain, the real question is whether the live path works. Three candidates, ranked, each grounded:

### H1 — Friend-presence privacy returns no joinable target (LEADING, ~medium confidence)
`UserPresence.PlaceId` / `GameJobId` are documented (`UserPresence.cs:26-35`) as populated "only when `PresenceType` is `InGame` AND the user's privacy settings allow visibility to the requesting cookie's owner." For the **Follow-an-alt** path this is usually fine (you own both accounts, mutual friends). For the **Friends modal** path, a friend with restricted "who can join me" / "who can see me" privacy returns `InGame` with null place data — the modal still renders a "Follow" button (it only checks `PresenceType.InGame`, `FriendFollowWindow.xaml.cs:72`), but `RequestFollowUser` then gets server-rejected and the launcher silently lands at the Roblox home page.
- **Why it reads as "broken":** the user clicks Follow, Roblox opens, drops them at home, nothing explains why. `FollowAltAsync` has a no-op guard + status banner (`MainViewModel.cs:1528-1533`), but the **Friends modal path does not** — `OpenFriendFollowAsync` (`MainViewModel.cs:1045-1048`) fires `LaunchAccountAsync` with zero presence/place validation and no land-at-home warning.
- **Grounded in:** `UserPresence.cs:26-35`, `FriendFollowWindow.xaml.cs:67-83`, `MainViewModel.cs:1045-1048` vs `1528-1533`.

### H2 — `RequestFollowUser` PlaceLauncher request type is stale/changed Roblox-side (CANNOT CONFIRM from code, ~low-medium)
The launch builds `request=RequestFollowUser&userId={X}` against `assetgame.roblox.com/game/PlaceLauncher.ashx` (`RobloxLauncher.cs:329-332`). The other request types (`RequestGame`, `RequestPrivateGame`) on the same endpoint are exercised by shipping, working launches — so the *endpoint* is live. Whether Roblox still honors the `RequestFollowUser` variant specifically is **not determinable from the code**; it's a Roblox-side contract. No code evidence it broke (never re-touched), but no code evidence it still works either.
- **What would confirm:** live follow attempt + watch whether Roblox connects to the friend's server vs bounces home vs errors.

### H3 — Friends/names endpoint regression already absorbed (RESOLVED, not the cause)
There WAS a real friends-path break: `friends.roblox.com/v1/users/{id}/friends` stopped returning name/displayName (cycle 5.5). That was fixed in `fd4ffec` with a bulk `users.roblox.com/v1/users` lookup (`RobloxApi.cs:354-374, 384-431`) and chunked thumbnail batching. This is already shipped and is **not** a current follow blocker — but it shows the friends path has a history of Roblox-side field-stripping, which keeps H1 plausible.

**What I cannot tell from code alone:** whether a real user actually hit a hard failure (vs the silent land-at-home of H1), and whether H2 is live. Both need a two-account live smoke. I will not assert the launch path is broken without that evidence — the code is intact and structurally identical to working launches.

## Scope recommendation

**IN-CYCLE (item 8 stays in v1.6.0).** Reasoning:

1. **No code is masked → no investigate-then-fix-then-unmask.** The spec's framing of item 8 (§5) assumed a hidden, broken surface. The surface is neither hidden nor demonstrably broken in source. That removes the bulk of the item's estimated weight.
2. **The real work is verification + a small UX guard, not a rebuild.** A two-account live follow confirms whether H1/H2 bite. Whatever the result, the code change is small:
   - If it works: item 8 is a smoke confirmation + close the gate. Possibly add a one-line note that the feature was never actually masked (correct the memory + banner-correct spec §5).
   - If it lands-at-home (H1): port the existing `FollowAltAsync` no-op guard/banner pattern (`MainViewModel.cs:1528-1533`) into the **Friends-modal** path (`OpenFriendFollowAsync`, `MainViewModel.cs:1045-1048`) so a non-joinable friend gets feedback instead of a silent bounce. ~10-20 lines + a VM test. In-cycle.
3. **Only H2 (Roblox dropped `RequestFollowUser` entirely) would force a defer** — and there's no code evidence for it, plus the shared endpoint is provably live for sibling request types. Low probability. If the live test surfaces a hard `RequestFollowUser` rejection, *then* split — but gate that decision on the live result, don't pre-pay for it.

### What item 8 should actually do (in-cycle plan)
1. Live two-account smoke: account A follows account B who is in a game → confirm A joins B's server. (Manual, per CLAUDE.md no-E2E-against-real-roblox rule — this is the §8 manual smoke trade.)
2. Live Friends-modal smoke: follow an in-game friend → confirm join vs land-at-home.
3. If land-at-home on a non-joinable target: add the missing presence/place feedback to `OpenFriendFollowAsync` (mirror `FollowAltAsync`).
4. Correct the record: update memory `project_rororo_follow_masked_v1.2` and banner-correct spec §5 / process-notes line 110 — the feature was never masked in committed code.

### If the live test contradicts this (defer trigger)
Defer to its own cycle ONLY if the live smoke shows `RequestFollowUser` is hard-rejected by Roblox (H2 confirmed). Then the follow-up cycle needs:
- A dedicated test-account pair with controlled privacy settings (per CLAUDE.md, a v1.2-style "owning a dedicated test account with appropriate isolation" conversation).
- Network capture of the failing launch to see Roblox's actual rejection vs the working `RequestGame`.
- Investigation of whether Roblox migrated follow/join to a newer endpoint (e.g. `gamejoin`/`game-join` API) that would replace the `PlaceLauncher.ashx?request=RequestFollowUser` shape entirely.

## Files read (evidence trail)
- `src/ROROROblox.App/Friends/FriendFollowWindow.xaml` + `.xaml.cs`
- `src/ROROROblox.App/ViewModels/MainViewModel.cs` (OpenFriendFollowAsync, FollowAltAsync, command wiring)
- `src/ROROROblox.App/ViewModels/AccountSummary.cs` (IsRunning, InGame, presence)
- `src/ROROROblox.App/MainWindow.xaml` + `.xaml.cs` (follow strip, Friends button, OnFollowChipClick)
- `src/ROROROblox.Core/LaunchTarget.cs`, `RobloxLauncher.cs`, `RobloxApi.cs`, `Friend.cs`, `UserPresence.cs`
- `docs/superpowers/specs/2026-05-21-rororo-account-transport-and-bundle-design.md` (§5, risks), `docs/checklist.md` (items 1, 8), `process-notes.md`
- Git history: `git log -S RequestFollowUser/FollowFriend/FriendFollow`, per-commit `Content="Friends"` visibility trace
