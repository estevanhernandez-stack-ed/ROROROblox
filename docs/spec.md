# RORORO — Technical Spec: flatline, the readable theme

Implements [`docs/prd.md`](prd.md). Cycle target **v1.17.0.0** (current shipped: 1.16.0.0).

**Anchor:** carry distinction without colour.

Every ratio in this document was measured on 2026-08-10, not computed by hand. The method is in
§11.1: the candidate palettes were resolved through the app's own `ThemeService.ApplyTo` and
measured with `ContrastGuard.RatioBetween`, against the pair list scanned live out of the XAML —
the same path `ContrastPairGateTests` takes. That method is the point. This cycle exists partly
because three register rows quote flatline ratios that no artifact has ever reproduced, and a spec
that answered that with more arithmetic in a doc would be repeating the defect while describing it.

This cycle breaks the repo's spec-first pattern deliberately. Cycles v1.1 through v1.7.0 did the
substantive design in a canonical doc under `docs/superpowers/specs/` and compressed `spec.md` to a
pointer-stub. Este's ruling this session routes design through Cart instead, so this file is the
canonical artifact for this cycle. Prior cycles' specs are indexed in the appendix and are not
superseded.

---

> ## Banner correction — what shipped, versus what this document proposed
>
> **Added at item 8, 2026-08-10, after the build.** Per `CLAUDE.md`'s "don't rewrite the canonical
> spec on drift" rule, nothing below is rewritten: the original reasoning stands as written and this
> block names where reality diverged. Section-level corrections carry their own blockquotes in §5.3,
> §5.4, §6.2, §6.3 and §7, in the same shape as the one §5.1 already carries.
>
> **The cycle ran ten items, not eight.** Items **3a** and **3b** were added at `/build` and approved
> rather than deferred. Both came from defects this document never enumerated, and both were found by
> the machinery the earlier items built rather than by re-reading the plan.
>
> - **3a — F-088, a fifth status-colour site.** §5.3 enumerated four. The status bar's live-process
>   dot held two literals inside a `Setter.Value`, the same brand green and grey the deleted
>   `StatusDotBrushConverter` held. It shipped with F-080 in PR #96 and is present on `main`, so it is
>   not a regression from this cycle — it is a site this cycle did not know about. **Both of item 3's
>   fences were structurally blind to it**: `ThemedStatusColourTests` walked `*.cs`, and
>   `ContrastPairGateTests` reads `Background` / `Foreground` attributes. A raw hex in a `Setter.Value`
>   is neither. The durable half is the fence, not the dot. `ThemedStatusColourTests` gained a third
>   fact that scans App **XAML** for literal `#RRGGBB` outside an allow-list, and the allow-list
>   carries a rule with teeth: **a literal is permitted only when an OPEN register row already owns
>   it**, with the finding id cited inline.
> - **3b — F-089, `SelectionDotStyle`'s four un-themed hexes.** Found by 3a's own scan on its first
>   run, in `App.xaml`'s `ControlTemplate` for the batch-selection toggle — a control that ships on
>   **every** account row, with the ring drawn whether or not the row is selected. Under flatline it
>   rendered brand cyan on an achromatic field. F-089's allow-list entry was retired *before* the fix
>   so the fence went red on all four hexes first, and the literal ceiling dropped 101 → 97. This does
>   **not** contradict §6.4, which recorded the toggle clean for *redundancy*: it carries its state in
>   shape, filled versus hollow, and that is untouched. A brand hue the theme cannot reach is a
>   separate defect from a state carried only in colour.
>
> **What held exactly as specified.** §4.1's palette, §4.3's measurements, §4.4's no-prompt claim,
> §4.5's App-layer lookup (the `Theme` contract stayed at ten slots), §6's three redundancy devices
> shipping for all four themes with no theme conditional, and §8's fixture — which reproduces all six
> recorded ratios to four decimals, with the 4.34 × 2.99 = 12.98:1 cross-check holding. Flatline
> clears every declared pair at 4.5:1 with **zero new exemptions**; white on the dark accent measures
> 4.68:1, so F-050's 3.20 floor is not load-bearing for it. **F-050 stays `open`**, per §15.6.
>
> **What was wrong in this document.** §5.3's site count (four, actually five — see 3a). §7's
> out-of-scope list, which named five surfaces and missed two, both now register rows: `ConsentSheet`
> (F-087) and `SelectionDotStyle` (F-089, since fixed). §7's Bloxstrap-banner literal count, which is
> **three** and not two (F-085). §4.5's "subject to copy polish at `/build`" — the sentence shipped as
> drafted and was approved at C1. §16, which is superseded by the list now in place.
>
> **Register after the cycle:** 34 clean · 54 open · 1 closed-as-ruled · 89 total.
>
> ### Line citations below are as-of-`/spec`, 2026-08-10 pre-build
>
> Items 3, 3a, 3b and 4 moved `MainWindow.xaml`, `AccountSummary.cs`, `ThemeStore.cs`,
> `PreferencesWindow.xaml` and three test files. Where a citation below points at a defect this cycle
> **removed**, the pre-build number is the honest one and it stays. Where it points at something a
> reader still needs to find, here is the as-built line, re-derived against the tree at item 8 rather
> than carried forward from any earlier measurement:
>
> | surface | cited below as | as built |
> |---|---|---|
> | per-row status dot | §5.1 `MainWindow.xaml:388` | `:443-462` — `Ellipse` + `Style` / `DataTrigger` |
> | idle chip | §5.1 `:419` | `:485-502` |
> | row memory chip | §5.1 `:433` | `:510-527` |
> | compact-mode memory chip | §5.1 `:78` | `:72-88` |
> | status-bar live-process dot | not cited — F-088, added at 3a | `:1888-1900` |
> | expired row fill + border | §6.2 `:217-218` | `:226-233` |
> | expired-row 3px left rule | §6.2, new in item 4 | `:357-371` |
> | compat banner | §6.2, §6.3 `:1516-1522` | `:1608-1625`; the `▲` `Run` at `:1624` |
> | Bloxstrap banner | §7 `:1528-1532` | `:1629-1647`; literals at `:1630`, `:1631`, `:1644` |
> | `SecondaryStatusText` | §6.1 `MainWindow.xaml:391`, `AccountSummary.cs:677-747` | `:463`, `AccountSummary.cs:702-772` |
> | `AccountSummary.IdleText` | §6.3 `:288-297` | `:311-322`; the `IdleWarn` setter raises it at `:287` |
> | selection toggle | §6.4 `:438-442`, label `:443-459` | `:531-535`, label `:536-552`; template `App.xaml:53-94` |
> | MAIN pill | §6.4 `:374-383` | `:424-431` |
> | flatline `Theme` record | §4.1 `ThemeStore.cs:202-251` | `BuildBuiltIns()` at `:202-268`, flatline record `:254-267` |
> | description line | §4.5 `PreferencesWindow.xaml:408-412` | picker `:408-412`, line `:416-421`, copy in `Theming/ThemeDescriptions.cs:14-21` |
> | gate's inline-pair regexes | §5.4, §11.1 `ContrastPairGateTests.cs:56-64` | `:75-84` |
> | `ResolveTheme` | §8.4 `:115-145` | `:138-168` |
> | `BuiltInThemes()` guard | §8.5 `:163-165` | `:176-191`; the guard message at `:186-187` |
> | `NoExemptionOutlivesItsFinding` | §10.1 `:254-288` | `:281-314` |
> | the "9 distinct pairs" claims | §10.3 `:52`, `:181`, `:193` | corrected to 8 in the class doc and at `:207`, `:219` — four places, not the three catalogued |
>
> Citations into `ThemeService.cs`, `EdgeRemediation.cs`, `ContrastGuard.cs`, `Theme.cs`,
> `ThemeStore.cs:71-76` and `scripts/capture-ui.ps1` were re-checked at item 8 and are unmoved.

---

## 1. Stack

No new dependencies. Nothing in this cycle reaches outside what already ships.

| Layer | What this cycle touches |
|---|---|
| `ROROROblox.Core` — .NET 10, no WPF reference | `Theming/ThemeStore.cs` (one record added). `Theme.cs`, `ContrastGuard.cs`, `EdgeRemediation.cs` unchanged. |
| `ROROROblox.App` — WPF + [WPF-UI by lepoco](https://github.com/lepoco/wpfui) | `Converters.cs` (two classes deleted), `MainWindow.xaml` (status styles + redundancy), `Preferences/PreferencesWindow.xaml(.cs)` (theme description line), new `Theming/ThemeDescriptions.cs` |
| `ROROROblox.Tests` — xUnit | `ContrastPairGateTests.cs`, `MutedTextFenceTests.cs` (doc corrections), new `FlatlineLabGateTests.cs`, new `ThemedStatusColourTests.cs` |
| `scripts/capture-ui.ps1` | **No edit.** Enumerates themes at runtime. §9 proves it. |

The contrast arithmetic is WCAG 2.1 relative luminance, already implemented in
[`ContrastGuard.cs:120-132`](../src/ROROROblox.Core/Theming/ContrastGuard.cs#L120-L132). Nothing in
this cycle reimplements it — every new measurement calls into it.

## 2. Runtime, deployment, identity and signing

Unchanged from v1.16.0.0, and listed only so `/checklist` does not re-derive it.

- **Runtime:** Windows 11 desktop, WPF, single process. No new runtime requirement. A theme is ten
  hex strings; there is nothing to install.
- **Distribution:** Microsoft Store MSIX plus GitHub Releases via Velopack, both driven from
  `scripts/finalize-store-build.ps1` and `scripts/build-velopack-release.ps1`.
- **Identity:** `626LabsLLC.RoRoRoBlox`, Publisher `CN=177BCE59-0966-4975-9962-10E36652141F`,
  4-part version with revision 0. `Package.appxmanifest` `Identity Version` and the csproj
  `<Version>` move in lockstep; both go to `1.17.0.0`.
- **Capabilities:** `runFullTrust` only. This cycle adds none.
- **Store listing:** an accessibility theme is a listing asset. Copy work, not build work — carried
  in the PRD's "with more time" list, not scheduled here.

> **Corrected at item 8.** The version lockstep landed as stated: `ROROROblox.App.csproj`
> `<Version>1.17.0.0</Version>` and `Package.appxmanifest` `Identity Version="1.17.0.0"`, both
> re-read after editing. `runFullTrust` is still the only capability, and it is still the only entry
> in the `<Capabilities>` block. The listing copy was **not** deferred: item 8 wrote both the Partner
> Center "What's new" block and the clan-facing long form, leading with the theme rather than
> footnoting it, in [`docs/store/release-notes-1.17.0.0.md`](store/release-notes-1.17.0.0.md).
> A reviewer letter and a release runbook for this version are not written; v1.16 has both and this
> version has neither.

> **Profile drift, surfaced not fixed.** `plugins.vibe-cartographer.deployment_target` in the
> unified profile currently reads `vibe-plugins-marketplace`, set by a later cycle in a different
> repo. For RORORO the target is `microsoft-store-msix-velopack`, which is what
> [`docs/builder-profile.md`](builder-profile.md) and `CLAUDE.md` both state and what this section
> uses. *(default — confirm on next interactive run)*

## 3. Architecture: where colour comes from, and where it escapes

```text
 Theme record (10 hex slots, Core)
        |
        v
 ThemeService.ApplyTo(resources, theme, edgeAnswer)      App/Theming/ThemeService.cs:226
        |  writes 10 SolidColorBrush instances into Application.Current.Resources
        |  derives an 11th, InteractiveEdgeBrush, via EdgeRemediation -> ContrastGuard
        v
 Application.Current.Resources
        |
        +--> {DynamicResource XBrush} in XAML ........... repaints live on theme change   [governed]
        |
        +--> Style + DataTrigger setting {DynamicResource} repaints live on theme change   [governed]
        |
        X--> IValueConverter returning a hardcoded brush   NEVER repaints, never themed  [ESCAPED]
        X--> literal #RRGGBB in XAML or C#                 never themed                  [ESCAPED]
```

Two escape routes exist today. The first is this cycle's core work (§5). The second is a small,
named set that stays out of scope (§7).

The load-bearing property of the governed path: `ApplySlot` **replaces** the brush instance rather
than mutating it ([`ThemeService.cs:262-269`](../src/ROROROblox.App/Theming/ThemeService.cs#L262-L269)),
because `DynamicResource` subscribers re-bind on dictionary change and ignore mutation of a held
brush. Any fix in §5 has to stay on that path or it will not repaint live, which PRD Story 1.1
requires.

## 4. Flatline as a fourth built-in

*Implements `prd.md > Epic 1`.*

### 4.1 The palette

Appended to `ThemeStore.BuildBuiltIns()`
([`ThemeStore.cs:202-251`](../src/ROROROblox.Core/Theming/ThemeStore.cs#L202-L251)) as a fourth
hardcoded `Theme` record, `IsBuiltIn: true`, in the same shape as the other three. No filesystem
dependency, no loader change, no `Theme` contract change.

```csharp
// Flatline — carries no meaning in colour. One page, one row surface, one text colour, one
// light accent and one dark accent. Achromatic throughout: nothing here encodes a hue, so
// nothing here is lost to colour vision deficiency, a bad panel, or direct sun.
new Theme(
    Id:               "flatline",
    Name:             "Flatline",
    Bg:               "#101010",
    Cyan:             "#D4D4D4",
    Magenta:          "#6E6E6E",
    White:            "#F5F5F5",
    MutedText:        "#989898",
    Divider:          "#333333",
    RowBg:            "#2A2A2A",
    RowExpiredBg:     "#3D3D3D",
    RowExpiredAccent: "#D4D4D4",
    Navy:             "#101010",
    IsBuiltIn:        true),
```

The id stays `flatline` whatever the display name becomes: `capture-ui.ps1` matches themes on the
`Id = <id>,` substring ([`capture-ui.ps1:652-655`](../scripts/capture-ui.ps1#L652-L655)), so the id
is tooling contract and the name is not.

**Display name: keep "Flatline."** The PRD recommended it and routed the call here. Este coined it,
it describes flattening colour rather than flattening a patient, and §4.2's sentence is what
actually carries the accessibility promise to a clan member. Changing it later costs one string.

### 4.2 Two design rules that decide every value above

**Rule 1 — flatline removes hue, never a distinction.** This is the direct consequence of the
product-theme ruling. Scope's loose note suggested `Bg`, `Navy` and `RowBg` collapse toward one
value; that would delete the row-versus-page distinction, which is *precisely* the defect F-002
already records (`RowBgBrush` vs `BgBrush` = 1.09:1 brand, 1.00:1 under the old flatline, cards
vanish entirely). Colour vision deficiency affects hue discrimination, not luminance
discrimination. An achromatic **ramp** is therefore the correct expression of "no meaning in
colour", and a single flat value is not. Every surface separation below is carried in lightness:

| separation | flatline | brand, for reference |
|---|---|---|
| RowBg vs Bg | **1.33:1** | 1.09:1 |
| RowExpiredBg vs RowBg | **1.32:1** | — |

Flatline separates rows from the page better than the shipped default does. That is the theme
arguing for itself.

**Rule 2 — two accent lightnesses, because the app pins foregrounds at author time.** The app
declares which foreground sits on which accent in markup: `NavyBrush` on `CyanBrush` at 22 sites,
`WhiteBrush` on `MagentaBrush` at 8. A dark text token and a light text token cannot both clear
4.5:1 against one accent value in any comfortable palette.

Verified by enumerating the full achromatic grid rather than argued: exactly **26** dark-page
single-accent solutions exist, every one requires a page at `#040404` or darker, and in every one
the best achievable RowBg-vs-Bg separation is **≤ 1.024:1**. A single accent value forces flatline
to reproduce F-002's own defect. So `Cyan` and `RowExpiredAccent` are light (they take dark text),
`Magenta` is dark (it takes light text). Both are achromatic. "One accent" survives as one accent
*treatment*; there is still no hue anywhere in the theme.

### 4.3 Measured result

Every pair the gate scans, resolved through `ThemeService.ApplyTo`, measured with
`ContrastGuard.RatioBetween`:

| foreground on fill | sites | flatline | AA |
|---|---|---|---|
| `NavyBrush` on `CyanBrush` | 22 | **12.84:1** | pass |
| `WhiteBrush` on `MagentaBrush` | 8 | **4.68:1** | **pass — no exemption needed** |
| `WhiteBrush` on `NavyBrush` | 5 | 17.45:1 | pass |
| `NavyBrush` on `RowExpiredAccentBrush` | 3 | 12.84:1 | pass |
| `WhiteBrush` on `RowBgBrush` | 2 | 13.17:1 | pass |
| `CyanBrush` on `RowBgBrush` | 2 | 9.68:1 | pass |
| `CyanBrush` on `NavyBrush` | 1 | 12.84:1 | pass |
| `WhiteBrush` on `BgBrush` | 1 | 17.45:1 | pass |

**Zero new exemptions, and the existing one is not load-bearing for this theme.** White on the
single dark accent measures 4.68:1, above AA outright — the F-050 exemption's 3.20 floor is
irrelevant to flatline, which is what PRD Story 1.4 asked for. For comparison the shipped themes
measure 3.79:1 (brand), 4.16:1 (midnight), 3.29:1 (magenta-heat) on that same pair.

Pairs the gate structurally cannot see, measured anyway because they are real bindings:

| pair | flatline | why it is not gated |
|---|---|---|
| `MutedTextBrush` on `BgBrush` | 6.60:1 | no element declares both inline |
| `MutedTextBrush` on `RowBgBrush` | 4.98:1 | fill is on the row `Border`, text on a child |
| `RowExpiredAccentBrush` on `RowExpiredBgBrush` | 7.33:1 | banner splits fill and text across two elements |
| `MutedTextBrush` vs `WhiteBrush` | **2.65:1** | text-vs-text, not text-on-fill |
| `DividerBrush` vs `NavyBrush` | 1.51:1 | hairline, deliberately below 3:1 — see below |
| derived `InteractiveEdgeBrush` vs `NavyBrush` | **3.03:1** | derived, not declared |

**MutedText carries "secondary" on ratio alone, and the bar is the shipped default.** PRD Story 1.4
allowed either resolution: keep the ratio, or move the job to weight and indent. Flatline separates
muted from body text at 2.65:1 while staying at 4.98:1 on the row surface it sits on — better
separation than brand's shipped 2.42:1 and magenta-heat's 2.46:1. No weight change needed, and
`MutedTextFenceTests`' role fence is untouched. F-032's 1.00:1 belongs to `flatline-lab` after this
cycle (§8).

**Divider stays a hairline on purpose.** At 1.51:1 it is below WCAG 1.4.11's 3:1, which means
`EdgeRemediation.Decide` returns `DeriveSilently` and `ContrastGuard.Ensure` derives
`InteractiveEdgeBrush = #606060` at 3.03:1. Verified by running it. That is deliberate: flatline
takes the exact same derivation path as brand, midnight and magenta-heat, so it exercises shipped
machinery rather than sidestepping it. A divider authored at 3:1 would be a loud rule rather than a
separator, which is the mistake [`Theme.cs:47-55`](../src/ROROROblox.Core/Theming/Theme.cs#L47-L55)
already warns against.

### 4.4 Selection raises nothing

*Implements `prd.md > Story 1.2`.*

`EdgeRemediation.Decide(isBuiltIn: true, ...)` returns `DeriveSilently` before any prompt branch is
reached ([`EdgeRemediation.cs:46`](../src/ROROROblox.Core/Theming/EdgeRemediation.cs#L46)), and
`ThemeService.SetActiveAsync` skips the answer read entirely for a built-in
([`ThemeService.cs:100-110`](../src/ROROROblox.App/Theming/ThemeService.cs#L100-L110)). Confirmed by
running `Decide` against the real flatline record: `DeriveSilently`. No code change. Story 1.2 stays
a verification item, not a build item — and it verifies **on screen**, not on paper, because "the
code says it cannot happen" is how it happens.

### 4.5 The description sentence

*Implements `prd.md > Story 1.3`. This was one of the two forks the PRD routed here.*

**Decision: an App-layer lookup keyed by theme id. The `Theme` contract does not grow.**

Rejected, and why:

- **An eleventh `Theme` slot.** [`ContrastGuard.cs:15-23`](../src/ROROROblox.Core/Theming/ContrastGuard.cs#L15-L23)
  already argues this case in the codebase's own voice: ten required slots, every user theme on
  disk supplies all ten, an eleventh breaks them all unless it defaults. A description is
  presentation copy for four themes the project ships. Paying a contract change for it is the
  expensive answer the PRD warned about.
- **A tooltip on the picker item.** PRD requires the sentence on *choosing or focusing*. Hover is
  neither. It also risks the `Id = <id>,` substring `capture-ui.ps1` reads out of the item's UIA
  name, and §9 depends on that string.

New file, `src/ROROROblox.App/Theming/ThemeDescriptions.cs`:

```csharp
/// One sentence per built-in, keyed by theme id. Deliberately NOT a Theme slot — the theme
/// contract stays at ten required fields so every user JSON on disk stays valid without its
/// author touching it (invariant 6). User themes return null and the line collapses.
internal static class ThemeDescriptions
{
    public static string? For(string id) => id.ToLowerInvariant() switch { ... };
}
```

Rendered as a `TextBlock` directly below the picker in `PageAppearance`
([`PreferencesWindow.xaml:408-412`](../src/ROROROblox.App/Preferences/PreferencesWindow.xaml#L408-L412)),
`MutedTextBrush`, 11px, wrapping, collapsed when `For(id)` is null. `OnThemeChanged` sets it. All
four built-ins get a sentence — three empty lines out of four would read as a bug.

Flatline's sentence, subject to copy polish at `/build`:

> Reads the same whether or not you can tell colours apart. Anything the app says in colour, it
> also says in words or shape.

Second person, no "WCAG", no "contrast ratio", no "CVD". Clan-facing register per `CLAUDE.md`.

## 5. Colour the theme cannot reach

*Implements `prd.md > Epic 3, Story 3.1`. The largest single risk in the cycle, and it sequences
ahead of §6 because a status dot still glowing brand green makes every redundancy screenshot
useless as evidence.*

### 5.1 The defect

Two converters hold RGB literals in C# and never read the active theme:

- `StatusDotBrushConverter` — green `#4FE08C`, yellow `#F1B232`, magenta `#F22F89`, grey `#4A5C70`
  ([`Converters.cs:169-198`](../src/ROROROblox.App/Converters.cs#L169-L198)), bound at
  [`MainWindow.xaml:388`](../src/ROROROblox.App/MainWindow.xaml#L388).
- `IdleChipBrushConverter` — amber `#F1B232`, muted `#8A93A0`
  ([`Converters.cs:205-218`](../src/ROROROblox.App/Converters.cs#L205-L218)), bound at
  [`:78`](../src/ROROROblox.App/MainWindow.xaml#L78),
  [`:419`](../src/ROROROblox.App/MainWindow.xaml#L419) and
  [`:433`](../src/ROROROblox.App/MainWindow.xaml#L433).

`ThemeService.ApplyTo` cannot reach either. Under flatline they paint brand green, amber and
magenta onto an achromatic field.

> **Corrected at `/checklist`, pre-build.** This section originally listed two `IdleChipBrushConverter`
> sites. A grep of the tree returns three: [`:78`](../src/ROROROblox.App/MainWindow.xaml#L78) is the
> **compact-mode** row's memory chip, bound to `MemoryWarning` exactly as [`:433`](../src/ROROROblox.App/MainWindow.xaml#L433)
> is in the standard row. Missing it would leave compact mode painting brand amber on an achromatic
> field — the half-painted defect Story 3.1 exists to kill, in the one row template the capture round
> is least likely to have open. Four binding sites total, not three.

**Collateral the deletion drags with it**, also found at `/checklist` and also not in the original
list. All four must land in the same commit or the build breaks:

| file | what | why it is load-bearing |
|---|---|---|
| [`App.xaml:23-24`](../src/ROROROblox.App/App.xaml#L23-L24) | both converters declared as `StaticResource` keys | a XAML resource entry naming a deleted type is a build failure, not a warning |
| [`ConvertersTests.cs`](../src/ROROROblox.Tests/ConvertersTests.cs) | the whole file — 2 facts, both asserting `IdleChipBrushConverter`'s literal RGB | deleted with the converter; the assertions it makes are the behaviour being removed |
| [`AccountSummary.cs:268`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L268) | `<see cref="IdleChipBrushConverter"/>` in the `IdleWarn` doc comment | a `cref` to a deleted type; rewrite to name the `DataTrigger` that replaces it |
| [`MainWindow.xaml:425-426`](../src/ROROROblox.App/MainWindow.xaml#L425-L426) | comment says "Amber via the same IdleChipBrushConverter" | goes false on merge, same defect class as §10.2 |

### 5.2 Decision: delete both converters, use Style + DataTrigger

**Not** "make the converter resolve from `Application.Current.Resources`." That fails the live
repaint requirement: `IValueConverter.Convert` re-runs when the *binding source* changes, not when
the resource dictionary changes, and `ApplySlot` replaces the brush instance rather than mutating
it — so a converter-fetched brush is a stale instance the moment the theme changes. PRD Story 1.1
requires the main window to repaint immediately.

`{DynamicResource}` inside a `DataTrigger` setter re-resolves on dictionary change and repaints
live. It is also the idiom this exact file already uses twice, at
[`MainWindow.xaml:213-231`](../src/ROROROblox.App/MainWindow.xaml#L213-L231) (row background) and
[`:394-407`](../src/ROROROblox.App/MainWindow.xaml#L394-L407) (secondary status text). The fix is
consistency, not invention.

### 5.3 Status-to-slot mapping

Every status colour resolves from a slot that already exists. No new slot, no contract growth.

> **Corrected at item 8, post-build. This section counted four status-colour sites. The app had
> five.** Item 6's register pass found the status bar's live-process dot at `MainWindow.xaml`
> — a literal `<SolidColorBrush Color="#4FE08C" />` inside a `Setter.Value`, swapping to `#4A5C70`
> at zero clients, the same two values the deleted `StatusDotBrushConverter` held. It shipped with
> F-080 in PR #96 and is present on `main`, so it is not a regression from this cycle; it is a site
> this section did not know about. Opened as **F-088**, fixed at item 3a, and now at `:1888-1900`
> using the mapping below — live takes `WhiteBrush`, zero clients takes `MutedTextBrush`.
>
> **The arrangement there is inverted from the per-row dot, on purpose.** Here the quiet state is the
> plain `Setter` and the loud states are triggers, because `StatusDot` is a four-string enum whose
> quiet value doubles as the fallback for a string nobody planned for. There the bound value is an
> `int` and `DataTrigger` matches on equality, so "zero" is expressible as a trigger and "any number
> of clients" is not. Putting live in the `Setter` is what keeps it on `{DynamicResource}` without a
> converter or a new view-model bool — the machinery §5.2 deleted. The cost, named rather than
> buried: a binding that fails outright leaves that dot at the live colour. It sits beside
> `LiveProcessSummary`, which states the count in words, so it is a redundant echo either way (§6.1).

| `AccountSummary.StatusDot` | meaning | slot | why |
|---|---|---|---|
| `green` | active — in game, in Studio, or pid alive | **`WhiteBrush`** | see below |
| `yellow` | session expired | `RowExpiredAccentBrush` | exact semantic match, already the expired token |
| `magenta` | limited by Roblox | `MagentaBrush` | exact match, already the attention token |
| `grey` | idle | `MutedTextBrush` | exact match, already the quiet token |

Idle and memory chips: `RowExpiredAccentBrush` when warning, `MutedTextBrush` otherwise. This
preserves the existing intent — the comment at
[`:425-426`](../src/ROROROblox.App/MainWindow.xaml#L425-L426) says both chips share the amber so
they read as one visual system, and `RowExpiredAccentBrush` *is* that amber in every shipped theme.

**Active maps to `WhiteBrush`, not `CyanBrush`.** Cyan is the tempting choice — it is the app's
"live/primary" accent. It was rejected on measurement: under flatline `CyanBrush` and
`RowExpiredAccentBrush` are the same value, so active and expired dots would land at **1.00:1** of
each other. `WhiteBrush` keeps all four states at distinct values in every theme:

| under flatline | value | vs the row it sits on |
|---|---|---|
| active | `#F5F5F5` | 13.17:1 |
| expired | `#D4D4D4` | 9.68:1 |
| idle | `#989898` | 4.98:1 |
| limited | `#6E6E6E` | 2.81:1 |

> **Corrected after the build, by measurement.** The rendered gate
> (`TriggeredStatusColourGateTests`) reproduces every value in the table above exactly, and found two
> things wrong with the prose around it.
>
> **The expired row is 9.68:1 against a surface it never shows.** That figure is `#D4D4D4` against
> `RowBgBrush`, and the column header says "vs the row it sits on". But `StatusDot` returns `yellow`
> if and only if `SessionExpired` ([`AccountSummary.cs:687-688`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L687-L688)),
> and `SessionExpired` is exactly the condition under which the row's own trigger repaints it to
> `RowExpiredBgBrush` ([`MainWindow.xaml:229-231`](../src/ROROROblox.App/MainWindow.xaml#L229-L231)).
> The expired dot is never seen against `RowBg`. Measured against the surface that state actually
> renders, it is **7.33:1** under flatline — 7.13 brand, 6.58 midnight, 7.13 magenta-heat. Lower than
> published, still comfortable, and the argument the row is making is unaffected. The number was
> arithmetically correct and described a state that cannot occur, which is the same defect shape as
> the three register rows this cycle existed to reconcile. The gate now prints both figures on every
> run rather than quietly replacing one with the other.
>
> **Flatline is not the hard case for dot separation, and nobody had checked.** The closest pair of
> dot values is **1.19:1 in midnight** and **1.29:1 in brand**, against flatline's 1.36:1. The
> achromatic theme separates its four states better than either chromatic theme, because it was
> designed against this constraint and they were not. §5.3 reasons about flatline throughout as
> though it were the worst case; it is the best one.

**Cost, named rather than buried:** under brand the active dot shifts from green `#4FE08C` to white
`#FFFFFF`. That is a visible change to the default theme, not just to flatline, and it is the
correct trade — a status colour the theme cannot reach is the actual bug. It wants eyes on the brand
capture at a checkpoint, not just the flatline one.

**Honest limit on 1.4.11.** The four dot values separate by only 1.36:1, 1.95:1 and 1.77:1 from each
other under flatline, and the `limited` dot sits at 2.81:1 against its row — below the 3:1
a graphical object would need if it carried information alone. It does not. `SecondaryStatusText`
states all four states in words beside it (§6.1), so the dot is a redundant echo rather than a
required graphical object. That is the claim, stated plainly so a reviewer can disagree with it.

### 5.4 Is the new path measured?

**No, and this spec does not claim otherwise.** `ContrastPairGateTests` scans for elements declaring
both `Background` and `Foreground` inline
([`ContrastPairGateTests.cs:56-64`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L56-L64)). A
`Style`/`DataTrigger` setter is not an inline attribute, so the gate cannot see the status dot
before this change and cannot see it after. Extending the gate to resolved-style setters is Phase 2
gate work with its own design, and the PRD already parks it.

What ships instead is a narrow regrowth fence, `ThemedStatusColourTests.cs`:

1. `StatusDotBrushConverter` and `IdleChipBrushConverter` no longer exist as types in the App
   assembly. Deletion is the assertion; it cannot be half-done.
2. No `Color.FromRgb` / literal `#RRGGBB` `SolidColorBrush` is constructed in App code outside a
   named allow-list. The allow-list is exactly the §7 out-of-scope set — `Converters.cs`'s caption
   `AutoPalette`, `RobloxWindowDecorator`'s per-account palette — each entry carrying the reason it
   is allowed. A new literal fails the build with the finding attached.

This is a fence, not a gate: it proves the colours come from the theme, not that they are legible.
A green run does not mean "the status dot's contrast is verified."

> **Corrected at item 8, post-build. The fence ships three facts, not two, and the allow-list is
> larger and stricter than this section describes.**
>
> - **Fact 3, added at item 3a**, scans App **XAML** for literal `#RRGGBB` outside its own allow-list.
>   Facts 1 and 2 walk `*.cs` only, which is why a hex in a `Setter.Value` (F-088) sat in front of
>   both of them unseen. The XAML allow-list carries the rule the C# one does not state: **a literal
>   is permitted only when an OPEN register row already owns it**, and every entry cites the id
>   inline. F-085, F-066, F-079 and F-063 each earn a region that way. `App.xaml`'s brush dictionary
>   is the one entry with no finding, because those instances are the governed path's *origin* rather
>   than an escape from it — `ApplySlot` replaces every one of them on every theme change.
> - **The clause carries a ceiling, not only a vacuity floor.** An allow-listed region is a region a
>   row has already counted, so a literal added inside one would be a new defect wearing an old row's
>   id, and the offender list would never see it. Measured against the tree: **97** allowed
>   occurrences, down from 101 when item 3b retired F-089's entry. F-032 went from 11 offending
>   controls at audit to 15 while two waves built machinery around it and nobody re-counted; that
>   lesson is arithmetic here instead of prose.
> - **The C# allow-list is `Converters.cs` whole-file plus `RobloxWindowDecorator`**, and the F-087
>   entry for `ConsentSheet.xaml.cs` this section did not anticipate. §7's list was incomplete; see
>   the correction there.
>
> **The fence's own blind spot, stated plainly:** fact 3 fires on a hex inside an XML comment, which
> it proved by failing on item 3a's first draft of the comment above the status-bar dot. That was
> kept deliberately. A comment naming a shipped colour is a claim that goes false the moment the
> theme changes that value, which is the defect class item 6 spent an item removing.

> **Corrected 2026-08-10, post-v1.17. The answer is now yes, and this section's "no" is why the gate
> was built.** `TriggeredStatusColourGateTests` — stage 3 of the rendered-contrast gate — parses the
> shipped `MainWindow.xaml` with `XDocument`, lifts the real `Ellipse` and the three chip
> `TextBlock` subtrees out of it, reconstitutes each through `XamlReader.Parse`, hands it a real
> `AccountSummary` and renders it. **40 cases** — 4 dot states x 4 built-in themes, plus 3 chips x 2
> states x 4 themes — and every one samples the exact resolved slot §5.3's table maps it to, to the
> byte. It measures the shipped markup rather than a copy of it, which is the whole point: a
> hand-written equivalent would pass forever while the real file rotted.
>
> It also asserts the four dot states stay **mutually distinct in every theme**, which turns the
> `WhiteBrush`-over-`CyanBrush` decision below from an argument into a gate. The fence is untouched
> and stays — this is a gate beside it, not instead of it.
>
> **Two of this section's published numbers are now reproduced by a test** rather than asserted: the
> flatline ratios 13.17 / 9.68 / 4.98 / 2.81, and the ladder 1.36 / 1.95 / 1.77. One of them needs a
> caveat the table does not carry. **9.68:1 is computed against `RowBgBrush`, and the `yellow` dot
> appears exactly when `SessionExpired` is true — which is exactly when the row's own `DataTrigger`
> ([`MainWindow.xaml:229`](../src/ROROROblox.App/MainWindow.xaml#L229)) repaints the row to
> `RowExpiredBgBrush`.** Against the surface that state actually shows, the expired dot measures
> **7.33:1**. Lower than the published figure, still clear of everything, and now printed beside it
> on every run.
>
> **And flatline is not the hard case for dot separation.** §5.3 reasons about it as though it were.
> Measured across all four built-ins, flatline's closest pair is green/yellow at 1.36:1 while
> `midnight` puts magenta/grey at **1.19:1** and `brand` puts yellow/grey at **1.29:1**. The
> achromatic theme separates its dots better than the two chromatic ones, because it was designed to
> and they were not.

## 6. Non-colour redundancy

*Implements `prd.md > Epic 2`. Ships for every theme. No `if (theme == flatline)` anywhere — that
is numbered non-goal 6, and it is the constraint most likely to be violated under build pressure.*

### 6.1 Account state — no work, and that is the finding

*`prd.md > Story 2.1`.*

`SecondaryStatusText` already states all four states in words, directly beside the dot at
[`MainWindow.xaml:391`](../src/ROROROblox.App/MainWindow.xaml#L391). Read out of
[`AccountSummary.cs:677-747`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L677-L747):

| state | dot | words already rendered |
|---|---|---|
| expired | yellow | `"Session expired"` |
| limited | magenta | `"Limited by Roblox — re-capture or wait"` |
| active | green | `"In {game} · {age}"` / `"In a game"` / `"In Studio"` / `"Connecting…"` / `"At Roblox home"` |
| idle | grey | `"Ready"` / `"Closed {ago}"` / `"Last launched {ago}"` |

Story 2.1's acceptance test — cover the dot, can you still tell the states apart — passes today in
every theme. **No device is added.** PRD Story 2.1 says it directly: where the words already carry
the state, say so and change nothing; ornament for its own sake is a regression in a theme whose
whole argument is legibility. The work here is §5 (the dot stops being off-theme) plus a capture
that proves the words are legible under flatline.

### 6.2 Expired sessions — a left rule on the row

*`prd.md > Story 2.2`.*

Amber is the entire signal across four sites. Three already have a non-colour carrier once you look:

| site | today | after |
|---|---|---|
| [`:399`](../src/ROROROblox.App/MainWindow.xaml#L399) status foreground | amber + `FontWeight="SemiBold"` | unchanged — weight plus the word "Session expired" already carry it |
| [`:217-218`](../src/ROROROblox.App/MainWindow.xaml#L217-L218) row background + border | amber fill and border, colour only | **add a 3px left rule** bound to `RowExpiredAccentBrush` |
| [`:1516-1522`](../src/ROROROblox.App/MainWindow.xaml#L1516-L1522) compat banner | amber fill, border, text | **prefix `▲`** — see §6.3 |

The left rule is the device because the app already uses it: Preferences' nav rail carries selection
on a 3px bar plus weight rather than a fill, precisely because that survives a theme that flattens
fills (F-002's shipped fix). Reusing it keeps one vocabulary instead of inventing a second.

Acceptance is tested by flattening `RowExpiredBg` to `RowBg` at test time and confirming the row is
still identifiable. In shipped flatline the two are **1.32:1** apart, so the row carries *both* a
lightness step and the rule. The rule is what makes it survive if the step ever goes away.

> **As built, item 4.** Line citations in the table above are pre-build; the as-built map is in the
> banner at the top of this file. Two implementation facts this section did not anticipate, both
> load-bearing:
>
> - **The rule lives in a reserved 14px grid column**, not in the row's padding. The row's left
>   padding moved into the grid's first column so the rule sits flush against the row's own edge
>   rather than floating inside the padding, and so a session going stale does not shift its own row.
>   Content geometry is unchanged: 3px of rule plus 11px of gap is the same 14px inset it always had.
> - **The `Hidden` default is a `Style` setter, not a local attribute.** A local `Visibility="Hidden"`
>   on the element would have outranked the `DataTrigger` and the rule would never have appeared —
>   the kind of miss that reviews clean and screenshots do not.
>
> **Measured, not assumed.** Through `ThemeService.ApplyTo` with `RowExpiredBg` flattened into
> `RowBg`, the rule clears WCAG 1.4.11 in every built-in: brand **8.14:1**, midnight **6.72:1**,
> magenta-heat **9.13:1**, flatline **9.68:1**.

### 6.3 Warning chips — extend the `▲` that already ships

*`prd.md > Story 2.3`.*

The memory chip already prefixes `▲` when a cap or projection trigger latches
([`:422-426`](../src/ROROROblox.App/MainWindow.xaml#L422-L426)), produced by
`MemoryChipFormatter.Format` into `MemoryText`. The app solved this once. Extend the same device:

- `AccountSummary.IdleText` ([`:288-297`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L288-L297))
  prefixes `"▲ "` when `IdleWarn` is true. One line.
- **Wiring detail for `/build`:** `IdleText` is recomputed on `SinceActivity` change
  ([`:260`](../src/ROROROblox.App/ViewModels/AccountSummary.cs#L260)) but not on `IdleWarn` change.
  The `IdleWarn` setter must also raise `OnPropertyChanged(nameof(IdleText))` or the glyph appears
  a tick late. This is the kind of miss that ships looking fine on a slow-moving row.
- The compat banner takes the same prefix, for one warning vocabulary across the window.

`▲` is a Segoe UI geometric glyph, not emoji — `Emoji_Presentation=No`. It does not trip the
register's invariant-5 emoji rule, which names `🎲` and `📋` as the two shipped exceptions.

> **As built, item 4.** Line citations above are pre-build; the as-built map is in the banner at the
> top of this file. `IdleText` is now `AccountSummary.cs:311-322` and the `IdleWarn` setter raises it
> at `:287` — the wiring detail this section flagged was real and was wired. The compat banner's
> prefix ships as a **literal `Run`** (`MainWindow.xaml:1624`) rather than a string baked into
> `RobloxCompatBanner`, for two reasons this section did not name: the banner string is the compat
> checker's own output and a presentation glyph does not belong in a drift-detection result, and the
> `Border` collapses on that same binding, so the glyph can never render alone.

### 6.4 Verified clean, no work

*`prd.md > Story 2.4`. "We looked and it was fine" is a finding. Recorded so `/build` does not churn
these and a later reviewer does not re-discover them.*

| surface | carrier | verdict |
|---|---|---|
| selection toggle, [`:438-442`](../src/ROROROblox.App/MainWindow.xaml#L438-L442) | shape — filled vs hollow, via `SelectionDotStyle`; the label beside it also reads "In batches" / "Skipped" at [`:443-459`](../src/ROROROblox.App/MainWindow.xaml#L443-L459) | clean, no work |
| MAIN pill, [`:374-383`](../src/ROROROblox.App/MainWindow.xaml#L374-L383) | the word MAIN. Magenta is emphasis on top of text, not the message | clean, no work |
| `InteractiveEdgeBrush` | derived to clear 3:1 under any theme; measured 3.03:1 under flatline | clean, no work |
| `SecondaryStatusText` | words, all four states — §6.1 | clean, no work |

## 7. Out of the theme's reach, and out of scope

*Implements `prd.md > Story 3.2`. Named with reasons, because silently skipped is how they come
back as bugs.*

- **Tray icons** — `tray-on` / `tray-off` / `tray-warn` / `tray-error` in
  [`src/ROROROblox.App/Tray/Resources/`](../src/ROROROblox.App/Tray/Resources/). Four distinct
  static assets, not one file recoloured. That is redundancy of the right kind: the shapes differ,
  so the state survives with no colour at all. Windows owns the tray surface and the theme does not
  reach it. **Out of scope, and correct as-is.**
- **`RobloxWindowDecorator`** — per-account title-bar colours from an 8-entry palette plus a magenta
  main-account colour
  ([`RobloxWindowDecorator.cs:36-51`](../src/ROROROblox.App/Tray/RobloxWindowDecorator.cs#L36-L51)).
  Per-account *identity* paint on a Win32 surface the theme does not own, and the window title
  already carries the account name, so identity is not colour-only there. **Out of scope.**
- **`Converters.cs` caption `AutoPalette`** — the same per-account identity paint, chosen by id
  hash. Same reasoning. **Out of scope**, and on §5.4's allow-list.
- **`AboutWindow`'s fixed logo hexes** — invariant 2 says the 626 Labs duo is never split and the
  logo's own brand hex stays fixed. F-063 covers the rest of that window. **Out of scope.**
- **The Bloxstrap banner's literal `#3F3000` / `#8F7000`**
  ([`:1528-1532`](../src/ROROROblox.App/MainWindow.xaml#L1528-L1532)) — a genuine un-themed literal
  that will read as a warm brown block under flatline. **Found during `/spec` recon, not in the PRD.
  Out of scope for this cycle**, because it is a colour-only banner whose fix is the F-068 shared
  button/banner style work, not a theme change. Flagged for a register row rather than absorbed
  silently.

> **Corrected at item 8, post-build. This list named five surfaces and was incomplete, and one of
> its counts was low.** All three corrections came from item 3a's XAML fence, which attributed every
> colour literal in App markup and had nowhere to put the ones no row owned. Each got a row rather
> than an untraceable exemption:
>
> - **The Bloxstrap banner holds three literals, not two.** `Background="#3F3000"` and
>   `BorderBrush="#8F7000"` as stated, plus a body `Foreground="#FFE3A6"` this section missed
>   (`MainWindow.xaml:1630`, `:1631`, `:1644`). Opened as **F-085**, still `open`, still out of
>   scope, still belonging with F-068.
> - **`ConsentSheet.xaml.cs:90-92`** constructs brand cyan and magenta in C# as `TryFindResource`
>   fallbacks — `CapabilityRow.NamespaceBrush` returns
>   `(Brush)(Application.Current.TryFindResource("CyanBrush") ?? new SolidColorBrush(Color.FromRgb(0x17, 0xD4, 0xFA)))`
>   host-enforced, and the `MagentaBrush` / `0xF2, 0x2F, 0x89` equivalent otherwise. Fallback-only
>   reach, so the practical blast radius is small, but the fence needed its allow-list entry to be
>   traceable to a finding. Opened as **F-087**, `open`, out of scope.
> - **`SelectionDotStyle` (`App.xaml:53-94`)** held four un-themed hexes on a control that ships on
>   every account row. Opened as **F-089** and **fixed at item 3b** rather than left out of scope —
>   see the banner at the top of this file.
>
> **A fourth, out of this section's reach and out of the register's:** the per-account identity
> palette in the first two bullets above exists in **three** hand-synced copies, in three different
> encodings — `Converters.cs:90-100` as `Color.FromRgb` triples,
> `Tray/RobloxWindowDecorator.cs:37-47` as packed `uint` ARGB, and
> `Theming/CaptionColorPickerWindow.xaml.cs:20-32` as `#RRGGBB` strings, the last of which also
> carries `#E13AA0` for `RobloxWindowDecorator`'s `MainCaptionColor`. Verified against the tree at
> item 8. **Neither of the two sync comments names all three**: `Converters.cs:90` points only at
> `RobloxWindowDecorator`, `CaptionColorPickerWindow` points only at `RobloxWindowDecorator`, and
> `RobloxWindowDecorator` points at neither. This is out-of-scope identity paint either way, so it
> gets no register row, but it is a real trap: a ninth colour added to one copy leaves the picker and
> the row badge disagreeing with the title bar, silently.

## 8. flatline-lab — proving the gate can fail

*Implements `prd.md > Epic 4`. Independent of §5 and §6; can land in parallel.*

### 8.1 Why it exists

A gate that has only ever seen passing themes is unproven. Shipping flatline as a product theme
strands the evidence behind F-031, F-032 and F-050, all of which argue from a theme that never
existed in git. `flatline-lab` is the answer to both at once: it preserves those numbers *and* earns
its place by being the input that makes the gate go red on demand.

The original flatline JSON is **unrecoverable** — `%LOCALAPPDATA%\ROROROblox\themes\` is empty and
`docs/ui-evidence/` holds no flatline round. So the fixture is reconstructed from the ratios the
register itself records, which is a stronger provenance than a rediscovered file would have been.

### 8.2 The fixture

Not in `BuildBuiltIns()`. Not in the picker. Never written to the user themes folder. Not
selectable by any path a user can reach. It is a `static readonly Theme` in the test project only.

```csharp
// Not shipped, not selectable. Reconstructed from the ratios F-031, F-032, F-050 and F-002
// record, per docs/ui-capture-checklist.md:21-23 — one background, one text colour, one accent.
private static readonly Theme FlatlineLab = new(
    Id: "flatline-lab", Name: "Flatline Lab",
    Bg: "#0D0D0D", Cyan: "#777777", Magenta: "#777777", White: "#D3D3D3",
    MutedText: "#D3D3D3", Divider: "#0D0D0D", RowBg: "#0D0D0D",
    RowExpiredBg: "#0D0D0D", RowExpiredAccent: "#777777", Navy: "#0D0D0D",
    IsBuiltIn: false);
```

`IsBuiltIn: false` because it is not built in. It changes nothing measurable — `EdgeRemediation`
returns `AskFirst` rather than `DeriveSilently`, and `Resolve` derives the same edge for both.

### 8.3 It reproduces the register's numbers

Measured through `ThemeService.ApplyTo`, the same path as everything else here:

| register row | records | fixture measures |
|---|---|---|
| F-031 | `DividerBrush` vs `NavyBrush` = 1.00:1 | **1.00:1** |
| F-031 | `CyanBrush` vs `NavyBrush` = 4.34:1 | **4.34:1** |
| F-032 | `MutedTextBrush` vs `WhiteBrush` = 1.00:1 | **1.00:1** |
| F-050 | `WhiteBrush` on `MagentaBrush` = 2.99:1 | **2.99:1** |
| F-002 | `RowBgBrush` vs `BgBrush` = 1.00:1 | **1.00:1** |

Worth recording: the two independent register ratios multiply out correctly. 4.34 × 2.99 = 12.98,
and the fixture's `WhiteBrush` vs `NavyBrush` measures **12.98:1**. Numbers recorded in three
separate findings, months apart, are mutually consistent with a single achromatic one-accent theme.
The reconstruction is faithful, not invented.

### 8.4 The failing assertion

New file `src/ROROROblox.Tests/FlatlineLabGateTests.cs`. It resolves the fixture through
`ThemeService.ApplyTo` exactly as `ContrastPairGateTests.ResolveTheme` does
([`:115-145`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L115-L145)) and asserts the AA
measurement **fails**, with the failure attributed:

1. **Named-pair assertions, not a bare count.** At minimum `WhiteBrush` on `MagentaBrush` is
   asserted below 3.20 (the exemption's own floor) at 2.99:1, and `NavyBrush` on `CyanBrush` below
   4.5 at 4.34:1. A malformed fixture that "fails" because a slot is missing or a hex will not parse
   proves nothing, so every assertion names its pair and its expected ratio to two decimals.
2. **Parse-health assertion first.** Every resolved slot returns a non-null ratio before any failure
   is asserted. This is what separates "fails for the stated reason" from "fails because the theme
   is broken."
3. **The recorded ratios are asserted directly**, so the fixture cannot drift away from the numbers
   it exists to preserve.

Measured today, the fixture puts **4 pairs below AA** and drops the exempted pair **below its 3.20
floor** — it would trip both branches of `EveryDeclaredPairClearsAaUnderEveryTheme`, including the
`EXEMPTED PAIR GOT WORSE` branch. The gate can go red. That is the deliverable.

### 8.5 One-line change in the existing gate

`BuiltInThemes()`'s guard still says "the 3 built-in themes (brand, midnight, magenta-heat)"
([`:163-165`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L163-L165)). It says four and names
flatline. Flatline enrols itself in `EveryDeclaredPairClearsAaUnderEveryTheme` the moment it ships,
because that test iterates whatever `ThemeStore` returns with `IsBuiltIn` — nothing to wire.

## 9. The fourth capture round

*Implements `prd.md > Epic 6`. Last, because it verifies §4 through §6.*

**No edit to `scripts/capture-ui.ps1`.** `Get-AvailableThemes` enumerates the live picker and matches
on the `Id = <id>,` substring ([`:635-664`](../scripts/capture-ui.ps1#L635-L664)); the expected-count
guard multiplies by the theme count it found ([`:977`](../scripts/capture-ui.ps1#L977)). Both numbers
are derived, neither is hardcoded.

Verified against the existing manifests rather than assumed: `docs/ui-evidence/run-brand.json`,
`run-magenta-heat.json` and `run-midnight.json` each record **14 surfaces, 14 ok** — 42 total, from
the **18** surfaces `docs/ui-routes.json` declares. A fourth round therefore yields **56**, and
`run-flatline.json` lands beside the other three. The PRD's numbers check out.

The round is the evidence §5 and §6 are signed off against. Somebody looks at the PNGs — that is the
acceptance criterion, not a green test run.

> **Capture safety, carried forward unchanged.** Do not capture `preferences-alerts` with live
> webhook URLs in the fields. A Discord webhook URL is a bearer credential (F-076). The script
> refuses mechanically, but UIA text is not rendered pixels, so the manual step stands.

## 10. Reconciling the numbers

*Implements `prd.md > Epic 5`. Per `CLAUDE.md`'s findings-register rule, every row flips in the same
PR that ships the change — not a follow-up doc, not the next wave's close-out.*

### 10.1 Register rows

`docs/superpowers/research/2026-08-04-rororo-settings-ui-audit-findings.md`. Each row states which
artifact reproduces which number:

- **F-031** (`clean`) — its flatline ratios (1.00:1 divider, 4.34:1 cyan) are reproduced by
  `flatline-lab`, cited by file. Shipped flatline measures 1.51:1 and 12.84:1 and is a different
  theme. Both stated; conflating them is the defect being fixed.
- **F-032** (`clean`) — 1.00:1 belongs to `flatline-lab`. Shipped flatline measures **2.65:1**,
  better than brand's 2.42:1.
- **F-050** (`open`, **stays open**) — 2.99:1 belongs to `flatline-lab`. Shipped flatline measures
  **4.68:1**, above AA and needing no exemption. The row does **not** close: this cycle does not
  implement F-050's fix direction (resolve CTA foreground at brush-application time), it only ships
  a theme that does not need the exemption. `NoExemptionOutlivesItsFinding`
  ([`:254-288`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L254-L288)) deletes the exemption
  automatically the moment that row stops being `open`, which would tighten the gate on brand
  (3.79:1), midnight (4.16:1) and magenta-heat (3.29:1) and turn **all three** red. Flipping it
  casually breaks the build.
  > **Corrected 2026-08-10 by the register re-verification.** This paragraph said "brand and
  > magenta-heat" and "both", omitting midnight, and §15.6 and the register's own F-050 row repeated
  > it. `AaThreshold` is 4.5 ([`ContrastPairGateTests.cs:62`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L62))
  > and §4.3 records midnight at 4.16:1 on that pair, three lines above where the claim was made.
  > Every pre-flatline built-in fails without the exemption; only flatline (4.68:1) survives. The
  > guard is 50% larger than it described itself as, which is the direction that matters least for
  > safety and most for whoever eventually decides this row is cheap to close.
- **F-002** — its 1.00:1 flatline row-vs-page number belongs to `flatline-lab`. Shipped flatline
  measures **1.33:1**, better than brand's 1.09:1.
- **Citation fix.** The register's "Flatline fixture definition" note cites
  `docs/ui-capture-checklist.md:8-9` for the one-background/one-text/one-accent definition. Lines
  8-9 are the nav-rail correction. The definition is at **`:21-23`**. Found during `/spec` recon;
  same defect class as the rows themselves, so it lands in the same PR.
- **New row for the Bloxstrap banner literals** (§7), so the finding exists rather than living in
  this spec alone.

Every updated row is verified against the tree. "The scope doc said so" is not evidence.

### 10.2 In-code claims that go false on merge

*`prd.md > Story 5.2`.*

- **`ContrastPairGateTests` class doc, [`:36-45`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L36-L45).**
  States flatline "is NOT covered here, because it is not a shipped theme", "was never committed as
  a `ThemeStore` entry", and that the gate "cannot reproduce those numbers." Every clause is false
  the moment this ships. Rewritten in the same PR to say what is true: flatline is a fourth built-in
  and enrols automatically; the adversarial numbers live in `flatline-lab`, which the sibling test
  measures.
- **`MutedTextFenceTests` doc, [`:10-13`](../src/ROROROblox.Tests/MutedTextFenceTests.cs#L10-L13).**
  Cites "1.00:1 under flatline." That number belongs to `flatline-lab` after this cycle. Corrected,
  with shipped flatline's 2.65:1 named beside it so the fence's rationale still reads.

### 10.3 A third stale claim, found by measurement

**Not in scope.md, not in the PRD. Surfaced during `/spec` and verified against two commits.**

`ContrastPairGateTests` states "44 elements across 18 files, collapsing to 9 distinct pairs"
([`:52`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L52)) and repeats "9 measured 2026-08-09"
in two assertion messages ([`:181`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L181),
[`:193`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L193)). The app ships **8**.

Verified rather than asserted: running the scan against the tree at `1fcf74d`, the commit that
authored the gate, returns 44 elements and 9 pairs. Against `HEAD` it returns 44 elements and 8.
Commit `2c9ab16` (the F-032 fix, PR #100) rebound three `MutedTextBrush` foregrounds to
`WhiteBrush`, merging `MutedTextBrush on NavyBrush` into the existing `WhiteBrush on NavyBrush` pair.
Element count unchanged, pair count down one.

Two consequences:

1. **The comments are stale.** `MinimumPairs` is 6 so nothing fails, but three in-code claims
   describe an app that has not existed since PR #100. Corrected in the same PR, same defect class
   as §10.2.
2. **The gate can no longer see `MutedTextBrush` at all.** Its own fix removed the only declared
   pair whose foreground was the prose token. So flatline's MutedText values are **unmeasured by the
   pair gate** — which is exactly why §4.3 measures them explicitly and states the numbers. This is
   a real blind spot in the gate, not a flatline problem, and it belongs in the register.

## 11. Testing

### 11.1 The measurement method used to write this spec

Reproducible, and stated so the numbers here can be re-derived rather than trusted:

1. The declared-pair list is scanned live from `src/ROROROblox.App/**/*.xaml` with the gate's own
   regexes ([`:56-64`](../src/ROROROblox.Tests/ContrastPairGateTests.cs#L56-L64)).
2. Candidate palettes are resolved through `ROROROblox.App.Theming.ThemeService.ApplyTo` into a real
   `ResourceDictionary`, including the derived `InteractiveEdgeBrush`.
3. Every ratio comes from `ContrastGuard.RatioBetween`.

Steps 1-3 were run inside the test project on 2026-08-10 as a temporary xUnit fact, deleted after
the numbers were recorded. Validation of the method itself: it reproduces brand's 3.79:1, midnight's
4.16:1 and magenta-heat's 3.29:1 on the F-050 pair, F-031's 1.26:1, F-032's 2.42:1, and the
`#1F3149 -> #5E6B7C` derived edge that [`Theme.cs:52`](../src/ROROROblox.Core/Theming/Theme.cs#L52)
records — six numbers recorded independently, in three different files, all matched.

### 11.2 Automated

| test | asserts | new? |
|---|---|---|
| `ContrastPairGateTests.EveryDeclaredPairClearsAaUnderEveryTheme` | flatline enrols automatically and passes every pair at AA | existing, no wiring |
| `ContrastPairGateTests.BuiltInThemes` guard | four built-ins, named | one-line edit |
| `FlatlineLabGateTests` | the gate goes red on the fixture, on named pairs, at recorded ratios | **new** |
| `ThemedStatusColourTests` | both converters gone; no new literal brushes outside the allow-list | **new** |
| `MutedTextFenceTests` | unchanged behaviour; doc corrected | doc only |
| `ThemeStoreTests` | four built-ins; a user `flatline.json` does not displace the built-in | extended |

### 11.3 Manual, and non-negotiable

Automated coverage stops at arithmetic. These do not:

1. Pick Flatline in Settings → Appearance. **No prompt, no modal, no warning** (Story 1.2, on
   screen, not on paper).
2. Main window repaints immediately, no restart. Reopen Diagnostics and About to confirm secondary
   dialogs adopt on next open, matching the copy already under the picker.
3. Restart the app. Flatline is still active.
4. Drop a hand-written `flatline.json` into `%LOCALAPPDATA%\ROROROblox\themes\`. The built-in wins
   and the file is dropped. **Verify by dropping one**, not by reading
   [`ThemeStore.cs:71-76`](../src/ROROROblox.Core/Theming/ThemeStore.cs#L71-L76).
5. Delete the themes folder entirely. Flatline still appears.
6. Cover the status dot on a screenshot with all four states present. Still readable.
7. **Look at the brand captures**, not just flatline — §5.3 changes the default theme's active dot.
8. Run `scripts/capture-ui.ps1`, confirm 56 captures and `run-flatline.json`.

## 12. Data model

**`Theme` is unchanged.** Ten required slots, no eleventh. Both forks the PRD routed here — the
description sentence (§4.5) and the converter colours (§5.3) — were resolved without growing the
contract, which is the invariant `ContrastGuard`'s own documentation defends. Every user theme JSON
on disk stays valid without its author touching it.

`ThemeDescriptions` (§4.5) is App-layer presentation copy keyed by id, deliberately not a slot.

## 13. File structure

Only what this cycle touches.

```text
src/
├── ROROROblox.Core/
│   └── Theming/
│       ├── Theme.cs                        UNCHANGED — the contract does not grow
│       ├── ContrastGuard.cs                UNCHANGED — every new measurement calls into it
│       ├── EdgeRemediation.cs              UNCHANGED — flatline takes DeriveSilently, verified
│       └── ThemeStore.cs                   +1 Theme record in BuildBuiltIns()          §4.1
├── ROROROblox.App/
│   ├── App.xaml                            -2 StaticResource converter declarations       §5.1
│   ├── Converters.cs                       -StatusDotBrushConverter -IdleChipBrushConverter §5.2
│   ├── MainWindow.xaml                     status dot / idle chip / memory chip x2 (incl.
│   │                                       compact row) -> Style+DataTrigger; expired-row
│   │                                       left rule; banner ▲                          §5,§6
│   ├── ViewModels/AccountSummary.cs        IdleText gains the ▲ prefix + INPC wiring      §6.3
│   ├── Theming/
│   │   ├── ThemeDescriptions.cs            NEW — id -> one sentence, App layer only       §4.5
│   │   └── ThemeService.cs                 UNCHANGED
│   └── Preferences/
│       ├── PreferencesWindow.xaml          description TextBlock under the picker         §4.5
│       └── PreferencesWindow.xaml.cs       OnThemeChanged sets it
└── ROROROblox.Tests/
    ├── ContrastPairGateTests.cs            class doc + guard + pair-count comments        §8.5,§10
    ├── ConvertersTests.cs                  DELETED — both facts assert the removed literals §5.1
    ├── MutedTextFenceTests.cs              class doc only                                 §10.2
    ├── FlatlineLabGateTests.cs             NEW — the gate can fail                        §8
    └── ThemedStatusColourTests.cs          NEW — regrowth fence                           §5.4

docs/
├── spec.md                                 this file
├── superpowers/research/2026-08-04-...-findings.md   F-031/032/050/002 + citation + new row §10.1
└── ui-evidence/run-flatline.json           NEW, produced by the capture run, gitignored    §9

scripts/capture-ui.ps1                      NO EDIT — enumerates at runtime                 §9
```

> **As built, item 8.** Five files not in the tree above, all from items 3a, 3b and 4:
>
> ```text
> src/ROROROblox.App/App.xaml                 SelectionDotStyle's 4 hexes -> {DynamicResource}   3b
> src/ROROROblox.App/MainWindow.xaml          + status-bar live-process dot (F-088)              3a
> src/ROROROblox.Tests/ExpiredRowRedundancyTests.cs   NEW — the left rule, measured per theme     4
> src/ROROROblox.Tests/AccountSummaryTests.cs        extended — IdleWarn raises IdleText          4
> src/ROROROblox.App/Preferences/PreferencesWindow.xaml.cs  UpdateThemeDescription               4.5
> ```
>
> The two version files — `ROROROblox.App.csproj` and `Package.appxmanifest`, both `1.17.0.0` — are
> item 8's own edit and were never in scope for the tree above.

## 14. Data flow

**Theme selection.** Picker `SelectionChanged` → `ThemeService.SetActiveAsync(id)` → `IThemeStore`
lookup → `IAppSettings.SetActiveThemeIdAsync` (failure is logged, not fatal — the theme still
applies for the session) → `ApplyToResources` marshals to the UI thread → `ApplyTo` replaces ten
brush instances and derives the eleventh → every `{DynamicResource}` subscriber re-binds, including
the `DataTrigger` setters added in §5 → main window repaints, no restart. `ThemeDescriptions.For(id)`
updates the line under the picker on the same event.

**Status colour, after this cycle.** `AccountSummary.StatusDot` returns `"green"` / `"yellow"` /
`"magenta"` / `"grey"` (unchanged, still four strings) → a `DataTrigger` on the `Ellipse`'s `Style`
matches the string and sets `Fill` to a `{DynamicResource}` slot → the brush resolves from the live
dictionary. Theme change repaints it; no converter, no cached instance, no literal.

## 15. Key technical decisions

1. **Flatline is an achromatic ramp, not a single flat surface.** Colour vision deficiency affects
   hue, not luminance. Collapsing `Bg`, `RowBg` and `RowExpiredBg` to one value would reproduce
   F-002's own defect while claiming to fix accessibility. Trade accepted: flatline is not literally
   "one background", and scope's loose note is superseded on the record.
2. **Two accent lightnesses, because a single value was disproved by enumeration.** 26 solutions
   exist across the whole achromatic grid; all force a page at `#040404` or darker and cap
   RowBg-vs-Bg separation at 1.024:1. Trade accepted: "one accent" becomes one accent treatment at
   two lightnesses, still with no hue.
3. **Delete the two converters rather than teach them to read the theme.** A converter cannot
   observe a resource-dictionary change, so a resolve-at-Convert-time fix would silently fail the
   live-repaint requirement. Trade accepted: three XAML `Style` blocks are more markup than two
   converter classes, and they match the idiom already used twice in the same file.
4. **Active maps to `WhiteBrush`, not `CyanBrush`.** Cyan collides with `RowExpiredAccent` at 1.00:1
   under flatline. Trade accepted and flagged for a checkpoint: brand's active dot changes from
   green to white, a visible change to the default theme.
5. **The description is an App-layer lookup, not an eleventh slot.** The theme contract does not
   grow; every user JSON stays valid. Trade accepted: user themes get no description.
6. **F-050 stays open.** This cycle ships a theme that does not need the exemption; it does not
   implement the exemption's fix. Closing the row would auto-delete the exemption and redden brand
   and magenta-heat.

## 16. Open issues

> **Updated at item 8, 2026-08-10, post-build.** The list below supersedes the one written at
> `/spec`. Every original entry is kept with what became of it, so nothing is dropped by being
> quietly true or quietly resolved, and four entries the build surfaced are added.

**Carried forward, still open:**

- **The gate cannot see `MutedTextBrush`** since PR #100 (§10.3). Now **F-086**, `open`. PR #100's
  own fix removed the last declared pair using the prose token as a foreground, so ~104 bindings are
  measured by nothing. `MinimumPairs` is 6, so losing a pair failed nothing and announced nothing.
  Flatline's muted values are measured in §4.3 and asserted nowhere. Phase 2 gate work.
- ~~**The gate cannot see style-resolved brushes**, so items 3, 3a and 3b are **fenced, not
  gated**~~ — **CLOSED after the cycle**, by the Phase 2 rendered gate on branch
  `feat/rendered-contrast-gate`. `TriggeredStatusColourGateTests` extracts the shipped `Style`
  subtrees out of `MainWindow.xaml` with `XamlReader`, binds a real `AccountSummary`, renders on an
  STA thread and samples pixels: 16 of 16 dot cases and 24 of 24 chip cases resolve to the exact
  theme slot §5.3 maps them to, across all four built-ins. Items 3, 3a and 3b are **gated now, not
  fenced.** `ThemedStatusColourTests` stays as it is — it guards against literals regrowing, which
  is a different question from whether the trigger fired.
  **The fence stays load-bearing for one site the gate does not reach:** the status bar's
  live-process dot (F-088's site, `MainWindow.xaml:1888`) binds `LiveProcessCount` on
  `MainViewModel`, which is not cheaply constructible in a test, so it is still guarded only by the
  XAML fence. Do not read "gated" as covering it.
- **Bloxstrap banner literals** (§7). Now **F-085**, `open`, and **three** literals rather than the
  two this document named: `#3F3000`, `#8F7000`, and a body `Foreground="#FFE3A6"`. Fix belongs with
  F-068.
- **`ui-routes.json` declares 18 surfaces and each round captures 14.** Still uninvestigated. The
  same four are missing in every round, so a fourth round is comparable to the other three either
  way.

**New, surfaced by the build:**

- **`ConsentSheet.xaml.cs:90-92` constructs brand cyan and magenta in C#** as `TryFindResource`
  fallbacks. **F-087**, `open`. Fallback-only reach, so the practical blast radius is small; the row
  exists so the fence's allow-list entry is traceable to a finding rather than being an untraceable
  exemption.
- **The mutex-recovery banner, F-066**, `open`. Item 6 found its `MainWindow.xaml` citations had
  drifted — and that they had already drifted on `main` before this branch was cut — and that the
  banner carries a third literal line the row never counted. Row reconciled; the defect is intact.
- **The per-account identity palette exists in three hand-synced copies, in three encodings.** See
  §7's correction. No register row, because §7 puts it out of the theme's reach as identity paint,
  but **neither of the two sync comments names all three copies**. A ninth colour added to one leaves
  the picker, the row badge and the Win32 title bar disagreeing, silently.
- **Every `Verify:` line in `docs/checklist.md` shipped with a test filter that matched nothing.**
  `--filter "ThemeStore*|ContrastPairGate*"` — VSTest's filter grammar has no glob wildcards, so the
  run reports `No test matches the given testcase filter` and a checkpoint could be signed off on
  zero tests. Corrected at item 8 in all three places to `FullyQualifiedName~…` form, and each
  corrected filter was run to confirm it matches something. Carried here because the shape will
  reappear in the next cycle's checklist unless somebody remembers it.

**Resolved, no longer open:**

- **Copy for §4.5's four sentences.** Drafted here, shipped as written, approved by the builder at
  checkpoint C1. `Theming/ThemeDescriptions.cs:14-21`.

**Still owed to a human:** §11.3's manual list and the fourth capture round (item 7), including eyes
on the **brand** captures — §5.3 changed the default theme's active dot. No test in this project
loads a `Window`, so nothing above substitutes for that.

---

## Appendix — cycle history

Prior cycles' canonical specs, none superseded by this file:

- v1.1 core: [`2026-05-03-rororoblox-design.md`](superpowers/specs/2026-05-03-rororoblox-design.md)
- v1.2 per-account FPS limiter: [`2026-05-07-per-account-fps-limiter-design.md`](superpowers/specs/2026-05-07-per-account-fps-limiter-design.md)
- v1.3.x default-game widget + local rename: [`2026-05-07-default-game-widget-and-rename-design.md`](superpowers/specs/2026-05-07-default-game-widget-and-rename-design.md)
- v1.3.x save-pasted-links: [`2026-05-08-save-pasted-links-design.md`](superpowers/specs/2026-05-08-save-pasted-links-design.md)
- v1.3.x Roblox-already-running detect: [`2026-05-08-roblox-already-running-detect-design.md`](superpowers/specs/2026-05-08-roblox-already-running-detect-design.md)
- v1.3.x persist `RobloxUserId`: [`2026-05-08-persist-roblox-user-id-design.md`](superpowers/specs/2026-05-08-persist-roblox-user-id-design.md)
- v1.4 plugin system: [`2026-05-09-rororo-plugin-system-design.md`](superpowers/specs/2026-05-09-rororo-plugin-system-design.md)
- v1.5.0 presence account-UX: [`2026-05-20-rororo-presence-account-ux-design.md`](superpowers/specs/2026-05-20-rororo-presence-account-ux-design.md)
- v1.6.0 account transport + bundle: [`2026-05-21-rororo-account-transport-and-bundle-design.md`](superpowers/specs/2026-05-21-rororo-account-transport-and-bundle-design.md)
- v1.7.0 install deferral: [`2026-05-21-rororo-install-deferral-design.md`](superpowers/specs/2026-05-21-rororo-install-deferral-design.md)
- glow campaign conventions: [`2026-08-04-rororo-settings-navigation-conventions.md`](superpowers/specs/2026-08-04-rororo-settings-navigation-conventions.md)
- F-032 control labels: [`2026-08-09-rororo-f032-control-labels-design.md`](superpowers/specs/2026-08-09-rororo-f032-control-labels-design.md)
- rendered contrast gate: [`2026-08-09-rororo-rendered-contrast-gate-design.md`](superpowers/specs/2026-08-09-rororo-rendered-contrast-gate-design.md)

When build reality drifts from a canonical spec, banner-correct at the top of that doc per
`CLAUDE.md`'s "Don't rewrite the canonical spec on drift" rule.
