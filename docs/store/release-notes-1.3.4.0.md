# Release notes — v1.3.4.0

> Paste the block between the `---` markers below into the GitHub Release body.
> Tone: clan-facing (Pet Sim 99 audience). Plain, warm, no Microsoft-reviewer formality.

---

## Download

[**rororo-win-Setup.exe**](https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/rororo-win-Setup.exe) — single click, installs to your user profile, lands in the Start Menu.

If you've already got RORORO installed via the installer or the Microsoft Store, this update rolls out automatically — no second prompt.

## What changed

### Adding a second or third account works in a row now

If you added Account #1, then immediately clicked **Add Account** again to add a second one, the login window would jump straight to "logged in as Account #1" and quietly save the same account a second time. Adding a third needed a full restart of RORORO between captures.

That's fixed. Every Add Account now starts on a fresh login page, even right after the previous one — no logout, no cache clear, no restart. Add as many accounts as you want in a single sitting.

What was happening: each Add Account flow reused the same Edge browser data folder, and the previous capture's browser processes were still holding onto session cookies in there. The next Add Account would boot straight into "you're already logged in." Now each capture gets its own clean folder and the previous one gets best-effort cleaned up in the background.

## Compatibility

- No new Windows permissions. No new network calls. No new dependencies.
- Saved accounts, favorites, private servers, default-game widget, renames, themes — all unchanged from v1.3.3.0.

## Known issues going into the next update

- **FPS cap can bleed between back-to-back launches.** If you Launch As account A (set to 20 FPS) and then launch account B (set to 120 FPS) within a few seconds, account B can come up at 20 instead of 120 — they're both reading the same Roblox-side settings file and the timing isn't quite right. Workaround: wait until account A's window is fully loaded before launching B. Real fix targeted for the next update.

## Other channels

- **Microsoft Store *(recommended)*** — [Install RoRoRo on the Microsoft Store](https://apps.microsoft.com/detail/9NMJCS390KWB). Signed by Microsoft, bypasses SmartScreen, auto-updates through the Store.
- **MSIX sideload** — `RORORO-Sideload.msix` + `dev-cert.cer` are also attached if you prefer the cert-import flow over `Setup.exe`. Same dev-cert as v1.3.1 — if you've already imported it once, no re-import needed.

## Issues, ideas

- **Bug or suggestion:** [open an issue](https://github.com/estevanhernandez-stack-ed/ROROROblox/issues/new)
- **Privacy details:** [privacy policy](https://estevanhernandez-stack-ed.github.io/ROROROblox/privacy/)

A 626 Labs product.
