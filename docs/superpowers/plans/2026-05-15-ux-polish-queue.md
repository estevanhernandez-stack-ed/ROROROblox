# UX-Polish Queue — 2026-05-15

> **Status:** queued from `/vibe-iterate:ux-polish` round 2 (session `f71ba9ca-101f-4ccc-aceb-f983fde9cfaa`). Predecessor: **PR #16** (brand-text "RORORO" → "RoRoRo" across 13 XAML files) — merge that first, then branch off `main` for any of these. Atlas entry written with `outcome: "queued"`.

Five rough patches surfaced. Top recommendation has full implementation steps; the others have enough detail to be actionable without a re-scan.

---

## Recommended sequence

1. **#1 — CookieCaptureWindow loading state** *(every first-time Add Account hits this; small surgical diff)*
2. **#2 — DpapiCorruptWindow "Start Fresh" confirmation** *(rare trigger, high-stakes outcome; defer until you decide which fix shape to pick)*
3. **#4 — RobloxNotInstalled "I have Bloxstrap" label** *(trivial one-liner; bundle with another polish if you have one open)*
4. **#5 — AboutWindow voxel theming** *(skip unless custom-themes audience grows)*
5. **#3 — CookieCapture error UI** *(skip on the polish track; promote to `feature-add` if WebView2-failure UX becomes a real complaint)*

---

## 1 — CookieCaptureWindow: loading state during slow WebView2 first-init **[RECOMMENDED]**

**Anchor:** `src/ROROROblox.App/CookieCapture/CookieCaptureWindow.xaml:57` + `CookieCaptureWindow.xaml.cs:20,38-52,67-68`
**Trust:** 3 (erodes) · **Effort:** 4/5 · **Score 10**

### The rough patch
On the first Add Account, the user sees the header explainer + a *blank pane* for several seconds while `CoreWebView2Environment.CreateAsync` + `EnsureCoreWebView2Async` + the first `Navigate` complete. The existing hint at lines 42-43 prescribes a recovery ("If the page is blank, click Reload (F5)") — useful as a worst-case failsafe but it tells the user *what to do if it's broken*, not *what's happening right now*.

### The fix
Spinner overlay in the WebView area, visible from window-load until the first `NavigationCompleted` returns success. Brand-cyan indeterminate progress bar + short status label.

### XAML edit — `CookieCaptureWindow.xaml`

Replace line 57:
```xml
<wv:WebView2 x:Name="WebView" Grid.Row="1" />
```

With:
```xml
<Grid Grid.Row="1">
    <wv:WebView2 x:Name="WebView" />

    <!-- Loading overlay — shown until the first NavigationCompleted fires successfully.
         Covers the slow CoreWebView2Environment.CreateAsync + EnsureCoreWebView2Async
         + first Navigate pre-render window. Hidden in code-behind once roblox.com paints. -->
    <Border x:Name="LoadingOverlay"
            Background="#0F1F31">
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
            <TextBlock Text="Loading roblox.com..."
                       Foreground="#17D4FA"
                       FontSize="14" FontWeight="SemiBold"
                       HorizontalAlignment="Center"
                       Margin="0,0,0,12" />
            <ProgressBar IsIndeterminate="True"
                         Width="200" Height="4"
                         Foreground="#17D4FA"
                         Background="#15263A"
                         BorderThickness="0" />
            <TextBlock Text="First open can take a few seconds on a fresh WebView2 install."
                       Foreground="#FFFFFF"
                       Opacity="0.65"
                       FontSize="11"
                       Margin="0,12,0,0"
                       HorizontalAlignment="Center"
                       TextWrapping="Wrap"
                       MaxWidth="300" />
        </StackPanel>
    </Border>
</Grid>
```

### Code-behind edit — `CookieCaptureWindow.xaml.cs`

After line 20 (`private bool _captured;`), add the new flag:
```csharp
    private bool _firstNavComplete;
```

Replace `OnNavigationCompleted` (lines 67-68):
```csharp
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        => _ = TryCaptureAsync("NavigationCompleted");
```

With:
```csharp
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_firstNavComplete && e.IsSuccess)
        {
            _firstNavComplete = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        _ = TryCaptureAsync("NavigationCompleted");
    }
```

### Test plan
- [ ] `dotnet build` — expect 0 errors
- [ ] `dotnet test src/ROROROblox.Tests/` — expect 346 passing (no logic affected, only UI state)
- [ ] Manual visual: launch RoRoRo → Add Account → spinner visible while WebView2 inits → spinner hides + roblox.com login renders

### PR commit message
```
polish(cookie-capture): spinner overlay during WebView2 first-init

CookieCaptureWindow showed a blank pane for several seconds while
CoreWebView2Environment.CreateAsync + EnsureCoreWebView2Async + the first
Navigate complete. The existing F5-recovery hint covered the worst case but
didn't tell the user what's happening right now. Brand-cyan indeterminate
ProgressBar + status label fills the gap; overlay hides on first successful
NavigationCompleted.
```

Branch suggestion: `polish/cookie-capture-loading-state` off `main`.

---

## 2 — DpapiCorruptWindow: "Start Fresh" is a single-click destructive CTA

**Anchor:** `src/ROROROblox.App/Modals/DpapiCorruptWindow.xaml:36-39` + `DpapiCorruptWindow.xaml.cs` (OnStartFreshClick).
**Trust:** 3 (erodes) · **Effort:** 4/5 · **Score 10**

### The rough patch
The cyan primary button wipes `accounts.dat` with no friction. Rare trigger (Windows restored from backup, `accounts.dat` copied across PCs) but high-stakes outcome. For a panicked user reading the scary modal, the prominent primary cyan button is positioned exactly for an accidental click.

### Fix options (pick one)
- **Option A — Two-click confirm.** First click renames to "Click again to confirm" + flips style (magenta). Second click within 5s commits; otherwise reverts. ~20-line code-behind addition.
- **Option B — Demote + reorder.** "Start Fresh" becomes secondary (dark, bordered like "Quit" today); "Quit" becomes primary cyan. The safe default wins by gravity. XAML-only, ~6-line swap. **Recommended.** Effort drops to 5/5.
- **Option C — Typed confirmation.** Add a `TextBox`: "Type **START FRESH** to confirm." Button stays `IsEnabled=false` until match. Strongest safety, heaviest UX. Effort 3/5.

### Recommendation
**Option B.** Matches the app's convention elsewhere that primary-cyan = safe-default. Doesn't require behavior change in the click handler. Cheapest meaningful win.

### Test plan
- [ ] Build clean
- [ ] Manual visual: hit the modal (force via `accounts.dat` corruption simulation or run with the corrupt fixture from tests) → verify Quit is primary, Start Fresh is secondary

---

## 3 — CookieCaptureWindow: no error UI on WebView2 navigation failure

**Anchor:** `src/ROROROblox.App/CookieCapture/CookieCaptureWindow.xaml.cs:67-68` — `OnNavigationCompleted` ignores `e.IsSuccess`.
**Trust:** 3 · **Effort:** 2/5 · **Score 8**

### The rough patch
When `NavigationCompleted` fires with `IsSuccess=false` (no network, roblox.com unreachable, cert error), the existing F5 hint is the only visible signal — and F5 won't fix a network outage. The user is stuck staring at a blank page with no diagnostic.

### Why this is on the queue rather than the polish track
Doing this *well* means handling `CoreWebView2WebErrorStatus` (~15 enum values), distinguishing network vs certificate vs CSP vs unknown, and providing actionable copy per category. That's feature-shaped, not polish-shaped. **If WebView2-failure UX becomes a real complaint surface, escalate to `/vibe-iterate:feature-add`.** Skip on the polish track.

---

## 4 — RobloxNotInstalledWindow: "I have Bloxstrap" label

**Anchor:** `src/ROROROblox.App/Modals/RobloxNotInstalledWindow.xaml:41`.
**Trust:** 1 · **Effort:** 5/5 · **Score 7**

### The rough patch
`Content="I have Bloxstrap"` reads as a user declaration ("yes I have it"), not a verb-first action label. Every other CTA in the app is verb-first.

### The fix
Rename the `Content` attribute. Verify what `OnBloxstrapClick` actually does first (in `RobloxNotInstalledWindow.xaml.cs`) — pick the label that names that action. Candidates:
- "Use Bloxstrap" (if click just dismisses)
- "Open Bloxstrap settings" (if click launches Bloxstrap's config)
- "Configure in Bloxstrap" (similar)

1-line XAML edit. Bundle with any other polish PR if you have one open.

---

## 5 — AboutWindow: iso-voxel shadow brushes hardcoded

**Anchor:** `src/ROROROblox.App/About/AboutWindow.xaml:13-19,34-53`.
**Trust:** 1 · **Effort:** 3/5 · **Score 5**

### The rough patch
Voxel face brushes use `{DynamicResource CyanBrush}` (themed); shadow brushes (`CyanShadowBrush`, `CyanDimBrush`, `CyanBrightBrush`, `MagentaDimBrush`, `MagentaShadowBrush`, `NavySoftBrush`) are local `{StaticResource}` definitions with hardcoded colors. On theme change the faces shift but shadows don't, breaking the iso-voxel illusion.

### The fix shape
Extend the app's theme palette in `App.xaml` (or wherever the cyan/magenta DynamicResources live) with `CyanShadow` / `CyanDim` / `CyanBright` / `MagentaShadow` / `MagentaDim` / `NavySoft` keys. Swap the AboutWindow voxel `StaticResource` references to `DynamicResource`.

### Why this might stay deferred
Pet Sim 99 clan default users never see this. Only affects users who built custom themes. Skip unless that audience grows.

---

## Notes

- All rough patches were sourced from the prior `/vibe-iterate:ux-polish` scan (Atlas entry 2026-05-14T22:24:xx, "Brand: user-facing 'RORORO' to 'RoRoRo' across in-app surfaces"). Surfaces haven't materially changed between scans.
- Re-running `/vibe-iterate:ux-polish` after merging PR #16 + shipping any of the above would re-walk the surfaces and may surface NEW patches that this round didn't find (the scan was scoped to high-traffic surfaces; not-scanned: `.cs` file user-facing strings, `Package.appxmanifest` DisplayName, README, and most power-user windows: Preferences/Settings/Diagnostics/SessionHistory/SquadLaunch/JoinByLink/ThemeBuilder/CaptionColorPicker).
- Atlas reference: `.vibe-iterate/atlas.jsonl` (last 3 entries: competitive queue 2026-05-06, ux-polish brand-fix 2026-05-14, this queue 2026-05-15).
