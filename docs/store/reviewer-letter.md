# Notes for certification — reviewer letter (v1.1.2.0)

> Paste the block between the `---` markers below into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.1.0.0 was rejected under **10.1.1.1 Inaccurate Representation** for containing the name of another product. v1.1.2.0 ships with the product renamed from `ROROROblox` to **`RORORO`** across every user-visible surface. The letter leads with the rename narrative so the reviewer reads "deliberate good-faith fix," not "same submission, second attempt."

---

```
Hello reviewer,

Thank you for your time on v1.1.2.0. This is a re-submission addressing
the v1.1.0.0 rejection under clause 10.1.1.1, plus four small polish
fixes we shipped in-band rather than queue for a future release.

THE RENAME (10.1.1.1 FIX)

The previous submission was rejected with this guidance:

  "The product name does not accurately represent the product. The
   product name contains the title of another piece of software or
   service. Please edit the Product Name field in the Store Listings
   section."

We took this to heart and renamed the product from ROROROblox to RORORO
everywhere a user can see it:

  - Store listing Product Name field
  - MSIX manifest <DisplayName> + <VisualElements DisplayName>
  - Every WPF window title (Main, About, Welcome, Diagnostics, Settings,
    Preferences, History, Theme Builder, Squad Launch, Friend Follow,
    Join by Link, modals)
  - In-app wordmark in the About box, Welcome screen, and main header
  - Wordmark on every tile, splash, box-art, poster, and hero graphic
    (32 PNG variants regenerated from a single source)
  - README, privacy policy, all in-product copy, all trademark notices
  - Tagline: "Multi-Roblox Instant Generator" -> "Multi-launcher for
    Windows" on every display surface (the longer phrase only appears
    inside the Store description body, where nominative use is permitted
    and clearly disclaimed)

The new name "RORORO" drops the "blox" suffix entirely. It does not
contain or resemble the name of any other product. The functionality is
unchanged from v1.1.0.0.

WHAT DID NOT CHANGE, AND WHY

  - Identity Name (`626LabsLLC.RoRoRoBlox`) and Partner Center reservation
    are unchanged. Your rejection note specifically asked us to edit the
    Product Name field in the Store Listings section, not to re-reserve
    a different package identity. If a different surface inside the
    package is the source of the concern, please tell us and we will
    re-reserve in a follow-up submission.
  - Source repository URL (github.com/estevanhernandez-stack-ed/ROROROblox)
    and the GitHub Pages baseurl that hosts our privacy policy are
    unchanged. Renaming those would break inbound links and prior issue
    references; the path is internal infrastructure, not user-facing
    branding.

The product itself — the user-visible application, its name, its
branding, its wordmark — is now exclusively RORORO.

POLISH SHIPPED IN v1.1.2.0 BEYOND THE RENAME

Four small UX issues surfaced during our pre-submission smoke. We chose
to fix them in this submission rather than defer:

  1. The embedded login WebView occasionally renders a blank page on
     first open. We added an inline reload hint above the WebView so
     users can recover with F5 instead of force-quitting.
  2. The Saved Games library could overflow the default window without
     a visible scrollbar (Auto-hide hid the affordance). Default window
     height bumped, scrollbar set to always-visible.
  3. The About box was reading the .NET-default assembly version
     (1.0.0) instead of the manifest version. The csproj <Version>
     element now matches the manifest, and our release script patches
     both atomically.
  4. The startup pass that proactively validated saved sessions against
     authenticated Roblox endpoints was removed. The eager-validation
     pattern was producing false-positive "session expired" badges on
     fresh cookies; lazy validation now happens on a Launch As attempt,
     where the friction is justified.

WHAT THE APP IS

RORORO is a Windows launcher that lets a user save multiple Roblox
account credentials and launch any of them — including several at once,
in separate client windows — without having to log out and back in
between accounts. It is a productivity utility for Roblox players who
maintain more than one account.

WHAT IT IS NOT

RORORO does not modify the Roblox client. It does not inject into, hook
into, attach a debugger to, or read memory from the Roblox process. It
does not bundle game content, scripts, plugins, exploits, or automation.
It does not include chat, multiplayer networking, user-generated content,
in-app purchases, or advertising.

HOW IT WORKS (TECHNICAL)

Two documented operations:

  1. Hold the Windows named mutex Local\ROBLOX_singletonEvent before
     launching the official Roblox client, so subsequent launches spawn
     new processes instead of focusing the first.
  2. Use Roblox's documented authentication-ticket flow (cookie -> CSRF
     -> /v1/authentication-ticket -> roblox-player: URI) to launch a
     Roblox client signed in as a specific saved account.

Roblox session cookies are stored locally only, encrypted with the
Windows Data Protection API (DPAPI) per Windows user. They never leave
the machine. There is no backend, no telemetry, no analytics, no
third-party SDKs, and no data collection.

TRADEMARK NOTICE

"Roblox" and the Roblox logo are trademarks of Roblox Corporation.
RORORO is an independent third-party tool, not affiliated with, endorsed
by, or sponsored by Roblox Corporation. Following the v1.1.0.0
rejection, the trademark no longer appears in the product name or in
any wordmark surface. It appears only in the long-form description body
to describe compatibility — nominative fair use — with a clearly
visible disclaimer in the same paragraph.

UNIQUE LASTING VALUE

Multiple cooperating features, not a single trick:

  - Multi-instance via mutex hold
  - DPAPI-encrypted account vault with WebView2-isolated login capture
  - Per-account game launch routing
  - System tray with state colours and double-click main launch
  - Squad Launch + Friend Follow + Join-by-link surfaces
  - Velopack auto-update with remote roblox-compat config
  - Diagnostics bundle for bug reports

PLATFORM SUPPORT (Arm64 acknowledgment)

v1.1.2.0 targets x64 only. We have read Partner Center's recommendation
that future Windows on Arm devices will not support AArch32 emulation,
and that an Arm64 build is the recommended path forward. An Arm64 build
flavour is on our v1.2 roadmap and is publicly tracked in our repository.
For now, x64 covers our entire targeted audience (Windows 11 on Intel
and AMD); Arm device customers can install via the standard x64-on-Arm
emulation path until the native Arm64 flavour ships. We are not asking
for a waiver — just confirming the recommendation is captured and
queued.

PRIVACY POLICY

Live at:
https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

If anything in this submission is unclear, the PROVENANCE.txt file
explains the relationship to the predecessor MultiBloxy implementation,
docs/store/rename-plan.md details every surface touched in the rename,
and docs/store/submission-checklist.md catalogues our trademark-
disclaimer surfaces.

Thank you again for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for resubmission edits)

If this submission is rejected on a different clause than 10.1.1.1, the response should reinforce the defense already in the letter. Don't argue the clause; cite which paragraph addresses it and what changed:

| Clause | Defense paragraph in this letter | If rejected, what to add |
|---|---|---|
| **10.1.1.1** name representation | "THE RENAME (10.1.1.1 FIX)" + "TRADEMARK NOTICE" | If rejected on Identity Name visibility, escalate to a new Partner Center reservation. Reference the listing-side surfaces that were already cleaned. |
| **10.1.4.4.a** trademark / attribution | "TRADEMARK NOTICE" | Reference the visible disclaimer surfaces (About box, README, privacy policy). Offer a screenshot of the in-app About box's trademark line. |
| **10.1.4.4.b** unique lasting value | "UNIQUE LASTING VALUE" | Map each listed feature to the screenshot # in the listing where it's visible. Reviewer needs to see the feature, not just read about it. |
| **10.1.4.4.c** navigation | (not pre-defended; address only if rejected) | Map screenshot # → button click → effect for each top-level surface. Demonstrate every claimed feature is reachable in ≤2 clicks from the main window. |
| **10.10** (security / functionality) | "WHAT IT IS NOT" | Cite that RORORO makes no calls to Roblox-side endpoints other than the documented public ones. Privacy policy lists these verbatim. |

## Pre-submission sanity check

Before clicking **Submit for review** for v1.1.2.0, eyeball:

- [ ] Privacy URL renders cleanly when opened in an incognito browser
- [ ] Trademark disclaimer is in the long description, not just the trademark info field
- [ ] Screenshots cover at least 4 of the 7 features in the UNIQUE LASTING VALUE list
- [ ] **Product Name field shows `RORORO`** (the field Microsoft asked us to fix)
- [ ] Age rating answers are consistent with privacy policy (no telemetry, no UGC, etc.)
- [ ] Identity name in `Package.appxmanifest` matches your Partner Center reservation
- [ ] Version in `Package.appxmanifest` is `1.1.2.0` (revision component is `0`)
- [ ] In-app About box wordmark reads `RORORO` and version reads `v1.1.2`
- [ ] In-app MainWindow title reads `RORORO`
- [ ] Wide tile and splash graphics show `RORORO` wordmark

## Source

This letter was generated as part of the v1.1.2.0 rename pass. Regenerable from chat history if needed; primary copy lives here.
