# What's new in this version — v1.23.0.0

> Paste the fenced block into Partner Center → your app → **Store listings** → *What's new in this
> version*.
>
> **Written for someone on v1.22** — a single-release jump. Ordered by what a player notices: the
> stats page is the visible thing, the rename fix is the thing that was quietly wrong.

---

```
Your stats, on the History page
• History now opens with your numbers: the most alts you've ever run
  at once, total hours across every account, your most-played game,
  your longest session, and a day streak — plus a leaderboard of your
  alts by hours played. Everything is computed on your PC from the
  history RoRoRo already kept. Nothing is sent anywhere, same as
  always.

Login streaks per alt
• Every alt tracks its own streak of days it actually got into a
  game. Launching isn't enough — an alt that got stuck at the home
  screen didn't log in, and the streak knows. Streaks start counting
  from this version.

Renames show up in History
• If you gave an account a nickname, new History entries now use it
  instead of the account's old Roblox name.

Small fixes
• Installing a plugin with a long description no longer pushes the
  Install and Cancel buttons off the bottom of the window.
• Fixed a rare crash that could stop the app from starting.
```

---

## What is deliberately not in this copy

- **The connector stop fix (F-121).** Plugin/power-user material — the plugin catalog doesn't
  ship in the Store edition, and this field is not the place to introduce "an AI can drive this
  app." The release notes and the connector's own docs carry it.
- **"Since history began" mechanics, backfill, the fold, missing-end accounting.** The honest
  labels are on the page itself, where they're next to the numbers they qualify. Here they'd be
  noise.
- **Presence-confirmed landing internals.** "Actually got into a game" is the same fact in this
  reader's language.
