# Store screenshots, variant B (draft)

> **Status: live as a product page experiment, 2026-11-22 to 2027-02-20, 50/50 split (read 2026-11-23). English frames, shown in every language.**

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

## Where it stands (2026-11-23)

**Live.** Read in Partner Center on 2026-11-23 at 09:10 CST: the one experiment, "Variant B: wall first, headlines", is "In Microsoft Store", running November 22, 2026 to February 20, 2027, with a 50/50 split between the original and the variant. It passed certification in under 22 hours. The report lags two days and held no data yet at that read; first numbers are due about 2026-11-25.

**Submitted 2026-11-22, about 11:28 CST, on Este's word ("Submit the experiment as staged").** The experiment holds these nine frames in order.

Submitting asked nothing: no confirmation and no field for the share of visitors who see the variant. The split was not chosen here; it came out even, which Microsoft's page describes as the usual case.

Reading it: Insights, Product page experiments, for product `9NMJCS390KWB`. The application overview's experiment line reads "Unknown" for a few seconds after the page loads; wait for it before trusting it.

**Until this experiment ends, no submission changes the listing's logo or screenshots.**

What the experiment page and Microsoft's page on it say (read 2026-11-22, `learn.microsoft.com/en-us/windows/apps/publish/product-page-experiments`):

- **It runs across all languages.** The form has no language choice. So visitors on the six translated listings who land in the variant group see this English set. Whether it replaces each language's own screenshots is not stated in so many words; assume it does.
- **Submitting sends it through certification.** Budget a day, as for a release.
- The share of visitors who see the variant can be chosen; an even split is recommended. The draft form did not show that field, so it may appear at submit.
- It runs for up to 90 days and can be stopped from the overview page. A winning variant can be applied to the product page.
- While it is live, Microsoft recommends not changing the logo or screenshots of the original listing in an update submission, because that can invalidate the result. So the recapture of the theme builder screenshot waits for the experiment to end.
- It reports the original's and the variant's conversion rate. Nothing is said about confidence.

**How long until it says anything.** On the funnel numbers we can read (2,125 views in 30 days, 53% clicking Get), telling a 10% relative lift from noise takes about 1,300 views in each group, which at an even split is about five weeks. That is an estimate from signed-in views only; if the experiment counts every visitor it is sooner, and that is not known. Either way the answer arrives after the 10K date. What the experiment does before then is show the variant to its share of visitors.

A translated variant is not possible inside one experiment: there is one screenshot set for every language.
