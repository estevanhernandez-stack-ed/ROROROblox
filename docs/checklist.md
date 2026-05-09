# ROROROblox — detect-Roblox-already-running Build Checklist

**Cycle:** v1.3.x detect-Roblox-already-running (cycle 4 — follows save-pasted-links, which shipped 2026-05-08 via PR #5)
**Cycle type:** Spec-first cycle (pattern mm). Substantive design at [`docs/superpowers/specs/2026-05-08-roblox-already-running-detect-design.md`](superpowers/specs/2026-05-08-roblox-already-running-detect-design.md). [`spec.md`](spec.md) is a pointer-stub to that canonical doc.
**Build mode:** autonomous-with-verification (one checkpoint after item 4 — gate is wired into `App.OnStartup`; verify both paths on a real Win11 box before doc/security pass)
**Comprehension checks:** off
**Git cadence:** commit after each item
**Branch:** `feat/roblox-already-running-detect` cut from `main` at item 1 start
**Repo:** [github.com/estevanhernandez-stack-ed/ROROROblox](https://github.com/estevanhernandez-stack-ed/ROROROblox)

**Effort estimates:** wall-clock guesses for autonomous mode. Total cycle ≈ 75–100 minutes including checkpoint.

---

- [ ] **1. IRobloxRunningProbe + RobloxRunningProbe impl (Core)**
  Spec ref: `spec.md > §4.1`, `§4.2`; canonical spec §4.1, §4.2
  Effort: ~10 min
  Dependencies: none — Core-only, no DI wire-up yet, no UI
  What to build: Two new files in `src/ROROROblox.Core/Diagnostics/`.
  - `IRobloxRunningProbe.cs` — interface with one method: `IReadOnlyList<int> GetRunningPlayerPids()`. XML doc comment names the cycle ("cycle 4 (2026-05-08)") and the asymmetry rule from §5 (fail-open at the call site, not in the probe itself). Namespace `ROROROblox.Core.Diagnostics` (matches `IDiagnosticsCollector`, `IRobloxProcessTracker` neighbors).
  - `RobloxRunningProbe.cs` — sealed class implementing the interface. Body matches spec §4.2 verbatim: `Process.GetProcessesByName("RobloxPlayerBeta")`, project to `p.Id` array, `try/finally` disposes every `Process` handle (avoids the leaked-handle anti-pattern that bit `RobloxProcessTracker.cs:188` historically). Constant `private const string PlayerProcessName = "RobloxPlayerBeta"` — single source of truth, matches `RobloxProcessTracker`'s constant naming.
  Acceptance: Both files compile clean as part of `ROROROblox.Core`. No new NuGet references in `ROROROblox.Core.csproj`. `Process.GetProcessesByName` resolves via the existing `System.Diagnostics.Process` reference (already used in `RobloxProcessTracker.cs` and `DiagnosticsCollector.cs` — confirmed).
  Verify: `dotnet build src/ROROROblox.Core` clean. `git diff --stat` shows two new files in `Core/Diagnostics/` and zero `.csproj` changes. Commit: `feat(core): IRobloxRunningProbe + RobloxRunningProbe impl`.

- [ ] **2. RobloxAlreadyRunningWindow modal — cycle-2 chrome**
  Spec ref: `spec.md > §4.3`; canonical spec §4.3
  Effort: ~20 min
  Dependencies: none (visual primitive — independent of probe + gate logic)
  What to build: New `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml` + `.xaml.cs`. Match the chrome of `RobloxNotInstalledWindow.xaml` exactly — same `x:ClassModifier="internal"`, `Background="{DynamicResource BgBrush}"`, `WindowStartupLocation="CenterScreen"` (NOT `CenterOwner` — there is no owner; this modal pre-empts MainWindow), `ResizeMode="NoResize"`. Window dimensions ~520x320 per spec.
  Layout (top-to-bottom, in a single `Grid` with `Margin="32"` and 5 `Auto` rows + a final `Auto` row for the action button):
  - Row 0: heading `Roblox is already running.` — Space Grotesk 20pt bold, cyan `#17D4FA` (matches the hardcoded hex pattern in `RobloxNotInstalledWindow.xaml:22` rather than `{DynamicResource CyanBrush}` — keeps modals self-contained for theme-load timing safety).
  - Row 1: body paragraph `Multi-instance launches won't work right while another Roblox process is open outside of RoRoRo. To get back to a clean state:` — Inter 12pt, white `#FFFFFF` Opacity 0.85, `TextWrapping="Wrap"`, `Margin="0,12,0,0"`.
  - Row 2: numbered list as a single `StackPanel` with three `TextBlock` rows. Numerals `1.` / `2.` / `3.` in magenta `#F22F89`; step text in white `#FFFFFF`. Inter 12pt. Steps:
    - `1. Close Roblox`
    - `2. Close RoRoRo`
    - `3. Re-open RoRoRo`
  - Row 3: mono-micro line `MUTEX HOLD REQUIRES CLEAN START` — JetBrains Mono 10pt, uppercase, `CharacterSpacing="120"` (≈0.12em tracking via WPF's per-mille unit), `Foreground="#5A6982"` (muted, matches the secondary-button border color used in cycle-2 modals).
  - Row 5: action `StackPanel` `HorizontalAlignment="Right" Margin="0,16,0,0"` with a single `Quit RoRoRo` button — `Background="#17D4FA"`, `Foreground="#0F1F31"`, `IsDefault="True"`, `IsCancel="True"`, `BorderThickness="0"`, `FontWeight="Bold"`, `Padding="16,8"`. Click handler `OnQuitClick`.
  Code-behind: trivial. `OnQuitClick` calls `DialogResult = true; Close();` — actual `Application.Current.Shutdown()` happens in `App.OnStartup` after `ShowDialog()` returns (keeps the shutdown sequencing in one place; mirrors how `WebView2NotInstalledWindow` defers the install-prompt action to the caller).
  Acceptance: XAML compiles clean. Window renders with the cycle-2 chrome (immersive dark title bar via `WindowTheming.RegisterGlobalDarkTitleBar`'s class handler — already wired in `App.OnStartup` line 48). Visual side-by-side with `RobloxNotInstalledWindow` shows matching margins, type stack, button shape. Esc and Enter both fire `OnQuitClick` (because `IsDefault` + `IsCancel` are both `True`).
  Verify: `dotnet build` clean. Manual XAML smoke — temporarily new-up the window from a `Debug.Click` handler somewhere convenient and `ShowDialog()` it; confirm chrome match. Discard the smoke handler. Commit: `feat(app): RobloxAlreadyRunningWindow modal — cycle-2 chrome`.

- [ ] **3. StartupGate — probe-driven shutdown decision + 5-case TDD test suite**
  Spec ref: `spec.md > §4.4`, `§5`, `§6`; canonical spec §4.4, §5, §6
  Effort: ~25–30 min
  Dependencies: item 1 (needs `IRobloxRunningProbe` to consume)
  What to build: Test-driven implementation — write the 5 unit tests first, watch them fail, implement to green.
  - **New test file:** `src/ROROROblox.Tests/StartupGateTests.cs`. Targets `StartupGate.ShouldProceed` with a hand-rolled `FakeRobloxRunningProbe` that exposes `IReadOnlyList<int> NextResult { get; set; }` and `Exception? NextThrow { get; set; }` — same hand-rolled-fake pattern as `JoinByLinkSaveTests.cs` (no Moq / no NSubstitute; project is fake-rolling deliberately). Use `Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupGate>.Instance` for the logger arg, OR a `ListLogger<StartupGate>` capture-fake if assertions on log level + message need verification (cases 4 + 5 require warning-level capture).
  - **5 cases per spec §6:**
    1. `Probe returns empty list` → `ShouldProceed` returns `true`. No log warning emitted (capture-fake confirms zero `Warning`-level entries).
    2. `Probe returns one PID` → `ShouldProceed` returns `false`. One `Information`-level log entry containing the PID string.
    3. `Probe returns multiple PIDs` → `ShouldProceed` returns `false`. One `Information`-level log entry containing comma-joined PIDs.
    4. `Probe throws InvalidOperationException` → `ShouldProceed` returns `true` (fail-open). One `Warning`-level log entry with the exception attached.
    5. `Probe throws unexpected exception type` (e.g., `Win32Exception` synthesized by hand) → `ShouldProceed` returns `true`. One `Warning`-level log entry. Defensive — covers any future Win32-shaped throw from `Process.GetProcessesByName`.
  - **Implementation:** `src/ROROROblox.App/Startup/StartupGate.cs`. Sealed class, ctor takes `IRobloxRunningProbe probe, ILogger<StartupGate>? log = null` — null logger collapses to `NullLogger<StartupGate>.Instance` so production DI registration is the only call site that supplies a real logger. Method `bool ShouldProceed()` matches spec §4.4 body verbatim — outer `try/catch (Exception ex)`, log warning + return `true` on any throw (false-positive is unrecoverable, false-negative is recoverable; §5 asymmetry is the reason).
  - **Naming note:** the existing `src/ROROROblox.App/Startup/` folder already holds `IStartupRegistration.cs` + `StartupRegistration.cs` (run-on-login). `StartupGate` lives in the same folder — both are "things that run during app startup."
  Acceptance: All 5 new tests pass on first green. `dotnet test` full suite still green (existing 70+ tests). `StartupGate` has zero WPF references — pure C# + `IRobloxRunningProbe` + `ILogger`. The class is `sealed`, the ctor parameter is the interface (not the impl), and there's no static state — fully unit-testable.
  Verify: `dotnet test --filter StartupGateTests`. `dotnet test` (full suite). Commit: `feat(app): StartupGate — probe-driven shutdown decision + 5-case TDD test suite`.

- [ ] **4. App.OnStartup wire-up — gate runs before mutex.Acquire**
  Spec ref: `spec.md > §4.5`; canonical spec §4.5
  Effort: ~10 min
  Dependencies: items 1, 2, 3 (probe + modal + gate all exist)
  What to build: Two edits to `src/ROROROblox.App/App.xaml.cs`.
  - **`ConfigureServices`** — register both new types as singletons. Add to the existing `services.AddSingleton<...>()` block:
    ```csharp
    services.AddSingleton<IRobloxRunningProbe, RobloxRunningProbe>();
    services.AddSingleton<StartupGate>();
    ```
    Keep the registrations next to the existing `Diagnostics`-namespace registrations (`IDiagnosticsCollector`, `IRobloxProcessTracker`) for visual cohesion.
  - **`OnStartup`** — insert the gate-check block. **Placement detail (drift from spec §4.5):** the spec says "between line 63 and line 91." The gate block must run AFTER `_services.GetRequiredService<ThemeService>().ApplyAtStartup()` (lines 70–77 in current `App.xaml.cs`) so the modal's `BgBrush` resource has resolved before `ShowDialog()` paints. It must run BEFORE `tray.Show()` (line 94), `_singleInstance.StartListening()` (line 95), `mainWindow.Show()` (line 96), and `mutex.Acquire()` (line 91). Insertion point: immediately after the `try { ApplyAtStartup() } catch { }` block on line 77, before the `var tray = ...` resolution on line 79. Block:
    ```csharp
    var gate = _services.GetRequiredService<StartupGate>();
    if (!gate.ShouldProceed())
    {
        var modal = new Modals.RobloxAlreadyRunningWindow();
        modal.ShowDialog();
        Shutdown(0);
        return;
    }
    ```
    The `Shutdown(0) + return` ensures we exit cleanly without proceeding to mutex acquisition, tray show, or MainWindow show. MainWindow never renders; tray icon never appears.
  - **Banner-correct trigger:** placement-after-`ApplyAtStartup` instead of "between line 63 and 91" is genuine drift from the spec — log it for the docs item (item 5).
  Acceptance: Cold start with no `RobloxPlayerBeta.exe` running → app starts normally (current behavior preserved, no regression). Cold start with `RobloxPlayerBeta.exe` running → modal appears, MainWindow does NOT flash, tray icon does NOT appear, clicking `Quit RoRoRo` exits cleanly with no orphan process in Task Manager.
  Verify: `dotnet build` clean. `dotnet test` (full suite — existing tests don't exercise `App.OnStartup` so they should be unchanged green; the regression test is the manual smoke at the checkpoint). `git diff src/ROROROblox.App/App.xaml.cs` shows ONLY the two intended edits (DI registration + gate block) — no incidental churn. Commit: `feat(app): App.OnStartup wire-up — gate runs before mutex.Acquire`.

- [ ] **CHECKPOINT 1** (after item 4 — gate wired; manual smoke per spec §6 before docs/security pass)
  Run on a clean Win11 box (or current dev box if the environment is representative). Path order matters — A confirms detection fires, B confirms the off-path is unbroken, C confirms the after-RoRoRo case is not regressed.
  - **Path A — Roblox running BEFORE RoRoRo (the bug we're fixing):** Open `roblox.com/games/<any>` in Chrome → click Play → confirm `RobloxPlayerBeta.exe` is in Task Manager. Start RoRoRo from the build output (or sideload exe). **Verify:** modal appears with cycle-2 chrome, MainWindow does NOT flash, tray icon does NOT appear. Click `Quit RoRoRo`. Confirm `ROROROblox.App.exe` is gone from Task Manager (no orphan).
  - **Path B — clean cold start:** With Roblox closed (Task Manager confirms no `RobloxPlayerBeta.exe`), start RoRoRo. **Verify:** no modal, MainWindow renders, tray icon appears, mutex acquired (status indicator on tray shows On).
  - **Path C — Roblox launched AFTER RoRoRo (must NOT regress):** With RoRoRo running from path B, click Launch As on any saved account. **Verify:** Roblox opens correctly, no modal triggered (cycle 4 only checks at startup), the auth-ticket hand-off works (account loads as the saved alt, not as a duplicate of any existing user). Then close that Roblox session and click Launch As on a different saved account — confirm multi-instance still works (the second alt opens as itself).
  - **Path D — fail-open smoke (optional but recommended):** temporarily edit `RobloxRunningProbe.GetRunningPlayerPids` to throw an `InvalidOperationException` unconditionally, rebuild, start RoRoRo. **Verify:** app starts normally despite the throw, log file shows one warning per startup, no modal. Revert the temporary throw before proceeding to item 5.
  - If any path is shaky — especially Path C, the "must not regress" lane — fix before proceeding to item 5. Documentation & Security Verification assumes the wire-up is bulletproof.

- [ ] **5. Documentation & Security Verification**
  Spec ref: Cart-required final + `spec.md > §10 Decisions to log on completion`; canonical spec §10
  Effort: ~10–15 min
  Dependencies: item 4 + checkpoint 1 (all paths smoked clean)
  What to build (verification + documentation, no production code):
  - **README touch-up:** if any user-visible flow text mentions startup behavior or the multi-instance bug ("Roblox alts launching as the same user"), add a one-line note about the cycle-4 hard-block guard. If the README doesn't surface this flow at all, no update needed (don't invent a new section).
  - **Spec banner-correct (drift expected per item 4):** the gate's placement-after-`ApplyAtStartup` is genuine drift from spec §4.5's "between line 63 and 91" wording. Add a top-of-doc warning block to `docs/superpowers/specs/2026-05-08-roblox-already-running-detect-design.md` per pattern v from Vibe Thesis. Name what was originally proposed ("between line 63 BuildServiceProvider and line 91 mutex.Acquire") vs what was actually built ("after ThemeService.ApplyAtStartup so BgBrush is resolved before modal paints; still before mutex.Acquire"). **Do NOT rewrite the spec top-to-bottom — that destroys /reflect-time framing.** If any other genuine drift surfaces during build (banner triggers below), include it in the same banner block.
  - **Secrets scan:** `git diff main...HEAD` reviewed for `.ROBLOSECURITY` literals, PFX/cert bytes, or any cookie-shaped string. The pre-commit hook should have run clean on every cycle commit (commit log shows `[secret-scan] clean`); confirm. Manual eyeball at the diff to belt-and-suspenders the hook.
  - **Local-path audit:** `git diff main...HEAD` reviewed for `c:\Users\` references in committable code (per pattern kk from wbp-azure). Pre-commit hook covers this; manual confirm anyway.
  - **Dependency audit:** `git diff main...HEAD -- '*.csproj' 'Directory.Packages.props' '*.sln'` should show **zero changes** — this cycle adds zero new dependencies (canonical spec §3 explicit). Any non-zero diff is a red flag; investigate before proceeding.
  - **Deployment-security check:** DPAPI envelope shape unchanged — the cycle didn't touch `accounts.dat` or any DPAPI-protected file. Confirm by inspecting `IAccountStore` calls in the diff (should be zero) and verifying no test-file fixtures contain `01 00 00 00 D0 8C 9D DF` byte changes.
  - **Decisions log to 626 Labs Dashboard** via `mcp__626Labs__manage_decisions log`, three entries per canonical spec §10:
    1. Architectural — "Cycle 4 detection runs in a `StartupGate` class extracted from `App.OnStartup`, calling `IRobloxRunningProbe` (Core interface). Reason: keeps the trigger logic unit-testable without WPF; matches cycle-3 `JoinByLinkSave` pattern. Fail-open on probe exceptions — false-positive blocks the user from starting the app at all, false-negative is recoverable via the same manual workaround that exists today."
    2. UX — "Hard-block modal with single `Quit RoRoRo` button, no in-place recheck affordance. Reason: verified mutex-recovery data says quitting RoRoRo is required for clean re-acquisition; offering a 'try again' button would invite the broken state."
    3. Insertion point — "Gate runs BEFORE `mutex.Acquire()` in `App.OnStartup`, after `ThemeService.ApplyAtStartup()` so brand-token brushes resolve. Reason: never enter the broken state, never have to release a hostile mutex on shutdown. MainWindow doesn't flash; tray doesn't appear; clean exit."
    Tag all three with the bound RORORO project ID.
  - **Final test suite pass:** `dotnet test` — full suite, all green. Tests added in item 3 + every existing test from cycles 1-3.
  Acceptance: `dotnet test` 100% green. `git diff main...HEAD --stat` shows expected files only (`IRobloxRunningProbe.cs`, `RobloxRunningProbe.cs`, `RobloxAlreadyRunningWindow.xaml`, `RobloxAlreadyRunningWindow.xaml.cs`, `StartupGate.cs`, `StartupGateTests.cs`, `App.xaml.cs`, optional README touch + optional canonical-spec banner). Pre-commit hooks (`secret-scan`, `local-path-guard`) ran clean across all cycle commits. Three decisions logged to the dashboard. No new dependencies in any `.csproj`. Spec banner-correct only if drift was real.
  Verify: `dotnet test`. `git diff --stat main...HEAD`. `git log main..HEAD --oneline` reviewed for shape (5 commits visible — items 1-4 + this one). Browse the dashboard to confirm three new decisions tagged with the RORORO project ID. Commit (only if README/spec changed): `docs: README + spec banner-correct (only on drift)`.

---

## Risk callouts logged for `/build`

- **Item 3 (StartupGate TDD) is the most discipline-sensitive step.** Write the 5 tests first, watch them fail with the right kind of failure (`return value was true, expected false` vs `log entry never written`), then implement. Skipping the watch-them-fail step is the TDD anti-pattern that hides false greens — it shipped clean elsewhere; don't drop it here.
- **Gate placement in `App.OnStartup` IS drift from the spec.** §4.5 says "between line 63 and 91." Build reality says "must run AFTER `ApplyAtStartup` (line 77) so `BgBrush` resolves before the modal's `ShowDialog()` paints, but BEFORE `mutex.Acquire()` (line 91)." Banner-correct it at item 5 — don't ship the spec saying the gate slots in at line 64.
- **Path C of the checkpoint is the regression-guard, not a nice-to-have.** "Roblox launched AFTER RoRoRo still works" is the exact lane cycle 4 must NOT break. The whole reason runtime detection is non-goal is that this lane works correctly today. Smoke it explicitly — don't assume.
- **Fail-open semantics are the correctness bar, not a polish item.** Cases 4 + 5 of the test suite (probe throws → `ShouldProceed` returns `true` + warning logged) are the regression guard against "false-positive blocks the user from starting the app at all." If those tests turn flaky or get skipped, ship is blocked until they're green.
- **Cycle-2 modal chrome is hardcoded hex, not `DynamicResource` brushes for the accent colors.** `RobloxNotInstalledWindow.xaml:22` uses `Foreground="#F22F89"` directly; the spec's `MagentaBrush` / `CyanBrush` token references are aspirational. Match the existing modals' hardcoded-hex pattern at item 2 — keeps modals self-contained for theme-load timing safety. The Window background still uses `{DynamicResource BgBrush}` (which is theme-aware and load-stable).
- **Reference-impl provenance is unaffected.** No touch to `MultiBloxy.exe` or `PROVENANCE.txt` — the "clean reimplementation, not a fork" framing remains intact.

## Spec banner-correct triggers

If any of these happen during build, banner-correct the canonical spec at `docs/superpowers/specs/2026-05-08-roblox-already-running-detect-design.md` per pattern v from Vibe Thesis. The placement drift in item 4 is already on the docket — others may join it:

- **Already on the docket:** Gate placement after `ApplyAtStartup` (line 77) instead of "between line 63 and line 91" — surface in the item 5 banner block.
- `StartupGate` ends up living somewhere other than `src/ROROROblox.App/Startup/` (e.g., extracted to Core for cross-process testability).
- A test case from canonical spec §6 turns out to be redundant or impossible to express cleanly with the hand-rolled-fake pattern.
- Modal chrome ends up needing changes to the cycle-2 pattern (e.g., `WindowStartupLocation="CenterScreen"` doesn't render correctly when MainWindow is suppressed; falls back to OS-default position).
- `Application.Current.Shutdown()` doesn't actually exit the process and we add `Environment.Exit(0)` as a hard-fallback (canonical spec §9 names this as a known open question).
- The probe's `Process.GetProcessesByName("RobloxPlayerBeta")` returns false-positives or false-negatives in real-world testing that the spec didn't anticipate (e.g., Bloxstrap shimming the process name in some configurations).

If only the placement drift surfaces — implementation otherwise matched spec — banner-correct just that. Banner-corrects are for genuine drift, not ceremony.
