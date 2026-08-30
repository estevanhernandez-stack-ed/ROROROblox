# Contributing to RORORO

This is a small, single-developer product for now. The notes below are the muscle memory you'll want re-reading before each release tag — most of it isn't checked by the build pipeline.

## Tech baseline

- **OS:** Windows 11, or Windows 10 22H2 (build 19045+, the Store package floor).
- **.NET:** SDK 10.0.203 is pinned by `global.json` (`rollForward: latestFeature`). `winget install Microsoft.DotNet.SDK.10`, or a user-scope install with Microsoft's `dotnet-install.ps1 -InstallDir "$env:USERPROFILE\.dotnet"` (avoids UAC; `scripts/build-msix.ps1` probes user-scope installs before PATH).
- **Windows SDK:** 10.0.22621+ for `makeappx.exe` + `signtool.exe`. Install via `winget install Microsoft.WindowsSDK.10.0.22621` or grab the latest from windows.com.
- **Roblox:** installed locally so the `roblox-player:` protocol handler is registered. Required for the auth-ticket spike and end-to-end Launch As tests.

## Day-to-day

```powershell
# Build (always name the .slnx: a stray gitignored ROROROblox.sln makes bare `dotnet build` fail with MSB1011)
dotnet build ROROROblox.slnx -c Release

# Run
dotnet run --project src/ROROROblox.App

# Test: whole solution (what CI runs), or one project
dotnet test ROROROblox.slnx -c Release --no-build
dotnet test src/ROROROblox.Tests/ -c Release --no-build
dotnet test src/ROROROblox.PluginTestHarness/ -c Release --no-build
```

**A running dev-build RoRoRo locks `bin\Debug`.** A Debug build fails at the copy step while an
instance launched from `bin\Debug` is up; build Release or quit the instance from the tray
(`Get-Process ROROROblox.App | Select-Object Path` shows which build is running). The older advice to
close RoRoRo before the test suite dated from F-105, whose real cause was the render harness running
the app's startup inside the test host; that was fixed on 2026-08-21 (`App.SuppressStartupForRenderHarness`)
and the suite now passes with an instance running.

**Nothing skips on CI any more.** The whole-window render gates used to carry a CI skip
(`[WindowRenderFact]`, `RORORO_FORCE_WINDOW_RENDER`); the skip and the env var were removed on
2026-08-21 when the CI wedge turned out to be the same F-105 defect (the attribute stays as the one place a future skip would live; its `SkipReason` is always null). A run at HEAD reports
**1899 passed, 0 skipped** for the unit project and **24 passed, 1 skipped** for the harness (the skip
is a deliberate `[Fact(Skip=...)]` for mid-stream consent revocation, deferred since v1.4).

Also worth knowing: an aborted run prints `Passed!  - Failed: 0` on one line and `Test Run Aborted.`
on the next. `dotnet test` does exit non-zero, so CI catches it, but do not read the first line alone.

The pre-commit hooks are git-installed via `.claude/hooks/install.ps1`. They block commits containing the real `.ROBLOSECURITY` cookie prefix or `c:\Users\<name>\` paths in code. False positives are rare — see [.claude/hooks/README.md](.claude/hooks/README.md) before bypassing.

## Auth-ticket spike (the release gate)

The contract between us and `auth.roblox.com/v1/authentication-ticket` can shift. Re-run the spike whenever:
- A user reports "Launch As stops working" with no obvious cause.
- Before a release tag, after long quiet periods on the Roblox-API side.
- Any time `dpapi-cookie-blast-radius` or `auth-ticket-flow-validator` agents flag something.

```powershell
$env:RORORO_TEST_COOKIE = '<paste .ROBLOSECURITY value from a TEST account>'
dotnet run --project spike/auth-ticket -- --validate-only
```

Document any contract shift in [process-notes.md](process-notes.md), update [the canonical spec](docs/superpowers/specs/2026-05-03-rororoblox-design.md) §5.7 / §6.2, log a decision via the dashboard MCP. Don't proceed to a release until the spike is green.

## Building MSIX

Two flavors. Both fail fast if the Store-bound logos under `src/ROROROblox.App/Package/Logos/` are missing or look like programmatic placeholders.

### Sideload (clan distribution)

```powershell
# One-time per dev machine — generate the self-signed cert.
powershell -ExecutionPolicy Bypass -File scripts/generate-dev-cert.ps1 -Password 'pick-a-password'

# Build the sideload MSIX.
powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword 'pick-a-password'
```

Output: `dist/RORORO-Sideload-x64-<version>.msix` (signed; the name carries flavor, arch and version) + `dev-cert.cer` (the public cert your testers import into **Local Machine → Trusted People** before installing).

The first-install flow on a fresh Win11 box:
1. Tester downloads `dev-cert.cer` and the `RORORO-Sideload-*.msix`.
2. Right-click `dev-cert.cer` → Install → Local Machine → Trusted People.
3. Double-click the `.msix` to install.
4. SmartScreen will prompt: "More info → Run anyway." Document this with a 30-second video on the README.
5. RORORO shows up in Start Menu.

### Store

```powershell
# Patches the csproj <Version> and Package.appxmanifest together, then calls build-msix.ps1 -Store.
powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 -Version <x.y.z.0> -IdentityName 626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName "626Labs LLC"
powershell -ExecutionPolicy Bypass -File scripts/finalize-store-build.ps1 -Version <x.y.z.0> -IdentityName 626LabsLLC.RoRoRoBlox -PublisherCN "CN=177BCE59-0966-4975-9962-10E36652141F" -PublisherDisplayName "626Labs LLC" -Architecture arm64
```

Output: `dist/RORORO-Store-<arch>-<version>.msix` (unsigned — Partner Center signs on submission). A submission ships **both** architectures. The fourth version component must be `0` (Partner Center rejects anything else) and `PublisherDisplayName` is `626Labs LLC` with no space. Calling `build-msix.ps1 -Store` directly also works (`-Runtime win-arm64` for Arm) but does not set the version. Validate locally before uploading:

```powershell
& "$env:ProgramFiles(x86)\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe" verify /p dist/RORORO-Store-x64-<version>.msix
```

Then upload via Partner Center → Apps & games → RORORO → Packages.

## Asset production (logos, splash, tray icons)

**Real assets must come from the `626labs-design` skill** — never ship programmatic placeholders. See [`src/ROROROblox.App/Package/Logos/README.md`](src/ROROROblox.App/Package/Logos/README.md) for the canonical sizes and the skill prompt.

The build script's logo-presence check is the gate. If you bypass it via `-AllowPlaceholders`, you accept a Store rejection or a bad clan-distribution moment.

The tray icon set (`src/ROROROblox.App/Tray/Resources/tray-{on,off,error,warn}.ico` plus the title-bar PNGs) is final art: on/off/error are rendered by `scripts/generate-tray-icons.ps1`, the warn pair was added by hand on 2026-08-01. Re-running the script does not regenerate the warn state.

## Releasing

The current runbook is [docs/store/release-playbook.md](docs/store/release-playbook.md) (`docs/checklist.md` is a retired v1.21 cycle snapshot). The short form:

1. Re-run the auth-ticket spike. PASS.
2. Walk every spec §8 manual smoke scenario on a clean Win11 VM.
3. Confirm the Store logos and tray icons are the design-skill output (`build-msix.ps1` refuses placeholders).
4. Bump the version with `scripts/finalize-store-build.ps1 -Version x.y.z.0 ...` (patches the csproj `<Version>` and the manifest together; the fourth component must be `0`).
5. Build sideload + Store MSIX flavors. Validate both.
6. Tag the release: `git tag v1.1.x.0 && git push origin v1.1.x.0`.
7. Cut the Velopack `Setup.exe` + delta package. **Two paths, same artifact:**

   **CI (default — recommended):** the tag push triggers `.github/workflows/release.yml`, which runs on `windows-latest`, executes the test suite, fetches the prior release for delta computation, runs `vpk pack`, and uploads everything as a **draft** GitHub Release. You review assets + add release notes + click **Publish release**. To skip the draft step (auto-publish), use Actions → Release → *Run workflow* with `publish=true`.

   **Local (fallback when CI is unavailable):**
   ```
   pwsh scripts/build-velopack-release.ps1 -Version 1.1.x.0
   ```
   Runs `dotnet publish` (self-contained `win-x64`) → generates a multi-size `AppIcon.ico` from Square44x44Logo PNGs → calls `vpk pack` with `--packId RORORO`. Artifacts land in `dist/release/`. Then drag every file from that dir into a GitHub Release manually — partial uploads break auto-update because `UpdateChecker` pings `releases.win.json` and pulls the matching `*-full.nupkg`.
8. `release.yml` signs and attaches `roblox-compat.json` + `roblox-compat.json.sig` and `docs/store/plugins-catalog.json` on every tag; if the known-good Roblox range or the mutex name moved, edit `roblox-compat.json` in the release commit (schema in [`docs/roblox-compat.example.json`](docs/roblox-compat.example.json)). The signing key exists only as the `ROBLOXCOMPAT_SIGNING_KEY` CI secret.
9. Submit the Store MSIX via Partner Center. Distribute the GitHub Release to clan via Discord with the SmartScreen-bypass video.

## Decision logging

Every architectural decision worth knowing in 3-6 months goes to the **626 Labs Dashboard** via `mcp__626Labs__manage_decisions log`. The bar and categories are in [CLAUDE.md](CLAUDE.md). When in doubt, log it — overshoot is cheaper than the gap.
