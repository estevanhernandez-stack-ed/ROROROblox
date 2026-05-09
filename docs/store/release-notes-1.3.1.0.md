# Release notes — v1.3.1.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RORORO installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Multi-instance is fixed (the real one this time)

If you ever clicked **Launch As** on two different alts and watched both windows open as the *same* Roblox account — kicking the second one with "Same account launched experience from different device" — that's done. Two Launch As clicks on two different saved accounts now opens two clients, each signed in as their own user. The way it always should have been.

The bug was a one-line config drift: an HTTP cookie cache that was supposed to be off had been silently turned back on, so every Launch As after the first re-used the first account's session token. Doesn't matter how many accounts you saved — RORORO was sending the same one. Fix is in, verified end-to-end, shipped.

### Hard-block when Roblox is already running

If you start Roblox first (Chrome's Play button, Discord deeplink, Start Menu shortcut) *before* opening RORORO, multi-instance can't work right — the alts route through the existing Roblox process and inherit whoever's logged in there. Instead of letting that broken state happen, RORORO now stops at startup with a clear modal: *"Roblox is already running. Close Roblox, close RoRoRo, re-open RoRoRo."* Single button. Done.

This doesn't change anything about the working path (open RORORO first, then Launch As). It only fires when you've opened Roblox by accident before opening RORORO.

## Compatibility

- No new Windows permissions. No new network calls. No new dependencies.
- Saved accounts, favorites, private servers, default-game widget, renames — all unchanged from v1.3.0.0.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
