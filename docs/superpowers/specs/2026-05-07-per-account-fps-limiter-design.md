# Per-Account FPS Limiter — Design Spec

**Version:** v1.2.x feature add
**Date:** 2026-05-07
**Status:** Approved for implementation planning
**Branch:** `feat/per-account-fps-limiter`
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

## 1. Overview

Each saved account in ROROROblox gets its own FPS cap, applied at launch via Roblox's `DFIntTaskSchedulerTargetFps` FFlag in `ClientAppSettings.json`. The strategic frame is **parity-plus with Roblox Account Manager (RAM)** — RAM's FPS limiter is the holdout knob, but it has reliability gaps in the wild (notably for users on the Microsoft Store / UWP install of Roblox, whose `ClientAppSettings.json` lives at a sandboxed path RAM does not appear to write to). Our version handles both install layouts so it works for every clan member, not just the Bloxstrap / standalone slice.

The technical core is small:

1. **Before each `Launch As`, write the account's FPS to `ClientAppSettings.json`** in the latest installed Roblox version folder, merging with any existing FFlags so we don't trample Bloxstrap or Fishstrap users.
2. **Serialize launches through a semaphore with a 250ms hold** so per-account FPS is deterministic — back-to-back launches each get their own write window.

Per-process FPS is technically not possible on Roblox: all clients of one installed version read the same `ClientAppSettings.json` once at startup. The 250ms hold is the smallest gate that lets each launched process finish reading FFlags before the next one writes over them.

## 2. Goals and non-goals

**Goals (v1.2.x):**
- Per-account FPS dropdown inline on each account row.
- Preset list `20 / 30 / 45 / 60 / 90 / 120 / 144 / 165 / 240 / Unlimited` plus a `Custom` integer entry clamped 10–9999.
- `—` (don't write) is the default and a first-class option — accounts the user hasn't touched do not modify `ClientAppSettings.json`.
- Preserve existing FFlags in `ClientAppSettings.json` (Bloxstrap-friendly merge).
- Detect Bloxstrap as the registered `roblox-player` handler and surface a one-time dismissible warning that our FPS write may be overridden.

**Non-goals (v1.2.x):**
- Per-process FPS for already-running clients (technically infeasible without per-version sandboxing — out of scope for v1).
- Live FPS adjustment without relaunch.
- Bloxstrap deep integration (writing into Bloxstrap's intermediate config). We use the canonical Roblox version folder; Bloxstrap users are warned, not auto-routed.
- Macros, FOV cap, graphics-quality FFlags, or any non-FPS FFlag exposure. The dropdown is FPS-only.

## 3. Stack

No new dependencies. Uses what's already in the solution:

- `System.Text.Json` — read/merge/write `ClientAppSettings.json`.
- `Microsoft.Win32.Registry` — Bloxstrap detection via `HKCU\Software\Classes\roblox-player\shell\open\command`.
- `System.Threading.SemaphoreSlim` — launch serialization.
- `System.Diagnostics.FileVersionInfo` — already used by `RobloxCompatChecker` for the latest-version-folder discovery.

## 4. Architecture

```
MainViewModel
  └─ AccountSummary.FpsCap (bound to row dropdown)
  └─ on dropdown change → IAccountStore.UpdateFpsCapAsync(id, value)
  └─ on Launch-As → IRobloxLauncher.LaunchAsync(account)
                      ├─ await launchSemaphore.WaitAsync()
                      ├─ if account.FpsCap.HasValue:
                      │    await IClientAppSettingsWriter.WriteFpsAsync(value)
                      ├─ existing CSRF + ticket exchange + roblox-player URI launch
                      ├─ await Task.Delay(250)
                      └─ launchSemaphore.Release()

App startup
  └─ IBloxstrapDetector.IsBloxstrapHandler() → MainViewModel.BloxstrapWarningVisible
       (suppressed when settings.json has bloxstrapWarningDismissed: true)
```

**External boundaries (additions):**
- Filesystem: `%LOCALAPPDATA%\Roblox\Versions\<latest>\ClientSettings\ClientAppSettings.json` — read + atomic-write.
- Registry: `HKCU\Software\Classes\roblox-player\shell\open\command` — read-only.

## 5. Components & interfaces

### 5.1 `IClientAppSettingsWriter` (Core, new)

```csharp
interface IClientAppSettingsWriter {
    Task WriteFpsAsync(int? fps, CancellationToken ct = default);
}
```

`null` removes the `DFIntTaskSchedulerTargetFps` key (and `FFlagTaskSchedulerLimitTargetFpsTo2402` if we set it). Non-null value writes both keys (the second is `false` when `fps > 240`, otherwise omitted).

Implementation:
1. **Resolve all candidate version folders** (this is where we beat RAM — see §11 decisions log):
   - Standard / Bloxstrap install: `%LOCALAPPDATA%\Roblox\Versions\`
   - Microsoft Store / UWP install: `%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\LocalCache\Local\Roblox\Versions\` (glob the package folder by prefix; user-hash suffix varies)
   For each path that exists, find the version subfolder containing `RobloxPlayerBeta.exe` with the newest `LastWriteTime`.
2. **Pick the active install** — if both paths have a `RobloxPlayerBeta.exe`, pick the one with the most-recently-modified binary. If both are within 30 days of each other (both look actively used), write to **both** — the operation is cheap and idempotent. This catches users who switched between standalone and UWP and have stale folders.
3. For each chosen folder, path: `<versionFolder>\ClientSettings\ClientAppSettings.json`. Create `ClientSettings\` if missing.
4. Read existing JSON (start with empty `{}` if file missing or malformed). Set/remove the FFlag key(s).
5. Atomic write: write to `ClientAppSettings.json.tmp` in the same folder, `File.Move(tmp, dest, overwrite: true)`.
6. If neither path resolves to a real version folder → throw `ClientAppSettingsWriteException("Roblox version folder not found")`. Other failures (permission denied, disk full) log and throw the same exception with the underlying message — caller treats as non-fatal and continues with the launch.

### 5.2 `IBloxstrapDetector` (Core, new)

```csharp
interface IBloxstrapDetector {
    bool IsBloxstrapHandler();
}
```

Reads `HKCU\Software\Classes\roblox-player\shell\open\command` (default value), returns true if the command path contains `Bloxstrap` (case-insensitive) — Bloxstrap's binaries are consistently named `Bloxstrap.exe` / `BloxstrapBootstrapper.exe`. Read-only — never modifies the registry. If the registry key is missing or empty, returns false (no exception).

### 5.3 `Account` record extension (Core, modified)

```csharp
record Account(
    Guid Id,
    string DisplayName,
    string AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLaunchedAt,
    int? FpsCap            // NEW — null means "don't write"
);
```

`FpsCap` defaults to `null`. v1.1-shaped JSON (no `FpsCap` key) deserializes to `null` cleanly via `System.Text.Json`'s missing-property tolerance.

### 5.4 `IAccountStore` extension (Core, modified)

Add one method:

```csharp
Task UpdateFpsCapAsync(Guid id, int? fps, CancellationToken ct = default);
```

Reads the encrypted blob, replaces the account's `FpsCap`, atomic-writes back. Same DPAPI envelope, same atomic-write semantics as the existing `UpdateCookieAsync`.

### 5.5 `RobloxLauncher` extension (Core, modified)

Inject `IClientAppSettingsWriter` and a launch-scoped `SemaphoreSlim(1, 1)`. `LaunchAsync` becomes:

```csharp
async Task<LaunchResult> LaunchAsync(Account account, string cookie, string? placeUrl) {
    await launchSemaphore.WaitAsync();
    try {
        if (account.FpsCap.HasValue) {
            try { await writer.WriteFpsAsync(account.FpsCap.Value); }
            catch (ClientAppSettingsWriteException) { /* log, continue */ }
        }
        var result = await ExistingLaunchFlow(cookie, placeUrl);
        await Task.Delay(250); // let Roblox finish reading FFlags
        return result;
    } finally {
        launchSemaphore.Release();
    }
}
```

The 250ms delay is held inside the semaphore — that's what makes per-account deterministic. The cost is ~250ms of added latency per launch in the rapid-fire case; a single launch sees no difference because the semaphore is already free.

### 5.6 `MainViewModel` and row UI (App, modified)

Each `AccountSummary` exposes `FpsCap` (int?) and a list of preset options bound to a `ComboBox` on the row. The dropdown is rendered between the status text and the existing `Launch As` button:

```
[avatar] [name + status]   [FPS: 60 ▾]   [Re-auth?]   [Launch As]   [Remove]
```

Dropdown content:
- `—` → null (don't write)
- `20` · `30` · `45` · `60` · `90` · `120` · `144` · `165` · `240`
- `Unlimited` → 9999
- `Custom...` → opens an inline number entry, clamped 10–9999, shows the resulting integer in the dropdown when committed (`Custom (75)`).

A new `BloxstrapWarningBanner` (yellow, dismissible) renders at the top of MainWindow when `IBloxstrapDetector.IsBloxstrapHandler() == true` AND `settings.bloxstrapWarningDismissed == false`. Copy:

> *Bloxstrap is set as your Roblox launcher — it will override per-account FPS. Set FPS in Bloxstrap to match. [Dismiss]*

Dismissal writes `bloxstrapWarningDismissed: true` to `%LOCALAPPDATA%\ROROROblox\settings.json`.

## 6. Data flows

### 6.1 Set per-account FPS

1. User picks a value from the row dropdown (or commits a Custom integer).
2. `MainViewModel.OnFpsCapChanged(account, value)` → `IAccountStore.UpdateFpsCapAsync(id, value)`.
3. AccountStore reads encrypted blob, mutates the account's `FpsCap`, atomic-writes back. UI binding refreshes.

No FFlag write happens here — the write only happens at launch time. Setting FPS to `—` removes any previously-written FFlag on the next launch (because `WriteFpsAsync(null)` removes the key).

### 6.2 Launch As (with FPS)

1. User clicks `Launch As` on a row whose `FpsCap` is set.
2. `RobloxLauncher.LaunchAsync` acquires the launch-semaphore.
3. `IClientAppSettingsWriter.WriteFpsAsync(account.FpsCap.Value)` reads the existing `ClientAppSettings.json`, sets `DFIntTaskSchedulerTargetFps` (and conditionally `FFlagTaskSchedulerLimitTargetFpsTo2402: false`), atomic-writes.
4. Existing CSRF dance + ticket exchange + `roblox-player:` URI launch via `Process.Start` (unchanged).
5. 250ms hold inside the semaphore — Roblox reads its FFlags during this window.
6. Semaphore released. Next queued launch (if any) proceeds.

If `FpsCap` is null, step 3 is skipped — `ClientAppSettings.json` is untouched.

## 7. Error handling

The five existing buckets from spec §7 are unchanged. One new sub-case:

### 7.7 FFlag write failed
**Trigger:** No Roblox installed, version folder missing, file permission denied, disk full, or `ClientSettings\` could not be created.

**Behavior:** Log the exception. Continue with the launch — FPS not being applied is a degraded state, not a launch blocker. Surface a yellow banner on the next MainWindow render: *"Couldn't write FPS setting (Roblox folder not found). Your launch will use Roblox's default FPS."* Banner dismissed automatically once a write succeeds.

**No modal.** This is a non-blocking degradation, not an error the user must act on.

## 8. Testing

### 8.1 Unit (`ROROROblox.Tests`, new file `ClientAppSettingsWriterTests.cs`)

- Round-trip: write 60, read back, value is 60.
- Preserve other FFlags: pre-seed file with `{ "FStringSomeOther": "x" }`, write FPS, both keys present after.
- Missing file: file absent, write 60, file created with only the FPS key.
- Malformed file: pre-seed with garbage, write 60, file replaced with only the FPS key (no exception).
- Null clears: pre-seed with FPS=60, write null, key absent (other keys preserved).
- Above 240: write 300, both `DFIntTaskSchedulerTargetFps: 300` and `FFlagTaskSchedulerLimitTargetFpsTo2402: false` present.
- 240 or below: write 144, only `DFIntTaskSchedulerTargetFps: 144` present (the cap-removal flag is omitted).
- Atomic write: simulate write failure mid-flight, original file intact.
- No Roblox installed (neither path exists): writer throws `ClientAppSettingsWriteException`.
- UWP install only (no standalone path): writer resolves the UWP path, writes successfully.
- Standalone only (no UWP package): writer resolves the standalone path, writes successfully.
- Both paths active (both binaries modified within 30 days): writer writes to both files in sequence, both contain the FPS flag after.

### 8.2 Unit (`BloxstrapDetectorTests.cs`)

- Path containing `Bloxstrap` (case-insensitive variants) → true.
- Path pointing to `RobloxPlayerBeta.exe` directly → false.
- Registry key missing → false (no exception).

### 8.3 Unit (`AccountStoreTests.cs`, additions)

- v1.1-shaped JSON (no `FpsCap` key) deserializes with `FpsCap == null`.
- `UpdateFpsCapAsync` round-trip preserves cookie + display name + avatar.

### 8.4 Integration (`RobloxLauncherTests.cs`, additions)

- Two parallel `LaunchAsync` calls with different FPS values: verify writes are sequenced (writer called in order, second write happens after first delay completes). Use a test double for the writer that records call order + timestamps.

### 8.5 Manual smoke (added to spec §8)

- Set Account A to 30, B to 144, C to Unlimited. Launch all three rapid-fire from row buttons. Open Roblox dev console (Shift+F5 or Ctrl+Shift+F2) in each and confirm actual FPS matches (allow ±2 fps rounding noise).
- Pre-existing `ClientAppSettings.json` with a Bloxstrap FFlag: launch with FPS=60. Confirm both Bloxstrap's flag and ours are present after.
- Bloxstrap registered as `roblox-player` handler: launch app, confirm warning banner appears. Dismiss. Restart app, confirm banner stays dismissed.
- Set FPS, then set back to `—`. Confirm next launch removes the key from `ClientAppSettings.json`.

## 9. Distribution

No distribution changes. Same MSIX flavors, same Velopack release path. The feature ships as a normal point release on the existing channel.

## 10. Open items

None mandatory before implementation. The Bloxstrap detection heuristic (path-contains-`Bloxstrap`) may need tightening if Bloxstrap renames or relocates; tracked as a v1.2.x follow-up.

## 11. Decisions log

| Decision | Why |
|---|---|
| Per-account FPS over app-wide-only | RAM offers per-account; we need parity to flip clan holdouts. App-wide-only ships nothing the user can't already do. |
| `—` (don't write) as the default | Bloxstrap and Fishstrap users already manage FFlags; default-not-writing avoids a fight on accounts the user hasn't configured. |
| Merge over overwrite for `ClientAppSettings.json` | Preserves Bloxstrap's other FFlags. Overwriting would erase user customization on every launch. |
| 250ms launch-serialization gate | Smallest gate that gives Roblox time to finish reading FFlags before the next write. Tested empirically against rapid-fire alts. |
| `Custom` clamped 10–9999 | 10 is the floor below which the client gets visibly broken; 9999 is the practical ceiling. Anything beyond is undefined-behavior territory. |
| 20 as the lowest preset (not 30) | Pet Sim 99 alt-farming use case explicitly wants very-low background FPS to multiply alts on a single GPU. |
| Bloxstrap detect-and-warn rather than disable | Disabling the dropdown is honest but creates a dead control on every row. Warning preserves the UX for users who later switch off Bloxstrap. |
| FPS write failure is non-blocking | The launch is the load-bearing operation. FPS is enrichment — its failure should not strand the user. |
| Reuse `RobloxCompatChecker`'s version-folder discovery | Same problem, same solution — DRY win and one place to fix when Roblox changes the version folder layout. (Note: §5.1 extends this with multi-source resolution for the UWP path; `RobloxCompatChecker` should adopt the same multi-source logic in a follow-up so the version banner doesn't miss UWP users.) |
| Multi-source version folder discovery (standalone + UWP) | Reported reliability gap: RAM's FPS limiter "doesn't work for everybody," and the Microsoft Store / UWP Roblox install path is the most likely culprit (sandboxed `%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\...` location that RAM does not write to). Covering both paths is the cheapest "we beat RAM in the wild" win. |
| Write to both folders if both are active within 30 days | Users sometimes have a stale standalone install left over after switching to UWP (or vice versa). Cheap idempotent double-write avoids guessing wrong. |
| FFlag name configurable via `roblox-compat.json` (optional field) | Same future-proofing pattern as the mutex name. If Roblox renames `DFIntTaskSchedulerTargetFps` we ship a remote config update + Velopack release without rebuilding. |

## Appendix A — Why per-process FPS is infeasible

Roblox reads `ClientAppSettings.json` once at process startup, from a single path: `%LOCALAPPDATA%\Roblox\Versions\<latest>\ClientSettings\ClientAppSettings.json`. All running clients of that installed version share that file. Distinct files per process would require either:

- **Per-version installs** — duplicating the ~500MB Roblox install per account. Breaks Roblox's auto-update detection. Not a clean ship.
- **Per-process file shim via filesystem redirection** — would require a kernel-mode filter driver. Out of scope (and out of trust budget) for a free Microsoft Store app.
- **In-memory FFlag patching** — undocumented, requires injecting into `RobloxPlayerBeta.exe`, instantly flagged by Roblox as cheating. Wall.

The 250ms launch-serialization gate is the pragmatic answer: per-account in the common case (sequential launches), with the rare back-to-back-within-250ms window degrading to "the second account's FPS applies to both" — the same race RAM accepts.

## Appendix B — Why RAM's FPS limiter doesn't work for some users (read of the field)

Reported failure mode in the clan: *"I set FPS in RAM, the cap doesn't apply."* No public bug tracker or root-cause writeup confirms the cause; this section is reasoned-from-evidence, not authoritative.

Most likely root causes, ranked:

1. **UWP / Microsoft Store install of Roblox.** The sandboxed `ClientAppSettings.json` lives at `%LOCALAPPDATA%\Packages\ROBLOXCORPORATION.ROBLOX_*\LocalCache\Local\Roblox\Versions\<latest>\ClientSettings\`. RAM appears to only target the standalone `%LOCALAPPDATA%\Roblox\Versions\` path. A user with both installs at any point, or who never had a standalone install, gets RAM's write into a folder Roblox never reads from. **Our §5.1 multi-source discovery directly addresses this slice.**
2. **Bloxstrap as the registered launcher.** Bloxstrap's launch path rewrites `ClientAppSettings.json` from its own FFlag config before the Roblox process starts, blowing away whatever RAM (or we) wrote. Our detect-and-warn (§5.2) tells the user where the override came from rather than letting it fail silently.
3. **FFlag rename on the Roblox side.** Periodic — Roblox reorganizes FFlag namespaces. Our `roblox-compat.json` carries an optional `fpsFFlagName` field for hours-not-rebuild recovery (§11).
4. **Stale standalone install while UWP is the active one.** Folder exists but the binary inside is months old; RAM writes to it but the running Roblox process is reading from the UWP path. Our most-recently-modified resolution + the both-paths-within-30-days rule covers this.

Items 1 + 4 together are likely the bulk of "doesn't work for me" reports. Items 2 + 3 are smaller slices and were already in our threat model from the start of this design.
