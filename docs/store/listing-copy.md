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
| **App name (Product Name)** | RORORO | The fix for 10.1.1.1. Drops `blox` entirely; keeps the stutter pattern that ties to the three-block voxel icon. **Open question (2026-08-22):** every other user-facing surface now spells it **RoRoRo** (About box, README, release names, this file's description body). If the reserved Product Name still displays all-caps, changing its casing is a listing-side edit worth making at a convenient submission — casing-only, so 10.1.1.1 is not re-opened. Este's call. |
| **Publisher display name** | 626Labs LLC | Matches Partner Center reservation. |
| **Copyright** | © 2026 626 Labs LLC. "Roblox" is a trademark of Roblox Corporation. RORORO is not affiliated with, endorsed by, or sponsored by Roblox Corporation. | Disclaimer in copyright field. |
| **Category** | Utilities & tools | Not "Games" — RORORO is a launcher, not a game. |
| **Sub-category** | (none — Partner Center doesn't show one for Utilities & tools) | |

## Short description (under 200 chars — Store snippet)

> Refreshed 2026-08-22 for the v1.22.0.0 submission — the previous block predated the memory
> watchdog, presence status, themes, and the tools window.

```
Multi-launcher for Windows. Run several Roblox clients side by side as different saved accounts. Encrypted account vault, Squad Launch, memory watchdog, live status, themes, auto-update.
```

## Long description

> Refreshed 2026-08-22 (v1.22.0.0). The old block described the v1.4-era app. Brand casing
> updated RORORO → RoRoRo to match the About box, README, and release names; the Partner
> Center *Product Name field* is a separate question — see the note under Identity.

```
Multi-launcher for Windows.

RoRoRo is a Windows launcher that lets you run several Roblox clients on one PC at the same time, each signed in as a different saved account you own. Add accounts once through Roblox's own login page, then launch any of them with one click — into their default game, a saved private server, or any game link you paste.

What you get:
• Multi-instance with one click. Holds the Roblox singleton mutex so additional clients open instead of focusing the first one.
• DPAPI-encrypted account vault. Saved cookies are encrypted with Windows' Data Protection API, tied to your Windows user account — a copy of the vault file moved to another PC will not decrypt. Moving accounts between your own PCs is a deliberate, passphrase-protected export.
• Live status for every account. See which account is in which game, who is idle and for how long, and per-account frame-rate caps that stick.
• Squad Launch + Friend Follow. Send every selected account into the same private server, follow a friend into theirs, or land your accounts together in one public server.
• Memory watchdog + Recycle. RoRoRo learns what a Roblox client actually costs in RAM on your machine and warns before you run out. One click closes a heavy client and puts it back in the same server it was in.
• One tools window. Games, Settings, History, Diagnostics, Plugins and About are pages of one window that sits beside your accounts. Keyboard shortcuts throughout — F1 shows the list.
• Themes. Four built-in, including one that carries no meaning in colour alone, plus a builder to make your own from ten colours and share it as a file.
• Optional Discord alerts. Only if you paste in a webhook you created — a fresh install makes no Discord calls at all.
• System tray UX. A state-coloured tray icon shows the multi-instance state at a glance; double-click launches your main account.
• Plugin system. Optional plugins run as separate processes and hold no permissions until you grant each one by name.
• Auto-update via Velopack. Remote config tracks the current known-good Roblox version and mutex name, so a Roblox-side rename doesn't break you for long.
• Accessible by measurement. Every control announces its name to assistive tech, and contrast is verified against rendered pixels in every theme.

Privacy & security:
Your Roblox password is never seen by RoRoRo. Login happens entirely inside Roblox's own page, embedded in a Microsoft Edge WebView2 frame — same HTML, same HTTPS connection your browser would make. RoRoRo captures only the session cookie that Roblox sets after successful login, and encrypts it before writing it to disk. No telemetry. No analytics. Nothing leaves your machine except the Roblox-side calls during launch — the same calls Roblox.com makes from your browser — and, only if you set one up yourself, alerts to your own Discord webhook.

Important: trademark and affiliation notice.
"Roblox" and the Roblox logo are trademarks of Roblox Corporation. RoRoRo is an independent third-party tool, not affiliated with, endorsed by, or sponsored by Roblox Corporation. The trademarked term is used solely to describe compatibility with the Roblox platform. RoRoRo launches the official Roblox client unmodified — it does not inject into, hook into, or alter the Roblox process in any way; it only holds a Windows named-mutex before launch so that subsequent client instances see the singleton check as already-claimed.

A 626 Labs product.
```

## Product features (paste each as one feature; up to 7)

> Refreshed 2026-08-22 (v1.22.0.0) — and trimmed to the stated cap of 7; the old list had 8.

```
One-click multi-instance launcher for Roblox on Windows
DPAPI-encrypted account vault with passphrase-protected export
Squad Launch, Friend Follow, and same-server Recycle rejoin
Memory watchdog that learns each client's real RAM cost on your PC
Live presence status — which account is in which game, idle times
Four built-in themes plus a build-your-own theme editor
Plugin system with per-capability consent and out-of-process isolation
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

## What's new in this version

> **This section moved.** Since v1.21 the paste-ready What's-new block lives in a per-version
> file, `docs/store/whats-new-<version>.md`, written next to that release's reviewer letter so
> the two are drafted against the same facts. Current:
> [`whats-new-1.22.0.0.md`](whats-new-1.22.0.0.md). Previous:
> [`whats-new-1.21.0.0.md`](whats-new-1.21.0.0.md). The v1.14 and v1.12 blocks that used to sit
> here are preserved in git history and in their `release-notes-*.md` files.
>
> House rules that carry across versions: Store field only; do not mention the plugin
> marketplace or contract versions; write for the customer's vocabulary ("the server you were
> in," never "instance"); do not describe defect mechanics in a public field.

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
| Memory watchdog + Recycle (v1.12+, learns per-machine v1.22) | Retention | Solves the reported "alts close on their own" failure; keeps long sessions alive. |
| Presence status + idle times (v1.5+) | Engagement | The main window is a live dashboard, not just a launcher list. |
| Themes incl. builder (v1.17+) | Engagement | Personalization + a shareable artifact (theme files travel between users). |
| One tools window + keyboard shortcuts (v1.22) | Engagement | Daily-driver ergonomics; the app stays open beside the game. |

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
