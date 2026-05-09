# Release notes — v1.3.2.1

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RORORO installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Friends list actually shows your friends now

If you opened the **Friends** sheet on an account in v1.3.1 you saw something weird — usernames blank, avatar circles empty, but the counts and game names worked. Roblox quietly stopped sending usernames in their friends-list response, and our bulk avatar fetch was choking on lists over 100 friends. Both fixed: usernames render, avatars render, accounts with 100+ friends work the same as smaller lists.

This also means the **Follow** button works the way it always should have — pick a friend, click Follow, your alt joins their server. No more "couldn't follow — userId not yet known" message even on a fresh restart.

### Multi-account follow works after a restart, no re-add needed

If you've been running RORORO since v1.3.0, the per-account "Follow:" chips and the **Friends** sheet sometimes wouldn't work right after a restart — the app forgot which Roblox user each saved account belonged to until you launched something. Fixed: the user IDs now stick across restarts. You don't need to re-add any accounts; the next time you open RORORO it backfills the missing ones quietly in the background.

## Compatibility

- No new Windows permissions. No new network calls *from your perspective* (the friends sheet does one extra round-trip behind the scenes to get usernames, but you don't see it).
- No new dependencies, no migration steps. Saved accounts, favorites, private servers, default-game widget, renames — all unchanged from v1.3.1.

## Known issues going into the next update

- **FPS cap can bleed between back-to-back launches.** If you Launch As account A (set to 20 FPS) and then launch account B (set to 120 FPS) within a few seconds, account B can come up at 20 instead of 120 — they're both reading the same Roblox-side settings file and the timing isn't quite right. Workaround: wait until account A's window is fully loaded before launching B. Real fix targeted for the next update.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`. The dev-cert was rotated in v1.3.1 — re-import the new one before installing.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
