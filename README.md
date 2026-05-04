# ROROROblox

**Run multiple Roblox clients side by side. Quick-launch saved accounts. A 626 Labs product.**

[![License: MIT](https://img.shields.io/badge/license-MIT-cyan)](#license)
[![Platform: Windows 11](https://img.shields.io/badge/platform-Windows%2011-magenta)](https://www.microsoft.com/windows/windows-11)
[![Stack: .NET 10 LTS](https://img.shields.io/badge/.NET-10%20LTS-cyan)](https://dotnet.microsoft.com/)

> *Imagine Something Else.*

---

## What it does

- **One-click multi-instance.** Tray toggle defeats Roblox's single-instance check so multiple clients can run side by side. Same trick MultiBloxy and other tools use, just packaged for the rest of us.
- **Saved Roblox accounts.** Add your alts once via an embedded login window. Click "Launch As" to spawn each one.
- **No DevTools, no registry edits.** Common-Windows-user UX from install through launch.

## Install

### Microsoft Store *(primary path)*

Coming soon. The Store-signed install bypasses SmartScreen entirely.

### Sideload *(clan-direct path)*

Until the Store listing is live:

1. Download the latest `ROROROblox-Sideload.msix` and `dev-cert.cer` from [Releases](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases).
2. Right-click `dev-cert.cer` → **Install Certificate** → **Local Machine** → **Place all certificates in the following store** → **Trusted People**. (One-time per machine.)
3. Double-click `ROROROblox-Sideload.msix` to install. SmartScreen will warn — click **More info** → **Run anyway**. (One-time per release.)
4. ROROROblox shows up in your Start Menu.

A 30-second video walkthrough is linked from each Release page.

## How to use

1. Click ROROROblox in the system tray to open the main window.
2. Click **+ Add Account**, log in with the Roblox account you want to save. The login happens entirely inside Roblox's own page — your password never touches our process.
3. Repeat for each alt.
4. Right-click the tray icon → toggle **Multi-Instance: ON**.
5. Click **Launch As** next to any saved account. Repeat for any other account to spawn another client.

The first time you Launch As, you'll be prompted for a default Roblox game URL. Paste any Roblox game's `roblox.com/games/...` link — that's where Launch As will land your alts. Edit it later in Settings.

## What gets stored on your PC

| Where | What |
|---|---|
| `%LOCALAPPDATA%\ROROROblox\accounts.dat` | Your saved Roblox cookies. **DPAPI-encrypted** (Windows-issued; tied to your Windows user). Cannot be moved between PCs. |
| `%LOCALAPPDATA%\ROROROblox\settings.json` | Your default game URL + UI preferences. Plain text (no secrets). |
| `%LOCALAPPDATA%\ROROROblox\webview2-data\` | Embedded-browser cache. Wiped before every Add Account so the next login starts on a fresh page. |

## What about my Roblox password?

Short version: **ROROROblox never sees it.**

Long version: when you click *Add Account*, ROROROblox opens an embedded Microsoft Edge WebView2 control pointed at `https://www.roblox.com/login`. The login page is Roblox's own — same HTML, same form, same HTTPS connection your browser would make. Your keystrokes go from the embedded browser straight to Roblox's servers. ROROROblox is the window frame, not the form handler.

What we **do** capture, after Roblox confirms a successful login, is the `.ROBLOSECURITY` session cookie that Roblox sets in your browser. That cookie is what we hand back to Roblox during *Launch As* to start a session as you. Before we write it to disk, we run it through Windows' [Data Protection API](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection) — encryption tied to your specific Windows user account on your specific machine. The encrypted file (`accounts.dat`) is unreadable on any other PC, by any other Windows user, or even by you if Windows ever loses its DPAPI master key (e.g., after a from-scratch reinstall).

We never log the cookie value. We never send the cookie to anyone other than Roblox. It exists in plaintext only briefly in memory during a single *Launch As* operation, then goes back to disk in encrypted form.

**No cookies are ever written to disk in plaintext. No data leaves your PC except Roblox-side calls during launch — the same calls Roblox.com makes from your browser.**

## Tech stack

- **.NET 10 LTS** + **C# 14**
- **WPF** + **WPF-UI** (Fluent-style controls)
- **Hardcodet.NotifyIcon.Wpf** (system tray)
- **Microsoft.Web.WebView2** (login capture)
- **Microsoft.Windows.CsWin32** (typed P/Invokes for the singleton-mutex hold)
- **System.Security.Cryptography.ProtectedData** (DPAPI envelope on saved cookies)
- **Velopack** (auto-update via GitHub Releases)
- **xUnit** (unit + integration tests)

## Provenance

The named-mutex defeat technique originated with **MultiBloxy** by [Zgoly](https://github.com/Zgoly/MultiBloxy). ROROROblox is **not a fork** — it's a clean reimplementation in C# with substantially expanded scope (account management, structured launch flow, error handling, distribution). The reference binary `MultiBloxy.exe` is in this repo for verification; details + hash + caveats in [`PROVENANCE.txt`](PROVENANCE.txt).

## Roblox-side caveats

- Roblox / Hyperion has stated that multi-instancing "may be considered malicious behavior." Risk of a ban appears low because we don't inject into or modify the Roblox client — we only hold a Windows mutex name before launch. But it is non-zero. Don't run this on accounts you can't afford to lose.
- The auth-ticket endpoint contract is what we depend on. If Roblox changes it, multi-instance launches will start failing — see the [auth-ticket-flow-validator agent](.claude/agents/auth-ticket-flow-validator.md) and the version-drift banner in the main window.

## Environment variables

None required for v1.1. `ROROROBLOX_TEST_COOKIE` is read **only** by the throwaway spike at `spike/auth-ticket/` — it's a verification gate for development, not used by the shipped product.

## Building from source

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the dev setup, spike re-run, and MSIX packaging walkthroughs. Short version:

```powershell
# Day-to-day
dotnet build
dotnet test
dotnet run --project src/ROROROblox.App
```

## Documentation

- **Architecture & decisions:** [`docs/superpowers/specs/2026-05-03-rororoblox-design.md`](docs/superpowers/specs/2026-05-03-rororoblox-design.md)
- **Build plan:** [`docs/checklist.md`](docs/checklist.md)
- **Cycle process notes:** [`process-notes.md`](process-notes.md)
- **Security audit:** [`docs/security-audit-2026-05-04.md`](docs/security-audit-2026-05-04.md)
- **Repo conventions for AI agents:** [`CLAUDE.md`](CLAUDE.md)

## License

Source code is MIT-licensed. The reference binary (`MultiBloxy.exe`) is governed by Zgoly's original license — see [`PROVENANCE.txt`](PROVENANCE.txt).

---

*A 626 Labs product. Imagine Something Else.*
