# ROROROblox — PRD (compressed)

Stories + acceptance criteria + prioritization. For full design rationale see [`spec.md`](spec.md) → canonical at `docs/superpowers/specs/2026-05-03-rororoblox-design.md`.

This is a Spec-first cycle (pattern mm) — the heavy thinking happened in the upstream design spec. This PRD compresses to stories that map cleanly to checklist items.

## Epic 1 — Core multi-instance + account management (v1.1 must-ship)

### Story 1.1 — Multi-instance toggle
**As a** Roblox player **I want** a tray toggle that flips multi-instance on/off **so that** I can run multiple clients side by side without DevTools or registry edits.
- AC: Tray icon visible after install. Right-click menu shows current state ("Multi-Instance ON / OFF") and a toggle item.
- AC: Toggle ON acquires `Local\ROBLOX_singletonEvent` within 200ms; toggle OFF releases it.
- AC: With toggle ON, launching Roblox a second time produces a second running client (verified in Task Manager).
- AC: With toggle OFF, the app does not interfere with Roblox's normal single-instance behavior.

### Story 1.2 — Add Account
**As a** user **I want** to add a saved Roblox account by logging in **so that** I can quick-launch it later.
- AC: "Add Account" opens a WebView2 modal pre-pointed at `roblox.com/login` on a fresh user-data folder (no leaked previous-account state).
- AC: After successful login, modal closes; account row appears in the list with display name + avatar.
- AC: Cookie is encrypted at rest (DPAPI), never written to logs, never serialized in plaintext.
- AC: Cancel returns to main window with no row added; login failure surfaces "Login was unsuccessful."

### Story 1.3 — Launch As
**As a** user **I want** to click "Launch as <account>" **so that** Roblox opens already signed in as that account.
- AC: One click on the account row spawns a Roblox client signed in as that account.
- AC: With Story 1.1's toggle ON, repeated Launch-As against different accounts spawns multiple clients side by side.
- AC: 401 from auth-ticket endpoint surfaces "Session expired" badge + Re-authenticate button (Story 2.3 flow).
- AC: `Win32Exception` on `Process.Start("roblox-player:...")` surfaces "Roblox doesn't appear to be installed" modal with Download Roblox + I have Bloxstrap buttons.
- AC: `lastLaunchedAt` updates on success.

### Story 1.4 — Remove / Re-authenticate account
**As a** user **I want** to remove a saved account or refresh its session **so that** I can manage my account list over time.
- AC: Remove asks for confirmation; on confirm, account row disappears and disk no longer contains its data after next save.
- AC: Re-authenticate opens the same WebView2 flow as Add but is contextually labeled; new cookie replaces the old one in-place; row turns green.

### Story 1.5 — First-run + tray UX
**As a** "common Windows user" **I want** the app to be obvious to use after installing **so that** I don't need a tutorial.
- AC: After install, tray icon appears within 5 seconds of first run.
- AC: Right-click tray → Open shows main window. Closing main window minimizes to tray (does not quit).
- AC: Quitting from tray menu fully exits and releases the mutex.
- AC: Run-on-login toggle in main window settings writes/clears `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Epic 2 — Resilience + distribution (v1.1 must-ship)

### Story 2.1 — Auto-update
- AC: Velopack checks for updates once per app launch (debounced 24h between checks).
- AC: User can decline an update; app continues working.
- AC: GitHub Releases is the update source.

### Story 2.2 — Roblox-update detection
- AC: On startup, app fetches a remote config file (GitHub-hosted alongside releases) with known-good Roblox version range and the current mutex name.
- AC: If the installed Roblox version is outside the known-good range, a yellow banner appears in main window with the documented copy + GitHub-issues link.
- AC: Mutex name is sourced from the remote config (NOT hardcoded) — when Roblox renames it, an updated config + Velopack release ships within hours, not a binary rebuild cycle.

### Story 2.3 — Cookie expired
- AC: 401 from auth-ticket endpoint marks the row yellow with a "Session expired" badge and a Re-authenticate button.
- AC: Re-authenticate opens the WebView2 flow; new cookie replaces the old via `IAccountStore.UpdateCookieAsync`; row turns green; no data loss.

### Story 2.4 — DPAPI failure recovery
- AC: On `CryptographicException` reading `accounts.dat`, modal: "Your saved accounts can't be unlocked on this PC..." with [Start Fresh] and [Quit] buttons.
- AC: Start Fresh renames the file to `accounts-corrupt-<date>.dat` and creates an empty store; the user's Roblox accounts themselves are unaffected, they just need to log in again.

### Story 2.5 — Missing-runtime modals
- AC: WebView2 runtime missing → modal with [Install Now] (downloads Evergreen Bootstrapper) and [Learn More].
- AC: Roblox not installed → modal with [Download Roblox] and [I have Bloxstrap].

### Story 2.6 — Distribution
- AC: MSIX builds successfully via Windows Application Packaging Project. Two flavors produced: Store-signed and self-signed sideload.
- AC: Self-signed sideload installs on a clean Win11 VM after the documented SmartScreen "More info → Run anyway" path; README + 30s video link cover the bypass.
- AC: Microsoft Store submission package validates locally with Microsoft's MSIX verification tooling (`MakeAppx.exe verify` + `signtool verify`).

## Prioritization

- **P0 (must ship in v1.1):** All Epic 1 stories + 2.3 + 2.4 + 2.5 + 2.6.
- **P0.5 (gate before broader distribution):** 2.1 + 2.2.
- **P1 (v1.2):** Per-cookie encryption, per-account WebView2 profiles, auto-tile, live "running" indicator, cross-machine cookie sync (only if requested by clan).

## Explicit cuts

- Macros / input automation (separate product **MaCro**)
- Setup-sharing / clan tooling (screenshots are the current pattern)
- Cross-machine cookie sync (DPAPI per-machine is intentional)
- E2E automation against real roblox.com (would require bot accounts + risks Roblox flagging)
- WPF visual snapshot tests (too brittle for v1's small UI)
