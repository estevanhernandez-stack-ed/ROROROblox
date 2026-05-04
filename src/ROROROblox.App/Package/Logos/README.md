# Package/Logos/ — Store assets

This directory holds the icons, tiles, and splash referenced by `Package.appxmanifest`. **Real assets must be produced before any MSIX submission** — programmatic placeholders are disqualifying per [pattern (x) from the SnipSnap retro](../../../../CLAUDE.md).

## Required files

The build will fail fast if any of these are missing or are flagged as placeholders.

| File | Size | Purpose |
|---|---|---|
| `StoreLogo.png` | 50×50 | Listing logo on the Store page |
| `Square150x150Logo.png` | 150×150 | Medium tile + Start menu |
| `Square44x44Logo.png` | 44×44 | Taskbar + alt+tab |
| `Wide310x150Logo.png` | 310×150 | Wide tile |
| `Square71x71Logo.png` | 71×71 | Small tile (optional but recommended) |
| `Square310x310Logo.png` | 310×310 | Large tile (optional but recommended) |
| `SplashScreen.png` | 620×300 | First-paint splash |

Each logo should also have scaled variants (`.scale-100`, `.scale-125`, `.scale-150`, `.scale-200`, `.scale-400`) for HiDPI Windows. The Microsoft Store toolchain auto-generates these from the source if you supply a high-resolution master, but you can also produce them manually.

## How to produce them

Use the **626labs-design skill** at `~/.claude/skills/626labs-design/`:

> Run the 626labs-design skill to produce ROROROblox Store assets. Brand tokens: cyan `#17d4fa` + magenta `#f22f89` paired, navy `#0f1f31` field. Tagline: *Imagine Something Else.* Visual reference: the in-app MainWindow header. Sizes: as listed in `src/ROROROblox.App/Package/Logos/README.md`. Output `.png` (24-bit, no alpha) for the Store-bound logos; `.png` with alpha for the in-app tray icons (separate from these).

The design skill outputs go directly into this folder. After the skill produces the files, run `scripts/build-msix.ps1 -Verify` to confirm the placeholder check passes.

## Why empty by default

Programmatic placeholders are disqualifying for the "won't ship a broken-looking tile even if the rest works" bar (pattern x from SnipSnap retro). The build script's logo-presence check is the gate that keeps us honest — if you bypass it via `-AllowPlaceholders` you accept a Store rejection or a bad clan-distribution moment.
