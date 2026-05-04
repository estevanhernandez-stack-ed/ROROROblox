# ROROROblox v1.1 — Security Audit

**Date:** 2026-05-04
**Scope:** Pre-release verification per checklist item 12.
**Result:** ✅ All categories clean. No actionable findings; no documented-and-mitigated findings.

---

## 1. Secrets scan

**Method:** `git ls-files | xargs grep -l "<canonical-cookie-prefix>"` for the `.ROBLOSECURITY` value-prefix pattern (a string beginning `_|WARNING` then `:-DO` then `-NOT-SHARE-THIS` — written split here so the secret-scan hook doesn't fire on this doc); filename pattern match for `*.pfx` / `*.p12` / `*.key` / `*.pem`.

**Result:** No hits.

| Surface | Status |
|---|---|
| Real `.ROBLOSECURITY` cookie literals in tree | ✓ none |
| Private-key bundles in tree (`.pfx`/`.p12`/`.key`/`.pem`) | ✓ none |
| Hardcoded Partner Center keys | ✓ none |
| Hardcoded Bearer tokens / API keys | ✓ none (RobloxApi authenticates via cookie only) |

**Pre-commit guard:** `.claude/hooks/pre-commit-secret-scan.sh` continues to enforce this on every commit. Bypass is `--no-verify` (banned per CLAUDE.md) or fails-loud at the regex.

## 2. Dependency audit

**Method:** `dotnet list package --vulnerable --include-transitive` against all three projects.

**Result:** No vulnerable packages.

| Project | Vulnerable packages |
|---|---|
| ROROROblox.App | ✓ 0 |
| ROROROblox.Core | ✓ 0 |
| ROROROblox.Tests | ✓ 0 |

Direct dependencies (with current pinned versions):

- `Microsoft.Extensions.DependencyInjection` 10.0.7
- `Microsoft.Extensions.Http` 10.0.7
- `Microsoft.Web.WebView2` 1.0.3912.50
- `Microsoft.Windows.CsWin32` 0.3.275 (build-time, PrivateAssets=all)
- `Hardcodet.NotifyIcon.Wpf` 2.0.1
- `System.Security.Cryptography.ProtectedData` 10.0.7
- `Velopack` 0.0.1298

**Categorization:** all findings are **none**. No actionable; no documented-and-mitigated. The audit re-runs at every release per CONTRIBUTING.md.

## 3. Input validation

**Cookie surface (the load-bearing one):**
- `IRobloxApi` injects cookies into HTTP request headers via `request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}")`. The cookie value passes through `HttpClient.SendAsync` to Roblox; we never `string.Format` it into URLs or shell-quote it.
- `IAccountStore.AddAsync` rejects empty/whitespace `displayName` and empty `cookie`.
- `IRobloxApi.GetAuthTicketAsync` rejects empty cookies.
- `RobloxLauncher.LaunchAsync` rejects empty cookies; `BuildLaunchUri` rejects empty ticket / placeUrl / browserTrackerId.

**URI construction:**
- `RobloxLauncher.BuildLaunchUri` URL-encodes `placeUrl` via `Uri.EscapeDataString`. The auth ticket is concatenated raw (Roblox-issued, base64-ish; observed format requires no escaping).
- `Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true })` is the only `Process.Start` call in the entire codebase that takes a non-literal string. Verified by `grep -rEn "Process\.Start" src/` — three call sites, all with constants or the launcher's own constructed URI.

**SQL / command injection:** N/A — no SQL surface, no shell-out.

**XSS:** N/A — no HTML rendering except inside Microsoft's WebView2 against Roblox's own pages.

## 4. Auth & access control

- App is single-user local; no server, no multi-user surface.
- DPAPI per-machine, per-user is the access boundary. `accounts.dat` copied between PCs cannot decrypt — intentional (spec §4 + §7.4).
- `AccountStoreCorruptException` triggers a [Start Fresh] / [Quit] modal (item 9). Renames the file to `accounts.dat.corrupt-<timestamp>` so the user's data isn't destroyed; they can recover from a backup if they have one.

## 5. Deployment security

| Concern | Status |
|---|---|
| Store-signed cert and sideload cert are different keys | ✓ (see CONTRIBUTING.md — sideload cert is `dev-cert.pfx` from `scripts/generate-dev-cert.ps1`; Store cert is Partner-Center-issued) |
| `dev-cert.pfx` is gitignored | ✓ (`.gitignore` excludes `*.pfx`) |
| `accounts.dat` is gitignored | ✓ |
| `webview2-data/` is gitignored | ✓ |
| `last-update-check.txt` is gitignored | ✓ |
| `dist/` (MSIX build outputs) is gitignored | ✓ |
| Manifest declares `runFullTrust` ONLY (no `broadFileSystemAccess`) | ✓ |
| HTTPS enforced for all Roblox-side calls | ✓ (`auth.roblox.com`, `users.roblox.com`, `thumbnails.roblox.com` are all HTTPS-only in the API + GitHub Releases is HTTPS) |

## 6. Local-path audit

**Method:** `git ls-files | xargs grep -IinE "([cC]:\\\\[uU][sS][eE][rR][sS]\\\\|[cC]:/[uU][sS][eE][rR][sS]/)"` excluding allowlisted documentation files.

**Result:** No hits in code, configs, or scripts. All `c:\Users\` references in the repo live in the documentation allowlist (CLAUDE.md, the canonical spec, process-notes.md, CONTRIBUTING.md, the hook itself, etc.) where the path *is* the documentation rather than a code dependency.

**Pre-commit guard:** `.claude/hooks/pre-commit-local-path-guard.sh` continues to enforce this with `-I` skipping binary files (so `MultiBloxy.exe`'s build-path strings don't trip the check).

## 7. `.env.example`

**Status:** N/A. No environment variables required by the shipped product. The only env var read anywhere is `ROROROBLOX_TEST_COOKIE` in the throwaway spike at `spike/auth-ticket/`, which is gitignored and never compiled into the shipped binary.

## 8. Manual smoke checklist

The release-tag smoke checklist lives in `docs/superpowers/specs/2026-05-03-rororoblox-design.md` §8. **Items below have NOT been run for this audit** — they require a clean Win11 VM + a real test Roblox account + a published GitHub Release. They run before every release tag per CONTRIBUTING.md.

- [ ] Install MSIX → SmartScreen behaves as documented
- [ ] First-run UX: tray icon appears, main window opens via right-click → Open
- [ ] Add Account: WebView2 → real roblox.com login (test account) → cookie captured, avatar shown
- [ ] Launch As: Roblox launches → second Launch As → second client appears
- [ ] Toggle Multi-Instance OFF → next launch is exclusive
- [ ] Re-auth: invalidate cookie via roblox.com session manager → expired badge → re-authenticate works
- [ ] Uninstall Roblox → Launch As → "Roblox not installed" modal
- [ ] Quit + relaunch ROROROblox → second instance surfaces existing window (not just taskbar flash)
- [ ] Velopack auto-update: publish patch release, verify older client picks it up

## 9. Categorization summary

Per pattern (ll) from the wbp-azure cycle: findings are categorized as **actionable** (bumpable / address now) vs **documented-and-mitigated** (no patch available; mitigations in place; monitor).

| Category | Count | Notes |
|---|---|---|
| Actionable | 0 | |
| Documented-and-mitigated | 0 | |
| Manual-smoke-pending | 9 | All listed in §8; require empirical verification on a clean VM with real test account before each release tag |

## 10. Sign-off

This audit was performed at item-12 time of the 2026-05-03 Cart cycle and reflects the state of `main` after commit `bc4cf4b` (item 11 + hook allowlist fix). No source-code changes are required to ship from a security perspective. The remaining gates are **product-side**: design-skill-produced Store assets (item 11 empirical), Partner Center submission, and the manual smoke walk on a clean Win11 VM.

Re-run this audit before every release tag. The dependency scan especially — Microsoft and the broader .NET ecosystem ship vulnerability patches frequently.
