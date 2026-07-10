# RORORO — streamer mode design

---
**Date:** 2026-07-10
**Status:** Approved (brainstorm complete) — ready for implementation plan
**Author:** The Architect + Este
**Scope:** A one-flip "streamer mode" that disguises the account manager so RoRoRo is safe to
screen-share. Not blur/redact-everything — fun **fake identities** (silly names + avatars) that
each account wears while the mode is on.
---

## Why

The idea started as "I need to mask my account names to make a promo video," widened the moment it
was clear streamers using RoRoRo would face the same thing *live and repeatedly* — RoRoRo puts your
entire alt roster, their real names, their avatars, and your friends list on one screen. Renaming
accounts to hide them is destructive and per-account tedious. A single toggle that disguises the
whole surface, non-destructively, is the fix. Streamers are amplifiers, and RoRoRo is a
brand-spreads-for-free product; making it stream-safe widens the audience at near-zero ongoing cost.

**The design principle underneath it:** cut the steps between downtime and uptime. Going live should
be one flip, no confirm dialog, no per-account fiddling.

## The core idea — costumes, not redaction

A blurred screen looks broken and reads as "this app has something to hide." Instead, each account
gets a **persistent fake identity**: a silly name (e.g. *CaptainNoodle*) and a silly avatar. On
stream it looks intentional and playful — "Imagine Something Else" applied to your own roster —
while revealing nothing real. Redaction is reserved only for the surfaces that *can't* wear a
costume (see Coverage).

## Scope boundary (stated up front, honestly)

Streamer mode masks the **account manager** — the thing that exposes your whole roster at once. It
does **not** touch what shows *inside* a Roblox game: your alt's real name on a leaderboard, in
chat, or in the friends list of another player. That is Roblox-side and out of RoRoRo's reach. The
UI copy and this spec say so plainly, so the feature does not over-promise "fully stream-safe." What
it delivers is: your screen no longer reveals *which accounts you own and what they're really
called* in one glance.

**Two further scope lines, stated so they're deliberate rather than discovered live (added after the
whole-branch review):**

- **The plugin boundary is not masked.** The plugin gRPC surface (`GetRunningAccounts`,
  `GetAccountActivity`, the account events) returns real display names and Roblox user ids to
  installed plugins, unaffected by streamer mode. That is correct: the plugin boundary has its own
  per-capability consent model, and a plugin runs in its own process. But a plugin that draws its
  **own** overlay/dashboard on the streamer's screen (say an idle monitor showing "RealAlt idle 5m")
  is outside streamer mode's reach — RoRoRo can't mask another process's window. If that becomes a
  real exposure, the fix belongs in the plugin (a streamer-mode-aware capability), not here.
- **The account-add / re-auth login is Roblox's own UI.** Adding or re-authenticating an account
  shows the real Roblox login WebView, which inherently displays the real identity. Streamer mode
  does not (and can't) mask that, and the confirmation messages tied to a just-completed login are
  left unmasked deliberately (masking a name the login page just showed is theater). Adding accounts
  is an off-stream action.

Privacy features fail in a specific way: a single forgotten surface leaks, and the streamer *thinks*
they're safe. So the masking is centralized, not scattered.

Every identity-bearing read routes through one `IStreamerIdentityProvider`. Given an account (or a
friend), it returns either the real identity or the account's fake identity, depending on the global
toggle. The alternative — each UI surface checking the flag itself — was rejected: every future
screen someone adds is a chance to forget one, and the failure is silent. Centralizing means one
place to get right and one place to test.

```
IStreamerIdentityProvider
  DisplayIdentity ForAccount(AccountSummary)   // { Name, AvatarSource }
  DisplayIdentity ForFriend(friendId, realName, realAvatar)
  bool IsActive                                 // the global toggle
  event Changed                                 // fires on toggle + reroll so bindings refresh
  void Reroll(accountOrFriendKey)               // new pick for one identity
  void RerollAll()                              // new pick for every identity
```

When `IsActive` is false it returns the real values untouched — RoRoRo behaves exactly as today.

## Data model

Each account gains a persisted fake identity, following the existing per-account-field pattern
(`Account.LocalName` + `AccountStore.UpdateLocalNameAsync`):

```
StreamerIdentity { string FakeName; string FakeAvatarId; }
```

- Assigned **lazily** — the first time an account needs a fake identity (mode turned on, or a fresh
  account added while on), it draws an unused pick from the pool.
- **Persisted** in the account store (DPAPI-encrypted like the rest of the account record) so it
  survives restarts — stable "alt #3 is CaptainNoodle" across a multi-session stream.
- The fake identity exists independent of the toggle; the toggle only decides whether it is
  *shown*. Turning streamer mode off doesn't discard the assignment.
- **Friends** get the same treatment, keyed by friend id, in their own small persisted map (friends
  aren't accounts, so they don't live on the `Account` record).

`FakeAvatarId` references a bundled asset (see Banked assets), not a URL — so a disguised avatar
never triggers a network fetch that could reveal anything.

## Coverage map

| Surface | Today | In streamer mode |
| --- | --- | --- |
| Account name (`RenderName`) | LocalName ?? DisplayName | **Fake name** |
| Account avatar (`AvatarUrl`) | Roblox avatar image | **Fake avatar** (bundled) |
| Roblox user id | shown in spots | **hidden** |
| "Start [name]" CTA | username | **fake name** |
| Roblox window title | `Roblox - {DisplayName}` (raw!) | **`Roblox - {FakeName}`** |
| Friends picker (names + avatars) | real | **fake** (keeps picker usable) |
| Private-server share link | joinable URL | **redacted** — `•••` pill, reveal-on-hover / copy for the streamer only |

Share links are the one surface that can't be disguised: a fake link is useless and the real one
lets a viewer join the server live. So it is hidden, with a private reveal so the streamer can still
grab it. The Roblox window-title row is a real current leak — the decorator titles windows with the
raw `DisplayName` ([RobloxWindowDecorator.cs:123](../../../src/ROROROblox.App/Tray/RobloxWindowDecorator.cs)),
not even the local nickname — so streamer mode must override it.

## Window-title propagation

`RobloxWindowDecorator` already sets each Roblox window's title per account via `SetWindowTextW`. In
streamer mode it reads the name from `IStreamerIdentityProvider` instead of `Summary.DisplayName`,
and its existing "push the latest title for tracked processes" refresh re-titles **open** windows
live when the toggle flips — so a streamer who forgot to flip it before launching can fix every open
window with one click, no relaunch. (Alt-tab and window-capture both surface these titles, so this
row is load-bearing, not cosmetic.)

## The toggle

- **Global, sticky.** One on/off for the whole app. Stays on across launches once flipped — a
  streamer wants it reliably on so the first frame is never a roster reveal. Off-stream cost is
  nil: you just see "CaptainNoodle" for a while.
- **Where:** the tray menu (beside the existing "Multi-Instance" toggle, reachable when the window
  is hidden) **and** a switch in the main window / settings for discoverability.
- **No confirm.** One flip, downtime to uptime.
- **Reroll controls:** per-identity reroll (on each row / friend) + a "Reroll all identities"
  button for a fresh set on demand.

**Related cleanup, tracked separately (not blocking):** the tray menu is already tall
(Multi-Instance, Stop-all, Open, Preferences, History, Diagnostics, Plugins, Quit). Adding Streamer
mode is one more line. A tray-grooming pass — likely folding the utility items into a submenu —
should be its own small change so streamer mode doesn't have to carry that decision.

## Banked assets

- **Names:** a curated list of silly, clearly-fake names bundled as text. Sized so a large roster
  (10+ accounts + a dozen friends shown) gets distinct picks without repeats; reroll draws a new
  unused one.
- **Avatars:** a pool of silly avatar images, bundled. These go through the **626 design skill** —
  they are a user-facing brand surface streamers will screenshot, and programmatic placeholders are
  disqualifying (pattern x from the SnipSnap retro; the same rule the icon/tile assets follow).
  Treated as a design deliverable, not a code deliverable.

## Testing

The load-bearing test, because this is a privacy feature:

- **Leak scan.** With streamer mode on, no real account/friend name, real avatar URL, or Roblox
  user id is returned by `IStreamerIdentityProvider` for any account or friend. This is the "one
  place to test" payoff of the centralized indirection — assert it once at the provider seam.

Plus:

- **Persistence.** A fake identity survives a store round-trip (restart) unchanged.
- **Reroll.** `Reroll` changes exactly one identity; `RerollAll` changes every identity; both raise
  `Changed`.
- **Toggle passthrough.** With the mode off, the provider returns the real identity byte-for-byte
  (RoRoRo unchanged when not streaming).
- **Window title.** The decorator emits `Roblox - {FakeName}` when active and re-titles an open,
  tracked window on toggle.
- **Pool exhaustion.** More identities than pool entries degrades gracefully (repeats allowed, never
  throws, never falls back to a real name).

## Non-goals (v1)

- **Auto-detect OBS / streaming software** and prompt — a Discord-style nicety, deferred. Manual is
  honest and predictable.
- **Masking in-game identity** — out of reach (scope boundary above).
- **Per-account selective masking** — the mode is global; the only per-account thing is *which* fake
  identity each wears.
- **User-supplied custom fake names/avatars** — the banked pool ships first; custom is a later ask
  if it comes.

## Open questions

None blocking. Two to settle during planning:

1. **Friends identity storage** — a dedicated small persisted map vs. riding along in settings. Both
   work; pick during the plan based on where the friends list is already cached.
2. **Reroll scope of avatar vs name** — does rerolling an identity re-pick both the name and the
   avatar together, or independently? Lean: together (one "give me a new disguise" action), but
   trivially separable if playtesting wants it split.
