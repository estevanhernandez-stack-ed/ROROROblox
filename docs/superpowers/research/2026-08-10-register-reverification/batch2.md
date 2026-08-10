# Re-verification — findings register batch 2

Repo `<repo root>`, branch `main`, `git describe` = `v1.17.0.0`, HEAD `b474a2f`.
Read-only pass. No repo file edited. No build or test run (three parallel audits; `obj/`/`bin/` lock risk).

Rows verified: F-041 F-042 F-043 F-044 F-045 F-046 F-048 F-050 F-051 F-052 F-053 F-054 F-055 F-056 F-057.

Baseline facts established once and reused below:

- `src/ROROROblox.App` holds **30** `.xaml` files (register's F-052 says 26) and **27** `Window`-rooted
  files (register says "25 windows"): 26 `<Window` + 1 `<ui:FluentWindow>` (MainWindow).
- `Controls/ControlStyles.xaml` exists with 7 keyed/implicit styles (`PrimaryButtonStyle`,
  `SecondaryButtonStyle`, `SecondaryStrongButtonStyle`, `AppTextBoxStyle`, `AppPasswordBoxStyle`,
  implicit `ComboBox`, `CardBorderStyle`, `SectionHeadingStyle`).
- `Preferences/PreferencesWindow.xaml` is now a 461-line 5-page nav-rail shell
  (Startup / Accounts / Alerts & memory / Discord / Appearance), resizable, `MinHeight=480 MinWidth=760`.
- The nav band on `MainWindow.xaml` is **two** controls (`Settings` at `:1144`,
  `ToolsDropDownButton` at `:1161`), both on `SecondaryButtonStyle`, both `FontSize="11"`.
- `Settings/SettingsWindow.xaml` no longer exists; it is `Games/GamesWindow.xaml`.
- `ThemeStore.cs` moved from the App project to `src/ROROROblox.Core/Theming/ThemeStore.cs`.

---

### F-041 — ACCURATE

- **Row claims:** nine Close buttons across five paddings; accent-filled/`IsDefault` at 22,8 / 20,8 / 14,6
  vs bordered-secondary/no-default at 16,8 / 14,8; no agreed weight, size or default behaviour.
- **Checked:** `grep -rn 'Content="Close"' --include=*.xaml src/ROROROblox.App` plus 6 lines of trailing
  context on each hit; `src/ROROROblox.Tests/ModalDefaultButtonSafetyTests.cs`.
- **Found:** exactly **nine**, and the split is verbatim intact.

  | window | line | padding | treatment | IsDefault |
  |---|---|---|---|---|
  | `About/AboutWindow.xaml` | :134 | 20,8 | Cyan fill, SemiBold | yes |
  | `History/SessionHistoryWindow.xaml` | :58 | 22,8 | Cyan fill, SemiBold | yes |
  | `Plugins/PluginsWindow.xaml` | :374 | 22,8 | Cyan fill, SemiBold | yes |
  | `Preferences/PreferencesWindow.xaml` | :451 | 22,8 | Cyan fill, SemiBold | yes |
  | `Theming/CaptionColorPickerWindow.xaml` | :82 | 14,6 | Cyan fill, SemiBold | yes |
  | `Games/GamesWindow.xaml` | :413 | 16,8 | `SecondaryStrongButtonStyle` | no |
  | `Diagnostics/DiagnosticsWindow.xaml` | :60 | 14,8 | `SecondaryStrongButtonStyle` | no |
  | `Friends/FriendFollowWindow.xaml` | :72 | 14,8 | `SecondaryStrongButtonStyle` | no |
  | `SquadLaunch/SquadLaunchWindow.xaml` | :96 | 14,8 | `SecondaryStrongButtonStyle` | no |

  Five distinct paddings (22,8 / 20,8 / 16,8 / 14,8 / 14,6), and they map to the two ranks exactly as
  the row says. All nine carry `IsCancel="True"`.
- **Direction:** same (9 buttons, 5 paddings, both unchanged).
- **Note:** one thing shifted under the row without changing it — the four bordered-secondary Closes now
  all consume `SecondaryStrongButtonStyle` from `ControlStyles.xaml` rather than hand-copied attributes.
  A `DialogFooter` composite is therefore a smaller job than when the row was written: the *treatment*
  half is already centralised; what is uncentralised is padding, rank choice and `IsDefault`.
  `ModalDefaultButtonSafetyTests` fences only destructive labels, not Close, so nothing watches this.

---

### F-042 — ALREADY FIXED

- **Row claims:** `MainWindow.xaml:1360` streamer mode is the only `ui:ToggleSwitch`; Preferences booleans
  are all `CheckBox`; two mutually exclusive boolean representations.
- **Checked:** `grep -rn "ToggleSwitch" --include=*.xaml --include=*.cs src/ROROROblox.App`;
  `grep -rc "<CheckBox" --include=*.xaml`; `src/ROROROblox.App/Preferences/PreferencesWindow.xaml:125-139`.
- **Found:** **zero `ToggleSwitch` elements exist in the app.** The only three hits are prose:
  `PreferencesWindow.xaml:127-128` and a doc-comment at `ViewModels/MainViewModel.cs:612`. The comment at
  `PreferencesWindow.xaml:125-130` states the fix outright: *"Rendered as a CheckBox rather than the
  ui:ToggleSwitch it was, to match the six settings already on this page — the ToggleSwitch was the app's
  only one."* Preferences holds 7 `CheckBox` (`:90, :107, :133, :161, :211, :368, :384`); the app holds
  12 total across 6 files and no other boolean control type.
- **Direction:** the minority representation went to zero. Preferences `CheckBox` count 6 → 7 (grew), and
  the +1 **is** the converted control.
- **Note:** this row was closed sideways by **F-008** (wave 1, streamer-mode relocation), which is the
  exact F-001 pattern. The 2026-08-09 reconciliation touched this row, counted the 7 CheckBoxes, and
  recorded "count drifted" — but the 7th one is the streamer toggle, i.e. the defect's disappearance was
  visible in the same count that was used to mark the row still-open. Its fix direction ("convert
  Preferences to ToggleSwitch, **or** Streamer mode to CheckBox on the way in") was satisfied by the
  second branch.

---

### F-043 — DRIFTED

- **Row claims:** six checkbox labels across three grammatical persons — second-person imperative at
  `:46, :83, :102`, first person "my"/"I'm" at `:63, :139, :155`.
- **Checked:** read `PreferencesWindow.xaml` in full; extracted every `CheckBox` child `TextBlock Text`.
- **Found:** **seven** labels now, and all six cited line numbers are dead (the file was rewritten into
  the nav-rail shell).

  | line | label | voice | terminal period |
  |---|---|---|---|
  | :95 | "Start RoRoRo when Windows starts." | 2nd/imperative | yes |
  | :112 | "Launch **my** main account when RoRoRo starts." | 1st person | yes |
  | :138 | "Streamer mode." | bare noun | yes |
  | :166 | "Always show Recycle on running accounts." | 2nd/imperative | yes |
  | :216 | "Mute idle alerts." | 2nd/imperative | yes |
  | :371 | "Show what **I'm** playing on Discord." | 1st person | yes |
  | :387 | "Let friends join **my** server from Discord" | 1st person | **no** |

  The three first-person labels are the *same three* the row's fix direction rewrites by example
  ("Launch your main account…", "Show what you're playing…", "Let friends join your server…"). Defect
  fully intact.
- **Direction:** grew — 6 → 7 labels; first-person count 3 → 3 (same); a **fourth** form appeared, the
  verbless noun "Streamer mode." at `:138`, which arrived with F-008's relocation.
- **Note:** the "all terminal periods" half of the fix direction has one live violation (`:387`), which
  the row never counted because that label was not among its six.

---

### F-044 — DRIFTED

- **Row claims:** 11px default body size app-wide; drops to 10px (helper/chip), 9px (MAIN pill), 8px
  (popup badge); "under the 12px floor and drops to 8-10px in six places."
- **Checked:** `grep -rho 'FontSize="[0-9.]*"' --include=*.xaml` and `grep -rho "FontSize = [0-9]*"
  --include=*.cs` over `src/ROROROblox.App`, plus site-level reads at `MainWindow.xaml:62, :77, :125,
  :429, :1259, :1448`. Also swept `src/ROROROblox.Tests` for any type-ladder fence.
- **Found:** combined XAML + code-behind census, **11 distinct sizes** app-wide:
  8px×3, 9px×9, 10px×37, 11px×154, 12px×55, 13px×30, 14px×14, 18px×10, 20px×3, 22px×5, 24px×3.
  Sub-12px total = **203 sites**. The 8-10px band alone = **49 sites** app-wide (14 of them in
  `MainWindow.xaml`), against the row's "six places." Role mapping still holds and is now denser than
  the row states: 9px MAIN pill (`:429`) **and an 8px compact-row MAIN pill** (`:62`) the row folded
  into "popup badge"; 8px popup DEFAULT badge (`:1448`); 9px memory chips (`:77`, `:125`); 9px widget
  DEFAULT label (`:1259`).
- **Direction:** grew. No 12px floor shipped and **no test measures font size anywhere** — the only
  `FontSize` references in `src/ROROROblox.Tests` are inside the rendering harness
  (`Rendering/ThemedRender.cs`, `Rendering/TriggeredStatusColourGateTests.cs`), which sets sizes to
  render, it does not assert on them.
- **Note:** three sub-10px sites now live in **C#**, not markup —
  `History/SessionHistoryWindow.xaml.cs:213` (8px), `:268` (9px),
  `SquadLaunch/SquadLaunchWindow.xaml.cs:174` (9px). Same blind spot class as F-085/F-088: a XAML-only
  sweep, which is how this row's census was taken, cannot see them.

---

### F-045 — PARTLY SHIPPED

- **Row claims:** three limbs. (1) `MainWindow.xaml:1020` "⚙ Settings" announces as "gear Settings";
  (2) the star-only button has no accessible name beyond the glyph; (3) `:302` literal die emoji in menu
  copy. Fix: mark glyphs decorative, carry meaning in a text name, promote the star's existing tooltip
  (`:687-691`) to its accessible name.
- **Checked:** `MainWindow.xaml:1144` (Settings button), `:776-808` (star button + tooltip),
  `:317` (Reroll identity), `grep -rn "AutomationProperties"`, `Controls/ToolsDropDownButton.cs`.
- **Found:**
  - **Limb 1 is fixed.** `MainWindow.xaml:1144` is `<Button Content="Settings"` with no glyph. The
    in-file comment at `:1120-1121` names the reason: *"The gear glyph is gone (F-012)."*
  - **Limb 2 is fully intact.** The star button is at `MainWindow.xaml:776`; its whole content is a
    child `TextBlock` swapping `&#x2606;`/`&#x2605;` via a nested style at `:799`/`:803`. No
    `AutomationProperties.Name`. The tooltip the fix direction wants promoted still exists, now at
    `:782-795` (was `:687-691`).
  - **Limb 3 is intact but ruled.** `Header="🎲 Reroll identity"` moved `:302` → `:317`. The register's
    **Rulings** section ("The two shipped emoji stay") closes findings *whose fix is deletion*; F-045's
    fix is accessible naming, not deletion, so the ruling narrows this limb rather than closing the row.
  - The "five glyphs" count is now higher — `↻` (`:142`), `☰` (`:383`), `▾` (`:1161`, `:1278`), `▲`
    (`:1624`), `▶` (`:1788`), `☆/★` (`:799`/`:803`), `🎲` (`:317`).
- **Direction:** limb count shrank 3 → 2 outstanding; glyph count grew.
- **Note:** one glyph now *does* what this row asks. `ToolsDropDownButton` at `:1161` carries
  `AutomationProperties.Name="Tools"` explicitly to strip the chevron from what is announced, and
  `Controls/ToolsDropDownButton.cs` adds a real `IExpandCollapseProvider` peer. That is the row's own
  pattern, shipped once, in wave 3 — evidence the fix direction is right and cheap, and a template for
  the star. Estimate ~2/3 of the row remains.

---

### F-046 — PARTLY SHIPPED

- **Row claims:** "Remove" is quiet bordered secondary on MainWindow/Settings but filled-accent SemiBold
  on Plugins; fill weight tracks neither consequence nor frequency. Fix: two ranks + one destructive
  variant in shared button styles, assigned by consequence.
- **Checked:** `grep -rn -A8 'Content="Remove'` across XAML and `.cs`; `Controls/ControlStyles.xaml`;
  `grep -rn "Destructive\|DangerButton"`.
- **Found:** the defect is intact and now has a fourth site.

  | site | treatment |
  |---|---|
  | `MainWindow.xaml:1008` | `SecondaryButtonStyle`, Padding 10,6 |
  | `Games/GamesWindow.xaml:175` | `SecondaryButtonStyle`, Padding 10,6 |
  | `Games/GamesWindow.xaml:287` | `SecondaryButtonStyle`, Padding 10,6 |
  | `Plugins/PluginsWindow.xaml:258-265` | hand-rolled `MagentaBrush` fill + `WhiteBrush` + **SemiBold**, Padding 10,4 |
  | `SquadLaunch/SquadLaunchWindow.xaml.cs:224-235` | built in C#, hand-rolled Navy/White/`InteractiveEdgeBrush` |

  Of the fix direction's three clauses: **ranks defined — yes** (three, in `ControlStyles.xaml`, used at
  16 + 16 + 5 call sites). **Destructive variant — no**; `grep` for `Destructive`/`DangerButton` returns
  only two prose comments in `StopAllConfirmWindow`. **Assigned by consequence — no**; Plugins' Remove
  is still the loudest control on its screen.
- **Direction:** Remove sites grew 3 → 4 (the C# one at `SquadLaunchWindow.xaml.cs:226` was never counted).
- **Note:** ~1 of 3 fix clauses shipped. The row's `MainWindow/Library` citation should read
  `MainWindow/Games`. The C# site is a fifth-copy-in-a-different-language problem — a style change in
  `ControlStyles.xaml` will not reach it, which `ControlStyles.xaml:9-13` already warns about by name.

---

### F-048 — DRIFTED

- **Row claims:** MainWindow is the only `ui:FluentWindow`; 7 windows with no `ResizeMode`, 12 `NoResize`
  (incl. all 8 modals), 2 `CanResize`; "Preferences fixed while sibling Library resizes."
- **Checked:** per-file `ResizeMode` extraction across all 27 `Window`-rooted XAML files; root-element
  census; `PreferencesWindow.xaml:7-8`.
- **Found:** every number moved, and the headline example is dead.
  - `ui:FluentWindow` — still exactly one (`MainWindow.xaml`). **Same.**
  - No `ResizeMode` attribute (defaults to `CanResize`): **10** — CookieCapture, Diagnostics,
    FriendFollow, Games, SessionHistory, MainWindow, Plugins, **Preferences**, SquadLaunch, ThemeBuilder.
  - `NoResize`: **15** — About, JoinByLink, CaptionColorPicker, ImportAccounts, ConsentSheet, and all
    **10** files under `Modals/`.
  - Explicit `CanResize`: **2** — Welcome, ExportAccounts.
  - **`Preferences` is no longer locked.** `PreferencesWindow.xaml:7-8` is `Height=640 Width=860
    MinHeight=480 MinWidth=760` with no `ResizeMode` — fixed by F-003.
- **Direction:** windows 25 → 27 (grew); unset 7 → 10 (grew); `NoResize` 12 → 15 (grew); `CanResize`
  2 → 2 (same); modals 8 → 10 (grew, still uniformly `NoResize`).
- **Note:** the row's core verdict — resize behaviour is not derived from window kind — is intact, and
  there is a **fresh instance the row never named**: `Transport/ExportAccountsWindow.xaml` is explicit
  `CanResize` while its sibling `Transport/ImportAccountsWindow.xaml` is `NoResize`. Same-folder, same
  task, opposite chrome. That is the exact shape of the Preferences/Library example this row was built
  on, re-created after that one was fixed.

---

### F-050 — ACCURATE. **DO NOT FLIP THE STATUS CELL.**

- **Row claims:** every filled CTA fails 4.5:1 in at least one shipped theme because foreground is
  chosen at author time and fill at theme time; white-on-magenta = 3.79:1 brand; best theme-derived
  foreground reaches only 4.40:1 brand. Fix: resolve CTA foreground at brush-application time in
  `ThemeService`, picking the higher-ratio slot, plus a validation step.
- **Checked:** `src/ROROROblox.Tests/ContrastPairGateTests.cs` in full (`:62`, `:92-110`, `:200-278`,
  `:281-314`); `src/ROROROblox.Tests/FlatlineLabGateTests.cs:35-126`;
  `src/ROROROblox.App/Theming/ThemeService.cs:173-289`; a Python replication of the gate's own scan
  regexes across all 30 app XAML files; a Python replication of the gate's status-cell parse.
- **Found:**
  - The auto-delete mechanism is exactly as described. `NoExemptionOutlivesItsFinding`
    (`ContrastPairGateTests.cs:281-314`) regex-matches `^\|\s*(F-\d+)\s*\|`, splits on `|`, takes
    `cells[^1].Trim()`, and asserts it equals the literal string `"open"`. Simulated against the current
    register: **F-050 parses as `'open'`.** So does every other row in this batch.
  - The exemption is one tuple at `ContrastPairGateTests.cs:109`:
    `("MagentaBrush", "WhiteBrush", "F-050", 3.20)`.
  - My scan reproduces the gate exactly: **44 elements, 8 distinct pairs, `WhiteBrush` on
    `MagentaBrush` at 8 sites.** Matches the gate's own `MinimumElements`/`MinimumPairs` floors and
    F-086's recorded numbers.
  - **The fix direction has not shipped.** `ThemeService.ApplyTo` (`:226`) and `ApplyToResources`
    (`:173`) call `ContrastGuard.Ensure` exactly once, at `:211`, and only to derive
    `InteractiveEdgeBrush` from `theme.Navy`/`theme.Divider`. There is no CTA-foreground resolution, no
    higher-ratio slot pick, and no validation step. v1.17 shipped a theme that clears the pair; it did
    not implement the mechanism that closes the row.
- **Direction:** n-a (site count 8, unchanged and independently confirmed).
- **Note — and this corrects the brief I was given.** Non-exempt pairs are measured against
  `AaThreshold = 4.5` (`ContrastPairGateTests.cs:62`), not 3:1. Deleting the exemption therefore reddens
  **three** built-in themes, not two: brand **3.79**, midnight **4.16**, magenta-heat **3.29** — all under
  4.5. Only flatline (4.68) clears. Ratios are the gate's own, recorded at `:94-102`. There is also a
  **second** consumer of the floor: `FlatlineLabGateTests.cs:59` hardcodes `F050ExemptionFloor = 3.20`
  and `:117`/`:125` assert the `flatline-lab` fixture reproduces F-050's 2.99 and the
  F-031×F-050 cross-check of 12.98. Any scoping that touches this row must plan for both files.
  **Recommendation: leave the status cell reading `open`.** Closing it is a code change in
  `ThemeService`, not a register edit.

---

### F-051 — ACCURATE

- **Row claims:** `ThemeService.cs:82-91` swallows a persist failure to a log warning and applies live
  anyway; `PreferencesWindow.xaml.cs:198-206` has a bare `catch{}`; the user sees the theme apply, gets
  no message, and finds the old theme back after restart.
- **Checked:** `Theming/ThemeService.cs:82-111`; `Preferences/PreferencesWindow.xaml.cs:264-281`;
  grep for a theme status line.
- **Found:** both limbs intact, both citations drifted.
  - `ThemeService.SetActiveAsync` at `:82`; the persist `try`/`catch` at `:91-97`, logging
    `_log.LogWarning(ex, "Saving active theme id failed; applying live anyway.")` at `:96`, then falling
    through to `ApplyToResources(theme, answer)` at `:111`. Nothing is surfaced to the UI.
  - The bare catch is now at `PreferencesWindow.xaml.cs:275-278` — literally
    `catch { // best-effort }` — inside `OnThemeChanged` (`:264`). Was `:198-206`.
  - The pattern the fix direction points at exists and is healthy: `AlertsStatusLine` is composed at
    `PreferencesWindow.xaml.cs:586` from `AlertStatusLine.Compose(...)` and bound at
    `PreferencesWindow.xaml:357`. The Appearance page has `ThemeDescriptionText` (`:416`) but **no
    status line**, so there is nowhere for the message to land yet.
- **Direction:** same (one swallow site each side).
- **Note:** the Appearance page also now hosts the F-078 "re-ask about my theme" affordance request.
  Both want a status/feedback slot on the same card.

---

### F-052 — DRIFTED (one sub-claim `CANNOT VERIFY`)

- **Row claims:** across all 26 XAML files, grep for
  `AutomationProperties|FocusVisualStyle|TabIndex|KeyboardNavigation|IsTabStop` returns **zero**;
  70 unnamed Buttons and 16 unnamed ComboBoxes in the main-window tree; no accessible-naming layer
  exists in the app's own composition.
- **Checked:** the row's own grep verbatim over `src/ROROROblox.App`; a Python tag-level census of
  `Button`/`ComboBox`/`ToggleButton`/`TextBox` declarations with and without
  `AutomationProperties.Name`; `Controls/ToolsDropDownButton.cs`.
- **Found:**
  - The grep **no longer returns zero.** Two hits, both in `MainWindow.xaml`: a comment at `:1159` and a
    live `AutomationProperties.Name="Tools"` at `:1162`. `FocusVisualStyle`, `TabIndex`,
    `KeyboardNavigation` and `IsTabStop` are still **zero across the whole app.**
  - Static declaration census, app-wide: **114 Buttons, 7 ComboBoxes, 5 ToggleButtons, 11 TextBoxes =
    137 controls, of which 0 carry `AutomationProperties.Name`.** The one name in the app sits on
    `controls:ToolsDropDownButton`, a custom type outside all four of those tags.
  - `MainWindow.xaml` statically declares 35 Buttons, 3 ComboBoxes, 3 ToggleButtons — none named.
  - File count 26 → **30**.
- **Direction:** file count grew 26 → 30; named controls 0 → 1; unnamed controls remain ~137 of 137 by
  declaration.
- **Note — `CANNOT VERIFY` on the specific "70 unnamed Buttons, 16 unnamed ComboBoxes" figures.** Those
  are runtime-UIA-tree counts (account rows are templated and repeat per account); reproducing them
  needs the app running, which I cannot do. What is verifiable is the static declaration count above.
  The row's headline sentence — "no accessible-naming layer exists in the app's own composition" — is
  now *narrowly* false and *broadly* true, and the exception is instructive:
  `Controls/ToolsDropDownButton.cs` ships a full `IExpandCollapseProvider` automation peer with
  open/close property-change events. Wave 3 built the row's fix direction once, deliberately, for one
  control. Scope this row as ~1/137 done, not as untouched.

---

### F-053 — ACCURATE

- **Row claims:** `PreferencesWindow.xaml:293` tooltip claims a dropped theme needs a restart;
  `ThemeStore.cs:42-72` re-enumerates on every `ListAsync()`, no restart needed; malformed themes drop
  silently.
- **Checked:** `Preferences/PreferencesWindow.xaml:434-441`; `src/ROROROblox.Core/Theming/ThemeStore.cs:42-160`;
  `Preferences/PreferencesWindow.xaml.cs:246, :305`.
- **Found:** all three limbs intact; both citations drifted.
  - Tooltip is now at `PreferencesWindow.xaml:441`, on the "Open themes folder" button (`:435`):
    *"Drop a *.json file in here to make it appear in the picker after restart."* The restart claim is
    verbatim still there.
  - `ListAsync()` is now `src/ROROROblox.Core/Theming/ThemeStore.cs:42-81` (file moved App → Core). It
    `EnumerateFiles("*.json", TopDirectoryOnly)` on every call (`:59`) with no cache. The window calls it
    on load at `PreferencesWindow.xaml.cs:246` and again after the theme builder at `:305`, so the true
    refresh unit is **reopening the window**, exactly what the fix direction says the copy should say.
  - Silent drop confirmed: `TryLoadFileAsync` swallows `JsonException` (`:125`) and
    `InvalidOperationException` (`:158`) and returns null; the caller `continue`s at `:70-71`. No inline
    report anywhere.
- **Direction:** same.
- **Note:** the fix direction's "reopen this page" is slightly imprecise against the current shape — the
  rail's Appearance *page* does not re-list; the *window* does. "Reopen Settings" is the accurate copy.
  Not a design change, just wording the builder should catch before writing it.

---

### F-054 — ACCURATE

- **Row claims:** grep for `KeyBinding|InputBindings|Gesture` across all XAML/CS returns zero;
  `CookieCaptureWindow.xaml` has no `Button` element at all, so Esc doesn't dismiss it.
- **Checked:** the row's grep verbatim over `src/ROROROblox.App` (exit 1, no matches);
  `grep -n "Button" CookieCapture/CookieCaptureWindow.xaml` (no matches);
  `grep -rn "IsCancel"` across the app.
- **Found:** both limbs exactly as written. **Zero** `KeyBinding` / `InputBindings` / `Gesture` anywhere
  in the app. `CookieCaptureWindow.xaml` contains **no `Button` element and no `IsCancel`** — it is the
  only `Window`-rooted file in the app with neither. Twenty-two other windows carry at least one
  `IsCancel="True"`, so Esc works everywhere except the one modal a first-run user is guaranteed to hit.
- **Direction:** same (zero, zero).
- **Note:** nothing else in the batch is this cleanly true. This row needs no re-measurement before
  scoping.

---

### F-055 — ALREADY FIXED

- **Row claims:** all six nav destinations use `MutedTextBrush` at 11px, the same role as helper prose;
  the FPS-banner body is also `MutedTextBrush`/11px, so nav is at parity, not quieter.
- **Checked:** `MainWindow.xaml:1108-1215` (the whole nav band), `Controls/ControlStyles.xaml:49-55`,
  `grep -n "MutedTextBrush" MainWindow.xaml`, `src/ROROROblox.Tests/MutedTextFenceTests.cs`.
- **Found:** **the premise is false on both limbs.**
  - There are **two** nav controls, not six: `Settings` (`MainWindow.xaml:1144`) and
    `ToolsDropDownButton` (`:1161`). F-009 collapsed the band; the four removed destinations moved into
    the Tools menu.
  - Neither binds `MutedTextBrush`. Both take `Style="{StaticResource SecondaryButtonStyle}"`, which
    sets `Foreground` to `{DynamicResource WhiteBrush}` (`ControlStyles.xaml:51`). The token that carried
    the defect is gone from the nav band entirely.
  - Nav is no longer "at parity with de-emphasized prose": it is on the primary text token, and the
    control-vs-prose distinction is carried by `InteractiveEdgeBrush` (`ControlStyles.xaml:53`), derived
    to clear 3:1 — which is the row's own fix direction ("let grouping … carry the 'chrome not content'
    distinction").
  - It is also **fenced**. `MutedTextFenceTests` enforces the role rule app-wide — the prose token may
    not label a control — so a regression here fails the build.
- **Direction:** nav destinations 6 → 2 (shrank); `MutedTextBrush` on nav 6 → 0.
- **Note:** closed by the combination of **F-032** (token moved to `WhiteBrush`, marked clean),
  **F-009** (six buttons → two) and **F-035** (shared styles). None of those three flipped this row.
  `MutedTextBrush` still has 22 references in `MainWindow.xaml` and 127 app-wide, but on prose, chips
  and empty states, which is what the token is for and what the fence permits.

---

### F-056 — ALREADY FIXED (in-scope), out-of-scope census drifted

- **Row claims:** the gear glyph on Settings (`:1020`) is the only glyph among six otherwise-identical
  peer nav buttons; 8 ad-hoc glyphs vs one real WPF-UI `SymbolIcon`. Fix direction is explicitly
  scoped: *"(in-scope: nav-band gear only)."*
- **Checked:** `MainWindow.xaml:1144`, `:1120-1121`; `grep -rn "SymbolIcon\|ImageIcon"`; a Python sweep
  for non-ASCII codepoints (including `&#xNNNN;` entities) inside `Text`/`Content`/`Header` attributes
  across all 30 app XAML files.
- **Found:** the in-scope defect is gone. `<Button Content="Settings"` at `:1144` carries no glyph, and
  the comment at `:1120-1121` names F-012 as the reason. There is no "row of six" any more.
  Out-of-scope, the census moved the other way: **13** glyph-bearing `Text`/`Content`/`Header` attributes
  (plus the `☆`/`★` style setters at `:799`/`:803`, so ~15 sites) against **2** WPF-UI icons —
  `ui:ImageIcon` at `MainWindow.xaml:1041` (title bar) and `ui:SymbolIcon Filter24` at `:1688`.
- **Direction:** nav-band glyphs 1-of-6 → 0-of-2; ad-hoc glyphs 8 → ~13-15 (grew); WPF-UI icons 1 → 2 (grew).
- **Note:** one wrinkle worth a sentence before anyone re-opens this. `ToolsDropDownButton` at `:1161`
  carries `Content="Tools  &#x25BE;"` — so one of the two nav controls does wear a glyph. It is a
  disclosure chevron, not decoration, and `AutomationProperties.Name="Tools"` strips it from what gets
  announced. That is a different thing from the gear and should not be read as the row recurring.
  F-059 is the near-duplicate of this row and is still marked `open` on the same evidence — outside my
  batch, flagged below.

---

### F-057 — DRIFTED

- **Row claims:** `MainWindow` alone uses 11px×33, 12px×11, 10px×10, 9px×4, 8px×2 — nine sizes with no
  ladder; the dominant 11px body size is 148 uses app-wide.
- **Checked:** `grep -o 'FontSize="[0-9.]*"' MainWindow.xaml | sort | uniq -c`; same app-wide across
  XAML and `.cs`; `grep -c "FontSize" MainWindow.xaml.cs` (= 0);
  `grep -rn 'Property="FontSize"' --include=*.xaml` (one hit, `ControlStyles.xaml:144`).
- **Found:** `MainWindow.xaml` now uses **seven** distinct sizes over 58 sites:
  11px×**27**, 12px×**11**, 10px×**8**, 9px×**4**, 8px×**2**, 13px×3, 14px×3. Nothing above 14px remains
  in that file, and `MainWindow.xaml.cs` sets no `FontSize` at all. App-wide 11px is **154**
  (146 XAML + 8 code-behind).
- **Direction:** distinct sizes in MainWindow 9 → **7** (shrank); 11px 33 → **27** (shrank);
  10px 10 → **8** (shrank); 12px, 9px, 8px unchanged; app-wide 11px 148 → **154** (grew).
- **Note:** the shrink is real but it is a side effect, not progress on this row — the sizes that
  disappeared are the 22px/18px header lockup and pitch removed by F-010/F-011/F-036, not a ladder
  anyone built. There is still no four-step ladder, no shared type resource, and no fence: the only
  centralised size in the app is `SectionHeadingStyle`'s 13px at `ControlStyles.xaml:144`. Re-scope
  against 7 sizes / 58 sites in `MainWindow.xaml` and 11 sizes / 342 sites app-wide, not the row's
  numbers.

---

## Batch summary

| verdict | count | rows |
|---|---|---|
| `ACCURATE` | 4 | F-041, F-050, F-051, F-053, F-054 → **5** |
| `DRIFTED` | 5 | F-043, F-044, F-048, F-052, F-057 |
| `PARTLY SHIPPED` | 2 | F-045, F-046 |
| `ALREADY FIXED` | 3 | F-042, F-055, F-056 |
| `SUPERSEDED` | 0 | — |
| `CANNOT VERIFY` | 0 whole rows | one sub-claim inside F-052 |

Corrected tally: **5 ACCURATE · 5 DRIFTED · 2 PARTLY SHIPPED · 3 ALREADY FIXED.**

### The rows that most change the picture

1. **F-042 is dead and the 2026-08-09 reconciliation walked past it.** Zero `ToggleSwitch` elements
   exist. The reconciliation counted Preferences' 7 CheckBoxes and recorded "count drifted" without
   noticing the 7th *is* the converted streamer toggle, i.e. the whole defect. Closed sideways by F-008
   in wave 1. This is the F-001 pattern repeating inside the pass that was supposed to catch it.

2. **F-055 and F-056 are both dead for the same structural reason** — the six-button nav band is two
   buttons on a shared style with a white label. Three separate closed rows (F-009, F-012, F-032) each
   took a piece; none flipped these. Any cycle picking "nav band" work off this register is picking
   twice-fixed ground.

3. **F-050 must not be flipped, and the blast radius is bigger than briefed.** Removing the exemption
   measures white-on-magenta against `AaThreshold = 4.5`, which reddens **three** built-ins —
   brand 3.79, midnight 4.16, magenta-heat 3.29 — not two. The 3.20 floor is also mirrored in
   `FlatlineLabGateTests.cs:59`. The fix direction is unimplemented: `ThemeService.ApplyTo` derives
   only `InteractiveEdgeBrush`, never a CTA foreground.

4. **Worst drift is F-048.** Every census number in it moved (25→27 windows, 7→10 unset, 12→15
   `NoResize`, 8→10 modals) and its headline example — "Preferences fixed while sibling Library
   resizes" — is fixed. Worse, the defect regenerated somewhere the row never looked:
   `ExportAccountsWindow` is `CanResize` while `ImportAccountsWindow` is `NoResize`, same folder, same
   task. Scoping this row off its stated evidence would fix a window that is already resizable and miss
   the live pair.

5. **F-046 and F-045 are half-built, and the built half is the template.**
   `ControlStyles.xaml` supplies the ranks F-046 asks for but no destructive variant and no
   consequence-based assignment; `ToolsDropDownButton` supplies exactly the automation-peer + name
   pattern F-045 and F-052 ask for, once. Both rows should be re-scoped as "extend the thing that
   exists," not "build a mechanism."

6. **Three rows still measure clean and need no re-measurement: F-041, F-053, F-054.** F-054 in
   particular is verbatim true — zero keyboard bindings app-wide and `CookieCaptureWindow.xaml` still
   has no `Button` and no `IsCancel`, making it the only window in the app Esc cannot dismiss.

### Incidental, outside the batch

Noted only; not audited, not verified beyond the single observation that surfaced them.

- **F-059** ("ui:ImageIcon appears once (title bar) … of six nav buttons only Settings carries one") is
  the near-duplicate of F-056 and rests on the same dead evidence — the gear is gone, there is no row of
  six, and `ui:SymbolIcon Filter24` at `MainWindow.xaml:1688` is a second WPF-UI icon. Likely also
  `ALREADY FIXED` in its in-scope half.
- **F-069** ("Header=`Reroll identity` — drop the glyph") has deletion as its entire fix direction, which
  is precisely the class the **Rulings** section closed on 2026-08-04 ("The two shipped emoji stay").
  It reads exactly like F-040 did before the reconciliation closed it as ruled, and it is still `open`.
- **F-026**'s reconciliation note ("Preferences currently holds 9 card borders under a 5-page nav rail")
  matches the tree: 9 `CardBorderStyle` uses, 5 `ListBoxItem` rail entries. Consistent.
- `src/ROROROblox.Tests/bin/` and `obj/` contain copies of six `Modals/*.xaml`. Harmless, but any
  register census run with a naive `find src -name "*.xaml"` will double-count them. Mine excluded them.
