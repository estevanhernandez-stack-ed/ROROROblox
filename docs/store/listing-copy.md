# Microsoft Store listing copy — RORORO

> **Stake:** v1.1.0.0 was rejected under clause **10.1.1.1 Inaccurate Representation** for containing the name of another product (Roblox) in the Product Name field. The fix was to rename the product to **RORORO** (drops the `blox` suffix entirely; keeps the stutter that ties to the icon and the brand DNA). v1.1.2.0 ships with the new name across every user-visible surface; nominative use of "Roblox" in the description body is permitted under fair-use precedent and is clearly disclaimed.

## Pre-submission checklist (do this BEFORE Partner Center)

- [ ] Confirm Partner Center reservation still uses `626LabsLLC.RoRoRoBlox` as the Identity Name (Microsoft asked for a Listing-side fix; not a re-reservation)
- [ ] Build sideload MSIX locally and `Add-AppxPackage` to verify it installs + runs (see `scripts/install-local-msix.ps1`)
- [ ] Privacy policy URL is live and crawlable: `https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/`
- [ ] Screenshots captured per `docs/store/screenshots-checklist.md`
- [ ] Trademark disclaimer present in: Store description, Store copyright field, MSIX `Description`, About box, README, privacy policy

## Identity

| Field | Value | Notes |
|---|---|---|
| **App name (Product Name)** | RORORO | The fix for 10.1.1.1. Drops `blox` entirely; keeps the stutter pattern that ties to the three-block voxel icon. |
| **Publisher display name** | 626Labs LLC | Matches Partner Center reservation. |
| **Copyright** | © 2026 626 Labs LLC. "Roblox" is a trademark of Roblox Corporation. RORORO is not affiliated with, endorsed by, or sponsored by Roblox Corporation. | Disclaimer in copyright field. |
| **Category** | Utilities & tools | Not "Games" — RORORO is a launcher, not a game. |
| **Sub-category** | (none — Partner Center doesn't show one for Utilities & tools) | |

## Short description (under 200 chars — Store snippet)

```
Multi-launcher for Windows. Run multiple Roblox clients side by side as different saved accounts. DPAPI-encrypted account vault, per-game launch routing, system tray controls, auto-update.
```

## Long description

```
Multi-launcher for Windows.

RORORO is a Windows launcher that lets you run several Roblox clients on one PC simultaneously, each signed in as a different saved account. You add accounts once via an embedded login window, then launch any of them with a single click — into a default Roblox game URL or a per-account custom one.

What you get:
• Multi-instance with one click. Holds the Roblox singleton mutex so additional clients open instead of focusing the first one.
• DPAPI-encrypted account vault. Saved cookies are encrypted with Windows' Data Protection API, tied to your Windows user account. A copy of the vault file moved to another PC will not decrypt.
• Per-game launch routing. Set a default Roblox game URL, or pick a different one per saved account.
• System tray UX. State-coloured tray icon (cyan = on, slate = off, magenta = error) shows the multi-instance state at a glance. Double-click launches your designated main account.
• Squad Launch + Friend Follow. Launch every selected account into the same Roblox private server, or follow a friend into theirs.
• Join by link. Paste any roblox.com/games URL into a saved account's row to launch that account into that specific game.
• Auto-update via Velopack. Drift-compatible with Roblox-side changes; remote config tells the app the current known-good Roblox version + mutex name so a Roblox-side rename doesn't break you for long.
• Diagnostics + structured logging. A bug-report bundle is one button away.

Privacy & security:
Your Roblox password is never seen by RORORO. Login happens entirely inside Roblox's own page, embedded in a Microsoft Edge WebView2 frame — same HTML, same HTTPS connection your browser would make. RORORO captures only the session cookie that Roblox sets after successful login, and encrypts it before writing it to disk. No telemetry. No analytics. No data leaves your machine except the Roblox-side calls during launch — the same calls Roblox.com makes from your browser.

Important: trademark and affiliation notice.
"Roblox" and the Roblox logo are trademarks of Roblox Corporation. RORORO is an independent third-party tool, not affiliated with, endorsed by, or sponsored by Roblox Corporation. The trademarked term is used solely to describe compatibility with the Roblox platform. RORORO launches the official Roblox client unmodified — it does not inject into, hook into, or alter the Roblox process in any way; it only holds a Windows named-mutex before launch so that subsequent client instances see the singleton check as already-claimed.

A 626 Labs product.
```

## Product features (paste each as one feature; up to 7)

```
One-click multi-instance launcher for Roblox on Windows
DPAPI-encrypted vault for saved Roblox accounts (per-Windows-user)
Per-account game launch routing — default URL or custom per account
System tray with state-coloured icon and double-click main launch
Squad Launch and Friend Follow into private servers
Join-by-link from any roblox.com URL
Plugin system with per-capability consent and out-of-process isolation (v1.4+)
Auto-update via Velopack with Roblox-compat drift detection
```

## Copyright (single line)

```
© 2026 626 Labs LLC. All rights reserved.
```

## Trademark info

```
"Roblox" and the Roblox logo are trademarks of Roblox Corporation. RORORO is an independent third-party tool, not affiliated with, endorsed by, or sponsored by Roblox Corporation. The trademarked term is used solely to describe compatibility with the Roblox platform. RORORO launches the official Roblox client unmodified.
```

## Additional license terms

```
RORORO source code is licensed under the MIT License. Full text: https://github.com/estevanhernandez-stack-ed/ROROROblox/blob/main/LICENSE

RORORO is provided "as is," without warranty of any kind. Use of RORORO to access the Roblox platform is governed by Roblox Corporation's own Terms of Use, which you must accept separately when you sign in to a Roblox account. Roblox Corporation has stated that multi-instancing tools "may be considered malicious behaviour"; while RORORO does not modify the Roblox client and only holds a Windows named-mutex before launch, the user accepts any risk of Roblox-side enforcement on their accounts.

No warranty is offered or implied for compatibility with future versions of the Roblox client. The bundled remote-config update mechanism is best-effort.
```

## Developed by

```
626 Labs LLC
```

## Keywords

(Partner Center allows ~7 keywords. Order by intent: most common first.)

```
roblox, multi instance, multi-account, launcher, account manager, alt accounts, multibox
```

> Avoid "cheat", "exploit", "bypass" — those will trigger reviewer concerns even if irrelevant.

## What's new (release notes for v1.1.2.0)

```
v1.1.2.0:
• Renamed product from ROROROblox to RORORO per Microsoft Store listing guidance (clause 10.1.1.1).
• Updated tagline: Multi-launcher for Windows.
• Functionality unchanged from v1.1.0.0.
```

## Multi-feature value justification (10.1.4.4.b — DO NOT SKIP)

A reviewer reading our description must see **multiple** features spanning discovery, engagement, and retention — not one trick. The list above is engineered around that:

| Feature | Maps to | Why it counts |
|---|---|---|
| Multi-instance via mutex hold | Discovery + retention | The reason most users find us. |
| DPAPI-encrypted account vault | Engagement + trust | Daily-use feature; trust signal that justifies Store distribution over a random GitHub binary. |
| Per-game launch routing | Engagement | Personalization that locks users into the workflow. |
| System tray UX with state colours | Engagement + retention | Glanceable status — users keep the tray icon visible. |
| Squad Launch + Friend Follow | Engagement | Social use case beyond pure utility. |
| Join by link | Engagement | Reduces switch-cost from any Roblox URL to a launch action. |
| Velopack auto-update + remote config | Retention + reliability | Drift-resistance is a feature; users who installed v1.0 keep working when Roblox renames its mutex. |
| Diagnostics bundle | Retention | Bug-report ergonomics — users stay through a Roblox-side break. |

## Response protocol if rejected (post-rename)

Per Sanduhr playbook:

1. Quote the specific clause number from reviewer feedback in the Notes-to-Publisher response.
2. Identify the root cause, not the symptom. Don't argue the symptom.
3. Add a regression test for that bug class if it's code-side.
4. Increment the version (`Identity Version` in `Package.appxmanifest`) — every resubmission gets a new version.
5. Frame the response as collaborative engineering, not pushback.

## References

- [`docs/PRIVACY.md`](../PRIVACY.md) — privacy policy (host this URL publicly before submission)
- [`docs/store/age-rating.md`](age-rating.md) — age-rating questionnaire answers
- [`docs/store/screenshots-checklist.md`](screenshots-checklist.md) — screenshots to capture
- [`docs/store/submission-checklist.md`](submission-checklist.md) — pre-flight + post-flight procedure
- [`docs/store/reviewer-letter.md`](reviewer-letter.md) — Notes-for-certification letter (post-rename version with rename context)
- [`docs/store/rename-plan.md`](rename-plan.md) — comprehensive rename plan (executed in v1.1.2.0)
