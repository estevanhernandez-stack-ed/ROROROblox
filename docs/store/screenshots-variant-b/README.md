# Store screenshots, variant B (draft)

> **Status: approved to run by Este, 2026-11-22. Not uploaded yet. English only.**

A second screenshot set for a Partner Center product page experiment. The set on the listing today (`../screenshots/en/`, variant A) opens on the account list and carries no words. Variant B opens on the eight-client wall and puts a headline on every frame.

## Why

In the 30 days to 2026-11-21 the listing page had 2,125 views and 1,129 clicks on Get: 47% of the people who looked did not click. 97% of tracked clicks come from inside the Store, so the listing page is the one step nearly every install passes through. The first screenshot is what a visitor sees before reading anything, and variant A's first screenshot does not show more than one Roblox client.

What the experiment tests: does opening on the wall, with words on the frames, raise the share of viewers who click Get. A 10% relative lift is roughly 12 installs a day at the current pace.

## The set

Nine frames, 1920x1080. Every picture is a real capture already in this repo; only the field, the headline and the frame are drawn.

| File | Capture | Headline |
|---|---|---|
| `01-multi-instance` | `../screenshots/en/05-multi-instance.png` | Eight Roblox clients. One PC. |
| `02-accounts` | `../../videos/v136-shots/en/accounts-running.png` | Every account, one click each. |
| `03-account-panel` | `…/accounts-panel.png` | Open an account, pick a game. |
| `04-account-health` | `…/accounts-health.png` | It tells you what needs a fix. |
| `05-library` | `…/library-grid.png` | Every game in one Library. |
| `06-history` | `../screenshots/en/06-history.png` | Every launch on record. |
| `07-themes` | `../screenshots/en/07-themes.png` | Four themes built in. |
| `08-plugins` | `../screenshots/en/09-plugins.png` | Plugins ask first. |
| `09-compact` | `../screenshots/en/10-compact.png` | Only what is running. |

Frame 2 carries the one trust line: "You sign in on Roblox's own page. RoRoRo never sees your password." Three of the fourteen text reviews say virus, Trojan or stolen account; this is the answer the listing can give without saying "safe".

**The theme builder frame is left out.** The live capture shows the dialog's button row clipped on the left ("pen themes folder"). That is the dialog, not the capture: `ThemeBuilderWindow.xaml` has five buttons in a right-aligned row in a window 640 wide. The same clipped frame is screenshot 8 on the listing today.

## Captions, in this set's order

The approved captions from `../listing-copy.md`, reordered to match and with the theme builder line removed:

```
Eight Roblox clients, eight accounts, one PC. Each window title carries the account signed into it, so you always know which is which.
Every account at a glance: Robux, Plus, friends online, your note, and the game each one is in right now.
Open an account and its games are right there: recent, favourites and private servers, one click to play.
When an account needs you it says so. Session expired, needs verification, limited by Roblox, each with a one-click fix.
Every game you have played, favourited or saved, in one Library. Search, filter, and choose which account plays.
Every launch is recorded, so you can see which account played what and for how long.
Four built-in themes. Flatline carries no meaning in colour at all, so nothing is lost to colour blindness, a bad panel, or direct sun.
Plugins run as separate processes and ask first. You grant each capability by name, and you can revoke it later.
Compact mode shows only what is running. Pin it to a corner of the screen and get back to the game.
```

## Regenerating

```
python scripts/store-graphics/make_listing_variant_b.py
```

Fonts come from the `626labs-design` skill's folder; pass `--fonts DIR` to point elsewhere.

## Before this goes anywhere

- Setting up the experiment is a Partner Center action and Este's call. Product page experiments cover the logo and screenshots only, run up to 90 days, and the original's assets must not change while one runs (noted 2026-11-16; not re-read since, so check the page).
- Whether an experiment can be limited to English, and whether the six translated listings need their own variant, is not known. The headlines here are English; a translated variant would go through the translation verifier first.
