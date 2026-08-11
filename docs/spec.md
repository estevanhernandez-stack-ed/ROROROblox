# RORORO — Technical Spec: Settings becomes a place

Implements [`docs/prd.md`](prd.md). Cycle target **v1.18.0.0** (current shipped: 1.17.0.0, published
as a pre-release).

**Anchor:** every page holds what its name promises.

This file is the canonical technical artifact for this cycle, as v1.17's was.
**Per `CLAUDE.md`, archive it into `docs/superpowers/specs/` before the next Cart round overwrites
it** — v1.17 was nearly lost that way and the obligation is now written into the file table.

Every current-state claim was read out of the tree on 2026-08-10. The recon is not re-derived here;
it lives in
[`2026-08-10-register-reverification/`](superpowers/research/2026-08-10-register-reverification/).

---

## 1. Stack

No new dependencies. Nothing here reaches outside what ships.

| Layer | What this cycle touches |
|---|---|
| `ROROROblox.Core` | Nothing. No contract change. |
| `ROROROblox.App` | `Preferences/PreferencesWindow.xaml(.cs)` (the bulk), `Core/AppSettings.cs` + `Core/IAppSettings.cs` (one field — note these live in **Core**, not App), `ViewModels/MainViewModel.cs` (compact persistence), `MainWindow.xaml.cs` (restore), `Controls/ControlStyles.xaml` (one style), 3 enumerated button sites |
| `ROROROblox.Tests` | `AppSettingsTests`, `PreferencesCopyTests` (new), `SettingsReachabilityTests` (new) |

## 2. Runtime, deployment, identity

Unchanged from v1.17.0.0. Version moves to **1.18.0.0** in lockstep across
`ROROROblox.App.csproj` `<Version>` and `Package.appxmanifest` `Identity Version`. Capabilities:
`runFullTrust` only.

**Store submission stays deferred.** The direct-download channel continues; whether v1.18 ships as a
pre-release like v1.17 or as a full release is a release-time call, not a build decision. Note the
consequence either way: `UpdateChecker.cs:43` constructs `GithubSource(..., prerelease: false)`, so a
pre-release reaches nobody automatically.

## 3. The four forks the PRD routed here, resolved

### 3.1 The hierarchy answer is a style that already ships

*Resolves `prd.md > Story 2.1`, and it resolved itself on inspection.*

`SectionHeadingStyle` ([`ControlStyles.xaml:143-148`](../src/ROROROblox.App/Controls/ControlStyles.xaml#L143-L148))
is `FontSize 13 / SemiBold / WhiteBrush / Margin 0,18,0,6`. Preferences' three hand-rolled headings
are **13px SemiBold White** — identical in every property except the margin.

So the style is not merely available, it is the same thing already being written by hand. And its
`Margin="0,18,0,6"` is precisely the level the page lacks: **18px above, 6px below, placing a heading
between cards rather than inside one.**

**Decision: a section is a `SectionHeadingStyle` heading standing outside the cards; a setting is a
row inside a card. No second container primitive.** That directly satisfies the PRD's constraint that
the answer must not be another container, because another container is the two-meanings defect
restated.

Consequence for the nine cards: cards holding a single control collapse into the section they belong
to rather than each wearing full chrome. Measured contents are 1/1/2/1/2/2/**8**/2/3 — the register
row's 10 does not reproduce, and 8 plus the idle card's 2 is the Alerts *page* total, so the row
attributed a page figure to a card. Re-measured 2026-08-10; cards went 9 → 7 and single-control cards
3 → 0 when item 1 landed.

Rejected: a new `SubCardBorderStyle`, a `GroupBox`, or an `Expander`. Each adds a primitive whose
meaning must then be taught, and the row's whole complaint is that one primitive already carries two
meanings.

### 3.2 Careful mode goes on Startup, and the page already mixes the two

*Resolves `prd.md > Story 1.2`.*

The rail has no launch page and `prd.md` non-goal 4 forbids a sixth for one checkbox.

**Decision: Startup.** Not a compromise — that page already holds *"Launch my main account when
RoRoRo starts"*
([`PreferencesWindow.xaml:110-118`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L110-L118)),
which is a launch setting, not a startup one. The page's contents are already about starting *and*
launching; careful mode is the third member of a set that exists.

**The nav item is not renamed.** `docs/ui-routes.json` declares the Preferences pages as capture
surfaces and the base `03-preferences` surface is this page; renaming the item churns the capture
round for a label change. Revisit if a fourth launch setting appears.

Shape: **a third row inside Startup's card.** Written as "a third card" before item 1 ran; item 1
collapsed that page's two single-control cards into one card with two rows, so a third card would
re-introduce exactly the single-control card the hierarchy work removed. Match the rows above it:
SemiBold 13px `CheckBox` label, muted 11px hint at `Margin="22,4,0,0"`, row panel at
`Margin="0,14,0,0"`. Binds `IAppSettings.CarefulSquadLaunch`, the
same value `SquadLaunchWindow.xaml.cs:59,73,79` reads and writes. Both surfaces re-read on open;
neither caches.

### 3.3 Epic 2 lands before Epic 1

*Resolves `prd.md > Open questions`.*

**Decision: hierarchy first, then the new controls.**

Epic 1 adds four memory controls to the Alerts page. Epic 2 changes how that page groups things.
Doing Epic 1 first means authoring four controls into a structure scheduled for replacement, then
re-authoring them — the rework the PRD named.

The reverse risk the PRD raised does not hold up: §3.1's answer is a heading placed between cards,
which is generic. It does not depend on knowing what the memory section will contain, only that it
will be a section. So the ordering cost is one-directional and the choice is not close.

### 3.4 New work ships named; the backlog stays F-052's

*Resolves `prd.md > Story 2.2`'s explicit demand for a statement.*

0 of 137 Button/ComboBox/ToggleButton/TextBox declarations carry an `AutomationProperties.Name`. F-052
owns that cross-surface pass and is not in this cycle.

**Decision, stated as a rule rather than a one-off: every control this cycle adds ships with an
accessible name, and no control this cycle does not otherwise touch gets retro-named.**

That draws a line a future reader can apply without re-litigating: new work never adds to F-052's
backlog, and F-052's backlog does not get half-eaten here in a way that makes its own count wrong —
which is exactly how F-032 drifted 11 → 15 while two waves built machinery around it.

Section headings added by §3.1 get names too; they are the structure a screen-reader user navigates.

## 4. Reachability, and the check that would have prevented this cycle

*Implements `prd.md > Epic 1`.*

### 4.1 The four watchdog settings

`MemoryWatchdogEnabled`, `MemoryReserveMb`, `MemoryCapMb`, `ProjectionWarnMinutes` are persisted at
[`AppSettings.cs:322-392`](../src/ROROROblox.Core/AppSettings.cs#L322-L392) and referenced by **zero**
`.xaml`. They land on the Alerts & memory page as a section per §3.1, beside the routing already
there.

Types: one bool, three numeric. The numerics need bounds — `prd.md > Story 1.1` requires an
out-of-range value to be refused **visibly**, which is the one acceptance criterion no register row
asked for and the one most likely to be skipped as "not in the row."

**Validation lives in the view layer and reports through the same status-line mechanism §5 defines**,
rather than silently clamping in the setter. A setter that clamps is indistinguishable from a setter
that worked.

### 4.2 The check that makes this class of defect fail a build

F-023 survived because nothing connects *persisted* to *reachable*. `prd.md > What we'd add` lists
this and it is cheap enough to do now:

**`SettingsReachabilityTests`** — every property on the settings record is either referenced by name
in App XAML or code-behind, or appears in a named exemption list with a reason. Same shape as the
XAML literal fence v1.17 shipped, and the same rule: **an exemption names why, so an unreachable
setting is a decision rather than an oversight.**

This is the only new mechanism in the cycle and it is what stops the cluster regrowing.

### 4.3 Muted accounts and the theme re-ask

Both read existing state. The muted set is materialised at
[`App.xaml.cs:1376`](../src/ROROROblox.App/App.xaml.cs#L1376); the count and an unmute-all sit in the
Alerts section. Zero muted renders as absence, not `0`.

The theme re-ask calls the existing consent setter. One control on Appearance.

## 5. Failure says so, using the pattern already on the page

*Implements `prd.md > Epic 3`.*

`AlertsStatusLine` ([`:357-358`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L357-L358))
is a `TextBlock`, 11px, wrapping, `CyanBrush`. A sibling `ThemeStatusLine` goes in the Theme section.

**One deliberate divergence: a failure message takes `RowExpiredAccentBrush` and the `▲` prefix, not
`CyanBrush`.** Cyan is the accent — the same treatment a success would get — and v1.17 established
`RowExpiredAccentBrush` plus `▲` as this app's warning vocabulary across expired rows, idle chips,
memory chips and the compat banner. A fourth warning surface should speak it too. `AlertsStatusLine`
is left alone; retro-fitting it is not this cycle's row.

Covers both Epic 3 stories: a failed theme persist (theme still applies live, session not degraded)
and an unreadable theme file, named. Success stays silent.

## 6. Voice and weight

*Implements `prd.md > Epic 4`.*

**Copy (Stories 4.1).** Six settings across the run-on-login and Discord cards go to second person
with terminal periods. The run-on-login hint currently reads *"Adds a value under HKCU Run. Removes
it when unchecked."* — a registry path presented as an effect, to an audience that does not have one.
Its first clause also restates the checkbox above it. Clan-facing register per `CLAUDE.md`.

**Close buttons (Story 4.2).** Nine Close buttons, measured today at 5 accent-filled and 4 secondary.
Sweep to secondary; Diagnostics is the precedent and does not change. The window's own primary action
keeps its fill.

**The destructive variant (Story 4.3), bounded by enumeration.** `ControlStyles.xaml` has two ranks
and no destructive one. Add `DestructiveButtonStyle`, and **assign it to a named list rather than by
sweep** — the PRD's boundary made operational:

| site | why it qualifies |
|---|---|
| Remove, on the account row | destroys a saved account and its cookie |
| Clear, in History | destroys the session log |
| Stop all Roblox instances | halts every running client at once |

**A site not on that list is F-068's, not this cycle's.** If applying the variant starts requiring
judgement calls about a fourth button, that is the signal the PRD named: this story drops to F-068's
cycle rather than dragging its 61 sites forward.

## 7. Persistence

*Implements `prd.md > Epic 5`.* `CompactMode` joins the settings record;
[`MainViewModel.cs:695-709`](../src/ROROROblox.App/ViewModels/MainViewModel.cs#L695-L709) persists on
set; `MainWindow` restores on load. No migration — the record's defaulted fields load cleanly on an
existing file, which `Core/AppSettings.cs:463-465` already documents.

## 8. Testing

| test | asserts | new? |
|---|---|---|
| `SettingsReachabilityTests` | every persisted setting is reachable or exempted with a reason | **new**, §4.2 |
| `PreferencesCopyTests` | second person, terminal periods, no first person, no duplicated label | **new** |
| `AppSettingsTests` | `CompactMode` round-trips; absent field defaults cleanly | extended |
| `ContrastPairGateTests` + the three v1.17 rendered gates | unchanged and green | existing |

**Manual, and non-negotiable.** Set each memory value and restart. Enter an out-of-range value and
read the message. Break the settings write and confirm the theme applies live and says so. Drop a
malformed theme JSON and read the report. Walk Settings with the keyboard. Look at all four themes,
flatline included — a hierarchy carried in colour fails the theme the last cycle shipped.

## 9. Key technical decisions

1. **The hierarchy is a heading that already ships**, not a new container. `SectionHeadingStyle` is
   already what the page hand-writes, and its 18/6 margin *is* the missing level.
2. **Careful mode goes on Startup** because that page already holds a launch setting. No sixth page,
   no rename.
3. **Hierarchy before content**, because the ordering cost is one-directional.
4. **New work ships named; F-052 keeps its backlog whole.** Stated as a rule so it survives the
   cycle.
5. **A failure message speaks the app's warning vocabulary**, not its accent.
6. **The destructive variant is assigned by enumeration, not by consequence-in-general**, which is
   how a 13-row cycle stays a 13-row cycle.
7. **`SettingsReachabilityTests` is the only new mechanism**, and it exists so this cluster cannot
   regrow silently.

## 10. Open issues

- **F-050 stays `open`** and is untouched. Its status cell auto-deletes the gate exemption and
  reddens brand 3.79:1, midnight 4.16:1 and magenta-heat 3.29:1 against a 4.5 threshold.
- **F-052's cross-surface naming pass** remains open, deliberately not half-eaten (§3.4).
- **F-068's 61 flat call sites** remain open; §6 touches three enumerated buttons and starts nothing.
- **F-091 plugin theming** remains open and is its own cycle.
- **`DefaultPlaceUrl` is documented as editable and is not.** `Core/IAppSettings.cs:7` says *"the
  Preferences dialog allows editing"*; the App references it **zero** times, and `RobloxLauncher.cs`
  calls it "(legacy single-URL setting)" and "vestigial" while a test pins that it must be ignored.
  Found by item 2's fence, exempted there to keep old files deserializing. **The honest end state is
  deleting the field, not adding a control** — worth a register row, and deliberately not opened
  mid-build.
- **`EdgeRemediationAnswers` is structurally invisible to any name-derived fence.** The persisted
  property is plural and its accessor is singular (`Get/SetEdgeRemediationAnswerAsync`), so the names
  do not match. Exempted rather than papered over.
- **`AlertsStatusLine` still uses `CyanBrush` for what may be a failure message.** Noticed while
  writing §5, out of scope, worth a row if it turns out to report failures too.
