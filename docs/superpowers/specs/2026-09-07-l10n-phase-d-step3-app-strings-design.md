# Localization Phase D, step 3 — the App-side composed strings + live toggle

**Date:** 2026-09-07
**Status:** design, pending implementation-plan
**Predecessors:** Phase C (static XAML → `{loc:Loc}`, #158), Phase D foundation (plural engine + `TranslationSource`/`Loc`/`LocExtension`, #162/#163), Phase D Core refactor (items 1–2: `CoreMessageCatalog` resx-backed + all 7 raw-prose composers migrated, #164–#171).

## 1. Goal

Localize the code-composed App layer — the ~425 strings a person reads in a window that are built in C# (ViewModels, page/window code-behind, Preferences, formatters) rather than declared in XAML — and make the **dynamic** layer switch language **live** on the picker toggle, the same way the static layer already does. This closes the "half-English screen" the v1.26 smoke exposed and is the last code work before step 4 (translation) and v1.27.

Non-goal: translating the new keys (step 4). Non-goal: re-touching the Core layer (done) or the static XAML (done).

## 2. What the survey found (2026-09-07, four read-only sweeps)

| Area | Must-localize | Displayed-VM | Momentary | Plural |
|---|---|---|---|---|
| `ViewModels/` | 112 | 35 | 77 | 25 |
| code-behind A — `History` `Games` `Diagnostics` `SquadLaunch` `Startup` | 119 | 59 | 60 | 3 |
| code-behind B — `Friends` `JoinByLink` `Transport` `Modals` `Tray` `Shell` `Discord` `Plugins` | 132 | 78 | 54 | 5 |
| `Preferences/` + `Theming/` | 88 | 24 | 64 | 6 |
| **raw** | **451** | **196** | **255** | **~40** |

Net ≈ **425 distinct** after cross-group duplicates (`"Couldn't save name change. Disk error?"` ×4, `"That {kind} isn't saved any more."` ×5, `"Place {0}"` fallback ×5, relative-time phrases shared by `AccountSummary` and `SquadLaunchWindow`, etc.). Heaviest single files: `MainViewModel.cs` (61), `SettingsPage.xaml.cs` (52), `GamesPage.xaml.cs` (32), `DiagnosticsPage.xaml.cs` panel (32), `SquadLaunchWindow.xaml.cs` (31), `AccountSummary.cs` (23), `PluginCapability.cs` (21), `PluginsViewModel.cs` (21).

## 3. Design

### A. The live-refresh mechanism (Option 1, approved)

A **long-lived surface that is displaying composed text at the moment of a toggle** subscribes once to the culture signal and refreshes its bindings:

```csharp
// ctor of a long-lived VM / code-behind:
TranslationSource.Instance.CultureChanged += OnCultureChanged;
private void OnCultureChanged(object? s, EventArgs e) => OnPropertyChanged(string.Empty); // "all changed"
// unsubscribe in the existing teardown (MainViewModel.StopPeriodicRefresh) to avoid leaking across tests.
```

In practice the toggle is on the **Settings** language picker, so the only surfaces open-and-displaying at that instant are the **shell** (behind Settings) and **Settings itself**. Those get the wiring. Every other window/modal/page is constructed (or navigated to) on demand and therefore reads the current culture at construction — **no subscription**, just resx extraction.

**The cached-field trap** (from the survey — this is the real work of the shell batch): a composed value stored in a *field* and set by a handler/ticker (`MainViewModel._idleSummaryText`, `_fpsCapWarningText`, `_contestedBannerText`; `AccountSummary._memoryText`, `_statusText`) will show stale language after a bare re-raise, because nothing recomputes it. Each such field must become a **computed getter** (recomposes on read) or be **recomposed inside `OnCultureChanged`**. Getters that already recompute (`SecondaryStatusText`, `IdleText`, `DefaultGameDisplay`, `DefaultGameTooltip`, `CompactToggleLabel`, `LiveProcessSummary`) are safe as-is. Transient `_statusBanner` (set at an event, momentary) is acceptable to leave.

### B. Category treatment — every string lands in one bucket

1. **Displayed on a long-lived surface** (shell rows/footer/status, Settings summaries/status) → resx + computed getter + `CultureChanged`. *Flips live.*
2. **On-demand window/modal/page** (Friends, Join, Transport, Diagnostics, Plugins, SquadLaunch, Theming windows, About) → resx only; reads culture at construction. *No subscription.*
3. **Momentary** (`$"Couldn't…: {ex.Message}"` toasts/status/MessageBox set in a handler or catch) → resx template (`"Couldn't save: {0}"`, `{0}=ex.Message`) only; composed fresh when it fires. *No re-raise.*
4. **Plural-forked** → `Key_one`/`_other` (+ `_few`/`_many` for ru/pl) via `Loc.Plural`. ~40 constructs; the densest cluster (idle, relative-time, client/expired/eligible counts) is in the shell.

### C. Batching — shell first, then by cohesive area

One coherent area per PR, each build-green + merged before the next, same rhythm as the Part B composers. The **shell goes first** because it is the most-visible surface, carries the cached-field live-toggle work, and holds the bulk of the plural families — it establishes the pattern every later batch copies. Order:

1. **Shell foundation** (pattern-setter) — `MainViewModel` displayed props + `AccountSummary` + `IdleSummary` + `MemoryChipFormatter` + `MultiInstanceCopy`: the `CultureChanged` wiring, the cached-field→computed-getter conversions, and the shell plural families (idle, relative-time, "N Roblox clients running").
2. **Shell status/banners** — `MainViewModel` momentary (56) + `LaunchEligibility` + `ServerLandingGate` + `LeftoverSummary` + `PreWarmGate`: launch/recycle/join/squad banners (resx-only, composed at event time). May split into two PRs by weight.
3. **Settings + summaries** — `SettingsPage.xaml.cs` + `AutomaticMemorySummary` + `MutedAccountsSummary`; wire Settings for live re-render; **reword the picker hint** (`SettingsPage_LanguageHint`, a copy fix across all 8 resx culture files — drop the "next time you open RoRoRo" clause now that it applies live) and fix the stale XAML comment at `SettingsPage.xaml:962-965`.
4. **Theming windows** — `ThemeStatusSummary`, `ThemeDescriptions`, `ThemeBuilderWindow`, `CaptionColorPickerWindow`.
5. **History + Games** — `SessionHistoryPage` (incl. the visible `"(unknown game)"`/`"Saved"`/`"PRIVATE"` that the a11y name already localizes), `SessionStatsPresenter`, `GamesPage`.
6. **Diagnostics panel** — `DiagnosticsPage` System-health labels only; the support-bundle body stays English (policy).
7. **SquadLaunch** — `SquadLaunchWindow.xaml.cs` + its `.xaml:44-50` mixed-`<Run>` paragraph.
8. **Friends + JoinByLink** — `FriendFollowWindow`, `JoinByLinkWindow`.
9. **Transport + Modals** — `ExportAccountsWindow`, `ImportAccountsWindow`, `PassphraseStrength`, `StopAllConfirmWindow`, `LaunchHeadroomWindow`, `EdgeRemediationWindow`, `RenameWindow`.
10. **Plugins** — `PluginCapability`, `PluginsViewModel`, `ConsentSheet`, `PluginDuplicates`, and the user-facing `PluginInstaller` failure messages (App-side, so they call `Loc` directly).
11. **Tray + Shell titles + Discord presence** — `TrayService` menu items, `ShellWindow` window/Alt-Tab titles, `DiscordPresenceService.StatusLine`, `WebhookProbe` fallbacks.
12. **XAML misses + guard** — the badges (`MAIN`/`DEFAULT`/`PRIVATE`/`AVAILABLE` across MainWindow/Games/Plugins) and the last stray literals; land the no-new-raw-prose guard.

(~12 PRs; adjacent light areas may combine. writing-plans fixes exact file lists per batch.)

### D. Testing + guards

- Every batch: `dotnet build` + full unit suite green before merge (same bar as Part B). New resx keys are English-only in the neutral catalog; satellites fall back until step 4, so nothing regresses.
- **Live-toggle test** for the shell batch: a `[Collection("MutatesUiCulture")]` test that sets `TranslationSource.CurrentCulture`, raises `CultureChanged`, and asserts a shell composed property returns the other-culture text (proves the wiring + computed-getter conversion; guards the cached-field trap).
- **`Loc` key-parity fence** (extend the existing `LocKeyParityFenceTests` idea to code): every `Loc.Get/Format/Plural` key referenced in App code exists in `Strings.resx`; every plural family is complete (`Plurals.Required`). Catches a typo'd or missing key at test time.
- **No-new-raw-prose guard** (the plan's item-5 fence): a test that flags a newly-introduced user-facing English string literal in a converted file, so a later edit can't quietly re-hardcode. Scoped to the converted files with a ratcheting allow-list, like the other copy fences.
- **CI flake:** the wall-clock family (`AppStorageDefenderTests`, `FpsCapSettlerTests`) will keep failing one arch on some PRs — re-run, as established. (Separately worth a dedicated injected-clock fix; out of scope here.)

### E. Policy — what stays English (survey-specific)

Extends the standing rules with what the sweeps surfaced:

- **`Tray/RobloxWindowTitle.cs:38` `"Roblox - {name}"` — MUST stay English.** It is round-tripped and regex-parsed by `RunningRobloxScanner` to re-attach to foreign Roblox windows; localizing it breaks re-attach. (Load-bearing, like `UseCookies=false`.)
- **Proper nouns:** `WebView2`, `Roblox`, `Discord`, `Pushover`, `ntfy`, and the product nouns (`RoRoRo`, `Squad Launch`, `Recycle`, `Friend Follow`, `Multi-Instance`) stay English wherever they appear, including inside otherwise-localized strings (the `PRODUCT_NOUNS` guard enforces it in step 4).
- **Notification payloads:** Discord webhook bodies, Discord Rich Presence card text (`"RoRoRo"`, `**{Title}**\n{Body}`), phone/Pushover/ntfy envelope title+text (`"RoRoRo test"`, the webhook/phone test bodies). They go to a service, not the app window.
- **Diagnostics support-bundle body** (`DiagnosticsPage` snapshot.txt/zip writer, `[REDACTED]` marker) — English by design (pasted into a maintainer's bug report). The System-health *panel* localizes.
- **Unit tokens / glyphs / format patterns:** `GB`, the `▲` warning glyph, `·`/`—` separators, `"MMM d"`/date formats, `"v{version}"`. **But relative-time words localize** — `"{n} min ago"`, `"{n} hr ago"`, `"{n} days ago"`, `"idle {n}m"` are prose (and plural-forked), not unit glyphs.
- **Not-UI strings excluded** (confirmed by survey, do not touch): `ILogger` messages, diagnostic `throw new …Exception` text, plugin-facing gRPC strings (`PluginHostService` reject reasons, `LaunchResult.FailureReason` over the wire, plugin-authored tray/badge/panel labels).

### F. A dependency the survey surfaced

A few momentary strings are English nouns passed **into already-localized `CoreMessageCatalog` methods**: `"application token"`/`"user key"` (→ `ForPushoverKey`) and `"My channel"`/`"Clan channel"` labels (→ the `{label}: …` webhook-test results). Extracting these means the catalog method (or the composition site) must accept a **localized** noun, not a raw English literal — handled in the Settings batch.

## 4. Plurals inventory (feeds the resx families)

~40 constructs. Densest: `AccountSummary` (idle {n}s/m/h, "{n} min/hr/days ago", RelativeAge) 7; `LaunchEligibility` ("{n} client(s) dispatched", already/expired/limited/deselected) 6; `Preferences` (muted accounts, unreadable/shadowed theme files) 6; `ServerLandingGate` 3; `SquadLaunch`/`Games`/`FriendFollow`/`StopAll`/`LaunchHeadroom`/`Plugins` count rows. Each becomes a `Loc.Plural` family (`_one`/`_other`, plus `_few`/`_many` where ru/pl need them). English neutral resx defines every category the six languages need so no key is ever missing.

## 5. Risks / notes

- **The cached-field trap** is the one place a bare "extract to resx" is insufficient; the shell batch's live-toggle test is the guard.
- **Batch 2 is large** (~76 shell momentary strings); split if review load warrants.
- **Duplicated strings** should resolve to one shared key (e.g. one `Common_CouldntSaveNameChange`), not N copies — reduces the translation surface in step 4.
- **Scope creep guard:** this step does not redesign any flow; it extracts existing wording verbatim (English preserved byte-for-byte) and wires refresh. Copy changes (like the picker hint) are the rare, explicit exception.

## 6. Definition of done

All ~425 App-side must-localize strings resolve through `Loc`/resx; the shell + Settings flip language live on the toggle; on-demand surfaces open in the current language; plurals use `Loc.Plural` families; the three guards (key-parity, plural-completeness, no-new-raw-prose) are green; full suite green on both arches. Then step 4 (translate ×6 via translate-cycle + verifier + `PRODUCT_NOUNS`) and v1.27.
