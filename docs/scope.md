# ROROROblox — Scope (pointer stub)

This is a Spec-first Cart cycle (pattern mm from cycle #13's reflection). The substantive scope decisions live in the upstream design spec authored before /onboard:

→ [docs/superpowers/specs/2026-05-03-rororoblox-design.md](superpowers/specs/2026-05-03-rororoblox-design.md)

## In scope (v1.1)

- One-click multi-instance Roblox via tray toggle
- Saved Roblox accounts with per-account quick-launch
- "Common Windows user" install + first-run UX (no DevTools, no registry edits)
- Microsoft Store distribution as primary path; self-signed MSIX sideload as fallback

## Out of scope (deferred or never)

- Macros / input automation (lives in **MaCro**, separate macOS product — explicitly walled off)
- Setup sharing or clan tooling (clan members share screenshots today, not files)
- Cross-machine cookie sync (DPAPI is per-machine by design)
- Auto-tile windows (deferred to v1.2)
- Live "Account is running" indicator (deferred to v1.2)
- Per-cookie encryption + per-account WebView2 profiles (v1.2)
- E2E automation against real roblox.com (manual smoke gate is the v1 trade)

## Distribution audience

Pet Sim 99 clan first (non-technical Windows users running multi-alt for farming), Microsoft Store second. UX bar is "common Windows user" — assume zero comfort with DevTools, registry, command-line, or unsigned-binary install warnings.
