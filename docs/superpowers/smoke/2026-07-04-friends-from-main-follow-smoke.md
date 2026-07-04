# Smoke checklist — friends-from-main follow

**Branch:** `feat/friends-from-main-follow` · **PR:** (this branch) · **Spec:** [`../specs/2026-07-04-friends-from-main-follow-design.md`](../specs/2026-07-04-friends-from-main-follow-design.md)

The WPF window is manual-smoke by house convention — the pure decision (`FriendSourcePlan.Build`) and the async main-resolution (`TryResolveMainFriendSourceAsync`) are unit-tested; everything below is the human pass that gates merge.

## Setup

- [ ] Quit the installed RoRoRo from the tray first (single-instance guard will self-exit the dev build otherwise).
- [ ] Build + launch the dev build:
  - `"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build ROROROblox.slnx -c Release`
  - run `src/ROROROblox.App/bin/Release/net10.0-windows/ROROROblox.App.exe`
- [ ] Have **2+ saved accounts** with a **main set** (right-click a row → set as main, if needed). At least one alt should have its own (possibly empty) friends list.

## Core behavior

- [ ] **1. Default source = main.** Open the Friends picker on an **alt** row. It opens showing the **main's** friends. The header names the main; the hint under the switch reads *"Follow one to launch [Alt] into their server."*
- [ ] **2. Switch to the alt's own list.** Click **"View [Alt]'s friends."** The list switches to the alt's own friends; the hint hides (you're now browsing the launcher's own list); the header names the alt.
- [ ] **3. Launch identity is the alt, not the main.** Switch back to the main's friends → click **Follow** on an in-game, joinable friend. The account that launches and joins the server is the **alt** (the row you opened), *not* the main. Confirm the right client window comes up.
- [ ] **4. Main's own row = single source.** Open the picker on the **main's** own row. There is **no** switch button (single source); it behaves exactly as before this feature.
- [ ] **5. No main set = single source.** Unset the main (no account flagged main), open the picker on any row. It shows that row's **own** friends, **no** switch button.

## Edge cases

- [ ] **6. Rapid-toggle doesn't corrupt the list (re-entrancy fix).** With two sources, click the switch button several times fast. The list must **never** show a mixed/concatenated set of both accounts' friends — it settles on exactly one account's list. The switch (and Refresh) button visibly disable while a load is in flight.
- [ ] **7. Expired fetch names the right account.** If an account's session is expired, the "session expired" message names the account whose fetch **actually** failed — not whichever source you switched to afterward. (Easiest to see if you can get one account into an expired state and toggle during the load.)
- [ ] **8. Expired main, graceful fallback.** If the **main's** session is expired, opening the picker on an alt surfaces a clear, main-named message and you can still switch to the alt's **own** friends.

## Known / by-design (not bugs)

- Following into a **friends-only** server where the launching alt isn't the target's friend lands the alt at home — best-effort, no friendship detection (spec §4). Public servers always work.
- The existing subtitle ("works for public AND private servers if your friend's privacy + server allowlist permit") is the friends-only caveat — no separate caveat line was added.

## Result

- [ ] All checks pass → the branch is merge-ready; merge the PR.
- [ ] Anything off → note the check number + what you saw; it comes back for a fix pass before merge.
