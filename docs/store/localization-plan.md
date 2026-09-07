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
> declaration is true). Suite 1987 pass. **Remaining:** the other five catalogs (fr, de, ru, pl,
> es) — same generator, one PR — each earning its manifest entry as it lands; the app-string
> **verification gate** (the tool is listing-shaped, so app-string ingest is its own dogfood) is
> the pre-Store-submission approval — merging catalogs to main is not a Store submission, so the
> gate binds before the next Store push, not before merge. **Two follow-ups noted:** the
> `JoinByLinkSentinel` picker item `(Paste a link...)` is a ViewModel display string outside the
> XAML sweep (identity is `PlaceId==0`, so its `Name` is safe to localize) — deferred to a
> deliberate ViewModel-display-string audit rather than cherry-picked; until then the pt-BR
> tooltip quotes the on-screen English label so the reference is accurate.

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

## Order

Core boundary (B) → WPF adapter → tool sweep (C). Phase A is independent of all of it and
can start as soon as the language set is picked.
