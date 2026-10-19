# Run Brief: rororo-ux

## Ask
"Let's jump on the next thing here" (Este, 2026-10-19 12:50): the RORORO UX batch, master list section 2.4, run on Dunder while Neb runs PAIR.

## Source
- **Kind:** checklist (estate master task list) plus dashboard tasks
- **Location:** `Project-626Labs-1/docs/master-task-list-2026-10-18.md` section 2.4, verified against `ROROROblox` `origin/main` (`c4f2acb`, v1.33.0.0 shipped both channels 2026-10-08); dashboard project `PBWgg5mimZyAzAG3niAp`

## Intent
Close the v1.33 smoke's nine UX findings that are still open, the two plugin-catalog hygiene rows that republish wrong data every morning, and the small parked items, so the next tag (v1.34) carries them. Two items that touch the plugin contract or need a diagnosis are built or investigated on hold branches.

## Done when
- Every `go` item is merged to the integration branch `cart/rororo-ux` with the full solution build and test suite green (unit + harness, Release, named `.slnx`), pushed, with one PR against main.
- `hold` items sit on their own branches with PRs.
- `report.md` carries the status-change list for the dashboard.

## Out of scope
- The captcha-marked / 2FA-vs-expired / Ur Reset cluster (`g5fgibvy7`, `vpk6ay1eu`, `6yktlfa0q`): needs the 403-equals-captcha experiment first, Este's hands.
- AI connector (`q60h0q3op`): three repos, contract-first, its own cycle.
- Windows version-gate rescope (`8pm4lzbdv`): decide.
- Translation verifier spot-review (`rdukoqcyz`): a run of the Translation Verification MCP, not repo work; gate for the v1.34 tag.
- Store support-email typo and support contact (`0h7s65ljx`, `bxranna8z`): Partner Center, folds into the Store Listing Console run (grant 7: drafts only).
- A host-level mute that plugins obey (the contract half of `y6u70eghn`): contract change plus plugin repos; this run does the honest wording.

## Inferred
- Harvest closed two rows as shipped in v1.33 per `docs/feature-ledger.md` line 11 and the tree: alert-sound preview ("Hear it" button, `iradsq3is`) and the nav rename to Language and Appearance (`ujufm3q25`). (from: ledger + `SettingsPage.xaml:413`)
- The catalog's `626 Labs LLC` publisher (5 rows) and `ur-mcp 0.2.0` are still on main; the hub's drift loop is at `626labs-hub/scripts/refresh-rororo-plugins.mjs:70-76` and only warns. (from: grep)
- Rules every item follows are the keystone's hard rules: fences with ratchet ceilings, new settings need the four `IAppSettings` fakes updated, new strings go into all seven catalogues (`CultureKeyParityFenceTests`), never a bare `dotnet build`. (from: `CLAUDE.md`)
- Cut from `origin/main` via the detached worktree `~\.cwt\rororo-main`; Este's checkout is on `docs/localization-wave-2`, 184 behind. (from: the cut rule)

## Mode
- **Mode:** Unattended, under the 2026-10-19 standing grants (push and PR without asking; task status moves).
- **Triggered by:** the ask, 12:50.
