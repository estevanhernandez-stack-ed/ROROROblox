# Discord asset brief — v1.2 clan-coordination

**Status:** active brief · pending production
**Owner:** Este (offline production via `626labs-design` skill or design tooling)
**Spec ref:** [`docs/superpowers/specs/2026-05-06-discord-clan-coordination-design.md`](superpowers/specs/2026-05-06-discord-clan-coordination-design.md) §9.2 + §9.3
**Why this doc exists:** v1.2 introduces five public-facing brand surfaces in Discord. Each one is reviewed by both Pet Sim 99 clan eyes and Microsoft Store reviewers. Pattern (x) from the SnipSnap retro applies: "won't ship a broken-looking tile even if the rest works." This brief locks the sizes, palette, composition, and disposition so production work hits the bar in one pass.

---

## Brand tokens (load-bearing)

Source of truth: `~/.claude/skills/626labs-design/colors_and_type.css`. Discord-asset subset:

| Token | Hex | Usage |
|---|---|---|
| Cyan | `#17D4FA` | Primary accent. Idle-state fields, idle ring. Discord color int = `1561082`. |
| Magenta | `#F22F89` | Secondary accent. Active-state ring, active dot. Always paired with cyan. |
| Navy field | `#0F1F31` | Background plate for all five assets. Deep enough that Discord light-mode users still get contrast. |
| Navy deep | `#091023` | Outer-frame shadow / depth gradient base. |
| White | `#F5F7FA` | Mark fill on dark backgrounds. Never pure `#FFFFFF`. |

Type (only used on the optional 626 Labs wordmark variant — primary marks stay icon-only):
- Display: **Space Grotesk** SemiBold 700
- Mono labels: **JetBrains Mono** Medium 500, **uppercase**, **+0.12em letter-spacing** if any text appears

**Hard rule:** never ship a programmatic placeholder. The Discord developer portal asset slots + GitHub Pages avatar are public-facing surfaces; reviewers and clanmates both see them.

---

## Asset 1 — `idle_large` (1024×1024)

**Where it lives:** Discord developer portal → Rich Presence → Art Assets → key `idle_large`.
**When it shows:** ROROROblox is running but no accounts launched (Idle state). Renders as the **large image** on the user's profile rich-presence card.

**Composition:**
- Square 1024×1024 PNG, 24-bit color, no transparency required (Discord clips to rounded square).
- Solid navy field `#0F1F31`. Optional subtle radial gradient toward `#091023` at the corners (≤8% intensity) for depth.
- Centered RORORO mark — the iso-voxel stack motif from the tray icons (Direction C). Render at ~512×512 px centered (50% of canvas).
- Mark fill: cyan `#17D4FA`, no magenta on the idle variant.
- 1px cyan inner-border 24px from the canvas edge OR a soft 32px cyan glow ring (pick one — not both). This is the visual cue that distinguishes "ROROROblox open" from "ROROROblox actively multi-clienting" (`active_large`).

**Reference:** `~/.claude/skills/626labs-design/assets/brand/icon-transparent-1024.png` shows the 626 Labs mark; the RORORO mark is its own thing (voxel stack on navy disk) — derive from `src/ROROROblox.App/Tray/Resources/tray-on.ico` for shape language.

---

## Asset 2 — `active_large` (1024×1024)

**Where it lives:** Discord developer portal → Rich Presence → Art Assets → key `active_large`.
**When it shows:** One or more accounts active, OR in a private server. Same large slot as `idle_large`; swapped per state.

**Composition:** identical to `idle_large` EXCEPT:
- Replace the cyan idle ring with a **magenta `#F22F89` accent ring** of the same width.
- Optional: thin cyan→magenta gradient on the ring (240° rotation, cyan top-left, magenta bottom-right) to signal "active + branded."
- The mark itself stays cyan-filled. Magenta is a state signal, not a re-color of the mark.

**Why the swap matters:** Discord shows both assets on different presence states. A clanmate seeing your card glance-distinguishes "alts running" from "just open" by the ring color. Same shape, different ring = correct mental model.

---

## Asset 3 — `idle_small` (256×256)

**Where it lives:** Discord developer portal → Rich Presence → Art Assets → key `idle_small`.
**When it shows:** Renders as the **small badge** in the bottom-right of the large image on the rich-presence card. ~32px on screen.

**Composition:**
- Square 256×256 PNG.
- 626 Labs mark on cyan field `#17D4FA` (inverted vs the large assets — small badge gets cyan plate so it pops against the navy large).
- Mark fill: navy `#0F1F31`.
- 16px corner radius if you want a softened look; sharp corners are also valid.

**Reference:** lift directly from `~/.claude/skills/626labs-design/assets/brand/icon-transparent-256.png` — the 626 Labs mark is already at the right size. Add the cyan field underneath; export.

---

## Asset 4 — `active_small` (256×256)

**Where it lives:** Discord developer portal → Rich Presence → Art Assets → key `active_small`.
**When it shows:** Same small-badge slot, swapped when active.

**Composition:** identical to `idle_small` EXCEPT:
- Add a **magenta `#F22F89` active dot** in the upper-right corner — 32×32px filled circle, 8px from the top edge and 8px from the right edge.
- The dot is the equivalent of an iOS notification badge: small, unmistakable, says "something is happening."

---

## Asset 5 — webhook avatar (≥256×256, square)

**Where it lives:** committed to `docs/assets/rororoblox-webhook-avatar.png` in this repo. Served via GitHub Pages at:
```
https://estevanhernandez-stack-ed.github.io/ROROROblox/assets/rororoblox-webhook-avatar.png
```
That URL is hardcoded in `DiscordWebhookService.AvatarUrl` and rendered as the poster avatar on every clan-channel webhook embed.

**Composition:**
- Recommend 512×512 (Discord caches and downscales; bigger source = better small renders).
- Same composition as `idle_large` works perfectly — RORORO mark on navy field with cyan ring. Don't use the magenta variant; the avatar is a brand-identity surface, not a state signal.
- Square aspect ratio is required. Discord clips to a circle on most surfaces; keep ≥40px breathing room from any edge so the clip doesn't eat the mark.
- PNG with full opacity. The webhook payload sets `username = "ROROROblox"` so the avatar pairs with the wordmark.

---

## Production checklist

- [ ] **Generate Asset 1** (`idle_large` 1024×1024) — RORORO mark on navy with cyan ring
- [ ] **Generate Asset 2** (`active_large` 1024×1024) — same shape, magenta ring
- [ ] **Generate Asset 3** (`idle_small` 256×256) — 626 Labs mark on cyan field, navy fill
- [ ] **Generate Asset 4** (`active_small` 256×256) — same as 3 + magenta active dot upper-right
- [ ] **Generate Asset 5** (webhook avatar 512×512) — `idle_large` composition at smaller resolution
- [ ] **Este uploads** assets 1-4 to Discord developer portal at https://discord.com/developers/applications/1501748116985221272 → Rich Presence → Art Assets, matching the keys exactly (`idle_large`, `active_large`, `idle_small`, `active_small` — the keys are referenced literally in `DiscordRichPresenceService` constants).
- [ ] **Commit** Asset 5 to `docs/assets/rororoblox-webhook-avatar.png`. The GitHub Pages site rebuilds on push and the URL above goes live.
- [ ] **Verify Pages URL** with `curl -I https://estevanhernandez-stack-ed.github.io/ROROROblox/assets/rororoblox-webhook-avatar.png` — expect `HTTP/2 200` and `content-type: image/png`.
- [ ] **Eyes-on review** against this brief and `~/.claude/skills/626labs-design/colors_and_type.css`. Pattern (x) gate: a glance at the Discord profile card should feel "like RORORO" without saying anything explicit.

---

## What NOT to do

- **No programmatic ICO/PNG generation** for these slots. The procedural tray icon scripts (`scripts/generate-tray-icons.ps1`) produce pragmatic system-tray assets at 16/24/32; that aesthetic doesn't scale to 1024×1024 brand surfaces.
- **No emoji on the assets.** Brand-spread surface, not a Discord profile decoration.
- **No "ROROROblox" wordmark text on the large assets.** Discord renders the app name beneath the asset already; doubling it is visual noise.
- **No magenta-tinted mark fills.** Magenta is a state signal (ring on large, dot on small). The mark itself stays cyan-on-navy across all states.
- **Don't upload PNGs over 1024×1024 to Discord.** Discord's portal caps at 1024 for asset uploads; bigger renders fail or get truncated.
