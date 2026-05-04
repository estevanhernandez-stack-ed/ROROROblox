# Microsoft Store listing copy — ROROROblox

> **Stake:** Sanduhr für Claude was rejected twice on policy 10.1.4.4 before passing. Our exposure is materially worse — we're using *Roblox* trademarks, not just *Anthropic*. Every surface below is engineered to thread that needle.

## Pre-submission checklist (do this BEFORE Partner Center)

- [ ] Reserve publisher name in Partner Center (do NOT submit unsigned builds before this lands — Identity name in `Package.appxmanifest` must match what Partner Center reserves)
- [ ] Build sideload MSIX locally and `Add-AppxPackage` to verify it installs + runs (see `scripts/install-local-msix.ps1`)
- [ ] Privacy policy URL is live and crawlable (host the rendered `docs/PRIVACY.md`; bare GitHub raw counts but a domain is better)
- [ ] Screenshots captured per `docs/store/screenshots-checklist.md`
- [ ] Trademark disclaimer present in: Store description, Store copyright field, MSIX `Description`, About box, README, privacy policy

## Identity

| Field | Value | Notes |
|---|---|---|
| **App name** | ROROROblox | Wordmark casing — preserve `ROROROblox`, not `RorOrOblox`. |
| **Publisher display name** | 626 Labs LLC *(or Estevan Hernandez until LLC paperwork lands)* | Must match Partner Center reservation. |
| **Copyright** | © 626 Labs LLC. "Roblox" is a trademark of Roblox Corporation. ROROROblox is not affiliated with, endorsed by, or sponsored by Roblox Corporation. | Disclaimer in copyright field per Sanduhr playbook 10.1.4.4.a guidance. |
| **Category** | Utilities & tools | Not "Games" — we're a launcher, not a game. |
| **Sub-category** | Productivity *(or Personalization if Productivity isn't an option)* | |

## Short description (under 200 chars — Store snippet)

> Run multiple Roblox clients side by side as different saved accounts. DPAPI-encrypted account vault, per-game launch routing, system tray controls, auto-update.

*Trademark disclaimer doesn't fit here — lives in the long description and copyright field.*

## Long description

> **Multi-Roblox Instant Generator.**
>
> ROROROblox is a Windows launcher that lets you run several Roblox clients on one PC simultaneously, each signed in as a different saved account. You add accounts once via an embedded login window, then launch any of them with a single click — into a default Roblox game URL or a per-account custom one.
>
> **What you get**
>
> - **Multi-instance with one click.** Holds the Roblox singleton mutex so additional clients open instead of focusing the first one.
> - **DPAPI-encrypted account vault.** Saved cookies are encrypted with Windows' Data Protection API, tied to your Windows user account. A copy of the vault file moved to another PC will not decrypt.
> - **Per-game launch routing.** Set a default Roblox game URL, or pick a different one per saved account. Each *Launch As* lands in the game you specified.
> - **System tray UX.** State-coloured tray icon (cyan = on, slate = off, magenta = error) shows the multi-instance state at a glance. Double-click launches your designated main account.
> - **Squad Launch + Friend Follow.** Launch every selected account into the same Roblox private server, or follow a friend into theirs.
> - **Join by link.** Paste any `roblox.com/games/...` URL into a saved account's row to launch that account into that specific game.
> - **Auto-update via Velopack.** Drift-compatible with Roblox-side changes; remote config tells the app the current known-good Roblox version + mutex name so a Roblox-side rename doesn't break you for long.
> - **Diagnostics + structured logging.** A bug-report bundle is one button away.
>
> **Privacy & security**
>
> - Your Roblox password is **never** seen by ROROROblox. Login happens entirely inside Roblox's own page, embedded in a Microsoft Edge WebView2 frame — same HTML, same HTTPS connection your browser would make. We capture only the session cookie that Roblox sets after successful login, and we encrypt it before writing it to disk.
> - No telemetry. No analytics. No data leaves your machine except the Roblox-side calls during launch — the same calls Roblox.com makes from your browser.
> - The embedded browser cache is wiped before every Add Account, so the next login starts on a fresh page.
>
> **Important: trademark and affiliation notice**
>
> "Roblox" and the Roblox logo are trademarks of Roblox Corporation. ROROROblox is an independent third-party tool, **not affiliated with, endorsed by, or sponsored by Roblox Corporation**. The trademarked term is used solely to describe compatibility with the Roblox platform. ROROROblox launches the official Roblox client unmodified — it does not inject into, hook into, or alter the Roblox process in any way; it only holds a Windows named-mutex before launch so that subsequent client instances see the singleton check as already-claimed.
>
> Roblox/Hyperion has stated that multi-instancing "may be considered malicious behavior." Risk of an account ban appears low because we don't modify the Roblox client, but it is non-zero. Use ROROROblox on accounts you can afford to lose.
>
> A 626 Labs product.

## Keywords

(Partner Center allows ~7 keywords. Order by intent: most common first.)

```
roblox, multi instance, multi-account, launcher, account manager, alt accounts, multibox
```

> Avoid "cheat", "exploit", "bypass" — those will trigger reviewer concerns even if irrelevant.

## What's new (release notes)

Leave version-specific text **unset until the technical fixes land**. Template:

```
v<X.Y.Z>:
• <Headline change>
• <Headline change>
• <Headline change>
Bug fixes and stability improvements.
```

## Multi-feature value justification (10.1.4.4.b — DO NOT SKIP)

Sanduhr's first rejection was 10.1.4.4.b ("unique lasting value"). A reviewer reading our description must see **multiple** features spanning discovery, engagement, and retention — not one trick. The list above is engineered around that:

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

If a reviewer rejects on 10.1.4.4.b, the response (per Sanduhr playbook): quote the feature table, point to the in-product surfaces (screenshots), don't restructure — just demonstrate the surfaces are real.

## Response protocol if rejected

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
- Sanduhr Store playbook (source): `docs/ms-store-submission-playbook.md` in `Sanduhr_f-r_Claude` repo
