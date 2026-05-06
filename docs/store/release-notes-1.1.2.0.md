# Release notes — v1.1.2.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

The installer is unsigned, so Windows SmartScreen will warn the first time you run it. Click **More info** → **Run anyway**. One-time per machine. Future updates roll out automatically — no second prompt.

## What changed

### The rename — `ROROROblox` → `RORORO`

The product name dropped the "blox" suffix everywhere a user can see it. Windows title bars, the wordmark on every tile and splash, README, About box, every modal — all read **RORORO** now. The product itself is identical; only the name and wordmark moved.

The repo URL (`github.com/.../ROROROblox`) is unchanged. Inbound links and bookmarks still work.

### Four small fixes

1. **Embedded login window goes blank on first open.** Added a reload hint above the WebView — press F5 to recover instead of force-quitting. (Underlying issue is a WebView2 first-paint race; reload is the cheap fix.)
2. **Saved Games library overflowed the window without a visible scrollbar.** Default window is taller now; scrollbar always shows.
3. **About box read the wrong version.** The displayed version now matches the actual build (was reading `1.0.0` instead of `1.1.2.0`).
4. **Removed the startup session check that was producing false "expired" badges.** Eager-validation on launch was triggering Roblox's anti-fraud heuristics on fresh cookies. Sessions now validate when you actually click Launch As — friction lands where it's earned, not on every startup.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store. Best path if you have a Microsoft account.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
