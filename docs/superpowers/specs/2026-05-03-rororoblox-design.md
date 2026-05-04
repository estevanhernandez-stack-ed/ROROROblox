# ROROROblox — Design Spec

**Version:** v1.1 (initial release)
**Date:** 2026-05-03
**Status:** Approved for implementation planning
**Repo:** https://github.com/estevanhernandez-stack-ed/ROROROblox

## 1. Overview

ROROROblox is a Windows desktop app that lets a Roblox player run multiple Roblox clients side by side and quickly launch them as different saved accounts. It is distributed first to a Pet Sim 99 clan and second through the Microsoft Store.

The technical core is two small ideas:

1. **Hold the named mutex `ROBLOX_singletonEvent`** so subsequent Roblox launches don't see themselves as the singleton — the same trick MultiBloxy and several predecessors use.
2. **Use Roblox's documented authentication-ticket flow** (cookie → ticket → `roblox-player:` URI) to launch a specific saved account, the same path Bloxstrap and earlier account managers use.

Everything else is the wrapper that makes those two operations safe, repeatable, and approachable for a non-technical Windows user.

## 2. Goals and non-goals

**Goals (v1.1):**
- One-click multi-instance Roblox via tray toggle.
- Saved Roblox accounts with per-account quick-launch.
- "Common Windows user" install + first-run UX (no DevTools, no registry edits).
- Microsoft Store distribution as primary path; self-signed MSIX sideload as fallback.

**Non-goals (v1.1):**
- Macros / input automation (lives in MaCro, the separate macOS product).
- Setup sharing or clan tooling (clan members share screenshots today, not files).
- Cross-machine cookie sync (DPAPI is per-machine by design).
- Auto-tile windows (deferred to v1.2).
- Live "Account is running" status (`ProcessTracker`) — deferred to v1.2.

## 3. Stack

| Layer | Pick | Rationale |
|---|---|---|
| Runtime | .NET 10 LTS (Nov 2025) | Current LTS, supported through Nov 2028 |
| Language | C# 14 | Ships with .NET 10 |
| UI framework | WPF | First-class on .NET 10, deepest tray + Win32 ecosystem |
| Visual styling | WPF-UI (lepoco) | Fluent-style controls — modern look without leaving WPF |
| Tray | Hardcodet.NotifyIcon.Wpf | De facto WPF tray library |
| Win32 calls | Microsoft.Windows.CsWin32 | Source generator, type-safe replacement for `DllImport` |
| Cookie storage | `System.Security.Cryptography.ProtectedData` (DPAPI) | Built-in, no key management |
| Embedded browser | Microsoft.Web.WebView2 | Edge engine; preinstalled on Win11; standard for cookie-capture UX |
| Packaging | Windows Application Packaging Project → MSIX | Store-eligible, current Microsoft path |
| Auto-update | Velopack | Modern Squirrel successor, GitHub-Releases-friendly |
| DI | Microsoft.Extensions.DependencyInjection | Standard |
| Tests | xUnit + (no UI automation in v1) | xUnit is the .NET default |

WPF was chosen over WinUI 3 for v1 because tray support and Win32 interop are battle-tested. Migration to WinUI 3 in a later major version remains an open path.

## 4. Architecture

A single WPF process. Tray-resident, opens its main window on demand. A small set of focused components plus a composition root — listed in §5.

```
ROROROblox.exe (single .NET 10 WPF process)
├── App / AppLifecycle           — composition root, single-instance check, DI
├── MutexHolder                  — owns the OS handle for ROBLOX_singletonEvent
├── TrayService                  — Hardcodet NotifyIcon, right-click menu
├── MainWindow + MainViewModel   — WPF + WPF-UI, hidden by default
├── AccountStore                 — DPAPI-encrypted JSON at %LOCALAPPDATA%
├── CookieCapture                — modal WebView2, captures .ROBLOSECURITY
├── RobloxLauncher               — cookie → auth ticket → roblox-player: URI
└── IRobloxApi                   — thin HttpClient wrapper for Roblox endpoints
```

**External boundaries:**
- Windows OS: named mutex, DPAPI, WebView2 runtime
- Roblox: `auth.roblox.com` (login + ticket exchange), `users.roblox.com` (avatar fetch), `RobloxPlayerLauncher.exe` (via `roblox-player:` protocol handler)
- Filesystem: `%LOCALAPPDATA%\ROROROblox\` containing `accounts.dat`, `logs/`, `webview2-data/`

**Why this shape:**
- Each component has one responsibility and a clear interface — independently testable.
- No background services, no admin rights, no kernel drivers — runs as plain user.
- Roblox-side calls go through documented public endpoints used by Bloxstrap and prior account managers — no novel attack surface.
- DPAPI per-user means an account on machine A can't be opened on machine B (intentional v1 limit).

## 5. Components & interfaces

### 5.1 `IMutexHolder`
```csharp
interface IMutexHolder {
    bool IsHeld { get; }
    bool Acquire();          // returns true if we now own it
    void Release();
    event EventHandler MutexLost;
}
```
Owns the OS handle for `Local\ROBLOX_singletonEvent`. Lifetime equals app lifetime when the toggle is on. ~30 lines.

### 5.2 `ITrayService`
```csharp
interface ITrayService {
    void Show();
    void UpdateStatus(MultiInstanceState state);   // ON / OFF / Error
    event EventHandler RequestOpenMainWindow;
    event EventHandler RequestToggleMutex;
    event EventHandler RequestQuit;
}
```
Owns the Hardcodet NotifyIcon and context menu. Doesn't own the mutex itself — it requests toggle. The composition root wires events to `IMutexHolder.Acquire/Release`.

### 5.3 `MainWindow` + `MainViewModel`
Standard MVVM. Hidden by default; shown via tray. Closing the X minimizes to tray (does not quit). The view model exposes:
```csharp
ObservableCollection<AccountSummary> Accounts;
ICommand AddAccountCommand;
ICommand LaunchAccountCommand;        // takes AccountSummary
ICommand RemoveAccountCommand;
ICommand ReauthenticateAccountCommand;
```

### 5.4 `IAccountStore`
```csharp
interface IAccountStore {
    Task<IReadOnlyList<Account>> ListAsync();
    Task<Account> AddAsync(string displayName, string avatarUrl, string cookie);
    Task RemoveAsync(Guid id);
    Task<string> RetrieveCookieAsync(Guid id);
    Task UpdateCookieAsync(Guid id, string newCookie);
    Task TouchLastLaunchedAsync(Guid id);
}

record Account(
    Guid Id,
    string DisplayName,
    string AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLaunchedAt);
// Cookie is NEVER on this record. Always retrieved on-demand via RetrieveCookieAsync.
```

**Storage:** `%LOCALAPPDATA%\ROROROblox\accounts.dat` — DPAPI-encrypted JSON blob.

**v1.1 chooses whole-blob encryption** (entire file as one encrypted unit). Simpler, fine for v1. v1.2 will move to per-cookie encryption alongside per-account WebView2 profiles.

### 5.5 `ICookieCapture`
```csharp
interface ICookieCapture {
    Task<CookieCaptureResult> CaptureAsync();
}

abstract record CookieCaptureResult {
    record Success(string Cookie, long UserId, string Username) : CookieCaptureResult;
    record Cancelled : CookieCaptureResult;
    record Failed(string Message) : CookieCaptureResult;
}
```
Owns the WebView2 modal lifetime.

**v1.1 wipes the WebView2 user-data folder per capture** — every Add Account shows a clean login page. This avoids the "still logged in as the previous account" trap when adding multiple accounts.

**v1.2 will switch to per-account WebView2 profiles** (folder keyed by Roblox `userId`) — first add for any account is fresh, but re-auth for an existing account reuses the profile so Roblox's "remember this device for 2FA" persists.

### 5.6 `IRobloxLauncher`
```csharp
interface IRobloxLauncher {
    Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null);
}

abstract record LaunchResult {
    record Started(int Pid) : LaunchResult;
    record CookieExpired : LaunchResult;
    record Failed(string Message) : LaunchResult;
}
```

**Internal flow:**

1. POST `auth.roblox.com/v1/authentication-ticket` with cookie + CSRF token + `Content-Type: application/json` (empty body). First call (without `X-CSRF-TOKEN`) returns 403 with `X-CSRF-TOKEN` response header; replay with that token to receive `RBX-Authentication-Ticket` response header. Both POSTs require `Content-Type: application/json`; Roblox returns 415 without it.
2. Construct URI: `roblox-player:1+launchmode:play+gameinfo:<ticket>+launchtime:<now-ms>+placelauncherurl:<encoded>+browsertrackerid:<rand>+robloxLocale:en_us+gameLocale:en_us`.
3. `Process.Start(uri)` with `UseShellExecute = true` — Windows hands off to the registered protocol handler.

The protocol-URI path (rather than calling `RobloxPlayerLauncher.exe` directly) is chosen because the URI is the public, stable contract Roblox web clients use. It survives Roblox client updates better than poking at the launcher binary's argv. Bloxstrap's protocol-handler interception remains compatible.

**`placelauncherurl` is required for the auth handshake to complete.** A `roblox-player:` URI without it opens the launcher but can't establish a session — the user sees their cached account context (avatar, username) but the new auth ticket never gets exchanged for a game-server connection (visible as "not logged in"). Caught at spike-time 2026-05-03. Implication: when `LaunchAsync(cookie, placeUrl: null)` is called, the implementation MUST resolve `placeUrl` to a non-empty default before URI construction. The default-place UX is decided at item 6 (likely an app-level "default game URL" setting; per-account default place is a v1.2 candidate).

### 5.7 `IRobloxApi`
```csharp
interface IRobloxApi {
    Task<AuthTicket> GetAuthTicketAsync(string cookie);
    Task<UserProfile> GetUserProfileAsync(string cookie);
    Task<string> GetAvatarHeadshotUrlAsync(long userId);
}
```
Thin HttpClient wrapper. Handles the X-CSRF-TOKEN dance in `GetAuthTicketAsync`. The auth-ticket POSTs require `Content-Type: application/json` even on empty body — Roblox returns 415 Unsupported Media Type without it (caught at spike-time 2026-05-03; not in v1.0 of this spec). UA string is `ROROROblox/<version>` — no browser spoofing.

### 5.8 `App` / `AppLifecycle`
- Single-instance check for ROROROblox itself via a separate mutex `Local\ROROROblox-app-singleton`. A second launch surfaces the existing main window instead of starting another process.
- DI wiring (`Microsoft.Extensions.DependencyInjection`).
- Optional "Run on login" via `HKCU\...\Run` registry key, toggleable in main window settings.

## 6. Data flows

### 6.1 Add Account

1. User clicks **Add Account**.
2. `MainWindow` calls `ICookieCapture.CaptureAsync()` (fresh WebView2 user-data folder for v1.1).
3. WebView2 navigates to `https://www.roblox.com/login`.
4. User logs in normally inside the embedded browser. Password never touches our process.
5. Roblox sets the `.ROBLOSECURITY` cookie on success.
6. CookieCapture polls `WebView2.CookieManager.GetCookiesAsync` for `.ROBLOSECURITY`; when present and the page has redirected to `/home`, closes the modal.
   - User closing the modal returns `Cancelled`.
   - Login failure returns `Failed("Login was unsuccessful")`.
7. CookieCapture calls `IRobloxApi.GetUserProfileAsync(cookie)` for `userId`, `username`, `displayName`.
8. CookieCapture returns `Success(cookie, userId, username)`.
9. `MainWindow` calls `IAccountStore.AddAsync(displayName, cookie)`. Store also fetches the avatar URL.
10. AccountStore serializes accounts to JSON, encrypts the entire blob with `ProtectedData.Protect()` (CurrentUser scope), and writes atomically (temp file + rename) to `%LOCALAPPDATA%\ROROROblox\accounts.dat`.
11. New account row appears in the list.

### 6.2 Launch As

1. User clicks **Launch as &lt;account&gt;**.
2. `MainWindow` calls `IAccountStore.RetrieveCookieAsync(accountId)`. DPAPI decrypts; plaintext cookie held briefly in memory.
   - Decrypt failure → "Can't read saved accounts. Re-add them." (see §7).
3. `MainWindow` calls `IRobloxLauncher.LaunchAsync(cookie)`.
4. RobloxLauncher exchanges cookie for auth ticket via `auth.roblox.com/v1/authentication-ticket` (CSRF dance).
   - 401 → returns `CookieExpired` → MainWindow surfaces re-auth flow via WebView2.
5. RobloxLauncher constructs the `roblox-player:` URI with the ticket.
6. `Process.Start(uri)` hands off to the OS.
   - Protocol handler missing → `Win32Exception` → returns `Failed("Roblox does not appear to be installed.")` with link to roblox.com/download.
7. The `RobloxPlayerLauncher.exe` reads the ticket and spawns `RobloxPlayerBeta.exe`. Because ROROROblox already holds `ROBLOX_singletonEvent`, the new client's singleton check is non-blocking — multi-instance works.
8. RobloxLauncher returns `Started(pid)`. The MainViewModel orchestrator (item 9) calls `IAccountStore.TouchLastLaunchedAsync(accountId)` on a `Started` result — `LaunchAsync(cookie, ...)` has no accountId in its signature, so the touch lives at the caller, not inside the launcher (pre-build drift caught at item 6 design).
9. UI shows a "Launched ✓" toast for 2s.

**Invariants:**
- Plaintext cookies live in memory only during a single launch operation. Never written to disk unencrypted, never logged.
- Password never touches our process — login happens inside Roblox's own page served into WebView2.
- No HTTP request from us masquerades as a browser. UA is `ROROROblox/<version>`.
- Only documented public Roblox endpoints are called.

## 7. Error handling

Six buckets, ordered by likelihood.

### 7.1 Roblox shipped a breaking update
**Trigger:** Roblox renames the singleton mutex or Hyperion adds a non-mutex check.

**Detection:** On startup, ROROROblox reads the locally-installed Roblox version. If it's outside our known-compatible range (fetched from a remote config file on our GitHub release), surface a yellow banner: *"Roblox updated to version X. We've tested up to Y. Multi-instance might not work — let us know at <github issues link>."*

**Recovery:** The known-compatible-range and current mutex name live in a remote config file fetched at app startup, not baked into the binary. When Roblox renames the mutex, we publish an updated config + Velopack release within hours instead of users waiting on a binary rebuild.

### 7.2 Cookie expired or invalidated
**Trigger:** `auth.roblox.com/v1/authentication-ticket` returns 401, or login response is a redirect to `/login`.

**UX:** Account row turns yellow with "Session expired" badge + Re-authenticate button. Click opens the WebView2 login modal pre-pointed at `/login`. New cookie captured via the existing flow → `IAccountStore.UpdateCookieAsync()` → row turns green. No data loss.

### 7.3 WebView2 runtime missing or broken
**Trigger:** `CoreWebView2Environment.CreateAsync()` throws.

**UX:** Modal: *"ROROROblox needs the Microsoft WebView2 runtime to manage Roblox logins. It's free and made by Microsoft. [Install Now] [Learn More]"*. Install Now downloads the Evergreen Bootstrapper.

**Mitigation:** Bundle WebView2 runtime in MSIX so the Store install includes it.

### 7.4 DPAPI decrypt fails
**Trigger:** User restored Windows from a backup that didn't preserve the DPAPI master key, SID change, profile corruption, or `accounts.dat` was copied from another machine.

**UX:** Modal on detection: *"Your saved accounts can't be unlocked on this PC. This usually means Windows was restored from a backup, or the file was copied from another machine. We need to start your account list fresh — your Roblox accounts themselves are fine, you'll just need to log in again."* Buttons: [Start Fresh] (rename to `accounts-corrupt-<date>.dat`, create empty store), [Quit] (so user can restore from a backup if they have one).

### 7.5 Roblox not installed / multiple Roblox installs
**Trigger:** `Process.Start("roblox-player:...")` throws `Win32Exception` or starts and exits immediately.

**UX:** Modal: *"Roblox doesn't appear to be installed. [Download Roblox] [I have Bloxstrap]"*. Bloxstrap path: *"Bloxstrap should also handle our launches — make sure Bloxstrap is set as the default Roblox launcher in its settings."*

### 7.6 Distribution friction
- **SmartScreen wall on first run.** Self-signed MSIX (clan-direct path) triggers SmartScreen's blue wall. v1 strategy: ship a one-line README + 30-second video showing the "More info → Run anyway" flow. Microsoft Store path bypasses this entirely (Store-signed binaries are SmartScreen-trusted).
- **MSIX sideload prompts cert trust.** For clan-direct distribution, our self-signed certificate is bundled in the MSIX and users are prompted to trust it on first install (one-time). On systems where sideload of unsigned-by-trusted-CA packages is restricted, users may need Developer Mode enabled in Settings. We document the exact path in the README based on the current Windows 11 install experience.
- **AV heuristic flags.** The mutex-holding pattern triggers some AVs. We mitigate by shipping a signed, identifiable, transparent app.

**Distribution decision:** Microsoft Store as primary submission path. If rejected, fall back to self-signed MSIX + clan-direct + documented SmartScreen-bypass instructions.

## 8. Testing

### Unit tests (xUnit, runs in CI)
- `IMutexHolder`: acquire/release/lost transitions (real Win32 mutex)
- `AccountStore`: add/list/remove/update-cookie roundtrip (real DPAPI on temp folder)
- `IRobloxApi`: CSRF dance, auth ticket extraction, error parsing (HttpMessageHandler stubs)
- `RobloxLauncher` URI construction: snapshot test against known-good URI
- URI escaping/encoding edges: special chars, Unicode (property test)

### Integration tests (xUnit, runs in CI)
- `AccountStore` full roundtrip on real disk (write → kill → read)
- `CookieCapture` flow against a local HTML stub mimicking roblox.com/login (no live calls)
- DPAPI tampered file → expect `CryptographicException` → verify "Start Fresh" surface

### Manual smoke checklist (before each release tag, on a clean Win11 VM)
- [ ] Install MSIX → SmartScreen behaves as documented
- [ ] First-run UX: tray icon appears, main window opens via right-click → Open
- [ ] Add Account: WebView2 → real roblox.com login (test account) → cookie captured, avatar shown
- [ ] Launch As: Roblox launches → second Launch As → second client appears
- [ ] Toggle Multi-Instance OFF → next launch is exclusive
- [ ] Re-auth: invalidate cookie via roblox.com session manager → expired badge → re-authenticate works
- [ ] Uninstall Roblox → Launch As → "Roblox not installed" modal
- [ ] Quit + relaunch ROROROblox → second instance surfaces existing window
- [ ] Velopack auto-update: publish patch release, verify older client picks it up

### Explicitly NOT automated
- End-to-end against real roblox.com (requires bot accounts, risks Roblox flagging, flaky CI signal).
- MSIX install on real Windows (manual VM step).
- WPF visual snapshot tests (too brittle for v1's small UI).

### CI/CD
- `pull_request` → `dotnet test` (unit + integration), `dotnet build`.
- Tag `v*.*.*` → build MSIX, sign, push to GitHub Releases via Velopack, update remote config file.
- Weekly cron → run integration tests against current dependencies (catches WebView2 SDK drift early).

## 9. Distribution

### Channels
1. **Microsoft Store (primary submission target).** Store-signed, SmartScreen-trusted, auto-update built in. Risk: certification might reject multi-instancing tools.
2. **Self-signed MSIX, clan-direct (fallback).** Distributed via GitHub Releases. SmartScreen will warn on first install — README + short video walks users through the bypass.

### Auto-update
Velopack reads from GitHub Releases. Update check on app startup (debounced to once per 24h). User can decline an update; we won't force-update.

### Versioning
Semantic versioning. v1.1.x for the initial release line. v1.2 will introduce per-cookie encryption + per-account WebView2 profiles + (likely) auto-tile.

## 10. Open items

### Mandatory before implementation
- **30-min spike:** verify `auth.roblox.com/v1/authentication-ticket` (CSRF dance + ticket exchange) still works as documented. Throwaway console app: log in to a test Roblox account, exchange cookie for ticket, build `roblox-player:` URI, spawn it, confirm Roblox launches as that account. If endpoint shifted, adapt design before building.

### Deferred / tracked
- Per-cookie encryption (v1.2)
- Per-account WebView2 profiles (v1.2)
- Auto-tile windows (v1.2)
- Live "Account is running" indicator (v1.2)
- Cross-machine cookie sync (out of scope; revisit only if requested by clan)
- Per-account default place URL (v1.2 candidate; v1.1 uses an app-level default — see §5.6)
- Runtime mutex-name swap from remote config (v1.2 candidate; v1.1 hardcodes `Local\ROBLOX_singletonEvent` and ships a Velopack release on Roblox rename — see §11)

## 11. Decisions log

| Decision | Rationale |
|---|---|
| WPF over WinUI 3 | Tray + Win32 ecosystem is more mature in WPF; WinUI 3 packaging quirks are real |
| Whole-blob encryption (v1.1) | Simpler than per-cookie; v1.2 upgrades alongside per-account WebView2 profiles |
| Wipe WebView2 per capture (v1.1) | Avoids "still logged in as previous account" trap during multi-add; v1.2 upgrades to per-account profiles |
| `roblox-player:` URI over direct launcher exec | Public, stable contract; survives Roblox client updates |
| Remote config file for known-good Roblox versions | Lets us respond to Roblox updates within hours, not binary release cycles |
| Microsoft Store primary, sideload fallback | Store bypasses SmartScreen; sideload is the safety net if Store rejects |
| Manual smoke gate over E2E automation | Real roblox.com automation risks flagging and produces flaky CI; manual is correct trade for a 1-person project |
| Auth-ticket POST requires `Content-Type: application/json` | Caught at spike-time 2026-05-03 — Roblox returns 415 Unsupported Media Type on empty-body POSTs without it. Spec v1.0 didn't capture this; updated §5.7 + §6.2 inline (pre-build drift, not post-build divergence — banner-correct convention applies after items have been built, not before). The spike is doing exactly the gating job §10 was designed for. |
| Auth handshake requires non-empty `placelauncherurl` | Also caught at spike-time 2026-05-03. Without `placelauncherurl`, Roblox opens the launcher with cached account context but can't establish a session — the user sees the right avatar/username but is "not logged in." `LaunchAsync(cookie, placeUrl: null)` must resolve `placeUrl` to a default destination before URI construction. v1.1 uses an app-level default (set at first run or in settings); v1.2 candidate: per-account default place. Spec §5.6 + §10 updated inline. |
| `IAccountStore.AddAsync` takes `avatarUrl` as a parameter | Spec v1.0's `AddAsync(displayName, cookie)` expected the store to fetch the avatar via `IRobloxApi` internally — but Core's clean layering wants the caller (MainViewModel, item 9) to coordinate the `IRobloxApi.GetAvatarHeadshotUrl` call and pass the result through. Adding `avatarUrl` as the second parameter avoids a back-pointer from `AccountStore` into `IRobloxApi` and keeps Core dependency-free at item 4 time. Pre-build drift, surgical inline edit. Spec §5.4 updated; checklist item 4 mirrors the change. |
| `TouchLastLaunchedAsync` is the caller's responsibility, not the launcher's | Spec v1.0 §6.2 step 8 had RobloxLauncher call `IAccountStore.TouchLastLaunchedAsync(accountId)` — but `LaunchAsync(cookie, placeUrl)` has no accountId in its signature, so the launcher would need IAccountStore as a dependency just to look up which account a cookie belongs to (which it can't do anyway, since cookies aren't indexed). Cleaner: MainViewModel orchestrates `RetrieveCookieAsync(id) → LaunchAsync(cookie) → on Started, TouchLastLaunchedAsync(id)`. Pre-build drift, surgical edit. Spec §6.2 step 8 updated; checklist item 6 reflects. |
| Mutex name stays hardcoded in v1.1; remote config drives only the version-drift banner | Spec §7.1 implies the mutex name itself comes from `roblox-compat.json` so a Roblox rename can ship via config-only. v1.1 instead hardcodes `Local\ROBLOX_singletonEvent` on `MutexHolder` and uses the remote config only for the version-drift banner. Rationale: runtime mutex-name swap requires a release+reacquire dance + cross-thread coordination that's bigger than item 10's scope. Trade for v1.1: if Roblox renames the mutex, ship a Velopack release with new hardcoded name (still hours, just a binary update instead of a config update). v1.2 candidate: introduce `IMutexHolder.RenameAsync` and DI factory that reads from cached config. Documented in §10 deferred. |

## Appendix A — Reference impl

The technique used by ROROROblox originated with MultiBloxy by Zgoly (https://github.com/Zgoly/MultiBloxy). The repo `c:\Users\estev\Projects\ROROROblox\PROVENANCE.txt` documents the binary, hash, technique, and caveats. ROROROblox is not a fork — it is a clean reimplementation in C# with substantially expanded scope (account management, structured launch flow, error handling, distribution).
