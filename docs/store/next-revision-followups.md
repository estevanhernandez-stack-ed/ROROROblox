# Next-revision follow-ups (post-v1.1.1.0)

> Captured during the v1.1.1.0 screenshot capture session. None of these blocked Microsoft cert review on either v1.1.0.0 (rejected on 10.1.1.1 only) or v1.1.1.0 (in review). Defer to the next minor — likely v1.2.0.0 since these are user-facing UX fixes, not patches.
>
> Discovered: 2026-05-04 by Este during sideload smoke. Carry forward each cycle until shipped.

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

**Symptom:** After renaming + bumping to MSIX manifest version `1.1.1.0`, the About box still shows `v1.0.0`. Caused by `Assembly.GetName().Version.ToString(3)` reading the .NET-default assembly version (1.0.0.0) because `<Version>` is not set in `ROROROblox.App.csproj`.

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

## Disposition

All three are queued for the **next minor release (v1.2.0.0)** — they're UX-quality fixes rather than cert-side blockers. Microsoft accepted v1.1.1.0 with all three present (or the rejection was 10.1.1.1, unrelated to these). Carry forward in `README.md` Roadmap → "Up next" so the items don't drop off the radar.

Workshop the order during the next vibe-cartographer / build-plan pass — likely #3 first (smallest, highest signal), then #1 (user-experience gap), then #2 (lowest urgency).
