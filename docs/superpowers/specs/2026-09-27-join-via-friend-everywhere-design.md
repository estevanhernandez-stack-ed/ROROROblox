# Join via friend, everywhere, with a captcha backup

**Status:** design agreed in conversation with Este, 2026-09-27; spec awaiting review
**Products:** RoRoRo (host), plugin contract, Ur OCR (plugin)
**Why now:** ELeonDog, an alt, lands on Roblox's "Verifying you're not a bot" page whenever it joins
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
   account row (or a small dialog for batch launches) says, for example: "ELeonDog joins through
   your main, and your main isn't in a game yet." It offers two choices:
   - **Follow another account**: a pick list of saved accounts that `EvaluateFollow` says are
     joinable right now. The launch follows the one picked.
   - **It's fixed, join directly**: clears the `JoinViaFriend` flag for that account and launches
     it directly. If the puzzle comes back, the captcha backup (below) catches it.

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
6. **The captcha backup, in Ur OCR.** A new trigger action, "Rejoin through the main":
   - A text trigger on "Verifying you're not a bot" (Ur OCR already reads text) fires it for the
     account whose window matched.
   - The action calls RoRoRo through the plugin contract: `StopAccounts([that account])`, waits for
     the client to exit, then `RequestLaunchTarget(that account, follow_user_id = main)`.
   - It only acts on accounts listed in the trigger (opt-in per account), and never on the main.
   - **Loop guard:** at most 3 rejoins per account per hour. The fourth match stops the action for
     that account and logs "ELeonDog hit the verification page 4 times in an hour; left it alone." A
     refused launch (main not joinable) also logs and waits for the next match.
   - It also works as a first-join check. If a launch that ignored the flag lands on the puzzle,
     for example an old RoRoRo build, the same trigger rejoins it.
   - Ur OCR asks for RoRoRo's stop and launch capabilities in its manifest; Este approves them
     once in RoRoRo's consent sheet.
   - Ur Task stops sending input to an account whose window shows the verification page, because
     its per-input foreground and colour checks already refuse an unexpected screen. Nothing new
     is needed there.

## Never

- Nothing clicks, types into or otherwise touches the verification page. It is closed, never
  answered.
- The main is never rejoined automatically.
- No silent direct join for a flagged account, on any path.

## Contract changes

Additive, contract version stays compatible:

- A new refusal reason `follow-target-not-joinable` on `LaunchResult`.
- No new RPCs. Ur OCR uses the existing `StopAccounts`, `RequestLaunchTarget` and `GetAccounts`
  (for the main's user id).

The contract is published on NuGet, so the host ships first, then Ur OCR takes the new package.

## Testing

- **Host:** one test per launch path in the table, each asserting that a flagged account resolves
  to follow-the-main, and asks or refuses when the main is not joinable. Tests for the "it's fixed"
  choice clearing the flag, the Squad Launch fallback change, and plugin refusals with the reason
  code.
- **Ur OCR:** tests for the rejoin action's sequence (stop, wait, follow), the opt-in list, never
  the main, and the 3-per-hour guard, all with a fake contract client.
- **Live, Dunder-MiffLan:** launch ELeonDog from the single Launch button with the main in the
  private server, and confirm it follows. Then stop the main and launch ELeonDog again, and
  confirm it asks. For the backup, confirm Ur OCR notices ELeonDog's verification page after a
  direct join and rejoins it through the main.

## Out of scope

- Solving or interacting with any captcha.
- A per-account follow target other than the main (the "follow another account" choice is per
  launch, not stored).
