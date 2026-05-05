# Notes for certification — reviewer letter

> Paste this into Partner Center → your app → **Submission options** → **Notes for certification**. Engineered around the two most-likely-to-bite clauses for ROROROblox: 10.1.4.4.a (trademark / nominative use) and 10.1.4.4.b (unique lasting value). Tone is collaborative engineering peer, not defensive.

---

```
Hello reviewer,

Thank you for taking the time to certify ROROROblox. A short orientation that should make this submission faster to evaluate:

WHAT THE APP IS
ROROROblox is a Windows launcher for the Roblox platform. It lets a user save multiple Roblox account credentials and launch any of them — including several at once, in separate client windows — without having to log out and back in between accounts. It is a productivity utility for Roblox players who maintain more than one account.

WHAT IT IS NOT
ROROROblox does not modify the Roblox client. It does not inject into, hook into, attach a debugger to, or read memory from the Roblox process. It does not bundle game content, scripts, plugins, exploits, or automation. It does not include chat, multiplayer networking, user-generated content, in-app purchases, or advertising.

HOW IT WORKS (TECHNICAL)
ROROROblox does two documented things:
1. It holds the Windows named mutex Local\ROBLOX_singletonEvent before launching the official Roblox client. This causes subsequent Roblox launches to spawn new processes instead of focusing the first one. The technique is standard and has been used by similar tools (e.g., MultiBloxy, Bloxstrap) for years.
2. It uses Roblox's documented authentication-ticket flow — the cookie → CSRF → /v1/authentication-ticket → roblox-player: URI sequence — to launch a Roblox client signed in as a specific saved account.

Roblox session cookies are stored locally only, encrypted with the Windows Data Protection API (DPAPI) per Windows user. They never leave the machine. There is no backend, no telemetry, no analytics, no third-party SDKs, and no data collection.

TRADEMARK NOTICE (10.1.4.4.a)
"Roblox" and the Roblox logo are trademarks of Roblox Corporation. ROROROblox is an independent third-party tool, not affiliated with, endorsed by, or sponsored by Roblox Corporation. The trademarked term is used solely to describe compatibility with the Roblox platform — nominative fair use. The disclaimer appears in: the Store description, the Copyright field, the Trademark info field, the in-app About box, the README, and the privacy policy.

UNIQUE LASTING VALUE (10.1.4.4.b)
Multiple cooperating features, not a single trick:
- Multi-instance via mutex hold
- DPAPI-encrypted account vault with WebView2-isolated login capture
- Per-account game launch routing
- System tray with state colours and double-click main launch
- Squad Launch + Friend Follow + Join-by-link surfaces
- Velopack auto-update with remote roblox-compat config
- Diagnostics bundle for bug reports

PLATFORM SUPPORT (Arm64 acknowledgment)
This v1.1 submission targets x64 only (`<Identity ProcessorArchitecture="x64" />`). We have read Partner Center's recommendation that future Windows on Arm devices will no longer support AArch32 emulation, and that an Arm64 (AArch64) build is the recommended path forward for Arm device customers. An Arm64 build flavor is on our v1.1.1 / v1.2 roadmap and is publicly tracked at docs/store/submission-checklist.md in the repository. For v1.1, x64 covers our entire targeted audience (Windows 11 on Intel and AMD). We are not asking for a waiver — just confirming we have the recommendation captured and queued, and that customers on Arm devices today can still install via the standard x64-on-Arm emulation path until the native Arm64 flavor ships.

PRIVACY POLICY
Live at: https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/

Source code is open under the MIT License at:
https://github.com/estevanhernandez-stack-ed/ROROROblox

If anything is unclear or you'd like additional information about the technical approach, the PROVENANCE.txt file in the repository explains the relationship to the predecessor MultiBloxy implementation, and docs/store/submission-checklist.md details our trademark-disclaimer surfaces.

Thank you for your time and consideration.

Estevan Hernandez
626 Labs LLC
```

---

## Defenses by clause (cheat sheet for resubmission edits)

If the cert reviewer rejects on a specific clause, the response should reinforce the defense already in the letter. Don't argue the clause; cite which paragraph addresses it and what changed:

| Clause | Defense paragraph in this letter | If rejected, what to add |
|---|---|---|
| **10.1.4.4.a** trademark / attribution | "TRADEMARK NOTICE" | Reference the visible disclaimer surfaces (About box, README, privacy policy). Offer a screenshot of the in-app About box's trademark line. |
| **10.1.4.4.b** unique lasting value | "UNIQUE LASTING VALUE" | Map each listed feature to the screenshot # in the listing where it's visible. Reviewer needs to see the feature, not just read about it. |
| **10.1.4.4.c** navigation | (not pre-defended; address only if rejected) | Map screenshot # → button click → effect for each top-level surface. Demonstrate every claimed feature is reachable in ≤2 clicks from the main window. |
| **10.10** (security / functionality) | "WHAT IT IS NOT" | Cite that ROROROblox makes no calls to Roblox-side endpoints other than the documented public ones (`auth.roblox.com`, `users.roblox.com`, `thumbnails.roblox.com`). Privacy policy already lists these verbatim. |

## Pre-submission sanity check

Before clicking **Submit for review**, eyeball:

- [ ] Privacy URL renders cleanly when opened in an incognito browser (the URL above)
- [ ] Trademark disclaimer is in the long description, not just the trademark info field
- [ ] Screenshots cover at least 4 of the 7 features in the UNIQUE LASTING VALUE list
- [ ] Age rating answers are consistent with privacy policy (no telemetry, no UGC, etc.)
- [ ] Identity name in `Package.appxmanifest` matches your Partner Center reservation
- [ ] Version in `Package.appxmanifest` matches the version you're submitting under

## Source

This letter was generated as part of the v1.1 Store-prep pass. Regenerable from chat history if needed; primary copy lives here.
