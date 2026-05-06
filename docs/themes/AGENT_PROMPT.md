# RORORO Theme Builder — AI Prompt

Paste this prompt into Claude, ChatGPT, or any chat agent along with a **vibe description** or **a reference image**, and the agent will return a ready-to-drop theme JSON for RORORO.

## How to use

1. Click **+ Build a theme...** in Preferences (or copy the prompt below manually).
2. Paste it into your AI of choice with a vibe description or reference image.
3. Paste the JSON it returns into the builder's text box and click **Save and apply** — the app validates, saves, and switches to the new theme live. No file fiddling.

If you'd rather drop a `.json` file in directly, that path still works:
`%LOCALAPPDATA%\ROROROblox\themes\<lowercase-kebab-name>.json` — re-open Preferences to see it.

---

## The prompt

You are designing a color theme for **RORORO**, a small Windows desktop app that lets a user run multiple Roblox clients side by side as different saved accounts. The window has a navy-leaning dark base with cyan + magenta as the brand accent pair. Themes are JSON dicts of named slot → hex; they are mutated into the app's brush dictionary at runtime, so the slot names matter exactly.

Given the reference image or vibe description I'm providing, return **only** a valid JSON object matching this schema. No markdown fences, no preamble, no commentary — just the raw JSON.

**Required color fields** (all lowercase `#rrggbb`):

| Field               | What it does                                                                            |
| ------------------- | --------------------------------------------------------------------------------------- |
| `name`              | Display name shown in the theme picker (1–18 chars, Title Case)                         |
| `bg`                | Window background (the darkest shade of the palette; what shows behind everything)      |
| `cyan`              | Primary brand accent — Launch As button bg, headers, status dots, primary CTAs          |
| `magenta`           | Secondary brand accent — Stop button bg, Private server CTA, MAIN pill, alert pills     |
| `white`             | Primary text (display names, headers) — must be high-contrast against `row_bg`          |
| `muted_text`        | Secondary text (status lines, footers, tooltips) — same hue as `white`, lower luminance |
| `divider`           | 1-px divider lines, subtle borders between sections                                     |
| `row_bg`            | Account row card background (the cards that hold each saved account)                    |
| `row_expired_bg`    | Card background when a session has expired — usually a warm tint                        |
| `row_expired_accent`| Accent on expired-session cards — yellow/amber by convention                            |
| `navy`              | Secondary button bodies + modal chrome (slightly brighter than `bg` for depth)          |

### Design rules to follow

1. **Contrast first.** `white` must be legible on `row_bg`. `muted_text` must be readable but obviously secondary. Aim for at least a 4.5:1 luminance ratio between `white` and `row_bg`.

2. **Cohesion.** Pull colors from the reference image / vibe. Don't mix unrelated accents.

3. **Two distinct accents.** `cyan` and `magenta` are the brand pair — pick **two visibly different hues**, never the same value. `cyan` is "primary / go" (Launch As, status); `magenta` is "stop / squad / alert" (Stop button, Private server, MAIN pill). If they collapse to the same color the user can't tell their CTAs apart.

4. **`cyan` ≠ `white`.** They appear next to each other in headers (`Cyan / Magenta / White` in big bold text). If `cyan` is `#ffffff` and `white` is `#eeeeee`, the header reads as one solid block. Even in a greyscale palette, give them at least 30 luminance points of separation — e.g. `cyan: #d8d8d8` + `white: #f0f0f0`.

5. **Layering matters.** The dark slots should step in luminance: `bg` (darkest, behind everything) → `row_bg` (slightly lighter, so cards rise off the surface) → `navy` (similar to `bg`, used for secondary button bodies). A theme where `bg == row_bg` makes the account list look like a flat wall.

6. **Dark base only.** `bg`, `row_bg`, `navy` should all be in the dark half of the spectrum (value < 50%). Light themes don't work — the app's chrome assumes a dark surface for translucency + Mica vibrancy.

7. **Expired tint stays warm.** `row_expired_bg` + `row_expired_accent` are reserved for "your cookie expired, click Re-authenticate." Use a warm amber/yellow even when the rest of the theme is cool. Visual semantics matter more than palette purity here.

### For greyscale / monochrome themes specifically

Greyscale palettes are the easiest to break rules 3 and 4 on. Treat the action hierarchy as a brightness ladder:

- `cyan` (primary): a bright neutral, e.g. `#d8d8d8`
- `magenta` (secondary): a mid-grey, e.g. `#6b6b6b`
- `white` (text): the brightest, e.g. `#f5f5f5`
- `muted_text`: a darker grey, e.g. `#888888`

That gives the same visual hierarchy as a colored theme — primary CTA reads brightest, stop/secondary reads mid, text stays distinct.

### Example (follows all the rules; for reference, do not copy verbatim)

```json
{
  "name": "Deep Teal",
  "bg": "#091820",
  "cyan": "#3FD2C2",
  "magenta": "#E63B7A",
  "white": "#EAF3F2",
  "muted_text": "#7C9197",
  "divider": "#152A33",
  "row_bg": "#10222A",
  "row_expired_bg": "#3A2D14",
  "row_expired_accent": "#F1B232",
  "navy": "#0A1A22"
}
```

Notice: distinct cyan + magenta hues; `cyan` is clearly different from `white`; `bg` is darker than `row_bg` so cards rise; expired warm slots stay amber.

---

## Installing a theme manually (skip if using the builder)

1. Save the JSON as `<lowercase-kebab-name>.json` (e.g. `sunset.json`, `midnight-metro.json`). The filename (minus `.json`) becomes the theme's id.
2. Drop it into `%LOCALAPPDATA%\ROROROblox\themes\`. (Open Preferences → "Open themes folder" to get there in one click.)
3. Re-open Preferences. Your theme appears in the picker; click to apply live.

If your theme doesn't show up, the JSON is probably missing a required field or has a typo — check the log at `%LOCALAPPDATA%\ROROROblox\logs\RORORO-*.log` for a "theme parse" warning. The in-app builder catches these inline; the drop-a-file path falls silent.
