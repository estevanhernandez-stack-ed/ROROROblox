# RORORO — Technical Spec: the surfaces behind the buttons

> ## ⚠ BANNER CORRECTION — archived 2026-08-12, after the build
>
> **Archived from `docs/spec.md` unchanged below this block.** Per `CLAUDE.md`, drift is
> banner-corrected rather than rewritten, so the original reasoning stays legible to `/reflect`.
> Four things in the text below are wrong or were overridden. Three of the four are the same shape
> as the three §0 catches this spec is itself proud of, which is the useful part: **§0 corrected the
> register, and nobody applied §0's own method to §0.**
>
> **1. §3 (History rows) prescribes a fix this codebase had already ruled against.** It says derive
> the row boundary through `ContrastGuard.Ensure` so it clears 3:1. `ThemeSlots.InteractiveEdge`'s
> own doc says WCAG 1.4.11 governs component boundaries and **not separators**, and that binding the
> derived edge to a card edge or a row rule *"would repaint every user's theme from a hairline to
> mid grey to fix a problem those surfaces do not have"* — enforced by
> `InteractiveEdgeBindingTests`, whose comment says it exists because *"just use the visible one
> everywhere, it looks cleaner"* is a very easy edit to make. Deriving one measures
> `#1F3149 -> #647181` in brand, which is that exact repaint. **Built instead:** F-065's own second
> option, *"a baseline rule OR fixed leading rhythm"* — gutter 12 against inset 8, geometry that
> cannot fail a theme. See item 3's commit and `HistoryRowRhythmTests`.
>
> **2. §2 asserts a gate that failed at HEAD.** It says the logo *"renders byte-identical across all
> four themes"* and calls that assertion the proof the artwork was left alone. `AboutWindow.xaml:44`
> — the magenta block's top face — was bound to the `MagentaBrush` **theme slot**, resolving
> `#F22F89` / `#C0407E` / `#6E6E6E` across the built-ins. §0.1 counted the eight declared brushes
> and never looked at what the polygons pointed to. Fixed in item 4; gated by `AboutArtworkTests`,
> which is the gate §2 asked for and which caught this on its first run.
>
> **SYMPTOM CORRECTED 2026-08-12.** Item 4 also claimed this rendered as "a grey top on a magenta
> body under flatline". It does not — that face is fully occluded by the cyan block stacked on it,
> proven by painting it `#00FF00` and rendering: zero green pixels. The markup fix stands on
> principle; the visual consequence was asserted from reading markup and never rendered, which is
> this banner's own §0-method failure committed one level down.
>
> **3. §0.2 (F-093) is itself a bad correction.** It says deleting `DefaultPlaceUrl` would *"silently
> move those users from their saved place to the home page"*. `IRobloxLauncher` has two `LaunchAsync`
> overloads: the legacy string one reads the setting, the typed `LaunchTarget` one ignores it and
> falls through to `Home`. **The app calls only the typed one** (`MainViewModel.cs:1568`), and
> `RobloxLauncherTests` pins that the legacy value must never reach the launch URI. Nobody's launch
> target changes. The register row's *"zero App references"* was right about the app as well as the
> UI. Option 2 was still taken — the setter is gone, the field stays — but for the smaller reason
> that the field costs nothing, not the stated one.
>
> **4. §4's premise about the ceiling was right and its blind spot was the mirror image.**
> `AllowedXamlLiteralCeiling` read 95 against a real 62 at the branch point, because v1.20 added
> `StripXmlComment` and v1.20's sweep removed four literals and neither re-derived the constant. The
> assertion is `allowed <= ceiling`, so improvements are invisible. §4 worried about gates that
> claim MORE than they measure; this one claimed LESS, which fails just as silently. Re-derived to
> 57 by the end of the cycle. Recorded on F-098.
>
> **What the spec got right and should be read for:** §0.1's ruling that the eight brushes are the
> mark and must not be themed (correct, and it prevented real damage), §1's banner ruling and its
> F-032 precedent, §4's diagnosis of the coverage gap, and the measurement table, every figure of
> which reproduced exactly.

**This file is the canonical technical artifact for the v1.21.0 cycle.** `docs/spec.md` is overwritten
every Cart round; **archive it into `docs/superpowers/specs/` before the next round.** v1.20's is
archived as
[`2026-08-11-rororo-button-vocabulary-design.md`](superpowers/specs/2026-08-11-rororo-button-vocabulary-design.md).

Implements [`docs/prd.md`](prd.md). Closes F-063, F-066, F-085, F-086, F-087 and the copy rows.
**Does not close F-050 or F-052.**

**Anchor:** v1.20 gave the buttons one vocabulary. This gives the surfaces under them one too.

---

## §0 Three rows the register got wrong, verified at HEAD

The build must read the site before building the row. On this scope the register was wrong three times
in ten rows, and two of the three would have caused real damage.

### §0.1 F-063 — the eight "literals" are the logo, and must not be touched

`AboutWindow.xaml:13-20` declares eight `SolidColorBrush` resources. They paint the **iso voxel stack
logo** on a 64×64 `Canvas` at `:33-56`: `CyanBrightBrush` `#6CEAFD`, `CyanDimBrush` `#12BFE3`,
`CyanShadowBrush` `#0D94B8`, `MagentaDimBrush` `#F22F89`, `MagentaShadowBrush` `#B81F66`,
`NavySoftBrush` `#0F1F31`, `TealBrush` `#2EE6C9`, `TealDeepBrush` `#1A9F8B`.

That is brand identity artwork — the same category as the per-account caption palette that
`ThemedStatusColourTests` allow-lists against spec §7, and for the same reason: it paints WHO
something is, not WHAT STATE it is in. **A themed logo is a broken logo.** Reading the row literally
and removing these recolours the mark.

**Real defect, two sites:** `:34`'s `Canvas Background="{StaticResource NavySoftBrush}"`, and `:96`'s
`Background="#15263A"`.

### §0.2 F-093 — the field is not dead, and deleting it changes where people launch

The row says the App references `DefaultPlaceUrl` **zero** times. True of the UI, false of the app:
`Core/RobloxLauncher.cs:258` awaits `GetDefaultPlaceUrlAsync()` as **step 3 of the live launch-target
resolution chain**, after favourites and before falling back to Roblox home.

No UI can set it any more, so only users who set it in an older version still carry a value. Deleting
the field silently moves those users from their saved place to the home page. That is a behaviour
change on the launch path, not a tidy-up, and it is why this item is not the 1-effort its row claims.

### §0.3 F-085 — the distinction is carried by the defect

`MainWindow.xaml:1592,1593,1606` hold `#3F3000` / `#8F7000` / `#FFE3A6`. One Grid row above,
`:1570-1572`, the compat banner is properly themed on `RowExpiredBgBrush` / `RowExpiredAccentBrush`.
The Bloxstrap comment calls its amber deliberate — "distinct from the red-ish compat banner above it".

Under flatline the compat banner goes grey and Bloxstrap stays amber. **The distinction exists only
because one banner ignores the theme.**

---

## §1 The warning-banner ruling

**Both banners collapse onto the one themed warning recipe. The distinction is carried by text and the
`▲` glyph, not by hue.**

This follows the app's own precedent rather than inventing one. F-032 faced exactly this: a control
label and the prose beside it were separated by colour, `MutedTextBrush` vs `WhiteBrush` measured
2.42:1 in brand and **1.00:1 under flatline**, so colour could not carry the distinction under a theme
the app ships. Weight carries it now. Same ruling here: **two warnings should look like two warnings**,
and what they say is what tells them apart.

The recipe already exists and is already in use at `MainWindow.xaml:1570-1572`:

```text
Background   {DynamicResource RowExpiredBgBrush}
BorderBrush  {DynamicResource RowExpiredAccentBrush}
Foreground   {DynamicResource RowExpiredAccentBrush}   (banner text)
prefix       "▲ " as a literal Run
```

**Do not add an eleventh theme slot.** Invariant 6 holds: every user theme on disk supplies ten, and an
eleventh breaks all of them.

**The Bloxstrap banner gains the `▲` prefix** it currently lacks — the same warning vocabulary
`ExpiredRowRedundancyTests.TheCompatBannerPrefixesTheSameWarnGlyph` already pins for the compat banner
and the memory/idle chips.

**Measurement gate:** banner text on banner surface must clear **4.5:1** in all four built-in themes.
`RowExpiredAccent` on `RowExpiredBg` — brand `#F1B232` on `#3A2D14`, flatline `#D4D4D4` on `#3D3D3D`.
**Measure before migrating.** If a theme fails, the foreground changes, not the floor.

## §2 About

- `:34` — bind the `Canvas` background to `{DynamicResource RowBgBrush}`. **Bind, do not remove.** The
  plate is a ground for a fixed-colour logo; removing it risks illegibility on a light user theme,
  which the four dark built-ins would never reveal. The defect was that the ground was frozen.
- `:96` — `#15263A` becomes `{DynamicResource RowBgBrush}`. That closes F-066's second site too.
- The eight artwork brushes stay and gain an **allow-list entry with a written reason** in
  `ThemedStatusColourTests`, in the same shape as the caption-palette entries.

**Gate:** the logo renders byte-identical across all four themes. That assertion is what proves the
artwork was left alone, and it is the one that fails if a future sweep themes it.

## §3 History rows

`SessionHistoryWindow.xaml.cs:150-155` builds a per-session `Border` separated only by
`Background = RowBgBrush`.

Add a **non-fill boundary**: `BorderThickness="0,0,0,1"` on `{DynamicResource DividerBrush}`, keeping
the themed fill.

**Measure it.** Under flatline `DividerBrush` is `#333333` against `RowBgBrush` `#2A2A2A` — a boundary
that may not read. If it does not clear **3:1** (WCAG 1.4.11, the floor `ContrastGuard` already
enforces for interactive boundaries), **derive it through `ContrastGuard.Ensure`** exactly as
`InteractiveEdgeBrush` is derived. Do not settle for a boundary that measures under the floor.

**This is code-behind**, so no XAML-reading gate can see it. Cover it the way `ButtonRankFenceTests`
covers code-built buttons — by scanning `.cs`, not markup.

## §4 F-086 — the pairs the gate cannot see

`ContrastPairGateTests` measures only elements declaring both halves inline. Since PR #100 rebound the
last declared `MutedTextBrush` foreground to `WhiteBrush`, **no scanned element pairs the prose token
with a fill**, leaving ~113 bindings unmeasured.

Add a list measured **unconditionally**, independent of what the scan happens to find:

| Pair | Why |
|---|---|
| `MutedTextBrush` on `RowBgBrush` | the most common prose-on-surface pairing |
| `MutedTextBrush` on `BgBrush` | prose on the page field |
| `MutedTextBrush` on `NavyBrush` | the disabled-button label, measured 4.50 in midnight — the thinnest margin in the app |

Record each ratio per theme. **Show the gate failing** on a deliberately broken pair before it counts.

**F-050 does not close here.** This is its prerequisite.

## §5 The small ones

- **F-087** — `ConsentSheet.xaml.cs:90-92`'s `NamespaceBrush` becomes a XAML `Style` + `DataTrigger` on
  `IsHostEnforced`. The `TryFindResource(...) ?? new SolidColorBrush(...)` literal fallbacks go; a
  `DynamicResource` that fails to resolve is a bug to surface, not to paper over.
- **F-093** — see §0.2. **The decision is the deliverable.** Three options:
  1. Delete the field and accept that legacy users fall back to home.
  2. Keep the read path, delete the setter, and correct `IAppSettings.cs:7`'s false claim.
  3. One-time migrate a legacy value into the default-game mechanism, then delete.

  **Recommended: (2) this cycle** — it removes the lie without changing anyone's launch target, and (3)
  is a migration that deserves its own row. Whatever is picked, `JsonOptions` (`AppSettings.cs:15`)
  must be confirmed not to set `UnmappedMemberHandling.Disallow`, and a legacy `settings.json` carrying
  the property must round-trip in a test.
- **Copy** — F-021 (`GamesWindow.xaml:396`), F-022 (`MultiInstanceCopy.FpsCapMismatchBanner`,
  **re-read first, it moved**), F-070 (`JoinByLinkWindow.xaml:27-33`, `WelcomeWindow.xaml:38-43`),
  F-074 (`StopAllConfirmWindow.xaml:36`).

## §6 What gets tested

| Gate | Change |
|---|---|
| `ContrastPairGateTests` | + unconditional named pairs (§4); + About artwork allow-list entries |
| `ExpiredRowRedundancyTests` | extend the `▲` assertion to the Bloxstrap banner |
| new `BannerRecipeTests` | no colour literal in either banner block; both clear 4.5:1 in all four themes |
| new `AboutArtworkTests` | the eight artwork brushes resolve identically in all four themes |
| history-row coverage | a `.cs`-scanning assertion that the row carries a non-fill boundary |
| `AppSettingsTests` | legacy `settings.json` round-trip for whichever §5 option is taken |

**Every new gate must be shown failing before it counts.** That rule earned its place in v1.20 twice.

## §7 File structure

```text
src/ROROROblox.App/
├── MainWindow.xaml                       # M Bloxstrap banner -> themed recipe + glyph (§1)
├── About/AboutWindow.xaml                # M :34 canvas ground, :96 rebind (§2)
├── History/SessionHistoryWindow.xaml.cs  # M row boundary (§3)
├── Plugins/ConsentSheet.xaml(.cs)        # M colour branch -> DataTrigger (§5)
├── Games/GamesWindow.xaml                # M F-021 copy
├── Modals/StopAllConfirmWindow.xaml      # M F-074 copy
├── JoinByLink/JoinByLinkWindow.xaml      # M F-070
└── About/WelcomeWindow.xaml              # M F-070

src/ROROROblox.Core/
├── AppSettings.cs                        # M per the §5 ruling
├── IAppSettings.cs                       # M the false comment at :7
├── RobloxLauncher.cs                     # read-only unless option 1 or 3 is taken
└── MultiInstanceCopy.cs                  # M F-022

src/ROROROblox.Tests/                     # + the gates in §6
```

## §8 What this cycle must not do

- **Do not theme the About logo.** §0.1.
- **Do not delete `DefaultPlaceUrl` without ruling on the launch path.** §0.2.
- **Do not add a theme slot.** Invariant 6.
- **Do not close F-050.** §4 is its prerequisite, not its fix.
- **Do not start F-052.** 60 of 76 controls, its own cycle.
- **Do not ship a gate that cannot be made to fail.**
- **Do not build a row from its register text.** Three of ten were wrong here.
