# RORORO — v1.21 PRD: the surfaces behind the buttons

Implements [`docs/scope.md`](scope.md). Two waves, autonomous build.
**Every citation below was read at HEAD on 2026-08-11**, not taken from the register — two rows
changed shape under that reading and one would have caused real damage if trusted.

---

## Story 1.1 — A warning banner looks like the theme, in every theme

**As** a player who picked flatline, **I want** the Bloxstrap warning to read as a warning **without**
depending on a hue the theme never chose.

**Today:** `MainWindow.xaml:1592,1593,1606` hardcode `#3F3000` / `#8F7000` / `#FFE3A6`. The banner
renders identical warm amber under all four built-in themes.

**The complication, and the reason this is item 1 rather than a rebind.** One Grid row above it,
`MainWindow.xaml:1570-1572`, the compat banner **is** themed — `RowExpiredBgBrush` surface,
`RowExpiredAccentBrush` border and text. The Bloxstrap banner's own comment says its amber is
deliberate: *"Warm amber tone distinct from the red-ish compat banner above it."* Both are
independently visible and **can show at once**.

Under flatline the compat banner goes grey and Bloxstrap stays amber. **The distinction survives only
because one banner ignores the theme** — it is carried by the defect, not by a decision.

**Acceptance:**
- No colour literal remains in the Bloxstrap banner block.
- Both banners are legible in all four built-in themes, measured, foreground against its own surface.
- **If the two banners can co-show, they remain distinguishable** — and the carrier of that
  distinction is stated. If it is not colour, say what it is.
- No eleventh theme slot. Invariant 6 holds: derived value or non-colour carrier only.

**Edge case:** both banners visible simultaneously under flatline is the case to check, not brand.

---

## Story 1.2 — The About box's artwork survives, and its chrome stops being hardcoded

**As** a player on flatline, **I want** the About box to sit on the page rather than on a navy plate.

**What is NOT wrong, and must not be "fixed".** `AboutWindow.xaml:13-20` declares eight
`SolidColorBrush` resources — `CyanBrightBrush`, `CyanDimBrush`, `CyanShadowBrush`, `MagentaDimBrush`,
`MagentaShadowBrush`, `NavySoftBrush`, `TealBrush`, `TealDeepBrush`. These paint the **iso voxel stack
logo** on a 64×64 `Canvas` (`:33-56`). That is brand identity artwork, in the same category as the
per-account caption palette that `ThemedStatusColourTests` allow-lists against spec §7. **A themed
logo is a broken logo.** The register row reads "8 literal SolidColorBrush resources" and taking that
at face value would have recoloured the mark.

**What IS wrong:**
- `:34` the `Canvas` carries `Background="{StaticResource NavySoftBrush}"` — a fixed navy plate behind
  the logo. Under flatline this is the "hard dark rectangle behind the icon" the finding describes.
- `:96` `Background="#15263A"` is `RowBgBrush`'s exact value, written by hand.

**Acceptance:**
- The logo renders **byte-identical** in all four themes. This is the assertion that proves the
  artwork was left alone.
- The plate behind it is gone or themed, so the logo sits on the page field.
- `:96` binds `RowBgBrush`.
- The eight artwork brushes are **exempted by name with a written reason**, so the next sweep does not
  re-litigate them.

---

## Story 1.3 — History rows survive a theme with one surface colour

**As** a player reading session history under flatline, **I want** to tell one session from the next.

**Today:** `SessionHistoryWindow.xaml.cs:150-155` builds each row as a `Border` whose only separation
is `Background = RowBgBrush`. Under flatline the row fill and the page field collapse to near-identical
greys and the rows disappear. The date-group heading survives, so navigation is not lost — row
*boundaries* are.

**Acceptance:**
- A row is distinguishable from its neighbour under every built-in theme, and the carrier is **not
  fill alone** — a rule, a baseline rhythm, or a boundary that survives greyscale.
- Rows still take a themed fill; this adds a carrier rather than removing one.
- The change is in code-behind, so it is invisible to any XAML-reading gate — **note that explicitly**
  and cover it the way `ButtonRankFenceTests` covers code-built buttons.

---

## Story 2.1 — The contrast gate measures the token it currently cannot see

**As** the next cycle's builder, **I want** the gate to measure `MutedTextBrush`, because F-050's fix
cannot be verified by a gate blind to its pairs.

**Today:** `ContrastPairGateTests` measures only elements declaring **both** `Background` and
`Foreground` inline. Since PR #100 rebound the last declared `MutedTextBrush` foreground to
`WhiteBrush`, **no scanned element pairs the prose token with a fill** — roughly 113 bindings are
measured by nothing.

**Acceptance:**
- A small named-pair list is measured **unconditionally**, independent of what the scan happens to
  find: `MutedTextBrush` on `RowBgBrush` and `MutedTextBrush` vs `WhiteBrush` at minimum.
- Each pair's ratio is recorded per theme.
- The gate is shown failing on a deliberately broken pair before it counts.
- **F-050 does not close in this cycle.** This is its prerequisite, not its fix.

---

## Story 2.2 — Small honesty fixes

| Row | Change | Acceptance |
|---|---|---|
| **F-087** | `ConsentSheet.xaml.cs:90-92` returns `CyanBrush` or `MagentaBrush` with hardcoded fallbacks, chosen in C# | The branch is a XAML `DataTrigger` on `IsHostEnforced`; no literal fallback |
| **F-093** | `DefaultPlaceUrl` is referenced **zero** times by the App; `IAppSettings.cs:7` claims Preferences edits it | Field removed from the record and interface. **Deserialization of an existing `settings.json` carrying it must be answered deliberately and tested**, not discovered |
| **F-021** | `GamesWindow.xaml:396` "Use the Squad Launch toolbar button to add one" — points at a closed window for something that saves itself | Copy names the real mechanism |
| **F-022** | `MultiInstanceCopy.FpsCapMismatchBanner` — **re-read first, the text moved since the row was written.** Still ~45 words with the action last | Action leads; mechanism follows; not longer |
| **F-070** | `JoinByLinkWindow.xaml:27-33` and `WelcomeWindow.xaml:38-43` ship half the cyan/magenta duo | Both match the 12 siblings, or the row closes as resolved with evidence |
| **F-074** | `StopAllConfirmWindow.xaml:36` puts "UNSAVED GAME STATE WILL BE LOST" in 10px mono uppercase at `#5A6982` — the dimmest text on the surface carrying the worst news | Ordinary body prose; mono-uppercase reserved for labels |

---

## Story 2.3 — Two rulings, not two builds

- **F-095** — the crash is fixed; the row is open only for its surfacing half. Rule on whether a log
  Warning is sufficient or a user-visible surface is owed, and record it.
- **F-098** — partly fixed. Its remaining scope is `capture-ui.ps1` and the packaging scripts. Rule on
  whether that lands here or waits.

Both close or narrow with a written decision. Neither gets code this cycle.

---

## Prioritisation

**Must-have (wave 1)** — 1.1, 1.2, 1.3. These are what a flatline screenshot shows, and the Store
recapture follows this cycle.

**Must-have (wave 2)** — 2.1 (unblocks F-050), 2.2's F-093 and F-087.

**Should-have** — 2.2's copy rows. Cheap, user-visible, no dependency.

**Explicitly later** — F-052, F-050, the ~55 literals in out-of-scope modals, the recapture.

## The cycle's own risk

The register was wrong twice in five rows on this scope, once damagingly. **Read the site before
building the row.** Where the tree and the register disagree, the tree wins and the row gets corrected
in the same PR.
