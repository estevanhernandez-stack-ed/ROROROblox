# RoRoRo UIA capture tool: design

**Date:** 2026-08-09
**Status:** approved, ready for implementation planning
**Supersedes:** the constraint claims in `docs/ui-capture-checklist.md` and `docs/ui-routes.json` (see §2)

## 1. Goal

Rebuild the UI capture harness the VibeGlow campaign has been running on since wave 1, as a
committed artifact that anyone can run again.

The harness was never in git. `git log --all --diff-filter=D` finds nothing, so it was not deleted,
it simply never landed. Every glow wave's evidence was produced by something that no longer exists
and cannot be re-run. Reproducing a finding today means reproducing the tool first.

Scope, as approved: rebuild plus safety rails. Not a pixel-assertion gate. Measuring contrast on
captured pixels overlaps Phase 2 of
`docs/superpowers/specs/2026-08-09-rororo-rendered-contrast-gate-design.md` and belongs there, not
here.

## 2. What the exploration falsified

The existing docs describe constraints that shaped the old harness. Three are false, and the design
depends on that, so the evidence is recorded rather than asserted. Every claim below was measured
against the running app on 2026-08-09.

### 2.1 "A popup is not in the main window's automation subtree even while open"

False. All eight Tools menu items resolve from the main window's subtree: History, Diagnostics,
Games, Plugins, Open log folder, Stop all Roblox instances, Welcome tour, About. A desktop-root
walk finds 24 MenuItems, of which 8 are ours.

Wave 3 replaced the nav buttons with `controls:ToolsDropDownButton` (`MainWindow.xaml:1068`), a
custom control carrying a ContextMenu, which reparented the items. It exposes both `Invoke` and
`ExpandCollapse`, and its own source comment notes ExpandCollapse was implemented precisely because
the Button pattern got the open/close behaviour wrong. `ExpandCollapse` is therefore the correct
verb for opening it, not `Invoke`.

`ui-routes.json` deleted five routes on the strength of the popup claim. Those five surfaces are
routable.

### 2.2 "The rail's pages cannot be routed"

False, though its premise is correct. The five rail pages are `ListItem`s exposing
`SelectionItem`, `ScrollItem` and `SynchronizedInput`, with no `InvokePattern`, exactly as
documented. The conclusion does not follow: `SelectionItemPattern.Select()` routes them. Driving
Accounts, Alerts & memory and Appearance each returned `IsSelected=True`.

The claim conflated "carries no InvokePattern" with "cannot be routed". The route vocabulary was
too narrow, not the app.

### 2.3 "Almost nothing in this app carries an AutomationId"

Overstated. Present today: `SettingsNav`, `ThemePicker`, `BuildThemeButton`,
`OpenThemesFolderButton`, `RunOnLoginToggle`, `LaunchMainToggle`, `DefaultGameWidget`,
`TitleBarMinimizeButton`, `TitleBarMaximizeButton`, `TitleBarCloseButton`. Enough to anchor the
routes that matter, including the nav rail.

### 2.4 The name-matching hazard is worse than documented

The docs warn that a copy change silently breaks a route. The real failure is quieter and worse:
a name match binds the **wrong element**, and the run proceeds.

- At desktop-root scope, `Name="Settings"` matched a `Chrome_WidgetWin_1` window. A full
  exploration turn was spent reading a browser's UIA tree. Window lookup must be process-scoped.
- Inside the app, five elements are named "Settings": a Window, a TitleBar, two Texts, and the
  Button. `FindFirst` returns the Window, which carries no `InvokePattern`. This is what threw
  `Unsupported Pattern` during exploration.
- Account rows repeat `Friends`, `Launch As` and `Remove` once per account. With eight accounts
  that is 32 identically named buttons. Row routes are meaningless without ancestor scoping.

### 2.5 The `flatline` theme does not exist

The checklist specifies three theme rounds, the third being `flatline`. The app ships three
built-ins and `flatline` is not among them: `brand`, `midnight`, `magenta-heat`. This is the second
missing artifact discovered alongside the missing script.

Consequence: the tool enumerates themes at runtime instead of hardcoding a list, so `flatline`
joins the rotation the day it ships and needs no code change.

### 2.6 Measured capture behaviour

| Method | Result |
| --- | --- |
| `PrintWindow`, `PW_RENDERFULLCONTENT` (flags=2) | `ok=True`, 0.0% black, full fidelity, **captured the main window in isolation while a dialog overlaid it** |
| `PrintWindow`, flags=0 | `ok=True`, 13.3% pure-black sampled pixels |
| `CopyFromScreen` | 0.0% black, but captured the overlaying dialog instead of the target |
| Any method, window minimized | frame bounds at the `-32000` sentinel, 160x28, 100% black |

`PrintWindow` with flag 2 is the primary path. It needs no foreground, so the run does not fight
the operator for focus and no stray window can occlude evidence. `CopyFromScreen` remains the
fallback for elements with no window handle.

### 2.7 .NET 10 breaks GDI+ under `Add-Type`

.NET 10 moved `Bitmap` and `Graphics` behind `System.Private.Windows.GdiPlus` and
`System.Private.Windows.Core`. C# compiled through `Add-Type` that names either type fails with
CS0012 regardless of the assemblies referenced.

Structural consequence, not a workaround: the compiled surface is **pure Win32 P/Invoke only**,
returning primitives and one `RECT` struct. Every GDI+ call happens in PowerShell, where types
resolve at runtime rather than compile time.

## 3. Shape

Two files, matching the documented shape and the `scripts/` single-file convention.

| Path | Responsibility |
| --- | --- |
| `scripts/capture-ui.ps1` | The whole tool: resolver, capture, guards, theme loop, reporting |
| `docs/ui-routes.json` | Route data. Rewritten; the current format cannot express the safety rules |

A note on durability, since it motivated the alternatives considered. The harness did not vanish
because PowerShell rots. It vanished because nobody committed it. Committing it is the fix.
A C# project in the solution was considered and cut: it would add build weight to every
`dotnet build` for a tool that can never run in CI, because it drives a live desktop app.

## 4. Route format

```json
{
  "app": "RoRoRo",
  "processName": "ROROROblox.App",
  "deny": ["Stop all Roblox instances", "Remove", "Launch As",
           "Launch multiple", "Squad Launch"],
  "surfaces": [
    {
      "id": "03d",
      "name": "preferences-appearance",
      "open": [
        { "do": "invoke", "name": "Settings",   "type": "Button" },
        { "do": "select", "name": "Appearance", "type": "ListItem",
          "within": "SettingsNav" }
      ],
      "capture": { "type": "Window", "name": "Settings" },
      "close":   [ { "do": "close-window", "name": "Settings" } ]
    }
  ]
}
```

Three rules carry the safety weight.

**`type` is mandatory on every step.** A step names a control type and a name, and the resolver
additionally requires the pattern its action needs. "Settings" as a `Button` requiring
`InvokePattern` resolves to exactly one element out of the five that share the name. A step that
resolves to zero or more than one element is an error, never a silent first-match.

**`within` scopes the search to an AutomationId ancestor.** This is what makes row-level routes
expressible at all given 32 identically named buttons, and it is how the rail is addressed.

**`do` is a closed vocabulary of four verbs**, chosen from the patterns the app actually exposes:

| Verb | Pattern | Used by |
| --- | --- | --- |
| `invoke` | `InvokePattern` | buttons, menu items |
| `select` | `SelectionItemPattern` | rail pages, theme picker items |
| `expand` | `ExpandCollapsePattern` | Tools button, ThemePicker combo |
| `close-window` | `WindowPattern.Close` | dialog teardown |

`capture` names an element rather than a window title. If it resolves to a nonzero
`NativeWindowHandle`, the tool calls `PrintWindow` on that handle. If the handle is zero, as with
popups and menus, it falls back to `CopyFromScreen` over the element's `BoundingRectangle`. One
capture path, no per-surface special casing.

The `deny` list is enforced inside the resolver. A step or a `-Watch` interaction targeting a
denied name aborts the run. These routes now reach "Stop all Roblox instances", "Remove" and
"Launch As", all of which have real side effects on live data and live game clients. A warning is
not sufficient for that.

## 5. Capture pipeline

Per surface: resolve, open, wait-for-stable, secret scan, capture, blank-frame check, write, close.

**Window state.** Restore when `IsIconic`, then verify the restore took. Reject
`DWMWA_EXTENDED_FRAME_BOUNDS` at the `-32000` sentinel or with non-positive dimensions.
`GetWindowRect` is not used: it includes the invisible resize border on Win10+ and would bake dead
margin into every capture.

**Wait-for-stable, not sleep.** After each step, poll until the capture target exists and its
bounding rectangle is unchanged across two consecutive reads, with a bounded timeout. Fixed sleeps
are how a harness like this becomes flaky on a slower machine.

**DPI.** `SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` at startup.
Without it a non-aware host receives virtualized coordinates and captures land cropped or scaled on
a scaled display.

**Blank-frame detection.** After capture, sample the bitmap. If more than 95% of sampled pixels are
a single colour, fail that surface loudly. The failure mode is measured and machine-dependent
(§2.6), so the tool detects it rather than writing a black PNG and calling it evidence.

## 6. Secret scan

Before any PNG is written, the tool scans the UIA text subtree of the capture target for two
patterns:

- `discord(app)?\.com/api/webhooks/\d+/[A-Za-z0-9_-]{20,}` — a webhook URL carrying its token
  segment. The token is the entire auth; anyone holding it can post to the channel.
- `_\|WARNING:-DO-NOT-SHARE-THIS` — the literal prefix of a `.ROBLOSECURITY` cookie value.

A hit aborts the run and names the offending surface and field. Nothing is written.

The scan runs on **every** capture, not only on `03b preferences-alerts`. Two reasons. The Discord
page `03c` carries a live status line that could render the same URL, so pinning the guard to `03b`
guards the wrong surface. And a `.ROBLOSECURITY` value in a PNG is strictly worse than a webhook,
being the repo's loudest prohibition, for the cost of one more regex.

This replaces a prose warning in the checklist with a mechanical guard. Wave 2's own scripted
capture produced exactly such a PNG, tracked as F-076, whose masking fix has since shipped.

**Stated limit.** UIA text is not the rendered pixels. A value could render without being exposed
to UIA, and the scan would miss it. This substantially reduces the risk. It does not prove a PNG is
clean. The checklist's prose warning stays in place as backup rather than being retired by the
guard, so that nobody reads the guard's existence as making the warning obsolete.

## 7. Theme rounds

The tool reads the `ThemePicker` combo and runs one full round per theme it finds.

Theme items are matched on the substring `Id = <id>,` within the item's UIA name. The picker sets
`DisplayMemberPath="Name"` (`PreferencesWindow.xaml:409`), which is visual only, so UIA exposes the
`Theme` record's `ToString()` rather than the display name. Matching on the id substring is stable
against that.

Today this yields two rounds, `brand` and `magenta-heat`. It becomes three when `flatline` ships,
with no change to the tool.

Evidence lands as `docs/ui-evidence/NN-<surface>--<theme>.png`, gitignored at `.gitignore:83`.

Each round writes `docs/ui-evidence/run-<theme>.json`: timestamp, app version, resolved theme id,
DPI scale, monitor, and per-surface status. Evidence that cannot say which build produced it is how
the findings register drifted six rows out of date.

`-DumpUia` writes the resolved tree alongside each PNG as `NN-<surface>--<theme>.uia.txt`. The
checklist wants this on at least the `brand` round.

Capture notes inherited from the checklist: one monitor and one scale factor for a whole round,
since a Windows scale change alters pixel dimensions mid-round.

## 8. Failure handling

Severity splits two ways.

**Continue and report.** A route that fails to resolve records the surface as failed and the run
proceeds. One broken route must not cost the other seventeen. The final report names every failure
and the run exits non-zero.

**Abort immediately.** A secret-scan hit, or any step resolving to a denied name.

A vacuity floor guards the report itself: a run that captured 3 of 18 surfaces must not exit
looking like success.

## 9. Verification

**`-Verify` mode** resolves every step of every route without invoking anything, asserting each
resolves to exactly one element carrying the pattern its verb requires. This is the direct answer
to the documented failure mode. A copy change now fails a fast read-only check instead of silently
capturing the wrong window.

**Route-file schema test**, in `src/ROROROblox.Tests`: every step carries a `type`, every `do` is a
known verb, ids are unique, surface ids match the checklist's list, and no route targets a name on
the deny list. The last is a safety property, and safety properties should fail at build time
rather than be discovered while the tool drives a live app.

**Deliberately cut:** an xUnit test asserting every route name exists somewhere in the app's XAML.
A name existing somewhere in the tree is weak evidence, because the same string legitimately lives
in several windows. The strong version of that check requires a running app, which is what
`-Verify` is.

## 10. Surfaces

Eighteen live surfaces. `04 preferences-scrolled` was retired by wave 2 and is not captured.

In scope: `01 main-window`, `02 main-window-empty`, `03 preferences`, `03a preferences-accounts`,
`03b preferences-alerts`, `03c preferences-discord`, `03d preferences-appearance`, `05 about`,
`06 history`, `07 diagnostics`, `08 games`, `09 plugins`, `10 theme-builder`, `11 tray-menu`.

Neighbors, context only: `20 welcome`, `21 squad-launch`, `22 join-by-link`, `23 export-accounts`.

Surfaces `05` through `09` are routable via Tools per §2.1, reversing the checklist's `-Watch`
designation. `20 welcome` is reachable through the Tools item "Welcome tour".

## 11. Known gaps

Stated rather than buried, because each is a real limit on what the first run will produce.

**`02 main-window-empty` is not capturable against the live profile.** It requires zero accounts
and the profile has eight. This is the accepted cost of the approved decision to capture against
real data, which was chosen because real accounts are better visual evidence: real row widths,
avatar loading, and expired-session states are what a glow review needs to see. It is worth naming
that this is the surface most likely to carry unreviewed chrome, precisely because nobody sees it.
Reversing it means specifying the `RORORO_DATA_DIR` override, which is app surface across roughly
twelve hardcoded `LocalApplicationData` call sites. `Environment.GetFolderPath` ignores the
`LOCALAPPDATA` environment variable, so there is no shell-level shortcut.

**`11 tray-menu` routability is unverified.** It is a Hardcodet NotifyIcon context menu, plausibly
its own in-process HWND. Implementation resolves it. If it does not route, it is the one genuine
`-Watch` surface.

**`21`, `22`, `23` have unverified paths.** Implementation resolves these against the live app.

`21 squad-launch` needs care, because §4 denies the very button §10 lists as a surface. That is
deliberate and not a contradiction: the button is denied until someone confirms what it does. If it
opens a dialog, it comes off the deny list and becomes a normal route. If it launches Roblox
directly, it stays denied and the surface is captured in `-Watch` mode or dropped. Default-denied
until proven safe is the right direction for a control that may spawn game clients, so the
resolution is an implementation step with an explicit verification, not a judgement call made now.

## 12. Out of scope

- Pixel-level contrast assertions. That is Phase 2 of the rendered contrast gate.
- Error and consent modals, per the checklist's own out-of-scope list. They are interruptions, not
  navigation.
- CI execution. The tool drives a live desktop app with a real profile and cannot run headless.

## 13. Follow-ups this exploration surfaced

Not part of this build, recorded so they are not rediscovered.

- **The `ThemePicker` combo exposes `Theme.ToString()` as its accessible name.** A screen reader
  reads the entire record, including every hex value. `DisplayMemberPath` fixes the visual only.
  Candidate row for the findings register, alongside the existing `AutomationProperties.Name`
  coverage finding.
- **`flatline` still does not exist.** Already tracked as a dashboard task; §2.5 is the evidence.
