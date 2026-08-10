# Re-verification — batch 1 (15 rows)

Repo: `<repo root>` · branch `main` @ `b474a2f` (v1.17.0.0) · 2026-08-10
Method: content-first location via Grep/Read, then record the current line. No build, no test run,
no app launch. Register read in full including Rulings / Absorbed / Refuted / Corrections / Notes.

Rows: F-013 F-018 F-019 F-020 F-021 F-022 F-023 F-024 F-026 F-027 F-033 F-034 F-037 F-038 F-039

---

### F-013 — DRIFTED
- **Row claims:** `MainViewModel.cs:3188-3547` every secondary-window opener is `window.ShowDialog()`; `PreferencesWindow.xaml.cs:43-55` documents DiscordConfig is safe only because the dialog is modal.
- **Checked:** `grep -n "ShowDialog\|\.Show()" MainViewModel.cs`; `grep -rn "\.Show();" src/ROROROblox.App --include=*.cs`; read `PreferencesWindow.xaml.cs:35-70`; `grep -n "PluginsWindow" MainViewModel.cs App.xaml.cs`.
- **Found:** Defect fully intact — **zero modeless secondary windows exist.** 11 `ShowDialog()` sites in `MainViewModel.cs`, at `:2092, :2350, :2482, :3299, :3307, :3313, :3573, :3598, :3651, :3658, :3665`. 41 `ShowDialog` hits app-wide. The only four `.Show()` calls in the whole app are the main window and tray: `App.xaml.cs:328, :330, :2064` and `AppLifecycle/SingleInstanceGuard.cs:168`. **Citation range is stale:** `:3188-3547` now spans only 3 of the 11 (Games `:3296-3299`, Diagnostics `:3304-3307`, About `:3310-3313`); openers now sit in two clusters, `:2077-2482` and `:3296-3665`. Plugins is opened from `App.xaml.cs:983`, not MainViewModel. The modal-safety comment resolves at **`PreferencesWindow.xaml.cs:44-56`** (row says `:43-55`, off by one), verbatim: *"which cannot run while this dialog is open because the dialog is modal. If Preferences ever becomes modeless, that becomes a real lost update and this whole scheme needs a single owner instead — it is the modality, not the code, that makes this safe today."*
- **Direction:** ShowDialog sites in MainViewModel — cannot compare to an audit number (row states no count). Modal islands: the toolbar collapsed from six buttons to Settings + Tools (F-009 clean), and Tools now fronts **six** modal destinations (History, Diagnostics, Games, Plugins, Welcome tour, About) plus the Settings button = 7 modal surfaces off two buttons. Same defect, re-shaped.
- **Note:** the "six modal islands off one toolbar" framing is now literally "seven modal surfaces off two buttons." The DiscordConfig single-owner prerequisite the row names is untouched and the code comment still states it as the load-bearing precondition.

---

### F-018 — DRIFTED
- **Row claims:** `TrayService.cs:203,215` Multi-Instance state + Stop all are tray-only. Reconciled 2026-08-09: Stop-all half shipped with F-001; Multi-Instance still tray-only, the one `multi-instance` hit being a comment at `MainWindow.xaml:1471`.
- **Checked:** `grep -n 'Header = "Multi-Instance: OFF"\|Header = "Stop all Roblox instances"' TrayService.cs`; `grep -ni "multi.instance\|multiinstance" MainWindow.xaml`; `grep -n "StopAll\|mutex\|ContestedBanner" MainWindow.xaml`; read the status bar at `MainWindow.xaml:1895-1945`.
- **Found:** `TrayService.cs:203` (`Header = "Multi-Instance: OFF"`) and `:215` (`Header = "Stop all Roblox instances"`) **still resolve exactly.** Stop all confirmed on the main window at `MainWindow.xaml:1193` (`Command="{Binding StopAllCommand}"`, Tools group). Multi-Instance state confirmed **still absent from the main window**: the sole `multi-instance` occurrence in `MainWindow.xaml` is a comment, now at **`:1564`** (row's reconciliation says `:1471` — drifted +93). The contested-lock banner is bound to `ContestedBannerText` at `:1542, :1566-1567` with a Retry at `:1573`; that is the banner copy the reconciliation described, not a state indicator. Status-bar cells are: live-process dot + `LiveProcessSummary`, the Compact toggle, an intentionally empty column 2 (F-011's removal, documented inline), and the minimize-to-tray hint. No Multi-Instance cell.
- **Direction:** same — one half shipped, one half open, exactly as the row's reconciliation states.
- **Note:** substance is correct; only the reconciled `MainWindow.xaml` line moved. Remaining scope is one status-bar cell.

---

### F-019 — PARTLY SHIPPED
- **Row claims:** `PreferencesWindow.xaml:37-330` one ScrollViewer, fixed NoResize; 19 sequential focus stops, no headings/groups; 3 section labels plain Text; RowBgBrush vs BgBrush = 1.09:1 brand.
- **Checked:** full read of `PreferencesWindow.xaml` (461 lines); `grep -rn "AutomationProperties" src/ROROROblox.App`; `ThemeStore.cs:202-267` slot values, ratio computed by hand.
- **Found:** the Settings shell shipped (F-002/F-003 clean) and it moved several of this row's legs.
  - **`NoResize` is gone.** `PreferencesWindow.xaml:7-8` now reads `Height="640" Width="860" MinHeight="480" MinWidth="760"`; no `ResizeMode` attribute anywhere in the file. That half of the evidence is dead.
  - **One ScrollViewer still, but it now wraps five nav pages,** at `:85-447`. The rail is a `ListBox` at `:72-83` with five items (`Startup`, `Accounts`, `Alerts & memory`, `Discord`, `Appearance`), pages `PageStartup :87`, `PageAccounts :124`, `PageAlerts :206`, `PageDiscord :363`, `PageAppearance :395`, four of them `Visibility="Collapsed"`.
  - **Focus stops re-counted.** 23 declared focusable controls now exist (rail 1, Startup 2, Accounts 4, Alerts 10, Discord 2, Appearance 3, Close 1). Collapsed pages are out of the tab order, so the **sequential run a user actually faces is 4-12, worst case 12 on Alerts & memory** — down from the audited 19. The rail is itself the group-to-group mechanism the fix direction asked for.
  - **Section labels still plain Text: 3** at card level — "Accounts" `:179-181`, "Alerts" `:251-252`, "Theme" `:400-402`, all hand-rolled 13px SemiBold White. `SectionHeadingStyle` **exists** (`Controls/ControlStyles.xaml:143`) and Preferences uses it **nowhere**; its only consumer app-wide is `DiagnosticsWindow.xaml.cs:98`.
  - **`AutomationProperties` is still effectively absent app-wide** — 2 hits total, both in `MainWindow.xaml` (`:1159` comment, `:1162` `AutomationProperties.Name="Tools"`). No card Border carries a role or a name.
  - **1.09:1 brand is still exact.** `ThemeStore.cs:208` `Bg: "#0F1F31"`, `:214` `RowBg: "#15263A"` → 1.087:1. Confirmed by hand.
- **Direction:** declared controls **grew** 19 → 23; the sequential run any one user faces **shrank** 19 → 12 worst case. Section labels **same** at 3.
- **Note:** roughly half the fix direction is delivered — real containers per group, a keyboard rail between them, and resizability. What remains is the accessible-naming layer (which is F-052's territory) and adopting `SectionHeadingStyle` on the three card headings. **CANNOT VERIFY on-screen:** what a screen reader announces for the rail + card structure needs a running app; the code evidence above is decisive only about what is declared.

---

### F-020 — ACCURATE
- **Row claims:** `SquadLaunchWindow.xaml.cs:59,73,79` read/write a global persisted preference reachable only from inside the modal it modifies.
- **Checked:** read `SquadLaunchWindow.xaml.cs:45-95`; `grep -n "CarefulSquadLaunchAsync"`; `grep -rn "Squad" PreferencesWindow.xaml PreferencesWindow.xaml.cs`.
- **Found:** all three citations resolve **exactly**. `:59` `CarefulModeToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();` (in `OnLoaded`), `:73` `await _settings.SetCarefulSquadLaunchAsync(...)`, `:79` the rollback re-read in the catch. `CarefulSquadLaunch` is a persisted `SettingsBlob` field (`AppSettings.cs:481`). Zero Squad-Launch references anywhere in `PreferencesWindow.xaml` or its code-behind — the mirror the fix direction asks for does not exist.
- **Direction:** n-a (no count in the row).
- **Note:** the Settings shell now has a five-page rail with no launch page. The fix direction says "the Settings shell's launch page" — that page does not exist, so the fix is a new page or a home on an existing one, not a one-line binding.

---

### F-021 — DRIFTED
- **Row claims:** empty-state copy "Use the Squad Launch toolbar button to add one." — reconciled to `Games/GamesWindow.xaml:404`; the server actually saves itself automatically at `SquadLaunchWindow.xaml.cs:367`.
- **Checked:** `grep -n "Use the Squad" GamesWindow.xaml` + surrounding read `:395-415`; `grep -n "_store.AddAsync" SquadLaunchWindow.xaml.cs` + read `:355-385`.
- **Found:** the reconciled citation is **exact** — `Games/GamesWindow.xaml:404` still carries `Text="Use the Squad Launch toolbar button to add one."` inside `ServersEmptyState` (`:400-409`), under "No saved private servers." at `:401`. The auto-save half has drifted: it is now at **`SquadLaunchWindow.xaml.cs:369`** (`var saved = await _store.AddAsync(ps.PlaceId, ps.Code, ps.Kind, placeName, placeName, thumbnail);` followed by `TouchLastLaunchedAsync` at `:370`), row says `:367`.
- **Direction:** same — one string, unchanged.
- **Note:** cheapest row in the batch. One `Text=` attribute; the replacement copy is already written in the fix direction.

---

### F-022 — ACCURATE
- **Row claims:** `MultiInstanceCopy.cs:23-26` — 45 words leading with diagnosis, actionable clause buried last; banner is conditional and dismissible.
- **Checked:** full read of `MultiInstanceCopy.cs` (27 lines); `grep -n -i "Dismiss" MainWindow.xaml`; read `MainWindow.xaml:1578-1596`.
- **Found:** citation resolves **exactly** — `FpsCapMismatchBanner` declared at `:23`, string body `:24-26`. Copy still leads "Different FPS caps will slow your launches." and still buries "Set every account to the same cap to launch at full speed." as the final clause. Conditional + dismissible confirmed: `MainWindow.xaml:1587` `Visibility="{Binding FpsCapWarningText, ...}"`, Dismiss button at `:1589-1590` bound to `DismissFpsCapWarningCommand`.
- **Direction:** word count **grew** slightly — **47 words**, not 45 (counting the em-dash as punctuation).
- **Note:** one caveat on the row's own framing. The first sentence *is* a consequence ("will slow your launches"), not pure mechanism, so "unpacks before the verdict" overstates it a little. The buried-instruction complaint holds intact.

---

### F-023 — DRIFTED
- **Row claims:** `AppSettings.cs:277-347` persists 4 watchdog settings; `IAppSettings.cs:104` "no dialog wiring exists yet"; zero `.xaml` hits.
- **Checked:** `grep -n "public async Task.*Memory\|.*Projection" AppSettings.cs`; read the `SettingsBlob` record `:473-488`; `grep -rn "MemoryWatchdogEnabled\|MemoryReserveMb\|MemoryCapMb\|ProjectionWarnMinutes" src/ROROROblox.App --include=*.xaml --include=*.xaml.cs`; `grep -n "dialog wiring" IAppSettings.cs`.
- **Found:** defect fully intact, evidence range stale. The four watchdog accessors now span **`AppSettings.cs:322-392`** — `MemoryWatchdogEnabled` `:322/:329`, `MemoryReserveMb` `:340/:347`, `MemoryCapMb` `:358/:365`, `ProjectionWarnMinutes` `:376/:383`. The cited `:277-347` covers only the first two. Record fields confirmed at `:483-486`. **Zero `.xaml` hits confirmed** — those four names appear only in the composition root (`App.xaml.cs:1136, :1151, :1162, :1171-1175`), never in markup. `PreferencesWindow.xaml:280`'s `MemoryWarningDestination` is the alert *routing* combo, not a watchdog threshold. The "no dialog wiring" language is at **`IAppSettings.cs:105`** ("File-only today — there is no Preferences dialog wiring for this or the three memory settings below it"), with the row's verbatim phrase "dialog wiring exists yet" repeating at `:118, :128, :136`.
- **Direction:** settings count **same** at 4; accessor range moved +45 lines.
- **Note worth the builder's attention:** the fix direction's destination now exists and is *already named for it*. `PreferencesWindow.xaml:80` is a nav item literally reading **"Alerts & memory"**, and its page holds only alert routing. The memory half of that page's own name is unimplemented.

---

### F-024 — DRIFTED
- **Row claims:** `MainWindow.xaml:282-291` mute persists per-account (`MainViewModel.cs:2883`); Alerts card (`:168-251`) never mentions muted accounts.
- **Checked:** `grep -n -i "mute" MainWindow.xaml` + read `:290-310`; `grep -n "SetAlertsMutedAsync" MainViewModel.cs`; `grep -rn -i "unmute\|MutedAccountCount\|MutedAccountIds" src/`; `grep -n -i "mute" PreferencesWindow.xaml*`.
- **Found:** defect fully intact; **all three citations moved.**
  - Per-account mute MenuItem is now at **`MainWindow.xaml:297-306`** with its rationale comment at `:292-296` (row says `:282-291`, drifted +10).
  - `SetAlertsMutedAsync` is now at **`MainViewModel.cs:2977`**, doc comment `:2970-2976` (row says `:2883`, drifted +94). Command wiring at `:235-238`, declaration at `:607`.
  - The Alerts card is now at **`PreferencesWindow.xaml:249-360`** on the "Alerts & memory" page (row says `:168-251`).
  - Confirmed absent: no muted-account count, no unmute-all, anywhere. `MutedAccountIds` exists only at `Core/Discord/DiscordConfig.cs:25`, consumed by `AlertRouter.cs:43` and hydrated into rows at `App.xaml.cs:1376`. Preferences' only "mute" is `MuteIdleAlertsToggle` (`:211`), an unrelated global.
- **Direction:** same — no count in the row.
- **Note:** the fix direction's "data already in the VM" claim still holds; `App.xaml.cs:1376` already materializes the muted set at startup.

---

### F-026 — DRIFTED
- **Row claims:** 8 sibling cards — 5 hold one setting, 3 (incl. Alerts, 13 controls) at identical weight. Reconciled 2026-08-09: still open, `Controls/ControlStyles.xaml:119-120` says so; 9 card borders under a 5-page nav rail; effort now a one-place edit.
- **Checked:** full read of `PreferencesWindow.xaml`; `grep -c "<Border"` and `grep -n "CardBorderStyle"`; read `ControlStyles.xaml:110-150`; `grep -rn "SectionHeadingStyle"`.
- **Found:** structural claim intact, every count moved.
  - **9 `CardBorderStyle` borders confirmed** at `:88, :105, :131, :159, :177, :209, :249, :366, :398`. The reconciled figure of 9 holds; the audit's 8 does not.
  - Contents per card: `:88` 1 control · `:105` 1 · `:131` 2 · `:159` 1 · `:177` 2 · `:209` 2 · `:249` **10** · `:366` 2 · `:398` 3.
  - So **3 cards hold a single control**, not 5. The **Alerts card holds 10 focusable controls**, not 13 (counting method: named focusable elements — CheckBox/ComboBox/TextBox/ToggleButton/Button).
  - The weight collision the row is actually about is **unchanged**: a one-checkbox card at `:105` and the ten-control Alerts card at `:249` both wear the identical `CardBorderStyle`.
  - **Reconciled citation is stale.** The "not smuggled into wave 6 / one-place edit instead of a nine-place one" passage now sits at **`ControlStyles.xaml:127-129`**, not `:119-120`. `:119-120` currently lands mid-comment-header.
- **Direction:** cards **grew** 8 → 9 · single-setting cards **shrank** 5 → 3 · Alerts card controls **shrank** 13 → 10.
- **Note (matters for scoping):** `SectionHeadingStyle` shipped at `ControlStyles.xaml:143` and **Preferences does not use it** — its only consumer app-wide is `DiagnosticsWindow.xaml.cs:98`. The row's fix direction ("a section needs a level above the card") now has half its vocabulary sitting unused in the dictionary. Also: the nav rail already supplied the level above the card, so what is genuinely left is the card-vs-row distinction *inside* a page, which is a smaller job than the row's original framing implies.

---

### F-027 — ALREADY FIXED
- **Row claims:** Cyan/slash/white lockup reused 9x with 6 different second-token meanings; retire it from page chrome.
- **Checked:** `grep -rn 'Text=" / "' src/ROROROblox.App --include=*.xaml`; `grep -rn "ShowWordmark"`; `grep -rn "controls:PageHeader"`; full read of `Controls/PageHeader.xaml`; read `AboutWindow.xaml:58-80`, `WelcomeWindow.xaml:30-50` and `:138-152`, `JoinByLinkWindow.xaml:20-40`.
- **Found:** the defect is gone. The three-part cyan/magenta/white lockup now renders at exactly **two sites**, with **two** second-token meanings:
  1. `MainWindow.xaml:1078` — `<controls:PageHeader ShowWordmark="True" Heading="Accounts" />`
  2. `AboutWindow.xaml:60-71` — "RoRoRo / ⟨version⟩" at 24px, still its own markup.
  Every other page header goes through the shared `PageHeader` control across **11 windows** (`DiagnosticsWindow:25`, `FriendFollowWindow:24`, `GamesWindow:312`, `SessionHistoryWindow:24`, `MainWindow:1078`, `PluginsWindow:67`, `PreferencesWindow:63`, `SquadLaunchWindow:27`, `ThemeBuilderWindow:23`, `ExportAccountsWindow:28`, `ImportAccountsWindow:26`), where the wordmark is **off by default** — `PageHeader.xaml:30` sets `Visibility="Collapsed"` and only a `ShowWordmark=True` DataTrigger reveals it. Size is fixed at 22px, grammar fixed to Heading + optional one-line Descriptor.
  The only other `Text=" / "` in the app is `WelcomeWindow.xaml:145`, which is body prose inside a tour paragraph ("In batches / Skipped"), not the lockup.
- **Direction:** lockup sites **shrank** 9 → 2 · second-token meanings **shrank** 6 → 2.
- **Note:** the fix direction says "About, splash, tray only" and MainWindow still carries it — but that is deliberate and later: **F-004 is `clean` and its fix direction reads "reserve two-tone wordmark for MainWindow + About."** `PageHeader.xaml:17-20` states the same rule in a comment citing F-004. The two surviving sites are exactly the two F-004 reserved. This row should close. The residual half-duo headers at `JoinByLinkWindow.xaml:27-33` and `WelcomeWindow.xaml:38-43` are **F-070's** rows, not this one.

---

### F-033 — DRIFTED
- **Row claims:** `MainViewModel.cs:663` plain SetField property; nothing writes it to disk; SettingsBlob has no compact field.
- **Checked:** `grep -n "IsCompact\|_isCompact\|Compact" MainViewModel.cs`; `grep -n -i "compact" AppSettings.cs IAppSettings.cs MainWindow.xaml.cs`; read `SettingsBlob` at `AppSettings.cs:473-488`.
- **Found:** defect **fully intact**; citation moved. `_isCompact` backing field is now at **`MainViewModel.cs:692`**, the property at **`:695-709`**, with `if (SetField(ref _isCompact, value))` at `:700` — a plain in-memory SetField with four `OnPropertyChanged` fan-outs and no persistence call. `ToggleCompact()` at `:3633` is `IsCompact = !IsCompact`. **Zero** `compact` matches in `AppSettings.cs`, `IAppSettings.cs`, or `MainWindow.xaml.cs`, so nothing writes it and nothing restores it. `SettingsBlob` (`:473-488`) confirmed to have no compact field.
- **Direction:** same — no count in the row.
- **Note:** the fix direction ("Add CompactMode bool to SettingsBlob, no migration needed") is still exactly right; `AppSettings.cs:463-465` documents that the blob's defaulted fields load cleanly with no migration step, which confirms the "no migration needed" claim in code.

---

### F-034 — ACCURATE
- **Row claims:** reconciled 2026-08-09 to three remaining leak sites — `TrayService.cs:98-101` (tooltip, all three states), `:221` ("Open ROROROblox"), `DiagnosticsWindow.xaml.cs:222`; the fourth (`FriendFollowWindow`) is fixed.
- **Checked:** `grep -n "ROROROblox" TrayService.cs DiagnosticsWindow.xaml.cs`; `grep -n "Title" FriendFollowWindow.xaml.cs`.
- **Found:** all three citations resolve **exactly**.
  - `TrayService.cs:98-101` — the tooltip switch, four arms: `"ROROROblox — Multi-Instance ON"`, `OFF`, `ERROR (mutex lost)`, and the `_ => "ROROROblox"` default.
  - `TrayService.cs:221` — `var open = new MenuItem { Header = "Open ROROROblox" };`
  - `DiagnosticsWindow.xaml.cs:222` — `writer.WriteLine($"ROROROblox support snapshot");`
  - Fourth site confirmed fixed: `FriendFollowWindow.xaml.cs:138` now reads `Title = $"Friends — {ChromeName(current)}"`.
- **Direction:** leak sites **shrank** 4 → 3, exactly as reconciled.
- **Note:** one adjacent occurrence that is correctly *not* a leak — `DiagnosticsWindow.xaml.cs:141` uses `"ROROROblox"` as the `%LOCALAPPDATA%` folder name, which is a path identifier, not user-facing copy. Do not sweep it with a blind find-replace.

---

### F-037 — DRIFTED
- **Row claims:** accent fill on Send test, "+ Build a theme...", and Close in Preferences; the same cyan Close is loudest on About/History/Plugins; Diagnostics reverses it.
- **Checked:** read `PreferencesWindow.xaml:340-460`; `grep -rn 'Content="Close"' src --include=*.xaml -A6`; read `ControlStyles.xaml:20-70`.
- **Found:** the three Preferences sites resolve **exactly** and all three still carry accent fill:
  - `Send test` `:349-351` — `Background="{DynamicResource MagentaBrush}"`, `BorderThickness="0"`, SemiBold.
  - `+ Build a theme...` `:423-431` — `Background="{DynamicResource MagentaBrush}"`, `BorderThickness="0"`, SemiBold.
  - `Close` `:451-458` — `Background="{DynamicResource CyanBrush}"`, `BorderThickness="0"`, `IsDefault="True"`, and still the loudest control on the page.
  Cross-surface census re-run over all 9 Close buttons:
  - **Accent-filled cyan + IsDefault (5):** `AboutWindow.xaml:134`, `SessionHistoryWindow.xaml:58`, `PluginsWindow.xaml:374`, `PreferencesWindow.xaml:451`, `CaptionColorPickerWindow.xaml:82`.
  - **Secondary (4):** `DiagnosticsWindow.xaml:60`, `FriendFollowWindow.xaml:72`, `GamesWindow.xaml:413`, `SquadLaunchWindow.xaml:96` — all `SecondaryStrongButtonStyle`.
  Confirmed *not* a fourth Preferences accent site: `Export accounts…` `:188` uses `PrimaryButtonStyle`, which is Navy fill + cyan edge (`ControlStyles.xaml:23-28`), not an accent fill.
- **Direction:** cyan Closes **grew** 4 → 5 (CaptionColorPicker joined) · secondary Closes **grew** 1 → 4 (FriendFollow, Games, SquadLaunch joined Diagnostics).
- **Note:** the split moved from 4-loud/1-quiet to 5-loud/4-quiet. Half the app now does what the fix direction asks; the inconsistency is now closer to an even split than a lone outlier, which changes how the fix reads — it is a decide-and-sweep, not a fix-the-one-window.

---

### F-038 — DRIFTED
- **Row claims:** `SessionHistoryWindow.xaml.cs:65-72` swallows read/Clear failures to the same empty-state copy; a failed Clear looks identical to a successful one; Diagnostics models this correctly (`:29-39`).
- **Checked:** read `SessionHistoryWindow.xaml.cs:45-110` and `:355-385`; read `SessionHistoryWindow.xaml:30-50`; read `DiagnosticsWindow.xaml.cs:25-45`.
- **Found:** the read half is **exact**. `ReloadAsync` at `:62`, `try { rows = await _store.ListAsync(); } catch { rows = []; }` at **`:65-72`**, then `_hasData = true` and `RenderRows()` shows `EmptyState` (`SessionHistoryWindow.xaml:35-48`) reading "No launches yet." / "Click Launch As on any account and you'll see entries here." An unreadable history file presents as a never-used one. Confirmed.
  **The Clear half of the claim is wrong as written.** `OnClearClick` is at `:361-382`; the `try` block wraps *both* `await _store.ClearAsync()` **and** `await ReloadAsync()` (`:374-376`), with `catch { // best-effort }` at `:377-380`. So a failed Clear does **not** reload — the old rows stay on screen, silently. A failed Clear looks like *nothing happened*, not like a success. Both are defects; the row's stated symptom is not the one in the tree.
  The Diagnostics comparison holds: `DiagnosticsWindow.xaml.cs:27-40` distinguishes "Collecting..." / captured-at / `Couldn't collect diagnostics: {ex.Message}` — three distinct states in a StatusText. Row cites `:29-39`, close enough (the block is `:27-40`).
- **Direction:** same — no count in the row.
- **Note:** the corrected Clear symptom is arguably worse for the user than the one the row describes, and it needs different copy than a failed-read message. Worth writing into the row before scoping.

---

### F-039 — ACCURATE
- **Row claims:** `WelcomeWindow.xaml.cs:26-38` one-shot sentinel; sole documentation of 6 unlabelled row affordances. Reconciled 2026-08-09: half shipped with F-001 — Tools entry exists, sentinel bug fixed; the **About** entry is still outstanding and is the only reason it stays open.
- **Checked:** read `WelcomeWindow.xaml.cs:1-100`; read `MainWindow.xaml.cs:101-116`; read the whole Tools menu at `MainWindow.xaml:1162-1210`; read `AboutWindow.xaml:95-140`; `grep -rn "ShowTour"`.
- **Found:** every claim in the reconciled row checks out.
  - `WelcomeWindow.xaml.cs:26-38` resolves **exactly** to `IsFirstRun()` (sentinel probe over `%LOCALAPPDATA%\ROROROblox\.welcome-shown`).
  - **Sentinel bug fixed:** `MainWindow.xaml.cs:112-114` — `MarkShown()` now sits *inside* the `ShouldShowOnStartup(IsFirstRun(), mvm.Accounts.Count)` branch, so an upgrading user with accounts no longer burns it.
  - **Tools entry shipped:** `MainWindow.xaml:1202-1204`, `<MenuItem Header="Welcome tour" Command="{Binding ShowWelcomeTourCommand}" ToolTip="Replay the first-run tour of the account row." />`, routed via `MainViewModel.cs:578` → `WelcomeWindow.ShowTour()` (`WelcomeWindow.xaml.cs:82-97`, which also carries the `owner.IsVisible` guard F-084 describes).
  - **About entry confirmed still absent.** `AboutWindow.xaml` Grid.Row 2 (`:105-131`) holds only three hyperlinks — repo, Report an issue, Open log folder — plus DPAPI and trademark notes. No tour affordance. Footer is a single Close at `:134`.
- **Direction:** outstanding items **shrank** 3 → 1 (Tools entry + sentinel fix landed; About entry remains).
- **Note for scoping:** this row's sev 4 / vis 3 is now badly out of line with its remaining work. What is left is **one `<Hyperlink>` or `<Button>` in `AboutWindow.xaml`** bound to the command that already exists (`ShowWelcomeTourCommand`). Cheapest close in the batch after F-021.

---

## Batch summary

| verdict | count | rows |
|---|---|---|
| ACCURATE | 4 | F-020, F-022, F-034, F-039 |
| DRIFTED | 9 | F-013, F-018, F-021, F-023, F-024, F-026, F-033, F-037, F-038 |
| PARTLY SHIPPED | 1 | F-019 |
| ALREADY FIXED | 1 | F-027 |
| SUPERSEDED | 0 | — |
| CANNOT VERIFY | 0 (1 partial) | F-019's on-screen/screen-reader half |

**Rows that most change the picture, in order:**

1. **F-027 should close.** The lockup went 9 sites / 6 meanings → 2 sites / 2 meanings, and the two survivors are exactly the two `F-004` (clean) reserved by name. `PageHeader` fixed the size and grammar across 11 windows. Nobody flipped the row. This is the F-001 pattern again.
2. **F-019 is roughly half-delivered by the Settings shell.** `NoResize` is gone, the rail supplies group-to-group keyboard movement, and the worst-case linear focus run dropped 19 → 12. What is left is the naming layer, which is F-052's job, plus adopting the `SectionHeadingStyle` that already exists.
3. **F-039 is one `<Hyperlink>` from closing** and is still carrying a sev 4.
4. **F-038's Clear-failure symptom is wrong in the register.** The tree does not do what the row says; a failed Clear leaves stale rows and says nothing, rather than mimicking success. Different copy fix.
5. **Worst drift: F-024** — all three citations moved, `MainViewModel.cs:2883` → `:2977` (+94 lines), and the Alerts card moved file-region entirely (`:168-251` → `PreferencesWindow.xaml:249-360`). Runner-up **F-013**, whose cited range `MainViewModel.cs:3188-3547` now contains 3 of 11 opener sites, and **F-018**, whose *reconciled-yesterday* `MainWindow.xaml:1471` is already `:1564`.
6. **F-023's fix destination now exists and is already named for it** — `PreferencesWindow.xaml:80` is a nav item reading "Alerts & memory" whose page has no memory controls.
7. **F-026's effort has dropped again beyond what the reconciliation recorded** — the nav rail supplied the level above the card, and `SectionHeadingStyle` is sitting unused.

**Counts re-measured, with direction:**

| row | metric | audit | now | direction |
|---|---|---|---|---|
| F-019 | declared focusable controls | 19 | 23 | grew |
| F-019 | worst-case sequential focus run | 19 | 12 | shrank |
| F-019 | plain-Text section labels | 3 | 3 | same |
| F-022 | banner word count | 45 | 47 | grew |
| F-023 | watchdog settings persisted | 4 | 4 | same |
| F-026 | Preferences card borders | 8 | 9 | grew |
| F-026 | cards holding one control | 5 | 3 | shrank |
| F-026 | Alerts-card controls | 13 | 10 | shrank |
| F-027 | lockup sites | 9 | 2 | shrank |
| F-027 | second-token meanings | 6 | 2 | shrank |
| F-034 | repo-name leak sites | 4 | 3 | shrank |
| F-037 | cyan-filled Close buttons | 4 | 5 | grew |
| F-037 | secondary Close buttons | 1 | 4 | grew |
| F-039 | outstanding fix items | 3 | 1 | shrank |

---

## Incidental (outside batch — flagged, not audited)

- **F-052 is factually wrong now.** Its evidence reads "grep for `AutomationProperties`... across src/ returns zero." There are 2 hits in `MainWindow.xaml` — a comment at `:1159` and a live `AutomationProperties.Name="Tools"` at `:1162`, added with the Tools drop-down. Two hits is still effectively no naming layer, but the row's claim as written is refutable on one grep, and it is `open` at sev 4.
- **F-070** still resolves exactly: `JoinByLinkWindow.xaml:27-33` (cyan + white, no magenta) and `WelcomeWindow.xaml:38-43` (white + cyan, no magenta). Both un-migrated to `PageHeader`.
- **F-041** ("nine Close buttons across five paddings") — 9 Close buttons confirmed; observed paddings `20,8` / `22,8` / `16,8` / `14,8`. Broadly holds; a precise padding recount would settle "five."
- **F-047** is `clean` and `SectionHeadingStyle` exists at `ControlStyles.xaml:143`, but it has exactly **one** consumer app-wide (`DiagnosticsWindow.xaml.cs:98`). Preferences' three card headings are still hand-rolled. Not a reopen — just worth knowing the style shipped without adoption.
