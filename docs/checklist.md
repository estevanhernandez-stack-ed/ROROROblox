# RORORO — v1.21.0 build checklist: the surfaces behind the buttons

**Cycle:** v1.21.0.0 (current shipped on `main`: 1.20.0.0, untagged)
**Cycle type:** Remediation. No new features, no contract change, no spike.
[`docs/spec.md`](spec.md) is the canonical technical artifact. **Archive it into
`docs/superpowers/specs/` before the next Cart round** — item 12 owns that.
**Anchor:** v1.20 gave the buttons one vocabulary. This gives the surfaces under them one too.

## Build Preferences

- **Build mode:** Autonomous. The builder does not have the planning conversation — this file, plus
  `spec.md` and `prd.md`, is the whole handoff.
- **Git:** Commit after each item. Conventional commits. Branch `feat/pre-store-remediation`, already
  cut and already carrying scope + PRD + spec.
- **Verification:** **C1 after item 5** (end of wave 1). Every wave-1 item is a surface a Store
  screenshot will show, so a regression there is worth catching before wave 2 buries it.
- **TDD:** strict on **item 6** (the gate must be shown failing) and **item 2** (measure before you
  migrate). Items 3-5 are verify-by-render; 7-11 are small and verify-by-read; 12 is audit.

## Effort

**Total ≈ 5-7 hours.** Heaviest is **item 3** (History, because the boundary must be derived — see
below) and **item 6** (the gate widening surfaces a live failure that needs a ruling).

## Three measurements already taken, so the build does not repeat them

Run 2026-08-11 against the four built-in themes, through the same arithmetic `ContrastGuard` uses.
**These are inputs, not tasks.**

| What | brand | midnight | magenta-heat | flatline | Verdict |
|---|---|---|---|---|---|
| **§1** banner: `RowExpiredAccent` on `RowExpiredBg` | 7.13 | 6.58 | 7.13 | 7.33 | **clears 4.5 everywhere — migrate as-is** |
| **§3** boundary: `Divider` on `RowBg` | 1.16 | 1.08 | 1.05 | 1.14 | **fails 3.0 in all four — derive, do not try the plain bind** |
| **§4** `MutedText` on `RowBg` | 6.33 | **4.19** | 6.07 | 4.98 | **midnight is UNDER AA and shipped** |

The third one is a finding, not a chore. **F-086's widening will turn the suite red on a real,
currently-shipped failure** — muted prose on a row surface in midnight. That is the gate doing its job
on its first run, and item 6 has to rule on it rather than tune around it.

## What recon found that the register did not

`spec.md > §0` carries this in full. The short version, because it is the difference between a good
cycle and a damaging one:

1. **F-063's "8 literal brushes" are the About logo** — `CyanBright`, `MagentaShadow`, `TealDeep` and
   five more, painting a 64×64 iso voxel stack. Brand artwork, same category as the caption palette.
   **Theming them recolours the mark.** The real defect is two sites, not eight.
2. **F-093's "dead field" is live.** `RobloxLauncher.cs:258` awaits `GetDefaultPlaceUrlAsync()` on the
   launch path. Deleting it moves legacy users from their saved place to Roblox home.
3. **F-085's "deliberate distinction" is carried by the defect.** The compat banner one Grid row above
   is themed and goes grey under flatline; Bloxstrap stays amber only because it ignores the theme.

**Read the site before building the row. Three of ten rows were wrong here.**

---

## Wave 1 — the surfaces a flatline screenshot shows

- [x] **1. The banner ruling, written down before anything moves**
  Spec ref: `spec.md > §1`, `§0.3`
  What to build: nothing yet. Record in the commit message the ruling and its precedent: **both
  banners take the one themed warning recipe; text and the `▲` glyph carry the difference, not hue.**
  Precedent is F-032 — `MutedText` vs `White` measured 1.00:1 under flatline, so colour could not
  carry "quiet" and weight took over. Same shape.
  Acceptance: the ruling is in the tree before the migration that depends on it.
  Verify: none. This is a decision item and it is deliberately its own commit.

- [x] **2. The Bloxstrap banner joins the vocabulary**
  Spec ref: `spec.md > §1`
  What to build: `MainWindow.xaml:1592,1593,1606` — `#3F3000` → `{DynamicResource RowExpiredBgBrush}`,
  `#8F7000` → `{DynamicResource RowExpiredAccentBrush}`, `#FFE3A6` → `{DynamicResource
  RowExpiredAccentBrush}`. Add the `▲ ` literal `Run` prefix the compat banner already carries.
  **Delete the "warm amber tone distinct from" comment** — it documents a decision this item reverses,
  and leaving it makes the next reader think the migration was a mistake.
  Acceptance (`prd.md > Story 1.1`): no colour literal remains in the block; the banner is legible in
  all four themes (already measured — 6.58 worst); the `▲` is present.
  Verify: run the app, force `BloxstrapWarningVisible`, look at it in **flatline and brand**. Then
  check the case that matters: **both banners visible at once.** They will look alike; confirm the
  text and glyph make them readable as two different warnings.

- [x] **3. History rows get a boundary that survives flatline**
  Spec ref: `spec.md > §3`
  What to build: `SessionHistoryWindow.xaml.cs:150-155`. Keep `Background = RowBgBrush`, add a
  bottom boundary. **The plain `DividerBrush` bind will not do** — measured 1.05-1.16 against 1.4.11's
  3.0 floor in every theme. **Derive it through `ContrastGuard.Ensure(surface, candidate)`**, the same
  path `InteractiveEdgeBrush` takes, so it clears 3:1 under any theme including ones users wrote.
  Acceptance (`prd.md > Story 1.3`): a row is distinguishable from its neighbour in all four themes,
  and the carrier is not fill alone. Record the derived ratios in the commit.
  Verify: open History under flatline with at least three sessions. **This is code-behind, so no
  XAML-reading gate sees it** — item 8 covers that.

- [x] **4. About: the ground gets themed, the logo does not**
  Spec ref: `spec.md > §2`, `§0.1`
  What to build: `AboutWindow.xaml:34` `Canvas Background` → `{DynamicResource RowBgBrush}` (**bind,
  do not remove** — the plate is a ground for a fixed-colour logo and a light user theme would expose
  its absence). `:96` `#15263A` → `{DynamicResource RowBgBrush}`. **Do not touch `:13-20`.**
  Acceptance (`prd.md > Story 1.2`): the eight artwork brushes are byte-identical in all four themes;
  the plate follows the theme; F-066's second site closes here.
  Verify: open About in all four themes. The logo must look the same in each. If it does not, the
  artwork was themed and the item is wrong.

- [x] **5. The artwork allow-list, so the next sweep does not undo item 4**
  Spec ref: `spec.md > §2`
  What to build: entries in `ThemedStatusColourTests`' `AllowList` for the eight About brushes, each
  with a written reason in the shape the caption-palette entries use. **Note the anchor-lookback
  constraint** — that file matches allow-list entries by searching 12 lines back for an anchor string,
  so keep the declarations compact and put explanations above them (`Converters.cs` carries the same
  warning after this bit the v1.20 cycle).
  Acceptance: the suite is green with the artwork in place and would fail if the brushes moved out of
  the allow-list.
  Verify: `dotnet test src/ROROROblox.Tests/ --filter "FullyQualifiedName~ThemedStatusColour"`.
  → **CHECKPOINT C1.** Every wave-1 surface is one a screenshot will show. Walk About, History and
  both banners in flatline and brand before starting wave 2.

---

## Wave 2 — the gate, the small ones, and the close-out

- [x] **6. F-086: the pairs the gate cannot see, and the failure it finds**
  Spec ref: `spec.md > §4`
  What to build: an **unconditionally measured** named-pair list in `ContrastPairGateTests`,
  independent of what the element scan finds: `MutedText` on `RowBg`, on `Bg`, on `Navy`.
  **It will go red: `MutedText` on `RowBg` is 4.19 in midnight.** That is a live shipped defect the
  gate was blind to. **Rule on it in this item** — either the pair is fixed (midnight's `MutedText` or
  `RowBg` moves) or it is exempted with a finding row and a floor at today's value, the same mechanism
  F-050 uses. **Do not lower the 4.5 floor.**
  Acceptance (`prd.md > Story 2.1`): three pairs measured every run, ratios recorded per theme, and
  the midnight failure has a decision attached.
  Verify: break a pair deliberately, confirm red naming the pair, restore. **F-050 does not close here.**

- [x] **7. F-087: the colour branch moves to XAML**
  Spec ref: `spec.md > §5`
  What to build: `ConsentSheet.xaml.cs:90-92`'s `NamespaceBrush` becomes a `Style` + `DataTrigger` on
  `IsHostEnforced` in `ConsentSheet.xaml`. Drop the `?? new SolidColorBrush(...)` literal fallbacks.
  Acceptance: no colour literal in the file; the sheet still distinguishes host-enforced capabilities.
  Verify: open the consent sheet for a plugin with both kinds of capability.

- [x] **8. The code-behind surfaces get a gate**
  Spec ref: `spec.md > §3`, `§6`
  What to build: an assertion that the History row carries a non-fill boundary, scanning `.cs` the way
  `ButtonRankFenceTests.NoCodeBehindButtonPaintsItself` does. F-098 established that a markup-only gate
  is evidence about markup only; item 3's change lives in code and would otherwise be unguarded.
  Acceptance: the gate fails if the boundary is removed.
  Verify: remove the boundary, confirm red, restore.

- [x] **9. F-093: the ruling, then the smallest change that honours it**
  Spec ref: `spec.md > §5`, `§0.2`
  What to build: **recommended option 2** — keep the read path in `RobloxLauncher`, delete
  `SetDefaultPlaceUrlAsync` from `IAppSettings` and the implementation, and correct
  `IAppSettings.cs:7`'s claim that "the Preferences dialog allows editing" (it does not, and has not
  for over a cycle). If a different option is taken, record why.
  Acceptance: no interface lies; nobody's launch target changes; `AppSettingsTests` still green.
  Verify: confirm `JsonOptions` (`AppSettings.cs:15`) does not set `UnmappedMemberHandling.Disallow`,
  then add a test that a legacy `settings.json` carrying `defaultPlaceUrl` round-trips.

- [x] **10. The copy rows**
  Spec ref: `spec.md > §5`
  What to build: **F-021** `GamesWindow.xaml:396` — "Use the Squad Launch toolbar button to add one"
  points at a closed window for something that saves itself; say what actually happens.
  **F-022** `MultiInstanceCopy.FpsCapMismatchBanner` — **re-read it first, it was rewritten since the
  register row.** Still ~45 words with the action last; lead with the action, same length or shorter.
  **F-074** `StopAllConfirmWindow.xaml:36` — "UNSAVED GAME STATE WILL BE LOST" is 10px mono uppercase
  at `#5A6982`, the dimmest text on the surface carrying the worst news; make it body prose.
  **F-070** `JoinByLinkWindow.xaml:27-33` + `WelcomeWindow.xaml:38-43` — half the cyan/magenta duo
  against 12 siblings that ship both; fix or close with evidence.
  Acceptance: each row's copy names the real mechanism; no row ships on its register text alone.
  Verify: read each string in the running app.

- [x] **11. Two rulings, no code**
  Spec ref: `prd.md > Story 2.3`
  What to build: a decision on **F-095** (crash fixed; is a log Warning enough, or is a user-visible
  surface owed?) and **F-098** (partly fixed; does `capture-ui.ps1` + the packaging scripts land here
  or wait?). Record both in the register with reasoning.
  Acceptance: both rows carry a decision and a date.
  Verify: neither row is left saying "open" with no reason.

- [x] **12. Documentation, security, and the numbers**
  Spec ref: `spec.md > §7`
  What to build: version `1.20.0.0` → `1.21.0.0` in `ROROROblox.App.csproj` and
  `Package.appxmanifest`, **lockstep**. Flip **F-063, F-066, F-085, F-087** to clean; record F-086's
  ruling; update **F-093**. Re-derive the register scoreboard **from the rows** — do not adjust the
  previous line, and check every row has 11 pipes before counting (two rows shipped malformed at v1.19
  and were invisible to the count). Update `docs/features.md` (move v1.21 out of "In flight" and fix
  the v1.20 line, which still says in-flight). Sync `CLAUDE.md`'s file table. **Archive `docs/spec.md`
  → `docs/superpowers/specs/2026-08-12-rororo-surface-vocabulary-design.md`** with a banner correction
  naming what was proposed vs built. Security pass: local-path grep over `src/`, `scripts/`,
  `.github/`; `dotnet list package --vulnerable --include-transitive`; secret scan.
  Acceptance: every flipped row carries evidence; versions lockstep; spec archived; scoreboard derived.
  Verify: `dotnet test ROROROblox.slnx` green.

---

## Checkpoints

**C1 (after item 5)** — every wave-1 surface is one the Store screenshots will show, and the recapture
follows this cycle. A regression found here is cheap; found in a submitted listing it is not.

## What this cycle must not do

- **Do not theme the About logo.** `spec.md > §0.1`. Eight brushes at `AboutWindow.xaml:13-20` are the
  mark, not chrome.
- **Do not delete `DefaultPlaceUrl` without ruling on the launch path.** `spec.md > §0.2`.
- **Do not add a theme slot.** Invariant 6 — every user theme on disk supplies ten and an eleventh
  breaks all of them. Derive instead.
- **Do not lower a contrast floor** to make a pair fit. Change the pair, or exempt it with a row and a
  recorded floor.
- **Do not close F-050.** Item 6 is its prerequisite, not its fix.
- **Do not start F-052** — borders, 60 of 76 controls, all 26 XAML files. Its own cycle.
- **Do not ship a gate that cannot be made to fail.**
- **Do not build a row from its register text.** Three of ten were wrong on this scope, two of them
  damagingly.
