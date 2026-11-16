# Run Brief: v139-stability

## Ask
"I feel like we can do those and 3 and 4 at the same time", in answer to a recommendation to ship board items 1, 2, 5 and 7 as v1.39. Before that: "I want to look for more quality and stability improvements."

## Source
- **Kind:** findings board
- **Location:** `docs/cycles/2026-11-16-quality-and-stability/board.md` (branch `docs/quality-stability-board`, PR #289), items 1, 2, 3, 4, 5 and 7.

## Intent
RoRoRo stops being able to lose a user's saved accounts, stops reporting a normal quit as a crash, and keeps a true picture of which client is running and whether an account is in a game. Two smaller annoyances go with it: End task hanging, and a memory warning with nothing running.

## Done when
- Each of the six board items has a fix and a test that fails when the fix is removed.
- `dotnet build ROROROblox.slnx -c Release` has 0 errors and `dotnet test ROROROblox.slnx -c Release --no-build` passes in full.
- Nothing is tagged, pushed as a release or submitted. This run ends at a branch ready for the owner's smoke.

## Out of scope
- Board item 6 (exit cleanup times out): the cause is not pinned.
- Board items 8, 9 and 10, the measure-first list, and the "small" list, except where a fix here closes one in passing.
- Making dispatcher exceptions non-fatal: a design choice that is the owner's call.
- Moving account facts out of `accounts.dat`. It would change the stored-files table in `docs/PRIVACY.md` and the listing's privacy paragraph; item 2 of this run removes the frequent write without it.
- The version bump, release notes, listing audit and Store submission. Those are the release playbook, after the owner's smoke.

## Inferred
Each line is something the owner did not say.
- This is v1.39.0.0, a MINOR bump (from: the playbook's bump table; fixes users can see).
- Board item 1 is two list items: durable writes with a backup, and the facts refresh no longer rewriting the vault. (from: reading `AccountStore.SetFactsAsync` and `AccountFactsService`: the existing no-op check compares a record that includes `FetchedAtUtc`, which changes on every refresh, so it never skips.)
- An external polite close ends the process cleanly; a click on the X, Alt+F4 and the taskbar's Close still hide to the tray. (from: board item 5. The polarity matters: a mistake here would make the X quit RoRoRo, so the X path is identified positively and tested.)
- Clearing presence on session expiry follows what `ApplySessionLimited` already does. (from: the code.)
- The memory warning needs at least one Roblox client before the free-memory axis can cross. (from: board item 7.)
- "Reproduce before fixing" is met with a test that fails on today's code, written first. A live run of a dev build would read and write the owner's real `accounts.dat`, since there is no data-folder override, so no dev build is launched during this run. (from: the repo's rule of manual smoke, and a grep for an override that found none.)
- Workers are dispatched at the bulk tier and their reports are checked against each item's done-when lines at the judgment tier. (from: the seat's model era map.)

## Mode
- **Mode:** Attended, autonomous, one commit per item on `fix/v1.39-stability`.
- **Triggered by:** attended by default (08:21 local, seven items).
