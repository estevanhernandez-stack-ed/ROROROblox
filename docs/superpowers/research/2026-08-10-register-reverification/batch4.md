# Re-verification — batch 4

Repo `<repo root>`, branch `main`, tag `v1.17.0.0`, HEAD `b474a2f`.
Read-only pass. No repo file edited, no build, no test run. Every number below was
re-measured against the tree or re-derived arithmetically from source; nothing was
carried forward from the row, from a changelog, or from a test's own doc comment.

Rows verified: F-074 F-075 F-078 F-079 F-083 F-084 F-085 F-086 F-087 F-090 F-091 F-092.

Two independent reproductions were run rather than trusted:

- **The contrast-gate scan** (F-086) — re-implemented `ContrastPairGateTests`' two regexes
  over the same file walk in PowerShell.
- **The `ContrastGuard.Ensure` derivation** (F-090, F-092) — re-implemented the blend walk
  including the snap-before-check, then computed WCAG ratios from `ThemeStore.cs`'s verbatim
  slot values.

The `626labs.ur-task` binary scan (F-091) was also reproduced, so nothing in this batch
lands on `CANNOT VERIFY`.

---

### F-074 — ACCURATE

- **Row claims:** `StopAllConfirmWindow.xaml:36` puts "UNSAVED GAME STATE WILL BE LOST" in the
  JetBrains Mono 10px uppercase label style at `#5A6982`, the dimmest text on the surface.
- **Checked:** read `src/ROROROblox.App/Modals/StopAllConfirmWindow.xaml` in full.
- **Found:** every element exact. `:36` `Text="UNSAVED GAME STATE WILL BE LOST"`, `:37`
  `FontFamily="JetBrains Mono, Cascadia Mono, Consolas"`, `:38` `FontSize="10"`, `:39`
  `Foreground="#5A6982"`. Dimmest confirmed against the two siblings on the same surface:
  heading `#17D4FA` at `:25`, body `#FFFFFF` at 0.85 opacity at `:31-32`. Nothing has moved
  and nothing has shipped against it.
- **Direction:** same.
- **Note:** the file is inside `Modals/`, which sits on the XAML colour-literal fence's
  directory-prefix allow-list under F-079/F-066. A fix here touches a region another open row
  already counts; the ceiling constant moves with it.

---

### F-075 — DRIFTED

- **Row claims:** six functional-glyph sites at `MainWindow.xaml:131,334,700,704,578,1141`, plus
  `WelcomeWindow.xaml:123,130` teaching the same glyphs; fix direction is "no change, recorded as
  a deliberate keep", with `▶ Start (:1682)` flagged as the one marginal case.
- **Checked:** swept both files for glyph codepoints and for `&#x` character entities (the glyphs
  are written as entities, which is why a raw-character grep returns nothing); `git blame` on the
  two sites the row does not name.
- **Found:** the two WelcomeWindow citations are exact — `&#x2630;` (☰) at `:123`, `&#x2605;` (★)
  at `:130`. **All six MainWindow citations have moved**, by between +11 and +99 lines, and the
  inventory has grown. Current sites, nine of them:

  | glyph | now at | was cited at |
  |---|---|---|
  | `&#x21BB;` ↻ recycle (compact) | `:142` | `:131` |
  | `&#x2630;` ☰ drag grip | `:383` | `:334` |
  | `&#x00D7;` × tag remove | `:672` | `:578` |
  | `&#x2606;` ☆ star empty | `:799` | `:700` |
  | `&#x2605;` ★ star filled | `:803` | `:704` |
  | `&#x25BE;` ▾ Tools | `:1161` | **not named** |
  | `&#x25BE;` ▾ widget chevron | `:1278` | `:1141` |
  | `&#x25B6;` ▶ Start | `:1788` | `:1682` (fix direction) |
  | `&#x2606;` ☆ empty state | `:1811` | **not named** |

  The two unnamed: `:1161` was added `3fc838b5`, 2026-08-04 — wave 3's own merge, the wave that
  built the Tools container. `:1811` dates to `009866a6`, 2026-05-04, so it was in the tree at
  audit and was simply missed.
- **Direction:** grew — 7 named (6 + the marginal ▶) to 9 present.
- **Note:** this row's whole job is to be a keep-list so a later pass does not strip these. A
  keep-list that misses two of the nine grants no protection to those two, and the one added by
  wave 3 is the newest. Re-cite by content before anything acts on it.

---

### F-078 — ACCURATE

- **Row claims:** `AppSettings.EdgeRemediationAnswers` holds the edge answer forever, nothing in
  the UI re-opens it, and `EdgeRemediationWiringTests` notes the store supports re-answering while
  no control calls it.
- **Checked:** `AppSettings.cs:135-178`, `IAppSettings.cs:40-41`, every caller of the two edge APIs
  across `src/ROROROblox.App`, `EdgeRemediation.Decide`, `PreferencesWindow.xaml` for any re-ask
  affordance.
- **Found:** intact in every part.
  - `AppSettings.cs:155` `SetEdgeRemediationAnswerAsync` exists and works; `EdgeRemediationAnswers`
    is on the settings record at `:489`.
  - `EdgeRemediationWiringTests.cs:75` — *"a 're-ask me' affordance in Settings would land on
    exactly this call."* Row's citation of that note is right.
  - `EdgeRemediationWindow.xaml.cs:104-105` says it outright in a code comment: *"there is no
    re-ask affordance anywhere."*
  - The only two UI entry points are `PreferencesWindow.xaml.cs:274` and `:321`, and both call
    `AskIfPendingAsync`, which returns immediately when `PendingEdgeQuestion` is null
    (`EdgeRemediationWindow.xaml.cs:88-89`). `EdgeRemediation.Decide:48` is
    `if (!alreadyAnswered) return Decision.AskFirst;` — once answered, `AskFirst` is unreachable
    for that theme id forever.
  - No control anywhere calls `SetEdgeRemediationAnswerAsync` except
    `ThemeService.AnswerEdgeQuestionAsync`, which only runs off the pending question.
- **Direction:** same.
- **Note:** the fix is genuinely one control plus one existing setter. The Appearance page already
  binds `InteractiveEdgeBrush` at `PreferencesWindow.xaml:318,342`, so the surface is there.

---

### F-079 — DRIFTED

- **Row claims:** twelve `Background="#..."` literals across `StopAllConfirmWindow`,
  `LeftoverProcessesWindow`, `RobloxAlreadyRunningWindow`, `RobloxNotInstalledWindow`,
  `WebView2NotInstalledWindow`, `RenameWindow`; none uses the three styles wave 5 created;
  `EdgeRemediationWindow` is the only window in that folder speaking the new vocabulary.
- **Checked:** counted `Background="#` per file across `Modals/`; counted all `#RRGGBB` per file;
  grepped `PrimaryButtonStyle` / `SecondaryButtonStyle` / `SecondaryStrongButtonStyle` usage across
  `Modals/`; `git blame` and `git log --diff-filter=A` on `LaunchHeadroomWindow`.
- **Found:** the total holds at **12**, but two of the three sub-claims have moved.
  1. **The file set is wrong by one, in both directions.** `RenameWindow.xaml` has **zero**
     `Background="#"` — its only hex is `BorderBrush="#5A6982"` at `:67`. `DpapiCorruptWindow.xaml`
     holds one the row never names, at `:37`. Current per-file split:
     `RobloxAlreadyRunningWindow` 4 (`:82,85,88,91`), `LeftoverProcessesWindow` 3 (`:30,33,36`),
     `StopAllConfirmWindow` 2 (`:49,53`), `DpapiCorruptWindow` 1 (`:37`),
     `RobloxNotInstalledWindow` 1 (`:45`), `WebView2NotInstalledWindow` 1 (`:36`).
  2. **"EdgeRemediationWindow is the only window in that folder speaking the new vocabulary" is
     now false.** `LaunchHeadroomWindow.xaml:56` uses `SecondaryStrongButtonStyle` and `:61` uses
     `PrimaryButtonStyle`. That file was added by `73ca12f` (PR #96) on 2026-08-09, after the row
     was written — real drift, not a write-time error. Two windows now, not one.
  3. **The core claim holds.** None of the six named windows uses any of the three styles.
- **Direction:** literal count same (12); style-adopting windows in `Modals/` grew 1 → 2.
- **Note:** the register already half-knows about the file-set gap — `ThemedStatusColourTests.cs:313-318`
  says *"DpapiCorruptWindow is the one file in the folder F-079 does not name; F-066's 'mostly in
  out-of-scope modals' count owns it."* Nobody wrote that back into the row. `RenameWindow` being on
  the list and holding nothing is the other half, and nothing knows about that one. Total hexes in
  `Modals/` is 54, which is the number the fence's ceiling budgets for the whole directory.

---

### F-083 — ACCURATE

- **Row claims:** `MemoryDefaults.ExpectedClientMb` is a 2650 MB constant measured on one machine;
  the pre-launch advisor and the fits-how-many arithmetic both consult it; the watchdog already has
  this machine's real answer in hand.
- **Checked:** `MemoryDefaults.cs`, every reference to `ExpectedClientMb` across the solution,
  a sweep for any learning implementation (`p75`, `learned`, `SettledSample`, `FootprintLearner`,
  `AdaptiveFootprint`) across Core and App, and the scope doc's existence.
- **Found:** unchanged and unimplemented. `MemoryDefaults.cs:25` is still
  `public const int ExpectedClientMb = 2650;` with the doc comment naming the single measurement
  (*"Measured 2026-08-07 on client 733 across 8 concurrent clients in Pet Sim 99: median 2650 MB,
  peak 3280 MB"*). Exactly the two consumers the row names, and no third:
  `LaunchHeadroomAdvisor.cs:53` (`roomFor = spare / (ExpectedClientMb * Mb)` — the fits-how-many
  arithmetic) and `MemoryDefaults.cs:76` (the advisor's headroom check). Zero hits for any learning
  machinery. Scope doc present at
  `docs/superpowers/specs/2026-08-09-rororo-adaptive-footprint-scope.md`.
- **Direction:** same.
- **Note:** the trap the row names (Windows trims working sets under pressure, so
  `PrivateMemorySize64` reads lower exactly when the user is in trouble) is still un-guarded in
  code — there is nothing to guard yet. Nothing in the tree contradicts the row's scoping.

---

### F-084 — DRIFTED

Two errors. Both were present when the row was written on 2026-08-09; neither is movement.

- **Row claims:** **eight** owner-resolution sites gate on `owner.IsLoaded` alone, then lists
  seven — `LaunchHeadroomWindow.xaml.cs:74`, `EdgeRemediationWindow.xaml.cs:94`,
  `ConsentSheet.xaml.cs:46`, `App.xaml.cs:984`, `:1008`, `:1594`, `:1622`. Harm: a tray-invoked
  dialog raised while RoRoRo is minimized becomes an owned child of a window the user cannot see,
  so dismissal returns activation to an invisible owner.
- **Checked:** grepped every `IsLoaded` and every `.Owner =` assignment across `src/ROROROblox.App`;
  read each of the seven sites verbatim with surrounding context; read `SurfaceMainWindow`;
  `git blame` on `SurfaceMainWindow` and on all four of its call sites.
- **Found:**
  1. **The count is seven, not eight.** All seven cited line numbers resolve verbatim — no drift
     at all in the citations. There is no eighth `IsLoaded`-only site in the tree, and the row's
     own evidence list only ever held seven. The headline number was wrong at write time.
  2. **Four of the seven do not have the defect.** Each of `App.xaml.cs:984`, `:1008`, `:1594` and
     `:1622` is immediately followed by `SurfaceMainWindow(owner)` (at `:985`, `:1009`, `:1595`,
     `:1626`) and only then `window.ShowDialog()`. `SurfaceMainWindow` at `App.xaml.cs:2060-2071`
     calls `mainWindow.Show()` when `!mainWindow.IsVisible`, restores `WindowState` from
     `Minimized`, and `Activate()`s — so the owner is on screen before the modal is shown, and the
     "activation returns to an invisible owner" harm cannot arise. `git blame` puts
     `SurfaceMainWindow` at 2026-05-04 (`138a965e`) and all four calls in May 2026, three months
     before the row.
  3. **Genuinely exposed: three.** `LaunchHeadroomWindow.xaml.cs:74`,
     `EdgeRemediationWindow.xaml.cs:94`, `ConsentSheet.xaml.cs:46`. None calls `SurfaceMainWindow`;
     each goes straight to `ShowDialog()` (Consent) or to a `CenterScreen` else-branch.
  4. **The stale-comment sub-claim is correct.** `StopAllConfirmWindow.xaml.cs:33` still reads
     *"Owner resolution matches `LaunchHeadroomWindow.ShouldProceed`"* while `StopAllConfirmWindow`
     checks `&& owner.IsVisible` at `:45` and `LaunchHeadroomWindow` does not at `:74`.
- **Direction:** shrank — 8 claimed / 7 cited / 7 present, and of those, 3 actually exposed.
- **Note:** the mitigation is a code-path reading, not an on-screen one — I cannot run the app, so
  the observed behaviour of a tray-invoked Preferences dialog with the main window hidden is
  **CANNOT VERIFY**. The sequence in source is unambiguous though. If a cycle scopes "add
  `&& owner.IsVisible` at eight sites," it will be scoping four no-ops. There is a second, unnamed
  defect class adjacent to this one: `MainViewModel.cs:3597`, `:3650`, `:3657` and `:3664` assign
  `window.Owner = Application.Current.MainWindow` with **no guard at all** — no `IsLoaded`, no
  `IsVisible`, no null check. That is a throw risk, not just a focus one, and no row owns it.

---

### F-085 — DRIFTED

- **Row claims:** three un-themed literals at `MainWindow.xaml:1630-1644`, not the two spec §7
  names; the banner is invisible to both fences v1.17 shipped, "so nothing would catch a fourth one
  being added tomorrow."
- **Checked:** read `MainWindow.xaml:1620-1655`; counted every hex in the file; read
  `ThemedStatusColourTests`' XAML clause, its allow-list, and its ceiling; re-counted the ceiling
  myself; cross-read `docs/spec.md:85,683-684`.
- **Found:** **the count half is exact; the fence half is now false.**
  - Count: three, at `:1630` `Background="#3F3000"`, `:1631` `BorderBrush="#8F7000"`, `:1644`
    `Foreground="#FFE3A6"`. The `Border` spans `:1629-1647`. `docs/spec.md:683-684` records the same
    correction, and `docs/spec.md:85` already carries the current line numbers.
  - Fence: v1.17 item 3a — the item that closed F-088 — added
    `ThemedStatusColourTests.NoColourLiteralIsWrittenIntoAppXamlOutsideTheAllowList`
    (`ThemedStatusColourTests.cs:383`), which walks App **XAML**, not `*.cs`. The Bloxstrap banner
    is an explicit allow-list entry at `ThemedStatusColourTests.cs:290-295`, anchored on the comment
    string `"Bloxstrap warning banner"` with a 20-line span, citing F-085 by id. And the clause is a
    **ceiling**, not just an offender list: `AllowedXamlLiteralCeiling = 97` at `:340`, asserted at
    `:426`. I re-counted it independently — App.xaml 11 + MainWindow.xaml 8 + AboutWindow.xaml 10 +
    CookieCaptureWindow.xaml 14 + `Modals/` 54 = **97 exactly**. A fourth literal added inside that
    banner takes the count to 98 and turns the build red.
- **Direction:** count same (three, and correct); fence coverage grew from zero to one.
- **Note:** this is the sharpest item in the batch. **A row written on 2026-08-10 was falsified by
  another item in the same cycle, on the same day** — item 6 opened F-085 saying nothing watches
  this, and item 3a then built the thing that watches it, and wrote F-085's own id into its
  allow-list. The defect is real and the count is right; the "nothing would catch a fourth one"
  sentence is the part to strike before anyone quotes it as a reason to prioritise.

---

### F-086 — ACCURATE

- **Row claims:** the gate measures only elements declaring both `Background` and `Foreground`
  inline as `{DynamicResource}`; 44 elements, 8 distinct pairs, `MutedTextBrush` the foreground of
  none; commit `2c9ab16` (PR #100) merged the ninth pair away; `MinimumPairs` is 6 so the drop from
  9 to 8 announced nothing; the token has roughly 104 bindings.
- **Checked:** read `ContrastPairGateTests.cs:54-135` and `196-219`; lifted its two regexes
  (`:82-83`) and its file walk (`XamlStyleIntegrityTests.cs:168-181`) and **re-ran the whole scan
  independently in PowerShell**; re-counted `MutedTextBrush` occurrences by form and by file;
  cross-read `MutedTextFenceTests` and `FlatlineLabGateTests`.
- **Found:** the reproduction lands on the row's numbers exactly — **44 elements across 18 files,
  8 distinct pairs**, and `MutedTextBrush` appears as the foreground of **none** of them. The eight
  pairs, with counts, are: `CyanBrush/NavyBrush` 22, `MagentaBrush/WhiteBrush` 8,
  `NavyBrush/WhiteBrush` 5, `RowExpiredAccentBrush/NavyBrush` 3, `RowBgBrush/WhiteBrush` 2,
  `RowBgBrush/CyanBrush` 2, `BgBrush/WhiteBrush` 1, `NavyBrush/CyanBrush` 1. `MinimumPairs = 6` at
  `:73`, confirmed. The in-code "9 distinct pairs" claims are corrected — the file now says 8 in
  four places (`:65`, `:196`, `:207`, `:219`).
  **The 104 re-measures at 105**: `Foreground="{DynamicResource MutedTextBrush}"` appears 105 times
  across 21 XAML files (`PreferencesWindow` 15, `MainWindow` 12, `GamesWindow` 12, `PluginsWindow`
  12, `WelcomeWindow` 12, then a long tail). All `{DynamicResource MutedTextBrush}` forms in XAML:
  114. Plus 9 in code-behind. Zero `StaticResource` uses.
- **Direction:** grew by 1 (104 → 105).
- **Note:** one honest qualification on *"measured by nothing on any test run."* Two things do touch
  the token, neither of which weakens the row. `MutedTextFenceTests` polices its **role** (which
  controls may bind it as a `Foreground`) and never a ratio — and its own doc at `:24` cites F-086
  for exactly this reason. `FlatlineLabGateTests.cs:116` asserts White-vs-MutedText at 1.00:1, but
  only for the adversarial `flatline-lab` fixture, not for any shipped theme. So no shipped theme's
  muted-text ratio is measured anywhere, which is the row's actual substance.

---

### F-087 — ACCURATE

- **Row claims:** `ConsentSheet.xaml.cs:90-92` resolves `NamespaceBrush` via `TryFindResource` with
  hardcoded brand-hex `??` fallbacks; `ConsentSheet.xaml:92` binds it; the allow-list entry is at
  `ThemedStatusColourTests.cs:84-89`.
- **Checked:** read all three locations directly.
- **Found:** all three citations exact, to the line.
  - `ConsentSheet.xaml.cs:90-92` — `public Brush NamespaceBrush => IsHostEnforced ?
    (Brush)(Application.Current.TryFindResource("CyanBrush") ?? new SolidColorBrush(Color.FromRgb(0x17, 0xD4, 0xFA)))`
    and the `MagentaBrush` / `0xF2, 0x2F, 0x89` branch.
  - `ConsentSheet.xaml:92` — `Foreground="{Binding NamespaceBrush}"`.
  - `ThemedStatusColourTests.cs:84-89` — the allow-list entry, anchored on `"NamespaceBrush"`,
    reason inline, ending *"Wants a register row."* This row is that row.
- **Direction:** same.
- **Note:** the row states its own size honestly — fallback-only reach, sev 1 / vis 1, paints
  nothing in a running app. Nothing here needs re-scoping.

---

### F-090 — ACCURATE

Independently reproduced rather than read off the test.

- **Row claims:** `InteractiveEdgeBrush` is derived by `ContrastGuard.Ensure(theme.Navy,
  theme.Divider)` at `ThemeService.cs:211` — against Navy and only Navy; clears 3:1 there at
  3.0699 / 3.0194 / 3.0364 / 3.0258 and lands at 2.8244 / 2.8075 / 2.8149 / 2.2825 on `RowBg`;
  14 call sites, 8 on `RowBgBrush`, 6 on `NavyBrush`, two remaining matches being the style
  definitions; `RenderedStyleGateTests` pins the gap rather than exempting it.
- **Checked:** read `ThemeService.cs:200-255`; grepped every `InteractiveEdgeBrush` reference;
  enumerated `AppTextBoxStyle` / `AppPasswordBoxStyle` call sites and spot-checked their surfaces;
  read `ThemeStore.cs:202-267` for verbatim slot values; **re-implemented `ContrastGuard.Ensure`**
  (blend walk, snap-to-byte before the check) and computed the ratios myself; read the pin at
  `RenderedStyleGateTests.cs:563-602`.
- **Found:** every claim holds.
  - `ThemeService.cs:211` is `DerivedEdge: ContrastGuard.Ensure(theme.Navy, theme.Divider)`, exact.
    The apply path at `:252` is `EdgeRemediation.Resolve(decision, theme.Navy, theme.Divider)` —
    also Navy-only. "Against Navy, and only Navy" is right for both paths.
  - **My independent computation reproduces the row's ratios to four decimals:**

    | theme | derived edge | vs Navy | vs RowBg |
    |---|---|---|---|
    | brand | `#5E6B7C` | 3.0699 | **2.8244** |
    | midnight | `#5A626D` | 3.0194 | **2.8075** |
    | magenta-heat | `#6C5D70` | 3.0364 | **2.8149** |
    | flatline | `#606060` | 3.0258 | **2.2825** |

    Every built-in does ship `RowBg` lighter than `Navy`, and flatline is worst.
  - Call sites: 16 matches = **14 call sites + 2 style definitions** (`ControlStyles.xaml:91`,
    `:99`). Split **8 on RowBg** — `GamesWindow:326`, `:360`, `JoinByLinkWindow:46`,
    `RenameWindow:45`, `ExportAccountsWindow:108`, `:138`, `ImportAccountsWindow:51`, `:69` — and
    **6 on Navy** — `PluginsWindow:146`, `PreferencesWindow:312`, `:336`, `SquadLaunchWindow:58`,
    `CaptionColorPickerWindow:55`, `ThemeBuilderWindow:76`. Spot-checked both ends:
    `ImportAccountsWindow.xaml:51` sets `Background="{DynamicResource RowBgBrush}"` explicitly,
    `PreferencesWindow.xaml:312` and `:336` set `NavyBrush` explicitly.
  - The pin behaves as described: `RenderedStyleGateTests.cs:579` asserts `< EdgeThreshold`,
    `:587` asserts the theme is in the recorded table, `:592` asserts the rounded ratio equals the
    record. Widening, a new unrecorded theme, and closing the gap all fail it.
- **Direction:** same on every number.
- **Note:** the only thing I could not reproduce is the *pixel* provenance — the row says the
  numbers were sampled off a `RenderTargetBitmap`, and I cannot run tests. The arithmetic agrees to
  four decimals, which is the strongest corroboration available without running it. This row is
  measurement-grade as written; scope against it with confidence.

---

### F-091 — ACCURATE (both halves, binary scan included)

- **Row claims:** the plugin contract has no theme message and its only colour is a `color_hex`
  badge override at `:244`; `626labs.ur-task` 0.5.0 advertises "theming that follows the host" but
  reads `activeThemeId` out of the host's `settings.json` against its own hardcoded palette;
  the binary carries ids `brand`, `midnight`, `magenta-heat` and hexes `17D4FA`, `0F1F31`,
  `F22F89`, `3FB8D9`, `1A0F1F`, and carries no `flatline`, no `101010`, no `D4D4D4`; manifest pins
  `contractVersion 1.0` / `minHostVersion 1.4.3`.
- **Checked:** read `src/ROROROblox.PluginContract/Protos/plugin_contract.proto` and grepped it for
  theme/colour/palette; read the installed manifest; **reproduced the binary scan** over
  `%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-task\626labs.ur-task.dll` in both UTF-16LE and
  ASCII; read `activeThemeId` from the live `settings.json`; listed the user-themes directory.
- **Found:** every claim reproduces.
  - **Proto:** 271 lines, **zero** occurrences of "theme" in any case. The only colour token in the
    whole contract is `:244` `string color_hex = 2;  // optional override; default to brand cyan`,
    inside `RowBadgeSpec`. No theme message exists. Exact.
  - **Binary:** `activeThemeId` present (2 UTF-16 hits); ids `brand`, `midnight`, `magenta-heat`
    all present; **`flatline` absent (0 hits, both encodings)**; hexes `17D4FA`, `0F1F31`,
    `F22F89`, `3FB8D9`, `1A0F1F` all present; **`101010` and `D4D4D4` both absent**;
    `settings.json` present. Every single item on the row's list, confirmed.
  - **Manifest:** `version 0.5.0`, `contractVersion "1.0"`, `minHostVersion "1.4.3"`, and the
    description does say *"...an action bridge for sibling plugins, and theming that follows the
    host."* All four exact.
  - **Live state:** `settings.json` reads `"activeThemeId": "flatline"` — the condition Este was
    sitting in when he found it on the C2 walkthrough.
- **Direction:** same.
- **Note:** the user-theme half ("every user-authored theme is already broken in every plugin") is
  an inference from the id table rather than a direct observation, and it is sound — the binary
  holds three ids and nothing else, so any fourth id falls through to whatever its default branch
  is. It is not directly observable on this machine: the `themes/` directory is empty, so there is
  no user theme installed to demonstrate it with. The row's own framing ("adding `flatline` to the
  plugin's table is explicitly NOT the fix") is the right read of that.

---

### F-092 — ACCURATE (ratios re-derived, not taken on trust)

- **Row claims:** active `WhiteBrush` at 13.17:1 against the row under flatline, idle
  `MutedTextBrush` at 4.98:1, gap about 2.6:1; `TriggeredStatusColourGateTests` confirms all four
  states resolve to distinct values in every built-in; `CyanBrush` was rejected for active because
  it collides with `RowExpiredAccent` at 1.00:1 under flatline; `SecondaryStatusText` states all
  four states in words beside the dot.
- **Checked:** read `TriggeredStatusColourGateTests.cs:1-120` for `DotMapping`;
  `MainWindow.xaml:441-465` for the actual triggers and the adjacent text; `ThemeStore.cs` for
  flatline's verbatim slots; **computed all three ratios myself** from those slots.
- **Found:**
  - **Mapping, from source:** `DotMapping` is `green→White`, `yellow→RowExpiredAccent`,
    `magenta→Magenta`, `grey→MutedText`, restated in the test deliberately rather than read from the
    markup. The shipped markup agrees — `MainWindow.xaml:450` `green`, `:453` `yellow`, `:456`
    `magenta`, with grey as the `Setter` fallback documented at `:441`. So "active is `WhiteBrush`,
    idle is `MutedTextBrush`" is correct.
  - **Ratios, computed from `ThemeStore.cs` flatline (`White #F5F5F5`, `MutedText #989898`,
    `RowBg #2A2A2A`):** White on RowBg = **13.1655** (row: 13.17 ✓), MutedText on RowBg =
    **4.9757** (row: 4.98 ✓), active-vs-idle = **2.6460** (row: "about 2.6:1" ✓). All three land.
  - The Cyan/RowExpiredAccent collision is real in the slot values — flatline sets both `Cyan` and
    `RowExpiredAccent` to `#D4D4D4`, so they are 1.00:1 of each other by construction.
  - `TheFourDotStatesStayMutuallyDistinctInEveryTheme` exists at
    `TriggeredStatusColourGateTests.cs:692`.
  - `SecondaryStatusText` sits directly beside the dot at `MainWindow.xaml:463` (and at `:96` in the
    compact row), and `AccountSummaryTests` asserts distinct words for every state, so the
    redundancy argument holds.
- **Direction:** same.
- **Note:** contrary to the batch brief, the ratios did **not** need the test run — they re-derive
  arithmetically from the shipped slot values and reproduce exactly. The only thing genuinely
  unverifiable is the human observation *"I still can't tell that it's on"*, and that is a report
  rather than a measurement, so it needs no verification. For reference, the same pair in the other
  three built-ins: brand 15.33 / 6.33, midnight 14.68 / 4.19, magenta-heat 14.92 / 6.07 — so
  flatline is not the worst case for the idle end; midnight is. The row is scoped correctly as
  "nothing this cycle."

---

## Batch summary

| verdict | count | rows |
|---|---|---|
| ACCURATE | 8 | F-074, F-078, F-083, F-086, F-087, F-090, F-091, F-092 |
| DRIFTED | 4 | F-075, F-079, F-084, F-085 |
| PARTLY SHIPPED | 0 | — |
| ALREADY FIXED | 0 | — |
| SUPERSEDED | 0 | — |
| CANNOT VERIFY | 0 | — |

Nothing in this batch has silently shipped. No row is scoped against fiction the way F-001 was.
The failures here are of count and of evidence, not of existence — every one of the twelve defects
is still in the tree.

### The rows that most change the picture

1. **F-085 — a row written 2026-08-10 was falsified by another item in the same cycle, the same
   day.** Its count (three literals) is exact and stays. Its claim that both fences are blind to
   the banner, and that "nothing would catch a fourth one being added tomorrow," is false: item 3a's
   `NoColourLiteralIsWrittenIntoAppXamlOutsideTheAllowList` walks App XAML, allow-lists this exact
   banner by F-085's id, and enforces a ceiling of 97 that I re-counted and confirmed. Strike the
   sentence before it gets quoted as a reason to prioritise.

2. **F-084 overstates by more than one.** It says eight sites, lists seven, and the tree has seven —
   all seven citations resolving verbatim. More importantly, four of the seven call
   `SurfaceMainWindow(owner)` on the very next line, which shows and un-minimizes the owner before
   `ShowDialog()`, so the harm the row describes cannot occur there. Real exposure is **three**
   sites, not eight. Both errors predate the row by months. A cycle scoped at "eight one-line
   fixes" would be scoping four no-ops — and would still miss the four genuinely unguarded
   `Owner` assignments in `MainViewModel.cs:3597,3650,3657,3664`, which no row owns.

3. **F-075 is the worst pure drift.** All six MainWindow citations moved (+11 to +99 lines) and the
   glyph inventory grew from 7 named to 9 present. Because this row's entire function is to be a
   protective keep-list, the two it does not name — the Tools `▾` at `:1161`, added by wave 3's own
   merge, and the empty-state `☆` at `:1811`, present since May — are unprotected and unrecorded.

4. **F-079's file set is wrong in both directions.** `RenameWindow` is named and holds zero
   `Background="#"`; `DpapiCorruptWindow.xaml:37` holds one and is not named. The register knows
   half of this already — `ThemedStatusColourTests.cs:313-318` says so in a code comment — and
   nobody wrote it back into the row. Its "EdgeRemediationWindow is the only window in that folder
   speaking the new vocabulary" line is also stale: `LaunchHeadroomWindow` (PR #96, 2026-08-09) now
   uses two of the three styles.

### What held up under independent reproduction

Three rows were re-derived from scratch rather than read off their own tests, and all three landed
exactly:

- **F-086** — re-ran the gate's regexes over the same file walk: 44 elements, 18 files, 8 pairs,
  `MutedTextBrush` the foreground of none. Its "~104 bindings" re-measures at 105, up by one.
- **F-090** — re-implemented `ContrastGuard.Ensure` and computed the ratios from `ThemeStore.cs`
  slot values: 3.0699 / 3.0194 / 3.0364 / 3.0258 on Navy, 2.8244 / 2.8075 / 2.8149 / 2.2825 on
  RowBg. Four decimals, every theme. Call sites re-counted at 14, split 8/6 as claimed.
- **F-091 / F-092** — the plugin binary scan reproduced item for item, and F-092's three ratios
  re-derive to 13.1655 / 4.9757 / 2.6460.

### Incidental, outside this batch

Noted, not audited:

- **F-045 (open) has two dead citations.** It cites `MainWindow.xaml:1020` `"⚙ Settings"` — there is
  no `⚙` anywhere in `MainWindow.xaml` today (F-012, `clean`, removed it) — and `:302` for the die
  emoji, which now sits at `:317`.
- **F-069 (open) cites `MainWindow.xaml:302`** for `"🎲 Reroll identity"`; it is at `:317`. Note
  also that F-069's fix direction is "drop the glyph," which the **Rulings by the user** section
  closed as a class on 2026-08-04 — the same ruling that closed F-040. F-069 looks like it is
  sitting open against a decision already made, which is precisely the F-040 failure the register
  called out. Worth a deliberate look.
- **F-066 (open) verifies clean at its reconciled citations** — `MainWindow.xaml:1568` `#F1B232`,
  `:1572` and `:1575`. Its 2026-08-10 correction is accurate.
- **`MainViewModel.cs:3597, 3650, 3657, 3664`** assign `window.Owner = Application.Current.MainWindow`
  with no guard of any kind. Adjacent to F-084's class, owned by no row.
