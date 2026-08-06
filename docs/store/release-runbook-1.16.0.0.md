# RoRoRo v1.16.0.0 — release runbook

**Shape of this release:** no new features. Six waves of a UI campaign, one
security fix, and one new test suite. Everything a user sees got measured
against WCAG and against the app's own conventions, and where the two
disagreed, the app moved.

That framing matters for the Store letter: v1.15 was "Discord alerting."
v1.16 is "the app stops contradicting itself."

## What ships

| area | change |
|---|---|
| **Accessibility** | Every interactive control's boundary now clears WCAG 1.4.11's 3:1 under any theme, including ones users wrote themselves. It shipped below that floor in the **default** theme — 1.26:1 — for every release up to and including v1.15. |
| **Security** | The saved Discord webhook is masked in Settings. A webhook URL is a bearer credential; it was rendered at full contrast on a screen this app's own streamer mode exists to keep clean (F-076). |
| **Navigation** | Settings became a five-page nav rail. Six main-window buttons became Settings + a Tools menu. Streamer mode and Reroll identity moved off the dashboard into Settings, where they belong. |
| **Page chrome** | One window-title rule, enforced by a test. Twelve hand-rolled page headers became one shared control. One window had been shipping the repo name to users in a runtime-assembled title. |
| **Component vocabulary** | Buttons, text inputs, cards and section headings each have one named style instead of 60+ hand-copied attribute sets — including two buttons and one heading that were built in C# where no markup review would ever have found them. |
| **Theme consent** | If the accessibility fix would change a theme the *user* authored, RoRoRo asks once, per theme, with the before/after on screen. Built-in themes are fixed silently, because that defect is ours. |

## The one thing a reviewer might ask about

**"Why does an update change how my custom theme looks?"**

It only does so with permission. The contrast fix derives a brighter
boundary from the theme's own colours; on a theme the user wrote, RoRoRo
shows a dialog before keeping it, and declining renders the theme exactly as
authored. Separators, card edges and row rules are never touched — WCAG
1.4.11 governs component boundaries, not decoration, and the split is
enforced by a test.

No new capability, no new permission, no manifest change beyond the version.

## Pre-tag checklist

- [x] `Package.appxmanifest` and `ROROROblox.App.csproj` both at `1.16.0.0`
- [x] Full suite green (1325 unit + 18 integration)
- [x] `runFullTrust` only — no capability added this cycle
- [ ] Tag `v1.16.0.0` pushed → `release.yml` builds Velopack and opens a DRAFT release
- [ ] Review the draft, then publish
- [ ] Store MSIX via `scripts/finalize-store-build.ps1`
- [ ] Partner Center submission (Este)

## Not in this release

- **F-026** — grouping still vanishes under a theme that sets `RowBg == Bg`.
  Cards are a fill and a radius; the structural fix is a heading level above
  the card and it is tracked as its own piece of work, not smuggled into a
  re-style.
- **F-034** — the repo name `ROROROblox` still appears in the tray tooltip,
  the tray menu, and the support-bundle header.
- 54 open findings remain in the campaign register. 25 are closed.
