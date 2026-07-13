# RoRoRo — TikTok ad video plan

> Companion to [`docs/features.md`](../features.md) (the claims source — don't put anything in the video that isn't in the ledger) and the Store listing copy. Written 2026-07-12, v1.11.1.0 era, ~300 new Store installs in the past month with zero paid promotion — this is the first deliberate ad.

## Strategy

**Audience:** Roblox grinders — Pet Sim 99 / RCU-style farm players, 16-30, non-technical, Windows. They already run alts the painful way (multiple PCs, VMs, incognito windows, logging in and out). The ad doesn't have to sell the *want* — it has to show the *way*, fast.

**Core promise:** several clients, several accounts, one machine, one click each. Free, on the Microsoft Store.

**Format:** native screen-recording energy, not an "ad." Vertical 9:16, 1080×1920, 21-34 seconds. Fast cuts, big text overlays, trending-sound friendly. The whole video is a screen capture of RoRoRo doing the thing — the product demo IS the hook.

**The built-in production trick:** record with **streamer mode on**. The disguised roster (silly names + hand-drawn avatars) is safer to publish AND is itself a feature demo. One recording sells two features.

## Hook variants (A/B the first 2 seconds)

1. **The count-up (recommended).** Cold open on a desktop as Roblox windows tile in one by one — text overlay counts "2… 3… 4… 5… 6." No intro, no logo, no talking head.
2. **The pain frame.** "POV: you're still logging out to switch alts" over a slow, miserable login screen — hard cut to the RoRoRo roster and six launches.
3. **The disbelief frame.** "Roblox says you can only run one client. Anyway —" cut to the tiled wall of clients.

## Script — 30s master cut (hook variant 1)

| Time | Screen | Text overlay | VO (optional — works muted) |
|---|---|---|---|
| 0:00-0:03 | Roblox clients tiling onto one monitor, one after another | "2… 3… 4… 5… 6." | "Six Roblox clients. One PC." |
| 0:03-0:07 | RoRoRo main window: the roster, streamer mode on | "This is RoRoRo — it's free" | "This is RoRoRo. Every alt saved, one click each." |
| 0:07-0:12 | Click **Launch multiple** → clients spawn with the ~5s throttle, presence chips flip to "In Pet Sim 99" | "One click. Whole roster." | "Launch the whole roster into your game — it even shows you which game each one is actually in." |
| 0:12-0:17 | Squad Launch panel → all alts landing in the same private server | "Same private server. All of them." | "Squad Launch puts every alt in the same private server. No links, no logging out." |
| 0:17-0:21 | Toggle streamer mode off/on — names and avatars swap live | "Streaming? Hide your alts." | "And if you stream — one flip disguises everything." |
| 0:21-0:26 | The trust beat: consent sheet or login window, brief | "Your password never touches the app" | "Login happens on Roblox's own page. RoRoRo never sees your password." |
| 0:26-0:30 | Microsoft Store listing page, Install button click | "Free · Microsoft Store · search RORORO" | "Free on the Microsoft Store. Search RORORO." |

**15s cut (for the ad slot):** rows 1, 3, 4, 7 only — hook, launch multiple, squad launch, CTA.

## Shot list (capture session checklist)

All captures on the clean-desktop profile, streamer mode ON, 1080p+, cursor visible, Windows notifications off:

1. Six clients tiling — script the launches so windows land in a 2×3 grid (this is the money shot; retake until the rhythm is right).
2. RoRoRo roster scroll — 6-8 accounts with tags, presence chips live.
3. "Launch multiple" click → throttled spawn sequence.
4. Squad Launch panel → careful-mode phase indicators → alts landing in one private server.
5. Streamer-mode toggle, off → on, names/avatars swapping in place.
6. Add-account window showing the Roblox login page (blur any real handle — use a burner alt even under streamer mode).
7. Store listing → Install click.

## Caption + tags

> Six Roblox clients on one PC — free, on the Microsoft Store. No injection, no mods, it just holds a lock and launches the real client. Search RORORO.

`#roblox #petsim99 #robloxalt #ps99 #robloxtips #multiinstance` — rotate the game tag per cut (PS99 first; it's the clan's game and the densest audience).

## Guardrails (do not cut these in the edit)

- **No ban-proof claims.** The README/site language is the ceiling: risk "appears low but is non-zero." Never say "undetectable," "safe," or "Roblox-approved."
- **Trademark care:** "for Roblox," never "by Roblox" or Roblox logo-forward branding. RoRoRo is an independent third-party tool — keep the 626 Labs mark on the end card.
- **No macro/automation footage.** Ur plugins stay out of the ad — the Store narrative is launcher-not-automation, and the ad must match the Store listing it points to.
- **Real UI only** — every frame is the shipping app. No mockups, no sped-up fakery beyond standard jump cuts (speed-ramps labeled by the cut rhythm are fine; don't fake launch speed in a way the app can't reproduce).
- **Sound:** use TikTok's commercial-safe library if this runs as a paid ad — trending sounds are only licensed for organic posts.

## Production route

Cheapest viable: OBS screen capture (game capture per Roblox window + display capture for RoRoRo) → CapCut for text overlays and cuts. The 626 brand end card (navy field, cyan/magenta pair, Space Grotesk "RORORO — Imagine Something Else.") goes through the `626labs-design` skill — no programmatic placeholder frames.

Deliverables to produce from one capture session: 30s master, 15s ad cut, 3 hook-variant opens (re-edit, no re-shoot). Post organic first, read retention on the hooks, then put spend behind the winner.
