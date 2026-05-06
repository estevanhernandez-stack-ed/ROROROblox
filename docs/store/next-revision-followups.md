# Follow-ups discovered during v1.1.2.0 screenshot capture

> Discovered: 2026-05-04 by Este during sideload smoke before the v1.1.2.0 Partner Center submission. The user opted to fix in-band rather than defer — a "minimum viable fix" for each item shipped in **v1.1.2.0** itself. The "proper fix" for each (deeper architectural changes, runtime manifest reads, distinct state machines) remains future work; tracked here for v1.2.0.0+ so the items don't drop off the radar.
>
> Status legend below: **✅ shipped in v1.1.2.0** (the minimum viable fix landed) — **🔮 v1.2.0.0+** (the proper fix is still queued).

## 1. WebView2 white-screen on Add Account

**Symptom:** Sometimes when the user clicks *+ Add Account*, the embedded WebView2 control opens to a blank white page instead of `roblox.com/login`. Refreshing the page (F5 or right-click → Reload) brings up the actual login form.

**Theory:** WebView2 navigation is racing the window-shown event. The page starts loading before the WebView is fully attached / sized, and on slower hardware the initial fetch silently drops or DOM doesn't paint until a re-navigate.

**Fix candidates (next revision):**
- **Add a UI hint** in `CookieCaptureWindow.xaml` that says *"Page blank? Click Reload (F5) and the login form will appear."* — visible by default, dismissible, persistent until first successful login. Lowest-touch fix; addresses the user-experience gap without solving the underlying race.
- **Retry navigation on `NavigationCompleted` with empty body** — if the initial load reports `IsSuccess=true` but the document is `about:blank` at the time the event fires, re-issue the navigation. More robust but needs WebView2 lifecycle care.
- **Show a loading state in front of the WebView** until the first `DOMContentLoaded` fires successfully on `roblox.com/login`. Then the user sees a spinner instead of a white void.

**Acceptance:** A user who has never seen the form before can recover from the white-screen state without external instructions.

**File:** `src/ROROROblox.App/CookieCapture/CookieCaptureWindow.xaml` + `.xaml.cs`

## 2. Games / Settings window — content cut off, no affordance to scroll or resize

**Symptom:** The Games tab (Settings window) renders content below the visible window viewport. Users don't know to drag the window taller or that there's more content. Scroll bar isn't appearing prominently.

**Fix candidates (next revision):**
- **`ScrollViewer.VerticalScrollBarVisibility="Auto"`** on the games-list container if not already set — the scrollbar would appear when content overflows.
- **Set a sensible default `Height`** on the SettingsWindow that fits the typical games library plus header without scrolling.
- **`MinHeight` floor** so resizing narrower doesn't force the same problem.
- **Drop-shadow at the bottom edge** as a visual cue that more content exists below the fold (Material Design pattern).

**File:** `src/ROROROblox.App/Settings/SettingsWindow.xaml`

**Acceptance:** A user opening the Games tab on a default Windows 11 install sees either all the content OR an obvious scroll affordance, without having to drag-resize.

## 3. About box shows v1.0.0 instead of the actual release version

**Symptom:** After renaming + bumping to MSIX manifest version `1.1.2.0`, the About box still shows `v1.0.0`. Caused by `Assembly.GetName().Version.ToString(3)` reading the .NET-default assembly version (1.0.0.0) because `<Version>` is not set in `ROROROblox.App.csproj`.

**Fix candidates (next revision):**

A. **Set `<Version>` in csproj manually** — every release needs the version edited in two places (csproj + manifest), prone to drift. Simplest:
   ```xml
   <PropertyGroup>
     <Version>1.2.0.0</Version>
   </PropertyGroup>
   ```

B. **Auto-derive from the MSIX manifest at build time** — single source of truth. Add a build target that reads `<Identity Version>` from `Package.appxmanifest` and sets the `Version` MSBuild property. More work up-front but eliminates the drift class entirely.

C. **Read the manifest at runtime** — pivot the About box to call `Package.Current.Id.Version` (Windows.ApplicationModel) instead of `Assembly.GetName().Version`. Falls back to `Assembly` for unpackaged dev runs. This is what Sanduhr does; we should adopt the same pattern.

**Recommended:** Option C. Most resilient to drift, doesn't require build-time wiring, gracefully handles dev-run case.

**File:** `src/ROROROblox.App/About/AboutWindow.xaml.cs`

**Acceptance:** About box reads `v1.2.0.0` (or whatever the manifest says) at runtime, both for MSIX-installed users and for unpackaged dev runs.

## 4. Session marked "expired" after using Friends / presence / enumerate features — surprise 2FA re-prompt

**Symptom:** Within ~30 minutes of installing v1.1.2.0 + saving a main account, using a feature that calls authenticated Roblox endpoints (Friends modal, Squad Launch, presence-fetching surfaces — anything that "enumerates Roblox info" beyond the basic profile) caused the saved session to flip to `SessionExpired=true`. Re-authenticate flow then forced a 2FA round-trip even though the cookie was minutes old.

**Theory:** This is largely Roblox-side behavior, not a bug in our cookie handling. Roblox's anti-fraud system flags a session when the cookie is suddenly used against endpoints it hasn't been used for before, and when small 2FA grace periods elapse. Our code sees a `CookieExpiredException` (mapped from Roblox's 401/403 response) in `ValidateSessionsAsync` (`MainViewModel.cs:461-465`) or in the per-feature call paths and marks the session expired. The cookie isn't strictly expired — Roblox just wants the user to re-confirm with 2FA. Once re-confirmed, the same cookie value is fine again.

**What we can do better:**

A. **Distinguish "permanently expired" from "needs re-verification."** Today both map to `SessionExpired=true`. Roblox's response codes can hint:
   - 401 with `Token-Validation-Required` or specific body text → needs 2FA refresh, but cookie is still valid
   - 401 without that hint → genuinely expired, need re-login
   Add a new `AccountSummary` property `NeedsReverification` distinct from `SessionExpired`, and a yellow "Verify in browser" button that opens an embedded WebView2 to roblox.com (which auto-prompts 2FA) without requiring full re-login.

B. **Reduce eager validation surface.** Today `ValidateSessionsAsync` runs at startup against every saved account. That's potentially ~N calls to `users.roblox.com/v1/users/authenticated` against a fresh-from-storage cookie — exactly the pattern Roblox's anti-fraud watches. Pivot to lazy validation: only validate on a Launch As attempt, not at startup. Keeps the badge accurate without provoking re-auth.

C. **Better messaging in the Re-authenticate flow.** When the user clicks Re-authenticate, the WebView2 opens to roblox.com/login. If they were already logged in via cookie, Roblox renders a "Verify it's you" page (2FA) instead of the login form. Add inline copy explaining: *"Roblox sometimes asks for 2FA again when you use new features. Verify here and your saved login keeps working."*

D. **Capture the 2FA experience under telemetry-free local logs.** Today logs redact cookie values but don't note "session forced re-auth at HH:MM after feature X was used." Adding that context to the Diagnostics bundle would make this kind of report repro-able for a future maintainer.

**Acceptance:** A user who triggers Roblox's anti-fraud re-verification understands what's happening, knows their cookie isn't lost, and re-verifies in under 30 seconds without the app suggesting they re-add the account from scratch.

**File:** `src/ROROROblox.App/ViewModels/MainViewModel.cs` (`ValidateSessionsAsync`, `ReauthenticateAsync`), `src/ROROROblox.Core/CookieExpiredException.cs` (split into two exceptions or add a `Reason` enum), `src/ROROROblox.App/CookieCapture/CookieCaptureWindow.xaml` (new copy for re-verify mode).

**Roblox-side reference:** the `xsrf-token` + `2sv` (two-step verification) flow is documented unofficially in community resources; Bloxstrap solves a similar problem by surfacing a "verification needed" state distinct from full logout. Worth a study session.

## What shipped in v1.1.2.0 vs. what's still queued

| # | Issue | v1.1.2.0 status | What's left for v1.2.0.0+ |
|---|---|---|---|
| 1 | WebView2 white-screen on Add Account | ✅ Visible reload hint added to CookieCaptureWindow.xaml. Inline copy now tells the user "If the page is blank, click Reload (F5)" plus a 2FA-re-prompt explainer. | 🔮 The proper fix is detecting the blank-document case at the WebView2 level and re-issuing navigation automatically. Hint copy is the user-facing band-aid. |
| 2 | Games / Settings tab — content cut off, no scrollbar affordance | ✅ Default Height bumped 600→720, MinHeight 480→540, Saved-games ScrollViewer set to `VerticalScrollBarVisibility="Visible"` (always-on, not Auto-hide). | 🔮 The proper fix is responsive layout that adapts when the user resizes; for now Visible scrollbar is the discoverability cue. |
| 3 | About box shows v1.0.0 instead of release version | ✅ `<Version>1.1.2.0</Version>` added to ROROROblox.App.csproj — assembly version now matches manifest. About reads `Assembly.GetName().Version.ToString(3)` correctly. `scripts/finalize-store-build.ps1` extended to patch both csproj + manifest atomically on every release bump. | 🔮 The proper fix is reading from `Package.Current.Id.Version` at runtime (single source of truth, no possibility of drift) per Sanduhr's pattern. Today csproj and manifest can still drift if hand-edited; the script keeps them honest but requires using the script. |
| 4 | Session "expired" after using authenticated endpoints — surprise 2FA re-prompt | ✅ Removed `vm.ValidateSessionsAsync()` from `App.xaml.cs RunStartupChecksAsync`. No longer hammers `users.roblox.com/v1/users/authenticated` for every saved account at startup. The eager-validation surface that pattern-matched Roblox's anti-fraud is gone. | 🔮 The proper fix is the `NeedsReverification` state distinct from `SessionExpired`, with a "Verify in browser" button. Today Launch As surfaces real expiry/reverify need lazily; the band-aid avoids the false-positive cascade but doesn't fix the underlying conflation of "expired" vs "needs 2FA confirm". |

The four `🔮` items remain in [`README.md`](../../README.md#roadmap) Roadmap → "Up next" so they carry into the v1.2.0.0 plan.

Workshop the proper-fix order during the next vibe-cartographer / build-plan pass — likely #4 first (highest-quality UX gain), then #3 (single-source-of-truth wins), then #1 (resilience), then #2 (responsive layout).
