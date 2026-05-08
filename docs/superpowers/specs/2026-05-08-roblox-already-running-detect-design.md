# Detect Roblox-Already-Running at Startup — Design Spec

**Version:** v1.3.x feature add (cycle 4 — follows save-pasted-links)
**Date:** 2026-05-08
**Status:** Approved for implementation planning
**Branch (implementation):** `feat/roblox-already-running-detect` (cut from `main` after spec lands)
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

## 1. Overview

When `RobloxPlayerBeta.exe` is running before RoRoRo starts (e.g., the user opened Roblox via Chrome's Discord-deeplink → "Open in Roblox" or via the Start Menu shortcut), every subsequent **Launch As** opens as the same Roblox user — alts launch into the existing logged-in account regardless of which auth-ticket RoRoRo handed off. The roblox-player URI handler routes to the existing process, which already has a user identity bound; the auth-ticket hand-off is effectively ignored.

**Verified by user 2026-05-08:** recovery requires quitting **BOTH** RoRoRo AND Roblox, then restarting RoRoRo. Closing only Roblox is NOT enough — `MutexHolder.Acquire` is one-shot at process startup; there's no in-flight re-acquisition path. Memory: `project_rororo_mutex_recovery.md`.

The fix is a hard-block startup check. Before `mutex.Acquire()`, probe for any running `RobloxPlayerBeta.exe`. If found, show a modal explaining the situation with a single **`Quit RoRoRo`** button. We never enter the broken state — the mutex is never acquired against a hostile Roblox process.

The technical core is small: a probe interface + impl in Core (~30 lines), a plain WPF Window with established cycle-2 chrome (~80 lines XAML+code-behind), and an extracted `StartupGate` class that owns the probe + modal + shutdown decision (~30 lines, fully testable).

## 2. Goals and non-goals

**Goals (cycle 4):**

- Detect any `RobloxPlayerBeta.exe` process at app startup, BEFORE `mutex.Acquire()` runs.
- If detection fires: show a hard-block modal with a single `Quit RoRoRo` button → `Application.Current.Shutdown()` on dismiss. MainWindow never renders.
- If detection is clean: proceed with the existing `mutex.Acquire()` + `MainWindow.Show()` path unchanged.
- **Fail open** on any probe failure (rare Win32 errors during enumeration) — log warning, proceed as if no Roblox running. False-negative is recoverable via the existing manual workaround; false-positive blocks the user from starting the app at all.
- Modal chrome matches the established cycle-2 modal pattern (`WebView2NotInstalledWindow`, `RobloxNotInstalledWindow`, `DpapiCorruptWindow`) — plain `Window` with `Background="{DynamicResource BgBrush}"`, immersive dark title bar, brand-token cyan/magenta accents.
- Extracted `StartupGate` class so the trigger logic is unit-testable without WPF.

**Non-goals (cycle 4):**

- **Runtime detection (Roblox launches AFTER RoRoRo).** Not the bug — RoRoRo's mutex hold defeats Roblox's own singleton check at *its* launch time, which is exactly when Roblox is launched after RoRoRo. Multi-instance keeps working in that direction.
- **In-place recheck affordance** ("I closed Roblox — try again"). Verified data says recovery requires quitting RoRoRo. No optimistic retry button.
- **`RobloxStudio.exe` detection.** Studio doesn't share the player singleton mutex; ignoring it avoids false-positives for users with Studio open.
- **Bloxstrap-specific handling.** Bloxstrap launches `RobloxPlayerBeta.exe` — same detection covers it, no special-casing.
- **Cross-Windows-user-session detection.** Edge case; reliably detecting Roblox running under a different Windows account requires elevated process inspection. Not chasing.
- **Mutex re-acquisition path inside `MutexHolder`.** Out of scope; would enable an in-place recheck affordance which we explicitly don't want.

## 3. Stack

No new dependencies. Reuses what's already in the app:

- `System.Diagnostics.Process.GetProcessesByName` — already used in `RobloxProcessTracker.cs:188` and `DiagnosticsCollector.cs`.
- `Microsoft.Extensions.Logging.Abstractions` — already in Core for `ILogger`.
- WPF `Window` chrome — established pattern from cycle-2 modals.
- DI registration in `App.xaml.cs:ConfigureServices` — established for every other service.

## 4. Architecture and change surface

Four files. Test-driven plumbing for the probe + decision logic.

### 4.1 `src/ROROROblox.Core/Diagnostics/IRobloxRunningProbe.cs` (NEW)

```csharp
namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Enumerates currently-running RobloxPlayerBeta.exe PIDs at the moment of the call.
/// Used at app startup (before mutex.Acquire) to detect "Roblox started before RoRoRo"
/// — that scenario breaks multi-instance because the auth-ticket hand-off routes through
/// an already-running process with a bound user identity. cycle 4 (2026-05-08).
/// </summary>
public interface IRobloxRunningProbe
{
    /// <summary>
    /// Snapshot of every RobloxPlayerBeta.exe PID running on this Windows user session
    /// at call time. Empty list = clean. Throwing implementations should be wrapped at
    /// the call site with fail-open semantics — see StartupGate.
    /// </summary>
    IReadOnlyList<int> GetRunningPlayerPids();
}
```

### 4.2 `src/ROROROblox.Core/Diagnostics/RobloxRunningProbe.cs` (NEW)

Thin wrapper:

```csharp
public sealed class RobloxRunningProbe : IRobloxRunningProbe
{
    private const string PlayerProcessName = "RobloxPlayerBeta";

    public IReadOnlyList<int> GetRunningPlayerPids()
    {
        var processes = Process.GetProcessesByName(PlayerProcessName);
        try
        {
            return processes.Select(p => p.Id).ToArray();
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}
```

### 4.3 `src/ROROROblox.App/Modals/RobloxAlreadyRunningWindow.xaml` + `.xaml.cs` (NEW)

Plain `Window` matching cycle-2 chrome. ~520x320, `ResizeMode="NoResize"`, `WindowStartupLocation="CenterScreen"`, `Background="{DynamicResource BgBrush}"`. Layout (top-to-bottom):

1. Heading: `Roblox is already running.` — Space Grotesk 20pt bold, `CyanBrush`.
2. Body paragraph: `Multi-instance launches won't work right while another Roblox process is open outside of RoRoRo. To get back to a clean state:` — Inter 12pt, `WhiteBrush`.
3. Numbered list:
   - `1. Close Roblox`
   - `2. Close RoRoRo`
   - `3. Re-open RoRoRo`
   Numerals in `MagentaBrush` (matching brand pairing); steps in `WhiteBrush`.
4. Mono-micro line: `MUTEX HOLD REQUIRES CLEAN START` — JetBrains Mono 10pt, uppercase, 0.12em letter spacing, `MutedTextBrush`.
5. Action button row: single **`Quit RoRoRo`** button — `Background="{DynamicResource CyanBrush}"`, `Foreground="{DynamicResource NavyBrush}"`, `IsDefault="True"`, `IsCancel="True"` (Esc also quits). No Cancel button — there's no in-place recovery path.

Code-behind: trivial `OnQuitClick` handler that calls `Application.Current.Shutdown()`. The Window's purpose is to display + collect the dismissal; `App.OnStartup` handles the shutdown sequencing.

### 4.4 `src/ROROROblox.App/Startup/StartupGate.cs` (NEW)

Extracted decision class so the trigger logic is unit-testable without WPF:

```csharp
public sealed class StartupGate
{
    private readonly IRobloxRunningProbe _probe;
    private readonly ILogger<StartupGate> _log;

    public StartupGate(IRobloxRunningProbe probe, ILogger<StartupGate>? log = null) { ... }

    /// <summary>
    /// Returns true if RoRoRo should proceed with normal startup (no foreign Roblox).
    /// Returns false if the caller should show the already-running modal and shut down.
    /// On probe failure, returns true (fail-open) and logs a warning.
    /// </summary>
    public bool ShouldProceed()
    {
        try
        {
            var pids = _probe.GetRunningPlayerPids();
            if (pids.Count > 0)
            {
                _log.LogInformation("Detected {Count} running RobloxPlayerBeta.exe process(es) at startup; blocking. PIDs: {Pids}", pids.Count, string.Join(",", pids));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RobloxRunningProbe threw; failing open and proceeding with startup.");
            return true;
        }
    }
}
```

### 4.5 `src/ROROROblox.App/App.xaml.cs` (MODIFIED)

Two edits:

**`ConfigureServices`** — register `IRobloxRunningProbe` + `StartupGate`:

```csharp
services.AddSingleton<IRobloxRunningProbe, RobloxRunningProbe>();
services.AddSingleton<StartupGate>();
```

**`OnStartup`** — insert between line 63 (`_services = services.BuildServiceProvider();`) and line 91 (`var acquired = mutex.Acquire();`):

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

The `Shutdown(0)` + `return` ensures we exit cleanly without proceeding to `mutex.Acquire()`, `tray.Show()`, or `mainWindow.Show()`. MainWindow never renders. The modal is the only UI surface.

## 5. Soft-fail discipline

**Fail open on probe failure.** Every awaited call inside `StartupGate.ShouldProceed` is wrapped in `try/catch`. If `Process.GetProcessesByName` throws (rare but real — e.g., process snapshot fails mid-enumeration on heavily loaded machines), `ShouldProceed` returns `true` and logs a warning. Reasoning:

| Failure mode | Effect | Recovery |
|---|---|---|
| **False-negative** (probe returns empty when Roblox actually running) | User experiences the original bug — alts launch as the same user | Existing manual workaround: quit both, restart RoRoRo |
| **False-positive** (modal shown when no Roblox running) | User cannot start the app at all | NONE — user has no escape path |

False-positive is unrecoverable from the user's perspective. Fail-open biases toward false-negatives, which are recoverable. The asymmetry justifies the soft-fail.

## 6. Testing (TDD, 5 unit cases)

**New test file:** `src/ROROROblox.Tests/StartupGateTests.cs`. Targets `StartupGate.ShouldProceed` with a hand-rolled `FakeRobloxRunningProbe` (zero new dependencies — same pattern as cycle 3's `JoinByLinkSaveTests`).

Cases:

1. **`Probe returns empty list`** → `ShouldProceed` returns `true`. No log warning.
2. **`Probe returns one PID`** → `ShouldProceed` returns `false`. Information-level log entry with the PID.
3. **`Probe returns multiple PIDs`** → `ShouldProceed` returns `false`. Information-level log entry with all PIDs.
4. **`Probe throws InvalidOperationException`** → `ShouldProceed` returns `true` (fail-open). Warning-level log entry with exception.
5. **`Probe throws unexpected exception type`** → `ShouldProceed` returns `true`. Warning-level log entry. Defensive — covers any future Win32-shaped throw.

The probe wrapper itself (`RobloxRunningProbe`) is not unit-tested — it's a thin wrapper over `Process.GetProcessesByName`. Integration coverage comes from the manual smoke step.

**Manual smoke (per spec §8 pattern, run on a clean Win11 box):**

1. Launch Roblox externally (e.g., open `roblox.com/games/<id>` in Chrome → click Play). Confirm `RobloxPlayerBeta.exe` is in Task Manager.
2. Start RoRoRo from Start Menu / sideload exe.
3. **Verify:** Modal appears, MainWindow does NOT flash, tray icon does NOT appear.
4. Click `Quit RoRoRo`. App exits cleanly (no orphan process; check Task Manager for `ROROROblox.App.exe`).
5. Close Roblox.
6. Start RoRoRo again. **Verify:** Normal startup, no modal, MainWindow renders, tray icon appears.
7. Once RoRoRo is up: launch Roblox externally again (the "after RoRoRo" case). **Verify:** No modal triggered (cycle 4 only checks at startup), and a subsequent Launch As works correctly (mutex already held).

## 7. Branch + commit plan

**Branch:** `feat/roblox-already-running-detect` cut from `main`. 5 commits:

1. `feat(core): IRobloxRunningProbe + RobloxRunningProbe impl`
2. `feat(app): RobloxAlreadyRunningWindow modal — cycle-2 chrome`
3. `feat(app): StartupGate — probe-driven shutdown decision + 5-case TDD test suite`
4. `feat(app): App.OnStartup wire-up — gate runs before mutex.Acquire`
5. `docs: README + spec banner-correct (only on drift)`

PR opens against `main`. Review checklist: visual smoke on the modal (matches cycle-2 chrome side-by-side), startup smoke on a clean Win11 box, dependency audit (zero new csproj/sln changes).

## 8. Out of scope (deliberate)

Repeated for emphasis — these are explicitly NOT in this cycle:

- Runtime detection of Roblox launches that happen after RoRoRo is up. Different mechanism, doesn't fail.
- In-place recheck affordance ("I closed Roblox, try again"). Verified-broken recovery model.
- `MutexHolder.ReleaseAndReacquire()` — would enable an in-place recheck which we don't want.
- `RobloxStudio.exe` detection.
- Bloxstrap-specific handling.
- Cross-Windows-user-session detection.
- Auto-launch-main coordination (already has its own defensive logic at `App.xaml.cs:175-179` — that's a separate code path, doesn't interact with cycle 4's gate).

## 9. Open questions / future

- **What happens if `Application.Current.Shutdown()` doesn't actually exit the process** (rare; happens when other foreground threads are blocked)? The existing pattern doesn't guard against this; if it surfaces in smoke, we add `Environment.Exit(0)` as a hard-fallback. Flag for /reflect.
- **Modal localization.** Hardcoded English copy. v1.x ships English-only; localization is a forward-looking decision (post-Store-launch).
- **Telemetry on detection-fired events.** Useful to know how often this triggers in the wild (informs whether a more sophisticated UX is justified). Crash report opt-in (already on the roadmap) is the surface for that.

## 10. Decisions to log on completion

After implementation merges, log to the 626 Labs Dashboard via `mcp__626Labs__manage_decisions log`:

- **Architectural choice:** "Cycle 4 detection runs in a `StartupGate` class extracted from `App.OnStartup`, calling `IRobloxRunningProbe` (Core interface). Reason: keeps the trigger logic unit-testable without WPF; matches cycle-3 `JoinByLinkSave` pattern. Fail-open on probe exceptions — false-positive blocks the user from starting the app at all, false-negative is recoverable via the same manual workaround that exists today."
- **UX choice:** "Hard-block modal with single `Quit RoRoRo` button, no in-place recheck affordance. Reason: verified mutex-recovery data says quitting RoRoRo is required for clean re-acquisition; offering a 'try again' button would invite the broken state."
- **Insertion point:** "Gate runs BEFORE `mutex.Acquire()` in `App.OnStartup`, not after. Reason: never enter the broken state, never have to release a hostile mutex on shutdown. MainWindow doesn't flash; tray doesn't appear; clean exit."
