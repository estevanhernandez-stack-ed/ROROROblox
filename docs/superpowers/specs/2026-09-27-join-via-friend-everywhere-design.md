# Join via friend, everywhere, and auto-rejoin

**Status:** design agreed in conversation with Este, 2026-09-27; auto-rejoin switched to presence
2026-09-29; spec approved by Este 2026-09-29
**Products:** RoRoRo (host), plugin contract
**Why now:** One alt lands on Roblox's "Verifying you're not a bot" page whenever it joins
directly, and has done for a while. Following the main gets it in every time. The account is flagged
`JoinViaFriend`, but most launches ignore the flag.

## What is wrong today

`Account.JoinViaFriend` is honoured by exactly one path, Squad Launch (`SquadLaunchPlan.Build`).
Every other launch resolves its target through `MainViewModel.ResolveLaunchTarget`, which never
reads the flag:

| Path | Honours the flag |
| --- | --- |
| Single Launch button, compact "start main" | no |
| Favourite game launch | no |
| Launch multiple (`LaunchAllAsync`) | no |
| Join by link | no (an explicit target trumps everything) |
| Memory-watchdog Recycle | no |
| Plugin `RequestLaunch` / `RequestLaunchTarget` (Ur MCP) | no, and `RequestLaunchTarget` also skips `EvaluateFollow` |
| Squad Launch | yes, but falls back to a direct join after 90 s with no anchor |

Nothing detects a client sitting on the verification page.

## What it does

1. **A flagged account never joins directly on its own.** Every launch path resolves a flagged
   account to "follow the main". The main is followed only when `EvaluateFollow` says it is in a
   joinable game, the same guard the Friends window uses.
2. **When the main is not joinable, RoRoRo asks.** It does not fall back to a direct join. The
   account row (or a small dialog for batch launches) says, for example: "AltDog joins through
   your main, and your main isn't in a game yet." It offers two choices:
   - **Follow another account**: a pick list of saved accounts that `EvaluateFollow` says are
     joinable right now. The launch follows the one picked.
   - **It's fixed, join directly**: clears the `JoinViaFriend` flag for that account and launches
     it directly. If the puzzle comes back, auto-rejoin (below) catches it, when it's on.

   Cancel leaves the account stopped.
3. **Batch launches do the same.** Launch multiple and Squad Launch treat flagged accounts as
   followers: they wait for a joinable anchor, and Squad Launch's 90 s direct fallback becomes the
   same question instead.
4. **Plugin launches are held to the same rule.**
   - `RequestLaunch` on a flagged account follows the main or refuses with a reason code
     (`follow-target-not-joinable`). It never asks: a plugin cannot answer a dialog.
   - `RequestLaunchTarget` with `follow_user_id` now runs `EvaluateFollow` and refuses with the same
     reason when the target is not joinable.
   - `RequestLaunchTarget` with an explicit `share_url` for a flagged account follows the main when
     the main is in that same server, and otherwise refuses. A direct join is exactly what the flag
     forbids.
5. **Join by link** for a flagged account follows the main when the main is in the linked
   server, and otherwise asks as in 2.
6. **Auto-rejoin, in RoRoRo** (replaces the Ur OCR captcha trigger drafted 2026-09-27; see
   "Why presence, not a screen read" below). This is a per-account opt-in, "Rejoin if it drops
   out", on the account row's menu. It is off by default and never offered for the main.
   - **The signal:** the account's Roblox client is still running (`RobloxProcessTracker`) but its
     own presence (`PresenceService`, 25 s poll) has not said `InGame` for **3 minutes** in a row.
     This one rule covers every way an alt ends up outside a game with its window open:
     - an idle kick (Error 278) or any other Disconnected dialog;
     - a failed join (an error dialog, a stuck loading screen);
     - the "Verifying you're not a bot" page.
     RoRoRo never needs to know which one it was.
   - **The action:** RoRoRo stops that client, waits for it to exit, and relaunches the account
     through the normal launch path, which applies rule 1:
     - a flagged account follows the main (if the main isn't joinable, it waits and retries on the
       next check, and never joins directly);
     - an unflagged account rejoins the server it was last in, when presence recorded one;
       otherwise it uses its last launch target.
   - **Loop guard:** at most 3 rejoins per account per hour. A fourth one stops auto-rejoin for
     that account until Este turns it back on, and raises a toast and the Discord alert (when
     alerts are on): "AltDog dropped out 4 times in an hour. Auto-rejoin is paused for it." A
     client that keeps landing on the verification page hits this guard, which is the intended
     outcome: a person looks at it.
   - **Grace:** the 3-minute clock starts only after the account has been `InGame` once since
     launch, so a slow first join isn't counted as a drop. A launch that never reaches `InGame`
     within 5 minutes counts as a failed join and gets the same stop-and-relaunch.
   - **Plugins see it:** each auto-rejoin is logged, and `GetAccountActivity` stays unchanged.
     Post-join steps (Best Mine, Auto Mine on) belong to Ur Task: a separate Ur Task change adds a
     per-account "after a rejoin, run these macros" list, triggered when the account goes from
     not-in-game back to `InGame`. That change is out of scope here.

### Why presence, not a screen read

- **Presence is server truth.** Self-presence isn't touched by privacy filters, and the host
  already polls it for every saved account. When the client shows the Disconnected dialog or the
  verification page, Roblox's servers no longer count the account as `InGame`.
- **A screen read is fragile.** A text or colour check on the dialog breaks with the next Roblox
  UI change, needs the window in a known size, and misses causes nobody wrote a check for.
- **Nothing reads, clicks or answers the verification page.** Presence doesn't need to see it.
- **Measured 2026-09-29 (a flagged alt, in the private server, from the Roblox client log and
  RoRoRo's log):**
  - 10:46:09: the verification page opened **inside the running game**. The client stayed
    connected, and presence stayed `InGame`.
  - 10:51:26.6: Roblox idle-kicked the account (reason 278) behind the page. The page covered
    the Disconnected dialog, so a screen check for that dialog would never have fired.
  - 10:51:44.5: RoRoRo's presence poll read `Offline`. **The lag was 18 s**, inside one 25 s
    poll. The worst case is one poll interval plus jitter, about 26 s.
- **A captcha during play shows up late.** The page blocks input, so the account stops being
  `InGame` only when Roblox idle-kicks it: up to 20 minutes after its last real input. Auto-rejoin
  still recovers it, just that much later. Detecting the page earlier would need a screen read,
  and that tradeoff isn't worth taking in the host. If it matters later, a plugin can ask for a
  rejoin through the existing contract.
- **Presence reads `Offline` after a kick, not `OnlineWebsite`.** The rule is "not `InGame`",
  so both count.

### Notes for the plan (from the host code, 2026-09-29)

- `ApplyPresence` clears `CurrentServer`, `CurrentPlaceId` and `LastLaunchTarget` on any
  not-in-game reading. Auto-rejoin needs its own "last server / last target" record, captured
  while the account is `InGame`.
- Plugin `StopAccounts` doesn't call `ExpectClose`. Auto-rejoin must treat any stop in progress,
  from any path, as "not a drop" so it never races a stop sequence's 10 s grace.
- Every launch path meets in `MainViewModel.LaunchAccountAsync`, which calls `ResolveLaunchTarget`.
  That is where rule 1 lands, so it doesn't need to be repeated per path.
- Build the auto-rejoin logic as its own component in the `AccountRecycler` shape: an injected
  clock, presence and process events in, launch and stop delegates out. That way it's testable
  without the view model, whose 30 s ticker is stopped in tests.

## Never

- Nothing clicks, types into or otherwise touches the verification page. It is closed, never
  answered.
- The main is never rejoined automatically.
- No silent direct join for a flagged account, on any path, including auto-rejoin.
- Auto-rejoin never fights a person: an account whose client Este closed by hand, or stopped from
  RoRoRo, is not relaunched. Only a client that is still running but out of the game counts.

## Contract changes

Additive, contract version stays compatible:

- `LaunchResult` has only a free-text `failure_reason` today. Add `string reason_code = 4`,
  with `follow-target-not-joinable` as its first value, and keep `failure_reason` as the
  readable text. This is additive, so the contract version stays "1.0".
- No new RPCs. Auto-rejoin lives entirely in the host.

The contract is published on NuGet, so the host ships first.

## Testing

- **Host, join via friend:** one test per launch path in the table, each asserting that a flagged
  account resolves to follow-the-main, and asks or refuses when the main is not joinable. Tests
  for the "it's fixed" choice clearing the flag, the Squad Launch fallback change, and plugin
  refusals with the reason code.
- **Host, auto-rejoin** (fake clock, fake presence and process events): out of game for 3 min with
  the client running → one stop and relaunch; a closed client → nothing; not opted in, or the main
  → nothing; the grace before the first `InGame`; the 5-minute failed-join case; a flagged account
  follows and waits when the main isn't joinable; the fourth rejoin in an hour pauses and alerts.
- **Live, Dunder-MiffLan:** launch the flagged alt from the single Launch button with the main in the
  private server, and confirm it follows. Stop the main, launch the flagged alt again, and confirm it
  asks. For auto-rejoin, let an opted-in alt idle out (20 min, Error 278) and confirm it's back in
  the server within the measured lag plus 3 minutes, having followed the main.

## Out of scope

- Solving or interacting with any captcha.
- A per-account follow target other than the main (the "follow another account" choice is per
  launch, not stored).
