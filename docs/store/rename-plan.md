# Rename plan — addressing 10.1.1.1 rejection

> **Context:** Partner Center rejected v1.1.0.0 under clause **10.1.1.1 Inaccurate Representation**. Reviewer note: *"The product name does not accurately represent the product. The product name contains the title of another piece of software or service. Please edit the Product Name field in the Store Listings section."*
>
> The fix is a rename of the user-visible product. The package's internal Identity (`626LabsLLC.RORORO`) and Partner Center reservation can stay — Microsoft asked specifically for a Listing-side change. We bump version (1.1.0.1 minimum, 1.2.0.0 if we want a clean reset), update every user-visible surface, re-upload.

## Decisions to lock before any execution

1. **New product name.** Recommendation: **RORORO** (drops `blox`, keeps the stutter that ties to the icon). Alternatives: `TripleStax`, `TripleR`. Rejected: `RoRoRoUrBlox` (still has `Ro…blox` shape, same trigger).
2. **New tagline.** Current `"Multi-launcher for Windows."` is fine in **description body** (nominative use with disclaimer is permitted) but should NOT appear in display-name slots (wordmark, manifest DisplayName, About box title). Proposal:
   - Display surfaces (wordmark / About / wide tile / splash): drop the tagline OR replace with a non-Roblox-naming line. Suggestion: **"Triple-launch for Windows."**
   - Description body (Store listing long description): keep "Multi-launcher for Windows" as the descriptive sub-positioning since description body has nominative-use latitude.
3. **Identity Name strategy.** Recommendation: **keep `626LabsLLC.RORORO`** for now (Microsoft's rejection was Listing-specific, not Identity-specific; re-reserving means a new Store ID + losing the existing reservation). If Microsoft re-rejects citing Identity-side visibility, escalate to a re-reservation in a follow-up cycle.
4. **Internal data path strategy.** Recommendation: **keep `%LOCALAPPDATA%\ROROROblox\`** as the local data folder. These paths are filesystem-internal, never user-facing. Changing them would orphan existing dev/test data. The constant string `"RORORO"` lives on as a folder name — Microsoft cert reviewers don't see filesystem structure inside the MSIX install (it's virtualized to package LocalState by Identity Name anyway).
5. **Repo name.** Recommendation: **keep `github.com/.../RORORO`**. Repo renames break external links and require Pages baseurl reconfig. Internal-only artifact; doesn't surface to Store reviewers.
6. **Version.** Recommendation: bump to **1.1.0.1** if Microsoft accepts a Listing-only fix without a manifest re-upload, OR **1.2.0.0** if we re-pack the MSIX with a new DisplayName.

## Surfaces inventory — every place "RORORO" appears as a user-visible string

### Phase 1 — Partner Center (USER does this)

- [ ] Partner Center → your app → **Store listings** → English (United States) → **Product name** field → set to new name
- [ ] (If you create a new submission) **What's new in this version** → "Renamed product per Partner Center listing guidance; functionality unchanged."
- [ ] Re-paste **Description** body from the updated `docs/store/listing-copy.md` (will be regenerated in Phase 5)
- [ ] Trademark info field — no change needed (already disclaims Roblox)
- [ ] Copyright field — no change needed
- [ ] Screenshots — only re-capture if any of yours show the old wordmark prominently in a window title bar; otherwise the same screenshot set covers the new name (the icon doesn't change, the tile graphics get re-rendered in Phase 4)

### Phase 2 — MSIX manifest (`src/ROROROblox.App/Package.appxmanifest`)

- [ ] `<Properties><DisplayName>RORORO</DisplayName>` → new name
- [ ] `<Properties><Description>` → revise (drop "Multi-launcher for Windows" from this slot)
- [ ] `<Application uap:VisualElements DisplayName="RORORO"` → new name
- [ ] `<Application uap:VisualElements Description="Run multiple Roblox clients side by side.">` → keep (nominative use OK in Description)
- [ ] `<Identity Name="626LabsLLC.RORORO" Version="1.1.0.0">` → bump Version to `1.1.0.1` (or `1.2.0.0` if we want a clean reset)
- [ ] `<Application Id="RORORO">` → leave (internal application ID, not user-visible)
- [ ] `<uap:ShowNameOnTiles>` declarations → keep (Windows overlays the new DisplayName automatically)

### Phase 3 — WPF UI strings (XAML + code-behind)

**Window titles + headers:**
- [ ] `MainWindow.xaml` line 7 — `Title="RORORO"` → new name
- [ ] `MainWindow.xaml` line 501 (TitleBar `Title=`) → new name
- [ ] `MainWindow.xaml` line 544 (header TextBlock `Text="RORORO"`) → new name
- [ ] `MainWindow.xaml` footer `Text="Multi-launcher for Windows. A 626 Labs product."` → revise (drop the Roblox-naming half OR replace with new tagline)
- [ ] `AboutWindow.xaml` `Title="About RORORO"` → new name
- [ ] `AboutWindow.xaml` line 58 hero `<TextBlock Text="RORORO">` → new name
- [ ] `AboutWindow.xaml` line 71 tagline `<TextBlock Text="Multi-launcher for Windows.">` → revise (drop or replace)
- [ ] `WelcomeWindow.xaml` line 41 `<TextBlock Text="RORORO">` → new name
- [ ] `WelcomeWindow.xaml` Title attribute → check + update
- [ ] All other secondary windows — Title attributes (Diagnostics, Settings, JoinByLink, SquadLaunch, FriendFollow, ThemeBuilder, CaptionColorPicker, SessionHistory, CookieCapture, RobloxNotInstalled, WebView2NotInstalled, DpapiCorrupt, Preferences, PreferencesWindow)
- [ ] `MainWindow.xaml` "Run multiple Roblox clients side by side. Add a saved account, click Launch As to open it." subtitle → keep (nominative use in description)

**Code-behind status messages + dialogs:**
- [ ] Search `*.xaml.cs` for any string literal containing `"RORORO"` and assess each — most are filesystem paths (keep) or window titles (change)

**Tray:**
- [ ] `TrayService.cs:67` — fallback string `"RORORO"` (likely a tooltip) → new name
- [ ] Tray menu items (Open, Toggle, Quit, etc.) → no rename needed unless they show the product name

### Phase 4 — Asset PNGs with wordmarks (re-render)

The voxel-stack icon itself is brand-agnostic and doesn't change. Wordmark text in PNG assets is what changes. Re-render via `scripts/generate-store-assets.ps1` after we update the wordmark constant in the script.

In-MSIX assets:
- [ ] `Wide310x150Logo.*.png` (5 scale variants + bare-name) — re-render
- [ ] `SplashScreen.*.png` (5 scale variants + bare-name) — re-render
- [ ] `Square*.png` and `StoreLogo.*.png` — no wordmark, no re-render needed

Partner Center listing graphics (`docs/store/graphics/`):
- [ ] `store-boxart-1080x1080.png` + `2160x2160.png` — re-render
- [ ] `store-poster-720x1080.png` + `1440x2160.png` — re-render
- [ ] `store-hero-1920x1080.png` + `3840x2160.png` — re-render
- [ ] `store-display-{71,150,300}x*.png` — no wordmark, no re-render needed

Tray ICOs (`src/ROROROblox.App/Tray/Resources/`):
- [ ] No wordmark, no re-render needed

Hero images for README (`docs/images/`):
- [ ] `hero.png` — derived from Square310, no wordmark, no re-render
- [ ] `hero-wide.png` — derived from Wide310x150 (HAS wordmark) — re-copy after Phase 4

### Phase 5 — Documentation

- [ ] `README.md` — hero centered title, badges, all body section references, footer attribution
- [ ] `docs/PRIVACY.md` — title, frontmatter title, body references
- [ ] `docs/index.md` — Pages landing
- [ ] `docs/_config.yml` — site `title:` field + meta description
- [ ] `docs/store/listing-copy.md` — entire file (long description, short description, copyright, trademark, additional license terms, product features)
- [ ] `docs/store/age-rating.md` — references
- [ ] `docs/store/screenshots-checklist.md` — references
- [ ] `docs/store/submission-checklist.md` — references
- [ ] `docs/store/reviewer-letter.md` — entire reviewer letter (now also notes the rename context — "Following the 10.1.1.1 rejection of v1.1.0.0, we have renamed the product per Microsoft's guidance...")
- [ ] `process-notes.md` — references
- [ ] `PROVENANCE.txt` — references
- [ ] `Overachiever.md` — references
- [ ] `CONTRIBUTING.md` — references
- [ ] `CLAUDE.md` — references throughout (this is the AI-agent guidance file; needs the new product name to set context properly for future Claude sessions)
- [ ] `docs/themes/AGENT_PROMPT.md` — references

### Phase 6 — Source code constants (narrow set)

**Change (user-visible network or registry surface):**
- [ ] `App.xaml.cs:235, 243` — `ProductInfoHeaderValue("RORORO", version)` (User-Agent sent to Roblox + GitHub) → new name (consistent with brand; servers don't enforce)
- [ ] `StartupRegistration.cs:8` — `ValueName = "RORORO"` (visible in Task Manager → Startup Apps tab) → new name

**Keep (filesystem-internal, no user-facing surface):**
- `AppLogging.cs:19` — log dir name `"RORORO"` → keep (filesystem path, not a label)
- `AppLogging.cs:32` — `.WithProperty("App", "RORORO")` → keep (internal log property)
- `AccountStore.cs:41`, `AppSettings.cs:40`, `FavoriteGameStore.cs:39` — data path `"RORORO"` → keep (path stability across rename)
- `CookieCapture.cs:18`, `WelcomeWindow.xaml.cs:14`, `App.xaml.cs:269`, `DiagnosticsWindow.xaml.cs:121`, `DpapiCorruptWindow.xaml.cs:32`, `UpdateChecker.cs:21` — same (path stability)
- `App.xaml.cs:53` — `SingleInstanceGuard("ROROROblox-app-singleton")` → keep (internal mutex)
- `Package.appxmanifest` `<Application Id="RORORO">` → keep (internal app ID)

### Phase 7 — Asset script + build

- [ ] `scripts/generate-store-assets.ps1` — replace the wordmark string constants in `Render-Wide`, `Render-Splash`, `Render-BoxArt`, `Render-Poster`, `Render-Hero`. Same script, new output.
- [ ] `scripts/finalize-store-build.ps1` — no change (uses parameters, not string literals)
- [ ] `scripts/build-msix.ps1` — no change (manifest-driven)
- [ ] Re-run `scripts/generate-store-assets.ps1` to regenerate all PNGs
- [ ] Re-run `scripts/finalize-store-build.ps1 -Version "1.1.0.1"` to rebuild the Store MSIX with the new DisplayName

### Phase 8 — Resubmit (USER does this)

- [ ] Upload `dist/ROROROblox-Store.msix` (still the same filename; package contents have new DisplayName)
- [ ] Edit Partner Center listing **Product Name** to the new name
- [ ] Update **What's new** field
- [ ] Re-paste long description from updated `docs/store/listing-copy.md`
- [ ] Submit for cert review
- [ ] **Notes for certification:** paste the updated `docs/store/reviewer-letter.md` — letter now includes a rename-context paragraph that proactively names the 10.1.1.1 fix.

## What stays unchanged (defense list)

These are NOT user-visible — DO NOT change them as part of the rename:

- C# namespaces: `ROROROblox.App`, `ROROROblox.Core`, `ROROROblox.App.Theming`, `ROROROblox.App.About`, etc.
- Source file paths: `src/ROROROblox.App/`, `src/ROROROblox.Core/`, `src/ROROROblox.Tests/`
- Project files: `ROROROblox.App.csproj`, `ROROROblox.Core.csproj`, `ROROROblox.Tests.csproj`
- Solution file: `ROROROblox.slnx`
- Filesystem data paths: `%LOCALAPPDATA%\ROROROblox\` and all subdirs (`logs/`, `webview2-data/`, `themes/`, `accounts.dat`, `settings.json`, `favorites.json`)
- Internal mutex: `"ROROROblox-app-singleton"`
- Internal application ID: `<Application Id="RORORO">`
- GitHub repo URL: `github.com/estevanhernandez-stack-ed/ROROROblox`
- GitHub Pages baseurl: `/RORORO`
- Privacy URL: `https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/`
- Identity Name: `626LabsLLC.RORORO` (unless Microsoft escalates the rejection)
- Publisher CN: `CN=177BCE59-0966-4975-9962-10E36652141F`
- Store ID: `9NMJCS390KWB`

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| **Microsoft rejects again citing Identity Name visibility** (unlikely — they specifically asked for the Listing field) | If they do, escalate to a new Partner Center reservation. Existing publisher account stays. Larger version bump. |
| **Existing dev/test data orphans** if we change the LocalAppData path | We're explicitly NOT changing it — see "What stays unchanged." |
| **External links to repo break** if we rename the GitHub repo | We're explicitly NOT renaming the repo. |
| **Pages URL breaks** if baseurl changes | We're keeping `/RORORO` as the baseurl. |
| **Tagline rewrite degrades brand voice** | Keep "Multi-launcher for Windows" in the description body (nominative-use defense already established); only remove from display-name surfaces. |
| **Microsoft notices the rename mid-cert and pauses** | Reviewer letter proactively explains the rename and references the rejection clause. Collaborative-engineering-peer framing per Sanduhr playbook. |

## Time budget

- Phase 1 (Partner Center listing edit + reservation check): ~10 min, USER
- Phases 2-7 (manifest + UI + assets + docs + script + build): ~30-45 min, ASSISTANT
- Phase 8 (upload + listing fields + submit): ~10 min, USER

Total: ~1 hour wall time. Most of it is the assistant's batch work; user time is the Partner Center clicks at the bookends.

## Execution gate

Lock the new name (default: **RORORO**), and assistant proceeds with Phases 2-7 in a single batch. Phase 1 and Phase 8 are yours.
