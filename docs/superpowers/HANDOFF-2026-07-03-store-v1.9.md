# HANDOFF — RoRoRo v1.9 Store-release feature set

**Date:** 2026-07-03 · **From:** crash-diagnostics/support session (Ur Task v0.5.0 + host #36/#38) · **For:** next Claude Fable 5 session, /vibe-iterate, **ultracode on**
**Repo:** ROROROblox (branch off `main`; PR per feature; never push `main` directly)

## Prompt (paste-ready)

> ultracode. Read `docs/superpowers/HANDOFF-2026-07-03-store-v1.9.md` in this repo, then execute the v1.9 feature set in its sequencing order — brainstorm → spec → plan → subagent-driven build per feature, one PR each. The skeptic constraints in that doc are build requirements, not suggestions. Verify against the Store gates named per feature before calling anything done. Log decisions to the RORORO dashboard project (PBWgg5mimZyAzAG3niAp).

## Where things stand

- Last Store release: **v1.8.0.0** (2026-07-02). Unreleased delta on `main` today has **one** user-visible item: consent-sheet-on-first-Launch fix (PR #35). No headline. That's the gap this feature set fills.
- **PR #38 is open and prerequisite** (`feat/plugin-lifecycle-logging`, implements issue #36 + riders): starter exit codes, supervisor/installer logging, `IAsyncDisposable` shutdown fix, framework log-noise cut. **Merge it first.** Residual on #36: `PluginsViewModel` consent-outcome logging.
- Plugin family shipped: Ur Task v0.5.0 (crash diagnostics incl. `DiagLog` + startup watchdog), Ur OCR v0.4.0, ur-afk v0.1.0. Support how-to at `docs/support/how-to-grab-your-logs.md` (PR #37, merged).
- Analysis provenance: 6-agent workflow (4 recon lenses → synthesis → adversarial Store-certification review), 38 findings. Skeptic verdicts are folded in below.

## The v1.9 feature set (in build order)

### 1. Plugin crash detection + user-visible exit status — M, skeptic: CLEAR

Merge PR #38, then build the layer its `PluginProcessSupervisor.PluginExited` docstring defers: crash-vs-clean-exit discrimination (exit code ≠ 0 or sub-threshold uptime), surfaced as a Plugins-page status line / banner — "Ur Task exited unexpectedly (code 0xE0434352)" — with a jump-to-logs affordance. No new MSIX capability (reading own children's exit codes). Strengthens the 10.2.2 out-of-process story in the reviewer letter.

### 2. Host-UI rendering v1 — tray-menu + row-badge actually paint — M, skeptic: CAUTION (constraints below)

`WpfPluginUIHost` is a logging stub; both shipped plugins already declare `host.ui.tray-menu` + `host.ui.row-badge`, users already consent to them, AUTHOR_GUIDE documents them as working. The pipeline (gRPC → PluginUITranslator → CapabilityInterceptor gating → GUID handles) exists — only pixels are missing. Scope to tray-menu + row-badge; defer status-panel.

**Skeptic constraints (build requirements):**
- `UpdateUI` is capability-ungated (`RpcMethodCapabilityMap.cs:27` maps it to null) — gate it before pixels land.
- No consent-revocation teardown exists: revoking tray/badge consent mid-session must remove painted surfaces (handle teardown on consent change), or the PERMISSION_DENIED consent claim in every reviewer letter breaks.
- Plugin badges must be visually distinct from host status badges (plugin-controlled colorHex/text can't be allowed to impersonate e.g. the "Limited by Roblox" dot) — 10.1.1 spoofing optics.

### 3. Tray per-account quick launch — S, skeptic: CLEAR

Per-account items in the tray menu (`LocalName ?? DisplayName`, zero schema change — pre-approved in the 2026-05-07 default-game-widget design §11). Pairs with #2: two tray features make the tray the release headline. Names stay local; tray-initiated Roblox control already reviewed in v1.8.0.0.

### 4. Support bundle: include plugin logs — S, skeptic: CAUTION (reframe required)

**Do NOT release-note this as new** — `DiagnosticsWindow.xaml` already has "Save support bundle (.zip)" with `.ROBLOSECURITY` redaction, and listing copy already claims it (10.1.1 double-claim risk). The genuinely new slice: bundle **plugin** logs (`%LOCALAPPDATA%\626Labs\*\logs`) with (a) the redaction pass extended to them, (b) an opt-in "include plugin logs" toggle, (c) in-UI privacy copy (plugin logs carry display names + Roblox user ids). Frame as "support bundle now covers plugins."

### Deferred — do not build in v1.9

- **Velopack download+apply (Setup.exe channel):** the standing 10.1.1 listing-copy exposure is real, but the skeptic requires a **compile-time** channel split (MSIX build must not ship apply code — today's gate is a runtime `manager.IsInstalled` check, which is a certification landmine). Needs its own spec; also fix `listing-copy.md:26,43,65` Store-facing auto-update claims when it lands.
- **Mid-session orphan/tray-resident Roblox cleanup:** top user-pain item but L, sits on the mutex-gate rework, and the 2026-07-02 tray-residence spec says it needs its own /scope. Ship alone later.
- **#36 residual:** PluginsViewModel consent-outcome logging — fold into feature 2's consent-teardown work if convenient (same file), else leave on #36.

## Gates every feature must respect (from reviewer-letter archaeology)

- **No new MSIX capability, ever, without a spec-level decision** — "no new capability" is the load-bearing line in the v1.7.0.0 letter.
- **No SetWindowsHookEx-class mechanisms** — v1.8.0.0 precedent explicitly rejected them.
- **10.1.1 accuracy:** every release-note and listing claim must be true at submission (the app was renamed once over this clause; don't feed it).
- **10.1.4.4.c:** new surfaces reachable in ≤2 clicks; add rows to the 10.1.4.4.b unique-value table in the reviewer letter.
- **No telemetry / nothing leaves the machine** — v1.8.0.0 precedent; logs local until the user sends them.

## Mechanics for the executing session

- **Shared tree warning:** Este commits to this checkout from parallel sessions. Verify `git branch --show-current` + tip inside every mutating command, or use a worktree. History rewrites mid-flight have happened.
- Flow per feature: superpowers brainstorm → spec (`docs/superpowers/specs/`) → plan (`docs/superpowers/plans/`) → subagent-driven build (fresh implementer per task, task review, final whole-branch review) → PR. This exact flow shipped Ur Task's crash diagnostics today at 711/711 + zero Critical/Important on final review.
- `/vibe-iterate` note: if `.vibe-iterate/` isn't bootstrapped in this repo, run `/vibe-iterate:bootstrap` first; `feature-add` mode maps to one candidate at a time. The ranked list above supersedes its own gap-scan for this release.
- Tests: `dotnet test src/ROROROblox.Tests/ROROROblox.Tests.csproj` — 711 green as of today. `CapturingLogger<T>` exists for log-line assertions; `DefaultPluginProcessStarterTests` shows the real-process pattern.
- When the set is done: release-notes + reviewer-letter updates live in `docs/store/` (follow the v1.8.0.0 file shapes), then Este runs the Store submission — that part is his.
