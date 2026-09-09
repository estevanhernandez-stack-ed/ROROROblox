# Launch video narration — EN source (translation kit)

The voiceover and overlay lines for the RoRoRo launch video
(`rororo-launch-16x9.mp4` in this folder; the 9:16 / 1:1 / 4:5 social
cuts live in the 626labs-hub repo). Source of truth:
`626labs-hub/assets/video/rororo/rororo-launch-script.md`.

**This file is the AUDIO layer.** Its sibling
`launch-video-script.md` is the VISUAL layer — every on-screen card
line, already translated into the six languages with slot budgets.
The two divide the video completely: that file is what viewers read,
this one is what they hear. Translate this one the same way.

**Return format:** copy this file to
`launch-video-narration-<lang>.md` in this folder (`fr`, `de`, `ru`,
`pt-BR`, `pl`, `es` — same tags as `docs/store/translations/ui-*.json`),
translate every VO and OVERLAY value, keep the keys and structure
untouched. Localized audio renders
in the same ElevenLabs voice on the multilingual model, so the lines
come back as *speakable* text, not listing copy.

## Translator rules

1. **Names stay in English:** RoRoRo, Squad Launch, 626 Labs, Microsoft
   Store, Roblox.
   **The tagline splits by where the language cooperates** (decision
   2026-09-08): where "Imagine" is the native imperative of the local
   imaginar/imaginer, the tagline speaks localized — fr `Imagine autre
   chose.`, pt-BR `Imagine algo diferente.`, es `Imagina algo
   diferente.` (tú register). Everywhere else (de, pl, ru) it stays
   English, matching the card footer, as a brand mark. These are the
   canonical renderings — the listings should eventually adopt them.
2. **Spoken digits:** the VO says "six-two-six Labs" because the TTS
   reads "626" as a number. Translate as the *spoken digits* of your
   language (e.g. DE "sechs-zwei-sechs Labs"). Keep digits as digits in
   OVERLAY text.
3. **Length budget:** each VO line is voiced over a fixed-length slide.
   Stay within ~110% of the English syllable count or the read gets
   rushed. Trimming intensifiers is the sanctioned move.
4. **Overlays are typed on screen** — keep them punchy, lowercase style
   preserved where the English is lowercase, max ~30 characters.
5. **Guardrails carry into every language:** never "undetectable",
   "safe", or anything implying Roblox approval; "for Roblox", never
   "by Roblox".

## Lines

### hook (slide 1)
- OVERLAY: `8 clients. 1 PC.`
- VO: `Okay, this is EIGHT Roblox clients, running at the same time, on one PC. And the app doing it is free.`

### accounts (slide 2)
- OVERLAY: `save once. launch as.`
- VO: `This is RoRoRo! Save every alt once, and it's one click each. Live status, the game each one is actually in, even the RAM each client is eating.`

### squad (slide 3)
- OVERLAY: `same private server. all of them.`
- VO: `Squad Launch is the best part. One link, and every single account lands in the SAME private server, together.`

### history (slide 4)
- OVERLAY: `it keeps score now`
- VO: `And it keeps score! Peak alts at once, total uptime, a leaderboard of your alts with login streaks. All computed on your machine, nothing leaves it.`

### themes (slide 5)
- OVERLAY: `AI builds your theme`
- VO: `Four built-in themes, plus a theme builder where an AI designs you a custom one from a vibe description.`

### trust (slide 6)
- OVERLAY: `your password never touches it`
- VO: `Your password never touches the app, the vault is encrypted, and a memory watchdog recycles a client before a leak crashes it. There are even keyboard shortcuts now.`

### cta (slide 7)
- OVERLAY: `scan it, open on desktop`
- VO: `Free on the Microsoft Store, Windows AND Mac. Scan the code, save the page, grab it on your desktop. RoRoRo, from six-two-six Labs. Imagine something else.`

## Post caption (for localized social posts)

`Eight Roblox clients on one PC — free. RoRoRo saves your alts, launches them one click each, lands them in the same private server, and now keeps your stats. Windows + Mac. Search RORORO. Not affiliated with Roblox.`

The "Not affiliated with Roblox." sentence is mandatory in every
language.
