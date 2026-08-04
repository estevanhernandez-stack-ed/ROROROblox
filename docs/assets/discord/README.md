# Discord app assets — upload checklist

Everything the Discord developer portal needs for RoRoRo's rich presence, in the shape it needs it.
Portal: <https://discord.com/developers/applications/1501748116985221272>

**Discord derives an asset's KEY from the uploaded filename.** That is the whole reason these copies
exist — the code asks for `active_large`, so the file must be named `active_large.png`. An asset
uploaded as `square310x310logo_scale-400` produces the key `square310x310logo_scale-400`, our request
misses, and Discord falls back to a placeholder. Keys cannot be renamed after saving; delete and
re-upload to change one.

## Upload map

| Portal location | File in this folder | Dimensions |
|---|---|---|
| **Rich Presence → Rich Presence Invite Image → Cover Image** | `cover-1024x576.png` | 1024×576 (16:9) |
| **Rich Presence → Rich Presence Assets** | `active_large.png` | 1240×1240 |
| **Rich Presence → Rich Presence Assets** | `idle_large.png` | 1240×1240 |
| **Rich Presence → Rich Presence Assets** | `active_small.png` | 600×600 |
| **Rich Presence → Rich Presence Assets** | `idle_small.png` | 600×600 |
| **General Information → App Icon** | `active_large.png` | 1240×1240 |

## What the code actually requests

`DiscordPresenceService` sends `LargeImageKey: "active_large"` with hover text `"RoRoRo"`. That is the
only key referenced today. `idle_*` and `active_small` are uploaded ahead of need so a future
idle/active visual distinction requires no code change — the slot keys are stable.

## Two things that bit us

**Discord raised its minimum to 512×512.** The earlier brief specified `Square44x44Logo.targetsize-256.png`
for the small slots; the portal now rejects it. `Square150x150Logo.scale-400.png` (600×600) is the
smallest shipped logo that clears the floor, and it is what `active_small` / `idle_small` are copied from.

**The square assets carry the old wordmark.** `Square310x310Logo.*` has "RORORO" set beneath the cube —
it predates the decision that the user-facing brand is **RoRoRo** and that ROROROblox is a repo and code
identifier only. Those files are Microsoft Store approved and are deliberately left untouched here, so
Discord will render the correct app name beside artwork spelling it the old way. `cover-1024x576.png` is
newly drawn and spells it correctly. Producing corrected square variants for Discord only — leaving the
Store assets alone — is a small pass whenever it is wanted.

## Sources

- `cover-1024x576.png` — drawn 2026-08-03 through the `626labs:design` skill. Navy `#0f1f31` field,
  cyan `#17d4fa` + magenta `#f22f89` paired, Space Grotesk display, JetBrains Mono meta at +0.12em
  uppercase, tagline *Imagine Something Else.*
- `active_large.png`, `idle_large.png` — copies of `src/ROROROblox.App/Package/Logos/Square310x310Logo.scale-400.png`
- `active_small.png`, `idle_small.png` — copies of `src/ROROROblox.App/Package/Logos/Square150x150Logo.scale-400.png`

Supersedes the asset brief on the unmerged `feat/discord-clan-coordination` branch, whose 256×256
recommendation no longer passes and whose slot names were never wired to code.
