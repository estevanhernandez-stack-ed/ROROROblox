# C1: mocks and probes

The build halts here, after item 5, before any XAML for the facts line, the panel, the Library or Play as. Two halves, both yours. Items 6, 7 and 9 build to the picks below and screenshot-compare against them before they close.

Open `c1-mocks.html` in a browser; the buttons at the top switch between the four built-in themes. `c1-brand.png` and `c1-flatline.png` are full-page captures of the same page. Every account and game in the mocks is made up.

## Half 1: the picks

| # | Decision | Options | Plan default |
|---|---|---|---|
| 0a | Follow mark on the name line (built in item 2) | keep, or move | keep |
| 0b | Cyan status dot for a marked account at rest (built in item 2) | cyan, or grey | cyan, as built; yours to veto |
| A | The row's facts line | A1 new line, collapses when empty · A2 always shown, dashes · A3 inline on the status line | A1 (assumption 21) |
| B | The panel | B1 three columns · B2 stacked groups · B3 icon strips | none set |
| C | The Library grid | C1 square icon tiles · C2 wide cards (list view shared) | none set |
| D | Play as | D1 one line, health word right · D2 two lines, status under the name | none set |
| E | A place with a saved private server but no favourite still shows in Recent when played publicly | keep, or hide | keep |

Reply in any shape, for example "0 keep, A1, B2, C1, D1, E keep".

## Half 2: the probes

These decide whether **Needs verification** and the **membership badge** ship. The probe only reads your vault and never prints a cookie. A run without `--live` sends nothing. Reports land under `docs/cycles/2026-11-01-account-library/probes/`, redacted.

The probe can run while the Store RoRoRo is running.

```powershell
cd <repo>
dotnet run --project tools/EndpointProbe -- --list                        # local names and ids, nothing else
dotnet run --project tools/EndpointProbe -- --account "<local name>" --live
```

Run `--live` once for each:

1. **An account in the captcha state.** The clan's follow-cancel-follow is the documented way in.
2. **An account whose cookie you know is expired.**
3. **An account Roblox currently shows as limited**, if you have one.
4. **A Plus account**, for `validate-membership`. Add a legacy-Premium account too if one exists.

A successful live ticket POST mints a real auth ticket and launches nothing. Any run you can't do tonight does not block items 6 to 9: Needs verification stays unset and membership stays off until the measurement exists, and the gate is re-checked at C2.

What the results decide (recorded in `docs/decisions.md`):

- **Needs verification ships** only if the captcha account's 403 carries a `rblx-challenge-type` that the limited account's 403 does not.
- **Membership ships** (as "Plus") only if `validate-membership` answers for the Plus cookie. Otherwise the fact is dropped from the row and from the privacy line.
- **Limited and Follow-marked never merge** unless the triples prove they are the same condition.

## Picks, 2026-11-02

| # | Pick |
|---|---|
| 0a | keep: the follow mark stays on the name line |
| 0b | cyan, as built |
| A | A1: a new line, collapsed when it has nothing to say |
| B | B1: three columns (asked separately the same morning; the first reply named no B option) |
| C | C1: square icon tiles |
| D | D2: two lines, status under the name |
| E | keep: a played place with only a saved private server still shows in Recent |

One ruling inside A1: last played comes from local history and is primed at load, so it counts as something to say. An account with history shows the facts line (last played alone) from the first paint. The "looks like v1.35 until the first refresh" property holds only for an account with no history and no note.

Probes: not run yet. Needs verification stays unset and membership stays off. Both are re-checked at C2.
