# Contributing to RORORO

This is a small, single-developer product for now. The notes below are the muscle memory you'll want re-reading before each release tag — most of it isn't checked by the build pipeline.

## Tech baseline

- **OS:** Windows 11 (or Windows 10 22000+).
- **.NET:** SDK 10.0+ (`winget install Microsoft.DotNet.SDK.10` or run `scripts/install-dotnet.ps1`). User-scope install at `%USERPROFILE%\.dotnet\` is fine and avoids UAC.
- **Windows SDK:** 10.0.22621+ for `makeappx.exe` + `signtool.exe`. Install via `winget install Microsoft.WindowsSDK.10.0.22621` or grab the latest from windows.com.
- **Roblox:** installed locally so the `roblox-player:` protocol handler is registered. Required for the auth-ticket spike (item 1) and end-to-end Launch As tests.

## Day-to-day

```powershell
# Build
dotnet build

# Run (from src/)
dotnet run --project src/ROROROblox.App

# Test
dotnet test
```

The pre-commit hooks are git-installed via `.claude/hooks/install.ps1`. They block commits containing the real `.ROBLOSECURITY` cookie prefix or `c:\Users\<name>\` paths in code. False positives are rare — see [.claude/hooks/README.md](.claude/hooks/README.md) before bypassing.

## Auth-ticket spike (item 1's verification gate)

The contract between us and `auth.roblox.com/v1/authentication-ticket` can shift. Re-run the spike whenever:
- A user reports "Launch As stops working" with no obvious cause.
- Before a release tag, after long quiet periods on the Roblox-API side.
- Any time `dpapi-cookie-blast-radius` or `auth-ticket-flow-validator` agents flag something.

```powershell
$env:RORORO_TEST_COOKIE = '<paste .ROBLOSECURITY value from a TEST account>'
dotnet run --project spike/auth-ticket -- --validate-only
```

Document any contract shift in [process-notes.md](process-notes.md), update [the canonical spec](docs/superpowers/specs/2026-05-03-RORORO-design.md) §5.7 / §6.2, log a decision via the dashboard MCP. Don't proceed to a release until the spike is green.

## Building MSIX

Two flavors. Both fail fast if the Store-bound logos under `src/ROROROblox.App/Package/Logos/` are missing or look like programmatic placeholders.

### Sideload (clan distribution)

```powershell
# One-time per dev machine — generate the self-signed cert.
powershell -ExecutionPolicy Bypass -File scripts/generate-dev-cert.ps1 -Password 'pick-a-password'

# Build the sideload MSIX.
powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Sideload -CertPath dev-cert.pfx -CertPassword 'pick-a-password'
```

Output: `dist/ROROROblox-Sideload.msix` (signed) + `dev-cert.cer` (the public cert your testers import into **Local Machine → Trusted People** before installing).

The first-install flow on a fresh Win11 box:
1. Tester downloads `dev-cert.cer` and `ROROROblox-Sideload.msix`.
2. Right-click `dev-cert.cer` → Install → Local Machine → Trusted People.
3. Double-click the `.msix` to install.
4. SmartScreen will prompt: "More info → Run anyway." Document this with a 30-second video on the README.
5. RORORO shows up in Start Menu.

### Store

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-msix.ps1 -Store
```

Output: `dist/ROROROblox-Store.msix` (unsigned — Partner Center signs on submission). Validate locally before uploading:

```powershell
& "$env:ProgramFiles(x86)\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe" verify /p dist/ROROROblox-Store.msix
```

Then upload via Partner Center → Apps & games → RORORO → Packages.

## Asset production (logos, splash, tray icons)

**Real assets must come from the `626labs-design` skill** — never ship programmatic placeholders. See [`src/ROROROblox.App/Package/Logos/README.md`](src/ROROROblox.App/Package/Logos/README.md) for the canonical sizes and the skill prompt.

The build script's logo-presence check is the gate. If you bypass it via `-AllowPlaceholders`, you accept a Store rejection or a bad clan-distribution moment.

The tray icons under `src/ROROROblox.App/Tray/Resources/*.placeholder.ico` are also placeholder-tier and must be replaced before ship; they're separate from the Store-bound assets.

## Releasing

The full pre-release checklist lives in [docs/checklist.md](docs/checklist.md) item 12. The short form:

1. Re-run the auth-ticket spike. PASS.
2. Walk every spec §8 manual smoke scenario on a clean Win11 VM.
3. Replace placeholder tray + Store icons via the design skill.
4. Bump version in `Package.appxmanifest` + the assembly version.
5. Build sideload + Store MSIX flavors. Validate both.
6. Tag the release: `git tag v1.1.x && git push origin v1.1.x`.
7. Cut a Velopack release: `vpk pack -u <updateUrl> -p dist/publish -e ROROROblox.App.exe -v 1.1.x`.
8. Publish `roblox-compat.json` as a release asset (schema in [`docs/roblox-compat.example.json`](docs/roblox-compat.example.json)).
9. Submit the Store MSIX via Partner Center. Distribute sideload to clan via Discord with the SmartScreen-bypass video.

## Decision logging

Every architectural decision worth knowing in 3-6 months goes to the **626 Labs Dashboard** via `mcp__626Labs__manage_decisions log`. The bar and categories are in [CLAUDE.md](CLAUDE.md). When in doubt, log it — overshoot is cheaper than the gap.
