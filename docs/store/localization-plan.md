# Localization plan — listing languages first, UI localization by phases

> Este's cross-app ruling, 2026-09-05 (same decision as 626-mod-launcher; his measurements of
> this repo, same day). Referenced from `release-playbook.md`. This file is the plan of record;
> the phases land as their own cycles.

## The stake

Este, 2026-09-05: "I think localization could be big in RoRoRo." The audience argument:
Roblox's player base is global, young, and heavily non-English (Brazil and Southeast Asia are
among its largest markets; Roblox itself ships in ~16 languages), while the multi-instance
tool ecosystem is English-only — a localized multi-launcher would be first-in-category, the
same position phone alerts just took. Partner Center's acquisitions-by-market report on the
existing English listing is the cheapest evidence for which languages earn Phase A slots.

## The rule (verbatim intent)

Three separate things get called "languages":

1. **Listing languages** — translated description and screenshots in Partner Center.
   Translation only, no code.
2. **Package declared languages** — the manifest's `<Resource Language="en-us" />`. Free to
   edit, and **a lie unless the UI is actually translated**.
3. **The app's UI language** — real localization.

**Add the first. Never add the second without the third.** The manifest stays `en-us`-only
until the UI genuinely speaks another language.

## Measured state (Este, 2026-09-05)

- 395 hardcoded string literals in the XAML; no localization infrastructure of any kind.
- ~113 English literals inside `ROROROblox.Core`.
- Neither of the mod launcher's blockers exists here: no `InvariantGlobalization` flag, and
  nothing strips satellite-language content out of the publish. That whole remediation step
  is skipped.

One framing correction for this repo, which changes the tooling target and nothing else:
RoRoRo is **WPF** (.NET 10 + WPF-UI), not WinUI/Windows App SDK. `x:Uid` + `.resw` is the
WinUI yardstick; the WPF equivalents are `.resx` resources referenced from XAML. The
measurement's meaning is unchanged — zero localization infrastructure either way — but the
`/vibe-lingual` adapter this repo needs is a **WPF adapter**, a sibling of the mod launcher's
WinUI one, not the same artifact.

## Phase A — listing languages (no code; can start any release)

Partner Center → Store listings → add a language; each added language gets its own
description, features, and what's-new fields. Screenshots may carry over from the default
language at first (the UI is English regardless — honest either way).

- **The language set (measured 2026-09-05):** Este pulled Partner Center's
  acquisitions-by-market CSV — 577 installs, ~43% from non-English markets against an
  English-only listing. Grouped by language, the clusters are French ~35 (FR+BE+CA),
  Russian ~20 (RU+CIS), German ~20, Portuguese 19 (pt-BR; Brazil is a top-3 Roblox market,
  so its modest count reads as discovery friction — the upside pick), Polish 13, Spanish
  ~15 spread across a dozen markets. **Wave 1: fr, de, ru, pt-BR, pl, es.** Deliberately
  skipped: Filipino (PH is the #2 country at 38, but English is official and demonstrably
  converts), the Nordics (four languages, 25 installs, best English proficiency in the
  data), Thai/Vietnamese/Indonesian/zh-TW (wave 2 if wave 1 moves numbers; zh-TW's 8 is
  first in line). Ukrainian (uk, 7 installs) alongside ru is a goodwill call left to Este.
- **Translations drafted 2026-09-05** — `listing-copy-<code>.md` beside `listing-copy.md`,
  one per wave-1 language, in Partner Center field order (short description, long
  description, 17 features, v1.25 what's-new, copyright, trademark), every capped field
  verified against its limit. The trademark disclaimer is in every language (certification
  surface), product nouns stay English, and each long description states plainly that the
  app's interface is currently English — the never-lie rule applied to listings. What
  remains is the approval gate below, then Este's Partner Center pass: add each language,
  paste its file's blocks, submit (listing-only submission, no new package).
- **Approval gate (Este, 2026-09-05): translations are verified before anything is
  submitted.** Este is building a translation verification tool (Gemini, on Google Cloud +
  Firebase); every translated block is presented there for approval first — no translated
  listing reaches Partner Center unapproved. The repo's side of the contract:
  `scripts/export-listing-translations.py` regenerates
  `docs/store/listing-translations.json` — every field (short, long, features, what's-new,
  copyright, trademark) with the English source beside each translation, its Partner Center
  cap, and the source commit — whenever the `listing-copy*.md` files change. The tool
  ingests that JSON; corrections come back as edits to the per-language `.md` files (the
  source of truth stays in this repo), then the JSON is regenerated. The same gate applies
  to every future release's translated what's-new blocks. Both ends of the loop are built:
  the build brief for the tool-builder agent is
  [`translation-verification-prompt.md`](translation-verification-prompt.md) (rubric
  embedded), and `scripts/apply-translation-verdicts.py` applies a verdicts JSON back to
  the `.md` files — quote→suggestedFix inside the right language/field block, missing or
  ambiguous quotes refused, caps re-checked, export regenerated.
- **GATE CLOSED 2026-09-07.** The verifier ran the full loop (4 review rounds + polish,
  ~30 real findings applied, dogfood items 1-12 filed in
  `translation-verification-prompt.md`), and `batch_approve_passing` recorded **36/36
  approved** at sourceCommit `148676f` (approver `este-via-claude-desktop-agent`, on Este's
  instruction) — all six languages READY FOR PARTNER CENTER. What remains is Este's Partner
  Center pass: add each language, paste its `listing-copy-<code>.md` blocks, submit
  (listing-only submission, no new package).
- **Standing per-release cost:** each `whats-new-X.Y.Z.0.md` must be translated for every
  listing language at Phase 2 time, and Phase 7 pastes each language's block. The Phase 2
  listing audit covers the translated listings the same as the English one.
- The privacy policy stays English unless separately translated; the URL is shared. If a
  reviewer asks, the letter says listing translation precedes UI translation.

## Phase B — the Core boundary — **DONE 2026-09-05** (PR #157; spec [`2026-09-05-core-string-boundary-design.md`](../superpowers/specs/2026-09-05-core-string-boundary-design.md))

> Merged same day it was spec'd: all six inventory rows crossed in one move,
> `CoreMessageCatalog` is the single App-side prose home (byte-for-byte English preserved),
> and `CoreStringBoundaryFenceTests` holds the boundary at zero. Bonus payoff:
> `MainViewModel` no longer branches on message TEXT (the `Contains("WebV2...")` /
> not-installed checks became Kind matches). Phase C is unblocked and waits only on the
> `/vibe-lingual` WPF adapter.

Core produces English display copy with no stable key beside it —
`CookieCaptureResult.Failed(string Message)` is the shape. Core is pure and cannot reach the
UI resource system, so **it hands over a key and the data to fill it, never prose**. The
in-repo precedent is `WebhookUrlVerdict`, which carries a `Kind` enum alongside its
`Message`: that is the rule now, for every new Core-produced string a viewer sees, and the
migration pattern for the existing ones.

Not every Core string is UI copy — the traps, named so nobody localizes them:

- **Discord webhook payloads** go to a channel, not a viewer. They stay English. Never
  localized to anyone's locale.
- **Phone alert envelopes:** the ntfy `Title: RoRoRo` header is static ASCII by transport
  design (HTTP header; account names deliberately never touch the envelope) — it stays
  exactly as it is. Alert title/body localization is a Phase C question, decided then.
- **Log lines and diagnostics** stay English; they are read by whoever debugs, not the user.

## Phase C — the WPF adapter, then the sweep (strictly after B)

> **THE SWEEP RAN 2026-09-06 — RoRoRo is localizABLE (PR #158).** The wpf-resx adapter
> extracted 480 UI strings across 29 XAML files to `{x:Static loc:Strings.Key}` over
> `Properties/Strings.resx`; a public accessor (`Strings.cs`, generated by
> `scripts/gen-strings-accessor.py` because VS's `PublicResXFileCodeGenerator` never runs under
> `dotnet build`) resolves each per `CurrentUICulture` with neutral fallback. All copy fences
> repointed at the resx baseline through the shared `ResxCatalog` resolver;
> `ResxAccessorParityFenceTests` guards resx⇄accessor drift. Build green, suite 1978 pass. The
> manifest stays `en-us` — the app is localizABLE, not yet localizED. **Remaining (the verified
> translation cycle, mirrors the listing-language flow):** export the 480 strings → Gemini
> verifier → apply → six `Strings.<culture>.resx`; THEN the culture-selection setting +
> bootstrap (culture applied BEFORE theme in the load-bearing startup order; a new IAppSettings
> key with its 4-fake ripple + SettingsReachability) + the language picker (x:Static binds once,
> so it applies on restart — the copy says so); manifest `<Resource>` languages LAST. Known
> edges left English on purpose: 3 mixed-inline SquadLaunch URL-template fragments, all-caps
> status chips, glyphs, keyboard gestures, `roblox.com`. One tokenizer miss hand-recovered
> (MainWindow Re-authenticate) — Vibe-Lingual issue #4.

> **CULTURE SWITCH LANDED 2026-09-07 (PR #159).** The `IAppSettings.UiLanguage` key (+ its
> 4-fake ripple, `ReadUiLanguageFast` startup read), `UiCulture` (the AVAILABLE-list guard —
> a language is offered ONLY when its satellite catalog ships, probed not assumed), the
> `Program.Main` bootstrap (culture applied before `new App()`, earlier than theme), and the
> Settings → Appearance language picker. English-only until a catalog shipped.
>
> **FIRST TRANSLATED UI SHIPPED 2026-09-07 (pt-BR).** The content cycle's tooling:
> `scripts/export-ui-strings.py` dumps the neutral catalog to `docs/store/ui-strings.json`
> (483 keys, **0 placeholders** — Phase B kept every format string in code, so the catalog is
> pure display copy); `scripts/gen-culture-resx.py <culture>` applies a per-culture
> `docs/store/translations/ui-<culture>.json` to a `Strings.<culture>.resx`, REFUSING an
> incomplete catalog (every neutral key, none extra, no empty value) so the honest-picker
> guarantee holds — a shipped language is fully translated. pt-BR is the first: all 483 strings
> translated (proper nouns + whitespace preserved), satellite builds by SDK auto-glob (no csproj
> edit), `UiCultureTests` reworked to the biconditional guard + a satellite-loads-real-Portuguese
> proof, manifest gains `<Resource Language="pt-BR"/>` (never-lie: catalog ships, so the
> declaration is true). Suite 1987 pass.

> **WAVE 1 COMPLETE 2026-09-07.** The other five catalogs (fr, de, ru, pl, es) landed the same
> day via five parallel translation agents (each 483/483, proper nouns + whitespace + per-language
> quote styles preserved, registers: fr *vous*, de *du*, es *tú*, ru/pl neutral-impersonal), the
> same generator, one PR. Manifest now declares all six; `UiCultureTests` ratchets to the full
> wave-1 set and a per-language Theory proves each satellite resolves real (non-English-fallback)
> text. Suite 1992 pass (1 documented wall-clock flake, green standalone). **The RoRoRo UI now
> speaks six languages.** The app-string **verification gate** (the verifier tool is
> listing-shaped, so app-string ingest is its own dogfood) is the pre-Store-submission approval —
> merging catalogs to main is not a Store submission, so the gate binds before the next Store
> push, not before merge; the six catalogs are the input to that pass. **Two follow-ups noted:** the
> `JoinByLinkSentinel` picker item `(Paste a link...)` is a ViewModel display string outside the
> XAML sweep (identity is `PlaceId==0`, so its `Name` is safe to localize) — deferred to a
> deliberate ViewModel-display-string audit rather than cherry-picked; until then the pt-BR
> tooltip quotes the on-screen English label so the reference is accurate.

> **VERIFICATION PILOT 2026-09-07 — the tool does app-string voice review.** Este's call: run
> the app catalogs through the translation-verification tool as both voice review and the
> pre-Store gate. `scripts/export-ui-translations.py` shapes the neutral + six catalogs into the
> tool's `fields` contract (`field`/`en`/`translations`, `pt-BR`→`pt-br`) → `ui-translations.json`,
> ingested by pinned commit like the listing dataset. Findings: (1) the tool ingests app-string
> shape and its readiness gate **generalizes** (`0/483`, not hard-locked to the 6 listing fields);
> caps handled gracefully (`capViolations: []`). (2) The es review validated the voice (*"correct
> 'tú' register, captures claims accurately"*) and flagged exactly one systematic class — **product
> feature nouns translated**: `Squad Launch`, `Recycle` rendered in the target language, against
> the approved listing's "product nouns stay English" rule (listing-copy-*.md line 5). Fixed
> across all six catalogs (feature name → English; descriptive "squad member" and `Launch multiple`
> stay translated; the localized `Tools → Games` nav path kept). (3) **Baked in:** a `PRODUCT_NOUNS`
> guard in `gen-culture-resx.py` fails the build if a must-stay-English noun (RoRoRo, Roblox, Squad
> Launch, Recycle, Multi-Instance) is translated away — and it immediately caught a **13th
> occurrence the Gemini review missed** (`WelcomeWindow_TogglesWhetherThisAccountJoins`), so the
> deterministic guard and the tool each catch what the other misses. Two dogfood findings for the
> tool repo: the ingest response echoes all 2,898 field-statuses (450KB, overflows at app-string
> scale — should summarize); and the review had a false negative on that 13th field.

Do not hand-sweep the 395. `/vibe-lingual` is the loop — extract, wire, translate, guard,
per-file backups, safe re-runs. It won't handle this XAML natively; it has an adapter seam
and reports not-yet-implemented rather than mangling anything. Build the **WPF adapter**
against that seam, then let the tool sweep.

**Live run 2026-09-05** (engine v0.1.0 cloned from the Vibe-Lingual repo and run against
this repo — the plugin itself is not installed on this box): `detect` returns
`framework: "none"` honestly; `scan` inventories **0 sites across 0 files** (the scanner is
JSX/TSX-bound and does not see XAML at all); `wire --locales fr,de,ru,pt-BR,pl,es` stands
down exactly as `adapter.contract.md` promises — `status: not-yet-implemented`, exit 1,
zero mutation. Two consequences for the adapter work (which lives in the Vibe-Lingual
repo, not here):

1. **The WPF gap is wider than the adapter seam.** The seam covers the mutating side
   (`wire` / `transform` / `emitParityTest` / `emitGuard`), but `detect` and `scan` are
   also JS-ecosystem-bound — WPF support needs a detector arm (csproj + XAML presence) and
   a XAML/C# scanner (literal inventory by kind: Text, Content, Header, ToolTip,
   AutomationProperties.Name, code-behind strings) before any adapter method runs.
   **→ Built the same day: Vibe-Lingual PR #1 (`feat/wpf-stack-readonly`, MERGED) ships the
   read side — detect arm, whitelist XAML scanner, honest stand-downs. Its dogfood run on
   THIS repo: 530 sites across 30 XAML files — 385 xaml-text (vs the hand-measured 395; two
   independent methods within ten) + 81 automation names + 62 tooltips + 2 placeholders.**
   **→ 2026-09-06: the MUTATING half landed as Vibe-Lingual PR #2 (`feat/wpf-resx-adapter`)
   — span-precise XAML codemod ({x:Static loc:Strings.Key}), idempotent resx merge +
   per-culture seeding, backup-batch reversibility, emitted C# parity + literal-ratchet
   fences, CLI `extract --clr-namespace … --dry-run`. RoRoRo dry-run: 29/29 files
   rewritten, 470 entries, 7 honestly staged (4 Hyperlink inner texts, 3 inline-split
   fragments). The remaining Phase C work is the RoRoRo-side cycle: real extraction run,
   the 7 hand conversions, per-culture translations, culture-selection setting (new
   IAppSettings ripple; culture applies BEFORE theme in the load-bearing startup order),
   copy fences re-pointed at the resx baseline, and only then manifest languages.**
2. **What the adapter's four methods mean here:** wire = resx infrastructure + culture
   selection; transform = XAML literal → resource reference and C# literal →
   `Resources.Key`; parity guard = resx key parity across languages (the engine's
   "highest-value guard" translates directly); guard ratchet = this repo already has the
   fence-test culture the ratchet wants — the copy fences learn the resource baseline in
   the same commit, per the hazards above.

Repo-specific hazards the sweep must respect, or the suite goes red:

- The copy fences (`PreferencesCopyTests` first-person rule, `WindowTitleConventionTests`,
  `AccessibleNamingFenceTests`) read source-tree strings today. They must learn to read the
  default-language resource baseline **in the same commit** the strings move — the standing
  fence ratchet rule.
- `ContrastPairGateTests` and the settings-reachability fence are content-independent but
  tree-reading; verify against the tree after the sweep, per the register rule.
- Themed/branded strings ("RoRoRo", "Imagine Something Else.", theme names per the findings
  register ruling) are proper nouns, not copy — excluded from extraction.

Only after C ships a genuinely translated UI does the manifest gain `<Resource>` entries for
those languages — never before (the rule above).

## Phase D — localization completeness (v1.27)

> **Why this phase exists.** v1.26 shipped Phase C (the 483 static XAML strings, six languages)
> and went live on GitHub 2026-09-07. The pre-Store localized smoke (Este, same day, in Spanish)
> found English patches in the running UI. **Este's ruling: HOLD v1.26 from the Store, ship a
> COMPLETE v1.27.** Localization is a first-impression feature — a half-English Store debut turns
> people off worse than a later, whole one. This phase closes the gap.

### The audit (2026-09-07, four parallel read-only sweeps)

Phase C swept **static XAML only**. Every string COMPOSED IN CODE (status banners, account-row
text, memory hints, join/follow flows, plugin/consent copy, error dialogs, theme + diagnostics
prose) was never touched and still renders English.

| Surface | Distinct | Notes |
|---|---|---|
| `ViewModels/` | 105 | whole layer had zero `Strings.` refs (`MainViewModel` alone 59) |
| `Preferences/` + `Theming/` | 94 | `SettingsPage.xaml.cs` heaviest single file |
| `Core` + `CoreMessageCatalog` | 71 | 28 keyed-but-English + 43 raw-prose composers |
| other code-behind + XAML misses | 265 | 256 code-behind (25 files) + 9 XAML misses |
| raw sum | **535** | — |
| ≈ distinct (minus ~20 cross-group dups) | **~515** | ~600 code sites |
| — policy "leave English" (~50) | | external notification payloads, diagnostics bundle, unit tokens |
| **must localize** | **≈ 465** | |

**The headline:** the code-composed layer (~515) is the same magnitude as the 483 static strings
that *were* localized — the app is **about half localized**. That is the "half-English screen"
the honest picker was meant to prevent; the picker's guarantee held only for the catalog, and
the catalog was not the whole UI.

### Per-category treatment

1. **`CoreMessageCatalog` (28)** — already keyed by enum; make it **resx-backed** (resolve the key
   through the resource system instead of returning literal English). Foundational, low-risk.
2. **Core raw-prose composers (7 files, 43)** — `AlertStatusLine`, `MultiInstanceStatusLine`,
   `RobloxCompatChecker`, `SessionHistoryRowName`, `DiagnosticsCollector`, `ThemeStore`,
   `AccountTransportService` — bypass the key+data boundary and emit finished English. Convert to
   the **return-a-Kind** pattern (`LaunchResult`/`WebhookUrlValidator` are the model), resolved
   App-side. Structural refactor with test ripple.
3. **App-side composed strings (~430)** — ViewModels, Preferences summaries, code-behind,
   formatters — extract to resx and resolve via the runtime lookup (see live-toggle below) +
   `string.Format`. The bulk, and nuanced: these are *composed* strings with placeholders and
   singular/plural forks, not static labels.
4. **XAML misses (9)** — badges (`MAIN`/`DEFAULT`/`PRIVATE`/`AVAILABLE`) + one mixed-`<Run>`
   paragraph (`SquadLaunchWindow.xaml:44-50`) → straight extraction.
5. **Translate** the new strings × 6 languages through the built pipeline (translation agents →
   `translation-verification` tool → `PRODUCT_NOUNS` guard in `gen-culture-resx.py`).

### Decision 1 — live-toggle (Este asked; the answer is YES, and Phase D is the time)

**Result: WPF can switch language instantly on the picker toggle — all open windows update with no
restart — but it requires moving off `{x:Static}`.** `x:Static` is resolved ONCE at load time; it
cannot react to a culture change (that is exactly why v1.26 applies-on-restart). Live switching is
the standard WPF pattern:

- A `TranslationSource` singleton implementing `INotifyPropertyChanged` with a `this[string key]`
  indexer that reads `ResourceManager.GetString(key, CurrentCulture)`, and a `CurrentCulture`
  setter that raises `PropertyChanged(Binding.IndexerName)`.
- A `{loc:Loc Key}` markup extension returning a one-way `Binding("[Key]")` to that singleton.
- XAML `{x:Static loc:Strings.Key}` → `{loc:Loc Key}`. On toggle, set `CurrentCulture` → every
  bound element re-pulls → the whole UI re-renders in the new language, live.
- ViewModel composed strings already ride `INotifyPropertyChanged`; on a culture-changed event the
  VM re-raises its display properties (or reads them through the same source).

**Why fold it into Phase D and not defer it:** live-toggle re-touches the 483 static strings (the
`x:Static` → `loc:Loc` rewrite). We are already re-touching the entire string surface for the
~515 composed strings. Do both in one pass, or pay the 483-string rewrite twice. **Bonus:** the
`{loc:Loc}` indexer looks strings up by key at runtime, which **removes the need for the generated
`Strings.cs` public accessor** (the CRUX that forced `gen-strings-accessor.py`) — a parity fence
that every `loc:Loc` key exists in the resx replaces the compile-time accessor check. Net: live
UX + less generated-code machinery. Cost: the codemod (a Vibe-Lingual transform variant), the tiny
provider/extension, VM notify wiring, and re-teaching the copy fences the new binding form.

### Decision 2 — plurals (the thing that separates "translated" from "correct")

Many strings are plural-forked: `"1 account is muted"` / `"{n} accounts are muted"`, `"{n} min
ago"`. English has 2 plural forms; **Russian and Polish have 3-4** with non-trivial rules. Naïve
per-count keys ship subtly wrong grammar in ru/pl. Options: ICU-style plural resources, or a
plural-selector helper keyed on each language's CLDR plural category. **This must be decided before
the App-side extraction** (it shapes how composed strings become resources).

### Decisions 1 & 2 RESOLVED + runtime foundation LANDED (2026-09-07)

- **Plurals: per-form resx keys + a CLDR selector** (not ICU MessageFormat). A plural string is a
  family — `Key_one`/`Key_other` (en/fr/de/es/pt), `Key_one`/`Key_few`/`Key_many` (ru/pl) — so each
  form stays a plain translatable string the agents + verifier handle like any other, with no new
  dependency. `Plurals.Category(culture, count)` implements the CLDR cardinal rules (integer counts
  only; UI counts are never fractional); `Loc.Plural(baseKey, count)` picks the form and formats it;
  `Plurals.Required` drives the lint plural-family guard. `PluralsTests` locks ru/pl with CLDR vectors.
- **Live-toggle: `{loc:Loc}` bindings over `TranslationSource`** — built + unit-proven. Setting
  `TranslationSource.CurrentCulture` raises the indexer change (bound XAML re-renders) and fires
  `CultureChanged` (ViewModels re-raise composed strings): instant switch, no restart. `LocExtension`
  is the `{loc:Loc Key}` markup extension; `Loc.Get/Format/Plural` the code side.
  `LocalizationRuntimeTests` proves per-culture resolution + notification against the shipped
  satellites. **Still to do (the extraction step):** codemod the 483 `x:Static` → `{loc:Loc}` and
  re-teach the copy fences the new form — which retires `gen-strings-accessor.py` for a resx⇄key
  parity fence.

### Core refactor DONE — per-category items 1 & 2 complete (2026-09-07)

The whole Core string layer (71 strings) now hands the App an enum + data, never a sentence.
**Core carries no user-facing prose.**

- **Item 1 — `CoreMessageCatalog` resx-backed (Part A, #164).** Every Core message Kind resolves a
  `CoreMsg_*` key through `Loc` instead of returning literal English; the history count rides
  `Loc.Plural`. This retired the hand-written `Strings.cs` accessor for runtime key lookup.
- **Item 2 — the 7 raw-prose composers (Part B, one PR each, #165–#171).** Each stopped emitting
  finished English:
  1. `RobloxCompatChecker` (#165) — `CompatCheckResult` carries `CompatDrift` (direction enum +
     versions); App composes the banner.
  2. `SessionHistoryRowName` (#166) — the spoken a11y row name; **moved whole to the App** (no Core
     consumer), resx-backed.
  3. `DiagnosticsCollector` (#167) — snapshot fields became data (`string?` versions, `bool
     MultiInstanceHeld`); the System-health panel localizes, the **support bundle stays English**.
  4. `AccountTransportService` (#168) — `AccountTransportException` carries no message (string ctor
     removed so prose can't return); App renders one localized line. No-oracle-leak invariant kept.
  5. `ThemeStore` (#169) — `InvalidThemeException` gained `Kind` (6) + `Detail`; App composes via
     `CoreMessageCatalog.For(InvalidThemeException)`.
  6. `MultiInstanceStatusLine` (#170) — moved whole to the App, resx-backed; "Multi-Instance" stays
     verbatim.
  7. `AlertStatusLine` (#171) — moved whole to the App; nine sentence keys + localized channel
     fragments joined by a localized `AlertStatus_ListConnector`; `#channel`/provider names are data.

All new keys are English-only in the neutral resx (satellites fall back) until step 4 translates
them. **CI-health note:** the wall-clock flake family (`AppStorageDefenderTests`,
`FpsCapSettlerTests`) forced a re-run on ~half these PRs — a pre-existing, arch-asymmetric,
timing-sensitive flake unrelated to the changes; a dedicated fix (injected clock or serial
collection) is worth a follow-up.

### App-side extraction DONE — items 3 & 4 + the guards complete (2026-09-07)

Every user-facing string the app renders now resolves through `Loc`/`{loc:Loc}`. Done as **15 small
PRs** (#172–#189, one coherent surface each), each build-green, unit-green, and CI-green (x64+arm64)
before squash-merge. **~457 new `CoreMsg_*`/`Shell_*`/`Plugin_*`/`Diag_*`/`Tray_*`/`Discord_*`/…
keys** carry the `localized Phase D step 3` marker in the neutral resx; all English-only until step 4.

- **Item 3 — the App-side composed strings + live-toggle (batches 1–14).** `Loc.Plural` was extended
  to carry extra format args ({0}=count, {1..}=extra). Batch 1 set the pattern: `MainViewModel`
  subscribes `TranslationSource.CultureChanged` (unsubscribe in `StopPeriodicRefresh`) and re-runs its
  composed getters + fans `NotifyCultureChanged()` across account rows, so long-lived surfaces
  re-narrate live. The category treatment held throughout: **displayed on a long-lived surface** →
  resx + `CultureChanged`; **on-demand window/page** → resx read at construction; **momentary
  (catch/event-time)** → resx template at the moment; **plural** → `Loc.Plural`. Surfaces converted:
  shell VM + account rows, Settings dialogs, theming (ThemeBuilder/CaptionColorPicker hold resx KEYS
  resolved at build time), Diagnostics panel, Games/History/SquadLaunch/Friends/Join, Transport
  (Export/Import) + shared modals, Plugins (consent sheet + window + capability vocabulary resolved at
  call time, never baked at static-init), Tray menu, shell window titles (shell subscribes
  `CultureChanged` — the language selector lives on its Settings page), and the Discord status line.
- **Item 4 — XAML misses (batch 15).** The last hardcoded literals — MAIN/DEFAULT/PRIVATE/AVAILABLE
  badges and the SquadLaunch link-help paragraph — moved to `{loc:Loc}`; DEFAULT/PRIVATE reuse the
  existing Squad/History badge keys, and the mixed-`<Run>` paragraph collapsed to one coherent
  sentence (URLs embedded verbatim) rather than fragments locked around monospace runs.
- **Guards from (e) — in place.** `LocKeyParityFenceTests` (pre-existing) proves every `{loc:Loc Key}`
  resolves to a real resx key; **new** `NoRawXamlProseFenceTests` fails on any raw-English
  Text/Header/Content/ToolTip/Title/PlaceholderText attribute or inline text node, carving out markup
  extensions, decoded glyphs/punctuation, and URLs/domains. `BrandNameFenceTests`' display-sink
  vacuity floor dropped 20→5 in the same commit that made the reduction (localization converts literal
  sinks to `Loc` calls the regex no longer counts).

**Scope decisions recorded here:** installer-thrown `PluginInstallerException` messages stay English
(they reach the user only via the localized `{ex.Message}` wrapper and flow into support logs — the
same treatment every `ex.Message` gets; a proper fix is a Kind-based refactor, deferred). The Discord
Rich Presence payload stays English (external payload). `SettingsPage` live-refresh is **deferred** —
its displayed summaries flip on next populate, not mid-toggle; a small follow-up subscribes it to
`CultureChanged` and re-runs its populate methods.

**Still open in Phase D:** step 4 (translate the ~457 new keys ×6 via the catalog pipeline, the
verifier, and the `PRODUCT_NOUNS` guard), the deferred `SettingsPage` live-refresh wiring, then re-cut v1.27 (version
bump, MSIX ×2 arch, GitHub release) and the Partner Center submission (the held v1.26 work,
re-versioned).

### Policy — what stays English (extend the never-lie rules)

- **External notification payloads** — Discord webhook bodies, Discord Rich Presence card, phone
  (Pushover/ntfy) envelopes. They go to a channel/service, not the app window. (Desktop toasts
  shown ON the user's PC — e.g. the memory-warning toast — DO localize.)
- **Diagnostics support-bundle body** — pasted into Discord for maintainers; English by design.
- **Unit tokens / glyphs / gestures / product nouns** — per the existing rules and `PRODUCT_NOUNS`.

### Sequence

(a) Core boundary + `CoreMessageCatalog` resx-backing → (b) settle the plural design + build the
`loc:Loc` provider/codemod → (c) App-side extraction (XAML `x:Static`→`loc:Loc` + composed
strings) with live-toggle wired → (d) translate + verify the new strings → (e) guards (resx⇄key
parity for `loc:Loc`, re-pointed copy fences, a fence against new raw UI prose in the converted
files). Then v1.27 ships a UI that is fully localized AND switches on the toggle, and the Store
submission (the held v1.26 work, re-versioned) goes out complete.

## Order

Core boundary (B) → WPF adapter → tool sweep (C, shipped v1.26) → completeness + live-toggle (D,
v1.27). Phase A (listing languages) is independent and rides each release's Phase 2 audit.
