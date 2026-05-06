# Notes for certification — reviewer letter (v1.1.0.1, post-rename)

> Paste this into Partner Center → your app → **Submission options** → **Notes for certification**.
>
> v1.1.0.0 was rejected under clause **10.1.1.1 Inaccurate Representation** for containing the name of another product. v1.1.0.1 ships with the product renamed from `ROROROblox` to **`RORORO`** across every user-visible surface. The letter below names the rename context up-front so the reviewer sees a deliberate good-faith fix, not a re-attempt of the same submission.

---

```
Hello reviewer,

Thank you for taking the time to certify v1.1.0.1 of this submission. A short orientation that should make this faster to evaluate.

CONTEXT — RENAME FROM v1.1.0.0
The previous submission (v1.1.0.0) was rejected under clause 10.1.1.1 Inaccurate Representation: "The product name does not accurately represent the product. The product name contains the title of another piece of software or service. Please edit the Product Name field in the Store Listings section."

For v1.1.0.1 we have renamed the product from "ROROROblox" to "RORORO" across every user-visible surface — the Store listing Product Name field, the MSIX manifest DisplayName + VisualElements DisplayName, the in-app window titles, the About box, the Welcome window, the wordmark in tile + splash + box-art + poster + hero graphics, the README, the privacy policy, and the trademark notices. The new name drops "blox" entirely; it does not contain or resemble the name of any other product. The functionality of the app is unchanged from v1.1.0.0.

The Identity Name (`626LabsLLC.RoRoRoBlox`) and Partner Center reservation are unchanged — your rejection note specifically asked us to edit the Product Name field in the Store Listings section, not to re-reserve. If a different surface inside the package is the source of the concern, please tell us and we will re-reserve in a follow-up submission.

WHAT THE APP IS
RORORO is a Windows launcher for the Roblox platform. It lets a user save multiple Roblox account credentials and launch any of them — including several at once, in separate client windows — without having to log out and back in between accounts. It is a productivity utility for Roblox players who maintain more than one account.

WHAT IT IS NOT
RORORO does not modify the Roblox client. It does not inject into, hook into, attach a debugger to, or read memory from the Roblox process. It does not bundle game content, scripts, plugins, exploits, or automation. It does not include chat, multiplayer networking, user-generated content, in-app purchases, or advertising.

HOW IT WORKS (TECHNICAL)
RORORO does two documented things:
1. It holds the Windows named mutex Local\ROBLOX_singletonEvent before launching the official Roblox client. This causes subsequent Roblox launches to spawn new processes instead of focusing the first one. The technique is standard and has been used by similar tools (e.g., MultiBloxy, Bloxstrap) for years.
2. It uses Roblox's documented authentication-ticket flow — the cookie → CSRF → /v1/authentication-ticket → roblox-player: URI sequence — to launch a Roblox client signed in as a specific saved account.

Roblox session cookies are stored locally only, encrypted with the Windows Data Protection API (DPAPI) per Windows user. They never leave the machine. There is no backend, no telemetry, no analytics, no third-party SDKs, and no data collection.

TRADEMARK NOTICE
"Roblox" and the Roblox logo are trademarks of Roblox Corporation. RORORO is an independent third-party tool, not affiliated with, endorsed by, or sponsored by Roblox Corporation. Following the v1.1.0.0 rejection, we have removed every appearance of the trademark from the user-visible product name and from the wordmark on tile + splash + box-art + poster + hero graphics. The trademarked term now appears only in the description body, where it is used solely to describe compatibility — nominative fair use — with a clearly visible disclaimer.

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
v1.1.0.1 targets x64 only (`<Identity ProcessorArchitecture="x64" />`). We have read Partner Center's recommendation that future Windows on Arm devices will no longer support AArch32 emulation, and that an Arm64 (AArch64) build is the recommended path forward for Arm device customers. An Arm64 build flavor is on our v1.1.1 / v1.2 roadmap and is publicly tracked at docs/store/submission-checklist.md in the repository. For v1.1.0.1, x64 covers our entire targeted audience (Windows 11 on Intel and AMD). We are not asking for a waiver — just confirming we have the recommendation captured and queued, and that customers on Arm devices today can still install via the standard x64-on-Arm emulation path until the native Arm64 flavor ships.

PRIVACY POLICY
Live at: https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

The repository name and Pages baseurl retain the previous "ROROROblox" path because changing them would break external links to documentation, the privacy policy URL, and prior issue references. The product itself — the user-visible application, its name, and its branding — is now exclusively "RORORO."

If anything is unclear or you'd like additional information about the technical approach or the rename, the PROVENANCE.txt file in the repository explains the relationship to the predecessor MultiBloxy implementation, docs/store/rename-plan.md details the v1.1.0.1 rename surfaces, and docs/store/submission-checklist.md details our trademark-disclaimer surfaces.

Thank you for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for resubmission edits)

If this submission is rejected on a different clause than 10.1.1.1, the response should reinforce the defense already in the letter. Don't argue the clause; cite which paragraph addresses it and what changed:

| Clause | Defense paragraph in this letter | If rejected, what to add |
|---|---|---|
| **10.1.1.1** name representation | "CONTEXT — RENAME FROM v1.1.0.0" + "TRADEMARK NOTICE" | If rejected on Identity Name visibility, escalate to a new Partner Center reservation. Reference the listing-side surfaces that were already cleaned. |
| **10.1.4.4.a** trademark / attribution | "TRADEMARK NOTICE" | Reference the visible disclaimer surfaces (About box, README, privacy policy). Offer a screenshot of the in-app About box's trademark line. |
| **10.1.4.4.b** unique lasting value | "UNIQUE LASTING VALUE" | Map each listed feature to the screenshot # in the listing where it's visible. Reviewer needs to see the feature, not just read about it. |
| **10.1.4.4.c** navigation | (not pre-defended; address only if rejected) | Map screenshot # → button click → effect for each top-level surface. Demonstrate every claimed feature is reachable in ≤2 clicks from the main window. |
| **10.10** (security / functionality) | "WHAT IT IS NOT" | Cite that RORORO makes no calls to Roblox-side endpoints other than the documented public ones. Privacy policy lists these verbatim. |

## Pre-submission sanity check

Before clicking **Submit for review** for v1.1.0.1, eyeball:

- [ ] Privacy URL renders cleanly when opened in an incognito browser
- [ ] Trademark disclaimer is in the long description, not just the trademark info field
- [ ] Screenshots cover at least 4 of the 7 features in the UNIQUE LASTING VALUE list
- [ ] **Product Name field shows `RORORO`** (this is the field Microsoft asked us to fix)
- [ ] Age rating answers are consistent with privacy policy (no telemetry, no UGC, etc.)
- [ ] Identity name in `Package.appxmanifest` matches your Partner Center reservation
- [ ] Version in `Package.appxmanifest` is `1.1.0.1`
- [ ] In-app About box wordmark reads `RORORO` (not `ROROROblox`)
- [ ] In-app MainWindow title reads `RORORO`
- [ ] Wide tile and splash graphics show `RORORO` wordmark

## Source

This letter was generated as part of the v1.1.0.1 rename pass. Regenerable from chat history if needed; primary copy lives here.
