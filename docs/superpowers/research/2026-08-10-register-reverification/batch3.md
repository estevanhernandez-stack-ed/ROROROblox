# Re-verification — batch 3 (F-058 … F-073)

Tree state: `main` @ `b474a2f` (Mon 2026-08-10 18:12 -0500), v1.17.0.0. Read-only pass.
No build, no test run, no repo file touched.

Method: located every citation by CONTENT first, then recorded today's line. Every count
re-derived with a XAML element-scanner (attribute-aware, handles multi-line tags) rather than
line greps, so `<Button.Style>` property elements and >200-char attribute lists don't skew it.
For F-068 the same scanner was run against three git snapshots so "direction" is measured, not
inferred.

---

### F-058 — ALREADY FIXED

- **Row claims:** `MainWindow.xaml:1353-1385` — streamer-mode toggle in column 0, explanation in
  column 2 right-aligned across a ~250px gap, duplicating the toggle's own ToolTip.
- **Checked:** `grep -n "ToggleSwitch\|Streamer" MainWindow.xaml` (app-wide too),
  `Preferences/PreferencesWindow.xaml:124-155`, `ViewModels/MainViewModel.cs:612`.
- **Found:** the band does not exist. `MainWindow.xaml` contains **zero** `ToggleSwitch`
  elements and zero streamer-mode band copy; the only streamer string left is the per-row
  *Reroll identity* context item at `:317`. The control was relocated to
  `PreferencesWindow.xaml:131-155` and **already ships in this row's own fix shape**: `CheckBox`
  at `:133-139`, helper prose directly beneath at `:140-144` with `Margin="22,4,0,8"` — the exact
  indent the fix direction names ("as Preferences already does (Margin=22,4,0,0)"). The tooltip
  duplication is gone: the toggle carries no ToolTip at all now, and the only ToolTip in the card
  belongs to the *Reroll all identities* button (`:153`) and says something different.
- **Artifact that fixed it:** F-008's relocation (register lists F-008 `clean`, verified against
  the tree 2026-08-04). `MainViewModel.cs:612` documents the ToggleSwitch→CheckBox swap and cites
  F-008 by id. `PreferencesWindow.xaml:125-130` carries the same citation in a comment.
- **Direction:** n-a — defect count 1 → 0.
- **Note:** this is an F-001-class row. It was never a wave's own batch item, so nothing flipped
  it. **Do not scope work against it.** Incidental: the same close-out kills F-042's premise too
  (see incidentals below).

---

### F-059 — PARTLY SHIPPED

- **Row claims:** `ui:ImageIcon` appears once (title bar); everything else is a literal text-run
  glyph; of six nav buttons only Settings carries one.
- **Checked:** `grep -rn "ImageIcon\|SymbolIcon\|ui:Icon" --include=*.xaml`, the nav band at
  `MainWindow.xaml:1143-1211`, entity-escape glyph census across all app XAML.
- **Found, clause by clause:**
  - *"six nav buttons, only Settings carries one"* — **dead.** The band holds **two** buttons:
    `Settings` (`MainWindow.xaml:1144`) and `ToolsDropDownButton` (`:1162`). Neither carries an
    icon. The gear glyph was deleted by F-012 (`clean`); six→two was F-009 (`clean`). The row's
    verdict sentence — "a single decorated button in a row of six reads as an accident" —
    describes an app that no longer exists.
  - *"`ui:ImageIcon` appears once"* — still true, `MainWindow.xaml:1041` (title bar).
  - *"everything else is a literal text-run glyph"* — **no longer strictly true.** A second
    WPF-UI icon source shipped post-audit: `Icon="{ui:SymbolIcon Filter24}"` on the account filter
    box at `MainWindow.xaml:1688`.
  - The general defect survives: **13 entity-escaped glyph sites across 8 distinct codepoints**
    (`&#x2606;`x3, `&#x2630;`x2, `&#x2605;`x2, `&#x25BE;`x2, `&#x25B6;`, `&#x21BB;`,
    `&#x1F4CB;`, `&#x00D7;`) plus raw-text glyphs, against 1 `ImageIcon` + 1 `SymbolIcon`. No
    single icon source, no contract.
- **Direction:** nav-band instance shrank 6→2 buttons and 1→0 icons (fixed). Icon *sources* grew
  1 → 2.
- **Note:** the surviving half is a cross-surface vocabulary claim, not the nav-band claim the
  evidence column argues. Its twin **F-056** (not in this batch) is worse off — it still cites
  `MainWindow.xaml:1020` for a gear glyph that F-012 deleted, and repeats the "six peer nav
  buttons" framing verbatim. Same staleness, unflagged.

---

### F-060 — ACCURATE

- **Row claims:** `MainWindow.xaml:9-11` hardcodes `Height=600 Width=900`, `CenterScreen`;
  nothing reads/writes placement; compact-restore fallbacks unreachable in normal operation.
- **Checked:** `MainWindow.xaml:1-20`; `grep -rn "WindowState\|RestoreBounds\|Placement"` across
  `src/ROROROblox.App`; `SettingsBlob` at `src/ROROROblox.Core/AppSettings.cs:473`;
  `MainWindow.xaml.cs:58-90`.
- **Found:** all three clauses hold.
  - `MainWindow.xaml:10` `Height="600" Width="900"`, `:11` `MinHeight="400" MinWidth="860"`,
    `:12` `WindowStartupLocation="CenterScreen"`. Citation drifted `:9-11` → `:10-12`.
  - Zero placement persistence anywhere. The only `WindowStartupLocation` writes in code are
    four modal owner-resolution sites setting `CenterScreen`/`CenterOwner`
    (`WelcomeWindow.xaml.cs:92`, `EdgeRemediationWindow.xaml.cs:100`,
    `LaunchHeadroomWindow.xaml.cs:80`, `StopAllConfirmWindow.xaml.cs:51`, `JoinRequestWindow.xaml.cs:66`).
    `SettingsBlob` carries no width/height/left/top/maximized field.
  - Fallbacks confirmed unreachable: `MainWindow.xaml.cs:86-89` reads
    `_expandedWidth > 0 ? _expandedWidth : 780`, and `:64-67` always populates those fields
    before compact is entered.
- **Direction:** same.
- **Note:** the row pairs itself with F-033's compact flag ("alongside the QF-7 compact flag").
  F-033 is still `open` and its premise (`MainViewModel.cs:663` plain `SetField`, nothing writes
  it to disk) was not in this batch — verify it before pairing the two.

---

### F-061 — ACCURATE (citations drifted, count grew)

- **Row claims:** three casings of one action on screen at once — `CookieCaptureWindow.xaml:19`
  "Add Roblox Account" (Title Case), `:6` "Add Roblox account — log in" (sentence case),
  `MainWindow.xaml:1000` "+ Add Account".
- **Checked:** `CookieCapture/CookieCaptureWindow.xaml`, `MainWindow.xaml`, `About/WelcomeWindow.xaml`.
- **Found:** all three strings verbatim intact.
  - `CookieCaptureWindow.xaml:6` `Title="Add Roblox account — log in"` — **exact, no drift.**
  - `CookieCaptureWindow.xaml:19` `Text="Add Roblox Account"` — **exact, no drift.**
  - `MainWindow.xaml:1097` `Content="+ Add Account"` — citation drifted `:1000` → `:1097`.
  - **Two sites the row never counted**, both Title Case, both post-dating nothing (they mirror
    the button string): `MainWindow.xaml:1707` "Click + Add Account to log in to your first
    Roblox account." (empty state) and `About/WelcomeWindow.xaml:61` "Click + Add Account."
    (welcome tour, step copy).
- **Direction:** **grew — 3 → 5 user-facing sites**, still 3 distinct casings.
- **Note:** *"all visible during the same task"* is a rendering claim — **CANNOT VERIFY** without
  running the app. The strings themselves are all present and the two new sites both reference the
  button by its literal Title-Case label, so a rename now touches 5 strings, not 3.

---

### F-062 — DRIFTED (citation moved 49 lines; defect intact)

- **Row claims:** `PreferencesWindow.xaml:52` "Adds a value under HKCU Run. Removes it when
  unchecked." — first helper line of the page, only string assuming Windows-internals literacy;
  first clause duplicates the checkbox label above it.
- **Checked:** `Preferences/PreferencesWindow.xaml:85-121`, full-page read for internals jargon.
- **Found:** string verbatim at **`PreferencesWindow.xaml:101`** (was `:52`) — drifted +49 lines,
  because the 5-page nav rail (F-002/F-003, both `clean`) added a `ListBox` rail and a
  `ScrollViewer`/`Grid` wrapper above it. It is still the first helper line of the first card of
  the first page (`PageStartup`, `:87-103`), so the "first read" framing survives the rename.
  Checkbox label above it is `:95` "Start RoRoRo when Windows starts."
- **Direction:** same (1 site).
- **Note:** the row's sub-claim *"first clause duplicates the checkbox label above it"* does not
  hold literally — "Adds a value under HKCU Run" and "Start RoRoRo when Windows starts" share no
  wording. The duplication is semantic, not textual. The load-bearing half (registry path as
  helper copy for a non-technical clan audience) is intact and unambiguous.

---

### F-063 — ACCURATE (all three citations resolve exactly)

- **Row claims:** `AboutWindow.xaml:13-20` 8 literal `SolidColorBrush` resources; `:34` unbound
  canvas; `:96` `#15263A` = RowBgBrush's literal value, not bound.
- **Checked:** `About/AboutWindow.xaml:1-110`; `ThemedStatusColourTests.cs:298-305` (the fence's
  allow-list entry for this file); `ThemeStore.cs:251-267` (shipped `flatline` slot values).
- **Found:** every citation is **still exact, zero drift**.
  - `:13-20` — eight `<SolidColorBrush x:Key=…>` literals (`CyanBrightBrush`, `CyanDimBrush`,
    `CyanShadowBrush`, `MagentaDimBrush`, `MagentaShadowBrush`, `NavySoftBrush`, `TealBrush`,
    `TealDeepBrush`). Count **8**, same.
  - `:34` — `<Canvas Width="64" Height="64" … Background="{StaticResource NavySoftBrush}">`.
    Still a fixed `#0F1F31` fill, still theme-unreachable.
  - `:96` — `Background="#15263A"` on the description card Border. Exact, unbound.
- **Direction:** same.
- **Note:** the fix direction is still executable as written, and the fence at
  `ThemedStatusColourTests.cs:298-305` already encodes the split the row asks for (§7 owns the
  logo tints at `:13-20` and the magenta glow at `:89`; F-063 owns the rest). **The flatline
  clause got stronger, not weaker.** Shipped `flatline` is `Bg #101010` — the `:34` canvas paints
  a `#0F1F31` navy square onto a near-black achromatic field, which is more visible than under
  brand where the canvas literal *equals* `BgBrush`. The on-screen read is **CANNOT VERIFY**
  (no app run), but the arithmetic is unambiguous.

---

### F-065 — ACCURATE

- **Row claims:** `SessionHistoryWindow.xaml.cs:150-155` rows built on `RowBgBrush` alone, vanish
  under flatline; the date-group heading (10px SemiBold Cyan) does survive.
- **Checked:** `History/SessionHistoryWindow.xaml.cs:129-299`, `History/SessionHistoryWindow.xaml`,
  `ThemeStore.cs:251-267`.
- **Found:** citation resolves almost exactly. The row `Border` is built at
  **`:150-156`**, `Background = (Brush)FindResource("RowBgBrush")` at **`:155`**, and it sets
  `Margin`/`Padding`/`CornerRadius` and **no `BorderBrush` at all**. Fill is the only boundary.
  The bucket header survives at `:129-136` (10px, `FontWeights.SemiBold`, `CyanBrush`) exactly as
  the row's own correction states.
- **Direction:** same.
- **Note two things before scoping.**
  1. **"Vanish under flatline" belongs to `flatline-lab`, not the shipped theme.** The register's
     own *Notes carried forward* correction (2026-08-10) says to read "flatline" in the audit rows
     as the adversarial fixture. Shipped `flatline` has `Bg #101010` / `RowBg #2A2A2A` = **1.33:1**
     — not 1.00:1. The rows do not literally disappear on the theme that ships; the boundary is
     just far below any legibility floor, in every theme (brand is 1.09:1). The defect is intact;
     the word "vanish" is not.
  2. **This row is named by the flatline note.** Its lens id is **VC-10**, one of the three
     (VC-8, VC-9, VC-10) the note says over-claimed disappearance. The row already absorbed that
     correction inline, so there is no unrecorded supersession — but a reader skimming the
     evidence column will over-weight the flatline half.

---

### F-066 — ACCURATE on its headline count; still undercounts at value level

- **Row claims:** 62 hardcoded hex duplicate an existing brush, mostly out-of-scope modals;
  in-scope residue is the mutex-recovery banner (reconciled to `MainWindow.xaml:1572,1575` plus a
  third at `:1568`) and `AboutWindow.xaml:96`.
- **Checked:** full hex-literal census across all app XAML, matched against `App.xaml`'s ten brush
  values; `MainWindow.xaml:1563-1605`; `ThemedStatusColourTests.cs:283-320` and its ceiling.
- **Found:**
  - **The 62 is exact.** Outside `App.xaml` the tree holds **86** hex literals in 10 files, of
    which **62** duplicate an `App.xaml` brush value. Unchanged from the audit number.
    (Corroboration: the fence's own ceiling is 97 = 86 + App.xaml's 11 — my independent count
    lands on the same 97.)
  - **The reconciled citations resolve exactly.** `MainWindow.xaml:1568`
    `Foreground="#F1B232"`, `:1572` `Background="#17D4FA" Foreground="#0F1F31"`, `:1575`
    `Background="#22314A" Foreground="#FFFFFF"`. `AboutWindow.xaml:96` `#15263A` exact.
  - **The row still undercounts its own residue, one level down.** "Three literals" is a *line*
    count. Those three lines carry **five hex values**. The fence already records this
    (`ThemedStatusColourTests.cs:286` — "Five hex occurrences on three lines"); the register row
    does not.
  - **One of the five maps to no brush key at all.** `#22314A` (the secondary recovery button's
    fill) is not any `App.xaml` slot. It is also the app's most-repeated orphan hex — **7
    hand-copied button sites** carry the `#22314A`/`#FFFFFF` recipe.
- **Direction:** headline count **same** (62). In-scope residue: stated as 3, actually **5**
  values on 3 lines.
- **Note:** the fix direction — "Replace the three in-scope literals with their existing brush
  keys" — **cannot be executed as written** for `#22314A`, because no existing key holds that
  value. That is a decision (which slot, or a new one), the same shape as F-089's resting-ring
  call, and it is not currently named anywhere. Budget for it.

---

### F-067 — DRIFTED (stack sites grew; one site never counted)

- **Row claims:** mono role declared two ways — fallback stack (**16** sites) vs bare "Consolas"
  (**7** sites, 3 in-scope); `RenameWindow.xaml:27` comment confirms the stack is the intended
  contract; **zero** display-family declarations app-wide.
- **Checked:** `grep -rn FontFamily` across all app XAML + CS; `Modals/RenameWindow.xaml:24-34`;
  `grep -rn "Space Grotesk\|MonoFontFamily\|DisplayFontFamily"`.
- **Found:**
  - Fallback stack `"JetBrains Mono, Cascadia Mono, Consolas"`: **19 XAML sites** — MainWindow
    `:1258,:1447`; EdgeRemediation `:58,:74`; JoinRequest `:40`; LaunchHeadroom `:44`; Rename
    `:31`; RobloxAlreadyRunning `:64`; StopAllConfirm `:37`; ConsentSheet `:30,:61,:90`;
    PluginsWindow `:131,:191,:213,:225,:298,:321`; ExportAccounts `:126`. **Grew 16 → 19 (+3).**
  - Bare `"Consolas"`: **7 XAML sites** — GamesWindow `:144,:356`; SquadLaunch `:46,:47`;
    CaptionColorPicker `:51`; ThemeBuilder `:69`; ImportAccounts `:48`. **Same, 7.**
    **Plus one site the row never counted:** `Diagnostics/DiagnosticsWindow.xaml.cs:121`
    `FontFamily = new FontFamily("Consolas")` — same defect class as F-047/F-064's "a third spec
    hiding in C# where no markup review would see it". **8 total.**
  - `RenameWindow` comment: at **`:26-28`** today (row cited `:27`); it does confirm the stack.
  - **Zero display-family declarations confirmed** — no `"Space Grotesk"` anywhere in
    `src/ROROROblox.App`. Also confirmed: no `MonoFontFamily` resource exists, so the fix
    direction is 0% shipped.
- **Direction:** stack sites **grew** (16 → 19); bare Consolas **same in XAML** (7), **grew to 8**
  counting the C# site; display family **same** (0).
- **Note:** the "3 in-scope" sub-count is an adjudication (which of Games / SquadLaunch /
  ThemeBuilder / CaptionColorPicker / Import counts as settings-nav-chrome), not a measurement.
  I did not re-adjudicate it. The raw counts above are the measurable part.

---

### F-068 — PARTLY SHIPPED  ★ the row that most changes the picture

- **Row claims:** 115 Button declarations, ~73 set `Background` inline; only keyed button style
  app-wide is `CyanCtaButton`; `App.xaml`'s only control style is a `ToggleButton` style.
  Fix direction: "Define PrimaryButtonStyle/SecondaryButtonStyle/TertiaryButtonStyle in App.xaml
  with template-level triggers bound to DynamicResource brushes."
- **Checked:** `Controls/ControlStyles.xaml` (full read), `App.xaml:1-95`, an attribute-aware XAML
  element census over every app `.xaml`, run against **three snapshots** — `bf1fdef^`
  (pre-wave-5), `bf1fdef` (the commit that created `ControlStyles.xaml`, 2026-08-05), and `HEAD`.
  Also `Rendering/RenderedStyleGateTests.cs:43-57`, `Plugins/PluginsWindow.xaml:23`.

**What is in the tree**

`Controls/ControlStyles.xaml` ships **7 keyed styles + 1 implicit**, merged into `App.xaml` at
`App.xaml:15` (deliberately after `ui:ControlsDictionary`, since the `BasedOn` is a parse-time
`StaticResource`):

| style | kind | what it sets |
|---|---|---|
| `PrimaryButtonStyle` `:23` | keyed | Navy fill, White text, **Cyan** edge, 1px |
| `SecondaryButtonStyle` `:49` | keyed | Navy fill, White text, Normal weight, **InteractiveEdgeBrush** |
| `SecondaryStrongButtonStyle` `:65` | keyed | same + SemiBold |
| `AppTextBoxStyle` `:91` | keyed | White fg, derived edge, no fill (deliberate) |
| `AppPasswordBoxStyle` `:99` | keyed | same |
| `ComboBox` `:111` | **implicit** | Navy fill, White fg, derived edge |
| `CardBorderStyle` `:131` | keyed | RowBg, radius 8, padding 14 |
| `SectionHeadingStyle` `:143` | keyed | 13px SemiBold White |

**How much of F-068 is shipped**

- Three button styles exist under two of the three names the row asks for. The third is
  `SecondaryStrongButtonStyle`, not `TertiaryButtonStyle` — a different decomposition, and the
  file argues the case at `:58-64` (the sweep found three recipes, not two).
- Location differs from the row: `Controls/ControlStyles.xaml`, merged into `App.xaml`, **not
  written into `App.xaml`.** The row's literal clause *"App.xaml's only control style is a
  ToggleButton style"* is **still true of `App.xaml` itself** — `SelectionDotStyle` at
  `App.xaml:61` remains the only `<Style>` in that file. Anyone re-reading the row without
  opening `Controls/` will conclude nothing shipped. That is the trap.
- `CyanCtaButton` **still exists** (`Plugins/PluginsWindow.xaml:23`, 4 call sites), so the clause
  "only keyed button style app-wide is CyanCtaButton" is now false in the other direction: there
  are 7 shared keyed styles *plus* the local one, which nothing has retired.
- **The "template-level triggers" half is 0% shipped.** All three button styles are setter-only,
  `BasedOn` WPF-UI's implicit `Button` style. `ControlStyles.xaml` contains **zero**
  `<ControlTemplate>` and **zero** `<Style.Triggers>`. Hover and pressed fills still come from
  WPF-UI's own dictionary, which `ThemeService` never touches — the identical residue the register
  already records for wave 3's `ContextMenu` items.

**The re-counted call-site number**

| snapshot | `<Button>` elements | on a shared style | **hand-copied** | files |
|---|---|---|---|---|
| `bf1fdef^` — pre-wave-5 | 101 | 0 | **96** | 24 |
| `bf1fdef` — 2026-08-05, the commit that wrote the "63/15" comment | 103 | 34 | **61** | 24 |
| `HEAD` — v1.17 | 105 | 36 (39 incl. `BasedOn` indirection) | **61** | **24** |

"Hand-copied" = a `<Button>` that is not on one of the seven shared styles and sets at least one
of `Background`/`Foreground`/`BorderBrush`/`BorderThickness` inline.

> **The number: 61 un-migrated button call sites across 24 files.
> Direction: FLAT. 61 on 2026-08-05, 61 on 2026-08-10.**

Wave 5 did the whole migration in one shot (96 → 61, 35 buttons) and **nothing has moved since**.
Two new buttons were added in those five days and both landed on a shared style, so the leak is
not spreading — but the backlog has not shrunk by one.

**What the `ControlStyles.xaml` comment's "63 hand-copied attribute sets across 15 files" is**

It does not reproduce as a buttons-only count at either snapshot (96 pre / 61 post) and the file
count matches neither (24 at both). It is closest to the post-wave-5 hand-copied button count
(61, off by 2) with a file count that is off by 9. **Treat "63 across 15 files" as unsourced.**
The reproducible number today is **61 across 24 files**, and the same scanner run over buttons +
text boxes + password boxes + borders gives **121 hand-copied element sites across 26 files**
(down from 177 pre-wave-5 and 144 post-wave-5 — inputs are now fully migrated at 11/11 TextBox
and 3/3 PasswordBox; **Borders are 60 of 76 still hand-themed**).

**What the 61 actually are** — this is the scoping shape:

| recipe | sites |
|---|---|
| `Background={DynamicResource CyanBrush}` + `Foreground={DynamicResource NavyBrush}` | **22** |
| `MagentaBrush` + `WhiteBrush` | **8** |
| `#22314A` + `#FFFFFF` (raw hex) | **7** |
| `#17D4FA` + `#0F1F31` (raw hex) | **6** |
| `RowExpiredAccentBrush` + `NavyBrush` | 3 |
| `BgBrush` + `#FFFFFF` + `#5A6982` | 3 |
| everything else | 12 |

Attribute-set shape: 51 of the 61 set exactly `Background`+`Foreground`+`BorderThickness`;
8 set all four. Distribution by file: `MainWindow.xaml` **19**, then
`RobloxAlreadyRunningWindow` 4, `PreferencesWindow` 4, `LeftoverProcessesWindow` 3,
`PluginsWindow` 3, and a long tail of 1-2 across 19 more files.

- **Direction:** button declarations **shrank** 115 → 105 element declarations (114 counting
  `<Button.…>` property-element tags, + 5 buttons constructed in C#). `Background` set inline
  **shrank ~73 → 61**. Shared styles **grew 0 → 7**. Un-migrated call sites **flat at 61 since
  2026-08-05**.
- **Note — three other rows depend on this and two of them depend on a part that is 0% built.**
  - **F-085** (open) says its fix "belongs with F-068's shared button/**banner** style work."
    There is **no banner style in `ControlStyles.xaml`** and no banner primitive anywhere. That
    dependency is 0% shipped, not partially.
  - **F-090** (open) says its fix "belongs with F-068's shared-style work, which already owns
    `ControlStyles.xaml`'s vocabulary." That one is true — the derived edge lives in three styles
    there, so a surface-aware derivation lands in one place.
  - **F-089** (closed 2026-08-10) noted F-068's three-button fix "would not reach a `ToggleButton`
    template" — correct, and still correct: `SelectionDotStyle` sits in `App.xaml`, outside the
    dictionary.
  - The single biggest un-styled recipe (22 cyan CTAs) has **no shared style proposed by this row
    at all** — Primary/Secondary/Tertiary as written doesn't name a filled-accent CTA, and
    `CyanCtaButton` covers 4 of the 22.
  - `RenderedStyleGateTests.cs:43-48` already states the scope honestly: "Seven keyed styles have
    been extracted so far. Everything not yet migrated is invisible here — including all 13
    magenta CTAs." A green gate run is not coverage of the 61.

---

### F-069 — SUPERSEDED

- **Row claims:** `MainWindow.xaml:302` `Header="🎲 Reroll identity"` — the sole emoji hit; fix
  direction "Header='Reroll identity' — drop the glyph."
- **Checked:** `MainWindow.xaml:310-325`; `Theming/ThemeBuilderWindow.xaml:43`; the register's
  **Rulings by the user** section.
- **Found:** the glyph is intact, at **`MainWindow.xaml:317`** (was `:302`, drifted +15). The
  companion at `ThemeBuilderWindow.xaml:43` (`&#x1F4CB; Copy AI prompt`) is also intact — the row
  cited `:52` for it via F-040.
  **But the ruling closes this class.** Verbatim, from *Rulings by the user (these override the
  register)*:
  > **The two shipped emoji stay.** Este, 2026-08-04, on approving this register: the die on
  > `Reroll identity` is thematic to streamer mode's deliberately silly naming, not decoration,
  > and the same goes for the clipboard on `Copy AI prompt`. **Any finding whose fix is "delete
  > the emoji" is closed as ruled, not open.**

  F-069's fix direction is, word for word, "drop the glyph." **F-040 was closed as ruled on
  exactly this basis** — and the reconciliation pass says F-040 "sat open for five days against a
  decision already made because nobody re-checked it." F-069 is the same row wearing a different
  lens id (QF-17 vs CV-10) and has now sat open six days longer than F-040 did.
- **Direction:** n-a.
- **Note:** the ruling also disposes of the residual: *"What those findings were really reacting
  to is **placement** … and that is already covered by the goal-1 relocation work."* F-008's
  close-out already ruled on this specific item — "the only streamer string left on MainWindow is
  the per-row *Reroll identity* context item, which is a per-account action, not the band the
  finding was about." **Nothing is left of F-069. Flip it, do not scope it.**

---

### F-070 — ACCURATE (defect intact; its fix-direction premise is now falsified)

- **Row claims:** `JoinByLinkWindow.xaml:27-33` cyan+white, no magenta, vs 12 siblings pairing
  Cyan/Magenta/White; `WelcomeWindow.xaml:38-43` also ships half the duo. Fix: "Resolved for free
  by CV-1's chrome rule."
- **Checked:** both header blocks; `Controls/PageHeader.xaml` (full); every `controls:PageHeader`
  call site.
- **Found:** both breaches intact, citations off by 1-2 lines.
  - `JoinByLinkWindow.xaml:26-33` — `"Join by link"` in `CyanBrush` + `AccountSubtitle` in
    `WhiteBrush`. **No magenta separator.**
  - `About/WelcomeWindow.xaml:36-43` — `"Welcome to "` in `WhiteBrush` + `"RoRoRo"` in
    `CyanBrush`. **No magenta.**
- **Direction:** same (2 windows).
- **Note — this is the load-bearing correction.** The fix direction says the row resolves *for
  free* once CV-1's chrome rule lands. **CV-1 landed. F-004 and F-007 are both `clean`, and
  `PageHeader` shipped with the magenta `" / "` hard-coded into it (`Controls/PageHeader.xaml:41`,
  commented "never one without the other (invariant 2)").** It is adopted by **11 windows**:
  Diagnostics `:25`, FriendFollow `:24`, Games `:312`, History `:24`, MainWindow `:1078`, Plugins
  `:67`, Preferences `:63`, SquadLaunch `:27`, ThemeBuilder `:23`, Export `:28`, Import `:26`.
  **`JoinByLinkWindow` and `WelcomeWindow` are two of the four windows that did not adopt it**
  (the others being `AboutWindow` and the modals). So the row's "resolved for free" is measurably
  false: the free fix shipped, skipped these two, and the row is exactly the residue. Scope it as
  standalone work — two windows adopting `PageHeader` — not as a downstream of a chrome rule.

---

### F-071 — DRIFTED (count grew; the row's own arithmetic never added up)

- **Row claims:** "Multi-Instance" surfaces in **8** places (tray tooltip x3, tray menu x3,
  `:203`, Diagnostics row + bundle line), not the 2 originally claimed.
- **Checked:** `grep -rn "Multi-Instance\|Multi-instance\|multi-instance"` across
  `src/ROROROblox.App` and `src/ROROROblox.Core`, then hand-classified user-facing vs comment/doc.
- **Found:** every cited site resolves — `TrayService.cs:98,:99,:100` (tooltip, three states),
  `:105,:106,:107` (menu label, three states), `:203` (menu item construction),
  `DiagnosticsWindow.xaml.cs:50` (row label), `:228` (bundle line).
  **That enumeration is nine items, and the row's headline says eight.** The row's own count was
  internally inconsistent from day one.
  **Plus a tenth the row never counted:** `About/WelcomeWindow.xaml:102` — "Multi-Instance is on
  by default — Roblox windows open side by side without fighting each other for the singleton."
  It is user-facing prose in the welcome tour and it dates to **2026-05-04** (`82462d2`), so it
  predates the audit. The row undercounted from the start.
  Widening to the lowercase prose variants a rename would also have to touch —
  `RobloxAlreadyRunningWindow.xaml:35,:66`, `MultiInstanceCopy.cs:9`, `LeftoverSummary.cs:17`,
  `RobloxCompatChecker.cs:106` — the surface is **15 user-facing strings**.
- **Direction:** **grew** — 8 claimed → **10** exact-form `Multi-Instance` user-facing sites
  (**15** including lowercase prose variants).
- **Note:** the row's verdict already says the finding "underprices" the rename as a product
  decision. It underprices it by more than it says: 10, or 15, not 8. If this ever gets picked up,
  the price is a copy sweep plus three-state tray tooltip/menu strings, not a find-and-replace.

---

### F-072 — DRIFTED (structural claim holds; interactive-surface claim is now false)

- **Row claims:** 503 unpaired Text nodes, 100 sessions, zero DataItem/List containers; **only two
  real focus stops (Clear history, Close), both terminal**; "its total lack of interactive surface
  is placement evidence for QF-19's tool-not-preference conclusion."
- **Checked:** `History/SessionHistoryWindow.xaml` (full, 68 lines),
  `History/SessionHistoryWindow.xaml.cs:100-300`, `SessionHistoryStore.cs:14`,
  `grep -rn "ItemsControl\|ListView\|ListBox\|DataItem\|AutomationProperties" History/`.
- **Found:**
  - **Structural claim intact.** `HistoryList` is a bare `<StackPanel>` (`SessionHistoryWindow.xaml:35`)
    inside a `ScrollViewer`; rows are `Border`s appended in code-behind at `:125`. **Zero**
    `ItemsControl`, `ListView`, `ListBox` or `DataItem` anywhere in `History/`. **Zero**
    `AutomationProperties` — app-wide there are only two, both on the Tools button
    (`MainWindow.xaml:1159,:1162`). Every field is a loose `TextBlock`.
  - **"Only two real focus stops, both terminal" is FALSE.** A per-row `+ Bookmark` **Button** is
    constructed at `SessionHistoryWindow.xaml.cs:277-293` — one per session whose `PlaceId` is not
    already saved, with a `Click` handler, `Cursor.Hand` and a ToolTip. History now has 2 + N
    focus stops, and it has real interactive surface. Rows whose place *is* saved get a "Saved"
    `TextBlock` instead (`:265-273`).
  - `MaxRows = 100` confirmed at `SessionHistoryStore.cs:14`, so "100 sessions" is the cap and
    still right.
  - Per-row text-node shape today: 4 minimum (name, detail, time, duration) + 1 if
    `IsPrivateServer` (the PRIVATE badge, `:212`) + 1 if saved, plus one bucket header per date
    group.
- **Direction:** focus stops **grew** 2 → 2+N. Text-node count is data-dependent.
- **Note:** **the 503 is not statically re-derivable** — it depends on how many of the 100 stored
  sessions are private servers, already-bookmarked, and how many date buckets they fall into. I did
  not reproduce it and I am not asserting it. Call that sub-claim **CANNOT VERIFY** without running
  the window against a populated store. Separately, the *placement-evidence* half of the verdict is
  spent: F-001 is `clean` and History sits in the Tools menu (`MainWindow.xaml:1172`), so the
  argument this row was carrying has already been cashed. What remains is a plain a11y row.

---

### F-073 — ACCURATE (defect intact; count grew to two banners)

- **Row claims:** UIA tree shows Button 'Dismiss' declared before the banner's own text; visual
  right-alignment via `DockPanel.Dock` means a sighted user sees nothing wrong.
- **Checked:** `MainWindow.xaml:1578-1647`; `grep -n "Dismiss\|DockPanel"` across `MainWindow.xaml`.
- **Found:** intact and now doubled.
  - **FPS-cap banner** (`MainWindow.xaml:1586-1603`): `<DockPanel>` at `:1586`, `<Button
    DockPanel.Dock="Right" Content="Dismiss">` at **`:1588-1598`**, `<TextBlock
    Text="{Binding FpsCapWarningText}">` at **`:1599-1602`**. Button first in markup, text second.
    No `AutomationProperties.Name`.
  - **Bloxstrap banner** (`MainWindow.xaml:1635-1646`): identical shape — `<DockPanel>` `:1635`,
    `Dismiss` button `:1636-1640`, `<TextBlock>` `:1641-1645`. Also unnamed. The markup at
    `:1583-1585` explicitly says the FPS banner is "same shape as the Bloxstrap banner below."
- **Direction:** **grew — 1 → 2** banners with `Dismiss`-before-text and no accessible name.
- **Note:** the UIA tree order itself is a runtime observation — **CANNOT VERIFY** without running
  the app or a UIA capture. What is verifiable is the markup declaration order and the absence of
  `AutomationProperties.Name`, and both hold. The fix direction already says "name what each
  Dismiss button dismisses" (plural), so the shape of the fix is unchanged; the size doubled.

---

## Batch summary

| verdict | rows |
|---|---|
| **ACCURATE** (6) | F-060, F-061, F-063, F-065, F-070, F-073 |
| **DRIFTED** (4) | F-062, F-067, F-071, F-072 |
| **PARTLY SHIPPED** (2) | F-059, **F-068** |
| **ALREADY FIXED** (1) | **F-058** |
| **SUPERSEDED** (1) | **F-069** |
| **CANNOT VERIFY** (0 whole rows) | — but four rows carry an unverifiable sub-claim: F-061 (simultaneity on screen), F-063 (flatline rendering), F-072 (the 503 count), F-073 (UIA tree order). |

**Two rows should not be scoped at all.** F-058 shipped with F-008 and nothing flipped it.
F-069 was closed by the 2026-08-04 ruling on the same day the register was approved — it is F-040's
twin and has outlived it. That is **2 of 15 rows, 13%, describing an app that does not exist** —
the same rate the 2026-08-09 reconciliation found.

**F-068 is the row that most changes the picture, in both directions.** More has shipped than the
row says (7 keyed styles, 3 of them buttons, in a file the row does not name; 36-39 buttons
migrated; text inputs 11/11 done) and less has shipped than the tree's own comment implies (the
template-trigger half is untouched, the banner primitive three other rows depend on does not
exist, and the biggest single recipe — 22 cyan CTAs — has no style proposed for it). **The number
to scope against is 61 un-migrated button call sites across 24 files, flat since 2026-08-05.**

**Worst drift: F-071**, whose headline count (8) contradicts its own enumeration (9), missed a
tenth site that has been in the tree since 2026-05-04, and prices a rename at roughly half its
real surface (15 user-facing strings).

**Most consequential single correction: F-070.** Its fix direction says it "resolves for free"
once CV-1's chrome rule lands. CV-1 landed, `PageHeader` shipped with the magenta separator baked
in, 11 windows adopted it, and these two did not. Anyone reading that fix direction will assume
the row is a no-op. It is standalone work.

**Counts that moved:** F-067 stack sites 16 → **19** (grew). F-071 8 → **10** (grew, 15 widened).
F-061 3 → **5** sites (grew). F-073 1 → **2** banners (grew). F-072 focus stops 2 → **2+N** (grew).
F-068 hand-copied buttons ~73 Background-inline → **61** (shrank), un-migrated sites **flat at 61**.
F-066 62 → **62** (same, but 3 stated in-scope literals are really **5** values).
F-059 nav buttons 6 → **2** (shrank), icon sources 1 → **2** (grew).
F-063, F-060, F-065, F-070, F-062 — **same**.

## Incidental (outside this batch, not audited — flagged only)

- **F-042** (`open`) claims "`MainWindow.xaml:1360` Streamer-mode is the only `ui:ToggleSwitch`;
  all 6 Preferences booleans are CheckBox." The app now contains **zero** `ToggleSwitch` controls —
  the only one was converted to a `CheckBox` when F-008 relocated it
  (`PreferencesWindow.xaml:127-129` and `MainViewModel.cs:612` both document the swap by finding
  id). The row's premise — two mutually exclusive boolean representations — no longer exists. Its
  2026-08-09 reconciliation updated the CheckBox count (6 → 7, and 7 is correct today) **but did
  not notice the ToggleSwitch was gone.** Strong ALREADY-FIXED candidate.
- **F-056** (`open`) still cites `MainWindow.xaml:1020` for the gear glyph that F-012 (`clean`)
  deleted, and repeats the "six otherwise-identical peer nav buttons" framing that F-009 (`clean`)
  collapsed to two. Same staleness as F-059's first clause, unrecorded.
- **F-052** (`open`) claims "grep for `AutomationProperties|FocusVisualStyle|TabIndex|
  KeyboardNavigation|IsTabStop` across src/ returns zero." It returns **two** today, both
  `AutomationProperties.Name` on the Tools button (`MainWindow.xaml:1159` comment, `:1162`
  attribute). Directionally the row still holds — 2 of ~105 buttons — but the stated zero is wrong.
- **Lens-id collision hazard.** The register uses `VC-` (visual) and `CV-` (copy) prefixes that
  differ by transposition. `VC-14` is Absorbed and `VC-19` is Refuted; **F-070 is `CV-14`** and
  **F-062 is `CV-19`**. Neither is absorbed or refuted. Anyone matching lens ids by eye will
  mis-close one of these two.
- **Fence corroboration:** my independent hex census landed on **97** allowed literals, exactly the
  ceiling asserted at `ThemedStatusColourTests.cs:340`. The fence and the tree agree.
